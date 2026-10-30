using UnityEditor;
using UnityEngine;

namespace AnimLab.KonoSuba.EditorTools
{
    /// <summary>
    /// Eletricidade em estilo anime: materiais partilhados (trovão, impacto, Kirin), o ponto de teste do trovão
    /// ("LightningTest") e a configuração do Kirin. Chamado por KonoSubaSceneSetup.Build().
    /// </summary>
    public static class LightningSetup
    {
        const string MatDir = "Assets/KonoSuba/Materials";

        public static void BuildLightningTest()
        {
            var go = new GameObject("LightningTest");
            go.transform.position = new Vector3(-18f, 0f, 22f);
            var demo = go.AddComponent<LightningStrikeDemo>();
            demo.material = Bolt();
            demo.impact = Impact();
        }

        /// <summary>Kirin (tecla 4) com o traço anime, olhos vermelhos e o impacto do trovão.</summary>
        public static void ConfigureKirin(KirinAbility kirin)
        {
            kirin.animeMaterial = Bolt();
            kirin.eyeMaterial = Get("Lightning_AnimeEyes", "KonoSuba/LightningAnime", m =>
            {
                m.SetColor("_Outline", new Color(1f, 0.15f, 0.45f));
                m.SetColor("_Halo", new Color(1f, 0.05f, 0.2f));
                m.SetFloat("_HaloIntensity", 1.6f);
            });
            kirin.impact = Impact();
            EditorUtility.SetDirty(kirin);
        }

        /// <summary>Trovão (tecla 9): o raio anime com o impacto no chão.</summary>
        public static void ConfigureThunder(ThunderAbility thunder)
        {
            thunder.boltMaterial = Bolt();
            thunder.impact = Impact();
            EditorUtility.SetDirty(thunder);
        }

        static Material Bolt() => Get("Lightning_Anime", "KonoSuba/LightningAnime", m => { });

        static LightningImpactMaterials Impact() => new LightningImpactMaterials
        {
            bolt = Bolt(),
            cracks = Get("Lightning_Cracks", "KonoSuba/LightningCracks", m => { }),
            shockwave = Get("Lightning_Shockwave", "KonoSuba/Distortion", m =>
            {
                m.SetFloat("_Mode", 2f);
                m.SetFloat("_Strength", 0.012f);
            }),
            debris = Get("Lightning_Debris", "Universal Render Pipeline/Lit", m =>
            {
                m.color = new Color(0.12f, 0.12f, 0.14f);
                m.SetFloat("_Smoothness", 0.2f);
            }),
        };

        // Carrega o material se já existir; senão cria-o e configura-o.
        static Material Get(string name, string shader, System.Action<Material> setup)
        {
            string path = MatDir + "/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m != null) return m;
            m = new Material(Shader.Find(shader)) { name = name };
            setup(m);
            AssetDatabase.CreateAsset(m, path);
            return m;
        }
    }
}
