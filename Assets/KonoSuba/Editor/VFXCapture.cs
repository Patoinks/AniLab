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
    /// </summary>
    public static class VFXCapture
    {
        const string Flag = "VFXCapture.Active";
        static int frames, index, lastFrame = -1;
        static int casts;
        static RenderTexture hdr, ldr;
        static Texture2D tex;

        [InitializeOnLoadMethod]
        static void Resume() { if (SessionState.GetBool(Flag, false)) EditorApplication.update += Tick; }

        public static void Run()
        {
            // Shaders acabados de mudar compilam em segundo plano e, até lá, os objetos não são desenhados:
            // na captura têm de estar prontos logo no primeiro frame.
            EditorSettings.asyncShaderCompilation = false;
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
            if (++frames < (int)F("-captureWarmup", 40)) return;

            Vector3 c = target.transform.position;
            bool concept = Arg("-captureCamPreset", "") == "ConceptMain";
            cam.fieldOfView = F("-captureFov", 40f);
            cam.transform.position = c + V("-captureOffset", concept ? new Vector3(0f, 5.5f, -11f) : new Vector3(0f, 1.2f, -8f));
            cam.transform.LookAt(c + Vector3.up * F("-captureLook", concept ? 1.2f : 2f));

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

            int count = (int)F("-captureFrames", 120);
            string dir = Arg("-captureOut", "Logs/cap/captura");
            Shot(cam, count == 1 ? dir + ".png" : Path.Combine(dir, "f_" + index.ToString("D4") + ".png"));
            if (++index < count) return;

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
            cam.targetTexture = hdr;
            cam.Render();
            cam.targetTexture = null;
            Graphics.Blit(hdr, ldr);
            RenderTexture.active = ldr;
            tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            File.WriteAllBytes(path, tex.EncodeToPNG());
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
