using UnityEngine;

namespace AnimLab.KonoSuba
{
    /// <summary>
    /// Alvo selecionado do player (estilo Metin2: escolhe-se clicando no inimigo, ver Metin2Camera).
    /// O alvo é largado se ficar demasiado longe ou desaparecer.
    /// Alvos sem círculo próprio recebem uma cópia de markerPrefab (o círculo vermelho do chão) ou,
    /// sem molde, do círculo de outro alvo.
    /// </summary>
    public class LockOn : MonoBehaviour
    {
        public float breakDistance = 35f;
        [Tooltip("Círculo de seleção para alvos que não têm o seu (ex.: o duelista).")]
        public GameObject markerPrefab;

        public LockOnTarget Current { get; private set; }

        public void Select(LockOnTarget t)
        {
            if (Current == t) return;
            Release();
            Current = t;
            if (Current == null) return;
            if (Current.lockMarker == null)
            {
                // Sem molde: copia o círculo de outro alvo que tenha um (o boneco de treino).
                GameObject template = markerPrefab;
                if (template == null)
                    foreach (LockOnTarget other in LockOnTarget.All)
                        if (other.lockMarker != null) { template = other.lockMarker; break; }
                if (template != null) Current.CreateMarker(template);
            }
            Current.SetLocked(true);
        }

        public void Release()
        {
            if (Current != null) Current.SetLocked(false);
            Current = null;
        }

        void Update()
        {
            if (Current != null && (!Current.isActiveAndEnabled ||
                Vector3.Distance(transform.position, Current.transform.position) > breakDistance))
                Release();
        }
    }
}
