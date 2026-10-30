using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace AnimLab.KonoSuba
{
    /// <summary>
    /// Tonemapper próprio do AnimLab (Volume do URP; aplicado pela AnimLabRenderFeature depois do pós-processamento
    /// do URP, com o Tonemapping do URP em None). Curva de Lottes (contraste no pé, ombro suave até ao branco),
    /// aplicada ao canal máximo e escalada nos três canais: a cor não muda de tom ao comprimir (o ACES puxava
    /// os laranjas para amarelo e lavava a saturação). Só as luzes muito fortes rolam para branco (destaques).
    /// </summary>
    [Serializable, VolumeComponentMenu("AnimLab/Tonemap")]
    public sealed class AnimLabTonemap : VolumeComponent
    {
        [Tooltip("Multiplicador de exposição antes da curva.")]
        public MinFloatParameter exposure = new MinFloatParameter(1f, 0f);
        [Tooltip("Contraste (inclinação da curva no pé).")]
        public ClampedFloatParameter contrast = new ClampedFloatParameter(1.25f, 0.8f, 2f);
        [Tooltip("Ombro: quão cedo as altas luzes começam a comprimir.")]
        public ClampedFloatParameter shoulder = new ClampedFloatParameter(0.97f, 0.8f, 1f);
        [Tooltip("Valor HDR que chega a branco.")]
        public MinFloatParameter whitePoint = new MinFloatParameter(12f, 1.1f);
        [Tooltip("Para onde vai o cinzento médio (0,18).")]
        public ClampedFloatParameter midOut = new ClampedFloatParameter(0.2f, 0.05f, 0.5f);
        [Tooltip("Quanto as altas luzes rolam para branco (núcleos de fogo, clarões).")]
        public ClampedFloatParameter highlightDesaturation = new ClampedFloatParameter(0.6f, 0f, 1f);
        [Tooltip("Saturação depois da curva.")]
        public ClampedFloatParameter saturation = new ClampedFloatParameter(1.1f, 0f, 2f);

        public bool IsActive() => active;
    }
}
