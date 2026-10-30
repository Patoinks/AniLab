using System.IO;
using UnityEditor;
using UnityEngine;

namespace AnimLab.KonoSuba.EditorTools
{
    /// <summary>
    /// Laboratório do shader-mestre AnimLab/VFX: três peças 3D, cada uma com uma combinação de técnicas
    /// (gradient map, distorção de UV, erosão com borda, fresnel, deformação de vértices, soft particles),
    /// os gradient maps base (fogo, arcano, vento, fumo) e, se existir, o fogo em flipbook do Blender.
    /// Chamado por KonoSubaSceneSetup.Build().
    /// </summary>
    public static class VFXLabSetup
    {
        const string MatDir = "Assets/KonoSuba/Materials";
        const string TexDir = "Assets/KonoSuba/Textures";
        const string GradDir = VFXTextureImporter.Root + "Gradients";
        const string FlipDir = VFXTextureImporter.Root + "Flipbooks";
        const string MotionDir = VFXTextureImporter.Root + "Motion";

        [System.Serializable]
        class FlipbookInfo { public int[] grid; public int frames; public int fps; public float motionStrength; }

        public static void Build()
        {
            var noise = AssetDatabase.LoadAssetAtPath<Texture2D>(TexDir + "/FogNoise.png");
            var fire = GradientMapBaker.Bake(GradDir + "/Gradient_Fire.png",
                ("#1a0402", 0f), ("#7a1206", 0.25f), ("#ff5b1a", 0.5f), ("#ffb823", 0.72f), ("#fff6a7", 0.9f), ("#ffffff", 1f));
            var arcane = GradientMapBaker.Bake(GradDir + "/Gradient_Arcane.png",
                ("#0b0218", 0f), ("#3a0a6e", 0.3f), ("#b03cff", 0.6f), ("#ff9cf2", 0.85f), ("#ffffff", 1f));
            var smoke = GradientMapBaker.Bake(GradDir + "/Gradient_Smoke.png",
                ("#050506", 0f), ("#2a2b30", 0.5f), ("#6a6c74", 1f));
            GradientMapBaker.Bake(GradDir + "/Gradient_Wind.png",
                ("#04142b", 0f), ("#0b7fa8", 0.35f), ("#73ffd9", 0.7f), ("#ffffff", 1f));

            var root = new GameObject("VFXLab");
            root.transform.position = new Vector3(0f, 0f, 14f);

            // 1) Orbe de fogo: aditivo, gradient map de fogo, UV a subir distorcida, vértices a ferver, brilho na borda.
            var orb = Save("VFX_FireOrb", VFXBlend.Additive, noise, m =>
            {
                m.SetVector("_MainPan", new Vector4(0.05f, 0.35f, 0f, 0f));
                m.SetTextureScale("_MainTex", new Vector2(2f, 1.5f));
                Feature(m, "_UV_DISTORT", "_UseDistort");
                m.SetFloat("_DistortStrength", 0.25f);
                Feature(m, "_GRADIENT_MAP", "_UseGradient");
                m.SetTexture("_GradientTex", fire);
                m.SetFloat("_GradientScale", 1.3f);
                m.SetFloat("_Intensity", 2.2f);
                Feature(m, "_FRESNEL", "_UseFresnel");
                m.SetColor("_FresnelColor", new Color(2f, 0.7f, 0.2f));
                Feature(m, "_VERTEX_OFFSET", "_UseVertex");
                m.SetFloat("_VertexOffset", 0.06f);
                m.SetFloat("_VertexSpeed", 1.6f);
            });
            Piece(root.transform, "Orbe de fogo", Resources.GetBuiltinResource<Mesh>("New-Sphere.fbx"), orb, new Vector3(-3.2f, 1.6f, 0f), Vector3.one * 1.6f);

            // 2) Anel arcano: aditivo, gradient arcano, erosão a pulsar com borda queimada, pan à volta.
            var ring = Save("VFX_ArcaneRing", VFXBlend.Additive, noise, m =>
            {
                m.SetVector("_MainPan", new Vector4(0.4f, 0f, 0f, 0f));
                m.SetTextureScale("_MainTex", new Vector2(4f, 1f));
                Feature(m, "_GRADIENT_MAP", "_UseGradient");
                m.SetTexture("_GradientTex", arcane);
                m.SetFloat("_Intensity", 2.5f);
                Feature(m, "_EROSION", "_UseErosion");
                m.SetFloat("_Erosion", 0.35f);
                m.SetFloat("_EdgeWidth", 0.08f);
                m.SetColor("_EdgeColor", new Color(3f, 1.2f, 4f));
                Feature(m, "_FRESNEL", "_UseFresnel");
                m.SetColor("_FresnelColor", new Color(0.8f, 0.3f, 1.6f));
            });
            var ringGo = Piece(root.transform, "Anel arcano", Torus(1f, 0.22f), ring, new Vector3(0f, 1.6f, 0f), Vector3.one);
            ringGo.AddComponent<VFXLabMotion>().spin = new Vector3(25f, 60f, 0f);
            ringGo.GetComponent<VFXLabMotion>().erosionPulse = true;

            // 3) Coluna de fumo: pré-multiplicado escuro, gradient de fumo, erosão suave, soft particles no chão.
            var column = Save("VFX_SmokeColumn", VFXBlend.Premultiplied, noise, m =>
            {
                m.SetVector("_MainPan", new Vector4(0.02f, 0.25f, 0f, 0f));
                m.SetTextureScale("_MainTex", new Vector2(2f, 1f));
                Feature(m, "_UV_DISTORT", "_UseDistort");
                m.SetFloat("_DistortStrength", 0.35f);
                Feature(m, "_GRADIENT_MAP", "_UseGradient");
                m.SetTexture("_GradientTex", smoke);
                Feature(m, "_EROSION", "_UseErosion");
                m.SetFloat("_Erosion", 0.3f);
                m.SetFloat("_ErosionSoftness", 0.35f);
                m.SetFloat("_EdgeWidth", 0f);
                Feature(m, "_SOFT_PARTICLES", "_UseSoft");
                m.SetFloat("_SoftDistance", 0.6f);
                Feature(m, "_FRESNEL", "_UseFresnel");
                m.SetFloat("_FresnelMode", 0f);
                m.SetColor("_FresnelColor", new Color(0.05f, 0.05f, 0.06f));
                m.SetColor("_Color", new Color(1f, 1f, 1f, 0.85f));
            });
            Piece(root.transform, "Coluna de fumo", Resources.GetBuiltinResource<Mesh>("Cylinder.fbx"), column, new Vector3(3.2f, 1.4f, 0f), new Vector3(1.4f, 1.4f, 1.4f));

            // 4) Fogo em flipbook (Blender, Tools/Blender/flipbook_bake.py): gradient map de fogo, frames interpolados
            //    pelos motion vectors, em três cartas cruzadas fixas no mundo (não viradas para a câmara).
            const string name = "Fire_Loop_8x8";
            var sheet = AssetDatabase.LoadAssetAtPath<Texture2D>(FlipDir + "/" + name + ".png");
            var motion = AssetDatabase.LoadAssetAtPath<Texture2D>(MotionDir + "/" + name + "_MV.png");
            var json = AssetDatabase.LoadAssetAtPath<TextAsset>(FlipDir + "/" + name + ".json");
            if (sheet != null && json != null)
            {
                var info = JsonUtility.FromJson<FlipbookInfo>(json.text);
                var flip = Save("VFX_FireFlipbook", VFXBlend.Additive, sheet, m =>
                {
                    Feature(m, "_FLIPBOOK", "_UseFlipbook");
                    m.SetVector("_FlipbookGrid", new Vector4(info.grid[0], info.grid[1], info.fps, 0f));
                    if (motion != null)
                    {
                        Feature(m, "_FLIPBOOK_MV", "_UseMotion");
                        m.SetTexture("_MotionTex", motion);
                        m.SetFloat("_MotionStrength", info.motionStrength);
                    }
                    Feature(m, "_GRADIENT_MAP", "_UseGradient");
                    m.SetTexture("_GradientTex", fire);
                    m.SetFloat("_GradientScale", 0.95f);
                    m.SetFloat("_Intensity", 1.0f);
                    m.SetFloat("_UVEdgeFade", 0.12f);
                    Feature(m, "_SOFT_PARTICLES", "_UseSoft");
                    m.SetFloat("_SoftDistance", 0.4f);
                });
                Piece(root.transform, "Fogo em flipbook", CrossCards(2.4f), flip, new Vector3(0f, 0f, 3.5f), Vector3.one);
            }
        }

        // Três cartas verticais cruzadas a 60° (base no chão), UV inteiras em cada uma.
        static Mesh CrossCards(float size)
        {
            var verts = new Vector3[12];
            var uvs = new Vector2[12];
            var normals = new Vector3[12];
            var tris = new int[18];
            for (int c = 0; c < 3; c++)
            {
                Quaternion rot = Quaternion.Euler(0f, c * 60f, 0f);
                Vector3 right = rot * Vector3.right * (size * 0.5f), n = rot * Vector3.back;
                int v = c * 4;
                verts[v] = -right; verts[v + 1] = right; verts[v + 2] = right + Vector3.up * size; verts[v + 3] = -right + Vector3.up * size;
                uvs[v] = new Vector2(0, 0); uvs[v + 1] = new Vector2(1, 0); uvs[v + 2] = new Vector2(1, 1); uvs[v + 3] = new Vector2(0, 1);
                for (int k = 0; k < 4; k++) normals[v + k] = n;
                int t = c * 6;
                tris[t] = v; tris[t + 1] = v + 2; tris[t + 2] = v + 1;
                tris[t + 3] = v; tris[t + 4] = v + 3; tris[t + 5] = v + 2;
            }
            var mesh = new Mesh { name = "Cartas cruzadas", vertices = verts, uv = uvs, normals = normals, triangles = tris };
            mesh.RecalculateBounds();
            return mesh;
        }

        static void Feature(Material m, string keyword, string toggle) => VFXMaterial.SetFeature(m, keyword, true, toggle);

        static Material Save(string name, VFXBlend blend, Texture2D mainTex, System.Action<Material> setup)
        {
            var m = VFXMaterial.Create(name, blend);
            m.SetTexture("_MainTex", mainTex);
            setup(m);
            AssetDatabase.CreateAsset(m, MatDir + "/" + name + ".mat");
            return m;
        }

        static GameObject Piece(Transform parent, string name, Mesh mesh, Material material, Vector3 position, Vector3 scale)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localScale = scale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }

        // Toro com UVs: u à volta do anel, v à volta do tubo.
        static Mesh Torus(float radius, float tube)
        {
            const int Seg = 96, Sides = 24;
            var verts = new Vector3[(Seg + 1) * (Sides + 1)];
            var normals = new Vector3[verts.Length];
            var uvs = new Vector2[verts.Length];
            for (int i = 0; i <= Seg; i++)
                for (int j = 0; j <= Sides; j++)
                {
                    float u = i / (float)Seg * Mathf.PI * 2f, v = j / (float)Sides * Mathf.PI * 2f;
                    var centre = new Vector3(Mathf.Cos(u), 0f, Mathf.Sin(u)) * radius;
                    var n = new Vector3(Mathf.Cos(u) * Mathf.Cos(v), Mathf.Sin(v), Mathf.Sin(u) * Mathf.Cos(v));
                    int k = i * (Sides + 1) + j;
                    verts[k] = centre + n * tube;
                    normals[k] = n;
                    uvs[k] = new Vector2(i / (float)Seg, j / (float)Sides);
                }
            var tris = new int[Seg * Sides * 6];
            int t = 0;
            for (int i = 0; i < Seg; i++)
                for (int j = 0; j < Sides; j++)
                {
                    int a = i * (Sides + 1) + j, b = a + Sides + 1;
                    tris[t++] = a; tris[t++] = a + 1; tris[t++] = b;
                    tris[t++] = a + 1; tris[t++] = b + 1; tris[t++] = b;
                }
            var mesh = new Mesh { name = "Toro", vertices = verts, normals = normals, uv = uvs, triangles = tris };
            mesh.RecalculateBounds();
            return mesh;
        }
    }

    /// <summary>Gradient maps: gradientes de cor cozidos numa textura 256×4 (sRGB, sem mipmaps, clamp).</summary>
    public static class GradientMapBaker
    {
        public static Texture2D Bake(string path, params (string hex, float t)[] keys)
        {
            var colors = new GradientColorKey[keys.Length];
            for (int i = 0; i < keys.Length; i++)
            {
                ColorUtility.TryParseHtmlString(keys[i].hex, out Color c);
                colors[i] = new GradientColorKey(c, keys[i].t);
            }
            var g = new Gradient();
            g.SetKeys(colors, new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            return Bake(path, g);
        }

        public static Texture2D Bake(string path, Gradient gradient)
        {
            const int W = 256, H = 4;
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
            for (int x = 0; x < W; x++)
            {
                Color c = gradient.Evaluate(x / (W - 1f));
                for (int y = 0; y < H; y++) tex.SetPixel(x, y, c);
            }
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.sRGBTexture = true;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
    }
}
