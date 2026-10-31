using System.Collections;
using System.Collections.Generic;
using AnimLab.Netcode.Sim;
using AnimLab.Netcode.Rollback;
using AnimLab.Netcode.Unity;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Motion = AnimLab.Netcode.Sim.Motion;

namespace AnimLab.Netcode.Tests
{
    public class PresentationTests
    {
        [UnityTearDown] public IEnumerator Cleanup()
        {
            if (Application.isPlaying)
            {
                NetMatch.Active?.Stop(); OnlineMenu.Current?.SetOpen(false); Time.captureFramerate=0;
                yield return new ExitPlayMode();
            }
        }
        [Test] public void InputLatchRetainsEdgesUntilScheduledAndDoesNotRepeatThem()
        {
            var latch=new InputLatch();
            latch.Sample(new PlayerInput { LookYaw=256,Buttons=Buttons.Attack|Buttons.Jump|Buttons.Guard,Targeted=true });
            latch.Sample(new PlayerInput { LookYaw=512,Buttons=Buttons.Run });
            Assert.AreEqual(Buttons.Attack|Buttons.Jump|Buttons.Run,latch.Pending.Buttons);
            Assert.AreEqual(512,latch.Pending.LookYaw);
            latch.Consume(); Assert.AreEqual(Buttons.Run,latch.Pending.Buttons);
            latch.Consume(); Assert.AreEqual(Buttons.Run,latch.Pending.Buttons);
        }
        [Test] public void CameraSpaceInputQuantizesToWorldDirection()
        {
            var p=InputSampling.Quantize(new Vector3(1,0,0),270,Buttons.Guard,true);
            Assert.AreEqual(64,p.MoveAngle); Assert.AreEqual(15,p.Magnitude); Assert.AreEqual(768,p.LookYaw); Assert.IsTrue(p.Targeted);
            Assert.Less(Fix.Abs(p.Wish.X-1).Raw,32);
        }
        [Test] public void AirborneCapsulesDoNotPushEachOtherAcrossVerticalGap()
        {
            var s=MatchState.Create(); s.P0.Position=new FixVec3(0,3,0); s.P0.Motion=Motion.Air; s.P1.Position=FixVec3.Zero;
            World.Step(ref s,default,default,new EventBuffer());
            Assert.AreEqual(Fix.Zero,s.P0.Position.X); Assert.AreEqual(Fix.Zero,s.P1.Position.X);
            Assert.AreEqual(Fix.Zero,s.P0.Position.Z); Assert.AreEqual(Fix.Zero,s.P1.Position.Z);
        }
        [UnityTest] public IEnumerator OnlineMenuDrivesSceneAndRestoresOfflineState()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/KonoSubaVoid.unity");
            yield return new EnterPlayMode();
            int oldRate=Time.captureFramerate; Time.captureFramerate=60;
            var player=GameObject.Find("Player");
            var body=(INetCombatView)System.Array.Find(player.GetComponents<MonoBehaviour>(),c=>c is INetCombatView);
            var mover=(INetMovementView)System.Array.Find(player.GetComponents<MonoBehaviour>(),c=>c is INetMovementView);
            var sword=(INetSwordView)System.Array.Find(player.GetComponents<MonoBehaviour>(),c=>c is INetSwordView);
            var flags=new List<KeyValuePair<Behaviour,bool>>();
            foreach(var b in player.GetComponents<Behaviour>()) flags.Add(new KeyValuePair<Behaviour,bool>(b,b.enabled));
            PlayerConfig before=NetMatch.CaptureConfig(player);
            float health=(float)((Component)body).GetType().GetField("health").GetValue(body);
            var menu=OnlineMenu.Current;
            Assert.IsNotNull(menu); menu.SetOpen(true); yield return null;
            Assert.IsTrue(NetMatch.InputBlocked);
            Assert.IsNotNull(menu.Window.Q<Button>("OnlineLocalTest"));
            Vector3 oldPosition=player.transform.position; Quaternion oldRotation=player.transform.rotation;
            menu.StartLocal(); yield return null; yield return null;
            var match=NetMatch.Active;
            Assert.IsNotNull(match); Assert.IsTrue(match.Ready);
            match.Controls=LocalControl.Bots;
            Assert.IsTrue(mover.ExternalDrive && body.ExternalDrive && sword.ExternalDrive);
            Assert.AreEqual(ViewConvert.Fixed(player.GetComponent<CharacterController>().radius),match.State.P0.Config.Radius);
            Assert.AreEqual(before.DamageScale,match.State.P0.Config.DamageScale);
            for(int i=0;i<180;i++) yield return null;
            Assert.IsNull(match.Loopback.A.Desync); Assert.IsNull(match.Loopback.B.Desync);
            Assert.Greater(match.Loopback.A.ChecksumsVerified,0);
            Assert.Less((player.transform.position-ViewConvert.Vector(match.State.P0.Position)).magnitude,0.5f);
            Assert.IsNotNull(match.Actor(1));
            match.SwitchView(); Assert.AreEqual(1,match.ViewPeer);
            match.Stop();
            Assert.AreEqual(1f,Time.timeScale);
            Assert.IsFalse(mover.ExternalDrive || body.ExternalDrive || sword.ExternalDrive);
            Assert.AreEqual(oldPosition,player.transform.position); Assert.AreEqual(oldRotation,player.transform.rotation);
            Assert.AreEqual(health,(float)((Component)body).GetType().GetField("health").GetValue(body));
            PlayerConfig after=NetMatch.CaptureConfig(player);
            Assert.AreEqual(before.Style,after.Style); Assert.AreEqual(before.MaxHealth,after.MaxHealth);
            foreach(var b in flags) Assert.AreEqual(b.Value,b.Key.enabled,b.Key.GetType().Name);
            menu.SetOpen(false); yield return null; yield return null;
            Assert.IsNull(NetMatch.Active); Assert.IsNull(GameObject.Find("NetRemote")); Assert.IsFalse(NetMatch.InputBlocked);
            Assert.IsTrue(GameObject.Find("Duelist").activeSelf); Assert.IsTrue(GameObject.Find("DummyEnemy").activeSelf);
            Time.captureFramerate=oldRate;
            yield return new ExitPlayMode();
        }
    }
}
