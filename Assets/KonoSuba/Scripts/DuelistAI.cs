using UnityEngine;

namespace AnimLab.KonoSuba
{
    /// <summary>
    /// Duelista de espada com comportamento "humano": não lê o jogo perfeitamente nem reage no mesmo frame.
    ///   · Pensa a um ritmo humano (decisões a cada 0.15–0.4 s) e reage com atraso (reactionTime).
    ///   · Footsie: anda à volta do adversário a uma distância que oscila, troca de lado, entra e sai.
    ///   · Defesa: quando vê um golpe a começar, depois do tempo de reação escolhe — parry (tenta carregar
    ///     no F mesmo antes do impacto; acerta mais com mais skill), bloquear, recuar, trocar golpe (pode dar
    ///     clash) ou, às vezes, não fazer nada a tempo (erro humano).
    ///   · Ataque: paciência — a próxima vontade de atacar fica marcada para daqui a um tempo (mais curto com
    ///     mais agressividade, sempre com variação); combos de 1 a 3 golpes com o ritmo das teclas a variar; se o adversário guarda, às vezes
    ///     corta o combo e espera que a janela de parry passe antes de voltar a atacar.
    ///   · Castiga: adversário em stagger, a ressaltar ou no fim de um combo falhado → ataca logo.
    ///   · Com pouca vida fica mais cauteloso (mais longe, guarda mais, ataca menos).
    /// Mexe-se pelo CapsulePlayer (aiControlled) e luta pelo SwordWielder (RequestAttack / guardHeld).
    /// </summary>
    [RequireComponent(typeof(CapsulePlayer), typeof(SwordWielder), typeof(Combatant))]
    public class DuelistAI : MonoBehaviour
    {
        [Tooltip("Adversário (por omissão, o Player).")]
        public Transform opponent;
        [Range(0f, 1f), Tooltip("Precisão do parry, leitura do adversário, menos erros.")]
        public float skill = 0.65f;
        [Range(0f, 1f), Tooltip("Vontade de atacar e de trocar golpes.")]
        public float aggression = 0.65f;
        [Tooltip("Tempo de reação (min, max) em segundos.")]
        public Vector2 reactionTime = new Vector2(0.16f, 0.32f);
        public float aggroRange = 12f;
        [Tooltip("Distância a que ataca (m, centro a centro). A espada chega a ~1.4 m do ombro + o passo do golpe.")]
        public float attackRange = 1.55f;
        [Tooltip("Distância de footsie: fora do alcance, a medir o adversário.")]
        public float footsieRange = 2.7f;
        public float respawnDelay = 3f;
        /// <summary>Escreve as decisões na consola (gravações e afinação).</summary>
        public static bool LogDecisions;

        CapsulePlayer body;
        SwordWielder sword;
        Combatant self;
        SwordWielder oppSword;
        Combatant oppBody;

        SwordWielder.State oppPrevState;
        int oppPrevCombo = -1;
        float nextThink, reactAt = -1f;
        float guardFrom = -1f, guardUntil = -1f;
        float attackAt = -1f, nextPressAt = -1f;
        int comboLeft;
        float backstepUntil, feintUntil, retreatUntil;
        float strafeSign = 1f, strafeSwitchAt, noiseSeed;
        float nextAttackWish;
        float downSince = -1f;
        bool lowHealth;

        void Awake()
        {
            body = GetComponent<CapsulePlayer>();
            sword = GetComponent<SwordWielder>();
            self = GetComponent<Combatant>();
            body.aiControlled = true;
            noiseSeed = Random.Range(0f, 100f);
        }

        void Start()
        {
            if (opponent == null)
            {
                GameObject p = GameObject.Find("Player");
                if (p != null) opponent = p.transform;
            }
            if (opponent != null)
            {
                oppSword = opponent.GetComponent<SwordWielder>();
                oppBody = opponent.GetComponent<Combatant>();
            }
            sword.preferredTarget = opponent;
        }

        void Update()
        {
            float now = Time.time;
            body.aiWish = Vector3.zero;
            body.aiRun = false;
            body.aiLookAt = opponent != null ? opponent.position : (Vector3?)null;

            if (Downed(now)) return;
            if (opponent == null) { sword.guardHeld = false; return; }

            Vector3 to = opponent.position - transform.position; to.y = 0f;
            float dist = to.magnitude;
            Vector3 fwd = dist > 0.01f ? to / dist : transform.forward;
            Vector3 side = Vector3.Cross(Vector3.up, fwd);

            if (dist > aggroRange) { sword.guardHeld = false; comboLeft = 0; return; }
            lowHealth = self.Health01 < 0.3f;

            // Em stagger ou a ressaltar não decide nada (um humano também fica "preso" na animação).
            if (self.IsStaggered || sword.IsRecoiling)
            {
                comboLeft = 0;
                guardUntil = Mathf.Min(guardUntil, now);
                sword.guardHeld = false;
                retreatUntil = now + Random.Range(0.3f, 0.7f); // depois de levar, afasta-se
                return;
            }

            ReadOpponent(now, dist);
            if (reactAt > 0f && now >= reactAt) { reactAt = -1f; React(now, dist); }
            if (now >= nextThink) { nextThink = now + Random.Range(0.15f, 0.4f); Think(now, dist); }

            // Guarda: mantida na janela planeada, e nunca enquanto ataca.
            sword.guardHeld = now >= guardFrom && now <= guardUntil && !sword.IsAttacking;

            Attack(now, dist);
            Move(now, dist, fwd, side);
        }

        // ------------------------------------------------------------------ Ler o adversário

        void ReadOpponent(float now, float dist)
        {
            if (oppSword == null) return;
            SwordWielder.State st = oppSword.Current;
            bool newSwing = st == SwordWielder.State.Windup
                            && (oppPrevState != SwordWielder.State.Windup || oppSword.ComboIndex != oppPrevCombo)
                            || st == SwordWielder.State.Plunge && oppPrevState != SwordWielder.State.Plunge;
            oppPrevState = st;
            oppPrevCombo = oppSword.ComboIndex;

            // Um golpe a começar ao alcance e virado para nós: reage daqui a um tempo humano.
            // (o pique vem de cima e corrige a direção: conta a partir de mais longe e de qualquer lado)
            bool plunging = st == SwordWielder.State.Plunge;
            if (newSwing && (plunging ? dist < 6f : dist < attackRange + 1.4f && Facing(opponent, transform, 70f)))
            {
                float r = Random.Range(reactionTime.x, reactionTime.y) * Mathf.Lerp(1.25f, 0.85f, skill);
                reactAt = now + r;
            }
        }

        void React(float now, float dist)
        {
            if (oppSword == null || !oppSword.IsAttacking) return;
            if (oppSword.IsPlunging)
            {
                // Pique: o mais natural é sair de baixo (roll para o lado); senão aguenta na guarda.
                comboLeft = 0;
                Vector3 side = Vector3.Cross(Vector3.up, transform.forward) * (Random.value < 0.5f ? -1f : 1f);
                var cc = GetComponent<CharacterController>();
                if (Random.value < 0.45f + skill * 0.3f && body.TryRoll(side, cc == null || cc.isGrounded, true)) { Log("roll ao pique"); return; }
                PlanGuard(now, now + 0.9f);
                return;
            }
            float impact = now + oppSword.TimeToImpact;
            float hp = lowHealth ? 1.4f : 1f;

            // Erro humano: às vezes não faz nada a tempo.
            if (Random.value < (1f - skill) * 0.3f) return;

            float roll = Random.value;
            float pParry = skill * 0.5f;
            float pTrade = sword.IsAttacking || lowHealth ? 0f : aggression * 0.18f;
            float pBack = 0.12f * hp;
            if (roll < pParry)
            {
                // Parry: carrega no F pouco antes do impacto. Quanto mais skill, mais certinho.
                float jitter = Random.Range(-0.08f, 0.08f) * (1.3f - skill);
                float press = impact - sword.parryWindow * Random.Range(0.35f, 0.8f) + jitter;
                if (press < now) press = now; // já não dá: fica um bloqueio (ou um parry à sorte)
                PlanGuard(press, impact + Random.Range(0.12f, 0.3f));
                comboLeft = 0;
            }
            else if (roll < pParry + pTrade && dist < attackRange + 0.3f && Log("troca"))
            {
                // Troca: ataca também — se as lâminas se cruzarem, clash.
                attackAt = now;
                comboLeft = 1;
            }
            else if (roll < pParry + pTrade + pBack)
            {
                backstepUntil = now + Random.Range(0.3f, 0.45f);
                comboLeft = 0;
            }
            else
            {
                // Bloqueio: guarda já e aguenta até o golpe (e às vezes o combo) acabar.
                PlanGuard(now, impact + Random.Range(0.25f, 0.55f) * hp);
                comboLeft = 0;
            }
        }

        void PlanGuard(float from, float until)
        {
            // Já a guardar há tempo: mantém (largar e voltar a carregar não daria parry, por causa do lockout).
            if (sword.IsGuarding) from = Mathf.Min(from, Time.time);
            guardFrom = from;
            guardUntil = Mathf.Max(guardUntil, until);
        }

        // ------------------------------------------------------------------ Decidir

        void Think(float now, float dist)
        {
            if (sword.IsAttacking || comboLeft > 0 || reactAt > 0f || sword.IsGuarding) return;

            // Castigar: adversário aberto e perto.
            bool open = oppBody != null && oppBody.IsStaggered
                        || oppSword != null && (oppSword.IsRecoiling
                            || oppSword.Current == SwordWielder.State.Recovery && oppSword.ComboIndex == oppSword.ComboLength - 1);
            if (open && dist < attackRange + 0.9f)
            {
                StartCombo(now + Random.Range(reactionTime.x, reactionTime.y) * 0.8f, Random.value < 0.6f ? 3 : 2);
                return;
            }

            if (now < retreatUntil || now < backstepUntil) return;

            // Adversário a guardar há algum tempo: a janela de parry dele já passou — atacar agora só bate
            // na guarda, mas parte-lhe a postura. Às vezes vai, às vezes espera.
            bool oppGuarding = oppSword != null && oppSword.IsGuarding;
            if (nextAttackWish <= 0f) ScheduleAttackWish(now, oppGuarding);

            if (dist < attackRange + 0.25f && Random.value < aggression * 0.5f)
            {
                // Demasiado perto: aproveita.
                StartCombo(now, PickComboLength());
            }
            else if (now >= nextAttackWish && dist < footsieRange + 0.8f)
            {
                // Chegou a vontade de atacar: entra (passo à frente e golpe).
                StartCombo(now + Random.Range(0.08f, 0.25f), PickComboLength());
            }
            else if (Random.value < 0.04f + skill * 0.04f)
            {
                // Finta: entra depressa e sai, para tentar arrancar um golpe ou um parry ao adversário.
                feintUntil = now + Random.Range(0.18f, 0.28f);
                backstepUntil = feintUntil + Random.Range(0.25f, 0.4f);
            }
        }

        void ScheduleAttackWish(float now, bool oppGuarding)
        {
            float wait = Mathf.Lerp(1.8f, 0.6f, aggression) * Random.Range(0.6f, 1.4f);
            if (lowHealth) wait *= 1.6f;
            if (oppGuarding) wait *= 1.3f;
            nextAttackWish = now + wait;
        }

        int PickComboLength()
        {
            float r = Random.value;
            if (lowHealth) return r < 0.7f ? 1 : 2;
            return r < 0.3f ? 1 : r < 0.72f ? 2 : 3;
        }

        void StartCombo(float at, int hits)
        {
            if (LogDecisions) Debug.Log($"[AI] {Time.frameCount} combo x{hits} em {at - Time.time:0.00}s");
            attackAt = at;
            comboLeft = hits;
            nextAttackWish = 0f; // a próxima marca-se quando voltar a pensar
            guardUntil = Mathf.Min(guardUntil, at);
        }

        // ------------------------------------------------------------------ Atacar

        void Attack(float now, float dist)
        {
            if (comboLeft <= 0) return;
            // Primeiro golpe: só quando já está ao alcance (enquanto isso, o Move aproxima-o).
            if (!sword.IsAttacking)
            {
                if (attackAt < 0f || now < attackAt) return;
                if (dist > attackRange + 0.1f) { if (now > attackAt + 1.2f) comboLeft = 0; return; }
                sword.guardHeld = false;
                sword.RequestAttack();
                comboLeft--;
                attackAt = -1f;
                nextPressAt = now + Random.Range(0.1f, 0.3f); // ritmo das teclas varia
                return;
            }
            // Golpes seguintes do combo: carrega outra vez a um tempo humano.
            if (now < nextPressAt) return;
            // Se o adversário está a guardar, às vezes corta o combo (para não bater na guarda à toa).
            if (oppSword != null && oppSword.IsGuarding && Random.value < 0.35f + skill * 0.25f)
            {
                comboLeft = 0;
                retreatUntil = now + Random.Range(0.35f, 0.7f);
                return;
            }
            sword.RequestAttack();
            comboLeft--;
            nextPressAt = now + Random.Range(0.12f, 0.3f);
            if (comboLeft == 0) retreatUntil = now + Random.Range(0.45f, 0.9f);
        }

        // ------------------------------------------------------------------ Movimento

        void Move(float now, float dist, Vector3 fwd, Vector3 side)
        {
            Vector3 wish = Vector3.zero;
            if (sword.IsAttacking) { body.aiWish = Vector3.zero; return; }

            if (now < feintUntil) wish = fwd * 1f;
            else if (now < backstepUntil) wish = -fwd * 0.9f + side * strafeSign * 0.3f;
            else if (comboLeft > 0)
            {
                // A entrar para o golpe.
                wish = dist > attackRange - 0.15f ? fwd : Vector3.zero;
                body.aiRun = dist > 4f;
            }
            else if (dist > footsieRange + 2.5f)
            {
                wish = fwd;
                body.aiRun = dist > 6f;
            }
            else
            {
                // Footsie: distância que respira (ruído), a rodar à volta e a trocar de lado de vez em quando.
                if (now > strafeSwitchAt)
                {
                    strafeSign = Random.value < 0.5f ? -strafeSign : strafeSign;
                    strafeSwitchAt = now + Random.Range(0.8f, 2.6f);
                }
                float range = footsieRange * (lowHealth ? 1.25f : 1f)
                              + (Mathf.PerlinNoise(now * 0.6f, noiseSeed) - 0.5f) * 1.1f;
                if (now < retreatUntil) range += 1.2f;
                float inOut = Mathf.Clamp((dist - range) * 1.4f, -1f, 1f);
                float strafe = Mathf.Lerp(0.25f, 0.65f, Mathf.PerlinNoise(now * 0.9f, noiseSeed + 7f)) * strafeSign;
                wish = fwd * inOut + side * strafe;
            }
            // Guardar trava o passo (o CapsulePlayer já abranda); parado demasiado perto: afasta-se um pouco.
            body.aiWish = Vector3.ClampMagnitude(wish, 1f);
        }

        // ------------------------------------------------------------------ Derrota

        bool Downed(float now)
        {
            if (self.health > 0f) { downSince = -1f; return false; }
            if (downSince < 0f)
            {
                downSince = now;
                comboLeft = 0;
                sword.Interrupt();
                self.Stagger(respawnDelay);
                Combat.Callout(transform.position + Vector3.up * 2.4f, "DEFEATED", new Color(1f, 0.45f, 0.32f), 30f);
            }
            sword.guardHeld = false;
            if (now - downSince >= respawnDelay)
            {
                self.health = self.maxHealth;
                downSince = -1f;
                retreatUntil = now + 1f;
            }
            return true;
        }

        static bool Log(string what)
        {
            if (LogDecisions) Debug.Log($"[AI] {Time.frameCount} reação: {what}");
            return true;
        }

        static bool Facing(Transform who, Transform at, float maxAngle)
        {
            Vector3 d = at.position - who.position; d.y = 0f;
            return d.sqrMagnitude < 1e-4f || Vector3.Angle(who.forward, d) <= maxAngle;
        }
    }
}
