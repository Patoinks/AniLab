using AnimLab.Netcode.Sim;
using AnimLab.Netcode.Rollback;
using NUnit.Framework;

namespace AnimLab.Netcode.Tests
{
    public class AbilityTests
    {
        static MatchState Duel()
        {
            var s=MatchState.Create(5);
            for(int i=0;i<2;i++) { ref PlayerState p=ref MatchState.Player(ref s,i); p.Config.Learn(AbilityId.Dash); p.Config.Learn(AbilityId.FireBall); }
            s.P0.Position=new FixVec3(0,0,0); s.P0.Yaw=0; s.P1.Position=new FixVec3(0,0,6); s.P1.Yaw=Fix.Pi;
            return s;
        }
        [Test] public void DashMovesItsDistanceSpendsManaAndRespectsCooldown()
        {
            var s=Duel(); var e=new EventBuffer();
            Fix mana=s.P0.Mana;
            World.Step(ref s,new PlayerInput { Cast=(byte)AbilityId.Dash,Magnitude=15,MoveAngle=64 },default,e);
            Assert.AreEqual(mana-s.Abilities.DashMana,s.P0.Mana);
            for(int i=0;i<12;i++) World.Step(ref s,default,default,e);
            Assert.IsFalse(s.P0.Dashing);
            // Arranca no máximo e trava: ~ a distância do Dash para o lado (+X), mais o passo normal.
            Assert.IsTrue(s.P0.Position.X>s.Abilities.DashDistance*Fix.Ratio(85,100),"dash distance "+s.P0.Position.X);
            Fix before=s.P0.Mana;
            World.Step(ref s,new PlayerInput { Cast=(byte)AbilityId.Dash },default,e);
            Assert.IsFalse(s.P0.Dashing,"cooldown"); Assert.IsTrue(s.P0.Mana>before,"mana not spent");
        }
        [Test] public void FireBallFlysHitsBurnsAndExplodes()
        {
            var s=Duel(); var e=new EventBuffer(); bool impact=false,burning=false;
            Fix health=s.P1.Health;
            World.Step(ref s,new PlayerInput { Cast=(byte)AbilityId.FireBall,Targeted=true,TargetSlot=2 },default,e);
            Assert.AreEqual(AbilityId.FireBall,s.Q0.Kind);
            for(int i=0;i<90 && !impact;i++)
            {
                World.Step(ref s,default,default,e);
                for(int k=0;k<e.Count;k++) { if(e[k].Kind==EventKind.Impact) impact=true; if(e[k].Kind==EventKind.StatusApplied && e[k].Status==Status.Burning) burning=true; }
            }
            Assert.IsTrue(impact,"impact"); Assert.IsTrue(burning,"burning");
            Assert.AreEqual(AbilityId.None,s.Q0.Kind,"projectile freed");
            Assert.IsTrue(s.P1.Health<health-s.Abilities.FireDamage*Fix.Ratio(9,10),"direct hit "+s.P1.Health);
            Assert.IsTrue(s.P1.StatusLeft(Status.Burning)>Fix.Ratio(3,1),"burning "+s.P1.StatusLeft(Status.Burning));
        }
        static PlayerInput Caster(int tick,int player,in MatchState s)
        {
            var input=BotInputs.Combat(tick,player,s);
            int t=(tick+player*53)%240;
            if(t==40) input.Cast=(byte)AbilityId.FireBall; else if(t==150) { input.Cast=(byte)AbilityId.Dash; input.Magnitude=15; }
            return input;
        }
        [Test] public void CastingBotsStayInSyncThroughRollback()
        {
            var initial=Duel(); initial.P0.Health=initial.P1.Health=initial.P0.Config.MaxHealth=initial.P1.Config.MaxHealth=100000;
            const int ticks=10800;
            using(var match=new LoopbackMatch(initial,120,20,2)) {
                var a=new PlayerInput[ticks+16]; var b=new PlayerInput[ticks+16];
                for(int i=0;i<2;i++) { match.A.Session.TryLocalInput(i,out a[i]); match.B.Session.TryLocalInput(i,out b[i]); }
                long wall=0; int attempts=0;
                while((match.A.Session.State.Tick<ticks || match.B.Session.State.Tick<ticks) && attempts<ticks*4+1000) {
                    wall=attempts*1000L/60; match.Pump(wall);
                    Queue(match.A.Session,a,ticks); Queue(match.B.Session,b,ticks);
                    match.A.SendInputs(wall); match.B.SendInputs(wall); match.Pump(wall);
                    Assert.IsFalse(match.A.Session.Faulted,match.A.Status); Assert.IsFalse(match.B.Session.Faulted,match.B.Status);
                    attempts++;
                }
                for(int i=0;i<80;i++) { wall+=17; match.Pump(wall); match.Send(wall); }
                Assert.AreEqual(StateCodec.Checksum(match.A.Session.State),StateCodec.Checksum(match.B.Session.State));
                Assert.IsNull(match.A.Desync); Assert.IsNull(match.B.Desync);
                var oracle=initial; var events=new EventBuffer(); int casts=0,impacts=0;
                for(int t=0;t<ticks;t++) {
                    World.Step(ref oracle,a[t],b[t],events);
                    for(int k=0;k<events.Count;k++) { if(events[k].Kind==EventKind.Cast && events[k].Other<0) casts++; if(events[k].Kind==EventKind.Impact) impacts++; }
                }
                Assert.AreEqual(StateCodec.Checksum(oracle),StateCodec.Checksum(match.A.Session.State),"undelayed oracle");
                TestContext.WriteLine($"casts {casts}, impacts {impacts}, rollbacks {match.A.Session.Rollbacks}/{match.B.Session.Rollbacks}");
                Assert.Greater(casts,60); Assert.Greater(impacts,20);
            }
        }
        static void Queue(RollbackSession s,PlayerInput[] inputs,int limit)
        {
            if(s.State.Tick>=limit) return;
            int tick=s.State.Tick+s.InputDelay;
            var input=Caster(s.State.Tick,s.LocalPlayer,s.State);
            if(s.QueueLocal(input) && tick<inputs.Length) inputs[tick]=input;
            s.TryAdvance();
        }
    }
}
