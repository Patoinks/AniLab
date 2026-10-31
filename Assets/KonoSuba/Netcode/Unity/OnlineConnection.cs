using System;
using System.Diagnostics;
using AnimLab.Netcode.Sim;
using AnimLab.Netcode.Rollback;
using AnimLab.Netcode.Transport;
using UnityEngine;

namespace AnimLab.Netcode.Unity
{
    public static class NetworkClock
    {
        static readonly Stopwatch clock=Stopwatch.StartNew();
        public static long Now => clock.ElapsedMilliseconds;
    }
    public sealed class OnlineConnection : MonoBehaviour
    {
        static OnlineConnection pending;
        UdpTransport transport;
        OnlineOptions options;
        bool transferred,automatic,background;
        int limit;
        string report,png;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register()
        {
            OnlineMenu.HostRequested=o=>Begin(o,true);
            OnlineMenu.JoinRequested=o=>Begin(o,false);
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void CommandLine()
        {
            string host=Arg("-netHost",null),join=Arg("-netJoin",null);
            if(host==null && join==null) return;
            var o=new OnlineOptions { Port=host==null?7777:int.Parse(host),Endpoint=join,Delay=2,Interpolate=true,
                Controls=Arg("-netBot","0")=="1"?LocalControl.Bots:LocalControl.HumanVsBot,
                FirstStyle=Enum.TryParse(Arg("-netStyle","Knight"),true,out Style style)?style:Style.Knight };
            var connection=Begin(o,host!=null); connection.automatic=true;
            connection.limit=Math.Max(60,int.Parse(Arg("-netTicks","10800"))/30*30);
            connection.report=Arg("-netReport",host!=null?"Logs/netcode/build_host.txt":"Logs/netcode/build_join.txt");
            connection.png=Arg("-netPng",null);
        }
        public static string Arg(string name,string fallback)
        {
            string[] args=Environment.GetCommandLineArgs(); int index=Array.IndexOf(args,name);
            return index>=0 && index+1<args.Length?args[index+1]:fallback;
        }
        public static OnlineConnection Begin(OnlineOptions options,bool host)
        {
            Cancel(); NetMatch.Active?.Stop();
            var player=GameObject.Find("Player"); if(player==null) throw new InvalidOperationException("Player missing");
            PlayerConfig config=NetMatch.CaptureConfig(player); config.Style=options.FirstStyle;
            if(Arg("-netEndurance","0")=="1") config.MaxHealth=100000;
            UdpTransport socket=host?new UdpTransport(options.Port,p=>MatchHandshake.Accept(p,config,options.Delay,(ulong)DateTime.UtcNow.Ticks)):
                new UdpTransport(options.Endpoint,MatchHandshake.Offer(config,options.Delay));
            var connection=new GameObject("OnlineConnection").AddComponent<OnlineConnection>();
            pending=connection; connection.transport=socket; connection.options=options;
            connection.background=Application.runInBackground; Application.runInBackground=true;
            OnlineMenu.Current?.ShowMessage(host?"Hosting UDP port "+socket.LocalPort+" · waiting for peer":"Connecting to "+options.Endpoint,true);
            return connection;
        }
        public static void Cancel()
        {
            if(pending==null) return;
            var p=pending; pending=null; p.enabled=false;
            if(!p.transferred) { p.transport?.Dispose(); Application.runInBackground=p.background; }
            Destroy(p.gameObject);
        }
        void Update()
        {
            transport.Update(NetworkClock.Now);
            if(transport.State==LinkState.Connected)
            {
                try {
                    MatchState state=MatchHandshake.Decode(transport.MatchPayload,out int delay);
                    var match=new GameObject("NetMatch").AddComponent<NetMatch>();
                    match.MovementDemo=false; match.UseRollback=true; match.InitialState=state;
                    match.LocalPlayer=transport.IsHost?0:1; match.InputDelay=delay; match.RestoreBackground=background;
                    match.Controls=options.Controls; match.Interpolate=options.Interpolate;
                    match.DirectPeer=new RollbackPeer(new RollbackSession(state,match.LocalPlayer,delay),transport,transport.SessionId);
                    if(automatic) { match.TickLimit=limit; match.ReportPath=report; var run=match.gameObject.AddComponent<NetBuildRun>(); run.Match=match; run.PngPath=png; }
                    transferred=true; pending=null; OnlineMenu.Current?.SetOpen(false); Destroy(gameObject);
                } catch(Exception e) { Fail(e.Message); }
            }
            else if(transport.State!=LinkState.Connecting) Fail(transport.Error);
        }
        void Fail(string error)
        {
            OnlineMenu.Current?.ShowMessage(error,true);
            if(automatic) { UnityEngine.Debug.LogError(error); Application.Quit(2); }
            Cancel();
        }
        void OnDestroy()
        {
            if(!transferred) { transport?.Dispose(); Application.runInBackground=background; }
            if(pending==this) pending=null;
        }
    }
}

