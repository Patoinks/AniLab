using System.Collections.Generic;
using UnityEngine;

namespace AnimLab.KonoSuba
{
    /// <summary>
    /// Simulação de ondas da água à volta do player: a superfície sobe e desce de verdade e as ondas viajam,
    /// cruzam-se e espalham-se sozinhas. É o ponto onde todos os efeitos de ambiente se ligam:
    ///   WaterSim.Impulse(ponto, raio, altura)   — impacto, passo, algo que cai na água
    ///   WaterSim.Push(origem, direção, ...)     — rajada de vento: empurra uma crista para a frente
    /// FogField e WaterRipples já chamam isto; ataques novos só precisam de chamar FogField.
    /// A área simulada (quadrado de <see cref="size"/> m) segue o player; fora dela a água fica parada.
    /// </summary>
    public class WaterSim : MonoBehaviour
    {
        public static WaterSim Instance { get; private set; }

        [Tooltip("Centro da área simulada (normalmente o player).")]
        public Transform follow;
        [Tooltip("Material da malha deslocada (o shader do chão com _SimMesh = 1).")]
        public Material surfaceMaterial;

        [Header("Simulação")]
        public float size = 48f;
        public int resolution = 512;
        [Tooltip("Profundidade da película de água (m). Varrer mais do que isto deixa o chão a seco.")]
        public float waterDepth = 0.06f;
        [Tooltip("Velocidade das ondas (m/s). Em água rasa as ondas são lentas.")]
        public float waveSpeed = 1.7f;
        [Range(0.95f, 1f), Tooltip("Perda de energia por passo (as ondas grandes perdem força com isto).")]
        public float damping = 0.994f;
        [Range(0f, 0.2f), Tooltip("Apaga mais depressa as ondulações curtas, como a água real.")]
        public float viscosity = 0.1f;

        [Header("Esteira do player")]
        [Tooltip("Profundidade empurrada por cada metro que o player anda (m/m).")]
        public float wakeDepth = 0.02f;
        public float wakeRadius = 0.35f;
        public int substeps = 2;

        [Header("Malha")]
        [Tooltip("Vértices por lado da malha deslocada (máx. 255).")]
        public int meshResolution = 255;

        const int MaxImpulses = 16, MaxPushers = 4, MaxRadials = 4; // têm de bater com WaterSim.shader

        // Zonas que o vento secou, para as habilidades saberem se há água à mão (sem ler a GPU).
        struct DryZone
        {
            public Vector2 origin, dir;   // dir = zero para zonas circulares
            public float radius, length, until;
        }
        readonly List<DryZone> dryZones = new List<DryZone>();
        [Tooltip("Tempo que a água demora a voltar a cobrir uma zona varrida depois do sopro (s).")]
        public float refillTime = 2.5f;

        class Radial
        {
            public Vector2 center;
            public float radius, expandTime, duration, depth, start;
        }

        class Pusher
        {
            public Vector2 origin, dir;
            public float width, length, duration, depth, start;
        }

        static readonly int SimTexId = Shader.PropertyToID("_WaterSimTex");
        static readonly int SimAreaId = Shader.PropertyToID("_WaterSimArea");
        static readonly int DepthId = Shader.PropertyToID("_WaterSimDepth");

        RenderTexture front, back;
        Material simMaterial;
        Transform surface;
        Mesh surfaceMesh;
        Vector2 center;
        readonly List<Vector4> pendingImpulses = new List<Vector4>();
        readonly List<float> pendingShapes = new List<float>();
        readonly List<Vector4> pendingTransfers = new List<Vector4>();
        readonly Vector4[] transferArray = new Vector4[16];
        readonly List<Pusher> pushers = new List<Pusher>();
        readonly List<Radial> radials = new List<Radial>();
        readonly Vector4[] radialArray = new Vector4[MaxRadials];
        readonly Vector4[] radialParamArray = new Vector4[MaxRadials];
        readonly Vector4[] impulseArray = new Vector4[MaxImpulses];
        readonly Vector4[] impulseShapeArray = new Vector4[MaxImpulses];
        Vector3 lastFollowPos;
        readonly Vector4[] pusherArray = new Vector4[MaxPushers];
        readonly Vector4[] pusherParamArray = new Vector4[MaxPushers];

        void OnEnable()
        {
            Instance = this;
            simMaterial = new Material(Shader.Find("Hidden/KonoSuba/WaterSim")) { hideFlags = HideFlags.DontSave };
            front = CreateTarget();
            back = CreateTarget();
            center = FollowPoint();
            if (follow != null) lastFollowPos = follow.position;
            BuildSurface();
        }

        void OnDisable()
        {
            if (Instance == this) Instance = null;
            Shader.SetGlobalVector(SimAreaId, Vector4.zero);
            if (front != null) { front.Release(); Destroy(front); }
            if (back != null) { back.Release(); Destroy(back); }
            if (simMaterial != null) Destroy(simMaterial);
            if (surface != null) Destroy(surface.gameObject);
            if (surfaceMesh != null) Destroy(surfaceMesh);
        }

        RenderTexture CreateTarget()
        {
            var rt = new RenderTexture(resolution, resolution, 0, RenderTextureFormat.RGFloat)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.DontSave
            };
            rt.Create();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            GL.Clear(false, true, Color.clear);
            RenderTexture.active = prev;
            return rt;
        }

        // Grelha densa que a simulação desloca. Fica por cima do chão normal, que se recorta nesta zona.
        void BuildSurface()
        {
            int n = Mathf.Clamp(meshResolution, 2, 255);
            var verts = new Vector3[n * n];
            var uvs = new Vector2[n * n];
            for (int z = 0; z < n; z++)
                for (int x = 0; x < n; x++)
                {
                    float u = x / (n - 1f), v = z / (n - 1f);
                    verts[z * n + x] = new Vector3((u - 0.5f) * size, 0f, (v - 0.5f) * size);
                    uvs[z * n + x] = new Vector2(u, v);
                }
            var tris = new int[(n - 1) * (n - 1) * 6];
            int t = 0;
            for (int z = 0; z < n - 1; z++)
                for (int x = 0; x < n - 1; x++)
                {
                    int i = z * n + x;
                    tris[t++] = i; tris[t++] = i + n; tris[t++] = i + 1;
                    tris[t++] = i + 1; tris[t++] = i + n; tris[t++] = i + n + 1;
                }
            var mesh = new Mesh { name = "WaterSurface", hideFlags = HideFlags.DontSave,
                indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.MarkDynamic();
            mesh.vertices = verts;
            mesh.uv = uvs;
            mesh.triangles = tris;
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.bounds = new Bounds(Vector3.zero, new Vector3(size, 4f, size));

            var go = new GameObject("WaterSurface") { hideFlags = HideFlags.DontSave };
            go.layer = gameObject.layer;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = surfaceMaterial;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            surface = go.transform;
            surface.position = new Vector3(center.x, 0f, center.y);
            surfaceMesh = mesh;
        }

        Vector2 FollowPoint()
        {
            Transform t = follow != null ? follow : (Camera.main != null ? Camera.main.transform : transform);
            return new Vector2(t.position.x, t.position.z);
        }

        void Update()
        {
            float texel = size / resolution;
            AddWake();

            // A área segue o player aos saltos de 4 texels; o conteúdo é deslocado no mesmo passo.
            float snapStep = texel * 4f;
            Vector2 want = FollowPoint();
            Vector2 snapped = new Vector2(Mathf.Round(want.x / snapStep), Mathf.Round(want.y / snapStep)) * snapStep;
            Vector2 shift = (snapped - center) / size;
            center = snapped;

            int steps = Mathf.Max(1, substeps);
            float dt = Mathf.Min(Time.deltaTime, 1f / 30f) / steps;
            float courant = waveSpeed * dt / texel;
            simMaterial.SetFloat("_C2", Mathf.Min(courant * courant, 0.45f));
            simMaterial.SetFloat("_Damping", damping);
            simMaterial.SetFloat("_Viscosity", viscosity);
            simMaterial.SetFloat("_WaterDepth", waterDepth);
            simMaterial.SetVector("_SimArea", new Vector4(center.x, center.y, size, 0f));

            for (int s = 0; s < steps; s++)
            {
                simMaterial.SetVector("_SimShift", s == 0 ? shift : Vector2.zero);

                int ic = 0;
                if (s == 0)
                    for (int i = 0; i < pendingImpulses.Count && ic < MaxImpulses; i++, ic++)
                    {
                        impulseArray[ic] = pendingImpulses[i];
                        impulseShapeArray[ic] = new Vector4(pendingShapes[i], 0f, 0f, 0f);
                    }
                simMaterial.SetVectorArray("_Impulses", impulseArray);
                simMaterial.SetVectorArray("_ImpulseShape", impulseShapeArray);
                simMaterial.SetInt("_ImpulseCount", ic);

                int tc = s == 0 ? Mathf.Min(pendingTransfers.Count, transferArray.Length) : 0;
                for (int i = 0; i < tc; i++) transferArray[i] = pendingTransfers[i];
                simMaterial.SetVectorArray("_Transfers", transferArray);
                simMaterial.SetInt("_TransferCount", tc);

                int pc = 0;
                float now = Time.time - (steps - 1 - s) * dt;
                foreach (Pusher p in pushers)
                {
                    float age = now - p.start;
                    if (age < 0f || age > p.duration || pc >= MaxPushers) continue;
                    // A frente percorre o corredor todo durante o sopro; a ravina vai-se cavando atrás dela.
                    float k = age / p.duration;
                    float frontDist = p.length * Mathf.Min(1f, k * 1.25f);
                    float fade = 1f - k * k;
                    pusherArray[pc] = new Vector4(p.origin.x, p.origin.y, p.dir.x, p.dir.y);
                    pusherParamArray[pc] = new Vector4(p.width, frontDist, p.depth * fade * dt, p.length);
                    pc++;
                }
                simMaterial.SetVectorArray("_Pushers", pusherArray);
                simMaterial.SetVectorArray("_PusherParams", pusherParamArray);
                simMaterial.SetInt("_PusherCount", pc);

                int rc = 0;
                foreach (Radial r in radials)
                {
                    float age = now - r.start;
                    if (age < 0f || age > r.duration || rc >= MaxRadials) continue;
                    float e = Mathf.Clamp01(age / r.expandTime);
                    float front = r.radius * (1f - (1f - e) * (1f - e) * (1f - e)); // mesma curva que o nevoeiro
                    float fade = 1f - Mathf.Clamp01((age - r.expandTime) / Mathf.Max(r.duration - r.expandTime, 1e-3f));
                    radialArray[rc] = new Vector4(r.center.x, r.center.y, front, 0.9f + front * 0.12f);
                    radialParamArray[rc] = new Vector4(r.depth * fade * dt, 0f, 0f, 0f);
                    rc++;
                }
                simMaterial.SetVectorArray("_Radials", radialArray);
                simMaterial.SetVectorArray("_RadialParams", radialParamArray);
                simMaterial.SetInt("_RadialCount", rc);

                Graphics.Blit(front, back, simMaterial);
                (front, back) = (back, front);
            }

            pendingImpulses.Clear();
            pendingShapes.Clear();
            pendingTransfers.RemoveRange(0, Mathf.Min(pendingTransfers.Count, transferArray.Length));
            pushers.RemoveAll(p => Time.time - p.start > p.duration);
            radials.RemoveAll(r => Time.time - r.start > r.duration);

            Shader.SetGlobalTexture(SimTexId, front);
            Shader.SetGlobalVector(SimAreaId, new Vector4(center.x, center.y, size, texel));
            Shader.SetGlobalFloat(DepthId, waterDepth);
            if (surface != null) surface.position = new Vector3(center.x, 0f, center.y);
        }

        // O corpo do player a andar empurra a água continuamente: deixa uma esteira em V atrás de si.
        void AddWake()
        {
            if (follow == null || !Application.isPlaying) return;
            Vector3 delta = follow.position - lastFollowPos;
            lastFollowPos = follow.position;
            delta.y = 0f;
            float moved = delta.magnitude;
            float speed = moved / Mathf.Max(Time.deltaTime, 1e-4f);
            if (speed < 0.3f || speed > 30f) return; // parado, ou teletransporte
            Impulse(follow.position, wakeRadius, -wakeDepth * moved);
        }

        /// <summary>
        /// Há água à mão neste ponto? Falso se não houver água no ambiente (sem simulação ou sem chão de água
        /// por baixo) ou se o vento a tiver varrido dali há pouco. Usado pelas habilidades de água.
        /// </summary>
        // Sopros fracos só ondulam a película: não a secam, por isso não contam como zona sem água.
        const float MinDryDepth = 0.2f;

        public static bool HasWaterAt(Vector3 position)
        {
            if (Instance == null) return false;
            int waterMask = 1 << Instance.gameObject.layer;
            if (!Physics.Raycast(position + Vector3.up * 2f, Vector3.down, 6f, waterMask, QueryTriggerInteraction.Ignore))
                return false;

            Instance.dryZones.RemoveAll(z => Time.time > z.until);
            var p = new Vector2(position.x, position.z);
            foreach (DryZone z in Instance.dryZones)
            {
                if (z.dir == Vector2.zero)
                {
                    if (Vector2.Distance(p, z.origin) < z.radius) return false;
                }
                else
                {
                    Vector2 d = p - z.origin;
                    float along = Vector2.Dot(d, z.dir);
                    float across = Mathf.Abs(Vector2.Dot(d, new Vector2(-z.dir.y, z.dir.x)));
                    if (along > 0f && along < z.length && across < z.radius) return false;
                }
            }
            return true;
        }

        /// <summary>Soma uma bossa (altura &gt; 0) ou cova (altura &lt; 0) à água; a onda espalha-se a partir daí.</summary>
        /// <param name="ringFrequency">Ondulação inicial (rad/m). 0 = bossa lisa; maior = mais anéis e mais finos.</param>
        public static void Impulse(Vector3 position, float radius, float height, float ringFrequency = 0f)
        {
            if (Instance == null) return;
            Instance.pendingImpulses.Add(new Vector4(position.x, position.z, Mathf.Max(radius, 0.05f), height));
            Instance.pendingShapes.Add(ringFrequency);
        }

        /// <summary>
        /// Tira (volume &lt; 0) ou devolve (volume &gt; 0) água à película, em m³, num perfil gaussiano.
        /// Altera a altura atual e a anterior em conjunto: mexer no volume não cria velocidade artificial.
        /// </summary>
        public static void TransferVolume(Vector3 position, float radius, float volume)
        {
            if (Instance == null || Mathf.Abs(volume) < 1e-9f) return;
            radius = Mathf.Max(radius, 0.05f);
            Instance.pendingTransfers.Add(new Vector4(position.x, position.z, radius, volume / (Mathf.PI * radius * radius)));
        }

        /// <summary>
        /// Rajada de vento em 360° (habilidade de área): a frente expande-se até <paramref name="radius"/> e varre
        /// a água para fora, deixando o chão a seco com um rebordo de água empurrada. Quando o sopro acaba,
        /// a água volta devagar a cobrir o círculo.
        /// </summary>
        /// <param name="depth">Profundidade varrida por segundo de sopro (m/s).</param>
        public static void Burst(Vector3 center, float radius, float expandTime, float duration, float depth)
        {
            if (Instance == null) return;
            if (depth >= MinDryDepth) Instance.dryZones.Add(new DryZone
            {
                origin = new Vector2(center.x, center.z),
                radius = radius * 0.8f,
                until = Time.time + duration + Instance.refillTime
            });
            Instance.radials.Add(new Radial
            {
                center = new Vector2(center.x, center.z),
                radius = radius,
                expandTime = Mathf.Max(expandTime, 0.05f),
                duration = Mathf.Max(duration, expandTime + 0.05f),
                depth = depth,
                start = Time.time
            });
        }

        /// <summary>
        /// Rajada de vento sobre a água: uma frente percorre o corredor (de <paramref name="origin"/>, na direção
        /// dada, até <paramref name="length"/> m) e cava uma ravina, atirando a água para as margens.
        /// Quando o sopro acaba, a ravina volta a encher e as ondas saem para os lados.
        /// </summary>
        /// <param name="depth">Profundidade cavada por segundo de sopro (m/s).</param>
        public static void Push(Vector3 origin, Vector3 direction, float width, float length, float duration, float depth)
        {
            if (Instance == null) return;
            Vector2 dir = new Vector2(direction.x, direction.z);
            if (dir.sqrMagnitude < 1e-6f) return;
            if (depth >= MinDryDepth) Instance.dryZones.Add(new DryZone
            {
                origin = new Vector2(origin.x, origin.z),
                dir = dir.normalized,
                radius = width * 0.3f,
                length = length,
                until = Time.time + duration + Instance.refillTime
            });
            Instance.pushers.Add(new Pusher
            {
                origin = new Vector2(origin.x, origin.z),
                dir = dir.normalized,
                width = width,
                length = length,
                duration = duration,
                depth = depth,
                start = Time.time
            });
        }
    }
}
