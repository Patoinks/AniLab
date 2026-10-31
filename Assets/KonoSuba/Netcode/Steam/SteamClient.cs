using System;
using System.Collections.Generic;
using AnimLab.Netcode.Sim;
using Steamworks;

namespace AnimLab.Netcode.Steam
{
    /// <summary>Um cliente por Play; sem dependências do jogo ou inicialização automática offline.</summary>
    public sealed class SteamClient : IDisposable
    {
        public static bool Initialized { get; private set; }
        public static int Generation { get; private set; }
        public static SteamClient Active { get; private set; }
        readonly CallResult<LobbyCreated_t> created;
        readonly CallResult<LobbyEnter_t> entered;
        readonly CallResult<LobbyMatchList_t> found;
        readonly Callback<LobbyChatUpdate_t> membership;
        readonly Callback<LobbyDataUpdate_t> dataChanged;
        readonly Callback<SteamServersDisconnected_t> disconnected;
        readonly List<SteamLobbyInfo> lobbies=new List<SteamLobbyInfo>();
        bool creating,joining,cancelOperation,disposed;
        ulong joiningId,expectedOwner;
        long lastSearch=-10000;
        Style hostStyle;
        public ulong LobbyId { get; private set; }
        public bool Hosting { get; private set; }
        public bool Busy => creating || joining;
        public bool Searching { get; private set; }
        public int SearchRevision { get; private set; }
        public int LastRawResultCount { get; private set; }
        public bool LastSearchContainsOwn { get; private set; }
        public string Status { get; private set; }="Steam ready";
        public string SearchStatus { get; private set; }="";
        public string Name { get; }
        public string RelayStatus => disposed?"Stopped":SteamNetworkingUtils.GetRelayNetworkStatus(out _).ToString();
        public IReadOnlyList<SteamLobbyInfo> Lobbies => lobbies;
        public event Action<bool,ulong> LobbyReady;
        public event Action<string> PeerLeft;
        SteamClient()
        {
            Name=SteamFriends.GetPersonaName();
            created=CallResult<LobbyCreated_t>.Create(OnCreated); entered=CallResult<LobbyEnter_t>.Create(OnEntered);
            found=CallResult<LobbyMatchList_t>.Create(OnFound); membership=Callback<LobbyChatUpdate_t>.Create(OnMembership);
            dataChanged=Callback<LobbyDataUpdate_t>.Create(OnData);
            disconnected=Callback<SteamServersDisconnected_t>.Create(_=>Lost("Steam disconnected"));
        }
        public static bool TryStart(out SteamClient client,out string error)
        {
            client=Active; error=""; if(client!=null && !client.disposed) return true;
            bool initialized=false;
            try {
                if(!SteamAPI.IsSteamRunning()) { error="Steam is not running"; return false; }
                if(SteamAPI.InitEx(out _)!=ESteamAPIInitResult.k_ESteamAPIInitResult_OK) { error="Steam initialization failed"; return false; }
                initialized=Initialized=true; Generation++;
                if(SteamUtils.GetAppID()!=new AppId_t(480) || !SteamUser.BLoggedOn()) { error="Steam account is not online (App 480)"; SteamAPI.Shutdown(); Initialized=false; return false; }
                SteamNetworkingUtils.InitRelayNetworkAccess();
                Active=client=new SteamClient(); return true;
            } catch(DllNotFoundException) { error="Steam is not running"; }
            catch(EntryPointNotFoundException) { error="Steam library is incompatible"; }
            catch(BadImageFormatException) { error="Steam library is incompatible"; }
            catch(InvalidOperationException) { error="Steam initialization failed"; }
            if(initialized) SteamAPI.Shutdown(); Initialized=false; return false;
        }
        public void Update(long nowMs,bool browse)
        {
            if(disposed) return;
            SteamAPI.RunCallbacks();
            if(browse && !Searching && nowMs-lastSearch>=4000) Refresh(nowMs);
        }
        public void Refresh(long nowMs)
        {
            if(disposed || Searching) return;
            SteamMatchmaking.AddRequestLobbyListStringFilter(SteamLobbyInfo.GameKey,SteamLobbyInfo.Game,ELobbyComparison.k_ELobbyComparisonEqual);
            SteamMatchmaking.AddRequestLobbyListStringFilter(SteamLobbyInfo.ProtocolKey,SteamLobbyInfo.Protocol,ELobbyComparison.k_ELobbyComparisonEqual);
            SteamMatchmaking.AddRequestLobbyListStringFilter(SteamLobbyInfo.BuildKey,SteamLobbyInfo.Build,ELobbyComparison.k_ELobbyComparisonEqual);
            SteamMatchmaking.AddRequestLobbyListDistanceFilter(ELobbyDistanceFilter.k_ELobbyDistanceFilterWorldwide);
            SteamMatchmaking.AddRequestLobbyListResultCountFilter(50);
            Searching=true; lastSearch=nowMs; SearchStatus="Refreshing...";
            found.Set(SteamMatchmaking.RequestLobbyList());
        }
        public void Host(Style style)
        {
            if(disposed || Busy || LobbyId!=0) return;
            if(style==Style.Iaido || !Enum.IsDefined(typeof(Style),style)) throw new ArgumentException("Style unavailable");
            hostStyle=style; creating=true; cancelOperation=false; Status="Creating Steam lobby...";
            created.Set(SteamMatchmaking.CreateLobby(ELobbyType.k_ELobbyTypePublic,2));
        }
        public void Join(ulong id)
        {
            if(disposed || Busy || LobbyId!=0) return;
            SteamLobbyInfo lobby=Read(id);
            if(lobby==null || !lobby.CanJoin || lobby.Owner==SteamUser.GetSteamID().m_SteamID) { Status="Lobby is no longer available"; Refresh(lastSearch+4000); return; }
            joining=true; cancelOperation=false; joiningId=id; expectedOwner=lobby.Owner; Status="Joining Steam lobby...";
            entered.Set(SteamMatchmaking.JoinLobby(new CSteamID(id)));
        }
        void OnCreated(LobbyCreated_t value,bool failure)
        {
            creating=false;
            if(failure || value.m_eResult!=EResult.k_EResultOK) { Status="Could not create Steam lobby"; return; }
            if(cancelOperation) { SteamMatchmaking.LeaveLobby(new CSteamID(value.m_ulSteamIDLobby)); return; }
            LobbyId=value.m_ulSteamIDLobby; Hosting=true; expectedOwner=SteamUser.GetSteamID().m_SteamID;
            var id=new CSteamID(LobbyId);
            bool ok=SteamMatchmaking.SetLobbyData(id,SteamLobbyInfo.GameKey,SteamLobbyInfo.Game);
            ok&=SteamMatchmaking.SetLobbyData(id,SteamLobbyInfo.ProtocolKey,SteamLobbyInfo.Protocol);
            ok&=SteamMatchmaking.SetLobbyData(id,SteamLobbyInfo.BuildKey,SteamLobbyInfo.Build);
            ok&=SteamMatchmaking.SetLobbyData(id,"host",Name.Length>128?Name.Substring(0,128):Name);
            ok&=SteamMatchmaking.SetLobbyData(id,"host_id",expectedOwner.ToString(System.Globalization.CultureInfo.InvariantCulture));
            ok&=SteamMatchmaking.SetLobbyData(id,"style",hostStyle.ToString());
            ok&=SteamMatchmaking.SetLobbyData(id,"state","Waiting");
            ok&=SteamMatchmaking.SetLobbyJoinable(id,true);
            if(!ok) { Leave(); Status="Could not publish Steam lobby"; return; }
            Status="Hosting · waiting for peer"; LobbyReady?.Invoke(true,expectedOwner); Refresh(lastSearch+4000);
        }
        void OnEntered(LobbyEnter_t value,bool failure)
        {
            joining=false; ulong id=value.m_ulSteamIDLobby;
            if(failure || value.m_EChatRoomEnterResponse!=(uint)EChatRoomEnterResponse.k_EChatRoomEnterResponseSuccess) { Status="Could not join Steam lobby"; return; }
            var lobby=Read(id);
            if(cancelOperation || id!=joiningId || lobby==null || lobby.Owner!=expectedOwner || lobby.Members!=2) {
                SteamMatchmaking.LeaveLobby(new CSteamID(id)); Status="Lobby is no longer compatible or available"; return;
            }
            LobbyId=id; Hosting=false; Status="Connecting through Steam relay..."; LobbyReady?.Invoke(false,expectedOwner);
        }
        SteamLobbyInfo Read(ulong value)
        {
            var id=new CSteamID(value);
            SteamLobbyInfo.TryRead(value,SteamMatchmaking.GetLobbyOwner(id).m_SteamID,SteamMatchmaking.GetNumLobbyMembers(id),
                SteamMatchmaking.GetLobbyMemberLimit(id),key=>SteamMatchmaking.GetLobbyData(id,key),out var lobby);
            return lobby;
        }
        void OnFound(LobbyMatchList_t value,bool failure)
        {
            Searching=false; SearchRevision++; lobbies.Clear(); LastSearchContainsOwn=false; LastRawResultCount=(int)value.m_nLobbiesMatching;
            if(failure) { SearchStatus="Could not refresh Steam lobbies"; return; }
            for(int i=0;i<value.m_nLobbiesMatching;i++) {
                var id=SteamMatchmaking.GetLobbyByIndex(i); var lobby=Read(id.m_SteamID);
                if(lobby!=null) { lobbies.Add(lobby); if(lobby.Id==LobbyId) LastSearchContainsOwn=true; }
            }
            // A Steam omite lobbies cheios; o nosso continua visível enquanto jogamos.
            if(LobbyId!=0 && !lobbies.Exists(l=>l.Id==LobbyId)) { var own=Read(LobbyId); if(own!=null) lobbies.Insert(0,own); }
            SearchStatus=lobbies.Count==0?"No compatible lobbies. Ask a friend to Host.":"Updates every 4 seconds";
        }
        public bool IsLobbyPeer(ulong peer)
        {
            if(disposed || LobbyId==0 || peer==0 || peer==SteamUser.GetSteamID().m_SteamID) return false;
            var id=new CSteamID(LobbyId);
            if(SteamMatchmaking.GetLobbyOwner(id).m_SteamID!=expectedOwner) return false;
            for(int i=0;i<SteamMatchmaking.GetNumLobbyMembers(id);i++) if(SteamMatchmaking.GetLobbyMemberByIndex(id,i).m_SteamID==peer) return true;
            return false;
        }
        void OnMembership(LobbyChatUpdate_t value)
        {
            if(value.m_ulSteamIDLobby!=LobbyId || LobbyId==0) return;
            var flags=(EChatMemberStateChange)value.m_rgfChatMemberStateChange;
            if((flags & ~EChatMemberStateChange.k_EChatMemberStateChangeEntered)!=0) { Lost("Peer left the Steam lobby"); return; }
            if(Hosting && SteamMatchmaking.GetNumLobbyMembers(new CSteamID(LobbyId))==2) SetState("Connecting");
        }
        void OnData(LobbyDataUpdate_t value)
        {
            if(value.m_ulSteamIDLobby==LobbyId && LobbyId!=0 && (Read(LobbyId)==null || SteamMatchmaking.GetLobbyOwner(new CSteamID(LobbyId)).m_SteamID!=expectedOwner)) Lost("Steam lobby changed or closed");
        }
        public void SetState(string state)
        {
            if(disposed || LobbyId==0) return;
            if(Hosting) { SteamMatchmaking.SetLobbyData(new CSteamID(LobbyId),"state",state); SteamMatchmaking.SetLobbyJoinable(new CSteamID(LobbyId),state=="Waiting"); }
            Status=state=="Playing"?"Steam relay · connected":"Steam relay · "+state;
        }
        void Lost(string reason) { Leave(); Status=reason; PeerLeft?.Invoke(reason); }
        public void Leave()
        {
            if(disposed) return; cancelOperation=true;
            ulong old=LobbyId; LobbyId=0; Hosting=false; LastSearchContainsOwn=false;
            if(old!=0) SteamMatchmaking.LeaveLobby(new CSteamID(old));
            lobbies.RemoveAll(l=>l.Id==old); Status="Steam ready";
        }
        public void Dispose()
        {
            if(disposed) return; Leave(); disposed=true;
            created.Dispose(); entered.Dispose(); found.Dispose(); membership.Dispose(); dataChanged.Dispose(); disconnected.Dispose();
            LobbyReady=null; PeerLeft=null; lobbies.Clear(); SteamAPI.Shutdown(); Initialized=false;
            if(ReferenceEquals(Active,this)) Active=null;
        }
    }
}
