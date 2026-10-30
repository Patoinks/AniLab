using UnityEngine;

namespace AnimLab.KonoSuba
{
    /// <summary>
    /// Interação de VFX com o nevoeiro. Qualquer ataque ou habilidade pode limpar o nevoeiro:
    ///   FogField.Blast(pontoDeImpacto)                 — impacto de espada, explosão...
    ///   FogField.Wind(origem, direcao)                 — habilidade de vento, corte em linha...
    /// O nevoeiro abre-se, acumula-se nas bordas e volta a fechar sozinho. A água rasa reage aos dois da mesma
    /// forma: o vento varre-a (em círculo ou em corredor), deixa o chão a seco e ela volta a cobri-lo depois.
    /// </summary>
    [ExecuteAlways]
    public class FogField : MonoBehaviour
    {
        public static FogField Instance { get; private set; }

        const int MaxBlasts = 8, MaxWinds = 4; // têm de bater com FogField.cginc

        // Fração do alcance do vento que chega à água (a água é muito mais pesada que o nevoeiro).
        const float WaterReach = 0.4f;

        static readonly int BlastsId = Shader.PropertyToID("_FogBlasts");
        static readonly int BlastParamsId = Shader.PropertyToID("_FogBlastParams");
        static readonly int WindsId = Shader.PropertyToID("_FogWinds");
        static readonly int WindParamsId = Shader.PropertyToID("_FogWindParams");
        static readonly int TimeId = Shader.PropertyToID("_FogTime");

        readonly Vector4[] blasts = new Vector4[MaxBlasts];
        readonly Vector4[] blastParams = new Vector4[MaxBlasts];
        readonly bool[] blastWaterSpray = new bool[MaxBlasts];
        readonly Vector4[] winds = new Vector4[MaxWinds];
        readonly Vector4[] windParams = new Vector4[MaxWinds];
        readonly float[] windSprayFront = new float[MaxWinds];
        readonly float[] windWater = new float[MaxWinds];
        int nextBlast, nextWind;

        [Tooltip("De quantos em quantos metros a frente de uma rajada levanta spray da água.")]
        public float windSprayEvery = 1.4f;

        void OnEnable()
        {
            Instance = this;
            for (int i = 0; i < MaxBlasts; i++) { blasts[i] = Vector4.zero; blastParams[i] = Vector4.zero; }
            for (int i = 0; i < MaxWinds; i++) { winds[i] = Vector4.zero; windParams[i] = Vector4.zero; }
            Push();
        }

        void OnDisable()
        {
            if (Instance == this) Instance = null;
        }

        void Update()
        {
            Shader.SetGlobalFloat(TimeId, Time.time);
            if (Application.isPlaying) { WindSpray(); BlastSpray(); }
        }

        // Enquanto a frente de uma rajada 360° se expande, levanta salpicos espaçados ao longo dela.
        void BlastSpray()
        {
            if (WaterRipples.Instance == null) return;
            for (int i = 0; i < MaxBlasts; i++)
            {
                Vector4 b = blasts[i], p = blastParams[i];
                float age = Time.time - b.z;
                float expand = Mathf.Max(p.z * 2f, 0.7f);
                if (!blastWaterSpray[i] || p.y <= 0f || age < 0f || age > expand) continue;
                float e = age / expand;
                float front = b.w * WaterReach * (1f - (1f - e) * (1f - e) * (1f - e));
                int count = Mathf.RoundToInt(2f + front * 0.6f);
                for (int k = 0; k < count; k++)
                {
                    float a = Random.value * Mathf.PI * 2f;
                    var at = new Vector3(b.x + Mathf.Cos(a) * front, 0f, b.y + Mathf.Sin(a) * front);
                    WaterRipples.Instance.Spray(at, 0.08f * p.x);
                }
            }
        }

        // Enquanto a frente de uma rajada avança, levanta pequenos salpicos da água ao longo do corredor.
        // Usa a mesma frente que o shader (o corredor cresce em 20% da duração).
        void WindSpray()
        {
            if (WaterRipples.Instance == null) return;
            for (int i = 0; i < MaxWinds; i++)
            {
                Vector4 w = winds[i], p = windParams[i];
                float age = Time.time - p.z;
                if (p.w <= 0f || age < 0f || age > p.w * 0.2f) continue;

                float front = p.x * WaterReach * Mathf.Clamp01(age / (p.w * 0.2f)); // só até onde o vento mexe a água
                Vector2 dir = new Vector2(w.z, w.w);
                float strength = Mathf.Clamp01(dir.magnitude);
                dir.Normalize();
                Vector2 perp = new Vector2(-dir.y, dir.x);
                while (windSprayFront[i] + windSprayEvery <= front)
                {
                    windSprayFront[i] += windSprayEvery;
                    float half = Mathf.Abs(p.y) * (0.5f + 0.5f * windSprayFront[i] / p.x) * 0.4f;
                    Vector2 at = new Vector2(w.x, w.y) + dir * windSprayFront[i] + perp * Random.Range(-half, half);
                    WaterRipples.Instance.Spray(new Vector3(at.x, 0f, at.y), 0.35f * strength * windWater[i]);
                }
            }
        }

        /// <summary>Abre um buraco circular no nevoeiro.</summary>
        /// <param name="radius">Raio máximo da limpeza (m).</param>
        /// <param name="strength">0..1: quanto do nevoeiro é removido.</param>
        /// <param name="duration">Tempo total até o nevoeiro voltar por completo (s).</param>
        /// <param name="expandTime">Tempo que a onda demora a chegar ao raio máximo (s).</param>
        /// <param name="waterScale">Quanto a água reage (0..1), p.ex. menos quando a explosão é no ar.</param>
        /// <param name="waterSpray">Emite salpicos de água; falso para um sopro quente com evaporação.</param>
        public static void Blast(Vector3 position, float radius = 7f, float strength = 1f, float duration = 6f, float expandTime = 0.35f, float waterScale = 1f,
                                 bool waterSpray = true)
        {
            if (Instance == null) return;
            FogField f = Instance;
            f.blasts[f.nextBlast] = new Vector4(position.x, position.z, Time.time, radius);
            f.blastParams[f.nextBlast] = new Vector4(Mathf.Clamp01(strength), duration, expandTime, 0f);
            f.blastWaterSpray[f.nextBlast] = waterSpray;
            f.nextBlast = (f.nextBlast + 1) % MaxBlasts;
            f.Push();

            // Rajada em 360°: varre a água para fora ao mesmo ritmo a que o nevoeiro abre, com spray no centro.
            float water = Mathf.Clamp01(strength) * Mathf.Clamp01(waterScale);
            if (water <= 0f) return;
            if (waterSpray && WaterRipples.Instance != null) WaterRipples.Instance.Spray(position, 1.2f * water);
            // A água é muito mais pesada que o nevoeiro: o mesmo sopro só a empurra numa área bem menor,
            // mais devagar, e só a afina (perto do centro pode ver-se o chão) em vez de a varrer toda.
            float waterExpand = Mathf.Max(expandTime * 2f, 0.7f);
            WaterSim.Burst(position, radius * WaterReach, waterExpand, waterExpand + 0.2f, 0.45f * water);
        }

        /// <summary>Abre um corredor no nevoeiro a partir de <paramref name="origin"/> na direção dada.</summary>
        /// <param name="straight">Rasto de projétil: reto, de largura constante e quase instantâneo.</param>
        /// <param name="waterScale">Quanto a água reage (0..1).</param>
        public static void Wind(Vector3 origin, Vector3 direction, float length = 22f, float width = 5f, float strength = 1f, float duration = 6f,
                                bool straight = false, float waterScale = 1f)
        {
            if (Instance == null) return;
            Vector2 dir = new Vector2(direction.x, direction.z);
            if (dir.sqrMagnitude < 1e-6f) return;
            dir = dir.normalized * Mathf.Clamp01(strength);

            FogField f = Instance;
            f.winds[f.nextWind] = new Vector4(origin.x, origin.z, dir.x, dir.y);
            f.windParams[f.nextWind] = new Vector4(length, straight ? -width : width, Time.time, duration);
            f.windSprayFront[f.nextWind] = 0f;
            f.windWater[f.nextWind] = Mathf.Clamp01(waterScale);
            f.nextWind = (f.nextWind + 1) % MaxWinds;
            f.Push();

            // O golpe de vento cava uma ravina de água ao longo do corredor (a mesma forma que limpa o nevoeiro).
            float water = Mathf.Clamp01(strength) * Mathf.Clamp01(waterScale);
            if (water <= 0f) return;
            if (WaterRipples.Instance != null) WaterRipples.Instance.Spawn(origin, 0.6f * water);
            WaterSim.Push(origin, direction, width * 0.5f, length * WaterReach, 1.2f, 0.45f * water);
        }

        void Push()
        {
            Shader.SetGlobalVectorArray(BlastsId, blasts);
            Shader.SetGlobalVectorArray(BlastParamsId, blastParams);
            Shader.SetGlobalVectorArray(WindsId, winds);
            Shader.SetGlobalVectorArray(WindParamsId, windParams);
            Shader.SetGlobalFloat(TimeId, Time.time);
        }
    }
}
