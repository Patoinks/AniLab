using System;
using System.Diagnostics;
using System.Threading;
using AnimLab.Netcode.Sim;
using AnimLab.Netcode.Rollback;
using AnimLab.Netcode.Transport;
using Steamworks;

namespace AnimLab.Netcode.Steam
{
    public static class SteamVerification
    {
        public static string Loopback(int limit=10800)
        {
            var c=PlayerConfig.Default; c.MaxHealth=100000; c.Style=Style.Reaper;
            var d=c; d.Style=Style.Daggers;
            SteamTransport.CreateLoopback(p=>MatchHandshake.Accept(p,c,2,4567),MatchHandshake.Offer(d,2),out var host,out var join);
            using(host) using(join) {
                var watch=Stopwatch.StartNew();
                while(watch.ElapsedMilliseconds<10000 && (host.State!=LinkState.Connected || join.State!=LinkState.Connected)) {
                    SteamAPI.RunCallbacks(); host.Update(watch.ElapsedMilliseconds); join.Update(watch.ElapsedMilliseconds); Thread.Sleep(1);
                }
                Require(host.State==LinkState.Connected && join.State==LinkState.Connected,"Steam handshake failed: "+host.Error+" / "+join.Error);
                var initial=MatchHandshake.Decode(host.MatchPayload,out int delay);
                Require(host.SessionId==join.SessionId && StateCodec.Checksum(initial)==StateCodec.Checksum(MatchHandshake.Decode(join.MatchPayload,out _)),"Steam handshake differs");
                using(var a=new RollbackPeer(new RollbackSession(initial,0,delay),host,host.SessionId))
                using(var b=new RollbackPeer(new RollbackSession(initial,1,delay),join,join.SessionId)) {
                    int passes=0,sentA=-1,sentB=-1; long lastA=-99,lastB=-99;
                    while(watch.ElapsedMilliseconds<45000 && (a.Session.State.Tick<limit || b.Session.State.Tick<limit || a.ChecksumsVerified<limit/30 || b.ChecksumsVerified<limit/30)) {
                        long now=watch.ElapsedMilliseconds; SteamAPI.RunCallbacks(); a.Pump(now); b.Pump(now);
                        Advance(a,limit); Advance(b,limit);
                        // Como no jogo: envia quando avança um tick (ou de 4 em 4 ms para acks); sem isto a simulação rápida inunda o limite de envio da Steam.
                        if(a.Session.State.Tick!=sentA || now-lastA>=4) { a.SendInputs(now); sentA=a.Session.State.Tick; lastA=now; }
                        if(b.Session.State.Tick!=sentB || now-lastB>=4) { b.SendInputs(now); sentB=b.Session.State.Tick; lastB=now; }
                        a.Pump(now); b.Pump(now);
                        Require(a.Desync==null && b.Desync==null && !a.Session.Faulted && !b.Session.Faulted,"Steam loopback desync or session fault");
                        Require(a.Link==LinkState.Connected && b.Link==LinkState.Connected,"Steam loopback disconnected");
                        if(++passes%4==0) Thread.Sleep(1);
                    }
                    Require(a.Session.State.Tick==limit && b.Session.State.Tick==limit && a.ChecksumsVerified==limit/30 && b.ChecksumsVerified==limit/30,
                        $"Steam loopback incomplete: ticks {a.Session.State.Tick}/{b.Session.State.Tick}, verified {a.ChecksumsVerified}/{b.ChecksumsVerified}, stalls {a.Session.Stalls}/{b.Session.Stalls}, gaps {a.PacketGaps}/{b.PacketGaps}");
                    Require(StateCodec.Checksum(a.Session.State)==StateCodec.Checksum(b.Session.State),"Steam final states differ");
                    return $"native_socket_pair=true; network_loopback=true; ticks={limit}; verified={a.ChecksumsVerified}/{b.ChecksumsVerified}; hash={StateCodec.Checksum(a.Session.State):X16}; desync=0; send_flags={SteamTransport.SendFlags}; rejected={host.Rejected}/{join.Rejected}";
                }
            }
        }
        static void Advance(RollbackPeer peer,int limit)
        {
            if(peer.Session.State.Tick>=limit) return;
            peer.Session.QueueLocal(BotInputs.Combat(peer.Session.State.Tick,peer.Session.LocalPlayer,peer.Session.State)); peer.Session.TryAdvance();
        }
        static void Require(bool valid,string error) { if(!valid) throw new InvalidOperationException(error); }
    }
}
