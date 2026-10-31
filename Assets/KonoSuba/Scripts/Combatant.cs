using System;
using System.Collections.Generic;
using UnityEngine;

namespace AnimLab.KonoSuba
{
    /// <summary>
    /// Corpo que recebe golpes: vida e o ponto de entrada de todos os golpes (TakeHit).
    /// A placa por cima do inimigo e os números de dano (CombatOverlay) leem daqui.
    /// O boneco de treino volta a encher a vida sozinho, alguns segundos depois do último golpe.
    /// Postura (poise): cada golpe gasta-a; quando parte, o corpo fica em stagger (não anda nem ataca).
    /// Knockback: impulso que decai, com gravidade; com CharacterController move-se por ele, senão pelo transform.
    /// Todos os golpes fazem o corpo (visual) recuar e inclinar-se para trás, como reação.
    /// </summary>
    public class Combatant : MonoBehaviour, AnimLab.Netcode.Unity.INetCombatView
    {
        public static readonly List<Combatant> All = new List<Combatant>();
        /// <summary>Qualquer golpe recebido, em qualquer corpo (números de dano, câmara...).</summary>
        public static event Action<Combatant, HitInfo> AnyHit;
        /// <summary>Um estado novo num corpo (o popup com o nome na placa).</summary>
        public static event Action<Combatant, StatusType> AnyStatusApplied;

        public class ActiveStatus
        {
            public StatusType type;
            public float duration, remaining;
            public float Remaining01 => duration > 0f ? remaining / duration : 0f;
        }

        /// <summary>Estados ativos, pela ordem em que entraram.</summary>
        public readonly List<ActiveStatus> statuses = new List<ActiveStatus>();

        public float maxHealth = 400f;
        public float health = 400f;

        [HideInInspector] public bool externalDrive;
        System.Action restoreExternal;
        public bool ExternalDrive
        {
            get => externalDrive;
            set
            {
                if (externalDrive == value) return;
                if (value)
                {
                    float oldHealth = health, oldMax = maxHealth, oldPoise = Poise, oldMaxPoise = maxPoise;
                    float oldFlinch = flinch, oldVelocity = flinchVel; Vector3 oldAxis = flinchAxis;
                    Quaternion oldExtra = extraRotation;
                    bool oldAir = airborne, oldArmor = superArmor;
                    var oldStatuses = new List<ActiveStatus>(statuses);
                    statuses.Clear();
                    restoreExternal = () => { health = oldHealth; maxHealth = oldMax; Poise = oldPoise; maxPoise = oldMaxPoise;
                        airborne = oldAir; superArmor = oldArmor; flinch = oldFlinch; flinchVel = oldVelocity; flinchAxis = oldAxis; extraRotation = oldExtra;
                        statuses.Clear(); statuses.AddRange(oldStatuses); };
                }
                else { restoreExternal?.Invoke(); restoreExternal = null; }
                externalDrive = value;
            }
        }
        bool externalStaggered, externalInvulnerable;

        public void CaptureNetConfig(ref AnimLab.Netcode.Sim.PlayerConfig c)
        {
            c.MaxHealth = AnimLab.Netcode.Unity.ViewConvert.Fixed(maxHealth); c.Defense = AnimLab.Netcode.Unity.ViewConvert.Fixed(defense);
            c.MaxPoise = AnimLab.Netcode.Unity.ViewConvert.Fixed(maxPoise); c.KnockbackScale = AnimLab.Netcode.Unity.ViewConvert.Fixed(knockbackScale);
            var sheet = GetComponent<CharacterStats>();
            c.HealthRegen = sheet != null ? AnimLab.Netcode.Unity.ViewConvert.Fixed(sheet.HealthRegen) : AnimLab.Netcode.Sim.Fix.Zero;
            c.MagicDefense = AnimLab.Netcode.Unity.ViewConvert.Fixed(magicDefense);
            float R(AbilityElement e) => elementResist.TryGetValue(e, out float r) ? r : 0f;
            float S(StatusType t) => statusResist.TryGetValue(t, out float r) ? r : 0f;
            c.ResistWater = AnimLab.Netcode.Unity.ViewConvert.Fixed(R(AbilityElement.Water)); c.ResistLightning = AnimLab.Netcode.Unity.ViewConvert.Fixed(R(AbilityElement.Lightning));
            c.ResistFire = AnimLab.Netcode.Unity.ViewConvert.Fixed(R(AbilityElement.Fire)); c.ResistAir = AnimLab.Netcode.Unity.ViewConvert.Fixed(R(AbilityElement.Air));
            c.ResistEarth = AnimLab.Netcode.Unity.ViewConvert.Fixed(R(AbilityElement.Earth)); c.ResistIce = AnimLab.Netcode.Unity.ViewConvert.Fixed(R(AbilityElement.Ice));
            c.ResistDark = AnimLab.Netcode.Unity.ViewConvert.Fixed(R(AbilityElement.Dark));
            c.ResistWet = AnimLab.Netcode.Unity.ViewConvert.Fixed(S(StatusType.Wet)); c.ResistStatic = AnimLab.Netcode.Unity.ViewConvert.Fixed(S(StatusType.Static));
            c.ResistGrounded = AnimLab.Netcode.Unity.ViewConvert.Fixed(S(StatusType.Grounded)); c.ResistFrozen = AnimLab.Netcode.Unity.ViewConvert.Fixed(S(StatusType.Frozen));
            c.ResistAsphyxia = AnimLab.Netcode.Unity.ViewConvert.Fixed(S(StatusType.Asphyxia)); c.ResistBurning = AnimLab.Netcode.Unity.ViewConvert.Fixed(S(StatusType.Burning));
            c.ResistBlind = AnimLab.Netcode.Unity.ViewConvert.Fixed(S(StatusType.Blind));
            var aimTarget = GetComponent<LockOnTarget>();
            if (aimTarget != null) c.AimHeight = AnimLab.Netcode.Unity.ViewConvert.Fixed(aimTarget.aimHeight);
            c.Refill = refill; c.RefillDelay = AnimLab.Netcode.Unity.ViewConvert.Fixed(refillDelay);
            c.RefillRate = AnimLab.Netcode.Unity.ViewConvert.Fixed(refillRate);
        }

        public void ApplyNetState(in AnimLab.Netcode.Sim.PlayerState state)
        {
            health = AnimLab.Netcode.Unity.ViewConvert.Number(state.Health);
            maxHealth = AnimLab.Netcode.Unity.ViewConvert.Number(state.Config.MaxHealth);
            maxPoise = AnimLab.Netcode.Unity.ViewConvert.Number(state.Config.MaxPoise);
            Poise = AnimLab.Netcode.Unity.ViewConvert.Number(state.Poise);
            externalStaggered = state.Staggered;
            externalInvulnerable = state.Invulnerable;
            airborne = state.KnockAirborne;
            superArmor = state.SuperArmor;
            // Estados vindos da simulação (ícones e anéis da placa).
            for (int t = 1; t <= 7; t++)
            {
                var type = (StatusType)t;
                float left = AnimLab.Netcode.Unity.ViewConvert.Number(state.StatusLeft((AnimLab.Netcode.Sim.Status)t));
                ActiveStatus found = null;
                foreach (ActiveStatus a in statuses) if (a.type == type) { found = a; break; }
                if (left <= 0f) { if (found != null) statuses.Remove(found); continue; }
                if (found == null) statuses.Add(found = new ActiveStatus { type = type });
                found.remaining = left;
                found.duration = AnimLab.Netcode.Unity.ViewConvert.Number(state.StatusDuration((AnimLab.Netcode.Sim.Status)t));
            }
        }

        public void PresentNetEvent(AnimLab.Netcode.Sim.SimEvent ev, Component source)
        {
            Vector3 point = AnimLab.Netcode.Unity.ViewConvert.Vector(ev.Point);
            Vector3 direction = AnimLab.Netcode.Unity.ViewConvert.Vector(ev.Direction);
            if (ev.Damage > AnimLab.Netcode.Sim.Fix.Zero)
            {
                AnyHit?.Invoke(this, new HitInfo { damage = AnimLab.Netcode.Unity.ViewConvert.Number(ev.Damage),
                    point = point, critical = ev.Critical });
                flinchAxis = Vector3.Cross(Vector3.up, direction);
                flinchVel += 300f;
            }
            var kind = ev.Kind;
            // Mesmo feedback do offline (SwordWielder.Show*). Só a apresentação usa o relógio do Unity.
            var attacker = source != null ? source.GetComponent<SwordWielder>() : null;
            var defender = GetComponent<SwordWielder>();
            Color spark = attacker != null ? attacker.sparkColor : new Color(1f, 0.75f, 0.45f);
            switch (kind)
            {
                case AnimLab.Netcode.Sim.EventKind.Jump:
                case AnimLab.Netcode.Sim.EventKind.Roll:
                case AnimLab.Netcode.Sim.EventKind.Land:
                    if (WaterRipples.Instance != null) WaterRipples.Instance.Spawn(point, 0.6f);
                    return;
                case AnimLab.Netcode.Sim.EventKind.Stagger: AnyStagger?.Invoke(this); return;
                case AnimLab.Netcode.Sim.EventKind.StatusApplied:
                    AnyStatusApplied?.Invoke(this, (StatusType)ev.Status); return;
                case AnimLab.Netcode.Sim.EventKind.Defeated:
                    Combat.Callout(transform.position + Vector3.up * 2.4f, "DEFEATED", new Color(1f, 0.45f, 0.32f), 30f); return;
                case AnimLab.Netcode.Sim.EventKind.Hit:
                    if (ev.VisualStop > AnimLab.Netcode.Sim.Fix.Zero && ev.Spray.LengthSquared > AnimLab.Netcode.Sim.Fix.Zero)
                        SwordWielder.ShowHit(point, AnimLab.Netcode.Unity.ViewConvert.Vector(ev.Spray), ev.Heavy, spark,
                            AnimLab.Netcode.Unity.ViewConvert.Number(ev.VisualStop));
                    else if (ev.VisualStop > AnimLab.Netcode.Sim.Fix.Zero) HitStop.Do(AnimLab.Netcode.Unity.ViewConvert.Number(ev.VisualStop));
                    break;
                // Num parry, o "atacante" do evento é quem foi aparado (source) e o alvo é quem aparou.
                case AnimLab.Netcode.Sim.EventKind.Parry:
                    SwordWielder.ShowParry(point, direction); defender?.Shake(); break;
                case AnimLab.Netcode.Sim.EventKind.Block:
                    SwordWielder.ShowBlock(point, direction, defender != null ? defender.sparkColor : spark, false); defender?.Shake(); break;
                case AnimLab.Netcode.Sim.EventKind.GuardBreak:
                    // Bloqueio que partiu a postura (com dano) ou Guard Breaker do montante.
                    if (ev.Heavy) SwordWielder.ShowGuardBreak(point, direction);
                    else { SwordWielder.ShowBlock(point, direction, defender != null ? defender.sparkColor : spark, true); defender?.Shake(); }
                    break;
                case AnimLab.Netcode.Sim.EventKind.Clash:
                    SwordWielder.ShowClash(point, direction, spark); attacker?.Shake(); defender?.Shake(); break;
            }
            if (kind == AnimLab.Netcode.Sim.EventKind.Plunge)
            {
                if (WaterRipples.Instance != null) WaterRipples.Instance.Spawn(point, 2.5f);
                FogField.Blast(point, radius: 5f, strength: 0.9f, duration: 2.4f, expandTime: 0.12f, waterScale: 1f, waterSpray: true);
                if (WaterSim.HasWaterAt(point)) WaterSim.Impulse(point, 1.6f, -0.12f, 4f);
            }
        }
        [Tooltip("Altura da placa (nome, vida, estados) acima dos pés (m).")]
        public float nameplateHeight = 2.35f;
        public bool showNameplate = true;

        [Header("Defesas (o CharacterStats preenche no player)")]
        [Tooltip("Reduz o dano sem elemento: dano × 100 / (100 + defesa).")]
        public float defense;
        [Tooltip("Reduz o dano com elemento (mesma fórmula, pela defesa mágica).")]
        public float magicDefense;
        /// <summary>Resistência por elemento (0–0.8): corta essa fração do dano desse elemento.</summary>
        public readonly Dictionary<AbilityElement, float> elementResist = new Dictionary<AbilityElement, float>();
        /// <summary>Resistência por estado (0–0.8): encurta a duração.</summary>
        public readonly Dictionary<StatusType, float> statusResist = new Dictionary<StatusType, float>();

        [Header("Postura e stagger")]
        public float maxPoise = 100f;
        [Tooltip("Segundos sem levar golpes até a postura começar a recuperar.")]
        public float poiseRegenDelay = 1.5f;
        public float poiseRegenRate = 60f;
        public float staggerTime = 0.7f;
        [Tooltip("Resistência ao knockback (1 = normal, 0 = imóvel).")]
        public float knockbackScale = 1f;
        [Tooltip("Parte que se inclina ao ser atingido (por omissão, o filho \"Body\").")]
        public Transform visual;

        [Header("Boneco de treino")]
        public bool refill = true;
        public float refillDelay = 3f;
        [Tooltip("Fração da vida por segundo.")]
        public float refillRate = 0.5f;

        public float LastHitTime { get; private set; } = -99f;
        /// <summary>Super armor (ex.: montante a golpear): os golpes quase não partem a postura nem empurram.</summary>
        [HideInInspector] public bool superArmor;
        /// <summary>Invulnerável até este instante (roll): os golpes passam por ele.</summary>
        [HideInInspector] public float invulnerableUntil = -99f;
        public bool IsInvulnerable => externalDrive ? externalInvulnerable : Time.time < invulnerableUntil;
        /// <summary>Rotação extra do corpo (roll, cambalhota do ground pound), somada à inclinação dos golpes.</summary>
        [HideInInspector] public Quaternion extraRotation = Quaternion.identity;
        public float Poise { get; private set; }
        public float Poise01 => Poise / Mathf.Max(maxPoise, 1f);
        public bool IsStaggered => externalDrive ? externalStaggered : Time.time < staggerUntil;
        public bool IsAirborne => airborne;
        /// <summary>Stagger a começar (popup, câmara...).</summary>
        public static event Action<Combatant> AnyStagger;
        public float Health01 => health / Mathf.Max(maxHealth, 1f);
        public string DisplayName => target != null ? target.displayName : name;

        LockOnTarget target;
        Collider body;
        CharacterController controller;
        Vector3 knock;          // velocidade horizontal do knockback
        float knockUp, groundY; // velocidade vertical e altura do chão (sem CharacterController)
        bool airborne;
        float staggerUntil = -99f;
        Vector3 flinchAxis;     // eixo da inclinação (perpendicular ao golpe)
        float flinch, flinchVel;
        Vector3 visualBasePos;
        Quaternion visualBaseRot;

        void Awake()
        {
            target = GetComponent<LockOnTarget>();
            body = GetComponent<Collider>();
            controller = GetComponent<CharacterController>();
            if (visual == null) visual = transform.Find("Body");
            if (visual != null) { visualBasePos = visual.localPosition; visualBaseRot = visual.localRotation; }
            Poise = maxPoise;
            groundY = transform.position.y;
        }

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        public Vector3 NameplatePoint => transform.position + Vector3.up * nameplateHeight;

        public Vector3 ClosestPoint(Vector3 p)
        {
            // O Collider.ClosestPoint não suporta CharacterController: aí é a cápsula dele, feita à mão.
            if (controller != null)
            {
                Vector3 c = transform.TransformPoint(controller.center);
                float r = controller.radius;
                float half = Mathf.Max(0f, controller.height * 0.5f - r);
                Vector3 onAxis = new Vector3(c.x, Mathf.Clamp(p.y, c.y - half, c.y + half), c.z);
                Vector3 d = p - onAxis;
                return d.sqrMagnitude <= r * r ? p : onAxis + d.normalized * r;
            }
            return body != null ? body.ClosestPoint(p) : transform.position + Vector3.up;
        }

        public void TakeHit(HitInfo hit)
        {
            if (externalDrive) return;
            if (!isActiveAndEnabled || IsInvulnerable) return;
            if (hit.point == Vector3.zero) hit.point = ClosestPoint(hit.source != null ? hit.source.position : transform.position);
            hit.damage = Mitigate(hit);
            health = Mathf.Max(0f, health - Mathf.Max(0f, hit.damage));
            LastHitTime = Time.time;

            // Postura: quando parte, stagger; o golpe que a parte empurra mais.
            float push = 1f;
            if (superArmor) { hit.stagger *= 0.25f; push = 0.3f; }
            if (hit.stagger > 0f)
            {
                Poise -= hit.stagger;
                if (Poise <= 0f) { Stagger(staggerTime); push = 1.4f; }
            }
            Knock(hit.knockback * push);

            // Reação: inclina-se para longe do golpe (mais forte com mais postura gasta).
            Vector3 dir = hit.knockback.sqrMagnitude > 1e-4f ? hit.knockback : transform.position - hit.point;
            dir.y = 0f;
            if (dir.sqrMagnitude > 1e-4f)
            {
                flinchAxis = Vector3.Cross(Vector3.up, dir.normalized);
                flinchVel += Mathf.Lerp(220f, 520f, Mathf.Clamp01(hit.stagger / 60f));
            }
            AnyHit?.Invoke(this, hit);
            if (hit.status != StatusType.None && hit.statusDuration > 0f) ApplyStatus(hit.status, hit.statusDuration);
        }

        float Mitigate(HitInfo hit)
        {
            float d = Mathf.Max(0f, hit.damage);
            bool elemental = hit.element != AbilityElement.Neutral;
            d *= 100f / (100f + Mathf.Max(0f, elemental ? magicDefense : defense));
            if (elemental && elementResist.TryGetValue(hit.element, out float r)) d *= 1f - Mathf.Clamp(r, 0f, 0.8f);
            return d;
        }

        /// <summary>Aplica um estado; se já estiver ativo, só renova a duração (sem novo popup).</summary>
        public void ApplyStatus(StatusType type, float duration)
        {
            if (externalDrive) return;
            if (statusResist.TryGetValue(type, out float r)) duration *= 1f - Mathf.Clamp(r, 0f, 0.8f);
            if (duration <= 0.05f) return;
            foreach (ActiveStatus a in statuses)
                if (a.type == type)
                {
                    a.duration = Mathf.Max(a.remaining, duration);
                    a.remaining = a.duration;
                    return;
                }
            statuses.Add(new ActiveStatus { type = type, duration = duration, remaining = duration });
            AnyStatusApplied?.Invoke(this, type);
        }

        public void Stagger(float duration)
        {
            bool fresh = !IsStaggered;
            staggerUntil = Mathf.Max(staggerUntil, Time.time + duration);
            Poise = maxPoise;
            if (fresh) AnyStagger?.Invoke(this);
        }

        /// <summary>Impulso (m/s): horizontal decai com atrito; vertical atira ao ar e cai com gravidade.</summary>
        public void Knock(Vector3 impulse)
        {
            impulse *= knockbackScale;
            knock += new Vector3(impulse.x, 0f, impulse.z);
            if (impulse.y > 0.01f) { knockUp = Mathf.Max(knockUp, impulse.y); airborne = true; }
        }

        public bool Has(StatusType type)
        {
            foreach (ActiveStatus a in statuses) if (a.type == type) return true;
            return false;
        }

        void Update()
        {
            if (externalDrive) { UpdateFlinch(Time.deltaTime); return; }
            for (int i = statuses.Count - 1; i >= 0; i--)
                if ((statuses[i].remaining -= Time.deltaTime) <= 0f) statuses.RemoveAt(i);
            if (refill && health < maxHealth && Time.time - LastHitTime > refillDelay)
                health = Mathf.Min(maxHealth, health + maxHealth * refillRate * Time.deltaTime);
            if (Poise < maxPoise && Time.time - LastHitTime > poiseRegenDelay)
                Poise = Mathf.Min(maxPoise, Poise + poiseRegenRate * Time.deltaTime);

            MoveKnockback(Time.deltaTime);
            UpdateFlinch(Time.deltaTime);
        }

        void MoveKnockback(float dt)
        {
            if (knock.sqrMagnitude < 1e-4f && !airborne) return;
            Vector3 step = knock * dt;
            if (airborne)
            {
                knockUp += Physics.gravity.y * dt;
                step.y = knockUp * dt;
            }
            if (controller != null && controller.enabled)
            {
                controller.Move(step);
                if (airborne && knockUp < 0f && controller.isGrounded) Land();
            }
            else
            {
                Vector3 p = transform.position + step;
                if (airborne && p.y <= groundY) { p.y = groundY; Land(); }
                transform.position = p;
            }
            // Atrito: no chão trava depressa; no ar quase não.
            knock = Vector3.MoveTowards(knock, Vector3.zero, (airborne ? 2f : 12f) * dt);
        }

        void Land()
        {
            airborne = false;
            knockUp = 0f;
            if (WaterRipples.Instance != null) WaterRipples.Instance.Spawn(transform.position, 0.6f);
        }

        // Mola amortecida: a inclinação salta com o golpe e volta ao sítio a oscilar um pouco.
        void UpdateFlinch(float dt)
        {
            if (visual == null) return;
            float stiffness = IsStaggered ? 60f : 220f, damping = IsStaggered ? 9f : 18f;
            float target = IsStaggered ? 14f : 0f; // em stagger fica dobrado para trás
            flinchVel += ((target - flinch) * stiffness - flinchVel * damping) * dt;
            flinch += flinchVel * dt;
            if (flinchAxis.sqrMagnitude < 1e-4f && extraRotation == Quaternion.identity) return;
            Vector3 localAxis = flinchAxis.sqrMagnitude > 1e-4f ? transform.InverseTransformDirection(flinchAxis) : Vector3.right;
            // Roda à volta dos pés: o corpo inclina-se em vez de rodar sobre o centro.
            Quaternion tilt = Quaternion.AngleAxis(flinch, localAxis);
            visual.localRotation = tilt * extraRotation * visualBaseRot;
            // (o player anima a altura do corpo no CapsulePlayer: aí só se inclina sobre o centro)
            if (controller == null) visual.localPosition = tilt * visualBasePos;
        }
    }
}
