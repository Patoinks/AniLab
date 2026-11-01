using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;

namespace AnimLab.KonoSuba.EditorTools
{
    /// <summary>
    /// Captura de VFX em batchmode (para mim e para o Codex): reconstrói a cena (opcional), entra em play mode,
    /// aponta a câmara a um objeto e grava N frames 1920×1080 a 30 fps em PNG (+ MP4 se houver ffmpeg).
    /// Exemplo:
    ///   Unity.exe -batchmode -projectPath . -executeMethod AnimLab.KonoSuba.EditorTools.VFXCapture.Run
    ///     -captureTarget FireTest -captureOffset 0,1.2,-8 -captureLook 2 -captureFov 40
    ///     -captureFrames 120 -captureWarmup 40 -captureOut Logs/cap/fogo -captureBuild 1
    /// Uma imagem só: -captureFrames 1. A câmara fica fixa (o Metin2Camera é desligado).
    /// Lançar uma habilidade no primeiro frame gravado: -captureCast KirinAbility [-captureCastTarget DummyEnemy]
    /// (o Player vira-se para o alvo e lança com ele como alvo). Pelo tipo ou pelo nome da habilidade.
    /// Várias, separadas por "|", uma a cada -captureCastEvery frames (45), cada uma no ponto alvo + o seu
    /// desvio em -captureCastOffsets "x,y,z|x,y,z|…" (com desvios, lança no ponto e não no alvo).
    /// Peças só para a captura (não ficam na cena guardada): -captureCall Namespace.Classe.Metodo (estático, sem argumentos).
    /// Cenário neutro: -captureLookdev 1. Câmara: -captureCamPreset ConceptMain (argumentos explícitos prevalecem).
    /// Comparação: -captureCompare imagem [-captureCompareFrame N] (por omissão, o frame do meio).
    /// HUD por cima (placas, números de dano, barras): -captureHud 1.
    /// Todos os estados no alvo da habilidade, no frame N (para ver os ícones): -captureAllStatusAt N.
    /// Player num ponto, virado para o alvo (durante o aquecimento): -capturePlayerAt x,y,z.
    /// Ataques de espada do Player nos frames dados (Espaço): -captureAttacks "10,16,22".
    /// Janela de stats aberta, a passar pelos separadores (um a cada 2 frames): -captureStats 1.
    /// Consola de admin (F6) com comandos em frames: -captureAdmin "30:kill ai|60:kill dummy".
    /// Árvores (tecla V): -captureSkills 1 — escolha Lightning + Fire nos frames 0–5, confirmada no 6, nós
    /// aprendidos no 8; no fim a escolha é desfeita (não fica nas PlayerPrefs do jogo).
    /// Duelista sempre a guardar (sem AI): -captureDuelistGuard 1.
    /// Estilo de espada do Player durante a gravação: -captureStyle Fencer (no fim volta ao Knight).
    /// Salto / roll do Player nos frames dados: -captureJump "10,40" -captureRoll "30". Desligar objetos: -captureDisable "Duelist".
    /// Guarda do Player (F) em intervalos de frames: -captureGuard "30-60,90-95". Semente: -captureSeed 7.
    /// (com -captureAttacks ou -captureGuard o Player deixa de ler o teclado; os callouts vão para o log)
    /// Duelo: -captureBetween Player enquadra o alvo e este objeto de lado (perpendicular à linha entre eles),
    /// a -captureDistance (7) m e -captureHeight (2.2) m; a câmara segue-os suavemente. Golpes vão para o log.
    /// A Bola de Água é canalizada: na captura dispara sozinha -captureWaterHold frames depois (30), solta.
    /// </summary>
    public static class VFXCapture
    {
        const string Flag = "VFXCapture.Active";
        static int frames, index, lastFrame = -1;
        static int casts;
        static Vector3 smoothCam;
        static bool hasSmoothCam;
        static RenderTexture hdr, ldr;
        static Texture2D tex;
        static RenderTexture ui;
        static Texture2D uiTex;
        static WaterBallProjectile pendingWater;
        static LockOnTarget pendingTarget;
        static Vector3 pendingAim;
        static int waterLaunchAt;

        [InitializeOnLoadMethod]
        static void Resume() { if (SessionState.GetBool(Flag, false)) EditorApplication.update += Tick; }

        public static void Run()
        {
            // Shaders acabados de mudar compilam em segundo plano e, até lá, os objetos não são desenhados:
            // na captura têm de estar prontos logo no primeiro frame.
            EditorSettings.asyncShaderCompilation = false;
            SaveProgress();
            bool lookdev = Arg("-captureLookdev", "0") == "1";
            if (Arg("-captureBuild", lookdev ? "0" : "1") == "1") KonoSubaSceneSetup.Build();
            EditorSceneManager.OpenScene("Assets/Scenes/KonoSubaVoid.unity");
            string call = Arg("-captureCall", null);
            if (call != null) Call(call);
            if (lookdev) Lookdev();
            SessionState.SetBool(Flag, true);
            EditorApplication.update += Tick;
            EditorApplication.EnterPlaymode();
        }

        static void Lookdev()
        {
            RenderSettings.fog = false;
            foreach (var volume in UnityEngine.Object.FindObjectsByType<Volume>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (volume.sharedProfile == null) continue;
                var profile = UnityEngine.Object.Instantiate(volume.sharedProfile);
                if (profile.TryGet<GroundFog>(out var fog)) fog.active = false;
                volume.profile = profile;
            }
            foreach (var renderer in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                foreach (var mat in renderer.sharedMaterials)
                    if (mat != null && mat.shader != null && mat.shader.name == "KonoSuba/FogParticle")
                        renderer.enabled = false;
            }
            var floor = GameObject.Find("Floor_CheckerWater");
            if (floor != null)
            {
                var material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                material.color = new Color(0.03f, 0.035f, 0.045f);
                material.SetFloat("_Smoothness", 0.35f);
                floor.GetComponent<Renderer>().sharedMaterial = material;
            }
            var dummy = GameObject.Find("DummyEnemy");
            if (dummy == null) return;
            foreach (var renderer in dummy.GetComponentsInChildren<MeshRenderer>(true)) renderer.enabled = false;
            var human = new GameObject("LookdevHuman");
            human.transform.SetPositionAndRotation(dummy.transform.position, dummy.transform.rotation);
            var bodyMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            bodyMat.color = new Color(0.12f, 0.12f, 0.13f);
            Part(PrimitiveType.Sphere, "Cabeca", human.transform, new Vector3(0f, 1.65f, 0f), new Vector3(0.3f, 0.3f, 0.3f), bodyMat);
            Part(PrimitiveType.Capsule, "Tronco", human.transform, new Vector3(0f, 1.13f, 0f), new Vector3(0.42f, 0.34f, 0.23f), bodyMat);
            foreach (float side in new[] { -1f, 1f })
            {
                Part(PrimitiveType.Capsule, "Braco", human.transform, new Vector3(side * 0.29f, 1.12f, 0f), new Vector3(0.13f, 0.32f, 0.13f), bodyMat);
                Part(PrimitiveType.Capsule, "Perna", human.transform, new Vector3(side * 0.12f, 0.37f, 0f), new Vector3(0.17f, 0.37f, 0.17f), bodyMat);
            }
        }

        static void Part(PrimitiveType type, string name, Transform parent, Vector3 position, Vector3 scale, Material material)
        {
            var part = GameObject.CreatePrimitive(type);
            part.name = name;
            part.transform.SetParent(parent, false);
            part.transform.localPosition = position;
            part.transform.localScale = scale;
            UnityEngine.Object.DestroyImmediate(part.GetComponent<Collider>());
            part.GetComponent<Renderer>().sharedMaterial = material;
        }

        static void Call(string path)
        {
            int dot = path.LastIndexOf('.');
            string typeName = path.Substring(0, dot);
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type t = asm.GetType(typeName);
                if (t == null) continue;
                t.GetMethod(path.Substring(dot + 1), System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static).Invoke(null, null);
                return;
            }
            Debug.LogError("VFXCapture: não encontrei " + path);
        }

        static string Arg(string key, string fallback)
        {
            string[] args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, key);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
        }

        static float F(string key, float fallback) => float.Parse(Arg(key, fallback.ToString(CultureInfo.InvariantCulture)), CultureInfo.InvariantCulture);

        static Vector3 V(string key, Vector3 fallback)
        {
            string s = Arg(key, null);
            return s == null ? fallback : ParseVector(s);
        }

        static Vector3 ParseVector(string s)
        {
            string[] p = s.Split(',');
            return new Vector3(float.Parse(p[0], CultureInfo.InvariantCulture), float.Parse(p[1], CultureInfo.InvariantCulture), float.Parse(p[2], CultureInfo.InvariantCulture));
        }

        static void Tick()
        {
            if (!EditorApplication.isPlaying || Time.frameCount == lastFrame) return;
            lastFrame = Time.frameCount;
            Camera cam = Camera.main;
            GameObject target = GameObject.Find(Arg("-captureTarget", "FireTest"));
            if (cam == null || target == null) return;
            var rig = cam.GetComponent<Metin2Camera>();
            if (rig != null) rig.enabled = false;
            Time.captureFramerate = 30;
            // (cedo, para os pés e a câmara assentarem antes de gravar)
            if (frames == 2 && Arg("-capturePlayerAt", null) != null) PlacePlayer(V("-capturePlayerAt", Vector3.zero));
            if (frames == 1) Scripted();
            if (++frames < (int)F("-captureWarmup", 40)) return;

            Vector3 c = target.transform.position;
            bool concept = Arg("-captureCamPreset", "") == "ConceptMain";
            cam.fieldOfView = F("-captureFov", 40f);
            GameObject second = Arg("-captureBetween", null) != null ? GameObject.Find(Arg("-captureBetween", null)) : null;
            if (second != null)
            {
                Vector3 mid = (c + second.transform.position) * 0.5f;
                Vector3 line = second.transform.position - c; line.y = 0f;
                Vector3 perp = line.sqrMagnitude > 1e-4f ? Vector3.Cross(Vector3.up, line.normalized) : Vector3.right;
                // Fica do lado em que já estava (não salta para o outro lado quando eles trocam de posição).
                if (hasSmoothCam && Vector3.Dot(perp, smoothCam - mid) < 0f) perp = -perp;
                Vector3 want = mid + perp * F("-captureDistance", 7f) + Vector3.up * F("-captureHeight", 2.2f);
                smoothCam = hasSmoothCam ? Vector3.Lerp(smoothCam, want, 0.08f) : want;
                hasSmoothCam = true;
                cam.transform.position = smoothCam;
                cam.transform.LookAt(mid + Vector3.up * F("-captureLook", 1f));
            }
            else
            {
                cam.transform.position = c + V("-captureOffset", concept ? new Vector3(0f, 5.5f, -11f) : new Vector3(0f, 1.2f, -8f));
                cam.transform.LookAt(c + Vector3.up * F("-captureLook", concept ? 1.2f : 2f));
            }

            string list = Arg("-captureCast", null);
            if (list != null)
            {
                string[] names = list.Split('|');
                string[] offsets = Arg("-captureCastOffsets", "").Split('|');
                int every = (int)F("-captureCastEvery", 45);
                if (casts < names.Length && index >= casts * every)
                {
                    Vector3? offset = casts < offsets.Length && offsets[casts].Length > 0 ? ParseVector(offsets[casts]) : (Vector3?)null;
                    Cast(names[casts].Trim(), offset);
                    casts++;
                }
            }

            foreach (string a in Arg("-captureAttacks", "").Split(','))
                if (a.Length > 0 && int.Parse(a) == index)
                    GameObject.Find("Player")?.GetComponent<SwordWielder>()?.RequestAttack();
            string guards = Arg("-captureGuard", null);
            if (guards != null)
            {
                bool held = false;
                foreach (string g in guards.Split(','))
                {
                    string[] r = g.Split('-');
                    if (r.Length == 2 && index >= int.Parse(r[0]) && index <= int.Parse(r[1])) held = true;
                }
                var sw = GameObject.Find("Player")?.GetComponent<SwordWielder>();
                if (sw != null) sw.guardHeld = held;
            }

            if (index == 0)
                foreach (string n in Arg("-captureDisable", "").Split(','))
                    if (n.Length > 0) GameObject.Find(n)?.SetActive(false);
            var mover = GameObject.Find("Player")?.GetComponent<CapsulePlayer>();
            foreach (string a in Arg("-captureJump", "").Split(','))
                if (a.Length > 0 && int.Parse(a) == index && mover != null) mover.Jump();
            foreach (string a in Arg("-captureRoll", "").Split(','))
                if (a.Length > 0 && int.Parse(a) == index && mover != null) mover.Roll(mover.transform.forward);
            if (Arg("-captureSkills", "0") == "1") SkillsStep();
            if (Arg("-captureAdmin", null) != null) AdminStep(Arg("-captureAdmin", ""));
            // Duelista parado a guardar (para testar bloqueios e o Guard Breaker).
            if (Arg("-captureDuelistGuard", "0") == "1")
            {
                GameObject d = GameObject.Find("Duelist");
                if (d != null)
                {
                    var ai = d.GetComponent<DuelistAI>();
                    if (ai != null && ai.enabled) { ai.enabled = false; d.GetComponent<CapsulePlayer>().aiWish = Vector3.zero; }
                    var dw = d.GetComponent<SwordWielder>();
                    if (dw != null) dw.guardHeld = true;
                }
            }
            if (index == 0 && Arg("-captureStyle", null) != null)
                GameHud.LocalPlayer<CharacterStats>()?.SetSwordStyle((SwordStyle)Enum.Parse(typeof(SwordStyle), Arg("-captureStyle", "Knight")));

            if (Arg("-captureStats", "0") == "1")
            {
                var hud = UnityEngine.Object.FindFirstObjectByType<GameHud>();
                if (hud != null && hud.Stats != null)
                {
                    if (!hud.Stats.IsOpen) hud.Stats.SetOpen(true);
                    hud.Stats.SelectTab(Mathf.Min(index / 2, 2));
                }
            }

            if (index == (int)F("-captureAllStatusAt", -1))
            {
                var dummy = GameObject.Find(Arg("-captureCastTarget", "DummyEnemy"));
                var body = dummy != null ? dummy.GetComponent<Combatant>() : null;
                if (body != null)
                    foreach (StatusType st in Enum.GetValues(typeof(StatusType)))
                        if (st != StatusType.None) body.ApplyStatus(st, 8f);
            }

            if (pendingWater != null && index >= waterLaunchAt)
            {
                pendingWater.Launch(pendingTarget, pendingAim, 0f);
                pendingWater = null;
            }

            int count = (int)F("-captureFrames", 120);
            string dir = Arg("-captureOut", "Logs/cap/captura");
            Shot(cam, count == 1 ? dir + ".png" : Path.Combine(dir, "f_" + index.ToString("D4") + ".png"));
            if (++index < count) return;

            if (Arg("-captureSkills", "0") == "1" || Arg("-captureStyle", null) != null) RestoreProgress();
            if (count > 1) Encode(dir);
            string reference = Arg("-captureCompare", null);
            if (reference != null) Compare(dir, count, reference);
            Debug.Log("VFXCapture: " + count + " frames em " + dir);
            SessionState.SetBool(Flag, false);
            EditorApplication.update -= Tick;
            EditorApplication.Exit(0);
        }

        static void Compare(string dir, int count, string reference)
        {
            int frame = (int)F("-captureCompareFrame", count / 2);
            if (frame < 0 || frame >= count) { Debug.LogError("VFXCapture: frame de comparação fora do intervalo"); return; }
            string shot = count == 1 ? dir + ".png" : Path.Combine(dir, "f_" + frame.ToString("D4") + ".png");
            try
            {
                var p = Process.Start(new ProcessStartInfo("ffmpeg",
                    $"-loglevel error -y -i \"{reference}\" -i \"{shot}\" -filter_complex \"[0:v]scale=-2:720,setsar=1[left];[1:v]scale=-2:720,setsar=1[right];[left][right]hstack=inputs=2\" -frames:v 1 \"{dir}_compare.png\"")
                { UseShellExecute = false, CreateNoWindow = true });
                if (p == null || !p.WaitForExit(120000) || p.ExitCode != 0) Debug.LogError("VFXCapture: falhou a comparação");
            }
            catch (Exception e) { Debug.LogError("VFXCapture: falhou a comparação (" + e.Message + ")"); }
        }

        // Consola de admin (F6): "frame:comando|frame:comando"; abre-a no primeiro e escreve cada comando no seu frame.
        static void AdminStep(string plan)
        {
            var hud = UnityEngine.Object.FindFirstObjectByType<GameHud>();
            if (hud == null || hud.Admin == null) return;
            foreach (string step in plan.Split('|'))
            {
                int colon = step.IndexOf(':');
                if (colon <= 0 || !int.TryParse(step.Substring(0, colon), out int at) || at != index) continue;
                hud.Admin.Open();
                hud.Admin.Execute(step.Substring(colon + 1));
            }
        }

        static void SkillsStep()
        {
            var hud = UnityEngine.Object.FindFirstObjectByType<GameHud>();
            var stats = GameHud.LocalPlayer<CharacterStats>();
            if (hud == null || hud.Trees == null || stats == null) return;
            if (index == 0) { stats.ResetTrees(); hud.Trees.SetOpen(true); }
            if (index == 1) hud.Trees.Preview(AbilityElement.Lightning, AbilityElement.Fire);
            if (index == 6) stats.ChooseTrees(AbilityElement.Lightning, AbilityElement.Fire);
            if (index == 8)
                foreach (string id in new[] { "light1", "light2a", "fire1" })
                    foreach (AbilityElement e in new[] { AbilityElement.Lightning, AbilityElement.Fire })
                        foreach (SkillNode n in SkillTrees.Of(e))
                            if (n.id == id) stats.Learn(e, n);
            // (o arrastar para a hotbar faz Place; aqui chama-se direto)
            if (index == 10 && hud.Bar != null)
            {
                hud.Bar.Place(0, hud.Bar.Find("Dash"));
                hud.Bar.Place(1, hud.Bar.Find("Lightning"));
                hud.Bar.Place(2, hud.Bar.Find("Thunder"));
                hud.Bar.Place(3, hud.Bar.Find("Fireball"));
            }
            if (index == 12) hud.Trees.SelectTab(1);
            if (index == 14) stats.SetSwordStyle(SwordStyle.Fencer);
        }

        // No fim: a hotbar, as árvores e o estilo voltam ao que eram (não ficam nas PlayerPrefs do jogo).
        // As preferências de progresso do jogador (hotbar, árvores, estilo) tal como estavam antes da gravação.
        static readonly string[] IntKeys = { "AnimLab.SkillTrees.A", "AnimLab.SkillTrees.B", "AnimLab.SkillTrees.Points", "AnimLab.SkillTrees.Style" };
        static readonly string[] StringKeys = { "AnimLab.SkillTrees.Learned", "AnimLab.Hotbar.0", "AnimLab.Hotbar.1", "AnimLab.Hotbar.2",
            "AnimLab.Hotbar.3", "AnimLab.Hotbar.4", "AnimLab.Hotbar.5", "AnimLab.Hotbar.6", "AnimLab.Hotbar.7", "AnimLab.Hotbar.8" };
        static readonly System.Collections.Generic.Dictionary<string, object> savedProgress = new System.Collections.Generic.Dictionary<string, object>();
        static void SaveProgress()
        {
            savedProgress.Clear();
            foreach (string k in IntKeys) savedProgress[k] = PlayerPrefs.HasKey(k) ? (object)PlayerPrefs.GetInt(k) : null;
            foreach (string k in StringKeys) savedProgress[k] = PlayerPrefs.HasKey(k) ? PlayerPrefs.GetString(k) : null;
        }
        // No fim volta exatamente ao que estava (não "limpa": o jogador pode ter a sua hotbar e as suas árvores).
        static void RestoreProgress()
        {
            foreach (var kv in savedProgress)
            {
                if (kv.Value == null) PlayerPrefs.DeleteKey(kv.Key);
                else if (kv.Value is int i) PlayerPrefs.SetInt(kv.Key, i);
                else PlayerPrefs.SetString(kv.Key, (string)kv.Value);
            }
            PlayerPrefs.Save();
        }

        // Player guiado pela gravação (sem teclado), semente fixa e callouts no log.
        static void Scripted()
        {
            Combat.LogCallouts = true;
            DuelistAI.LogDecisions = true;
            Combatant.AnyHit += (c, h) => Debug.Log($"[HIT] {Time.frameCount} {(h.source != null ? h.source.name : "?")} -> {c.name} {h.damage:0.#}{(h.critical ? " crit" : "")} hp {c.health:0}/{c.maxHealth:0}{(c.IsStaggered ? " STAGGER" : "")}");
            SwordWielder.AnyGuarded += (a, d, parry) => Debug.Log($"[GUARD] {Time.frameCount} {d.name} {(parry ? "PARRY" : "BLOCK")} {a.name}");
            SwordWielder.AnyClash += (a, b) => Debug.Log($"[CLASH] {Time.frameCount} {a.name} x {b.name}");
            if (Arg("-captureSeed", null) != null) UnityEngine.Random.InitState((int)F("-captureSeed", 1));
            if (Arg("-captureAttacks", null) == null && Arg("-captureGuard", null) == null) return;
            var sw = GameObject.Find("Player")?.GetComponent<SwordWielder>();
            if (sw != null) sw.playerControlled = false;
        }

        static void PlacePlayer(Vector3 at)
        {
            GameObject player = GameObject.Find("Player");
            if (player == null) return;
            var cc = player.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            player.transform.position = at;
            GameObject t = GameObject.Find(Arg("-captureCastTarget", "DummyEnemy"));
            if (t != null)
            {
                Vector3 d = t.transform.position - at; d.y = 0f;
                if (d.sqrMagnitude > 0.01f) player.transform.rotation = Quaternion.LookRotation(d);
                // Alvo selecionado: a guarda vira-se para ele.
                var lockOn = player.GetComponent<LockOn>();
                var lt = t.GetComponent<LockOnTarget>();
                if (lockOn != null && lt != null) lockOn.Select(lt);
            }
            if (cc != null) cc.enabled = true;
        }

        static void Cast(string name, Vector3? offset)
        {
            GameObject player = GameObject.Find("Player");
            if (player == null) return;
            GameObject t = GameObject.Find(Arg("-captureCastTarget", "DummyEnemy"));
            LockOnTarget lockOn = t != null && offset == null ? t.GetComponentInChildren<LockOnTarget>() : null;
            Vector3 aim = (t != null ? t.transform.position : player.transform.position + player.transform.forward * 8f) + (offset ?? Vector3.zero);
            Vector3 d = aim - player.transform.position;
            d.y = 0f;
            if (d.sqrMagnitude > 0.01f) player.transform.rotation = Quaternion.LookRotation(d);
            foreach (Ability a in player.GetComponents<Ability>())
                if (a.GetType().Name == name || a.displayName == name)
                {
                    Debug.Log("VFXCapture: " + name + " lançado: "
                              + a.TryCast(new AbilityContext { caster = player.transform, target = lockOn, aimPoint = aim }));
                    if (a is WaterBallAbility)
                    {
                        pendingWater = UnityEngine.Object.FindFirstObjectByType<WaterBallProjectile>();
                        pendingTarget = lockOn;
                        pendingAim = aim + Vector3.up * 1.1f;
                        waterLaunchAt = index + (int)F("-captureWaterHold", 30);
                    }
                    return;
                }
        }

        static void Shot(Camera cam, string path)
        {
            const int W = 1920, H = 1080;
            if (hdr == null)
            {
                hdr = new RenderTexture(W, H, 24, RenderTextureFormat.DefaultHDR);
                ldr = new RenderTexture(W, H, 0);
                tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            }
            // A câmara fica a apontar para o alvo da captura: assim os efeitos que leem o tamanho da câmara
            // (ex.: o reflexo da água, criado no LateUpdate) usam 1920×1080 e não o tamanho da janela do batchmode.
            cam.targetTexture = hdr;
            cam.Render();
            Graphics.Blit(hdr, ldr);
            RenderTexture.active = ldr;
            tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            if (Arg("-captureHud", "0") == "1") CompositeHud(W, H);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            File.WriteAllBytes(path, tex.EncodeToPNG());
        }

        // O painel do HUD passa a desenhar numa textura (cópia das PanelSettings, para não mexer no asset);
        // a textura traz o frame anterior do HUD, composto por cima da imagem (alfa pré-multiplicado).
        static void CompositeHud(int w, int h)
        {
            if (ui == null)
            {
                var doc = UnityEngine.Object.FindFirstObjectByType<UnityEngine.UIElements.UIDocument>();
                if (doc == null || doc.panelSettings == null) return;
                ui = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                uiTex = new Texture2D(w, h, TextureFormat.RGBA32, false);
                var settings = UnityEngine.Object.Instantiate(doc.panelSettings);
                settings.targetTexture = ui;
                settings.clearColor = true;
                settings.colorClearValue = Color.clear;
                doc.panelSettings = settings;
                return; // o painel só desenha na textura a partir do próximo frame
            }
            RenderTexture.active = ui;
            uiTex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            uiTex.Apply();
            RenderTexture.active = null;
            Color32[] a = tex.GetPixels32(), b = uiTex.GetPixels32();
            for (int i = 0; i < a.Length; i++)
            {
                int k = 255 - b[i].a;
                if (k == 255) continue;
                a[i].r = (byte)Mathf.Min(255, b[i].r + a[i].r * k / 255);
                a[i].g = (byte)Mathf.Min(255, b[i].g + a[i].g * k / 255);
                a[i].b = (byte)Mathf.Min(255, b[i].b + a[i].b * k / 255);
            }
            tex.SetPixels32(a);
            tex.Apply();
        }

        // MP4 ao lado da pasta dos frames, se o ffmpeg estiver no PATH.
        static void Encode(string dir)
        {
            try
            {
                var p = Process.Start(new ProcessStartInfo("ffmpeg",
                    $"-loglevel error -y -framerate 30 -i \"{dir}/f_%04d.png\" -c:v libx264 -pix_fmt yuv420p -crf 18 \"{dir}.mp4\"")
                { UseShellExecute = false, CreateNoWindow = true });
                p?.WaitForExit(120000);
            }
            catch (Exception e) { Debug.Log("VFXCapture: sem ffmpeg (" + e.Message + ")"); }
        }
    }
}
