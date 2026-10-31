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
    public interface ISessionTransport : ITransport
    {
        bool IsHost { get; }
        ulong SessionId { get; }
        byte[] MatchPayload { get; }
    }
    // O canal entrega datagramas; o handshake e a sessão são partilhados.
    public interface IDatagramChannel : IDisposable
    {
        LinkState State { get; }
        string Error { get; }
        void Update(long nowMs);
        bool Send(byte[] packet,string remote);
        bool TryReceive(out byte[] packet,out string sender);
    }
}

