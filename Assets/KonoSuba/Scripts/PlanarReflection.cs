using System.Collections.Generic;
using UnityEngine;

namespace AnimLab.KonoSuba
{
    /// <summary>
    /// Reflexo planar (espelho) para o chão de água: renderiza a cena espelhada no plano do objeto
    /// para uma RenderTexture e publica-a como textura global _ReflectionTex (chão e malha simulada usam-na).
    /// URP: a câmara do reflexo é renderizada no LateUpdate, antes do frame (dentro do render da câmara
    /// principal, como no Built-in, o URP não aceita outra câmara). Corre depois de todos os outros
    /// LateUpdate (a câmara do jogo também se move aí): senão o reflexo usava a câmara do frame anterior
    /// e escorregava pelo chão ao mexer a câmara.
    /// </summary>
    [ExecuteAlways, RequireComponent(typeof(Renderer)), DefaultExecutionOrder(10000)]
    public class PlanarReflection : MonoBehaviour
    {
        [Range(0.25f, 1f)] public float resolutionScale = 1f;
        public float clipPlaneOffset = 0.02f;
        [Tooltip("Layers que aparecem no reflexo (o próprio chão é sempre excluído).")]
        public LayerMask reflectLayers = ~0;

        static readonly int ReflectionTexId = Shader.PropertyToID("_ReflectionTex");

        readonly Dictionary<Camera, Camera> reflectionCameras = new Dictionary<Camera, Camera>();
        RenderTexture reflectionTexture;

        void OnDisable()
        {
            foreach (Camera c in reflectionCameras.Values)
                if (c != null) DestroyImmediate(c.gameObject);
            reflectionCameras.Clear();
            if (reflectionTexture != null) { reflectionTexture.Release(); DestroyImmediate(reflectionTexture); }
        }

        void LateUpdate()
        {
            Camera cam = Camera.main;
            if (cam == null) return;

            Camera rc = GetReflectionCamera(cam);
            EnsureTexture(cam);

            Vector3 pos = transform.position;
            Vector3 normal = transform.up;
            float d = -Vector3.Dot(normal, pos) - clipPlaneOffset;
            Matrix4x4 reflection = ReflectionMatrix(new Vector4(normal.x, normal.y, normal.z, d));

            rc.CopyFrom(cam);
            rc.cameraType = CameraType.Reflection;
            rc.depthTextureMode = DepthTextureMode.Depth; // soft particles do nevoeiro também no reflexo
            rc.cullingMask = reflectLayers & ~(1 << gameObject.layer);
            rc.targetTexture = reflectionTexture;
            rc.worldToCameraMatrix = cam.worldToCameraMatrix * reflection;
            rc.transform.position = reflection.MultiplyPoint(cam.transform.position);
            // Plano de recorte oblíquo: nada abaixo da água entra no reflexo.
            rc.projectionMatrix = cam.CalculateObliqueMatrix(CameraSpacePlane(rc, pos, normal));

            GL.invertCulling = true;
            rc.Render();
            GL.invertCulling = false;

            Shader.SetGlobalTexture(ReflectionTexId, reflectionTexture);
        }

        Camera GetReflectionCamera(Camera source)
        {
            if (reflectionCameras.TryGetValue(source, out Camera rc) && rc != null) return rc;
            var go = new GameObject("PlanarReflectionCamera") { hideFlags = HideFlags.HideAndDontSave };
            rc = go.AddComponent<Camera>();
            rc.enabled = false;
            reflectionCameras[source] = rc;
            return rc;
        }

        void EnsureTexture(Camera cam)
        {
            int w = Mathf.Max(16, (int)(cam.pixelWidth * resolutionScale));
            int h = Mathf.Max(16, (int)(cam.pixelHeight * resolutionScale));
            if (reflectionTexture != null && reflectionTexture.width == w && reflectionTexture.height == h) return;
            if (reflectionTexture != null) { reflectionTexture.Release(); DestroyImmediate(reflectionTexture); }
            reflectionTexture = new RenderTexture(w, h, 24, RenderTextureFormat.ARGBHalf)
            {
                name = "PlanarReflection",
                hideFlags = HideFlags.DontSave,
                antiAliasing = Mathf.Max(1, QualitySettings.antiAliasing) // linhas finas refletidas sem serrilhado
            };
        }

        Vector4 CameraSpacePlane(Camera cam, Vector3 pos, Vector3 normal)
        {
            Matrix4x4 m = cam.worldToCameraMatrix;
            Vector3 cpos = m.MultiplyPoint(pos + normal * clipPlaneOffset);
            Vector3 cnormal = m.MultiplyVector(normal).normalized;
            return new Vector4(cnormal.x, cnormal.y, cnormal.z, -Vector3.Dot(cpos, cnormal));
        }

        static Matrix4x4 ReflectionMatrix(Vector4 p)
        {
            Matrix4x4 m = Matrix4x4.identity;
            m.m00 = 1f - 2f * p.x * p.x; m.m01 = -2f * p.x * p.y;     m.m02 = -2f * p.x * p.z;     m.m03 = -2f * p.w * p.x;
            m.m10 = -2f * p.y * p.x;     m.m11 = 1f - 2f * p.y * p.y; m.m12 = -2f * p.y * p.z;     m.m13 = -2f * p.w * p.y;
            m.m20 = -2f * p.z * p.x;     m.m21 = -2f * p.z * p.y;     m.m22 = 1f - 2f * p.z * p.z; m.m23 = -2f * p.w * p.z;
            return m;
        }
    }
}
