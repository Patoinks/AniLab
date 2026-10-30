using UnityEditor;
using UnityEngine;

namespace AnimLab.KonoSuba.EditorTools
{
    /// <summary>
    /// Importação do kit de gelo vindo do Blender (VFX/Ice/*.fbx): só malhas, normais planas do ficheiro e as cores
    /// por vértice (baricêntricas das arestas); sem materiais, animação nem UVs de lightmap.
    /// </summary>
    public class IceKitImporter : AssetPostprocessor
    {
        void OnPreprocessModel()
        {
            if (!assetPath.StartsWith("Assets/KonoSuba/VFX/Ice/")) return;
            var m = (ModelImporter)assetImporter;
            m.materialImportMode = ModelImporterMaterialImportMode.None;
            m.importAnimation = false;
            m.animationType = ModelImporterAnimationType.None;
            m.importNormals = ModelImporterNormals.Import;
            m.importTangents = ModelImporterTangents.None;
            m.meshCompression = ModelImporterMeshCompression.Off;
            m.weldVertices = false; // os cantos de cada triângulo têm cores diferentes
            m.generateSecondaryUV = false;
            m.importCameras = false;
            m.importLights = false;
        }

        /// <summary>Lista as malhas do kit (batchmode: -executeMethod …IceKitImporter.Report -quit).</summary>
        public static void Report()
        {
            foreach (Object o in AssetDatabase.LoadAllAssetsAtPath("Assets/KonoSuba/VFX/Ice/IceKit.fbx"))
                if (o is Mesh mesh)
                    Debug.Log($"IceKit: {mesh.name} tris={mesh.triangles.Length / 3} cores={mesh.colors32.Length > 0} bounds={mesh.bounds}");
        }
    }
}
