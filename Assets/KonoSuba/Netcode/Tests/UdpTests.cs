using System;
using System.IO;
using AnimLab.Netcode.Sim;
using AnimLab.Netcode.Rollback;
using AnimLab.Netcode.Transport;
using NUnit.Framework;

namespace AnimLab.Netcode.Tests
{
    public class UdpTests
    {
        [Test] public void ReorderedPacketsDoNotCountAsPermanentLoss()
        {
            using(var match=new LoopbackMatch(MatchState.Create(),60,50,0)) {
                long now=0;
                for(int i=0;i<100;i++) { match.Pump(now); match.Send(now); now+=17; }
                for(int i=0;i<30;i++) { match.Pump(now); now+=17; }
                Assert.AreEqual(0,match.A.PacketGaps); Assert.AreEqual(0,match.B.PacketGaps);
                Assert.AreEqual(0,match.A.BadPackets); Assert.AreEqual(0,match.B.BadPackets);
            }
        }
        [Test] public void HandshakeSharesBothConfigsAndNegotiatesSymmetricDelay()
        {
            var host=PlayerConfig.Default; var join=host; join.Style=Style.Daggers; join.MaxHealth=500;
            byte[] payload=MatchHandshake.Accept(MatchHandshake.Offer(join,3),host,2,1234);
            var s=MatchHandshake.Decode(payload,out int delay);
            Assert.AreEqual(3,delay); Assert.AreEqual(Style.Knight,s.P0.Config.Style); Assert.AreEqual(Style.Daggers,s.P1.Config.Style);
            Assert.AreEqual((Fix)500,s.P1.Health); Assert.AreEqual(1234UL,s.Rng);
            join.RollDuration=0; Assert.Throws<InvalidDataException>(()=>MatchHandshake.Offer(join,2));
        }
        [Test] public void UdpTimeoutAndAddressValidation()
        {
            Assert.Throws<FormatException>(()=>UdpTransport.Parse("localhost"));
            Assert.Throws<FormatException>(()=>UdpTransport.Parse("127.0.0.1:99999"));
            using(var host=new UdpTransport(0,_=>new byte[] {1})) {
                host.Update(0); host.Update(10001); Assert.AreEqual(LinkState.TimedOut,host.State);
            }
        }
        [Test] public void RealUdpPeersReachThreeMinutesWithConfirmedIdenticalStates()
        {
            var c=PlayerConfig.Default; c.MaxHealth=100000; c.Style=Style.Reaper;
            var d=c; d.Style=Style.Daggers;
            using(var host=new UdpTransport(0,p=>MatchHandshake.Accept(p,c,2,4567)))
            using(var join=new UdpTransport("127.0.0.1:"+host.LocalPort,MatchHandshake.Offer(d,2))) {
                long now=0;
                for(int i=0;i<1000 && (host.State!=LinkState.Connected || join.State!=LinkState.Connected);i++) {
                    host.Update(now); join.Update(now); now+=17;
                }
                Assert.AreEqual(LinkState.Connected,host.State,host.Error); Assert.AreEqual(LinkState.Connected,join.State,join.Error);
                Assert.AreEqual(host.SessionId,join.SessionId);
                MatchState initial=MatchHandshake.Decode(host.MatchPayload,out int delay);
                Assert.AreEqual(StateCodec.Checksum(initial),StateCodec.Checksum(MatchHandshake.Decode(join.MatchPayload,out _)));
                using(var a=new RollbackPeer(new RollbackSession(initial,0,delay),host,host.SessionId))
                using(var b=new RollbackPeer(new RollbackSession(initial,1,delay),join,join.SessionId)) {
                    const int limit=10800;
                    for(int i=0;i<limit*3 && (a.Session.State.Tick<limit || b.Session.State.Tick<limit);i++) {
                        now+=17; a.Pump(now); b.Pump(now);
                        Tick(a,limit); Tick(b,limit); a.SendInputs(now); b.SendInputs(now); a.Pump(now); b.Pump(now);
                        Assert.IsFalse(a.Session.Faulted,a.Status); Assert.IsFalse(b.Session.Faulted,b.Status);
                    }
                    for(int i=0;i<100;i++) { now+=17; a.Pump(now); b.Pump(now); a.SendInputs(now); b.SendInputs(now); }
                    Assert.AreEqual(limit,a.Session.State.Tick); Assert.AreEqual(limit,b.Session.State.Tick);
                    Assert.AreEqual(StateCodec.Checksum(a.Session.State),StateCodec.Checksum(b.Session.State));
                    Assert.IsNull(a.Desync); Assert.IsNull(b.Desync); Assert.AreEqual(360,a.ChecksumsVerified); Assert.AreEqual(360,b.ChecksumsVerified);
                    string summary=$"Real UDP loopback: ticks={limit}; hash={StateCodec.Checksum(a.Session.State):X16}; verified={a.ChecksumsVerified}/{b.ChecksumsVerified}; desync=0; rejected={host.Rejected}/{join.Rejected}";
                    TestContext.WriteLine(summary); Directory.CreateDirectory("Logs/netcode"); File.WriteAllText("Logs/netcode/udp_editmode.txt",summary);
                }
            }
        }
        static void Tick(RollbackPeer p,int limit)
        {
            if(p.Session.State.Tick>=limit) return;
            p.Session.QueueLocal(BotInputs.Combat(p.Session.State.Tick,p.Session.LocalPlayer,p.Session.State)); p.Session.TryAdvance();
        }
    }
}
