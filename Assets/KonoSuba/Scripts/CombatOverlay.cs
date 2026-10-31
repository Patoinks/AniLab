using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace AnimLab.KonoSuba
{
    /// <summary>
    /// Camada do HUD presa ao mundo: placa por cima de cada inimigo (nome, vida e os ícones dos estados,
    /// com o tempo que falta a contar à volta) e números de dano na cor do elemento. Quando um estado entra,
    /// o nome dele salta por cima da placa.
    /// Construída e atualizada pelo GameHud, no mesmo painel e com a mesma fonte.
    /// </summary>
    public class CombatOverlay
    {
        static readonly Color EnemyHealth = new Color(1f, 0.36f, 0.32f);
        const float MaxDistance = 40f;
        const float PopupLife = 1.4f;

        readonly GameHud hud;
        readonly VisualElement layer;
        readonly Dictionary<Combatant, Plate> plates = new Dictionary<Combatant, Plate>();
        readonly List<Number> numbers = new List<Number>();
        readonly List<Combatant> gone = new List<Combatant>();

        class Plate
        {
            public VisualElement root, icons;
            public GameHud.GlowBar bar;
            public Label name;
            public readonly Dictionary<StatusType, StatusIcon> shown = new Dictionary<StatusType, StatusIcon>();
            public readonly List<Popup> popups = new List<Popup>();
        }

        class Number
        {
            public Label label;
            public Vector3 world;
            public float born, size;
            public Vector2 drift;
        }

        class Popup
        {
            public Label label;
            public float born;
        }

        public CombatOverlay(GameHud hud, VisualElement root)
        {
            this.hud = hud;
            layer = new VisualElement { pickingMode = PickingMode.Ignore };
            layer.style.position = Position.Absolute;
            layer.style.left = layer.style.top = layer.style.right = layer.style.bottom = 0;
            root.Insert(0, layer); // por baixo das barras e da hotbar
            Combatant.AnyHit += OnHit;
            Combatant.AnyStatusApplied += OnStatus;
            Combat.AnyCallout += OnCallout;
        }

        public void Dispose()
        {
            Combatant.AnyHit -= OnHit;
            Combatant.AnyStatusApplied -= OnStatus;
            Combat.AnyCallout -= OnCallout;
            layer.RemoveFromHierarchy();
        }

        void OnHit(Combatant c, HitInfo hit)
        {
            if (hit.damage < 0.5f) return;
            // Fogo, água e eletricidade têm números próprios no mundo (letras em degradê com o elemento atrás).
            if (ElementDamageNumber.Spawn(c.NameplatePoint - Vector3.up * 0.3f, Mathf.RoundToInt(hit.damage),
                    hit.critical, hit.element)) return;
            // Crítico: maior e dourado.
            bool big = hit.damage >= 40f || hit.critical;
            string text = Mathf.RoundToInt(hit.damage).ToString();
            var l = hud.MakeLabel(text, hit.critical ? 34 : big ? 30 : 22);
            l.style.position = Position.Absolute;
            l.style.color = hit.critical ? new Color(1f, 0.82f, 0.35f)
                : Color.Lerp(Combat.ElementColor(hit.element), Color.white, 0.25f);
            l.style.unityFontStyleAndWeight = FontStyle.Bold;
            l.style.textShadow = new TextShadow { offset = new Vector2(0f, 1.5f), blurRadius = 4f, color = new Color(0f, 0f, 0f, 0.8f) };
            l.style.translate = new Translate(Length.Percent(-50), Length.Percent(-100));
            layer.Add(l);
            // Nasce ao lado da placa (fora da explosão e dos ícones) e sobe na diagonal para fora.
            float side = Random.value < 0.5f ? -1f : 1f;
            numbers.Add(new Number
            {
                label = l,
                world = c.NameplatePoint - Vector3.up * 0.3f,
                born = Time.time,
                size = big ? 1.25f : 1f,
                drift = new Vector2(side * Random.Range(78f, 100f), side * Random.Range(30f, 45f))
            });
        }

        // Callout: como um número de dano, mas centrado, espaçado e a subir a direito.
        void OnCallout(Vector3 point, string text, Color color, float size)
        {
            var l = hud.MakeLabel(text, Mathf.RoundToInt(size));
            l.style.position = Position.Absolute;
            l.style.color = color;
            l.style.unityFontStyleAndWeight = FontStyle.Bold;
            l.style.letterSpacing = size * 0.12f;
            l.style.textShadow = new TextShadow { offset = new Vector2(0f, 2f), blurRadius = 8f, color = new Color(color.r * 0.25f, color.g * 0.25f, color.b * 0.25f, 0.95f) };
            l.style.translate = new Translate(Length.Percent(-50), Length.Percent(-100));
            layer.Add(l);
            numbers.Add(new Number { label = l, world = point, born = Time.time, size = 1f, drift = Vector2.zero });
        }

        void OnStatus(Combatant c, StatusType type)
        {
            if (!plates.TryGetValue(c, out Plate p)) return;
            Color col = Combat.StatusColor(type);
            var l = hud.MakeLabel(Combat.StatusName(type).ToUpperInvariant(), 15);
            l.style.position = Position.Absolute;
            l.style.left = Length.Percent(50);
            l.style.top = 0;
            l.style.color = Color.Lerp(col, Color.white, 0.2f);
            l.style.unityFontStyleAndWeight = FontStyle.Bold;
            l.style.letterSpacing = 2f;
            l.style.textShadow = new TextShadow { offset = new Vector2(0f, 1f), blurRadius = 5f, color = new Color(col.r * 0.3f, col.g * 0.3f, col.b * 0.3f, 0.9f) };
            l.style.translate = new Translate(Length.Percent(-50), Length.Percent(-100));
            p.root.Add(l);
            p.popups.Add(new Popup { label = l, born = Time.time });
        }

        public void Update()
        {
            Camera cam = Camera.main;
            IPanel panel = layer.panel;
            if (cam == null || panel == null) return;

            foreach (Combatant c in Combatant.All)
                if (c.showNameplate && !plates.ContainsKey(c)) plates[c] = MakePlate(c);

            gone.Clear();
            foreach (var kv in plates)
            {
                Combatant c = kv.Key;
                Plate p = kv.Value;
                if (c == null || !c.isActiveAndEnabled) { p.root.RemoveFromHierarchy(); gone.Add(c); continue; }
                bool visible = Project(cam, panel, c.NameplatePoint, out Vector2 at)
                               && Vector3.Distance(cam.transform.position, c.transform.position) < MaxDistance;
                p.root.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
                if (!visible) continue;
                p.root.style.left = at.x;
                p.root.style.top = at.y;
                p.bar.SetValue(c.Health01);
                p.bar.MarkDirtyRepaint(); // brilho a pulsar
                UpdateIcons(c, p);
                UpdatePopups(p);
            }
            foreach (Combatant c in gone) plates.Remove(c);

            for (int i = numbers.Count - 1; i >= 0; i--)
            {
                Number n = numbers[i];
                float t = Time.time - n.born;
                if (t > 1f) { n.label.RemoveFromHierarchy(); numbers.RemoveAt(i); continue; }
                if (!Project(cam, panel, n.world, out Vector2 at)) { n.label.style.display = DisplayStyle.None; continue; }
                n.label.style.display = DisplayStyle.Flex;
                // Salta grande e assenta (0–0.12 s), sobe a abrandar, desaparece no último terço.
                float pop = t < 0.12f ? Mathf.Lerp(1.6f, 1f, t / 0.12f) : 1f;
                float rise = 1f - (1f - t) * (1f - t);
                n.label.style.left = at.x + n.drift.x + n.drift.y * rise;
                n.label.style.top = at.y - 60f * rise;
                n.label.style.scale = new Scale(Vector3.one * (pop * n.size));
                n.label.style.opacity = Mathf.Clamp01((1f - t) / 0.35f);
            }
        }

        void UpdateIcons(Combatant c, Plate p)
        {
            foreach (Combatant.ActiveStatus s in c.statuses)
            {
                if (!p.shown.TryGetValue(s.type, out StatusIcon icon))
                {
                    icon = new StatusIcon(s.type);
                    p.shown[s.type] = icon;
                    p.icons.Add(icon);
                }
                icon.Set(s);
            }
            // Ícones de estados que acabaram.
            List<StatusType> ended = null;
            foreach (var kv in p.shown)
                if (!c.Has(kv.Key)) (ended ??= new List<StatusType>()).Add(kv.Key);
            if (ended != null)
                foreach (StatusType t in ended) { p.shown[t].RemoveFromHierarchy(); p.shown.Remove(t); }
        }

        void UpdatePopups(Plate p)
        {
            for (int i = p.popups.Count - 1; i >= 0; i--)
            {
                Popup u = p.popups[i];
                float t = Time.time - u.born;
                if (t > PopupLife) { u.label.RemoveFromHierarchy(); p.popups.RemoveAt(i); continue; }
                // Salta (escala 1.5 → 1), sobe devagar e desvanece no fim; os mais novos empurram os outros para cima.
                int above = p.popups.Count - 1 - i;
                float k = Mathf.Clamp01(t / PopupLife);
                float pop = t < 0.15f ? Mathf.Lerp(1.5f, 1f, t / 0.15f) : 1f;
                u.label.style.top = -4f - 18f * above - 20f * (1f - (1f - k) * (1f - k));
                u.label.style.scale = new Scale(Vector3.one * pop);
                u.label.style.opacity = Mathf.Clamp01((PopupLife - t) / 0.4f) * Mathf.Clamp01(t / 0.05f);
            }
        }

        static bool Project(Camera cam, IPanel panel, Vector3 world, out Vector2 at)
        {
            at = default;
            // Pelo viewport (e não pelo Screen): também certo quando a câmara ou o painel desenham para uma textura.
            Vector3 v = cam.WorldToViewportPoint(world);
            if (v.z <= 0.1f) return false;
            Rect r = panel.visualTree.layout;
            at = new Vector2(v.x * r.width, (1f - v.y) * r.height);
            return true;
        }

        Plate MakePlate(Combatant c)
        {
            var p = new Plate();
            p.root = new VisualElement { pickingMode = PickingMode.Ignore };
            p.root.style.position = Position.Absolute;
            p.root.style.alignItems = Align.Center;
            p.root.style.width = 140;
            // A âncora é o fundo da placa, ao centro.
            p.root.style.translate = new Translate(Length.Percent(-50), Length.Percent(-100));

            p.name = hud.MakeLabel(c.DisplayName, 13);
            p.name.style.letterSpacing = 1.2f;
            p.name.style.opacity = 0.9f;
            p.root.Add(p.name);

            p.bar = new GameHud.GlowBar(EnemyHealth, 2.2f, Random.Range(0f, 50f));
            p.bar.style.width = 120;
            p.bar.style.height = 14;
            p.bar.style.marginTop = 1;
            p.root.Add(p.bar);

            // Estados por baixo da barra; a linha tem sempre a altura de um ícone, para a placa não saltar.
            p.icons = new VisualElement { pickingMode = PickingMode.Ignore };
            p.icons.style.flexDirection = FlexDirection.Row;
            p.icons.style.justifyContent = Justify.Center;
            p.icons.style.height = StatusIcon.Size;
            p.icons.style.marginTop = 2;
            p.root.Add(p.icons);

            layer.Add(p.root);
            return p;
        }

        /// <summary>
        /// Ícone de um estado: disco escuro, anel com o tempo que falta (a esvaziar no sentido dos ponteiros)
        /// e o símbolo do estado em vetor. Entra a saltar, pisca ao ser renovado e no último segundo e meio.
        /// </summary>
        class StatusIcon : VisualElement
        {
            public const float Size = 22f;
            readonly StatusType type;
            readonly Color color;
            readonly float born;
            float remaining01 = 1f, remaining, refreshedAt = -9f, lastDuration;

            public StatusIcon(StatusType type)
            {
                this.type = type;
                color = Combat.StatusColor(type);
                born = Time.time;
                pickingMode = PickingMode.Ignore;
                style.width = style.height = Size;
                style.marginLeft = style.marginRight = 1.5f;
                generateVisualContent += Draw;
            }

            public void Set(Combatant.ActiveStatus s)
            {
                if (s.remaining > remaining + 0.05f && Time.time - born > 0.1f) refreshedAt = Time.time;
                remaining = s.remaining;
                remaining01 = s.Remaining01;
                lastDuration = s.duration;
                float t = Time.time - born;
                float pop = t < 0.15f ? Mathf.Lerp(1.6f, 1f, t / 0.15f) : 1f;
                float flash = Time.time - refreshedAt < 0.2f ? 1.25f : 1f;
                style.scale = new Scale(Vector3.one * pop * flash);
                style.opacity = remaining < 1.5f ? 0.55f + 0.45f * Mathf.Abs(Mathf.Cos(Time.time * 7f)) : 1f;
                MarkDirtyRepaint();
            }

            void Draw(MeshGenerationContext ctx)
            {
                var p = ctx.painter2D;
                var c = new Vector2(Size * 0.5f, Size * 0.5f);
                float r = Size * 0.5f - 1.5f;
                p.fillColor = new Color(color.r, color.g, color.b, 0.12f);
                p.BeginPath(); p.Arc(c, r + 1.5f, 0, 360); p.Fill();
                p.fillColor = new Color(0.02f, 0.03f, 0.05f, 0.78f);
                p.BeginPath(); p.Arc(c, r, 0, 360); p.Fill();

                // Anel do tempo: trilho ténue e o que falta, a partir do topo.
                p.lineWidth = 1.6f;
                p.strokeColor = new Color(color.r, color.g, color.b, 0.22f);
                p.BeginPath(); p.Arc(c, r - 0.8f, 0, 360); p.Stroke();
                if (remaining01 > 0.001f)
                {
                    p.strokeColor = color;
                    p.lineCap = LineCap.Round;
                    p.BeginPath(); p.Arc(c, r - 0.8f, -90f, -90f + 360f * remaining01); p.Stroke();
                }

                Glyph(p, type, c, 0.78f, Color.Lerp(color, Color.white, 0.35f));
            }
        }

        /// <summary>Símbolo de cada estado, desenhado numa caixa de ±7 unidades à volta de c.</summary>
        internal static void Glyph(Painter2D p, StatusType type, Vector2 c, float s, Color col)
        {
            Vector2 P(float x, float y) => c + new Vector2(x, y) * s;
            p.fillColor = col;
            p.strokeColor = col;
            p.lineWidth = 1.5f;
            p.lineCap = LineCap.Round;
            p.lineJoin = LineJoin.Round;
            switch (type)
            {
                case StatusType.Wet: // gota
                    p.BeginPath();
                    p.MoveTo(P(0f, -6.5f));
                    p.BezierCurveTo(P(2.2f, -3f), P(4.4f, -0.6f), P(4.4f, 2f));
                    p.Arc(P(0f, 2f), 4.4f * s, 0f, 180f);
                    p.BezierCurveTo(P(-4.4f, -0.6f), P(-2.2f, -3f), P(0f, -6.5f));
                    p.ClosePath(); p.Fill();
                    break;
                case StatusType.Static: // raio
                    p.BeginPath();
                    p.MoveTo(P(1.8f, -7f)); p.LineTo(P(-3.8f, 0.8f)); p.LineTo(P(-0.3f, 0.8f));
                    p.LineTo(P(-1.8f, 7f)); p.LineTo(P(3.8f, -1f)); p.LineTo(P(0.3f, -1f));
                    p.ClosePath(); p.Fill();
                    break;
                case StatusType.Burning: // chama com língua interior
                    p.BeginPath();
                    p.MoveTo(P(0.5f, -7f));
                    p.BezierCurveTo(P(3.5f, -3.5f), P(5f, -0.5f), P(4.6f, 2.2f));
                    p.Arc(P(0f, 2.2f), 4.6f * s, 0f, 180f);
                    p.BezierCurveTo(P(-4.8f, -0.5f), P(-3.2f, -2.4f), P(-2f, -4f));
                    p.BezierCurveTo(P(-1.6f, -2.2f), P(-0.6f, -1.6f), P(0f, -2f));
                    p.BezierCurveTo(P(1f, -3.5f), P(1f, -5.5f), P(0.5f, -7f));
                    p.ClosePath(); p.Fill();
                    p.fillColor = new Color(0.02f, 0.03f, 0.05f, 0.7f);
                    p.BeginPath();
                    p.MoveTo(P(0f, -0.5f));
                    p.BezierCurveTo(P(1.6f, 1f), P(2.2f, 2.4f), P(1.9f, 3.4f));
                    p.Arc(P(0f, 3.4f), 1.9f * s, 0f, 180f);
                    p.BezierCurveTo(P(-2f, 2.2f), P(-1f, 1f), P(0f, -0.5f));
                    p.ClosePath(); p.Fill();
                    break;
                case StatusType.Frozen: // floco de neve
                    for (int i = 0; i < 3; i++)
                    {
                        float a = (90f + i * 60f) * Mathf.Deg2Rad;
                        var d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                        var n = new Vector2(-d.y, d.x);
                        p.lineWidth = 1.2f;
                        p.BeginPath(); p.MoveTo(c - d * 7f * s); p.LineTo(c + d * 7f * s); p.Stroke();
                        foreach (float side in new[] { -1f, 1f })
                        {
                            Vector2 b = c + d * side * 5f * s;
                            p.BeginPath();
                            p.MoveTo(b + (n * 1.6f - d * side * 1.6f) * s); p.LineTo(b); p.LineTo(b + (-n * 1.6f - d * side * 1.6f) * s);
                            p.Stroke();
                        }
                    }
                    break;
                case StatusType.Grounded: // seta para o chão
                    p.BeginPath(); p.MoveTo(P(0f, -6.5f)); p.LineTo(P(0f, 2f)); p.Stroke();
                    p.BeginPath(); p.MoveTo(P(-3.5f, -1.5f)); p.LineTo(P(0f, 2f)); p.LineTo(P(3.5f, -1.5f)); p.Stroke();
                    p.lineWidth = 2f;
                    p.BeginPath(); p.MoveTo(P(-5.5f, 5f)); p.LineTo(P(5.5f, 5f)); p.Stroke();
                    break;
                case StatusType.Asphyxia: // rajadas de vento
                    p.BeginPath(); p.MoveTo(P(-5.5f, -2.5f)); p.LineTo(P(2f, -2.5f));
                    p.BezierCurveTo(P(5f, -2.5f), P(5f, -6.2f), P(2.4f, -6f)); p.Stroke();
                    p.BeginPath(); p.MoveTo(P(-5.5f, 1f)); p.LineTo(P(4.5f, 1f)); p.Stroke();
                    p.BeginPath(); p.MoveTo(P(-4f, 4.4f)); p.LineTo(P(0.5f, 4.4f));
                    p.BezierCurveTo(P(3.2f, 4.4f), P(3.2f, 7f), P(1f, 6.8f)); p.Stroke();
                    break;
                case StatusType.Blind: // olho riscado
                    p.BeginPath(); p.MoveTo(P(-6f, 0f));
                    p.BezierCurveTo(P(-3f, -4.8f), P(3f, -4.8f), P(6f, 0f));
                    p.BezierCurveTo(P(3f, 4.8f), P(-3f, 4.8f), P(-6f, 0f));
                    p.Stroke();
                    p.BeginPath(); p.Arc(c, 1.7f * s, 0f, 360f); p.Fill();
                    p.BeginPath(); p.MoveTo(P(-5f, 5.5f)); p.LineTo(P(5f, -5.5f)); p.Stroke();
                    break;
            }
        }
    }
}
