using System;
using System.Net;
using System.Net.Sockets;

namespace AnimLab.Netcode.Transport
{
    public sealed class UdpTransport : DatagramSession
    {
        public int LocalPort => ((UdpChannel)Channel).LocalPort;
        public UdpTransport(int port,Func<byte[],byte[]> acceptClient) : base(new UdpChannel(port),acceptClient) { }
        public UdpTransport(string address,byte[] offer) : this(Parse(address),offer) { }
        UdpTransport(IPEndPoint endpoint,byte[] offer) : base(new UdpChannel(0),endpoint.ToString(),offer) { }
        public static IPEndPoint Parse(string address)
        {
            int colon=address==null?-1:address.LastIndexOf(':');
            if(colon<=0 || !IPAddress.TryParse(address.Substring(0,colon),out IPAddress ip) || ip.AddressFamily!=AddressFamily.InterNetwork ||
                !int.TryParse(address.Substring(colon+1),out int port) || port<1 || port>65535)
                throw new FormatException("Enter an IPv4 address and port, for example 192.168.1.10:7777");
            return new IPEndPoint(ip,port);
        }
    }
    sealed class UdpChannel : IDatagramChannel
    {
        readonly Socket socket;
        readonly byte[] buffer=new byte[1401];
        public int LocalPort => ((IPEndPoint)socket.LocalEndPoint).Port;
        public LinkState State { get; private set; }=LinkState.Connected;
        public string Error { get; private set; }="";
        public UdpChannel(int port)
        {
            if(port<0 || port>65535) throw new ArgumentOutOfRangeException(nameof(port));
            socket=new Socket(AddressFamily.InterNetwork,SocketType.Dgram,ProtocolType.Udp);
            try { socket.ReceiveBufferSize=262144; socket.Blocking=false; socket.Bind(new IPEndPoint(IPAddress.Any,port)); }
            catch { socket.Dispose(); throw; }
        }
        public void Update(long nowMs) { }
        public bool Send(byte[] packet,string remote)
        {
            try { socket.SendTo(packet,UdpTransport.Parse(remote)); return true; }
            catch(SocketException e) { Fault(e); return false; }
        }
        public bool TryReceive(out byte[] packet,out string sender)
        {
            packet=null; sender=null;
            if(State!=LinkState.Connected || !socket.Poll(0,SelectMode.SelectRead)) return false;
            EndPoint endpoint=new IPEndPoint(IPAddress.Any,0);
            try {
                int count=socket.ReceiveFrom(buffer,ref endpoint); packet=new byte[count];
                Buffer.BlockCopy(buffer,0,packet,0,count); sender=endpoint.ToString(); return true;
            } catch(SocketException e) { if(e.SocketErrorCode!=SocketError.ConnectionReset) Fault(e); return false; }
        }
        void Fault(SocketException e)
        {
            if(e.SocketErrorCode==SocketError.WouldBlock || e.SocketErrorCode==SocketError.NoBufferSpaceAvailable) return;
            State=LinkState.Failed; Error="UDP error: "+e.SocketErrorCode;
        }
        public void Dispose() { if(State==LinkState.Disconnected) return; socket.Dispose(); State=LinkState.Disconnected; }
    }
}
