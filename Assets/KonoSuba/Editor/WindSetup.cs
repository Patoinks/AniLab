using UnityEditor;
using UnityEngine;

namespace AnimLab.KonoSuba.EditorTools
{
    /// <summary>
    /// Vento: materiais partilhados (tornado de teste e Bola de Vento), a peça de teste "WindTest" e a habilidade
    /// da tecla 6. Chamado por KonoSubaSceneSetup.Build().
    /// </summary>
    public static class WindSetup
    {
        const string MatDir = "Assets/KonoSuba/Materials";

        public static void BuildWindTest()
        {
            var go = new GameObject("WindTest");
            go.transform.position = new Vector3(6f, 0f, 6f);
            var tornado = go.AddComponent<WindTornado>();
            tornado.streakMaterial = Streak();
            tornado.sparkMaterial = Spark();
            tornado.shellMaterial = Shell();
            tornado.coreMaterial = Core();
            tornado.discMaterial = Disc();
            tornado.dustMaterial = Dust();
            tornado.distortionMaterial = Distortion();
        }

        /// <summary>Bola de Vento na tecla 6 (slot 5), canalizada como a Bola de Água.</summary>
        public static void BuildWindBallAbility(GameObject player)
        {
            var wind = player.GetComponent<WindBallAbility>();
            if (wind == null)
            {
                wind = player.AddComponent<WindBallAbility>();
                wind.displayName = "Wind Ball";
                wind.element = AbilityElement.Air;
                wind.slot = 5;
                wind.manaCost = 0f;
                wind.cooldown = 0.6f;
            }
            wind.streakMaterial = Streak();
            wind.sparkMaterial = Spark();
            wind.shellMaterial = Shell();
            wind.coreMaterial = Core();
            wind.discMaterial = Disc();
            wind.dustMaterial = Dust();
            wind.distortionMaterial = Distortion();
            wind.ballDustMaterial = Get("Wind_BallDust", "KonoSuba/WindDust", m =>
            {
                m.SetFloat("_Shape", 1f);
                m.SetFloat("_Radius", 0.6f);
                m.SetFloat("_Size", 0.022f);
                m.SetFloat("_Trail", 5f);
                m.SetFloat("_Intensity", 7f);
            });
            EditorUtility.SetDirty(wind);
        }

        static Material Streak() => Get("Wind_Streak", "KonoSuba/WindStreak", m =>
        {
            m.SetFloat("_Intensity", 2.8f);
            m.SetFloat("_Window", 0.55f);
            m.SetFloat("_Base", 0.3f);
        });

        static Material Spark() => Get("Wind_Spark", "KonoSuba/WindStreak", m =>
        {
            m.SetFloat("_Intensity", 9f);
            m.SetFloat("_Window", 0.035f);
            m.SetFloat("_Rate", 0.22f);
            m.SetFloat("_Base", 0f);
            m.SetFloat("_Solid", 1f);
            m.SetFloat("_Heat", 0.4f);
        });

        static Material Shell() => Get("Wind_Shell", "KonoSuba/WindStreak", m =>
        {
            m.SetFloat("_Intensity", 0.6f);
            m.SetFloat("_Mode", 1f);
            m.SetFloat("_Heat", -0.15f);
        });

        static Material Core() => Get("Wind_Core", "KonoSuba/WindStreak", m =>
        {
            m.SetFloat("_Intensity", 1.1f);
            m.SetFloat("_Mode", 1f);
            m.SetFloat("_Shear", 4f);
            m.SetFloat("_Heat", 0.1f);
        });

        static Material Disc() => Get("Wind_Disc", "KonoSuba/WindStreak", m =>
        {
            m.SetFloat("_Intensity", 0.7f);
            m.SetFloat("_Mode", 2f);
            m.SetFloat("_Wobble", 0f);
        });

        static Material Dust() => Get("Wind_Dust", "KonoSuba/WindDust", m => { });

        // Ar a girar à volta do tornado: o ruído roda depressa e sobe devagar.
        static Material Distortion() => Get("Wind_Distortion", "KonoSuba/Distortion", m =>
        {
            m.SetFloat("_Mode", 1f);
            m.SetFloat("_Strength", 0.006f);
            m.SetVector("_Scale", new Vector4(9f, 3f, 0f, 0f));
            m.SetVector("_Speed", new Vector4(0.9f, 0.25f, 0f, 0f));
        });

        // Carrega o material se já existir (o tornado e a habilidade partilham-no); senão cria-o e configura-o.
        static Material Get(string name, string shader, System.Action<Material> setup)
        {
            string path = MatDir + "/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m != null) return m;
            m = new Material(Shader.Find(shader));
            setup(m);
            AssetDatabase.CreateAsset(m, path);
            return m;
        }
    }
}
