using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace AnimLab.KonoSuba.EditorTools
{
    /// <summary>
    /// Gelo: o material das lâminas e a pré-visualização da flor de gelo para a captura
    /// (-captureCall AnimLab.KonoSuba.EditorTools.IceSetup.BuildIcePreview; não fica na cena guardada).
    /// </summary>
    public static class IceSetup
    {
        const string MatDir = "Assets/KonoSuba/Materials";

        public static void BuildIcePreview()
        {
            var go = new GameObject("IcePreview");
            go.transform.position = GameObject.Find("DummyEnemy").transform.position;
            go.AddComponent<MeshFilter>().sharedMesh = IceShard.Burst(7);
            go.AddComponent<MeshRenderer>().sharedMaterial = Ice();
            go.AddComponent<VFXLabMotion>().spin = new Vector3(0f, 9f, 0f);
        }

        /// <summary>
        /// Explosão de gelo montada com o kit do Blender, só para a captura (-captureCall …IceSetup.BuildBurstPreview):
        /// 8 picos grandes em estrela com a curva para fora, 8 menores entre eles, placas no chão, lascas e a névoa.
        /// </summary>
        public static void BuildBurstPreview()
        {
            var meshes = new Dictionary<string, Mesh>();
            foreach (Object o in AssetDatabase.LoadAllAssetsAtPath("Assets/KonoSuba/VFX/Ice/IceKit.fbx"))
                if (o is Mesh mesh) meshes[mesh.name] = mesh;
            Material ice = Crystal();
            var root = new GameObject("IceBurstPreview");
            root.transform.position = GameObject.Find("DummyEnemy").transform.position;
            var rng = new System.Random(5);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);

            void Piece(string name, float angleDeg, float dist, float lean, float scale)
            {
                var go = new GameObject(name);
                go.transform.SetParent(root.transform, false);
                float a = angleDeg * Mathf.Deg2Rad;
                var outDir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                go.transform.localPosition = outDir * dist;
                // No Unity a curva do pico aponta para -X: roda-se para -X ficar virado para fora e inclina-se para fora.
                Vector3 up = Vector3.up * Mathf.Cos(lean * Mathf.Deg2Rad) + outDir * Mathf.Sin(lean * Mathf.Deg2Rad);
                go.transform.localRotation = Quaternion.FromToRotation(Vector3.up, up) * Quaternion.Euler(0f, 180f - angleDeg, 0f);
                go.transform.localScale = Vector3.one * scale;
                go.AddComponent<MeshFilter>().sharedMesh = meshes[name];
                go.AddComponent<MeshRenderer>().sharedMaterial = ice;
            }

            for (int i = 0; i < 8; i++)
            {
                float ang = (i + R(-0.12f, 0.12f)) * 45f;
                Piece("Spike_P" + (1 + (i * 3) % 5), ang, R(0.7f, 1.1f), R(5f, 18f), R(0.85f, 1.05f));
                Piece("Spike_P" + (1 + (i * 2 + 1) % 5), ang + 22.5f + R(-6f, 6f), R(1.7f, 2.3f), R(15f, 30f), R(0.42f, 0.58f));
            }
            Piece("Ground_Ring", 0f, 0f, 0f, 1.2f);
            Piece("Ground_Star", 37f, 0f, 0f, 1.1f);
            for (int i = 0; i < 18; i++)
            {
                string shard = "Shard_" + (1 + i % 11).ToString("00");
                Piece(shard, R(0f, 360f), R(2.6f, 4.6f), R(-40f, 40f), R(0.8f, 1.4f));
            }

            BuildMistPreview();
            GameObject.Find("IceMistPreview").transform.SetParent(root.transform, true);
            var light = new GameObject("IceGlow").AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(0.45f, 0.75f, 1f);
            light.intensity = 8f;
            light.range = 9f;
            light.transform.SetParent(root.transform, false);
            light.transform.localPosition = Vector3.up * 1.2f;
            root.AddComponent<VFXLabMotion>().spin = new Vector3(0f, 8f, 0f);
        }

        public static Material Crystal()
        {
            const string path = MatDir + "/IceCrystal.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("KonoSuba/IceCrystal")) { name = "IceCrystal" };
                AssetDatabase.CreateAsset(m, path);
            }
            m.shader = Shader.Find("KonoSuba/IceCrystal");
            m.SetTexture("_Noise", VolumeNoiseBaker.Get());
            // Folha de assets: manchas de azul fundo, arestas finas e discretas, fraturas com arco-íris.
            m.SetColor("_Deep", new Color(0.01f, 0.04f, 0.13f));
            m.SetColor("_Mid", new Color(0.2f, 0.45f, 0.82f));
            m.SetColor("_Pale", new Color(0.68f, 0.86f, 1f));
            m.SetFloat("_Opacity", 0.62f);
            m.SetFloat("_MarbleScale", 0.8f);
            m.SetColor("_EdgeColor", new Color(0.55f, 0.66f, 0.78f));
            m.SetFloat("_EdgeWidth", 0.75f);
            m.SetFloat("_Fractures", 0.8f);
            m.SetFloat("_Iridescence", 0.8f);
            m.SetColor("_Glow", new Color(0.07f, 0.18f, 0.45f));
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>Névoa de gelo à volta do boneco, só para a captura (-captureCall …IceSetup.BuildMistPreview).</summary>
        public static void BuildMistPreview()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "IceMistPreview";
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.position = GameObject.Find("DummyEnemy").transform.position + Vector3.up * 1.3f;
            go.transform.localScale = new Vector3(10f, 2.6f, 10f);
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = Mist();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        public static Material Mist()
        {
            const string path = MatDir + "/IceMist.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("KonoSuba/IceMist")) { name = "IceMist" };
                AssetDatabase.CreateAsset(m, path);
            }
            m.shader = Shader.Find("KonoSuba/IceMist");
            m.SetTexture("_Noise", VolumeNoiseBaker.Get());
            m.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Back);
            m.SetFloat("_ZTest", (float)UnityEngine.Rendering.CompareFunction.LessEqual);
            m.SetFloat("_Radius", 4.8f);
            m.SetFloat("_Height", 2.6f);
            m.SetFloat("_Density", 2.6f);
            m.SetFloat("_Absorb", 1.8f);
            m.SetFloat("_NoiseScale", 0.3f);
            m.SetFloat("_Erode", 0.5f);
            m.SetFloat("_Ring", 0.62f);
            // Folha de conceito: vapor branco-azulado, sombras azul frio, brilho ciano de energia junto ao chão.
            m.SetColor("_LitColor", new Color(0.86f, 0.94f, 1f));
            m.SetColor("_ShadowColor", new Color(0.16f, 0.32f, 0.58f));
            m.SetColor("_CoreColor", new Color(0.18f, 0.45f, 0.95f));
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>Material das lâminas (os valores são reaplicados a cada build, para afinar por código).</summary>
        public static Material Ice()
        {
            string path = MatDir + "/Ice.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("KonoSuba/Ice")) { name = "Ice" };
                AssetDatabase.CreateAsset(m, path);
            }
            m.shader = Shader.Find("KonoSuba/Ice");
            // Gelo leitoso e claro como na referência: tons altos, azul só nas facetas de sombra.
            m.SetColor("_Deep", new Color(0.16f, 0.38f, 0.62f));
            m.SetColor("_Mid", new Color(0.45f, 0.74f, 0.95f));
            m.SetColor("_Pale", new Color(0.86f, 0.97f, 1f));
            m.SetFloat("_Brightness", 0.95f);
            m.SetFloat("_Opacity", 0.62f);
            m.SetColor("_Tint", new Color(0.5f, 0.82f, 1f));
            m.SetFloat("_Refraction", 0.05f);
            m.SetColor("_CrackColor", new Color(0.75f, 0.92f, 1f));
            m.SetFloat("_CrackScale", 1.8f);
            m.SetFloat("_CrackStrength", 0.45f);
            m.SetColor("_EdgeColor", new Color(0.9f, 1.05f, 1.15f));
            m.SetFloat("_EdgeWidth", 0.018f);
            m.SetColor("_RimColor", new Color(0.55f, 0.85f, 1.05f));
            m.SetColor("_GlintColor", new Color(1.8f, 2.1f, 2.3f));
            m.SetFloat("_FrostHeight", 0.32f);
            EditorUtility.SetDirty(m);
            return m;
        }
    }
}
