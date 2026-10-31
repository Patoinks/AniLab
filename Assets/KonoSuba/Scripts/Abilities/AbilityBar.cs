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
    public class AbilityBar : MonoBehaviour
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
