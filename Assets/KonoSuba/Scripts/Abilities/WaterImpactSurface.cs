using UnityEngine;

namespace AnimLab.KonoSuba
{
    /// <summary>
    /// Impacto da Bola de Água como uma só massa contínua (nunca se parte em gotas).
    /// A própria bola vira uma malha fechada cujos vértices são "nós" de água com posição e velocidade:
    ///   - contra um corpo: a água não ressalta — perde a velocidade que entra no corpo e transforma-a em
    ///     espalhamento à volta do ponto de contacto; o lençol envolve o corpo, cola-se a ele (abranda junto à
    ///     superfície) e escorre com a gravidade; a parte de trás da bola atravessa o corpo e sai por trás;
    ///   - no chão: espalha-se rente à película; na queda direta na água, o bordo sobe numa coroa baixa;
    ///   - coesão: cada nó é puxado para a média dos vizinhos e as arestas não esticam sem limite (tensão
    ///     superficial), por isso a massa fica sempre ligada e não vira fios;
    ///   - volume: cada nó leva uma parte fixa da água; quando assenta no chão entrega-a à película (uma só vez).
    ///     A espessura vem do volume que falta entregar a dividir pela área da malha, e o shader (WaterSheet)
    ///     usa-a: lençol fino fica quase invisível, por isso a água desaparece a afinar-se, não a partir-se.
    /// Uma bala comprimida leva a água toda da bola cheia: no impacto descomprime-se para o raio real.
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class WaterImpactSurface : MonoBehaviour
    {
        const int Lon = 40, Lat = 28;
        const float Step = 1f / 120f;
        const float Skin = 0.02f;          // a água fica rente à pele, não dentro dela
        const float MaxWaterSpeed = 12f;
        const float ImpactKeep = 0.55f;
        const float MaxClimb = 3f;         // velocidade máxima a subir depois de tocar no corpo (m/s)    // velocidade tangencial que sobra depois do choque
        const float Spread = 0.45f;        // fração da velocidade de choque que vira espalhamento (o resto perde-se)
        const float WallFriction = 3.5f;   // abrandamento junto ao corpo (água "cola")
        const float Adhesion = 5f;         // puxa de volta à pele a água lenta que se afasta pouco
        const float GroundFriction = 2.2f;
        const float Cohesion = 0.3f;      // suavização das posições (por passo)
        const float VelocityCohesion = 0.16f;
        const float MaxStretch = 3.5f;       // comprimento máximo de uma aresta face ao inicial
        const float SettleTime = 0.18f;    // tempo no chão até a água desse nó passar para a película
        const float MaxLife = 4.5f;

        Vector3[] pos, vel, renderPos;
        float[] groundTime, aeration;
        bool[] pierce, deposited, touchedBody;
        Color[] colors;
        float[] vertexArea;
        int[] tris;
        Mesh mesh;
        float meanVertexArea;
        float restEdge, nodeVolume, totalVolume, depositedVolume, age, accumulator, crown;
        Vector3 bodyImpact, groundImpact;
        bool hasGroundImpact;
        CapsuleCollider capsule;
        Transform bodyRoot;
        int bodyContacts;

        /// <param name="center">Centro da bola no instante do contacto.</param>
        /// <param name="visibleRadius">Raio que se vê (pequeno na bala comprimida).</param>
        /// <param name="waterRadius">Raio da água que a bola leva de facto.</param>
        /// <param name="body">Corpo atingido, ou null se caiu na água.</param>
        /// <param name="volumeFraction">Parte da água da bola que esta massa leva (o impacto comprimido reparte-a).</param>
        /// <param name="pierceFraction">Parte de trás que atravessa o corpo; negativo = automático.</param>
        public static WaterImpactSurface Spawn(Vector3 center, Vector3 velocity, float visibleRadius, float waterRadius,
                                               float compression, Collider body, Material material,
                                               float volumeFraction = 1f, float pierceFraction = -1f,
                                               float fanSpeed = 0f)
        {
            waterRadius *= Mathf.Pow(Mathf.Clamp01(volumeFraction), 1f / 3f);
            visibleRadius = Mathf.Min(visibleRadius, waterRadius);
            var go = new GameObject("WaterImpact");
            var s = go.AddComponent<WaterImpactSurface>();
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
            go.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            s.Init(center, velocity, visibleRadius, waterRadius, compression, body, pierceFraction, fanSpeed);
            return s;
        }

        void Init(Vector3 center, Vector3 velocity, float visibleRadius, float waterRadius, float compression, Collider body, float pierceOverride, float fanSpeed)
        {
            int n = (Lon + 1) * (Lat + 1);
            pos = new Vector3[n];
            vel = new Vector3[n];
            renderPos = new Vector3[n];
            groundTime = new float[n];
            aeration = new float[n];
            pierce = new bool[n];
            deposited = new bool[n];
            touchedBody = new bool[n];
            colors = new Color[n];
            vertexArea = new float[n];

            capsule = body as CapsuleCollider;
            bodyRoot = body != null ? (body.attachedRigidbody != null ? body.attachedRigidbody.transform : body.transform) : null;
            totalVolume = WaterBallAbility.VolumeOf(waterRadius);
            nodeVolume = totalVolume / n;
            crown = body == null ? Mathf.Lerp(0.3f, 0.45f, compression) : 0f;

            Vector3 dir = velocity.sqrMagnitude > 1e-6f ? velocity.normalized : Vector3.down;
            // A massa de água não mantém a velocidade de um projétil (sobretudo a bala a 110 m/s): ao chegar ao alvo
            // continua no máximo a uma velocidade de "balde de água atirado", senão sairia do ecrã num fotograma.
            velocity = dir * Mathf.Min(velocity.magnitude, MaxWaterSpeed);
            // A parte de trás da bola atravessa o corpo (mais na bala comprimida, que tem muito mais força).
            float pierceFraction = pierceOverride >= 0f ? pierceOverride : body != null ? Mathf.Lerp(0.3f, 0.6f, compression) : 0f;
            // A bala comprimida descomprime-se para o raio real nos primeiros ~60 ms.
            float decompress = Mathf.Max(0f, waterRadius - visibleRadius) / 0.06f;

            float seed = Random.value * 50f;
            for (int y = 0; y <= Lat; y++)
                for (int x = 0; x <= Lon; x++)
                {
                    float th = y / (float)Lat * Mathf.PI, ph = x / (float)Lon * Mathf.PI * 2f;
                    Vector3 d = new Vector3(Mathf.Sin(th) * Mathf.Cos(ph), -Mathf.Cos(th), Mathf.Sin(th) * Mathf.Sin(ph));
                    int i = y * (Lon + 1) + x;
                    if (fanSpeed > 0f)
                    {
                        // Descompressão em leque: a água abre-se num disco perpendicular ao movimento (um lençol
                        // que avança e alarga), não numa bola que incha.
                        // Abre em cone para a frente (as bordas vão mais à frente) e com a borda irregular,
                        // para não ficar um disco perfeito.
                        Vector3 lateral = d - dir * Vector3.Dot(d, dir);
                        lateral.y *= 0.45f; // abre-se mais para os lados do que para cima (cai, não sobe)
                        float ragged = 0.7f + 0.6f * Mathf.PerlinNoise(ph * 1.3f + seed, th * 0.7f);
                        pos[i] = center + lateral * visibleRadius + dir * Vector3.Dot(d, dir) * visibleRadius * 0.15f;
                        vel[i] = velocity + lateral * fanSpeed * ragged + dir * fanSpeed * 0.6f * lateral.magnitude;
                    }
                    else
                    {
                        pos[i] = center + d * visibleRadius;
                        vel[i] = velocity + d * decompress;
                    }
                    pierce[i] = Vector3.Dot(d, -dir) > 1f - 2f * pierceFraction;
                }
            restEdge = 2f * Mathf.PI * waterRadius / Lon;

            tris = new int[Lon * Lat * 6];
            int t = 0;
            for (int y = 0; y < Lat; y++)
                for (int x = 0; x < Lon; x++)
                {
                    int a = y * (Lon + 1) + x, b = a + Lon + 1;
                    tris[t++] = a; tris[t++] = b; tris[t++] = a + 1;
                    tris[t++] = a + 1; tris[t++] = b; tris[t++] = b + 1;
                }

            mesh = new Mesh { name = "WaterImpact" };
            mesh.MarkDynamic();
            mesh.vertices = pos;
            mesh.triangles = tris;
            GetComponent<MeshFilter>().sharedMesh = mesh;
            UpdateMesh();
        }

        void Update()
        {
            accumulator += Mathf.Min(Time.deltaTime, 0.05f);
            while (accumulator >= Step)
            {
                accumulator -= Step;
                Simulate(Step);
            }
            age += Time.deltaTime;


            bool done = depositedVolume >= totalVolume * 0.999f || age > MaxLife;
            if (done)
            {
                // O que ainda falta entrega-se onde está, para não perder água.
                for (int i = 0; i < pos.Length; i++) Deposit(i);
                Destroy(gameObject);
                return;
            }
            UpdateMesh();
        }

        void Simulate(float dt)
        {
            Vector3 a = Vector3.zero, b = Vector3.zero;
            float rc = 0f;
            if (capsule != null) CapsuleSegment(capsule, out a, out b, out rc);

            for (int i = 0; i < pos.Length; i++)
            {
                if (deposited[i]) continue;
                Vector3 v = vel[i] + Physics.gravity * dt;
                v *= Mathf.Exp(-1.2f * dt); // lençol fino: o ar trava-o bastante
                Vector3 p = pos[i] + v * dt;

                if (capsule != null)
                {
                    Vector3 q = ClosestOnSegment(a, b, p);
                    Vector3 d = p - q;
                    float dist = d.magnitude;
                    Vector3 n = dist > 1e-5f ? d / dist : Vector3.up;
                    if (pierce[i])
                    {
                        // A atravessar o corpo: perde força lá dentro e sai pelo outro lado.
                        if (dist < rc) v *= Mathf.Exp(-20f * dt);
                    }
                    else if (dist < rc + Skin)
                    {
                        p = q + n * (rc + Skin);
                        if (!touchedBody[i])
                        {
                            touchedBody[i] = true;
                            bodyContacts++;
                            if (bodyContacts == 1) bodyImpact = p;
                            // O corpo fica molhado só onde a água lhe tocou.
                            if (bodyRoot != null) WetSurface.AddSpot(bodyRoot, q + n * rc, 0.22f);
                        }
                        float vn = Vector3.Dot(v, n);
                        if (vn < 0f)
                        {
                            v -= n * vn;
                            // Choque inelástico: a água perde grande parte da energia contra o corpo.
                            if (vn < -2f) v *= ImpactKeep;
                            // A velocidade de choque não ressalta: vira espalhamento à volta do ponto de contacto,
                            // mais para os lados do que para cima (a gravidade ganha).
                            Vector3 away = Vector3.ProjectOnPlane(p - bodyImpact, n);
                            if (away.sqrMagnitude < 1e-6f) away = Vector3.ProjectOnPlane(Random.onUnitSphere, n);
                            away = away.normalized;
                            if (away.y > 0f) away.y *= 0.4f;
                            v += away.normalized * (-vn) * Spread;
                        }
                        // Junto à pele a água abranda (cola) e escorre com a gravidade.
                        Vector3 vt = v - n * Vector3.Dot(v, n);
                        v = n * Vector3.Dot(v, n) + vt * Mathf.Exp(-WallFriction * dt);
                        // Uma massa tão pesada não sobe muito por cima do corpo: a subida perde-se depressa.
                        if (v.y > MaxClimb) v.y = MaxClimb;
                    }
                    else if (touchedBody[i] && dist < rc + Skin + 0.08f && v.magnitude < 2.5f)
                    {
                        v -= n * Adhesion * dt; // água lenta perto da pele volta a encostar-se
                    }
                }

                // Chão (película de água).
                if (p.y < Skin * 0.5f)
                {
                    p.y = Skin * 0.5f;
                    if (!hasGroundImpact) { hasGroundImpact = true; groundImpact = new Vector3(p.x, 0f, p.z); }
                    if (v.y < 0f)
                    {
                        float vn = -v.y;
                        v.y = 0f;
                        Vector3 away = new Vector3(p.x - groundImpact.x, 0f, p.z - groundImpact.z);
                        if (away.sqrMagnitude < 1e-6f) away = new Vector3(v.x, 0f, v.z);
                        if (away.sqrMagnitude > 1e-6f) v += away.normalized * vn * 0.6f;
                        // Queda direta na água: o bordo sobe numa coroa baixa (mais longe do centro, mais sobe).
                        float rim = Mathf.Clamp01(away.magnitude / Mathf.Max(restEdge * Lon / (2f * Mathf.PI), 0.1f));
                        v.y = vn * crown * rim;
                    }
                    Vector3 h = new Vector3(v.x, 0f, v.z) * Mathf.Exp(-GroundFriction * dt);
                    v = new Vector3(h.x, v.y, h.z);
                    groundTime[i] += dt;
                    if (groundTime[i] > SettleTime) Deposit(i);
                }

                // Ar na água: onde vai depressa ou bate com força, a água enche-se de bolhas e fica branca;
                // quando abranda, o ar sai aos poucos e volta a ficar transparente.
                float churn = Mathf.Clamp01((v.magnitude - 4f) / 10f);
                aeration[i] = Mathf.Max(aeration[i] * Mathf.Exp(-2.5f * dt), churn);

                pos[i] = p;
                vel[i] = v;
            }

            Cohere();
        }

        // Tensão superficial: puxa cada nó para a média dos vizinhos (posição e velocidade) e não deixa as arestas
        // esticarem sem limite — a massa fica ligada e lisa, sem pregas nem fios.
        void Cohere()
        {
            int w = Lon + 1;
            var avgP = renderPos;
            for (int y = 0; y <= Lat; y++)
                for (int x = 0; x <= Lon; x++)
                {
                    int i = y * w + x;
                    int xl = (x == 0 ? Lon - 1 : x - 1), xr = (x == Lon ? 1 : x + 1);
                    Vector3 sum = pos[y * w + xl] + pos[y * w + xr];
                    Vector3 vs = vel[y * w + xl] + vel[y * w + xr];
                    int c = 2;
                    if (y > 0) { sum += pos[i - w]; vs += vel[i - w]; c++; }
                    if (y < Lat) { sum += pos[i + w]; vs += vel[i + w]; c++; }
                    avgP[i] = sum / c;
                    if (!deposited[i]) vel[i] = Vector3.Lerp(vel[i], vs / c, VelocityCohesion);
                }
            float maxLen = restEdge * MaxStretch;
            for (int i = 0; i < pos.Length; i++)
            {
                if (deposited[i]) continue;
                // Onde o lençol se dobra sobre si próprio (área local muito pequena), alisa mais: tira os vincos.
                float fold = vertexArea[i] > 0f && vertexArea[i] < meanVertexArea * 0.25f ? 0.35f : 0f;
                Vector3 target = Vector3.Lerp(pos[i], avgP[i], Cohesion + fold);
                if (capsule == null || !touchedBody[i]) pos[i] = target;
                else pos[i] = Vector3.Lerp(pos[i], target, 0.5f); // na pele, suaviza menos para não a atravessar
                if ((avgP[i] - pos[i]).magnitude > maxLen) pos[i] = avgP[i] + (pos[i] - avgP[i]).normalized * maxLen;
            }
            // Polos e costura: os vértices repetidos têm de coincidir para a malha ficar fechada.
            for (int y = 0; y <= Lat; y += Lat)
            {
                Vector3 m = Vector3.zero, mv = Vector3.zero;
                for (int x = 0; x <= Lon; x++) { m += pos[y * w + x]; mv += vel[y * w + x]; }
                m /= Lon + 1; mv /= Lon + 1;
                for (int x = 0; x <= Lon; x++) { pos[y * w + x] = m; vel[y * w + x] = mv; }
            }
            for (int y = 0; y <= Lat; y++) { pos[y * w + Lon] = pos[y * w]; vel[y * w + Lon] = vel[y * w]; }
        }

        void Deposit(int i)
        {
            if (deposited[i]) return;
            deposited[i] = true;
            depositedVolume += nodeVolume;
            Vector3 g = new Vector3(pos[i].x, 0f, pos[i].z);
            WaterSim.TransferVolume(g, 0.35f, nodeVolume);
            // Uma ondinha a cada tantos nós, para a película mostrar a água a chegar sem ruído a mais.
            if ((i & 15) == 0) WaterSim.Impulse(g, 0.3f, -0.012f, 4f);
        }

        void UpdateMesh()
        {
            // Espessura: água que ainda não entrou na película a dividir pela área (é um volume fechado, por isso
            // cada face conta metade). Por vértice, mais grossa onde a malha está mais junta.
            System.Array.Clear(vertexArea, 0, vertexArea.Length);
            float area = 0f;
            for (int t = 0; t < tris.Length; t += 3)
            {
                int i0 = tris[t], i1 = tris[t + 1], i2 = tris[t + 2];
                float ar = Vector3.Cross(pos[i1] - pos[i0], pos[i2] - pos[i0]).magnitude * 0.5f;
                area += ar;
                vertexArea[i0] += ar; vertexArea[i1] += ar; vertexArea[i2] += ar;
            }
            float remaining = Mathf.Max(0f, totalVolume - depositedVolume);
            float thickness = 2f * remaining / Mathf.Max(area, 1e-4f);
            float meanArea = area * 3f / pos.Length;
            meanVertexArea = meanArea;
            for (int i = 0; i < pos.Length; i++)
            {
                float local = thickness * Mathf.Clamp(meanArea / Mathf.Max(vertexArea[i], 1e-6f), 0.3f, 3f);
                if (deposited[i]) local *= 0.2f;
                colors[i] = new Color(local, aeration[i], 0f, 1f);
                renderPos[i] = pos[i] - transform.position;
            }
            mesh.vertices = renderPos;
            mesh.colors = colors;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
        }

        static void CapsuleSegment(CapsuleCollider c, out Vector3 a, out Vector3 b, out float radius)
        {
            Transform t = c.transform;
            Vector3 axis = c.direction == 0 ? Vector3.right : c.direction == 1 ? Vector3.up : Vector3.forward;
            Vector3 s = t.lossyScale;
            float axisScale = Mathf.Abs(c.direction == 0 ? s.x : c.direction == 1 ? s.y : s.z);
            float radScale = c.direction == 0 ? Mathf.Max(Mathf.Abs(s.y), Mathf.Abs(s.z))
                           : c.direction == 1 ? Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.z))
                           : Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.y));
            radius = c.radius * radScale;
            float half = Mathf.Max(0f, c.height * axisScale * 0.5f - radius);
            Vector3 center = t.TransformPoint(c.center);
            Vector3 worldAxis = t.TransformDirection(axis).normalized;
            a = center - worldAxis * half;
            b = center + worldAxis * half;
        }

        static Vector3 ClosestOnSegment(Vector3 a, Vector3 b, Vector3 p)
        {
            Vector3 ab = b - a;
            float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-6f));
            return a + ab * t;
        }

        void OnDestroy()
        {
            if (mesh != null) Destroy(mesh);
        }
    }
}
