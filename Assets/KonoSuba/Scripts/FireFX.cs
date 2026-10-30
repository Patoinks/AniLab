using System.Collections.Generic;
using UnityEngine;

namespace AnimLab.KonoSuba
{
    /// <summary>
    /// Caminho de cor próprio para o fogo: os renderers registados aqui não são desenhados pela câmara normal
    /// (ficam na layer <see cref="Layer"/>, que a câmara não vê); a renderer feature do AnimLab
    /// (AnimLabRenderFeature) desenha-os num buffer HDR à parte, depois dos transparentes da cena, e junta-o ao
    /// ecrã DEPOIS do color grading, com bloom próprio. Assim as cores do fogo são as que estão nos materiais.
    /// A oclusão pela cena faz-se no shader do fogo, contra a depth texture da câmara (FireFXCommon.cginc).
    /// Ordem: cada peça é tratada como uma casca de raio R à volta do centro dos bounds. Materiais com 2 passes
    /// desenham as faces de trás (passe 0) à distância d+R e as da frente (passe 1) a d−R; tudo junto, de trás
    /// para a frente — assim cascas encaixadas umas nas outras e fitas à volta delas ficam pela ordem certa.
    /// Materiais com o passe "FireColor" desenham-se uma vez, e o passe "FireDepth" resolve antes o interior
    /// opaco por pixel.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class FireFX : MonoBehaviour
    {
        public const int Layer = 30;
        static readonly List<Renderer> renderers = new List<Renderer>();
        static readonly Dictionary<Renderer, float> radii = new Dictionary<Renderer, float>();

        /// <summary>Um desenho do fogo: renderer, material, submalha, passe de cor e passe de profundidade (−1 se não tem).</summary>
        public struct Draw
        {
            public Renderer renderer;
            public Material material;
            public int submesh, pass, depthPass;
            public float depth;
        }

        /// <summary>Passa a desenhar este renderer pelo caminho do fogo.</summary>
        public static void Register(Renderer r, float radius = 0f)
        {
            r.gameObject.layer = Layer;
            radii[r] = radius;
            if (!renderers.Contains(r)) renderers.Add(r);
        }

        public static void Unregister(Renderer r)
        {
            renderers.Remove(r);
            radii.Remove(r);
        }

        /// <summary>Lista de desenho do fogo para esta câmara, de trás para a frente.</summary>
        public static void CollectDraws(Camera cam, List<Draw> draws)
        {
            draws.Clear();
            renderers.RemoveAll(r => r == null);
            Vector3 eye = cam.transform.position, forward = cam.transform.forward;
            foreach (Renderer r in renderers)
            {
                if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
                float d = Vector3.Dot(r.bounds.center - eye, forward);
                float radius = radii.TryGetValue(r, out float rr) ? rr : 0f;
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    Material m = mats[i];
                    if (m == null) continue;
                    int depthPass = m.FindPass("FireDepth"), colorPass = m.FindPass("FireColor");
                    if (colorPass >= 0) draws.Add(new Draw { renderer = r, material = m, submesh = i, pass = colorPass, depthPass = depthPass, depth = d });
                    else if (m.passCount >= 2)
                    {
                        draws.Add(new Draw { renderer = r, material = m, submesh = i, pass = 0, depthPass = depthPass, depth = d + radius });
                        draws.Add(new Draw { renderer = r, material = m, submesh = i, pass = 1, depthPass = depthPass, depth = d - radius });
                    }
                    else draws.Add(new Draw { renderer = r, material = m, submesh = i, pass = 0, depthPass = depthPass, depth = d });
                }
            }
            draws.Sort((a, b) => b.depth.CompareTo(a.depth));
        }

        void OnEnable() => GetComponent<Camera>().cullingMask &= ~(1 << Layer);
    }
}
