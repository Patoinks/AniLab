using UnityEngine;
using UnityEngine.UIElements;

namespace AnimLab.KonoSuba
{
    /// <summary>
    /// HUD minimalista (UI Toolkit, construído em código). Tudo o que não é retângulo direito é desenhado em
    /// vetor (Painter2D, com anti-aliasing): barras com brilho, losangos e ornamentos.
    ///   canto superior esquerdo — vida (barra branca) e mana (barra azul) com emblema em losango
    ///   em baixo                — barra de habilidades 1–9 com ornamentos dourados, símbolo de cada habilidade
    ///                             e recarga (AbilityBar preenche isto); arrasta-se para ela a partir da janela V
    ///                             e entre espaços (estilo Metin2), e para fora para tirar
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    [DefaultExecutionOrder(-50)] // Depois do UIDocument (-100), antes dos controlos do jogador.
    public class GameHud : MonoBehaviour
    {
        [Header("Valores")]
        public float health = 100f, maxHealth = 100f;
        public float mana = 80f, maxMana = 100f;

        [Header("Habilidades")]
        [Tooltip("Ícones dos espaços 1–9 (vazio = espaço sem habilidade).")]
        public Texture2D[] abilityIcons = new Texture2D[9];
        public int selectedSlot;

        public enum Glyph { None, Dash, WaterBall, Lightning, Kirin, Fire, Wind }

        readonly Glyph[] glyphs = new Glyph[9];
        readonly float[] cooldowns = new float[9];
        readonly float[] deniedAt = { -9f, -9f, -9f, -9f, -9f, -9f, -9f, -9f, -9f };
        readonly VisualElement[] glyphElements = new VisualElement[9];
        readonly Image[] slotImages = new Image[9];
        readonly bool[] hasIcon = new bool[9];
        readonly VisualElement[] cooldownElements = new VisualElement[9];
        int lastSelected = -1;

        /// <summary>Conteúdo de um espaço: ícone em papel se houver, senão o símbolo vetorial (None = vazio).</summary>
        public void SetSlot(int slot, Texture2D icon, Glyph g)
        {
            if (slot < 0 || slot >= 9) return;
            glyphs[slot] = icon != null ? Glyph.None : g;
            hasIcon[slot] = icon != null;
            if (slotImages[slot] != null)
            {
                slotImages[slot].image = icon;
                slotImages[slot].style.display = icon != null ? DisplayStyle.Flex : DisplayStyle.None;
            }
            glyphElements[slot]?.MarkDirtyRepaint();
        }

        public void SetSlotGlyph(int slot, Glyph g)
        {
            if (slot < 0 || slot >= 9) return;
            glyphs[slot] = g;
            glyphElements[slot]?.MarkDirtyRepaint();
        }

        public void SetCooldown(int slot, float remaining01)
        {
            if (slot < 0 || slot >= 9 || Mathf.Approximately(cooldowns[slot], remaining01)) return;
            cooldowns[slot] = remaining01;
            cooldownElements[slot]?.MarkDirtyRepaint();
        }

        static GameHud instance;
        VisualElement uiRoot;

        // ------------------------------------------------------------------ Arrastar para a hotbar (estilo Metin2)

        AbilityBar bar;
        public AbilityBar Bar => bar != null && bar.name == "Player" ? bar : bar = LocalPlayer<AbilityBar>();
        /// <summary>
        /// Componente do jogador deste PC. Numa partida online existe uma cópia do Player (NetRemote) para o adversário:
        /// procurar "o primeiro CharacterStats" podia apanhar a cópia; o jogador local é sempre o objeto "Player".
        /// </summary>
        public static T LocalPlayer<T>() where T : Component
        {
            var go = GameObject.Find("Player");
            return go != null ? go.GetComponent<T>() : null;
        }
        VisualElement dragGhost;
        Ability dragAbility;
        int dragFrom = -1;
        bool dragArmed, dragging;
        Vector2 dragStart;

        /// <summary>
        /// Torna um elemento numa fonte de arrasto. get() diz que habilidade leva (null = nenhuma);
        /// fromSlot é o espaço da hotbar de onde sai (-1 = vem de uma janela).
        /// Largar num espaço põe-na lá (vinda de outro espaço, trocam); largar fora da hotbar tira-a do espaço.
        /// </summary>
        public void MakeDraggable(VisualElement source, System.Func<Ability> get, int fromSlot)
        {
            source.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button != 0) return;
                Ability a = get();
                if (a == null) return;
                dragAbility = a;
                dragFrom = fromSlot;
                dragStart = e.position;
                dragArmed = true;
                source.CapturePointer(e.pointerId);
            });
            source.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (!dragArmed) return;
                if (!dragging && ((Vector2)e.position - dragStart).magnitude > 6f) StartGhost();
                if (dragging) MoveGhost(e.position);
            });
            source.RegisterCallback<PointerUpEvent>(e =>
            {
                if (!dragArmed) return;
                dragArmed = false;
                source.ReleasePointer(e.pointerId);
                if (dragging) Drop(e.position);
            });
        }

        void StartGhost()
        {
            dragging = true;
            dragGhost = new VisualElement { pickingMode = PickingMode.Ignore };
            dragGhost.style.position = Position.Absolute;
            dragGhost.style.width = dragGhost.style.height = 46;
            dragGhost.style.opacity = 0.85f;
            dragGhost.style.backgroundColor = SlotFill;
            SetBorder(dragGhost, 1.5f, Gold);
            Bar.Visual(dragAbility, out Texture2D icon, out Glyph g);
            if (icon != null)
            {
                var img = new Image { image = icon, scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
                img.style.width = img.style.height = 44;
                dragGhost.Add(img);
            }
            else dragGhost.generateVisualContent += ctx => DrawGlyph(ctx.painter2D, g, 23f, 23f);
            uiRoot.Add(dragGhost);
        }

        void MoveGhost(Vector2 at)
        {
            dragGhost.style.left = at.x - 23f;
            dragGhost.style.top = at.y - 23f;
        }

        void Drop(Vector2 at)
        {
            dragging = false;
            dragGhost?.RemoveFromHierarchy();
            dragGhost = null;
            int target = -1;
            for (int i = 0; i < slots.Length; i++)
                if (slots[i] != null && slots[i].worldBound.Contains(at)) target = i;
            if (target >= 0) Bar.Place(target, dragAbility, dragFrom);
            else if (dragFrom >= 0) Bar.Clear(dragFrom);
            dragAbility = null;
        }

        /// <summary>
        /// O rato está em cima de algo do HUD que recebe cliques (janela de stats, hotbar, barras)?
        /// Aí o clique esquerdo é da interface e não ataca.
        /// </summary>
        public static bool PointerOverUI
        {
            get
            {
                if (instance != null && instance.settingsMenu != null && instance.settingsMenu.IsBlockingInput) return true;
                if (instance != null && instance.onlineMenu != null && instance.onlineMenu.IsBlockingInput) return true;
                if (instance != null && instance.adminConsole != null && instance.adminConsole.IsBlockingInput) return true;
                IPanel panel = instance != null ? instance.GetComponent<UIDocument>().rootVisualElement?.panel : null;
                if (panel == null) return false;
                Vector2 screen = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
                VisualElement picked = panel.Pick(RuntimePanelUtils.ScreenToPanel(panel, screen));
                return picked != null && picked != instance.GetComponent<UIDocument>().rootVisualElement;
            }
        }

        /// <summary>Consola de admin (F6).</summary>
        public AdminConsole Admin => adminConsole;

        /// <summary>Janela das árvores de habilidades (tecla V).</summary>
        public SkillTreeWindow Trees => skillTrees;

        /// <summary>Janela do personagem (tecla C).</summary>
        public StatsWindow Stats => statsWindow;

        /// <summary>Pisca o espaço a vermelho (sem mana ou em recarga).</summary>
        public void Deny(int slot)
        {
            if (slot >= 0 && slot < 9) deniedAt[slot] = Time.time;
        }

        static readonly Color Gold = new Color(0.86f, 0.70f, 0.40f);
        static readonly Color TextColor = new Color(0.92f, 0.93f, 0.96f, 1f);
        static readonly Color HealthColor = new Color(0.97f, 0.98f, 1f);
        static readonly Color ManaColor = new Color(0.36f, 0.64f, 1f);
        static readonly Color SlotBorder = new Color(0.80f, 0.83f, 0.90f, 0.38f);
        static readonly Color SlotFill = new Color(0.03f, 0.045f, 0.075f, 0.6f);

        FontDefinition font;
        bool hasFont; // sem fonte do sistema, fica a do tema por defeito
        GlowBar healthBar, manaBar, staminaBar;
        static readonly Color StaminaColor = new Color(0.74f, 0.9f, 0.46f);
        static readonly Color TiredColor = new Color(1f, 0.5f, 0.32f);
        float stamina01 = 1f;
        CombatOverlay combat;
        StatsWindow statsWindow;
        SettingsMenu settingsMenu;
        AdminConsole adminConsole;
        AnimLab.Netcode.Unity.OnlineMenu onlineMenu;
        SkillTreeWindow skillTrees;
        CharacterStats playerStats;
        Combatant playerBody;
        bool inOnlineMatch;
        Label healthText, manaText;
        readonly VisualElement[] slots = new VisualElement[9];
        readonly VisualElement[] slotGlows = new VisualElement[9];
        readonly Label[] slotKeys = new Label[9];
        readonly System.Collections.Generic.List<VisualElement> glowing = new System.Collections.Generic.List<VisualElement>();

        /// <summary>
        /// Pulsar irregular (0.8–1.2) para os brilhos: dois ruídos de Perlin a velocidades diferentes,
        /// com uma semente por elemento, para nada pulsar em sincronia nem de forma regular.
        /// </summary>
        static float Pulse(float seed)
        {
            float t = Time.time;
            float n = Mathf.PerlinNoise(t * 1.1f, seed) * 0.65f + Mathf.PerlinNoise(t * 3.7f, seed + 17.3f) * 0.35f;
            return 0.8f + 0.4f * n;
        }

        void OnEnable()
        {
            instance = this;
            LoadFont();

            VisualElement root = GetComponent<UIDocument>().rootVisualElement;
            uiRoot = root;
            root.Clear();
            root.pickingMode = PickingMode.Ignore;
            root.style.flexGrow = 1;

            BuildBars(root);
            BuildHotbar(root);
            combat = new CombatOverlay(this, root);
            statsWindow = new StatsWindow(this, root);
            skillTrees = new SkillTreeWindow(this, root);
            settingsMenu = new SettingsMenu(this, root);
            // Consola de admin (F6), se a tecla não estiver atribuída a uma ação do jogo.
            adminConsole = new AdminConsole(this, root, () => !GameInput.IsBound(KeyCode.F6));
            // O atalho novo não rouba uma tecla já configurada para jogar.
            onlineMenu = new AnimLab.Netcode.Unity.OnlineMenu(root, MakeLabel, () =>
                !System.Array.Exists((GameAction[])System.Enum.GetValues(typeof(GameAction)), a => GameInput.Key(a) == KeyCode.F8));
            GameInput.Rebound += RefreshBinding;
            Refresh();
        }

        void OnDisable()
        {
            onlineMenu?.Dispose();
            onlineMenu = null;
            GameInput.Rebound -= RefreshBinding;
            settingsMenu?.Dispose();
            settingsMenu = null;
            adminConsole?.Dispose();
            adminConsole = null;
            combat?.Dispose();
            combat = null;
        }

        void Update()
        {
            settingsMenu?.Update();
            adminConsole?.Update();
            onlineMenu?.Update();
            // As janelas partilham a suspensão sem restaurar o estado umas das outras.
            GameInput.Suspended = (settingsMenu != null && settingsMenu.IsBlockingInput) || (onlineMenu != null && onlineMenu.IsBlockingInput)
                || (adminConsole != null && adminConsole.IsBlockingInput);
            AnimLab.Netcode.Unity.NetMatch.InputBlocked = GameInput.Suspended;
            AnimLab.Netcode.Unity.NetMatch.KeyTaken ??= GameInput.IsBound;
            var net = AnimLab.Netcode.Unity.NetMatch.Active;
            if (net != null && net.Ready)
            {
                health = AnimLab.Netcode.Unity.ViewConvert.Number(net.LocalState.Health);
                maxHealth = AnimLab.Netcode.Unity.ViewConvert.Number(net.LocalState.Config.MaxHealth);
                mana = AnimLab.Netcode.Unity.ViewConvert.Number(net.LocalState.Mana);
                maxMana = AnimLab.Netcode.Unity.ViewConvert.Number(net.LocalState.Config.MaxMana);
                stamina01 = AnimLab.Netcode.Unity.ViewConvert.Number(net.LocalState.Stamina)
                    / Mathf.Max(1f, AnimLab.Netcode.Unity.ViewConvert.Number(net.LocalState.Config.MaxStamina));
                inOnlineMatch = true;
            }
            else if (inOnlineMatch)
            {
                // A partida guardou a ficha do início; o que se mudou durante ela (C, V) entra agora no jogo.
                inOnlineMatch = false;
                if (playerStats == null) playerStats = LocalPlayer<CharacterStats>();
                playerStats?.Apply(true);
            }
            foreach (VisualElement e in glowing) e.MarkDirtyRepaint();
            if (selectedSlot != lastSelected) { lastSelected = selectedSlot; RefreshSlots(); }
            for (int i = 0; i < 9; i++)
                if (Time.time - deniedAt[i] < 0.3f || cooldowns[i] > 0f) StyleSlot(i);
            combat?.Update();
            // Os menus são locais: abrem também numa partida online (as mudanças valem na partida seguinte).
            statsWindow?.Update();
            skillTrees?.Update();
            Refresh();
        }

        // Fonte com serifa do sistema (Cambria tem algarismos alinhados, que se leem melhor que os do Georgia).
        // Gerada com hinting e a uma resolução alta, para o texto pequeno ficar nítido.
        void LoadFont()
        {
            string fonts = System.Environment.GetFolderPath(System.Environment.SpecialFolder.Fonts);
            foreach (string file in new[] { "cambria.ttc", "times.ttf" })
            {
                string path = System.IO.Path.Combine(fonts, file);
                if (!System.IO.File.Exists(path)) continue;
                var asset = UnityEngine.TextCore.Text.FontAsset.CreateFontAsset(path, 0, 72, 8,
                    UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA_HINTED, 1024, 1024);
                if (asset == null) continue;
                font = FontDefinition.FromSDFFont(asset);
                hasFont = true;
                return;
            }
        }

        // ------------------------------------------------------------------ Vida e mana

        void BuildBars(VisualElement root)
        {
            var box = new VisualElement();
            box.style.position = Position.Absolute;
            box.style.left = 30;
            box.style.top = 30;
            root.Add(box);

            box.Add(BarRow(true, out healthBar, out healthText));
            VisualElement manaRow = BarRow(false, out manaBar, out manaText);
            manaRow.style.marginTop = 6;
            box.Add(manaRow);

            // Stamina: barra fina por baixo da mana, alinhada com as outras (sem emblema nem números).
            staminaBar = new GlowBar(StaminaColor, 2f, 8.3f);
            glowing.Add(staminaBar);
            staminaBar.style.width = 180;
            staminaBar.style.height = 14;
            staminaBar.style.marginLeft = 26;
            staminaBar.style.marginTop = 4;
            box.Add(staminaBar);
        }

        VisualElement BarRow(bool isHealth, out GlowBar bar, out Label text)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;

            var emblem = new Emblem(isHealth ? new Color(0.88f, 0.86f, 0.80f) : ManaColor, isHealth, isHealth ? 9.1f : 13.7f);
            glowing.Add(emblem);
            row.Add(emblem);

            bar = new GlowBar(isHealth ? HealthColor : ManaColor, isHealth ? 3f : 2.6f, isHealth ? 1.3f : 5.9f);
            glowing.Add(bar);
            bar.style.width = isHealth ? 228 : 200;
            bar.style.height = 22;
            bar.style.marginLeft = 8;
            row.Add(bar);

            text = MakeLabel("", 14);
            text.style.marginLeft = 12;
            text.style.letterSpacing = 1.2f;
            row.Add(text);
            return row;
        }

        void Refresh()
        {
            // Mana: degradê entre as cores das duas árvores escolhidas (sem árvores, o azul de sempre).
            if (playerStats == null) playerStats = LocalPlayer<CharacterStats>();
            if (playerStats != null && playerStats.ManaColors(out Color a, out Color b)) manaBar.SetColors(a, b);
            else manaBar.SetColors(ManaColor, ManaColor);
            healthBar.SetValue(health / Mathf.Max(maxHealth, 1f));
            manaBar.SetValue(mana / Mathf.Max(maxMana, 1f));
            if (!inOnlineMatch)
            {
                if (playerBody == null) playerBody = LocalPlayer<Combatant>();
                if (playerBody != null) stamina01 = playerBody.Stamina01;
            }
            // Pouca stamina: a barra aquece para avisar que a guarda está por um fio.
            Color sc = stamina01 < 0.25f ? TiredColor : StaminaColor;
            staminaBar.SetColors(sc, sc);
            staminaBar.SetValue(stamina01);
            healthText.text = $"{Mathf.CeilToInt(health)} / {Mathf.CeilToInt(maxHealth)}";
            manaText.text = $"{Mathf.CeilToInt(mana)} / {Mathf.CeilToInt(maxMana)}";
        }

        /// <summary>
        /// Barra fina com brilho: trilho ténue, halo em camadas, núcleo claro e ponta luminosa.
        /// Com duas cores (SetColors) é um degradê ao longo da barra inteira (a cor fica presa à posição, não ao
        /// preenchimento), com uma ondulação lenta a fazer as cores fundirem-se e recuarem.
        /// </summary>
        internal class GlowBar : VisualElement
        {
            Color color, colorB;
            bool gradient;
            readonly float thickness, seed;
            float value = -1f;

            public GlowBar(Color color, float thickness, float seed)
            {
                this.color = colorB = color;
                this.thickness = thickness;
                this.seed = seed;
                pickingMode = PickingMode.Ignore;
                generateVisualContent += Draw;
            }

            public void SetColors(Color a, Color b)
            {
                if (a == color && b == colorB) return;
                color = a;
                colorB = b;
                gradient = a != b;
                MarkDirtyRepaint();
            }

            public void SetValue(float v)
            {
                v = Mathf.Clamp01(v);
                if (Mathf.Approximately(v, value)) return;
                value = v;
                MarkDirtyRepaint();
            }

            void Draw(MeshGenerationContext ctx)
            {
                var p = ctx.painter2D;
                float w = contentRect.width, y = contentRect.height * 0.5f;
                float x0 = thickness, x1 = w - thickness;
                float xf = Mathf.Lerp(x0, x1, Mathf.Max(value, 0f));
                p.lineCap = LineCap.Round;

                Line(p, x0, x1, y, thickness, new Color(1f, 1f, 1f, 0.1f));
                if (value <= 0f) return;

                if (gradient) { DrawGradient(p, x0, x1, xf, y); return; }

                // Halo: traços largos e transparentes por baixo do núcleo, a pulsar de forma irregular.
                float k = Pulse(seed);
                Line(p, x0, xf, y, thickness * 9f * k, new Color(color.r, color.g, color.b, 0.05f * k));
                Line(p, x0, xf, y, thickness * 6f, new Color(color.r, color.g, color.b, 0.09f * k));
                Line(p, x0, xf, y, thickness * 3.6f, new Color(color.r, color.g, color.b, 0.18f * k));
                Line(p, x0, xf, y, thickness * 2f, new Color(color.r, color.g, color.b, 0.38f * k));
                Line(p, x0, xf, y, thickness, color);
                // Fio mais claro no centro, para a barra parecer acesa.
                Line(p, x0, xf, y, thickness * 0.4f, Color.Lerp(color, Color.white, 0.7f));

                // Ponta luminosa no fim do preenchimento.
                float tip = Pulse(seed + 41f);
                p.fillColor = new Color(color.r, color.g, color.b, 0.15f * tip);
                p.BeginPath(); p.Arc(new Vector2(xf, y), thickness * 3.6f * tip, 0, 360); p.Fill();
                p.fillColor = new Color(color.r, color.g, color.b, 0.35f * tip);
                p.BeginPath(); p.Arc(new Vector2(xf, y), thickness * 2f, 0, 360); p.Fill();
                p.fillColor = Color.Lerp(color, Color.white, 0.8f);
                p.BeginPath(); p.Arc(new Vector2(xf, y), thickness * 0.8f, 0, 360); p.Fill();
            }

            // Cor na posição x da barra: degradê suave de color para colorB, com uma ondulação lenta.
            Color At(float x, float x0, float x1)
            {
                float t = Mathf.InverseLerp(x0, x1, x);
                t += Mathf.Sin(Time.time * 0.9f + t * 4f + seed) * 0.07f;
                t = Mathf.Clamp01(t);
                return Color.Lerp(color, colorB, t * t * (3f - 2f * t));
            }

            void DrawGradient(Painter2D p, float x0, float x1, float xf, float y)
            {
                float k = Pulse(seed);
                GradientStroke(p, x0, x1, xf, y, thickness * 9f * k, 0.05f * k, 0f);
                GradientStroke(p, x0, x1, xf, y, thickness * 6f, 0.09f * k, 0f);
                GradientStroke(p, x0, x1, xf, y, thickness * 3.6f, 0.18f * k, 0f);
                GradientStroke(p, x0, x1, xf, y, thickness * 2f, 0.38f * k, 0f);
                GradientStroke(p, x0, x1, xf, y, thickness, 1f, 0f);
                GradientStroke(p, x0, x1, xf, y, thickness * 0.4f, 1f, 0.7f);

                Color c = At(xf, x0, x1);
                float tip = Pulse(seed + 41f);
                p.fillColor = new Color(c.r, c.g, c.b, 0.15f * tip);
                p.BeginPath(); p.Arc(new Vector2(xf, y), thickness * 3.6f * tip, 0, 360); p.Fill();
                p.fillColor = new Color(c.r, c.g, c.b, 0.35f * tip);
                p.BeginPath(); p.Arc(new Vector2(xf, y), thickness * 2f, 0, 360); p.Fill();
                p.fillColor = Color.Lerp(c, Color.white, 0.8f);
                p.BeginPath(); p.Arc(new Vector2(xf, y), thickness * 0.8f, 0, 360); p.Fill();
            }

            // Traço em segmentos de pontas retas (sem sobreposições, que somariam alfa) com meias-luas nas pontas.
            void GradientStroke(Painter2D p, float x0, float x1, float xf, float y, float width, float alpha, float whiten)
            {
                int n = Mathf.Clamp(Mathf.CeilToInt((xf - x0) / 5f), 1, 60);
                p.lineCap = LineCap.Butt;
                p.lineWidth = width;
                for (int i = 0; i < n; i++)
                {
                    float a = Mathf.Lerp(x0, xf, i / (float)n), b = Mathf.Lerp(x0, xf, (i + 1) / (float)n);
                    Color c = Color.Lerp(At((a + b) * 0.5f, x0, x1), Color.white, whiten);
                    p.strokeColor = new Color(c.r, c.g, c.b, alpha);
                    p.BeginPath(); p.MoveTo(new Vector2(a, y)); p.LineTo(new Vector2(b, y)); p.Stroke();
                }
                Color s = Color.Lerp(At(x0, x0, x1), Color.white, whiten), e = Color.Lerp(At(xf, x0, x1), Color.white, whiten);
                p.fillColor = new Color(s.r, s.g, s.b, alpha);
                p.BeginPath(); p.Arc(new Vector2(x0, y), width * 0.5f, 90f, 270f); p.ClosePath(); p.Fill();
                p.fillColor = new Color(e.r, e.g, e.b, alpha);
                p.BeginPath(); p.Arc(new Vector2(xf, y), width * 0.5f, -90f, 90f); p.ClosePath(); p.Fill();
                p.lineCap = LineCap.Round;
            }

            static void Line(Painter2D p, float a, float b, float y, float width, Color c)
            {
                p.strokeColor = c;
                p.lineWidth = width;
                p.BeginPath();
                p.MoveTo(new Vector2(a, y));
                p.LineTo(new Vector2(Mathf.Max(b, a + 0.01f), y));
                p.Stroke();
            }
        }

        /// <summary>Emblema em losango: moldura, miolo e um pequeno brilho por trás.</summary>
        class Emblem : VisualElement
        {
            public Emblem(Color c, bool large, float seed)
            {
                pickingMode = PickingMode.Ignore;
                style.width = 18;
                style.height = 22;
                generateVisualContent += ctx =>
                {
                    var p = ctx.painter2D;
                    var m = new Vector2(9, 11);
                    float r = large ? 6.5f : 5.5f;
                    float k = Pulse(seed);
                    p.fillColor = new Color(c.r, c.g, c.b, 0.12f * k);
                    Diamond(p, m, (r + 3f) * k); p.Fill();
                    p.strokeColor = new Color(c.r, c.g, c.b, 0.9f);
                    p.lineWidth = 1.1f;
                    Diamond(p, m, r); p.Stroke();
                    p.fillColor = c;
                    Diamond(p, m, r * 0.38f); p.Fill();
                };
            }
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

        // ------------------------------------------------------------------ Barra de habilidades

        void BuildHotbar(VisualElement root)
        {
            var bar = new VisualElement();
            bar.style.position = Position.Absolute;
            bar.style.left = 0;
            bar.style.right = 0;
            bar.style.bottom = 36;
            bar.style.flexDirection = FlexDirection.Row;
            bar.style.justifyContent = Justify.Center;
            bar.style.alignItems = Align.FlexStart;
            root.Add(bar);

            bar.Add(new Ornament(false));
            for (int i = 0; i < 9; i++) bar.Add(Slot(i));
            bar.Add(new Ornament(true));
        }

        VisualElement Slot(int index)
        {
            var col = new VisualElement();
            col.style.alignItems = Align.Center;
            col.style.marginLeft = 4;
            col.style.marginRight = 4;

            var holder = new VisualElement();
            holder.style.width = 46;
            holder.style.height = 46;
            col.Add(holder);

            // Brilho dourado à volta do espaço selecionado (desenhado em vetor, por trás do espaço).
            var glow = new VisualElement { pickingMode = PickingMode.Ignore };
            glow.style.position = Position.Absolute;
            glow.style.left = -8; glow.style.top = -8; glow.style.right = -8; glow.style.bottom = -8;
            float slotSeed = 31.7f + index * 4.3f;
            glowing.Add(glow);
            glow.generateVisualContent += ctx =>
            {
                var p = ctx.painter2D;
                float k = Pulse(slotSeed);
                foreach (var (grow, alpha) in new[] { (6f, 0.10f), (4f, 0.18f), (2f, 0.32f) })
                {
                    p.strokeColor = new Color(Gold.r, Gold.g, Gold.b, alpha * k);
                    p.lineWidth = 2f;
                    p.BeginPath();
                    p.MoveTo(new Vector2(8 - grow, 8 - grow));
                    p.LineTo(new Vector2(54 + grow, 8 - grow));
                    p.LineTo(new Vector2(54 + grow, 54 + grow));
                    p.LineTo(new Vector2(8 - grow, 54 + grow));
                    p.ClosePath();
                    p.Stroke();
                }
            };
            holder.Add(glow);
            slotGlows[index] = glow;

            var slot = new VisualElement();
            slot.style.position = Position.Absolute;
            slot.style.left = 0; slot.style.top = 0; slot.style.right = 0; slot.style.bottom = 0;
            slot.style.alignItems = Align.Center;
            slot.style.justifyContent = Justify.Center;
            holder.Add(slot);
            slots[index] = slot;

            // Ícone em papel (escondido se a habilidade não tiver) e, por baixo, o símbolo vetorial (ou um ponto,
            // se o espaço estiver vazio). O AbilityBar preenche os dois com SetSlot.
            var img = new Image { scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
            img.style.width = 44; // o ícone já tem a sua moldura de papel: ocupa o espaço todo
            img.style.height = 44;
            img.style.display = DisplayStyle.None;
            slot.Add(img);
            slotImages[index] = img;
            var glyph = new VisualElement { pickingMode = PickingMode.Ignore };
            glyph.style.position = Position.Absolute;
            glyph.style.left = 0; glyph.style.top = 0; glyph.style.right = 0; glyph.style.bottom = 0;
            glyph.generateVisualContent += ctx => { if (!hasIcon[index]) DrawGlyph(ctx.painter2D, glyphs[index], 23f, 23f); };
            slot.Add(glyph);
            glyphElements[index] = glyph;
            MakeDraggable(slot, () => Bar != null ? Bar.At(index) : null, index);

            // Recarga: véu escuro que desce de cima e vai encolhendo.
            var cd = new VisualElement { pickingMode = PickingMode.Ignore };
            cd.style.position = Position.Absolute;
            cd.style.left = 0; cd.style.top = 0; cd.style.right = 0; cd.style.bottom = 0;
            cd.generateVisualContent += ctx =>
            {
                float k = cooldowns[index];
                if (k <= 0f) return;
                var p = ctx.painter2D;
                float h = 44f * k;
                p.fillColor = new Color(0f, 0f, 0f, 0.6f);
                p.BeginPath();
                p.MoveTo(new Vector2(1, 1)); p.LineTo(new Vector2(45, 1));
                p.LineTo(new Vector2(45, 1 + h)); p.LineTo(new Vector2(1, 1 + h));
                p.ClosePath(); p.Fill();
                p.strokeColor = new Color(Gold.r, Gold.g, Gold.b, 0.7f);
                p.lineWidth = 1f;
                p.BeginPath(); p.MoveTo(new Vector2(1, 1 + h)); p.LineTo(new Vector2(45, 1 + h)); p.Stroke();
            };
            slot.Add(cd);
            cooldownElements[index] = cd;

            var num = MakeLabel(GameInput.Label(GameInput.Key((GameAction)((int)GameAction.Ability1 + index))), 14);
            num.style.marginTop = 7;
            num.style.maxWidth = 54;
            num.style.overflow = Overflow.Hidden;
            num.style.textOverflow = TextOverflow.Ellipsis;
            slotKeys[index] = num;
            col.Add(num);

            StyleSlot(index);
            return col;
        }

        void RefreshBinding(GameAction action, KeyCode key)
        {
            int index = (int)action - (int)GameAction.Ability1;
            if (index >= 0 && index < slotKeys.Length && slotKeys[index] != null)
                slotKeys[index].text = GameInput.Label(key);
        }

        void RefreshSlots()
        {
            for (int i = 0; i < 9; i++) StyleSlot(i);
        }

        void StyleSlot(int i)
        {
            if (slots[i] == null) return;
            bool selected = i == selectedSlot;
            bool denied = Time.time - deniedAt[i] < 0.3f;
            SetBorder(slots[i], selected ? 1.5f : 1f, denied ? new Color(0.9f, 0.3f, 0.25f) : selected ? Gold : SlotBorder);
            slots[i].style.backgroundColor = selected ? new Color(0.14f, 0.11f, 0.06f, 0.62f) : SlotFill;
            slotGlows[i].style.display = selected ? DisplayStyle.Flex : DisplayStyle.None;
        }

        /// <summary>Linha dourada que se desvanece para fora, com losango vazado e losango cheio junto à barra.</summary>
        class Ornament : VisualElement
        {
            public Ornament(bool right)
            {
                pickingMode = PickingMode.Ignore;
                style.width = 96;
                style.height = 46;
                style.marginLeft = right ? 6 : 0;
                style.marginRight = right ? 0 : 6;
                generateVisualContent += ctx =>
                {
                    var p = ctx.painter2D;
                    float y = 23f;
                    float dir = right ? 1f : -1f;
                    float start = right ? 0f : 96f;
                    Vector2 At(float d) => new Vector2(start + dir * d, y);

                    p.strokeColor = new Color(Gold.r, Gold.g, Gold.b, 0.9f);
                    p.lineWidth = 1f;
                    Diamond(p, At(6), 3.6f); p.Stroke();
                    p.fillColor = new Color(Gold.r, Gold.g, Gold.b, 0.95f);
                    Diamond(p, At(16), 2.2f); p.Fill();

                    // Linha em segmentos cada vez mais transparentes (desvanece para fora).
                    p.lineWidth = 1f;
                    for (int s = 0; s < 8; s++)
                    {
                        float a = 22 + s * 9, b = a + 9;
                        p.strokeColor = new Color(Gold.r, Gold.g, Gold.b, 0.85f * (1f - s / 9f));
                        p.BeginPath(); p.MoveTo(At(a)); p.LineTo(At(b)); p.Stroke();
                    }
                };
            }
        }

        // Símbolos das habilidades, desenhados em vetor (sem ícones em imagem por agora).
        static void DrawGlyph(Painter2D p, Glyph g, float cx, float cy)
        {
            var c = new Vector2(cx, cy);
            switch (g)
            {
                case Glyph.None:
                    p.fillColor = new Color(1f, 1f, 1f, 0.6f);
                    p.BeginPath(); p.Arc(c, 1.1f, 0, 360); p.Fill();
                    break;

                case Glyph.Dash:
                    // Duas setas a apontar para a frente e um rasto.
                    p.lineCap = LineCap.Round;
                    p.lineJoin = LineJoin.Round;
                    p.strokeColor = new Color(0.92f, 0.94f, 1f, 0.95f);
                    p.lineWidth = 2f;
                    for (int i = 0; i < 2; i++)
                    {
                        float x = cx - 2f + i * 7f;
                        p.BeginPath();
                        p.MoveTo(new Vector2(x - 5f, cy - 7f));
                        p.LineTo(new Vector2(x + 2f, cy));
                        p.LineTo(new Vector2(x - 5f, cy + 7f));
                        p.Stroke();
                    }
                    p.strokeColor = new Color(0.92f, 0.94f, 1f, 0.45f);
                    p.lineWidth = 1.2f;
                    p.BeginPath(); p.MoveTo(new Vector2(cx - 14f, cy - 3f)); p.LineTo(new Vector2(cx - 9f, cy - 3f)); p.Stroke();
                    p.BeginPath(); p.MoveTo(new Vector2(cx - 15f, cy + 3f)); p.LineTo(new Vector2(cx - 10f, cy + 3f)); p.Stroke();
                    break;

                case Glyph.WaterBall:
                    // Gota com brilho.
                    var water = new Color(0.45f, 0.72f, 1f);
                    p.fillColor = new Color(water.r, water.g, water.b, 0.25f);
                    p.BeginPath(); p.Arc(c + new Vector2(0, 3), 11f, 0, 360); p.Fill();
                    p.fillColor = water;
                    p.BeginPath();
                    p.MoveTo(new Vector2(cx, cy - 11f));
                    p.BezierCurveTo(new Vector2(cx + 3f, cy - 5f), new Vector2(cx + 8f, cy - 1f), new Vector2(cx + 8f, cy + 4f));
                    p.Arc(c + new Vector2(0, 4), 8f, 0, 180);
                    p.BezierCurveTo(new Vector2(cx - 8f, cy - 1f), new Vector2(cx - 3f, cy - 5f), new Vector2(cx, cy - 11f));
                    p.ClosePath();
                    p.Fill();
                    p.fillColor = new Color(1f, 1f, 1f, 0.85f);
                    p.BeginPath(); p.Arc(c + new Vector2(-3f, 3f), 1.8f, 0, 360); p.Fill();
                    break;

                case Glyph.Kirin:
                    // Cabeça de dragão roxa de perfil: cornos recuados, focinho e mandíbula aberta.
                    p.fillColor = new Color(0.65f, 0.3f, 1f, 0.2f);
                    p.BeginPath(); p.Arc(c, 13f, 0, 360); p.Fill();
                    p.strokeColor = new Color(0.76f, 0.5f, 1f);
                    p.lineWidth = 1.8f;
                    p.lineCap = LineCap.Round;
                    p.lineJoin = LineJoin.Round;
                    p.BeginPath();
                    p.MoveTo(c + new Vector2(-10, 10));
                    p.LineTo(c + new Vector2(-7, 0));
                    p.LineTo(c + new Vector2(-12, -10));
                    p.LineTo(c + new Vector2(-3, -5));
                    p.LineTo(c + new Vector2(-3, -13));
                    p.LineTo(c + new Vector2(3, -6));
                    p.LineTo(c + new Vector2(7, -3));
                    p.LineTo(c + new Vector2(13, -1));
                    p.LineTo(c + new Vector2(11, 4));
                    p.LineTo(c + new Vector2(3, 3));
                    p.LineTo(c + new Vector2(6, 8));
                    p.LineTo(c + new Vector2(11, 7));
                    p.LineTo(c + new Vector2(6, 12));
                    p.LineTo(c + new Vector2(-2, 8));
                    p.Stroke();
                    p.strokeColor = new Color(0.96f, 0.88f, 1f);
                    p.lineWidth = 1.5f;
                    p.BeginPath(); p.MoveTo(c + new Vector2(0, -3)); p.LineTo(c + new Vector2(4, -1)); p.Stroke();
                    p.BeginPath(); p.MoveTo(c + new Vector2(8, 3)); p.LineTo(c + new Vector2(7, 6)); p.Stroke();
                    break;

                case Glyph.Fire:
                    // Chama laranja com o centro amarelo, desenhada por curvas contínuas.
                    p.fillColor = new Color(1f, 0.39f, 0.045f);
                    p.BeginPath();
                    p.MoveTo(c + new Vector2(1, -13));
                    p.BezierCurveTo(c + new Vector2(9, -7), c + new Vector2(3, -5), c + new Vector2(8, -1));
                    p.BezierCurveTo(c + new Vector2(14, 5), c + new Vector2(6, 13), c + new Vector2(0, 12));
                    p.BezierCurveTo(c + new Vector2(-12, 12), c + new Vector2(-12, 2), c + new Vector2(-6, -5));
                    p.BezierCurveTo(c + new Vector2(-6, 1), c + new Vector2(0, 0), c + new Vector2(1, -13));
                    p.ClosePath(); p.Fill();
                    p.fillColor = new Color(1f, 0.88f, 0.36f);
                    p.BeginPath();
                    p.MoveTo(c + new Vector2(1, -2));
                    p.BezierCurveTo(c + new Vector2(1, 3), c + new Vector2(7, 6), c + new Vector2(2, 10));
                    p.BezierCurveTo(c + new Vector2(-7, 12), c + new Vector2(-5, 4), c + new Vector2(1, -2));
                    p.ClosePath(); p.Fill();
                    break;

                case Glyph.Wind:
                    // Três arcos de vento em espiral, ciano, com halo.
                    var wind = new Color(0.45f, 1f, 0.88f);
                    p.fillColor = new Color(wind.r, wind.g, wind.b, 0.18f);
                    p.BeginPath(); p.Arc(c, 12f, 0, 360); p.Fill();
                    p.lineCap = LineCap.Round;
                    p.strokeColor = wind;
                    p.lineWidth = 2f;
                    p.BeginPath(); p.Arc(c, 9f, 200f, 330f); p.Stroke();
                    p.strokeColor = new Color(0.85f, 1f, 0.97f);
                    p.lineWidth = 1.6f;
                    p.BeginPath(); p.Arc(c + new Vector2(1f, 1f), 5.5f, 20f, 170f); p.Stroke();
                    p.strokeColor = new Color(wind.r, wind.g, wind.b, 0.7f);
                    p.lineWidth = 1.3f;
                    p.BeginPath(); p.Arc(c + new Vector2(-1f, 0f), 2.5f, 240f, 420f); p.Stroke();
                    break;

                case Glyph.Lightning:
                    // Raio roxo em ziguezague, com halo.
                    var violet = new Color(0.7f, 0.45f, 1f);
                    p.fillColor = new Color(violet.r, violet.g, violet.b, 0.22f);
                    p.BeginPath(); p.Arc(c, 12f, 0, 360); p.Fill();
                    p.fillColor = new Color(0.9f, 0.82f, 1f);
                    p.BeginPath();
                    p.MoveTo(new Vector2(cx + 3f, cy - 12f));
                    p.LineTo(new Vector2(cx - 6f, cy + 1f));
                    p.LineTo(new Vector2(cx - 0.5f, cy + 1f));
                    p.LineTo(new Vector2(cx - 3f, cy + 12f));
                    p.LineTo(new Vector2(cx + 7f, cy - 2f));
                    p.LineTo(new Vector2(cx + 1.5f, cy - 2f));
                    p.ClosePath();
                    p.Fill();
                    break;
            }
        }

        // ------------------------------------------------------------------ Utilitários

        internal Label MakeLabel(string text, int size)
        {
            var l = new Label(text) { pickingMode = PickingMode.Ignore };
            if (hasFont) l.style.unityFontDefinition = font;
            l.style.fontSize = size;
            l.style.color = TextColor;
            // Sombra muito ténue: separa o texto do nevoeiro claro sem parecer contorno.
            l.style.textShadow = new TextShadow { offset = new Vector2(0f, 1f), blurRadius = 2f, color = new Color(0f, 0f, 0f, 0.55f) };
            l.style.paddingLeft = l.style.paddingRight = l.style.paddingTop = l.style.paddingBottom = 0;
            l.style.marginLeft = l.style.marginRight = l.style.marginTop = l.style.marginBottom = 0;
            return l;
        }

        static void SetBorder(VisualElement e, float w, Color c)
        {
            e.style.borderLeftWidth = e.style.borderRightWidth = e.style.borderTopWidth = e.style.borderBottomWidth = w;
            e.style.borderLeftColor = e.style.borderRightColor = e.style.borderTopColor = e.style.borderBottomColor = c;
        }
    }
}
