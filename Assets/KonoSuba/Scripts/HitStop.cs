using UnityEngine;

namespace AnimLab.KonoSuba
{
    /// <summary>
    /// Hit-stop: o jogo quase para durante uns frames no impacto, para o golpe ter peso.
    /// Vários pedidos seguidos não se somam: fica o mais longo e o mais lento. O tempo conta em tempo real.
    /// Then(): câmara lenta que começa quando a paragem acaba (o parry: para, e depois arrasta-se um pouco).
    /// </summary>
    public class HitStop : MonoBehaviour
    {
        static HitStop instance;
        float remaining, scale = 1f;
        float slowRemaining, slowScale = 1f;

        /// <param name="seconds">Duração em tempo real.</param>
        /// <param name="timeScale">Velocidade do jogo durante a paragem (0.02 ≈ parado).</param>
        public static void Do(float seconds, float timeScale = 0.02f)
        {
            if (instance == null)
            {
                var go = new GameObject("HitStop") { hideFlags = HideFlags.HideInHierarchy };
                instance = go.AddComponent<HitStop>();
            }
            instance.scale = instance.remaining > 0f ? Mathf.Min(instance.scale, timeScale) : timeScale;
            instance.remaining = Mathf.Max(instance.remaining, seconds);
            Time.timeScale = instance.scale;
        }

        /// <summary>Câmara lenta depois da paragem atual (ou já, se não houver paragem).</summary>
        public static void Then(float seconds, float timeScale)
        {
            if (instance == null) Do(0f, 1f);
            instance.slowRemaining = Mathf.Max(instance.slowRemaining, seconds);
            instance.slowScale = Mathf.Min(instance.slowScale, timeScale);
            if (instance.remaining <= 0f) Time.timeScale = instance.slowScale;
        }

        void Update()
        {
            if (remaining <= 0f && slowRemaining <= 0f) return;
            // Com Time.captureFramerate (gravações) o tempo real não avança: usa o passo fixo da gravação.
            float dt = Time.captureFramerate > 0 ? 1f / Time.captureFramerate : Time.unscaledDeltaTime;
            if (remaining > 0f)
            {
                remaining -= dt;
                if (remaining <= 0f) { remaining = 0f; scale = 1f; Time.timeScale = slowRemaining > 0f ? slowScale : 1f; }
                return;
            }
            slowRemaining -= dt;
            // A câmara lenta volta ao normal suavemente nos últimos 0.15 s.
            Time.timeScale = Mathf.Lerp(1f, slowScale, Mathf.Clamp01(slowRemaining / 0.15f));
            if (slowRemaining <= 0f) { slowRemaining = 0f; slowScale = 1f; Time.timeScale = 1f; }
        }

        void OnDestroy()
        {
            if (instance == this) { Time.timeScale = 1f; instance = null; }
        }
    }
}
