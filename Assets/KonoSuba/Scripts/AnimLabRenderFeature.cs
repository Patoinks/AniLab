using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

namespace AnimLab.KonoSuba
{
    /// <summary>
    /// Renderer feature do AnimLab (URP, Render Graph). Passes próprios do projeto:
    ///   - Nevoeiro rasteiro (antes dos transparentes): GroundFog do Volume, calculado no ecrã contra a profundidade.
    ///   - Distorção (depois dos transparentes): materiais com o passe "AnimLabDistortion" escrevem desvios de
    ///     ecrã num buffer à parte, que depois refrata a imagem (ar quente, vento, ondas de choque, água).
    ///   - Tonemapper próprio (depois do pós-processamento do URP, com o Tonemapping do URP em None).
    ///   - Fogo, desenho (depois dos transparentes): os renderers do FireFX num buffer HDR à parte, com o interior
    ///     opaco resolvido antes por pixel (passe FireDepth).
    ///   - Fogo, composição (depois do pós-processamento): pirâmide de bloom do fogo e junção ao ecrã depois do
    ///     grading — as cores do fogo saem como estão nos materiais (pintadas em sRGB).
    /// Câmaras de reflexo e de pré-visualização ficam de fora.
    /// </summary>
    public class AnimLabRenderFeature : ScriptableRendererFeature
    {
        GroundFogPass fog;
        DistortionPass distortion;
        TonemapPass tonemap;
        FireDrawPass fireDraw;
        FireCompositePass fireComposite;

        public override void Create()
        {
            fog = new GroundFogPass { renderPassEvent = RenderPassEvent.BeforeRenderingTransparents };
            fireDraw = new FireDrawPass { renderPassEvent = RenderPassEvent.AfterRenderingTransparents };
            distortion = new DistortionPass { renderPassEvent = RenderPassEvent.AfterRenderingTransparents };
            tonemap = new TonemapPass { renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing };
            fireComposite = new FireCompositePass { renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing };
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            CameraType type = renderingData.cameraData.cameraType;
            if (type == CameraType.Preview || type == CameraType.Reflection) return;
            renderer.EnqueuePass(fog);
            renderer.EnqueuePass(fireDraw);
            renderer.EnqueuePass(distortion);
            renderer.EnqueuePass(tonemap); // antes da composição do fogo: o fogo junta-se já com a cor final
            renderer.EnqueuePass(fireComposite);
        }

        protected override void Dispose(bool disposing)
        {
            fog?.Dispose();
            distortion?.Dispose();
            tonemap?.Dispose();
            fireComposite?.Dispose();
        }
    }

    /// <summary>O buffer do fogo deste frame, passado do desenho para a composição.</summary>
    class FireFrameData : ContextItem
    {
        public TextureHandle fire;
        public bool any;

        public override void Reset()
        {
            fire = TextureHandle.nullHandle;
            any = false;
        }
    }

    class GroundFogPass : ScriptableRenderPass
    {
        static readonly Vector3[] corners = new Vector3[4];
        Material material;

        public GroundFogPass() { requiresIntermediateTexture = true; }

        class PassData { public TextureHandle source; public Material material; }

        public void Dispose() => CoreUtils.Destroy(material);

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var settings = VolumeManager.instance.stack.GetComponent<GroundFog>();
            if (settings == null || !settings.IsActive()) return;
            if (material == null)
            {
                Shader shader = Shader.Find("Hidden/KonoSuba/GroundFog");
                if (shader == null) return;
                material = CoreUtils.CreateEngineMaterial(shader);
            }

            var resources = frameData.Get<UniversalResourceData>();
            Camera cam = frameData.Get<UniversalCameraData>().camera;
            Apply(settings, cam);

            TextureHandle color = resources.activeColorTexture;
            TextureDesc desc = renderGraph.GetTextureDesc(color);
            desc.name = "GroundFogSource";
            desc.clearBuffer = false;
            desc.msaaSamples = MSAASamples.None;
            TextureHandle copy = renderGraph.CreateTexture(desc);
            renderGraph.AddBlitPass(color, copy, Vector2.one, Vector2.zero, passName: "Nevoeiro: copiar cor");

            using (var builder = renderGraph.AddRasterRenderPass<PassData>("Nevoeiro rasteiro", out var data))
            {
                data.source = copy;
                data.material = material;
                builder.UseTexture(copy);
                if (resources.cameraDepthTexture.IsValid()) builder.UseTexture(resources.cameraDepthTexture);
                builder.SetRenderAttachment(color, 0);
                builder.SetRenderFunc((PassData d, RasterGraphContext ctx) =>
                    Blitter.BlitTexture(ctx.cmd, d.source, new Vector4(1, 1, 0, 0), d.material, 0));
            }
        }

        void Apply(GroundFog s, Camera cam)
        {
            // Direções dos cantos do frustum (em mundo, com componente 1 ao longo da frente da câmara):
            // o shader interpola-as para obter o raio de cada pixel.
            cam.CalculateFrustumCorners(new Rect(0f, 0f, 1f, 1f), 1f, Camera.MonoOrStereoscopicEye.Mono, corners);
            material.SetVector("_CornerBL", cam.transform.TransformVector(corners[0]));
            material.SetVector("_CornerTL", cam.transform.TransformVector(corners[1]));
            material.SetVector("_CornerTR", cam.transform.TransformVector(corners[2]));
            material.SetVector("_CornerBR", cam.transform.TransformVector(corners[3]));
            material.SetVector("_CamPos", cam.transform.position);
            material.SetVector("_FogShape", new Vector4(s.height.value, s.density.value, s.heightFalloff.value, s.maxDistance.value));
            material.SetVector("_FogNoiseParams", new Vector4(s.noiseScale.value, s.coverage.value, s.softness.value, s.stretch.value));
            Vector2 wind = s.windDirection.value.sqrMagnitude > 1e-6f ? s.windDirection.value.normalized : Vector2.right;
            material.SetVector("_FogWindParams2", new Vector4(wind.x, wind.y, s.windSpeed.value, s.swirl.value));
            material.SetColor("_FogTint", s.tint.value);
            material.SetColor("_FogGlow", s.centerGlow.value);
            material.SetVector("_FogMisc", new Vector4(s.centerGlowRadius.value, s.baseHaze.value,
                Application.isPlaying ? Time.time : Time.realtimeSinceStartup, 0f));
            material.SetVector("_FogDetail", new Vector4(s.detailStart.value, Mathf.Max(s.detailEnd.value, s.detailStart.value + 0.1f), 0f, 0f));
            material.SetTexture("_NoiseTex", s.noise.value);
        }
    }

    class DistortionPass : ScriptableRenderPass
    {
        static readonly ShaderTagId Tag = new ShaderTagId("AnimLabDistortion");
        static readonly int TexId = Shader.PropertyToID("_DistortionTex");
        Material apply;

        public DistortionPass() { requiresIntermediateTexture = true; }

        class DrawData { public RendererListHandle list; }
        class ApplyData { public TextureHandle source; public Material material; }

        public void Dispose() => CoreUtils.Destroy(apply);

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (apply == null)
            {
                Shader shader = Shader.Find("Hidden/KonoSuba/DistortionApply");
                if (shader == null) return;
                apply = CoreUtils.CreateEngineMaterial(shader);
            }
            var resources = frameData.Get<UniversalResourceData>();
            var renderingData = frameData.Get<UniversalRenderingData>();
            var cameraData = frameData.Get<UniversalCameraData>();
            var lightData = frameData.Get<UniversalLightData>();

            // 1) Desvios de ecrã (xy, em UV) de todos os materiais com o passe AnimLabDistortion, somados.
            DrawingSettings drawing = CreateDrawingSettings(Tag, renderingData, cameraData, lightData, SortingCriteria.CommonTransparent);
            var filtering = new FilteringSettings(RenderQueueRange.all);
            RendererListHandle list = renderGraph.CreateRendererList(new RendererListParams(renderingData.cullResults, drawing, filtering));
            TextureDesc colorDesc = renderGraph.GetTextureDesc(resources.cameraColor);
            TextureHandle offsets = renderGraph.CreateTexture(new TextureDesc(colorDesc.width, colorDesc.height)
            { format = GraphicsFormat.R16G16_SFloat, clearBuffer = true, clearColor = Color.clear, filterMode = FilterMode.Bilinear, name = "Distortion" });
            using (var builder = renderGraph.AddRasterRenderPass<DrawData>("Distorção: desenhar", out var data))
            {
                data.list = list;
                builder.UseRendererList(list);
                builder.SetRenderAttachment(offsets, 0);
                if (resources.cameraDepthTexture.IsValid()) builder.UseTexture(resources.cameraDepthTexture);
                builder.SetGlobalTextureAfterPass(offsets, TexId);
                builder.SetRenderFunc((DrawData d, RasterGraphContext ctx) => ctx.cmd.DrawRendererList(d.list));
            }

            // 2) Refração: a cor atual é lida deslocada pelos desvios.
            TextureHandle color = resources.activeColorTexture;
            TextureDesc desc = renderGraph.GetTextureDesc(color);
            desc.name = "DistortionSource";
            desc.clearBuffer = false;
            desc.msaaSamples = MSAASamples.None;
            TextureHandle copy = renderGraph.CreateTexture(desc);
            renderGraph.AddBlitPass(color, copy, Vector2.one, Vector2.zero, passName: "Distorção: copiar cor");
            using (var builder = renderGraph.AddRasterRenderPass<ApplyData>("Distorção: refratar", out var data))
            {
                data.source = copy;
                data.material = apply;
                builder.UseTexture(copy);
                builder.UseTexture(offsets);
                builder.SetRenderAttachment(color, 0);
                builder.SetRenderFunc((ApplyData d, RasterGraphContext ctx) =>
                    Blitter.BlitTexture(ctx.cmd, d.source, new Vector4(1, 1, 0, 0), d.material, 0));
            }
        }
    }

    class TonemapPass : ScriptableRenderPass
    {
        static readonly int CurveId = Shader.PropertyToID("_TonemapCurve");
        static readonly int ParamsId = Shader.PropertyToID("_TonemapParams");
        Material material;

        public TonemapPass() { requiresIntermediateTexture = true; }

        class PassData { public TextureHandle source; public Material material; }

        public void Dispose() => CoreUtils.Destroy(material);

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var s = VolumeManager.instance.stack.GetComponent<AnimLabTonemap>();
            if (s == null || !s.IsActive()) return;
            if (material == null)
            {
                Shader shader = Shader.Find("Hidden/KonoSuba/Tonemap");
                if (shader == null) return;
                material = CoreUtils.CreateEngineMaterial(shader);
            }

            // Curva de Lottes: o cinzento médio 0,18 vai para midOut e whitePoint chega a 1.
            float a = s.contrast.value, d = s.shoulder.value, white = s.whitePoint.value, midIn = 0.18f, midOut = s.midOut.value;
            float wa = Mathf.Pow(white, a), wad = Mathf.Pow(white, a * d), ma = Mathf.Pow(midIn, a), mad = Mathf.Pow(midIn, a * d);
            float den = (wad - mad) * midOut;
            float b = (-ma + wa * midOut) / den;
            float c = (wad * ma - wa * mad * midOut) / den;
            material.SetVector(CurveId, new Vector4(a, d, b, c));
            material.SetVector(ParamsId, new Vector4(s.exposure.value, s.highlightDesaturation.value, s.saturation.value, 0f));

            var resources = frameData.Get<UniversalResourceData>();
            TextureHandle color = resources.activeColorTexture;
            TextureDesc desc = renderGraph.GetTextureDesc(color);
            desc.name = "TonemapSource";
            desc.clearBuffer = false;
            desc.msaaSamples = MSAASamples.None;
            TextureHandle copy = renderGraph.CreateTexture(desc);
            renderGraph.AddBlitPass(color, copy, Vector2.one, Vector2.zero, passName: "Tonemap: copiar cor");
            using (var builder = renderGraph.AddRasterRenderPass<PassData>("Tonemap AnimLab", out var data))
            {
                data.source = copy;
                data.material = material;
                builder.UseTexture(copy);
                builder.SetRenderAttachment(color, 0);
                builder.SetRenderFunc((PassData p, RasterGraphContext ctx) =>
                    Blitter.BlitTexture(ctx.cmd, p.source, new Vector4(1, 1, 0, 0), p.material, 0));
            }
        }
    }

    class FireDrawPass : ScriptableRenderPass
    {
        static readonly int FireTexId = Shader.PropertyToID("_FireFXTex");
        static readonly int SolidId = Shader.PropertyToID("_FireSolidDepth");
        readonly List<FireFX.Draw> draws = new List<FireFX.Draw>();

        class PassData { public List<FireFX.Draw> draws; public bool depthOnly; }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var resources = frameData.Get<UniversalResourceData>();
            var cameraData = frameData.Get<UniversalCameraData>();
            var fd = frameData.Create<FireFrameData>();
            FireFX.CollectDraws(cameraData.camera, draws);
            if (draws.Count == 0) return;

            TextureDesc colorDesc = renderGraph.GetTextureDesc(resources.cameraColor);
            int w = colorDesc.width, h = colorDesc.height;
            TextureHandle solid = renderGraph.CreateTexture(new TextureDesc(w, h)
            { format = GraphicsFormat.R32_SFloat, clearBuffer = true, clearColor = new Color(10000f, 0f, 0f, 0f), filterMode = FilterMode.Point, name = "FireSolidDepth" });
            TextureHandle depth = renderGraph.CreateTexture(new TextureDesc(w, h)
            { format = GraphicsFormat.D32_SFloat, clearBuffer = true, name = "FireDepth" });
            TextureHandle fire = renderGraph.CreateTexture(new TextureDesc(w, h)
            { format = GraphicsFormat.R16G16B16A16_SFloat, clearBuffer = true, clearColor = Color.clear, filterMode = FilterMode.Bilinear, name = "FireFX" });

            // 1) Interior opaco do fogo por pixel (evita cortes de ordenação entre secções curvas).
            using (var builder = renderGraph.AddRasterRenderPass<PassData>("Fogo: profundidade", out var data))
            {
                data.draws = draws;
                data.depthOnly = true;
                builder.SetRenderAttachment(solid, 0);
                builder.SetRenderAttachmentDepth(depth, AccessFlags.Write);
                if (resources.cameraDepthTexture.IsValid()) builder.UseTexture(resources.cameraDepthTexture);
                builder.SetGlobalTextureAfterPass(solid, SolidId);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc((PassData d, RasterGraphContext ctx) => DrawAll(d, ctx));
            }
            // 2) Cor do fogo, pré-multiplicada, de trás para a frente.
            using (var builder = renderGraph.AddRasterRenderPass<PassData>("Fogo: cor", out var data))
            {
                data.draws = draws;
                data.depthOnly = false;
                builder.SetRenderAttachment(fire, 0);
                builder.SetRenderAttachmentDepth(depth, AccessFlags.ReadWrite);
                builder.UseTexture(solid);
                if (resources.cameraDepthTexture.IsValid()) builder.UseTexture(resources.cameraDepthTexture);
                builder.SetGlobalTextureAfterPass(fire, FireTexId);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc((PassData d, RasterGraphContext ctx) => DrawAll(d, ctx));
            }
            fd.fire = fire;
            fd.any = true;
        }

        static void DrawAll(PassData d, RasterGraphContext ctx)
        {
            foreach (FireFX.Draw x in d.draws)
            {
                if (x.renderer == null) continue;
                if (d.depthOnly)
                {
                    if (x.depthPass >= 0) ctx.cmd.DrawRenderer(x.renderer, x.material, x.submesh, x.depthPass);
                }
                else ctx.cmd.DrawRenderer(x.renderer, x.material, x.submesh, x.pass);
            }
        }
    }

    class FireCompositePass : ScriptableRenderPass
    {
        const int Levels = 8;
        static readonly int StepId = Shader.PropertyToID("_FireBloomStep");
        static readonly int ExposureId = Shader.PropertyToID("_FireExposure");
        static readonly int BloomId = Shader.PropertyToID("_FireBloom");
        readonly Material[] materials = new Material[Levels + 1]; // 0 = composição, 1.. = pirâmide (um por nível)

        public FireCompositePass() { requiresIntermediateTexture = true; }

        class PassData
        {
            public TextureHandle source, near, mid, wide, halo;
            public Material material;
        }

        public void Dispose()
        {
            foreach (Material m in materials) CoreUtils.Destroy(m);
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (!frameData.Contains<FireFrameData>()) return;
            var fd = frameData.Get<FireFrameData>();
            if (!fd.any || !fd.fire.IsValid()) return;
            if (materials[0] == null)
            {
                Shader shader = Shader.Find("Hidden/KonoSuba/FireComposite");
                if (shader == null) return;
                for (int i = 0; i < materials.Length; i++) materials[i] = CoreUtils.CreateEngineMaterial(shader);
            }
            var settings = VolumeManager.instance.stack.GetComponent<FireComposite>();
            float exposure = settings != null ? settings.exposure.value : 1f;
            float bloom = settings != null ? settings.bloom.value : 1f;

            var resources = frameData.Get<UniversalResourceData>();
            TextureHandle source = resources.activeColorTexture;
            TextureDesc desc = renderGraph.GetTextureDesc(source);

            // Pirâmide de bloom do fogo: extrai os brilhos e desce oito níveis.
            var levels = new TextureHandle[Levels];
            TextureHandle previous = fd.fire;
            int w = desc.width, h = desc.height;
            for (int i = 0; i < Levels; i++)
            {
                Material m = materials[i + 1];
                m.SetVector(StepId, new Vector4(1f / w, 1f / h, 0f, 0f));
                m.SetFloat(ExposureId, exposure);
                w = Mathf.Max(2, w / 2);
                h = Mathf.Max(2, h / 2);
                levels[i] = renderGraph.CreateTexture(new TextureDesc(w, h)
                { format = GraphicsFormat.R16G16B16A16_SFloat, filterMode = FilterMode.Bilinear, name = "FireBloom" + i });
                renderGraph.AddBlitPass(new RenderGraphUtils.BlitMaterialParameters(previous, levels[i], m, i == 0 ? 1 : 2), "Fogo: bloom " + i);
                previous = levels[i];
            }

            // Cópia da cor atual; a composição escreve por cima da própria cor ativa (seja qual for a textura
            // que o passe final do URP — SMAA, upscaling — vai ler a seguir).
            desc.name = "FireCompositeSource";
            desc.clearBuffer = false;
            desc.msaaSamples = MSAASamples.None;
            TextureHandle copy = renderGraph.CreateTexture(desc);
            renderGraph.AddBlitPass(source, copy, Vector2.one, Vector2.zero, passName: "Fogo: copiar cor");
            Material composite = materials[0];
            composite.SetFloat(ExposureId, exposure);
            composite.SetFloat(BloomId, bloom);
            using (var builder = renderGraph.AddRasterRenderPass<PassData>("Fogo: composição", out var data))
            {
                data.source = copy;
                data.near = levels[1];
                data.mid = levels[4];
                data.wide = levels[6];
                data.halo = levels[7];
                data.material = composite;
                builder.UseTexture(copy);
                builder.UseTexture(fd.fire);
                builder.UseTexture(data.near);
                builder.UseTexture(data.mid);
                builder.UseTexture(data.wide);
                builder.UseTexture(data.halo);
                builder.SetRenderAttachment(source, 0);
                builder.AllowGlobalStateModification(true);
                builder.SetRenderFunc((PassData d, RasterGraphContext ctx) =>
                {
                    ctx.cmd.SetGlobalTexture("_FireBloomNear", d.near);
                    ctx.cmd.SetGlobalTexture("_FireBloomMid", d.mid);
                    ctx.cmd.SetGlobalTexture("_FireBloomWide", d.wide);
                    ctx.cmd.SetGlobalTexture("_FireBloomHalo", d.halo);
                    Blitter.BlitTexture(ctx.cmd, d.source, new Vector4(1, 1, 0, 0), d.material, 0);
                });
            }
        }
    }
}
