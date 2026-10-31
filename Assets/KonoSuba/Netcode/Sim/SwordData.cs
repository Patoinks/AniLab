namespace AnimLab.Netcode.Sim
{
    public readonly struct SwingData
    {
        public readonly Fix Windup, Active, Recovery, Damage, Stagger, Push, Lift, Lunge, HitStop;
        public SwingData(int windup, int active, int recovery, int damage, int stagger, int push, int lift, int lunge, int stop)
        {
            Windup = Fix.Ratio(windup,100); Active = Fix.Ratio(active,100); Recovery = Fix.Ratio(recovery,100);
            Damage = damage; Stagger = stagger; Push = Fix.Ratio(push,100); Lift = Fix.Ratio(lift,100);
            Lunge = Fix.Ratio(lunge,100); HitStop = Fix.Ratio(stop,100);
        }
    }
    public static class SwordData
    {
        static readonly SwingData[] knight = {
            new SwingData(12,13,30,18,30,220,0,30,6), new SwingData(12,13,30,20,35,260,0,30,6),
            new SwingData(18,14,42,34,70,700,300,55,11) };
        static readonly SwingData[] fencer = {
            new SwingData(8,10,22,13,18,120,0,45,4), new SwingData(8,10,24,13,18,120,0,45,4),
            new SwingData(16,16,36,28,45,350,0,240,9) };
        static readonly SwingData[] reaper = {
            new SwingData(20,20,32,22,30,-350,0,20,6), new SwingData(18,20,34,22,30,-350,0,20,6),
            new SwingData(28,32,45,32,55,-500,0,10,10) };
        static readonly SwingData[] daggers = {
            new SwingData(6,8,16,9,14,80,0,35,3), new SwingData(6,8,16,9,14,80,0,35,3),
            new SwingData(6,8,16,9,14,80,0,35,3), new SwingData(6,8,16,9,14,80,0,35,3),
            new SwingData(12,12,30,20,35,250,0,60,7) };
        static readonly SwingData[] greatsword = {
            new SwingData(32,20,45,38,60,400,0,35,10), new SwingData(28,22,50,38,60,500,0,30,10),
            new SwingData(42,18,60,55,100,700,250,45,14) };
        public static readonly SwingData Plunge = new SwingData(16,0,45,30,55,300,150,0,8);
        public static int Count(Style style) => style == Style.Daggers ? 5 : 3;
        public static SwingData Get(Style style, int i)
        {
            i = System.Math.Max(0, System.Math.Min(i, Count(style) - 1));
            switch (style) {
                case Style.Fencer: return fencer[i]; case Style.Reaper: return reaper[i];
                case Style.Daggers: return daggers[i]; case Style.Greatsword: return greatsword[i];
                default: return knight[i];
            }
        }
        public static Fix ArcU(PlayerState p) => 1 - Fix.Pow22(1 - Fix.Clamp01(p.WeaponTime / Get(p.Config.Style,p.Swing).Active));
    }

    public struct BladePose
    {
        public FixVec3 Hand, X, Y, Z;
        public FixVec3 Point(FixVec3 p) => Hand + X * p.X + Y * p.Y + Z * p.Z;
    }

    public static class SwordGeometry
    {
        static Fix F(long n, long d = 100) => Fix.Ratio(n,d);
        static FixVec3 V(int x, int y, int z) => new FixVec3(F(x),F(y),F(z));
        public static BladePose Make(FixVec3 hand, FixVec3 dir, FixVec3 edge)
        {
            FixVec3 y = dir.Normalized, x = edge - y * FixVec3.Dot(edge,y);
            if (x.LengthSquared < F(1,10000)) x = FixVec3.Right - y * y.X;
            x = x.Normalized;
            return new BladePose { Hand = hand, X = x, Y = y, Z = FixVec3.Cross(x,y) };
        }
        static BladePose Mirror(BladePose p) => Make(new FixVec3(-p.Hand.X,p.Hand.Y,p.Hand.Z),
            new FixVec3(-p.Y.X,p.Y.Y,p.Y.Z), new FixVec3(-p.X.X,p.X.Y,p.X.Z));

        public static BladePose Arc(Style style, int i, Fix u, Fix bodyRadius)
        {
            if (style == Style.Fencer)
            {
                FixVec3 from, to, d0, d1;
                if (i == 0) { from=V(20,128,2); to=V(10,134,62); d0=V(8,12,100); d1=V(0,2,100); }
                else if (i == 1) { from=V(24,105,0); to=V(8,98,60); d0=V(10,-5,100); d1=V(-2,-12,100); }
                else { from=V(30,132,-20); to=V(6,122,78); d0=V(15,20,100); d1=FixVec3.Forward; }
                Fix k = Fix.Clamp(u,F(-20),F(110));
                return Make(FixVec3.Lerp(from,to,k),FixVec3.Slerp(d0.Normalized,d1.Normalized,Fix.Clamp01(k)),FixVec3.Up);
            }
            if (style == Style.Reaper)
            {
                Fix a = Fix.Degrees(i == 0 ? Fix.Lerp(-40,220,u) : i == 1 ? Fix.Lerp(215,-45,u) : Fix.Lerp(-60,420,u));
                FixVec3 d = new FixVec3(Fix.Cos(a),i == 0 ? F(12) : i == 1 ? F(20) : F(6),Fix.Sin(a)).Normalized;
                FixVec3 t = new FixVec3(-Fix.Sin(a),0,Fix.Cos(a)) * (i == 1 ? -Fix.One : Fix.One);
                return Make(V(5,112,5)+d*F(45),d,t);
            }
            if (style == Style.Daggers)
            {
                if (i >= 4) return Make(FixVec3.Lerp(V(20,112,0),V(12,112,50),u),V(2,5,100),FixVec3.Up);
                Fix a = Fix.Degrees(Fix.Lerp(0,150,u));
                FixVec3 d = new FixVec3(Fix.Cos(a),i < 2 ? Fix.Lerp(F(15),F(-10),u) : Fix.Lerp(F(-25),F(25),u),Fix.Sin(a)).Normalized;
                var p = Make(V(12,115,5)+d*F(42),d,new FixVec3(-Fix.Sin(a),0,Fix.Cos(a)));
                return i % 2 == 1 ? Mirror(p) : p;
            }
            Fix arm = F(48) + (bodyRadius - F(30)) * F(150);
            if (i == 0) {
                Fix a = Fix.Degrees(Fix.Lerp(-35,205,u));
                FixVec3 d = new FixVec3(Fix.Cos(a),Fix.Lerp(F(5),F(-20),u),Fix.Sin(a)).Normalized;
                return Make(V(12,122,5)+d*arm,d,new FixVec3(-Fix.Sin(a),0,Fix.Cos(a)));
            }
            if (i == 1) {
                Fix a = Fix.Degrees(Fix.Lerp(200,-30,u));
                FixVec3 d = new FixVec3(Fix.Cos(a),Fix.Lerp(F(-45),F(40),u),Fix.Sin(a)).Normalized;
                return Make(V(5,115,5)+d*(arm+F(2)),d,new FixVec3(Fix.Sin(a),F(35),-Fix.Cos(a)));
            }
            {
                Fix a = Fix.Degrees(Fix.Lerp(-45,125,u));
                FixVec3 d = new FixVec3(F(12),Fix.Cos(a),Fix.Sin(a)).Normalized;
                return Make(V(14,138,5)+d*(arm+F(2)),d,new FixVec3(0,-Fix.Sin(a),Fix.Cos(a)));
            }
        }
        public static void Blade(PlayerState p, Fix u, out FixVec3 a, out FixVec3 b)
        {
            BladePose pose = p.Weapon == WeaponState.Plunge
                ? Make(V(2,100,42),V(0,-100,10),FixVec3.Forward) : Arc(p.Config.Style,p.Swing,u,F(30));
            FixVec3 from = V(0,12,0), to = V(0,90,0);
            if (p.Config.Style == Style.Reaper) { from=V(0,55,0); to=V(76,97,0); }
            else if (p.Config.Style == Style.Greatsword) { from=V(0,15,0); to=V(0,143,0); }
            else if (p.Config.Style == Style.Daggers) { from=V(0,6,0); to=V(0,34,0); }
            a = p.Position + FixVec3.RotateY(pose.Point(from),p.Yaw);
            b = p.Position + FixVec3.RotateY(pose.Point(to),p.Yaw);
        }
        public static Fix SegmentDistance(FixVec3 p0, FixVec3 p1, FixVec3 q0, FixVec3 q1, out FixVec3 at)
        {
            FixVec3 d1=p1-p0,d2=q1-q0,r=p0-q0;
            Fix a=FixVec3.Dot(d1,d1),e=FixVec3.Dot(d2,d2),f=FixVec3.Dot(d2,r),s,t;
            if (a<=Fix.Epsilon && e<=Fix.Epsilon) { s=t=0; }
            else if (a<=Fix.Epsilon) { s=0; t=Fix.Clamp01(f/e); }
            else {
                Fix c=FixVec3.Dot(d1,r);
                if (e<=Fix.Epsilon) { t=0; s=Fix.Clamp01(-c/a); }
                else {
                    Fix b=FixVec3.Dot(d1,d2),denom=a*e-b*b;
                    s=denom>Fix.Epsilon?Fix.Clamp01((b*f-c*e)/denom):Fix.Zero;
                    t=(b*s+f)/e;
                    if (t<0) { t=0; s=Fix.Clamp01(-c/a); }
                    else if (t>1) { t=1; s=Fix.Clamp01((b-c)/a); }
                }
            }
            FixVec3 c1=p0+d1*s,c2=q0+d2*t; at=(c1+c2)*F(50);
            return (c1-c2).Length;
        }
        public static FixVec3 ClosestPoint(PlayerState p, FixVec3 point)
        {
            Fix half=Fix.Max(0,p.Config.Height/2-p.Config.Radius);
            FixVec3 center=p.Position+FixVec3.Up*p.Config.CenterY;
            var axis=new FixVec3(center.X,Fix.Clamp(point.Y,center.Y-half,center.Y+half),center.Z);
            FixVec3 d=point-axis;
            return d.LengthSquared<=p.Config.Radius*p.Config.Radius?point:axis+d.Normalized*p.Config.Radius;
        }
        public static Fix CapsuleDistance(PlayerState p, FixVec3 a, FixVec3 b, out FixVec3 at)
        {
            Fix half=Fix.Max(0,p.Config.Height/2-p.Config.Radius);
            FixVec3 center=p.Position+FixVec3.Up*p.Config.CenterY;
            return SegmentDistance(a,b,center-FixVec3.Up*half,center+FixVec3.Up*half,out at)-p.Config.Radius;
        }
    }
}

