using System;
using System.Runtime.InteropServices;
using AnimLab.Netcode.Transport;
using Steamworks;

namespace AnimLab.Netcode.Steam
{
    /// <summary>SDR sem ICE; inputs não fiáveis, sem Nagle nem fila de espera.</summary>
    public sealed class SteamTransport : DatagramSession
    {
        public const int VirtualPort=28641;
        public const int SendFlags=Constants.k_nSteamNetworkingSend_UnreliableNoDelay;
        SteamTransport(SteamSocketChannel channel,Func<byte[],byte[]> accept) : base(channel,accept,true) { }
        SteamTransport(SteamSocketChannel channel,string peer,byte[] offer) : base(channel,peer,offer) { }
        public static SteamTransport Host(Func<ulong,bool> allowed,Func<byte[],byte[]> accept) => new SteamTransport(new SteamSocketChannel(allowed),accept);
        public static SteamTransport Join(ulong owner,byte[] offer) => new SteamTransport(new SteamSocketChannel(owner),owner.ToString(),offer);
        public static void CreateLoopback(Func<byte[],byte[]> accept,byte[] offer,out SteamTransport host,out SteamTransport join)
        {
            var a=new SteamNetworkingIdentity(); var b=new SteamNetworkingIdentity();
            if(!SteamNetworkingSockets.CreateSocketPair(out var first,out var second,true,ref a,ref b)) throw new InvalidOperationException("Steam loopback failed");
            host=new SteamTransport(new SteamSocketChannel(first),accept);
            join=new SteamTransport(new SteamSocketChannel(second),"loopback",offer);
        }
    }
    sealed class SteamSocketChannel : IDatagramChannel
    {
        HSteamNetConnection connection;
        HSteamListenSocket listen;
        readonly int generation=SteamClient.Generation;
        readonly Func<ulong,bool> allowed;
        readonly Callback<SteamNetConnectionStatusChangedCallback_t> changed;
        readonly IntPtr[] message=new IntPtr[1];
        string peer="loopback";
        bool disposed;
        bool Available => SteamClient.Initialized && generation==SteamClient.Generation;
        public LinkState State { get; private set; }=LinkState.Connecting;
        public string Error { get; private set; }="";
        static SteamNetworkingConfigValue_t[] Options() => new[] {
            new SteamNetworkingConfigValue_t { m_eValue=ESteamNetworkingConfigValue.k_ESteamNetworkingConfig_P2P_Transport_ICE_Enable,
                m_eDataType=ESteamNetworkingConfigDataType.k_ESteamNetworkingConfig_Int32,
                m_val=new SteamNetworkingConfigValue_t.OptionValue { m_int32=0 } }
        };
        public SteamSocketChannel(Func<ulong,bool> allowed)
        {
            this.allowed=allowed??throw new ArgumentNullException(nameof(allowed));
            var options=Options(); listen=SteamNetworkingSockets.CreateListenSocketP2P(SteamTransport.VirtualPort,options.Length,options);
            if(listen==HSteamListenSocket.Invalid) throw new InvalidOperationException("Steam listen socket failed");
            changed=Callback<SteamNetConnectionStatusChangedCallback_t>.Create(OnChanged);
        }
        public SteamSocketChannel(ulong owner)
        {
            peer=owner.ToString(); var identity=new SteamNetworkingIdentity(); identity.SetSteamID64(owner); var options=Options();
            connection=SteamNetworkingSockets.ConnectP2P(ref identity,SteamTransport.VirtualPort,options.Length,options);
            if(connection==HSteamNetConnection.Invalid) throw new InvalidOperationException("Steam connection failed");
        }
        public SteamSocketChannel(HSteamNetConnection handle) { connection=handle; State=LinkState.Connected; }
        void OnChanged(SteamNetConnectionStatusChangedCallback_t value)
        {
            if(disposed || value.m_info.m_hListenSocket!=listen || value.m_info.m_eState!=ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connecting) return;
            ulong id=value.m_info.m_identityRemote.GetSteamID64();
            if(connection!=HSteamNetConnection.Invalid || !allowed(id)) { SteamNetworkingSockets.CloseConnection(value.m_hConn,0,"Lobby members only",false); return; }
            if(SteamNetworkingSockets.AcceptConnection(value.m_hConn)!=EResult.k_EResultOK) {
                SteamNetworkingSockets.CloseConnection(value.m_hConn,0,"Accept failed",false); return;
            }
            connection=value.m_hConn; peer=id.ToString();
        }
        public void Update(long nowMs)
        {
            if(disposed) return;
            if(!Available) { State=LinkState.Disconnected; Error="Steam disconnected"; return; }
            if(connection==HSteamNetConnection.Invalid) return;
            if(!SteamNetworkingSockets.GetConnectionInfo(connection,out var info)) { State=LinkState.Failed; Error="Steam connection unavailable"; return; }
            switch(info.m_eState) {
                case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connected: State=LinkState.Connected; break;
                case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ClosedByPeer: State=LinkState.Disconnected; Error="Peer left"; break;
                case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ProblemDetectedLocally: State=LinkState.Failed; Error="Steam connection lost"; break;
            }
        }
        public bool Send(byte[] packet,string remote)
        {
            if(disposed || !Available || State!=LinkState.Connected || remote!=peer) return false;
            var pinned=GCHandle.Alloc(packet,GCHandleType.Pinned);
            try {
                EResult result=SteamNetworkingSockets.SendMessageToConnection(connection,pinned.AddrOfPinnedObject(),(uint)packet.Length,SteamTransport.SendFlags,out _);
                if(result==EResult.k_EResultOK) return true;
                if(result!=EResult.k_EResultIgnored && result!=EResult.k_EResultLimitExceeded) { State=LinkState.Failed; Error="Steam send failed: "+result; }
                return false;
            } finally { pinned.Free(); }
        }
        public bool TryReceive(out byte[] packet,out string sender)
        {
            packet=null; sender=peer;
            if(disposed || !Available || State!=LinkState.Connected || SteamNetworkingSockets.ReceiveMessagesOnConnection(connection,message,1)<=0) return false;
            try {
                var value=SteamNetworkingMessage_t.FromIntPtr(message[0]);
                packet=new byte[value.m_cbSize<=1400 && value.m_cbSize>=0?value.m_cbSize:0];
                if(packet.Length>0) Marshal.Copy(value.m_pData,packet,0,packet.Length);
                return true;
            } finally { SteamNetworkingMessage_t.Release(message[0]); message[0]=IntPtr.Zero; }
        }
        public void Dispose()
        {
            if(disposed) return; disposed=true; changed?.Dispose();
            if(Available) {
                if(connection!=HSteamNetConnection.Invalid) SteamNetworkingSockets.CloseConnection(connection,0,"Left match",false);
                if(listen!=HSteamListenSocket.Invalid) SteamNetworkingSockets.CloseListenSocket(listen);
            }
            connection=HSteamNetConnection.Invalid; listen=HSteamListenSocket.Invalid; State=LinkState.Disconnected;
        }
    }
}
