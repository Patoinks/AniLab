using UnityEngine;

namespace AnimLab.KonoSuba
{
    /// <summary>
    /// Player de teste: cápsula com dois pés procedurais.
    /// Controlo misto: WASD relativo à câmara (Genshin/Roblox/Smite) ou manter o botão esquerdo
    /// para andar até ao cursor (PoE/LoL). Shift corre.
    /// Os pés dão passos alternados: quando um pé fica para trás, levanta-se num arco e assenta
    /// à frente; ao assentar cria uma onda na água.
    /// Salto (Espaço), roll (Q, com invulnerabilidade a meio), salto longo (Espaço durante o roll: sai no fim)
    /// e golpe a pique (ataque no ar: ergue a espada, mergulha a pique para o alvo e crava-a no chão,
    /// com onda de choque em área).
    /// As teclas vêm do GameInput (configuráveis no menu Settings).
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class CapsulePlayer : MonoBehaviour, AnimLab.Netcode.Unity.INetMovementView
    {
        [Header("Movimento")]
        public float walkSpeed = 3.5f;
        public float runSpeed = 6.5f;
        public float acceleration = 20f;
        public float turnSpeed = 900f;
        public float gravity = -20f;
        public bool clickToMove = true;
        [Tooltip("Multiplicador da velocidade (o CharacterStats escreve o Move Speed aqui).")]
        public float speedScale = 1f;

        // Em rede só anima; a simulação é a única dona do movimento.
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
                    Vector3 oldPosition = transform.position, oldVelocity = velocity;
                    Quaternion oldRotation = transform.rotation;
                    Motion oldMotion = motion; float oldTime = motionTime;
                    float oldStance = stance, oldSquash = landSquash;
                    Vector3 oldBodyPosition = body != null ? body.localPosition : Vector3.zero;
                    Quaternion oldBodyRotation = body != null ? body.localRotation : Quaternion.identity;
                    bool oldLong = longJump, oldDive = dived;
                    restoreExternal = () =>
                    {
                        transform.SetPositionAndRotation(oldPosition, oldRotation);
                        velocity = oldVelocity; motion = oldMotion; motionTime = oldTime; longJump = oldLong; dived = oldDive;
                        stance = oldStance; landSquash = oldSquash;
                        if (body != null) { body.localPosition = oldBodyPosition; body.localRotation = oldBodyRotation; }
                        foreach (Foot foot in new[] { left, right })
                            if (foot?.t != null) { foot.planted = FootHome(foot, 0f); foot.t.position = foot.planted; foot.stepping = false; }
                    };
                }
                else { restoreExternal?.Invoke(); restoreExternal = null; }
                externalDrive = value;
            }
        }
        bool externalRunning, externalGrounded, externalFrozen;

        public void CaptureNetConfig(ref AnimLab.Netcode.Sim.PlayerConfig c)
        {
            c.Walk = AnimLab.Netcode.Unity.ViewConvert.Fixed(walkSpeed); c.Run = AnimLab.Netcode.Unity.ViewConvert.Fixed(runSpeed);
            c.Acceleration = AnimLab.Netcode.Unity.ViewConvert.Fixed(acceleration); c.Turn = AnimLab.Netcode.Sim.Fix.Degrees(AnimLab.Netcode.Unity.ViewConvert.Fixed(turnSpeed));
            c.Gravity = AnimLab.Netcode.Unity.ViewConvert.Fixed(gravity); c.SpeedScale = AnimLab.Netcode.Unity.ViewConvert.Fixed(speedScale);
            c.JumpHeight = AnimLab.Netcode.Unity.ViewConvert.Fixed(jumpHeight); c.AirControl = AnimLab.Netcode.Unity.ViewConvert.Fixed(airControl);
            c.RollDistance = AnimLab.Netcode.Unity.ViewConvert.Fixed(rollDistance); c.RollDuration = AnimLab.Netcode.Unity.ViewConvert.Fixed(rollDuration);
            c.RollCooldown = AnimLab.Netcode.Unity.ViewConvert.Fixed(rollCooldown);
            c.RollInvulFrom = AnimLab.Netcode.Unity.ViewConvert.Fixed(rollInvulnerable.x); c.RollInvulTo = AnimLab.Netcode.Unity.ViewConvert.Fixed(rollInvulnerable.y);
            c.LongJumpSpeed = AnimLab.Netcode.Unity.ViewConvert.Fixed(longJumpSpeed); c.LongJumpHeight = AnimLab.Netcode.Unity.ViewConvert.Fixed(longJumpHeight);
            c.PlungeRaise = AnimLab.Netcode.Unity.ViewConvert.Fixed(plungeRaise); c.PlungeSpeed = AnimLab.Netcode.Unity.ViewConvert.Fixed(plungeSpeed);
            c.PlungeHoming = AnimLab.Netcode.Unity.ViewConvert.Fixed(plungeHoming); c.PlungeMinHeight = AnimLab.Netcode.Unity.ViewConvert.Fixed(plungeMinHeight);
            c.PlungeRadius = AnimLab.Netcode.Unity.ViewConvert.Fixed(plungeRadius); c.PlungeDamage = AnimLab.Netcode.Unity.ViewConvert.Fixed(plungeDamage);
            c.PlungeRecovery = AnimLab.Netcode.Unity.ViewConvert.Fixed(plungeRecovery);
            var cc = GetComponent<CharacterController>();
            if (cc != null) { c.Radius = AnimLab.Netcode.Unity.ViewConvert.Fixed(cc.radius); c.Height = AnimLab.Netcode.Unity.ViewConvert.Fixed(cc.height); c.CenterY = AnimLab.Netcode.Unity.ViewConvert.Fixed(cc.center.y); }
        }

        public AnimLab.Netcode.Sim.PlayerInput SampleNetInput()
        {
            if (GameInput.Suspended) return AnimLab.Netcode.Unity.InputSampling.Quantize(Vector3.zero, transform.eulerAngles.y, 0);
            if (cam == null) cam = Camera.main;
            var rig = cam != null ? cam.GetComponent<Metin2Camera>() : null;
            Vector3 wish = rig != null ? rig.ReadMovement(transform).wish : ReadInput();
            Vector3 look = lockOn != null && lockOn.Current != null ? lockOn.Current.transform.position - transform.position : wish;
            float yaw = look.sqrMagnitude > 0.001f ? Mathf.Atan2(look.x, look.z) * Mathf.Rad2Deg : transform.eulerAngles.y;
            var buttons = (GameInput.GetDown(GameAction.Attack) && !GameHud.PointerOverUI ? AnimLab.Netcode.Sim.Buttons.Attack : 0) |
                (GameInput.Get(GameAction.Guard) ? AnimLab.Netcode.Sim.Buttons.Guard : 0) |
                (GameInput.GetDown(GameAction.Jump) ? AnimLab.Netcode.Sim.Buttons.Jump : 0) |
                (GameInput.GetDown(GameAction.Roll) ? AnimLab.Netcode.Sim.Buttons.Roll : 0) |
                (GameInput.Get(GameAction.Run) ? AnimLab.Netcode.Sim.Buttons.Run : 0);
            var input = AnimLab.Netcode.Unity.InputSampling.Quantize(wish, yaw, buttons, lockOn != null && lockOn.Current != null);
            // Alvo do lock-on como índice do lutador; teclas 1–9 da hotbar; ponto de mira no chão debaixo do rato.
            int target = lockOn != null && lockOn.Current != null ? AnimLab.Netcode.Unity.NetMatch.FighterIndexOf(lockOn.Current.gameObject) : -1;
            if (target >= 0) input.TargetSlot = (byte)(target + 1);
            // Vai a habilidade que está no espaço (não o número do espaço): a hotbar pode mudar a meio da partida.
            var bar = GetComponent<AbilityBar>();
            for (int i = 0; i < 9 && bar != null; i++)
            {
                var key = (GameAction)((int)GameAction.Ability1 + i);
                byte id = (byte)AbilityBar.IdOf(bar.At(i));
                if (id == 0) continue;
                if (GameInput.GetDown(key) && input.Cast == 0) input.Cast = id;
                if (GameInput.Get(key) && input.Held == 0) input.Held = id;
            }
            Vector3 aim = transform.position + transform.forward * 10f;
            if (cam != null && new Plane(Vector3.up, Vector3.zero).Raycast(cam.ScreenPointToRay(Input.mousePosition), out float d))
                aim = cam.ScreenPointToRay(Input.mousePosition).GetPoint(d);
            input.AimX = (short)Mathf.Clamp(Mathf.RoundToInt(aim.x * 100f), short.MinValue, short.MaxValue);
            input.AimZ = (short)Mathf.Clamp(Mathf.RoundToInt(aim.z * 100f), short.MinValue, short.MaxValue);
            return input;
        }

        public void ApplyNetState(in AnimLab.Netcode.Sim.PlayerState state, Vector3 position, float yaw)
        {
            transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            velocity = AnimLab.Netcode.Unity.ViewConvert.Vector(state.Velocity);
            slide = AnimLab.Netcode.Unity.ViewConvert.Vector(state.Knock);
            motion = (Motion)state.Motion;
            motionTime = AnimLab.Netcode.Unity.ViewConvert.Number(state.MotionTime);
            longJump = state.LongJump;
            dived = state.Dived;
            externalGrounded = state.Position.Y <= AnimLab.Netcode.Sim.Fix.Epsilon;
            externalRunning = velocity.magnitude > walkSpeed * speedScale;
            externalFrozen = state.HitStop > 0;
        }

        [Header("Salto, roll e golpe a pique")]
        public float jumpHeight = 1.1f;
        [Tooltip("Controlo no ar (fração da aceleração no chão).")]
        public float airControl = 0.35f;
        public float rollDistance = 4f, rollDuration = 0.42f, rollCooldown = 0.5f;
        [Tooltip("Invulnerabilidade durante o roll: de / até (s desde o início).")]
        public Vector2 rollInvulnerable = new Vector2(0.04f, 0.32f);
        public float longJumpSpeed = 9f, longJumpHeight = 1.3f;
        [Tooltip("Golpe a pique: preparação no ar (s), velocidade da queda, correção horizontal máxima para o alvo (m/s).")]
        public float plungeRaise = 0.16f, plungeSpeed = 26f, plungeHoming = 7f;
        [Tooltip("Altura mínima ao chão para o ataque no ar virar golpe a pique (m).")]
        public float plungeMinHeight = 0.5f;
        [Tooltip("Onda de choque ao cravar: raio (m) e dano.")]
        public float plungeRadius = 2.6f, plungeDamage = 22f;
        [Tooltip("Tempo preso no chão com a espada cravada (s).")]
        public float plungeRecovery = 0.45f;

        [Header("AI")]
        [Tooltip("Controlado por uma AI (DuelistAI): ignora teclado e rato e usa os campos abaixo.")]
        public bool aiControlled;
        [HideInInspector] public Vector3 aiWish;
        [HideInInspector] public bool aiRun;
        /// <summary>Para onde a AI quer olhar (null = para onde anda).</summary>
        public Vector3? aiLookAt;

        [Header("Pés")]
        public Transform leftFoot;
        public Transform rightFoot;
        [Tooltip("Afastamento lateral dos pés em relação ao centro (m).")]
        public float footSpacing = 0.17f;
        [Tooltip("Distância que um pé fica para trás antes de dar um passo (m).")]
        public float stepThreshold = 0.35f;
        public float stepDuration = 0.18f;
        public float stepHeight = 0.12f;
        [Tooltip("Quanto o pé se adianta na direção do movimento ao assentar.")]
        public float stepLead = 0.12f;

        [Header("Corpo")]
        public Transform body;
        public float bobAmount = 0.04f;

        [Header("Água")]
        public float walkRippleStrength = 0.35f;
        public float runRippleStrength = 0.6f;

        CharacterController controller;
        Combatant combatant;
        SwordWielder sword;
        LockOn lockOn;
        Camera cam;
        Vector3 velocity;
        /// <summary>Knockback horizontal (encontrões): os pés andam com ele, para o corpo não deslizar.</summary>
        Vector3 slide;
        Vector3 Moving => new Vector3(velocity.x + slide.x, 0f, velocity.z + slide.z);
        float verticalVelocity;
        Vector3 moveTarget;
        bool hasMoveTarget;
        Vector3 bodyBase;

        enum Motion { Ground, Air, Roll, Plunge }
        Motion motion;
        float motionTime, rollReadyAt, landLagUntil, landSquash, stance;
        Vector3 rollDir;
        bool rollJumpQueued, longJump, dived;
        Transform plungeTarget;
        float plungeGroundY;
        public bool IsAirborne => motion == Motion.Air || motion == Motion.Plunge;
        public bool IsRolling => motion == Motion.Roll;

        class Foot
        {
            public Transform t;
            public float side;
            public Vector3 planted, from, to;
            public bool stepping;
            public float progress;
        }

        Foot left, right;
        Foot lastStepped;

        void Awake()
        {
            controller = GetComponent<CharacterController>();
            combatant = GetComponent<Combatant>();
            sword = GetComponent<SwordWielder>();
            lockOn = GetComponent<LockOn>();
            if (sword != null) sword.attackOverride = AirAttack;
            cam = Camera.main;
            if (body != null) bodyBase = body.localPosition;
            left = new Foot { t = leftFoot, side = -1f };
            right = new Foot { t = rightFoot, side = 1f };
            foreach (Foot f in new[] { left, right })
            {
                if (f == null || f.t == null) continue; // (os pés não sobrevivem a uma recompilação em play mode)
                f.t.SetParent(null, true); // os pés vivem no mundo: ficam assentes enquanto o corpo avança
                f.planted = FootHome(f, 0f);
                f.t.position = f.planted;
            }
        }

        void OnDestroy()
        {
            if (left?.t != null) Destroy(left.t.gameObject);
            if (right?.t != null) Destroy(right.t.gameObject);
        }

        void Update()
        {
            if (externalDrive) { if (!externalFrozen) { UpdateFeet(externalRunning); UpdateBody(); } return; }
            if (cam == null) cam = Camera.main;
            var rig = cam != null ? cam.GetComponent<Metin2Camera>() : null;

            // Com a câmara Metin2, os controlos (WASD, clique, alvo) vêm dela.
            Vector3 wish;
            bool setFacing = false;
            float facingYaw = 0f;
            if (aiControlled)
            {
                wish = Vector3.ClampMagnitude(aiWish, 1f);
                if (aiLookAt.HasValue)
                {
                    Vector3 d = aiLookAt.Value - transform.position; d.y = 0f;
                    if (d.sqrMagnitude > 0.01f) { setFacing = true; facingYaw = Quaternion.LookRotation(d).eulerAngles.y; }
                }
            }
            else if (rig != null)
            {
                Metin2Camera.MoveCommand cmd = rig.ReadMovement(transform);
                wish = cmd.wish;
                setFacing = cmd.setFacing;
                facingYaw = cmd.facingYaw;
            }
            else wish = ReadInput();

            // Em stagger e no ressalto não anda nem roda; a atacar quase não anda e é a espada que decide para
            // onde olha; a guardar anda devagar, sem virar as costas (vira-se para o alvo, se houver).
            bool staggered = combatant != null && combatant.IsHurt; // stagger ou flinch
            bool attacking = sword != null && sword.IsAttacking;
            bool recoiling = sword != null && sword.IsRecoiling;
            bool guarding = sword != null && sword.IsGuarding;
            float moveScale = staggered || recoiling ? 0f : attacking ? 0.15f : guarding ? 0.4f : 1f;
            if (guarding && !aiControlled && !setFacing)
            {
                LockOnTarget t = lockOn != null ? lockOn.Current : null;
                Vector3 d = t != null ? t.transform.position - transform.position : Vector3.zero; d.y = 0f;
                if (d.sqrMagnitude > 0.01f) { setFacing = true; facingYaw = Quaternion.LookRotation(d).eulerAngles.y; }
            }

            if (Time.time < landLagUntil) moveScale = 0f;

            bool running = (aiControlled ? aiRun : GameInput.Get(GameAction.Run)) && !guarding;
            float maxSpeed = (running ? runSpeed : walkSpeed) * speedScale;
            float dt = Time.deltaTime;
            bool grounded = controller.isGrounded;
            bool swinging = sword != null && (sword.Current == SwordWielder.State.Windup || sword.Current == SwordWielder.State.Active);
            bool free = !staggered && !recoiling && !swinging;

            if (!aiControlled)
            {
                if (GameInput.GetDown(GameAction.Jump)) PressJump(grounded, free && !attacking && !guarding);
                if (GameInput.GetDown(GameAction.Roll)) TryRoll(wish, grounded, free);
            }
            motionTime += dt;

            switch (motion)
            {
                case Motion.Roll:
                {
                    // Arranca forte e trava (a distância total é rollDistance).
                    float t = Mathf.Clamp01(motionTime / rollDuration);
                    float v0 = rollDistance / (rollDuration * 0.65f);
                    velocity = rollDir * v0 * (1f - 0.7f * t);
                    if (combatant != null && motionTime >= rollInvulnerable.x && motionTime <= rollInvulnerable.y)
                        combatant.invulnerableUntil = Time.time + 0.05f;
                    if (t >= 1f)
                    {
                        if (rollJumpQueued) LongJump();
                        else { motion = Motion.Ground; velocity = rollDir * walkSpeed * speedScale; }
                    }
                    break;
                }
                case Motion.Plunge:
                    // Parry, bloqueio, clash ou stagger tiraram a espada do pique: cai normalmente.
                    if (sword != null && sword.Current != SwordWielder.State.Plunge) { motion = Motion.Air; break; }
                    if (motionTime < plungeRaise)
                    {
                        // Ergue a espada e quase para no ar.
                        velocity *= 1f - Mathf.Clamp01(8f * dt);
                        verticalVelocity = 1.2f;
                    }
                    else
                    {
                        if (!dived) { dived = true; if (sword != null) sword.PlungeDive(); }
                        verticalVelocity = -plungeSpeed;
                        velocity = PlungeHoming();
                    }
                    break;
                case Motion.Air:
                    // No ar mantém o embalo; o salto longo quase não se deixa desviar.
                    velocity = Vector3.MoveTowards(velocity, wish * maxSpeed * moveScale,
                                                   acceleration * (longJump ? 0.12f : airControl) * dt);
                    break;
                default:
                    velocity = Vector3.MoveTowards(velocity, wish * maxSpeed * moveScale, acceleration * dt);
                    break;
            }

            // Vira-se para o alvo quando a câmara o pede (a ir até ele / ao pé dele); senão para onde anda.
            Quaternion? look = null;
            if (motion == Motion.Roll) look = Quaternion.LookRotation(rollDir, Vector3.up);
            else if (motion == Motion.Plunge && plungeTarget != null && Flat(plungeTarget.position - transform.position).sqrMagnitude > 0.01f)
                look = Quaternion.LookRotation(Flat(plungeTarget.position - transform.position), Vector3.up);
            else if (setFacing) look = Quaternion.Euler(0f, facingYaw, 0f);
            else if (wish.sqrMagnitude > 0.001f && !guarding) look = Quaternion.LookRotation(wish, Vector3.up);
            if (look.HasValue && !staggered && !recoiling && (!attacking || motion == Motion.Plunge))
                transform.rotation = Quaternion.RotateTowards(transform.rotation, look.Value, turnSpeed * dt);

            if (motion == Motion.Ground || motion == Motion.Roll)
                verticalVelocity = grounded ? -2f : verticalVelocity + gravity * dt;
            else if (motion == Motion.Air)
                verticalVelocity += gravity * dt;
            controller.Move((velocity + Vector3.up * verticalVelocity) * dt);

            // Aterrar.
            if (controller.isGrounded && verticalVelocity <= 0f)
            {
                if (motion == Motion.Air) Landed(longJump ? 0.9f : 0.6f);
                else if (motion == Motion.Plunge && dived) PlungeImpact();
            }
            if (sword != null) sword.busy = motion == Motion.Roll || motion == Motion.Plunge || Time.time < landLagUntil;

            slide = combatant != null ? combatant.KnockVelocity : Vector3.zero;
            UpdateFeet(running);
            UpdateBody();
        }

        // ------------------------------------------------------------------ Salto, roll e golpe a pique

        /// <summary>Salto como se tivesse carregado na tecla (gravações e AI).</summary>
        public void Jump() { if (!externalDrive) PressJump(controller.isGrounded, true); }

        /// <summary>Roll como se tivesse carregado na tecla (gravações e AI).</summary>
        public void Roll(Vector3 direction) => TryRoll(direction, controller.isGrounded, true);

        static readonly float RollStamina = AnimLab.Netcode.Unity.ViewConvert.Number(AnimLab.Netcode.Sim.SwordData.RollStamina);

        void PressJump(bool grounded, bool canAct)
        {
            if (motion == Motion.Roll) { rollJumpQueued = true; return; } // sai no fim do roll, longo
            if (motion != Motion.Ground || !grounded || !canAct || Time.time < landLagUntil) return;
            verticalVelocity = Mathf.Sqrt(2f * -gravity * jumpHeight);
            motion = Motion.Air;
            motionTime = 0f;
            longJump = false;
            Splash(0.5f);
        }

        /// <summary>Roll na direção pedida (ou para a frente). Cancela a guarda e o fim de um golpe.</summary>
        public bool TryRoll(Vector3 direction, bool grounded, bool canAct)
        {
            if (externalDrive) return false;
            if (motion != Motion.Ground || !grounded || !canAct || Time.time < rollReadyAt || Time.time < landLagUntil) return false;
            if (combatant != null && combatant.IsHurt) return false;
            if (combatant != null) combatant.SpendStamina(RollStamina);
            direction.y = 0f;
            rollDir = direction.sqrMagnitude > 0.01f ? direction.normalized : transform.forward;
            transform.rotation = Quaternion.LookRotation(rollDir, Vector3.up);
            if (sword != null) sword.Interrupt();
            motion = Motion.Roll;
            motionTime = 0f;
            rollJumpQueued = false;
            rollReadyAt = Time.time + rollDuration + rollCooldown;
            Splash(0.45f);
            return true;
        }

        void LongJump()
        {
            motion = Motion.Air;
            motionTime = 0f;
            longJump = true;
            rollJumpQueued = false;
            verticalVelocity = Mathf.Sqrt(2f * -gravity * longJumpHeight);
            velocity = rollDir * longJumpSpeed * speedScale;
            Splash(0.6f);
        }

        // Ataque pedido: no ar vira golpe a pique (se houver altura); a rolar ou já no pique é ignorado.
        bool AirAttack()
        {
            if (motion == Motion.Roll || motion == Motion.Plunge) return true;
            if (motion != Motion.Air) return false;
            if (combatant != null && combatant.IsHurt) return true;
            if (HeightAboveGround(out float groundY) < plungeMinHeight) return true;
            if (combatant != null && sword != null) combatant.SpendStamina(sword.plunge.stamina);
            motion = Motion.Plunge;
            motionTime = 0f;
            longJump = false;
            dived = false;
            plungeGroundY = groundY;
            plungeTarget = PickPlungeTarget();
            if (sword != null) sword.PlungeRaise();
            return true;
        }

        float HeightAboveGround(out float groundY)
        {
            // (o raio começa dentro da própria cápsula, por isso não a apanha)
            if (Physics.Raycast(transform.position + Vector3.up * 0.1f, Vector3.down, out RaycastHit hit, 100f, ~0, QueryTriggerInteraction.Ignore))
            {
                groundY = hit.point.y;
                return hit.distance - 0.1f;
            }
            groundY = 0f;
            return transform.position.y;
        }

        // Alvo do pique: o selecionado, ou o corpo mais perto à frente (até 4 m na horizontal).
        Transform PickPlungeTarget()
        {
            if (lockOn != null && lockOn.Current != null) return lockOn.Current.transform;
            Transform best = null;
            float bestD = 4f;
            foreach (Combatant c in Combatant.All)
            {
                if (c == combatant) continue;
                Vector3 d = Flat(c.transform.position - transform.position);
                if (d.magnitude < bestD && Vector3.Angle(transform.forward, d) < 80f) { bestD = d.magnitude; best = c.transform; }
            }
            return best;
        }

        // Corrige a queda para cravar a espada mesmo à frente do alvo (a lâmina vai ~0.5 m à frente do corpo).
        Vector3 PlungeHoming()
        {
            if (plungeTarget == null) return Vector3.zero;
            Vector3 d = Flat(plungeTarget.position - transform.position);
            if (d.magnitude > 6f) return Vector3.zero;
            Vector3 aim = d - (d.sqrMagnitude > 1e-4f ? d.normalized * 0.55f : Vector3.zero);
            float fall = Mathf.Max(0.05f, (transform.position.y - plungeGroundY) / plungeSpeed);
            return Vector3.ClampMagnitude(aim / fall, plungeHoming);
        }

        static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

        void Landed(float strength)
        {
            motion = Motion.Ground;
            motionTime = 0f;
            longJump = false;
            landSquash = strength * 0.5f;
            foreach (Foot f in new[] { left, right })
                if (f?.t != null) { f.planted = FootHome(f, 0f); f.stepping = false; }
            Splash(strength);
        }

        // A espada crava no chão à frente: onda de choque em área (empurra para fora e atira ao ar), faíscas e
        // clarão no ponto onde entra, onda e cratera na água, nevoeiro rebentado e uma paragem curta.
        void PlungeImpact()
        {
            Vector3 at = transform.position;
            Vector3 stab = at + transform.forward * 0.5f;
            stab.y = plungeGroundY;
            Combat.Explode(stab + Vector3.up * 0.4f, plungeRadius, new HitInfo
            {
                damage = plungeDamage * (sword != null ? sword.damageScale : 1f), element = AbilityElement.Neutral,
                stagger = 55f * (sword != null ? sword.staggerScale : 1f),
                knockback = transform.forward * 5f + Vector3.up * 3f, source = transform
            });
            SwordSparks.Burst(stab + Vector3.up * 0.05f, Vector3.up, 26, sword != null ? sword.sparkColor : Color.white,
                              speed: 9f, spread: 0.9f, flash: 0.4f);
            if (WaterRipples.Instance != null) WaterRipples.Instance.Spawn(stab, 2.5f);
            if (WaterSim.HasWaterAt(stab)) WaterSim.Impulse(stab, 1.6f, -0.12f, 4f);
            FogField.Blast(stab, radius: 5f, strength: 0.9f, duration: 2.4f, expandTime: 0.12f, waterScale: 1f, waterSpray: true);
            HitStop.Do(0.09f);
            Landed(1.6f);
            landLagUntil = Time.time + plungeRecovery;
            if (sword != null) sword.PlungeStuck(plungeRecovery - 0.08f);
        }

        void Splash(float strength)
        {
            if (WaterRipples.Instance != null) WaterRipples.Instance.Spawn(transform.position, strength);
        }

        // Controlos simples para quando não há rig de câmara: WASD relativo à câmara ou clique para andar.
        Vector3 ReadInput()
        {
            Vector2 axis = GameInput.MoveAxis();
            if (axis.sqrMagnitude > 0.01f)
            {
                hasMoveTarget = false;
                Vector3 fwd = cam != null ? cam.transform.forward : Vector3.forward;
                Vector3 right = cam != null ? cam.transform.right : Vector3.right;
                fwd.y = 0f; right.y = 0f;
                Vector3 dir = fwd.normalized * axis.y + right.normalized * axis.x;
                return Vector3.ClampMagnitude(dir, 1f);
            }

            if (clickToMove && cam != null && Input.GetMouseButton(0))
            {
                var ground = new Plane(Vector3.up, Vector3.zero);
                Ray ray = cam.ScreenPointToRay(Input.mousePosition);
                if (ground.Raycast(ray, out float d)) { moveTarget = ray.GetPoint(d); hasMoveTarget = true; }
            }

            if (hasMoveTarget)
            {
                Vector3 to = moveTarget - transform.position;
                to.y = 0f;
                if (to.magnitude < 0.15f) { hasMoveTarget = false; return Vector3.zero; }
                // Abranda ao chegar, para não ultrapassar o destino.
                return to.normalized * Mathf.Clamp01(to.magnitude / 0.6f);
            }
            return Vector3.zero;
        }

        // Onde o pé "quer" estar: ao lado do corpo, adiantado na direção do movimento.
        Vector3 FootHome(Foot f, float lead)
        {
            Vector3 p = transform.position + transform.right * (f.side * footSpacing);
            Vector3 v = Moving;
            p += v.normalized * (lead * Mathf.Clamp01(v.magnitude / walkSpeed));
            p.y = 0.04f;
            return p;
        }

        void UpdateFeet(bool running)
        {
            // No ar e a rolar, os pés encolhem-se debaixo do corpo (e voltam a assentar ao aterrar).
            if (motion != Motion.Ground || !(externalDrive ? externalGrounded : controller.isGrounded))
            {
                float k = 1f - Mathf.Exp(-25f * Time.deltaTime);
                foreach (Foot f in new[] { left, right })
                {
                    if (f == null || f.t == null) continue;
                    Vector3 tuck = transform.position + transform.right * (f.side * footSpacing * 0.8f)
                                   + Vector3.up * (motion == Motion.Roll ? 0.45f : 0.22f);
                    f.t.position = Vector3.Lerp(f.t.position, tuck, k);
                    f.t.rotation = Quaternion.Slerp(f.t.rotation, transform.rotation, k);
                    f.planted = f.t.position;
                    f.stepping = false;
                }
                return;
            }

            float speed01 = Mathf.Clamp01(Moving.magnitude / runSpeed);
            float duration = Mathf.Lerp(stepDuration, stepDuration * 0.7f, speed01);
            float threshold = Mathf.Lerp(stepThreshold, stepThreshold * 1.6f, speed01);
            // Empurrado: passos curtos e rápidos para trás, a tropeçar.
            if (slide.sqrMagnitude > 1f) { duration *= 0.55f; threshold *= 0.5f; }

            foreach (Foot f in new[] { left, right })
            {
                if (f == null || f.t == null) continue; // (os pés não sobrevivem a uma recompilação em play mode)
                if (f.stepping)
                {
                    f.progress += Time.deltaTime / duration;
                    float k = Mathf.Clamp01(f.progress);
                    float ease = k * k * (3f - 2f * k);
                    Vector3 p = Vector3.Lerp(f.from, f.to, ease);
                    p.y += Mathf.Sin(k * Mathf.PI) * stepHeight * (0.6f + 0.6f * speed01);
                    f.t.position = p;
                    f.t.rotation = Quaternion.Slerp(f.t.rotation, transform.rotation, 0.5f);
                    if (k >= 1f)
                    {
                        f.stepping = false;
                        f.planted = f.to;
                        Land(f, running);
                    }
                }
                else
                {
                    f.t.position = f.planted;
                }
            }

            // Só um pé de cada vez, e alterna: dá o ritmo esquerdo-direito.
            if (left.stepping || right.stepping) return;
            Foot candidate = lastStepped == left ? right : left;
            Foot other = candidate == left ? right : left;
            Foot pick = null;
            if (NeedsStep(candidate, threshold)) pick = candidate;
            else if (NeedsStep(other, threshold * 1.4f)) pick = other;
            if (pick == null) return;

            pick.stepping = true;
            pick.progress = 0f;
            pick.from = pick.planted;
            pick.to = FootHome(pick, stepLead + threshold * 0.5f);
            lastStepped = pick;
        }

        bool NeedsStep(Foot f, float threshold)
        {
            if (f.t == null) return false;
            Vector3 home = FootHome(f, 0f);
            Vector3 d = home - f.planted; d.y = 0f;
            // Parado: arruma os pés quando ficam tortos depois de rodar.
            float limit = Moving.sqrMagnitude < 0.01f ? 0.12f : threshold;
            return d.magnitude > limit;
        }

        void Land(Foot f, bool running)
        {
            if (WaterRipples.Instance == null) return;
            float moving = Mathf.Clamp01(velocity.magnitude / walkSpeed);
            float strength = Mathf.Lerp(0.15f, running ? runRippleStrength : walkRippleStrength, moving);
            WaterRipples.Instance.Spawn(f.planted, strength);
        }

        void UpdateBody()
        {
            if (body == null) return;
            // O corpo sobe ligeiramente a meio de cada passo e desce ao assentar: dá peso ao andar.
            float lift = 0f;
            if (left.stepping) lift = Mathf.Max(lift, Mathf.Sin(Mathf.Clamp01(left.progress) * Mathf.PI));
            if (right.stepping) lift = Mathf.Max(lift, Mathf.Sin(Mathf.Clamp01(right.progress) * Mathf.PI));
            // Roll: o corpo baixa e dá uma cambalhota para a frente; pique: inclina-se para trás ao erguer a
            // espada e para a frente ao mergulhar; esmaga ao aterrar.
            Quaternion spin = Quaternion.identity;
            float drop = 0f;
            if (motion == Motion.Roll)
            {
                float t = Mathf.Clamp01(motionTime / rollDuration);
                float e = 1f - (1f - t) * (1f - t);
                spin = Quaternion.AngleAxis(360f * e, Vector3.right);
                drop = Mathf.Sin(t * Mathf.PI) * 0.33f;
            }
            else if (motion == Motion.Plunge)
            {
                float t = Mathf.Clamp01(motionTime / plungeRaise);
                spin = Quaternion.AngleAxis(dived ? 18f : -12f * t, Vector3.right);
            }
            // Postura do estilo de espada (o Fencer põe-se de lado); a rolar volta de frente.
            float stanceTarget = sword != null && motion != Motion.Roll ? sword.StanceYaw : 0f;
            stance = Mathf.Lerp(stance, stanceTarget, 1f - Mathf.Exp(-10f * Time.deltaTime));
            spin = Quaternion.Euler(0f, stance, 0f) * spin;
            landSquash = Mathf.MoveTowards(landSquash, 0f, Time.deltaTime * 3f);
            drop += landSquash * 0.25f;
            if (combatant != null) combatant.extraRotation = spin;
            else body.localRotation = spin;
            body.localPosition = bodyBase + Vector3.up * (lift * bobAmount - drop);
        }
    }
}
