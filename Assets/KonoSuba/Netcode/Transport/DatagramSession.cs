using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;

namespace AnimLab.Netcode.Transport
{
    /// <summary>Handshake idempotente, tokens, keep-alive e saída comuns a UDP e Steam.</summary>
    public class DatagramSession : ISessionTransport
    {
        const uint Magic=0x554E4C41;
        const int MaximumDatagram=1400,TimeoutMs=10000;
        protected readonly IDatagramChannel Channel;
        readonly Queue<byte[]> inbox=new Queue<byte[]>();
        readonly Func<byte[],byte[]> accept;
        readonly byte[] offer;
        string remote;
        readonly bool waitForPeer;
        ulong clientToken;
        long started=-1,lastReceive,lastSend=-10000,lastHandshake=-10000;
        long clock;
        bool receivedData,disposed;
        public LinkState State { get; private set; }=LinkState.Connecting;
        public string Error { get; private set; }="";
        public long Sent { get; private set; }
        public long Received { get; private set; }
        public int Rejected { get; private set; }
        public bool IsHost { get; }
        public ulong SessionId { get; private set; }
        public byte[] MatchPayload { get; private set; }
        public string RemoteAddress => remote??"";
        public DatagramSession(IDatagramChannel channel,Func<byte[],byte[]> acceptClient,bool waitForPeer=false)
        {
            IsHost=true; accept=acceptClient??throw new ArgumentNullException(nameof(acceptClient));
            SessionId=Token(); Channel=channel??throw new ArgumentNullException(nameof(channel)); this.waitForPeer=waitForPeer;
        }
        public DatagramSession(IDatagramChannel channel,string remote,byte[] offer)
        {
            this.remote=remote??throw new ArgumentNullException(nameof(remote)); this.offer=(byte[])(offer??throw new ArgumentNullException(nameof(offer))).Clone();
            if(offer.Length>1300) throw new ArgumentOutOfRangeException(nameof(offer));
            clientToken=Token(); Channel=channel??throw new ArgumentNullException(nameof(channel));
        }
        static ulong Token()
        {
            var bytes=new byte[8]; using(var random=RandomNumberGenerator.Create()) random.GetBytes(bytes);
            ulong value=BitConverter.ToUInt64(bytes,0); return value==0?1:value;
        }
        void Packet(byte kind,byte[] payload=null)
        {
            if(remote==null || disposed) return;
            using(var stream=new MemoryStream()) using(var writer=new BinaryWriter(stream)) {
                writer.Write(Magic); writer.Write((byte)1); writer.Write(kind);
                writer.Write(kind==1?0UL:SessionId); writer.Write(clientToken);
                if(payload!=null) { writer.Write((ushort)payload.Length); writer.Write(payload); }
                byte[] bytes=stream.ToArray();
                if(bytes.Length>MaximumDatagram) throw new InvalidOperationException("Datagram too large");
                if(Channel.Send(bytes,remote)) { Sent++; lastSend=clock; }
            }
        }
        public void Update(long nowMs)
        {
            clock=nowMs;
            if(disposed || State==LinkState.Failed || State==LinkState.TimedOut || State==LinkState.Disconnected) return;
            Channel.Update(nowMs);
            if(Channel.State!=LinkState.Connecting && Channel.State!=LinkState.Connected) { State=Channel.State; Error=Channel.Error; return; }
            bool waiting=waitForPeer && remote==null && Channel.State==LinkState.Connecting;
            if(started<0 || waiting) { started=lastReceive=nowMs; }
            int budget=256;
            while(budget-->0 && Channel.TryReceive(out byte[] receive,out string sender)) {
                try {
                    int count=receive.Length;
                    if(count>MaximumDatagram || count<22) { Rejected++; continue; }
                    Receive(sender,receive,nowMs);
                } catch(InvalidDataException) { Rejected++; } catch(EndOfStreamException) { Rejected++; }
            }
            if(State!=LinkState.Connecting && State!=LinkState.Connected) return;
            if(!waiting && nowMs-lastReceive>TimeoutMs) { State=LinkState.TimedOut; Error="Connection timed out"; return; }
            if(nowMs-lastHandshake>=250 && (State==LinkState.Connecting || IsHost && !receivedData)) {
                if(IsHost) { if(remote!=null && MatchPayload!=null) Packet(State==LinkState.Connected?(byte)4:(byte)2,State==LinkState.Connected?null:MatchPayload); }
                else Packet(MatchPayload==null?(byte)1:(byte)3,MatchPayload==null?offer:null);
                lastHandshake=lastSend=nowMs;
            }
            if(State==LinkState.Connected && nowMs-lastSend>=1000) { Packet(6); lastSend=nowMs; }
        }
        void Receive(string sender,byte[] receive,long nowMs)
        {
            using(var stream=new MemoryStream(receive,false)) using(var reader=new BinaryReader(stream)) {
                if(reader.ReadUInt32()!=Magic || reader.ReadByte()!=1) { Rejected++; return; }
                byte kind=reader.ReadByte(); ulong session=reader.ReadUInt64(),client=reader.ReadUInt64();
                if(remote!=null && !remote.Equals(sender)) { Rejected++; return; }
                if(IsHost && kind==1)
                {
                    if(session!=0 || client==0) throw new InvalidDataException();
                    byte[] payload=ReadPayload(reader);
                    if(remote==null) {
                        byte[] accepted;
                        try { accepted=accept(payload); } catch(ArgumentException) { Rejected++; return; } catch(InvalidDataException) { Rejected++; return; }
                        if(accepted==null || accepted.Length>1300) throw new InvalidDataException();
                        remote=sender; clientToken=client; MatchPayload=accepted;
                    } else if(client!=clientToken) { Rejected++; return; }
                    Packet(2,MatchPayload);
                }
                else if(!IsHost && kind==2)
                {
                    if(client!=clientToken || session==0 || SessionId!=0 && session!=SessionId) throw new InvalidDataException();
                    byte[] payload=ReadPayload(reader);
                    if(MatchPayload==null) { SessionId=session; MatchPayload=payload; }
                    else if(!Equal(payload,MatchPayload)) throw new InvalidDataException();
                    Packet(3);
                }
                else
                {
                    if(remote==null || SessionId==0 || session!=SessionId || client!=clientToken) { Rejected++; return; }
                    if(kind==3 && IsHost) { RequireEnd(reader); State=LinkState.Connected; Packet(4); }
                    else if(kind==4 && !IsHost && MatchPayload!=null) { RequireEnd(reader); State=LinkState.Connected; Packet(6); }
                    else if(kind==5 && State==LinkState.Connected) { byte[] data=ReadPayload(reader); if(inbox.Count<256) inbox.Enqueue(data); receivedData=true; }
                    else if(kind==6 && State==LinkState.Connected) { RequireEnd(reader); receivedData=true; }
                    else if(kind==7) { RequireEnd(reader); State=LinkState.Disconnected; Error="Peer left"; }
                    else { Rejected++; return; }
                }
                Received++; lastReceive=nowMs;
            }
        }
        static void RequireEnd(BinaryReader reader) { if(reader.BaseStream.Position!=reader.BaseStream.Length) throw new InvalidDataException(); }
        static byte[] ReadPayload(BinaryReader reader)
        {
            int length=reader.ReadUInt16();
            if(length>1300 || length!=reader.BaseStream.Length-reader.BaseStream.Position) throw new InvalidDataException();
            return reader.ReadBytes(length);
        }
        static bool Equal(byte[] a,byte[] b) { if(a.Length!=b.Length) return false; for(int i=0;i<a.Length;i++) if(a[i]!=b[i]) return false; return true; }
        void Fail(string error) { Error=error; State=LinkState.Failed; }
        public void Send(byte[] packet)
        {
            if(State!=LinkState.Connected || disposed) return;
            if(packet==null || packet.Length>1300) throw new ArgumentOutOfRangeException(nameof(packet));
            Packet(5,packet);
        }
        public bool TryReceive(out byte[] packet) { if(inbox.Count==0) { packet=null; return false; } packet=inbox.Dequeue(); return true; }
        public void Dispose()
        {
            if(disposed) return;
            if(State==LinkState.Connected) Packet(7);
            disposed=true; Channel.Dispose(); inbox.Clear(); State=LinkState.Disconnected;
        }
    }
}

