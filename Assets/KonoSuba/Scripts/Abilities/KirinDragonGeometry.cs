using System.Collections.Generic;
using UnityEngine;

namespace AnimLab.KonoSuba
{
    /// <summary>
    /// Dragão feito de contornos elétricos contínuos. As ondas viajam da cabeça para a cauda e o ruído
    /// fino evolui suavemente. Usa o mesmo formato de fita e a mesma tabela do LightningDischarge.
    /// </summary>
    public class KirinDragonGeometry
    {
        const int BodyChannels = 6, HeadStart = 6, ImpactStart = 52, ChargeStart = 60;
        const int MaxBranches = 64, TableRows = 64, Samples = 144, BodySamples = 144;
        public const float Descent = 0.65f;
        const float BodyLength = 22.5f, Bite = 1.35f, BiteDuration = 0.12f;
        const float FallSpeed = 9f, Gravity = 32f, NoseReach = 2.65f;

        class Channel
        {
            public readonly List<Vector3> points = new List<Vector3>(BodySamples + 1);
            public float arrival, hold, width, gain, length;
            public bool eye, primary;
        }

        public Mesh Mesh { get; }
        public Texture2D Table { get; }
        public Vector3 ImpactPoint { get; private set; }
        public float ImpactTime { get; private set; }
        public float TailTime { get; private set; }
        public float EndTime => TailTime + 0.3f;
        readonly Channel[] channels = new Channel[MaxBranches];
        readonly Color[] rows = new Color[3 * TableRows];
        readonly Vector3[] controls = new Vector3[5], curve = new Vector3[Samples + 1];
        readonly float[] lengths = new float[Samples + 1];
        readonly Vector3[] body = new Vector3[BodySamples + 1];
        readonly List<Vector3> vertices = new List<Vector3>(6000), tangents = new List<Vector3>(6000);
        readonly List<Vector2> uv0 = new List<Vector2>(6000), uv1 = new List<Vector2>(6000), uv2 = new List<Vector2>(6000);
        readonly List<int> triangles = new List<int>(18000);
        readonly float seed;
        Vector3 head;
        Quaternion facing;
        float age, size;
        int headChannel;

        public KirinDragonGeometry()
        {
            seed = Random.Range(1f, 1000f);
            for (int i = 0; i < channels.Length; i++) channels[i] = new Channel();
            Mesh = new Mesh { name = "KirinDragon", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            Mesh.MarkDynamic();
            Table = new Texture2D(3, TableRows, TextureFormat.RGBAFloat, false, true)
                { name = "KirinBranches", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        }

        public void Rebuild(float time, Vector3 caster, Vector3 destination, Vector3 lateral, Bounds bounds)
        {
            age = time;
            size = Mathf.Clamp(Mathf.Max(bounds.size.x, bounds.size.z) * 1.3f, 1.8f, 1.875f);
            foreach (Channel c in channels) c.points.Clear();

            // Origem mais alta e afastada: o corpo pode sair do enquadramento sem parecer encolhido.
            Vector3 down = Vector3.down;
            ImpactPoint = new Vector3(bounds.center.x, 0f, bounds.center.z);
            // O focinho envolve o alto do alvo; há espaço para fechar a boca antes de tocar na água.
            Vector3 bite = ImpactPoint + Vector3.up * Mathf.Max(bounds.max.y + 0.65f * size, NoseReach * size + 1.8f);
            controls[0] = destination + lateral * 8f + Vector3.up * 28f;
            controls[1] = destination + lateral * 8.5f + Vector3.up * 23f;
            controls[2] = destination + lateral * 3.6f + Vector3.up * 14f;
            controls[3] = bite - down * 2.4f;
            controls[4] = bite;
            BuildCurve();
            ImpactTime = Bite + FallTime(bite.y - NoseReach * size);
            // Margem para o último canal fino e o tremor da cauda passarem inteiramente o corte.
            TailTime = Bite + FallTime(bite.y + BodyLength + 0.35f);
            float u = Mathf.InverseLerp(Descent, Bite, age);
            float endSlope = FallSpeed * (Bite - Descent) / lengths[Samples];
            float progress = (endSlope - 2f) * u * u * u + (3f - endSlope) * u * u;
            float falling = Mathf.Max(0f, age - Bite);
            // A velocidade no fim da curva é a velocidade inicial da queda; nunca fica suspenso na mordida.
            float distance = lengths[Samples] * progress + FallSpeed * falling + 0.5f * Gravity * falling * falling;
            head = Along(distance);
            Vector3 forward = (Along(distance + 0.08f) - Along(distance - 0.08f)).normalized;
            if (forward.sqrMagnitude < 0.01f) forward = down;
            // O eixo vertical da cabeça aponta aproximadamente para o conjurador, legível de cima.
            Vector3 up = Vector3.ProjectOnPlane(caster - destination, forward).normalized;
            if (up.sqrMagnitude < 0.01f) up = Vector3.ProjectOnPlane(Vector3.forward, forward).normalized;
            if (up.sqrMagnitude < 0.01f) up = Vector3.ProjectOnPlane(Vector3.right, forward).normalized;
            facing = Quaternion.LookRotation(forward, up);
            // Sacudir e inclinar sem desviar a mordida; na queda mantém só a rotação em torno do próprio eixo.
            float aim = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(Bite - 0.2f, Bite, age));
            facing *= Quaternion.Euler(aim * (Mathf.Sin(age * 7f) * 5f + Mathf.Sin(age * 12f) * 1.5f),
                                      aim * Mathf.Sin(age * 5.3f) * 7f, Mathf.Sin(age * 4.2f) * (3f + aim * 8f));
            BuildBody(distance, lateral);
            BuildHead();
            BuildCharge(lateral);
            BuildImpact(lateral);
            Upload();
        }

        // Resolver v*t + g*t²/2 = distância, para o impacto e o fim seguirem o tamanho real do dragão.
        static float FallTime(float distance)
        {
            return (Mathf.Sqrt(FallSpeed * FallSpeed + 2f * Gravity * Mathf.Max(0f, distance)) - FallSpeed) / Gravity;
        }

        // Catmull–Rom, com tabela de comprimentos para a cabeça e a cauda viajarem à mesma velocidade.
        void BuildCurve()
        {
            lengths[0] = 0f;
            for (int i = 0; i <= Samples; i++)
            {
                float u = i * 4f / Samples;
                int k = Mathf.Min(3, Mathf.FloorToInt(u));
                float t = u - k;
                Vector3 a = controls[Mathf.Max(0, k - 1)], b = controls[k];
                Vector3 c = controls[k + 1], d = controls[Mathf.Min(4, k + 2)];
                curve[i] = 0.5f * ((2f * b) + (-a + c) * t + (2f * a - 5f * b + 4f * c - d) * t * t
                                  + (-a + 3f * b - 3f * c + d) * t * t * t);
                if (i > 0) lengths[i] = lengths[i - 1] + Vector3.Distance(curve[i - 1], curve[i]);
            }
        }

        Vector3 Along(float distance)
        {
            if (distance <= 0f) return curve[0] + (curve[1] - curve[0]).normalized * distance;
            if (distance >= lengths[Samples])
                return curve[Samples] + Vector3.down * (distance - lengths[Samples]);
            int lo = 0, hi = Samples;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) / 2;
                if (lengths[mid] < distance) lo = mid; else hi = mid;
            }
            return Vector3.Lerp(curve[lo], curve[hi], Mathf.InverseLerp(lengths[lo], lengths[hi], distance));
        }

        float Noise(int id, float s)
        {
            return (Mathf.PerlinNoise(seed + id * 7.13f + age * 3f, s * 3.2f) - 0.5f) * 2f;
        }

        Channel Begin(int id, float arrival, float hold, float width, float gain)
        {
            Channel c = channels[id];
            c.arrival = arrival; c.hold = hold; c.width = width; c.gain = gain; c.eye = false; c.primary = false;
            return c;
        }

        void BuildBody(float distance, Vector3 lateral)
        {
            Vector3 bendUp = Vector3.Cross(lateral, Vector3.down).normalized;
            for (int i = 0; i <= BodySamples; i++)
            {
                float s = BodyLength * i / BodySamples, k = i / (float)BodySamples;
                body[i] = Along(distance - s);
                // Frequências sobrepostas, sempre contínuas; a fase avança no sentido da cauda.
                float envelope = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(s / 3f)) * (1f - k * 0.35f);
                body[i] += envelope * (lateral * (1.15f * Mathf.Sin(s * 0.65f - age * 5f)
                                                      + 0.3f * Mathf.Sin(s * 1.5f - age * 8f))
                                      + bendUp * (0.75f * Mathf.Sin(s * 0.85f - age * 4.3f)
                                                        + 0.22f * Mathf.Sin(s * 1.8f - age * 7.2f)));
            }
            for (int f = 0; f < BodyChannels; f++)
            {
                Channel c = Begin(f, 0.4f + f * 0.018f, EndTime, f % 2 == 0 ? 1.3f : 1f, f % 2 == 0 ? 1.05f : 0.8f);
                for (int i = 0; i <= BodySamples; i++)
                {
                    float k = i / (float)BodySamples, s = k * BodyLength;
                    Vector3 tangent = (body[Mathf.Max(0, i - 1)] - body[Mathf.Min(BodySamples, i + 1)]).normalized;
                    if (tangent.sqrMagnitude < 0.01f) tangent = facing * Vector3.forward;
                    Vector3 right = Vector3.ProjectOnPlane(lateral, tangent).normalized;
                    if (right.sqrMagnitude < 0.01f) right = Vector3.ProjectOnPlane(bendUp, tangent).normalized;
                    Vector3 up = Vector3.Cross(right, tangent).normalized;
                    float angle = f * Mathf.PI * 2f / BodyChannels + s * 0.28f - age * 1.2f
                                  + 0.18f * Mathf.Sin(s * 0.8f - age * 3f);
                    float radius = Mathf.Lerp(1.55f, 0.055f, Mathf.Pow(k, 1.5f));
                    radius *= 1f + 0.07f * Mathf.Sin(s * 1.1f - age * 4f) + Noise(f, s) * 0.025f;
                    Vector3 radial = right * Mathf.Cos(angle) + up * Mathf.Sin(angle);
                    Vector3 offset = radial * radius;
                    // Mistura ao longo do pescoço, para a cabeça poder sacudir sem partir a ligação.
                    float neckAngle = f * Mathf.PI * 2f / BodyChannels - age * 1.2f;
                    Vector3 neck = head + facing * new Vector3(Mathf.Cos(neckAngle) * 0.86f,
                        Mathf.Sin(neckAngle) * 0.7f, -0.3f - s / size) * size;
                    Vector3 p = body[i] + offset + right * Noise(f + 20, s) * 0.018f;
                    c.points.Add(Vector3.Lerp(neck, p, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(s / 3f))));
                }
            }
        }

        void BuildHead()
        {
            headChannel = HeadStart;
            float close = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(Bite, Bite + BiteDuration, age));
            float prepare = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(Descent, Bite - 0.08f, age));
            float open = (Mathf.Lerp(72f, 86f, prepare) + Mathf.Sin(age * 9f) * 3f) * (1f - close);
            // Contorno contínuo: crânio, focinho comprido e mandíbula independente.
            HeadLine(false, open, 1.65f, new Vector3(-0.9f, 0f, -0.55f), new Vector3(-1f, 0.45f, 0.3f),
                     new Vector3(-0.7f, 0.38f, 1.7f), new Vector3(-0.55f, 0.2f, 2.65f),
                     new Vector3(0.55f, 0.2f, 2.65f), new Vector3(0.7f, 0.38f, 1.7f),
                     new Vector3(1f, 0.45f, 0.3f), new Vector3(0.9f, 0f, -0.55f));
            channels[headChannel - 1].primary = true; // crânio e focinho: o traço principal
            HeadLine(true, open, 1.6f, new Vector3(-0.92f, -0.12f, -0.15f), new Vector3(-0.82f, -0.28f, 0.65f),
                     new Vector3(-0.62f, -0.3f, 1.9f), new Vector3(-0.5f, -0.24f, 2.55f),
                     new Vector3(0.5f, -0.24f, 2.55f), new Vector3(0.62f, -0.3f, 1.9f),
                     new Vector3(0.82f, -0.28f, 0.65f), new Vector3(0.92f, -0.12f, -0.15f));
            channels[headChannel - 1].primary = true; // mandíbula
            for (int side = -1; side <= 1; side += 2)
            {
                // Sobrancelha, olho, corno recuado e maçã do rosto.
                HeadLine(false, open, 1.45f, new Vector3(side * 1f, 0.32f, 0.25f),
                         new Vector3(side * 0.82f, 0.75f, 0.68f), new Vector3(side * 0.48f, 0.58f, 1.25f));
                // Um ponto quente fechado por olho, em vez de mais um risco a confundir o focinho.
                HeadLine(false, open, 1.9f, new Vector3(side * 0.75f, 0.42f, 0.85f),
                         new Vector3(side * 0.66f, 0.5f, 0.92f), new Vector3(side * 0.57f, 0.42f, 0.99f),
                         new Vector3(side * 0.66f, 0.36f, 0.92f), new Vector3(side * 0.75f, 0.42f, 0.85f));
                channels[headChannel - 1].eye = true;
                HeadLine(false, open, 1.3f, new Vector3(side * 0.78f, 0.5f, -0.1f),
                         new Vector3(side * 1.1f, 1.05f, -0.85f), new Vector3(side * 1.15f, 1.4f, -1.75f),
                         new Vector3(side * 0.9f, 1.6f, -2.6f));
                HeadLine(false, open, 1.05f, new Vector3(side * 0.9f, 0f, -0.55f),
                         new Vector3(side * 1f, -0.16f, 0.45f), new Vector3(side * 0.68f, 0.05f, 1.75f));
                // Bigodes longos: presos ao focinho e soltos na ponta, sem faíscas avulsas.
                FlowLine(side, false, new Vector3(side * 0.64f, 0.18f, 2.1f),
                         new Vector3(side * 2.6f, 0.15f, 2.25f), new Vector3(side * 3f, 0.8f, -0.7f),
                         new Vector3(side * 2.3f, 1.25f, -2.2f));
                FlowLine(side, true, new Vector3(side * 0.84f, 0.3f, -0.35f),
                         new Vector3(side * 1.8f, 0.8f, -0.9f), new Vector3(side * 1.6f, 0.6f, -2.2f),
                         new Vector3(side * 0.7f, 0.4f, -3.4f));
                // Presas nas duas mandíbulas: seguem a articulação, sem tocar no collider do inimigo.
                for (int tooth = 0; tooth < 3; tooth++)
                {
                    float z = 0.95f + tooth * 0.65f, x = side * (0.8f - tooth * 0.11f);
                    float fang = tooth == 1 ? 0.72f : 0.48f;
                    HeadLine(false, open, 0.9f, new Vector3(x, 0.25f, z), new Vector3(x * 0.9f, 0.25f - fang, z + 0.12f),
                             new Vector3(x, 0.25f, z + 0.22f));
                    HeadLine(true, open, 0.8f, new Vector3(x, -0.27f, z), new Vector3(x * 0.9f, -0.27f + fang * 0.8f, z + 0.12f),
                             new Vector3(x, -0.27f, z + 0.22f));
                }
            }
            HeadLine(false, open, 1.1f, new Vector3(-0.9f, 0.4f, -0.2f), new Vector3(0f, 0.85f, -0.55f), new Vector3(0.9f, 0.4f, -0.2f));
            HeadLine(false, open, 0.85f, new Vector3(0f, 0.85f, -0.55f), new Vector3(0f, 0.52f, 1.2f), new Vector3(0f, 0.2f, 2.65f));
            // Duas cristas compridas acompanham o pescoço ondulante, sem voltar a encher o corpo de fios.
            for (int side = -1; side <= 1; side += 2)
            {
                Channel c = Begin(headChannel++, 0.4f, EndTime, 1.05f, 0.85f);
                for (int i = 0; i <= 48; i++)
                {
                    float k = i / 48f, s = BodyLength * i / BodySamples;
                    Vector3 crest = facing * new Vector3(side * (1.35f + 0.32f * Mathf.Sin(s * 2.2f - age * 6f)),
                                                        0.65f + 0.28f * Mathf.Sin(s * 1.8f - age * 5f), 0f);
                    Vector3 p = body[i] + crest * Mathf.Sin(k * Mathf.PI);
                    Vector3 start = head + facing * new Vector3(side * 0.9f, 0.3f, -0.55f) * size;
                    c.points.Add(Vector3.Lerp(start, p, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(s / 2f))));
                }
            }
        }

        void FlowLine(int side, bool mane, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            Channel channel = Begin(headChannel++, 0.38f, EndTime, mane ? 1.1f : 1.2f, mane ? 0.8f : 1f);
            for (int i = 0; i <= 40; i++)
            {
                float t = i / 40f, u = 1f - t;
                Vector3 p = u * u * u * a + 3f * u * u * t * b + 3f * u * t * t * c + t * t * t * d;
                p += new Vector3(side * 0.22f * Mathf.Sin(t * 8f - age * 5f),
                                 0.32f * Mathf.Sin(t * 10f - age * 6f + side),
                                 0.18f * Mathf.Sin(t * 7f - age * 4f)) * t;
                channel.points.Add(head + facing * p * size);
            }
        }

        void HeadLine(bool jaw, float angle, float width, params Vector3[] points)
        {
            int id = headChannel++;
            Channel c = Begin(id, 0.35f + (id - HeadStart) * 0.002f, EndTime, width, width > 1.5f ? 1.2f : 0.9f);
            Quaternion rotation = Quaternion.Euler(angle, 0f, 0f);
            for (int segment = 0; segment < points.Length - 1; segment++)
            {
                int steps = Mathf.Max(2, Mathf.CeilToInt(Vector3.Distance(points[segment], points[segment + 1]) / 0.09f));
                for (int j = segment == 0 ? 0 : 1; j <= steps; j++)
                {
                    Vector3 p = Vector3.Lerp(points[segment], points[segment + 1], j / (float)steps);
                    float noise = Noise(id, segment + j / (float)steps);
                    p += new Vector3(noise, Noise(id + 10, p.z), noise * 0.3f) * 0.009f;
                    if (jaw) p = rotation * (p - new Vector3(0f, -0.12f, 0f)) + new Vector3(0f, -0.12f, 0f);
                    c.points.Add(head + facing * p * size);
                }
            }
        }

        void BuildCharge(Vector3 lateral)
        {
            for (int f = 0; f < 2; f++)
            {
                Channel c = Begin(ChargeStart + f, f * 0.045f, 0.23f, 0.7f, age < 0.35f ? 0.45f : 0f);
                Vector3 to = controls[0];
                Vector3 from = to + lateral * (f == 0 ? -4f : 4.5f) + Vector3.up * 3f;
                for (int j = 0; j <= 16; j++)
                {
                    float t = j / 16f;
                    c.points.Add(Vector3.Lerp(from, to, t) + lateral * Noise(ChargeStart + f, t) * Mathf.Sin(t * Mathf.PI) * 0.05f);
                }
            }
        }

        void BuildImpact(Vector3 lateral)
        {
            if (age < ImpactTime || age >= EndTime) return;
            float arrival = ImpactTime + Mathf.Floor((age - ImpactTime) / 0.04f) * 0.04f;
            float gain = 0.95f * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(TailTime, EndTime, age)));
            Vector3 across = Vector3.Cross(Vector3.up, lateral);
            // Um anel partido e quatro estalos rasteiros, refeitos aos saltos enquanto o corpo entra.
            for (int f = 0; f < 8; f++)
            {
                Channel c = Begin(ImpactStart + f, arrival, 0f, f < 4 ? 0.8f : 0.55f, gain);
                float angle = (f % 4) * Mathf.PI * 0.5f + Noise(ImpactStart + f, 0f) * 0.4f;
                float radius = 0.22f + 0.14f * (1f + Noise(ImpactStart + f, 1f));
                for (int j = 0; j <= 10; j++)
                {
                    float k = j / 10f;
                    float a = angle + (f < 4 ? k * 1.35f : Noise(ImpactStart + f, k) * 0.35f);
                    float r = f < 4 ? radius + Noise(ImpactStart + f, k) * 0.04f : 0.06f + k * (radius + 0.35f);
                    float height = 0.025f + (f < 4 ? 0.02f : 0.09f) * Mathf.Sin(k * Mathf.PI);
                    c.points.Add(ImpactPoint + (lateral * Mathf.Cos(a) + across * Mathf.Sin(a)) * r + Vector3.up * height);
                }
            }
        }

        Vector3 PinchAtGround(Vector3 p)
        {
            // Os últimos centímetros convergem para o mesmo ponto, em vez de atravessarem a água espalhados.
            float spread = Mathf.Clamp01(p.y / 0.45f);
            p.x = Mathf.Lerp(ImpactPoint.x, p.x, spread);
            p.z = Mathf.Lerp(ImpactPoint.z, p.z, spread);
            return p;
        }

        void RibbonPoint(Vector3 p, Vector3 tangent, float s, int id, Channel c)
        {
            if (tangent.sqrMagnitude < 1e-8f) tangent = Vector3.up;
            // O shader pode alargar o halo até 25 cm para cada lado. Junto ao corte, alargar só na horizontal:
            // assim nem a fita em píxeis passa por baixo de y=0, sem alterar o shader partilhado com o Raio.
            if (p.y <= 0.26f) tangent = Vector3.up;
            float taper = Mathf.Max(0.05f, Mathf.Clamp01((c.length - s) / 0.18f));
            for (int side = -1; side <= 1; side += 2)
            {
                vertices.Add(p); tangents.Add(tangent);
                uv0.Add(new Vector2(s, side));
                uv1.Add(new Vector2(c.arrival + (id < BodyChannels ? s * 0.004f : 0f), id));
                uv2.Add(new Vector2(c.width * (0.97f + Noise(id, s) * 0.03f), taper));
            }
        }

        void Upload()
        {
            vertices.Clear(); tangents.Clear(); uv0.Clear(); uv1.Clear(); uv2.Clear(); triangles.Clear();
            System.Array.Clear(rows, 0, rows.Length);
            for (int id = 0; id < MaxBranches; id++)
            {
                Channel c = channels[id];
                int n = c.points.Count;
                if (n < 2) continue;
                float s = 0f;
                c.length = 0f;
                for (int i = 1; i < n; i++) c.length += Vector3.Distance(c.points[i - 1], c.points[i]);
                for (int i = 1; i < n; i++)
                {
                    Vector3 a = c.points[i - 1], b = c.points[i];
                    float fromS = s;
                    s += Vector3.Distance(a, b);
                    if (a.y < 0f && b.y < 0f) continue;
                    float toS = s;
                    // Cortar cada segmento separadamente: nunca unir duas partes através da zona já engolida.
                    if (a.y < 0f || b.y < 0f)
                    {
                        float cut = a.y / (a.y - b.y);
                        Vector3 edge = Vector3.Lerp(a, b, cut);
                        edge.y = 0f;
                        float edgeS = Mathf.Lerp(fromS, toS, cut);
                        if (a.y < 0f) { a = edge; fromS = edgeS; }
                        else { b = edge; toS = edgeS; }
                    }
                    if (id < ImpactStart) { a = PinchAtGround(a); b = PinchAtGround(b); }
                    Vector3 tangentA = c.points[i] - c.points[Mathf.Max(i - 2, 0)];
                    Vector3 tangentB = c.points[Mathf.Min(i + 1, n - 1)] - c.points[i - 1];
                    int start = vertices.Count;
                    RibbonPoint(a, tangentA, fromS, id, c);
                    RibbonPoint(b, tangentB, toS, id, c);
                    triangles.Add(start); triangles.Add(start + 2); triangles.Add(start + 1);
                    triangles.Add(start + 1); triangles.Add(start + 2); triangles.Add(start + 3);
                }
                float flicker = 0.97f + Noise(id, 0f) * 0.03f;
                float gain = c.gain * flicker;
                rows[id * 3] = new Color(gain, Mathf.Repeat(seed + id * 0.137f, 1f), 1f, c.hold);
                if (age >= c.arrival && id < BodyChannels)
                {
                    float pulse = 0.55f + Mathf.Floor(Mathf.Max(0f, age - 0.55f) / 0.3f) * 0.3f;
                    rows[id * 3 + 1] = new Color(pulse, c.length, -55f, 0.94f);
                    rows[id * 3 + 2] = new Color(pulse - 0.3f, c.length, -55f, 0.9f);
                }
            }
            Mesh.Clear();
            Mesh.SetVertices(vertices); Mesh.SetNormals(tangents);
            Mesh.SetUVs(0, uv0); Mesh.SetUVs(1, uv1); Mesh.SetUVs(2, uv2);
            Mesh.SetTriangles(triangles, 0);
            Bounds bounds = Mesh.bounds;
            bounds.Expand(1f);
            Mesh.bounds = bounds;
            Table.SetPixels(rows);
            Table.Apply(false);
        }

        /// <summary>Amostras projetadas para o brilho difuso no nevoeiro, com a mesma sustentação da cabeça.</summary>
        /// <summary>
        /// Os contornos atuais como fios do raio anime (AnimeBolt): recortados com o rng dado (o KirinEffect muda-o
        /// várias vezes por segundo: crepitar), cortados no chão, com fiapos a saltar da cabeça.
        /// Hierarquia: principais (crânio, focinho, mandíbula) grossos e pouco recortados; pormenores da cabeça
        /// (cornos, sobrancelhas, bigodes, dentes, cristas) finos; corpo no meio; olhos à parte.
        /// </summary>
        public void CollectBranches(List<AnimeBolt.Branch> primary, List<AnimeBolt.Branch> lines, List<AnimeBolt.Branch> eyes,
            float baseWidth, System.Random rng)
        {
            primary.Clear();
            lines.Clear();
            eyes.Clear();
            float R() => (float)rng.NextDouble();
            float scale = size / 1.8f;
            for (int id = 0; id < MaxBranches; id++)
            {
                Channel c = channels[id];
                if (c.points.Count < 2 || age < c.arrival || c.gain <= 0.01f) continue;
                if (id >= ChargeStart && age > c.arrival + c.hold) continue;
                bool head = id >= HeadStart && id < ImpactStart && !c.eye;
                float tier = c.primary ? 1.7f : head ? 0.6f : 0.85f;
                float width = baseWidth * c.width * Mathf.Clamp(c.gain, 0.4f, 1.2f) * scale * tier;
                float jag = width * (c.primary ? 0.35f : 0.9f);
                List<Vector3> run = null;
                for (int i = 0; i < c.points.Count; i++)
                {
                    Vector3 p = c.points[i];
                    if (p.y < 0f) { Flush(c, id, width); run = null; continue; }
                    if (id < ImpactStart) p = PinchAtGround(p);
                    if (i > 0 && i < c.points.Count - 1) p += new Vector3(R() * 2f - 1f, R() * 2f - 1f, R() * 2f - 1f) * jag;
                    run ??= new List<Vector3>();
                    run.Add(p);
                }
                Flush(c, id, width);

                void Flush(Channel ch, int channelId, float w)
                {
                    if (run == null || run.Count < 2) return;
                    (ch.eye ? eyes : ch.primary ? primary : lines).Add(new AnimeBolt.Branch { points = run, width = w, id = Mathf.Repeat(channelId * 0.137f, 1f), taper = false });
                    // Fiapos a crepitar a sair dos contornos da cabeça.
                    if (ch.eye || channelId < HeadStart || channelId >= ImpactStart) return;
                    for (int k = 0; k < run.Count; k++)
                    {
                        if (R() >= 0.035f) continue;
                        Vector3 dir = new Vector3(R() * 2f - 1f, R() * 2f - 1f, R() * 2f - 1f).normalized;
                        List<Vector3> hair = AnimeBolt.Jagged(run[k], run[k] + dir * Mathf.Lerp(0.25f, 0.7f, R()) * scale, 3, 0.4f, 0f, rng);
                        lines.Add(new AnimeBolt.Branch { points = hair, width = w * 0.45f, id = R(), taper = true });
                    }
                }
            }
        }

        public void FogSamples(List<Vector3> positions, List<float> arrivals, List<float> holds)
        {
            positions.Clear(); arrivals.Clear(); holds.Clear();
            for (int id = 0; id < headChannel; id++)
            {
                Channel c = channels[id];
                for (int i = 0; i < c.points.Count; i += 6)
                {
                    positions.Add(c.points[i]); arrivals.Add(c.arrival); holds.Add(c.hold);
                }
            }
        }

        public void Release()
        {
            Object.Destroy(Mesh);
            Object.Destroy(Table);
        }
    }
}
