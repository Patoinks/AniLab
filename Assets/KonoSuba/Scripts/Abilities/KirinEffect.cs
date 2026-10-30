using System.Collections.Generic;
using UnityEngine;

namespace AnimLab.KonoSuba
{
    /// <summary>
    /// Invocação, mordida em andamento e queda do Kirin, a desfazer-se ao entrar no chão. A forma e a coreografia
    /// vêm de KirinDragonGeometry; o traço é o raio anime (LightningAnime): núcleo branco, contorno ciano e halo
    /// azul, recortado de novo várias vezes por segundo (crepitar), com fiapos a saltar da cabeça e olhos
    /// vermelhos. O impacto é o do trovão (LightningImpact), com arcos a correr pelo chão.
    /// </summary>
    public class KirinEffect : MonoBehaviour
    {
        const float BaseWidth = 0.075f, CrackleRate = 18f;
        static readonly int ColorId = Shader.PropertyToID("_LightningColor");

        LockOnTarget target;
        Material material, eyeMaterial;
        LightningImpactMaterials impactMaterials;
        Vector3 casterPoint, destination, lateral;
        Bounds targetBounds;
        KirinDragonGeometry dragon;
        Mesh primary, primaryHalo, lines, halo, eyes;
        Renderer primaryRenderer, primaryHaloRenderer, lineRenderer, haloRenderer, eyeRenderer, eyeHaloRenderer;
        MaterialPropertyBlock block;
        LightningFogGlow fog;
        readonly List<Vector3> fogPositions = new List<Vector3>();
        readonly List<float> fogArrivals = new List<float>();
        readonly List<float> fogHolds = new List<float>();
        readonly List<AnimeBolt.Branch> primaryBranches = new List<AnimeBolt.Branch>();
        readonly List<AnimeBolt.Branch> lineBranches = new List<AnimeBolt.Branch>();
        readonly List<AnimeBolt.Branch> eyeBranches = new List<AnimeBolt.Branch>();
        float age, nextRipple;
        bool struck;
        int seed;

        public static KirinEffect Create(AbilityContext ctx, KirinAbility ability)
        {
            var go = new GameObject("Kirin");
            var effect = go.AddComponent<KirinEffect>();
            effect.target = ctx.target;
            effect.material = ability.animeMaterial;
            effect.eyeMaterial = ability.eyeMaterial != null ? ability.eyeMaterial : ability.animeMaterial;
            effect.impactMaterials = ability.impact;
            effect.casterPoint = ctx.caster.position;
            effect.destination = ctx.target != null ? ctx.target.transform.position : ctx.aimPoint;
            effect.destination.y = 0f;
            Vector3 forward = effect.destination - effect.casterPoint;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.01f) forward = ctx.caster.forward;
            effect.lateral = Vector3.Cross(Vector3.up, forward.normalized).normalized;
            if (effect.lateral.sqrMagnitude < 0.01f) effect.lateral = Vector3.right;
            effect.ReadTarget();
            effect.dragon = new KirinDragonGeometry();
            effect.seed = Random.Range(0, 100000);
            effect.block = new MaterialPropertyBlock();
            effect.primary = new Mesh { name = "Kirin (contornos principais)" };
            effect.primaryHalo = new Mesh { name = "Kirin (halo principal)" };
            effect.lines = new Mesh { name = "Kirin" };
            effect.halo = new Mesh { name = "Kirin (halo)" };
            effect.eyes = new Mesh { name = "Kirin (olhos)" };
            effect.primaryRenderer = effect.Part("Contornos principais", effect.primary, effect.material);
            effect.primaryHaloRenderer = effect.Part("Halo principal", effect.primaryHalo, effect.material);
            effect.lineRenderer = effect.Part("Traço", effect.lines, effect.material);
            effect.haloRenderer = effect.Part("Halo", effect.halo, effect.material);
            effect.eyeRenderer = effect.Part("Olhos", effect.eyes, effect.eyeMaterial);
            effect.eyeHaloRenderer = effect.Part("Olhos (halo)", effect.eyes, effect.eyeMaterial);
            // Cor do clarão no chão e no nevoeiro (partilhada pelos shaders da água e do nevoeiro).
            Shader.SetGlobalColor(ColorId, new Color(0.3f, 0.75f, 1f));
            effect.fog = new LightningFogGlow(effect.destination);
            effect.dragon.Rebuild(0f, effect.casterPoint, effect.destination, effect.lateral, effect.targetBounds);
            effect.Draw();
            return effect;
        }

        Renderer Part(string name, Mesh mesh, Material m)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = m;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return r;
        }

        void ReadTarget()
        {
            // Se desaparecer, ficam o último ponto e os últimos limites válidos.
            if (target != null)
            {
                destination = target.transform.position;
                destination.y = 0f;
                Collider body = target.GetComponent<Collider>();
                targetBounds = body != null ? body.bounds : new Bounds(destination + Vector3.up, new Vector3(0.8f, 2f, 0.8f));
            }
            else if (targetBounds.size == Vector3.zero)
                targetBounds = new Bounds(destination + Vector3.up, new Vector3(0.8f, 2f, 0.8f));
        }

        void Update()
        {
            float previous = age;
            age += Time.deltaTime;
            // Incluir o frame que cruza 0,65 s; depois a trajetória fica fixa.
            if (previous < KirinDragonGeometry.Descent) ReadTarget();
            dragon.Rebuild(age, casterPoint, destination, lateral, targetBounds);
            if (!struck && age >= dragon.ImpactTime)
            {
                struck = true;
                Vector3 impact = dragon.ImpactPoint;
                FogField.Blast(impact, radius: 5f, duration: 1.2f, expandTime: 0.18f, waterScale: 0f);
                WaterSim.Impulse(impact, 0.8f, -0.025f);
                if (impactMaterials != null && impactMaterials.cracks != null)
                {
                    LightningImpact.Create(impact, impactMaterials, seed);
                    var rng = new System.Random(seed);
                    for (int k = 0; k < 5; k++) AnimeLightningStrike.GroundArc(impact, material, rng);
                }
                nextRipple = dragon.ImpactTime + 0.055f;
                fog?.Release();
                fog = null; // o impacto passa a iluminar o ambiente
            }
            // Impulsos pequenos enquanto o corpo se despeja; não repetir o grande impacto da cabeça.
            while (struck && nextRipple <= age && nextRipple < dragon.TailTime)
            {
                WaterSim.Impulse(dragon.ImpactPoint, 0.3f, -0.003f, 6f);
                nextRipple += 0.055f;
            }
            if (age >= dragon.EndTime) { Destroy(gameObject); return; }
            Draw();
        }

        void Draw()
        {
            // O recorte muda CrackleRate vezes por segundo (estável entre saltos): crepitar sem tremer por frame.
            var rng = new System.Random(seed + Mathf.FloorToInt(age * CrackleRate));
            dragon.CollectBranches(primaryBranches, lineBranches, eyeBranches, BaseWidth, rng);
            AnimeBolt.BuildMesh(primary, primaryBranches, transform);
            AnimeBolt.BuildMesh(primaryHalo, primaryBranches, transform, 3);
            AnimeBolt.BuildMesh(lines, lineBranches, transform);
            AnimeBolt.BuildMesh(halo, lineBranches, transform, 3);
            AnimeBolt.BuildMesh(eyes, eyeBranches, transform);
            float flash = 1f + 0.15f * Mathf.PerlinNoise(age * 30f, 0.5f);
            // Os principais mais brilhantes que os pormenores (hierarquia de leitura).
            Set(primaryRenderer, 0f, flash * 1.35f);
            Set(primaryHaloRenderer, 1f, flash * 1.3f);
            Set(lineRenderer, 0f, flash * 0.8f);
            Set(haloRenderer, 1f, flash * 0.7f);
            Set(eyeRenderer, 0f, flash * 1.2f);
            Set(eyeHaloRenderer, 1f, flash);
            if (fog != null)
            {
                dragon.FogSamples(fogPositions, fogArrivals, fogHolds);
                fog.Update(fogPositions, fogArrivals, age, 0.5f, fogHolds);
            }
        }

        void Set(Renderer r, float part, float flash)
        {
            block.Clear();
            block.SetFloat("_Part", part);
            block.SetFloat("_Flash", flash);
            r.SetPropertyBlock(block);
        }

        void OnDestroy()
        {
            fog?.Release();
            dragon?.Release();
            if (primary != null) Destroy(primary);
            if (primaryHalo != null) Destroy(primaryHalo);
            if (lines != null) Destroy(lines);
            if (halo != null) Destroy(halo);
            if (eyes != null) Destroy(eyes);
        }
    }
}
