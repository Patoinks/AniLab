using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace AnimLab.KonoSuba.EditorTools
{
    /// <summary>
    /// Monta o cenário do pós-vida de KonoSuba (ep. 1): vazio estrelado, chão axadrezado com película de água,
    /// nevoeiro rasteiro em partículas, brilhos a flutuar, luz central e pós-processamento.
    /// Menu: AnimLab > Build KonoSuba Scene. Batchmode: -executeMethod AnimLab.KonoSuba.EditorTools.KonoSubaSceneSetup.BuildBatch
    /// </summary>
    public static class KonoSubaSceneSetup
    {
        const string Root = "Assets/KonoSuba";
        const string TexDir = Root + "/Textures";
        const string MatDir = Root + "/Materials";
        const string ScenePath = "Assets/Scenes/KonoSubaVoid.unity";

        static readonly Color FogColor = new Color(0.045f, 0.05f, 0.06f);
        const int WaterLayer = 4; // layer built-in "Water"

        [MenuItem("AnimLab/Build KonoSuba Scene")]
        public static void Build()
        {
            URPSetup.EnsurePipeline();
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // Apaga tudo o que foi gerado antes: reescrever por cima de assets existentes com o editor
            // aberto deixava os materiais sem textura (partículas a aparecer como retângulos).
            foreach (string generated in new[] { MatDir, TexDir, Root + "/KonoSubaPostProcess.asset", URPSetup.VolumeProfilePath, Root + "/HudPanel.asset",
                         Root + "/Floor_CheckerWater.prefab", Root + "/CrystalMesh.asset", Root + "/HaloQuad.asset" })
                AssetDatabase.DeleteAsset(generated);

            Directory.CreateDirectory(TexDir);
            Directory.CreateDirectory(MatDir);
            Directory.CreateDirectory("Assets/Scenes");
            AssetDatabase.Refresh();

            Texture2D fogShape = SaveTexture("FogShape", 256, FogShapePixel, TextureWrapMode.Clamp);
            Texture2D fogNoise = SaveTexture("FogNoise", 256, TileableNoisePixel, TextureWrapMode.Repeat);
            Texture2D sparkle = SaveTexture("Sparkle", 128, SparklePixel, TextureWrapMode.Clamp);
            Texture2D mote = SaveTexture("Mote", 64, MotePixel, TextureWrapMode.Clamp);

            SetupEnvironment();
            BuildFloor(mote, fogShape, fogNoise);
            BuildLights();
            BuildFog(fogShape, fogNoise);
            BuildSparkles(sparkle, mote);
            BuildCamera(fogNoise);
            Transform player = BuildPlayer();
            BuildFogInteraction(player);
            BuildWaterSim(player);
            BuildHud();

            EditorSceneManager.SaveScene(scene, ScenePath);
            var scenes = new System.Collections.Generic.List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            if (!scenes.Exists(s => s.path == ScenePath)) scenes.Add(new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            AssetDatabase.SaveAssets();
            Debug.Log("KonoSuba: cena criada em " + ScenePath);
        }

        public static void BuildBatch()
        {
            try { Build(); EditorApplication.Exit(0); }
            catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }

        // ---------------------------------------------------------------- Ambiente

        static void SetupEnvironment()
        {
            Material sky = SaveMaterial("VoidSky", "KonoSuba/VoidSky");
            sky.SetColor("_HorizonColor", FogColor * 1.6f);
            RenderSettings.skybox = sky;

            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.055f, 0.062f, 0.085f);
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
            RenderSettings.reflectionIntensity = 1f;

            // Fog exponencial: esconde o fim do chão e funde-o com a faixa do horizonte.
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = FogColor;
            RenderSettings.fogDensity = 0.022f;
        }

        static void BuildFloor(Texture2D mote, Texture2D fogShape, Texture2D fogNoise)
        {
            GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Floor_CheckerWater";
            floor.transform.localScale = new Vector3(40f, 1f, 40f); // 400 x 400 m, o fog esconde as bordas
            floor.GetComponent<MeshRenderer>().sharedMaterial = SaveMaterial("CheckerWaterFloor", "KonoSuba/CheckerWaterFloor");
            floor.isStatic = true;
            floor.layer = WaterLayer;
            floor.AddComponent<PlanarReflection>();

            // Ondas de choque ao pisar: shader + salpico em três camadas, filhos do chão para irem no prefab.
            var ripples = floor.AddComponent<WaterRipples>();
            Material dropMat = SaveMaterial("SplashDroplet", "KonoSuba/SparkleAdditive");
            dropMat.SetTexture("_MainTex", mote);
            dropMat.SetFloat("_Intensity", 0.55f); // secundário: lê-se como água, não como luz
            Material mistMat = FogMaterial("SplashMist", fogShape, fogNoise, new Color(0.6f, 0.64f, 0.7f, 0.14f), 0.3f);
            mistMat.SetFloat("_CameraFadeStart", 0.3f);
            mistMat.SetFloat("_CameraFadeEnd", 1.5f);

            ripples.impactVfx = new[]
            {
                new WaterRipples.ImpactVfx { system = SplashDroplets(floor.transform, dropMat), count = 12 },
                new WaterRipples.ImpactVfx { system = SplashCrown(floor.transform, dropMat), count = 9 },
                new WaterRipples.ImpactVfx { system = SplashMist(floor.transform, mistMat), count = 2 },
            };

            PrefabUtility.SaveAsPrefabAssetAndConnect(floor, Root + "/Floor_CheckerWater.prefab", InteractionMode.AutomatedAction);
        }

        // Gotas atiradas para cima que caem e desaparecem ao tocar na água.
        static ParticleSystem SplashDroplets(Transform parent, Material mat)
        {
            ParticleSystem ps = BurstOnlySystem("SplashDroplets", parent, mat, 400);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 0.85f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.0f, 2.1f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.008f, 0.018f);
            main.gravityModifier = 1.1f;
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.6f, 0.68f, 0.76f, 0.55f), new Color(0.75f, 0.8f, 0.86f, 0.55f));

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 30f;
            shape.radius = 0.06f;
            shape.rotation = new Vector3(-90f, 0f, 0f); // cone a apontar para cima

            var sol = ps.sizeOverLifetime;
            sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.6f));
            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = FadeInOut(Color.white, 0.02f, 0.75f);

            DieOnWater(ps);
            Streak(ps, 0.03f);
            return ps;
        }

        // Coroa: gotas mais baixas atiradas para os lados, rente à água.
        static ParticleSystem SplashCrown(Transform parent, Material mat)
        {
            ParticleSystem ps = BurstOnlySystem("SplashCrown", parent, mat, 400);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.45f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 1.1f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.007f, 0.014f);
            main.gravityModifier = 0.6f;
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.6f, 0.68f, 0.76f, 0.45f), new Color(0.75f, 0.8f, 0.86f, 0.45f));

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 0.12f;
            shape.radiusThickness = 0f;
            shape.rotation = new Vector3(90f, 0f, 0f); // círculo deitado, a emitir para fora

            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(0f, 0f);
            vel.y = new ParticleSystem.MinMaxCurve(0.6f, 1.1f);
            vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);

            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = FadeInOut(Color.white, 0.02f, 0.6f);

            DieOnWater(ps);
            Streak(ps, 0.03f);
            return ps;
        }

        // Spray: nuvem fina de água pulverizada que se expande e dissipa.
        static ParticleSystem SplashMist(Transform parent, Material mat)
        {
            ParticleSystem ps = BurstOnlySystem("SplashMist", parent, mat, 64);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.7f, 1.2f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.15f, 0.4f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.35f, 0.7f);

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Hemisphere;
            shape.radius = 0.1f;
            shape.rotation = new Vector3(-90f, 0f, 0f);

            var sol = ps.sizeOverLifetime;
            sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.5f, 1f, 1.8f));
            var rot = ps.rotationOverLifetime;
            rot.enabled = true;
            rot.z = new ParticleSystem.MinMaxCurve(-0.5f, 0.5f);
            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = FadeInOut(Color.white, 0.08f, 0.4f);
            return ps;
        }

        static void DieOnWater(ParticleSystem ps)
        {
            var c = ps.collision;
            c.enabled = true;
            c.type = ParticleSystemCollisionType.World;
            c.mode = ParticleSystemCollisionMode.Collision3D;
            c.collidesWith = 1 << WaterLayer;
            c.lifetimeLoss = 1f;
            c.bounce = 0f;
            c.dampen = 0f;
            c.radiusScale = 0.5f;
            c.quality = ParticleSystemCollisionQuality.High;
        }

        // Gotas esticadas pela velocidade, para lerem como riscos de água em movimento.
        static void Streak(ParticleSystem ps, float velocityScale)
        {
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Stretch;
            r.velocityScale = velocityScale;
            r.lengthScale = 1f;
        }

        // Sistema sem emissão própria: só emite quando WaterRipples chama Emit().
        static ParticleSystem BurstOnlySystem(string name, Transform parent, Material mat, int max)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.loop = true;
            main.playOnAwake = true;
            main.startSpeed = 0f;
            main.maxParticles = max;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Local; // ignora a escala 40x do chão
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);

            var emission = ps.emission;
            emission.rateOverTime = 0f;
            var shape = ps.shape;
            shape.enabled = false;

            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            return ps;
        }

        static void BuildLights()
        {
            // Luz fria e fraca de cima para dar forma ao nevoeiro.
            var moon = new GameObject("MoonLight").AddComponent<Light>();
            moon.type = LightType.Directional;
            moon.color = new Color(0.55f, 0.62f, 0.8f);
            moon.intensity = 0.12f; // Linear (URP): valores abaixo de 1 rendem mais do que em Gamma
            moon.shadows = LightShadows.None;
            moon.transform.rotation = Quaternion.Euler(65f, 20f, 0f);

            // Foco central: a mancha clara no chão onde ficam as cadeiras.
            var spot = new GameObject("CenterSpotLight").AddComponent<Light>();
            spot.type = LightType.Spot;
            spot.color = new Color(0.82f, 0.88f, 1f);
            spot.intensity = 120f; // URP: queda física (inverso do quadrado) a 16 m de altura
            spot.range = 40f;
            spot.spotAngle = 75f;
            spot.innerSpotAngle = 20f;
            spot.shadows = LightShadows.Soft;
            spot.transform.SetPositionAndRotation(new Vector3(0f, 16f, 0f), Quaternion.Euler(90f, 0f, 0f));
        }

        // ---------------------------------------------------------------- Nevoeiro

        static void BuildFog(Texture2D shape, Texture2D noise)
        {
            var root = new GameObject("FogVFX").transform;

            // Na referência o nevoeiro é estratificado: faixas horizontais finas e compridas,
            // translúcidas perto da câmara e a acumular numa faixa clara a meia distância.

            // O nevoeiro rasteiro é o efeito de ecrã GroundFog (ver BuildCamera).
            // Só fica um card, o véu do horizonte, ao longe onde não se nota que é plano.

            // Faixa clara do horizonte: muito larga e lisa (sem ruído), por cima do fim do chão.
            Material veil = FogMaterial("Fog_Veil", shape, noise, new Color(0.40f, 0.43f, 0.49f, 0.18f), 4f);
            veil.SetFloat("_NoiseAmount", 0f);
            veil.SetFloat("_Distortion", 0f);
            veil.SetFloat("_DistortSpeed", 0.04f);
            veil.SetColor("_GlowColor", Color.black);
            ParticleSystem far = FogSystem("HorizonVeil", root, veil, ParticleSystemRenderMode.Billboard,
                count: 50, area: Vector3.one, height: 2.5f,
                sizeX: new Vector2(80f, 130f), sizeY: new Vector2(6f, 12f), rotationRange: 0.02f, drift: 0.15f);
            Ring(far, 75f, 30f, 3f);
            var veilSheet = far.textureSheetAnimation;
            veilSheet.enabled = false; // forma suave única, sem os riscos do atlas
        }

        // Emite num anel à volta do centro (as faixas ficam a meia distância, nunca em cima da câmara).
        static void Ring(ParticleSystem ps, float radius, float thickness, float heightSpread)
        {
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Donut;
            shape.radius = radius;
            shape.donutRadius = thickness;
            shape.rotation = new Vector3(90f, 0f, 0f);
            shape.scale = new Vector3(1f, 1f, heightSpread / Mathf.Max(thickness, 0.01f));
        }

        static Material FogMaterial(string name, Texture2D shape, Texture2D noise, Color tint, float soft)
        {
            Material m = SaveMaterial(name, "KonoSuba/FogParticle");
            m.SetTexture("_MainTex", shape);
            m.SetTexture("_NoiseTex", noise);
            m.SetColor("_Tint", tint);
            m.SetFloat("_SoftDistance", soft);
            return m;
        }

        static ParticleSystem FogSystem(string name, Transform parent, Material mat, ParticleSystemRenderMode mode,
                                        int count, Vector3 area, float height, Vector2 sizeX, Vector2 sizeY,
                                        float rotationRange, float drift)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, height, 0f);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.loop = true;
            main.prewarm = true;
            main.duration = 30f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(28f, 40f);
            main.startSpeed = 0f;
            // Tamanho 3D: faixas compridas e baixas em vez de manchas redondas.
            main.startSize3D = true;
            main.startSizeX = new ParticleSystem.MinMaxCurve(sizeX.x, sizeX.y);
            main.startSizeY = new ParticleSystem.MinMaxCurve(sizeY.x, sizeY.y);
            main.startSizeZ = 1f;
            // Rotação quase nula: todas as faixas alinhadas com o vento.
            main.startRotation = new ParticleSystem.MinMaxCurve(-rotationRange, rotationRange);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 1f, 1f, 0.7f), Color.white);
            main.maxParticles = count;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var emission = ps.emission;
            emission.rateOverTime = count / 34f;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = area;

            // Deriva lenta ao longo do comprimento das faixas.
            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(drift * 0.6f, drift);
            vel.y = new ParticleSystem.MinMaxCurve(0f, 0f);
            vel.z = new ParticleSystem.MinMaxCurve(-drift * 0.1f, drift * 0.1f);

            // Vaguear orgânico: ruído turbulento mexe na posição, roda ligeiramente e faz "respirar" o tamanho,
            // por isso cada fiapo serpenteia em vez de seguir uma linha reta.
            var turb = ps.noise;
            turb.enabled = true;
            turb.strength = drift * 1.2f;
            turb.frequency = 0.035f;
            turb.scrollSpeed = 0.04f;
            turb.octaveCount = 2;
            turb.damping = true;
            turb.quality = ParticleSystemNoiseQuality.Medium;
            turb.positionAmount = 1f;
            turb.rotationAmount = 0.15f;
            turb.sizeAmount = 0.25f;

            // Cresce ao longo da vida, como fumo que se espalha e dissipa.
            var size01 = ps.sizeOverLifetime;
            size01.enabled = true;
            size01.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.7f, 1f, 1.35f));

            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = FadeInOut(Color.white, 0.25f, 0.75f);

            // Cada partícula usa um dos 4 fiapos do atlas, fixo durante a vida.
            var sheet = ps.textureSheetAnimation;
            sheet.enabled = true;
            sheet.numTilesX = 2;
            sheet.numTilesY = 2;
            sheet.frameOverTime = new ParticleSystem.MinMaxCurve(0f);
            sheet.startFrame = new ParticleSystem.MinMaxCurve(0f, 3.999f);

            var r = go.GetComponent<ParticleSystemRenderer>();
            r.renderMode = mode;
            r.sharedMaterial = mat;
            r.sortMode = ParticleSystemSortMode.Distance;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.maxParticleSize = 5f;
            return ps;
        }

        // ---------------------------------------------------------------- Brilhos

        static void BuildSparkles(Texture2D sparkle, Texture2D mote)
        {
            Material moteMat = SaveMaterial("Mote", "KonoSuba/SparkleAdditive");
            moteMat.SetTexture("_MainTex", mote);
            moteMat.SetFloat("_Intensity", 1.1f); // abaixo do limiar do bloom: ambiente, não destaque
            var root = new GameObject("SparklesVFX").transform;

            // Poeira luminosa: pontos pequenos e nítidos à volta da zona central.
            MoteSystem("GlowMotes", root, moteMat, 110, new Vector3(60f, 10f, 60f), 5f, new Vector2(0.02f, 0.05f));
            // Pontos dispersos ao longe, maiores: lêem-se como estrelas baixas no vazio.
            MoteSystem("FarMotes", root, moteMat, 90, new Vector3(180f, 25f, 180f), 10f, new Vector2(0.06f, 0.14f));

            BuildCrystals(root, sparkle);
        }

        static void MoteSystem(string name, Transform parent, Material mat, int count, Vector3 area, float height, Vector2 size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, height, 0f);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.loop = true;
            main.prewarm = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(8f, 14f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(size.x, size.y);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.5f, 0.82f, 1f), new Color(0.85f, 0.95f, 1f));
            main.maxParticles = count;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var emission = ps.emission;
            emission.rateOverTime = count / 11f;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = area;

            // Sobem muito devagar, com um leve vaguear.
            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(0f, 0f);
            vel.y = new ParticleSystem.MinMaxCurve(0.02f, 0.1f);
            vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.12f;
            noise.frequency = 0.1f;
            noise.scrollSpeed = 0.05f;

            // Cintilar na opacidade (o tamanho fica fixo): vidas diferentes desincronizam os pontos.
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.1f), new GradientAlphaKey(0.3f, 0.25f),
                              new GradientAlphaKey(0.95f, 0.4f), new GradientAlphaKey(0.4f, 0.56f), new GradientAlphaKey(1f, 0.72f),
                              new GradientAlphaKey(0f, 1f) });
            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = new ParticleSystem.MinMaxGradient(g);

            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
        }

        // Cristais facetados a flutuar (o losango azul da referência), cada um com um halo.
        static void BuildCrystals(Transform parent, Texture2D sparkle)
        {
            Mesh mesh = CrystalMesh();
            AssetDatabase.CreateAsset(mesh, Root + "/CrystalMesh.asset");
            Mesh haloMesh = HaloQuadMesh();
            AssetDatabase.CreateAsset(haloMesh, Root + "/HaloQuad.asset");

            Material crystalMat = SaveMaterial("Crystal", "KonoSuba/Crystal");
            crystalMat.SetFloat("_Intensity", 1.1f);
            crystalMat.SetFloat("_Alpha", 0.7f);
            Material haloMat = SaveMaterial("CrystalHalo", "KonoSuba/SparkleAdditive");
            haloMat.SetTexture("_MainTex", sparkle);
            haloMat.SetFloat("_Intensity", 0.45f);

            var rng = new System.Random(7);
            for (int i = 0; i < 3; i++)
            {
                float ang = (float)rng.NextDouble() * Mathf.PI * 2f;
                float dist = Mathf.Lerp(10f, 30f, (float)rng.NextDouble());
                float scale = Mathf.Lerp(0.45f, 0.7f, (float)rng.NextDouble());

                var crystal = new GameObject("Crystal_" + i);
                crystal.transform.SetParent(parent, false);
                crystal.transform.position = new Vector3(Mathf.Cos(ang) * dist, Mathf.Lerp(1.2f, 6f, (float)rng.NextDouble()), Mathf.Sin(ang) * dist);
                crystal.transform.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);

                var body = new GameObject("Body");
                body.transform.SetParent(crystal.transform, false);
                body.transform.localScale = new Vector3(0.11f, 0.2f, 0.11f) * scale;
                body.AddComponent<MeshFilter>().sharedMesh = mesh;
                var br = body.AddComponent<MeshRenderer>();
                br.sharedMaterial = crystalMat;
                br.shadowCastingMode = ShadowCastingMode.Off;

                var halo = new GameObject("Halo");
                halo.transform.SetParent(crystal.transform, false);
                halo.transform.localScale = Vector3.one * 0.6f * scale;
                halo.AddComponent<MeshFilter>().sharedMesh = haloMesh;
                var hr = halo.AddComponent<MeshRenderer>();
                hr.sharedMaterial = haloMat;
                hr.shadowCastingMode = ShadowCastingMode.Off;

                var fc = crystal.AddComponent<FloatingCrystal>();
                fc.halo = halo.transform;
                fc.spinSpeed = Mathf.Lerp(12f, 30f, (float)rng.NextDouble());
            }
        }

        // Bipirâmide hexagonal com normais planas (cada face com o seu tom).
        static Mesh CrystalMesh()
        {
            const int sides = 6;
            var ring = new Vector3[sides];
            for (int i = 0; i < sides; i++)
            {
                float a = i * Mathf.PI * 2f / sides;
                ring[i] = new Vector3(Mathf.Cos(a), 0.15f, Mathf.Sin(a));
            }
            Vector3 top = Vector3.up, bottom = Vector3.down;

            var verts = new System.Collections.Generic.List<Vector3>();
            var normals = new System.Collections.Generic.List<Vector3>();
            void Tri(Vector3 a, Vector3 b, Vector3 c)
            {
                Vector3 n = Vector3.Cross(b - a, c - a).normalized;
                if (Vector3.Dot(n, (a + b + c) / 3f) < 0f) { (b, c) = (c, b); n = -n; } // garante a face para fora
                verts.Add(a); verts.Add(b); verts.Add(c);
                normals.Add(n); normals.Add(n); normals.Add(n);
            }
            for (int i = 0; i < sides; i++)
            {
                Vector3 r0 = ring[i], r1 = ring[(i + 1) % sides];
                Tri(top, r0, r1);
                Tri(bottom, r1, r0);
            }

            var mesh = new Mesh { name = "Crystal" };
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            var idx = new int[verts.Count];
            for (int i = 0; i < idx.Length; i++) idx[i] = i;
            mesh.SetTriangles(idx, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        // Quad com cores de vértice brancas (o shader aditivo multiplica pela cor do vértice).
        static Mesh HaloQuadMesh()
        {
            var mesh = new Mesh { name = "HaloQuad" };
            mesh.vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f) };
            mesh.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
            mesh.colors = new[] { Color.white, Color.white, Color.white, Color.white };
            mesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        static ParticleSystem.MinMaxGradient FadeInOut(Color c, float inEnd, float outStart)
        {
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(c, 0f), new GradientColorKey(c, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, inEnd),
                              new GradientAlphaKey(1f, outStart), new GradientAlphaKey(0f, 1f) });
            return new ParticleSystem.MinMaxGradient(g);
        }

        // ---------------------------------------------------------------- Câmara e pós-processamento

        static void BuildCamera(Texture2D fogNoise)
        {
            var go = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = go.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.fieldOfView = 40f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 400f;
            cam.allowHDR = true;
            // MSAA 4x para as bordas da geometria (a bola de água, os raios finos); o SMAA trata do resto.
            cam.allowMSAA = true;
            QualitySettings.antiAliasing = 4;
            go.AddComponent<AudioListener>();
            go.AddComponent<CameraDepthTexture>();
            go.transform.position = new Vector3(0f, 1.7f, -16f);
            go.transform.LookAt(new Vector3(0f, 1.2f, 0f));

            // Pós-processamento do URP (Volume global com o look da cena, nevoeiro rasteiro e composição do fogo);
            // os passes próprios estão na AnimLabRenderFeature. Ver URPSetup.
            URPSetup.SetupCamera(cam, fogNoise);
            AlwaysIncludeShader("Hidden/KonoSuba/GroundFog");
            AlwaysIncludeShader("Hidden/KonoSuba/FireComposite");
            AlwaysIncludeShader("Hidden/KonoSuba/Tonemap");
            AlwaysIncludeShader("Hidden/KonoSuba/DistortionApply");
            go.AddComponent<FireFX>();
        }

        // FogField recebe os impactos/ventos dos VFX (as teclas 8 e 9 do antigo demo são agora ataques).
        static void BuildFogInteraction(Transform player)
        {
            var go = new GameObject("FogField");
            go.AddComponent<FogField>();
        }

        // Simulação de ondas à volta do player: liga vento, impactos e passos à água.
        static void BuildWaterSim(Transform player)
        {
            Material floorMat = AssetDatabase.LoadAssetAtPath<Material>(MatDir + "/CheckerWaterFloor.mat");
            var surfaceMat = new Material(floorMat);
            surfaceMat.SetFloat("_SimMesh", 1f);
            AssetDatabase.CreateAsset(surfaceMat, MatDir + "/WaterSurface.mat");

            var go = new GameObject("WaterSim") { layer = WaterLayer };
            var sim = go.AddComponent<WaterSim>();
            sim.follow = player;
            sim.surfaceMaterial = surfaceMat;
            AlwaysIncludeShader("Hidden/KonoSuba/WaterSim");
        }

        // HUD em UI Toolkit: o GameHud constrói os elementos em código; aqui só se cria o painel.
        static void BuildHud()
        {
            var panel = ScriptableObject.CreateInstance<UnityEngine.UIElements.PanelSettings>();
            panel.scaleMode = UnityEngine.UIElements.PanelScaleMode.ScaleWithScreenSize;
            panel.referenceResolution = new Vector2Int(1672, 941); // a resolução da maquete de referência
            panel.screenMatchMode = UnityEngine.UIElements.PanelScreenMatchMode.MatchWidthOrHeight;
            panel.match = 0.5f;
            AssetDatabase.CreateAsset(panel, Root + "/HudPanel.asset");

            var go = new GameObject("HUD");
            go.AddComponent<UnityEngine.UIElements.UIDocument>().panelSettings = panel;
            var hud = go.AddComponent<GameHud>();
            hud.abilityIcons[0] = Icon("Dash");
            hud.abilityIcons[1] = Icon("WaterBall");
            hud.abilityIcons[2] = Icon("Lightning");
            hud.abilityIcons[3] = Icon("Kirin"); // sem PNG, o HUD usa Glyph.Kirin
            hud.abilityIcons[4] = null; // chama vetorial, Glyph.Fire

            // As habilidades do player escrevem a mana e a recarga no HUD.
            var bar = GameObject.Find("Player")?.GetComponent<AbilityBar>();
            if (bar != null) bar.hud = hud;
        }

        // Ícones das habilidades (Assets/KonoSuba/Icons): reduzidos para o tamanho do HUD, com mipmaps.
        static Texture2D Icon(string name)
        {
            string path = Root + "/Icons/" + name + ".png";
            if (AssetImporter.GetAtPath(path) is TextureImporter importer &&
                (importer.maxTextureSize != 256 || !importer.mipmapEnabled))
            {
                importer.textureType = TextureImporterType.Default;
                importer.maxTextureSize = 256;
                importer.mipmapEnabled = true;
                importer.alphaIsTransparency = true;
                importer.textureCompression = TextureImporterCompression.CompressedHQ;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // ---------------------------------------------------------------- Player de teste

        // Cápsula com dois pés para testar o andar na água e a câmara.
        static Transform BuildPlayer()
        {
            Material bodyMat = SaveMaterial("PlayerBody", "Universal Render Pipeline/Lit");
            bodyMat.color = new Color(0.82f, 0.8f, 0.76f);
            bodyMat.SetFloat("_Smoothness", 0.35f);
            Material footMat = SaveMaterial("PlayerFeet", "Universal Render Pipeline/Lit");
            footMat.color = new Color(0.25f, 0.24f, 0.26f);
            footMat.SetFloat("_Smoothness", 0.2f);

            var player = new GameObject("Player");
            player.transform.position = new Vector3(0f, 0f, -4f);
            var cc = player.AddComponent<CharacterController>();
            cc.height = 1.6f;
            cc.radius = 0.35f;
            cc.center = new Vector3(0f, 0.85f, 0f);
            cc.skinWidth = 0.03f;

            var body = Primitive(PrimitiveType.Capsule, "Body", player.transform, new Vector3(0f, 0.95f, 0f), new Vector3(0.6f, 0.65f, 0.6f), bodyMat);
            // "Nariz" para se perceber para onde está virado.
            Primitive(PrimitiveType.Cube, "Facing", body, new Vector3(0f, 0.35f, 0.45f), new Vector3(0.35f, 0.12f, 0.2f), footMat);

            Transform leftFoot = Primitive(PrimitiveType.Sphere, "LeftFoot", player.transform, new Vector3(-0.17f, 0.04f, 0f), new Vector3(0.17f, 0.08f, 0.26f), footMat);
            Transform rightFoot = Primitive(PrimitiveType.Sphere, "RightFoot", player.transform, new Vector3(0.17f, 0.04f, 0f), new Vector3(0.17f, 0.08f, 0.26f), footMat);

            player.AddComponent<LockOn>();
            BuildAbilities(player);
            var cp = player.AddComponent<CapsulePlayer>();
            cp.body = body;
            cp.leftFoot = leftFoot;
            cp.rightFoot = rightFoot;

            var cam = Camera.main.gameObject.AddComponent<Metin2Camera>();
            cam.target = player.transform;

            BuildDummyEnemy();
            BuildHotbarVFX(player);
            return player.transform;
        }

        // Ataques invocados das teclas 7–9 (Tornado de Fogo, Tornado de Vento, Trovão). Nada fica no chão:
        // os tornados são modelos desativados na cena, que as habilidades copiam para o alvo a cada uso.
        static void BuildHotbarVFX(GameObject player)
        {
            var templates = new GameObject("VFX (modelos)");
            BuildFireTest();
            GameObject fire = Template(GameObject.Find("FireTest"), "Tornado de Fogo (modelo)", templates.transform);
            WindSetup.BuildWindTest();
            GameObject wind = Template(GameObject.Find("WindTest"), "Tornado de Vento (modelo)", templates.transform);

            var fireTornado = player.AddComponent<SummonAbility>();
            fireTornado.displayName = "Tornado de Fogo";
            fireTornado.element = AbilityElement.Fire;
            fireTornado.slot = 6;
            fireTornado.manaCost = 40f;
            fireTornado.cooldown = 8f;
            fireTornado.template = fire;
            fireTornado.glyph = GameHud.Glyph.Fire;

            var windTornado = player.AddComponent<SummonAbility>();
            windTornado.displayName = "Tornado de Vento";
            windTornado.element = AbilityElement.Air;
            windTornado.slot = 7;
            windTornado.manaCost = 35f;
            windTornado.cooldown = 8f;
            windTornado.template = wind;
            windTornado.glyph = GameHud.Glyph.Wind;

            var thunder = player.AddComponent<ThunderAbility>();
            thunder.displayName = "Trovão";
            thunder.element = AbilityElement.Lightning;
            thunder.slot = 8;
            thunder.manaCost = 30f;
            thunder.cooldown = 2f;
            LightningSetup.ConfigureThunder(thunder);
        }

        static GameObject Template(GameObject go, string name, Transform parent)
        {
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = Vector3.zero;
            go.SetActive(false);
            return go;
        }

        // Peça de teste do material de fogo (tornado), afastada do player.
        static void BuildFireTest()
        {
            var go = new GameObject("FireTest");
            go.transform.position = new Vector3(-5f, 0f, 6f);
            ConfigureFireTest(go.AddComponent<FireTornado>());
            // Ar quente a subir à volta do tornado (passe de distorção do URP).
            var haze = SaveMaterial("FireHeatHaze", "KonoSuba/Distortion");
            haze.SetFloat("_Strength", 0.007f);
            haze.SetVector("_Scale", new Vector4(7f, 4f, 0f, 0f));
            haze.SetVector("_Speed", new Vector4(0.15f, 1.1f, 0f, 0f));
            HeatHaze.Add(go.transform, haze, 2.1f, 1.9f, 6.5f);
        }

        // Scoped refresh for fire reviews: preserves WindTest, the scene and every other ability.
        [MenuItem("AnimLab/Refresh Fire Tornado Materials")]
        public static void RefreshFireTestMaterials()
        {
            var go = GameObject.Find("FireTest");
            if (go == null) throw new InvalidOperationException("Open the existing KonoSubaVoid scene first.");
            ConfigureFireTest(go.GetComponent<FireTornado>());
            AssetDatabase.SaveAssets();
        }

        static void ConfigureFireTest(FireTornado tornado)
        {
            tornado.coreMaterial = TornadoMaterial("Flame_Core", "KonoSuba/FireFlame");
            tornado.coreMaterial.SetFloat("_Core", 1);
            tornado.coreMaterial.SetFloat("_Alpha", 1);
            tornado.coreMaterial.SetFloat("_Emission", 0.55f);
            tornado.mainMaterial = TornadoMaterial("Flame_Main", "KonoSuba/FireFlame");
            tornado.mainMaterial.SetFloat("_Core", 0);
            tornado.mainMaterial.SetFloat("_Alpha", 0.98f);
            tornado.mainMaterial.SetFloat("_Emission", 0.40f);
            tornado.ribbonMaterial = TornadoMaterial("Flame_Brush", "KonoSuba/FireTornadoStroke");
            tornado.inkMaterial = TornadoMaterial("Flame_Ink", "KonoSuba/FireTornadoStroke");
            tornado.inkMaterial.SetFloat("_Kind", 1);
            tornado.sparkMaterial = TornadoMaterial("Flame_Ember", "KonoSuba/FireTornadoStroke");
            tornado.sparkMaterial.SetFloat("_Kind", 2);
            tornado.groundMaterial = TornadoMaterial("Flame_Ground", "KonoSuba/FireTornadoGround");
            var grain = AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/FireArt/TornadoFlow.png");
            tornado.ribbonMaterial.SetTexture("_FlowTex", grain);
            tornado.inkMaterial.SetTexture("_FlowTex", grain);
            tornado.sparkMaterial.SetTexture("_FlowTex", grain);
            foreach (var m in new[] { tornado.coreMaterial, tornado.mainMaterial, tornado.ribbonMaterial,
                tornado.inkMaterial, tornado.sparkMaterial, tornado.groundMaterial }) EditorUtility.SetDirty(m);
        }

        static Material TornadoMaterial(string name, string shaderName)
        {
            string path = MatDir + "/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            var defaults = new Material(Shader.Find(shaderName));
            if (material == null)
            {
                material = defaults;
                material.name = name;
                AssetDatabase.CreateAsset(material, path);
            }
            else
            {
                material.shader = defaults.shader;
                material.CopyPropertiesFromMaterial(defaults);
                UnityEngine.Object.DestroyImmediate(defaults);
            }
            return material;
        }

        // Inimigo parado, só para testar a seleção de alvo (clicar nele com o botão esquerdo).
        static void BuildDummyEnemy()
        {
            Material enemyMat = SaveMaterial("EnemyBody", "KonoSuba/Wettable"); // molha só onde a água toca
            enemyMat.color = new Color(0.45f, 0.16f, 0.18f);
            enemyMat.SetFloat("_Glossiness", 0.3f);
            Material markerMat = SaveMaterial("TargetRing", "KonoSuba/SparkleAdditive");
            markerMat.SetTexture("_MainTex", SaveTexture("TargetRing", 256, TargetRingPixel, TextureWrapMode.Clamp));
            markerMat.SetFloat("_Intensity", 1.4f);

            var enemy = new GameObject("DummyEnemy");
            enemy.transform.position = new Vector3(2.5f, 0f, 7f);
            enemy.transform.rotation = Quaternion.Euler(0f, 200f, 0f);
            var body = Primitive(PrimitiveType.Capsule, "Body", enemy.transform, new Vector3(0f, 1f, 0f), new Vector3(0.8f, 1f, 0.8f), enemyMat);
            Primitive(PrimitiveType.Cube, "Facing", body, new Vector3(0f, 0.3f, 0.45f), new Vector3(0.4f, 0.1f, 0.2f), enemyMat);
            var col = enemy.AddComponent<CapsuleCollider>();
            col.center = new Vector3(0f, 1f, 0f);
            col.height = 2f;
            col.radius = 0.4f;

            // Indicador de alvo: círculo vermelho deitado no chão, a rodar, só visível quando selecionado.
            var marker = new GameObject("TargetRing");
            marker.transform.SetParent(enemy.transform, false);
            marker.transform.localPosition = new Vector3(0f, 0.03f, 0f);
            marker.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            marker.transform.localScale = Vector3.one * 1.7f;
            marker.AddComponent<MeshFilter>().sharedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(Root + "/HaloQuad.asset");
            var mr = marker.AddComponent<MeshRenderer>();
            mr.sharedMaterial = markerMat;
            mr.shadowCastingMode = ShadowCastingMode.Off;

            var target = enemy.AddComponent<LockOnTarget>();
            target.displayName = "Boneco de treino";
            target.lockMarker = marker;
        }

        // Habilidades: 1 = Dash, 2 = Bola de Água, 3 = Raio, 4 = Kirin, 5 = Bola de Fogo.
        static void BuildAbilities(GameObject player)
        {
            var dash = player.AddComponent<DashAbility>();
            dash.displayName = "Dash";
            dash.element = AbilityElement.Neutral;
            dash.slot = 0;
            dash.manaCost = 12f;
            dash.cooldown = 1.2f;

            var ball = player.AddComponent<WaterBallAbility>();
            ball.displayName = "Bola de Água";
            ball.element = AbilityElement.Water;
            ball.slot = 1;
            ball.manaCost = 0f; // canalizada: cada bolinha custa 1 de mana
            ball.cooldown = 0.6f;
            // A mesma água na bola grande e nas bolinhas que convergem nela.
            ball.orbMaterial = SaveMaterial("WaterOrb", "KonoSuba/WaterOrb");
            ball.orbMaterial.SetFloat("_Swirl", 1f);
            ball.auraMaterial = SaveMaterial("ManaAura", "KonoSuba/ManaAura");
            ball.impactMaterial = SaveMaterial("WaterSheet", "KonoSuba/WaterSheet");
            ball.veilMaterial = SaveMaterial("WaterPressureVeil", "KonoSuba/WaterPressureVeil");

            var bolt = player.AddComponent<LightningAbility>();
            bolt.displayName = "Raio";
            bolt.element = AbilityElement.Lightning;
            bolt.slot = 2;
            bolt.manaCost = 30f;
            bolt.cooldown = 3f;
            bolt.boltMaterial = SaveMaterial("LightningBolt", "KonoSuba/LightningBolt");

            var kirin = player.AddComponent<KirinAbility>();
            kirin.displayName = "Kirin";
            kirin.element = AbilityElement.Lightning;
            kirin.slot = 3;
            kirin.manaCost = 60f;
            kirin.cooldown = 15f;
            kirin.range = 14f;
            kirin.boltMaterial = bolt.boltMaterial;
            LightningSetup.ConfigureKirin(kirin);

            BuildFireAbility(player);
            WindSetup.BuildWindBallAbility(player);

            player.AddComponent<AbilityBar>();
        }

        // Instala apenas o fogo na cena que já está aberta, sem reconstruir o domínio ou as outras habilidades.
        [MenuItem("AnimLab/Adicionar Bola de Fogo à cena atual")]
        public static void AddFireBallToCurrentScene()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Adicionar a Bola de Fogo fora do Play Mode.");
            Scene scene = SceneManager.GetActiveScene();
            AbilityBar player = null;
            foreach (var bar in UnityEngine.Object.FindObjectsByType<AbilityBar>(FindObjectsSortMode.None))
            {
                if (bar.gameObject.scene != scene) continue;
                if (player != null) throw new InvalidOperationException("Há várias AbilityBar na cena; selecionar primeiro a cena do player.");
                player = bar;
            }
            if (player == null) throw new InvalidOperationException("A cena atual não tem um player com AbilityBar.");
            foreach (var a in player.GetComponents<Ability>())
                if (!(a is FireBallAbility) && a.slot == 4)
                    throw new InvalidOperationException("A tecla 5 já está ocupada por outra habilidade.");
            BuildFireAbility(player.gameObject);
            var hud = player.hud != null ? player.hud : UnityEngine.Object.FindFirstObjectByType<GameHud>();
            if (hud != null && hud.abilityIcons != null && hud.abilityIcons.Length > 4)
            {
                Undo.RecordObject(hud, "Ícone da Bola de Fogo");
                hud.abilityIcons[4] = null;
                EditorUtility.SetDirty(hud);
            }
            EditorSceneManager.MarkSceneDirty(scene); // o utilizador decide quando gravar a cena
            Debug.Log("Bola de Fogo instalada na tecla 5. Gravar a cena para conservar a habilidade.");
        }

        static void BuildFireAbility(GameObject player)
        {
            var fire = player.GetComponent<FireBallAbility>();
            if (fire == null)
            {
                fire = Undo.AddComponent<FireBallAbility>(player);
                fire.displayName = "Bola de Fogo";
                fire.element = AbilityElement.Fire;
                fire.slot = 4;
                fire.manaCost = 25f;
                fire.cooldown = 1.5f;
            }
            Undo.RecordObject(fire, "Materiais da Bola de Fogo");
            // Fogo volumétrico (raymarching) na bola, no rasto e na explosão; o vapor é o mesmo shader em modo claro.
            if (fire.flameMaterial == null) fire.flameMaterial = FireMaterial("FireVolumetric", "KonoSuba/FireVolumetric");
            if (fire.steamMaterial == null)
            {
                fire.steamMaterial = FireMaterial("FireSteamVolumetric", "KonoSuba/FireVolumetric");
                fire.steamMaterial.SetFloat("_Steam", 1f);
                EditorUtility.SetDirty(fire.steamMaterial);
                AssetDatabase.SaveAssetIfDirty(fire.steamMaterial);
            }
            if (fire.ringMaterial == null) fire.ringMaterial = FireMaterial("FireRing", "KonoSuba/FireRing");
            if (fire.ballMaterial == null) fire.ballMaterial = FireMaterial("FireToon", "KonoSuba/FireToon");
            if (fire.hitMaterial == null) fire.hitMaterial = FireMaterial("FireHitGlow", "KonoSuba/FireHitGlow");
            EditorUtility.SetDirty(fire);
        }

        static Material FireMaterial(string name, string shader)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Material>(MatDir + "/" + name + ".mat");
            return existing != null ? existing : SaveMaterial(name, shader);
        }

        static Transform Primitive(PrimitiveType type, string name, Transform parent, Vector3 localPos, Vector3 scale, Material mat)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = scale;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            return go.transform;
        }

        // O shader do efeito é carregado com Shader.Find, por isso tem de ir sempre no build.
        static void AlwaysIncludeShader(string name)
        {
            Shader shader = Shader.Find(name);
            if (shader == null) return;
            var settings = new SerializedObject(AssetDatabase.LoadAssetAtPath<UnityEngine.Object>("ProjectSettings/GraphicsSettings.asset"));
            SerializedProperty list = settings.FindProperty("m_AlwaysIncludedShaders");
            for (int i = 0; i < list.arraySize; i++)
                if (list.GetArrayElementAtIndex(i).objectReferenceValue == shader) return;
            list.InsertArrayElementAtIndex(list.arraySize);
            list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = shader;
            settings.ApplyModifiedProperties();
        }

        // ---------------------------------------------------------------- Texturas e materiais

        static Material SaveMaterial(string name, string shader)
        {
            string path = MatDir + "/" + name + ".mat";
            var m = new Material(Shader.Find(shader));
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        static Texture2D SaveTexture(string name, int size, Func<float, float, Color> pixel, TextureWrapMode wrap)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    tex.SetPixel(x, y, pixel((x + 0.5f) / size, (y + 0.5f) / size));
            string path = TexDir + "/" + name + ".png";
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);

            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.alphaIsTransparency = true;
            importer.wrapMode = wrap;
            importer.mipmapEnabled = true;
            importer.SaveAndReimport();
            var result = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (result == null) throw new InvalidOperationException("KonoSuba: falhou a importação da textura " + path);
            return result;
        }

        static float Fbm(float x, float y, int octaves)
        {
            float v = 0f, a = 0.5f, f = 1f;
            for (int i = 0; i < octaves; i++) { v += a * Mathf.PerlinNoise(x * f + 31.7f * i, y * f + 17.3f * i); a *= 0.5f; f *= 2f; }
            return v;
        }

        // Nuvem achatada: falloff elíptico suave com bordas recortadas por fBm.
        static Color FogShapePixel(float u, float v)
        {
            float dx = (u - 0.5f) * 2f, dy = (v - 0.5f) * 2f;
            float r = Mathf.Sqrt(dx * dx + dy * dy * 1.6f);
            float n = Fbm(u * 4f, v * 4f, 5);
            float a = Mathf.Clamp01(1f - r - (n - 0.5f) * 0.7f);
            a = Mathf.SmoothStep(0f, 1f, a);
            return new Color(1f, 1f, 1f, a * a);
        }

        // Ruído sem costuras: mistura 4 amostras deslocadas pelo período (técnica clássica de seamless blend).
        static Color TileableNoisePixel(float u, float v)
        {
            const float p = 6f;
            float x = u * p, y = v * p;
            float a = Fbm(x, y, 5), b = Fbm(x - p, y, 5), c = Fbm(x, y - p, 5), d = Fbm(x - p, y - p, 5);
            float n = Mathf.Lerp(Mathf.Lerp(a, b, u), Mathf.Lerp(c, d, u), v);
            n = Mathf.Clamp01((n - 0.2f) / 0.6f);
            return new Color(n, n, n, 1f);
        }

        // Círculo de alvo: aro vermelho fino com três arcos mais grossos (para se ver que roda).
        static Color TargetRingPixel(float u, float v)
        {
            float dx = (u - 0.5f) * 2f, dy = (v - 0.5f) * 2f;
            float r = Mathf.Sqrt(dx * dx + dy * dy);
            float ang = Mathf.Atan2(dy, dx);
            float rim = Mathf.Exp(-(r - 0.86f) * (r - 0.86f) * 2500f);
            float arcs = Mathf.SmoothStep(0.25f, 0.6f, Mathf.Cos(ang * 3f)) * Mathf.Exp(-(r - 0.76f) * (r - 0.76f) * 900f);
            float glow = Mathf.Exp(-(r - 0.86f) * (r - 0.86f) * 150f) * 0.25f;
            float a = Mathf.Clamp01(rim + arcs * 0.9f + glow) * (r < 1f ? 1f : 0f);
            return new Color(1f, 0.18f, 0.12f, a);
        }

        // Ponto de luz simples: núcleo nítido + halo suave (sem cruz, para não parecer artificial).
        static Color MotePixel(float u, float v)
        {
            float dx = u - 0.5f, dy = v - 0.5f;
            float d2 = dx * dx + dy * dy;
            float a = Mathf.Clamp01(Mathf.Exp(-d2 * 120f) + Mathf.Exp(-d2 * 18f) * 0.25f);
            return new Color(1f, 1f, 1f, a * (d2 < 0.25f ? 1f : 0f));
        }

        // Brilho: núcleo gaussiano + cruz fina de lens flare.
        static Color SparklePixel(float u, float v)
        {
            float dx = u - 0.5f, dy = v - 0.5f;
            float d2 = dx * dx + dy * dy;
            float core = Mathf.Exp(-d2 * 400f);
            float halo = Mathf.Exp(-d2 * 40f) * 0.35f;
            float cross = (Mathf.Exp(-dx * dx * 8000f) + Mathf.Exp(-dy * dy * 8000f)) * Mathf.Exp(-d2 * 30f) * 0.6f;
            float a = Mathf.Clamp01(core + halo + cross);
            return new Color(1f, 1f, 1f, a);
        }
    }
}
