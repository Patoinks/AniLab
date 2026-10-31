using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace AnimLab.KonoSuba.EditorTools
{
    /// <summary>
    /// Espadas protótipo (Tools/Blender/swords_proto.py → ProtoSwords.fbx): uma na mão do Player e outra
    /// no boneco de treino, com o SwordWielder e os materiais (aço e punho).
    /// Também o duelista (DuelistAI): uma cápsula com pés como o Player, espada longa e AI de duelo.
    /// Chamado por KonoSubaSceneSetup.Build(); Install() aplica-o à cena guardada sem a reconstruir.
    /// </summary>
    public static class SwordSetup
    {
        const string Fbx = "Assets/KonoSuba/Models/Swords/ProtoSwords.fbx";
        const string MatDir = "Assets/KonoSuba/Materials";
        const string ScenePath = "Assets/Scenes/KonoSubaVoid.unity";

        [MenuItem("AnimLab/Instalar espadas, ficha, boneco e duelista")]
        public static void Install()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath);
            Build();
            EditorSceneManager.SaveScene(scene);
        }

        public static void Build()
        {
            GameObject player = GameObject.Find("Player");
            GameObject dummy = GameObject.Find("DummyEnemy");
            if (player != null)
            {
                var w = Equip(player, "Sword_Player");
                w.playerControlled = true;
                w.bladeTip = 0.9f;
                // Armas dos outros estilos (Weapon Arts): escondidas até o estilo as usar.
                w.scythe = Extra(player, "Scythe");
                w.greatsword = Extra(player, "Greatsword");
                w.dagger = Extra(player, "Dagger", "Dagger_R");
                w.offhand = Extra(player, "Dagger", "Dagger_L");
                w.daggerHitFrom = new Vector3(0f, 0.06f, 0f);
                w.daggerHitTo = new Vector3(0f, 0.34f, 0f);
                w.greatswordHitFrom = new Vector3(0f, 0.15f, 0f);
                w.greatswordHitTo = new Vector3(0f, 1.43f, 0f);
                // (o segmento de corte vai explícito: os valores por omissão não chegam a um componente que já existe)
                w.scytheHitFrom = new Vector3(0f, 0.55f, 0f);
                w.scytheHitTo = new Vector3(0.76f, 0.97f, 0f);
                EditorUtility.SetDirty(w);
                var body = player.GetComponent<Combatant>();
                if (body == null) body = player.AddComponent<Combatant>();
                body.showNameplate = false;
                body.maxHealth = body.health = 100f;
                EditorUtility.SetDirty(body);
                // Ficha do personagem (stats, tecla C): escreve vida, mana, velocidades e dano nos outros componentes.
                if (player.GetComponent<CharacterStats>() == null) player.AddComponent<CharacterStats>();
                // Círculo de seleção para todos os alvos (o do boneco serve de molde).
                var lockOn = player.GetComponent<LockOn>();
                Transform ring = dummy != null ? dummy.transform.Find("TargetRing") : null;
                if (lockOn != null && ring != null)
                {
                    bool was = ring.gameObject.activeSelf;
                    ring.gameObject.SetActive(true);
                    lockOn.markerPrefab = PrefabUtility.SaveAsPrefabAsset(ring.gameObject, "Assets/KonoSuba/TargetRing.prefab");
                    ring.gameObject.SetActive(was);
                    EditorUtility.SetDirty(lockOn);
                }
            }
            // O boneco de treino (modelo do Astra) segura a espada na mão, sem a usar.
            TrainingDummySetup.Build(dummy);
            BuildDuelist();
        }

        static void BuildDuelist()
        {
            GameObject old = GameObject.Find("Duelist");
            if (old != null) Object.DestroyImmediate(old);

            Material bodyMat = Get("DuelistBody", "Universal Render Pipeline/Lit", m =>
            {
                m.SetColor("_BaseColor", new Color(0.26f, 0.3f, 0.38f));
                m.SetFloat("_Smoothness", 0.4f);
            });
            Material footMat = Get("DuelistFeet", "Universal Render Pipeline/Lit", m =>
            {
                m.SetColor("_BaseColor", new Color(0.12f, 0.12f, 0.14f));
                m.SetFloat("_Smoothness", 0.2f);
            });

            var go = new GameObject("Duelist");
            go.transform.position = new Vector3(-2.5f, 0f, 7.5f);
            go.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
            var cc = go.AddComponent<CharacterController>();
            cc.height = 1.6f;
            cc.radius = 0.35f;
            cc.center = new Vector3(0f, 0.85f, 0f);
            cc.skinWidth = 0.03f;

            // Mesmas medidas do Player: o duelista é um "jogador" do outro lado.
            Transform body = Part(PrimitiveType.Capsule, "Body", go.transform, new Vector3(0f, 0.95f, 0f), new Vector3(0.6f, 0.65f, 0.6f), bodyMat);
            Part(PrimitiveType.Cube, "Facing", body, new Vector3(0f, 0.35f, 0.45f), new Vector3(0.35f, 0.12f, 0.2f), footMat);
            Transform left = Part(PrimitiveType.Sphere, "LeftFoot", go.transform, new Vector3(-0.17f, 0.04f, 0f), new Vector3(0.17f, 0.08f, 0.26f), footMat);
            Transform right = Part(PrimitiveType.Sphere, "RightFoot", go.transform, new Vector3(0.17f, 0.04f, 0f), new Vector3(0.17f, 0.08f, 0.26f), footMat);

            var target = go.AddComponent<LockOnTarget>();
            target.displayName = "Swordsman";
            var c = go.AddComponent<Combatant>();
            c.maxHealth = c.health = 300f;
            c.refill = false; // a AI trata da derrota e volta com a vida cheia
            c.nameplateHeight = 2.1f;

            var mover = go.AddComponent<CapsulePlayer>();
            mover.body = body;
            mover.leftFoot = left;
            mover.rightFoot = right;
            mover.aiControlled = true;
            mover.walkSpeed = 3.2f;
            mover.runSpeed = 5.8f;

            SwordWielder w = Equip(go, "Sword_Player");
            w.playerControlled = false;
            w.bladeTip = 0.9f;
            w.sparkColor = new Color(1f, 0.7f, 0.5f);
            go.AddComponent<DuelistAI>();
        }

        static Transform Part(PrimitiveType type, string name, Transform parent, Vector3 pos, Vector3 scale, Material mat)
        {
            GameObject p = GameObject.CreatePrimitive(type);
            Object.DestroyImmediate(p.GetComponent<Collider>());
            p.name = name;
            p.transform.SetParent(parent, false);
            p.transform.localPosition = pos;
            p.transform.localScale = scale;
            p.GetComponent<MeshRenderer>().sharedMaterial = mat;
            return p.transform;
        }

        static SwordWielder Equip(GameObject owner, string modelName)
        {
            Transform old = owner.transform.Find("Sword");
            if (old != null) Object.DestroyImmediate(old.gameObject);

            var model = AssetDatabase.LoadAssetAtPath<GameObject>(Fbx);
            Transform src = model != null ? model.transform.Find(modelName) : null;
            if (src == null) { Debug.LogError("SwordSetup: não encontrei " + modelName + " em " + Fbx); return null; }

            // "Sword" é o pivô (centro do punho) que o SwordWielder move; o modelo vai num filho com a
            // rotação/escala que o importador do FBX lhe der.
            var sword = new GameObject("Sword").transform;
            sword.SetParent(owner.transform, false);
            var mesh = new GameObject("Model").transform;
            mesh.SetParent(sword, false);
            mesh.localRotation = src.localRotation;
            mesh.localScale = src.localScale;
            mesh.gameObject.AddComponent<MeshFilter>().sharedMesh = src.GetComponent<MeshFilter>().sharedMesh;
            var r = mesh.gameObject.AddComponent<MeshRenderer>();
            Material[] mats = src.GetComponent<MeshRenderer>().sharedMaterials;
            var own = new Material[mats.Length];
            for (int i = 0; i < mats.Length; i++)
                own[i] = mats[i] != null && mats[i].name.Contains("Blade") ? Blade() : Hilt();
            r.sharedMaterials = own;

            var w = owner.GetComponent<SwordWielder>();
            if (w == null) w = owner.AddComponent<SwordWielder>();
            w.sword = sword;
            w.bladeStart = 0.12f;
            EditorUtility.SetDirty(w);
            Bounds b = mesh.GetComponent<MeshFilter>().sharedMesh.bounds;
            Debug.Log($"SwordSetup: {modelName} em {owner.name} (malha {b.size}, rotação {src.localEulerAngles}, escala {src.localScale})");
            return w;
        }

        const string WeaponsFbx = "Assets/KonoSuba/Models/Weapons/Weapons.fbx";

        static Transform Extra(GameObject owner, string modelName, string childName = null)
        {
            childName ??= modelName;
            Transform old = owner.transform.Find(childName);
            if (old != null) Object.DestroyImmediate(old.gameObject);
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(WeaponsFbx);
            Transform src = model != null ? model.transform.Find(modelName) : null;
            if (src == null) { Debug.LogError("SwordSetup: não encontrei " + modelName + " em " + WeaponsFbx); return null; }
            var pivot = new GameObject(childName).transform;
            pivot.SetParent(owner.transform, false);
            var mesh = new GameObject("Model").transform;
            mesh.SetParent(pivot, false);
            mesh.localRotation = src.localRotation;
            mesh.localScale = src.localScale;
            mesh.gameObject.AddComponent<MeshFilter>().sharedMesh = src.GetComponent<MeshFilter>().sharedMesh;
            Material[] mats = src.GetComponent<MeshRenderer>().sharedMaterials;
            var own = new Material[mats.Length];
            for (int i = 0; i < mats.Length; i++) own[i] = mats[i] != null && mats[i].name.Contains("Blade") ? Blade() : Hilt();
            mesh.gameObject.AddComponent<MeshRenderer>().sharedMaterials = own;
            pivot.gameObject.SetActive(false);
            Bounds b = mesh.GetComponent<MeshFilter>().sharedMesh.bounds;
            Debug.Log($"SwordSetup: {modelName} em {owner.name} (malha {b.min} → {b.max})");
            return pivot;
        }

        static Material Blade() => Get("Sword_Blade", "Universal Render Pipeline/Lit", m =>
        {
            m.SetColor("_BaseColor", new Color(0.8f, 0.83f, 0.88f));
            m.SetFloat("_Metallic", 0.85f);
            m.SetFloat("_Smoothness", 0.78f);
        });

        static Material Hilt() => Get("Sword_Hilt", "Universal Render Pipeline/Lit", m =>
        {
            m.SetColor("_BaseColor", new Color(0.13f, 0.11f, 0.1f));
            m.SetFloat("_Metallic", 0.2f);
            m.SetFloat("_Smoothness", 0.35f);
        });

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
