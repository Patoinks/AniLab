namespace AnimLab.Netcode.Sim
{
    public static partial class World
    {
        static partial void AfterMovement(ref MatchState s, PlayerInput[] input, int frozen, EventBuffer events)
        {
            for (int i = 0; i < s.Count; i++)
                if ((frozen & 1 << i) == 0) { TickBody(ref s,i,events); TickWeapon(ref s,i,input[i]); }
            for (int i = 0; i < s.Count; i++)
                if ((frozen & 1 << i) == 0) Sweep(ref s,i,events);
            Separate(ref s,frozen);
        }
        static void TickBody(ref MatchState s,int id,EventBuffer events)
        {
            ref PlayerState p=ref MatchState.Player(ref s,id);
            if (p.Health>0 && p.Health<p.Config.MaxHealth) p.Health=Fix.Min(p.Config.MaxHealth,p.Health+p.Config.HealthRegen*Dt);
            if (p.Config.Refill && p.Health<p.Config.MaxHealth && p.BodyTime-p.LastHitAt>p.Config.RefillDelay)
                p.Health=Fix.Min(p.Config.MaxHealth,p.Health+p.Config.MaxHealth*p.Config.RefillRate*Dt);
            if (p.Poise<p.Config.MaxPoise && p.BodyTime-p.LastHitAt>F(150)) p.Poise=Fix.Min(p.Config.MaxPoise,p.Poise+60*Dt);
            if (p.Mana<p.Config.MaxMana) p.Mana=Fix.Min(p.Config.MaxMana,p.Mana+p.Config.ManaRegen*Dt);
            // Stamina: só recupera depois de uma pausa sem gastar (mais longa se esgotou); a guardar, a metade.
            if (p.Stamina<p.Config.MaxStamina && p.BodyTime-p.StaminaUsedAt>(p.HasStamina?SwordData.StaminaDelay:SwordData.ExhaustedDelay))
                p.Stamina=Fix.Min(p.Config.MaxStamina,p.Stamina+p.Config.StaminaRegen*(p.Weapon==WeaponState.Guard?F(50):Fix.One)*Dt);
            TickStatuses(ref p);
            FixVec3 step=p.Knock*Dt;
            if (p.KnockAirborne) { p.KnockUp+=F(-981,100)*Dt; step.Y=p.KnockUp*Dt; }
            p.Position+=step;
            if (p.KnockAirborne && p.Position.Y<=0) {
                p.Position.Y=0; p.KnockAirborne=false; p.KnockUp=0;
                events.Add(s.Tick,EventKind.Land,id,id,p.Position,FixVec3.Up);
            }
            p.Knock=FixVec3.MoveTowards(p.Knock,FixVec3.Zero,(p.KnockAirborne?2:p.Weapon==WeaponState.Recoil?SwordData.RecoilBrake:12)*Dt);
            Bounds(ref p);
        }
        static void TickWeapon(ref MatchState s,int id,PlayerInput input)
        {
            int targetId=TargetOf(ref s,id,input);
            bool hasTarget=targetId>=0;
            FixVec3 target=hasTarget?MatchState.Player(ref s,targetId).Position:FixVec3.Zero;
            ref PlayerState p=ref MatchState.Player(ref s,id);
            bool busy=p.Motion==Motion.Roll || p.Motion==Motion.Plunge || p.BodyTime<p.LandLagUntil;
            bool guard=input.Has(Buttons.Guard) && !busy && !p.Hurt;
            if (input.Has(Buttons.Attack) && p.Motion==Motion.Ground && !busy && !p.Hurt)
            {
                // Na pausa do fim do combo, o clique perde-se; a meio do golpe, só conta da metade em diante.
                if (p.Weapon==WeaponState.Idle || p.Weapon==WeaponState.Guard) {
                    if (!Resting(p)) {
                        if (p.Weapon==WeaponState.Guard) p.GuardReleasedAt=p.BodyTime;
                        int next=p.BodyTime-p.IdleSince<F(25) && p.Swing<SwordData.Count(p.Config.Style)-1?p.Swing+1:0;
                        StartSwing(ref p,next,hasTarget,target,input.Targeted);
                    }
                }
                else if (CanQueue(p)) p.Queued=p.Swing<SwordData.Count(p.Config.Style)-1;
            }
            if (p.Staggered && p.Weapon!=WeaponState.Idle) { p.Weapon=WeaponState.Idle; p.IdleSince=-9; }
            Fix dt=Dt*(p.Attacking?SwingRate(p):Fix.One);
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
                {
                    // Golpe falhado (não tocou em ninguém): recuperação inteira, sem cortar para o golpe seguinte nem para a guarda.
                    bool whiff=p.StruckMask==0;
                    if(p.Queued && p.WeaponTime>=sw.Recovery*(whiff?Fix.One:F(35))) StartSwing(ref p,p.Swing+1,hasTarget,target,input.Targeted);
                    else if(guard && !whiff && p.WeaponTime>=sw.Recovery*F(50)) { EndSwing(ref p); EnterGuard(ref p); }
                    else if(p.WeaponTime>=sw.Recovery) { p.Weapon=WeaponState.Idle; EndSwing(ref p); }
                    break;
                }
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
        /// <summary>O clique para o golpe seguinte conta (da metade da fase ativa em diante).</summary>
        public static bool CanQueue(in PlayerState p) => p.Weapon==WeaponState.Recovery ||
            p.Weapon==WeaponState.Active && p.WeaponTime>=SwordData.Get(p.Config.Style,p.Swing).Active*SwordData.QueueFrom;
        /// <summary>Pausa depois do último golpe do combo: ainda não pode recomeçar.</summary>
        public static bool Resting(in PlayerState p) =>
            p.Swing==SwordData.Count(p.Config.Style)-1 && p.BodyTime-p.IdleSince<SwordData.ComboRest;
        /// <summary>Gasta stamina (nunca abaixo de zero) e adia a recuperação.</summary>
        public static void Spend(ref PlayerState p,Fix amount) { p.Stamina=Fix.Max(0,p.Stamina-amount); p.StaminaUsedAt=p.BodyTime; }
        // O golpe acabou (para o encadeamento e para a pausa do fim do combo).
        static void EndSwing(ref PlayerState p) { p.IdleSince=p.BodyTime; }
        /// <summary>Ritmo do golpe em curso: Attack Speed, e mais devagar se começou sem stamina.</summary>
        public static Fix SwingRate(in PlayerState p) =>
            Fix.Max(F(10),p.Config.AttackSpeed)/(p.TiredSwing?SwordData.TiredSlowdown:Fix.One);
        static void StartSwing(ref PlayerState p,int index,bool hasTarget,FixVec3 target,bool selected)
        {
            Fix cost=SwordData.Get(p.Config.Style,index).Stamina;
            p.TiredSwing=p.Stamina<cost;
            Spend(ref p,cost);
            p.Swing=index; p.Weapon=WeaponState.Windup; p.WeaponTime=0; p.Queued=false; p.StruckMask=0;
            if(!hasTarget) return;
            FixVec3 d=(target-p.Position).Flat;
            if(d.LengthSquared>F(1) && d.Length<7 && (selected || d.Length<F(350) &&
                FixVec3.Dot(p.Forward,d.Normalized)>Fix.Cos(Fix.Degrees(75)))) p.Yaw=Fix.Atan2(d.X,d.Z);
        }
        static void EnterGuard(ref PlayerState p) {
            p.Weapon=WeaponState.Guard; p.WeaponTime=0; p.GuardSince=p.BodyTime;
            p.ParryArmed=p.BodyTime-p.GuardReleasedAt>F(35); p.Queued=false;
        }
        /// <summary>Tempo real desde que a lâmina começou a cortar (o pique conta sempre como a tempo).</summary>
        static Fix SinceActive(in PlayerState p) => p.Weapon==WeaponState.Plunge?Fix.Zero:p.WeaponTime/SwingRate(p);
        /// <summary>As duas lâminas começaram a cortar quase ao mesmo tempo: só aí o cruzamento dá clash.</summary>
        public static bool InSync(in PlayerState a,in PlayerState b) =>
            a.Weapon==WeaponState.Plunge || b.Weapon==WeaponState.Plunge || Fix.Abs(SinceActive(a)-SinceActive(b))<=SwordData.ClashWindow;
        static bool Swinging(in PlayerState p) => p.Weapon==WeaponState.Active || p.Weapon==WeaponState.Plunge && p.PlungePhase==1;
        public static void Recoil(ref PlayerState p,Fix seconds) {
            p.Weapon=WeaponState.Recoil; p.WeaponTime=0; p.RecoilFor=seconds; p.Queued=false; p.IdleSince=-9; p.SuperArmor=false;
        }
        /// <param name="by">Quem causou o stagger (origem do evento).</param>
        public static void Stagger(ref MatchState s,int id,Fix duration,EventBuffer events,int by=-1) {
            ref PlayerState p=ref MatchState.Player(ref s,id); bool fresh=!p.Staggered;
            p.StaggerUntil=Fix.Max(p.StaggerUntil,p.BodyTime+duration); p.Poise=p.Config.MaxPoise;
            if(fresh) events.Add(s.Tick,EventKind.Stagger,by>=0?by:id==0?1:0,id,p.Position,FixVec3.Zero);
        }
        public static void Knock(ref PlayerState p,FixVec3 impulse) {
            impulse*=p.Config.KnockbackScale; p.Knock+=impulse.Flat;
            if(impulse.Y>F(1)) { p.KnockUp=Fix.Max(p.KnockUp,impulse.Y); p.KnockAirborne=true; }
        }
        // Paragem de impacto: como o HitStop do offline (que para o tempo do jogo), congela todos os lutadores.
        static void Stop(ref MatchState s,Fix seconds) {
            int ticks=(seconds*60).Ceil;
            for(int i=0;i<s.Count;i++) { ref PlayerState p=ref MatchState.Player(ref s,i); p.HitStop=System.Math.Max(p.HitStop,ticks); }
        }
        static Fix Damage(ref MatchState s,int attacker,int target,Fix damage,Fix poise,FixVec3 knock,FixVec3 at,EventBuffer events,Element element=Element.Neutral)
        {
            ref PlayerState p=ref MatchState.Player(ref s,target);
            if(p.Invulnerable) return 0;
            // Combatant.Mitigate: defesa física ou mágica, e resistência ao elemento (até 80%).
            bool elemental=element!=Element.Neutral;
            damage=Fix.Max(0,damage)*100/(100+Fix.Max(0,elemental?p.Config.MagicDefense:p.Config.Defense));
            if(elemental) damage*=1-Fix.Clamp(p.Config.ElementResist(element),0,F(80));
            p.Health=Fix.Max(0,p.Health-damage); p.LastHitAt=p.BodyTime;
            Fix push=1;
            poise*=SwordData.StaggerTaken(p.Config.Style);
            if(p.SuperArmor) { poise*=F(25); push=F(30); }
            if(poise>0) {
                p.Poise-=poise;
                if(p.Poise<=0) { Stagger(ref s,target,F(70),events,attacker); push=F(140); }
            }
            Knock(ref p,knock*push);
            return damage;
        }
        static ulong NextRandom(ref MatchState s) {
            ulong x=s.Rng; x^=x<<13; x^=x>>7; x^=x<<17; s.Rng=x; return x;
        }
        public static bool Guards(in PlayerState p,FixVec3 from) {
            FixVec3 d=(from-p.Position).Flat;
            return p.Weapon==WeaponState.Guard && !p.Staggered &&
                (d.LengthSquared<Fix.Epsilon || FixVec3.Dot(p.Forward,d.Normalized)>=Fix.Cos(Fix.Degrees(75)));
        }
        /// <summary>Atalho 1v1 dos testes: o alvo é o outro lutador.</summary>
        public static void Strike(ref MatchState s,int attacker,FixVec3 point,EventBuffer events,FixVec3 run=default) =>
            Strike(ref s,attacker,attacker==0?1:0,point,events,run);
        /// <summary>Contacto real da lâmina: usado pelo varrimento e pelos testes de regras.</summary>
        public static void Strike(ref MatchState s,int attacker,int target,FixVec3 point,EventBuffer events,FixVec3 run=default)
        {
            ref PlayerState a=ref MatchState.Player(ref s,attacker);
            ref PlayerState b=ref MatchState.Player(ref s,target);
            if((a.StruckMask & 1<<target)!=0) return;
            a.StruckMask|=(ushort)(1<<target);
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
                    Stagger(ref s,attacker,SwordData.ParryStun,events,target); Knock(ref a,-dir*3); Stop(ref s,F(12));
                    events.Add(s.Tick,EventKind.Parry,attacker,target,point,-dir,visualStop:F(12));
                }
                else if(a.Config.Style==Style.Greatsword && a.Swing==SwordData.Count(a.Config.Style)-1)
                {
                    b.Weapon=WeaponState.Idle;
                    Fix d=Damage(ref s,attacker,target,sw.Damage*a.Config.DamageScale*F(50),0,dir*sw.Push*F(60),point,events);
                    Stagger(ref s,target,F(120),events,attacker); Stop(ref s,F(12));
                    events.Add(s.Tick,EventKind.GuardBreak,attacker,target,point,dir,d,visualStop:F(12),heavy:true);
                }
                else
                {
                    // Bloquear gasta stamina (não postura); quando acaba, a guarda parte.
                    Fix d=Damage(ref s,attacker,target,sw.Damage*a.Config.DamageScale*(1-b.Config.BlockReduction),
                        0,dir*sw.Push*F(45),point,events);
                    Spend(ref b,sw.Stagger*a.Config.StaggerScale*SwordData.BlockStamina);
                    bool broken=!b.HasStamina && !b.Staggered;
                    if(broken) { b.Weapon=WeaponState.Idle; Stagger(ref s,target,F(70),events,attacker); }
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
            Flinch(ref b);
            Stop(ref s,sw.HitStop);
            bool heavy=a.Weapon==WeaponState.Plunge || a.Swing==SwordData.Count(a.Config.Style)-1;
            events.Add(s.Tick,EventKind.Hit,attacker,target,point,dir,dealt,crit,sw.HitStop,run.Normalized+dir*F(50),heavy);
        }
        /// <summary>Um golpe entrou: o alvo perde o seu golpe e fica um instante sem agir (o super armor aguenta, salvo em stagger).</summary>
        static void Flinch(ref PlayerState b)
        {
            if(b.SuperArmor && !b.Staggered) return;
            b.HurtUntil=Fix.Max(b.HurtUntil,b.BodyTime+SwordData.Flinch*SwordData.StaggerTaken(b.Config.Style));
            if(b.Attacking && !(b.Weapon==WeaponState.Plunge && b.PlungePhase==2)) {
                b.Weapon=WeaponState.Idle; b.Queued=false; b.IdleSince=-9; b.SuperArmor=false;
            }
        }
        /// <summary>Atalho 1v1 dos testes.</summary>
        public static void Clash(ref MatchState s,int attacker,FixVec3 at,EventBuffer events) => Clash(ref s,attacker,attacker==0?1:0,at,events);
        /// <summary>As lâminas de detector e other cruzaram-se: anulam-se e ambos recuam.</summary>
        public static void Clash(ref MatchState s,int detector,int other,FixVec3 at,EventBuffer events)
        {
            int lo=System.Math.Min(detector,other),hi=System.Math.Max(detector,other);
            ref PlayerState a=ref MatchState.Player(ref s,lo); ref PlayerState b=ref MatchState.Player(ref s,hi);
            FixVec3 apart=(a.Position-b.Position).Flat;
            apart=apart.LengthSquared>F(1,10000)?apart.Normalized:-a.Forward;
            Recoil(ref a,SwordData.ClashRecoil); Recoil(ref b,SwordData.ClashRecoil);
            Knock(ref a,apart*SwordData.ClashPush); Knock(ref b,-apart*SwordData.ClashPush); Stop(ref s,F(14));
            // Direção do lado de quem detetou o clash, como no offline (as faíscas de cada cor saem para o seu lado).
            events.Add(s.Tick,EventKind.Clash,detector,other,at,detector==lo?apart:-apart,visualStop:F(14));
        }
        static void Sweep(ref MatchState s,int id,EventBuffer events)
        {
            ref PlayerState a=ref MatchState.Player(ref s,id);
            if(!Swinging(a)) { a.LastU=0; a.HasLastBlade=false; return; }
            bool plunge=a.Weapon==WeaponState.Plunge;
            Fix u=plunge?Fix.Zero:SwordData.ArcU(a),u0=a.LastU;
            int steps=plunge?(a.HasLastBlade?4:1):System.Math.Max(1,System.Math.Min(16,(Fix.Abs(u-u0)*40).Ceil));
            SwordGeometry.Blade(a,u,out FixVec3 nowA,out FixVec3 nowB);
            FixVec3 tip=a.HasLastBlade?a.LastBladeB:nowB;
            for(int step=1;step<=steps;step++)
            {
                Fix k=Fix.Ratio(step,steps); FixVec3 b0,b1;
                if(plunge) {
                    b0=a.HasLastBlade?FixVec3.Lerp(a.LastBladeA,nowA,k):nowA;
                    b1=a.HasLastBlade?FixVec3.Lerp(a.LastBladeB,nowB,k):nowB;
                } else SwordGeometry.Blade(a,Fix.Lerp(u0,u,k),out b0,out b1);
                for(int j=0;j<s.Count;j++)
                {
                    if(!MatchState.Hostile(ref s,id,j)) continue;
                    ref PlayerState b=ref MatchState.Player(ref s,j);
                    if(Swinging(b) && InSync(a,b))
                    {
                        SwordGeometry.Blade(b,b.Weapon==WeaponState.Plunge?Fix.Zero:SwordData.ArcU(b),out FixVec3 q0,out FixVec3 q1);
                        if(SwordGeometry.SegmentDistance(b0,b1,q0,q1,out FixVec3 at)<=F(14)) {
                            Clash(ref s,id,j,at,events); return;
                        }
                    }
                    if((a.StruckMask & 1<<j)==0 && SwordGeometry.CapsuleDistance(b,b0,b1,out FixVec3 hit)<=F(12))
                    {
                        Strike(ref s,id,j,hit,events,b1-tip);
                        if(!Swinging(a)) return;
                    }
                }
                tip=b1;
            }
            a.LastU=plunge?Fix.Zero:u; a.LastBladeA=nowA; a.LastBladeB=nowB; a.HasLastBlade=true;
        }
        static partial void PlungeImpact(ref MatchState s,int id,EventBuffer events)
        {
            for(int j=0;j<s.Count;j++)
            {
                if(!MatchState.Hostile(ref s,id,j)) continue;
                ref PlayerState a=ref MatchState.Player(ref s,id); ref PlayerState b=ref MatchState.Player(ref s,j);
                FixVec3 stab=a.Position+a.Forward*F(50),center=stab+FixVec3.Up*F(40);
                FixVec3 at=SwordGeometry.ClosestPoint(b,center); Fix distance=(at-center).Length;
                if(distance<=a.Config.PlungeRadius && !b.Invulnerable) {
                    Fix k=Fix.Lerp(1,F(40),distance/Fix.Max(a.Config.PlungeRadius,F(1)));
                    FixVec3 away=(b.Position-center).Flat;
                    if(away.LengthSquared<F(1,10000)) away=a.Forward;
                    Fix dealt=Damage(ref s,id,j,a.Config.PlungeDamage*a.Config.DamageScale*k,55*a.Config.StaggerScale*k,
                        away.Normalized*5*k+FixVec3.Up*3*k,at,events);
                    events.Add(s.Tick,EventKind.Hit,id,j,at,away.Normalized,dealt,visualStop:F(9),other:1);
                }
            }
            Stop(ref s,F(9));
        }
    }
}
