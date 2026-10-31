using UnityEditor;
using UnityEngine;

namespace AnimLab.KonoSuba.EditorTools
{
    /// <summary>
    /// Boneco de treino do Astra (concept gerada → modelo no Blender → Tools/Blender/export_dummy.py → FBX)
    /// no lugar da cápsula antiga do DummyEnemy. Mantém o DummyEnemy (alvo, vida, círculo de seleção) e troca
    /// só o corpo: o modelo vai como filho "Body" (o Combatant inclina-o a partir da base) e a espada fica
    /// presa no Hand_R. É um boneco: não ataca, por isso não tem SwordWielder.
    /// O material é o Wettable (molha onde a água toca) com as cores dos vértices e o alvo pintado no peito.
    /// </summary>
    public static class TrainingDummySetup
    {
        const string Fbx = "Assets/KonoSuba/Models/TrainingDummy/TrainingDummy.fbx";
        const string SwordFbx = "Assets/KonoSuba/Models/Swords/ProtoSwords.fbx";
        const string MatPath = "Assets/KonoSuba/Materials/TrainingDummy.mat";

        public static void Build(GameObject dummy)
        {
            if (dummy == null) return;
            // O FBX não traz materiais: a cor vem dos vértices.
            if (AssetImporter.GetAtPath(Fbx) is ModelImporter importer && importer.materialImportMode != ModelImporterMaterialImportMode.None)
            {
                importer.materialImportMode = ModelImporterMaterialImportMode.None;
                importer.SaveAndReimport();
            }
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(Fbx);
            // Com um só objeto de topo no FBX, o Unity faz dele a própria raiz do modelo.
            Transform src = model == null ? null
                : model.GetComponent<MeshFilter>() != null ? model.transform : model.transform.Find("TrainingDummy");
            if (src == null) { Debug.LogError("TrainingDummySetup: não encontrei TrainingDummy em " + Fbx); return; }
            Transform srcHand = null;
            foreach (Transform t in model.GetComponentsInChildren<Transform>(true))
                if (t.name == "Hand_R") srcHand = t;
            if (srcHand == null) Debug.LogWarning("TrainingDummySetup: o FBX não tem Hand_R; filhos: " + string.Join(", ", System.Array.ConvertAll(model.GetComponentsInChildren<Transform>(true), t => t.name)));

            // Fora: a cápsula antiga, a espada procedural e o SwordWielder.
            foreach (string child in new[] { "Body", "Sword" })
            {
                Transform t = dummy.transform.Find(child);
                if (t != null) Object.DestroyImmediate(t.gameObject);
            }
            var wielder = dummy.GetComponent<SwordWielder>();
            if (wielder != null) Object.DestroyImmediate(wielder);

            var body = new GameObject("Body").transform;
            body.SetParent(dummy.transform, false);
            body.localRotation = src == model.transform ? Quaternion.identity : src.localRotation;
            body.localScale = src == model.transform ? Vector3.one : src.localScale;
            body.gameObject.AddComponent<MeshFilter>().sharedMesh = src.GetComponent<MeshFilter>().sharedMesh;
            body.gameObject.AddComponent<MeshRenderer>().sharedMaterial = Material();

            // Mão: mesma pose do Empty do Blender; a espada encaixa nela com a lâmina para cima.
            var hand = new GameObject("Hand_R").transform;
            hand.SetParent(body, false);
            if (srcHand != null) { hand.localPosition = srcHand.localPosition; hand.localRotation = srcHand.localRotation; }
            var swordModel = AssetDatabase.LoadAssetAtPath<GameObject>(SwordFbx);
            Transform swordSrc = swordModel != null ? swordModel.transform.Find("Sword_Dummy") : null;
            if (swordSrc != null)
            {
                var sword = new GameObject("Sword").transform;
                sword.SetParent(hand, false);
                sword.localRotation = swordSrc.localRotation;
                sword.gameObject.AddComponent<MeshFilter>().sharedMesh = swordSrc.GetComponent<MeshFilter>().sharedMesh;
                var mats = swordSrc.GetComponent<MeshRenderer>().sharedMaterials;
                var own = new Material[mats.Length];
                for (int i = 0; i < mats.Length; i++)
                    own[i] = AssetDatabase.LoadAssetAtPath<Material>("Assets/KonoSuba/Materials/" +
                        (mats[i] != null && mats[i].name.Contains("Blade") ? "Sword_Blade.mat" : "Sword_Hilt.mat"));
                sword.gameObject.AddComponent<MeshRenderer>().sharedMaterials = own;
            }

            // Colisor: tronco e cabeça (os braços ficam de fora, como num boneco a sério).
            var col = dummy.GetComponent<CapsuleCollider>();
            if (col != null)
            {
                col.center = new Vector3(0f, 1.1f, 0f);
                col.height = 1.7f;
                col.radius = 0.3f;
            }
            var c = dummy.GetComponent<Combatant>();
            if (c != null) { c.visual = body; c.nameplateHeight = 2.25f; EditorUtility.SetDirty(c); }

            Bounds b = body.GetComponent<MeshFilter>().sharedMesh.bounds;
            Debug.Log($"TrainingDummySetup: malha {b.min} → {b.max}, mão {hand.localPosition}, rotação {src.localEulerAngles}, escala {src.localScale}");
        }

        static Material Material()
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(MatPath);
            if (m == null)
            {
                m = new Material(Shader.Find("KonoSuba/Wettable"));
                AssetDatabase.CreateAsset(m, MatPath);
            }
            m.SetColor("_Color", Color.white);
            m.SetFloat("_Glossiness", 0.18f);
            m.SetFloat("_VertexColor", 1f);
            m.SetVector("_RingCenter", new Vector4(0f, 1.19f, 1f, 0.1f));
            EditorUtility.SetDirty(m);
            return m;
        }
    }
}
