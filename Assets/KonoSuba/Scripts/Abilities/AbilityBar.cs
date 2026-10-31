using System.Collections.Generic;
using UnityEngine;

namespace AnimLab.KonoSuba
{
    /// <summary>
    /// Liga as habilidades do player às teclas 1–9, gere a mana (custo e regeneração) e atualiza o HUD.
    /// Habilidades canalizadas gastam mana aos poucos (TrySpend) e, enquanto canalizam, a mana não regenera.
    /// A hotbar começa vazia: o jogador arrasta para ela (estilo Metin2) as habilidades que aprendeu na janela V
    /// (o Dash é geral, está sempre disponível). A disposição fica guardada em PlayerPrefs; uma habilidade que
    /// deixe de estar aprendida (ex.: mudar de árvores) sai sozinha da hotbar.
    /// </summary>
    public class AbilityBar : MonoBehaviour, AnimLab.Netcode.Unity.INetConfigSource, AnimLab.Netcode.Unity.INetAbilityView
    {
        public GameHud hud;
        public float maxMana = 100f;
        public float mana = 100f;
        [Tooltip("Mana recuperada por segundo.")]
        public float manaRegen = 6f;

        Ability[] abilities;
        int channels;
        const string Prefs = "AnimLab.Hotbar.";
        readonly Ability[] slots = new Ability[9];
        readonly Dictionary<Ability, (Texture2D icon, GameHud.Glyph glyph)> visuals = new Dictionary<Ability, (Texture2D, GameHud.Glyph)>();
        CharacterStats stats;

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
            stats = GetComponent<CharacterStats>();
            if (Camera.main != null) rig = Camera.main.GetComponent<Metin2Camera>();
            // O aspeto de cada habilidade (ícone em papel, ou símbolo vetorial) vinha preso ao espaço de origem.
            foreach (Ability a in abilities)
            {
                Texture2D icon = hud != null && hud.abilityIcons != null && a.slot >= 0 && a.slot < hud.abilityIcons.Length ? hud.abilityIcons[a.slot] : null;
                GameHud.Glyph glyph = a is DashAbility ? GameHud.Glyph.Dash
                                    : a is LightningAbility ? GameHud.Glyph.Lightning
                                    : a is KirinAbility ? GameHud.Glyph.Kirin
                                    : a is FireBallAbility ? GameHud.Glyph.Fire
                                    : a is WindBallAbility ? GameHud.Glyph.Wind
                                    : a is SummonAbility summon ? summon.glyph
                                    : a is ThunderAbility ? GameHud.Glyph.Lightning
                                    : GameHud.Glyph.WaterBall;
                visuals[a] = (icon, glyph);
            }
            Load();
            Show();
        }

        // ------------------------------------------------------------------ Hotbar

        public Ability At(int slot) => slot >= 0 && slot < slots.Length ? slots[slot] : null;

        public Ability Find(string displayName)
        {
            if (abilities == null) abilities = GetComponents<Ability>();
            foreach (Ability a in abilities) if (a.displayName == displayName) return a;
            return null;
        }

        /// <summary>O Dash é geral; as outras têm de estar aprendidas numa das árvores escolhidas.</summary>
        public bool CanUse(Ability a) => a != null && (a is DashAbility || stats == null || stats.KnowsAbility(a.displayName));

        public bool Visual(Ability a, out Texture2D icon, out GameHud.Glyph glyph)
        {
            icon = null;
            glyph = GameHud.Glyph.None;
            if (a == null || !visuals.TryGetValue(a, out var v)) return false;
            (icon, glyph) = v;
            return true;
        }

        /// <summary>Põe a habilidade no espaço (sai de onde estava). Vinda de outro espaço, trocam de lugar.</summary>
        public void Place(int slot, Ability a, int from = -1)
        {
            if (slot < 0 || slot >= slots.Length || !CanUse(a)) return;
            Ability previous = slots[slot];
            for (int i = 0; i < slots.Length; i++) if (slots[i] == a) slots[i] = null;
            slots[slot] = a;
            if (from >= 0 && from < slots.Length && from != slot) slots[from] = previous;
            Save();
            Show();
        }

        public void Clear(int slot)
        {
            if (slot < 0 || slot >= slots.Length || slots[slot] == null) return;
            slots[slot] = null;
            Save();
            Show();
        }

        void Load()
        {
            for (int i = 0; i < slots.Length; i++)
            {
                Ability a = Find(PlayerPrefs.GetString(Prefs + i, ""));
                slots[i] = CanUse(a) ? a : null;
            }
        }

        void Save()
        {
            for (int i = 0; i < slots.Length; i++) PlayerPrefs.SetString(Prefs + i, slots[i] != null ? slots[i].displayName : "");
            PlayerPrefs.Save();
        }

        void Show()
        {
            if (hud == null) return;
            for (int i = 0; i < slots.Length; i++)
            {
                Visual(slots[i], out Texture2D icon, out GameHud.Glyph glyph);
                hud.SetSlot(i, icon, glyph);
            }
        }

        void Update()
        {
            if (externalDrive) return;
            if (channels == 0) mana = Mathf.Min(maxMana, mana + manaRegen * Time.deltaTime);

            for (int i = 0; i < slots.Length; i++)
            {
                Ability a = slots[i];
                // Deixou de estar aprendida (mudou de árvores): sai da hotbar.
                if (a != null && !CanUse(a)) { Clear(i); continue; }
                if (!GameInput.GetDown((GameAction)((int)GameAction.Ability1 + i))) continue;
                if (hud != null) hud.selectedSlot = i;
                if (a == null) continue;
                if (mana < a.manaCost || !a.IsReady) { if (hud != null) hud.Deny(i); continue; }
                if (a.TryCast(BuildContext())) mana -= a.manaCost;
            }

            if (hud != null)
            {
                hud.mana = mana;
                hud.maxMana = maxMana;
                for (int i = 0; i < slots.Length; i++) hud.SetCooldown(i, slots[i] != null ? slots[i].CooldownRemaining01 : 0f);
            }
        }

        // ------------------------------------------------------------------ Online (simulação de rede)
        // Com a partida em rede, a hotbar só mostra: as teclas vão no input, a mana e as recargas vêm do estado,
        // e os VFX dos lançamentos e dos projéteis seguem os eventos e as posições simuladas.

        bool externalDrive;
        public bool ExternalDrive { get => externalDrive; set => externalDrive = value; }
        readonly Dictionary<int, FireBallFlightVisual> netBalls = new Dictionary<int, FireBallFlightVisual>();

        static AnimLab.Netcode.Sim.AbilityId IdOf(Ability a) =>
            a is DashAbility ? AnimLab.Netcode.Sim.AbilityId.Dash : a is FireBallAbility ? AnimLab.Netcode.Sim.AbilityId.FireBall
            : a is WaterBallAbility ? AnimLab.Netcode.Sim.AbilityId.WaterBall : a is WindBallAbility ? AnimLab.Netcode.Sim.AbilityId.WindBall
            : a is LightningAbility ? AnimLab.Netcode.Sim.AbilityId.Lightning : a is ThunderAbility ? AnimLab.Netcode.Sim.AbilityId.Thunder
            : a is KirinAbility ? AnimLab.Netcode.Sim.AbilityId.Kirin : a is SummonAbility ? AnimLab.Netcode.Sim.AbilityId.Summon
            : AnimLab.Netcode.Sim.AbilityId.None;

        public void CaptureNetConfig(ref AnimLab.Netcode.Sim.PlayerConfig c)
        {
            if (abilities == null) { abilities = GetComponents<Ability>(); stats = GetComponent<CharacterStats>(); Load(); }
            c.MaxMana = AnimLab.Netcode.Unity.ViewConvert.Fixed(maxMana);
            c.ManaRegen = AnimLab.Netcode.Unity.ViewConvert.Fixed(manaRegen);
            for (int i = 0; i < slots.Length && i < 9; i++) c.SetSlot(i, slots[i] != null ? IdOf(slots[i]) : AnimLab.Netcode.Sim.AbilityId.None);
            if (stats != null)
            {
                c.CastSpeed = AnimLab.Netcode.Unity.ViewConvert.Fixed(stats.CastSpeed);
                c.SpellPower = AnimLab.Netcode.Unity.ViewConvert.Fixed(stats.SpellScale);
                c.CooldownReduction = AnimLab.Netcode.Unity.ViewConvert.Fixed(stats.CooldownReduction);
            }
        }

        public void ApplyNetState(in AnimLab.Netcode.Sim.PlayerState state, float now)
        {
            // Só a barra do jogador local escreve no HUD (a cópia remota partilha a referência).
            var net = AnimLab.Netcode.Unity.NetMatch.Active;
            if (hud == null || net == null || net.LocalActor != gameObject) return;
            float cdr = stats != null ? stats.CooldownReduction : 0f;
            for (int i = 0; i < slots.Length && i < 9; i++)
            {
                Ability a = slots[i];
                float total = a != null ? a.cooldown * (1f - cdr) : 0f;
                float left = AnimLab.Netcode.Unity.ViewConvert.Number(state.CooldownUntil(i)) - now;
                hud.SetCooldown(i, total > 0f ? Mathf.Clamp01(left / total) : 0f);
            }
        }

        public void PresentNetAbility(AnimLab.Netcode.Sim.SimEvent ev, GameObject other)
        {
            Vector3 at = AnimLab.Netcode.Unity.ViewConvert.Vector(ev.Point);
            Vector3 dir = AnimLab.Netcode.Unity.ViewConvert.Vector(ev.Direction);
            switch (ev.Ability)
            {
                case AnimLab.Netcode.Sim.AbilityId.Dash:
                    if (ev.Other == 1) { if (WaterRipples.Instance != null) WaterRipples.Instance.Spawn(at, 0.9f); break; }
                    var dash = GetComponent<DashAbility>();
                    // O ar empurrado à frente do corpo: rasto no nevoeiro e na água (como DashAbility.Dash).
                    FogField.Wind(at, dir, (dash != null ? dash.distance : 4.5f) * 1.4f, 1.8f, 0.45f, 3f);
                    if (WaterRipples.Instance != null) WaterRipples.Instance.Spray(at, 0.5f);
                    break;
                case AnimLab.Netcode.Sim.AbilityId.FireBall:
                    if (ev.Kind == AnimLab.Netcode.Sim.EventKind.Impact)
                    {
                        var fire = GetComponent<FireBallAbility>();
                        Collider body = other != null ? other.GetComponentInChildren<Collider>() : null;
                        if (fire != null) FireBallEffect.Impact(at, body, transform, fire);
                    }
                    if (ev.Kind != AnimLab.Netcode.Sim.EventKind.Cast) ClearProjectile(ev.Target);
                    break;
            }
        }

        public void SyncProjectile(int slot, AnimLab.Netcode.Sim.ProjectileState now, AnimLab.Netcode.Sim.ProjectileState before, float alpha, float dt)
        {
            if (now.Kind != AnimLab.Netcode.Sim.AbilityId.FireBall) return;
            var fire = GetComponent<FireBallAbility>();
            if (fire == null) return;
            Vector3 position = Vector3.Lerp(AnimLab.Netcode.Unity.ViewConvert.Vector(before.Position), AnimLab.Netcode.Unity.ViewConvert.Vector(now.Position), alpha);
            if (!netBalls.TryGetValue(slot, out var visual) || visual == null)
            {
                float radius = Mathf.Clamp(fire.visualRadius, 0.9f, 1.1f) * 0.85f;
                netBalls[slot] = visual = FireBallFlightVisual.Create(position, radius, slot * 7919 + 13, fire.ballMaterial);
            }
            float formation = AnimLab.Netcode.Unity.ViewConvert.Number(now.Formation);
            float f = Mathf.SmoothStep(0f, 1f, AnimLab.Netcode.Unity.ViewConvert.Number(now.Age) / Mathf.Max(0.05f, formation));
            visual.Tick(dt, position, now.Launched ? AnimLab.Netcode.Unity.ViewConvert.Vector(now.Velocity) : Vector3.zero, f);
        }

        public void ClearProjectile(int slot)
        {
            if (netBalls.TryGetValue(slot, out var visual)) { if (visual != null) visual.Release(); netBalls.Remove(slot); }
        }

        AbilityContext BuildContext()
        {
            var ctx = new AbilityContext { caster = transform, target = lockOn != null ? lockOn.Current : null };

            Camera cam = Camera.main;
            if (cam != null && new Plane(Vector3.up, Vector3.zero).Raycast(cam.ScreenPointToRay(Input.mousePosition), out float d))
                ctx.aimPoint = cam.ScreenPointToRay(Input.mousePosition).GetPoint(d);
            else
                ctx.aimPoint = transform.position + transform.forward * 10f;

            Vector2 axis = GameInput.MoveAxis();
            if (axis.sqrMagnitude > 0.01f)
            {
                float yaw = rig != null ? rig.yaw : (cam != null ? cam.transform.eulerAngles.y : 0f);
                ctx.moveDirection = (Quaternion.Euler(0f, yaw, 0f) * new Vector3(axis.x, 0f, axis.y)).normalized;
            }
            return ctx;
        }
    }
}
