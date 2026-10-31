using System;
using System.IO;

namespace AnimLab.Netcode.Sim
{
    /// <summary>Formato canónico explícito, independente de padding, plataforma e GetHashCode.</summary>
    public static class StateCodec
    {
        public const int Version = 6;
        public static byte[] Encode(in MatchState state)
        {
            var s = state;
            using (var ms = new MemoryStream()) using (var w = new BinaryWriter(ms)) {
                w.Write(Version); w.Write(s.Tick); w.Write(s.Rng); w.Write(s.Count);
                for (int i = 0; i < s.Count; i++) WritePlayer(w, MatchState.Player(ref s, i));
                WriteTable(w, s.Abilities);
                for (int i = 0; i < MatchState.ProjectileCapacity; i++) WriteProjectile(w, MatchState.Projectile(ref s, i));
                return ms.ToArray();
            }
        }
        public static MatchState Decode(byte[] bytes)
        {
            using (var ms = new MemoryStream(bytes)) using (var r = new BinaryReader(ms)) {
                if (r.ReadInt32() != Version) throw new InvalidDataException("State version");
                var s = new MatchState { Tick = r.ReadInt32(), Rng = r.ReadUInt64(), Count = r.ReadInt32() };
                if (s.Count < 0 || s.Count > MatchState.Capacity) throw new InvalidDataException("State fighters");
                for (int i = 0; i < s.Count; i++) MatchState.Player(ref s, i) = ReadPlayer(r);
                s.Abilities = ReadTable(r);
                for (int i = 0; i < MatchState.ProjectileCapacity; i++) MatchState.Projectile(ref s, i) = ReadProjectile(r);
                if (ms.Position != ms.Length) throw new InvalidDataException("State size");
                return s;
            }
        }
        public static ulong Checksum(in MatchState s)
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
            w.Write(c.Refill);
            w.Write(c.RefillDelay.Raw);
            w.Write(c.RefillRate.Raw);
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
            w.Write((byte)c.Slot0);
            w.Write((byte)c.Slot1);
            w.Write((byte)c.Slot2);
            w.Write((byte)c.Slot3);
            w.Write((byte)c.Slot4);
            w.Write((byte)c.Slot5);
            w.Write((byte)c.Slot6);
            w.Write((byte)c.Slot7);
            w.Write((byte)c.Slot8);
            w.Write(c.MaxMana.Raw);
            w.Write(c.ManaRegen.Raw);
            w.Write(c.CastSpeed.Raw);
            w.Write(c.SpellPower.Raw);
            w.Write(c.CooldownReduction.Raw);
            w.Write(c.MagicDefense.Raw);
            w.Write(c.AimHeight.Raw);
            w.Write(c.ResistWater.Raw);
            w.Write(c.ResistLightning.Raw);
            w.Write(c.ResistFire.Raw);
            w.Write(c.ResistAir.Raw);
            w.Write(c.ResistEarth.Raw);
            w.Write(c.ResistIce.Raw);
            w.Write(c.ResistDark.Raw);
            w.Write(c.ResistWet.Raw);
            w.Write(c.ResistStatic.Raw);
            w.Write(c.ResistGrounded.Raw);
            w.Write(c.ResistFrozen.Raw);
            w.Write(c.ResistAsphyxia.Raw);
            w.Write(c.ResistBurning.Raw);
            w.Write(c.ResistBlind.Raw);
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
            c.Refill = r.ReadBoolean();
            c.RefillDelay = Fix.FromRaw(r.ReadInt64());
            c.RefillRate = Fix.FromRaw(r.ReadInt64());
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
            c.Style = (Style)r.ReadByte();
            c.Slot0 = (AbilityId)r.ReadByte();
            c.Slot1 = (AbilityId)r.ReadByte();
            c.Slot2 = (AbilityId)r.ReadByte();
            c.Slot3 = (AbilityId)r.ReadByte();
            c.Slot4 = (AbilityId)r.ReadByte();
            c.Slot5 = (AbilityId)r.ReadByte();
            c.Slot6 = (AbilityId)r.ReadByte();
            c.Slot7 = (AbilityId)r.ReadByte();
            c.Slot8 = (AbilityId)r.ReadByte();
            c.MaxMana = Fix.FromRaw(r.ReadInt64());
            c.ManaRegen = Fix.FromRaw(r.ReadInt64());
            c.CastSpeed = Fix.FromRaw(r.ReadInt64());
            c.SpellPower = Fix.FromRaw(r.ReadInt64());
            c.CooldownReduction = Fix.FromRaw(r.ReadInt64());
            c.MagicDefense = Fix.FromRaw(r.ReadInt64());
            c.AimHeight = Fix.FromRaw(r.ReadInt64());
            c.ResistWater = Fix.FromRaw(r.ReadInt64());
            c.ResistLightning = Fix.FromRaw(r.ReadInt64());
            c.ResistFire = Fix.FromRaw(r.ReadInt64());
            c.ResistAir = Fix.FromRaw(r.ReadInt64());
            c.ResistEarth = Fix.FromRaw(r.ReadInt64());
            c.ResistIce = Fix.FromRaw(r.ReadInt64());
            c.ResistDark = Fix.FromRaw(r.ReadInt64());
            c.ResistWet = Fix.FromRaw(r.ReadInt64());
            c.ResistStatic = Fix.FromRaw(r.ReadInt64());
            c.ResistGrounded = Fix.FromRaw(r.ReadInt64());
            c.ResistFrozen = Fix.FromRaw(r.ReadInt64());
            c.ResistAsphyxia = Fix.FromRaw(r.ReadInt64());
            c.ResistBurning = Fix.FromRaw(r.ReadInt64());
            c.ResistBlind = Fix.FromRaw(r.ReadInt64());
            return c;
        }
        static void WriteTable(BinaryWriter w, AbilityTable t)
        {
            w.Write(t.DashMana.Raw);
            w.Write(t.DashCooldown.Raw);
            w.Write(t.DashDistance.Raw);
            w.Write(t.DashDuration.Raw);
            w.Write(t.FireMana.Raw);
            w.Write(t.FireCooldown.Raw);
            w.Write(t.FireFormation.Raw);
            w.Write(t.FireRadius.Raw);
            w.Write(t.FireSpeed.Raw);
            w.Write(t.FireGravity.Raw);
            w.Write(t.FireRange.Raw);
            w.Write(t.FireDamage.Raw);
            w.Write(t.FireBlast.Raw);
            WriteVec(w, t.FireShoulder);
        }
        static AbilityTable ReadTable(BinaryReader r)
        {
            var t = new AbilityTable();
            t.DashMana = Fix.FromRaw(r.ReadInt64());
            t.DashCooldown = Fix.FromRaw(r.ReadInt64());
            t.DashDistance = Fix.FromRaw(r.ReadInt64());
            t.DashDuration = Fix.FromRaw(r.ReadInt64());
            t.FireMana = Fix.FromRaw(r.ReadInt64());
            t.FireCooldown = Fix.FromRaw(r.ReadInt64());
            t.FireFormation = Fix.FromRaw(r.ReadInt64());
            t.FireRadius = Fix.FromRaw(r.ReadInt64());
            t.FireSpeed = Fix.FromRaw(r.ReadInt64());
            t.FireGravity = Fix.FromRaw(r.ReadInt64());
            t.FireRange = Fix.FromRaw(r.ReadInt64());
            t.FireDamage = Fix.FromRaw(r.ReadInt64());
            t.FireBlast = Fix.FromRaw(r.ReadInt64());
            t.FireShoulder = ReadVec(r);
            return t;
        }
        // Projéteis vazios ocupam um byte (o tipo None).
        static void WriteProjectile(BinaryWriter w, ProjectileState b)
        {
            w.Write((byte)b.Kind);
            if (b.Kind == AbilityId.None) return;
            w.Write(b.Owner); w.Write(b.Target); w.Write(b.Launched);
            WriteVec(w, b.Position); WriteVec(w, b.Velocity); WriteVec(w, b.Aim);
            w.Write(b.Age.Raw); w.Write(b.Travelled.Raw); w.Write(b.Formation.Raw); w.Write(b.Power.Raw);
        }
        static ProjectileState ReadProjectile(BinaryReader r)
        {
            var b = new ProjectileState { Kind = (AbilityId)r.ReadByte() };
            if (b.Kind == AbilityId.None) return b;
            b.Owner = r.ReadSByte(); b.Target = r.ReadSByte(); b.Launched = r.ReadBoolean();
            b.Position = ReadVec(r); b.Velocity = ReadVec(r); b.Aim = ReadVec(r);
            b.Age = Fix.FromRaw(r.ReadInt64()); b.Travelled = Fix.FromRaw(r.ReadInt64());
            b.Formation = Fix.FromRaw(r.ReadInt64()); b.Power = Fix.FromRaw(r.ReadInt64());
            return b;
        }
        static void WriteAi(BinaryWriter w, AiState a)
        {
            foreach (Fix f in new[] { a.Skill, a.Aggression, a.ReactMin, a.ReactMax, a.AggroRange, a.AttackRange, a.FootsieRange, a.RespawnDelay,
                a.NextThink, a.ReactAt, a.GuardFrom, a.GuardUntil, a.AttackAt, a.NextPressAt, a.BackstepUntil, a.FeintUntil, a.RetreatUntil,
                a.StrafeSign, a.StrafeSwitchAt, a.NoiseSeed, a.NextAttackWish, a.DownSince }) w.Write(f.Raw);
            w.Write(a.Rng); w.Write(a.Opponent); w.Write(a.OppPrevSwing); w.Write(a.ComboLeft); w.Write((byte)a.OppPrevWeapon);
        }
        static AiState ReadAi(BinaryReader r)
        {
            Fix N() => Fix.FromRaw(r.ReadInt64());
            var a = new AiState { Skill = N(), Aggression = N(), ReactMin = N(), ReactMax = N(), AggroRange = N(), AttackRange = N(),
                FootsieRange = N(), RespawnDelay = N(), NextThink = N(), ReactAt = N(), GuardFrom = N(), GuardUntil = N(), AttackAt = N(),
                NextPressAt = N(), BackstepUntil = N(), FeintUntil = N(), RetreatUntil = N(), StrafeSign = N(), StrafeSwitchAt = N(),
                NoiseSeed = N(), NextAttackWish = N(), DownSince = N() };
            a.Rng = r.ReadUInt64(); a.Opponent = r.ReadInt32(); a.OppPrevSwing = r.ReadInt32(); a.ComboLeft = r.ReadInt32();
            a.OppPrevWeapon = (WeaponState)r.ReadByte();
            return a;
        }
        static void WritePlayer(BinaryWriter w, in PlayerState p)
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
            w.Write(p.StruckMask);
            w.Write((byte)p.Kind);
            w.Write(p.Team);
            WriteAi(w, p.Ai);
            w.Write(p.Mana.Raw);
            w.Write(p.Cooldown0.Raw);
            w.Write(p.Cooldown1.Raw);
            w.Write(p.Cooldown2.Raw);
            w.Write(p.Cooldown3.Raw);
            w.Write(p.Cooldown4.Raw);
            w.Write(p.Cooldown5.Raw);
            w.Write(p.Cooldown6.Raw);
            w.Write(p.Cooldown7.Raw);
            w.Write(p.Cooldown8.Raw);
            w.Write(p.DashTime.Raw);
            w.Write(p.WetLeft.Raw);
            w.Write(p.WetDuration.Raw);
            w.Write(p.StaticLeft.Raw);
            w.Write(p.StaticDuration.Raw);
            w.Write(p.GroundedLeft.Raw);
            w.Write(p.GroundedDuration.Raw);
            w.Write(p.FrozenLeft.Raw);
            w.Write(p.FrozenDuration.Raw);
            w.Write(p.AsphyxiaLeft.Raw);
            w.Write(p.AsphyxiaDuration.Raw);
            w.Write(p.BurningLeft.Raw);
            w.Write(p.BurningDuration.Raw);
            w.Write(p.BlindLeft.Raw);
            w.Write(p.BlindDuration.Raw);
            w.Write(p.Dashing);
            WriteVec(w, p.DashDir);
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
            p.StruckMask = r.ReadUInt16();
            p.Kind = (FighterKind)r.ReadByte();
            p.Team = r.ReadByte();
            p.Ai = ReadAi(r);
            p.Mana = Fix.FromRaw(r.ReadInt64());
            p.Cooldown0 = Fix.FromRaw(r.ReadInt64());
            p.Cooldown1 = Fix.FromRaw(r.ReadInt64());
            p.Cooldown2 = Fix.FromRaw(r.ReadInt64());
            p.Cooldown3 = Fix.FromRaw(r.ReadInt64());
            p.Cooldown4 = Fix.FromRaw(r.ReadInt64());
            p.Cooldown5 = Fix.FromRaw(r.ReadInt64());
            p.Cooldown6 = Fix.FromRaw(r.ReadInt64());
            p.Cooldown7 = Fix.FromRaw(r.ReadInt64());
            p.Cooldown8 = Fix.FromRaw(r.ReadInt64());
            p.DashTime = Fix.FromRaw(r.ReadInt64());
            p.WetLeft = Fix.FromRaw(r.ReadInt64());
            p.WetDuration = Fix.FromRaw(r.ReadInt64());
            p.StaticLeft = Fix.FromRaw(r.ReadInt64());
            p.StaticDuration = Fix.FromRaw(r.ReadInt64());
            p.GroundedLeft = Fix.FromRaw(r.ReadInt64());
            p.GroundedDuration = Fix.FromRaw(r.ReadInt64());
            p.FrozenLeft = Fix.FromRaw(r.ReadInt64());
            p.FrozenDuration = Fix.FromRaw(r.ReadInt64());
            p.AsphyxiaLeft = Fix.FromRaw(r.ReadInt64());
            p.AsphyxiaDuration = Fix.FromRaw(r.ReadInt64());
            p.BurningLeft = Fix.FromRaw(r.ReadInt64());
            p.BurningDuration = Fix.FromRaw(r.ReadInt64());
            p.BlindLeft = Fix.FromRaw(r.ReadInt64());
            p.BlindDuration = Fix.FromRaw(r.ReadInt64());
            p.Dashing = r.ReadBoolean();
            p.DashDir = ReadVec(r);
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

