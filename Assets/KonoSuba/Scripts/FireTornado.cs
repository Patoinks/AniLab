using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace AnimLab.KonoSuba
{
    /// <summary>Advected 3D flame sheets, feeding arms and shed material in a shared vortex.</summary>
    public class FireTornado : MonoBehaviour
    {
        public Material coreMaterial, mainMaterial, ribbonMaterial, inkMaterial, sparkMaterial, groundMaterial;
        public float height = 4.9f;
        public int ribbons = 48;
        public int seed = 7;
        [NonSerialized] public float captureTime = -1f;
        sealed class Piece
        {
            public Mesh mesh;
            public Vector3[] vertices;
            public Vector2[] metricUV;
            public Renderer renderer;
            public MaterialPropertyBlock block = new MaterialPropertyBlock();
            public int kind, steps, cross;
            public float phase, angle, lane, span, width, heat, variation, period, start, end;
            public Vector3[] trajectory;
            public float trajectoryStep;
        }
        readonly List<Piece> pieces = new List<Piece>();
        readonly List<Mesh> meshes = new List<Mesh>();
        static readonly int TimeId = Shader.PropertyToID("_FireTime");
        static readonly int LifeId = Shader.PropertyToID("_Opacity");
        static readonly int TopId = Shader.PropertyToID("_Top");
        public static float Radius(float h) => FireTornadoFlow.Radius(h);

        void Start()
        {
            var rng = new System.Random(seed);
            AddVolume(mainMaterial);
            int flameCount = ribbons * 3;
            for (int i = 0; i < flameCount + 20; i++)
            {
                bool red = i >= flameCount;
                var p = NewSheet(red ? "Red shear " + i : "Flame current " + i, ribbonMaterial, 0, 42, 8);
                p.phase = (i + Next(rng, 0, 0.8f)) / (flameCount + 20);
                p.angle = Next(rng, 0, Mathf.PI * 2);
                p.lane = red ? Next(rng, 0.97f, 1.15f) : Next(rng, 0.64f, 1.10f);
                p.span = Next(rng, 0.19f, 0.46f);
                p.width = red ? Next(rng, 0.13f, 0.31f) : Next(rng, 0.25f, 0.52f);
                p.heat = red ? Next(rng, 0.09f, 0.28f)
                    : Mathf.Clamp01(1.04f - (p.lane-.64f)*1.05f + Next(rng,-.16f,.04f));
                p.variation = Next(rng, 0, 100);
                p.period = FireTornadoFlow.Travel(height + 0.5f) + p.span + 0.15f;
                SetVariation(p);
            }
            for (int i = 0; i < 3; i++)
            {
                var p = NewSheet("Ground feeder " + i, ribbonMaterial, 3, 100, 10);
                p.angle = new[] { 0.0f, 2.08f, 4.43f }[i];
                p.lane = new[] { 1.0f, 0.92f, 1.065f }[i];
                p.width = new[] { 0.67f, 0.53f, 0.44f }[i];
                p.start = -Mathf.Log(new[] { 2.77f, 2.64f, 2.84f }[i] / (Radius(.055f) * p.lane)) / FireTornadoFlow.GroundSink;
                p.end = new[] { 0.72f, 0.53f, 0.65f }[i];
                p.heat = new[] { 0.94f, 0.83f, 0.88f }[i];
                p.variation = 5.23f + i * 19.37f;
                SetVariation(p);
            }
            if (groundMaterial != null) AddStatic("Fire ground radiance", Ground(), groundMaterial, 0);
            if (inkMaterial != null) for (int i = 0; i < 112; i++) AddEjecta(rng, i, false);
            if (sparkMaterial != null) for (int i = 0; i < 128; i++) AddEjecta(rng, i, true);
            Evaluate(0);
        }

        void AddEjecta(System.Random rng, int index, bool spark)
        {
            var p = NewSheet((spark ? "Shed ember " : "Shed ink ") + index, spark ? sparkMaterial : inkMaterial,
                spark ? 2 : 1, spark ? 7 : 16, 4);
            p.phase = Next(rng, 0, 1);
            p.angle = Next(rng, 0, 2 * Mathf.PI);
            p.start = Mathf.Lerp(0.12f, height * 0.79f, Mathf.Pow(Next(rng, 0, 1), spark ? 1.4f : 0.65f));
            p.lane = Next(rng, 0.96f, 1.055f);
            p.span = spark ? Next(rng, 0.012f, index % 9 == 0 ? 0.13f : 0.045f) : Next(rng, 0.09f, 0.27f);
            p.width = spark ? Next(rng, 0.004f, 0.014f) : Mathf.Min(Next(rng, 0.028f, 0.12f),p.span*.5f);
            p.heat = spark ? 1 : 0;
            p.variation = Next(rng, 0, 100);
            p.period = spark ? Next(rng, 1.2f, 2.0f) : Next(rng, 0.75f, 1.25f);
            // Integrate the common field; centrifugal separation is only applied after emission.
            const int samples = 128;
            p.trajectory = new Vector3[samples + 1];
            p.trajectoryStep = p.period / samples;
            float h = p.start, r = Radius(h) * p.lane, angle = 0;
            float ejection = spark ? Next(rng, 0.7f, 1.5f) : Next(rng, 0.4f, 1.0f);
            for (int j = 0; j <= samples; j++)
            {
                p.trajectory[j] = new Vector3(h, r, angle);
                float dt = p.trajectoryStep, age = j * dt;
                float hm = h + (FireTornadoFlow.Rise + FireTornadoFlow.Acceleration * h) * dt * 0.5f;
                float drift = ejection * Mathf.SmoothStep(0, 1, age / 0.27f);
                float nextH = h + (FireTornadoFlow.Rise + FireTornadoFlow.Acceleration * hm) * dt;
                float follow = Radius(nextH) - Radius(h);
                float rm = r + (follow + drift * dt) * 0.5f;
                angle += FireTornadoFlow.Omega(hm, rm / Radius(hm)) * dt;
                r += follow + drift * dt;
                h = nextH;
            }
            SetVariation(p);
        }

        static float Next(System.Random rng, float lo, float hi) => Mathf.Lerp(lo, hi, (float)rng.NextDouble());
        void SetVariation(Piece p)
        {
            p.block.SetFloat("_Seed", p.variation);
            p.block.SetFloat("_Heat", p.heat);
            p.block.SetFloat("_Ground", p.kind == 3 ? 1 : 0);
        }
        Piece NewSheet(string label, Material material, int kind, int steps, int cross)
        {
            var vertices = new Vector3[(steps + 1) * (cross + 1)];
            var uv = new Vector2[vertices.Length];
            for (int s = 0; s <= steps; s++)
                for (int c = 0; c <= cross; c++)
                    uv[s * (cross + 1) + c] = new Vector2(c / (float)cross, s / (float)steps);
            var mesh = GridMesh(label, vertices, uv, cross, steps);
            mesh.MarkDynamic();
            var p = AddStatic(label, mesh, material, 0);
            p.vertices = vertices; p.kind = kind; p.steps = steps; p.cross = cross;
            p.metricUV = new Vector2[vertices.Length];
            // Keep each sheet continuous; opaque interiors now resolve overlap per pixel.
            // Splitting a translucent sheet into separately sorted thirds exposed straight seams.
            return p;
        }
        Piece AddStatic(string label, Mesh mesh, Material material, float sortRadius)
        {
            meshes.Add(mesh);
            var go = new GameObject(label);
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            go.AddComponent<FireFXRegister>().SetRadius(sortRadius);
            var p = new Piece { mesh = mesh, renderer = renderer, kind = -1 };
            pieces.Add(p);
            return p;
        }
        void LateUpdate() => Evaluate(captureTime >= 0 ? captureTime : Time.timeSinceLevelLoad);
        public void Evaluate(float time)
        {
            foreach (var p in pieces)
            {
                p.block.SetFloat(TimeId, time);
                if (p.vertices != null) UpdateSheet(p, time);
                p.renderer.SetPropertyBlock(p.block);
            }
        }
        Vector3 Trajectory(Piece p, float age)
        {
            float k = Mathf.Clamp(age / p.trajectoryStep, 0, p.trajectory.Length - 1.001f);
            int index = (int)k;
            return Vector3.LerpUnclamped(p.trajectory[index], p.trajectory[index + 1], k - index);
        }
        Vector3 Centre(Piece p, float s, float time, float age, float birth, out Vector3 outward, out float h)
        {
            float angle;
            Vector3 result;
            if (p.kind == 1 || p.kind == 2)
            {
                Vector3 q = Trajectory(p, Mathf.Max(0, age - (1 - s) * p.span));
                h = q.x;
                angle = p.angle + FireTornadoFlow.Omega(p.start, p.lane) * birth + q.z;
                result = FireTornadoFlow.Cylindrical(h, q.y, angle, time);
            }
            else
            {
                float travel = p.kind == 3 ? Mathf.Lerp(p.start, p.end, s) : age - 0.14f - (1 - s) * p.span;
                angle = p.angle + FireTornadoFlow.Turn(travel, p.lane)
                    + (p.kind == 3 ? FireTornadoFlow.SourceSpin * (time - travel) : birth * FireTornadoFlow.SourceSpin);
                result = FireTornadoFlow.Position(travel, p.lane, angle, time);
                h = result.y;
            }
            outward = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
            return result;
        }
        void UpdateSheet(Piece p, float time)
        {
            float age = p.kind == 3 ? 0 : Mathf.Repeat(time + p.phase * p.period, p.period);
            float birth = time - age;
            float fade = p.kind == 3 ? 1 : Mathf.SmoothStep(0, 1, age / 0.12f)
                * (1 - Mathf.SmoothStep(0, 1, (age / p.period - 0.72f) / 0.28f));
            float top = 0;
            float distance = 0;
            Vector3 previousCentre = Vector3.zero;
            for (int i = 0; i <= p.steps; i++)
            {
                float s = i / (float)p.steps;
                Vector3 centre = Centre(p, s, time, age, birth, out Vector3 outward, out float h);
                if (i > 0) distance += Vector3.Distance(previousCentre, centre);
                previousCentre = centre;
                Vector3 before = Centre(p, Mathf.Max(0, s - 0.005f), time, age, birth, out _, out _);
                Vector3 after = Centre(p, Mathf.Min(1, s + 0.005f), time, age, birth, out _, out _);
                Vector3 tangent = (after - before).normalized;
                if (tangent.sqrMagnitude < 0.01f) tangent = Vector3.Cross(outward, Vector3.up);
                Vector3 across = Vector3.Cross(tangent, outward).normalized;
                float roll = p.kind == 3 ? Mathf.Lerp(Mathf.PI * 0.5f, 0.08f, Mathf.SmoothStep(0, 1, (h - 0.06f) / 0.4f))
                    : p.kind == 1 ? Mathf.Sin(p.variation) * 0.5f : 0.12f * Mathf.Sin(s * 4 + p.variation);
                across = across * Mathf.Cos(roll) + outward * Mathf.Sin(roll);
                float taper = Mathf.Pow(Mathf.Max(0, Mathf.Sin(Mathf.PI * s)), p.kind == 3 ? 0.42f : 1.15f)
                    * (p.kind == 3 ? 0.67f + 0.75f*s : 1.65f-1.35f*s);
                float width = p.width * taper * (.82f + .38f * Mathf.PerlinNoise(distance*5+p.variation,p.variation*.3f));
                if (p.kind == 0) width *= Mathf.Lerp(1, 0.68f, h / height);
                for (int j = 0; j <= p.cross; j++)
                {
                    float u = j / (float)p.cross;
                    float edge = .9f+.2f*Mathf.PerlinNoise(distance*17+p.variation,u*3+p.variation);
                    float arch = Mathf.Sin(u * Mathf.PI) * width * (p.kind == 3 ? 0.09f : 0.12f);
                    p.vertices[i * (p.cross + 1) + j] = centre + across * ((u - 0.5f) * width * edge) + outward * arch;
                    p.metricUV[i * (p.cross + 1) + j] = new Vector2(distance,(u-.5f)*width);
                }
                top += h / height;
            }
            p.mesh.vertices = p.vertices;
            p.mesh.uv2 = p.metricUV;
            p.mesh.RecalculateBounds();
            p.block.SetFloat(LifeId, fade);
            p.block.SetFloat(TopId, Mathf.Clamp01(top / (p.steps + 1)));
        }
        void AddVolume(Material material)
        {
            const int segments = 96, rings = 96;
            var vertices = new Vector3[(segments + 1) * (rings + 1)];
            var uv = new Vector2[vertices.Length];
            for (int y = 0; y <= rings; y++)
                for (int x = 0; x <= segments; x++)
                {
                    float v = y / (float)rings, h = v * height, a = x * 2 * Mathf.PI / segments;
                    float r = Radius(h) * 1.23f;
                    int k = y * (segments + 1) + x;
                    vertices[k] = new Vector3(Mathf.Cos(a) * r, h, Mathf.Sin(a) * r);
                    uv[k] = new Vector2(x / (float)segments, v);
                }
            var p = AddStatic("Fire Volume", GridMesh("Fire Volume", vertices, uv, segments, rings), material, 0);
            p.block.SetFloat("_Height", height);
        }

        static Mesh Ground()
        {
            const int segments = 128, rings = 20;
            var vertices = new Vector3[(segments + 1) * (rings + 1)];
            var uv = new Vector2[vertices.Length];
            for (int y = 0; y <= rings; y++)
                for (int x = 0; x <= segments; x++)
                {
                    float r = 3.05f * y / rings, a = x * 2 * Mathf.PI / segments;
                    int k = y * (segments + 1) + x;
                    uv[k] = new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r);
                    vertices[k] = new Vector3(uv[k].x, 0.035f, uv[k].y);
                }
            return GridMesh("Fire radiance", vertices, uv, segments, rings);
        }
        static Mesh GridMesh(string label, Vector3[] vertices, Vector2[] uv, int segments, int rings)
        {
            var indices = new int[segments * rings * 6];
            for (int y = 0, n = 0; y < rings; y++)
                for (int x = 0; x < segments; x++)
                {
                    int a = y * (segments + 1) + x, b = a + segments + 1;
                    indices[n++] = a; indices[n++] = a + 1; indices[n++] = b;
                    indices[n++] = a + 1; indices[n++] = b + 1; indices[n++] = b;
                }
            var mesh = new Mesh { name = label, vertices = vertices, uv = uv, triangles = indices };
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            var bounds = mesh.bounds; bounds.Expand(0.5f); mesh.bounds = bounds;
            return mesh;
        }
        void OnDestroy()
        {
            foreach (var mesh in meshes) if (mesh != null) Destroy(mesh);
            meshes.Clear(); pieces.Clear();
        }
    }
}
