using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using AnimLab.Netcode.Sim;
using AnimLab.Netcode.Rollback;
using AnimLab.Netcode.Transport;
using AnimLab.Netcode.Steam;
using AnimLab.Netcode.Unity;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace AnimLab.Netcode.Tests
{
    public class SteamTests
    {
        static Dictionary<string,string> Metadata() => new Dictionary<string,string> {
            [SteamLobbyInfo.GameKey]=SteamLobbyInfo.Game,[SteamLobbyInfo.ProtocolKey]=SteamLobbyInfo.Protocol,[SteamLobbyInfo.BuildKey]=SteamLobbyInfo.Build,
            ["host_id"]="42",["host"]="Friend",["style"]="Reaper",["state"]="Waiting"
        };
        static bool Read(Dictionary<string,string> data,out SteamLobbyInfo lobby,int members=1,int limit=2,ulong owner=42) =>
            SteamLobbyInfo.TryRead(100,owner,members,limit,key=>data.TryGetValue(key,out var value)?value:"",out lobby);
        [Test] public void CompatibleLobbyHasHostStyleAndStatus()
        {
            Assert.IsTrue(Read(Metadata(),out var lobby)); Assert.IsTrue(lobby.CanJoin);
            Assert.AreEqual("Friend",lobby.Host); Assert.AreEqual(Style.Reaper,lobby.Style); Assert.AreEqual("Waiting",lobby.Status);
        }
        [TestCase("animlab_game","Spacewar")]
        [TestCase("animlab_protocol","old")]
        [TestCase("animlab_build","other-build")]
        [TestCase("host_id","43")]
        [TestCase("host","")]
        [TestCase("style","Iaido")]
        [TestCase("style","0")]
        [TestCase("style","Bogus")]
        [TestCase("state","Unknown")]
        public void GlobalSpacewarLobbiesAreStrictlyFiltered(string key,string value)
        {
            var data=Metadata(); data[key]=value; Assert.IsFalse(Read(data,out _));
        }
        [Test] public void FullConnectingAndWrongCapacityLobbiesCannotBeJoined()
        {
            var data=Metadata(); Assert.IsFalse(Read(data,out _,1,4)); Assert.IsFalse(Read(data,out _,0)); Assert.IsFalse(Read(data,out _,1,2,99));
            Assert.IsTrue(Read(data,out var full,2)); Assert.IsFalse(full.CanJoin);
            data["state"]="Playing"; Assert.IsTrue(Read(data,out var playing,2)); Assert.IsFalse(playing.CanJoin);
            data["state"]="Connecting"; Assert.IsTrue(Read(data,out var connecting)); Assert.IsFalse(connecting.CanJoin);
        }
        sealed class Channel : IDatagramChannel
        {
            public readonly Queue<KeyValuePair<string,byte[]>> Inbox=new Queue<KeyValuePair<string,byte[]>>();
            public Channel Other; public string Name; public int Drop;
            public LinkState State { get; set; }=LinkState.Connected;
            public string Error => "";
            public void Update(long nowMs) { }
            public bool Send(byte[] packet,string remote) { if(Drop-->0) return true; Other.Inbox.Enqueue(new KeyValuePair<string,byte[]>(Name,(byte[])packet.Clone())); return true; }
            public bool TryReceive(out byte[] packet,out string sender) {
                packet=null; sender=null; if(Inbox.Count==0) return false;
                var value=Inbox.Dequeue(); packet=value.Value; sender=value.Key; return true;
            }
            public void Dispose() { State=LinkState.Disconnected; }
        }
        [Test] public void SharedHandshakeSurvivesLossAndRejectsUnrelatedPeers()
        {
            var a=new Channel { Name="host",Drop=2 }; var b=new Channel { Name="join",Drop=2 }; a.Other=b; b.Other=a;
            var c=PlayerConfig.Default;
            using(var host=new DatagramSession(a,p=>MatchHandshake.Accept(p,c,2,55)))
            using(var join=new DatagramSession(b,"host",MatchHandshake.Offer(c,3))) {
                a.Inbox.Enqueue(new KeyValuePair<string,byte[]>("stranger",new byte[22]));
                long now=0;
                for(int i=0;i<300 && (host.State!=LinkState.Connected || join.State!=LinkState.Connected);i++) { host.Update(now); join.Update(now); now+=17; }
                Assert.AreEqual(LinkState.Connected,host.State); Assert.AreEqual(LinkState.Connected,join.State);
                Assert.AreEqual(host.SessionId,join.SessionId); Assert.AreEqual(1,host.Rejected);
                MatchHandshake.Decode(host.MatchPayload,out int delay); Assert.AreEqual(3,delay);
                join.Send(new byte[] {1,2,3}); host.Update(now);
                Assert.IsTrue(host.TryReceive(out var packet)); CollectionAssert.AreEqual(new byte[] {1,2,3},packet);
                join.Dispose(); host.Update(now+17); Assert.AreEqual(LinkState.Disconnected,host.State);
            }
        }
        [Test] public void SteamHostCanWaitWithoutAHandshakeTimeout()
        {
            var a=new Channel { State=LinkState.Connecting };
            using(var host=new DatagramSession(a,_=>new byte[] {1},true)) {
                host.Update(0); host.Update(60000); Assert.AreEqual(LinkState.Connecting,host.State);
                a.State=LinkState.Connected; host.Update(60001); host.Update(70002); Assert.AreEqual(LinkState.TimedOut,host.State);
            }
        }
        [Test] public void NativeSteamSocketPairReusesHandshakeRollbackAndChecksums()
        {
            if(!SteamClient.TryStart(out var client,out string error)) Assert.Ignore(error);
            using(client) {
                string report=SteamVerification.Loopback();
                Directory.CreateDirectory("Logs/netcode"); File.WriteAllText("Logs/netcode/steam_editmode.txt",report); TestContext.WriteLine(report);
            }
            Assert.IsFalse(SteamClient.Initialized);
        }
        [UnityTest] public IEnumerator SteamLobbyLeavesAcrossTwoPlaysWithoutDomainReload()
        {
            if(!SteamClient.TryStart(out var probe,out string error)) Assert.Ignore(error);
            probe.Dispose();
            bool enabled=EditorSettings.enterPlayModeOptionsEnabled; var previous=EditorSettings.enterPlayModeOptions;
            try {
                EditorSettings.enterPlayModeOptionsEnabled=true; EditorSettings.enterPlayModeOptions=EnterPlayModeOptions.DisableDomainReload;
                EditorSceneManager.OpenScene("Assets/Scenes/KonoSubaVoid.unity");
                for(int play=0;play<2;play++) {
                    yield return new EnterPlayMode(); yield return null;
                    var menu=OnlineMenu.Current; Assert.IsNotNull(menu); menu.SetOpen(true); yield return null;
                    var client=SteamOnline.Client; Assert.IsNotNull(client,SteamOnline.Status); Assert.AreEqual(0UL,client.LobbyId);
                    Assert.IsNotNull(menu.Window.Q<Button>("OnlineSteamHost")); Assert.IsNotNull(menu.Window.Q<Button>("OnlineLocalTest"));
                    var options=new OnlineOptions { FirstStyle=Style.Reaper,Delay=2,Interpolate=true };
                    SteamOnline.Host(options); menu.Leave();
                    double until=Time.realtimeSinceStartupAsDouble+30;
                    while(client.Busy && Time.realtimeSinceStartupAsDouble<until) yield return null;
                    Assert.IsFalse(client.Busy); Assert.AreEqual(0UL,client.LobbyId);
                    SteamOnline.Host(options); until=Time.realtimeSinceStartupAsDouble+30;
                    while((client.LobbyId==0 || !client.LastSearchContainsOwn) && Time.realtimeSinceStartupAsDouble<until) yield return null;
                    Assert.AreNotEqual(0UL,client.LobbyId,client.Status); Assert.IsTrue(client.LastSearchContainsOwn,client.SearchStatus);
                    Assert.IsTrue(System.Linq.Enumerable.Any(client.Lobbies,l=>l.Id==client.LobbyId && l.Style==Style.Reaper && l.CanJoin));
                    menu.Leave(); Assert.AreEqual(0UL,client.LobbyId); Assert.IsNull(NetMatch.Active);
                    SteamOnline.Host(options); until=Time.realtimeSinceStartupAsDouble+30;
                    while(client.LobbyId==0 && Time.realtimeSinceStartupAsDouble<until) yield return null;
                    Assert.AreNotEqual(0UL,client.LobbyId,client.Status);
                    yield return new ExitPlayMode();
                    Assert.IsFalse(SteamClient.Initialized); Assert.IsNull(SteamClient.Active); Assert.IsNull(SteamOnline.Client);
                }
                Directory.CreateDirectory("Logs/netcode"); File.WriteAllText("Logs/netcode/steam_editor_lifecycle.txt","plays=2; domain_reload=false; canceled_create=true; raw_list_contains_own=true; metadata=true; leave=true; stop_leaves=true; sdk_shutdown=true; reinitialize=true");
            } finally {
                EditorSettings.enterPlayModeOptions=previous; EditorSettings.enterPlayModeOptionsEnabled=enabled;
                if(!Application.isPlaying) SteamOnline.Shutdown();
            }
        }
        [UnityTearDown] public IEnumerator Cleanup()
        {
            SteamOnline.Shutdown();
            if(Application.isPlaying) yield return new ExitPlayMode();
        }
    }
}
