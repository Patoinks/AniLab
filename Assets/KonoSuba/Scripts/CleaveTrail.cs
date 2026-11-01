using UnityEngine;

namespace AnimLab.KonoSuba
{
    /// <summary>
    /// Rasto de cleave dos golpes de espada: um crescente 3D construído a partir do próprio arco do golpe
    /// (não das posições de frame a frame), por isso sai sempre liso e igual offline e online.
    /// A cor é a da mana do lutador (gradiente entre as duas árvores escolhidas); sem árvores, aço claro.
    /// </summary>
    [RequireComponent(typeof(SwordWielder))]
    public class CleaveTrail : MonoBehaviour, AnimLab.Netcode.Unity.INetPresentation
    {
        [Tooltip("Quanto do arco o rasto cobre atrás da lâmina (0..1 do golpe).")]
        [Range(0.1f, 1f)] public float length = 0.6f;
        [Tooltip("Tempo (s) que o rasto leva a desaparecer depois do golpe.")]
        public float fadeTime = 0.16f;
        public float intensity = 1.6f;
        [ColorUsage(false, true)] public Color steel = new Color(0.75f, 0.85f, 1.1f);

        const int Segments = 28;
        SwordWielder wielder;
        CharacterStats stats;
        Mesh mesh;
        MeshRenderer view;
        Material material;
        MaterialPropertyBlock block;
        readonly Vector3[] verts = new Vector3[(Segments + 1) * 2];
        readonly Vector2[] uvs = new Vector2[(Segments + 1) * 2];
        float fading = 1f;
        int lastSwing = -1;
        float lastHead;
        static readonly int ColorAId = Shader.PropertyToID("_ColorA"), ColorBId = Shader.PropertyToID("_ColorB");
        static readonly int IntensityId = Shader.PropertyToID("_Intensity"), FadeId = Shader.PropertyToID("_Fade");

        void Awake()
        {
            wielder = GetComponent<SwordWielder>();
            stats = GetComponent<CharacterStats>();
            var shader = Shader.Find("KonoSuba/CleaveTrail");
            if (shader == null) { enabled = false; return; }
            material = new Material(shader) { name = "CleaveTrail" };
            mesh = new Mesh { name = "CleaveTrail" };
            mesh.MarkDynamic();
            var tris = new int[Segments * 6];
            for (int i = 0, t = 0; i < Segments; i++)
            {
                int a = i * 2;
                tris[t++] = a; tris[t++] = a + 2; tris[t++] = a + 1;
                tris[t++] = a + 1; tris[t++] = a + 2; tris[t++] = a + 3;
            }
            mesh.vertices = verts; mesh.uv = uvs; mesh.triangles = tris;
            // Fora da hierarquia do lutador: os vértices já estão no mundo.
            var go = new GameObject(name + " CleaveTrail");
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            view = go.AddComponent<MeshRenderer>();
            view.sharedMaterial = material;
            view.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            view.receiveShadows = false;
            view.enabled = false;
            block = new MaterialPropertyBlock();
        }

        void OnDestroy()
        {
            if (view != null) Destroy(view.gameObject);
            if (mesh != null) Destroy(mesh);
            if (material != null) Destroy(material);
        }

        void LateUpdate()
        {
            if (wielder == null || view == null) return;
            if (wielder.CleaveArc(out int swing, out float head))
            {
                // Golpe a decorrer: o rasto vai da cauda (head - length) até à lâmina.
                fading = 0f; lastSwing = swing; lastHead = head;
                Build(swing, Mathf.Max(0f, head - length), head);
            }
            else if (lastSwing >= 0 && fading < 1f)
            {
                // Depois do golpe, o crescente fica onde acabou e desvanece da cauda para a frente.
                fading = Mathf.Min(1f, fading + Time.deltaTime / Mathf.Max(0.01f, fadeTime));
                Build(lastSwing, Mathf.Max(0f, lastHead - length), lastHead);
            }
            else { view.enabled = false; return; }

            Colors(out Color a, out Color b);
            block.SetColor(ColorAId, a);
            block.SetColor(ColorBId, b);
            block.SetFloat(IntensityId, intensity);
            block.SetFloat(FadeId, fading);
            view.SetPropertyBlock(block);
            view.enabled = true;
        }

        void Build(int swing, float from, float to)
        {
            if (to - from < 0.01f) { view.enabled = false; return; }
            for (int i = 0; i <= Segments; i++)
            {
                float k = i / (float)Segments;
                wielder.BladeAt(swing, Mathf.Lerp(from, to, k), out Vector3 root, out Vector3 tip);
                verts[i * 2] = root; verts[i * 2 + 1] = tip;
                uvs[i * 2] = new Vector2(k, 0f); uvs[i * 2 + 1] = new Vector2(k, 1f);
            }
            mesh.vertices = verts;
            mesh.uv = uvs;
            mesh.RecalculateBounds();
        }

        // Mana do lutador: cauda na cor da árvore A, junto à lâmina na da B (a mesma ordem da barra de mana).
        void Colors(out Color a, out Color b)
        {
            var net = AnimLab.Netcode.Unity.NetMatch.Active;
            bool own = net == null || net.LocalActor == gameObject;
            if (own && stats != null && stats.ManaColors(out a, out b)) { a *= 1.6f; b *= 1.6f; return; }
            a = b = steel;
        }
    }
}
