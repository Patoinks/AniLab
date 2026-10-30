using UnityEngine;

namespace AnimLab.KonoSuba
{
    /// <summary>
    /// Liga as habilidades do player às teclas 1–9, gere a mana (custo e regeneração) e atualiza o HUD.
    /// Habilidades canalizadas gastam mana aos poucos (TrySpend) e, enquanto canalizam, a mana não regenera.
    /// </summary>
    public class AbilityBar : MonoBehaviour
    {
        public GameHud hud;
        public float maxMana = 100f;
        public float mana = 100f;
        [Tooltip("Mana recuperada por segundo.")]
        public float manaRegen = 6f;

        Ability[] abilities;
        int channels;

        /// <summary>Enquanto houver canalizações ativas, a mana não regenera.</summary>
        public void SetChanneling(bool on) => channels = Mathf.Max(0, channels + (on ? 1 : -1));

        /// <summary>Gasta mana se houver; devolve falso (sem gastar) se não chegar.</summary>
        public bool TrySpend(float amount)
        {
            if (mana < amount) return false;
            mana -= amount;
            return true;
        }
        LockOn lockOn;
        Metin2Camera rig;

        void Start()
        {
            abilities = GetComponents<Ability>();
            lockOn = GetComponent<LockOn>();
            if (Camera.main != null) rig = Camera.main.GetComponent<Metin2Camera>();
            if (hud != null)
                foreach (Ability a in abilities)
                    hud.SetSlotGlyph(a.slot, a is DashAbility ? GameHud.Glyph.Dash
                                           : a is LightningAbility ? GameHud.Glyph.Lightning
                                           : a is KirinAbility ? GameHud.Glyph.Kirin
                                           : a is FireBallAbility ? GameHud.Glyph.Fire
                                           : a is WindBallAbility ? GameHud.Glyph.Wind
                                           : a is SummonAbility summon ? summon.glyph
                                           : a is ThunderAbility ? GameHud.Glyph.Lightning
                                           : GameHud.Glyph.WaterBall);
        }

        void Update()
        {
            if (channels == 0) mana = Mathf.Min(maxMana, mana + manaRegen * Time.deltaTime);

            foreach (Ability a in abilities)
            {
                if (!Input.GetKeyDown(KeyCode.Alpha1 + a.slot)) continue;
                if (hud != null) hud.selectedSlot = a.slot;
                if (mana < a.manaCost || !a.IsReady) { if (hud != null) hud.Deny(a.slot); continue; }
                if (a.TryCast(BuildContext())) mana -= a.manaCost;
            }

            if (hud != null)
            {
                hud.mana = mana;
                hud.maxMana = maxMana;
                foreach (Ability a in abilities) hud.SetCooldown(a.slot, a.CooldownRemaining01);
            }
        }

        AbilityContext BuildContext()
        {
            var ctx = new AbilityContext { caster = transform, target = lockOn != null ? lockOn.Current : null };

            Camera cam = Camera.main;
            if (cam != null && new Plane(Vector3.up, Vector3.zero).Raycast(cam.ScreenPointToRay(Input.mousePosition), out float d))
                ctx.aimPoint = cam.ScreenPointToRay(Input.mousePosition).GetPoint(d);
            else
                ctx.aimPoint = transform.position + transform.forward * 10f;

            Vector2 axis = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
            if (axis.sqrMagnitude > 0.01f)
            {
                float yaw = rig != null ? rig.yaw : (cam != null ? cam.transform.eulerAngles.y : 0f);
                ctx.moveDirection = (Quaternion.Euler(0f, yaw, 0f) * new Vector3(axis.x, 0f, axis.y)).normalized;
            }
            return ctx;
        }
    }
}
