using System;
using System.Collections.Generic;
using UnityEngine;

namespace AnimLab.KonoSuba
{
    /// <summary>
    /// Espada procedural para as cápsulas (sem braços): a espada segue uma "mão" invisível que descreve arcos
    /// à volta de um ombro. Combo de 3 golpes (corte horizontal, corte de volta a subir, golpe de cima).
    /// Cada golpe: preparação (windup), fase ativa (a lâmina dá dano) e recuperação (janela para o golpe seguinte).
    /// A lâmina é varrida entre frames (vários pontos ao longo dela, em subpassos), por isso não atravessa
    /// alvos mesmo a grande velocidade.
    ///
    /// Guard (manter F): a espada atravessa-se à frente. Um golpe de frente é bloqueado: o defensor leva só
    /// uma fração do dano e parte da postura (se a postura partir: GUARD BREAK), o atacante ressalta.
    /// Parry: o golpe chega nos primeiros instantes do guard (parryWindow) → sem dano; o atacante fica em
    /// stagger, com paragem e câmara lenta. Largar e voltar a carregar logo a seguir não abre nova janela
    /// (parryLockout): não dá para fazer parry só a martelar a tecla.
    /// Sword Clash: duas lâminas em fase ativa cruzam-se → os dois golpes anulam-se e ambos recuam.
    /// Golpe a pique (Plunge, comandado pelo CapsulePlayer no ar): espada erguida sobre a cabeça, desce a
    /// direito à frente do corpo a varrer (pode ser bloqueado, levar parry ou dar clash) e crava no chão.
    ///
    /// O modelo da espada tem a origem no centro do punho e a lâmina em +Y local.
    /// </summary>
    [DefaultExecutionOrder(50)] // depois do CapsulePlayer: a espada segue o corpo já movido
    public class SwordWielder : MonoBehaviour
    {
        [Serializable]
        public class Swing
        {
            public float windup = 0.12f, active = 0.13f, recovery = 0.3f;
            public float damage = 20f, stagger = 35f;
            [Tooltip("Knockback horizontal e vertical (m/s).")]
            public float push = 2.5f, lift = 0f;
            [Tooltip("Avanço do corpo durante a fase ativa (m).")]
            public float lunge = 0.35f;
            public float hitStop = 0.06f;
        }

        public static readonly List<SwordWielder> All = new List<SwordWielder>();

        public Transform sword;
        [Tooltip("Altura local (Y) onde começa a lâmina e onde está a ponta.")]
        public float bladeStart = 0.12f, bladeTip = 0.9f;
        public float hitRadius = 0.12f;
        [Tooltip("Distância entre lâminas que conta como choque (m).")]
        public float clashRadius = 0.14f;
        [Tooltip("Raio do corpo: a mão e os arcos afastam-se dele para a espada não o atravessar.")]
        public float bodyRadius = 0.3f;
        public Color sparkColor = new Color(1f, 0.75f, 0.45f);

        [Header("Stats (o CharacterStats escreve aqui)")]
        public float damageScale = 1f;
        [Tooltip("Attack Speed: os golpes (preparação, golpe e recuperação) correm este fator mais depressa.")]
        public float speedScale = 1f;
        public float critChance, critDamage = 1.5f;
        public float staggerScale = 1f;
        [Range(0f, 1f)] public float blockReduction = 0.8f;
        public float parryWindow = 0.2f;

        [Header("Guarda")]
        [Tooltip("Ângulo (a partir da frente) dentro do qual a guarda apanha golpes.")]
        public float guardArc = 75f;
        [Tooltip("Depois de largar a guarda, durante este tempo voltar a guardar não abre janela de parry.")]
        public float parryLockout = 0.35f;
        [Tooltip("Stagger do atacante quando leva parry (s).")]
        public float parriedStagger = 1.1f;

        [Header("Controlo")]
        public bool playerControlled;
        [Tooltip("Guarda pedida (o player escreve com a tecla; a AI escreve diretamente).")]
        public bool guardHeld;
        /// <summary>O corpo está ocupado (roll, golpe a pique): não ataca nem guarda.</summary>
        [HideInInspector] public bool busy;
        [Tooltip("Alvo preferido para virar ao atacar (a AI põe o seu adversário aqui).")]
        public Transform preferredTarget;
        [Tooltip("Vira-se para o inimigo mais próximo à frente, até esta distância (m).")]
        public float autoAimRange = 3.5f;

        [Header("Golpes")]
        public Swing[] combo =
        {
            new Swing { damage = 18f, stagger = 30f, push = 2.2f, lunge = 0.3f },
            new Swing { damage = 20f, stagger = 35f, push = 2.6f, lunge = 0.3f },
            new Swing { windup = 0.18f, active = 0.14f, recovery = 0.42f, damage = 34f, stagger = 70f, push = 7f, lift = 3f, lunge = 0.55f, hitStop = 0.11f },
        };

        [Tooltip("Golpe a pique: windup = preparação no ar; o resto do dano/empurrão é o da lâmina na descida.")]
        public Swing plunge = new Swing { windup = 0.16f, active = 0f, recovery = 0.45f, damage = 30f, stagger = 55f, push = 3f, lift = 1.5f, lunge = 0f, hitStop = 0.08f };
        /// <summary>Chamado antes de cada pedido de ataque; devolve true se o pedido foi tratado (ex.: no ar → pique).</summary>
        public Func<bool> attackOverride;

        public enum State { Idle, Windup, Active, Recovery, Guard, Recoil, Plunge }
        public State Current => state;
        public bool IsAttacking => state == State.Windup || state == State.Active || state == State.Recovery || state == State.Plunge;
        /// <summary>A preparar ou a descer no golpe a pique (ainda não cravou).</summary>
        public bool IsPlunging => state == State.Plunge && plungePhase < 2;
        public bool IsGuarding => state == State.Guard;
        public bool IsRecoiling => state == State.Recoil;
        public int ComboIndex => swingIndex;
        public bool InParryWindow => IsGuarding && parryArmed && Time.time - guardSince <= parryWindow;
        /// <summary>Segundos até a lâmina chegar (estimativa para a AI reagir): 0 se não está a atacar.</summary>
        public float TimeToImpact
        {
            get
            {
                Swing sw = combo[Mathf.Clamp(swingIndex, 0, combo.Length - 1)];
                float k = Mathf.Max(0.1f, speedScale);
                if (state == State.Plunge) return plungePhase == 0 ? Mathf.Max(0f, plunge.windup - stateTime) + 0.15f : plungePhase == 1 ? 0.1f : 0f;
                if (state == State.Windup) return (sw.windup - stateTime + sw.active * 0.4f) / k;
                if (state == State.Active) return Mathf.Max(0f, sw.active * 0.4f - stateTime) / k;
                return 0f;
            }
        }
        /// <summary>Ponta e base da lâmina no mundo (para clashes e efeitos).</summary>
        public Vector3 BladeBase => sword != null ? sword.TransformPoint(0f, bladeStart, 0f) : transform.position;
        public Vector3 BladeTip => sword != null ? sword.TransformPoint(0f, bladeTip, 0f) : transform.position;
        public Vector3 BladeMid => Vector3.Lerp(BladeBase, BladeTip, 0.55f);
        /// <summary>Um golpe acertou (o alvo e o ponto) — para efeitos de impacto.</summary>
        public static event Action<SwordWielder, Combatant, Vector3> AnyBladeHit;
        /// <summary>Resultado de um golpe contra uma guarda: (atacante, defensor, foi parry).</summary>
        public static event Action<SwordWielder, SwordWielder, bool> AnyGuarded;
        public static event Action<SwordWielder, SwordWielder> AnyClash;

        static readonly Color ParryColor = new Color(1f, 0.84f, 0.42f);
        static readonly Color ClashColor = new Color(0.78f, 0.88f, 1f);
        static readonly Color BreakColor = new Color(1f, 0.45f, 0.32f);

        State state;
        int swingIndex;
        bool queued;
        float stateTime, idleSince = -9f;
        float guardSince, guardReleasedAt = -9f, recoilFor, shake;
        bool parryArmed;
        int plungePhase;   // 0 erguer, 1 descer, 2 cravada
        float stuckFor;
        Combatant self;
        CharacterController controller;
        LockOn lockOn;
        readonly HashSet<Combatant> struck = new HashSet<Combatant>();
        readonly Vector3[] lastBlade = new Vector3[6];
        bool hasLastBlade;
        float lastU;

        struct Pose { public Vector3 pos; public Quaternion rot; }
        Pose shown; // pose atual (local ao corpo), para misturar de forma contínua entre estados
        bool hasShown;

        void Awake()
        {
            self = GetComponent<Combatant>();
            controller = GetComponent<CharacterController>();
            lockOn = GetComponent<LockOn>();
        }

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        public void RequestAttack()
        {
            if (attackOverride != null && attackOverride()) return;
            if (busy || self != null && self.IsStaggered) return;
            switch (state)
            {
                case State.Idle:
                case State.Guard: // atacar larga a guarda
                    if (state == State.Guard) guardReleasedAt = Time.time;
                    // Encadeia se o último golpe acabou há pouco; senão recomeça o combo.
                    StartSwing(Time.time - idleSince < 0.25f && swingIndex < combo.Length - 1 ? swingIndex + 1 : 0);
                    break;
                case State.Windup:
                case State.Active:
                case State.Recovery:
                    if (swingIndex < combo.Length - 1) queued = true;
                    break;
            }
        }

        // ------------------------------------------------------------------ Golpe a pique (o CapsulePlayer comanda as fases)

        public void PlungeRaise()
        {
            state = State.Plunge;
            plungePhase = 0;
            stateTime = 0f;
            queued = false;
            struck.Clear();
            hasLastBlade = false;
        }

        public void PlungeDive()
        {
            if (state != State.Plunge) return;
            plungePhase = 1;
            hasLastBlade = false;
        }

        /// <summary>Cravou no chão: fica presa uns instantes e depois volta a idle.</summary>
        public void PlungeStuck(float seconds)
        {
            if (state != State.Plunge) return;
            plungePhase = 2;
            stateTime = 0f;
            stuckFor = seconds;
        }

        /// <summary>Cancela o golpe em curso (sem recuo).</summary>
        public void Interrupt()
        {
            if (IsAttacking) { state = State.Idle; idleSince = -9f; }
            queued = false;
        }

        /// <summary>Ressalto: a espada é atirada para trás e durante este tempo não ataca nem guarda.</summary>
        public void Recoil(float seconds)
        {
            state = State.Recoil;
            stateTime = 0f;
            recoilFor = seconds;
            queued = false;
            idleSince = -9f;
        }

        void StartSwing(int index)
        {
            swingIndex = index;
            state = State.Windup;
            stateTime = 0f;
            queued = false;
            struck.Clear();
            FaceTarget();
        }

        void FaceTarget()
        {
            Transform best = preferredTarget;
            if (best == null && lockOn != null && lockOn.Current != null) best = lockOn.Current.transform;
            if (best == null)
            {
                float bestD = autoAimRange;
                foreach (Combatant c in Combatant.All)
                {
                    if (c == self) continue;
                    Vector3 to = c.transform.position - transform.position; to.y = 0f;
                    if (to.magnitude < bestD && Vector3.Angle(transform.forward, to) < 75f) { bestD = to.magnitude; best = c.transform; }
                }
            }
            if (best == null) return;
            Vector3 d = best.position - transform.position; d.y = 0f;
            if (d.sqrMagnitude > 0.01f && d.magnitude < autoAimRange * 2f) transform.rotation = Quaternion.LookRotation(d);
        }

        void Update()
        {
            if (playerControlled)
            {
                // Teclas configuráveis (GameInput): por omissão, botão esquerdo ataca e F guarda.
                if (GameInput.GetDown(GameAction.Attack) && !GameHud.PointerOverUI) RequestAttack();
                guardHeld = GameInput.Get(GameAction.Guard);
            }
            if (busy) guardHeld = false;
            if (sword == null) return;

            bool staggered = self != null && self.IsStaggered;
            if (staggered && state != State.Idle) { state = State.Idle; idleSince = -9f; }

            float dt = Time.deltaTime * (IsAttacking ? Mathf.Max(0.1f, speedScale) : 1f);
            Swing sw = combo[Mathf.Clamp(swingIndex, 0, combo.Length - 1)];
            stateTime += dt;
            shake = Mathf.MoveTowards(shake, 0f, Time.deltaTime * 6f);
            float prevU = lastU;
            switch (state)
            {
                case State.Windup:
                    if (stateTime >= sw.windup) { state = State.Active; stateTime -= sw.windup; }
                    break;
                case State.Active:
                    if (controller != null) controller.Move(transform.forward * (sw.lunge / sw.active) * dt);
                    if (stateTime >= sw.active) { state = State.Recovery; stateTime -= sw.active; }
                    break;
                case State.Recovery:
                    // O golpe seguinte entra a meio da recuperação, se foi pedido.
                    if (queued && stateTime >= sw.recovery * 0.35f) { StartSwing(swingIndex + 1); break; }
                    // A guarda também pode cortar o fim da recuperação.
                    if (guardHeld && stateTime >= sw.recovery * 0.5f) { EnterGuard(); break; }
                    if (stateTime >= sw.recovery) { state = State.Idle; idleSince = Time.time; }
                    break;
                case State.Guard:
                    if (!guardHeld || staggered) { state = State.Idle; guardReleasedAt = Time.time; }
                    break;
                case State.Recoil:
                    if (stateTime >= recoilFor) { state = State.Idle; idleSince = -9f; }
                    break;
                case State.Plunge:
                    if (plungePhase == 2 && stateTime >= stuckFor) { state = State.Idle; idleSince = -9f; }
                    break;
            }
            if (state == State.Idle && guardHeld && !staggered) EnterGuard();

            Pose target = TargetPose(out float u);
            // Nos golpes a pose é a do arco (exata); nos outros estados, aproxima-se suavemente.
            if (!hasShown || state == State.Active || state == State.Plunge && plungePhase == 1) shown = target;
            else
            {
                float rate = state == State.Idle ? 10f : state == State.Recoil ? 22f : state == State.Plunge ? 34f : 28f;
                float k = 1f - Mathf.Exp(-rate * Time.deltaTime);
                shown.pos = Vector3.Lerp(shown.pos, target.pos, k);
                shown.rot = Quaternion.Slerp(shown.rot, target.rot, k);
            }
            hasShown = true;
            // Golpe na guarda: a espada estremece para trás.
            Vector3 jolt = new Vector3(Mathf.Sin(Time.time * 90f) * 0.015f, 0f, -0.09f) * shake;
            sword.position = transform.TransformPoint(shown.pos + jolt);
            sword.rotation = transform.rotation * shown.rot;

            if (state == State.Active) Sweep(prevU, u);
            else if (state == State.Plunge && plungePhase == 1) SweepLinear();
            lastU = state == State.Active ? u : 0f;
        }

        void EnterGuard()
        {
            state = State.Guard;
            stateTime = 0f;
            guardSince = Time.time;
            // Martelar a tecla não serve: só há janela de parry se a guarda esteve em baixo algum tempo.
            parryArmed = Time.time - guardReleasedAt > parryLockout;
            queued = false;
        }

        // ------------------------------------------------------------------ Poses

        Pose TargetPose(out float u)
        {
            u = 0f;
            Swing sw = combo[Mathf.Clamp(swingIndex, 0, combo.Length - 1)];
            switch (state)
            {
                case State.Windup:
                {
                    // Anticipação: recua para lá do início do arco.
                    float k = Ease(stateTime / sw.windup);
                    return Arc(swingIndex, Mathf.Lerp(-0.08f, 0f, k));
                }
                case State.Active:
                    // Arranca depressa e trava no fim (o peso da lâmina).
                    u = 1f - Mathf.Pow(1f - Mathf.Clamp01(stateTime / sw.active), 2.2f);
                    return Arc(swingIndex, u);
                case State.Recovery:
                    return Arc(swingIndex, 1.04f);
                case State.Guard:
                    return GuardPose();
                case State.Recoil:
                    return RecoilPose();
                case State.Plunge:
                    // Erguida a direito sobre a cabeça → a direito para baixo à frente do peito → cravada no chão.
                    if (plungePhase == 0) return Make(new Vector3(0.05f, 1.78f, 0.05f), new Vector3(0f, 1f, -0.12f), Vector3.forward);
                    if (plungePhase == 1) return Make(new Vector3(0.02f, 1.0f, bodyRadius + 0.12f), new Vector3(0f, -1f, 0.1f), Vector3.forward);
                    return Make(new Vector3(0.02f, 0.62f, bodyRadius + 0.18f), new Vector3(0f, -1f, 0.05f), Vector3.forward);
                default:
                    if (self != null && self.IsStaggered) return StaggerPose();
                    return IdlePose();
            }
        }

        static float Ease(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }

        Pose IdlePose()
        {
            // Em baixo ao lado, a apontar para a frente e para baixo; respira um pouco.
            float breathe = Mathf.Sin(Time.time * 2.1f) * 0.012f;
            Vector3 dir = new Vector3(0.12f, -0.38f, 1f).normalized;
            return Make(new Vector3(bodyRadius + 0.06f, 0.92f + breathe, 0.16f), dir, Vector3.up);
        }

        Pose GuardPose()
        {
            // Atravessada à frente do peito, a subir para a esquerda, gume virado ao adversário.
            Vector3 dir = new Vector3(-1f, 0.32f, 0.1f).normalized;
            return Make(new Vector3(0.1f + bodyRadius * 0.4f, 1.22f, bodyRadius + 0.24f), dir, Vector3.forward);
        }

        Pose RecoilPose()
        {
            // Ressalto: braço atirado para cima e para trás, lâmina a apontar para trás.
            Vector3 dir = new Vector3(0.35f, 0.9f, -0.55f).normalized;
            return Make(new Vector3(bodyRadius + 0.1f, 1.4f, -0.02f), dir, Vector3.forward);
        }

        Pose StaggerPose()
        {
            // Braço atirado para trás e para fora, lâmina solta para cima.
            Vector3 dir = new Vector3(0.55f, 0.75f, -0.35f).normalized;
            return Make(new Vector3(bodyRadius + 0.15f, 1.2f, -0.12f), dir, Vector3.forward);
        }

        /// <summary>Pose ao longo do arco do golpe i (u=0 início, u=1 fim; fora disso, extrapola).</summary>
        Pose Arc(int i, float u)
        {
            float arm = 0.48f + (bodyRadius - 0.3f) * 1.5f; // corpos mais largos, braço mais comprido
            switch (i)
            {
                case 0: // corte horizontal, da direita para a esquerda, ligeiramente a descer
                {
                    float a = Mathf.LerpUnclamped(-35f, 205f, u) * Mathf.Deg2Rad;
                    Vector3 d = new Vector3(Mathf.Cos(a), Mathf.LerpUnclamped(0.05f, -0.2f, u), Mathf.Sin(a)).normalized;
                    Vector3 t = new Vector3(-Mathf.Sin(a), 0f, Mathf.Cos(a));
                    return Make(new Vector3(0.12f, 1.22f, 0.05f) + d * arm, d, t);
                }
                case 1: // corte de volta, da esquerda para a direita, a subir
                {
                    float a = Mathf.LerpUnclamped(200f, -30f, u) * Mathf.Deg2Rad;
                    Vector3 d = new Vector3(Mathf.Cos(a), Mathf.LerpUnclamped(-0.45f, 0.4f, u), Mathf.Sin(a)).normalized;
                    Vector3 t = new Vector3(Mathf.Sin(a), 0.35f, -Mathf.Cos(a));
                    return Make(new Vector3(0.05f, 1.15f, 0.05f) + d * (arm + 0.02f), d, t);
                }
                default: // golpe de cima: de trás da cabeça até à frente, em baixo
                {
                    float a = Mathf.LerpUnclamped(-45f, 125f, u) * Mathf.Deg2Rad;
                    Vector3 d = new Vector3(0.12f, Mathf.Cos(a), Mathf.Sin(a)).normalized;
                    Vector3 t = new Vector3(0f, -Mathf.Sin(a), Mathf.Cos(a));
                    return Make(new Vector3(0.14f, 1.38f, 0.05f) + d * (arm + 0.02f), d, t);
                }
            }
        }

        /// <summary>Lâmina ao longo de dir (Y local) com o gume virado para edge (X local).</summary>
        static Pose Make(Vector3 hand, Vector3 dir, Vector3 edge)
        {
            Vector3 y = dir.normalized;
            Vector3 x = Vector3.ProjectOnPlane(edge, y);
            if (x.sqrMagnitude < 1e-4f) x = Vector3.ProjectOnPlane(Vector3.right, y);
            x.Normalize();
            return new Pose { pos = hand, rot = Quaternion.LookRotation(Vector3.Cross(x, y), y) };
        }

        // ------------------------------------------------------------------ Golpe

        // A lâmina roda muito entre frames: uma reta entre as posições cortaria por dentro do arco e falharia
        // o alvo. Por isso a varredura segue o próprio arco, em subpassos. Em cada subpasso vê primeiro se
        // cruza outra lâmina a golpear (clash) e só depois os corpos.
        void Sweep(float u0, float u1)
        {
            int n = lastBlade.Length;
            int steps = Mathf.Clamp(Mathf.CeilToInt(Mathf.Abs(u1 - u0) * 40f), 1, 16);
            for (int s = 1; s <= steps; s++)
            {
                Pose p = Arc(swingIndex, Mathf.Lerp(u0, u1, s / (float)steps));
                Vector3 hand = transform.TransformPoint(p.pos);
                Quaternion rot = transform.rotation * p.rot;
                Vector3 b0 = hand + rot * new Vector3(0f, bladeStart, 0f);
                Vector3 b1 = hand + rot * new Vector3(0f, bladeTip, 0f);

                foreach (SwordWielder other in All)
                {
                    if (other == this || !other.Swinging) continue;
                    if (SegmentDistance(b0, b1, other.BladeBase, other.BladeTip, out Vector3 at) <= clashRadius)
                    {
                        Clash(other, at);
                        return;
                    }
                }

                for (int i = 0; i < n; i++)
                {
                    Vector3 q0 = Vector3.Lerp(b0, b1, i / (n - 1f));
                    lastBlade[i] = q0;
                    foreach (Combatant c in Combatant.All)
                    {
                        if (c == self || struck.Contains(c)) continue;
                        Vector3 q = c.ClosestPoint(q0);
                        if ((q - q0).sqrMagnitude <= hitRadius * hitRadius && Strike(c, q, rot * Vector3.right)) return;
                        if (state != State.Active) return; // o golpe acabou (ex.: ressaltou)
                    }
                }
            }
        }

        /// <summary>A lâmina está a cortar (fase ativa de um golpe ou a descer no pique): conta para clashes.</summary>
        bool Swinging => state == State.Active || state == State.Plunge && plungePhase == 1;

        // Pique: a lâmina só desce (não roda), por isso uma reta entre o frame anterior e este chega.
        void SweepLinear()
        {
            int n = lastBlade.Length;
            var now = new Vector3[n];
            for (int i = 0; i < n; i++) now[i] = Vector3.Lerp(BladeBase, BladeTip, i / (n - 1f));
            int steps = hasLastBlade ? 4 : 1;
            for (int s = 1; s <= steps; s++)
            {
                float f = s / (float)steps;
                Vector3 b0 = hasLastBlade ? Vector3.Lerp(lastBlade[0], now[0], f) : now[0];
                Vector3 b1 = hasLastBlade ? Vector3.Lerp(lastBlade[n - 1], now[n - 1], f) : now[n - 1];
                foreach (SwordWielder other in All)
                {
                    if (other == this || !other.Swinging) continue;
                    if (SegmentDistance(b0, b1, other.BladeBase, other.BladeTip, out Vector3 at) <= clashRadius)
                    {
                        Clash(other, at);
                        return;
                    }
                }
                for (int i = 0; i < n; i++)
                {
                    Vector3 q0 = Vector3.Lerp(b0, b1, i / (n - 1f));
                    foreach (Combatant c in Combatant.All)
                    {
                        if (c == self || struck.Contains(c)) continue;
                        Vector3 q = c.ClosestPoint(q0);
                        if ((q - q0).sqrMagnitude <= hitRadius * hitRadius && Strike(c, q, Vector3.down)) return;
                        if (state != State.Plunge) return;
                    }
                }
            }
            Array.Copy(now, lastBlade, n);
            hasLastBlade = true;
        }

        Swing CurrentSwing => state == State.Plunge ? plunge : combo[Mathf.Clamp(swingIndex, 0, combo.Length - 1)];
        bool Heavy => state == State.Plunge || swingIndex == combo.Length - 1;

        /// <summary>Acerta num corpo. Devolve true se o golpe parou (guarda ou parry).</summary>
        /// <param name="run">Direção em que a lâmina corre (o gume vai à frente).</param>
        bool Strike(Combatant c, Vector3 point, Vector3 run)
        {
            struck.Add(c);
            if (c.IsInvulnerable) return false; // rolou por baixo do golpe
            Swing sw = CurrentSwing;
            Vector3 away = c.transform.position - transform.position; away.y = 0f;
            if (away.sqrMagnitude < 1e-4f) away = transform.forward;

            var guard = c.GetComponent<SwordWielder>();
            if (guard != null && guard.Guards(transform.position))
            {
                if (guard.InParryWindow) guard.Parried(this);
                else guard.Blocked(this, sw);
                return true;
            }

            bool crit = UnityEngine.Random.value < critChance;
            c.TakeHit(new HitInfo
            {
                damage = sw.damage * damageScale * (crit ? critDamage : 1f), critical = crit,
                element = AbilityElement.Neutral, stagger = sw.stagger * staggerScale,
                knockback = away.normalized * sw.push + Vector3.up * sw.lift,
                point = point, source = transform
            });
            // Quem leva um golpe a meio do seu próprio golpe perde-o.
            if (guard != null && guard.IsAttacking && c.IsStaggered) guard.Interrupt();
            HitStop.Do(sw.hitStop);
            // Faíscas a sair na direção em que a lâmina corre (e um pouco para fora do alvo).
            SwordSparks.Burst(point, run.normalized + away.normalized * 0.5f, Heavy ? 22 : 12, sparkColor,
                              speed: 8f, spread: 0.5f, flash: Heavy ? 0.28f : 0.16f);
            AnyBladeHit?.Invoke(this, c, point);
            return false;
        }

        bool Guards(Vector3 from)
        {
            if (!IsGuarding || (self != null && self.IsStaggered)) return false;
            Vector3 to = from - transform.position; to.y = 0f;
            return Vector3.Angle(transform.forward, to) <= guardArc;
        }

        // Este corpo fez parry ao golpe de attacker.
        void Parried(SwordWielder attacker)
        {
            Vector3 at = BladeMid;
            Vector3 toAttacker = attacker.transform.position - transform.position; toAttacker.y = 0f;
            attacker.Interrupt();
            if (attacker.self != null)
            {
                attacker.self.Stagger(parriedStagger);
                attacker.self.Knock(toAttacker.normalized * 3f);
            }
            shake = 1f;
            SwordSparks.Burst(at, toAttacker.normalized + Vector3.up * 0.4f, 34, ParryColor, speed: 10f, spread: 0.75f, flash: 0.42f);
            HitStop.Do(0.12f);
            HitStop.Then(0.45f, 0.3f);
            Combat.Callout(at + Vector3.up * 0.55f, "PARRY", ParryColor, 34f);
            AnyGuarded?.Invoke(attacker, this, true);
        }

        // Este corpo bloqueou o golpe de attacker: leva uma fração do dano e da postura; o atacante ressalta.
        void Blocked(SwordWielder attacker, Swing sw)
        {
            Vector3 at = BladeMid;
            Vector3 fromAttacker = transform.position - attacker.transform.position; fromAttacker.y = 0f;
            bool wasStaggered = self != null && self.IsStaggered;
            if (self != null)
                self.TakeHit(new HitInfo
                {
                    damage = sw.damage * attacker.damageScale * (1f - blockReduction), element = AbilityElement.Neutral,
                    stagger = sw.stagger * attacker.staggerScale * 0.6f,
                    knockback = fromAttacker.normalized * sw.push * 0.45f, point = at, source = attacker.transform
                });
            if (self != null && !wasStaggered && self.IsStaggered)
            {
                Combat.Callout(at + Vector3.up * 0.55f, "GUARD BREAK", BreakColor, 28f);
                state = State.Idle;
            }
            attacker.Recoil(0.22f);
            shake = 1f;
            if (self == null || !self.IsStaggered) Combat.Callout(at + Vector3.up * 0.45f, "BLOCK", new Color(0.78f, 0.82f, 0.9f), 18f);
            SwordSparks.Burst(at, -fromAttacker.normalized + Vector3.up * 0.3f, 14, sparkColor, speed: 7f, spread: 0.6f, flash: 0.18f);
            HitStop.Do(0.05f);
            AnyGuarded?.Invoke(attacker, this, false);
        }

        // As duas lâminas cruzaram-se a meio dos golpes: anulam-se e ambos recuam.
        void Clash(SwordWielder other, Vector3 at)
        {
            Vector3 apart = transform.position - other.transform.position; apart.y = 0f;
            apart = apart.sqrMagnitude > 1e-4f ? apart.normalized : -transform.forward;
            Recoil(0.42f);
            other.Recoil(0.42f);
            shake = other.shake = 1f;
            if (self != null) self.Knock(apart * 3.5f);
            if (other.self != null) other.self.Knock(-apart * 3.5f);
            SwordSparks.Burst(at, Vector3.up + apart * 0.6f, 22, ClashColor, speed: 10f, spread: 0.8f, flash: 0.5f);
            SwordSparks.Burst(at, Vector3.up - apart * 0.6f, 22, sparkColor, speed: 10f, spread: 0.8f, flash: 0f);
            HitStop.Do(0.14f);
            HitStop.Then(0.25f, 0.45f);
            Combat.Callout(at + Vector3.up * 0.5f, "CLASH", ClashColor, 32f);
            AnyClash?.Invoke(this, other);
        }

        /// <summary>Distância mínima entre os segmentos p0–p1 e q0–q1; at = ponto médio entre os mais próximos.</summary>
        static float SegmentDistance(Vector3 p0, Vector3 p1, Vector3 q0, Vector3 q1, out Vector3 at)
        {
            Vector3 d1 = p1 - p0, d2 = q1 - q0, r = p0 - q0;
            float a = Vector3.Dot(d1, d1), e = Vector3.Dot(d2, d2), f = Vector3.Dot(d2, r);
            float s, t;
            if (a <= 1e-6f && e <= 1e-6f) { s = t = 0f; }
            else if (a <= 1e-6f) { s = 0f; t = Mathf.Clamp01(f / e); }
            else
            {
                float c = Vector3.Dot(d1, r);
                if (e <= 1e-6f) { t = 0f; s = Mathf.Clamp01(-c / a); }
                else
                {
                    float b = Vector3.Dot(d1, d2), denom = a * e - b * b;
                    s = denom > 1e-6f ? Mathf.Clamp01((b * f - c * e) / denom) : 0f;
                    t = (b * s + f) / e;
                    if (t < 0f) { t = 0f; s = Mathf.Clamp01(-c / a); }
                    else if (t > 1f) { t = 1f; s = Mathf.Clamp01((b - c) / a); }
                }
            }
            Vector3 c1 = p0 + d1 * s, c2 = q0 + d2 * t;
            at = (c1 + c2) * 0.5f;
            return Vector3.Distance(c1, c2);
        }
    }
}
