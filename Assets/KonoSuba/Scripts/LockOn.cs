using UnityEngine;

namespace AnimLab.KonoSuba
{
    /// <summary>
    /// Alvo selecionado do player (estilo Metin2: escolhe-se clicando no inimigo, ver Metin2Camera).
    /// O alvo é largado se ficar demasiado longe ou desaparecer.
    /// </summary>
    public class LockOn : MonoBehaviour
    {
        public float breakDistance = 35f;

        public LockOnTarget Current { get; private set; }

        public void Select(LockOnTarget t)
        {
            if (Current == t) return;
            Release();
            Current = t;
            if (Current != null) Current.SetLocked(true);
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
