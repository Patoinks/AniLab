using System;
using System.IO;
using AnimLab.Netcode.Sim;

namespace AnimLab.Netcode.Rollback
{
    public static class MatchHandshake
    {
        public const int RulesVersion=1;
        public static byte[] Offer(PlayerConfig player,int delay)
        {
            Validate(player);
            var s=MatchState.Create(); s.P0=MatchState.CreatePlayer(player,s.P0.Position,s.P0.Yaw);
            s.P1=MatchState.CreatePlayer(player,s.P1.Position,s.P1.Yaw);
            return Encode(s,delay);
        }
        /// <param name="extra">Lutadores da IA (duelista, bonecos) que o host junta à partida.</param>
        /// <param name="playerTeam">0 = jogadores hostis entre si; 1 = cooperativo contra a IA.</param>
        public static byte[] Accept(byte[] offer,PlayerConfig host,int delay,ulong seed,PlayerState[] extra=null,byte playerTeam=0,AbilityTable? table=null)
        {
            MatchState proposed=Decode(offer,out int remoteDelay); Validate(host); Validate(proposed.P0.Config);
            var s=MatchState.Create(seed);
            if(table.HasValue) { ValidateTable(table.Value); s.Abilities=table.Value; }
            s.P0=MatchState.CreatePlayer(host,s.P0.Position,s.P0.Yaw); s.P0.Team=playerTeam;
            s.P1=MatchState.CreatePlayer(proposed.P0.Config,s.P1.Position,s.P1.Yaw); s.P1.Team=playerTeam;
            if(extra!=null) foreach(var f in extra) { ValidateFighter(f); MatchState.Add(ref s,f); }
            return Encode(s,Math.Max(delay,remoteDelay));
        }
        static void ValidateFighter(in PlayerState f)
        {
            if(f.Kind==FighterKind.Player || f.Team>2) throw new InvalidDataException("Fighter invalid");
            Validate(f.Config);
        }
        public static byte[] Encode(in MatchState state,int delay)
        {
            if(delay<0 || delay>8 || state.Tick!=0) throw new InvalidDataException("Match settings invalid");
            byte[] bytes=Packing.Deflate(StateCodec.Encode(state));
            using(var stream=new MemoryStream()) using(var writer=new BinaryWriter(stream)) {
                writer.Write(RulesVersion); writer.Write(RulesFingerprint.Value); writer.Write((byte)delay); writer.Write((ushort)bytes.Length); writer.Write(bytes); return stream.ToArray();
            }
        }
        public static MatchState Decode(byte[] bytes,out int delay)
        {
            using(var stream=new MemoryStream(bytes,false)) using(var reader=new BinaryReader(stream)) {
                if(reader.ReadInt32()!=RulesVersion || reader.ReadUInt64()!=RulesFingerprint.Value)
                    throw new InvalidDataException("Game version differs: both players need the same project version (git pull)");
                delay=reader.ReadByte(); int length=reader.ReadUInt16();
                if(delay>8 || length!=stream.Length-stream.Position) throw new InvalidDataException("Match settings invalid");
                MatchState s=StateCodec.Decode(Packing.Inflate(reader.ReadBytes(length)));
                if(s.Tick!=0 || s.Rng==0) throw new InvalidDataException("Initial state invalid");
                Validate(s.P0.Config); Validate(s.P1.Config);
                if(s.Count<2 || s.P0.Team!=s.P1.Team || s.P0.Team>1) throw new InvalidDataException("Initial state invalid");
                var canonical=MatchState.Create(s.Rng);
                ValidateTable(s.Abilities); canonical.Abilities=s.Abilities;
                canonical.P0=MatchState.CreatePlayer(s.P0.Config,canonical.P0.Position,canonical.P0.Yaw); canonical.P0.Team=s.P0.Team;
                canonical.P1=MatchState.CreatePlayer(s.P1.Config,canonical.P1.Position,canonical.P1.Yaw); canonical.P1.Team=s.P1.Team;
                for(int i=2;i<s.Count;i++) { var f=MatchState.Player(ref s,i); ValidateFighter(f); MatchState.Add(ref canonical,f); }
                if(StateCodec.Checksum(canonical)!=StateCodec.Checksum(s)) throw new InvalidDataException("Initial state invalid");
                return s;
            }
        }
        static void ValidateTable(AbilityTable t)
        {
            Range(t.DashMana,0,1000); Range(t.DashCooldown,0,60); Range(t.DashDistance,0,50); Range(t.DashDuration,Fix.Ratio(1,100),5);
            Range(t.FireMana,0,1000); Range(t.FireCooldown,0,60); Range(t.FireFormation,0,5); Range(t.FireRadius,Fix.Ratio(1,100),5);
            Range(t.FireSpeed,1,200); Range(t.FireGravity,0,100); Range(t.FireRange,0,500); Range(t.FireDamage,0,10000); Range(t.FireBlast,0,50);
        }
        static void Range(Fix v,Fix min,Fix max) { if(v<min || v>max) throw new InvalidDataException("Player parameters invalid"); }
        public static void Validate(PlayerConfig c)
        {
            Range(c.MaxHealth,1,1000000); Range(c.MaxPoise,1,10000); Range(c.Defense,0,10000); Range(c.HealthRegen,0,1000); Range(c.RefillDelay,0,100); Range(c.RefillRate,0,10);
            Range(c.MaxStamina,1,10000); Range(c.StaminaRegen,0,1000);
            Range(c.MaxMana,0,100000); Range(c.ManaRegen,0,1000); Range(c.CastSpeed,Fix.Ratio(1,10),10); Range(c.SpellPower,0,100);
            Range(c.CooldownReduction,0,Fix.Ratio(9,10)); Range(c.MagicDefense,0,10000); Range(c.AimHeight,0,10);
            Range(c.Walk,0,50); Range(c.Run,0,50); Range(c.Acceleration,Fix.Ratio(1,10),200); Range(c.Gravity,-200,Fix.Ratio(-1,10));
            Range(c.Turn,Fix.Ratio(1,10),100); Range(c.SpeedScale,Fix.Ratio(1,10),5);
            Range(c.Radius,Fix.Ratio(5,100),2); Range(c.Height,c.Radius*2,5); Range(c.CenterY,0,5);
            Range(c.JumpHeight,Fix.Ratio(1,100),10); Range(c.AirControl,0,1);
            Range(c.RollDistance,Fix.Ratio(1,100),20); Range(c.RollDuration,Fix.Ratio(1,10),2); Range(c.RollCooldown,0,10);
            Range(c.RollInvulFrom,0,c.RollDuration); Range(c.RollInvulTo,c.RollInvulFrom,c.RollDuration);
            Range(c.LongJumpSpeed,0,50); Range(c.LongJumpHeight,Fix.Ratio(1,100),10);
            Range(c.PlungeRaise,0,2); Range(c.PlungeSpeed,1,100); Range(c.PlungeHoming,0,50); Range(c.PlungeMinHeight,0,10);
            Range(c.PlungeRadius,Fix.Ratio(1,100),10); Range(c.PlungeDamage,0,1000); Range(c.PlungeRecovery,Fix.Ratio(8,100),5);
            Range(c.DamageScale,0,100); Range(c.AttackSpeed,Fix.Ratio(1,10),5); Range(c.CritChance,0,1); Range(c.CritDamage,1,10);
            Range(c.StaggerScale,0,100); Range(c.KnockbackScale,0,1); Range(c.BlockReduction,0,1); Range(c.ParryWindow,0,1);
            if(c.Style==Style.Iaido || (int)c.Style>5) throw new InvalidDataException("Style unavailable");
        }
    }
}

