using UnityEngine;
using UnityEngine.TextCore.Text;

namespace AnimLab.KonoSuba
{
    /// <summary>
    /// Número de dano elemental (fogo, água, eletricidade, ar, terra, gelo, trevas): uma quad virada para a câmara
    /// com o efeito do elemento atrás das letras (KonoSuba/ElementDamageNumber). Tamanho constante no ecrã.
    /// Críticos: maiores, a tremer e com os algarismos a saltar em onda.
    /// </summary>
    public class ElementDamageNumber : MonoBehaviour
    {
        const int MaxGlyphs = 8;
        const float NormalPx = 38f, CritPx = 52f; // altura de 1 em no ecrã

        // Algarismos a pincel do projeto (Resources/Fonts, só tem 0–9). Sem ela, a primeira destas fontes do Windows
        // que existir: serifadas itálicas a negro, finas e afiadas.
        const string BrushFont = "Fonts/AnimLab-Brush-Numeros";
        static readonly string[] FontFiles = { "cambriaz.ttf", "palabi.ttf", "timesbi.ttf", "arialbd.ttf" };

        // Duas versões da mesma fonte: SDF curto (0.11 em; os efeitos dos elementos estão afinados para isto)
        // e SDF largo (0.6 em) para a variante 9, cujo brilho se estende até ~0.75 da altura dos algarismos.
        static FontAsset font, wideFont;
        static Material material, wideMaterial;
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
        static readonly int FxId = Shader.PropertyToID("_Fx");
        static readonly int SdfPerEmId = Shader.PropertyToID("_SdfPerEm");

        /// <summary>
        /// Variante do efeito (0 completo; 1 brilho, 2 contorno, 3 subtil, 4 faíscas, 5 risco, 6 plano, 7 Metin,
        /// 8 só a fonte, 9 creme com brilho laranja e marcas a pincel). Cada número guarda a que estava ativa
        /// quando nasceu. A 7 e a 9 têm a animação curta do Metin.
        /// </summary>
        public static int Variant = 9;

        bool Metin => fx == 7 || fx == 9;
        FontAsset fa;
        Material mat;

        readonly Vector4[] boxes = new Vector4[MaxGlyphs];
        readonly Vector4[] baseBoxes = new Vector4[MaxGlyphs];
        readonly Vector4[] uvs = new Vector4[MaxGlyphs];
        int count;
        Vector3 world;
        Vector2 drift;
        float age, lifetime, seed, capHalf;
        int fx;
        int style; // 0 fogo, 1 água, 2 eletricidade, 3 ar, 4 terra, 5 gelo, 6 trevas (como no shader)
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
                AbilityElement.Air => 3,
                AbilityElement.Earth => 4,
                AbilityElement.Ice => 5,
                AbilityElement.Dark => 6,
                _ => -1
            };
            if (style < 0 || !Load()) return false;
            // Layer UI: fora do reflexo do chão (PlanarReflection).
            var n = new GameObject(element + " DamageNumber") { layer = LayerMask.NameToLayer("UI") }
                .AddComponent<ElementDamageNumber>();
            n.style = style;
            n.fx = Variant;
            n.fa = Variant == 9 ? wideFont : font;
            n.mat = Variant == 9 ? wideMaterial : material;
            n.Init(worldPos, amount.ToString(), crit);
            return true;
        }

        static bool Load()
        {
            if (loaded) return font != null && material != null && wideFont != null;
            loaded = true;
            Shader shader = Shader.Find("KonoSuba/ElementDamageNumber");
            if (shader == null) return false;
            return Make(shader, 10, 512, out font, out material) && Make(shader, 54, 1024, out wideFont, out wideMaterial);
        }

        static bool Make(Shader shader, int padding, int atlas, out FontAsset fontAsset, out Material mat)
        {
            fontAsset = null;
            mat = null;
            var brush = Resources.Load<Font>(BrushFont);
            if (brush != null)
            {
                fontAsset = FontAsset.CreateFontAsset(brush, 90, padding,
                    UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA, atlas, atlas, AtlasPopulationMode.Dynamic, false);
                if (fontAsset == null || !fontAsset.TryAddCharacters("0123456789", out _)) fontAsset = null;
            }
            string fonts = System.Environment.GetFolderPath(System.Environment.SpecialFolder.Fonts);
            foreach (string file in FontFiles)
            {
                if (fontAsset != null) break;
                string path = System.IO.Path.Combine(fonts, file);
                if (!System.IO.File.Exists(path)) continue;
                fontAsset = FontAsset.CreateFontAsset(path, 0, 90, padding,
                    UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA, atlas, atlas);
                if (fontAsset != null && fontAsset.TryAddCharacters("0123456789", out _)) break;
                fontAsset = null;
            }
            if (fontAsset == null) return false;
            mat = new Material(shader) { hideFlags = HideFlags.DontSave, mainTexture = fontAsset.atlasTexture };
            return true;
        }

        void Init(Vector3 worldPos, string text, bool isCrit)
        {
            world = worldPos;
            crit = isCrit;
            seed = Random.value * 100f;
            lifetime = Metin ? (crit ? 0.9f : 0.75f) : crit ? 1.6f : 1.25f;
            // Espalhar um pouco para golpes seguidos não ficarem uns por cima dos outros.
            drift = new Vector2(Random.Range(-28f, 28f), Random.Range(-6f, 10f));

            // Algarismos em espaço "em" (1 = tamanho da fonte), centrados no ponto.
            float em = fa.faceInfo.pointSize, pad = fa.atlasPadding;
            float atlasW = fa.atlasWidth, atlasH = fa.atlasHeight;
            float pen = 0f;
            foreach (char c in text)
            {
                if (count >= MaxGlyphs || !fa.characterLookupTable.TryGetValue(c, out Character ch)) continue;
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
            // Altura dos algarismos (a fonte a pincel não tem maiúsculas, por isso a capLine não serve).
            float cap = fa.characterLookupTable.TryGetValue('0', out Character zero)
                ? zero.glyph.metrics.horizontalBearingY / em : fa.faceInfo.capLine / em;
            float width = pen / em;
            capHalf = cap * 0.5f;
            for (int i = 0; i < count; i++) { baseBoxes[i].x -= width * 0.5f; baseBoxes[i].y -= cap * 0.5f; }

            // A quad tem folga para o efeito (chamas/fumo/espigões em cima, gotas/pingentes/pó em baixo, rajadas
            // à direita, arcos à volta) e para o salto dos críticos.
            float k = crit ? 1.2f : 1f;
            float x0 = -width * 0.5f - 0.3f, x1 = width * 0.5f + (style == 3 ? 0.75f * k : 0.3f);
            float y0 = -cap * 0.5f - (style == 1 ? 0.6f * k : style == 4 || style == 5 ? 0.45f * k : 0.3f);
            float y1 = cap * 0.5f + (style == 0 || style == 6 ? 0.7f * k : style == 5 ? 0.6f * k : 0.35f)
                + (crit ? 0.1f : 0f);
            // Metin: só brilho e riscos, que passam do número para os lados. 9: brilho largo e as marcas.
            if (Metin) { x0 = -width * 0.5f - 0.9f; x1 = width * 0.5f + 0.9f; y0 = -cap * 0.5f - 0.5f; y1 = cap * 0.5f + 0.5f; }
            if (fx == 9) { x0 = -width * 0.5f - 0.75f; x1 = width * 0.5f + 0.75f; y0 = -cap * 0.5f - 0.6f; y1 = cap * 0.5f + 0.6f; }
            var verts = new[] { new Vector3(x0, y0), new Vector3(x1, y0), new Vector3(x0, y1), new Vector3(x1, y1) };
            mesh = new Mesh { name = "ElementDamageNumber", hideFlags = HideFlags.DontSave };
            mesh.vertices = verts;
            mesh.uv = new[] { new Vector2(x0, y0), new Vector2(x1, y0), new Vector2(x0, y1), new Vector2(x1, y1) };
            mesh.triangles = new[] { 0, 2, 1, 1, 2, 3 };
            mesh.RecalculateBounds();

            gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            meshRenderer = gameObject.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = mat;
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
            // Soco: entra grande e assenta em ~0.14 s. Metin: entra a 80%, chega a 100% (com um pouco de
            // passagem) aos 0.10 s e assenta.
            float pop = Mathf.Clamp01(age / 0.14f);
            float s = Mathf.Lerp(crit ? 2.3f : 1.8f, 1f, 1f - (1f - pop) * (1f - pop));
            if (Metin)
            {
                float a = Mathf.Clamp01(age / 0.1f);
                s = age < 0.1f ? Mathf.Lerp(0.8f, crit ? 1.12f : 1.06f, 1f - (1f - a) * (1f - a))
                               : Mathf.Lerp(crit ? 1.12f : 1.06f, 1f, Mathf.Clamp01((age - 0.1f) / 0.08f));
            }
            // Sobem por defeito; a água cai um pouco no fim (escorre), a terra cai com peso, o ar é levado
            // para o lado pelo vento.
            float up = (crit ? 50f : 65f) * (1f - (1f - t) * (1f - t));
            if (style == 1) up = 40f * Mathf.Sin(Mathf.Min(t, 0.5f) * Mathf.PI) - 25f * Mathf.Max(0f, t - 0.5f) * 2f;
            if (style == 4) up = 30f * Mathf.Sin(Mathf.Min(t, 0.4f) / 0.4f * Mathf.PI * 0.5f) - 160f * Mathf.Pow(Mathf.Max(0f, t - 0.55f), 2f);
            Vector2 px = drift + new Vector2(style == 3 ? -90f * t * t : 0f, up);
            if (Metin) px = drift + new Vector2(0f, 45f * (1f - (1f - t) * (1f - t))); // sobe a desaparecer
            if (style == 4 && age < 0.18f && !Metin)
            {
                // Terra: um baque seco ao aterrar, mesmo sem crítico.
                float thud = (1f - age / 0.18f) * 6f;
                px += new Vector2(Mathf.PerlinNoise(age * 50f, seed) - 0.5f, Mathf.PerlinNoise(seed, age * 50f) - 0.5f) * (2f * thud);
            }
            if (crit)
            {
                // Tremer: forte no impacto e nunca para de todo.
                float shake = Metin ? Mathf.Lerp(6f, 1.5f, Mathf.Clamp01(age / 0.3f))
                                    : Mathf.Lerp(9f, 3f, Mathf.Clamp01(age / 0.35f));
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
            block.SetFloat(BurnId, Metin ? Mathf.Clamp01((t - 0.45f) / 0.55f) : Mathf.Clamp01((t - 0.6f) / 0.4f));
            block.SetFloat(CritId, crit ? 1f : 0f);
            block.SetFloat(CapHalfId, capHalf);
            block.SetFloat(StyleId, style);
            block.SetFloat(FxId, fx);
            // O SDF vai de 0.5 (borda) a 0/1 ao longo do padding: isto em unidades de em. No bloco e não no
            // material, porque não está nas Properties do shader e um reimport apagava-o do material.
            block.SetFloat(SdfPerEmId, 0.5f / fa.atlasPadding * fa.faceInfo.pointSize);
            meshRenderer.SetPropertyBlock(block);
        }

        void OnDestroy()
        {
            if (mesh != null) Destroy(mesh);
        }
    }
}
