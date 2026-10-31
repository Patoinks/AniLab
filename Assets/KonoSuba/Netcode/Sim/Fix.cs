using System;

namespace AnimLab.Netcode.Sim
{
    /// <summary>Q32.32. Intermediários de 128 bits em ulong, sem alocar; o mesmo resultado bit a bit que a versão com BigInteger.</summary>
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
        public static Fix Ratio(long a, long b) => FromRaw(ShiftDivide(a, b));

        // Produto 64×64 → 128 sem sinal, em metades de 32 bits.
        static void Multiply(ulong x, ulong y, out ulong hi, out ulong lo)
        {
            ulong xl = (uint)x, xh = x >> 32, yl = (uint)y, yh = y >> 32;
            ulong ll = xl * yl, lh = xl * yh, hl = xh * yl, hh = xh * yh;
            ulong mid = (ll >> 32) + (uint)lh + (uint)hl;
            lo = (mid << 32) | (uint)ll;
            hi = hh + (lh >> 32) + (hl >> 32) + (mid >> 32);
        }
        // (a·b) >> 32 com arredondamento para −∞; overflow lança, como o cast do BigInteger.
        static long MultiplyShift(long a, long b)
        {
            Multiply((ulong)a, (ulong)b, out ulong hi, out ulong lo);
            long top = unchecked((long)hi - (a < 0 ? b : 0) - (b < 0 ? a : 0));
            if ((top >> 31) != 0 && (top >> 31) != -1) throw new OverflowException();
            return unchecked((long)(((ulong)top << 32) | (lo >> 32)));
        }
        // (a << 32) / b truncado para zero; overflow lança, como o cast do BigInteger.
        static long ShiftDivide(long a, long b)
        {
            if (b == 0) throw new DivideByZeroException();
            if (a > int.MinValue && a < int.MaxValue) return (a << 32) / b;
            bool negative = (a < 0) != (b < 0);
            ulong n = a < 0 ? unchecked((ulong)-a) : (ulong)a, d = b < 0 ? unchecked((ulong)-b) : (ulong)b;
            ulong hi = n >> 32, lo = n << 32;
            if (hi >= d) throw new OverflowException();
            // Divisão longa 128/64 bit a bit; o quociente cabe em 64 bits porque hi < d.
            ulong q = 0, r = hi;
            for (int i = 63; i >= 0; i--)
            {
                bool carry = (r >> 63) != 0;
                r = (r << 1) | ((lo >> i) & 1); q <<= 1;
                if (carry || r >= d) { r -= d; q |= 1; }
            }
            if (negative) { if (q > 1UL << 63) throw new OverflowException(); return unchecked(-(long)q); }
            if (q > long.MaxValue) throw new OverflowException();
            return (long)q;
        }
        public int Floor => (int)(Raw >> 32);
        public int Ceil => (int)((Raw >> 32) + ((Raw & 0xffffffffL) != 0 ? 1 : 0));
        public static implicit operator Fix(int n) => FromInt(n);
        public static Fix operator +(Fix a, Fix b) => FromRaw(checked(a.Raw + b.Raw));
        public static Fix operator -(Fix a, Fix b) => FromRaw(checked(a.Raw - b.Raw));
        public static Fix operator -(Fix a) => FromRaw(checked(-a.Raw));
        public static Fix operator *(Fix a, Fix b) => FromRaw(MultiplyShift(a.Raw, b.Raw));
        public static Fix operator /(Fix a, Fix b) => FromRaw(ShiftDivide(a.Raw, b.Raw));
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
            // floor(√(Raw·2³²)): a estimativa em double só acelera; o acerto inteiro exato torna-a irrelevante para o resultado.
            ulong vh = (ulong)n.Raw >> 32, vl = (ulong)n.Raw << 32;
            ulong x = (ulong)Math.Sqrt(n.Raw * 4294967296.0);
            while (true) { Multiply(x, x, out ulong h, out ulong l); if (h > vh || h == vh && l > vl) x--; else break; }
            while (true) { Multiply(x + 1, x + 1, out ulong h, out ulong l); if (h < vh || h == vh && l <= vl) x++; else break; }
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

