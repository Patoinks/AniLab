using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AnimLab.EditorTools
{
    /// <summary>
    /// Monta a cena de demonstração: terreno procedural, personagem articulado em primitivas,
    /// clips de animação gerados por código (Idle/Walk/Run/Jump) e o Animator Controller.
    /// Menu: AnimLab > Build Demo Scene. Em batchmode: -executeMethod AnimLab.EditorTools.AnimLabSetup.BuildBatch
    /// </summary>
    public static class AnimLabSetup
    {
        const string Root = "Assets/AnimLab";
        const string TexDir = Root + "/Textures";
        const string MatDir = Root + "/Materials";
        const string AnimDir = Root + "/Animations";
        const string TerrainDir = Root + "/Terrain";
        const string ScenePath = "Assets/Scenes/AnimLab.unity";

        const float HipsHeight = 0.95f;

        [MenuItem("AnimLab/Build Demo Scene")]
        public static void Build()
        {
            foreach (string dir in new[] { TexDir, MatDir, AnimDir, TerrainDir, "Assets/Scenes" })
                Directory.CreateDirectory(dir);

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            Terrain terrain = BuildTerrain();
            AnimatorController controller = BuildAnimator();
            GameObject player = BuildCharacter(controller);

            // Coloca o personagem no centro, em cima do terreno.
            Vector3 center = terrain.transform.position + terrain.terrainData.size * 0.5f;
            center.y = terrain.SampleHeight(center) + terrain.transform.position.y + 0.05f;
            player.transform.position = center;

            Camera cam = Camera.main;
            var follow = cam.gameObject.AddComponent<ThirdPersonCamera>();
            follow.target = player.transform;
            cam.transform.position = center + new Vector3(0f, 3f, -5f);
            player.GetComponent<PlayerController>().cameraTransform = cam.transform;

            Light sun = UnityEngine.Object.FindFirstObjectByType<Light>();
            if (sun != null)
            {
                sun.transform.rotation = Quaternion.Euler(45f, -35f, 0f);
                sun.shadows = LightShadows.Soft;
            }

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            Debug.Log("AnimLab: cena criada em " + ScenePath);
        }

        public static void BuildBatch()
        {
            try { Build(); EditorApplication.Exit(0); }
            catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }

        // ---------------------------------------------------------------- Terreno

        static Terrain BuildTerrain()
        {
            var data = new TerrainData
            {
                heightmapResolution = 257,
                alphamapResolution = 256,
                size = new Vector3(200f, 35f, 200f)
            };
            data.terrainLayers = new[]
            {
                MakeLayer("Grass", new Color(0.30f, 0.52f, 0.20f), new Color(0.20f, 0.40f, 0.12f), 4f),
                MakeLayer("Rock", new Color(0.50f, 0.48f, 0.45f), new Color(0.32f, 0.31f, 0.30f), 8f),
                MakeLayer("Sand", new Color(0.82f, 0.74f, 0.52f), new Color(0.70f, 0.62f, 0.42f), 3f),
            };
            AssetDatabase.CreateAsset(data, TerrainDir + "/AnimLabTerrain.asset");

            GameObject go = Terrain.CreateTerrainGameObject(data);
            go.name = "Terrain";
            go.transform.position = new Vector3(-100f, 0f, -100f);
            go.AddComponent<ProceduralTerrain>().Generate();
            EditorUtility.SetDirty(data);
            return go.GetComponent<Terrain>();
        }

        static TerrainLayer MakeLayer(string name, Color a, Color b, float tile)
        {
            var layer = new TerrainLayer
            {
                diffuseTexture = MakeNoiseTexture(name, a, b),
                tileSize = new Vector2(tile, tile)
            };
            AssetDatabase.CreateAsset(layer, TerrainDir + "/" + name + ".terrainlayer");
            return layer;
        }

        // Textura simples de ruído entre duas cores, guardada como PNG para poderes trocar depois.
        static Texture2D MakeNoiseTexture(string name, Color a, Color b)
        {
            const int size = 256;
            var tex = new Texture2D(size, size, TextureFormat.RGB24, false);
            var rng = new System.Random(name.GetHashCode());
            float ox = rng.Next(0, 1000), oy = rng.Next(0, 1000);
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                // Ruído tileable: amostra num toro para não haver costuras.
                float u = x / (float)size * Mathf.PI * 2f, v = y / (float)size * Mathf.PI * 2f;
                float n = Mathf.PerlinNoise(ox + Mathf.Cos(u) * 2f, oy + Mathf.Sin(v) * 2f + Mathf.Sin(u)) * 0.6f
                        + (float)rng.NextDouble() * 0.4f;
                tex.SetPixel(x, y, Color.Lerp(a, b, n));
            }
            string path = TexDir + "/" + name + ".png";
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // ---------------------------------------------------------------- Personagem

        static GameObject BuildCharacter(RuntimeAnimatorController controller)
        {
            Material body = MakeMaterial("Body", new Color(0.20f, 0.45f, 0.85f));
            Material skin = MakeMaterial("Skin", new Color(0.95f, 0.78f, 0.62f));
            Material legs = MakeMaterial("Legs", new Color(0.18f, 0.18f, 0.22f));

            var player = new GameObject("Player");
            var cc = player.AddComponent<CharacterController>();
            cc.height = 1.8f;
            cc.radius = 0.3f;
            cc.center = new Vector3(0f, 0.92f, 0f);
            var animator = player.AddComponent<Animator>();
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.runtimeAnimatorController = controller;
            player.AddComponent<PlayerController>().animator = animator;

            // Hierarquia de "ossos": cada pivot é um Transform vazio que as animações rodam,
            // e as primitivas são filhas desses pivots apenas para visualização.
            Transform hips = Pivot("Hips", player.transform, new Vector3(0f, HipsHeight, 0f));
            Part(PrimitiveType.Cube, hips, new Vector3(0f, 0f, 0f), new Vector3(0.36f, 0.18f, 0.2f), legs);

            Transform spine = Pivot("Spine", hips, new Vector3(0f, 0.08f, 0f));
            Part(PrimitiveType.Cube, spine, new Vector3(0f, 0.25f, 0f), new Vector3(0.42f, 0.48f, 0.22f), body);

            Transform head = Pivot("Head", spine, new Vector3(0f, 0.52f, 0f));
            Part(PrimitiveType.Sphere, head, new Vector3(0f, 0.15f, 0f), Vector3.one * 0.26f, skin);
            Part(PrimitiveType.Cube, head, new Vector3(0f, 0.17f, 0.12f), new Vector3(0.14f, 0.04f, 0.04f), legs); // "olhos" para ver a frente

            foreach (int side in new[] { -1, 1 })
            {
                string s = side < 0 ? "L" : "R";

                Transform shoulder = Pivot("Shoulder" + s, spine, new Vector3(0.27f * side, 0.46f, 0f));
                Part(PrimitiveType.Cube, shoulder, new Vector3(0f, -0.15f, 0f), new Vector3(0.11f, 0.3f, 0.11f), body);
                Transform elbow = Pivot("Elbow" + s, shoulder, new Vector3(0f, -0.3f, 0f));
                Part(PrimitiveType.Cube, elbow, new Vector3(0f, -0.13f, 0f), new Vector3(0.09f, 0.26f, 0.09f), skin);

                Transform hip = Pivot("Hip" + s, hips, new Vector3(0.11f * side, -0.05f, 0f));
                Part(PrimitiveType.Cube, hip, new Vector3(0f, -0.21f, 0f), new Vector3(0.14f, 0.42f, 0.14f), legs);
                Transform knee = Pivot("Knee" + s, hip, new Vector3(0f, -0.43f, 0f));
                Part(PrimitiveType.Cube, knee, new Vector3(0f, -0.21f, 0f), new Vector3(0.12f, 0.42f, 0.12f), legs);
                Part(PrimitiveType.Cube, knee, new Vector3(0f, -0.44f, 0.05f), new Vector3(0.13f, 0.06f, 0.24f), body);
            }

            PrefabUtility.SaveAsPrefabAssetAndConnect(player, Root + "/Player.prefab", InteractionMode.AutomatedAction);
            return player;
        }

        static Transform Pivot(string name, Transform parent, Vector3 localPos)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = localPos;
            return t;
        }

        static void Part(PrimitiveType type, Transform parent, Vector3 localPos, Vector3 scale, Material mat)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
            go.name = parent.name + "_Mesh";
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = scale;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        }

        static Material MakeMaterial(string name, Color color)
        {
            var mat = new Material(Shader.Find("Standard")) { color = color };
            mat.SetFloat("_Glossiness", 0.2f);
            AssetDatabase.CreateAsset(mat, MatDir + "/" + name + ".mat");
            return mat;
        }

        // ---------------------------------------------------------------- Animações
        //
        // Convenção de rotações (graus, eixo X local):
        //   Hip    negativo = perna para a frente     Knee  positivo = dobra o joelho
        //   Shoulder negativo = braço para a frente   Elbow negativo = dobra o cotovelo
        //   Spine  positivo = inclina para a frente
        // Shoulder Z: negativo afasta o braço esquerdo do corpo, positivo o direito.

        static AnimatorController BuildAnimator()
        {
            AnimationClip idle = SaveClip("Idle", 3f, true, IdlePose);
            AnimationClip walk = SaveClip("Walk", 1f, true, (p, t) => Gait(p, t, 25f, 5f, 40f, 20f, -15f, 0f, 0.025f));
            AnimationClip run = SaveClip("Run", 0.65f, true, (p, t) => Gait(p, t, 45f, 10f, 85f, 45f, -75f, 10f, 0.05f));
            AnimationClip jump = SaveClip("Jump", 0.8f, true, JumpPose);

            string path = AnimDir + "/Player.controller";
            AnimatorController ac = AnimatorController.CreateAnimatorControllerAtPath(path);
            ac.AddParameter("Speed", AnimatorControllerParameterType.Float);
            ac.AddParameter("Grounded", AnimatorControllerParameterType.Bool);
            AnimatorControllerParameter[] parameters = ac.parameters; // devolve cópia: alterar e reatribuir
            parameters[1].defaultBool = true;
            ac.parameters = parameters;

            AnimatorStateMachine sm = ac.layers[0].stateMachine;
            AnimatorState locomotion = ac.CreateBlendTreeInController("Locomotion", out BlendTree tree, 0);
            tree.blendParameter = "Speed";
            tree.useAutomaticThresholds = false;
            tree.AddChild(idle, 0f);
            tree.AddChild(walk, 0.5f);
            tree.AddChild(run, 1f);

            AnimatorState air = sm.AddState("Airborne");
            air.motion = jump;
            sm.defaultState = locomotion;

            AnimatorStateTransition toAir = locomotion.AddTransition(air);
            toAir.hasExitTime = false;
            toAir.duration = 0.1f;
            toAir.AddCondition(AnimatorConditionMode.IfNot, 0f, "Grounded");

            AnimatorStateTransition toGround = air.AddTransition(locomotion);
            toGround.hasExitTime = false;
            toGround.duration = 0.15f;
            toGround.AddCondition(AnimatorConditionMode.If, 0f, "Grounded");

            EditorUtility.SetDirty(ac);
            return ac;
        }

        /// <summary>Uma pose é uma função (fase 0..1) que escreve rotações/posições num dicionário.</summary>
        delegate void PoseFn(Pose pose, float phase);

        class Pose
        {
            public readonly Dictionary<string, Vector3> Rot = new Dictionary<string, Vector3>();
            public float HipsY = HipsHeight;
            public void Set(string bone, float x, float y = 0f, float z = 0f) => Rot[bone] = new Vector3(x, y, z);
        }

        static readonly Dictionary<string, string> BonePaths = new Dictionary<string, string>
        {
            { "Hips", "Hips" },
            { "Spine", "Hips/Spine" },
            { "Head", "Hips/Spine/Head" },
            { "ShoulderL", "Hips/Spine/ShoulderL" }, { "ElbowL", "Hips/Spine/ShoulderL/ElbowL" },
            { "ShoulderR", "Hips/Spine/ShoulderR" }, { "ElbowR", "Hips/Spine/ShoulderR/ElbowR" },
            { "HipL", "Hips/HipL" }, { "KneeL", "Hips/HipL/KneeL" },
            { "HipR", "Hips/HipR" }, { "KneeR", "Hips/HipR/KneeR" },
        };

        static void IdlePose(Pose p, float t)
        {
            float b = Mathf.Sin(t * Mathf.PI * 2f);
            p.Set("Spine", 2f * b);
            p.Set("Head", -1.5f * b);
            p.Set("ShoulderL", 0f, 0f, -4f - 1.5f * b);
            p.Set("ShoulderR", 0f, 0f, 4f + 1.5f * b);
            p.Set("ElbowL", -8f);
            p.Set("ElbowR", -8f);
            p.HipsY = HipsHeight + 0.006f * b;
        }

        // Ciclo de marcha parametrizado: walk e run usam a mesma lógica com amplitudes diferentes.
        static void Gait(Pose p, float t, float hipAmp, float kneeBase, float kneeAmp,
                         float armAmp, float elbow, float lean, float bob)
        {
            float phi = t * Mathf.PI * 2f;
            float hipL = hipAmp * Mathf.Sin(phi);
            // O joelho dobra quando a perna está a avançar (derivada da anca negativa).
            float kneeL = kneeBase + kneeAmp * Mathf.Max(0f, -Mathf.Cos(phi));
            float kneeR = kneeBase + kneeAmp * Mathf.Max(0f, Mathf.Cos(phi));

            p.Set("HipL", hipL);
            p.Set("HipR", -hipL);
            p.Set("KneeL", kneeL);
            p.Set("KneeR", kneeR);
            // Braços opostos às pernas.
            p.Set("ShoulderL", -armAmp * Mathf.Sin(phi), 0f, -4f);
            p.Set("ShoulderR", armAmp * Mathf.Sin(phi), 0f, 4f);
            p.Set("ElbowL", elbow);
            p.Set("ElbowR", elbow);
            p.Set("Spine", lean, 6f * Mathf.Sin(phi));
            p.Set("Head", -lean * 0.5f, -6f * Mathf.Sin(phi));
            // Sobe quando as pernas passam pela vertical (2x por ciclo).
            p.HipsY = HipsHeight - bob + bob * Mathf.Cos(2f * phi);
        }

        static void JumpPose(Pose p, float t)
        {
            float w = Mathf.Sin(t * Mathf.PI * 2f);
            p.Set("HipL", -45f);
            p.Set("KneeL", 75f);
            p.Set("HipR", 15f);
            p.Set("KneeR", 35f);
            p.Set("ShoulderL", -20f, 0f, -70f + 5f * w);
            p.Set("ShoulderR", -20f, 0f, 70f - 5f * w);
            p.Set("ElbowL", -20f);
            p.Set("ElbowR", -20f);
            p.Set("Spine", 8f);
        }

        static AnimationClip SaveClip(string name, float length, bool loop, PoseFn fn)
        {
            const int samples = 24;
            var curves = new Dictionary<EditorCurveBinding, AnimationCurve>();

            for (int i = 0; i <= samples; i++)
            {
                float phase = i / (float)samples;
                var pose = new Pose();
                // Todos os ossos começam a zero para cada clip definir a pose completa.
                foreach (string bone in BonePaths.Keys) pose.Set(bone, 0f);
                fn(pose, phase);

                float time = phase * length;
                foreach (var kv in pose.Rot)
                {
                    string path = BonePaths[kv.Key];
                    AddKey(curves, path, "localEulerAnglesRaw.x", time, kv.Value.x);
                    AddKey(curves, path, "localEulerAnglesRaw.y", time, kv.Value.y);
                    AddKey(curves, path, "localEulerAnglesRaw.z", time, kv.Value.z);
                }
                AddKey(curves, "Hips", "m_LocalPosition.x", time, 0f);
                AddKey(curves, "Hips", "m_LocalPosition.y", time, pose.HipsY);
                AddKey(curves, "Hips", "m_LocalPosition.z", time, 0f);
            }

            var clip = new AnimationClip { name = name, frameRate = 30f };
            foreach (var kv in curves)
            {
                for (int k = 0; k < kv.Value.length; k++) kv.Value.SmoothTangents(k, 0f);
                AnimationUtility.SetEditorCurve(clip, kv.Key, kv.Value);
            }
            AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = loop;
            AnimationUtility.SetAnimationClipSettings(clip, settings);

            AssetDatabase.CreateAsset(clip, AnimDir + "/" + name + ".anim");
            return clip;
        }

        static void AddKey(Dictionary<EditorCurveBinding, AnimationCurve> curves, string path, string prop, float time, float value)
        {
            EditorCurveBinding binding = EditorCurveBinding.FloatCurve(path, typeof(Transform), prop);
            if (!curves.TryGetValue(binding, out AnimationCurve curve))
                curves[binding] = curve = new AnimationCurve();
            curve.AddKey(time, value);
        }
    }
}
