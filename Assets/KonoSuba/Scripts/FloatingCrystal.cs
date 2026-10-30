using UnityEngine;

namespace AnimLab.KonoSuba
{
    /// <summary>
    /// Cristal a flutuar no vazio: sobe e desce devagar, roda sobre si próprio
    /// e mantém o halo virado para a câmara com um pulsar suave.
    /// </summary>
    public class FloatingCrystal : MonoBehaviour
    {
        public float bobAmplitude = 0.18f;
        public float bobSpeed = 0.5f;
        public float spinSpeed = 22f;
        public Transform halo;
        public float haloPulse = 0.15f;

        Vector3 basePos;
        Vector3 haloScale;
        float phase;

        void Start()
        {
            basePos = transform.position;
            phase = Random.value * 100f;
            if (halo != null) haloScale = halo.localScale;
        }

        void Update()
        {
            float t = Time.time * bobSpeed + phase;
            transform.position = basePos + Vector3.up * (Mathf.Sin(t) * bobAmplitude);
            transform.Rotate(0f, spinSpeed * Time.deltaTime, 0f, Space.World);
        }

        void LateUpdate()
        {
            if (halo == null || Camera.main == null) return;
            halo.rotation = Camera.main.transform.rotation;
            float pulse = 1f + haloPulse * Mathf.Sin(Time.time * 2.3f + phase * 1.7f);
            halo.localScale = haloScale * pulse;
        }
    }
}
