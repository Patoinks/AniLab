using System;
using System.Collections.Generic;
using UnityEngine;

namespace AnimLab.KonoSuba
{
    /// <summary>
    /// Ficha do personagem (como no Metin2): nível, experiência e pontos para os 4 atributos
    /// (Vitality, Intelligence, Strength, Dexterity). Tudo o resto deriva deles e é aplicado ao jogo:
    ///   Vitality      → vida máxima, defesa, regeneração de vida, postura
    ///   Intelligence  → mana máxima, regeneração de mana, ataque e defesa mágicos, velocidade de lançamento
    ///   Strength      → ataque (dano da espada), força de stagger
    ///   Dexterity     → velocidade de ataque e de movimento, crítico, defesa
    /// A janela (StatsWindow, tecla C) só lê daqui e chama Spend().
    /// Árvores de habilidades (SkillTreeWindow, tecla V): o jogador escolhe dois elementos e aprende nós
    /// com skill points. A barra de mana passa a ser um degradê entre as cores desses dois elementos.
    /// A escolha e os nós aprendidos ficam guardados em PlayerPrefs.
    /// </summary>
    [DefaultExecutionOrder(-50)] // antes do jogo usar os valores no frame
    public class CharacterStats : MonoBehaviour
    {
        public enum Attribute { Vitality, Intelligence, Strength, Dexterity }

        public string characterName = "Adventurer";
        public string guild = "No Guild";

        [Header("Progressão")]
        public int level = 12;
        public float exp = 6200f;
        public int statPoints = 5;
        [Tooltip("Pontos por nível.")]
        public int pointsPerLevel = 3;

        [Header("Árvores de habilidades")]
        public AbilityElement treeA = AbilityElement.Neutral, treeB = AbilityElement.Neutral;
        public int skillPoints = 4;

        [Header("Atributos")]
        public int vitality = 8, intelligence = 6, strength = 9, dexterity = 7;

        [Header("Resistências base (0–0.8)")]
        public float fireResist = 0.05f, waterResist = 0.1f, lightningResist = 0f, airResist = 0.05f,
                     earthResist = 0.1f, iceResist = 0f, darkResist = 0f;

        public event Action Changed;

        // ------------------------------------------------------------------ Derivados

        public float ExpToNext => Mathf.Round(100f * Mathf.Pow(level, 1.8f));
        public float MaxHealth => 100f + vitality * 12f + level * 8f;
        public float MaxMana => 60f + intelligence * 8f + level * 4f;
        public float HealthRegen => 0.5f + vitality * 0.15f;
        public float ManaRegen => 4f + intelligence * 0.4f;
        public float Attack => 10f + strength * 2f + level;
        public float Defense => 5f + vitality * 1.2f + dexterity * 0.5f + level * 0.5f;
        public float MagicAttack => 8f + intelligence * 2.2f + level;
        public float MagicDefense => 4f + intelligence * 1.2f + vitality * 0.4f + level * 0.5f;
        public float MoveSpeed => 1f + dexterity * 0.004f;       // fator (1 = 100%)
        public float AttackSpeed => 1f + dexterity * 0.008f;     // fator
        public float CastSpeed => 1f + intelligence * 0.008f;
        public float CritChance => Mathf.Min(0.6f, 0.03f + dexterity * 0.004f);
        public float CritDamage => 1.5f + strength * 0.005f;
        public float Poise => 100f + vitality * 2f;
        public float StaggerPower => 1f + strength * 0.01f;
        public float KnockbackResist => Mathf.Min(0.6f, vitality * 0.006f);
        public float BlockReduction => 0.8f;
        public float ParryWindow => 0.2f;
        public float CooldownReduction => Mathf.Min(0.4f, intelligence * 0.004f);
        /// <summary>O dano da espada escala com o ataque, a partir de uma base de 30.</summary>
        public float WeaponScale => Attack / 30f;
        /// <summary>O dano das magias escala com o ataque mágico, a partir de uma base de 30.</summary>
        public float SpellScale => MagicAttack / 30f;

        public int Get(Attribute a) => a switch
        {
            Attribute.Vitality => vitality,
            Attribute.Intelligence => intelligence,
            Attribute.Strength => strength,
            _ => dexterity
        };

        public float ElementResist(AbilityElement e) => e switch
        {
            AbilityElement.Fire => fireResist,
            AbilityElement.Water => waterResist,
            AbilityElement.Lightning => lightningResist,
            AbilityElement.Air => airResist,
            AbilityElement.Earth => earthResist,
            AbilityElement.Ice => iceResist,
            AbilityElement.Dark => darkResist,
            _ => 0f
        };

        /// <summary>Resistência a um estado: a do elemento que o causa, mais um pouco de vitalidade.</summary>
        public float StatusResist(StatusType s)
        {
            AbilityElement from = s switch
            {
                StatusType.Wet => AbilityElement.Water,
                StatusType.Static => AbilityElement.Lightning,
                StatusType.Grounded => AbilityElement.Earth,
                StatusType.Frozen => AbilityElement.Ice,
                StatusType.Asphyxia => AbilityElement.Air,
                StatusType.Burning => AbilityElement.Fire,
                _ => AbilityElement.Dark
            };
            return Mathf.Min(0.8f, ElementResist(from) + vitality * 0.003f);
        }

        // ------------------------------------------------------------------ Ações

        // ------------------------------------------------------------------ Árvores

        const string TreePrefs = "AnimLab.SkillTrees.";
        readonly HashSet<string> learned = new HashSet<string>();

        public bool HasTrees => treeA != AbilityElement.Neutral && treeB != AbilityElement.Neutral;
        public bool IsLearned(SkillNode n) => learned.Contains(n.id);

        /// <summary>Base sem requisitos; ramos pedem a base; o final pede um dos ramos.</summary>
        public bool CanLearn(AbilityElement tree, SkillNode n)
        {
            if (!n.Available || IsLearned(n) || skillPoints < SkillTrees.PointsPerNode) return false;
            if (tree != treeA && tree != treeB) return false;
            foreach (SkillNode other in SkillTrees.Of(tree))
                if (other.tier == n.tier - 1 && IsLearned(other)) return true;
            return n.tier == 1;
        }

        public bool Learn(AbilityElement tree, SkillNode n)
        {
            if (!CanLearn(tree, n)) return false;
            skillPoints -= SkillTrees.PointsPerNode;
            learned.Add(n.id);
            SaveTrees();
            Changed?.Invoke();
            return true;
        }

        public void ChooseTrees(AbilityElement a, AbilityElement b)
        {
            treeA = a;
            treeB = b;
            SaveTrees();
            Changed?.Invoke();
        }

        /// <summary>Volta a escolher (protótipo): devolve os pontos dos nós aprendidos.</summary>
        public void ResetTrees()
        {
            skillPoints += learned.Count * SkillTrees.PointsPerNode;
            learned.Clear();
            treeA = treeB = AbilityElement.Neutral;
            SaveTrees();
            Changed?.Invoke();
        }

        void LoadTrees()
        {
            if (!PlayerPrefs.HasKey(TreePrefs + "A")) return;
            treeA = (AbilityElement)PlayerPrefs.GetInt(TreePrefs + "A");
            treeB = (AbilityElement)PlayerPrefs.GetInt(TreePrefs + "B");
            skillPoints = PlayerPrefs.GetInt(TreePrefs + "Points", skillPoints);
            learned.Clear();
            foreach (string id in PlayerPrefs.GetString(TreePrefs + "Learned", "").Split(','))
                if (id.Length > 0) learned.Add(id);
        }

        void SaveTrees()
        {
            PlayerPrefs.SetInt(TreePrefs + "A", (int)treeA);
            PlayerPrefs.SetInt(TreePrefs + "B", (int)treeB);
            PlayerPrefs.SetInt(TreePrefs + "Points", skillPoints);
            PlayerPrefs.SetString(TreePrefs + "Learned", string.Join(",", learned));
            PlayerPrefs.Save();
        }

        /// <summary>Cores da barra de mana: as dos dois elementos (sem árvores, o azul de sempre).</summary>
        public bool ManaColors(out Color a, out Color b)
        {
            a = b = default;
            if (!HasTrees) return false;
            a = Combat.ElementColor(treeA);
            b = Combat.ElementColor(treeB);
            return true;
        }

        public bool Spend(Attribute a)
        {
            if (statPoints <= 0) return false;
            statPoints--;
            switch (a)
            {
                case Attribute.Vitality: vitality++; break;
                case Attribute.Intelligence: intelligence++; break;
                case Attribute.Strength: strength++; break;
                default: dexterity++; break;
            }
            Apply(keepRatios: true);
            Changed?.Invoke();
            return true;
        }

        public void GainExp(float amount)
        {
            exp += amount;
            while (exp >= ExpToNext) { exp -= ExpToNext; level++; statPoints += pointsPerLevel; }
            Apply(keepRatios: true);
            Changed?.Invoke();
        }

        // ------------------------------------------------------------------ Aplicação ao jogo

        Combatant body;
        AbilityBar bar;
        CapsulePlayer mover;
        SwordWielder sword;
        GameHud hud;

        void Awake()
        {
            body = GetComponent<Combatant>();
            bar = GetComponent<AbilityBar>();
            mover = GetComponent<CapsulePlayer>();
            sword = GetComponent<SwordWielder>();
            hud = FindFirstObjectByType<GameHud>();
            LoadTrees();
            Apply(keepRatios: false);
        }

        void Update()
        {
            if (body != null && body.health > 0f && body.health < body.maxHealth)
                body.health = Mathf.Min(body.maxHealth, body.health + HealthRegen * Time.deltaTime);
            if (hud != null && body != null) { hud.health = body.health; hud.maxHealth = body.maxHealth; }
        }

        /// <summary>Escreve os derivados nos sistemas do jogo. keepRatios: vida e mana mantêm a percentagem.</summary>
        public void Apply(bool keepRatios)
        {
            if (body != null)
            {
                float ratio = keepRatios ? body.Health01 : 1f;
                body.maxHealth = MaxHealth;
                body.health = MaxHealth * ratio;
                body.defense = Defense;
                body.magicDefense = MagicDefense;
                body.maxPoise = Poise;
                body.knockbackScale = 1f - KnockbackResist;
                foreach (AbilityElement e in Combat.Elements) body.elementResist[e] = ElementResist(e);
                foreach (StatusType s in Combat.Statuses) body.statusResist[s] = StatusResist(s);
            }
            if (bar != null)
            {
                float ratio = keepRatios ? bar.mana / Mathf.Max(bar.maxMana, 1f) : 1f;
                bar.maxMana = MaxMana;
                bar.mana = MaxMana * ratio;
                bar.manaRegen = ManaRegen;
            }
            if (mover != null) mover.speedScale = MoveSpeed;
            if (sword != null)
            {
                sword.damageScale = WeaponScale;
                sword.speedScale = AttackSpeed;
                sword.critChance = CritChance;
                sword.critDamage = CritDamage;
                sword.staggerScale = StaggerPower;
                sword.blockReduction = BlockReduction;
                sword.parryWindow = ParryWindow;
            }
        }
    }
}
