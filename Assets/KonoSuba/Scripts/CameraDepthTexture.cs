using UnityEngine;

namespace AnimLab.KonoSuba
{
    /// <summary>Garante a depth texture da câmara, usada pelas soft particles do nevoeiro.</summary>
    [ExecuteAlways, RequireComponent(typeof(Camera))]
    public class CameraDepthTexture : MonoBehaviour
    {
        void OnEnable() => GetComponent<Camera>().depthTextureMode |= DepthTextureMode.Depth;
    }
}
