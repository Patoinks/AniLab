namespace AnimLab.Netcode.Sim
{
    public struct FixVec3
    {
        public Fix X, Y, Z;
        public FixVec3(Fix x, Fix y, Fix z) { X = x; Y = y; Z = z; }
        public static readonly FixVec3 Zero = new FixVec3(0, 0, 0), Up = new FixVec3(0, 1, 0),
            Right = new FixVec3(1, 0, 0), Forward = new FixVec3(0, 0, 1);
        public Fix LengthSquared => Dot(this, this);
        public Fix Length => Fix.Sqrt(LengthSquared);
        public FixVec3 Flat => new FixVec3(X, 0, Z);
        public FixVec3 Normalized => Length > Fix.Epsilon ? this / Length : Zero;
        public static FixVec3 operator +(FixVec3 a, FixVec3 b) => new FixVec3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static FixVec3 operator -(FixVec3 a, FixVec3 b) => new FixVec3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static FixVec3 operator -(FixVec3 a) => new FixVec3(-a.X, -a.Y, -a.Z);
        public static FixVec3 operator *(FixVec3 a, Fix b) => new FixVec3(a.X * b, a.Y * b, a.Z * b);
        public static FixVec3 operator /(FixVec3 a, Fix b) => new FixVec3(a.X / b, a.Y / b, a.Z / b);
        public static Fix Dot(FixVec3 a, FixVec3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
        public static FixVec3 Cross(FixVec3 a, FixVec3 b) => new FixVec3(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
        public static FixVec3 Lerp(FixVec3 a, FixVec3 b, Fix u) => a + (b - a) * u;
        public static FixVec3 ClampMagnitude(FixVec3 v, Fix max) => v.LengthSquared > max * max ? v.Normalized * max : v;
        public static FixVec3 MoveTowards(FixVec3 a, FixVec3 b, Fix delta) => a + ClampMagnitude(b - a, delta);
        public static FixVec3 RotateY(FixVec3 v, Fix angle)
        {
            Fix c = Fix.Cos(angle), s = Fix.Sin(angle);
            return new FixVec3(v.X * c + v.Z * s, v.Y, v.Z * c - v.X * s);
        }
        // Interpolação esférica por base ortogonal; sem quaternions nem trigonometria do sistema.
        public static FixVec3 Slerp(FixVec3 a, FixVec3 b, Fix u)
        {
            a = a.Normalized; b = b.Normalized;
            Fix dot = Fix.Clamp(Dot(a, b), -Fix.One, Fix.One);
            FixVec3 tangent = b - a * dot;
            if (tangent.LengthSquared < Fix.Epsilon)
            {
                if (dot > 0) return Lerp(a, b, u).Normalized;
                tangent = Cross(a, AbsY(a) < Fix.Ratio(9, 10) ? Up : Right);
            }
            Fix angle = Fix.Atan2(Fix.Sqrt(Fix.Max(0, 1 - dot * dot)), dot);
            return a * Fix.Cos(angle * u) + tangent.Normalized * Fix.Sin(angle * u);
        }
        static Fix AbsY(FixVec3 v) => Fix.Abs(v.Y);
    }
}

