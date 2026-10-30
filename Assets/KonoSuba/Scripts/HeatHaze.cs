using UnityEngine;

namespace AnimLab.KonoSuba
{
    /// <summary>
    /// Ar a distorcer à volta de um efeito (ar quente do fogo, p.ex.): um tubo aberto e cónico com um material
    /// de distorção (KonoSuba/Distortion). Não aparece na imagem; só refrata o que está atrás.
    /// uv.x à volta, uv.y de baixo para cima.
    /// </summary>
    public class HeatHaze : MonoBehaviour
    {
        public Material material;
        public float bottomRadius = 1.9f, topRadius = 1.6f, height = 5.5f;

        public static HeatHaze Add(Transform parent, Material material, float bottomRadius, float topRadius, float height)
        {
            var go = new GameObject("Ar quente");
            go.transform.SetParent(parent, false);
            var haze = go.AddComponent<HeatHaze>();
            haze.material = material;
            haze.bottomRadius = bottomRadius;
            haze.topRadius = topRadius;
            haze.height = height;
            return haze;
        }

        void Start()
        {
            gameObject.AddComponent<MeshFilter>().sharedMesh = Tube(bottomRadius, topRadius, height);
            var r = gameObject.AddComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
        }

        static Mesh Tube(float r0, float r1, float h)
        {
            const int Seg = 48, Rings = 24;
            var verts = new Vector3[(Seg + 1) * (Rings + 1)];
            var uvs = new Vector2[verts.Length];
            for (int y = 0; y <= Rings; y++)
                for (int x = 0; x <= Seg; x++)
                {
                    float u = x / (float)Seg, v = y / (float)Rings, a = u * Mathf.PI * 2f, r = Mathf.Lerp(r0, r1, v);
                    int i = y * (Seg + 1) + x;
                    verts[i] = new Vector3(Mathf.Cos(a) * r, v * h, Mathf.Sin(a) * r);
                    uvs[i] = new Vector2(u, v);
                }
            var tris = new int[Seg * Rings * 6];
            int t = 0;
            for (int y = 0; y < Rings; y++)
                for (int x = 0; x < Seg; x++)
                {
                    int a = y * (Seg + 1) + x, b = a + Seg + 1;
                    tris[t++] = a; tris[t++] = a + 1; tris[t++] = b;
                    tris[t++] = a + 1; tris[t++] = b + 1; tris[t++] = b;
                }
            var mesh = new Mesh { name = "Ar quente", vertices = verts, uv = uvs, triangles = tris };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
