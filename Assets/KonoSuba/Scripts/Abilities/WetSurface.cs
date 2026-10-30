using UnityEngine;

namespace AnimLab.KonoSuba
{
    /// <summary>
    /// Marcas molhadas num corpo: só onde a água tocou, com escorrências para baixo, e vão secando.
    /// Guarda até <see cref="MaxSpots"/> manchas (no espaço do corpo, para o acompanharem) e passa-as ao shader
    /// KonoSuba/Wettable. Corpos com outros shaders ficam molhados por inteiro (mais escuros e brilhantes).
    /// </summary>
    public class WetSurface : MonoBehaviour
    {
        public const int MaxSpots = 16;
        const float DryTime = 7f;

        readonly Vector3[] localPoints = new Vector3[MaxSpots];
        readonly float[] radii = new float[MaxSpots];
        readonly float[] wetAt = new float[MaxSpots];
        readonly Vector4[] spots = new Vector4[MaxSpots];
        readonly float[] wetness = new float[MaxSpots];
        int next;
        Renderer[] renderers;
        MaterialPropertyBlock block;
        static readonly int SpotsId = Shader.PropertyToID("_WetSpots");
        static readonly int WetnessId = Shader.PropertyToID("_WetAmounts");
        static readonly int GlossId = Shader.PropertyToID("_Glossiness");
        static readonly int ColorId = Shader.PropertyToID("_Color");

        /// <summary>Molha o corpo à volta de um ponto (mundo). Pontos muito juntos reforçam a mesma mancha.</summary>
        public static void AddSpot(Transform root, Vector3 worldPoint, float radius)
        {
            var w = root.GetComponent<WetSurface>();
            if (w == null) w = root.gameObject.AddComponent<WetSurface>();
            w.Add(root.InverseTransformPoint(worldPoint), radius);
            w.enabled = true;
        }

        /// <summary>O quão molhado está o corpo (0..1), pela mancha mais molhada.</summary>
        public static float WetnessOf(Transform root)
        {
            var w = root.GetComponentInParent<WetSurface>();
            if (w == null) return 0f;
            float best = 0f;
            for (int i = 0; i < MaxSpots; i++)
            {
                float k = 1f - Mathf.Clamp01((Time.time - w.wetAt[i]) / DryTime);
                best = Mathf.Max(best, k * k);
            }
            return best;
        }

        void Awake()
        {
            renderers = GetComponentsInChildren<Renderer>();
            block = new MaterialPropertyBlock();
            for (int i = 0; i < MaxSpots; i++) wetAt[i] = -100f;
        }

        void Add(Vector3 local, float radius)
        {
            for (int i = 0; i < MaxSpots; i++)
            {
                if (Time.time - wetAt[i] > DryTime) continue;
                if ((localPoints[i] - local).sqrMagnitude < radius * radius * 0.36f)
                {
                    wetAt[i] = Time.time;
                    radii[i] = Mathf.Max(radii[i], radius);
                    return;
                }
            }
            localPoints[next] = local;
            radii[next] = radius;
            wetAt[next] = Time.time;
            next = (next + 1) % MaxSpots;
        }

        void Update()
        {
            float any = 0f;
            for (int i = 0; i < MaxSpots; i++)
            {
                float k = 1f - Mathf.Clamp01((Time.time - wetAt[i]) / DryTime);
                wetness[i] = k * k;
                any = Mathf.Max(any, wetness[i]);
                Vector3 p = transform.TransformPoint(localPoints[i]);
                spots[i] = new Vector4(p.x, p.y, p.z, radii[i]);
            }

            foreach (Renderer r in renderers)
            {
                Material m = r.sharedMaterial;
                if (m == null) continue;
                r.GetPropertyBlock(block);
                if (m.HasProperty("_WetRunoff"))
                {
                    block.SetVectorArray(SpotsId, spots);
                    block.SetFloatArray(WetnessId, wetness);
                }
                else if (m.HasProperty(GlossId) && m.HasProperty(ColorId))
                {
                    float wet = any * 0.6f;
                    block.SetFloat(GlossId, Mathf.Lerp(m.GetFloat(GlossId), 0.92f, wet));
                    block.SetColor(ColorId, Color.Lerp(m.color, m.color * 0.6f, wet));
                }
                else continue;
                r.SetPropertyBlock(block);
            }
            if (any <= 0f) enabled = false;
        }
    }
}
