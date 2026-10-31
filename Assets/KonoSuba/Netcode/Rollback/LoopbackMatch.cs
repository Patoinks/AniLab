using System;
using AnimLab.Netcode.Sim;
using AnimLab.Netcode.Transport;

namespace AnimLab.Netcode.Rollback
{
    /// <summary>O mesmo protocolo e as mesmas sessões usados pelos transportes reais.</summary>
    public sealed class LoopbackMatch : IDisposable
    {
        public LoopbackNetwork Network { get; }
        public RollbackPeer A { get; }
        public RollbackPeer B { get; }
        public int Attempts { get; private set; }
        public bool FirstQueued { get; private set; }
        public bool SecondQueued { get; private set; }
        public LoopbackMatch(MatchState initial,int latencyMs=120,int jitterMs=20,int lossPercent=2,int delay=2)
        {
            Network=new LoopbackNetwork(latencyMs,jitterMs,lossPercent);
            A=new RollbackPeer(new RollbackSession(initial,0,delay),Network.A);
            B=new RollbackPeer(new RollbackSession(initial,1,delay),Network.B);
        }
        public void Pump(long nowMs) { Network.Advance(nowMs); A.Pump(nowMs); B.Pump(nowMs); }
        public void Send(long nowMs) { A.SendInputs(nowMs); B.SendInputs(nowMs); }
        public void Tick(long nowMs,int limit=int.MaxValue,PlayerInput? firstInput=null,PlayerInput? secondInput=null)
        {
            Attempts++; Pump(nowMs);
            FirstQueued=SecondQueued=false;
            if(A.Session.State.Tick<limit) {
                FirstQueued=A.Session.QueueLocal(firstInput??BotInputs.Combat(A.Session.State.Tick,0,A.Session.State));
                A.Session.TryAdvance();
            }
            if(B.Session.State.Tick<limit) {
                SecondQueued=B.Session.QueueLocal(secondInput??BotInputs.Combat(B.Session.State.Tick,1,B.Session.State));
                B.Session.TryAdvance();
            }
            Send(nowMs); Pump(nowMs);
        }
        public void Dispose() { A.Dispose(); B.Dispose(); }
    }
}

