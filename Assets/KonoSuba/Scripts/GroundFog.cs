using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace AnimLab.KonoSuba
{
    /// <summary>
    /// Nevoeiro rasteiro calculado no ecrã (definições num Volume do URP; desenhado pela AnimLabRenderFeature).
    /// Para cada pixel mede quanto nevoeiro existe entre a câmara e a cena, em camadas horizontais
    /// com ruído animado e advectado pelo vento. Não há geometria, por isso nada "passa à frente da lente".
    /// Reage a FogField (impactos e vento).
    /// </summary>
    [Serializable, VolumeComponentMenu("AnimLab/Ground Fog")]
    public sealed class GroundFog : VolumeComponent
    {
        [Header("Forma")]
        [Tooltip("Altura do topo do nevoeiro (m).")] public FloatParameter height = new FloatParameter(2.2f);
        public FloatParameter density = new FloatParameter(0.35f);
        [Tooltip("Quanto a densidade cai com a altura.")] public FloatParameter heightFalloff = new FloatParameter(2.4f);
        [Tooltip("Distância máxima em que o nevoeiro é calculado (m).")] public FloatParameter maxDistance = new FloatParameter(90f);

        [Header("Fiapos")]
        public TextureParameter noise = new TextureParameter(null);
        public FloatParameter noiseScale = new FloatParameter(0.05f);
        [Tooltip("Maior = mais buracos entre os fiapos.")] public ClampedFloatParameter coverage = new ClampedFloatParameter(0.38f, 0f, 1f);
        public ClampedFloatParameter softness = new ClampedFloatParameter(0.5f, 0.01f, 1f);
        [Tooltip("Esticar os fiapos na direção do vento.")] public ClampedFloatParameter stretch = new ClampedFloatParameter(0.4f, 0.1f, 1f);
        [Tooltip("Névoa de base sempre presente entre os fiapos.")] public ClampedFloatParameter baseHaze = new ClampedFloatParameter(0.45f, 0f, 1f);

        [Tooltip("A partir desta distância (m) os fiapos começam a diluir-se em névoa uniforme.")]
        public FloatParameter detailStart = new FloatParameter(6f);
        [Tooltip("A partir desta distância (m) o nevoeiro é totalmente uniforme.")]
        public FloatParameter detailEnd = new FloatParameter(28f);

        [Header("Movimento")]
        public Vector2Parameter windDirection = new Vector2Parameter(new Vector2(1f, 0.25f));
        public FloatParameter windSpeed = new FloatParameter(0.6f);
        [Tooltip("Quanto os fiapos se enrolam (flow map de curl).")] public FloatParameter swirl = new FloatParameter(0.35f);

        [Header("Luz")]
        public ColorParameter tint = new ColorParameter(new Color(0.50f, 0.53f, 0.60f));
        public ColorParameter centerGlow = new ColorParameter(new Color(0.30f, 0.32f, 0.38f));
        public FloatParameter centerGlowRadius = new FloatParameter(15f);

        public bool IsActive() => active && density.value > 0f && noise.value != null;
    }
}
