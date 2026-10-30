using UnityEngine;

namespace AnimLab.KonoSuba
{
    /// <summary>Movimento das peças do laboratório de VFX: rotação e, opcionalmente, a erosão a pulsar (via MaterialPropertyBlock).</summary>
    public class VFXLabMotion : MonoBehaviour
    {
        public Vector3 spin;
        public bool erosionPulse;
        MaterialPropertyBlock block;
        Renderer r;

        void Start() => r = GetComponent<Renderer>();

        void Update()
        {
            transform.Rotate(spin * Time.deltaTime, Space.Self);
            if (!erosionPulse || r == null) return;
            block ??= new MaterialPropertyBlock();
            block.SetFloat("_Erosion", 0.15f + 0.6f * (0.5f + 0.5f * Mathf.Sin(Time.time * 1.2f)));
            r.SetPropertyBlock(block);
        }
    }
}
