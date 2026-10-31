using AnimLab.Netcode.Steam;
using UnityEngine;

namespace AnimLab.Netcode.Unity
{
    [DefaultExecutionOrder(-80)]
    public sealed class SteamOnline : MonoBehaviour
    {
        static SteamOnline instance;
        static string error="";
        SteamClient client;
        OnlineOptions options;
        public static SteamClient Client => instance!=null?instance.client:null;
        public static string Status => Client?.Status??error;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Shutdown(); instance=null; error=""; }
        public static bool Ensure()
        {
            if(Client!=null) return true;
            if(OnlineConnection.Arg("-netSteamDisabled","0")=="1") { error="Steam is not running"; return false; }
            if(!SteamClient.TryStart(out var steam,out error)) return false;
            instance=new GameObject("SteamOnline").AddComponent<SteamOnline>(); instance.client=steam;
            steam.LobbyReady+=instance.OnLobbyReady; steam.PeerLeft+=instance.OnPeerLeft;
            return true;
        }
        public static void Host(OnlineOptions options)
        {
            if(!Ensure()) return;
            OnlineConnection.Cancel(); NetMatch.Active?.Stop(); Client.Leave();
            instance.options=options; instance.options.Controls=LocalControl.HumanVsBot;
            Client.Host(options.FirstStyle);
        }
        public static void Join(ulong lobby,OnlineOptions options)
        {
            if(!Ensure()) return;
            OnlineConnection.Cancel(); NetMatch.Active?.Stop(); Client.Leave();
            instance.options=options; instance.options.Controls=LocalControl.HumanVsBot;
            Client.Join(lobby);
        }
        public static void JoinCode(ulong lobby,OnlineOptions options)
        {
            if(!Ensure()) return;
            OnlineConnection.Cancel(); NetMatch.Active?.Stop(); Client.Leave();
            instance.options=options; instance.options.Controls=LocalControl.HumanVsBot;
            Client.JoinCode(lobby);
        }
        public static void Refresh() { if(Ensure()) Client.Refresh(NetworkClock.Now); }
        public static void Leave() { Client?.Leave(); }
        void OnLobbyReady(bool host,ulong owner)
        {
            try { OnlineConnection.BeginSteam(options,client,host,owner); }
            catch(System.Exception e) { client.Leave(); OnlineMenu.Current?.ShowMessage(e.Message,true); }
        }
        void OnPeerLeft(string reason)
        {
            OnlineConnection.Cancel();
            if(NetMatch.Active?.ConnectionName=="Steam relay") NetMatch.Active.Stop();
            OnlineMenu.Current?.ShowMessage(reason,true);
        }
        void Update() { client?.Update(NetworkClock.Now,OnlineMenu.Current?.IsOpen==true); }
        public static void Shutdown()
        {
            OnlineConnection.Cancel();
            if(NetMatch.Active?.ConnectionName=="Steam relay") NetMatch.Active.Stop();
            var previous=instance; instance=null;
            SteamClient.Active?.Dispose();
            if(previous!=null) { previous.client=null; Destroy(previous.gameObject); }
        }
        void OnApplicationQuit() { Shutdown(); }
        void OnDestroy() { if(instance==this) Shutdown(); }
    }
}
