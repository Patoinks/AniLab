using System;
using System.IO;
using AnimLab.Netcode.Sim;
using AnimLab.Netcode.Rollback;
using AnimLab.Netcode.Transport;
using NUnit.Framework;

namespace AnimLab.Netcode.Tests
{
    public class RollbackTests
    {
        [Test] public void PredictionKeepsHeldButtonsAndClearsAllEdges()
        {
            var p=new PlayerInput { MoveAngle=4,Magnitude=8,LookYaw=777,Buttons=Buttons.Attack|Buttons.Jump|Buttons.Roll|Buttons.Guard|Buttons.Run };
            var q=p.Predicted;
            Assert.AreEqual(Buttons.Guard|Buttons.Run,q.Buttons);
            Assert.AreEqual(p.MoveAngle,q.MoveAngle); Assert.AreEqual(p.LookYaw,q.LookYaw);
        }
        [Test] public void MissingRemoteInputStallsAtEightAndCanResume()
        {
            var s=new RollbackSession(MatchState.Create(),0,0); int advanced=0;
            for(int i=0;i<20;i++) { s.QueueLocal(default); if(s.TryAdvance()) advanced++; }
            Assert.AreEqual(8,advanced); Assert.AreEqual(8,s.State.Tick); Assert.Greater(s.Stalls,0);
            for(int i=0;i<8;i++) s.ReceiveRemote(i,default);
            s.QueueLocal(default); Assert.IsTrue(s.TryAdvance());
        }
        [Test] public void ReorderedLateInputsEqualUndelayedOracleAtEveryConfirmedState()
        {
            var initial=MatchState.Create(); var session=new RollbackSession(initial,0,0);
            var oracle=initial; var events=new EventBuffer();
            const int count=900;
            var mine=new PlayerInput[count]; var other=new PlayerInput[count];
            for(int i=0;i<count;i++) { mine[i]=BotInputs.Movement(i,0); other[i]=BotInputs.Movement(i,1); other[i].LookYaw=(ushort)((i*31)%1024); }
            var expected=new ulong[count+1]; expected[0]=StateCodec.Checksum(oracle);
            for(int t=0;t<count;t++) { World.Step(ref oracle,mine[t],other[t],events); expected[t+1]=StateCodec.Checksum(oracle); }
            for(int tick=0;tick<count;tick++) {
                session.QueueLocal(mine[tick]); Assert.IsTrue(session.TryAdvance());
                if(tick>=6) { session.ReceiveRemote(tick-4,other[tick-4]); session.ReceiveRemote(tick-6,other[tick-6]); }
                session.Reconcile();
                Assert.IsTrue(session.TryState(session.ConfirmedTick,out MatchState confirmed));
                Assert.AreEqual(expected[session.ConfirmedTick],StateCodec.Checksum(confirmed));
            }
            for(int t=count-6;t<count;t++) session.ReceiveRemote(t,other[t]);
            session.Reconcile();
            Assert.AreEqual(StateCodec.Checksum(oracle),StateCodec.Checksum(session.State));
            Assert.Greater(session.Rollbacks,100); Assert.LessOrEqual(session.MaximumRollback,8); Assert.IsFalse(session.Faulted,session.Error);
        }
        [Test] public void FrameAdvantageThrottlesOnlyTheAheadPeer()
        {
            var s=new RollbackSession(MatchState.Create(),0,2);
            for(int i=0;i<8;i++) { s.QueueLocal(default); s.ReceiveRemote(i,i<2?PlayerInput.Neutral(s.State.P1.Yaw):default); s.TryAdvance(); }
            s.RemoteTick=0; int skips=s.TimingSkips;
            for(int i=0;i<8;i++) { s.QueueLocal(default); s.ReceiveRemote(s.State.Tick,default); s.TryAdvance(); }
            Assert.Greater(s.TimingSkips,skips);
        }
        [TestCase(0,0,0,1200)]
        [TestCase(120,20,2,10800)]
        [TestCase(200,45,10,1800)]
        public void LoopbackProtocolMatchesOracleWithLatencyJitterLoss(int latency,int jitter,int loss,int ticks)
        {
            var initial=MatchState.Create(); initial.P0.Health=initial.P1.Health=100000;
            initial.P0.Config.MaxHealth=initial.P1.Config.MaxHealth=100000;
            initial.P0.Config.Style=Style.Reaper; initial.P1.Config.Style=Style.Daggers;
            using(var match=new LoopbackMatch(initial,latency,jitter,loss)) {
                var aInputs=new PlayerInput[ticks+16]; var bInputs=new PlayerInput[ticks+16];
                var aKnown=new bool[ticks+16]; var bKnown=new bool[ticks+16]; aKnown[0]=aKnown[1]=bKnown[0]=bKnown[1]=true;
                for(int i=0;i<2;i++) { match.A.Session.TryLocalInput(i,out aInputs[i]); match.B.Session.TryLocalInput(i,out bInputs[i]); }
                long wall=0; int attempts=0;
                while((match.A.Session.State.Tick<ticks || match.B.Session.State.Tick<ticks) && attempts<ticks*4+1000) {
                    wall=attempts*1000L/60;
                    match.Pump(wall);
                    Schedule(match.A.Session,aInputs,aKnown,ticks); Schedule(match.B.Session,bInputs,bKnown,ticks);
                    match.A.SendInputs(wall); match.B.SendInputs(wall); match.Pump(wall);
                    Assert.IsFalse(match.A.Session.Faulted,match.A.Status); Assert.IsFalse(match.B.Session.Faulted,match.B.Status);
                    attempts++;
                }
                Assert.AreEqual(ticks,match.A.Session.State.Tick); Assert.AreEqual(ticks,match.B.Session.State.Tick);
                for(int i=0;i<80;i++) { wall+=17; match.Pump(wall); match.Send(wall); }
                Assert.AreEqual(StateCodec.Checksum(match.A.Session.State),StateCodec.Checksum(match.B.Session.State));
                Assert.IsNull(match.A.Desync); Assert.IsNull(match.B.Desync);
                var oracle=initial; var events=new EventBuffer();
                for(int t=0;t<ticks;t++) { Assert.IsTrue(aKnown[t]&&bKnown[t]); World.Step(ref oracle,aInputs[t],bInputs[t],events); }
                Assert.AreEqual(StateCodec.Checksum(oracle),StateCodec.Checksum(match.A.Session.State),"undelayed oracle");
                Assert.LessOrEqual(match.A.Session.MaximumRollback,8); Assert.LessOrEqual(match.B.Session.MaximumRollback,8);
                Assert.AreEqual(ticks/30,match.A.ChecksumsVerified); Assert.AreEqual(ticks/30,match.B.ChecksumsVerified);
                if(latency>0) Assert.Greater(match.A.Session.Rollbacks+match.B.Session.Rollbacks,0);
                string summary=$"ticks={ticks}; wall_ms={wall}; latency_one_way_ms={latency}; jitter_ms={jitter}; loss_percent={loss}; dropped={match.Network.Dropped}; rollbacks={match.A.Session.Rollbacks}/{match.B.Session.Rollbacks}; resimulated={match.A.Session.RolledBackTicks}/{match.B.Session.RolledBackTicks}; max_depth={match.A.Session.MaximumRollback}/{match.B.Session.MaximumRollback}; stalls={match.A.Session.Stalls}/{match.B.Session.Stalls}; verified={match.A.ChecksumsVerified}/{match.B.ChecksumsVerified}; desync=0; hash={StateCodec.Checksum(oracle):X16}";
                TestContext.WriteLine(summary);
                Directory.CreateDirectory("Logs/netcode"); File.WriteAllText($"Logs/netcode/loopback_{latency}_{jitter}_{loss}.txt",summary);
            }
        }
        static void Schedule(RollbackSession s,PlayerInput[] inputs,bool[] known,int limit)
        {
            if(s.State.Tick>=limit) return;
            int tick=s.State.Tick+s.InputDelay; var input=BotInputs.Combat(s.State.Tick,s.LocalPlayer,s.State);
            if(s.QueueLocal(input)) { inputs[tick]=input; known[tick]=true; }
            s.TryAdvance();
        }
        [Test] public void DesyncExchangesBothStatesAndWritesReproducibleDump()
        {
            using(var match=new LoopbackMatch(MatchState.Create(),0,0,0)) {
                long now=0;
                for(int i=0;i<65;i++) { match.Tick(now); now+=17; }
                match.A.Session.State.P0.Health-=1;
                for(int i=0;i<100 && (match.A.Desync==null || match.A.Desync.RemoteState==null);i++) { match.Tick(now); now+=17; }
                Assert.IsNotNull(match.A.Desync); Assert.IsNotNull(match.A.Desync.RemoteState);
                Assert.AreNotEqual(match.A.Desync.LocalHash,match.A.Desync.RemoteHash);
                match.A.WriteDesync("Logs/netcode/injected_test");
                Assert.IsTrue(File.Exists("Logs/netcode/injected_test/desync_"+match.A.Desync.Tick+"_p0_local.bin"));
                Assert.IsTrue(File.Exists("Logs/netcode/injected_test/desync_"+match.A.Desync.Tick+"_p0_remote.bin"));
            }
        }
    }
}

