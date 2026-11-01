using UnityEngine;

namespace AnimLab.KonoSuba
{
    /// <summary>Bola de Fogo, tecla 5: condensa no ombro e segue para o alvo ou para o ponto de mira.</summary>
    public class FireBallAbility : Ability, AnimLab.Netcode.Unity.INetAbilitySource, AnimLab.Netcode.Unity.INetAbilityPresenter
    {
        // ------------------------------------------------------------------ Online
        readonly System.Collections.Generic.Dictionary<int, FireBallFlightVisual> netBalls = new System.Collections.Generic.Dictionary<int, FireBallFlightVisual>();
        public AnimLab.Netcode.Sim.AbilityId NetAbility => AnimLab.Netcode.Sim.AbilityId.FireBall;
        public void Present(AnimLab.Netcode.Sim.SimEvent ev, GameObject other)
        {
            if (ev.Kind != AnimLab.Netcode.Sim.EventKind.Impact) return;
            Collider body = other != null ? other.GetComponentInChildren<Collider>() : null;
            FireBallEffect.Impact(AnimLab.Netcode.Unity.ViewConvert.Vector(ev.Point), body, transform, this);
        }
        public void Sync(int slot, in AnimLab.Netcode.Sim.ProjectileState now, in AnimLab.Netcode.Sim.ProjectileState before, float alpha, float dt)
        {
            Vector3 position = Vector3.Lerp(AnimLab.Netcode.Unity.ViewConvert.Vector(before.Position), AnimLab.Netcode.Unity.ViewConvert.Vector(now.Position), alpha);
            if (!netBalls.TryGetValue(slot, out var visual) || visual == null)
                netBalls[slot] = visual = FireBallFlightVisual.Create(position, Mathf.Clamp(visualRadius, 0.9f, 1.1f) * 0.85f, slot * 7919 + 13, ballMaterial);
            float formation = AnimLab.Netcode.Unity.ViewConvert.Number(now.Formation);
            float f = Mathf.SmoothStep(0f, 1f, AnimLab.Netcode.Unity.ViewConvert.Number(now.Age) / Mathf.Max(0.05f, formation));
            visual.Tick(dt, position, now.Launched ? AnimLab.Netcode.Unity.ViewConvert.Vector(now.Velocity) : Vector3.zero, f);
        }
        public void Clear(int slot)
        {
            if (netBalls.TryGetValue(slot, out var visual)) { if (visual != null) visual.Release(); netBalls.Remove(slot); }
        }
        public void CaptureNetAbility(ref AnimLab.Netcode.Sim.AbilityTable t)
        {
            t.FireMana = AnimLab.Netcode.Unity.ViewConvert.Fixed(manaCost); t.FireCooldown = AnimLab.Netcode.Unity.ViewConvert.Fixed(cooldown);
            t.FireFormation = AnimLab.Netcode.Unity.ViewConvert.Fixed(formationTime); t.FireRadius = AnimLab.Netcode.Unity.ViewConvert.Fixed(radius);
            t.FireSpeed = AnimLab.Netcode.Unity.ViewConvert.Fixed(speed); t.FireGravity = AnimLab.Netcode.Unity.ViewConvert.Fixed(gravity);
            t.FireRange = AnimLab.Netcode.Unity.ViewConvert.Fixed(maxRange); t.FireDamage = AnimLab.Netcode.Unity.ViewConvert.Fixed(damage);
            t.FireBlast = AnimLab.Netcode.Unity.ViewConvert.Fixed(blastRadius);
            t.FireShoulder = new AnimLab.Netcode.Sim.FixVec3(AnimLab.Netcode.Unity.ViewConvert.Fixed(shoulderOffset.x),
                AnimLab.Netcode.Unity.ViewConvert.Fixed(shoulderOffset.y), AnimLab.Netcode.Unity.ViewConvert.Fixed(shoulderOffset.z));
        }
        public Material flameMaterial, steamMaterial, ringMaterial, hitMaterial;
        [Tooltip("Fogo estilizado da bola e da explosão (KonoSuba/FireToon).")]
        public Material ballMaterial;
        public Vector3 shoulderOffset = new Vector3(0.85f, 2.25f, 0.55f);
        [Min(0.05f)] public float formationTime = 0.2f;
        [Min(0.1f)] public float radius = 0.52f;
        [Tooltip("Raio visual em metros; as labaredas ultrapassam o corpo sem alargar a colisão.")]
        [Range(0.9f, 1.1f)] public float visualRadius = 1f;
        [Min(1f)] public float speed = 20f;
        [Tooltip("Gravidade própria (m/s²): um arco pequeno, compensado na mira inicial.")]
        public float gravity = 2.4f;
        public float maxRange = 36f;
        [Header("Golpe")]
        public float damage = 60f;
        [Tooltip("Raio da explosão: os corpos dentro levam dano (até 40% na borda).")]
        public float blastRadius = 2.2f;
        [Tooltip("Volume evaporado no impacto junto à água (m³). Não aplica Burning.")]
        public float evaporatedVolume = 0.12f;

        void Reset()
        {
            displayName = "Fireball";
            element = AbilityElement.Fire;
            slot = 4;
            manaCost = 25f;
            cooldown = 1.5f;
        }

        public Vector3 HoldPoint(Transform caster) => caster.position + caster.right * shoulderOffset.x
            + Vector3.up * shoulderOffset.y + caster.forward * shoulderOffset.z;

        protected override bool Cast(AbilityContext ctx)
        {
            if (ctx.caster == null || flameMaterial == null || steamMaterial == null
                || ringMaterial == null || hitMaterial == null || ballMaterial == null) return false;
            FireBallProjectile.Create(ctx, this);
            return true;
        }
    }
}
