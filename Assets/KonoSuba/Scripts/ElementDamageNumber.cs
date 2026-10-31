using UnityEngine;
using UnityEngine.TextCore.Text;

namespace AnimLab.KonoSuba
{
    /// <summary>
    /// Número de dano elemental (fogo, água, eletricidade): uma quad virada para a câmara com o efeito do
    /// elemento atrás das letras (KonoSuba/ElementDamageNumber). Tamanho constante no ecrã.
    /// Críticos: "!" no fim, maiores, a tremer e com os algarismos a saltar em onda.
    /// </summary>
    public class ElementDamageNumber : MonoBehaviour
    {
        const int MaxGlyphs = 8;
        const float NormalPx = 38f, CritPx = 52f; // altura de 1 em no ecrã

        // Serifadas itálicas a negro: algarismos finos e afiados (Impact parecia cartoon). A primeira que existir.
        static readonly string[] FontFiles = { "cambriaz.ttf", "palabi.ttf", "timesbi.ttf", "arialbd.ttf" };

        static FontAsset font;
        static Material material;
        static bool loaded;

        static readonly int GlyphBoxId = Shader.PropertyToID("_GlyphBox");
        static readonly int GlyphUVId = Shader.PropertyToID("_GlyphUV");
        static readonly int GlyphCountId = Shader.PropertyToID("_GlyphCount");
        static readonly int SeedId = Shader.PropertyToID("_Seed");
        static readonly int AgeId = Shader.PropertyToID("_Age");
        static readonly int BurnId = Shader.PropertyToID("_Burn");
        static readonly int CritId = Shader.PropertyToID("_Crit");
        static readonly int CapHalfId = Shader.PropertyToID("_CapHalf");
        static readonly int StyleId = Shader.PropertyToID("_Style");

        readonly Vector4[] boxes = new Vector4[MaxGlyphs];
        readonly Vector4[] baseBoxes = new Vector4[MaxGlyphs];
        readonly Vector4[] uvs = new Vector4[MaxGlyphs];
        int count;
        Vector3 world;
        Vector2 drift;
        float age, lifetime, seed, capHalf;
        int style; // 0 fogo, 1 água, 2 eletricidade (como no shader)
        bool crit;
        Mesh mesh;
        MeshRenderer meshRenderer;
        MaterialPropertyBlock block;

        /// <summary>
        /// Devolve false para elementos sem estilo próprio, ou sem fonte/shader (quem chama usa o número normal).
        /// </summary>
        public static bool Spawn(Vector3 worldPos, int amount, bool crit, AbilityElement element)
        {
            int style = element switch
            {
                AbilityElement.Fire => 0,
                AbilityElement.Water => 1,
                AbilityElement.Lightning => 2,
                _ => -1
            };
            if (style < 0 || !Load()) return false;
            // Layer UI: fora do reflexo do chão (PlanarReflection).
            var n = new GameObject(element + " DamageNumber") { layer = LayerMask.NameToLayer("UI") }
                .AddComponent<ElementDamageNumber>();
            n.style = style;
            n.Init(worldPos, amount.ToString() + (crit ? "!" : ""), crit);
            return true;
        }

        static bool Load()
        {
            if (loaded) return font != null && material != null;
            loaded = true;
            Shader shader = Shader.Find("KonoSuba/ElementDamageNumber");
            string fonts = System.Environment.GetFolderPath(System.Environment.SpecialFolder.Fonts);
            foreach (string file in FontFiles)
            {
                string path = System.IO.Path.Combine(fonts, file);
                if (!System.IO.File.Exists(path)) continue;
                font = FontAsset.CreateFontAsset(path, 0, 90, 10,
                    UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA, 512, 512);
                if (font != null && font.TryAddCharacters("0123456789!", out _)) break;
                font = null;
            }
            if (font == null || shader == null) return false;
            material = new Material(shader) { hideFlags = HideFlags.DontSave, mainTexture = font.atlasTexture };
            // O SDF vai de 0.5 (borda) a 0/1 ao longo do padding: isto em unidades de em.
            material.SetFloat("_SdfPerEm", 0.5f / font.atlasPadding * font.faceInfo.pointSize);
            return true;
        }

        void Init(Vector3 worldPos, string text, bool isCrit)
        {
            world = worldPos;
            crit = isCrit;
            seed = Random.value * 100f;
            lifetime = crit ? 1.6f : 1.25f;
            // Espalhar um pouco para golpes seguidos não ficarem uns por cima dos outros.
            drift = new Vector2(Random.Range(-28f, 28f), Random.Range(-6f, 10f));

            // Algarismos em espaço "em" (1 = tamanho da fonte), centrados no ponto.
            float em = font.faceInfo.pointSize, pad = font.atlasPadding;
            float atlasW = font.atlasWidth, atlasH = font.atlasHeight;
            float pen = 0f;
            foreach (char c in text)
            {
                if (count >= MaxGlyphs || !font.characterLookupTable.TryGetValue(c, out Character ch)) continue;
                var g = ch.glyph;
                var m = g.metrics;
                var r = g.glyphRect;
                baseBoxes[count] = new Vector4((pen + m.horizontalBearingX - pad) / em,
                    (m.horizontalBearingY - m.height - pad) / em, (m.width + 2f * pad) / em, (m.height + 2f * pad) / em);
                uvs[count] = new Vector4((r.x - pad) / atlasW, (r.y - pad) / atlasH,
                    (r.width + 2f * pad) / atlasW, (r.height + 2f * pad) / atlasH);
                pen += m.horizontalAdvance * (c == '!' ? 0.9f : 0.95f); // um pouco apertado
                count++;
            }
            float width = pen / em, cap = font.faceInfo.capLine / em;
            capHalf = cap * 0.5f;
            for (int i = 0; i < count; i++) { baseBoxes[i].x -= width * 0.5f; baseBoxes[i].y -= cap * 0.5f; }

            // A quad tem folga para o efeito (chamas em cima, gotas em baixo, arcos à volta) e para o salto
            // dos críticos.
            float x0 = -width * 0.5f - 0.3f, x1 = width * 0.5f + 0.3f;
            float y0 = -cap * 0.5f - (style == 1 ? (crit ? 0.75f : 0.6f) : 0.3f);
            float y1 = cap * 0.5f + (style == 0 ? (crit ? 0.9f : 0.7f) : 0.35f) + (crit ? 0.1f : 0f);
            var verts = new[] { new Vector3(x0, y0), new Vector3(x1, y0), new Vector3(x0, y1), new Vector3(x1, y1) };
            mesh = new Mesh { name = "ElementDamageNumber", hideFlags = HideFlags.DontSave };
            mesh.vertices = verts;
            mesh.uv = new[] { new Vector2(x0, y0), new Vector2(x1, y0), new Vector2(x0, y1), new Vector2(x1, y1) };
            mesh.triangles = new[] { 0, 2, 1, 1, 2, 3 };
            mesh.RecalculateBounds();

            gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            meshRenderer = gameObject.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = material;
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
            block = new MaterialPropertyBlock();
            Place();
        }

        void LateUpdate()
        {
            age += Time.deltaTime;
            if (age >= lifetime) { Destroy(gameObject); return; }
            Place();
        }

        void Place()
        {
            Camera cam = Camera.main;
            if (cam == null) return;
            Transform ct = cam.transform;
            float dist = Vector3.Dot(world - ct.position, ct.forward);
            meshRenderer.enabled = dist > 0.1f;
            if (dist <= 0.1f) return;
            float worldPerPx = 2f * dist * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) / Screen.height;

            float t = age / lifetime;
            // Soco: entra grande e assenta em ~0.14 s.
            float pop = Mathf.Clamp01(age / 0.14f);
            float s = Mathf.Lerp(crit ? 2.3f : 1.8f, 1f, 1f - (1f - pop) * (1f - pop));
            // Fogo e eletricidade sobem; a água cai um pouco no fim, como se escorresse.
            float up = (crit ? 50f : 65f) * (1f - (1f - t) * (1f - t));
            if (style == 1) up = 40f * Mathf.Sin(Mathf.Min(t, 0.5f) * Mathf.PI) - 25f * Mathf.Max(0f, t - 0.5f) * 2f;
            Vector2 px = drift + new Vector2(0f, up);
            if (crit)
            {
                // Tremer: forte no impacto e nunca para de todo.
                float shake = Mathf.Lerp(9f, 3f, Mathf.Clamp01(age / 0.35f));
                px += new Vector2(Mathf.PerlinNoise(age * 38f, seed) - 0.5f,
                                  Mathf.PerlinNoise(seed, age * 38f) - 0.5f) * (2f * shake);
            }

            transform.SetPositionAndRotation(world + (ct.right * px.x + ct.up * px.y) * worldPerPx, ct.rotation);
            transform.localScale = Vector3.one * ((crit ? CritPx : NormalPx) * worldPerPx * s);

            // Críticos: os algarismos saltam um a um, em onda.
            // Eletricidade: de vez em quando um algarismo dá um salto seco para o lado (glitch), ~16 vezes/s.
            int tick = Mathf.FloorToInt(age * 16f);
            for (int i = 0; i < count; i++)
            {
                boxes[i] = baseBoxes[i];
                if (crit) boxes[i].y += 0.1f * Mathf.Max(0f, Mathf.Sin(age * 11f - i * 0.8f));
                if (style == 2)
                {
                    float h = Mathf.Repeat(Mathf.Sin((tick + i * 7.13f + seed) * 12.9898f) * 43758.5453f, 1f);
                    if (h > (crit ? 0.7f : 0.82f)) boxes[i].x += (h - 0.85f) * 0.35f;
                }
            }

            block.SetVectorArray(GlyphBoxId, boxes);
            block.SetVectorArray(GlyphUVId, uvs);
            block.SetFloat(GlyphCountId, count);
            block.SetFloat(SeedId, seed);
            block.SetFloat(AgeId, age);
            block.SetFloat(BurnId, Mathf.Clamp01((t - 0.6f) / 0.4f));
            block.SetFloat(CritId, crit ? 1f : 0f);
            block.SetFloat(CapHalfId, capHalf);
            block.SetFloat(StyleId, style);
            meshRenderer.SetPropertyBlock(block);
        }

        void OnDestroy()
        {
            if (mesh != null) Destroy(mesh);
        }
    }
}
