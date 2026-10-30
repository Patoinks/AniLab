using UnityEngine;

namespace AnimLab
{
    /// <summary>
    /// Câmara orbital em terceira pessoa.
    /// Clique esquerdo bloqueia o cursor, Esc liberta. Rato roda, scroll faz zoom.
    /// </summary>
    public class ThirdPersonCamera : MonoBehaviour
    {
        public Transform target;
        public Vector3 targetOffset = new Vector3(0f, 1.5f, 0f);
        public float distance = 5f;
        public float minDistance = 1.5f;
        public float maxDistance = 12f;
        public float sensitivity = 3f;
        public float minPitch = -30f;
        public float maxPitch = 70f;
        public float collisionRadius = 0.25f;
        public LayerMask collisionMask = ~0;

        float yaw;
        float pitch = 15f;

        void Start()
        {
            if (target != null) yaw = target.eulerAngles.y;
        }

        void LateUpdate()
        {
            if (target == null) return;

            if (Input.GetMouseButtonDown(0)) Cursor.lockState = CursorLockMode.Locked;
            if (Input.GetKeyDown(KeyCode.Escape)) Cursor.lockState = CursorLockMode.None;

            if (Cursor.lockState == CursorLockMode.Locked)
            {
                yaw += Input.GetAxis("Mouse X") * sensitivity;
                pitch -= Input.GetAxis("Mouse Y") * sensitivity;
                pitch = Mathf.Clamp(pitch, minPitch, maxPitch);
            }
            distance = Mathf.Clamp(distance - Input.mouseScrollDelta.y * 0.5f, minDistance, maxDistance);

            Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
            Vector3 pivot = target.position + targetOffset;
            Vector3 dir = rot * Vector3.back;

            // Evita que a câmara atravesse o terreno, ignorando o próprio personagem.
            float d = distance;
            foreach (RaycastHit hit in Physics.SphereCastAll(pivot, collisionRadius, dir, distance, collisionMask, QueryTriggerInteraction.Ignore))
            {
                if (hit.transform.IsChildOf(target)) continue;
                d = Mathf.Min(d, hit.distance);
            }

            transform.SetPositionAndRotation(pivot + dir * d, rot);
        }
    }
}
