using System;
using System.IO;

namespace AnimLab.Netcode.Sim
{
    [Flags] public enum Buttons : byte { None = 0, Attack = 1, Guard = 2, Jump = 4, Roll = 8, Run = 16 }
    public enum Motion : byte { Ground, Air, Roll, Plunge }
    public enum WeaponState : byte { Idle, Windup, Active, Recovery, Guard, Recoil, Plunge }
    // Mesmos índices persistidos pelo SwordStyle original; Iaido fica reservado.
    public enum Style : byte { Knight, Fencer, Iaido, Greatsword, Reaper, Daggers }
    public enum EventKind : byte { Jump, Roll, Land, Plunge, Hit, Block, Parry, Clash, GuardBreak, Stagger }

    public struct PlayerInput : IEquatable<PlayerInput>
    {
        public byte MoveAngle, Magnitude;
        public ushort LookYaw;
        public Buttons Buttons;
        public bool Targeted;
        public const int WireSize = 5;
        public static PlayerInput Neutral(Fix yaw)
        {
            long angle=yaw.Raw%Fix.TwoPi.Raw; if(angle<0) angle+=Fix.TwoPi.Raw;
            return new PlayerInput { LookYaw=(ushort)(((angle*1024+Fix.TwoPi.Raw/2)/Fix.TwoPi.Raw)&1023) };
        }
        public bool Has(Buttons button) => (Buttons & button) != 0;
        public Fix Yaw => Fix.TwoPi * Fix.Ratio(LookYaw & 1023, 1024);
        public FixVec3 Wish => FixVec3.RotateY(FixVec3.Forward, Fix.TwoPi * Fix.Ratio(MoveAngle, 256)) * Fix.Ratio(Magnitude & 15, 15);
        public PlayerInput Predicted { get { var p = this; p.Buttons &= ~(Buttons.Attack | Buttons.Jump | Buttons.Roll); return p; } }
        public bool Equals(PlayerInput p) => MoveAngle == p.MoveAngle && Magnitude == p.Magnitude && LookYaw == p.LookYaw && Buttons == p.Buttons && Targeted == p.Targeted;
        public override bool Equals(object o) => o is PlayerInput p && Equals(p);
        public override int GetHashCode() => MoveAngle | (Magnitude << 8) | ((int)Buttons << 12) ^ (LookYaw << 17) ^ (Targeted ? 1<<30 : 0);
        public void Write(BinaryWriter w) { w.Write((byte)(Magnitude | (Targeted ? 16 : 0))); w.Write(MoveAngle); w.Write(LookYaw); w.Write((byte)Buttons); }
        public static PlayerInput Read(BinaryReader r)
        {
            byte m = r.ReadByte(), a = r.ReadByte(); ushort yaw = r.ReadUInt16(); var b = (Buttons)r.ReadByte();
            if (m > 31 || yaw > 1023 || ((byte)b & ~31) != 0) throw new InvalidDataException("Invalid input");
            return new PlayerInput { Magnitude = (byte)(m & 15), Targeted = (m & 16) != 0, MoveAngle = a, LookYaw = yaw, Buttons = b };
        }
    }

    /// <summary>Constantes da partida. Sem referências mutáveis nos snapshots.</summary>
    public struct PlayerConfig
    {
        public Fix Walk, Run, Acceleration, Turn, Gravity, SpeedScale;
        public Fix JumpHeight, AirControl, RollDistance, RollDuration, RollCooldown, RollInvulFrom, RollInvulTo;
        public Fix LongJumpSpeed, LongJumpHeight, PlungeRaise, PlungeSpeed, PlungeHoming, PlungeMinHeight, PlungeRadius, PlungeDamage, PlungeRecovery;
        public Fix MaxHealth, Defense, MaxPoise, KnockbackScale, HealthRegen;
        public Fix DamageScale, AttackSpeed, CritChance, CritDamage, StaggerScale, BlockReduction, ParryWindow;
        public Fix Radius, Height, CenterY;
        public Style Style;
        public static PlayerConfig Default => new PlayerConfig {
            Walk = Fix.Ratio(35,10), Run = Fix.Ratio(65,10), Acceleration = 20, Turn = Fix.Degrees(900),
            Gravity = -20, SpeedScale = Fix.Ratio(1028,1000),
            JumpHeight = Fix.Ratio(11,10), AirControl = Fix.Ratio(35,100), RollDistance = 4,
            RollDuration = Fix.Ratio(42,100), RollCooldown = Fix.Ratio(5,10), RollInvulFrom = Fix.Ratio(4,100), RollInvulTo = Fix.Ratio(32,100),
            LongJumpSpeed = 9, LongJumpHeight = Fix.Ratio(13,10), PlungeRaise = Fix.Ratio(16,100),
            PlungeSpeed = 26, PlungeHoming = 7, PlungeMinHeight = Fix.Ratio(5,10),
            PlungeRadius = Fix.Ratio(26,10), PlungeDamage = 22, PlungeRecovery = Fix.Ratio(45,100),
            MaxHealth = 292, Defense = Fix.Ratio(241,10), MaxPoise = 116, KnockbackScale = Fix.Ratio(952,1000), HealthRegen = Fix.Ratio(17,10),
            DamageScale = Fix.Ratio(40,30), AttackSpeed = Fix.Ratio(1056,1000), CritChance = Fix.Ratio(58,1000),
            CritDamage = Fix.Ratio(1545,1000), StaggerScale = Fix.Ratio(109,100), BlockReduction = Fix.Ratio(8,10), ParryWindow = Fix.Ratio(2,10),
            Radius = Fix.Ratio(35,100), Height = Fix.Ratio(16,10), CenterY = Fix.Ratio(85,100), Style = Style.Knight
        };
    }

    public struct PlayerState
    {
        public PlayerConfig Config;
        public FixVec3 Position, Velocity, RollDirection, Knock;
        public Fix Yaw, VerticalVelocity, KnockUp;
        public Motion Motion;
        public Fix MotionTime, RollReadyAt, LandLagUntil, InvulnerableUntil;
        public bool RollJumpQueued, LongJump, Dived, HasPlungeTarget, KnockAirborne;
        public WeaponState Weapon;
        public int Swing, PlungePhase;
        public Fix WeaponTime, IdleSince, GuardSince, GuardReleasedAt, RecoilFor, StuckFor, LastU;
        public bool Queued, ParryArmed, Struck, SuperArmor, HasLastBlade;
        public FixVec3 LastBladeA, LastBladeB;
        public Fix Health, Poise, StaggerUntil, LastHitAt;
        public int HitStop;
        public Buttons BufferedEdges;
        public Fix BodyTime;
        public bool Attacking => Weapon == WeaponState.Windup || Weapon == WeaponState.Active || Weapon == WeaponState.Recovery || Weapon == WeaponState.Plunge;
        public bool Staggered => BodyTime < StaggerUntil;
        public bool Invulnerable => BodyTime < InvulnerableUntil;
        public FixVec3 Forward => FixVec3.RotateY(FixVec3.Forward, Yaw);
    }

    public struct MatchState
    {
        public int Tick;
        public ulong Rng;
        public PlayerState P0, P1;
        public static ref PlayerState Player(ref MatchState s, int id)
        {
            if (id == 0) return ref s.P0;
            if (id == 1) return ref s.P1;
            throw new ArgumentOutOfRangeException(nameof(id));
        }
        public static MatchState Create(ulong seed = 7)
        {
            PlayerConfig c = PlayerConfig.Default;
            return new MatchState { Rng = seed == 0 ? 1UL : seed, P0 = CreatePlayer(c, new FixVec3(-1,0,0), Fix.Pi / 2),
                P1 = CreatePlayer(c, new FixVec3(1,0,0), -Fix.Pi / 2) };
        }
        public static PlayerState CreatePlayer(PlayerConfig c, FixVec3 position, Fix yaw) => new PlayerState {
            Config = c, Position = position, Yaw = yaw, Health = c.MaxHealth, Poise = c.MaxPoise,
            IdleSince = -9, GuardReleasedAt = -9, LastHitAt = -99, StaggerUntil = -99, InvulnerableUntil = -99
        };
    }

    public struct SimEvent
    {
        public int Tick, Id, Source, Target;
        public EventKind Kind;
        public FixVec3 Point, Direction;
        public Fix Damage;
        public Fix VisualStop;
        public bool Critical;
        public ulong Key => ((ulong)(uint)Tick << 32) | (uint)Id;
    }

    public sealed class EventBuffer
    {
        readonly SimEvent[] items = new SimEvent[32];
        public int Count { get; private set; }
        public SimEvent this[int i] => items[i];
        public void Clear() { Count = 0; }
        public void Add(int tick, EventKind kind, int source, int target, FixVec3 at, FixVec3 direction, Fix damage = default, bool critical = false, Fix visualStop = default)
        {
            if (Count == items.Length) throw new InvalidOperationException("Event capacity");
            // O id descreve o evento, não a ordem em que foi acrescentado durante a previsão.
            int id = ((int)kind << 8) | (source << 4) | target;
            items[Count++] = new SimEvent { Tick = tick, Id = id, Kind = kind, Source = source, Target = target, Point = at, Direction = direction, Damage = damage, Critical = critical, VisualStop = visualStop };
        }
    }
}

