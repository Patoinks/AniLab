using System.Collections.Generic;
using UnityEngine;

namespace AnimLab.KonoSuba
{
    /// <summary>
    /// Aviso de parry: a arma de quem ataca acende a dourado (casca com contorno brilhante) e uma estrela 3D
    /// cintila na ponta, a partir de um tempo de reação antes da janela de parry abrir até ao impacto.
    /// Não aparece nos golpes do próprio jogador (só nos que ele pode aparar). Funciona offline e online:
    /// lê o estado do SwordWielder, que no online vem da simulação.
    /// </summary>
    [RequireComponent(typeof(SwordWielder))]
    public class ParryGlint : MonoBehaviour, AnimLab.Netcode.Unity.INetPresentation
    {
        [Tooltip("Antecedência (s) com que o brilho começa antes da janela de parry: o tempo de reação humano.")]
        public float lead = 0.15f;
        [ColorUsage(false, true)] public Color color = new Color(3f, 2.3f, 1.05f);
        [Tooltip("Tamanho da estrela na ponta da arma (m).")]
        public float starSize = 0.32f;

        SwordWielder wielder;
        Material shell, star;
        Mesh diamond;
        MaterialPropertyBlock block;
        readonly Dictionary<Transform, Renderer[]> shells = new Dictionary<Transform, Renderer[]>();
        Transform lit;
        float shown;
        static readonly int IntensityId = Shader.PropertyToID("_Intensity");
        static readonly int ColorId = Shader.PropertyToID("_Color");

        void Awake()
        {
            wielder = GetComponent<SwordWielder>();
            var glint = Shader.Find("KonoSuba/ParryGlint");
            var spark = Shader.Find("KonoSuba/SwordSpark");
            if (glint != null) shell = new Material(glint) { name = "ParryGlint" };
            if (spark != null) star = new Material(spark) { name = "ParryStar" };
            if (star != null) star.SetFloat("_Edge", 0.35f);
            diamond = SwordSparks.DiamondMesh;
            block = new MaterialPropertyBlock();
        }

        void OnDestroy()
        {
            if (shell != null) Destroy(shell);
            if (star != null) Destroy(star);
        }

        // Quanto o golpe que vem pede parry agora (0..1): sobe no tempo de reação antes da janela, cheio na janela.
        float Target()
        {
            if (wielder == null || !wielder.IncomingStrike || wielder.IsViewer) return 0f;
            float t = wielder.TimeToImpact, window = wielder.ParryWindowSeconds;
            if (t <= 0f || t > window + lead) return 0f;
            if (t <= window) return 1f;
            float k = 1f - (t - window) / Mathf.Max(lead, 1e-3f);
            return k * k * (3f - 2f * k);
        }

        void LateUpdate()
        {
            float target = Target();
            // Acende depressa e apaga ainda mais depressa no impacto (não fica a "pedir" parry depois do golpe).
            shown = Mathf.MoveTowards(shown, target, Time.unscaledDeltaTime * (target > shown ? 14f : 24f));
            Transform weapon = shown > 0.001f ? wielder.ActiveWeapon : null;
            if (lit != null && lit != weapon) SetShell(lit, 0f);
            lit = weapon;
            if (weapon == null) return;
            // Cintilar irregular dentro da janela: lê-se como um brilho vivo, não como uma luz fixa.
            float flicker = 0.85f + 0.15f * Mathf.PerlinNoise(Time.unscaledTime * 18f, GetInstanceID() * 0.01f);
            SetShell(weapon, shown * flicker);
            DrawStar(weapon.TransformPoint(wielder.ActiveTipLocal), shown * flicker);
        }

        void SetShell(Transform weapon, float intensity)
        {
            if (shell == null) return;
            if (!shells.TryGetValue(weapon, out var renderers)) shells[weapon] = renderers = BuildShell(weapon);
            foreach (var r in renderers)
            {
                if (r == null) continue;
                r.enabled = intensity > 0.001f;
                r.GetPropertyBlock(block);
                block.SetFloat(IntensityId, intensity);
                block.SetColor(ColorId, color);
                r.SetPropertyBlock(block);
            }
        }

        // Cópia das malhas da arma (e dos filhos) com o material do brilho, presa à arma.
        Renderer[] BuildShell(Transform weapon)
        {
            var list = new List<Renderer>();
            foreach (var filter in weapon.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null || filter.name == "ParryGlintShell") continue;
                var go = new GameObject("ParryGlintShell");
                go.transform.SetParent(filter.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
                var r = go.AddComponent<MeshRenderer>();
                // Uma cópia do material por parte da malha (as armas têm lâmina e punho em partes separadas).
                var mats = new Material[Mathf.Max(1, filter.sharedMesh.subMeshCount)];
                for (int i = 0; i < mats.Length; i++) mats[i] = shell;
                r.sharedMaterials = mats;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                r.enabled = false;
                list.Add(r);
            }
            return list.ToArray();
        }

        // Estrela de quatro pontas na ponta da lâmina: dois losangos 3D cruzados, de frente para a câmara.
        void DrawStar(Vector3 tip, float intensity)
        {
            var cam = Camera.main;
            if (star == null || diamond == null || cam == null || intensity <= 0.001f) return;
            Quaternion face = Quaternion.LookRotation(cam.transform.forward, cam.transform.up);
            float spin = Time.unscaledTime * 40f;
            float size = starSize * (0.6f + 0.4f * intensity);
            block.Clear();
            block.SetColor(ColorId, color * intensity);
            for (int i = 0; i < 2; i++)
            {
                // Losango ao longo de Z: rodado para ficar no plano da câmara, um na vertical e outro na horizontal.
                Quaternion arm = face * Quaternion.Euler(0f, 0f, spin + i * 90f) * Quaternion.Euler(90f, 0f, 0f);
                var m = Matrix4x4.TRS(tip, arm, new Vector3(size * 0.07f, size * 0.07f, size * (i == 0 ? 1f : 0.7f)));
                Graphics.DrawMesh(diamond, m, star, 0, null, 0, block, false, false);
            }
        }
    }
}
