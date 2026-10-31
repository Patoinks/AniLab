using System;
using System.Collections;
using System.IO;
using AnimLab.Netcode.Sim;
using AnimLab.Netcode.Steam;
using UnityEngine;
using UnityEngine.UIElements;

namespace AnimLab.Netcode.Unity
{
    /// <summary>Validação com uma conta, sem input do sistema nem outro jogador.</summary>
    public sealed class SteamSmokeRun : MonoBehaviour
    {
        string result="";
        bool success;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Run()
        {
            if(OnlineConnection.Arg("-netSteamSmoke","0")=="1") new GameObject("SteamSmokeRun").AddComponent<SteamSmokeRun>();
        }
        IEnumerator Start()
        {
            Application.runInBackground=true;
            double until=Time.realtimeSinceStartupAsDouble+20;
            while(OnlineMenu.Current==null && Time.realtimeSinceStartupAsDouble<until) yield return null;
            if(OnlineMenu.Current==null) { Finish("Online menu missing"); yield break; }
            OnlineMenu.Current.SetOpen(true); yield return null;
            if(OnlineConnection.Arg("-netSteamDisabled","0")=="1") {
                success=SteamOnline.Client==null && SteamOnline.Status=="Steam is not running";
                yield return Capture();
                var player=GameObject.Find("Player"); Vector3 position=player.transform.position;
                OnlineMenu.Current.StartLocal(); yield return null; yield return null;
                var match=NetMatch.Active;
                if(match==null || !match.Ready) { success=false; Finish("Local test unavailable without Steam"); yield break; }
                match.Controls=LocalControl.Bots; until=Time.realtimeSinceStartupAsDouble+15;
                while(match.State.Tick<180 && Time.realtimeSinceStartupAsDouble<until) yield return null;
                int ticks=match.State.Tick,checks=match.Loopback.A.ChecksumsVerified;
                success&=ticks>=180 && checks>0 && match.Loopback.A.Desync==null && match.Loopback.B.Desync==null;
                OnlineMenu.Current.Leave(); success&=SteamOnline.Client==null && player.transform.position==position;
                Finish("steam_absent_simulated="+success+"; status="+SteamOnline.Status+"; local_test_ticks="+ticks+"; checksums="+checks+"; offline_restored="+(player.transform.position==position)); yield break;
            }
            var client=SteamOnline.Client;
            if(client==null) { Finish(SteamOnline.Status); yield break; }
            try { result="app=480; initialized=true; "+SteamVerification.Loopback()+"\n"; }
            catch(Exception e) { Finish(e.ToString()); yield break; }
            OnlineMenu.Current.Window.Q<DropdownField>().value=Style.Reaper.ToString();
            SteamOnline.Host(OnlineMenu.Current.Options);
            until=Time.realtimeSinceStartupAsDouble+30;
            while((client.LobbyId==0 || !client.LastSearchContainsOwn) && Time.realtimeSinceStartupAsDouble<until) yield return null;
            var own=System.Linq.Enumerable.FirstOrDefault(client.Lobbies,l=>l.Id==client.LobbyId);
            if(own==null || !client.LastSearchContainsOwn || own.Style!=Style.Reaper || own.Limit!=2 || own.Members!=1 || !own.CanJoin) { Finish(result+"Lobby metadata/search failed: "+client.Status+" / "+client.SearchStatus); yield break; }
            result+=$"lobby={own.Id}; host={own.Host}; style={own.Style}; state={own.Status}; members={own.Members}/2; game={SteamLobbyInfo.Game}; protocol={SteamLobbyInfo.Protocol}; build={SteamLobbyInfo.Build}; raw_list_contains_own=true; raw_results={client.LastRawResultCount}; relay_status={client.RelayStatus}\n";
            OnlineMenu.Current.Update(); yield return null; yield return Capture();
            OnlineMenu.Current.Leave();
            if(client.LobbyId!=0 || NetMatch.Active!=null) { Finish(result+"Leave failed"); yield break; }
            result+="leave=true; "; SteamOnline.Shutdown(); yield return null;
            if(SteamClient.Initialized || !SteamOnline.Ensure()) { Finish(result+"Reinitialization failed: "+SteamOnline.Status); yield break; }
            result+="shutdown=true; reinitialize=true; ";
            SteamOnline.Shutdown(); success=true; Finish(result+"two_accounts_two_pcs=not_verified");
        }
        IEnumerator Capture()
        {
            var document=FindFirstObjectByType<UIDocument>();
            if(document==null) yield break;
            var previous=document.panelSettings; var settings=Instantiate(previous);
            var ui=new RenderTexture(1280,720,24,RenderTextureFormat.ARGB32);
            settings.targetTexture=ui; settings.clearColor=true; settings.colorClearValue=Color.clear; document.panelSettings=settings;
            yield return null; yield return null;
            NetBuildRun.Capture(OnlineConnection.Arg("-netSteamPng","Logs/cap/netcode_steam_lobbies.png"),ui);
            document.panelSettings=previous; ui.Release(); Destroy(ui); Destroy(settings);
        }
        void Finish(string text)
        {
            string path=OnlineConnection.Arg("-netSteamReport","Logs/netcode/steam_smoke.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))); File.WriteAllText(path,text+"\nsuccess="+success);
            Debug.Log("STEAM SMOKE: "+text+"; success="+success); SteamOnline.Shutdown();
            Application.Quit(success?0:2);
        }
    }
}
