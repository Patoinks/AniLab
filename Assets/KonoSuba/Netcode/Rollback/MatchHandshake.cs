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
        public static byte[] Accept(byte[] offer,PlayerConfig host,int delay,ulong seed)
        {
            MatchState proposed=Decode(offer,out int remoteDelay); Validate(host); Validate(proposed.P0.Config);
            var s=MatchState.Create(seed);
            s.P0=MatchState.CreatePlayer(host,s.P0.Position,s.P0.Yaw);
            s.P1=MatchState.CreatePlayer(proposed.P0.Config,s.P1.Position,s.P1.Yaw);
            return Encode(s,Math.Max(delay,remoteDelay));
        }
        public static byte[] Encode(MatchState state,int delay)
        {
            if(delay<0 || delay>8 || state.Tick!=0) throw new InvalidDataException("Match settings invalid");
            byte[] bytes=StateCodec.Encode(state);
            using(var stream=new MemoryStream()) using(var writer=new BinaryWriter(stream)) {
                writer.Write(RulesVersion); writer.Write((byte)delay); writer.Write((ushort)bytes.Length); writer.Write(bytes); return stream.ToArray();
            }
        }
        public static MatchState Decode(byte[] bytes,out int delay)
        {
            using(var stream=new MemoryStream(bytes,false)) using(var reader=new BinaryReader(stream)) {
                if(reader.ReadInt32()!=RulesVersion) throw new InvalidDataException("Game version differs");
                delay=reader.ReadByte(); int length=reader.ReadUInt16();
                if(delay>8 || length!=stream.Length-stream.Position) throw new InvalidDataException("Match settings invalid");
                MatchState s=StateCodec.Decode(reader.ReadBytes(length));
                if(s.Tick!=0 || s.Rng==0) throw new InvalidDataException("Initial state invalid");
                Validate(s.P0.Config); Validate(s.P1.Config);
                var canonical=MatchState.Create(s.Rng);
                canonical.P0=MatchState.CreatePlayer(s.P0.Config,canonical.P0.Position,canonical.P0.Yaw);
                canonical.P1=MatchState.CreatePlayer(s.P1.Config,canonical.P1.Position,canonical.P1.Yaw);
                if(StateCodec.Checksum(canonical)!=StateCodec.Checksum(s)) throw new InvalidDataException("Initial state invalid");
                return s;
            }
        }
        static void Range(Fix v,Fix min,Fix max) { if(v<min || v>max) throw new InvalidDataException("Player parameters invalid"); }
        public static void Validate(PlayerConfig c)
        {
            Range(c.MaxHealth,1,1000000); Range(c.MaxPoise,1,10000); Range(c.Defense,0,10000); Range(c.HealthRegen,0,1000);
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

