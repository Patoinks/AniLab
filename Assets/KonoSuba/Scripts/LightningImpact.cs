using System;
using System.Collections.Generic;
using UnityEngine;

namespace AnimLab.KonoSuba
{
    /// <summary>Materiais do impacto do trovão.</summary>
    [Serializable]
    public class LightningImpactMaterials
    {
        [Tooltip("KonoSuba/LightningAnime (faíscas e arcos rasteiros).")] public Material bolt;
        [Tooltip("KonoSuba/LightningCracks.")] public Material cracks;
        [Tooltip("KonoSuba/Distortion em modo onda (2).")] public Material shockwave;
        [Tooltip("Material escuro e iluminado para os pedaços de chão.")] public Material debris;
    }

    /// <summary>
    /// Impacto do trovão no chão (referência: raio a cair num chão de xadrez):
    ///   - clarão: luz real que acende o chão e quem estiver perto, e volta a acender a cada re-cair (Pulse);
    ///   - fissuras: rede de rachas a brilhar que abre do centro e apaga das pontas (LightningCracks);
    ///   - faíscas: traços brancos/ciano projetados, com gravidade (fitas do LightningAnime, refeitas por frame);
    ///   - detritos: pedaços escuros de chão projetados, a rodar e a ressaltar;
    ///   - onda de choque: anel de distorção a abrir.
    /// Destrói-se sozinho.
    /// </summary>
    public class LightningImpact : MonoBehaviour
    {
        const float Life = 1.4f;
        LightningImpactMaterials materials;
        Light flash;
        Renderer cracks, shock, sparkRenderer;
        Mesh sparkMesh;
        MaterialPropertyBlock block;
        float age, pulse = 1f;

        struct Spark { public Vector3 p, v; public float life, width; }
        struct Chunk { public Transform t; public Vector3 v, spin; }
        readonly List<Spark> sparks = new List<Spark>();
        readonly List<Chunk> chunks = new List<Chunk>();
        readonly List<AnimeBolt.Branch> sparkBranches = new List<AnimeBolt.Branch>();

        public static LightningImpact Create(Vector3 point, LightningImpactMaterials materials, int seed)
        {
            var go = new GameObject("Impacto do trovão");
            go.transform.position = point;
            var impact = go.AddComponent<LightningImpact>();
            impact.materials = materials;
            impact.Build(new System.Random(seed));
            return impact;
        }

        /// <summary>Re-cair: o clarão volta a acender e salta mais uma rajada de faíscas.</summary>
        public void Pulse(System.Random rng)
        {
            pulse = 1f;
            Burst(rng, 10);
        }

        void Build(System.Random rng)
        {
            float R() => (float)rng.NextDouble();
            block = new MaterialPropertyBlock();

            flash = new GameObject("Clarão").AddComponent<Light>();
            flash.transform.SetParent(transform, false);
            flash.transform.localPosition = Vector3.up * 0.6f;
            flash.type = LightType.Point;
            flash.color = new Color(0.55f, 0.8f, 1f);
            flash.range = 12f;
            flash.shadows = LightShadows.None;

            cracks = Part("Fissuras", Quad(5.5f), materials.cracks, 0.03f);
            cracks.transform.localRotation = Quaternion.Euler(0f, R() * 360f, 0f);
            block.SetFloat("_Seed", R() * 100f);
            cracks.SetPropertyBlock(block);
            block.Clear();

            if (materials.shockwave != null) shock = Part("Onda de choque", WindTornado.Disc(0.75f, 1f, 0f), materials.shockwave, 0.5f);

            sparkMesh = new Mesh { name = "Faíscas" };
            var sp = new GameObject("Faíscas");
            sp.transform.SetParent(transform, false);
            sp.AddComponent<MeshFilter>().sharedMesh = sparkMesh;
            sparkRenderer = sp.AddComponent<MeshRenderer>();
            sparkRenderer.sharedMaterial = materials.bolt;
            sparkRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            Burst(rng, 26);

            // Pedaços de chão: blocos irregulares escuros, iluminados pelo clarão.
            Mesh cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            for (int i = 0; i < 9; i++)
            {
                var c = new GameObject("Pedaço");
                c.transform.SetParent(transform, false);
                c.transform.localPosition = new Vector3(R() - 0.5f, 0.1f, R() - 0.5f) * 0.6f;
                c.transform.localScale = new Vector3(Mathf.Lerp(0.1f, 0.26f, R()), Mathf.Lerp(0.06f, 0.14f, R()), Mathf.Lerp(0.1f, 0.22f, R()));
                c.transform.localRotation = UnityEngine.Random.rotation;
                c.AddComponent<MeshFilter>().sharedMesh = cube;
                var r = c.AddComponent<MeshRenderer>();
                r.sharedMaterial = materials.debris;
                Vector2 d = UnityEngine.Random.insideUnitCircle.normalized;
                chunks.Add(new Chunk
                {
                    t = c.transform,
                    v = new Vector3(d.x, 0f, d.y) * Mathf.Lerp(1.5f, 4f, R()) + Vector3.up * Mathf.Lerp(3f, 6.5f, R()),
                    spin = UnityEngine.Random.insideUnitSphere * 720f
                });
            }
        }

        void Burst(System.Random rng, int count)
        {
            float R() => (float)rng.NextDouble();
            for (int i = 0; i < count; i++)
            {
                Vector2 d = new Vector2(R() * 2f - 1f, R() * 2f - 1f).normalized;
                sparks.Add(new Spark
                {
                    p = transform.position + Vector3.up * 0.1f,
                    v = new Vector3(d.x, 0f, d.y) * Mathf.Lerp(2f, 8f, R()) + Vector3.up * Mathf.Lerp(2f, 9f, R()),
                    life = Mathf.Lerp(0.3f, 0.8f, R()),
                    width = Mathf.Lerp(0.015f, 0.035f, R())
                });
            }
        }

        Renderer Part(string name, Mesh mesh, Material material, float y)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.up * y;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return r;
        }

        static Mesh Quad(float size)
        {
            float h = size * 0.5f;
            var mesh = new Mesh
            {
                name = "Disco das fissuras",
                vertices = new[] { new Vector3(-h, 0, -h), new Vector3(h, 0, -h), new Vector3(h, 0, h), new Vector3(-h, 0, h) },
                uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) },
                triangles = new[] { 0, 2, 1, 0, 3, 2 }
            };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            age += dt;
            if (age > Life) { Destroy(gameObject); return; }

            // Clarão: dispara a cada golpe e cai depressa, com tremor.
            pulse *= Mathf.Exp(-dt * 9f);
            flash.intensity = 70f * pulse * (0.8f + 0.4f * Mathf.PerlinNoise(age * 50f, 0.2f)) + 6f * Mathf.Clamp01(1f - age / Life);

            // Fissuras: abrem em 0,08 s e apagam das pontas para o centro.
            block.Clear();
            block.SetFloat("_Reach", Mathf.Clamp01(age / 0.08f));
            block.SetFloat("_Fade", Mathf.Clamp01(1f - (age - 0.35f) / 0.9f));
            block.SetFloat("_Intensity", 3.5f + 3f * pulse);
            cracks.SetPropertyBlock(block);

            // Onda de choque: abre até ~6 m em 0,4 s.
            if (shock != null)
            {
                float s = Mathf.Clamp01(age / 0.4f);
                shock.transform.localScale = Vector3.one * Mathf.Lerp(0.3f, 6f, 1f - (1f - s) * (1f - s));
                block.Clear();
                block.SetFloat("_Fade", (1f - s) * (1f - s));
                shock.SetPropertyBlock(block);
            }

            // Faíscas: balística simples; cada uma é um traço curto no sentido da velocidade.
            sparkBranches.Clear();
            for (int i = sparks.Count - 1; i >= 0; i--)
            {
                Spark s = sparks[i];
                s.life -= dt;
                if (s.life <= 0f) { sparks.RemoveAt(i); continue; }
                s.v += Vector3.down * 14f * dt;
                s.v *= 1f - 1.2f * dt;
                s.p += s.v * dt;
                if (s.p.y < transform.position.y) { s.p.y = transform.position.y; s.v.y *= -0.3f; }
                sparks[i] = s;
                sparkBranches.Add(new AnimeBolt.Branch
                {
                    points = new List<Vector3> { s.p, s.p - s.v * 0.035f },
                    width = s.width * Mathf.Clamp01(s.life * 3f),
                    id = i * 0.137f % 1f,
                    taper = true
                });
            }
            AnimeBolt.BuildMesh(sparkMesh, sparkBranches, sparkRenderer.transform);
            block.Clear();
            block.SetFloat("_Part", 0f);
            block.SetFloat("_Flash", 1.2f);
            sparkRenderer.SetPropertyBlock(block);

            // Detritos: gravidade, rotação, ressalto no chão, e encolhem no fim.
            float shrink = Mathf.Clamp01((Life - age) / 0.3f);
            for (int i = 0; i < chunks.Count; i++)
            {
                Chunk c = chunks[i];
                c.v += Vector3.down * 16f * dt;
                Vector3 p = c.t.position + c.v * dt;
                if (p.y < transform.position.y + 0.03f)
                {
                    p.y = transform.position.y + 0.03f;
                    c.v = new Vector3(c.v.x * 0.5f, -c.v.y * 0.3f, c.v.z * 0.5f);
                    c.spin *= 0.5f;
                }
                c.t.position = p;
                c.t.Rotate(c.spin * dt, Space.Self);
                if (shrink < 1f) c.t.localScale *= Mathf.Lerp(1f, 0.85f, 1f - shrink);
                chunks[i] = c;
            }
        }

        void OnDestroy()
        {
            if (sparkMesh != null) Destroy(sparkMesh);
        }
    }
}
