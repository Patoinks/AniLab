using UnityEngine;

namespace AnimLab.KonoSuba
{
    /// <summary>
    /// Habilidade de teste (só para desenvolvimento): golpe instantâneo de um elemento no alvo selecionado (ou no
    /// inimigo mais próximo), com o estado do elemento e hipótese de crítico. Serve para ver os números de dano
    /// de cada elemento sem precisar das habilidades nem das árvores. Não fica na cena: Install() junta-as ao
    /// player em Play (menu AnimLab/Teste de dano) e põe-nas na hotbar, teclas 2–8.
    /// </summary>
    public class ElementTestAbility : Ability
    {
        public int minDamage = 40, maxDamage = 130;
        [Range(0f, 1f)] public float critChance = 0.25f;
        public float critMultiplier = 1.8f;
        public float range = 30f;

        static readonly AbilityElement[] Elements =
        {
            AbilityElement.Fire, AbilityElement.Water, AbilityElement.Lightning, AbilityElement.Air,
            AbilityElement.Earth, AbilityElement.Ice, AbilityElement.Dark
        };

        void Reset()
        {
            manaCost = 0f;
            cooldown = 0.25f;
        }

        protected override bool Cast(AbilityContext ctx)
        {
            Combatant target = ctx.target != null ? ctx.target.GetComponentInParent<Combatant>() : null;
            if (target == null) target = Nearest(ctx.caster);
            if (target == null) return false;
            bool crit = Random.value < critChance;
            float damage = Random.Range(minDamage, maxDamage + 1) * (crit ? critMultiplier : 1f);
            target.TakeHit(new HitInfo
            {
                damage = damage, element = element, critical = crit,
                status = SkillTrees.Glyph(element), statusDuration = 3f, source = ctx.caster
            });
            return true;
        }

        Combatant Nearest(Transform caster)
        {
            Combatant best = null;
            float bestD = range * range;
            foreach (Combatant c in Combatant.All)
            {
                if (c == null || (caster != null && c.transform.IsChildOf(caster))) continue;
                float d = (c.transform.position - caster.position).sqrMagnitude;
                if (d < bestD) { bestD = d; best = c; }
            }
            return best;
        }

        /// <summary>Junta uma habilidade de teste por elemento ao player e põe-nas na hotbar (espaços 1–7).</summary>
        public static int Install(GameObject player)
        {
            var bar = player.GetComponent<AbilityBar>();
            if (bar == null) return 0;
            foreach (ElementTestAbility old in player.GetComponents<ElementTestAbility>()) Destroy(old);
            for (int i = 0; i < Elements.Length; i++)
            {
                var a = player.AddComponent<ElementTestAbility>();
                a.element = Elements[i];
                a.displayName = "Teste " + SkillTrees.ElementName(Elements[i]);
                a.slot = i + 1;
                a.manaCost = 0f;
                a.cooldown = 0.25f;
                bar.RegisterVisual(a, Icon(Combat.ElementColor(Elements[i])), GameHud.Glyph.None);
                bar.Place(i + 1, a);
            }
            return Elements.Length;
        }

        // Ícone simples: um círculo na cor do elemento, com borda clara.
        static Texture2D Icon(Color color)
        {
            const int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { hideFlags = HideFlags.DontSave };
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(size, size) * 0.5f) / (size * 0.5f);
                    float fill = Mathf.Clamp01((0.72f - d) * 20f);
                    float ring = Mathf.Clamp01((0.86f - d) * 20f) - fill;
                    Color c = color * fill + Color.white * (ring * 0.8f);
                    c.a = Mathf.Clamp01(fill + ring);
                    px[y * size + x] = c;
                }
            tex.SetPixels(px);
            tex.Apply();
            return tex;
        }
    }
}
