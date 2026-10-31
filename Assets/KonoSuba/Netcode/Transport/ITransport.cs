using System;

namespace AnimLab.Netcode.Transport
{
    public enum LinkState { Connecting, Connected, Disconnected, TimedOut, Failed }
    public interface ITransport : IDisposable
    {
        LinkState State { get; }
        string Error { get; }
        long Sent { get; }
        long Received { get; }
        void Update(long nowMs);
        void Send(byte[] packet);
        bool TryReceive(out byte[] packet);
    }
}

