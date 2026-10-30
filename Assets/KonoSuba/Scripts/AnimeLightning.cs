using System.Collections.Generic;
using UnityEngine;

namespace AnimLab.KonoSuba
{
    /// <summary>
    /// Forma de um raio em estilo anime, recortada mas orgânica: a coluna é um feixe de fios finos entrelaçados
    /// (cada um com o seu serpentear e dobras fractais de vários tamanhos), com ramos que seguem o sentido da
    /// descarga e curvam, e fiapos curtos a crepitar junto aos fios.
    /// </summary>
    public static class AnimeBolt
    {
        public struct Branch
        {
            public List<Vector3> points;
            public float width;   // largura do núcleo (m) no início do ramo
            public float id;      // 0..1, para o cintilar e a espessura por ramo
            public bool taper;    // afina até à ponta (ramos); o principal mantém a largura
        }

        public static List<Branch> Generate(Vector3 from, Vector3 to, float width, System.Random rng, int forks = 6, int crackles = 16, int strands = 4)
        {
            float R() => (float)rng.NextDouble();
            var list = new List<Branch>();
            float length = Vector3.Distance(from, to);
            // Coluna: fios finos do topo ao chão, cada um com o seu caminho; partem e chegam quase ao mesmo ponto.
            var mains = new List<List<Vector3>>();
            for (int k = 0; k < strands; k++)
            {
                Vector3 a = from + Random3(rng) * length * 0.025f, b = to + Random3(rng) * length * 0.02f;
                List<Vector3> strand = Jagged(a, b, 6, 0.3f, 0.07f, rng);
                mains.Add(strand);
                list.Add(new Branch { points = strand, width = width * (k == 0 ? 0.65f : Mathf.Lerp(0.3f, 0.5f, R())), id = R(), taper = false });
            }
            List<Vector3> main = mains[0];

            // Ramos: saem para a frente (no sentido da descarga) e para o lado, e curvam.
            for (int f = 0; f < forks; f++)
            {
                main = mains[rng.Next(mains.Count)];
                int idx = rng.Next(main.Count / 12, main.Count * 7 / 10);
                Vector3 dir = Flow(main, idx);
                Vector3 fdir = (dir + Perpendicular(dir, R() * Mathf.PI * 2f) * Mathf.Lerp(0.45f, 1.1f, R())).normalized;
                float len = length * Mathf.Lerp(0.12f, 0.34f, R()) * (1f - 0.5f * idx / main.Count);
                List<Vector3> fork = Jagged(main[idx], main[idx] + fdir * len, 5, 0.34f, 0.14f, rng);
                float fw = width * Mathf.Lerp(0.3f, 0.5f, R());
                list.Add(new Branch { points = fork, width = fw, id = R(), taper = true });
                for (int k = 0, subs = rng.Next(0, 3); k < subs; k++)
                {
                    int j = rng.Next(fork.Count / 5, fork.Count * 3 / 4);
                    Vector3 fd = Flow(fork, j);
                    Vector3 sdir = (fd + Perpendicular(fd, R() * Mathf.PI * 2f) * Mathf.Lerp(0.5f, 1.2f, R())).normalized;
                    List<Vector3> sub = Jagged(fork[j], fork[j] + sdir * len * Mathf.Lerp(0.25f, 0.55f, R()), 4, 0.36f, 0.12f, rng);
                    list.Add(new Branch { points = sub, width = fw * 0.5f, id = R(), taper = true });
                }
            }

            // Fiapos a crepitar: muito curtos e finos, presos ao tronco.
            for (int c = 0; c < crackles; c++)
            {
                main = mains[rng.Next(mains.Count)];
                int idx = rng.Next(2, main.Count - 2);
                Vector3 dir = Flow(main, idx);
                Vector3 cdir = (dir * 0.3f + Perpendicular(dir, R() * Mathf.PI * 2f)).normalized;
                List<Vector3> hair = Jagged(main[idx], main[idx] + cdir * length * Mathf.Lerp(0.02f, 0.06f, R()), 3, 0.45f, 0f, rng);
                list.Add(new Branch { points = hair, width = width * Mathf.Lerp(0.12f, 0.22f, R()), id = R(), taper = true });
            }
            return list;
        }

        static Vector3 Random3(System.Random rng) =>
            new Vector3((float)rng.NextDouble() * 2f - 1f, (float)rng.NextDouble() * 2f - 1f, (float)rng.NextDouble() * 2f - 1f);

        static Vector3 Flow(List<Vector3> path, int i) =>
            (path[Mathf.Min(i + 2, path.Count - 1)] - path[Mathf.Max(i - 2, 0)]).normalized;

        /// <summary>
        /// Linha de a a b: primeiro um serpentear suave (meander, em fração do comprimento), depois dobras
        /// fractais: cada nível divide os segmentos num ponto entre 35% e 65% e desloca-o para o lado do próprio
        /// segmento, com a força a cair de nível para nível (dobras grandes, depois tremidos pequenos).
        /// </summary>
        public static List<Vector3> Jagged(Vector3 a, Vector3 b, int depth, float roughness, float meander, System.Random rng)
        {
            float R() => (float)rng.NextDouble();
            Vector3 axis = (b - a).normalized;
            float length = Vector3.Distance(a, b);
            var points = new List<Vector3>();
            // Serpentear: duas ondas suaves em planos diferentes, nulas nas pontas.
            Vector3 u = Perpendicular(axis, R() * Mathf.PI * 2f), v = Vector3.Cross(axis, u);
            float p1 = R() * 6.28f, p2 = R() * 6.28f, f1 = Mathf.Lerp(1f, 2.2f, R()), f2 = Mathf.Lerp(2f, 3.5f, R());
            const int Base = 6;
            for (int k = 0; k <= Base; k++)
            {
                float t = k / (float)Base, env = Mathf.Sin(Mathf.PI * t);
                Vector3 off = (u * Mathf.Sin(t * f1 * 6.28f + p1) + v * 0.6f * Mathf.Sin(t * f2 * 6.28f + p2)) * meander * length * env;
                points.Add(Vector3.Lerp(a, b, t) + off);
            }
            float rough = roughness;
            for (int d = 0; d < depth; d++)
            {
                var next = new List<Vector3>(points.Count * 2) { points[0] };
                for (int i = 0; i < points.Count - 1; i++)
                {
                    Vector3 p0 = points[i], q1 = points[i + 1];
                    Vector3 seg = q1 - p0;
                    float t = Mathf.Lerp(0.35f, 0.65f, R());
                    Vector3 side = Perpendicular(seg.normalized, R() * Mathf.PI * 2f);
                    next.Add(p0 + seg * t + side * seg.magnitude * rough * (R() * 2f - 1f));
                    next.Add(q1);
                }
                points = next;
                rough *= 0.78f;
            }
            return points;
        }

        static Vector3 Perpendicular(Vector3 axis, float angle)
        {
            Vector3 u = Vector3.Cross(axis, Mathf.Abs(axis.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            Vector3 v = Vector3.Cross(axis, u);
            return u * Mathf.Cos(angle) + v * Mathf.Sin(angle);
        }

        /// <summary>
        /// Malha de fitas (2 vértices por ponto); o shader LightningAnime vira-as para a câmara.
        /// stride > 1 salta pontos (caminho suavizado, para o halo não se dobrar sobre si próprio).
        /// </summary>
        public static void BuildMesh(Mesh mesh, List<Branch> branches, Transform space, int stride = 1)
        {
            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var uv0 = new List<Vector2>();
            var uv1 = new List<Vector2>();
            var tris = new List<int>();
            var path = new List<Vector3>();
            foreach (Branch b in branches)
            {
                path.Clear();
                for (int k = 0; k < b.points.Count; k += stride) path.Add(b.points[k]);
                if ((b.points.Count - 1) % stride != 0) path.Add(b.points[b.points.Count - 1]);
                int n = path.Count, start = verts.Count;
                if (n < 2) continue;
                for (int k = 0; k < n; k++)
                {
                    Vector3 p = space.InverseTransformPoint(path[k]);
                    Vector3 t = space.InverseTransformDirection(path[Mathf.Min(k + 1, n - 1)] - path[Mathf.Max(k - 1, 0)]).normalized;
                    float s = k / (n - 1f);
                    float w = b.taper ? b.width * Mathf.Pow(1f - s, 0.7f) : b.width * (0.85f + 0.15f * Mathf.Sin(Mathf.PI * s));
                    // Espessura viva: engrossa e estrangula ao longo do fio.
                    w *= 0.65f + 0.7f * Mathf.PerlinNoise(s * 9f + b.id * 13f, b.id * 7f);
                    for (int side = -1; side <= 1; side += 2)
                    {
                        verts.Add(p);
                        normals.Add(t);
                        uv0.Add(new Vector2(s, side));
                        uv1.Add(new Vector2(w, b.id));
                    }
                    if (k < n - 1)
                    {
                        int a = start + k * 2;
                        tris.Add(a); tris.Add(a + 2); tris.Add(a + 1);
                        tris.Add(a + 1); tris.Add(a + 2); tris.Add(a + 3);
                    }
                }
            }
            mesh.Clear();
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uv0);
            mesh.SetUVs(1, uv1);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
        }
    }
}
