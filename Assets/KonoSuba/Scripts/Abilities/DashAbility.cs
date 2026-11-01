using System.Collections;
using UnityEngine;

namespace AnimLab.KonoSuba
{
    /// <summary>
    /// Dash (neutro): arranque curto e rápido na direção de WASD (ou para a frente, se parado).
    /// Não usa o ambiente, mas mexe nele: o ar deslocado abre um rasto no nevoeiro e, mais fraco
    /// (a água é mais pesada), na água, com salpicos à partida e à chegada.
    /// </summary>
    public class DashAbility : Ability, AnimLab.Netcode.Unity.INetAbilitySource, AnimLab.Netcode.Unity.INetAbilityPresenter
    {
        // ------------------------------------------------------------------ Online
        public AnimLab.Netcode.Sim.AbilityId NetAbility => AnimLab.Netcode.Sim.AbilityId.Dash;
        public void Present(AnimLab.Netcode.Sim.SimEvent ev, GameObject other)
        {
            Vector3 at = AnimLab.Netcode.Unity.ViewConvert.Vector(ev.Point);
            if (ev.Other == 1) { if (WaterRipples.Instance != null) WaterRipples.Instance.Spawn(at, 0.9f); return; }
            // O ar empurrado à frente do corpo: rasto no nevoeiro e na água (como em Dash()).
            FogField.Wind(at, AnimLab.Netcode.Unity.ViewConvert.Vector(ev.Direction), distance * 1.4f, 1.8f, 0.45f, 3f);
            if (WaterRipples.Instance != null) WaterRipples.Instance.Spray(at, 0.5f);
        }
        public void Sync(int slot, in AnimLab.Netcode.Sim.ProjectileState now, in AnimLab.Netcode.Sim.ProjectileState before, float alpha, float dt) { }
        public void Clear(int slot) { }
        public void CaptureNetAbility(ref AnimLab.Netcode.Sim.AbilityTable t)
        {
            t.DashMana = AnimLab.Netcode.Unity.ViewConvert.Fixed(manaCost); t.DashCooldown = AnimLab.Netcode.Unity.ViewConvert.Fixed(cooldown);
            t.DashDistance = AnimLab.Netcode.Unity.ViewConvert.Fixed(distance); t.DashDuration = AnimLab.Netcode.Unity.ViewConvert.Fixed(duration);
        }
        public float distance = 4.5f;
        public float duration = 0.16f;

        CharacterController controller;

        void Reset()
        {
            displayName = "Dash";
            element = AbilityElement.Neutral;
            slot = 0;
            manaCost = 12f;
            cooldown = 1.2f;
        }

        void Awake() => controller = GetComponent<CharacterController>();

        protected override bool Cast(AbilityContext ctx)
        {
            Vector3 dir = ctx.moveDirection.sqrMagnitude > 0.01f ? ctx.moveDirection : Flat(ctx.caster.forward);
            if (dir.sqrMagnitude < 0.01f) return false;
            StartCoroutine(Dash(dir.normalized));
            return true;
        }

        static Vector3 Flat(Vector3 v) { v.y = 0f; return v.normalized; }

        IEnumerator Dash(Vector3 dir)
        {
            Vector3 start = transform.position;
            transform.rotation = Quaternion.LookRotation(dir);

            // O ar empurrado à frente do corpo: rasto no nevoeiro e na água (mais fraco na água).
            FogField.Wind(start, dir, distance * 1.4f, 1.8f, 0.45f, 3f);
            if (WaterRipples.Instance != null) WaterRipples.Instance.Spray(start, 0.5f);

            float t = 0f;
            while (t < duration)
            {
                float dt = Time.deltaTime;
                t += dt;
                // Começa no máximo e trava no fim (dá peso ao arranque).
                float k = Mathf.Clamp01(t / duration);
                float speed = distance / duration * 2f * (1f - k);
                if (controller != null) controller.Move(dir * speed * dt);
                else transform.position += dir * speed * dt;
                yield return null;
            }

            if (WaterRipples.Instance != null) WaterRipples.Instance.Spawn(transform.position, 0.9f);
        }
    }
}
