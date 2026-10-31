namespace AnimLab.Netcode.Sim
{
    public static partial class World
    {
        static partial void AfterMovement(ref MatchState s, PlayerInput a, PlayerInput b, bool freeze0, bool freeze1, EventBuffer events)
        {
            if (!freeze0) { TickBody(ref s,0,events); TickWeapon(ref s,0,a); }
            if (!freeze1) { TickBody(ref s,1,events); TickWeapon(ref s,1,b); }
            if (!freeze0) Sweep(ref s,0,events);
            if (!freeze1) Sweep(ref s,1,events);
            Separate(ref s,freeze0,freeze1);
        }
        static void TickBody(ref MatchState s,int id,EventBuffer events)
        {
            ref PlayerState p=ref MatchState.Player(ref s,id);
            if (p.Health>0 && p.Health<p.Config.MaxHealth) p.Health=Fix.Min(p.Config.MaxHealth,p.Health+p.Config.HealthRegen*Dt);
            if (p.Poise<p.Config.MaxPoise && p.BodyTime-p.LastHitAt>F(150)) p.Poise=Fix.Min(p.Config.MaxPoise,p.Poise+60*Dt);
            FixVec3 step=p.Knock*Dt;
            if (p.KnockAirborne) { p.KnockUp+=F(-981,100)*Dt; step.Y=p.KnockUp*Dt; }
            p.Position+=step;
            if (p.KnockAirborne && p.Position.Y<=0) {
                p.Position.Y=0; p.KnockAirborne=false; p.KnockUp=0;
                events.Add(s.Tick,EventKind.Land,id,id,p.Position,FixVec3.Up);
            }
            p.Knock=FixVec3.MoveTowards(p.Knock,FixVec3.Zero,(p.KnockAirborne?2:12)*Dt);
            Bounds(ref p);
        }
        static void TickWeapon(ref MatchState s,int id,PlayerInput input)
        {
            ref PlayerState p=ref MatchState.Player(ref s,id);
            bool busy=p.Motion==Motion.Roll || p.Motion==Motion.Plunge || p.BodyTime<p.LandLagUntil;
            bool guard=input.Has(Buttons.Guard) && !busy;
            if (input.Has(Buttons.Attack) && p.Motion==Motion.Ground && !busy && !p.Staggered)
            {
                if (p.Weapon==WeaponState.Idle || p.Weapon==WeaponState.Guard) {
                    if (p.Weapon==WeaponState.Guard) p.GuardReleasedAt=p.BodyTime;
                    int next=p.BodyTime-p.IdleSince<F(25) && p.Swing<SwordData.Count(p.Config.Style)-1?p.Swing+1:0;
                    StartSwing(ref p,next,MatchState.Player(ref s,1-id).Position,input.Targeted);
                }
                else if (p.Weapon==WeaponState.Windup || p.Weapon==WeaponState.Active || p.Weapon==WeaponState.Recovery)
                    p.Queued=p.Swing<SwordData.Count(p.Config.Style)-1;
            }
            if (p.Staggered && p.Weapon!=WeaponState.Idle) { p.Weapon=WeaponState.Idle; p.IdleSince=-9; }
            Fix dt=Dt*(p.Attacking?Fix.Max(F(10),p.Config.AttackSpeed):Fix.One);
            SwingData sw=SwordData.Get(p.Config.Style,p.Swing);
            p.WeaponTime+=dt;
            switch(p.Weapon)
            {
                case WeaponState.Windup:
                    if(p.WeaponTime>=sw.Windup) { p.Weapon=WeaponState.Active; p.WeaponTime-=sw.Windup; }
                    break;
                case WeaponState.Active:
                    p.Position+=p.Forward*(sw.Lunge/sw.Active)*dt; Bounds(ref p);
                    if(p.WeaponTime>=sw.Active) { p.Weapon=WeaponState.Recovery; p.WeaponTime-=sw.Active; }
                    break;
                case WeaponState.Recovery:
                    if(p.Queued && p.WeaponTime>=sw.Recovery*F(35)) StartSwing(ref p,p.Swing+1,MatchState.Player(ref s,1-id).Position,input.Targeted);
                    else if(guard && p.WeaponTime>=sw.Recovery*F(50)) EnterGuard(ref p);
                    else if(p.WeaponTime>=sw.Recovery) { p.Weapon=WeaponState.Idle; p.IdleSince=p.BodyTime; }
                    break;
                case WeaponState.Guard:
                    if(!guard || p.Staggered) { p.Weapon=WeaponState.Idle; p.GuardReleasedAt=p.BodyTime; }
                    break;
                case WeaponState.Recoil:
                    if(p.WeaponTime>=p.RecoilFor) { p.Weapon=WeaponState.Idle; p.IdleSince=-9; }
                    break;
                case WeaponState.Plunge:
                    if(p.PlungePhase==2 && p.WeaponTime>=p.StuckFor) { p.Weapon=WeaponState.Idle; p.IdleSince=-9; }
                    break;
            }
            if(p.Weapon==WeaponState.Idle && guard && !p.Staggered) EnterGuard(ref p);
            p.SuperArmor=p.Config.Style==Style.Greatsword && (p.Weapon==WeaponState.Windup || p.Weapon==WeaponState.Active);
        }
        static void StartSwing(ref PlayerState p,int index,FixVec3 target,bool selected)
        {
            p.Swing=index; p.Weapon=WeaponState.Windup; p.WeaponTime=0; p.Queued=false; p.Struck=false;
            FixVec3 d=(target-p.Position).Flat;
            if(d.LengthSquared>F(1) && d.Length<7 && (selected || d.Length<F(350) &&
                FixVec3.Dot(p.Forward,d.Normalized)>Fix.Cos(Fix.Degrees(75)))) p.Yaw=Fix.Atan2(d.X,d.Z);
        }
        static void EnterGuard(ref PlayerState p) {
            p.Weapon=WeaponState.Guard; p.WeaponTime=0; p.GuardSince=p.BodyTime;
            p.ParryArmed=p.BodyTime-p.GuardReleasedAt>F(35); p.Queued=false;
        }
        static bool Swinging(PlayerState p) => p.Weapon==WeaponState.Active || p.Weapon==WeaponState.Plunge && p.PlungePhase==1;
        public static void Recoil(ref PlayerState p,Fix seconds) {
            p.Weapon=WeaponState.Recoil; p.WeaponTime=0; p.RecoilFor=seconds; p.Queued=false; p.IdleSince=-9; p.SuperArmor=false;
        }
        public static void Stagger(ref MatchState s,int id,Fix duration,EventBuffer events) {
            ref PlayerState p=ref MatchState.Player(ref s,id); bool fresh=!p.Staggered;
            p.StaggerUntil=Fix.Max(p.StaggerUntil,p.BodyTime+duration); p.Poise=p.Config.MaxPoise;
            if(fresh) events.Add(s.Tick,EventKind.Stagger,1-id,id,p.Position,FixVec3.Zero);
        }
        public static void Knock(ref PlayerState p,FixVec3 impulse) {
            impulse*=p.Config.KnockbackScale; p.Knock+=impulse.Flat;
            if(impulse.Y>F(1)) { p.KnockUp=Fix.Max(p.KnockUp,impulse.Y); p.KnockAirborne=true; }
        }
        static void Stop(ref MatchState s,Fix seconds) {
            int ticks=(seconds*60).Ceil;
            s.P0.HitStop=System.Math.Max(s.P0.HitStop,ticks); s.P1.HitStop=System.Math.Max(s.P1.HitStop,ticks);
        }
        static Fix Damage(ref MatchState s,int attacker,int target,Fix damage,Fix poise,FixVec3 knock,FixVec3 at,EventBuffer events)
        {
            ref PlayerState p=ref MatchState.Player(ref s,target);
            if(p.Invulnerable) return 0;
            damage=Fix.Max(0,damage)*100/(100+Fix.Max(0,p.Config.Defense));
            p.Health=Fix.Max(0,p.Health-damage); p.LastHitAt=p.BodyTime;
            Fix push=1;
            if(p.SuperArmor) { poise*=F(25); push=F(30); }
            if(poise>0) {
                p.Poise-=poise;
                if(p.Poise<=0) { Stagger(ref s,target,F(70),events); push=F(140); }
            }
            Knock(ref p,knock*push);
            return damage;
        }
        static ulong NextRandom(ref MatchState s) {
            ulong x=s.Rng; x^=x<<13; x^=x>>7; x^=x<<17; s.Rng=x; return x;
        }
        public static bool Guards(PlayerState p,FixVec3 from) {
            FixVec3 d=(from-p.Position).Flat;
            return p.Weapon==WeaponState.Guard && !p.Staggered &&
                (d.LengthSquared<Fix.Epsilon || FixVec3.Dot(p.Forward,d.Normalized)>=Fix.Cos(Fix.Degrees(75)));
        }
        /// <summary>Contacto real da lâmina: usado pelo varrimento e pelos testes de regras.</summary>
        public static void Strike(ref MatchState s,int attacker,FixVec3 point,EventBuffer events)
        {
            int target=1-attacker;
            ref PlayerState a=ref MatchState.Player(ref s,attacker);
            ref PlayerState b=ref MatchState.Player(ref s,target);
            if(a.Struck) return;
            a.Struck=true;
            if(b.Invulnerable) return;
            SwingData sw=a.Weapon==WeaponState.Plunge?SwordData.Plunge:SwordData.Get(a.Config.Style,a.Swing);
            FixVec3 away=(b.Position-a.Position).Flat;
            if(away.LengthSquared<F(1,10000)) away=a.Forward;
            FixVec3 dir=away.Normalized;
            if(Guards(b,a.Position))
            {
                Fix window=b.Config.ParryWindow+(b.Config.Style==Style.Fencer?F(8):Fix.Zero);
                if(b.ParryArmed && b.BodyTime-b.GuardSince<=window)
                {
                    a.Weapon=WeaponState.Idle; a.IdleSince=-9; a.Queued=false; a.SuperArmor=false;
                    Stagger(ref s,attacker,F(110),events); Knock(ref a,-dir*3); Stop(ref s,F(12));
                    events.Add(s.Tick,EventKind.Parry,attacker,target,point,-dir,visualStop:F(12));
                }
                else if(a.Config.Style==Style.Greatsword && a.Swing==SwordData.Count(a.Config.Style)-1)
                {
                    b.Weapon=WeaponState.Idle;
                    Fix d=Damage(ref s,attacker,target,sw.Damage*a.Config.DamageScale*F(50),0,dir*sw.Push*F(60),point,events);
                    Stagger(ref s,target,F(120),events); Stop(ref s,F(12));
                    events.Add(s.Tick,EventKind.GuardBreak,attacker,target,point,dir,d,visualStop:F(12));
                }
                else
                {
                    bool was=b.Staggered;
                    Fix d=Damage(ref s,attacker,target,sw.Damage*a.Config.DamageScale*(1-b.Config.BlockReduction),
                        sw.Stagger*a.Config.StaggerScale*F(60),dir*sw.Push*F(45),point,events);
                    bool broken=!was && b.Staggered;
                    if(broken) b.Weapon=WeaponState.Idle;
                    if(a.Config.Style!=Style.Greatsword) Recoil(ref a,F(22));
                    Stop(ref s,F(5));
                    events.Add(s.Tick,broken?EventKind.GuardBreak:EventKind.Block,attacker,target,point,dir,d,visualStop:F(5));
                }
                return;
            }
            bool crit=Fix.FromRaw((long)(NextRandom(ref s)>>32))<a.Config.CritChance;
            Fix push=sw.Push;
            if(push<0) push*=Fix.Clamp01((away.Length-F(140))/F(60));
            Fix dealt=Damage(ref s,attacker,target,sw.Damage*a.Config.DamageScale*(crit?a.Config.CritDamage:Fix.One),
                sw.Stagger*a.Config.StaggerScale,dir*push+FixVec3.Up*sw.Lift,point,events);
            if(b.Attacking && b.Staggered) { b.Weapon=WeaponState.Idle; b.Queued=false; b.IdleSince=-9; b.SuperArmor=false; }
            Stop(ref s,sw.HitStop);
            events.Add(s.Tick,EventKind.Hit,attacker,target,point,dir,dealt,crit,sw.HitStop);
        }
        public static void Clash(ref MatchState s,int attacker,FixVec3 at,EventBuffer events)
        {
            FixVec3 apart=(s.P0.Position-s.P1.Position).Flat;
            apart=apart.LengthSquared>F(1,10000)?apart.Normalized:-s.P0.Forward;
            Recoil(ref s.P0,F(42)); Recoil(ref s.P1,F(42));
            Knock(ref s.P0,apart*F(350)); Knock(ref s.P1,-apart*F(350)); Stop(ref s,F(14));
            events.Add(s.Tick,EventKind.Clash,attacker,1-attacker,at,apart,visualStop:F(14));
        }
        static void Sweep(ref MatchState s,int id,EventBuffer events)
        {
            ref PlayerState a=ref MatchState.Player(ref s,id);
            if(!Swinging(a)) { a.LastU=0; a.HasLastBlade=false; return; }
            ref PlayerState b=ref MatchState.Player(ref s,1-id);
            bool plunge=a.Weapon==WeaponState.Plunge;
            Fix u=plunge?Fix.Zero:SwordData.ArcU(a),u0=a.LastU;
            int steps=plunge?(a.HasLastBlade?4:1):System.Math.Max(1,System.Math.Min(16,(Fix.Abs(u-u0)*40).Ceil));
            SwordGeometry.Blade(a,u,out FixVec3 nowA,out FixVec3 nowB);
            for(int step=1;step<=steps;step++)
            {
                Fix k=Fix.Ratio(step,steps); FixVec3 b0,b1;
                if(plunge) {
                    b0=a.HasLastBlade?FixVec3.Lerp(a.LastBladeA,nowA,k):nowA;
                    b1=a.HasLastBlade?FixVec3.Lerp(a.LastBladeB,nowB,k):nowB;
                } else SwordGeometry.Blade(a,Fix.Lerp(u0,u,k),out b0,out b1);
                if(Swinging(b))
                {
                    SwordGeometry.Blade(b,b.Weapon==WeaponState.Plunge?Fix.Zero:SwordData.ArcU(b),out FixVec3 q0,out FixVec3 q1);
                    if(SwordGeometry.SegmentDistance(b0,b1,q0,q1,out FixVec3 at)<=F(14)) {
                        Clash(ref s,id,at,events); return;
                    }
                }
                if(!a.Struck && SwordGeometry.CapsuleDistance(b,b0,b1,out FixVec3 hit)<=F(12))
                {
                    Strike(ref s,id,hit,events);
                    if(!Swinging(a)) return;
                }
            }
            a.LastU=plunge?Fix.Zero:u; a.LastBladeA=nowA; a.LastBladeB=nowB; a.HasLastBlade=true;
        }
        static partial void PlungeImpact(ref MatchState s,int id,EventBuffer events)
        {
            ref PlayerState a=ref MatchState.Player(ref s,id); ref PlayerState b=ref MatchState.Player(ref s,1-id);
            FixVec3 stab=a.Position+a.Forward*F(50),center=stab+FixVec3.Up*F(40);
            FixVec3 at=SwordGeometry.ClosestPoint(b,center); Fix distance=(at-center).Length;
            if(distance<=a.Config.PlungeRadius && !b.Invulnerable) {
                Fix k=Fix.Lerp(1,F(40),distance/Fix.Max(a.Config.PlungeRadius,F(1)));
                FixVec3 away=(b.Position-center).Flat;
                if(away.LengthSquared<F(1,10000)) away=a.Forward;
                Fix dealt=Damage(ref s,id,1-id,a.Config.PlungeDamage*a.Config.DamageScale*k,55*a.Config.StaggerScale*k,
                    away.Normalized*5*k+FixVec3.Up*3*k,at,events);
                events.Add(s.Tick,EventKind.Hit,id,1-id,at,away.Normalized,dealt,visualStop:F(9));
            }
            Stop(ref s,F(9));
        }
    }
}

