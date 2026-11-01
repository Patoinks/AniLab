using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace AnimLab.KonoSuba
{
    /// <summary>
    /// Consola de admin (F6), construída e atualizada pelo GameHud: limpa o mapa durante os testes.
    /// Comandos: kill tornados | kill ai | kill all | kill &lt;nome&gt; | help. Botões para os mais usados e um modo
    /// "clique para matar" (o clique no mapa apaga o alvo mais perto do ponto: inimigo, boneco ou tornado).
    /// Só apaga na sessão (play mode); o Player, a câmara e a interface nunca são apagados.
    /// Numa partida online só limpa efeitos locais (tornados): os lutadores são da simulação.
    /// </summary>
    public class AdminConsole : IDisposable
    {
        static readonly Color Gold = new Color(0.86f, 0.70f, 0.40f);
        static readonly Color Dim = new Color(0.72f, 0.75f, 0.82f);
        static readonly Color Box = new Color(0.01f, 0.015f, 0.03f, 0.55f);
        static readonly Color Line = new Color(0.80f, 0.83f, 0.90f, 0.22f);
        static readonly Color Red = new Color(0.95f, 0.30f, 0.25f);
        const KeyCode Key = KeyCode.F6;
        const string Help = "kill tornados · kill ai · kill all · kill <name> · help";

        readonly GameHud hud;
        readonly Func<bool> shortcutAvailable;
        readonly VisualElement layer, window;
        readonly TextField input;
        readonly Label log, clickLabel;
        readonly VisualElement clickButton;
        readonly List<string> lines = new List<string>();
        bool open, previousSuspended, restorePending, clickMode;
        int closedFrame;

        public bool IsBlockingInput => open || restorePending;
        /// <summary>O rato está em cima da janela (o clique é da consola, não do mapa).</summary>
        bool PointerOverWindow
        {
            get
            {
                if (window.panel == null) return false;
                Vector2 screen = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
                Vector2 p = RuntimePanelUtils.ScreenToPanel(window.panel, screen);
                return window.worldBound.Contains(p);
            }
        }

        public AdminConsole(GameHud hud, VisualElement root, Func<bool> shortcutAvailable)
        {
            this.hud = hud;
            this.shortcutAvailable = shortcutAvailable;
            layer = new VisualElement { pickingMode = PickingMode.Ignore };
            layer.style.position = Position.Absolute;
            layer.style.left = layer.style.right = layer.style.top = layer.style.bottom = 0;
            layer.style.display = DisplayStyle.None;
            root.Add(layer);

            window = new VisualElement();
            window.style.position = Position.Absolute;
            window.style.left = 30;
            window.style.bottom = 120;
            window.style.width = 440;
            window.style.backgroundColor = new Color(0.025f, 0.035f, 0.06f, 0.9f);
            window.style.paddingLeft = window.style.paddingRight = 12;
            window.style.paddingBottom = 10;
            SetBorder(window, Line);
            window.generateVisualContent += DrawFrame;
            layer.Add(window);

            var title = hud.MakeLabel("ADMIN CONSOLE", 14);
            title.style.color = Gold;
            title.style.letterSpacing = 3f;
            title.style.unityTextAlign = TextAnchor.MiddleCenter;
            title.style.height = 30;
            title.style.borderBottomWidth = 1;
            title.style.borderBottomColor = Line;
            title.style.marginLeft = title.style.marginRight = -12;
            window.Add(title);

            var buttons = new VisualElement();
            buttons.style.flexDirection = FlexDirection.Row;
            buttons.style.marginTop = 10;
            window.Add(buttons);
            buttons.Add(Button("Kill tornados", () => Run("kill tornados")));
            buttons.Add(Button("Kill AI", () => Run("kill ai")));
            clickButton = Button("", () => SetClickMode(!clickMode));
            clickLabel = hud.MakeLabel("", 13);
            clickButton.Add(clickLabel);
            buttons.Add(clickButton);
            SetClickMode(false);

            log = hud.MakeLabel("", 12);
            log.style.color = Dim;
            log.style.whiteSpace = WhiteSpace.Normal;
            log.style.minHeight = 70;
            log.style.marginTop = 10;
            log.style.paddingTop = 6;
            log.style.borderTopWidth = 1;
            log.style.borderTopColor = Line;
            window.Add(log);

            input = new TextField();
            input.style.marginTop = 8;
            input.style.marginLeft = input.style.marginRight = 0;
            input.style.height = 26;
            // A caixa interior do TextField traz o fundo branco do tema: fica escura como as outras.
            VisualElement field = input.Q(className: TextField.inputUssClassName) ?? input;
            field.style.color = Color.white;
            field.style.backgroundColor = Box;
            field.style.unityFontDefinition = log.style.unityFontDefinition;
            field.style.fontSize = 13;
            SetBorder(field, Line);
            // Enter executa (o TextField de linha única não o consome).
            input.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode != KeyCode.Return && e.keyCode != KeyCode.KeypadEnter) return;
                Run(input.value);
                input.value = "";
                input.schedule.Execute(() => input.Focus());
            }, TrickleDown.TrickleDown);
            window.Add(input);
            Print("Type a command or use the buttons. " + Help);
        }

        /// <summary>Abre a consola (gravações e atalhos).</summary>
        public void Open() => SetOpen(true);
        /// <summary>Corre um comando como se tivesse sido escrito.</summary>
        public void Execute(string command) => Run(command);

        public void Update()
        {
            if (restorePending && Time.frameCount > closedFrame)
            {
                restorePending = false;
                GameInput.Suspended = previousSuspended;
            }
            if (Input.GetKeyDown(Key) && (shortcutAvailable == null || shortcutAvailable())) { SetOpen(!open); return; }
            if (!open) return;
            GameInput.Suspended = true;
            if (Input.GetKeyDown(KeyCode.Escape)) { SetOpen(false); return; }
            if (clickMode && Input.GetMouseButtonDown(0) && !PointerOverWindow) KillAtCursor();
        }

        public void Dispose()
        {
            if (IsBlockingInput) GameInput.Suspended = previousSuspended;
            open = restorePending = false;
            layer.RemoveFromHierarchy();
        }

        void SetOpen(bool value)
        {
            if (open == value) return;
            open = value;
            layer.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
            if (open)
            {
                if (!restorePending) previousSuspended = GameInput.Suspended;
                restorePending = false;
                GameInput.Suspended = true;
                layer.BringToFront();
                input.schedule.Execute(() => input.Focus());
            }
            else
            {
                // Mantém o frame do fecho suspenso: o Esc não passa para o jogo.
                restorePending = true;
                closedFrame = Time.frameCount;
                SetClickMode(false);
            }
        }

        void SetClickMode(bool value)
        {
            clickMode = value;
            clickLabel.text = value ? "Click to kill: ON" : "Click to kill";
            clickLabel.style.color = value ? Red : Color.white;
            SetBorder(clickButton, value ? Red : Line);
        }

        // ------------------------------------------------------------------ Comandos

        void Run(string command)
        {
            string c = (command ?? "").Trim();
            if (c.Length == 0) return;
            Print("> " + c);
            string lower = c.ToLowerInvariant();
            if (lower == "help" || lower == "?") { Print(Help); return; }
            if (!lower.StartsWith("kill ")) { Print("Unknown command. " + Help); return; }
            string what = lower.Substring(5).Trim();
            switch (what)
            {
                case "tornado":
                case "tornados":
                    Report(Kill(Tornados()), "tornado");
                    return;
                case "ai":
                case "ais":
                    if (Online()) return;
                    Report(Kill(AIs()), "AI");
                    return;
                case "all":
                    int n = Kill(Tornados());
                    if (AnimLab.Netcode.Unity.NetMatch.Active == null) n += Kill(AIs());
                    Report(n, "object");
                    return;
            }
            if (Online()) return;
            if (what.Length < 3) { Print("Name too short (3+ letters), to avoid killing half the map."); return; }
            Report(Kill(ByName(what)), "object");
        }

        bool Online()
        {
            if (AnimLab.Netcode.Unity.NetMatch.Active == null) return false;
            Print("Not in an online match: fighters belong to the simulation. Tornados can still be cleared.");
            return true;
        }

        void Report(int count, string noun) => Print(count == 0 ? "Nothing to kill." : $"Killed {count} {noun}{(count == 1 ? "" : "s")}.");

        static List<GameObject> Tornados()
        {
            var found = new List<GameObject>();
            foreach (var t in UnityEngine.Object.FindObjectsByType<FireTornado>(FindObjectsSortMode.None)) found.Add(t.gameObject);
            foreach (var t in UnityEngine.Object.FindObjectsByType<WindTornado>(FindObjectsSortMode.None)) found.Add(t.gameObject);
            return found;
        }

        static List<GameObject> AIs()
        {
            var found = new List<GameObject>();
            foreach (var a in UnityEngine.Object.FindObjectsByType<DuelistAI>(FindObjectsSortMode.None)) found.Add(a.gameObject);
            return found;
        }

        // Objetos cujo nome contém o texto; só o de cima de cada ramo (apagar o pai apaga os filhos).
        static List<GameObject> ByName(string text)
        {
            var found = new List<GameObject>();
            foreach (var t in UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
            {
                if (t.name.IndexOf(text, StringComparison.OrdinalIgnoreCase) < 0) continue;
                bool parentMatches = false;
                for (Transform p = t.parent; p != null; p = p.parent)
                    if (p.name.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0) { parentMatches = true; break; }
                if (!parentMatches) found.Add(t.gameObject);
            }
            return found;
        }

        // O Player, a câmara, a interface e a rede nunca se apagam.
        static bool Protected(GameObject go)
        {
            Transform player = GameHud.LocalPlayer<Transform>();
            for (Transform t = go.transform; t != null; t = t.parent)
                if (t == player) return true;
            if (player != null && player.IsChildOf(go.transform)) return true;
            return go.GetComponentInChildren<Camera>(true) != null || go.GetComponentInChildren<UIDocument>(true) != null
                || go.GetComponentInChildren<GameHud>(true) != null || go.GetComponentInChildren<AnimLab.Netcode.Unity.NetMatch>(true) != null;
        }

        static int Kill(List<GameObject> targets)
        {
            int n = 0;
            foreach (GameObject go in targets)
            {
                if (go == null || Protected(go)) continue;
                UnityEngine.Object.Destroy(go);
                n++;
            }
            return n;
        }

        // Clique no mapa: o inimigo, boneco ou tornado mais perto do ponto clicado (até 2,5 m).
        void KillAtCursor()
        {
            Camera cam = Camera.main;
            if (cam == null) return;
            Ray ray = cam.ScreenPointToRay(Input.mousePosition);
            Vector3 at;
            if (Physics.Raycast(ray, out RaycastHit hit, 500f)) at = hit.point;
            else if (new Plane(Vector3.up, Vector3.zero).Raycast(ray, out float d)) at = ray.GetPoint(d);
            else return;

            var candidates = Tornados();
            foreach (Combatant c in Combatant.All) candidates.Add(c.gameObject);
            GameObject best = null;
            float bestD = 2.5f;
            foreach (GameObject go in candidates)
            {
                if (go == null || Protected(go)) continue;
                if (AnimLab.Netcode.Unity.NetMatch.Active != null && go.GetComponent<Combatant>() != null) continue;
                Vector3 to = go.transform.position - at; to.y = 0f;
                if (to.magnitude < bestD) { bestD = to.magnitude; best = go; }
            }
            if (best == null) { Print("Nothing to kill there."); return; }
            Print($"Killed {best.name}.");
            UnityEngine.Object.Destroy(best);
        }

        // ------------------------------------------------------------------ Interface

        void Print(string line)
        {
            lines.Add(line);
            while (lines.Count > 6) lines.RemoveAt(0);
            log.text = string.Join("\n", lines);
        }

        VisualElement Button(string text, Action click)
        {
            var button = new VisualElement();
            button.style.flexGrow = 1;
            button.style.height = 28;
            button.style.marginRight = 6;
            button.style.alignItems = Align.Center;
            button.style.justifyContent = Justify.Center;
            button.style.backgroundColor = Box;
            SetBorder(button, Line);
            if (text.Length > 0) button.Add(hud.MakeLabel(text, 13));
            button.RegisterCallback<PointerEnterEvent>(_ => { if (button != clickButton || !clickMode) SetBorder(button, Gold); });
            button.RegisterCallback<PointerLeaveEvent>(_ => { if (button != clickButton || !clickMode) SetBorder(button, Line); });
            button.RegisterCallback<ClickEvent>(e => { if (e.button == 0) click(); });
            return button;
        }

        // Mesmos cantos dourados das outras janelas.
        void DrawFrame(MeshGenerationContext ctx)
        {
            var p = ctx.painter2D;
            float w = window.layout.width, h = window.layout.height, k = 14f;
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
