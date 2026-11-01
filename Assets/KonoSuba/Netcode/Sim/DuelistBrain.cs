namespace AnimLab.Netcode.Sim
{
    /// <summary>Memória da IA de um lutador (entra nos snapshots: o rollback refaz as decisões).</summary>
    public struct AiState
    {
        // Constantes da partida (DuelistAI do Inspector).
        public Fix Skill, Aggression, ReactMin, ReactMax, AggroRange, AttackRange, FootsieRange, RespawnDelay;
        // Memória.
        public ulong Rng;
        public int Opponent, OppPrevSwing, ComboLeft;
        public WeaponState OppPrevWeapon;
        public Fix NextThink, ReactAt, GuardFrom, GuardUntil, AttackAt, NextPressAt;
        public Fix BackstepUntil, FeintUntil, RetreatUntil, StrafeSign, StrafeSwitchAt, NoiseSeed, NextAttackWish, DownSince;
        public static AiState Duelist(ulong seed) => new AiState {
            Skill = Fix.Ratio(65,100), Aggression = Fix.Ratio(65,100), ReactMin = Fix.Ratio(16,100), ReactMax = Fix.Ratio(32,100),
            AggroRange = 12, AttackRange = Fix.Ratio(155,100), FootsieRange = Fix.Ratio(27,10), RespawnDelay = 3,
            Rng = seed == 0 ? 1UL : seed, Opponent = -1, OppPrevSwing = -1, OppPrevWeapon = WeaponState.Idle,
            ReactAt = -1, GuardFrom = -1, GuardUntil = -1, AttackAt = -1, NextPressAt = -1, StrafeSign = 1, DownSince = -1
        };
    }

    /// <summary>
    /// O DuelistAI do offline dentro da simulação: as mesmas decisões, a produzir um PlayerInput por tick.
    /// Comportamento "humano": pensa a um ritmo humano, reage com atraso, faz footsie, parry/bloqueio/troca/recuo,
    /// combos com paciência, castiga aberturas e fica cauteloso com pouca vida. Aleatoriedade com RNG próprio no estado.
    /// </summary>
    public static class DuelistBrain
    {
        static Fix F(long n, long d = 100) => Fix.Ratio(n, d);
        static readonly Fix Facing70 = Fix.Cos(Fix.Degrees(70));

        static Fix Unit(ref AiState ai) { ulong x = ai.Rng; x ^= x << 13; x ^= x >> 7; x ^= x << 17; ai.Rng = x; return Fix.FromRaw((long)(x >> 32)); }
        static Fix Range(ref AiState ai, Fix a, Fix b) => a + (b - a) * Unit(ref ai);
        static Fix Lerp(Fix a, Fix b, Fix t) => a + (b - a) * t;

        // Ruído suave determinístico em [0,1] (substitui o PerlinNoise do offline: a mesma "respiração" da distância).
        static Fix Noise(Fix t, Fix seed)
        {
            int n = t.Floor; Fix f = t - n, s = f * f * (3 - 2 * f);
            return Lerp(Hash(n, seed), Hash(n + 1, seed), s);
        }
        static Fix Hash(int n, Fix seed)
        {
            ulong x = (ulong)(uint)n * 0x9E3779B97F4A7C15UL ^ (ulong)seed.Raw * 0xC2B2AE3D27D4EB4FUL;
            x ^= x >> 31; x *= 0xBF58476D1CE4E5B9UL; x ^= x >> 29;
            return Fix.FromRaw((long)(x >> 32));
        }

        /// <summary>Tempo até o golpe do lutador chegar (como SwordWielder.TimeToImpact).</summary>
        public static Fix TimeToImpact(in PlayerState p)
        {
            Fix k = World.SwingRate(p);
            if (p.Weapon == WeaponState.Plunge)
                return p.PlungePhase == 0 ? Fix.Max(0, SwordData.Plunge.Windup - p.WeaponTime) + F(15) : p.PlungePhase == 1 ? F(10) : Fix.Zero;
            SwingData sw = SwordData.Get(p.Config.Style, p.Swing);
            if (p.Weapon == WeaponState.Windup) return (sw.Windup - p.WeaponTime + sw.Active * F(40)) / k;
            if (p.Weapon == WeaponState.Active) return Fix.Max(0, sw.Active * F(40) - p.WeaponTime) / k;
            return 0;
        }
        static bool CanRoll(in PlayerState p) =>
            p.Motion == Motion.Ground && p.Position.Y <= Fix.Epsilon && !p.Staggered && p.Weapon != WeaponState.Recoil &&
            p.Weapon != WeaponState.Windup && p.Weapon != WeaponState.Active && p.BodyTime >= p.RollReadyAt && p.BodyTime >= p.LandLagUntil;

        static PlayerInput Look(in PlayerState self, FixVec3 at, FixVec3 wish, Buttons buttons, int target)
        {
            FixVec3 d = (at - self.Position).Flat;
            Fix yaw = d.LengthSquared > F(1, 10000) ? Fix.Atan2(d.X, d.Z) : self.Yaw;
            long a = yaw.Raw % Fix.TwoPi.Raw; if (a < 0) a += Fix.TwoPi.Raw;
            var input = new PlayerInput { LookYaw = (ushort)(((a * 1024 + Fix.TwoPi.Raw / 2) / Fix.TwoPi.Raw) & 1023), Buttons = buttons };
            if (target >= 0) { input.Targeted = true; input.TargetSlot = (byte)(target + 1); }
            Fix m = Fix.Clamp01(wish.Length);
            if (m > Fix.Zero)
            {
                long w = Fix.Atan2(wish.X, wish.Z).Raw % Fix.TwoPi.Raw; if (w < 0) w += Fix.TwoPi.Raw;
                input.MoveAngle = (byte)(((w * 256 + Fix.TwoPi.Raw / 2) / Fix.TwoPi.Raw) & 255);
                input.Magnitude = (byte)((m * 15 + F(50)).Floor);
            }
            return input;
        }

        /// <summary>Decide o input deste tick para o lutador id (Kind = Duelist).</summary>
        public static PlayerInput Think(ref MatchState s, int id, EventBuffer events)
        {
            Fix now = Fix.FromInt(s.Tick) * World.Dt;
            ref PlayerState self = ref MatchState.Player(ref s, id);
            ref AiState ai = ref self.Ai;
            int oppId = ai.Opponent >= 0 && MatchState.Hostile(ref s, id, ai.Opponent) ? ai.Opponent : World.TargetOf(ref s, id, default);
            if (oppId < 0) return Look(self, self.Position + self.Forward, FixVec3.Zero, 0, -1);
            ai.Opponent = oppId;
            PlayerState opp = MatchState.Player(ref s, oppId);

            // Derrota: cai, fica em stagger, e volta com a vida cheia depois de respawnDelay.
            if (self.Health <= 0)
            {
                if (ai.DownSince < 0)
                {
                    ai.DownSince = now; ai.ComboLeft = 0;
                    self.Weapon = WeaponState.Idle; self.IdleSince = -9; self.Queued = false;
                    World.Stagger(ref s, id, ai.RespawnDelay, events, oppId);
                    events.Add(s.Tick, EventKind.Defeated, oppId, id, self.Position, FixVec3.Up);
                }
                if (now - ai.DownSince >= ai.RespawnDelay) { self.Health = self.Config.MaxHealth; ai.DownSince = -1; ai.RetreatUntil = now + 1; }
                return Look(self, opp.Position, FixVec3.Zero, 0, oppId);
            }
            ai.DownSince = -1;

            FixVec3 to = (opp.Position - self.Position).Flat;
            Fix dist = to.Length;
            FixVec3 fwd = dist > F(1) ? to / dist : self.Forward;
            FixVec3 side = FixVec3.Cross(FixVec3.Up, fwd);
            if (dist > ai.AggroRange) { ai.ComboLeft = 0; return Look(self, opp.Position, FixVec3.Zero, 0, oppId); }
            bool lowHealth = self.Health < self.Config.MaxHealth * F(30);

            // Em stagger, a sentir um golpe ou a ressaltar não decide nada.
            if (self.Hurt || self.Weapon == WeaponState.Recoil)
            {
                ai.ComboLeft = 0;
                ai.GuardUntil = Fix.Min(ai.GuardUntil, now);
                ai.RetreatUntil = now + Range(ref ai, F(30), F(70));
                return Look(self, opp.Position, FixVec3.Zero, 0, oppId);
            }

            // Ler o adversário: um golpe a começar ao alcance e virado para nós → reage daqui a um tempo humano.
            bool newSwing = opp.Weapon == WeaponState.Windup && (ai.OppPrevWeapon != WeaponState.Windup || opp.Swing != ai.OppPrevSwing)
                            || opp.Weapon == WeaponState.Plunge && ai.OppPrevWeapon != WeaponState.Plunge;
            ai.OppPrevWeapon = opp.Weapon; ai.OppPrevSwing = opp.Swing;
            bool plunging = opp.Weapon == WeaponState.Plunge;
            if (newSwing && (plunging ? dist < 6 : dist < ai.AttackRange + F(140) && Facing(opp, self.Position)))
                ai.ReactAt = now + Range(ref ai, ai.ReactMin, ai.ReactMax) * Lerp(F(125), F(85), ai.Skill);

            FixVec3 rollWish = FixVec3.Zero; bool roll = false;
            if (ai.ReactAt > 0 && now >= ai.ReactAt) { ai.ReactAt = -1; React(ref ai, self, opp, now, dist, lowHealth, ref roll, ref rollWish); }
            if (now >= ai.NextThink) { ai.NextThink = now + Range(ref ai, F(15), F(40)); ThinkAhead(ref ai, self, opp, now, dist, lowHealth); }

            // Guarda: mantida na janela planeada, e nunca enquanto ataca.
            bool guard = now >= ai.GuardFrom && now <= ai.GuardUntil && !self.Attacking;
            bool attack = Attack(ref ai, self, opp, now, dist, ref guard);
            bool run = false;
            FixVec3 wish = rollWish;
            if (!roll) wish = Move(ref ai, self, now, dist, fwd, side, lowHealth, out run);
            Buttons b = (guard ? Buttons.Guard : 0) | (attack ? Buttons.Attack : 0) | (roll ? Buttons.Roll : 0) | (run ? Buttons.Run : 0);
            return Look(self, opp.Position, wish, b, oppId);
        }

        static bool Facing(in PlayerState who, FixVec3 at)
        {
            FixVec3 d = (at - who.Position).Flat;
            return d.LengthSquared < F(1, 10000) || FixVec3.Dot(who.Forward, d.Normalized) >= Facing70;
        }

        static void React(ref AiState ai, in PlayerState self, in PlayerState opp, Fix now, Fix dist, bool lowHealth, ref bool roll, ref FixVec3 rollWish)
        {
            if (!opp.Attacking) return;
            if (opp.Weapon == WeaponState.Plunge && opp.PlungePhase < 2)
            {
                // Pique: o mais natural é sair de baixo (roll para o lado); senão aguenta na guarda.
                ai.ComboLeft = 0;
                FixVec3 side = FixVec3.Cross(FixVec3.Up, self.Forward) * (Unit(ref ai) < F(50) ? -Fix.One : Fix.One);
                if (Unit(ref ai) < F(45) + ai.Skill * F(30) && CanRoll(self)) { roll = true; rollWish = side; return; }
                PlanGuard(ref ai, self, now, now, now + F(90));
                return;
            }
            Fix impact = now + TimeToImpact(opp);
            Fix hp = lowHealth ? F(140) : Fix.One;
            // Erro humano: às vezes não faz nada a tempo.
            if (Unit(ref ai) < (1 - ai.Skill) * F(30)) return;
            Fix r = Unit(ref ai);
            Fix pParry = ai.Skill * F(50);
            Fix pTrade = self.Attacking || lowHealth || !Affords(self, 1) ? Fix.Zero : ai.Aggression * F(18);
            // Com pouca stamina, bloquear parte a guarda: prefere recuar.
            Fix pBack = F(12) * hp + (self.Stamina < self.Config.MaxStamina * F(25) ? F(40) : Fix.Zero);
            if (r < pParry)
            {
                // Parry: carrega na guarda pouco antes do impacto. Quanto mais skill, mais certinho.
                Fix jitter = Range(ref ai, F(-8), F(8)) * (F(130) - ai.Skill);
                Fix press = impact - self.Config.ParryWindow * Range(ref ai, F(35), F(80)) + jitter;
                if (press < now) press = now;
                PlanGuard(ref ai, self, now, press, impact + Range(ref ai, F(12), F(30)));
                ai.ComboLeft = 0;
            }
            else if (r < pParry + pTrade && dist < ai.AttackRange + F(30)) { ai.AttackAt = now; ai.ComboLeft = 1; }
            else if (r < pParry + pTrade + pBack) { ai.BackstepUntil = now + Range(ref ai, F(30), F(45)); ai.ComboLeft = 0; }
            else { PlanGuard(ref ai, self, now, now, impact + Range(ref ai, F(25), F(55)) * hp); ai.ComboLeft = 0; }
        }

        static void PlanGuard(ref AiState ai, in PlayerState self, Fix now, Fix from, Fix until)
        {
            // Já a guardar: mantém (largar e voltar a carregar não daria parry, por causa do lockout).
            if (self.Weapon == WeaponState.Guard) from = Fix.Min(from, now);
            ai.GuardFrom = from;
            ai.GuardUntil = Fix.Max(ai.GuardUntil, until);
        }

        static void ThinkAhead(ref AiState ai, in PlayerState self, in PlayerState opp, Fix now, Fix dist, bool lowHealth)
        {
            if (self.Attacking || ai.ComboLeft > 0 || ai.ReactAt > 0 || self.Weapon == WeaponState.Guard) return;
            // Castigar: adversário aberto e perto.
            bool open = opp.Staggered || opp.Weapon == WeaponState.Recoil
                        || opp.Weapon == WeaponState.Recovery && opp.Swing == SwordData.Count(opp.Config.Style) - 1;
            if (open && dist < ai.AttackRange + F(90) && Affords(self, 1))
            {
                StartCombo(ref ai, now + Range(ref ai, ai.ReactMin, ai.ReactMax) * F(80), Fit(self, Unit(ref ai) < F(60) ? 3 : 2));
                return;
            }
            // Cansado: afasta-se para recuperar a stamina antes de voltar a entrar.
            if (self.Stamina < self.Config.MaxStamina * F(35)) { ai.RetreatUntil = Fix.Max(ai.RetreatUntil, now + F(40)); return; }
            if (now < ai.RetreatUntil || now < ai.BackstepUntil) return;
            bool oppGuarding = opp.Weapon == WeaponState.Guard;
            if (ai.NextAttackWish <= 0)
            {
                Fix wait = Lerp(F(180), F(60), ai.Aggression) * Range(ref ai, F(60), F(140));
                if (lowHealth) wait *= F(160);
                if (oppGuarding) wait *= F(130);
                ai.NextAttackWish = now + wait;
            }
            if (dist < ai.AttackRange + F(25) && Unit(ref ai) < ai.Aggression * F(50)) StartCombo(ref ai, now, Fit(self, PickComboLength(ref ai, lowHealth)));
            else if (now >= ai.NextAttackWish && dist < ai.FootsieRange + F(80)) StartCombo(ref ai, now + Range(ref ai, F(8), F(25)), Fit(self, PickComboLength(ref ai, lowHealth)));
            else if (Unit(ref ai) < F(4) + ai.Skill * F(4))
            {
                // Finta: entra depressa e sai, para arrancar um golpe ou um parry ao adversário.
                ai.FeintUntil = now + Range(ref ai, F(18), F(28));
                ai.BackstepUntil = ai.FeintUntil + Range(ref ai, F(25), F(40));
            }
        }

        // Stamina para os primeiros golpes de um combo (a começar do início).
        static bool Affords(in PlayerState self, int hits)
        {
            Fix cost = 0;
            for (int i = 0; i < hits - 1; i++) cost += SwordData.Get(self.Config.Style, i).Stamina;
            return self.Stamina > cost; // o último só precisa de haver alguma
        }
        static int Fit(in PlayerState self, int hits)
        {
            while (hits > 1 && !Affords(self, hits)) hits--;
            return hits;
        }
        static int PickComboLength(ref AiState ai, bool lowHealth)
        {
            Fix r = Unit(ref ai);
            if (lowHealth) return r < F(70) ? 1 : 2;
            return r < F(30) ? 1 : r < F(72) ? 2 : 3;
        }
        static void StartCombo(ref AiState ai, Fix at, int hits)
        {
            ai.AttackAt = at; ai.ComboLeft = hits; ai.NextAttackWish = 0;
            ai.GuardUntil = Fix.Min(ai.GuardUntil, at);
        }

        // Devolve true quando carrega no ataque neste tick.
        static bool Attack(ref AiState ai, in PlayerState self, in PlayerState opp, Fix now, Fix dist, ref bool guard)
        {
            if (ai.ComboLeft <= 0) return false;
            if (!self.Attacking)
            {
                if (ai.AttackAt < 0 || now < ai.AttackAt) return false;
                if (dist > ai.AttackRange + F(10)) { if (now > ai.AttackAt + F(120)) ai.ComboLeft = 0; return false; }
                if (!self.HasStamina) { ai.ComboLeft = 0; return false; }
                if (World.Resting(self)) return false; // espera o fim da pausa do combo
                guard = false;
                ai.ComboLeft--; ai.AttackAt = -1;
                ai.NextPressAt = now + Range(ref ai, F(10), F(30));
                return true;
            }
            // O clique seguinte só conta da metade do golpe em diante: espera por lá (um humano sente o ritmo).
            if (now < ai.NextPressAt || !World.CanQueue(self)) return false;
            if (!self.HasStamina) { ai.ComboLeft = 0; return false; }
            // Se o adversário está a guardar, às vezes corta o combo (para não bater na guarda à toa).
            if (opp.Weapon == WeaponState.Guard && Unit(ref ai) < F(35) + ai.Skill * F(25))
            {
                ai.ComboLeft = 0; ai.RetreatUntil = now + Range(ref ai, F(35), F(70));
                return false;
            }
            ai.ComboLeft--;
            ai.NextPressAt = now + Range(ref ai, F(12), F(30));
            if (ai.ComboLeft == 0) ai.RetreatUntil = now + Range(ref ai, F(45), F(90));
            return true;
        }

        static FixVec3 Move(ref AiState ai, in PlayerState self, Fix now, Fix dist, FixVec3 fwd, FixVec3 side, bool lowHealth, out bool run)
        {
            run = false;
            if (self.Attacking) return FixVec3.Zero;
            FixVec3 wish;
            if (now < ai.FeintUntil) wish = fwd;
            else if (now < ai.BackstepUntil) wish = -fwd * F(90) + side * ai.StrafeSign * F(30);
            else if (ai.ComboLeft > 0) { wish = dist > ai.AttackRange - F(15) ? fwd : FixVec3.Zero; run = dist > 4; }
            else if (dist > ai.FootsieRange + F(250)) { wish = fwd; run = dist > 6; }
            else
            {
                // Footsie: distância que respira, a rodar à volta e a trocar de lado de vez em quando.
                if (now > ai.StrafeSwitchAt)
                {
                    ai.StrafeSign = Unit(ref ai) < F(50) ? -ai.StrafeSign : ai.StrafeSign;
                    ai.StrafeSwitchAt = now + Range(ref ai, F(80), F(260));
                }
                Fix range = ai.FootsieRange * (lowHealth ? F(125) : Fix.One) + (Noise(now * F(60), ai.NoiseSeed) - F(50)) * F(110);
                if (now < ai.RetreatUntil) range += F(120);
                Fix inOut = Fix.Clamp((dist - range) * F(140), -Fix.One, Fix.One);
                Fix strafe = Lerp(F(25), F(65), Noise(now * F(90), ai.NoiseSeed + 7)) * ai.StrafeSign;
                wish = fwd * inOut + side * strafe;
            }
            return FixVec3.ClampMagnitude(wish, 1);
        }
    }
}
