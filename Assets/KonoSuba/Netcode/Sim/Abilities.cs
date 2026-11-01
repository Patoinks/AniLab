namespace AnimLab.Netcode.Sim
{
    /// <summary>Habilidades da hotbar (mesmos nomes das do offline).</summary>
    public enum AbilityId : byte { None, Dash, FireBall, WaterBall, WindBall, Lightning, Thunder, Kirin, Summon }
    /// <summary>Mesmos índices do AbilityElement do offline.</summary>
    public enum Element : byte { Neutral, Water, Lightning, Fire, Air, Earth, Ice, Dark }
    /// <summary>Mesmos índices do StatusType do offline.</summary>
    public enum Status : byte { None, Wet, Static, Grounded, Frozen, Asphyxia, Burning, Blind }

    /// <summary>
    /// Parâmetros das habilidades, lidos dos componentes da cena no início da partida (a mesma fonte do offline)
    /// e iguais para todos os lutadores. Os valores por omissão são os do código dos componentes.
    /// </summary>
    public struct AbilityTable
    {
        public Fix DashMana, DashCooldown, DashDistance, DashDuration;
        public Fix FireMana, FireCooldown, FireFormation, FireRadius, FireSpeed, FireGravity, FireRange, FireDamage, FireBlast;
        public FixVec3 FireShoulder;
        public static AbilityTable Default => new AbilityTable {
            DashMana = 12, DashCooldown = Fix.Ratio(12,10), DashDistance = Fix.Ratio(45,10), DashDuration = Fix.Ratio(16,100),
            FireMana = 25, FireCooldown = Fix.Ratio(15,10), FireFormation = Fix.Ratio(2,10), FireRadius = Fix.Ratio(52,100),
            FireSpeed = 20, FireGravity = Fix.Ratio(24,10), FireRange = 36, FireDamage = 60, FireBlast = Fix.Ratio(22,10),
            FireShoulder = new FixVec3(Fix.Ratio(85,100), Fix.Ratio(225,100), Fix.Ratio(55,100))
        };
        public Fix Mana(AbilityId a) => a == AbilityId.Dash ? DashMana : a == AbilityId.FireBall ? FireMana : Fix.Zero;
        public Fix Cooldown(AbilityId a) => a == AbilityId.Dash ? DashCooldown : a == AbilityId.FireBall ? FireCooldown : Fix.Zero;
        /// <summary>Habilidades já portadas para a simulação (as outras ainda não lançam online).</summary>
        public static bool Simulated(AbilityId a) => World.HasRule(a);
    }

    /// <summary>Projétil em voo (bola de fogo, …). Posições fixas no estado: Q0..Q15.</summary>
    public struct ProjectileState
    {
        public AbilityId Kind;
        public sbyte Owner, Target;
        public bool Launched;
        public FixVec3 Position, Velocity, Aim;
        public Fix Age, Travelled, Formation, Power;
    }

    public static partial class World
    {
        static readonly Fix Edge = Fix.Ratio(40,100);

        // Regras das habilidades, registadas por AbilityId (ver Docs/Online_Rules.md):
        //   Cast: no lançamento (validações, criação de projétil/efeito); devolve false se não lançou (sem gastar).
        //   Tick: cada tick de um projétil desse tipo (voo, colisões, impacto).
        delegate bool CastRule(ref MatchState s,int id,PlayerInput input,EventBuffer events);
        delegate void TickRule(ref MatchState s,int slot,EventBuffer events);
        static readonly CastRule[] castRules=new CastRule[16];
        static readonly TickRule[] tickRules=new TickRule[16];
        static void Register(AbilityId a,CastRule cast,TickRule tick=null) { castRules[(int)a]=cast; tickRules[(int)a]=tick; }
        static World()
        {
            Register(AbilityId.Dash,CastDash);
            Register(AbilityId.FireBall,CastFireBall,TickFireBall);
        }
        public static bool HasRule(AbilityId a) => (int)a<castRules.Length && castRules[(int)a]!=null;

        /// <summary>Habilidade pedida neste tick: tem de ser conhecida; gasta mana, arma a recarga (por habilidade) e lança.</summary>
        static void TickAbility(ref MatchState s,int id,PlayerInput input,EventBuffer events)
        {
            var a=(AbilityId)input.Cast;
            if(a==AbilityId.None || !AbilityTable.Simulated(a)) return;
            ref PlayerState p=ref MatchState.Player(ref s,id);
            if(!p.Config.Knows(a)) return;
            Fix now=Fix.FromInt(s.Tick)*Dt, mana=s.Abilities.Mana(a);
            if(p.Mana<mana || now<p.CooldownUntil((int)a-1)) return;
            if(!Cast(ref s,id,a,input,events)) return;
            ref PlayerState q=ref MatchState.Player(ref s,id);
            q.Mana-=mana;
            q.SetCooldownUntil((int)a-1,now+s.Abilities.Cooldown(a)*(1-q.Config.CooldownReduction));
        }

        static bool Cast(ref MatchState s,int id,AbilityId a,PlayerInput input,EventBuffer events) =>
            castRules[(int)a]!=null && castRules[(int)a](ref s,id,input,events);

        // ------------------------------------------------------------------ Dash
        // Na direção de WASD (ou para a frente, se parado); arranca no máximo e trava no fim.
        static bool CastDash(ref MatchState s,int id,PlayerInput input,EventBuffer events)
        {
            ref PlayerState p=ref MatchState.Player(ref s,id);
            FixVec3 wish=input.Wish;
            FixVec3 dir=wish.LengthSquared>F(1)?wish.Normalized:p.Forward.Flat.Normalized;
            if(dir.LengthSquared<F(1)) return false;
            Spend(ref p,SwordData.DashStamina);
            p.DashDir=dir; p.DashTime=0; p.Dashing=true; p.Yaw=Fix.Atan2(dir.X,dir.Z);
            events.Add(s.Tick,EventKind.Cast,id,id,p.Position,dir,ability:AbilityId.Dash);
            return true;
        }

        // ------------------------------------------------------------------ Bola de Fogo
        static bool CastFireBall(ref MatchState s,int id,PlayerInput input,EventBuffer events)
        {
            int slot=FreeProjectile(ref s);
            if(slot<0) return false;
            int target=input.TargetSlot>0?TargetOf(ref s,id,input):-1;
            ref PlayerState p=ref MatchState.Player(ref s,id);
            ref ProjectileState b=ref MatchState.Projectile(ref s,slot);
            b=new ProjectileState { Kind=AbilityId.FireBall,Owner=(sbyte)id,Target=(sbyte)target,Aim=input.Aim,
                Formation=Fix.Max(F(5),s.Abilities.FireFormation/Fix.Max(F(10),p.Config.CastSpeed)),Power=p.Config.SpellPower };
            b.Position=HoldPoint(ref s,id);
            events.Add(s.Tick,EventKind.Cast,id,slot,b.Position,p.Forward,ability:AbilityId.FireBall);
            return true;
        }

        static FixVec3 HoldPoint(ref MatchState s,int owner)
        {
            ref PlayerState p=ref MatchState.Player(ref s,owner);
            FixVec3 o=s.Abilities.FireShoulder;
            return p.Position+FixVec3.RotateY(new FixVec3(o.X,0,o.Z),p.Yaw)+FixVec3.Up*o.Y;
        }
        static int FreeProjectile(ref MatchState s)
        {
            for(int i=0;i<MatchState.ProjectileCapacity;i++) if(MatchState.Projectile(ref s,i).Kind==AbilityId.None) return i;
            return -1;
        }

        /// <summary>Dash em curso: desloca por cima do movimento normal, como a corrotina do offline.</summary>
        static void TickDash(ref MatchState s,int id,EventBuffer events)
        {
            ref PlayerState p=ref MatchState.Player(ref s,id);
            if(!p.Dashing) return;
            Fix duration=s.Abilities.DashDuration;
            p.DashTime+=Dt;
            Fix k=Fix.Clamp01(p.DashTime/duration);
            p.Position+=p.DashDir*(s.Abilities.DashDistance/duration*2*(1-k))*Dt;
            Bounds(ref p);
            if(p.DashTime>=duration) { p.Dashing=false; events.Add(s.Tick,EventKind.Cast,id,id,p.Position,p.DashDir,ability:AbilityId.Dash,other:1); }
        }

        /// <summary>Cada projétil avança pela regra do seu tipo; um dono que saiu da partida apaga-o.</summary>
        static void TickProjectiles(ref MatchState s,EventBuffer events)
        {
            for(int i=0;i<MatchState.ProjectileCapacity;i++)
            {
                ref ProjectileState b=ref MatchState.Projectile(ref s,i);
                if(b.Kind==AbilityId.None) continue;
                if(b.Owner<0 || b.Owner>=s.Count || tickRules[(int)b.Kind]==null) { b=default; continue; }
                tickRules[(int)b.Kind](ref s,i,events);
            }
        }
        /// <summary>Formação no ombro, voo balístico com varrimento, explosão no impacto.</summary>
        static void TickFireBall(ref MatchState s,int i,EventBuffer events)
        {
            ref ProjectileState b=ref MatchState.Projectile(ref s,i);
            b.Age+=Dt;
            if(!b.Launched)
            {
                b.Position=HoldPoint(ref s,b.Owner);
                if(b.Age<b.Formation) return;
                FixVec3 to=b.Target>=0 && b.Target<s.Count ? AimPoint(ref s,b.Target) : b.Aim;
                FixVec3 delta=to-b.Position;
                if(delta.LengthSquared<F(1)) delta=MatchState.Player(ref s,b.Owner).Forward;
                Fix flight=Fix.Max(delta.Length/Fix.Max(1,s.Abilities.FireSpeed),F(5));
                b.Velocity=delta/flight+FixVec3.Up*(F(50)*s.Abilities.FireGravity*flight);
                b.Launched=true;
                // O primeiro passo de voo usa só o tempo que sobrou da formação.
                Fix rest=Fix.Min(Dt,b.Age-b.Formation);
                if(rest>Fix.Zero) FlyFireBall(ref s,i,rest,events);
                return;
            }
            FlyFireBall(ref s,i,Dt,events);
        }
        static FixVec3 AimPoint(ref MatchState s,int id)
        {
            ref PlayerState p=ref MatchState.Player(ref s,id);
            return p.Position+FixVec3.Up*p.Config.AimHeight;
        }

        static void FlyFireBall(ref MatchState s,int i,Fix dt,EventBuffer events)
        {
            ref ProjectileState b=ref MatchState.Projectile(ref s,i);
            Fix radius=s.Abilities.FireRadius;
            // Encostado a um corpo (o varrimento não deteta sobreposições iniciais).
            int touching=Nearest(ref s,b.Owner,b.Position,b.Position,radius,out _);
            if(touching>=0) { Explode(ref s,i,touching,events); return; }
            FixVec3 g=FixVec3.Up*-s.Abilities.FireGravity;
            FixVec3 step=b.Velocity*dt+g*(F(50)*dt*dt);
            b.Velocity+=g*dt;
            Fix distance=step.Length;
            if(distance<=Fix.Epsilon) return;
            int hit=Nearest(ref s,b.Owner,b.Position,b.Position+step,radius,out Fix along);
            if(hit>=0) { b.Position+=step*along; Explode(ref s,i,hit,events); return; }
            b.Position+=step; b.Travelled+=distance;
            // Chão/água em y=0; fora do alcance, apaga-se sem explodir.
            if(b.Position.Y<=radius) { Explode(ref s,i,-1,events); return; }
            if(b.Travelled>=s.Abilities.FireRange || b.Age>5) { events.Add(s.Tick,EventKind.Fizzle,b.Owner,i,b.Position,b.Velocity,ability:b.Kind); b=default; }
        }

        /// <summary>O corpo (exceto o dono) que a esfera a→b toca primeiro; along = fração do percurso até ao contacto.</summary>
        static int Nearest(ref MatchState s,int owner,FixVec3 a,FixVec3 b,Fix radius,out Fix along)
        {
            int best=-1; along=Fix.One; Fix bestT=Fix.One+Fix.One;
            for(int j=0;j<s.Count;j++)
            {
                if(!MatchState.Hostile(ref s,owner,j)) continue;
                ref PlayerState q=ref MatchState.Player(ref s,j);
                Fix half=Fix.Max(0,q.Config.Height/2-q.Config.Radius);
                FixVec3 c=q.Position+FixVec3.Up*q.Config.CenterY;
                Fix d=SwordGeometry.SegmentDistance(a,b,c-FixVec3.Up*half,c+FixVec3.Up*half,out FixVec3 at);
                if(d>radius+q.Config.Radius) continue;
                FixVec3 ab=b-a; Fix len=ab.LengthSquared;
                Fix t=len>Fix.Epsilon?Fix.Clamp01(FixVec3.Dot(at-a,ab)/len):Fix.Zero;
                if(t<bestT) { bestT=t; best=j; }
            }
            if(best>=0) along=bestT;
            return best;
        }

        /// <summary>Combat.Explode: dano a cair até 40% na borda, knockback para fora; o atingido em cheio leva tudo.</summary>
        static void Explode(ref MatchState s,int i,int direct,EventBuffer events)
        {
            ProjectileState b=MatchState.Projectile(ref s,i);
            FixVec3 center=b.Position, push=b.Velocity.Flat;
            Fix radius=s.Abilities.FireBlast, pushXZ=6, lift=F(250);
            FixVec3 pushDir=push.Normalized;
            events.Add(s.Tick,EventKind.Impact,b.Owner,i,center,b.Velocity,ability:b.Kind,other:direct);
            for(int j=0;j<s.Count;j++)
            {
                if(!MatchState.Hostile(ref s,b.Owner,j)) continue;
                FixVec3 closest=SwordGeometry.ClosestPoint(MatchState.Player(ref s,j),center);
                Fix d=j==direct?Fix.Zero:(closest-center).Length;
                if(d>radius) continue;
                Fix k=Fix.Lerp(1,Edge,d/Fix.Max(radius,F(1)));
                FixVec3 away=(MatchState.Player(ref s,j).Position-center).Flat;
                if(away.LengthSquared<F(1,10000)) away=pushDir;
                FixVec3 knock=away.Normalized*(pushXZ*k)+FixVec3.Up*(lift*k);
                Fix dealt=Damage(ref s,b.Owner,j,s.Abilities.FireDamage*b.Power*k,40*k,knock,closest,events,Element.Fire);
                if(!MatchState.Player(ref s,j).Invulnerable || dealt>Fix.Zero)
                    ApplyStatus(ref s,j,Status.Burning,4,events);
                events.Add(s.Tick,EventKind.Hit,b.Owner,j,closest,away.Normalized,dealt,visualStop:Fix.Zero,ability:b.Kind,element:Element.Fire);
            }
            MatchState.Projectile(ref s,i)=default;
        }

        /// <summary>Combatant.ApplyStatus: a resistência encurta; se já está ativo, só renova.</summary>
        public static void ApplyStatus(ref MatchState s,int id,Status type,Fix duration,EventBuffer events)
        {
            ref PlayerState p=ref MatchState.Player(ref s,id);
            if(p.Invulnerable) return;
            duration*=1-Fix.Clamp(p.Config.StatusResist(type),0,F(80));
            if(duration<=F(5)) return;
            bool fresh=p.StatusLeft(type)<=Fix.Zero;
            p.SetStatus(type,fresh?duration:Fix.Max(p.StatusLeft(type),duration),fresh?duration:Fix.Max(p.StatusLeft(type),duration));
            if(fresh) events.Add(s.Tick,EventKind.StatusApplied,id,id,p.Position,FixVec3.Up,status:type);
        }
        static void TickStatuses(ref PlayerState p)
        {
            for(int t=1;t<=7;t++)
            {
                Fix left=p.StatusLeft((Status)t);
                if(left>Fix.Zero) p.SetStatus((Status)t,Fix.Max(0,left-Dt),p.StatusDuration((Status)t));
            }
        }
    }
}
