using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace AnimLab.KonoSuba
{
    /// <summary>
    /// Janela das árvores de habilidades (tecla V), na estética do HUD (como a StatsWindow).
    ///   Sem árvores escolhidas: os 7 elementos em cartões; escolhem-se dois e vê-se logo a barra de mana
    ///   que essa combinação dá (degradê entre as duas cores). "Confirm" fixa a escolha.
    ///   Com árvores: as duas lado a lado, cada uma em V (base → dois ramos → final), ligadas por linhas.
    ///   Clicar num nó que se pode aprender gasta um skill point. Nós por fazer aparecem como "SOON".
    ///   O rodapé explica o nó por baixo do cursor.
    /// Construída e atualizada pelo GameHud.
    /// </summary>
    public class SkillTreeWindow
    {
        static readonly Color Gold = new Color(0.86f, 0.70f, 0.40f);
        static readonly Color Dim = new Color(0.72f, 0.75f, 0.82f);
        static readonly Color Box = new Color(0.01f, 0.015f, 0.03f, 0.55f);
        static readonly Color Line = new Color(0.80f, 0.83f, 0.90f, 0.22f);
        const float Width = 640f;
        const string Hint = "Hover a skill to see what it does.   [V] close";

        readonly GameHud hud;
        readonly VisualElement window, chooser, treesView;
        readonly List<AbilityElement> picked = new List<AbilityElement>();
        readonly List<VisualElement> repaint = new List<VisualElement>();
        readonly Dictionary<AbilityElement, VisualElement> cards = new Dictionary<AbilityElement, VisualElement>();
        CharacterStats stats;
        GameHud.GlowBar preview;
        Label footer, pointsLabel, confirmLabel;
        VisualElement confirm;
        AbilityElement builtA, builtB;
        bool open;
        float openedAt;
        Vector2 dragFrom, posFrom;
        bool dragging;

        public bool IsOpen => open;

        public SkillTreeWindow(GameHud hud, VisualElement root)
        {
            this.hud = hud;
            window = new VisualElement();
            window.style.position = Position.Absolute;
            window.style.left = 440;
            window.style.top = 110;
            window.style.width = Width;
            window.style.backgroundColor = new Color(0.025f, 0.035f, 0.06f, 0.92f);
            SetBorder(window, 1f, Line);
            window.style.display = DisplayStyle.None;
            window.generateVisualContent += DrawCorners;
            root.Add(window);

            BuildTitle();
            chooser = new VisualElement();
            window.Add(chooser);
            treesView = new VisualElement();
            window.Add(treesView);
            BuildChooser();

            footer = hud.MakeLabel(Hint, 11);
            footer.style.color = Dim;
            footer.style.whiteSpace = WhiteSpace.Normal;
            footer.style.minHeight = 34;
            footer.style.marginTop = 6;
            footer.style.paddingLeft = footer.style.paddingRight = 12;
            footer.style.paddingTop = 6;
            footer.style.paddingBottom = 8;
            footer.style.borderTopWidth = 1;
            footer.style.borderTopColor = Line;
            window.Add(footer);
        }

        CharacterStats Stats
        {
            get
            {
                if (stats == null) stats = Object.FindFirstObjectByType<CharacterStats>();
                return stats;
            }
        }

        /// <summary>Marca dois elementos no ecrã de escolha (gravações), sem confirmar.</summary>
        public void Preview(AbilityElement a, AbilityElement b)
        {
            picked.Clear();
            picked.Add(a);
            picked.Add(b);
            RefreshChooser();
        }

        public void SetOpen(bool value)
        {
            open = value;
            openedAt = Time.unscaledTime;
            window.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
            if (open) Rebuild();
        }

        public void Update()
        {
            if (GameInput.GetDown(GameAction.SkillTree)) SetOpen(!open);
            if (open && Input.GetKeyDown(KeyCode.Escape)) SetOpen(false);
            if (!open) return;
            float t = Mathf.Clamp01((Time.unscaledTime - openedAt) / 0.14f);
            window.style.opacity = t;
            window.style.translate = new Translate(0, (1f - t) * 8f);
            if (Stats != null && (Stats.HasTrees != (treesView.style.display == DisplayStyle.Flex)
                                  || Stats.treeA != builtA || Stats.treeB != builtB)) Rebuild();
            if (pointsLabel != null && Stats != null) pointsLabel.text = Stats.skillPoints.ToString();
            foreach (VisualElement e in repaint) e.MarkDirtyRepaint(); // brilhos a pulsar
        }

        void Rebuild()
        {
            if (Stats == null) return;
            bool trees = Stats.HasTrees;
            chooser.style.display = trees ? DisplayStyle.None : DisplayStyle.Flex;
            treesView.style.display = trees ? DisplayStyle.Flex : DisplayStyle.None;
            if (trees) BuildTrees();
            else RefreshChooser();
        }

        // ------------------------------------------------------------------ Título

        void BuildTitle()
        {
            var bar = new VisualElement();
            bar.style.height = 30;
            bar.style.alignItems = Align.Center;
            bar.style.justifyContent = Justify.Center;
            bar.style.borderBottomWidth = 1;
            bar.style.borderBottomColor = Line;
            window.Add(bar);
            var title = hud.MakeLabel("SKILL TREES", 14);
            title.style.letterSpacing = 3f;
            title.style.color = Gold;
            bar.Add(title);

            bar.RegisterCallback<PointerDownEvent>(e =>
            {
                dragging = true;
                dragFrom = e.position;
                posFrom = new Vector2(window.resolvedStyle.left, window.resolvedStyle.top);
                bar.CapturePointer(e.pointerId);
            });
            bar.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (!dragging) return;
                Vector2 d = (Vector2)e.position - dragFrom;
                window.style.left = posFrom.x + d.x;
                window.style.top = posFrom.y + d.y;
            });
            bar.RegisterCallback<PointerUpEvent>(e => { dragging = false; bar.ReleasePointer(e.pointerId); });

            var close = new VisualElement();
            close.style.position = Position.Absolute;
            close.style.right = 6;
            close.style.top = 5;
            close.style.width = close.style.height = 20;
            SetBorder(close, 1f, Line);
            close.generateVisualContent += ctx =>
            {
                var p = ctx.painter2D;
                p.strokeColor = Dim; p.lineWidth = 1.4f; p.lineCap = LineCap.Round;
                p.BeginPath(); p.MoveTo(new Vector2(6, 6)); p.LineTo(new Vector2(14, 14)); p.Stroke();
                p.BeginPath(); p.MoveTo(new Vector2(14, 6)); p.LineTo(new Vector2(6, 14)); p.Stroke();
            };
            Hover(close, new Color(0.9f, 0.3f, 0.25f, 0.8f));
            close.RegisterCallback<ClickEvent>(_ => SetOpen(false));
            bar.Add(close);
        }

        // ------------------------------------------------------------------ Escolha dos dois elementos

        void BuildChooser()
        {
            chooser.style.paddingLeft = chooser.style.paddingRight = 14;
            chooser.style.paddingTop = 12;

            var intro = hud.MakeLabel("CHOOSE TWO ELEMENTS", 12);
            intro.style.letterSpacing = 2f;
            intro.style.color = Gold;
            intro.style.alignSelf = Align.Center;
            chooser.Add(intro);

            var grid = new VisualElement();
            grid.style.flexDirection = FlexDirection.Row;
            grid.style.flexWrap = Wrap.Wrap;
            grid.style.justifyContent = Justify.Center;
            grid.style.marginTop = 10;
            chooser.Add(grid);
            foreach (AbilityElement e in Combat.Elements) grid.Add(Card(e));

            // Pré-visualização da mana com a combinação escolhida.
            var manaRow = new VisualElement();
            manaRow.style.flexDirection = FlexDirection.Row;
            manaRow.style.alignItems = Align.Center;
            manaRow.style.alignSelf = Align.Center;
            manaRow.style.marginTop = 12;
            chooser.Add(manaRow);
            var manaTag = hud.MakeLabel("MANA", 11);
            manaTag.style.color = Dim;
            manaTag.style.letterSpacing = 1.5f;
            manaRow.Add(manaTag);
            preview = new GameHud.GlowBar(new Color(0.36f, 0.64f, 1f), 2.6f, 7.7f);
            preview.style.width = 260;
            preview.style.height = 22;
            preview.style.marginLeft = 10;
            preview.SetValue(1f);
            manaRow.Add(preview);
            repaint.Add(preview);

            confirm = new VisualElement();
            confirm.style.alignSelf = Align.Center;
            confirm.style.marginTop = 12;
            confirm.style.marginBottom = 4;
            confirm.style.width = 160;
            confirm.style.height = 28;
            confirm.style.alignItems = Align.Center;
            confirm.style.justifyContent = Justify.Center;
            confirm.style.backgroundColor = new Color(0.14f, 0.11f, 0.06f, 0.7f);
            SetBorder(confirm, 1f, Gold);
            confirmLabel = hud.MakeLabel("CONFIRM", 12);
            confirmLabel.style.letterSpacing = 2f;
            confirmLabel.style.color = Gold;
            confirm.Add(confirmLabel);
            Hover(confirm, Color.white, Gold);
            confirm.RegisterCallback<ClickEvent>(_ =>
            {
                if (picked.Count == 2 && Stats != null) Stats.ChooseTrees(picked[0], picked[1]);
            });
            chooser.Add(confirm);
        }

        VisualElement Card(AbilityElement e)
        {
            Color c = Combat.ElementColor(e);
            var card = new VisualElement();
            card.style.width = 112;
            card.style.height = 132;
            card.style.marginLeft = card.style.marginRight = 5;
            card.style.marginBottom = 10;
            card.style.alignItems = Align.Center;
            card.style.paddingTop = 10;
            card.style.backgroundColor = Box;
            SetBorder(card, 1f, Line);

            var emblem = new VisualElement { pickingMode = PickingMode.Ignore };
            emblem.style.width = emblem.style.height = 46;
            emblem.generateVisualContent += ctx => DrawEmblem(ctx.painter2D, new Vector2(23, 23), 20f, e, picked.Contains(e) ? 1f : 0.55f);
            card.Add(emblem);
            repaint.Add(emblem);

            var name = hud.MakeLabel(SkillTrees.ElementName(e).ToUpperInvariant(), 12);
            name.style.letterSpacing = 1.5f;
            name.style.color = Color.Lerp(c, Color.white, 0.25f);
            name.style.marginTop = 6;
            card.Add(name);
            var blurb = hud.MakeLabel(SkillTrees.ElementBlurb(e), 10);
            blurb.style.color = Dim;
            blurb.style.whiteSpace = WhiteSpace.Normal;
            blurb.style.unityTextAlign = TextAnchor.UpperCenter;
            blurb.style.paddingLeft = blurb.style.paddingRight = 6;
            blurb.style.marginTop = 3;
            card.Add(blurb);

            card.RegisterCallback<ClickEvent>(_ =>
            {
                if (picked.Contains(e)) picked.Remove(e);
                else
                {
                    if (picked.Count == 2) picked.RemoveAt(0);
                    picked.Add(e);
                }
                RefreshChooser();
            });
            card.RegisterCallback<PointerEnterEvent>(_ =>
            {
                footer.text = $"{SkillTrees.ElementName(e)} — {SkillTrees.ElementBlurb(e)}";
                if (!picked.Contains(e)) SetBorder(card, 1f, new Color(c.r, c.g, c.b, 0.6f));
            });
            card.RegisterCallback<PointerLeaveEvent>(_ => { footer.text = Hint; RefreshChooser(); });
            cards[e] = card;
            return card;
        }

        void RefreshChooser()
        {
            foreach (var kv in cards)
            {
                bool on = picked.Contains(kv.Key);
                Color c = Combat.ElementColor(kv.Key);
                SetBorder(kv.Value, on ? 1.5f : 1f, on ? c : Line);
                kv.Value.style.backgroundColor = on ? new Color(c.r * 0.12f, c.g * 0.12f, c.b * 0.12f, 0.75f) : Box;
            }
            Color a = picked.Count > 0 ? Combat.ElementColor(picked[0]) : new Color(0.36f, 0.64f, 1f);
            Color b = picked.Count > 1 ? Combat.ElementColor(picked[1]) : a;
            preview.SetColors(a, b);
            bool ready = picked.Count == 2;
            confirm.SetEnabled(ready);
            confirm.style.opacity = ready ? 1f : 0.35f;
            confirmLabel.text = ready ? $"CONFIRM  {SkillTrees.ElementName(picked[0]).ToUpperInvariant()} + {SkillTrees.ElementName(picked[1]).ToUpperInvariant()}" : "CONFIRM";
            confirm.style.width = ready ? 300 : 160;
        }

        // ------------------------------------------------------------------ As duas árvores

        void BuildTrees()
        {
            treesView.Clear();
            repaint.RemoveAll(e => e.panel == null);
            builtA = Stats.treeA;
            builtB = Stats.treeB;

            var top = new VisualElement();
            top.style.flexDirection = FlexDirection.Row;
            top.style.alignItems = Align.Center;
            top.style.paddingLeft = top.style.paddingRight = 14;
            top.style.paddingTop = 10;
            treesView.Add(top);

            var mana = new GameHud.GlowBar(Combat.ElementColor(builtA), 2.4f, 3.3f);
            mana.SetColors(Combat.ElementColor(builtA), Combat.ElementColor(builtB));
            mana.SetValue(1f);
            mana.style.width = 200;
            mana.style.height = 20;
            top.Add(mana);
            repaint.Add(mana);

            var spacer = new VisualElement { pickingMode = PickingMode.Ignore };
            spacer.style.flexGrow = 1;
            top.Add(spacer);
            var tag = hud.MakeLabel("SKILL POINTS", 11);
            tag.style.letterSpacing = 1.5f;
            tag.style.color = Dim;
            top.Add(tag);
            pointsLabel = hud.MakeLabel(Stats.skillPoints.ToString(), 15);
            pointsLabel.style.color = Gold;
            pointsLabel.style.marginLeft = 8;
            pointsLabel.style.minWidth = 18;
            top.Add(pointsLabel);

            var change = new VisualElement();
            change.style.marginLeft = 12;
            change.style.paddingLeft = change.style.paddingRight = 8;
            change.style.height = 22;
            change.style.justifyContent = Justify.Center;
            SetBorder(change, 1f, Line);
            var changeLabel = hud.MakeLabel("CHANGE", 10);
            changeLabel.style.letterSpacing = 1.5f;
            changeLabel.style.color = Dim;
            change.Add(changeLabel);
            Hover(change, Gold);
            change.RegisterCallback<ClickEvent>(_ => { picked.Clear(); Stats.ResetTrees(); });
            change.RegisterCallback<PointerEnterEvent>(_ => footer.text = "Choose two different elements. Learned skills are refunded.");
            change.RegisterCallback<PointerLeaveEvent>(_ => footer.text = Hint);
            top.Add(change);

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.paddingLeft = row.style.paddingRight = 10;
            row.style.marginTop = 10;
            treesView.Add(row);
            row.Add(Tree(builtA));
            var divider = new VisualElement { pickingMode = PickingMode.Ignore };
            divider.style.width = 1;
            divider.style.marginLeft = divider.style.marginRight = 8;
            divider.style.backgroundColor = Line;
            row.Add(divider);
            row.Add(Tree(builtB));
        }

        const float TreeW = 296f, TreeH = 330f, Node = 54f;

        // Posição de cada nó, de cima para baixo como numa árvore de talentos: base ao centro, os dois ramos
        // abrem para os lados, o final volta ao centro.
        static Vector2 NodePos(SkillNode n) => n.tier switch
        {
            1 => new Vector2(TreeW * 0.5f, 92f),
            2 => new Vector2(TreeW * (n.branch == 0 ? 0.24f : 0.76f), 182f),
            _ => new Vector2(TreeW * 0.5f, 272f),
        };

        VisualElement Tree(AbilityElement e)
        {
            Color c = Combat.ElementColor(e);
            var tree = new VisualElement();
            tree.style.width = TreeW;
            tree.style.height = TreeH;

            // Cabeçalho: emblema e nome do elemento.
            var head = new VisualElement();
            head.style.flexDirection = FlexDirection.Row;
            head.style.alignItems = Align.Center;
            head.style.justifyContent = Justify.Center;
            head.style.height = 44;
            var emblem = new VisualElement { pickingMode = PickingMode.Ignore };
            emblem.style.width = emblem.style.height = 36;
            emblem.generateVisualContent += ctx => DrawEmblem(ctx.painter2D, new Vector2(18, 18), 15f, e, 1f);
            head.Add(emblem);
            repaint.Add(emblem);
            var name = hud.MakeLabel(SkillTrees.ElementName(e).ToUpperInvariant(), 14);
            name.style.letterSpacing = 2.5f;
            name.style.color = Color.Lerp(c, Color.white, 0.25f);
            name.style.marginLeft = 8;
            head.Add(name);
            tree.Add(head);

            SkillNode[] nodes = SkillTrees.Of(e);

            // Ligações (por trás dos nós): acesas quando o nó de cima está aprendido.
            var links = new VisualElement { pickingMode = PickingMode.Ignore };
            links.style.position = Position.Absolute;
            links.style.left = links.style.top = 0;
            links.style.width = TreeW;
            links.style.height = TreeH;
            links.generateVisualContent += ctx =>
            {
                var p = ctx.painter2D;
                p.lineCap = LineCap.Round;
                foreach (SkillNode a in nodes)
                foreach (SkillNode b in nodes)
                {
                    if (b.tier != a.tier + 1) continue;
                    bool lit = Stats != null && Stats.IsLearned(a);
                    Vector2 pa = NodePos(a), pb = NodePos(b);
                    Vector2 dir = (pb - pa).normalized;
                    pa += dir * (Node * 0.5f + 2f);
                    pb -= dir * (Node * 0.5f + 2f);
                    if (lit)
                    {
                        float k = 0.8f + 0.2f * Mathf.Sin(Time.time * 2f);
                        p.strokeColor = new Color(c.r, c.g, c.b, 0.18f * k);
                        p.lineWidth = 7f;
                        p.BeginPath(); p.MoveTo(pa); p.LineTo(pb); p.Stroke();
                    }
                    p.strokeColor = lit ? c : new Color(1f, 1f, 1f, 0.16f);
                    p.lineWidth = lit ? 2f : 1.2f;
                    p.BeginPath(); p.MoveTo(pa); p.LineTo(pb); p.Stroke();
                }
            };
            tree.Add(links);
            repaint.Add(links);

            foreach (SkillNode n in nodes) tree.Add(NodeElement(e, n));
            return tree;
        }

        VisualElement NodeElement(AbilityElement tree, SkillNode n)
        {
            Color c = Combat.ElementColor(tree);
            Vector2 at = NodePos(n);
            var holder = new VisualElement();
            holder.style.position = Position.Absolute;
            holder.style.left = at.x - 60f;
            holder.style.top = at.y - Node * 0.5f;
            holder.style.width = 120;
            holder.style.alignItems = Align.Center;
            // Nomes fora das linhas: a base tem o nome por cima, os ramos puxam-no para fora.
            if (n.tier == 1)
            {
                holder.style.flexDirection = FlexDirection.ColumnReverse;
                holder.style.top = at.y - Node * 0.5f - 17f;
            }

            var node = new VisualElement();
            node.style.width = node.style.height = Node;
            node.generateVisualContent += ctx =>
            {
                var p = ctx.painter2D;
                var m = new Vector2(Node * 0.5f, Node * 0.5f);
                bool learned = Stats != null && Stats.IsLearned(n);
                bool can = Stats != null && Stats.CanLearn(tree, n);
                float k = 0.75f + 0.25f * Mathf.Sin(Time.time * 4f + at.x);
                float r = Node * 0.5f - 3f;

                // Losango: aprendido = cheio e a brilhar; pode aprender = contorno a pulsar; resto apagado.
                if (learned)
                {
                    p.fillColor = new Color(c.r, c.g, c.b, 0.1f * k);
                    Diamond(p, m, r + 9f); p.Fill();
                    p.fillColor = new Color(c.r, c.g, c.b, 0.22f * k);
                    Diamond(p, m, r + 4f); p.Fill();
                }
                p.fillColor = learned ? new Color(c.r * 0.42f, c.g * 0.42f, c.b * 0.42f, 0.97f) : new Color(0.02f, 0.03f, 0.05f, 0.9f);
                Diamond(p, m, r); p.Fill();
                p.lineWidth = learned ? 2f : 1.4f;
                p.strokeColor = learned ? c : can ? new Color(Gold.r, Gold.g, Gold.b, k) : new Color(1f, 1f, 1f, n.Available ? 0.28f : 0.12f);
                Diamond(p, m, r); p.Stroke();
                Color glyph = learned ? Color.Lerp(c, Color.white, 0.35f) : n.Available ? new Color(c.r, c.g, c.b, 0.55f) : new Color(1f, 1f, 1f, 0.18f);
                float scale = n.tier == 3 ? 1.35f : 1.1f;
                CombatOverlay.Glyph(p, SkillTrees.Glyph(tree), m, scale, glyph);
            };
            holder.Add(node);
            repaint.Add(node);

            var label = hud.MakeLabel(n.name, 11);
            label.style.marginTop = n.tier == 1 ? 0 : 3;
            label.style.marginBottom = n.tier == 1 ? 3 : 0;
            if (n.tier == 2) label.style.translate = new Translate(n.branch == 0 ? -26 : 26, 0);
            label.style.color = n.Available ? Dim : new Color(Dim.r, Dim.g, Dim.b, 0.45f);
            holder.Add(label);
            if (!n.Available)
            {
                var soon = hud.MakeLabel("SOON", 9);
                soon.style.letterSpacing = 1.5f;
                soon.style.color = new Color(Gold.r, Gold.g, Gold.b, 0.6f);
                if (n.tier == 2) soon.style.translate = new Translate(n.branch == 0 ? -26 : 26, 0);
                holder.Add(soon);
            }

            node.RegisterCallback<ClickEvent>(_ =>
            {
                if (Stats == null) return;
                if (Stats.Learn(tree, n)) footer.text = $"Learned {n.name}.";
            });
            node.RegisterCallback<PointerEnterEvent>(_ => footer.text = Describe(tree, n));
            node.RegisterCallback<PointerLeaveEvent>(_ => footer.text = Hint);
            return holder;
        }

        string Describe(AbilityElement tree, SkillNode n)
        {
            string state;
            if (!n.Available) state = "Coming soon.";
            else if (Stats != null && Stats.IsLearned(n)) state = "Learned.";
            else if (Stats != null && Stats.CanLearn(tree, n)) state = $"Click to learn ({SkillTrees.PointsPerNode} skill point).";
            else if (Stats != null && Stats.skillPoints < SkillTrees.PointsPerNode) state = "Not enough skill points.";
            else state = n.tier == 1 ? "" : n.tier == 2 ? "Requires the base skill." : "Requires one of the branch skills.";
            return $"{n.name} — {n.description}   {state}";
        }

        // ------------------------------------------------------------------ Desenho

        // Emblema de um elemento: círculo com halo na cor dele e o símbolo do seu estado.
        static void DrawEmblem(Painter2D p, Vector2 m, float r, AbilityElement e, float strength)
        {
            Color c = Combat.ElementColor(e);
            float k = 0.85f + 0.15f * Mathf.Sin(Time.time * 1.7f + (int)e);
            p.fillColor = new Color(c.r, c.g, c.b, 0.16f * strength * k);
            p.BeginPath(); p.Arc(m, r + 4f, 0, 360); p.Fill();
            p.fillColor = new Color(0.02f, 0.03f, 0.05f, 0.9f);
            p.BeginPath(); p.Arc(m, r, 0, 360); p.Fill();
            p.strokeColor = new Color(c.r, c.g, c.b, 0.4f + 0.6f * strength);
            p.lineWidth = 1.6f;
            p.BeginPath(); p.Arc(m, r - 1f, 0, 360); p.Stroke();
            CombatOverlay.Glyph(p, SkillTrees.Glyph(e), m, r / 9f, Color.Lerp(c, Color.white, 0.3f * strength));
        }

        static void Diamond(Painter2D p, Vector2 c, float r)
        {
            p.BeginPath();
            p.MoveTo(c + new Vector2(0, -r));
            p.LineTo(c + new Vector2(r, 0));
            p.LineTo(c + new Vector2(0, r));
            p.LineTo(c + new Vector2(-r, 0));
            p.ClosePath();
        }

        void DrawCorners(MeshGenerationContext ctx)
        {
            var p = ctx.painter2D;
            Rect r = window.contentRect;
            float w = r.width, h = r.height, k = 14f;
            p.strokeColor = new Color(Gold.r, Gold.g, Gold.b, 0.85f);
            p.lineWidth = 1.5f;
            foreach (var (x, y, sx, sy) in new[] { (0f, 0f, 1f, 1f), (w, 0f, -1f, 1f), (0f, h, 1f, -1f), (w, h, -1f, -1f) })
            {
                p.BeginPath();
                p.MoveTo(new Vector2(x, y + sy * k));
                p.LineTo(new Vector2(x, y));
                p.LineTo(new Vector2(x + sx * k, y));
                p.Stroke();
            }
        }

        static void Hover(VisualElement e, Color over, Color? normal = null)
        {
            Color back = normal ?? Line;
            e.RegisterCallback<PointerEnterEvent>(_ => SetBorder(e, 1f, over));
            e.RegisterCallback<PointerLeaveEvent>(_ => SetBorder(e, 1f, back));
        }

        static void SetBorder(VisualElement e, float w, Color c)
        {
            e.style.borderLeftWidth = e.style.borderRightWidth = e.style.borderTopWidth = e.style.borderBottomWidth = w;
            e.style.borderLeftColor = e.style.borderRightColor = e.style.borderTopColor = e.style.borderBottomColor = c;
        }
    }
}
