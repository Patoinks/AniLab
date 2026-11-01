using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace AnimLab.KonoSuba
{
    /// <summary>
    /// Janela do personagem (tecla C), com o layout da do Metin2 e a estética do nosso HUD:
    ///   barra de título (arrastável) com fechar · retrato · guilda e nome · nível e barra de EXP ·
    ///   separadores (Status / Combat / Resist) · pontos por gastar · grelha de stats com scroll ·
    ///   rodapé que explica o stat por baixo do cursor.
    /// Status: os 4 atributos com "+" à esquerda e os derivados à direita, como no Metin2.
    /// Lê tudo do CharacterStats a cada frame (só enquanto está aberta) e chama Spend() nos "+".
    /// Construída e atualizada pelo GameHud, no mesmo painel e com a mesma fonte.
    /// </summary>
    public class StatsWindow
    {
        static readonly Color Gold = new Color(0.86f, 0.70f, 0.40f);
        static readonly Color Dim = new Color(0.72f, 0.75f, 0.82f);
        static readonly Color Box = new Color(0.01f, 0.015f, 0.03f, 0.55f);
        static readonly Color Line = new Color(0.80f, 0.83f, 0.90f, 0.22f);
        const float Width = 372f;

        readonly GameHud hud;
        readonly VisualElement window;
        readonly List<(Label label, Func<string> value)> live = new List<(Label, Func<string>)>();
        readonly List<VisualElement> plusButtons = new List<VisualElement>();
        readonly VisualElement[] tabs = new VisualElement[3];
        readonly VisualElement[] pages = new VisualElement[3];
        CharacterStats stats;
        GameHud.GlowBar expBar;
        Label footer, pointsValue;
        int tab;
        bool open;
        float openedAt;
        Vector2 dragFrom, posFrom;
        bool dragging;

        public bool IsOpen => open;

        public StatsWindow(GameHud hud, VisualElement root)
        {
            this.hud = hud;
            window = new VisualElement();
            window.style.position = Position.Absolute;
            window.style.left = 60;
            window.style.top = 120;
            window.style.width = Width;
            window.style.backgroundColor = new Color(0.025f, 0.035f, 0.06f, 0.9f);
            SetBorder(window, 1f, Line);
            window.style.display = DisplayStyle.None;
            window.generateVisualContent += DrawFrame;
            root.Add(window);

            BuildTitle();
            BuildHeader();
            BuildTabs();
            BuildPages();
            BuildFooter();
            SelectTab(0);
        }

        public void Toggle() => SetOpen(!open);

        public void SetOpen(bool value)
        {
            open = value;
            openedAt = Time.unscaledTime;
            window.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
            if (open) Refresh();
        }

        public void Update()
        {
            if (GameInput.GetDown(GameAction.Character)) Toggle();
            if (open && !GameInput.Suspended && Input.GetKeyDown(KeyCode.Escape)) SetOpen(false);
            if (!open) return;
            // Abre com um pequeno deslize e fade.
            float t = Mathf.Clamp01((Time.unscaledTime - openedAt) / 0.14f);
            window.style.opacity = t;
            window.style.translate = new Translate(0, (1f - t) * 8f);
            Refresh();
        }

        CharacterStats Stats
        {
            get
            {
                if (stats == null || stats.name != "Player") stats = GameHud.LocalPlayer<CharacterStats>();
                return stats;
            }
        }

        void Refresh()
        {
            if (Stats == null) return;
            foreach (var (label, value) in live)
            {
                string v = value();
                if (label.text != v) label.text = v;
            }
            expBar.SetValue(stats.exp / Mathf.Max(stats.ExpToNext, 1f));
            expBar.MarkDirtyRepaint();
            bool canSpend = stats.statPoints > 0;
            pointsValue.style.color = canSpend ? Gold : Dim;
            foreach (VisualElement b in plusButtons)
            {
                b.SetEnabled(canSpend);
                b.style.opacity = canSpend ? 1f : 0.3f;
            }
        }

        // ------------------------------------------------------------------ Partes da janela

        void BuildTitle()
        {
            var bar = new VisualElement();
            bar.style.height = 30;
            bar.style.flexDirection = FlexDirection.Row;
            bar.style.alignItems = Align.Center;
            bar.style.justifyContent = Justify.Center;
            bar.style.borderBottomWidth = 1;
            bar.style.borderBottomColor = Line;
            window.Add(bar);

            var title = hud.MakeLabel("CHARACTER", 14);
            title.style.letterSpacing = 3f;
            title.style.color = Gold;
            bar.Add(title);

            // Arrastar pela barra de título.
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

        void BuildHeader()
        {
            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.paddingLeft = header.style.paddingRight = 10;
            header.style.paddingTop = 10;
            window.Add(header);

            // Retrato: moldura com a silhueta da cápsula e um losango dourado no canto.
            var portrait = new VisualElement();
            portrait.style.width = portrait.style.height = 72;
            portrait.style.backgroundColor = Box;
            SetBorder(portrait, 1f, new Color(Gold.r, Gold.g, Gold.b, 0.6f));
            portrait.generateVisualContent += ctx =>
            {
                var p = ctx.painter2D;
                p.fillColor = new Color(0.62f, 0.66f, 0.74f, 0.9f);
                p.BeginPath(); p.Arc(new Vector2(36, 34), 12, 180, 360); p.LineTo(new Vector2(48, 60));
                p.Arc(new Vector2(36, 60), 12, 0, 180); p.ClosePath(); p.Fill();
                p.fillColor = new Color(0.12f, 0.13f, 0.16f);
                p.BeginPath(); p.MoveTo(new Vector2(36, 30)); p.LineTo(new Vector2(46, 30)); p.LineTo(new Vector2(46, 35));
                p.LineTo(new Vector2(36, 35)); p.ClosePath(); p.Fill();
            };
            header.Add(portrait);

            var right = new VisualElement();
            right.style.flexGrow = 1;
            right.style.marginLeft = 10;
            header.Add(right);

            var names = new VisualElement();
            names.style.flexDirection = FlexDirection.Row;
            right.Add(names);
            names.Add(ValueBox(() => Stats != null ? Stats.guild : "", 1f, 13, Dim));
            var nameBox = ValueBox(() => Stats != null ? Stats.characterName : "", 1f, 13, Color.white);
            nameBox.style.marginLeft = 6;
            names.Add(nameBox);

            // Nível e experiência.
            var lv = new VisualElement();
            lv.style.flexDirection = FlexDirection.Row;
            lv.style.alignItems = Align.Center;
            lv.style.marginTop = 8;
            right.Add(lv);
            var lvTag = hud.MakeLabel("LV", 11);
            lvTag.style.color = Gold;
            lvTag.style.letterSpacing = 1.5f;
            lv.Add(lvTag);
            var lvValue = Live(hud.MakeLabel("", 18), () => Stats != null ? Stats.level.ToString() : "");
            lvValue.style.marginLeft = 6;
            lvValue.style.minWidth = 26;
            lv.Add(lvValue);

            var expCol = new VisualElement();
            expCol.style.flexGrow = 1;
            expCol.style.marginLeft = 8;
            lv.Add(expCol);
            expBar = new GameHud.GlowBar(new Color(0.95f, 0.78f, 0.42f), 2f, 23.1f);
            expBar.style.height = 12;
            expCol.Add(expBar);
            var expText = Live(hud.MakeLabel("", 11), () => Stats == null ? ""
                : $"EXP  {Stats.exp:0} / {Stats.ExpToNext:0}   ({Stats.exp / Mathf.Max(Stats.ExpToNext, 1f) * 100f:0.0}%)");
            expText.style.color = Dim;
            expText.style.alignSelf = Align.FlexEnd;
            expCol.Add(expText);
            Explain(lv, "Level and experience. Each level gives stat points to spend on attributes.");
        }

        void BuildTabs()
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginTop = 12;
            row.style.marginLeft = row.style.marginRight = 10;
            window.Add(row);
            string[] names = { "STATUS", "COMBAT", "RESIST" };
            for (int i = 0; i < 3; i++)
            {
                int index = i;
                var t = new VisualElement();
                t.style.flexGrow = 1;
                t.style.height = 26;
                t.style.alignItems = Align.Center;
                t.style.justifyContent = Justify.Center;
                if (i > 0) t.style.marginLeft = 4;
                var l = hud.MakeLabel(names[i], 12);
                l.style.letterSpacing = 2f;
                t.Add(l);
                t.RegisterCallback<ClickEvent>(_ => SelectTab(index));
                Hover(t, new Color(Gold.r, Gold.g, Gold.b, 0.5f));
                row.Add(t);
                tabs[i] = t;
            }
        }

        public void SelectTab(int index)
        {
            tab = index;
            for (int i = 0; i < 3; i++)
            {
                bool on = i == tab;
                tabs[i].style.backgroundColor = on ? new Color(0.14f, 0.11f, 0.06f, 0.7f) : Box;
                SetBorder(tabs[i], 1f, on ? Gold : Line);
                ((Label)tabs[i][0]).style.color = on ? Gold : Dim;
                pages[i].style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        void BuildPages()
        {
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.height = 330;
            scroll.style.marginTop = 8;
            scroll.style.marginLeft = 10;
            scroll.style.marginRight = 4;
            scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            scroll.verticalScrollerVisibility = ScrollerVisibility.Auto;
            window.Add(scroll);
            for (int i = 0; i < 3; i++)
            {
                pages[i] = new VisualElement();
                pages[i].style.paddingRight = 6;
                scroll.Add(pages[i]);
            }
            BuildStatusPage(pages[0]);
            BuildCombatPage(pages[1]);
            BuildResistPage(pages[2]);
        }

        void BuildStatusPage(VisualElement page)
        {
            // Pontos por gastar.
            var points = new VisualElement();
            points.style.flexDirection = FlexDirection.Row;
            points.style.alignItems = Align.Center;
            points.style.justifyContent = Justify.FlexEnd;
            points.style.marginBottom = 6;
            var tag = hud.MakeLabel("STAT POINTS", 11);
            tag.style.letterSpacing = 1.5f;
            tag.style.color = Dim;
            points.Add(tag);
            pointsValue = Live(hud.MakeLabel("", 15), () => Stats != null ? Stats.statPoints.ToString() : "");
            pointsValue.style.marginLeft = 8;
            pointsValue.style.minWidth = 18;
            points.Add(pointsValue);
            Explain(points, "Unspent points. Press + next to an attribute to raise it.");
            page.Add(points);

            // Atributos (com +) à esquerda, derivados principais à direita — como no Metin2.
            var grid = Columns(page);
            Attribute(grid.left, CharacterStats.Attribute.Vitality, "Vitality", new Color(0.95f, 0.45f, 0.42f),
                "Vitality: max HP, defense, HP regen, poise, knockback resist and status resist.");
            Attribute(grid.left, CharacterStats.Attribute.Intelligence, "Intelligence", new Color(0.45f, 0.65f, 1f),
                "Intelligence: max mana, mana regen, magic attack/defense, cast speed and cooldown reduction.");
            Attribute(grid.left, CharacterStats.Attribute.Strength, "Strength", new Color(1f, 0.62f, 0.3f),
                "Strength: attack (sword damage), crit damage and stagger power.");
            Attribute(grid.left, CharacterStats.Attribute.Dexterity, "Dexterity", new Color(0.55f, 0.9f, 0.6f),
                "Dexterity: attack speed, move speed, crit chance and some defense.");
            Stat(grid.right, "HP", () => Hp(), "Max health. Regenerates slowly over time (HP Regen).");
            Stat(grid.right, "Mana", () => Mp(), "Max mana, spent by abilities. Regenerates when not channeling.");
            Stat(grid.right, "Attack", () => $"{Stats?.Attack:0}", "Physical attack. Scales sword damage (30 = base damage).");
            Stat(grid.right, "Defense", () => $"{Stats?.Defense:0}", "Reduces physical damage: damage × 100 / (100 + defense).");

            Divider(page);
            var sec = Columns(page);
            Stat(sec.left, "Move Speed", () => Pct(Stats?.MoveSpeed), "How fast you walk and run.");
            Stat(sec.left, "Attack Speed", () => Pct(Stats?.AttackSpeed), "How fast sword swings play (wind-up, swing and recovery).");
            Stat(sec.left, "Cast Speed", () => Pct(Stats?.CastSpeed), "How fast spells form, charge and channel.");
            Stat(sec.left, "Cooldown Red.", () => Pct(Stats?.CooldownReduction, 0f), "Shortens ability cooldowns.");
            Stat(sec.left, "HP Regen", () => $"{Stats?.HealthRegen:0.0}/s", "Health recovered per second.");
            Stat(sec.right, "Magic Attack", () => $"{Stats?.MagicAttack:0}", "Scales spell damage (30 = base damage).");
            Stat(sec.right, "Magic Defense", () => $"{Stats?.MagicDefense:0}", "Reduces elemental damage: damage × 100 / (100 + magic defense).");
            Stat(sec.right, "Crit Chance", () => Pct(Stats?.CritChance, 0f), "Chance for a sword hit to be critical.");
            Stat(sec.right, "Crit Damage", () => Pct(Stats?.CritDamage), "Damage of a critical hit.");
            Stat(sec.right, "Mana Regen", () => $"{Stats?.ManaRegen:0.0}/s", "Mana recovered per second (not while channeling).");
        }

        void BuildCombatPage(VisualElement page)
        {
            Section(page, "OFFENSE");
            var a = Columns(page);
            Stat(a.left, "Attack", () => $"{Stats?.Attack:0}", "Physical attack. Scales sword damage (30 = base damage).");
            Stat(a.left, "Weapon Dmg", () => Pct(Stats?.WeaponScale), "Sword damage multiplier from Attack.");
            Stat(a.left, "Stagger Power", () => Pct(Stats?.StaggerPower), "How much poise your hits break. Broken poise = stagger.");
            Stat(a.right, "Magic Attack", () => $"{Stats?.MagicAttack:0}", "Scales spell damage (30 = base damage).");
            Stat(a.right, "Spell Dmg", () => Pct(Stats?.SpellScale), "Spell damage multiplier from Magic Attack.");
            Stat(a.right, "Crit", () => $"{Pct(Stats?.CritChance, 0f)} ×{Stats?.CritDamage:0.00}", "Crit chance and crit damage multiplier.");

            Section(page, "DEFENSE");
            var d = Columns(page);
            Stat(d.left, "Defense", () => $"{Stats?.Defense:0}", "Reduces physical damage: damage × 100 / (100 + defense).");
            Stat(d.left, "Poise", () => $"{Stats?.Poise:0}", "How much stagger you take before being staggered.");
            Stat(d.right, "Magic Defense", () => $"{Stats?.MagicDefense:0}", "Reduces elemental damage.");
            Stat(d.right, "Knockback Res.", () => Pct(Stats?.KnockbackResist, 0f), "Reduces how far hits push you.");

            Section(page, "GUARD");
            var g = Columns(page);
            Stat(g.left, "Block", () => Pct(Stats?.BlockReduction, 0f), "Damage blocked from the front while holding F.");
            Stat(g.right, "Parry Window", () => $"{Stats?.ParryWindow:0.00}s", "Press F this close to a hit to parry it (staggers the attacker).");
        }

        void BuildResistPage(VisualElement page)
        {
            Section(page, "ELEMENTS");
            var e = Columns(page);
            for (int i = 0; i < Combat.Elements.Length; i++)
            {
                AbilityElement el = Combat.Elements[i];
                Stat(i % 2 == 0 ? e.left : e.right, el.ToString(), () => Pct(Stats?.ElementResist(el), 0f),
                    $"Cuts {el} damage by this much (max 80%).", Combat.ElementColor(el));
            }
            Section(page, "STATUS EFFECTS");
            var s = Columns(page);
            for (int i = 0; i < Combat.Statuses.Length; i++)
            {
                StatusType st = Combat.Statuses[i];
                StatusRow(i % 2 == 0 ? s.left : s.right, st);
            }
        }

        void BuildFooter()
        {
            footer = hud.MakeLabel("", 11);
            footer.style.color = Dim;
            footer.style.whiteSpace = WhiteSpace.Normal;
            footer.style.minHeight = 34;
            footer.style.marginTop = 6;
            footer.style.paddingLeft = footer.style.paddingRight = 12;
            footer.style.paddingTop = 6;
            footer.style.paddingBottom = 8;
            footer.style.borderTopWidth = 1;
            footer.style.borderTopColor = Line;
            footer.text = "Hover a stat to see what it does.   [Esc] close";
            window.Add(footer);
        }

        // ------------------------------------------------------------------ Peças

        (VisualElement left, VisualElement right) Columns(VisualElement page)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            page.Add(row);
            var l = new VisualElement(); l.style.flexGrow = 1; l.style.flexBasis = 0;
            var r = new VisualElement(); r.style.flexGrow = 1; r.style.flexBasis = 0; r.style.marginLeft = 10;
            row.Add(l); row.Add(r);
            return (l, r);
        }

        void Section(VisualElement page, string title)
        {
            var l = hud.MakeLabel(title, 11);
            l.style.letterSpacing = 2f;
            l.style.color = Gold;
            l.style.marginTop = page.childCount > 0 ? 10 : 0;
            l.style.marginBottom = 4;
            page.Add(l);
        }

        void Divider(VisualElement page)
        {
            var d = new VisualElement { pickingMode = PickingMode.Ignore };
            d.style.height = 1;
            d.style.marginTop = 8;
            d.style.marginBottom = 8;
            d.style.backgroundColor = Line;
            page.Add(d);
        }

        /// <summary>Linha de stat: losango da cor (ou o símbolo dado), nome, e o valor numa caixa à direita.</summary>
        VisualElement Stat(VisualElement col, string name, Func<string> value, string help, Color? accent = null,
                           Action<Painter2D> icon = null)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.height = 26;
            row.style.marginBottom = 3;

            Color c = accent ?? new Color(0.8f, 0.83f, 0.9f, 0.55f);
            var dot = new VisualElement { pickingMode = PickingMode.Ignore };
            dot.style.width = dot.style.height = icon != null ? 16 : 10;
            dot.generateVisualContent += ctx =>
            {
                var p = ctx.painter2D;
                if (icon != null) { icon(p); return; }
                p.fillColor = c;
                p.BeginPath(); p.MoveTo(new Vector2(5, 1)); p.LineTo(new Vector2(9, 5)); p.LineTo(new Vector2(5, 9)); p.LineTo(new Vector2(1, 5)); p.ClosePath(); p.Fill();
            };
            row.Add(dot);

            var n = hud.MakeLabel(name, 12);
            n.style.color = Dim;
            n.style.marginLeft = 6;
            n.style.flexGrow = 1;
            row.Add(n);

            var box = ValueBox(value, 0f, 13, Color.white);
            box.style.minWidth = 64;
            row.Add(box);
            Explain(row, help);
            col.Add(row);
            return row;
        }

        void Attribute(VisualElement col, CharacterStats.Attribute a, string name, Color color, string help)
        {
            VisualElement row = Stat(col, name, () => Stats != null ? Stats.Get(a).ToString() : "", help, color);
            var plus = new VisualElement();
            plus.style.width = plus.style.height = 20;
            plus.style.marginLeft = 4;
            SetBorder(plus, 1f, new Color(Gold.r, Gold.g, Gold.b, 0.7f));
            plus.style.backgroundColor = new Color(0.14f, 0.11f, 0.06f, 0.6f);
            plus.generateVisualContent += ctx =>
            {
                var p = ctx.painter2D;
                p.strokeColor = Gold; p.lineWidth = 1.6f; p.lineCap = LineCap.Round;
                p.BeginPath(); p.MoveTo(new Vector2(10, 5)); p.LineTo(new Vector2(10, 15)); p.Stroke();
                p.BeginPath(); p.MoveTo(new Vector2(5, 10)); p.LineTo(new Vector2(15, 10)); p.Stroke();
            };
            Hover(plus, Gold);
            plus.RegisterCallback<ClickEvent>(_ => Stats?.Spend(a));
            plusButtons.Add(plus);
            row.Add(plus);
        }

        // O losango dá lugar ao símbolo do estado (o mesmo da placa dos inimigos).
        void StatusRow(VisualElement col, StatusType st) =>
            Stat(col, Combat.StatusName(st), () => Pct(Stats?.StatusResist(st), 0f),
                 $"{Combat.StatusName(st)} lasts this much shorter on you (max 80%).", Combat.StatusColor(st),
                 p => CombatOverlay.Glyph(p, st, new Vector2(8, 8), 0.95f, Combat.StatusColor(st)));

        VisualElement ValueBox(Func<string> value, float grow, int size, Color color)
        {
            var box = new VisualElement { pickingMode = PickingMode.Ignore };
            box.style.flexGrow = grow;
            box.style.flexBasis = grow > 0f ? 0 : StyleKeyword.Auto;
            box.style.height = 24;
            box.style.paddingLeft = box.style.paddingRight = 8;
            box.style.justifyContent = Justify.Center;
            box.style.alignItems = grow > 0f ? Align.Center : Align.FlexEnd;
            box.style.backgroundColor = Box;
            SetBorder(box, 1f, Line);
            var l = Live(hud.MakeLabel("", size), value);
            l.style.color = color;
            box.Add(l);
            return box;
        }

        Label Live(Label l, Func<string> value)
        {
            live.Add((l, value));
            return l;
        }

        void Explain(VisualElement e, string help)
        {
            e.RegisterCallback<PointerEnterEvent>(_ => footer.text = help);
            e.RegisterCallback<PointerLeaveEvent>(_ => footer.text = "Hover a stat to see what it does.   [Esc] close");
        }

        static void Hover(VisualElement e, Color border)
        {
            e.RegisterCallback<PointerEnterEvent>(_ => SetBorder(e, 1f, border));
            e.RegisterCallback<PointerLeaveEvent>(_ => SetBorder(e, 1f, Line));
        }

        string Hp()
        {
            var body = Stats != null ? Stats.GetComponent<Combatant>() : null;
            return body != null ? $"{Mathf.CeilToInt(body.health)}/{Mathf.CeilToInt(body.maxHealth)}" : $"{Stats?.MaxHealth:0}";
        }

        string Mp()
        {
            var bar = Stats != null ? Stats.GetComponent<AbilityBar>() : null;
            return bar != null ? $"{Mathf.CeilToInt(bar.mana)}/{Mathf.CeilToInt(bar.maxMana)}" : $"{Stats?.MaxMana:0}";
        }

        /// <summary>Fator ou fração em percentagem (1.08 → 108%, 0.05 → 5%). O segundo argumento é só leitura.</summary>
        static string Pct(float? v, float baseline = 1f) => v.HasValue ? $"{v.Value * 100f:0.#}%" : "";

        // Cantos dourados (os cantos da moldura do Metin2, em linha fina).
        void DrawFrame(MeshGenerationContext ctx)
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

        static void SetBorder(VisualElement e, float w, Color c)
        {
            e.style.borderLeftWidth = e.style.borderRightWidth = e.style.borderTopWidth = e.style.borderBottomWidth = w;
            e.style.borderLeftColor = e.style.borderRightColor = e.style.borderTopColor = e.style.borderBottomColor = c;
        }
    }
}
