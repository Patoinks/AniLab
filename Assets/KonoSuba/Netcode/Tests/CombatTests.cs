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
            Assert.AreEqual(Fix.Ratio(11,10),s.P0.StaggerUntil);
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
            Assert.AreEqual(9,s.P0.HitStop); Assert.IsTrue(Has(e,EventKind.Clash));
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
            Assert.AreEqual(Fix.Ratio(745,2),s.P1.Health); Assert.IsTrue(s.P1.Staggered);
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
            World.Strike(ref s,0,FixVec3.Up,e); int stop=s.P0.HitStop;
            for(int i=0;i<stop;i++) World.Step(ref s,new PlayerInput { Buttons=i==0?Buttons.Attack:0 },default,e);
            Assert.IsTrue((s.P0.BufferedEdges&Buttons.Attack)!=0);
            World.Step(ref s,default,default,e);
            Assert.IsTrue(s.P0.Queued); Assert.AreEqual(Buttons.None,s.P0.BufferedEdges);
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
