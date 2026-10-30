using System.Collections.Generic;
using UnityEngine;

namespace AnimLab.KonoSuba
{
    /// <summary>
    /// Malhas de gelo: lâminas facetadas (prismas de 4–6 lados, achatados, com a ponta descentrada) e a "flor" de gelo,
    /// uma coroa de lâminas grandes inclinadas para fora com lascas mais pequenas à volta da base.
    /// Atributos para o shader KonoSuba/Ice: uv0 = (tom da faceta, altura 0..1 ao longo da lâmina),
    /// uv1 = (aleatório da lâmina, comprimento), uv2 = distâncias (m) às arestas da faceta, tangente = eixo da lâmina.
    /// </summary>
    public static class IceShard
    {
        const float NoEdge = 9f;

        class Builder
        {
            public readonly List<Vector3> pos = new(), nrm = new();
            public readonly List<Vector2> uv0 = new(), uv1 = new();
            public readonly List<Vector4> uv2 = new(), tan = new();
            public readonly List<int> tris = new();

            // Triângulo com a normal plana virada para fora do eixo da lâmina.
            public void Tri(Vector3 p0, Vector3 p1, Vector3 p2, Vector4 e0, Vector4 e1, Vector4 e2,
                            Vector3 bottom, Vector3 axis, float total, float tone, float shard, float length)
            {
                Vector3 c = (p0 + p1 + p2) / 3f;
                Vector3 onAxis = bottom + axis * Vector3.Dot(c - bottom, axis);
                Vector3 n = Vector3.Cross(p1 - p0, p2 - p0).normalized;
                if (Vector3.Dot(n, c - onAxis) < 0f)
                {
                    (p1, p2) = (p2, p1);
                    (e1, e2) = (e2, e1);
                    n = -n;
                }
                int i = pos.Count;
                Add(p0, e0); Add(p1, e1); Add(p2, e2);
                tris.Add(i); tris.Add(i + 1); tris.Add(i + 2);

                void Add(Vector3 p, Vector4 e)
                {
                    pos.Add(p);
                    nrm.Add(n);
                    uv0.Add(new Vector2(tone, Mathf.Clamp01(Vector3.Dot(p - bottom, axis) / total)));
                    uv1.Add(new Vector2(shard, length));
                    uv2.Add(e);
                    tan.Add(new Vector4(axis.x, axis.y, axis.z, 1f));
                }
            }

            public Mesh ToMesh(string name)
            {
                var m = new Mesh { name = name };
                if (pos.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                m.SetVertices(pos);
                m.SetNormals(nrm);
                m.SetTangents(tan);
                m.SetUVs(0, uv0);
                m.SetUVs(1, uv1);
                m.SetUVs(2, uv2);
                m.SetTriangles(tris, 0);
                m.RecalculateBounds();
                return m;
            }
        }

        static float R(System.Random rng, float a, float b) => a + (float)rng.NextDouble() * (b - a);

        /// <summary>Flor de gelo: lâminas grandes à volta do centro (que fica livre para o alvo) e lascas até ao raio.</summary>
        public static Mesh Burst(int seed, float radius = 3.2f, float height = 5f, int blades = 11, int core = 22, int chips = 60)
        {
            var b = new Builder();
            var rng = new System.Random(seed);
            for (int i = 0; i < blades; i++)
            {
                float ang = (i + R(rng, -0.3f, 0.3f)) / blades * Mathf.PI * 2f;
                var outDir = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang));
                var side = new Vector3(-outDir.z, 0f, outDir.x);
                float lean = R(rng, 16f, 40f) * Mathf.Deg2Rad;
                Vector3 axis = (Vector3.up * Mathf.Cos(lean) + outDir * Mathf.Sin(lean) + side * R(rng, -0.15f, 0.15f)).normalized;
                float len = height * (i % 3 == 0 ? R(rng, 0.85f, 1f) : R(rng, 0.5f, 0.8f));
                Shard(b, rng, outDir * R(rng, 0.6f, 1.1f), axis, len, len * R(rng, 0.09f, 0.13f), R(rng, 0.45f, 0.75f), rng.Next(4, 7));
            }
            // Monte de lâminas médias à volta da base: dá a massa do centro.
            for (int i = 0; i < core; i++)
            {
                float ang = R(rng, 0f, Mathf.PI * 2f);
                var outDir = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang));
                var side = new Vector3(-outDir.z, 0f, outDir.x);
                float lean = R(rng, 22f, 55f) * Mathf.Deg2Rad;
                Vector3 axis = (Vector3.up * Mathf.Cos(lean) + outDir * Mathf.Sin(lean) + side * R(rng, -0.35f, 0.35f)).normalized;
                float len = height * R(rng, 0.22f, 0.5f);
                Shard(b, rng, outDir * R(rng, 0.5f, 1.5f), axis, len, len * R(rng, 0.14f, 0.22f), R(rng, 0.5f, 0.85f), rng.Next(4, 7));
            }
            for (int i = 0; i < chips; i++)
            {
                float ang = R(rng, 0f, Mathf.PI * 2f);
                float d01 = Mathf.Sqrt(R(rng, 0.08f, 1f));
                var outDir = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang));
                var side = new Vector3(-outDir.z, 0f, outDir.x);
                float lean = Mathf.Lerp(28f, 72f, d01) * Mathf.Deg2Rad * R(rng, 0.8f, 1.15f);
                Vector3 axis = (Vector3.up * Mathf.Cos(lean) + outDir * Mathf.Sin(lean) + side * R(rng, -0.3f, 0.3f)).normalized;
                float len = Mathf.Lerp(height * 0.42f, height * 0.1f, d01) * R(rng, 0.6f, 1.2f);
                Shard(b, rng, outDir * (0.55f + d01 * (radius - 0.55f)), axis, len, len * R(rng, 0.12f, 0.2f), R(rng, 0.5f, 0.9f), rng.Next(4, 7));
            }
            return b.ToMesh("IceBurst");
        }

        // Uma lâmina: anel enterrado na base, anel do meio mais estreito e a ponta, descentrada.
        static void Shard(Builder b, System.Random rng, Vector3 basePos, Vector3 axis, float length, float radius, float flat, int sides)
        {
            Vector3 t1 = Vector3.Cross(axis, Vector3.up);
            t1 = t1.sqrMagnitude < 1e-4f ? Vector3.right : t1.normalized;
            t1 = Quaternion.AngleAxis(R(rng, 0f, 360f), axis) * t1;
            Vector3 t2 = Vector3.Cross(axis, t1);

            float buried = length * 0.15f, total = length + buried;
            Vector3 bottom = basePos - axis * buried;
            Vector3 mid = basePos + axis * (length * R(rng, 0.55f, 0.8f));
            Vector3 tip = basePos + axis * length + (t1 * R(rng, -0.35f, 0.35f) + t2 * R(rng, -0.35f, 0.35f)) * radius;
            float shard = (float)rng.NextDouble();

            var ring0 = new Vector3[sides];
            var ring1 = new Vector3[sides];
            for (int k = 0; k < sides; k++)
            {
                float a = (k + R(rng, -0.2f, 0.2f)) / sides * Mathf.PI * 2f;
                Vector3 dir = t1 * Mathf.Cos(a) + t2 * (Mathf.Sin(a) * flat);
                ring0[k] = bottom + dir * (radius * R(rng, 0.9f, 1.1f));
                ring1[k] = mid + dir * (radius * R(rng, 0.55f, 0.85f));
            }
            for (int k = 0; k < sides; k++)
            {
                Vector3 A = ring0[k], B = ring0[(k + 1) % sides], C = ring1[(k + 1) % sides], D = ring1[k];
                float wb = Vector3.Distance(A, B), wm = Vector3.Distance(C, D);
                float h = 0.5f * (Vector3.Distance(A, D) + Vector3.Distance(B, C));
                float tone = (float)rng.NextDouble();
                // x/y: distância às arestas laterais, w: à aresta do anel do meio (a de baixo está enterrada).
                var eA = new Vector4(0f, wb, NoEdge, h);
                var eB = new Vector4(wb, 0f, NoEdge, h);
                var eC = new Vector4(wm, 0f, NoEdge, 0f);
                var eD = new Vector4(0f, wm, NoEdge, 0f);
                b.Tri(A, B, C, eA, eB, eC, bottom, axis, total, tone, shard, length);
                b.Tri(A, C, D, eA, eC, eD, bottom, axis, total, tone, shard, length);

                // Ponta: cada componente é a distância à aresta oposta a um dos vértices (baricêntrica × altura).
                float hD = DistanceToLine(D, C, tip), hC = DistanceToLine(C, tip, D), hT = DistanceToLine(tip, D, C);
                b.Tri(D, C, tip, new Vector4(0f, hD, 0f, NoEdge), new Vector4(hC, 0f, 0f, NoEdge), new Vector4(0f, 0f, hT, NoEdge),
                      bottom, axis, total, Mathf.Repeat(tone + 0.37f, 1f), shard, length);
            }
        }

        static float DistanceToLine(Vector3 p, Vector3 a, Vector3 b)
        {
            Vector3 ab = (b - a).normalized;
            return Vector3.Cross(p - a, ab).magnitude;
        }
    }
}
