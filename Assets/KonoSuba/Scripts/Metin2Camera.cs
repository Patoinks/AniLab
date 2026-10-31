using UnityEngine;

namespace AnimLab.KonoSuba
{
    /// <summary>
    /// Câmara e controlos ao estilo Metin2. A câmara só se mexe com o rato e nunca roda sozinha.
    ///   Botão direito arrastado — rodar e inclinar        Scroll — zoom
    ///   Direito no inimigo      — selecionar como alvo (círculo vermelho no chão)
    ///   WASD                    — andar relativo à câmara
    ///   Esc                     — largar o alvo
    ///   Botão esquerdo          — nada, por agora
    /// </summary>
    public class Metin2Camera : MonoBehaviour
    {
        /// <summary>O que o player deve fazer neste frame (lido por CapsulePlayer).</summary>
        public struct MoveCommand
        {
            public Vector3 wish;      // direção e intensidade (0..1) do movimento, no plano
            public bool setFacing;    // se a câmara decide para onde o boneco olha
            public float facingYaw;
        }

        public Transform target;
        public float targetHeight = 1.1f;
        public bool showHud = true;

        [Header("Órbita")]
        public float yaw = 0f;
        public float pitch = 32f;
        public float minPitch = 8f;
        public float maxPitch = 75f;
        public float mouseSensitivity = 3f;

        [Header("Zoom")]
        public float distance = 9f;
        public float minDistance = 4f;
        public float maxDistance = 18f;
        public float zoomStep = 0.12f;

        LockOn lockOn;
        Camera cam;
        float smoothDistance;
        Vector3 focus;
        LockOnTarget hovered;      // inimigo debaixo do cursor

        void Start()
        {
            cam = GetComponent<Camera>();
            smoothDistance = distance;
            if (target == null) return;
            lockOn = target.GetComponent<LockOn>();
            focus = target.position;
            yaw = target.eulerAngles.y;
        }

        // ------------------------------------------------------------------ Controlos do player

        // O lock é de alvo, não de câmara: o botão direito em cima de um inimigo seleciona-o (sem andar até ele).
        // O botão esquerdo não faz nada por agora. Anda-se só com WASD; Esc larga o alvo.
        public MoveCommand ReadMovement(Transform player)
        {
            var cmd = new MoveCommand();
            hovered = HoveredTarget();

            if (lockOn != null)
            {
                if (Input.GetMouseButtonDown(1) && hovered != null) lockOn.Select(hovered);
                if (GameInput.GetDown(GameAction.ReleaseTarget)) lockOn.Release();
            }

            Vector2 axis = GameInput.MoveAxis();
            if (axis.sqrMagnitude > 0.01f)
                cmd.wish = Vector3.ClampMagnitude(Quaternion.Euler(0f, yaw, 0f) * new Vector3(axis.x, 0f, axis.y), 1f);
            return cmd;
        }

        LockOnTarget HoveredTarget()
        {
            if (cam == null) return null;
            Ray ray = cam.ScreenPointToRay(Input.mousePosition);
            return Physics.Raycast(ray, out RaycastHit hit, 300f) ? hit.collider.GetComponentInParent<LockOnTarget>() : null;
        }

        // ------------------------------------------------------------------ Câmara

        void LateUpdate()
        {
            if (target == null) return;
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);

            if (Input.GetMouseButton(1))
            {
                yaw += Input.GetAxis("Mouse X") * mouseSensitivity;
                pitch = Mathf.Clamp(pitch - Input.GetAxis("Mouse Y") * mouseSensitivity, minPitch, maxPitch);
            }
            if (Mathf.Abs(Input.mouseScrollDelta.y) > 0.01f)
                distance = Mathf.Clamp(distance * (1f - Input.mouseScrollDelta.y * zoomStep), minDistance, maxDistance);

            smoothDistance = Mathf.Lerp(smoothDistance, distance, 1f - Mathf.Exp(-10f * dt));
            focus = Vector3.Lerp(focus, target.position, 1f - Mathf.Exp(-14f * dt));

            Vector3 pivot = focus + Vector3.up * targetHeight;
            Vector3 pos = pivot - Quaternion.Euler(pitch, yaw, 0f) * Vector3.forward * smoothDistance;
            pos.y = Mathf.Max(pos.y, 0.25f); // nunca atravessa a água
            transform.SetPositionAndRotation(pos, Quaternion.LookRotation(pivot - pos, Vector3.up));
        }

        // ------------------------------------------------------------------ HUD

        void OnGUI()
        {
            if (!showHud) return;

            // Moldura do alvo selecionado (topo do ecrã) e nome do inimigo debaixo do cursor.
            LockOnTarget current = lockOn != null ? lockOn.Current : null;
            var center = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 13, richText = true };
            if (current != null)
            {
                var frame = new Rect(Screen.width * 0.5f - 130f, 14f, 260f, 40f);
                GUI.color = new Color(0f, 0f, 0f, 0.6f);
                GUI.DrawTexture(frame, Texture2D.whiteTexture);
                GUI.color = new Color(0.75f, 0.12f, 0.12f);
                GUI.DrawTexture(new Rect(frame.x + 8f, frame.y + 24f, frame.width - 16f, 9f), Texture2D.whiteTexture);
                GUI.color = Color.white;
                GUI.Label(new Rect(frame.x, frame.y + 2f, frame.width, 20f), $"<b>{current.displayName}</b>", center);
            }
            if (hovered != null && cam != null)
            {
                Vector3 sp = cam.WorldToScreenPoint(hovered.AimPoint + Vector3.up * 1.2f);
                if (sp.z > 0f)
                    GUI.Label(new Rect(sp.x - 100f, Screen.height - sp.y - 10f, 200f, 20f), $"<color=#ff8a80><b>{hovered.displayName}</b></color>", center);
            }
        }
    }
}
