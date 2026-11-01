using System;
using System.IO;

namespace AnimLab.Netcode.Sim
{
    [Flags] public enum Buttons : byte { None = 0, Attack = 1, Guard = 2, Jump = 4, Roll = 8, Run = 16 }
    public enum Motion : byte { Ground, Air, Roll, Plunge }
    public enum WeaponState : byte { Idle, Windup, Active, Recovery, Guard, Recoil, Plunge }
    // Mesmos índices persistidos pelo SwordStyle original; Iaido fica reservado.
    public enum Style : byte { Knight, Fencer, Iaido, Greatsword, Reaper, Daggers }
    public enum EventKind : byte { Jump, Roll, Land, Plunge, Hit, Block, Parry, Clash, GuardBreak, Stagger, Defeated, Cast, Impact, Fizzle, StatusApplied }
    /// <summary>Quem conduz o lutador: um jogador (input da rede) ou a IA da simulação.</summary>
    public enum FighterKind : byte { Player, Duelist, Dummy }

    public struct PlayerInput : IEquatable<PlayerInput>
    {
        public byte MoveAngle, Magnitude;
        public ushort LookYaw;
        public Buttons Buttons;
        public bool Targeted;
        /// <summary>Alvo escolhido (lock-on): 0 = o mais próximo hostil; senão índice do lutador + 1.</summary>
        public byte TargetSlot;
        /// <summary>Habilidade pedida neste tick (AbilityId; 0 = nenhuma): a que está no espaço da hotbar carregado.</summary>
        public byte Cast;
        /// <summary>Habilidade cuja tecla está segura (AbilityId; 0 = nenhuma): canalizações e cargas.</summary>
        public byte Held;
        /// <summary>Ponto de mira no chão (cm).</summary>
        public short AimX, AimZ;
        public FixVec3 Aim => new FixVec3(Fix.Ratio(AimX, 100), Fix.Zero, Fix.Ratio(AimZ, 100));
        public const int WireSize = 12;
        public static PlayerInput Neutral(Fix yaw)
        {
            long angle=yaw.Raw%Fix.TwoPi.Raw; if(angle<0) angle+=Fix.TwoPi.Raw;
            return new PlayerInput { LookYaw=(ushort)(((angle*1024+Fix.TwoPi.Raw/2)/Fix.TwoPi.Raw)&1023) };
        }
        public bool Has(Buttons button) => (Buttons & button) != 0;
        public Fix Yaw => Fix.TwoPi * Fix.Ratio(LookYaw & 1023, 1024);
        public FixVec3 Wish => FixVec3.RotateY(FixVec3.Forward, Fix.TwoPi * Fix.Ratio(MoveAngle, 256)) * Fix.Ratio(Magnitude & 15, 15);
        public PlayerInput Predicted { get { var p = this; p.Buttons &= ~(Buttons.Attack | Buttons.Jump | Buttons.Roll); p.Cast = 0; return p; } }
        public bool Equals(PlayerInput p) => MoveAngle == p.MoveAngle && Magnitude == p.Magnitude && LookYaw == p.LookYaw && Buttons == p.Buttons && Targeted == p.Targeted && TargetSlot == p.TargetSlot && Cast == p.Cast && Held == p.Held && AimX == p.AimX && AimZ == p.AimZ;
        public override bool Equals(object o) => o is PlayerInput p && Equals(p);
        public override int GetHashCode() => MoveAngle | (Magnitude << 8) | ((int)Buttons << 12) ^ (LookYaw << 17) ^ (Targeted ? 1<<30 : 0) ^ (TargetSlot << 24) ^ (Cast << 20) ^ (Held << 16) ^ AimX ^ (AimZ << 7);
        public void Write(BinaryWriter w) { w.Write((byte)(Magnitude | (Targeted ? 16 : 0))); w.Write(MoveAngle); w.Write(LookYaw); w.Write((byte)Buttons); w.Write(TargetSlot); w.Write(Cast); w.Write(Held); w.Write(AimX); w.Write(AimZ); }
        public static PlayerInput Read(BinaryReader r)
        {
            byte m = r.ReadByte(), a = r.ReadByte(); ushort yaw = r.ReadUInt16(); var b = (Buttons)r.ReadByte(); byte target = r.ReadByte(), cast = r.ReadByte(), held = r.ReadByte(); short ax = r.ReadInt16(), az = r.ReadInt16();
            if (m > 31 || yaw > 1023 || ((byte)b & ~31) != 0 || target > MatchState.Capacity || cast > 15 || held > 15) throw new InvalidDataException("Invalid input");
            return new PlayerInput { Magnitude = (byte)(m & 15), Targeted = (m & 16) != 0, MoveAngle = a, LookYaw = yaw, Buttons = b, TargetSlot = target,
                Cast = cast, Held = held, AimX = ax, AimZ = az };
        }
    }

    /// <summary>Constantes da partida. Sem referências mutáveis nos snapshots.</summary>
    public struct PlayerConfig
    {
        public Fix Walk, Run, Acceleration, Turn, Gravity, SpeedScale;
        public Fix JumpHeight, AirControl, RollDistance, RollDuration, RollCooldown, RollInvulFrom, RollInvulTo;
        public Fix LongJumpSpeed, LongJumpHeight, PlungeRaise, PlungeSpeed, PlungeHoming, PlungeMinHeight, PlungeRadius, PlungeDamage, PlungeRecovery;
        public Fix MaxHealth, Defense, MaxPoise, KnockbackScale, HealthRegen;
        /// <summary>Stamina: golpes, roll, dash e bloqueios gastam; sem ela não ataca e a guarda parte.</summary>
        public Fix MaxStamina, StaminaRegen;
        /// <summary>Recarga de vida do Combatant: depois de RefillDelay s sem levar, enche RefillRate×vida por segundo.</summary>
        public bool Refill;
        public Fix RefillDelay, RefillRate;
        public Fix DamageScale, AttackSpeed, CritChance, CritDamage, StaggerScale, BlockReduction, ParryWindow;
        public Fix Radius, Height, CenterY;
        public Style Style;
        // Magia: habilidades que o lutador conhece (bit = AbilityId), mana e ficha do lançador; defesas mágicas.
        // A hotbar é só do cliente: o input já diz qual habilidade foi pedida, por isso reorganizá-la a meio da partida funciona.
        public ushort KnownAbilities;
        public bool Knows(AbilityId a) => a != AbilityId.None && (KnownAbilities & (1 << (int)a)) != 0;
        public void Learn(AbilityId a) { if (a != AbilityId.None) KnownAbilities |= (ushort)(1 << (int)a); }
        public Fix MaxMana, ManaRegen, CastSpeed, SpellPower, CooldownReduction, MagicDefense, AimHeight;
        public Fix ResistWater, ResistLightning, ResistFire, ResistAir, ResistEarth, ResistIce, ResistDark;
        public Fix ResistWet, ResistStatic, ResistGrounded, ResistFrozen, ResistAsphyxia, ResistBurning, ResistBlind;
        public Fix ElementResist(Element e)
        {
            switch (e)
            {
                case Element.Water: return ResistWater;
                case Element.Lightning: return ResistLightning;
                case Element.Fire: return ResistFire;
                case Element.Air: return ResistAir;
                case Element.Earth: return ResistEarth;
                case Element.Ice: return ResistIce;
                case Element.Dark: return ResistDark;
            }
            return Fix.Zero;
        }
        public Fix StatusResist(Status t)
        {
            switch (t)
            {
                case Status.Wet: return ResistWet;
                case Status.Static: return ResistStatic;
                case Status.Grounded: return ResistGrounded;
                case Status.Frozen: return ResistFrozen;
                case Status.Asphyxia: return ResistAsphyxia;
                case Status.Burning: return ResistBurning;
                case Status.Blind: return ResistBlind;
            }
            return Fix.Zero;
        }
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
            Radius = Fix.Ratio(35,100), Height = Fix.Ratio(16,10), CenterY = Fix.Ratio(85,100), Style = Style.Knight,
            MaxMana = 100, ManaRegen = 6, CastSpeed = 1, SpellPower = 1, AimHeight = Fix.Ratio(12,10),
            MaxStamina = SwordData.MaxStamina, StaminaRegen = SwordData.StaminaRegen
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
        public bool Queued, ParryArmed, SuperArmor, HasLastBlade;
        /// <summary>O golpe em curso começou sem stamina para o pagar: corre mais devagar.</summary>
        public bool TiredSwing;
        /// <summary>Lutadores já atingidos por este golpe (bit = índice).</summary>
        public ushort StruckMask;
        public FighterKind Kind;
        public AiState Ai;
        // Magia: mana, recargas por habilidade (Cooldown{AbilityId-1}: tempo global em que fica pronta), dash em curso, estados.
        public Fix Mana;
        public Fix Cooldown0, Cooldown1, Cooldown2, Cooldown3, Cooldown4, Cooldown5, Cooldown6, Cooldown7, Cooldown8;
        public bool Dashing;
        public Fix DashTime;
        public FixVec3 DashDir;
        public Fix WetLeft, WetDuration, StaticLeft, StaticDuration, GroundedLeft, GroundedDuration, FrozenLeft, FrozenDuration, AsphyxiaLeft, AsphyxiaDuration, BurningLeft, BurningDuration, BlindLeft, BlindDuration;
        public Fix CooldownUntil(int i)
        {
            switch (i)
            {
                case 0: return Cooldown0;
                case 1: return Cooldown1;
                case 2: return Cooldown2;
                case 3: return Cooldown3;
                case 4: return Cooldown4;
                case 5: return Cooldown5;
                case 6: return Cooldown6;
                case 7: return Cooldown7;
                case 8: return Cooldown8;
            }
            return Fix.Zero;
        }
        public void SetCooldownUntil(int i, Fix until)
        {
            switch (i)
            {
                case 0: Cooldown0 = until; break;
                case 1: Cooldown1 = until; break;
                case 2: Cooldown2 = until; break;
                case 3: Cooldown3 = until; break;
                case 4: Cooldown4 = until; break;
                case 5: Cooldown5 = until; break;
                case 6: Cooldown6 = until; break;
                case 7: Cooldown7 = until; break;
                case 8: Cooldown8 = until; break;
            }
        }
        public Fix StatusLeft(Status t)
        {
            switch (t)
            {
                case Status.Wet: return WetLeft;
                case Status.Static: return StaticLeft;
                case Status.Grounded: return GroundedLeft;
                case Status.Frozen: return FrozenLeft;
                case Status.Asphyxia: return AsphyxiaLeft;
                case Status.Burning: return BurningLeft;
                case Status.Blind: return BlindLeft;
            }
            return Fix.Zero;
        }
        public Fix StatusDuration(Status t)
        {
            switch (t)
            {
                case Status.Wet: return WetDuration;
                case Status.Static: return StaticDuration;
                case Status.Grounded: return GroundedDuration;
                case Status.Frozen: return FrozenDuration;
                case Status.Asphyxia: return AsphyxiaDuration;
                case Status.Burning: return BurningDuration;
                case Status.Blind: return BlindDuration;
            }
            return Fix.Zero;
        }
        public void SetStatus(Status t, Fix left, Fix duration)
        {
            switch (t)
            {
                case Status.Wet: WetLeft = left; WetDuration = duration; break;
                case Status.Static: StaticLeft = left; StaticDuration = duration; break;
                case Status.Grounded: GroundedLeft = left; GroundedDuration = duration; break;
                case Status.Frozen: FrozenLeft = left; FrozenDuration = duration; break;
                case Status.Asphyxia: AsphyxiaLeft = left; AsphyxiaDuration = duration; break;
                case Status.Burning: BurningLeft = left; BurningDuration = duration; break;
                case Status.Blind: BlindLeft = left; BlindDuration = duration; break;
            }
        }
        /// <summary>0 = sem equipa (hostil a todos); lutadores da mesma equipa (≥1) não se atingem.</summary>
        public byte Team;
        public FixVec3 LastBladeA, LastBladeB;
        public Fix Health, Poise, StaggerUntil, LastHitAt;
        /// <summary>Stamina atual, quando foi gasta pela última vez, e até quando está atordoado por um golpe (flinch).</summary>
        public Fix Stamina, StaminaUsedAt, HurtUntil;
        public int HitStop;
        public Buttons BufferedEdges;
        public byte BufferedCast;
        public Fix BodyTime;
        public bool Attacking => Weapon == WeaponState.Windup || Weapon == WeaponState.Active || Weapon == WeaponState.Recovery || Weapon == WeaponState.Plunge;
        public bool Staggered => BodyTime < StaggerUntil;
        /// <summary>Em stagger ou a sentir um golpe: não ataca, não guarda, não anda.</summary>
        public bool Hurt => Staggered || BodyTime < HurtUntil;
        public bool HasStamina => Stamina > Fix.Zero;
        public bool Invulnerable => BodyTime < InvulnerableUntil;
        public FixVec3 Forward => FixVec3.RotateY(FixVec3.Forward, Yaw);
    }

    /// <summary>Estado da partida, copiável por valor (snapshots do rollback). Lutadores em posições fixas P0..P7.</summary>
    public struct MatchState
    {
        public const int Capacity = 8, ProjectileCapacity = 16;
        public int Tick;
        public ulong Rng;
        /// <summary>Lutadores ativos: índices 0..Count-1.</summary>
        public int Count;
        public PlayerState P0, P1, P2, P3, P4, P5, P6, P7;
        public AbilityTable Abilities;
        public ProjectileState Q0, Q1, Q2, Q3, Q4, Q5, Q6, Q7, Q8, Q9, Q10, Q11, Q12, Q13, Q14, Q15;
        public static ref ProjectileState Projectile(ref MatchState s, int i)
        {
            switch (i)
            {
                case 0: return ref s.Q0;
                case 1: return ref s.Q1;
                case 2: return ref s.Q2;
                case 3: return ref s.Q3;
                case 4: return ref s.Q4;
                case 5: return ref s.Q5;
                case 6: return ref s.Q6;
                case 7: return ref s.Q7;
                case 8: return ref s.Q8;
                case 9: return ref s.Q9;
                case 10: return ref s.Q10;
                case 11: return ref s.Q11;
                case 12: return ref s.Q12;
                case 13: return ref s.Q13;
                case 14: return ref s.Q14;
                case 15: return ref s.Q15;
            }
            throw new ArgumentOutOfRangeException(nameof(i));
        }
        public static ref PlayerState Player(ref MatchState s, int id)
        {
            switch (id)
            {
                case 0: return ref s.P0;
                case 1: return ref s.P1;
                case 2: return ref s.P2;
                case 3: return ref s.P3;
                case 4: return ref s.P4;
                case 5: return ref s.P5;
                case 6: return ref s.P6;
                case 7: return ref s.P7;
            }
            throw new ArgumentOutOfRangeException(nameof(id));
        }
        public static bool Hostile(ref MatchState s, int a, int b)
        {
            if (a == b || b < 0 || b >= s.Count) return false;
            byte ta = Player(ref s, a).Team;
            return ta == 0 || ta != Player(ref s, b).Team;
        }
        /// <summary>Acrescenta um lutador; devolve o índice.</summary>
        public static int Add(ref MatchState s, PlayerState p)
        {
            if (s.Count >= Capacity) throw new InvalidOperationException("Fighter capacity");
            Player(ref s, s.Count) = p; return s.Count++;
        }
        public static MatchState Create(ulong seed = 7)
        {
            PlayerConfig c = PlayerConfig.Default;
            var s = new MatchState { Rng = seed == 0 ? 1UL : seed, Count = 2, Abilities = AbilityTable.Default, P0 = CreatePlayer(c, new FixVec3(-1,0,0), Fix.Pi / 2),
                P1 = CreatePlayer(c, new FixVec3(1,0,0), -Fix.Pi / 2) };
            return s;
        }
        public static PlayerState CreatePlayer(PlayerConfig c, FixVec3 position, Fix yaw) => new PlayerState {
            Config = c, Position = position, Yaw = yaw, Health = c.MaxHealth, Poise = c.MaxPoise, Mana = c.MaxMana,
            Stamina = c.MaxStamina, StaminaUsedAt = -99, HurtUntil = -99,
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
        public bool Critical, Heavy;
        public AbilityId Ability;
        public Element Element;
        public Status Status;
        /// <summary>Índice extra (ex.: o corpo atingido em cheio por um projétil; -1 = nenhum).</summary>
        public int Other;
        /// <summary>Para onde saltam as faíscas (só apresentação; não entra no estado).</summary>
        public FixVec3 Spray;
        public ulong Key => ((ulong)(uint)Tick << 32) | (uint)Id;
    }

    public sealed class EventBuffer
    {
        readonly SimEvent[] items = new SimEvent[128];
        public int Count { get; private set; }
        public SimEvent this[int i] => items[i];
        public void Clear() { Count = 0; }
        public void Add(int tick, EventKind kind, int source, int target, FixVec3 at, FixVec3 direction, Fix damage = default, bool critical = false, Fix visualStop = default, FixVec3 spray = default, bool heavy = false,
            AbilityId ability = AbilityId.None, Status status = Status.None, int other = -1, Element element = Element.Neutral)
        {
            if (Count == items.Length) throw new InvalidOperationException("Event capacity");
            // O id descreve o evento, não a ordem em que foi acrescentado durante a previsão.
            int id = ((int)kind << 8) | (source << 4) | target;
            items[Count++] = new SimEvent { Tick = tick, Id = id, Kind = kind, Source = source, Target = target, Point = at, Direction = direction, Damage = damage, Critical = critical, VisualStop = visualStop, Spray = spray, Heavy = heavy, Ability = ability, Status = status, Other = other, Element = element };
        }
    }
}

