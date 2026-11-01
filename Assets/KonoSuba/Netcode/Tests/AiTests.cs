using AnimLab.Netcode.Sim;
using AnimLab.Netcode.Rollback;
using NUnit.Framework;

namespace AnimLab.Netcode.Tests
{
    public class AiTests
    {
        /// <summary>Duelista (Reaper) e boneco na equipa 2, como o host junta a IA da cena.</summary>
        public static PlayerState[] Fighters()
        {
            var duelist=MatchState.CreatePlayer(PlayerConfig.Default,new FixVec3(3,0,2),Fix.Pi);
            duelist.Kind=FighterKind.Duelist; duelist.Team=2; duelist.Config.Style=Style.Reaper;
            duelist.Ai=AiState.Duelist(99); duelist.Ai.NoiseSeed=Fix.FromInt(11);
            var dummy=MatchState.CreatePlayer(PlayerConfig.Default,new FixVec3(-4,0,4),0);
            dummy.Kind=FighterKind.Dummy; dummy.Team=2; dummy.Config.Walk=dummy.Config.Run=0;
            dummy.Config.Refill=true; dummy.Config.RefillDelay=3; dummy.Config.RefillRate=Fix.Ratio(1,2);
            return new[] { duelist,dummy };
        }
        [Test] public void DuelistFightsLikeTheOfflineAiAndIsDeterministic()
        {
            ulong first=0;
            for(int run=0;run<2;run++)
            {
                // O bot de combate luta contra o lutador 1: aqui o lutador 1 é o duelista, e há um boneco ao lado.
                var s=MatchState.Create(11); var ai=Fighters(); s.P1=ai[0]; MatchState.Add(ref s,ai[1]);
                var e=new EventBuffer(); int attacks=0,guardTicks=0,parries=0,blocks=0; var prev=s.P1.Weapon;
                for(int t=0;t<10800;t++)
                {
                    World.Step(ref s,BotInputs.Combat(t,0,s),default,e);
                    for(int i=0;i<e.Count;i++)
                    {
                        if(e[i].Target==1 && e[i].Kind==EventKind.Parry) parries++;
                        if(e[i].Target==1 && e[i].Kind==EventKind.Block) blocks++;
                    }
                    if(s.P1.Weapon==WeaponState.Windup && prev!=WeaponState.Windup) attacks++;
                    if(s.P1.Weapon==WeaponState.Guard) guardTicks++;
                    prev=s.P1.Weapon;
                }
                TestContext.WriteLine($"AI attacks {attacks}, guard {guardTicks/60f:F1}s, parries {parries}, blocks {blocks}");
                Assert.Greater(attacks,20,"the duelist attacks"); Assert.Greater(guardTicks,60,"the duelist guards");
                Assert.Greater(parries+blocks,1,"the duelist defends");
                ulong hash=StateCodec.Checksum(s);
                if(run==0) first=hash; else Assert.AreEqual(first,hash);
            }
        }
        [Test] public void HandshakeWithAiFitsOneDatagramAndRoundTrips()
        {
            var host=PlayerConfig.Default; var guest=PlayerConfig.Default; guest.Style=Style.Fencer;
            byte[] offer=MatchHandshake.Offer(guest,2);
            byte[] accepted=MatchHandshake.Accept(offer,host,2,1234,Fighters(),1);
            Assert.Less(offer.Length,1300); Assert.Less(accepted.Length,1300,"initial state fits a datagram");
            MatchState s=MatchHandshake.Decode(accepted,out int delay);
            Assert.AreEqual(4,s.Count); Assert.AreEqual(1,s.P0.Team); Assert.AreEqual(1,s.P1.Team);
            Assert.AreEqual(FighterKind.Duelist,s.P2.Kind); Assert.AreEqual(FighterKind.Dummy,s.P3.Kind);
            Assert.IsFalse(MatchState.Hostile(ref s,0,1),"co-op players"); Assert.IsTrue(MatchState.Hostile(ref s,0,2));
        }
    }
}
