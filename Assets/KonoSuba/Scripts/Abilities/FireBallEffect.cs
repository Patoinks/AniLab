using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace AnimLab.KonoSuba
{
    /// <summary>
    /// Explosão do impacto, no mesmo estilo da bola (KonoSuba/FireToon):
    ///   - a bola rebenta até ~4 m em ~0,15 s, rodopia mais depressa e desfaz-se — os buracos do anel crescem até
    ///     não sobrar nada — enquanto sobe um pouco;
    ///   - uma segunda camada mais pequena e quente por cima, desfasada, para a explosão ter corpo;
    ///   - clarão e luz quente; sopro no nevoeiro; vapor (e evaporação da película) quando bate em água;
    ///   - brilho quente breve no alvo.
    /// O rasto em voo está em FireBallFlightVisual. Os materiais partilhados nunca são alterados.
    /// </summary>
    public class FireBallEffect : MonoBehaviour
    {
        class Part
        {
            public MeshRenderer renderer;
            public Vector3 offset;
            public float delay, size, seed;
        }

        FireBallAbility ability;
        MeshRenderer outer, inner;
        readonly List<Part> steam = new List<Part>();
        readonly List<Renderer> afterglow = new List<Renderer>();
        MaterialPropertyBlock properties;
        Light flash;
        float age, seed;
        bool hasGround, wet;
        Vector3 center, ground;

        public static void Impact(Vector3 center, Collider body, Transform caster, FireBallAbility ability)
        {
            var e = new GameObject("FireBallImpact").AddComponent<FireBallEffect>();
            e.ability = ability;
            e.center = center;
            e.seed = Random.value * 100f;
            e.transform.position = center;
            e.properties = new MaterialPropertyBlock();
            var target = body != null ? body.GetComponentInParent<LockOnTarget>() : null;
            e.hasGround = FindGround(center, caster, target, out RaycastHit floor)
                && center.y - floor.point.y < 3f;
            e.ground = floor.point;
            if (!e.hasGround)
            {
                e.ground = new Vector3(center.x, 0f, center.z);
                e.hasGround = center.y < 3f && WaterSim.HasWaterAt(e.ground);
            }
            e.wet = e.hasGround && Mathf.Abs(e.ground.y) < 0.25f && center.y - e.ground.y < 2.5f
                && WaterSim.HasWaterAt(e.ground);
            FogField.Blast(center, radius: 5f, strength: 0.9f, duration: 2.2f,
                expandTime: 0.14f, waterScale: 0f, waterSpray: false);
            if (e.wet)
            {
                WaterSim.TransferVolume(e.ground, 1.3f, -Mathf.Max(0f, ability.evaporatedVolume));
                WaterSim.Impulse(e.ground, 1.5f, -0.05f);
            }

            e.outer = FireBallVisual.Billboard("Explosão", e.transform, ability.ballMaterial);
            e.inner = FireBallVisual.Billboard("Núcleo da explosão", e.transform, ability.ballMaterial);
            e.flash = FireBallVisual.Glow(e.transform, 14f);
            if (e.wet)
                for (int i = 0; i < 3; i++)
                    e.steam.Add(new Part
                    {
                        renderer = FireBallVisual.Volume("Vapor", e.transform, ability.steamMaterial),
                        offset = new Vector3(Random.Range(-1.2f, 1.2f), 0f, Random.Range(-1.2f, 1.2f)),
                        delay = 0.3f + i * 0.15f, size = Random.Range(2.6f, 3.6f), seed = Random.value * 100f
                    });
            if (target != null)
            {
                e.AddAfterglow(target);
                int dmg = ability.RollDamage(out bool crit);
                DamageNumbers.Show(target.AimPoint + Vector3.up * 1.1f, dmg, crit, ability.element);
            }
            e.DrawImpact();
        }

        static bool FindGround(Vector3 at, Transform caster, LockOnTarget target, out RaycastHit floor)
        {
            floor = default;
            float nearest = float.PositiveInfinity;
            foreach (var hit in Physics.RaycastAll(at + Vector3.up * 0.2f, Vector3.down, 7f,
                ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.normal.y < 0.6f || (caster != null && hit.transform.IsChildOf(caster))
                    || (target != null && hit.transform.IsChildOf(target.transform))) continue;
                if (hit.distance < nearest) { nearest = hit.distance; floor = hit; }
            }
            return nearest < float.PositiveInfinity;
        }

        void AddAfterglow(LockOnTarget target)
        {
            foreach (Renderer original in target.GetComponentsInChildren<Renderer>())
            {
                if (!original.enabled || !original.gameObject.activeInHierarchy
                    || (target.lockMarker != null && original.transform.IsChildOf(target.lockMarker.transform))) continue;
                var skin = original as SkinnedMeshRenderer;
                var filter = original.GetComponent<MeshFilter>();
                if (skin == null && (!(original is MeshRenderer) || filter == null || filter.sharedMesh == null)) continue;
                var go = new GameObject("Calor do impacto");
                go.transform.SetParent(original.transform, false);
                Renderer overlay;
                if (skin != null)
                {
                    var copy = go.AddComponent<SkinnedMeshRenderer>();
                    copy.sharedMesh = skin.sharedMesh;
                    copy.bones = skin.bones;
                    copy.rootBone = skin.rootBone;
                    copy.localBounds = skin.localBounds;
                    // A malha acompanha também expressões/poses com blend shapes, sem tocar no original.
                    if (skin.sharedMesh != null)
                        for (int i = 0; i < skin.sharedMesh.blendShapeCount; i++)
                            copy.SetBlendShapeWeight(i, skin.GetBlendShapeWeight(i));
                    overlay = copy;
                }
                else
                {
                    go.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
                    overlay = go.AddComponent<MeshRenderer>();
                }
                int submeshes = skin != null ? (skin.sharedMesh != null ? skin.sharedMesh.subMeshCount : 1)
                    : filter.sharedMesh.subMeshCount;
                var materials = new Material[Mathf.Max(1, submeshes)];
                for (int i = 0; i < materials.Length; i++) materials[i] = ability.hitMaterial;
                overlay.sharedMaterials = materials;
                FireBallVisual.NoShadows(overlay);
                afterglow.Add(overlay);
            }
        }

        void Update()
        {
            age += Time.deltaTime;
            if (ability == null) { Destroy(gameObject); return; }
            DrawImpact();
        }

        void DrawImpact()
        {
            Camera cam = Camera.main;
            Quaternion face = cam != null ? cam.transform.rotation : Quaternion.identity;
            float floorY = hasGround ? ground.y : -100f;

            // Camada exterior: rebenta, sobe um pouco e desfaz-se.
            float grow = 1f - Mathf.Exp(-age * 9f);
            float unit = Mathf.Lerp(0.5f, 1.55f, grow);
            float burst = Mathf.SmoothStep(0f, 1f, (age - 0.18f) / 0.7f);
            Vector3 pos = center + Vector3.up * (age * 0.8f);
            // Toda a bola acima do chão: o quadrado virado para a câmara não pode atravessar a água.
            pos.y = Mathf.Max(pos.y, floorY + unit * 1.7f);
            Draw(outer, pos, face, unit, burst, spin: 2.2f, s: seed, tailLen: 0.3f);

            // Camada interior: mais pequena e quente, desfaz-se primeiro.
            float innerUnit = Mathf.Lerp(0.3f, 0.95f, 1f - Mathf.Exp(-age * 12f));
            float innerBurst = Mathf.SmoothStep(0f, 1f, (age - 0.1f) / 0.45f);
            Draw(inner, pos + Vector3.up * 0.15f, face, innerUnit, innerBurst, spin: -2.8f, s: seed + 31f, tailLen: 0.2f);

            // Vapor claro a subir (quando a explosão bate em água).
            foreach (Part st in steam)
            {
                float t = age - st.delay;
                st.renderer.enabled = t > 0f && t < 2.6f;
                if (!st.renderer.enabled) continue;
                float k = t / 2.6f;
                st.renderer.transform.SetPositionAndRotation(ground + st.offset + Vector3.up * (0.6f + t * 1.1f), Quaternion.identity);
                st.renderer.transform.localScale = Vector3.one * st.size * Mathf.Lerp(0.6f, 1.6f, k);
                float sf = Mathf.SmoothStep(0f, 1f, t / 0.4f) * (1f - Mathf.SmoothStep(0f, 1f, k));
                properties.Clear();
                properties.SetFloat("_Heat", 0f);
                properties.SetFloat("_Fade", sf * 0.55f);
                properties.SetFloat("_Seed", st.seed);
                properties.SetVector("_Flow", new Vector4(0f, 1f, 0f, 0.8f));
                properties.SetVector("_Core", Vector4.zero);
                properties.SetFloat("_Turb", 1.4f);
                st.renderer.SetPropertyBlock(properties);
            }

            // Clarão curto e luz quente enquanto há fogo.
            flash.transform.position = center + Vector3.up * 0.8f;
            flash.intensity = 6f * Mathf.Exp(-age * 18f) + 3f * (1f - burst);

            foreach (Renderer r in afterglow)
            {
                if (r == null) continue;
                properties.Clear();
                properties.SetFloat("_Fade", Mathf.Pow(Mathf.Clamp01(1f - age / 0.8f), 2f));
                r.SetPropertyBlock(properties);
                r.enabled = age < 0.8f;
            }
            if (age > 3.2f) Destroy(gameObject);
        }

        void Draw(MeshRenderer r, Vector3 pos, Quaternion face, float unit, float burst, float spin, float s, float tailLen)
        {
            const float Extent = 2.6f;
            r.enabled = burst < 0.99f;
            r.transform.SetPositionAndRotation(pos, face);
            r.transform.localScale = Vector3.one * unit * 2f * Extent;
            FireBallVisual.SetToon(r, properties, s, Vector2.up, tailLen, Extent, burst, spin, 1f);
        }

        void OnDestroy()
        {
            // As sobreposições estão parentadas ao alvo; pertencem ao efeito, não à personagem.
            foreach (Renderer r in afterglow) if (r != null) Destroy(r.gameObject);
        }
    }

    static class FireBallVisual
    {
        static Mesh quad;

        public static void NoShadows(Renderer r)
        {
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        public static MeshRenderer Volume(string name, Transform parent, Material material)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = WaterBallProjectile.SmoothSphere();
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = material;
            NoShadows(r);
            return r;
        }

        /// <summary>Quadrado de lado 1 (uv 0..1), para o fogo virado para a câmara.</summary>
        public static MeshRenderer Billboard(string name, Transform parent, Material material)
        {
            if (quad == null)
            {
                quad = new Mesh { name = "FireQuad" };
                quad.vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f),
                    new Vector3(0.5f, 0.5f, 0f), new Vector3(0.5f, -0.5f, 0f) };
                quad.uv = new[] { Vector2.zero, Vector2.up, Vector2.one, Vector2.right };
                quad.triangles = new[] { 0, 1, 2, 0, 2, 3 };
                quad.RecalculateBounds();
            }
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = quad;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = material;
            NoShadows(r);
            return r;
        }

        public static void SetToon(Renderer r, MaterialPropertyBlock block, float seed, Vector2 tailDir, float tailLen,
            float extent, float burst, float spin, float fade)
        {
            block.Clear();
            block.SetFloat("_Seed", seed);
            block.SetVector("_TailDir", new Vector4(tailDir.x, tailDir.y, 0f, 0f));
            block.SetFloat("_TailLen", tailLen);
            block.SetFloat("_Extent", extent);
            block.SetFloat("_Burst", burst);
            block.SetFloat("_Spin", spin);
            block.SetFloat("_Fade", fade);
            r.SetPropertyBlock(block);
        }

        public static Light Glow(Transform parent, float range)
        {
            var go = new GameObject("Luz quente");
            go.transform.SetParent(parent, false);
            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.34f, 0f);
            light.range = range;
            light.intensity = 0f;
            light.renderMode = LightRenderMode.ForcePixel;
            light.shadows = LightShadows.None;
            return light;
        }
    }
}
