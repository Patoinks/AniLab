using System.Collections.Generic;
using UnityEngine;

namespace AnimLab.KonoSuba
{
    /// <summary>
    /// Uma descarga do Raio (especificação acordada com o Codex, afinada depois). Não marca área e não sai
    /// de um ponto: nasce em sítios aleatórios à volta do player e cresce pela água.
    ///   0–30 ms      estalos curtos no chão, em pontos aleatórios à volta (sai quase de imediato).
    ///   30–60 ms     10–12 origens a toda a volta (um setor cada), a 0,5–3,2 m, todas quase de uma vez, cada uma
    ///                com uma ponta que parte mais ou menos para fora. Alguns impulsos saltam da água num arco
    ///                baixo e há espigões curtos.
    ///   depois       as pontas crescem pela água ligada aos saltos rápidos (cada salto 1,2–2 m em 20–30 ms,
    ///                pausa 10–25 ms — ~19 m em ~0,6 s), com direção persistente (60% a anterior, 20% um
    ///                campo de direções suave, 20% para fora), ganchos ocasionais e tremor fino. Em seco contornam
    ///                (±30/60/90°) ou morrem — nunca saltam. A energia gasta-se por metro e reparte-se
    ///                pelos ramos; os ramos nascem dos canais já acesos.
    ///   apagar       cada ponto vive ~0,65 s desde que a corrente lá chega: o raio apaga-se a começar pela cauda
    ///                e o apagar corre atrás da ponta. Reacendimentos correm pelos ramos; 40% dos ramos têm um
    ///                apagão curto. A água brilha com os mesmos sinais e o nevoeiro ganha cor por cima dos raios.
    /// Inimigos a menos de 3,5 m de uma ponta puxam a corrente; quando ela lhes chega aos pés, sobe pelo corpo
    /// como uma cobra, a enrolá-lo (Climb).
    /// Cada ramo tem um ID (0–63) numa tabela (_LightningBranches) lida pelo raio e pela água (LightningCommon).
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class LightningDischarge : MonoBehaviour
    {
        struct Point
        {
            public Vector3 pos;
            public float arrival, s, width;
        }

        class Branch
        {
            public int id;
            public readonly List<Point> points = new List<Point>();
            public float energy, length, limit; // limit 0 = sem limite (canal principal)
            public bool finished, onGround;
            public float seed;
            public Vector4 pulse0, pulse1;
            public float blackoutFrom = -1f, blackoutTo = -1f;
            public float lastForkS;
            public int bodyChecked; // pontos já testados contra os corpos
        }

        class Tip
        {
            public Branch branch;
            public Vector3 pos, heading;
            public float nextTime, hookLeft, hookRate;
        }

        const float Impact = 0.03f, GrowthEnd = Impact + 1.0f, FadeFrom = 1.75f, End = 1.85f; // a última ponta acaba ~1,03 s + 0,65 s
        const float EnergyPerOrigin = 24f, Step = 0.05f, MaxDistance = 19f, PulseSpeed = 40f;
        const int MaxTips = 26, MaxBranches = 64;
        static readonly Color HaloColor = new Color(0.55f, 0.2f, 1f);
        static readonly int FlashId = Shader.PropertyToID("_LightningFlash");
        static readonly int FlashColorId = Shader.PropertyToID("_LightningColor");
        static readonly int TimeId = Shader.PropertyToID("_LightningTime");
        static readonly int FadeId = Shader.PropertyToID("_LightningFade");
        static readonly int TableId = Shader.PropertyToID("_LightningBranches");

        Transform caster;
        Vector3 center;
        float impactTime = Impact, growthEnd = GrowthEnd, fadeFrom = FadeFrom, end = End;
        float maxDistance = MaxDistance, originEnergy = EnergyPerOrigin, flashDecay = 0.1f;
        MeshRenderer meshRenderer;
        MaterialPropertyBlock properties;
        LightningWaterGrid grid;
        LightningGroundField ground;
        readonly List<Branch> branches = new List<Branch>();
        readonly List<Tip> tips = new List<Tip>();
        readonly List<(float time, Vector3 at)> ripples = new List<(float, Vector3)>();
        readonly HashSet<LockOnTarget> shocked = new HashSet<LockOnTarget>();
        Texture2D table;
        readonly Color[] tableColors = new Color[3 * MaxBranches];
        Mesh mesh;
        LightningFogGlow fogGlow;
        readonly List<Vector3> fogPos = new List<Vector3>();
        readonly List<float> fogArrival = new List<float>();
        float age, fieldSeedX, fieldSeedZ;
        readonly List<(float time, Vector3 at)> origins = new List<(float, Vector3)>();

        readonly List<Vector3> verts = new List<Vector3>(), normals = new List<Vector3>();
        readonly List<Vector2> uv0 = new List<Vector2>(), uv1 = new List<Vector2>(), uv2 = new List<Vector2>();
        readonly List<int> tris = new List<int>();

        public static LightningDischarge Create(Transform caster, LightningAbility ability)
        {
            var d = CreateAt(new Vector3(caster.position.x, 0f, caster.position.z), ability.boltMaterial);
            d.caster = caster;
            d.BuildCharge();
            return d;
        }

        /// <summary>Golpe direto do Kirin; a propagação continua a exigir água ligada. Sem estalos de carga.</summary>
        public static LightningDischarge CreateImpact(Vector3 point, Material material, LockOnTarget primary,
                                                       int originCount = 6, float range = 6f, float growth = 0.35f)
        {
            var d = CreateAt(new Vector3(point.x, 0f, point.z), material);
            d.impactTime = 0f;
            d.growthEnd = Mathf.Max(0.01f, growth);
            d.end = d.growthEnd + 0.73f;
            d.fadeFrom = d.end - 0.1f;
            d.maxDistance = Mathf.Max(0.5f, range);
            d.originEnergy = d.maxDistance * 1.3f;
            d.flashDecay = 0.025f; // clarão concentrado nos primeiros ~70 ms

            // Reservar os primeiros IDs para a cobra principal, antes de qualquer origem ou ramificação.
            if (primary != null)
            {
                d.shocked.Add(primary);
                d.Climb(primary, new Point { pos = d.center + Vector3.up * 0.025f, arrival = 0f });
            }
            int n = Mathf.Clamp(originCount, 5, 7);
            float turn = Random.value * 360f;
            for (int i = 0; i < n; i++)
            {
                Vector3 at = d.center + Quaternion.Euler(0f, turn + (i + Random.Range(0.1f, 0.9f)) * 360f / n, 0f)
                                           * Vector3.forward * Random.Range(0.15f, 0.55f);
                at.y = 0.025f;
                // Uma origem isolada do impacto por chão seco não recebe corrente.
                if (d.grid.Segment(d.center, at)) d.origins.Add((Random.Range(0f, 0.02f), at));
            }
            return d;
        }

        static LightningDischarge CreateAt(Vector3 center, Material material)
        {
            var go = new GameObject("LightningDischarge");
            var d = go.AddComponent<LightningDischarge>();
            d.center = center;
            d.grid = new LightningWaterGrid();
            d.ground = new LightningGroundField(d.center);
            d.fogGlow = new LightningFogGlow(d.center);
            d.fieldSeedX = Random.value * 100f;
            d.fieldSeedZ = Random.value * 100f;
            d.table = new Texture2D(3, MaxBranches, TextureFormat.RGBAFloat, false, true) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            d.mesh = new Mesh { name = "Lightning", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            d.mesh.MarkDynamic();
            go.GetComponent<MeshFilter>().sharedMesh = d.mesh;
            var mr = go.GetComponent<MeshRenderer>();
            d.meshRenderer = mr;
            d.properties = new MaterialPropertyBlock();
            mr.sharedMaterial = material;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            Shader.SetGlobalColor(FlashColorId, HaloColor);
            return d;
        }

        void Update()
        {
            age += Time.deltaTime;
            if (age >= end) { Destroy(gameObject); return; }

            for (int i = origins.Count - 1; i >= 0; i--)
                if (age >= origins[i].time) { StartOrigin(origins[i].at, origins[i].time); origins.RemoveAt(i); }
            Grow();
            ShockBodies();

            for (int i = ripples.Count - 1; i >= 0; i--)
                if (age >= ripples[i].time) { WaterSim.Impulse(ripples[i].at, 0.3f, -0.003f, 6f); ripples.RemoveAt(i); }

            float fade = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(fadeFrom, end, age));
            Shader.SetGlobalFloat(TimeId, age);
            Shader.SetGlobalFloat(FadeId, fade);
            UpdateTable();
            properties.SetFloat(TimeId, age);
            properties.SetFloat(FadeId, fade);
            properties.SetTexture(TableId, table);
            meshRenderer.SetPropertyBlock(properties);
            ground.Flush();
            ground.SetActive(true);
            fogPos.Clear(); fogArrival.Clear();
            foreach (Branch b in branches)
                if (b.onGround) for (int i = 0; i < b.points.Count; i += 3) { fogPos.Add(b.points[i].pos); fogArrival.Add(b.points[i].arrival); }
            fogGlow.Update(fogPos, fogArrival, age, fade);

            // Nevoeiro e chão: só o clarão do impacto (20 ms a subir, 100 ms a descer).
            float a = age - impactTime;
            float flash = a < 0f ? 0f : a < 0.02f ? a / 0.02f : Mathf.Exp(-(a - 0.02f) / flashDecay);
            Vector3 p = caster != null ? caster.position : center;
            Shader.SetGlobalVector(FlashId, new Vector4(p.x, 1f, p.z, flash * fade));

            BuildMesh();
        }

        void OnDestroy()
        {
            if (table != null && Shader.GetGlobalTexture(TableId) == table)
            {
                Shader.SetGlobalVector(FlashId, Vector4.zero);
                Shader.SetGlobalFloat(FadeId, 0f);
                Shader.SetGlobalTexture(TableId, Texture2D.blackTexture);
            }
            ground?.Release();
            fogGlow?.Release();
            if (mesh != null) Destroy(mesh);
            if (table != null) Destroy(table);
        }

        // ------------------------------------------------------------------ Ramos

        Branch NewBranch(float energy, float limit, bool onGround)
        {
            if (branches.Count >= MaxBranches) return null;
            var b = new Branch { id = branches.Count, energy = energy, limit = limit, onGround = onGround, seed = Random.value };
            // 40% dos ramos têm um apagão curto (nunca nos primeiros 50 ms do ramo).
            if (Random.value < 0.4f)
            {
                b.blackoutFrom = age + 0.05f + Random.Range(0f, 0.3f);
                b.blackoutTo = b.blackoutFrom + Random.Range(0.035f, 0.06f);
            }
            branches.Add(b);
            return b;
        }

        void AddPoint(Branch b, Vector3 pos, float arrival)
        {
            float s = 0f;
            if (b.points.Count > 0)
            {
                Point last = b.points[b.points.Count - 1];
                s = last.s + Vector3.Distance(last.pos, pos);
                if (b.onGround) ground.Stamp(last.pos, pos, last.arrival, arrival, last.s, s, b.id);
            }
            float width = 0.75f + 0.5f * Mathf.PerlinNoise(b.seed * 50f, s * 3f); // 0,75–1,25×, fixo no canal
            b.points.Add(new Point { pos = pos, arrival = arrival, s = s, width = width });
            b.length = s;
        }

        void SetPulse(Branch b, float start, float originS, float speed)
        {
            var p = new Vector4(start, originS, speed, Random.Range(0.6f, 1f));
            if (b.pulse0.w <= 0f) b.pulse0 = p;
            else if (b.pulse1.w <= 0f) b.pulse1 = p;
        }

        // ------------------------------------------------------------------ Carga e golpe

        // Caminho com direção persistente até um alvo (raios no ar: subir pelo corpo).
        static List<Vector3> AirPath(Vector3 from, Vector3 to, float step, float wander)
        {
            var pts = new List<Vector3> { from };
            Vector3 p = from, heading = (to - from).normalized;
            Vector3 axis = Vector3.Cross(heading, Random.onUnitSphere).normalized;
            float seed = Random.value * 100f, travelled = 0f;
            for (int guard = 0; guard < 300 && Vector3.Distance(p, to) > step * 1.5f; guard++)
            {
                Vector3 toTarget = (to - p).normalized;
                float bend = (Mathf.PerlinNoise(seed, travelled * 2f) - 0.5f) * 2f * wander;
                Vector3 field = Quaternion.AngleAxis(bend * Mathf.Rad2Deg, axis) * toTarget;
                float pull = Mathf.Clamp01(travelled / Mathf.Max(Vector3.Distance(from, to), 0.01f));
                heading = (heading * 0.7f + field * (0.3f + 0.5f * pull)).normalized;
                p += heading * step;
                travelled += step;
                pts.Add(p);
            }
            pts.Add(to);
            return pts;
        }

        // ------------------------------------------------------------------ Carga e origens

        // Estalos curtos no chão em pontos aleatórios à volta; depois as origens da descarga, também aleatórias.
        void BuildCharge()
        {
            int sparks = Random.Range(5, 8);
            for (int i = 0; i < sparks; i++)
            {
                Vector3 at = RandomAround(0.4f, 2.2f);
                Branch b = NewBranch(0f, 0f, true);
                if (b == null) break;
                float t0 = Random.Range(0.0f, Impact);
                Vector3 h = Quaternion.Euler(0f, Random.value * 360f, 0f) * Vector3.forward;
                Vector3 p = at;
                for (int k = 0; k < 8; k++)
                {
                    AddPoint(b, p, t0 + 0.004f * k);
                    h = Quaternion.Euler(0f, Random.Range(-50f, 50f), 0f) * h;
                    p += h * 0.05f;
                }
                b.finished = true;
            }

            // Origens a toda a volta (360°): um setor para cada uma, com o ângulo e a distância ao acaso dentro dele.
            int n = Random.Range(10, 13);
            float turn = Random.value * 360f;
            for (int i = 0; i < n; i++)
            {
                float angle = turn + (i + Random.Range(0.1f, 0.9f)) * 360f / n;
                float r = Mathf.Sqrt(Random.Range(0.5f * 0.5f, 3.2f * 3.2f));
                Vector3 at = center + Quaternion.Euler(0f, angle, 0f) * Vector3.forward * r;
                at.y = 0.025f;
                origins.Add((Impact + Random.Range(0f, 0.03f), at)); // todas de uma vez: impacto seco
            }
        }

        Vector3 RandomAround(float minR, float maxR)
        {
            Vector2 d = Random.insideUnitCircle.normalized * Mathf.Sqrt(Random.Range(minR * minR, maxR * maxR));
            return new Vector3(center.x + d.x, 0.025f, center.z + d.y);
        }

        // Uma origem: um estalo no sítio e uma ponta que parte mais ou menos para fora.
        void StartOrigin(Vector3 at, float time)
        {
            if (!grid.NearestWater(at, 0.4f, out Vector3 w)) return; // sem água ali, não nasce nada
            w.y = 0.025f;
            WaterSim.Impulse(w, 0.35f, -0.005f, 0f);
            Branch br = NewBranch(originEnergy, 0f, true);
            if (br == null) return;
            AddPoint(br, w, time);
            Vector3 outward = new Vector3(w.x - center.x, 0f, w.z - center.z).normalized;
            Vector3 heading = Quaternion.Euler(0f, Random.Range(-60f, 60f), 0f) * outward;
            tips.Add(new Tip { branch = br, pos = w, heading = heading, nextTime = time });
        }

        void Spur(Vector3 from, float arrival)
        {
            Branch b = NewBranch(0f, 0f, false);
            if (b == null) return;
            Vector3 top = from + Vector3.up * Random.Range(0.25f, 0.6f) + Random.insideUnitSphere * 0.3f;
            var pts = AirPath(from, top, 0.04f, 1.1f);
            for (int k = 0; k < pts.Count; k++) AddPoint(b, pts[k], arrival + 0.02f * k / pts.Count);
            b.finished = true;
        }

        Vector3 Field(Vector3 p)
        {
            float a = Mathf.PerlinNoise(p.x / 0.65f + fieldSeedX, p.z / 0.65f + fieldSeedZ) * Mathf.PI * 4f;
            return new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
        }

        LockOnTarget[] targets = new LockOnTarget[0];

        void Grow()
        {
            targets = FindObjectsByType<LockOnTarget>(FindObjectsSortMode.None);
            for (int ti = 0; ti < tips.Count; ti++)
            {
                Tip tip = tips[ti];
                if (tip.branch.finished || age < tip.nextTime) continue;
                Advance(tip);
            }
            tips.RemoveAll(t => t.branch.finished);
        }

        // Um impulso de uma ponta: um salto de 1,2–2 m em 20–30 ms (vê-se a correr; às vezes num arco baixo por cima
        // da água), depois uma pausa de 10–25 ms.
        void Advance(Tip tip)
        {
            Branch b = tip.branch;
            float start = tip.nextTime;
            if (start > growthEnd) { b.finished = true; return; }
            float length = Random.Range(1.2f, 2f), duration = Random.Range(0.02f, 0.03f);
            int steps = Mathf.Max(1, Mathf.RoundToInt(length / Step));
            Vector3 outward = new Vector3(tip.pos.x - center.x, 0f, tip.pos.z - center.z).normalized;
            // Alguns impulsos saltam da água num arco baixo (até ~45 cm) e voltam a tocar mais à frente.
            float hop = Random.value < 0.35f ? Random.Range(0.1f, 0.45f) * Mathf.Clamp01(length / 1.2f) : 0f;

            for (int k = 1; k <= steps; k++)
            {
                float arrival = start + duration * k / steps;

                // Gancho ocasional: curva forte de 120–200° com raio de 0,25–0,45 m.
                if (tip.hookLeft <= 0f && Random.value < 1f - Mathf.Exp(-0.08f * Step))
                {
                    tip.hookLeft = Random.Range(120f, 200f);
                    tip.hookRate = Step / Random.Range(0.25f, 0.45f) * Mathf.Rad2Deg * (Random.value < 0.5f ? -1f : 1f);
                }
                Vector3 desired;
                if (tip.hookLeft > 0f)
                {
                    desired = Quaternion.Euler(0f, tip.hookRate, 0f) * tip.heading;
                    tip.hookLeft -= Mathf.Abs(tip.hookRate);
                }
                else desired = (tip.heading * 0.6f + Field(tip.pos) * 0.2f + outward * 0.2f).normalized;

                // Um inimigo perto puxa a corrente: a ponta desvia-se para os pés dele (mais forte quanto mais perto).
                if (Attractor(tip.pos, out Vector3 toward, out float pull))
                {
                    desired = Vector3.Lerp(desired, toward, pull).normalized;
                    tip.hookLeft = 0f;
                }

                // Validar o passo; em seco tentar ±30/60/90° (menor desvio, evitando água já percorrida).
                Vector3 chosen = Vector3.zero;
                float best = float.MaxValue;
                float[] offsets = { 0f, 30f, -30f, 60f, -60f, 90f, -90f };
                foreach (float off in offsets)
                {
                    Vector3 d = Quaternion.Euler(0f, off, 0f) * desired;
                    Vector3 next = tip.pos + d * Step;
                    if ((next - center).magnitude > maxDistance || !grid.Segment(tip.pos, next)) continue;
                    float score = Mathf.Abs(off) + 25f * grid.Visits(next);
                    if (score < best) { best = score; chosen = d; }
                }
                if (best == float.MaxValue) { b.finished = true; break; }

                tip.heading = chosen;
                Vector3 np = tip.pos + chosen * Step;
                np.y = 0.025f;
                tip.pos = np;
                grid.Visit(np);
                // Tremor fino (1–2,5 cm), só no desenho.
                Vector3 perp = new Vector3(-chosen.z, 0f, chosen.x);
                float jitter = (Mathf.PerlinNoise(b.seed * 31f, b.length / Random.Range(0.1f, 0.2f)) - 0.5f) * 2f * Random.Range(0.01f, 0.025f);
                float height = hop * Mathf.Sin(Mathf.PI * k / steps) + Random.Range(0f, 0.05f);
                Vector3 drawn = np + perp * jitter + Vector3.up * height;
                AddPoint(b, drawn, arrival);

                // Espigões: de vez em quando um raio curto salta para cima a partir do canal.
                if (Random.value < 1f - Mathf.Exp(-0.06f * Step)) Spur(drawn, arrival);

                // Energia gasta por metro; ramos secundários com comprimento limitado.
                b.energy -= Step;
                if (b.energy <= 0f || (b.limit > 0f && b.length >= b.limit)) { b.finished = true; break; }

                // Ramificar a partir do canal já aceso.
                if (Random.value < 1f - Mathf.Exp(-0.18f * Step) && b.energy >= 3f && tips.Count < MaxTips && b.length - b.lastForkS >= 0.6f)
                {
                    float give = Mathf.Min(0.3f * b.energy, 3f);
                    Branch child = NewBranch(give, 3f, true);
                    if (child != null)
                    {
                        b.energy -= give;
                        b.lastForkS = b.length;
                        AddPoint(child, drawn, arrival);
                        float ang = Random.Range(35f, 80f) * (Random.value < 0.5f ? -1f : 1f);
                        tips.Add(new Tip { branch = child, pos = np, heading = Quaternion.Euler(0f, ang, 0f) * chosen, nextTime = arrival + Random.Range(0.01f, 0.04f) });
                        SetPulse(b, arrival, b.length, -PulseSpeed); // ao alimentar o filho, um pulso corre para trás
                    }
                }
            }

            if (!b.finished && Random.value < 0.3f) SetPulse(b, start + duration + Random.Range(0.06f, 0.18f), 0f, PulseSpeed);
            if (Random.value < 0.35f) ripples.Add((start + duration, tip.pos));
            tip.nextTime = start + duration + Random.Range(0.01f, 0.025f);
        }

        // Corpos: quando um canal passa junto aos pés, o choque sobe pelo corpo; molhados levam mais.
        const float AttractRange = 3.5f;

        // Inimigo ainda não eletrocutado mais perto da ponta, dentro do alcance de atração.
        bool Attractor(Vector3 from, out Vector3 toward, out float pull)
        {
            toward = Vector3.zero;
            pull = 0f;
            float best = AttractRange;
            foreach (LockOnTarget t in targets)
            {
                if (t == null || shocked.Contains(t)) continue;
                Vector3 d = t.transform.position - from;
                d.y = 0f;
                float dist = d.magnitude;
                if (dist >= best || dist < 1e-3f) continue;
                best = dist;
                toward = d / dist;
                pull = Mathf.Lerp(0.85f, 0.3f, dist / AttractRange);
            }
            return pull > 0f;
        }

        void ShockBodies()
        {
            // Climb acrescenta ramos: percorrer só os que existiam à entrada, sem invalidar o enumerador.
            int count = branches.Count;
            for (int bi = 0; bi < count; bi++)
            {
                Branch b = branches[bi];
                if (!b.onGround) { b.bodyChecked = b.points.Count; continue; }
                for (; b.bodyChecked < b.points.Count; b.bodyChecked++)
                {
                    Point pt = b.points[b.bodyChecked];
                    foreach (LockOnTarget t in targets)
                    {
                        if (t == null || shocked.Contains(t)) continue;
                        Vector3 tp = t.transform.position;
                        if (new Vector2(pt.pos.x - tp.x, pt.pos.z - tp.z).magnitude > 0.85f) continue;
                        shocked.Add(t);
                        Climb(t, pt);
                    }
                }
            }
        }

        // Eletrocutar: a corrente sobe pelo corpo como uma cobra, depressa, a enrolá-lo (hélice à volta do corpo,
        // 2–3 voltas em ~120 ms), e volta a correr por ela duas vezes. Molhado: duas cobras em sentidos opostos.
        void Climb(LockOnTarget t, Point from)
        {
            float wet = WetSurface.WetnessOf(t.transform);
            int snakes = wet > 0.2f ? 2 : 1;
            Vector3 axis = t.transform.position;
            float radius = 0.4f, height = 2f;
            if (t.GetComponent<Collider>() is CapsuleCollider cap)
            {
                Vector3 s3 = cap.transform.lossyScale;
                radius = cap.radius * Mathf.Max(Mathf.Abs(s3.x), Mathf.Abs(s3.z));
                height = cap.height * Mathf.Abs(s3.y);
                axis = cap.transform.TransformPoint(cap.center) - Vector3.up * height * 0.5f;
            }
            else if (t.GetComponent<Collider>() is Collider c)
            {
                radius = Mathf.Min(c.bounds.extents.x, c.bounds.extents.z);
                height = c.bounds.size.y;
                axis = new Vector3(c.bounds.center.x, c.bounds.min.y, c.bounds.center.z);
            }

            Vector3 start = from.pos - axis;
            float startAngle = Mathf.Atan2(start.z, start.x);
            for (int sIndex = 0; sIndex < snakes; sIndex++)
            {
                Branch b = NewBranch(0f, 0f, false);
                if (b == null) return;
                b.blackoutFrom = b.blackoutTo = -1f; // a cobra não tem apagões: é o momento principal
                float turns = Random.Range(2f, 3f);
                float dir = sIndex == 0 ? (Random.value < 0.5f ? 1f : -1f) : -1f;
                float top = height * Random.Range(0.85f, 0.97f);
                float a0 = startAngle + sIndex * Mathf.PI;
                int n = Mathf.RoundToInt(turns * 28f);
                AddPoint(b, from.pos, from.arrival);
                for (int i = 1; i <= n; i++)
                {
                    float k = i / (float)n;
                    float ang = a0 + dir * k * turns * Mathf.PI * 2f;
                    // Um pouco irregular: o raio da hélice e a subida tremem, como uma cobra a correr.
                    float r = radius + 0.05f + (Mathf.PerlinNoise(b.seed * 40f, k * 9f) - 0.5f) * 0.06f;
                    float h = Mathf.Lerp(0.05f, top, Mathf.Pow(k, 0.85f)) + (Mathf.PerlinNoise(k * 13f, b.seed * 17f) - 0.5f) * 0.08f;
                    Vector3 q = axis + new Vector3(Mathf.Cos(ang) * r, h, Mathf.Sin(ang) * r);
                    AddPoint(b, q, from.arrival + 0.015f + 0.12f * k);
                }
                b.finished = true;
                // Continua a eletrocutar: a corrente volta a correr pela cobra, de baixo para cima.
                SetPulse(b, from.arrival + Random.Range(0.2f, 0.26f), 0f, PulseSpeed);
                SetPulse(b, from.arrival + Random.Range(0.38f, 0.46f), 0f, PulseSpeed);
            }
        }

        // ------------------------------------------------------------------ Tabela e malha

        void UpdateTable()
        {
            for (int i = 0; i < MaxBranches; i++)
            {
                float gate = 0f, seed = 0f;
                Vector4 p0 = Vector4.zero, p1 = Vector4.zero;
                if (i < branches.Count)
                {
                    Branch b = branches[i];
                    gate = age >= b.blackoutFrom && age < b.blackoutTo ? 0f : 1f;
                    seed = b.seed;
                    p0 = b.pulse0;
                    p1 = b.pulse1;
                }
                tableColors[i * 3 + 0] = new Color(gate, seed, 1f, 0f);
                tableColors[i * 3 + 1] = new Color(p0.x, p0.y, p0.z, p0.w);
                tableColors[i * 3 + 2] = new Color(p1.x, p1.y, p1.z, p1.w);
            }
            table.SetPixels(tableColors);
            table.Apply(false);
            Shader.SetGlobalTexture(TableId, table);
        }

        void BuildMesh()
        {
            verts.Clear(); normals.Clear(); uv0.Clear(); uv1.Clear(); uv2.Clear(); tris.Clear();
            foreach (Branch b in branches)
            {
                int n = b.points.Count;
                if (n < 2) continue;
                int start = verts.Count;
                for (int i = 0; i < n; i++)
                {
                    Point p = b.points[i];
                    Vector3 tangent = b.points[Mathf.Min(i + 1, n - 1)].pos - b.points[Mathf.Max(i - 1, 0)].pos;
                    // Afinar os últimos 18 cm dos ramos que já acabaram.
                    float taper = b.finished ? Mathf.Clamp01((b.length - p.s) / 0.18f) : 1f;
                    taper = Mathf.Max(taper, 0.05f);
                    for (int side = -1; side <= 1; side += 2)
                    {
                        verts.Add(p.pos);
                        normals.Add(tangent);
                        uv0.Add(new Vector2(p.s, side));
                        uv1.Add(new Vector2(p.arrival, b.id));
                        uv2.Add(new Vector2(p.width, taper));
                    }
                    if (i == n - 1) continue;
                    int a = start + i * 2;
                    tris.Add(a); tris.Add(a + 2); tris.Add(a + 1);
                    tris.Add(a + 1); tris.Add(a + 2); tris.Add(a + 3);
                }
            }
            mesh.Clear();
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uv0);
            mesh.SetUVs(1, uv1);
            mesh.SetUVs(2, uv2);
            mesh.SetTriangles(tris, 0);
            // Os vértices são alargados no shader: dar margem aos limites.
            Bounds bounds = mesh.bounds;
            bounds.Expand(1f);
            mesh.bounds = bounds;
        }
    }
}
