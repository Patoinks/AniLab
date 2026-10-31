using System;
using System.Collections.Generic;
using AnimLab.Netcode.Sim;
using UnityEngine;
using UnityEngine.UIElements;

namespace AnimLab.Netcode.Unity
{
    public struct OnlineOptions
    {
        public int Port,Latency,Jitter,Loss,Delay;
        public string Endpoint;
        public Style FirstStyle,SecondStyle;
        public LocalControl Controls;
        public bool Interpolate;
        /// <summary>Junta a IA da cena (duelista e boneco); CoOp põe os jogadores na mesma equipa contra ela.</summary>
        public bool WithAI,CoOp;
    }

    /// <summary>Janela do mesmo UIDocument do HUD. Não depende da assembly do jogo.</summary>
    public sealed class OnlineMenu : IDisposable
    {
        static readonly Color Gold=new Color(0.86f,0.70f,0.40f),Dim=new Color(0.72f,0.75f,0.82f);
        static readonly List<string> Styles=new List<string> { "Knight","Fencer","Reaper","Daggers","Greatsword" };
        readonly Func<string,int,Label> label;
        readonly Func<bool> shortcutAvailable;
        readonly VisualElement layer,window;
        readonly Button launcher,host,join;
        readonly Button steamHost,refresh;
        readonly Label steamStatus;
        readonly VisualElement steamRows;
        readonly Label status;
        readonly IntegerField port,jitter,loss,delay;
        readonly TextField endpoint;
        readonly DropdownField latency,controls,firstStyle,secondStyle;
        readonly Toggle interpolation,withAI,coop;
        readonly TextField lobbyCode;
        int closedAt=-10;
        bool open;
        string message="Offline";
        string steamSignature;
        public static OnlineMenu Current { get; private set; }
        public static Action<OnlineOptions> HostRequested,JoinRequested;
        public bool IsBlockingInput => open || Time.frameCount<=closedAt;
        public bool IsOpen => open;
        public VisualElement Window => window;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Current=null; HostRequested=null; JoinRequested=null; }

        public OnlineMenu(VisualElement root,Func<string,int,Label> makeLabel,Func<bool> canUseShortcut=null)
        {
            Current=this; label=makeLabel; shortcutAvailable=canUseShortcut;
            launcher=new Button(()=>SetOpen(!open)) { text="Online",name="OnlineButton" };
            launcher.style.position=Position.Absolute; launcher.style.right=58; launcher.style.top=24;
            launcher.style.width=92; launcher.style.height=34; launcher.style.color=Gold;
            launcher.style.backgroundColor=new Color(0.025f,0.035f,0.06f,0.9f);
            root.Add(launcher);
            layer=new VisualElement { name="OnlineLayer" };
            layer.style.position=Position.Absolute;
            layer.style.left=layer.style.right=layer.style.top=layer.style.bottom=0;
            layer.style.alignItems=Align.Center; layer.style.justifyContent=Justify.Center;
            layer.style.backgroundColor=new Color(0,0,0,0.22f);
            layer.style.display=DisplayStyle.None;
            root.Add(layer);
            window=new ScrollView { name="OnlineWindow",horizontalScrollerVisibility=ScrollerVisibility.Hidden };
            window.style.width=550; window.style.maxHeight=new Length(94,LengthUnit.Percent);
            window.style.backgroundColor=new Color(0.025f,0.035f,0.06f,0.96f);
            window.style.paddingLeft=window.style.paddingRight=18;
            window.style.paddingTop=window.style.paddingBottom=14;
            window.style.borderLeftWidth=window.style.borderRightWidth=window.style.borderTopWidth=window.style.borderBottomWidth=1;
            window.style.borderLeftColor=window.style.borderRightColor=window.style.borderTopColor=window.style.borderBottomColor=new Color(0.8f,0.83f,0.9f,0.3f);
            layer.Add(window);
            var title=Row(); var heading=label("ONLINE",22); heading.style.color=Gold; heading.style.flexGrow=1; title.Add(heading);
            title.Add(new Button(Leave) { text="Leave",name="OnlineLeave" });
            title.Add(new Button(()=>SetOpen(false)) { text="Close" }); window.Add(title);
            status=label(message,14); status.style.color=Dim; status.style.whiteSpace=WhiteSpace.Normal; status.style.marginBottom=12; window.Add(status);
            firstStyle=new DropdownField("Your style",Styles,0); window.Add(firstStyle);
            Section("STEAM LOBBIES");
            var steamActions=Row();
            steamHost=new Button(()=>SteamOnline.Host(Options)) { text="Host",name="OnlineSteamHost" }; steamHost.style.flexGrow=1;
            refresh=new Button(SteamOnline.Refresh) { text="Refresh",name="OnlineSteamRefresh" };
            steamActions.Add(steamHost); steamActions.Add(refresh); window.Add(steamActions);
            steamStatus=label("Open Online to connect to Steam",13); steamStatus.name="OnlineSteamStatus";
            steamStatus.style.color=Dim; steamStatus.style.whiteSpace=WhiteSpace.Normal; window.Add(steamStatus);
            var list=new ScrollView { name="OnlineSteamList" }; list.style.height=135; list.style.marginTop=8;
            steamRows=list.contentContainer; window.Add(list);
            // Código do lobby: o host copia-o (Copy code) e o amigo cola-o aqui, sem depender da lista.
            var codeRow=Row();
            lobbyCode=new TextField("Lobby code") { value="",name="OnlineLobbyCode" }; lobbyCode.style.flexGrow=1;
            codeRow.Add(lobbyCode);
            codeRow.Add(new Button(()=> { if(ulong.TryParse(lobbyCode.value.Trim(),out ulong id)) SteamOnline.JoinCode(id,Options); else ShowMessage("Paste the host's lobby code",true); }) { text="Join code" });
            codeRow.Add(new Button(()=> { var c=SteamOnline.Client; if(c!=null && c.LobbyId!=0) { GUIUtility.systemCopyBuffer=c.LobbyId.ToString(); ShowMessage("Lobby code copied",true); } }) { text="Copy code" });
            window.Add(codeRow);
            var directOptions=new Foldout { text="Direct IP (UDP)",value=false }; window.Add(directOptions);
            directOptions.name="OnlineDirectOptions";
            var direct=Row();
            port=new IntegerField("Port") { value=7777,name="OnlinePort" }; port.style.width=230;
            host=new Button(()=>Request(true)) { text="Host" }; host.style.flexGrow=1; direct.Add(port); direct.Add(host); directOptions.Add(direct);
            var connect=Row();
            endpoint=new TextField("Address") { value="127.0.0.1:7777",name="OnlineAddress" }; endpoint.style.width=340;
            join=new Button(()=>Request(false)) { text="Join" }; join.style.flexGrow=1; connect.Add(endpoint); connect.Add(join); directOptions.Add(connect);
            withAI=new Toggle("AI enemies (Duelist, Dummy)") { value=true }; window.Add(withAI);
            coop=new Toggle("Co-op vs AI") { value=false }; window.Add(coop);
            var localOptions=new Foldout { text="Local test & advanced settings",value=false }; window.Add(localOptions);
            localOptions.name="OnlineLocalOptions";
            controls=new DropdownField("Controls",new List<string> { "You vs bot","Bot vs bot","Two keyboards" },0); localOptions.Add(controls);
            secondStyle=new DropdownField("Opponent style",Styles,1); localOptions.Add(secondStyle);
            latency=new DropdownField("Latency (one way)",new List<string> { "0 ms","60 ms","120 ms","200 ms" },2); localOptions.Add(latency);
            jitter=new IntegerField("Jitter (± ms)") { value=20 }; loss=new IntegerField("Packet loss (%)") { value=2 };
            delay=new IntegerField("Input delay (ticks)") { value=2 }; localOptions.Add(jitter); localOptions.Add(loss); localOptions.Add(delay);
            interpolation=new Toggle("Smooth rendering") { value=true }; localOptions.Add(interpolation);
            var local=Row();
            var start=new Button(()=>StartLocal()) { text="Local Test",name="OnlineLocalTest" }; start.style.flexGrow=1;
            local.Add(start); local.Add(new Button(()=>NetMatch.Active?.SwitchView()) { text="Switch peer" });
            localOptions.Add(local);
            var help=label("P1: configured controls. P2: arrows, J attack, K guard, R-Shift jump, R-Ctrl roll, Enter run.",12);
            help.style.whiteSpace=WhiteSpace.Normal; help.style.color=Dim; help.style.marginTop=10; localOptions.Add(help);
            var keys=label("F8 Online · F9 peer view · F10 Settings · F11 stats · Version "+RulesFingerprint.Value.ToString("X16").Substring(0,8),12);
            keys.style.color=Gold; keys.style.marginTop=8; window.Add(keys);
            foreach(var fieldLabel in window.Query<Label>(className:"unity-base-field__label").ToList())
            {
                fieldLabel.style.color=Dim; fieldLabel.style.minWidth=155;
            }
            foreach(var foldout in new[] {directOptions,localOptions}) {
                var toggle=foldout.Q<Toggle>();
                foreach(var text in toggle.Query<Label>().ToList()) text.style.color=Dim;
                var arrow=toggle.Q(className:"unity-toggle__checkmark");
                if(arrow!=null) arrow.style.unityBackgroundImageTintColor=Dim;
            }
            Update();
        }
        static VisualElement Row()
        {
            var row=new VisualElement(); row.style.flexDirection=FlexDirection.Row; row.style.alignItems=Align.Center;
            row.style.marginBottom=7; return row;
        }
        void Section(string text) { var h=label(text,13); h.style.color=Gold; h.style.letterSpacing=2; h.style.marginTop=14; h.style.marginBottom=8; window.Add(h); }
        public OnlineOptions Options => new OnlineOptions {
            Port=Mathf.Clamp(port.value,1,65535),Endpoint=endpoint.value,
            FirstStyle=(Style)Enum.Parse(typeof(Style),firstStyle.value),SecondStyle=(Style)Enum.Parse(typeof(Style),secondStyle.value),
            Latency=new[] {0,60,120,200}[latency.index],Jitter=Mathf.Clamp(jitter.value,0,500),
            Loss=Mathf.Clamp(loss.value,0,100),Delay=Mathf.Clamp(delay.value,0,8),
            Controls=controls.index==0?LocalControl.HumanVsBot:controls.index==1?LocalControl.Bots:LocalControl.TwoKeyboards,
            Interpolate=interpolation.value,WithAI=withAI.value,CoOp=coop.value
        };
        public void SetOpen(bool value)
        {
            if(open==value) return;
            open=value; layer.style.display=open?DisplayStyle.Flex:DisplayStyle.None;
            if(open) { SteamOnline.Ensure(); layer.BringToFront(); } else closedAt=Time.frameCount;
        }
        public void StartLocal()
        {
            OnlineConnection.Cancel(); SteamOnline.Leave();
            OnlineOptions options=Options;
            NetMatch.Active?.Stop();
            var match=new GameObject("NetMatch").AddComponent<NetMatch>();
            match.UseRollback=true; match.MovementDemo=false; match.CaptureSceneConfig=true;
            match.Controls=options.Controls; match.FirstStyle=options.FirstStyle; match.SecondStyle=options.SecondStyle;
            match.LatencyMs=options.Latency; match.JitterMs=options.Jitter; match.LossPercent=options.Loss;
            match.InputDelay=options.Delay; match.Interpolate=options.Interpolate;
            match.WithAI=options.WithAI; match.CoOp=options.CoOp;
            message="Starting local test"; SetOpen(false);
        }
        void Request(bool hosting)
        {
            try {
                OnlineOptions direct=Options; direct.Controls=LocalControl.HumanVsBot;
                if(hosting) HostRequested?.Invoke(direct); else JoinRequested?.Invoke(direct);
                SetOpen(true);
            } catch(Exception e) { message=e.Message; }
        }
        public void Leave() { OnlineConnection.Cancel(); NetMatch.Active?.Stop(); SteamOnline.Leave(); message="Offline"; }
        public void ShowMessage(string text,bool show=false) { message=text; if(show) SetOpen(true); }
        public void ShowMatchOptions(NetMatch match)
        {
            firstStyle.value=(match.LocalPlayer==0?match.FirstStyle:match.SecondStyle).ToString();
            secondStyle.value=(match.LocalPlayer==0?match.SecondStyle:match.FirstStyle).ToString();
            controls.index=match.Controls==LocalControl.Bots?1:match.Controls==LocalControl.TwoKeyboards?2:0;
            latency.index=Array.IndexOf(new[] {0,60,120,200},match.LatencyMs);
            jitter.value=match.JitterMs; loss.value=match.LossPercent; delay.value=match.InputDelay; interpolation.value=match.Interpolate;
        }
        public void BringToFront() { if(open) layer.BringToFront(); }
        public void Update()
        {
            if(Input.GetKeyDown(KeyCode.F8) && (shortcutAvailable==null || shortcutAvailable())) SetOpen(!open);
            if(open && Input.GetKeyDown(KeyCode.Escape)) SetOpen(false);
            BringToFront();
            host.SetEnabled(HostRequested!=null); join.SetEnabled(JoinRequested!=null);
            host.tooltip=HostRequested==null?"Direct UDP is pending validation":"Host a direct connection";
            join.tooltip=JoinRequested==null?"Direct UDP is pending validation":"Join a direct connection";
            UpdateSteam();
            var match=NetMatch.Active;
            status.text=match!=null && match.Ready ? (match.DirectPeer!=null?match.ConnectionName:"Local test")+" · "+(match.DirectPeer?.Status??match.Loopback?.A.Status??"Connected")+" · Tick "+match.State.Tick : message;
        }
        void UpdateSteam()
        {
            var client=SteamOnline.Client;
            steamHost.SetEnabled(client!=null && !client.Busy && client.LobbyId==0);
            refresh.SetEnabled(client==null || !client.Searching);
            steamStatus.text=SteamOnline.Status+(client==null?"":"\n"+client.SearchStatus);
            string signature=(client?.SearchRevision??-1)+":"+(client?.LobbyId??0)+":"+SteamOnline.Status;
            if(signature==steamSignature) return; steamSignature=signature; steamRows.Clear();
            if(client==null || client.Lobbies.Count==0) {
                var empty=label(client==null?"Steam is required for lobbies. Local test and Direct IP are available.":"No compatible lobbies yet.",13);
                empty.style.whiteSpace=WhiteSpace.Normal; empty.style.color=Dim; steamRows.Add(empty); return;
            }
            foreach(var lobby in client.Lobbies) {
                var row=Row(); row.name="SteamLobby-"+lobby.Id;
                var text=label(lobby.Host.Replace('\n',' ').Replace('\r',' ')+" · "+lobby.Style+"\n"+lobby.Status+" · "+lobby.Members+"/2",14);
                text.enableRichText=false; text.style.color=Dim; text.style.flexGrow=1; text.style.flexShrink=1; text.style.whiteSpace=WhiteSpace.Normal;
                row.Add(text);
                ulong id=lobby.Id;
                var enter=new Button(()=>SteamOnline.Join(id,Options)) { text=id==client.LobbyId?"Your lobby":"Join" };
                enter.SetEnabled(id!=client.LobbyId && client.LobbyId==0 && !client.Busy && lobby.CanJoin);
                row.Add(enter); steamRows.Add(row);
            }
        }
        public void Dispose()
        {
            layer.RemoveFromHierarchy(); launcher.RemoveFromHierarchy();
            if(Current==this) Current=null;
        }
    }
}
