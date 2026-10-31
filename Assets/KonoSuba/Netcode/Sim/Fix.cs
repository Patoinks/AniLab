using System;
using System.Numerics;

namespace AnimLab.Netcode.Sim
{
    /// <summary>Q32.32. Os intermediários largos evitam overflow antes do deslocamento.</summary>
    public readonly struct Fix : IEquatable<Fix>, IComparable<Fix>
    {
        public readonly long Raw;
        public const long Unit = 1L << 32;
        public static readonly Fix Zero = FromRaw(0), One = FromRaw(Unit);
        public static readonly Fix Pi = FromRaw(13493037705L), TwoPi = FromRaw(26986075409L);
        public static readonly Fix Epsilon = FromRaw(4295);
        Fix(long raw) { Raw = raw; }
        public static Fix FromRaw(long raw) => new Fix(raw);
        public static Fix FromInt(int n) => FromRaw((long)n << 32);
        public static Fix Ratio(long a, long b) => FromRaw((long)(((BigInteger)a << 32) / b));
        public int Floor => (int)(Raw >> 32);
        public int Ceil => (int)((Raw >> 32) + ((Raw & 0xffffffffL) != 0 ? 1 : 0));
        public static implicit operator Fix(int n) => FromInt(n);
        public static Fix operator +(Fix a, Fix b) => FromRaw(checked(a.Raw + b.Raw));
        public static Fix operator -(Fix a, Fix b) => FromRaw(checked(a.Raw - b.Raw));
        public static Fix operator -(Fix a) => FromRaw(checked(-a.Raw));
        public static Fix operator *(Fix a, Fix b) => FromRaw((long)(((BigInteger)a.Raw * b.Raw) >> 32));
        public static Fix operator /(Fix a, Fix b) => FromRaw((long)(((BigInteger)a.Raw << 32) / b.Raw));
        public static bool operator <(Fix a, Fix b) => a.Raw < b.Raw;
        public static bool operator >(Fix a, Fix b) => a.Raw > b.Raw;
        public static bool operator <=(Fix a, Fix b) => a.Raw <= b.Raw;
        public static bool operator >=(Fix a, Fix b) => a.Raw >= b.Raw;
        public static bool operator ==(Fix a, Fix b) => a.Raw == b.Raw;
        public static bool operator !=(Fix a, Fix b) => a.Raw != b.Raw;
        public static Fix Abs(Fix n) => n.Raw < 0 ? -n : n;
        public static Fix Min(Fix a, Fix b) => a < b ? a : b;
        public static Fix Max(Fix a, Fix b) => a > b ? a : b;
        public static Fix Clamp(Fix n, Fix lo, Fix hi) => Max(lo, Min(n, hi));
        public static Fix Clamp01(Fix n) => Clamp(n, Zero, One);
        public static Fix Lerp(Fix a, Fix b, Fix t) => a + (b - a) * t;
        public static Fix MoveTowards(Fix a, Fix b, Fix delta) => a < b ? Min(a + delta, b) : Max(a - delta, b);
        public static Fix Sqrt(Fix n)
        {
            if (n.Raw < 0) throw new ArgumentOutOfRangeException(nameof(n));
            if (n.Raw == 0) return Zero;
            BigInteger value = (BigInteger)n.Raw << 32, x = value, next = (x + 1) >> 1;
            while (next < x) { x = next; next = (x + value / x) >> 1; }
            return FromRaw((long)x);
        }
        static Fix Sample(long[] table, Fix u)
        {
            Fix p = Clamp01(u) * (table.Length - 1);
            int i = p.Floor;
            return i >= table.Length - 1 ? FromRaw(table[table.Length - 1])
                : Lerp(FromRaw(table[i]), FromRaw(table[i + 1]), p - i);
        }
        public static Fix Sin(Fix angle)
        {
            long r = angle.Raw % TwoPi.Raw;
            if (r < 0) r += TwoPi.Raw;
            Fix turn = Ratio(r, TwoPi.Raw) * 4;
            int q = turn.Floor; Fix u = turn - q;
            Fix v = Sample(FixTables.SinQuarter, (q & 1) == 0 ? u : One - u);
            return q < 2 ? v : -v;
        }
        public static Fix Cos(Fix angle) => Sin(angle + Pi / 2);
        public static Fix Atan2(Fix y, Fix x)
        {
            if (x == Zero && y == Zero) return Zero;
            Fix ax = Abs(x), ay = Abs(y);
            Fix a = ax >= ay ? Sample(FixTables.Atan, ay / ax)
                : Pi / 2 - Sample(FixTables.Atan, ax / ay);
            if (x < Zero) a = Pi - a;
            return y < Zero ? -a : a;
        }
        public static Fix Pow22(Fix n) => Sample(FixTables.Pow22, n);
        public static Fix Degrees(Fix degrees) => degrees * Pi / 180;
        public static Fix DeltaAngle(Fix from, Fix to)
        {
            long d = (to.Raw - from.Raw) % TwoPi.Raw;
            if (d > Pi.Raw) d -= TwoPi.Raw;
            if (d < -Pi.Raw) d += TwoPi.Raw;
            return FromRaw(d);
        }
        public bool Equals(Fix other) => Raw == other.Raw;
        public override bool Equals(object obj) => obj is Fix f && Equals(f);
        public override int GetHashCode() => Raw.GetHashCode();
        public int CompareTo(Fix other) => Raw.CompareTo(other.Raw);
        public override string ToString() => (Raw / (decimal)Unit).ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);
    }
}

