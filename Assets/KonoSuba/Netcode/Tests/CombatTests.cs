using System;
using System.Linq;
using System.Reflection;
using AnimLab.Netcode.Sim;
using NUnit.Framework;
using UnityEngine;

namespace AnimLab.Netcode.Tests
{
    public class CombatTests
    {
        static MatchState Pair(Style style = Style.Knight)
        {
            var c = PlayerConfig.Default;
            c.DamageScale = c.AttackSpeed = c.StaggerScale = c.KnockbackScale = 1;
            c.CritChance = c.Defense = c.HealthRegen = 0; c.MaxHealth = 400; c.MaxPoise = 100;
            var s = MatchState.Create();
            s.P0 = MatchState.CreatePlayer(c,new FixVec3(0,0,0),0);
            c.Style = style; s.P0.Config.Style = style;
            s.P1 = MatchState.CreatePlayer(c,new FixVec3(0,0,1),Fix.Pi);
            s.P0.Weapon = WeaponState.Active;
            return s;
        }
        static void Guard(ref MatchState s,bool parry) {
            s.P1.Weapon = WeaponState.Guard; s.P1.ParryArmed = parry; s.P1.GuardSince = 0;
        }
        [Test] public void ParryAtInclusiveBoundaryStaggersAttackerWithoutDamage()
        {
            var s=Pair(); var e=new EventBuffer(); Guard(ref s,true); s.P1.BodyTime=s.P1.Config.ParryWindow;
            World.Strike(ref s,0,new FixVec3(0,1,1),e);
            Assert.IsTrue(s.P0.Staggered); Assert.AreEqual((Fix)400,s.P1.Health);
            Assert.AreEqual(WeaponState.Idle,s.P0.Weapon); Assert.IsTrue(Has(e,EventKind.Parry));
            Assert.AreEqual(SwordData.ParryStun,s.P0.StaggerUntil);
        }
        [Test] public void BlockCutsEightyPercentAndReboundsAttacker()
        {
            var s=Pair(); var e=new EventBuffer(); Guard(ref s,false);
            World.Strike(ref s,0,FixVec3.Up,e);
            Assert.Less(Fix.Abs((400-s.P1.Health)-Fix.Ratio(36,10)).Raw,32);
            Assert.AreEqual(WeaponState.Recoil,s.P0.Weapon); Assert.IsTrue(Has(e,EventKind.Block));
        }
        [Test] public void ExpiredParryWindowBlocksInstead()
        {
            var s=Pair(); var e=new EventBuffer(); Guard(ref s,true); s.P1.BodyTime=s.P1.Config.ParryWindow+World.Dt;
            World.Strike(ref s,0,FixVec3.Up,e);
            Assert.IsFalse(s.P0.Staggered); Assert.IsTrue(Has(e,EventKind.Block));
        }
        [Test] public void ClashReboundsBothAndAppliesOppositeKnock()
        {
            var s=Pair(); var e=new EventBuffer(); s.P1.Weapon=WeaponState.Active;
            World.Clash(ref s,0,FixVec3.Up,e);
            Assert.AreEqual(WeaponState.Recoil,s.P0.Weapon); Assert.AreEqual(WeaponState.Recoil,s.P1.Weapon);
            Assert.Less(FixVec3.Dot(s.P0.Knock,s.P1.Knock).Raw,0);
            // Os dois são empurrados para longe um do outro.
            Assert.Less(s.P0.Knock.Z.Raw,0); Assert.Greater(s.P1.Knock.Z.Raw,0);
            Assert.Less(Fix.Abs(s.P0.Knock.Length-SwordData.ClashPush*s.P0.Config.KnockbackScale).Raw,64);
            Assert.AreEqual(9,s.P0.HitStop); Assert.IsTrue(Has(e,EventKind.Clash));
        }
        [Test] public void ClashOnlyWhenBothBladesStartedTogether()
        {
            var s=Pair(); s.P1.Weapon=WeaponState.Active;
            s.P0.WeaponTime=Fix.Ratio(5,100); s.P1.WeaponTime=Fix.Ratio(8,100);
            Assert.IsTrue(World.InSync(s.P0,s.P1));
            s.P1.WeaponTime=s.P0.WeaponTime+SwordData.ClashWindow+Fix.Ratio(2,100); // um começou claramente antes
            Assert.IsFalse(World.InSync(s.P0,s.P1));
        }
        [Test] public void RollAvoidsContactDuringInvulnerability()
        {
            var s=Pair(); var e=new EventBuffer(); s.P1.Weapon=WeaponState.Idle; s.P0.Weapon=WeaponState.Idle;
            for(int i=0;i<5;i++) World.Step(ref s,default,new PlayerInput { Buttons=i==0?Buttons.Roll:0,LookYaw=512 },e);
            Assert.IsTrue(s.P1.Invulnerable); s.P0.StruckMask=0;
            World.Strike(ref s,0,FixVec3.Up,e); Assert.AreEqual((Fix)400,s.P1.Health);
        }
        [Test] public void GreatswordGuardBreakerAndArmorMatchRules()
        {
            var s=Pair(Style.Greatsword); var e=new EventBuffer(); s.P0.Swing=2; Guard(ref s,false);
            World.Strike(ref s,0,FixVec3.Up,e);
            Assert.AreEqual(400-SwordData.Get(Style.Greatsword,2).Damage*Fix.Ratio(1,2),s.P1.Health); Assert.IsTrue(s.P1.Staggered);
            Assert.AreEqual(WeaponState.Active,s.P0.Weapon); Assert.IsTrue(Has(e,EventKind.GuardBreak));
            s=Pair(); s.P1.SuperArmor=true;
            World.Strike(ref s,0,FixVec3.Up,e);
            Assert.AreEqual(Fix.Ratio(925,10),s.P1.Poise);
            Assert.Less(Fix.Abs(s.P1.Knock.Length-Fix.Ratio(66,100)).Raw,32);
        }
        [Test] public void GreatswordCannotIgnoreParryAndDoesNotRecoilOnOrdinaryBlock()
        {
            var s=Pair(Style.Greatsword); var e=new EventBuffer(); s.P0.Swing=2; Guard(ref s,true);
            World.Strike(ref s,0,FixVec3.Up,e); Assert.IsTrue(s.P0.Staggered); Assert.AreEqual((Fix)400,s.P1.Health);
            s=Pair(Style.Greatsword); Guard(ref s,false);
            World.Strike(ref s,0,FixVec3.Up,e); Assert.AreEqual(WeaponState.Active,s.P0.Weapon);
        }
        [Test] public void ReaperPullStopsAtIdealDistance()
        {
            var s=Pair(Style.Reaper); var e=new EventBuffer();
            s.P1.Position.Z=Fix.Ratio(14,10); World.Strike(ref s,0,FixVec3.Up,e);
            Assert.AreEqual(Fix.Zero,s.P1.Knock.Length);
            s=Pair(Style.Reaper); s.P1.Position.Z=Fix.Ratio(17,10); World.Strike(ref s,0,FixVec3.Up,e);
            Assert.Less(s.P1.Knock.Z.Raw,0);
            Assert.Less(Fix.Abs(s.P1.Knock.Length-Fix.Ratio(175,100)).Raw,64);
        }
        [Test] public void HitStopAdvancesTickAndFreezesBodyLogic()
        {
            var s=Pair(); var e=new EventBuffer();
            World.Strike(ref s,0,FixVec3.Up,e); Fix time=s.P0.BodyTime; ulong rng=s.Rng;
            int ticks=s.P0.HitStop;
            for(int i=0;i<ticks;i++) World.Step(ref s,new PlayerInput { Buttons=Buttons.Attack },default,e);
            Assert.AreEqual(ticks,s.Tick); Assert.AreEqual(time,s.P0.BodyTime); Assert.AreEqual(rng,s.Rng);
            Assert.AreEqual(0,s.P0.HitStop);
        }
        [Test] public void AttackPressedDuringHitStopIsQueuedAfterTheFreeze()
        {
            var s=Pair(); var e=new EventBuffer();
            s.P0.WeaponTime=SwordData.Get(Style.Knight,0).Active*SwordData.QueueFrom; // já na janela do golpe seguinte
            World.Strike(ref s,0,FixVec3.Up,e); int stop=s.P0.HitStop;
            for(int i=0;i<stop;i++) World.Step(ref s,new PlayerInput { Buttons=i==0?Buttons.Attack:0 },default,e);
            Assert.IsTrue((s.P0.BufferedEdges&Buttons.Attack)!=0);
            World.Step(ref s,default,default,e);
            Assert.IsTrue(s.P0.Queued); Assert.AreEqual(Buttons.None,s.P0.BufferedEdges);
        }

        // ------------------------------------------------------------------ Ritmo (anti-spam)
        static void Run(ref MatchState s,int ticks,PlayerInput a=default)
        {
            var e=new EventBuffer();
            for(int i=0;i<ticks;i++) World.Step(ref s,a,default,e);
        }
        [Test] public void HitInterruptsTheTargetsSwingAndFlinches()
        {
            var s=Pair(); var e=new EventBuffer(); s.P1.Weapon=WeaponState.Windup;
            World.Strike(ref s,0,FixVec3.Up,e);
            Assert.AreEqual(WeaponState.Idle,s.P1.Weapon); Assert.IsTrue(s.P1.Hurt); Assert.IsFalse(s.P1.Staggered);
            // Durante o flinch não ataca nem guarda.
            s.P0.Weapon=WeaponState.Idle; s.P0.HitStop=0; s.P1.HitStop=0;
            World.Step(ref s,default,new PlayerInput { Buttons=Buttons.Attack|Buttons.Guard },e);
            Assert.AreEqual(WeaponState.Idle,s.P1.Weapon);
        }
        [Test] public void GreatswordTakesLessStaggerAndReachesFurther()
        {
            var s=Pair(); var e=new EventBuffer(); s.P1.Config.Style=Style.Greatsword;
            World.Strike(ref s,0,FixVec3.Up,e);
            Fix lost=s.P1.Config.MaxPoise-s.P1.Poise, flinch=s.P1.HurtUntil-s.P1.BodyTime;
            var k=Pair(); e=new EventBuffer();
            World.Strike(ref k,0,FixVec3.Up,e);
            Assert.Less(lost.Raw,(k.P1.Config.MaxPoise-k.P1.Poise).Raw);
            Assert.Less(flinch.Raw,(k.P1.HurtUntil-k.P1.BodyTime).Raw);
            // O arco do montante leva a mão mais longe do que o da Espada.
            Fix u=Fix.Ratio(1,2);
            Assert.Greater(SwordGeometry.Arc(Style.Greatsword,0,u,Fix.Ratio(3,10)).Hand.Length.Raw,
                SwordGeometry.Arc(Style.Knight,0,u,Fix.Ratio(3,10)).Hand.Length.Raw);
        }
        [Test] public void GreatswordSuperArmorIsNotInterrupted()
        {
            var s=Pair(); var e=new EventBuffer();
            s.P1.Config.Style=Style.Greatsword; s.P1.Weapon=WeaponState.Windup; s.P1.SuperArmor=true;
            World.Strike(ref s,0,FixVec3.Up,e);
            Assert.AreEqual(WeaponState.Windup,s.P1.Weapon); Assert.IsFalse(s.P1.Hurt);
        }
        [Test] public void SwingsSpendStaminaAndTiredSwingsAreSlower()
        {
            var s=Pair(); s.P0.Weapon=WeaponState.Idle;
            Run(ref s,1,new PlayerInput { Buttons=Buttons.Attack });
            Assert.AreEqual(WeaponState.Windup,s.P0.Weapon); Assert.IsFalse(s.P0.TiredSwing);
            Assert.AreEqual(s.P0.Config.MaxStamina-SwordData.Get(Style.Knight,0).Stamina,s.P0.Stamina);
            Fix fresh=s.P0.WeaponTime;
            // Sem stamina o golpe sai na mesma, mas 30% mais lento.
            s=Pair(); s.P0.Weapon=WeaponState.Idle; s.P0.Stamina=5; s.P0.StaminaUsedAt=s.P0.BodyTime;
            Run(ref s,1,new PlayerInput { Buttons=Buttons.Attack });
            Assert.AreEqual(WeaponState.Windup,s.P0.Weapon); Assert.IsTrue(s.P0.TiredSwing);
            Assert.AreEqual(Fix.Zero,s.P0.Stamina);
            Assert.Less(Fix.Abs(s.P0.WeaponTime*SwordData.TiredSlowdown-fresh).Raw,64);
        }
        [Test] public void StaminaRecoversOnlyAfterAPause()
        {
            var s=Pair(); s.P0.Weapon=WeaponState.Idle; s.P0.Stamina=50; s.P0.StaminaUsedAt=0;
            Run(ref s,(int)(SwordData.StaminaDelay*60).Floor-2);
            Assert.AreEqual((Fix)50,s.P0.Stamina);
            Run(ref s,60);
            Assert.Greater(s.P0.Stamina.Raw,((Fix)70).Raw);
        }
        [Test] public void EarlyClickIsLostAndLateClickChains()
        {
            var s=Pair(); s.P0.Weapon=WeaponState.Windup;
            Run(ref s,1,new PlayerInput { Buttons=Buttons.Attack });
            Assert.IsFalse(s.P0.Queued);
            s=Pair(); s.P0.WeaponTime=SwordData.Get(Style.Knight,0).Active*SwordData.QueueFrom;
            Run(ref s,1,new PlayerInput { Buttons=Buttons.Attack });
            Assert.IsTrue(s.P0.Queued);
        }
        [Test] public void WhiffHasFullRecoveryWithoutGuardCancel()
        {
            SwingData sw=SwordData.Get(Style.Knight,0);
            var s=Pair(); s.P0.Weapon=WeaponState.Recovery; s.P0.WeaponTime=sw.Recovery*Fix.Ratio(6,10); s.P0.StruckMask=0;
            Run(ref s,1,new PlayerInput { Buttons=Buttons.Guard });
            Assert.AreEqual(WeaponState.Recovery,s.P0.Weapon);
            s=Pair(); s.P0.Weapon=WeaponState.Recovery; s.P0.WeaponTime=sw.Recovery*Fix.Ratio(6,10); s.P0.StruckMask=2;
            Run(ref s,1,new PlayerInput { Buttons=Buttons.Guard });
            Assert.AreEqual(WeaponState.Guard,s.P0.Weapon);
            s=Pair(); s.P0.Weapon=WeaponState.Recovery; s.P0.WeaponTime=sw.Recovery*Fix.Ratio(6,10); s.P0.Queued=true;
            Run(ref s,1);
            Assert.AreEqual(WeaponState.Recovery,s.P0.Weapon); // falhou: o seguinte espera pelo fim
        }
        [Test] public void LastHitOfTheComboRestsBeforeRestarting()
        {
            int last=SwordData.Count(Style.Knight)-1;
            var s=Pair(); s.P0.Weapon=WeaponState.Idle; s.P0.Swing=last; s.P0.IdleSince=s.P0.BodyTime;
            Run(ref s,1,new PlayerInput { Buttons=Buttons.Attack });
            Assert.AreEqual(WeaponState.Idle,s.P0.Weapon);
            Run(ref s,(int)(SwordData.ComboRest*60).Ceil);
            Run(ref s,1,new PlayerInput { Buttons=Buttons.Attack });
            Assert.AreEqual(WeaponState.Windup,s.P0.Weapon); Assert.AreEqual(0,s.P0.Swing);
        }
        [Test] public void BlockingDrainsStaminaUntilTheGuardBreaks()
        {
            var s=Pair(); var e=new EventBuffer(); Guard(ref s,false);
            World.Strike(ref s,0,new FixVec3(0,1,1),e);
            Assert.Less(s.P1.Stamina.Raw,s.P1.Config.MaxStamina.Raw); Assert.AreEqual(WeaponState.Guard,s.P1.Weapon);
            Assert.AreEqual(s.P1.Config.MaxPoise,s.P1.Poise); // bloquear não gasta postura
            s=Pair(); e=new EventBuffer(); Guard(ref s,false); s.P1.Stamina=1;
            World.Strike(ref s,0,new FixVec3(0,1,1),e);
            Assert.IsTrue(s.P1.Staggered); Assert.AreEqual(WeaponState.Idle,s.P1.Weapon);
            Assert.IsTrue(Enumerable.Range(0,e.Count).Any(i=>e[i].Kind==EventKind.GuardBreak));
        }
        [Test] public void GuardToggleLockoutDoesNotRearmParry()
        {
            var s=Pair(); s.P0.Weapon=WeaponState.Idle; var e=new EventBuffer();
            World.Step(ref s,new PlayerInput { Buttons=Buttons.Guard },default,e);
            Assert.IsTrue(s.P0.ParryArmed);
            World.Step(ref s,default,default,e);
            World.Step(ref s,new PlayerInput { Buttons=Buttons.Guard },default,e);
            Assert.IsFalse(s.P0.ParryArmed);
        }
        [TestCase(Style.Knight)] [TestCase(Style.Fencer)] [TestCase(Style.Reaper)]
        [TestCase(Style.Daggers)] [TestCase(Style.Greatsword)]
        public void ThreeMinuteCombatIsDeterministicAndActuallyHits(Style style)
        {
            var a=Pair(style); a.P0.Config.MaxHealth=a.P1.Config.MaxHealth=100000; a.P0.Health=a.P1.Health=100000;
            var b=a; var ea=new EventBuffer(); var eb=new EventBuffer(); int contacts=0;
            for(int i=0;i<10800;i++) {
                var p=BotInputs.Combat(i,0,a); var q=BotInputs.Combat(i,1,a);
                World.Step(ref a,p,q,ea); World.Step(ref b,p,q,eb);
                Assert.AreEqual(StateCodec.Checksum(a),StateCodec.Checksum(b),"tick "+i);
                for(int k=0;k<ea.Count;k++) if(ea[k].Kind==EventKind.Hit || ea[k].Kind==EventKind.Block || ea[k].Kind==EventKind.Parry || ea[k].Kind==EventKind.Clash) contacts++;
            }
            Assert.Greater(contacts,30,"Bot test must exercise real contacts");
            TestContext.WriteLine(style+": "+contacts+" contacts, checksum "+StateCodec.Checksum(a).ToString("X16"));
        }
        [Test] public void SwingNumbersAndArcGeometryMatchCurrentSwordWielder()
        {
            Type type=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("AnimLab.KonoSuba.SwordWielder")).First(t=>t!=null);
            var go=new GameObject("Netcode parity test");
            try {
                var sword=go.AddComponent(type); MethodInfo arc=type.GetMethod("Arc",BindingFlags.Instance|BindingFlags.NonPublic);
                var styleField=type.GetField("style");
                foreach(Style style in new[] {Style.Knight,Style.Fencer,Style.Reaper,Style.Daggers,Style.Greatsword}) {
                    styleField.SetValue(sword,Enum.ToObject(styleField.FieldType,(int)style));
                    // Os golpes do SwordWielder vêm do SwordData (fonte única); confirma a conversão para float.
                    var moves=(Array)type.GetProperty("Moves").GetValue(sword);
                    Assert.AreEqual(SwordData.Count(style),moves.Length);
                    for(int i=0;i<moves.Length;i++) {
                        object original=moves.GetValue(i); var expected=SwordData.Get(style,i);
                        foreach(string field in new[] {"Windup","Active","Recovery","Damage","Stagger","Push","Lift","Lunge","HitStop"}) {
                            string source=char.ToLowerInvariant(field[0])+field.Substring(1);
                            decimal actual=Convert.ToDecimal(original.GetType().GetField(source).GetValue(original));
                            Fix value=(Fix)typeof(SwingData).GetField(field).GetValue(expected);
                            Assert.Less(Math.Abs(actual-value.Raw/(decimal)Fix.Unit),0.00001m,style+" "+i+" "+field);
                        }
                        for(int step=0;step<=8;step++) {
                            float u=step/8f; object pose=arc.Invoke(sword,new object[] {i,u});
                            var pos=(Vector3)pose.GetType().GetField("pos").GetValue(pose);
                            var rot=(Quaternion)pose.GetType().GetField("rot").GetValue(pose);
                            BladePose fixedPose=SwordGeometry.Arc(style,i,Fix.Ratio(step,8),Fix.Ratio(3,10));
                            Assert.Less((pos-ToVector(fixedPose.Hand)).magnitude,0.0002f,style+" hand");
                            Assert.Less((rot*Vector3.up-ToVector(fixedPose.Y)).magnitude,0.001f,style+" blade");
                            Assert.Less((rot*Vector3.right-ToVector(fixedPose.X)).magnitude,0.001f,style+" edge");
                        }
                    }
                }
            } finally { UnityEngine.Object.DestroyImmediate(go); }
        }
        static Vector3 ToVector(FixVec3 p) => new Vector3((float)(p.X.Raw/(double)Fix.Unit),(float)(p.Y.Raw/(double)Fix.Unit),(float)(p.Z.Raw/(double)Fix.Unit));
        static bool Has(EventBuffer e,EventKind kind) { for(int i=0;i<e.Count;i++) if(e[i].Kind==kind) return true; return false; }
    }
}
