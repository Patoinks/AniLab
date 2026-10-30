using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace AnimLab.KonoSuba
{
    /// <summary>
    /// Definições (Volume do URP) da composição do fogo: o buffer do fogo (FireFX) junta-se ao ecrã depois de
    /// todos os efeitos da cena, com uma pirâmide de bloom própria. O fogo vem pré-multiplicado:
    /// resultado = fogo + cena × (1 − cobertura). Desenhado pela AnimLabRenderFeature.
    /// </summary>
    [Serializable, VolumeComponentMenu("AnimLab/Fire Composite")]
    public sealed class FireComposite : VolumeComponent
    {
        [Tooltip("Exposição do fogo.")]
        public FloatParameter exposure = new FloatParameter(1f);

        [Tooltip("Força do bloom próprio do fogo.")]
        public ClampedFloatParameter bloom = new ClampedFloatParameter(1f, 0f, 3f);
    }
}
