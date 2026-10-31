namespace AnimLab.Netcode.Sim
{
    /// <summary>Simulação a 60 Hz. Ordem fixa: movimento 0/1, armas 0/1, contactos 0/1.</summary>
    public static partial class World
    {
        public static readonly Fix Dt = Fix.Ratio(1, 60), ArenaRadius = 20;
        static Fix F(long n, long d = 100) => Fix.Ratio(n, d);
        public static void Step(ref MatchState state, PlayerInput a, PlayerInput b, EventBuffer events)
        {
            events.Clear();
            bool freeze0 = state.P0.HitStop > 0, freeze1 = state.P1.HitStop > 0;
            // O impacto não perde pedidos de combo/salto/roll feitos durante a paragem.
            const Buttons edges = Buttons.Attack | Buttons.Jump | Buttons.Roll;
            if (freeze0) state.P0.BufferedEdges |= a.Buttons & edges;
            else { a.Buttons |= state.P0.BufferedEdges; state.P0.BufferedEdges = 0; }
            if (freeze1) state.P1.BufferedEdges |= b.Buttons & edges;
            else { b.Buttons |= state.P1.BufferedEdges; state.P1.BufferedEdges = 0; }
            if (freeze0) state.P0.HitStop--;
            if (freeze1) state.P1.HitStop--;
            if (!freeze0) { state.P0.BodyTime += Dt; Move(ref state, 0, a, events); }
            if (!freeze1) { state.P1.BodyTime += Dt; Move(ref state, 1, b, events); }
            Separate(ref state, freeze0, freeze1);
            AfterMovement(ref state, a, b, freeze0, freeze1, events);
            state.Tick++;
        }
        static partial void AfterMovement(ref MatchState s, PlayerInput a, PlayerInput b, bool freeze0, bool freeze1, EventBuffer events);

        static void Move(ref MatchState s, int id, PlayerInput input, EventBuffer events)
        {
            ref PlayerState p = ref MatchState.Player(ref s, id);
            FixVec3 target = MatchState.Player(ref s, 1 - id).Position;
            FixVec3 wish = input.Wish;
            bool attack = p.Attacking, recoil = p.Weapon == WeaponState.Recoil, guard = p.Weapon == WeaponState.Guard;
            bool swinging = p.Weapon == WeaponState.Windup || p.Weapon == WeaponState.Active;
            bool free = !p.Staggered && !recoil && !swinging;
            Fix scale = p.Staggered || recoil ? 0 : attack ? F(15) : guard ? F(40) : 1;
            if (p.BodyTime < p.LandLagUntil) scale = 0;
            bool grounded = p.Position.Y <= Fix.Epsilon;
            if (input.Has(Buttons.Jump))
            {
                if (p.Motion == Motion.Roll) p.RollJumpQueued = true;
                else if (p.Motion == Motion.Ground && grounded && free && !attack && !guard && p.BodyTime >= p.LandLagUntil)
                {
                    p.VerticalVelocity = Fix.Sqrt(-2 * p.Config.Gravity * p.Config.JumpHeight);
                    p.Motion = Motion.Air; p.MotionTime = 0; p.LongJump = false;
                    events.Add(s.Tick, EventKind.Jump, id, id, p.Position, FixVec3.Up);
                }
            }
            if (input.Has(Buttons.Roll) && p.Motion == Motion.Ground && grounded && free && p.BodyTime >= p.RollReadyAt && p.BodyTime >= p.LandLagUntil)
            {
                p.RollDirection = wish.LengthSquared > F(1) ? wish.Normalized : p.Forward;
                p.Yaw = Fix.Atan2(p.RollDirection.X, p.RollDirection.Z);
                p.Weapon = WeaponState.Idle; p.IdleSince = -9; p.Queued = false;
                p.Motion = Motion.Roll; p.MotionTime = 0; p.RollJumpQueued = false; p.RollReadyAt = p.BodyTime + p.Config.RollDuration + p.Config.RollCooldown;
                events.Add(s.Tick, EventKind.Roll, id, id, p.Position, p.RollDirection);
            }
            if (input.Has(Buttons.Attack) && p.Motion == Motion.Air && !p.Staggered && p.Position.Y >= p.Config.PlungeMinHeight)
            {
                p.Motion = Motion.Plunge; p.MotionTime = 0; p.LongJump = false; p.Dived = false;
                FixVec3 d = (target - p.Position).Flat;
                p.HasPlungeTarget = input.Targeted || d.Length < 4 && FixVec3.Dot(p.Forward, d.Normalized) > Fix.Cos(Fix.Degrees(80));
                p.Weapon = WeaponState.Plunge; p.PlungePhase = 0; p.WeaponTime = 0; p.Queued = false; p.Struck = false; p.HasLastBlade = false;
            }
            Fix max = (input.Has(Buttons.Run) && !guard ? p.Config.Run : p.Config.Walk) * p.Config.SpeedScale;
            p.MotionTime += Dt;
            switch (p.Motion)
            {
                case Motion.Roll:
                    Fix t = Fix.Clamp01(p.MotionTime / p.Config.RollDuration);
                    p.Velocity = p.RollDirection * (p.Config.RollDistance / (p.Config.RollDuration * F(65))) * (1 - F(70) * t);
                    if (p.MotionTime >= p.Config.RollInvulFrom && p.MotionTime <= p.Config.RollInvulTo) p.InvulnerableUntil = p.BodyTime + F(5);
                    if (t >= 1)
                    {
                        if (p.RollJumpQueued)
                        {
                            p.Motion = Motion.Air; p.MotionTime = 0; p.LongJump = true; p.RollJumpQueued = false;
                            p.VerticalVelocity = Fix.Sqrt(-2 * p.Config.Gravity * p.Config.LongJumpHeight);
                            p.Velocity = p.RollDirection * p.Config.LongJumpSpeed * p.Config.SpeedScale;
                            events.Add(s.Tick, EventKind.Jump, id, id, p.Position, p.RollDirection);
                        }
                        else { p.Motion = Motion.Ground; p.Velocity = p.RollDirection * p.Config.Walk * p.Config.SpeedScale; }
                    }
                    break;
                case Motion.Plunge:
                    if (p.Weapon != WeaponState.Plunge) { p.Motion = Motion.Air; break; }
                    if (p.MotionTime < p.Config.PlungeRaise) { p.Velocity *= 1 - Fix.Clamp01(8 * Dt); p.VerticalVelocity = F(120); }
                    else
                    {
                        if (!p.Dived) { p.Dived = true; p.PlungePhase = 1; p.HasLastBlade = false; }
                        p.VerticalVelocity = -p.Config.PlungeSpeed; p.Velocity = FixVec3.Zero;
                        FixVec3 d = (target - p.Position).Flat;
                        if (p.HasPlungeTarget && d.Length <= 6)
                        {
                            FixVec3 aim = d - (d.LengthSquared > F(1,10000) ? d.Normalized * F(55) : FixVec3.Zero);
                            p.Velocity = FixVec3.ClampMagnitude(aim / Fix.Max(F(5), p.Position.Y / p.Config.PlungeSpeed), p.Config.PlungeHoming);
                        }
                    }
                    break;
                case Motion.Air:
                    p.Velocity = FixVec3.MoveTowards(p.Velocity, wish * max * scale, p.Config.Acceleration * (p.LongJump ? F(12) : p.Config.AirControl) * Dt);
                    break;
                default:
                    p.Velocity = FixVec3.MoveTowards(p.Velocity, wish * max * scale, p.Config.Acceleration * Dt);
                    break;
            }
            Fix yaw = input.Yaw;
            if (p.Motion == Motion.Roll) yaw = Fix.Atan2(p.RollDirection.X, p.RollDirection.Z);
            else if (p.Motion == Motion.Plunge && p.HasPlungeTarget && (target - p.Position).Flat.LengthSquared > F(1))
                yaw = Fix.Atan2(target.X - p.Position.X, target.Z - p.Position.Z);
            if (!p.Staggered && !recoil && (!attack || p.Motion == Motion.Plunge))
                p.Yaw += Fix.Clamp(Fix.DeltaAngle(p.Yaw, yaw), -p.Config.Turn * Dt, p.Config.Turn * Dt);
            if (p.Motion == Motion.Ground || p.Motion == Motion.Roll) p.VerticalVelocity = grounded ? -2 : p.VerticalVelocity + p.Config.Gravity * Dt;
            else if (p.Motion == Motion.Air) p.VerticalVelocity += p.Config.Gravity * Dt;
            p.Position += (p.Velocity + FixVec3.Up * p.VerticalVelocity) * Dt;
            if (p.Position.Y <= 0)
            {
                p.Position.Y = 0;
                if (p.VerticalVelocity <= 0 && (p.Motion == Motion.Air || p.Motion == Motion.Plunge && p.Dived))
                {
                    bool plunge = p.Motion == Motion.Plunge;
                    p.Motion = Motion.Ground; p.MotionTime = 0; p.LongJump = false;
                    if (plunge)
                    {
                        p.LandLagUntil = p.BodyTime + p.Config.PlungeRecovery; p.PlungePhase = 2; p.WeaponTime = 0; p.StuckFor = p.Config.PlungeRecovery - F(8);
                        PlungeImpact(ref s, id, events);
                    }
                    events.Add(s.Tick, plunge ? EventKind.Plunge : EventKind.Land, id, id, p.Position + (plunge ? p.Forward * F(50) : FixVec3.Zero), FixVec3.Up,visualStop:plunge?F(9):Fix.Zero);
                }
            }
            Bounds(ref p);
        }
        static partial void PlungeImpact(ref MatchState s, int id, EventBuffer events);

        static void Bounds(ref PlayerState p)
        {
            FixVec3 flat = p.Position.Flat;
            if (flat.Length > ArenaRadius - p.Config.Radius)
            {
                flat = flat.Normalized * (ArenaRadius - p.Config.Radius);
                p.Position.X = flat.X; p.Position.Z = flat.Z;
            }
        }
        static void Separate(ref MatchState s, bool freeze0, bool freeze1)
        {
            ref PlayerState a = ref s.P0; ref PlayerState b = ref s.P1;
            Fix halfA = Fix.Max(0,a.Config.Height/2-a.Config.Radius), halfB = Fix.Max(0,b.Config.Height/2-b.Config.Radius);
            Fix centerA = a.Position.Y+a.Config.CenterY, centerB = b.Position.Y+b.Config.CenterY;
            Fix gap = Fix.Max(0,Fix.Abs(centerB-centerA)-halfA-halfB), radii = a.Config.Radius+b.Config.Radius;
            if (gap >= radii) return;
            FixVec3 d = (b.Position - a.Position).Flat;
            Fix distance = d.Length, overlap = Fix.Sqrt(radii*radii-gap*gap) - distance;
            if (overlap <= 0 || freeze0 && freeze1) return;
            FixVec3 push = (distance > Fix.Epsilon ? d / distance : FixVec3.Right) * overlap;
            if (!freeze0) a.Position -= push * (freeze1 ? Fix.One : F(50));
            if (!freeze1) b.Position += push * (freeze0 ? Fix.One : F(50));
            Bounds(ref a); Bounds(ref b);
        }
    }
}

