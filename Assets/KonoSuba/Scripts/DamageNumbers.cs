using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace AnimLab.KonoSuba
{
    /// <summary>
    /// Números de dano flutuantes (estilo Dragon Nest): o número aparece grande por cima do alvo, encolhe
    /// num "soco", sobe devagar e desvanece. Os críticos são maiores e dourados.
    /// Desenhado no painel do HUD (UI Toolkit); cria-se sozinho na primeira chamada a Show.
    /// </summary>
    public class DamageNumbers : MonoBehaviour
    {
        class Popup
        {
            public Label label;
            public Vector3 world;
            public Vector2 drift;
            public float age, scale;
            public bool crit;
        }

        public float lifetime = 1.1f;
        [Tooltip("Subida total no ecrã (px) durante a vida do número.")]
        public float rise = 70f;
        public float fontSize = 34f;

        static readonly Color NormalColor = new Color(0.93f, 0.93f, 0.95f);
        static readonly Color CritColor = new Color(1f, 0.78f, 0.25f);
        static readonly Color Outline = new Color(0.08f, 0.06f, 0.06f, 0.95f);

        static DamageNumbers instance;
        readonly List<Popup> popups = new List<Popup>();
        VisualElement layer;
        FontDefinition font;
        bool hasFont;

        /// <summary>Mostra um número de dano por cima de um ponto do mundo. Fogo usa números a arder.</summary>
        public static void Show(Vector3 worldPos, int amount, bool crit = false,
            AbilityElement element = AbilityElement.Neutral)
        {
            if (element == AbilityElement.Fire && FireDamageNumber.Spawn(worldPos, amount, crit)) return;
            if (instance == null) instance = new GameObject("DamageNumbers").AddComponent<DamageNumbers>();
            instance.Spawn(worldPos, amount, crit);
        }

        void OnDestroy() { if (instance == this) instance = null; }

        bool EnsureLayer()
        {
            if (layer != null && layer.panel != null) return true;
            var hud = FindFirstObjectByType<GameHud>();
            UIDocument doc = hud != null ? hud.GetComponent<UIDocument>() : FindFirstObjectByType<UIDocument>();
            if (doc == null || doc.rootVisualElement == null) return false;

            if (!hasFont) LoadFont();
            layer = new VisualElement { pickingMode = PickingMode.Ignore };
            layer.style.position = Position.Absolute;
            layer.style.left = layer.style.top = layer.style.right = layer.style.bottom = 0;
            doc.rootVisualElement.Add(layer);
            return true;
        }

        // Algarismos grossos e condensados, como os do Dragon Nest; Arial Black/Bold se não houver Impact.
        void LoadFont()
        {
            string fonts = System.Environment.GetFolderPath(System.Environment.SpecialFolder.Fonts);
            foreach (string file in new[] { "impact.ttf", "ariblk.ttf", "arialbd.ttf" })
            {
                string path = System.IO.Path.Combine(fonts, file);
                if (!System.IO.File.Exists(path)) continue;
                var asset = UnityEngine.TextCore.Text.FontAsset.CreateFontAsset(path, 0, 90, 10,
                    UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA, 1024, 1024);
                if (asset == null) continue;
                font = FontDefinition.FromSDFFont(asset);
                hasFont = true;
                return;
            }
        }

        void Spawn(Vector3 worldPos, int amount, bool crit)
        {
            if (!EnsureLayer()) return;
            layer.BringToFront(); // por cima do resto do HUD (p.ex. a moldura do alvo)
            var label = new Label(amount + (crit ? "!" : "")) { pickingMode = PickingMode.Ignore };
            label.style.position = Position.Absolute;
            label.style.fontSize = fontSize * (crit ? 1.45f : 1f);
            label.style.color = crit ? CritColor : NormalColor;
            label.style.unityTextOutlineColor = Outline;
            label.style.unityTextOutlineWidth = 0.22f;
            label.style.unityTextAlign = TextAnchor.MiddleCenter;
            label.style.translate = new Translate(Length.Percent(-50), Length.Percent(-50));
            if (hasFont) label.style.unityFontDefinition = font;
            layer.Add(label);

            // Espalhar um pouco para golpes seguidos não ficarem uns por cima dos outros.
            popups.Add(new Popup
            {
                label = label,
                world = worldPos,
                drift = new Vector2(Random.Range(-28f, 28f), Random.Range(-10f, 6f)),
                scale = crit ? 2.4f : 1.9f,
                crit = crit
            });
            Place(popups[popups.Count - 1]);
        }

        void LateUpdate()
        {
            for (int i = popups.Count - 1; i >= 0; i--)
            {
                Popup p = popups[i];
                p.age += Time.deltaTime;
                if (p.age >= lifetime || p.label.panel == null)
                {
                    p.label.RemoveFromHierarchy();
                    popups.RemoveAt(i);
                    continue;
                }
                Place(p);
            }
        }

        void Place(Popup p)
        {
            Camera cam = Camera.main;
            if (cam == null) return;
            Vector3 screen = cam.WorldToScreenPoint(p.world);
            p.label.visible = screen.z > 0f;
            if (screen.z <= 0f) return;

            float t = p.age / lifetime;
            // Soco: entra grande e assenta em ~0.12 s; os críticos dão um segundo pulo pequeno.
            float pop = Mathf.Clamp01(p.age / 0.12f);
            float s = Mathf.Lerp(p.scale, 1f, 1f - (1f - pop) * (1f - pop));
            if (p.crit && p.age > 0.12f && p.age < 0.3f) s += Mathf.Sin((p.age - 0.12f) / 0.18f * Mathf.PI) * 0.12f;
            // Sobe depressa ao início e abranda; desvanece no último terço.
            float up = rise * (1f - (1f - t) * (1f - t));
            float alpha = t < 0.65f ? 1f : 1f - (t - 0.65f) / 0.35f;

            Vector2 panelPos = RuntimePanelUtils.ScreenToPanel(p.label.panel,
                new Vector2(screen.x, Screen.height - screen.y));
            p.label.style.left = panelPos.x + p.drift.x;
            p.label.style.top = panelPos.y + p.drift.y - up;
            p.label.style.scale = new Scale(new Vector3(s, s, 1f));
            p.label.style.opacity = alpha;
        }
    }
}
