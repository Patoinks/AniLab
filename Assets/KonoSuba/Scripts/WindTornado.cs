using System.Collections.Generic;
using UnityEngine;

namespace AnimLab.KonoSuba
{
    /// <summary>
    /// Tornado de vento (referência: NS_Air_AeroTornado). Ampulheta: topo largo, cintura fina a meio, base a abrir
    /// para o chão. Peças, todas malhas 3D:
    ///   - traços: fitas helicoidais (poucas largas e brilhantes, muitas finas), cada uma com o seu raio, voltas,
    ///     largura e velocidade; o shader varre cada uma como um rasto que aparece e some;
    ///   - faíscas: hélices longas onde só uma ponta curta está acesa, a subir pela espiral;
    ///   - casca e núcleo: tubos abertos e ténues com o perfil da ampulheta (dão corpo e luz ao centro);
    ///   - chão: braços em espiral planos a rodar depressa e um disco de poeira em espiral;
    ///   - poeira: centenas de partículas 3D (octaedros esticados em rasto) animadas no shader (WindDust);
    ///   - vida: as velocidades de rotação variam em rajadas (Perlin) e _WindGust faz respirar o brilho;
    ///   - topo: braços em espiral ligeiramente em taça.
    /// Cor da vertex color: r = fase aleatória (padrão do shader), g = brilho do traço.
    /// </summary>
    public class WindTornado : MonoBehaviour
    {
        public Material streakMaterial, sparkMaterial, shellMaterial, coreMaterial, discMaterial, dustMaterial;
        [Tooltip("Ar a girar à volta (KonoSuba/Distortion, modo vento); opcional.")]
        public Material distortionMaterial;
        public int dust = 450, groundDust = 90;
        public float height = 5.5f;
        public int streaks = 36;
        public int sparks = 50;
        public int seed = 3;

        // Raio (m) a cada altura (m): ampulheta.
        static readonly Vector2[] Profile =
        {
            new Vector2(0f, 1.5f), new Vector2(0.3f, 0.95f), new Vector2(1.2f, 0.48f), new Vector2(2.4f, 0.35f),
            new Vector2(3.6f, 0.62f), new Vector2(4.6f, 1.3f), new Vector2(5.5f, 2.1f)
        };

        readonly List<Transform> spinners = new List<Transform>();
        readonly List<float> speeds = new List<float>();
        readonly List<Renderer> renderers = new List<Renderer>();
        MaterialPropertyBlock block;

        /// <summary>Desvanece o tornado inteiro (0..1), p.ex. no impacto da bola de vento.</summary>
        public void SetFade(float fade, float sway = 1f)
        {
            block ??= new MaterialPropertyBlock();
            block.SetFloat("_Fade", fade);
            block.SetFloat("_Sway", sway);
            foreach (Renderer r in renderers) r.SetPropertyBlock(block);
        }

        public static float Radius(float h)
        {
            if (h <= Profile[0].x) return Profile[0].y;
            for (int i = 1; i < Profile.Length; i++)
                if (h <= Profile[i].x)
                {
                    float t = Mathf.InverseLerp(Profile[i - 1].x, Profile[i].x, h);
                    return Mathf.Lerp(Profile[i - 1].y, Profile[i].y, t * t * (3f - 2f * t));
                }
            return Profile[Profile.Length - 1].y;
        }

        // Eixo em S suave.
        static Vector3 Axis(float h) => new Vector3(Mathf.Sin(h * 0.6f) * 0.25f, h, Mathf.Sin(h * 0.45f + 1f) * 0.12f);

        void Start()
        {
            var rng = new System.Random(seed);
            float R() => (float)rng.NextDouble();

            Add("Poeira", DustMesh(rng, dust, groundDust, height), dustMaterial, 0f);
            if (distortionMaterial != null) Add("Distorção", Shell(1.15f), distortionMaterial, 200f);
            Add("Casca", Shell(0.92f), shellMaterial, 260f);
            Add("Núcleo", Shell(0.45f), coreMaterial, 340f);
            Add("Disco", Disc(0.35f, 2.9f, 0.03f), discMaterial, 300f);
            for (int i = 0; i < streaks; i++)
            {
                bool hero = i < 7;
                float h0 = R() * (height - 1.2f);
                float span = 1.2f + R() * 3.2f;
                float h1 = Mathf.Min(h0 + span, height);
                // Junto à cintura o raio é pequeno: mais voltas para o traço parecer apertado.
                float turns = 1f + R() * 2.2f;
                float scale = 0.85f + R() * 0.45f;
                float width = hero ? 0.08f + R() * 0.12f : 0.015f + R() * 0.045f;
                float bright = hero ? 0.85f + R() * 0.15f : 0.3f + R() * 0.45f;
                Add("Traço " + i, Helix(R() * Mathf.PI * 2f, h0, h1, turns, scale, width, R(), bright),
                    streakMaterial, 200f + R() * 260f);
            }
            for (int i = 0; i < sparks; i++)
                Add("Faísca " + i, Helix(R() * Mathf.PI * 2f, 0.05f, height - 0.2f, 3f + R() * 3f, 0.8f + R() * 0.5f, 0.035f + R() * 0.03f, R(), 0.6f + R() * 0.4f),
                    sparkMaterial, 120f + R() * 200f);
            for (int i = 0; i < 5; i++)
                Add("Chão " + i, Spiral(i * Mathf.PI * 2f / 5f, 0.04f + i * 0.008f, 0.4f + R() * 0.3f, 2.2f + R() * 0.8f, 0.8f + R() * 0.5f, 0.06f + R() * 0.16f, 0f, R()),
                    streakMaterial, 320f + R() * 120f);
            for (int i = 0; i < 3; i++)
                Add("Topo " + i, Spiral(i * Mathf.PI * 2f / 3f + 0.5f, height - 0.25f + i * 0.08f, 0.9f, 2.4f + R() * 0.3f, 0.9f, 0.12f + R() * 0.1f, 0.35f, R()),
                    streakMaterial, 180f + R() * 60f);
        }

        void Update()
        {
            // Rajadas: a rotação de cada peça acelera e abranda ao seu ritmo; o brilho global respira.
            float t = Time.time;
            float gust = Mathf.PerlinNoise(t * 0.35f, 0.5f);
            Shader.SetGlobalFloat("_WindGust", gust);
            for (int i = 0; i < spinners.Count; i++)
            {
                float k = 0.55f + 0.9f * Mathf.PerlinNoise(t * 0.4f, i * 1.37f);
                spinners[i].Rotate(0f, -speeds[i] * k * (0.8f + 0.5f * gust) * Time.deltaTime, 0f, Space.Self);
            }
        }

        void Add(string name, Mesh mesh, Material material, float degreesPerSecond)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localRotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            renderers.Add(r);
            spinners.Add(go.transform);
            speeds.Add(degreesPerSecond);
        }

        /// <summary>Fita helicoidal ao longo do perfil; larga no meio, afiada nas pontas; normal para fora.</summary>
        static Mesh Helix(float a0, float h0, float h1, float turns, float scale, float width, float phase, float bright)
        {
            const int Steps = 120;
            return Strip(Steps, t =>
            {
                float h = Mathf.Lerp(h0, h1, t);
                float a = a0 + t * turns * Mathf.PI * 2f;
                float r = Radius(h) * scale;
                return Axis(h) + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
            }, width, phase, bright, false);
        }

        /// <summary>Espiral quase plana (chão/topo): do raio r0 ao r1 em <paramref name="turns"/> voltas; cup sobe na borda.</summary>
        static Mesh Spiral(float a0, float y, float r0, float r1, float turns, float width, float cup, float phase)
        {
            const int Steps = 90;
            return Strip(Steps, t =>
            {
                float a = a0 + t * turns * Mathf.PI * 2f;
                float r = Mathf.Lerp(r0, r1, t);
                return new Vector3(Mathf.Cos(a) * r, y + cup * t * t, Mathf.Sin(a) * r);
            }, width, phase, 1f, true);
        }

        /// <summary>
        /// Fita ao longo de <paramref name="path"/> (uv.x através, uv.y ao longo), larga no meio e afiada nas pontas.
        /// Virada para <paramref name="outwardOf"/> (por omissão: para fora do eixo vertical); flat = deitada.
        /// </summary>
        internal static Mesh Strip(int steps, System.Func<float, Vector3> path, float width, float phase, float bright, bool flat,
            System.Func<Vector3, Vector3> outwardOf = null)
        {
            var verts = new Vector3[(steps + 1) * 2];
            var uvs = new Vector2[verts.Length];
            var colors = new Color[verts.Length];
            for (int k = 0; k <= steps; k++)
            {
                float t = k / (float)steps;
                Vector3 p = path(t);
                Vector3 tan = (path(Mathf.Min(t + 0.005f, 1f)) - path(Mathf.Max(t - 0.005f, 0f))).normalized;
                Vector3 outward = outwardOf != null ? outwardOf(p) : new Vector3(p.x, 0f, p.z).normalized;
                // Traços: fita virada para fora (largura quase vertical). Chão/topo: fita deitada (largura radial).
                Vector3 across = flat ? Vector3.Cross(Vector3.up, tan).normalized : Vector3.Cross(tan, outward).normalized;
                float w = width * Mathf.Pow(Mathf.Max(0f, Mathf.Sin(Mathf.PI * t)), 0.6f);
                verts[k * 2] = p - across * w * 0.5f;
                verts[k * 2 + 1] = p + across * w * 0.5f;
                uvs[k * 2] = new Vector2(0f, t);
                uvs[k * 2 + 1] = new Vector2(1f, t);
                colors[k * 2] = colors[k * 2 + 1] = new Color(phase, bright, 0f, 1f);
            }
            var tris = new int[steps * 6];
            for (int k = 0, n = 0; k < steps; k++)
            {
                int a = k * 2;
                tris[n++] = a; tris[n++] = a + 2; tris[n++] = a + 1;
                tris[n++] = a + 1; tris[n++] = a + 2; tris[n++] = a + 3;
            }
            var mesh = new Mesh { name = "Traço", vertices = verts, uv = uvs, colors = colors, triangles = tris };
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// Partículas de poeira: dois planos cruzados por partícula (um deitado, um em pé, ambos ao longo do
        /// movimento), em coordenadas unitárias (x ao longo, y em pé, z radial); o shader (WindDust) posiciona-as
        /// e desenha um traço suave com cauda de cometa.
        /// uv = (altura inicial, ângulo inicial), uv2 = (escala do raio, velocidade), cor = (fase, tamanho, chão, brilho).
        /// </summary>
        internal static Mesh DustMesh(System.Random rng, int dust, int groundDust, float height)
        {
            float R() => (float)rng.NextDouble();
            Vector3[] corners =
            {
                new Vector3(-1f, -1f, 0f), new Vector3(1f, -1f, 0f), new Vector3(1f, 1f, 0f), new Vector3(-1f, 1f, 0f),
                new Vector3(-1f, 0f, -1f), new Vector3(1f, 0f, -1f), new Vector3(1f, 0f, 1f), new Vector3(-1f, 0f, 1f)
            };
            int[] faces = { 0, 1, 2, 0, 2, 3, 4, 5, 6, 4, 6, 7 };
            int count = dust + groundDust;
            var verts = new Vector3[count * corners.Length];
            var uvs = new Vector2[verts.Length];
            var uv2 = new Vector2[verts.Length];
            var colors = new Color[verts.Length];
            var tris = new int[count * faces.Length];
            for (int p = 0; p < count; p++)
            {
                bool ground = p >= dust;
                var uv = new Vector2(R(), R());
                var param = new Vector2(ground ? R() : 0.7f + R() * 0.7f, 0.5f + R());
                var c = new Color(R(), R(), ground ? 1f : 0f, 0.3f + R() * 0.7f);
                for (int k = 0; k < corners.Length; k++)
                {
                    int i = p * corners.Length + k;
                    verts[i] = corners[k];
                    uvs[i] = uv;
                    uv2[i] = param;
                    colors[i] = c;
                }
                for (int k = 0; k < faces.Length; k++) tris[p * faces.Length + k] = p * corners.Length + faces[k];
            }
            var mesh = new Mesh { name = "Poeira do vento", vertices = verts, uv = uvs, uv2 = uv2, colors = colors, triangles = tris };
            // As posições reais vêm do shader: limites largos para não ser recortada.
            mesh.bounds = new Bounds(new Vector3(0f, height * 0.5f, 0f), new Vector3(8f, height + 2f, 8f));
            return mesh;
        }

        /// <summary>Anel plano no chão: uv.x à volta, uv.y do raio interior ao exterior.</summary>
        internal static Mesh Disc(float r0, float r1, float y)
        {
            const int Seg = 96, Rings = 12;
            var verts = new Vector3[(Seg + 1) * (Rings + 1)];
            var uvs = new Vector2[verts.Length];
            var colors = new Color[verts.Length];
            for (int k = 0; k <= Rings; k++)
                for (int x = 0; x <= Seg; x++)
                {
                    float u = x / (float)Seg, v = k / (float)Rings, a = u * Mathf.PI * 2f, r = Mathf.Lerp(r0, r1, v);
                    int i = k * (Seg + 1) + x;
                    verts[i] = new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r);
                    uvs[i] = new Vector2(u, v);
                    colors[i] = new Color(0.5f, 1f, 0f, 1f);
                }
            return Grid("Disco do vento", verts, uvs, colors, Seg, Rings);
        }

        /// <summary>Tubo aberto com o perfil da ampulheta (raio × scale): uv.x à volta, uv.y ao longo.</summary>
        Mesh Shell(float scale)
        {
            const int Seg = 64, Rings = 60;
            var verts = new Vector3[(Seg + 1) * (Rings + 1)];
            var uvs = new Vector2[verts.Length];
            var colors = new Color[verts.Length];
            for (int y = 0; y <= Rings; y++)
            {
                float v = y / (float)Rings, h = v * height;
                for (int x = 0; x <= Seg; x++)
                {
                    float u = x / (float)Seg, a = u * Mathf.PI * 2f;
                    float r = Radius(h) * scale;
                    int i = y * (Seg + 1) + x;
                    verts[i] = Axis(h) + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                    uvs[i] = new Vector2(u, v);
                    colors[i] = new Color(0.5f, 1f, 0f, 1f);
                }
            }
            return Grid("Casca do vento", verts, uvs, colors, Seg, Rings);
        }

        static Mesh Grid(string name, Vector3[] verts, Vector2[] uvs, Color[] colors, int seg, int rings)
        {
            var tris = new int[seg * rings * 6];
            int t = 0;
            for (int y = 0; y < rings; y++)
                for (int x = 0; x < seg; x++)
                {
                    int a = y * (seg + 1) + x, b = a + seg + 1;
                    tris[t++] = a; tris[t++] = a + 1; tris[t++] = b;
                    tris[t++] = a + 1; tris[t++] = b + 1; tris[t++] = b;
                }
            var mesh = new Mesh { name = name, vertices = verts, uv = uvs, colors = colors, triangles = tris };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
