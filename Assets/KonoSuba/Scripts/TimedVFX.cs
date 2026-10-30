using UnityEngine;

namespace AnimLab.KonoSuba
{
    /// <summary>
    /// Vida de um VFX invocado: cresce do chão, fica o tempo pedido e encolhe até desaparecer (o tornado de vento
    /// também desvanece pelo SetFade). Destrói-se no fim.
    /// </summary>
    public class TimedVFX : MonoBehaviour
    {
        public float duration = 7f, growIn = 0.5f, shrinkOut = 0.7f;
        Vector3 baseScale;
        WindTornado wind;
        float age;

        void Start()
        {
            baseScale = transform.localScale;
            wind = GetComponent<WindTornado>();
            Apply();
        }

        void Update()
        {
            age += Time.deltaTime;
            if (age >= duration) { Destroy(gameObject); return; }
            Apply();
        }

        void Apply()
        {
            float grow = Mathf.SmoothStep(0f, 1f, age / Mathf.Max(growIn, 0.01f));
            float shrink = Mathf.SmoothStep(0f, 1f, (duration - age) / Mathf.Max(shrinkOut, 0.01f));
            float k = Mathf.Min(grow, shrink);
            transform.localScale = new Vector3(baseScale.x * Mathf.Lerp(0.3f, 1f, k), baseScale.y * Mathf.Max(k, 0.01f), baseScale.z * Mathf.Lerp(0.3f, 1f, k));
            if (wind != null) wind.SetFade(k);
        }
    }
}
