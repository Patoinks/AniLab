using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace AnimLab.KonoSuba.EditorTools
{
    /// <summary>
    /// Inspetor do shader-mestre AnimLab/VFX: a base sempre visível e cada técnica numa secção que só mostra os
    /// seus parâmetros quando está ligada. A mistura aplica os blend factors (VFXMaterial.SetBlend).
    /// </summary>
    public class AnimLabVFXGUI : ShaderGUI
    {
        static readonly string[] Base = { "_Cull", "_Color", "_Intensity", "_MainTex", "_MainPan", "_MaskChannel", "_UVEdgeFade" };

        // Toggle da técnica → parâmetros que só aparecem com ela ligada.
        static readonly (string toggle, string[] props)[] Sections =
        {
            ("_UseDistort", new[] { "_NoiseScale", "_NoisePan", "_DistortStrength" }),
            ("_UseFlow", new[] { "_FlowTex", "_FlowStrength", "_FlowSpeed" }),
            ("_UseFlipbook", new[] { "_FlipbookGrid", "_UseMotion", "_MotionTex", "_MotionStrength" }),
            ("_UseGradient", new[] { "_GradientTex", "_GradientScale", "_GradientShift" }),
            ("_UseErosion", new[] { "_Erosion", "_ErosionSoftness", "_ErosionNoiseMix", "_EdgeWidth", "_EdgeColor" }),
            ("_UseSoft", new[] { "_SoftDistance" }),
            ("_UseFresnel", new[] { "_FresnelMode", "_FresnelPower", "_FresnelColor" }),
            ("_UseVertex", new[] { "_VertexOffset", "_VertexNoiseScale", "_VertexSpeed" }),
        };

        public override void OnGUI(MaterialEditor editor, MaterialProperty[] properties)
        {
            var map = new Dictionary<string, MaterialProperty>();
            foreach (MaterialProperty p in properties) map[p.name] = p;

            EditorGUILayout.LabelField("Base", EditorStyles.boldLabel);
            if (map.TryGetValue("_Blend", out MaterialProperty blend))
            {
                EditorGUI.BeginChangeCheck();
                editor.ShaderProperty(blend, blend.displayName);
                if (EditorGUI.EndChangeCheck())
                    foreach (Object o in editor.targets)
                        VFXMaterial.SetBlend((Material)o, (VFXBlend)(int)blend.floatValue);
            }
            foreach (string name in Base)
                if (map.TryGetValue(name, out MaterialProperty p)) editor.ShaderProperty(p, p.displayName);

            foreach (var (toggle, props) in Sections)
            {
                if (!map.TryGetValue(toggle, out MaterialProperty t)) continue;
                EditorGUILayout.Space(6);
                editor.ShaderProperty(t, t.displayName);
                if (t.floatValue < 0.5f) continue;
                EditorGUI.indentLevel++;
                foreach (string name in props)
                {
                    if (!map.TryGetValue(name, out MaterialProperty p)) continue;
                    // Os motion vectors só fazem sentido com a interpolação ligada.
                    if ((name == "_MotionTex" || name == "_MotionStrength") && map["_UseMotion"].floatValue < 0.5f) continue;
                    editor.ShaderProperty(p, p.displayName);
                }
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space(6);
            editor.RenderQueueField();
        }
    }
}
