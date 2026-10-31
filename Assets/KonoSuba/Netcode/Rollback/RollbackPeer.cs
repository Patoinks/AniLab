using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using AnimLab.Netcode.Sim;
using AnimLab.Netcode.Transport;

namespace AnimLab.Netcode.Rollback
{
    public sealed class DesyncReport
    {
        public int Tick;
        public ulong LocalHash,RemoteHash;
        public byte[] LocalState,RemoteState;
        public byte[] BaseState;
        public string Inputs,Reason;
    }

    /// <summary>Datagramas redundantes; acks e checksums nunca usam estado previsto.</summary>
    public sealed class RollbackPeer : IDisposable
    {
        const uint Magic=0x314E5241;
        const int Redundancy=24,MaximumPacket=1400;
        readonly ITransport transport;
        readonly ulong matchId,initialHash;
        readonly Checkpoint[] remoteChecks=new Checkpoint[128];
        readonly MemoryStream output=new MemoryStream(512);
        readonly BinaryWriter writer;
        int sequence,highestSequence=-1,nextVerify=30,remoteVerified;
        int finalizedLoss;
        readonly HashSet<int> missingPackets=new HashSet<int>();
        readonly Queue<int> missingOrder=new Queue<int>();
        long lastRemoteSent=-1,lastReceive=-1;
        bool disposed;
        public RollbackSession Session { get; }
        public DesyncReport Desync { get; private set; }
        public int VerifiedTick { get; private set; }
        public int ChecksumsVerified { get; private set; }
        public int BadPackets { get; private set; }
        public int PacketGaps => finalizedLoss+missingPackets.Count;
        public int PingMs { get; private set; }
        public long PacketsReceived { get; private set; }
        public long PacketsSent { get; private set; }
        public event Action<DesyncReport> Desynced;
        public LinkState Link => transport.State;
        public string Status => Desync!=null?"DESYNC":Session.Faulted?Session.Error:transport.State.ToString();

        public RollbackPeer(RollbackSession session,ITransport transport,ulong matchId=7)
        {
            Session=session; this.transport=transport; this.matchId=matchId; initialHash=StateCodec.Checksum(session.State);
            writer=new BinaryWriter(output);
        }
        void Begin(byte kind)
        {
            output.SetLength(0); output.Position=0; writer.Write(Magic); writer.Write((byte)1); writer.Write(kind); writer.Write(matchId);
        }
        void Send() { writer.Flush(); transport.Send(output.ToArray()); PacketsSent++; }
        public void SendInputs(long nowMs)
        {
            if(disposed || transport.State!=LinkState.Connected) return;
            Begin(1);
            writer.Write(sequence++); writer.Write(nowMs); writer.Write(lastRemoteSent); writer.Write(initialHash);
            writer.Write(Session.State.Tick); writer.Write(Session.RemoteConfirmedInput); writer.Write(VerifiedTick);
            int last=Session.LocalLatest,first=Math.Max(0,last-Redundancy+1);
            writer.Write(first); writer.Write((byte)(last-first+1));
            for(int t=first;t<=last;t++) {
                if(!Session.TryLocalInput(t,out PlayerInput input)) throw new InvalidOperationException("Input redundancy expired");
                input.Write(writer);
            }
            int count=0;
            for(int t=remoteVerified+30;t<=Session.ConfirmedTick && count<4;t+=30) if(Session.TryCheckpoint(t,out _)) count++; else break;
            writer.Write((byte)count);
            for(int i=0;i<count;i++) {
                int t=remoteVerified+30*(i+1); Session.TryCheckpoint(t,out Checkpoint c); writer.Write(t); writer.Write(c.Hash);
            }
            Send();
        }
        public void Pump(long nowMs)
        {
            if(disposed) return;
            transport.Update(nowMs);
            int budget=256;
            while(budget-->0 && transport.TryReceive(out byte[] packet)) {
                if(packet==null || packet.Length>MaximumPacket || packet.Length<14) { BadPackets++; continue; }
                try {
                    using(var stream=new MemoryStream(packet,false)) using(var r=new BinaryReader(stream)) {
                        if(r.ReadUInt32()!=Magic || r.ReadByte()!=1) { BadPackets++; continue; }
                        byte kind=r.ReadByte(); if(r.ReadUInt64()!=matchId) { BadPackets++; continue; }
                        if(kind==1) ReadInputs(r,nowMs);
                        else if(kind==2) ReplyState(r.ReadInt32());
                        else if(kind==3) ReadState(r);
                        else BadPackets++;
                        if(stream.Position!=stream.Length) BadPackets++;
                    }
                    PacketsReceived++; lastReceive=nowMs;
                } catch(EndOfStreamException) { BadPackets++; } catch(InvalidDataException) { BadPackets++; } catch(OverflowException) { BadPackets++; }
            }
            Session.Reconcile(); Verify();
            if(lastReceive>=0 && nowMs-lastReceive>10000 && !Session.Faulted) Session.Fail("Peer timed out");
            if(Desync!=null && Desync.RemoteState==null) RequestState(Desync.Tick);
        }
        void ReadInputs(BinaryReader r,long nowMs)
        {
            int seq=r.ReadInt32(); long sent=r.ReadInt64(),echo=r.ReadInt64(); ulong initial=r.ReadUInt64();
            int tick=r.ReadInt32(),ack=r.ReadInt32(),verified=r.ReadInt32(),first=r.ReadInt32(); int count=r.ReadByte();
            if(initial!=initialHash) { Session.Fail("Initial state differs"); throw new InvalidDataException("Initial state differs"); }
            if(seq<0 || tick<0 || tick>Session.State.Tick+128 || ack>Session.LocalLatest || verified<0 || verified%30!=0 ||
                verified>Session.ConfirmedTick || first<0 || count<0 || count>Redundancy || first+(long)count>Session.State.Tick+65L)
                throw new InvalidDataException("Packet bounds");
            // Valida o datagrama inteiro antes de entregar qualquer input à sessão.
            var inputs=new PlayerInput[count];
            for(int i=0;i<count;i++) inputs[i]=PlayerInput.Read(r);
            int checks=r.ReadByte();
            if(checks>4) throw new InvalidDataException("Checksum count");
            var frames=new int[checks]; var hashes=new ulong[checks];
            for(int i=0;i<checks;i++) {
                frames[i]=r.ReadInt32(); hashes[i]=r.ReadUInt64();
                if(frames[i]<=0 || frames[i]%30!=0 || frames[i]>Session.State.Tick+128) throw new InvalidDataException("Checksum frame");
            }
            if(r.BaseStream.Position!=r.BaseStream.Length) throw new InvalidDataException("Trailing data");
            if(seq>highestSequence) {
                if((long)seq-highestSequence>4096) throw new InvalidDataException("Sequence gap");
                for(int n=highestSequence+1;n<seq;n++) { missingPackets.Add(n); missingOrder.Enqueue(n); }
                highestSequence=seq;
                while(missingOrder.Count>0 && missingOrder.Peek()<highestSequence-1024)
                    if(missingPackets.Remove(missingOrder.Dequeue())) finalizedLoss++;
            } else missingPackets.Remove(seq);
            lastRemoteSent=sent;
            if(echo>=0 && echo<=nowMs) PingMs=(int)Math.Min(60000,nowMs-echo);
            Session.RemoteTick=Math.Max(Session.RemoteTick,tick+PingMs*60/2000);
            remoteVerified=Math.Max(remoteVerified,verified);
            for(int i=0;i<count;i++) Session.ReceiveRemote(first+i,inputs[i]);
            for(int i=0;i<checks;i++) {
                int slot=(frames[i]/30)&127;
                var previous=remoteChecks[slot];
                if(previous!=null && previous.Tick==frames[i] && previous.Hash!=hashes[i]) {
                    Session.Faulted=true; throw new InvalidDataException("Checksum changed");
                }
                remoteChecks[slot]=new Checkpoint { Tick=frames[i],Hash=hashes[i] };
            }
        }
        void Verify()
        {
            if(Desync!=null) return;
            while(nextVerify<=Session.ConfirmedTick)
            {
                Checkpoint remote=remoteChecks[(nextVerify/30)&127];
                if(remote==null || remote.Tick!=nextVerify || !Session.TryCheckpoint(nextVerify,out Checkpoint local)) break;
                if(local.Hash!=remote.Hash)
                {
                    Desync=new DesyncReport { Tick=nextVerify,LocalHash=local.Hash,RemoteHash=remote.Hash,LocalState=local.State,
                        Inputs=Session.InputHistory(),Reason="Confirmed state checksum differs" };
                    if(Session.TryState(Math.Max(0,Session.State.Tick-RollbackSession.Capacity+1),out MatchState first))
                        Desync.BaseState=StateCodec.Encode(first);
                    Session.Faulted=true; Desynced?.Invoke(Desync); RequestState(nextVerify); break;
                }
                VerifiedTick=nextVerify; ChecksumsVerified++; nextVerify+=30;
            }
        }
        void RequestState(int tick) { Begin(2); writer.Write(tick); Send(); }
        void ReplyState(int tick)
        {
            if(tick<=0 || tick%30!=0 || !Session.TryCheckpoint(tick,out Checkpoint c)) return;
            byte[] packed=Packing.Deflate(c.State);
            if(packed.Length>MaximumPacket-20) return; // estado grande demais para um datagrama: o dump fica só com o local
            Begin(3); writer.Write(tick); writer.Write((ushort)packed.Length); writer.Write(packed); Send();
        }
        void ReadState(BinaryReader r)
        {
            int tick=r.ReadInt32(),length=r.ReadUInt16();
            if(length>MaximumPacket-20 || length!=r.BaseStream.Length-r.BaseStream.Position) throw new InvalidDataException("State size");
            byte[] bytes=Packing.Inflate(r.ReadBytes(length));
            if(Desync==null || Desync.Tick!=tick) return;
            MatchState state=StateCodec.Decode(bytes);
            if(state.Tick!=tick || StateCodec.Checksum(state)!=Desync.RemoteHash) throw new InvalidDataException("State checksum");
            Desync.RemoteState=bytes; Desynced?.Invoke(Desync);
        }
        public void WriteDesync(string directory)
        {
            if(Desync==null) return;
            Directory.CreateDirectory(directory);
            string prefix=Path.Combine(directory,"desync_"+Desync.Tick+"_p"+Session.LocalPlayer);
            File.WriteAllBytes(prefix+"_local.bin",Desync.LocalState);
            if(Desync.BaseState!=null) File.WriteAllBytes(prefix+"_base.bin",Desync.BaseState);
            if(Desync.RemoteState!=null) File.WriteAllBytes(prefix+"_remote.bin",Desync.RemoteState);
            File.WriteAllText(prefix+"_inputs.csv",Desync.Inputs,Encoding.UTF8);
            File.WriteAllText(prefix+".txt","Tick: "+Desync.Tick+"\nLocal: "+Desync.LocalHash.ToString("X16")+"\nRemote: "+Desync.RemoteHash.ToString("X16")+"\n"+Desync.Reason,Encoding.UTF8);
        }
        public void Dispose()
        {
            if(disposed) return; disposed=true; transport.Dispose(); writer.Dispose(); output.Dispose();
        }
    }
}

