using UnityEngine;

namespace AnimLab.KonoSuba
{
    /// <summary>
    /// Impacto da bala de água comprimida — uma lança de água, não um balde:
    ///   Num corpo — contacto estreito; o núcleo atravessa-o na direção do disparo (~7 ms) e sai pelas costas
    ///               num pulso de jato curto, seguido de uma manga de spray em cone (superfície contínua, não gotas).
    ///               Logo atrás do alvo a água descomprime-se para o volume real e cai como massa contínua
    ///               (WaterImpactSurface); só uma pequena parte fica na entrada e escorre.
    ///               Volume repartido uma vez: 15% entrada, 85% atrás do alvo.
    ///   Na água   — cavidade estreita na película e rebordo grosso que sobe e abre; se o tiro vier inclinado,
    ///               um sulco curto na direção do disparo. Sem jato por baixo.
    /// </summary>
    public class WaterPressureImpact : MonoBehaviour
    {
        const float FrontShare = 0.15f;
        const float FanSpeed = 9f; // abertura lateral da água que se descomprime atrás do alvo (m/s)
        const float JetSpeed = 80f, JetTime = 0.035f;
        const float VeilLength = 3.2f, VeilAngle = 20f, VeilGrow = 0.12f, VeilLife = 0.45f;
        const int VeilRings = 10, VeilSegments = 28;

        Transform core;
        MaterialPropertyBlock coreBlock;
        Vector3 exit, dir;
        float age, coreRadius;
        Mesh veilMesh;
        MeshRenderer veilRenderer;
        MaterialPropertyBlock veilBlock;
        static readonly int GlowId = Shader.PropertyToID("_Glow");
        static readonly int FadeId = Shader.PropertyToID("_Fade");

        /// <summary>A bala comprimida bateu num corpo.</summary>
        public static void OnBody(Collider body, Vector3 center, Vector3 velocity, float visibleRadius, float waterRadius, WaterBallAbility ability)
        {
            Vector3 dir = velocity.sqrMagnitude > 1e-6f ? velocity.normalized : Vector3.forward;
            // Ponto de saída: a mesma reta vista do outro lado do corpo.
            Vector3 exit = center + dir * 0.8f;
            if (body.Raycast(new Ray(center + dir * 6f, -dir), out RaycastHit back, 6f)) exit = back.point;
            Vector3 entry = body.ClosestPoint(center - dir * 0.5f);

            // Na entrada fica pouca água, que se cola e escorre.
            WaterImpactSurface.Spawn(entry - dir * visibleRadius, dir * 3f, visibleRadius, waterRadius, 0f, body,
                                     ability.impactMaterial, FrontShare, 0f);
            // Atrás do alvo: a maior parte descomprime-se e cai como massa contínua.
            WaterImpactSurface.Spawn(exit + dir * 0.4f, dir * 9f, visibleRadius, waterRadius, 1f, body,
                                     ability.impactMaterial, 1f - FrontShare, 0f, FanSpeed);

            var go = new GameObject("WaterPressureImpact");
            var p = go.AddComponent<WaterPressureImpact>();
            p.Init(exit, dir, visibleRadius, ability);

            // O ar à saída também é empurrado: o nevoeiro abre-se num corredor atrás do alvo (a água não, aqui).
            Vector3 g = new Vector3(exit.x, 0f, exit.z);
            FogField.Wind(g, dir, 7f, 1.6f, 1f, 3.5f, true, 0f);
            Transform root = body.attachedRigidbody != null ? body.attachedRigidbody.transform : body.transform;
            WetSurface.AddSpot(root, entry, 0.16f); // furo de entrada
            WetSurface.AddSpot(root, exit, 0.2f);   // saída pelas costas
        }

        /// <summary>A bala comprimida falhou e bateu na película de água.</summary>
        public static void OnWater(Vector3 center, Vector3 velocity, float visibleRadius, float waterRadius, WaterBallAbility ability)
        {
            Vector3 ground = new Vector3(center.x, 0f, center.z);
            Vector3 dir = velocity.sqrMagnitude > 1e-6f ? velocity.normalized : Vector3.down;
            // Cavidade estreita: a película é afastada de repente (pode ver-se o fundo por um instante).
            WaterSim.Impulse(ground, 0.22f, -0.12f, 0f);
            // Tiro inclinado: sulco curto na direção do disparo.
            Vector3 flat = Vector3.ProjectOnPlane(dir, Vector3.up);
            float slant = flat.magnitude;
            if (slant > 0.35f) WaterSim.Push(ground, flat, 0.5f, Mathf.Lerp(1f, 3f, slant), 0.4f, 0.35f);
            // Rebordo grosso que sobe e abre: a água desce devagar e espalha-se com coroa (WaterImpactSurface).
            WaterImpactSurface.Spawn(ground + Vector3.up * visibleRadius, flat * 6f + Vector3.down * 6f, visibleRadius, waterRadius,
                                     1f, null, ability.impactMaterial, 1f, 0f, FanSpeed);
        }

        void Init(Vector3 exit, Vector3 dir, float visibleRadius, WaterBallAbility ability)
        {
            this.exit = exit;
            this.dir = dir;
            coreRadius = Mathf.Clamp(visibleRadius * 0.6f, 0.04f, 0.1f);

            // Núcleo: o pulso de água que sai pelas costas, esticado, ainda a brilhar da compressão.
            core = new GameObject("Jet").transform;
            core.SetParent(transform, false);
            core.gameObject.AddComponent<MeshFilter>().sharedMesh = WaterBallProjectile.SmoothSphere();
            var mr = core.gameObject.AddComponent<MeshRenderer>();
            mr.sharedMaterial = ability.orbMaterial;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            coreBlock = new MaterialPropertyBlock();
            core.position = exit;
            core.rotation = Quaternion.LookRotation(dir);

            // Manga de spray em cone a partir da saída.
            veilMesh = new Mesh { name = "PressureVeil" };
            veilMesh.MarkDynamic();
            var veil = new GameObject("Veil");
            veil.transform.SetParent(transform, false);
            veil.AddComponent<MeshFilter>().sharedMesh = veilMesh;
            veilRenderer = veil.AddComponent<MeshRenderer>();
            veilRenderer.sharedMaterial = ability.veilMaterial;
            veilRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            veilBlock = new MaterialPropertyBlock();
            BuildVeil(true);
        }

        void Update()
        {
            age += Time.deltaTime;

            // Pulso de jato: sai a ~80 m/s, esticado; ao fim de ~35 ms já se desfez na massa que cai.
            if (core != null)
            {
                float k = Mathf.Clamp01(age / JetTime);
                core.position = exit + dir * JetSpeed * Mathf.Min(age, JetTime) * 0.7f;
                float len = Mathf.Lerp(6f, 14f, k) * coreRadius * 2f;
                float w = coreRadius * 2f * (1f - k * 0.6f);
                core.localScale = new Vector3(w, w, len);
                coreBlock.SetFloat(GlowId, 1.4f * (1f - k));
                core.GetComponent<MeshRenderer>().SetPropertyBlock(coreBlock);
                if (age > JetTime * 1.6f) { Destroy(core.gameObject); core = null; }
            }

            if (veilMesh != null)
            {
                BuildVeil(false);
                float fade = 1f - Mathf.Clamp01((age - VeilGrow) / (VeilLife - VeilGrow));
                veilBlock.SetFloat(FadeId, fade * fade);
                veilRenderer.SetPropertyBlock(veilBlock);
            }

            if (age > VeilLife) Destroy(gameObject);
        }

        // Cone aberto a partir da saída: cresce depressa, abre-se e cede um pouco à gravidade.
        void BuildVeil(bool first)
        {
            float grow = 1f - Mathf.Pow(1f - Mathf.Clamp01(age / VeilGrow), 2f);
            float length = VeilLength * grow;
            float tan = Mathf.Tan(VeilAngle * Mathf.Deg2Rad) * (1f + 0.6f * Mathf.Clamp01(age / VeilLife));
            Vector3 u = Vector3.Cross(dir, Mathf.Abs(dir.y) > 0.9f ? Vector3.forward : Vector3.up).normalized;
            Vector3 v = Vector3.Cross(dir, u);
            var verts = new Vector3[(VeilRings + 1) * (VeilSegments + 1)];
            var uvs = new Vector2[verts.Length];
            for (int i = 0; i <= VeilRings; i++)
            {
                float t = i / (float)VeilRings;
                float along = length * t;
                float r = coreRadius + along * tan;
                Vector3 c = exit + dir * along + Physics.gravity * (0.5f * age * age * t);
                for (int j = 0; j <= VeilSegments; j++)
                {
                    float a = j / (float)VeilSegments * Mathf.PI * 2f;
                    verts[i * (VeilSegments + 1) + j] = c + (Mathf.Cos(a) * u + Mathf.Sin(a) * v) * r - transform.position;
                    uvs[i * (VeilSegments + 1) + j] = new Vector2(j / (float)VeilSegments, t);
                }
            }
            veilMesh.vertices = verts;
            veilMesh.uv = uvs;
            if (first)
            {
                var tris = new int[VeilRings * VeilSegments * 6];
                int n = 0;
                for (int i = 0; i < VeilRings; i++)
                    for (int j = 0; j < VeilSegments; j++)
                    {
                        int a = i * (VeilSegments + 1) + j, b = a + VeilSegments + 1;
                        tris[n++] = a; tris[n++] = b; tris[n++] = a + 1;
                        tris[n++] = a + 1; tris[n++] = b; tris[n++] = b + 1;
                    }
                veilMesh.triangles = tris;
            }
            veilMesh.RecalculateNormals();
            veilMesh.RecalculateBounds();
        }

        void OnDestroy()
        {
            if (veilMesh != null) Destroy(veilMesh);
        }
    }
}
