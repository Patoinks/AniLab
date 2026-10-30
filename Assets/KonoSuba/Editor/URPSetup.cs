using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace AnimLab.KonoSuba.EditorTools
{
    /// <summary>
    /// Setup de render do AnimLab em URP (etapa 1 do Setup personalizado):
    ///   - pipeline URP próprio (Assets/Settings): HDR, depth e opaque texture, MSAA 4×;
    ///   - espaço de cor Linear;
    ///   - câmara com pós-processamento do URP e um Volume global que reproduz o look antigo do PPv2
    ///     afinado para Linear: bloom, grading suave, vinheta e o tonemapper próprio (AnimLabTonemap);
    ///   - renderer feature do AnimLab (nevoeiro rasteiro e fogo), com as definições no mesmo Volume.
    /// Chamado por KonoSubaSceneSetup.Build().
    /// </summary>
    public static class URPSetup
    {
        const string SettingsDir = "Assets/Settings";
        const string PipelinePath = SettingsDir + "/AnimLab_URP.asset";
        const string RendererPath = SettingsDir + "/AnimLab_URP_Renderer.asset";
        public const string VolumeProfilePath = "Assets/KonoSuba/KonoSubaVolume.asset";

        [MenuItem("AnimLab/Render/Aplicar pipeline URP")]
        public static void EnsurePipeline()
        {
            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelinePath);
            if (pipeline == null)
            {
                System.IO.Directory.CreateDirectory(SettingsDir);
                var renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
                renderer.postProcessData = AssetDatabase.LoadAssetAtPath<PostProcessData>(
                    "Packages/com.unity.render-pipelines.universal/Runtime/Data/PostProcessData.asset");
                AssetDatabase.CreateAsset(renderer, RendererPath);
                pipeline = UniversalRenderPipelineAsset.Create(renderer);
                AssetDatabase.CreateAsset(pipeline, PipelinePath);
            }

            pipeline.supportsHDR = true;
            pipeline.supportsCameraDepthTexture = true;  // soft particles, nevoeiro, oclusão do fogo
            pipeline.supportsCameraOpaqueTexture = true; // distorção e refração (etapas seguintes)
            pipeline.msaaSampleCount = 4;
            pipeline.shadowDistance = 60f;
            EditorUtility.SetDirty(pipeline);

            // Passes próprios do AnimLab (nevoeiro rasteiro, fogo com bloom próprio).
            var rendererData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
            // A profundidade da cena tem de estar pronta logo depois dos opacos: o nevoeiro (antes dos
            // transparentes) e a oclusão do fogo leem-na.
            if (rendererData != null && rendererData.copyDepthMode != CopyDepthMode.AfterOpaques)
            {
                rendererData.copyDepthMode = CopyDepthMode.AfterOpaques;
                EditorUtility.SetDirty(rendererData);
            }
            if (rendererData != null && !rendererData.rendererFeatures.Exists(f => f is AnimLabRenderFeature))
            {
                var feature = ScriptableObject.CreateInstance<AnimLabRenderFeature>();
                feature.name = "AnimLab";
                AssetDatabase.AddObjectToAsset(feature, rendererData);
                rendererData.rendererFeatures.Add(feature);
                rendererData.SetDirty();
                EditorUtility.SetDirty(rendererData);
            }
            AssetDatabase.SaveAssets();

            GraphicsSettings.defaultRenderPipeline = pipeline;
            int current = QualitySettings.GetQualityLevel();
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = pipeline;
            }
            QualitySettings.SetQualityLevel(current, false);

            if (PlayerSettings.colorSpace != ColorSpace.Linear) PlayerSettings.colorSpace = ColorSpace.Linear;
        }

        /// <summary>Pós-processamento do URP na câmara e Volume global com o look atual da cena.</summary>
        public static void SetupCamera(Camera cam, Texture2D fogNoise)
        {
            var data = cam.GetComponent<UniversalAdditionalCameraData>();
            if (data == null) data = cam.gameObject.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            data.requiresDepthTexture = true;
            data.requiresColorTexture = true;

            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, VolumeProfilePath);

            var bloom = profile.Add<Bloom>(true);
            bloom.intensity.Override(1.0f);
            bloom.threshold.Override(1.0f);
            bloom.scatter.Override(0.7f);
            bloom.tint.Override(new Color(0.88f, 0.94f, 1f));

            // Tonemapping do URP desligado: o tonemapper próprio (AnimLabTonemap) mantém o tom das cores.
            var tone = profile.Add<Tonemapping>(true);
            tone.mode.Override(TonemappingMode.None);
            profile.Add<AnimLabTonemap>(true);

            var adjust = profile.Add<ColorAdjustments>(true);
            adjust.postExposure.Override(0f);
            adjust.contrast.Override(5f);
            adjust.saturation.Override(-10f);

            var white = profile.Add<WhiteBalance>(true);
            white.temperature.Override(-5f);

            var vignette = profile.Add<Vignette>(true);
            vignette.intensity.Override(0.38f);
            vignette.smoothness.Override(0.5f);

            // Nevoeiro rasteiro em ecrã: o protagonista da cena, sem cards perto da câmara.
            var groundFog = profile.Add<GroundFog>(true);
            groundFog.noise.Override(fogNoise);
            groundFog.density.Override(0.12f); // em Linear a densidade antiga lia-se leitosa
            groundFog.baseHaze.Override(0.22f);
            // O fogo tem caminho de cor próprio: desenhado à parte (FireFX) e junto depois do grading.
            profile.Add<FireComposite>(true);

            foreach (VolumeComponent c in profile.components) AssetDatabase.AddObjectToAsset(c, profile);
            EditorUtility.SetDirty(profile);

            var volume = new GameObject("Volume Global").AddComponent<Volume>();
            volume.isGlobal = true;
            volume.sharedProfile = profile;
        }
    }
}
