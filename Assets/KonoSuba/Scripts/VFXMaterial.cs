using UnityEngine;
using UnityEngine.Rendering;

namespace AnimLab.KonoSuba
{
    public enum VFXBlend { Additive = 0, Premultiplied = 1, Multiply = 2 }

    /// <summary>Ajudas para materiais do shader-mestre AnimLab/VFX (mistura e técnicas por keyword).</summary>
    public static class VFXMaterial
    {
        public const string ShaderName = "AnimLab/VFX";

        public static Material Create(string name, VFXBlend blend)
        {
            var m = new Material(Shader.Find(ShaderName)) { name = name };
            SetBlend(m, blend);
            return m;
        }

        /// <summary>Aplica o modo de mistura (blend factors e keyword); a cor do shader já vem pré-multiplicada.</summary>
        public static void SetBlend(Material m, VFXBlend blend)
        {
            m.SetFloat("_Blend", (float)blend);
            switch (blend)
            {
                case VFXBlend.Additive:
                    m.SetFloat("_SrcBlend", (float)BlendMode.One);
                    m.SetFloat("_DstBlend", (float)BlendMode.One);
                    break;
                case VFXBlend.Premultiplied:
                    m.SetFloat("_SrcBlend", (float)BlendMode.One);
                    m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                    break;
                case VFXBlend.Multiply:
                    m.SetFloat("_SrcBlend", (float)BlendMode.DstColor);
                    m.SetFloat("_DstBlend", (float)BlendMode.Zero);
                    break;
            }
            SetFeature(m, "_BLEND_MULTIPLY", blend == VFXBlend.Multiply);
        }

        /// <summary>Liga/desliga uma técnica (keyword) e o toggle correspondente, se existir.</summary>
        public static void SetFeature(Material m, string keyword, bool on, string toggle = null)
        {
            if (on) m.EnableKeyword(keyword);
            else m.DisableKeyword(keyword);
            if (toggle != null) m.SetFloat(toggle, on ? 1f : 0f);
        }
    }
}
