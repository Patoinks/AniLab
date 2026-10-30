using UnityEngine;

namespace AnimLab.KonoSuba
{
    /// <summary>One cylindrical flow, in metres and seconds. Mirrored in FireTornadoFlow.cginc.</summary>
    public static class FireTornadoFlow
    {
        public const float Rise = 0.9f, Acceleration = 0.30f, Spin = -5.2f, SpinRise = -0.58f;
        public const float GroundSink = 0.52f, SourceSpin = -0.46f;
        public static float Radius(float h)
        {
            h = Mathf.Max(0, h);
            return 0.66f + 0.34f * Mathf.Exp(-h * 3.8f)
                + 0.36f * Mathf.SmoothStep(0, 1, Mathf.InverseLerp(1.25f, 4.7f, h));
        }
        public static float Travel(float h) => Mathf.Log(1 + Mathf.Max(0, h) * Acceleration / Rise) / Acceleration;
        public static float Height(float travel) => Rise / Acceleration * (Mathf.Exp(Acceleration * Mathf.Max(0, travel)) - 1);
        public static float Omega(float h, float lane) => (Spin + SpinRise * Mathf.Max(0, h)) / (0.55f + 0.72f * lane * lane);
        public static float Turn(float travel, float lane)
        {
            if (travel < 0)
            {
                float b = 0.72f * lane * lane;
                return Spin / 0.55f * (travel + Mathf.Log((0.55f + b * Mathf.Exp(-2 * GroundSink * travel)) / (0.55f + b)) / (2 * GroundSink));
            }
            return ((Spin - SpinRise * Rise / Acceleration) * travel + SpinRise * Height(travel) / Acceleration)
                / (0.55f + 0.72f * lane * lane);
        }
        public static Vector3 Axis(float h, float time)
        {
            float fade = Mathf.Clamp01(h * 1.3f);
            return new Vector3(Mathf.Sin(h * 1.55f - time * 1.05f) * 0.085f * fade,
                h, Mathf.Cos(h * 1.35f - time * 0.9f) * 0.065f * fade);
        }
        public static Vector3 Position(float travel, float lane, float angle, float time)
        {
            float h = travel < 0 ? 0.055f : Height(travel) + 0.055f;
            float r = travel < 0 ? Radius(0.055f) * lane * Mathf.Exp(-GroundSink * travel) : Radius(h) * lane;
            return Cylindrical(h, r, angle, time);
        }
        public static Vector3 Cylindrical(float h, float r, float angle, float time)
        {
            float turbulence = 0.085f * Mathf.Sin(angle * 3 - h * 3.4f + time * 3.0f)
                + 0.025f * Mathf.Sin(angle * 7 + h * 4.7f - time * 5.3f)
                + 0.012f * Mathf.Sin(angle * 11 - h * 7 - time * 8);
            float bend = 1 + turbulence * Mathf.Lerp(.22f,1,Mathf.SmoothStep(0,1,h/.6f));
            return Axis(h, time) + new Vector3(Mathf.Cos(angle) * r * bend, 0, Mathf.Sin(angle) * r * bend);
        }
    }
}
