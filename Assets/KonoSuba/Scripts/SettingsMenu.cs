using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace AnimLab.KonoSuba
{
    /// <summary>
    /// Janela de teclas, construída e atualizada pelo GameHud. F10 fica sempre reservado ao menu.
    /// A captura lê o input diretamente, porque as ações do jogo ficam suspensas enquanto está aberta.
    /// </summary>
    public class SettingsMenu : IDisposable
    {
        static readonly Color Gold = new Color(0.86f, 0.70f, 0.40f);
        static readonly Color Dim = new Color(0.72f, 0.75f, 0.82f);
        static readonly Color Box = new Color(0.01f, 0.015f, 0.03f, 0.55f);
        static readonly Color Line = new Color(0.80f, 0.83f, 0.90f, 0.22f);
        static readonly Color Red = new Color(0.95f, 0.30f, 0.25f);
        static readonly KeyCode[] Keys = (KeyCode[])Enum.GetValues(typeof(KeyCode));
        const string Help = "Click a key to rebind. Esc cancels. F10 closes.";

        class BindingRow
        {
            public GameAction action;
            public VisualElement row, box;
            public Label key;
            public bool hovered;
            public float displacedAt = -9f;
        }

        readonly GameHud hud;
        readonly VisualElement layer, blocker, window;
        readonly ScrollView scroll;
        readonly Dictionary<GameAction, BindingRow> rows = new Dictionary<GameAction, BindingRow>();
        Label footer;
        GameAction? waiting;
        bool open, previousSuspended, restorePending, positioned, dragging;
        bool suppressPointer;
        int captureFrame, closedFrame, pointerReleasedFrame = -1;
        float openedAt;
        Vector2 dragFrom, posFrom;

        public bool IsOpen => open;
        public bool IsBlockingInput => open || restorePending;

        public SettingsMenu(GameHud hud, VisualElement root)
        {
            this.hud = hud;
            layer = new VisualElement { pickingMode = PickingMode.Ignore };
            layer.style.position = Position.Absolute;
            layer.style.left = layer.style.right = layer.style.top = layer.style.bottom = 0;
            root.Add(layer);

            blocker = new VisualElement();
            blocker.style.position = Position.Absolute;
            blocker.style.left = blocker.style.right = blocker.style.top = blocker.style.bottom = 0;
            blocker.style.backgroundColor = new Color(0f, 0f, 0f, 0.12f);
            blocker.style.display = DisplayStyle.None;
            layer.Add(blocker);

            window = new VisualElement();
            window.style.position = Position.Absolute;
            window.style.left = 60;
            window.style.top = 72;
            window.style.width = 420;
            window.style.height = 570;
            window.style.backgroundColor = new Color(0.025f, 0.035f, 0.06f, 0.9f);
            SetBorder(window, Line);
            window.generateVisualContent += DrawFrame;
            blocker.Add(window);

            BuildTitle();
            var heading = hud.MakeLabel("KEY BINDINGS", 12);
            heading.style.color = Gold;
            heading.style.letterSpacing = 2f;
            heading.style.marginLeft = 12;
            heading.style.marginTop = heading.style.marginBottom = 10;
            heading.style.flexShrink = 0;
            window.Add(heading);

            scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1;
            scroll.style.flexBasis = 0;
            scroll.style.minHeight = 0;
            scroll.style.marginLeft = 10;
            scroll.style.marginRight = 4;
            scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            scroll.verticalScrollerVisibility = ScrollerVisibility.Auto;
            window.Add(scroll);
            AddSection(scroll, "MOVEMENT", GameAction.MoveForward, GameAction.MoveBack,
                GameAction.MoveLeft, GameAction.MoveRight, GameAction.Run, GameAction.Jump, GameAction.Roll);
            AddSection(scroll, "COMBAT", GameAction.Attack, GameAction.Guard, GameAction.ReleaseTarget);
            AddSection(scroll, "ABILITIES", GameAction.Ability1, GameAction.Ability2, GameAction.Ability3,
                GameAction.Ability4, GameAction.Ability5, GameAction.Ability6, GameAction.Ability7,
                GameAction.Ability8, GameAction.Ability9);
            AddSection(scroll, "INTERFACE", GameAction.Character, GameAction.SkillTree);
            BuildFooter();
            BuildGear();

            // O clique que associa um botão do rato não pode também ativar um botão da janela.
            layer.RegisterCallback<PointerDownEvent>(e =>
            {
                if (waiting.HasValue || suppressPointer) e.StopImmediatePropagation();
            }, TrickleDown.TrickleDown);
            layer.RegisterCallback<PointerUpEvent>(e =>
            {
                if (waiting.HasValue || suppressPointer) e.StopImmediatePropagation();
            }, TrickleDown.TrickleDown);
            layer.RegisterCallback<ClickEvent>(e =>
            {
                if (waiting.HasValue || suppressPointer) e.StopImmediatePropagation();
            }, TrickleDown.TrickleDown);
            GameInput.Rebound += OnRebound;
            RefreshRows();
        }

        public void Toggle() => SetOpen(!open);

        public void SetOpen(bool value)
        {
            if (open == value) return;
            open = value;
            waiting = null;
            dragging = false;
            blocker.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
            if (open)
            {
                if (!restorePending) previousSuspended = GameInput.Suspended;
                restorePending = false;
                GameInput.Suspended = true;
                openedAt = Time.unscaledTime;
                footer.text = Help;
                footer.style.color = Dim;
                layer.BringToFront();
                FitWindow();
                RefreshRows();
            }
            else
            {
                // Mantém o frame do fecho suspenso: Esc ou o clique não passam para o jogo.
                restorePending = true;
                closedFrame = Time.frameCount;
            }
        }

        public void Update()
        {
            if (restorePending && Time.frameCount > closedFrame)
            {
                restorePending = false;
                GameInput.Suspended = previousSuspended;
            }
            if (suppressPointer)
            {
                bool held = false;
                for (int i = 0; i < 7; i++) held |= Input.GetMouseButton(i);
                if (held) pointerReleasedFrame = -1;
                else if (pointerReleasedFrame < 0) pointerReleasedFrame = Time.frameCount;
                else if (Time.frameCount > pointerReleasedFrame) suppressPointer = false;
            }

            if (Input.GetKeyDown(KeyCode.F10)) { Toggle(); return; }
            if (!open) return;
            GameInput.Suspended = true;
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (waiting.HasValue)
                {
                    waiting = null;
                    footer.text = "Rebinding cancelled. " + Help;
                    footer.style.color = Dim;
                }
                else SetOpen(false);
            }
            else if (waiting.HasValue && Time.frameCount > captureFrame && Input.anyKeyDown)
            {
                foreach (KeyCode key in Keys)
                {
                    // Aceita teclado e Mouse0–Mouse6; F10 e Esc são tratados acima.
                    if (key <= KeyCode.None || key >= KeyCode.JoystickButton0 || !Input.GetKeyDown(key)) continue;
                    if (key >= KeyCode.Mouse0 && key <= KeyCode.Mouse6)
                    {
                        suppressPointer = true;
                        pointerReleasedFrame = -1;
                    }
                    GameAction action = waiting.Value;
                    waiting = null;
                    GameAction? displaced = GameInput.Rebind(action, key);
                    footer.style.color = displaced.HasValue ? Red : Dim;
                    footer.text = displaced.HasValue
                        ? $"{GameInput.Label(key)} assigned to {Name(action)}. {Name(displaced.Value)} is now unbound."
                        : $"{Name(action)}: {GameInput.Label(key)}. " + Help;
                    if (displaced.HasValue)
                    {
                        BindingRow row = rows[displaced.Value];
                        row.displacedAt = Time.unscaledTime;
                        scroll.ScrollTo(row.row);
                    }
                    break;
                }
            }
            if (!open) return;
            FitWindow();
            float t = Mathf.Clamp01((Time.unscaledTime - openedAt) / 0.14f);
            window.style.opacity = t;
            window.style.translate = new Translate(0, (1f - t) * 8f);
            RefreshRows();
        }

        public void Dispose()
        {
            GameInput.Rebound -= OnRebound;
            if (IsBlockingInput) GameInput.Suspended = previousSuspended;
            open = restorePending = false;
            layer.RemoveFromHierarchy();
        }

        void OnRebound(GameAction action, KeyCode key) => RefreshRows();

        void RefreshRows()
        {
            foreach (BindingRow row in rows.Values)
            {
                bool listening = waiting == row.action;
                row.key.text = listening ? "Press a key…" : GameInput.Label(GameInput.Key(row.action));
                row.key.style.color = listening ? Gold : Color.white;
                float age = Time.unscaledTime - row.displacedAt;
                float flash = age < 1.2f ? (0.5f + 0.5f * Mathf.Cos(age * 20f)) * (1f - age / 1.2f) : 0f;
                row.row.style.backgroundColor = new Color(Red.r, Red.g, Red.b, flash * 0.3f);
                SetBorder(row.box, flash > 0.1f ? Red : listening || row.hovered ? Gold : Line);
            }
        }

        void AddSection(VisualElement parent, string title, params GameAction[] actions)
        {
            var section = hud.MakeLabel(title, 11);
            section.style.color = Gold;
            section.style.letterSpacing = 2f;
            section.style.marginTop = 8;
            section.style.marginBottom = 6;
            section.style.paddingBottom = 4;
            section.style.borderBottomWidth = 1;
            section.style.borderBottomColor = Line;
            parent.Add(section);
            foreach (GameAction action in actions)
            {
                var row = new BindingRow { action = action, row = new VisualElement(), box = new VisualElement() };
                row.row.style.flexDirection = FlexDirection.Row;
                row.row.style.alignItems = Align.Center;
                row.row.style.flexShrink = 0;
                row.row.style.height = 34;
                row.row.style.marginRight = 6;
                var label = hud.MakeLabel(Name(action), 13);
                label.style.flexGrow = 1;
                label.style.flexShrink = 1;
                row.row.Add(label);
                row.box.style.width = 136;
                row.box.style.height = 26;
                row.box.style.flexShrink = 0;
                row.box.style.alignItems = Align.Center;
                row.box.style.justifyContent = Justify.Center;
                row.box.style.backgroundColor = Box;
                row.key = hud.MakeLabel("", 13);
                row.box.Add(row.key);
                row.box.RegisterCallback<PointerEnterEvent>(_ => row.hovered = true);
                row.box.RegisterCallback<PointerLeaveEvent>(_ => row.hovered = false);
                row.box.RegisterCallback<ClickEvent>(e =>
                {
                    if (e.button != 0) return;
                    waiting = row.action;
                    captureFrame = Time.frameCount;
                    footer.text = $"{Name(row.action)}: press a key or mouse button. Esc cancels. F10 closes.";
                    footer.style.color = Dim;
                    RefreshRows();
                });
                row.row.Add(row.box);
                parent.Add(row.row);
                rows.Add(action, row);
            }
        }

        void BuildTitle()
        {
            var bar = new VisualElement();
            bar.style.height = 30;
            bar.style.flexShrink = 0;
            bar.style.alignItems = Align.Center;
            bar.style.justifyContent = Justify.Center;
            bar.style.borderBottomWidth = 1;
            bar.style.borderBottomColor = Line;
            window.Add(bar);
            var title = hud.MakeLabel("SETTINGS", 14);
            title.style.letterSpacing = 3f;
            title.style.color = Gold;
            bar.Add(title);
            bar.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button != 0) return;
                dragging = true;
                dragFrom = e.position;
                posFrom = new Vector2(window.resolvedStyle.left, window.resolvedStyle.top);
                bar.CapturePointer(e.pointerId);
            });
            bar.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (!dragging) return;
                Vector2 d = (Vector2)e.position - dragFrom;
                window.style.left = Mathf.Clamp(posFrom.x + d.x, 0f, Mathf.Max(0f, layer.contentRect.width - window.resolvedStyle.width));
                window.style.top = Mathf.Clamp(posFrom.y + d.y, 0f, Mathf.Max(0f, layer.contentRect.height - window.resolvedStyle.height));
            });
            bar.RegisterCallback<PointerUpEvent>(e => { dragging = false; bar.ReleasePointer(e.pointerId); });
            bar.RegisterCallback<PointerCaptureOutEvent>(_ => dragging = false);

            var close = Button("", () => SetOpen(false));
            close.style.position = Position.Absolute;
            close.style.right = 6;
            close.style.top = 5;
            close.style.width = close.style.height = 20;
            close.RegisterCallback<PointerDownEvent>(e => e.StopPropagation());
            close.generateVisualContent += ctx =>
            {
                var p = ctx.painter2D;
                p.strokeColor = Dim; p.lineWidth = 1.4f; p.lineCap = LineCap.Round;
                p.BeginPath(); p.MoveTo(new Vector2(6, 6)); p.LineTo(new Vector2(14, 14)); p.Stroke();
                p.BeginPath(); p.MoveTo(new Vector2(14, 6)); p.LineTo(new Vector2(6, 14)); p.Stroke();
            };
            bar.Add(close);
        }

        void BuildFooter()
        {
            var reset = Button("Reset to defaults", () =>
            {
                foreach (BindingRow row in rows.Values) row.displacedAt = -9f;
                GameInput.ResetAll();
                footer.text = "Default bindings restored. " + Help;
                footer.style.color = Dim;
            });
            reset.style.height = 28;
            reset.style.flexShrink = 0;
            reset.style.marginLeft = reset.style.marginRight = 10;
            reset.style.marginTop = 10;
            window.Add(reset);
            footer = hud.MakeLabel(Help, 11);
            footer.style.color = Dim;
            footer.style.whiteSpace = WhiteSpace.Normal;
            footer.style.minHeight = 46;
            footer.style.flexShrink = 0;
            footer.style.marginTop = 8;
            footer.style.paddingLeft = footer.style.paddingRight = 12;
            footer.style.paddingTop = 6;
            footer.style.paddingBottom = 8;
            footer.style.borderTopWidth = 1;
            footer.style.borderTopColor = Line;
            window.Add(footer);
        }

        VisualElement Button(string text, Action click)
        {
            var button = new VisualElement();
            button.style.alignItems = Align.Center;
            button.style.justifyContent = Justify.Center;
            button.style.backgroundColor = Box;
            SetBorder(button, Line);
            if (text.Length > 0) button.Add(hud.MakeLabel(text, 13));
            button.RegisterCallback<PointerEnterEvent>(_ => SetBorder(button, Gold));
            button.RegisterCallback<PointerLeaveEvent>(_ => SetBorder(button, Line));
            button.RegisterCallback<ClickEvent>(e => { if (e.button == 0) click(); });
            return button;
        }

        void BuildGear()
        {
            var gear = Button("", Toggle);
            gear.tooltip = "Settings (F10)";
            gear.style.position = Position.Absolute;
            gear.style.top = gear.style.right = 24;
            gear.style.width = gear.style.height = 30;
            gear.generateVisualContent += ctx =>
            {
                var p = ctx.painter2D;
                var center = new Vector2(14, 14);
                p.strokeColor = Gold;
                p.lineWidth = 1.4f;
                p.lineJoin = LineJoin.Round;
                p.BeginPath();
                for (int i = 0; i < 32; i++)
                {
                    float angle = (i * 360f / 32f - 90f) * Mathf.Deg2Rad;
                    float radius = i % 4 == 1 || i % 4 == 2 ? 10f : 7.5f;
                    Vector2 point = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                    if (i == 0) p.MoveTo(point); else p.LineTo(point);
                }
                p.ClosePath(); p.Stroke();
                p.BeginPath(); p.Arc(center, 3.2f, 0, 360); p.Stroke();
            };
            layer.Add(gear);
        }

        void FitWindow()
        {
            float w = layer.contentRect.width, h = layer.contentRect.height;
            if (w <= 0f || h <= 0f) return;
            float width = Mathf.Min(420f, Mathf.Max(0f, w - 24f));
            float height = Mathf.Min(570f, Mathf.Max(0f, h - 88f));
            window.style.width = width;
            window.style.height = height;
            float left = positioned ? window.resolvedStyle.left : w - width - 30f;
            float top = positioned ? window.resolvedStyle.top : 72f;
            window.style.left = Mathf.Clamp(left, 0f, Mathf.Max(0f, w - width));
            window.style.top = Mathf.Clamp(top, 0f, Mathf.Max(0f, h - height));
            positioned = true;
        }

        static string Name(GameAction action) => action switch
        {
            GameAction.MoveForward => "Move Forward",
            GameAction.MoveBack => "Move Backward",
            GameAction.MoveLeft => "Move Left",
            GameAction.MoveRight => "Move Right",
            GameAction.Run => "Run",
            GameAction.Attack => "Attack",
            GameAction.Guard => "Guard / Parry",
            GameAction.Jump => "Jump",
            GameAction.Roll => "Roll",
            GameAction.Character => "Character",
            GameAction.SkillTree => "Skill Trees",
            GameAction.ReleaseTarget => "Release Target",
            _ => "Ability " + ((int)action - (int)GameAction.Ability1 + 1)
        };

        // Mesmos cantos dourados da janela do personagem.
        void DrawFrame(MeshGenerationContext ctx)
        {
            var p = ctx.painter2D;
            float w = window.contentRect.width, h = window.contentRect.height, k = 14f;
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

        static void SetBorder(VisualElement element, Color color)
        {
            element.style.borderLeftWidth = element.style.borderRightWidth = element.style.borderTopWidth = element.style.borderBottomWidth = 1;
            element.style.borderLeftColor = element.style.borderRightColor = element.style.borderTopColor = element.style.borderBottomColor = color;
        }
    }
}
