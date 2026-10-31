using System;
using System.IO;

namespace AnimLab.Netcode.Sim
{
    /// <summary>Formato canónico explícito, independente de padding, plataforma e GetHashCode.</summary>
    public static class StateCodec
    {
        public const int Version = 3;
        public static byte[] Encode(MatchState s)
        {
            using (var ms = new MemoryStream()) using (var w = new BinaryWriter(ms)) {
                w.Write(Version); w.Write(s.Tick); w.Write(s.Rng); WritePlayer(w, s.P0); WritePlayer(w, s.P1); return ms.ToArray();
            }
        }
        public static MatchState Decode(byte[] bytes)
        {
            using (var ms = new MemoryStream(bytes)) using (var r = new BinaryReader(ms)) {
                if (r.ReadInt32() != Version) throw new InvalidDataException("State version");
                var s = new MatchState { Tick = r.ReadInt32(), Rng = r.ReadUInt64(), P0 = ReadPlayer(r), P1 = ReadPlayer(r) };
                if (ms.Position != ms.Length) throw new InvalidDataException("State size");
                return s;
            }
        }
        public static ulong Checksum(MatchState s)
        {
            ulong h = 14695981039346656037UL;
            foreach (byte b in Encode(s)) { h ^= b; h = unchecked(h * 1099511628211UL); }
            return h;
        }
        static void WriteVec(BinaryWriter w, FixVec3 v) { w.Write(v.X.Raw); w.Write(v.Y.Raw); w.Write(v.Z.Raw); }
        static FixVec3 ReadVec(BinaryReader r) => new FixVec3(Fix.FromRaw(r.ReadInt64()), Fix.FromRaw(r.ReadInt64()), Fix.FromRaw(r.ReadInt64()));
        static void WriteConfig(BinaryWriter w, PlayerConfig c)
        {
            w.Write(c.Walk.Raw);
            w.Write(c.Run.Raw);
            w.Write(c.Acceleration.Raw);
            w.Write(c.Turn.Raw);
            w.Write(c.Gravity.Raw);
            w.Write(c.SpeedScale.Raw);
            w.Write(c.JumpHeight.Raw);
            w.Write(c.AirControl.Raw);
            w.Write(c.RollDistance.Raw);
            w.Write(c.RollDuration.Raw);
            w.Write(c.RollCooldown.Raw);
            w.Write(c.RollInvulFrom.Raw);
            w.Write(c.RollInvulTo.Raw);
            w.Write(c.LongJumpSpeed.Raw);
            w.Write(c.LongJumpHeight.Raw);
            w.Write(c.PlungeRaise.Raw);
            w.Write(c.PlungeSpeed.Raw);
            w.Write(c.PlungeHoming.Raw);
            w.Write(c.PlungeMinHeight.Raw);
            w.Write(c.PlungeRadius.Raw);
            w.Write(c.PlungeDamage.Raw);
            w.Write(c.PlungeRecovery.Raw);
            w.Write(c.MaxHealth.Raw);
            w.Write(c.Defense.Raw);
            w.Write(c.MaxPoise.Raw);
            w.Write(c.KnockbackScale.Raw);
            w.Write(c.HealthRegen.Raw);
            w.Write(c.DamageScale.Raw);
            w.Write(c.AttackSpeed.Raw);
            w.Write(c.CritChance.Raw);
            w.Write(c.CritDamage.Raw);
            w.Write(c.StaggerScale.Raw);
            w.Write(c.BlockReduction.Raw);
            w.Write(c.ParryWindow.Raw);
            w.Write(c.Radius.Raw);
            w.Write(c.Height.Raw);
            w.Write(c.CenterY.Raw);
            w.Write((byte)c.Style);
        }
        static PlayerConfig ReadConfig(BinaryReader r)
        {
            var c = new PlayerConfig();
            c.Walk = Fix.FromRaw(r.ReadInt64());
            c.Run = Fix.FromRaw(r.ReadInt64());
            c.Acceleration = Fix.FromRaw(r.ReadInt64());
            c.Turn = Fix.FromRaw(r.ReadInt64());
            c.Gravity = Fix.FromRaw(r.ReadInt64());
            c.SpeedScale = Fix.FromRaw(r.ReadInt64());
            c.JumpHeight = Fix.FromRaw(r.ReadInt64());
            c.AirControl = Fix.FromRaw(r.ReadInt64());
            c.RollDistance = Fix.FromRaw(r.ReadInt64());
            c.RollDuration = Fix.FromRaw(r.ReadInt64());
            c.RollCooldown = Fix.FromRaw(r.ReadInt64());
            c.RollInvulFrom = Fix.FromRaw(r.ReadInt64());
            c.RollInvulTo = Fix.FromRaw(r.ReadInt64());
            c.LongJumpSpeed = Fix.FromRaw(r.ReadInt64());
            c.LongJumpHeight = Fix.FromRaw(r.ReadInt64());
            c.PlungeRaise = Fix.FromRaw(r.ReadInt64());
            c.PlungeSpeed = Fix.FromRaw(r.ReadInt64());
            c.PlungeHoming = Fix.FromRaw(r.ReadInt64());
            c.PlungeMinHeight = Fix.FromRaw(r.ReadInt64());
            c.PlungeRadius = Fix.FromRaw(r.ReadInt64());
            c.PlungeDamage = Fix.FromRaw(r.ReadInt64());
            c.PlungeRecovery = Fix.FromRaw(r.ReadInt64());
            c.MaxHealth = Fix.FromRaw(r.ReadInt64());
            c.Defense = Fix.FromRaw(r.ReadInt64());
            c.MaxPoise = Fix.FromRaw(r.ReadInt64());
            c.KnockbackScale = Fix.FromRaw(r.ReadInt64());
            c.HealthRegen = Fix.FromRaw(r.ReadInt64());
            c.DamageScale = Fix.FromRaw(r.ReadInt64());
            c.AttackSpeed = Fix.FromRaw(r.ReadInt64());
            c.CritChance = Fix.FromRaw(r.ReadInt64());
            c.CritDamage = Fix.FromRaw(r.ReadInt64());
            c.StaggerScale = Fix.FromRaw(r.ReadInt64());
            c.BlockReduction = Fix.FromRaw(r.ReadInt64());
            c.ParryWindow = Fix.FromRaw(r.ReadInt64());
            c.Radius = Fix.FromRaw(r.ReadInt64());
            c.Height = Fix.FromRaw(r.ReadInt64());
            c.CenterY = Fix.FromRaw(r.ReadInt64());
            c.Style = (Style)r.ReadByte(); return c;
        }
        static void WritePlayer(BinaryWriter w, PlayerState p)
        {
            WriteConfig(w, p.Config);
            WriteVec(w, p.Position);
            WriteVec(w, p.Velocity);
            WriteVec(w, p.RollDirection);
            WriteVec(w, p.Knock);
            WriteVec(w, p.LastBladeA);
            WriteVec(w, p.LastBladeB);
            w.Write(p.Yaw.Raw);
            w.Write(p.VerticalVelocity.Raw);
            w.Write(p.KnockUp.Raw);
            w.Write(p.MotionTime.Raw);
            w.Write(p.RollReadyAt.Raw);
            w.Write(p.LandLagUntil.Raw);
            w.Write(p.InvulnerableUntil.Raw);
            w.Write(p.WeaponTime.Raw);
            w.Write(p.IdleSince.Raw);
            w.Write(p.GuardSince.Raw);
            w.Write(p.GuardReleasedAt.Raw);
            w.Write(p.RecoilFor.Raw);
            w.Write(p.StuckFor.Raw);
            w.Write(p.LastU.Raw);
            w.Write(p.Health.Raw);
            w.Write(p.Poise.Raw);
            w.Write(p.StaggerUntil.Raw);
            w.Write(p.LastHitAt.Raw);
            w.Write(p.BodyTime.Raw);
            w.Write(p.RollJumpQueued);
            w.Write(p.LongJump);
            w.Write(p.Dived);
            w.Write(p.HasPlungeTarget);
            w.Write(p.KnockAirborne);
            w.Write(p.Queued);
            w.Write(p.ParryArmed);
            w.Write(p.Struck);
            w.Write(p.SuperArmor);
            w.Write(p.HasLastBlade);
            w.Write(p.Swing);
            w.Write(p.PlungePhase);
            w.Write(p.HitStop);
            w.Write((byte)p.BufferedEdges);
            w.Write((byte)p.Motion);
            w.Write((byte)p.Weapon);
        }
        static PlayerState ReadPlayer(BinaryReader r)
        {
            var p = new PlayerState { Config = ReadConfig(r) };
            p.Position = ReadVec(r);
            p.Velocity = ReadVec(r);
            p.RollDirection = ReadVec(r);
            p.Knock = ReadVec(r);
            p.LastBladeA = ReadVec(r);
            p.LastBladeB = ReadVec(r);
            p.Yaw = Fix.FromRaw(r.ReadInt64());
            p.VerticalVelocity = Fix.FromRaw(r.ReadInt64());
            p.KnockUp = Fix.FromRaw(r.ReadInt64());
            p.MotionTime = Fix.FromRaw(r.ReadInt64());
            p.RollReadyAt = Fix.FromRaw(r.ReadInt64());
            p.LandLagUntil = Fix.FromRaw(r.ReadInt64());
            p.InvulnerableUntil = Fix.FromRaw(r.ReadInt64());
            p.WeaponTime = Fix.FromRaw(r.ReadInt64());
            p.IdleSince = Fix.FromRaw(r.ReadInt64());
            p.GuardSince = Fix.FromRaw(r.ReadInt64());
            p.GuardReleasedAt = Fix.FromRaw(r.ReadInt64());
            p.RecoilFor = Fix.FromRaw(r.ReadInt64());
            p.StuckFor = Fix.FromRaw(r.ReadInt64());
            p.LastU = Fix.FromRaw(r.ReadInt64());
            p.Health = Fix.FromRaw(r.ReadInt64());
            p.Poise = Fix.FromRaw(r.ReadInt64());
            p.StaggerUntil = Fix.FromRaw(r.ReadInt64());
            p.LastHitAt = Fix.FromRaw(r.ReadInt64());
            p.BodyTime = Fix.FromRaw(r.ReadInt64());
            p.RollJumpQueued = r.ReadBoolean();
            p.LongJump = r.ReadBoolean();
            p.Dived = r.ReadBoolean();
            p.HasPlungeTarget = r.ReadBoolean();
            p.KnockAirborne = r.ReadBoolean();
            p.Queued = r.ReadBoolean();
            p.ParryArmed = r.ReadBoolean();
            p.Struck = r.ReadBoolean();
            p.SuperArmor = r.ReadBoolean();
            p.HasLastBlade = r.ReadBoolean();
            p.Swing = r.ReadInt32();
            p.PlungePhase = r.ReadInt32();
            p.HitStop = r.ReadInt32();
            p.BufferedEdges = (Buttons)r.ReadByte();
            p.Motion = (Motion)r.ReadByte();
            p.Weapon = (WeaponState)r.ReadByte();
            return p;
        }
    }
}

