using System.Collections.Generic;
using UnityEngine;

namespace AnimLab.KonoSuba
{
    /// <summary>
    /// Um trovão a cair: re-cai várias vezes muito depressa (forma nova e clarão a cada golpe), fica um instante
    /// a tremeluzir e apaga-se. Destrói-se no fim.
    /// Com materiais de impacto, cria o impacto no chão (LightningImpact) e, a cada golpe, volta a acendê-lo e
    /// solta arcos pequenos a correr pelo chão. ground = arco rasteiro (os pontos não descem abaixo do chão).
    /// </summary>
    public class AnimeLightningStrike : MonoBehaviour
    {
        public Material material;
        public Vector3 from, to;
        public float width = 0.14f;
        public int strikes = 4;
        public float strikeInterval = 0.07f;
        public float hold = 0.15f;
        public float fade = 0.25f;
        public LightningImpactMaterials impactMaterials;
        public bool ground;

        Mesh mesh, haloMesh;
        LightningImpact impact;
        MeshRenderer meshRenderer, haloRenderer;
        MaterialPropertyBlock block;
        System.Random rng;
        int strike = -1;
        float age;

        public static AnimeLightningStrike Spawn(Vector3 from, Vector3 to, Material material, float width, int seed,
            LightningImpactMaterials impactMaterials = null)
        {
            var go = new GameObject("Trovão");
            var s = go.AddComponent<AnimeLightningStrike>();
            s.material = material;
            s.from = from;
            s.to = to;
            s.width = width;
            s.rng = new System.Random(seed);
            s.impactMaterials = impactMaterials;
            return s;
        }

        /// <summary>Arco pequeno a correr pelo chão a partir de um ponto (curto, dois golpes).</summary>
        public static void GroundArc(Vector3 origin, Material material, System.Random rng)
        {
            float a = (float)rng.NextDouble() * Mathf.PI * 2f, d = Mathf.Lerp(1.2f, 3f, (float)rng.NextDouble());
            var arc = Spawn(origin + Vector3.up * 0.05f, origin + new Vector3(Mathf.Cos(a) * d, 0.05f, Mathf.Sin(a) * d), material,
                Mathf.Lerp(0.04f, 0.07f, (float)rng.NextDouble()), rng.Next());
            arc.ground = true;
            arc.strikes = 2;
            arc.strikeInterval = 0.05f;
            arc.hold = 0.06f;
            arc.fade = 0.15f;
        }

        void Start()
        {
            rng ??= new System.Random();
            mesh = new Mesh { name = "Trovão" };
            gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            meshRenderer = gameObject.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = material;
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            // Halo: a mesma forma suavizada, noutra malha (shader em modo halo por MaterialPropertyBlock).
            var halo = new GameObject("Halo");
            halo.transform.SetParent(transform, false);
            haloMesh = new Mesh { name = "Trovão (halo)" };
            halo.AddComponent<MeshFilter>().sharedMesh = haloMesh;
            haloRenderer = halo.AddComponent<MeshRenderer>();
            haloRenderer.sharedMaterial = material;
            haloRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            block = new MaterialPropertyBlock();
            if (impactMaterials != null && impactMaterials.cracks != null) impact = LightningImpact.Create(to, impactMaterials, rng.Next());
            Tick(0f);
        }

        void Update() => Tick(Time.deltaTime);

        void Tick(float dt)
        {
            age += dt;
            float strikesEnd = strikes * strikeInterval;
            int current = Mathf.Min((int)(age / strikeInterval), strikes - 1);
            if (current != strike)
            {
                // Re-cair: forma nova, mesmas pontas.
                strike = current;
                var bolt = ground ? AnimeBolt.Generate(from, to, width, rng, 2, 4, 1) : AnimeBolt.Generate(from, to, width, rng);
                if (ground)
                    foreach (AnimeBolt.Branch b in bolt)
                        for (int k = 0; k < b.points.Count; k++)
                        {
                            Vector3 q = b.points[k];
                            b.points[k] = new Vector3(q.x, Mathf.Max(q.y, to.y + 0.02f), q.z);
                        }
                if (impact != null)
                {
                    impact.Pulse(rng);
                    for (int k = 0; k < 2; k++) GroundArc(to, material, rng);
                }
                AnimeBolt.BuildMesh(mesh, bolt, transform);
                AnimeBolt.BuildMesh(haloMesh, bolt, transform, 3);
            }
            // O brilho fica alto até ao fim: o raio desaparece a afinar e a partir-se aos bocados (erosão ao longo
            // dos fios, a piscar), não a escurecer para cinzento.
            float flash, thin = 1f, erode = 0f;
            if (age < strikesEnd)
            {
                float local = age - strike * strikeInterval;
                flash = 0.75f + 0.6f * Mathf.Exp(-local * 35f);
            }
            else if (age < strikesEnd + hold)
            {
                flash = 0.8f + 0.2f * Mathf.PerlinNoise(age * 40f, 0.3f);
                erode = 0.08f * Mathf.PerlinNoise(age * 30f, 1.7f);
            }
            else
            {
                float f = Mathf.Clamp01((age - strikesEnd - hold) / fade);
                flash = 0.85f;
                thin = Mathf.Lerp(1f, 0.35f, f);
                erode = Mathf.Lerp(0.1f, 1.05f, f);
            }
            if (age > strikesEnd + hold + fade) { Destroy(gameObject); return; }
            block.SetFloat("_Thin", thin);
            block.SetFloat("_Erode", erode);
            block.SetFloat("_Flash", flash);
            block.SetFloat("_Part", 0f);
            meshRenderer.SetPropertyBlock(block);
            block.SetFloat("_Part", 1f);
            haloRenderer.SetPropertyBlock(block);
        }

        void OnDestroy()
        {
            if (mesh != null) Destroy(mesh);
            if (haloMesh != null) Destroy(haloMesh);
        }
    }
}
