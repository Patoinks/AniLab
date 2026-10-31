using System;
using System.Collections.Generic;

namespace AnimLab.Netcode.Transport
{
    /// <summary>Latência de uma via, jitter uniforme e perda independente por datagrama.</summary>
    public sealed class LoopbackNetwork
    {
        public int LatencyMs, JitterMs, LossPercent;
        public long NowMs { get; private set; }
        public long Dropped { get; private set; }
        public LoopbackEndpoint A { get; }
        public LoopbackEndpoint B { get; }
        ulong rng;
        long sequence;
        readonly List<Delivery> queue = new List<Delivery>();
        sealed class Delivery { public long At, Sequence; public LoopbackEndpoint Target; public byte[] Bytes; }
        public LoopbackNetwork(int latencyMs=120,int jitterMs=20,int lossPercent=2,ulong seed=12345)
        {
            if(latencyMs<0 || jitterMs<0 || lossPercent<0 || lossPercent>100) throw new ArgumentOutOfRangeException();
            LatencyMs=latencyMs; JitterMs=jitterMs; LossPercent=lossPercent; rng=seed==0?1:seed;
            A=new LoopbackEndpoint(this); B=new LoopbackEndpoint(this); A.Other=B; B.Other=A;
        }
        ulong Next() { rng^=rng<<13; rng^=rng>>7; rng^=rng<<17; return rng; }
        internal void Send(LoopbackEndpoint target,byte[] bytes)
        {
            if((int)(Next()%100)<LossPercent) { Dropped++; return; }
            int jitter=JitterMs==0?0:(int)(Next()%(uint)(JitterMs*2+1))-JitterMs;
            var item=new Delivery { At=NowMs+Math.Max(0,LatencyMs+jitter),Sequence=sequence++,Target=target,Bytes=(byte[])bytes.Clone() };
            int i=queue.Count;
            while(i>0 && (queue[i-1].At>item.At || queue[i-1].At==item.At && queue[i-1].Sequence>item.Sequence)) i--;
            queue.Insert(i,item);
        }
        public void Advance(long nowMs)
        {
            if(nowMs<NowMs) throw new ArgumentOutOfRangeException(nameof(nowMs));
            NowMs=nowMs;
            while(queue.Count>0 && queue[0].At<=nowMs) {
                var item=queue[0]; queue.RemoveAt(0); item.Target.Deliver(item.Bytes);
            }
        }
    }
    public sealed class LoopbackEndpoint : ITransport
    {
        readonly LoopbackNetwork network;
        readonly Queue<byte[]> inbox = new Queue<byte[]>();
        internal LoopbackEndpoint Other;
        public LinkState State { get; private set; } = LinkState.Connected;
        public string Error => "";
        public long Sent { get; private set; }
        public long Received { get; private set; }
        internal LoopbackEndpoint(LoopbackNetwork network) { this.network=network; }
        internal void Deliver(byte[] bytes) { if(State==LinkState.Connected) { inbox.Enqueue(bytes); Received++; } }
        public void Update(long nowMs) { network.Advance(nowMs); }
        public void Send(byte[] packet) { if(State==LinkState.Connected) { Sent++; network.Send(Other,packet); } }
        public bool TryReceive(out byte[] packet) { if(inbox.Count==0) { packet=null; return false; } packet=inbox.Dequeue(); return true; }
        public void Dispose() { State=LinkState.Disconnected; inbox.Clear(); }
    }
}

