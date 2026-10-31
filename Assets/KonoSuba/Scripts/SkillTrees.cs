using System.Collections.Generic;
using UnityEngine;

namespace AnimLab.KonoSuba
{
    /// <summary>Um nó de uma árvore: uma habilidade (ou uma por fazer, "Soon").</summary>
    public class SkillNode
    {
        public string id, name, description;
        /// <summary>1 = base, 2 = ramos, 3 = final.</summary>
        public int tier;
        /// <summary>0 = esquerda, 1 = direita (só nos ramos do tier 2).</summary>
        public int branch;
        /// <summary>displayName da habilidade que já existe no jogo; null = ainda por fazer.</summary>
        public string ability;
        public bool Available => ability != null;
    }

    /// <summary>
    /// Árvores de habilidades, uma por elemento (Bíblia: Gelo, Terra, Fogo, Água, Eletricidade, Ar, Dark).
    /// Cada árvore é um V: base → dois ramos → final. O jogador escolhe duas árvores (CharacterStats).
    /// Os nós ligados a habilidades que já existem podem ser aprendidos; os outros aparecem como "Soon".
    /// </summary>
    public static class SkillTrees
    {
        public const int PointsPerNode = 1;

        static readonly Dictionary<AbilityElement, SkillNode[]> trees = new Dictionary<AbilityElement, SkillNode[]>
        {
            { AbilityElement.Fire, new[]
            {
                N("fire1", 1, 0, "Fireball", "Fireball", "A burning orb that explodes on impact. Applies Burning."),
                N("fire2a", 2, 0, "Fire Tornado", "Fire Tornado", "Summons a roaring column of fire at the target."),
                N("fire2b", 2, 1, "Burning Blade", null, "Sword hits set enemies on fire."),
                N("fire3", 3, 0, "Inferno", null, "The ground erupts in flames around you."),
            } },
            { AbilityElement.Water, new[]
            {
                N("water1", 1, 0, "Water Ball", "Water Ball", "Channel water into a ball; hold to compress it into a lance. Applies Wet."),
                N("water2a", 2, 0, "Tidal Wave", null, "A wall of water that sweeps enemies away."),
                N("water2b", 2, 1, "Water Whip", null, "A continuous stream that lashes and pulls."),
                N("water3", 3, 0, "Leviathan", null, "A water serpent that coils around the battlefield."),
            } },
            { AbilityElement.Lightning, new[]
            {
                N("light1", 1, 0, "Lightning", "Lightning", "Discharge that crawls through water. Hits harder on Wet targets."),
                N("light2a", 2, 0, "Thunder", "Thunder", "A bolt from the sky on the target."),
                N("light2b", 2, 1, "Static Field", null, "Charges the air around you. Applies Static."),
                N("light3", 3, 0, "Kirin", "Kirin", "Calls down the lightning dragon."),
            } },
            { AbilityElement.Air, new[]
            {
                N("air1", 1, 0, "Wind Ball", "Wind Ball", "A compressed sphere of wind that bursts on impact."),
                N("air2a", 2, 0, "Wind Tornado", "Wind Tornado", "A tornado that lifts and spins enemies."),
                N("air2b", 2, 1, "Gale Step", null, "A dash carried by the wind."),
                N("air3", 3, 0, "Tempest", null, "A storm that steals the breath. Applies Asphyxia."),
            } },
            { AbilityElement.Earth, new[]
            {
                N("earth1", 1, 0, "Stone Spike", null, "A spike of rock bursts from the ground."),
                N("earth2a", 2, 0, "Quake", null, "Shakes the ground. Applies Grounded."),
                N("earth2b", 2, 1, "Stone Skin", null, "Hardens your body against hits."),
                N("earth3", 3, 0, "Titan", null, "Rise as a giant of stone."),
            } },
            { AbilityElement.Ice, new[]
            {
                N("ice1", 1, 0, "Ice Burst", null, "A flower of ice spikes erupts at the target."),
                N("ice2a", 2, 0, "Frost Lance", null, "A piercing shard of ice. Applies Frozen."),
                N("ice2b", 2, 1, "Frozen Ground", null, "Freezes the water around you."),
                N("ice3", 3, 0, "Glacier", null, "Entombs the battlefield in ice."),
            } },
            { AbilityElement.Dark, new[]
            {
                N("dark1", 1, 0, "Shadow Bolt", null, "A bolt of darkness."),
                N("dark2a", 2, 0, "Blinding Veil", null, "A cloud of shadow. Applies Blind."),
                N("dark2b", 2, 1, "Shadow Step", null, "Step through the shadows."),
                N("dark3", 3, 0, "Abyss", null, "Opens a void that swallows the light."),
            } },
        };

        static SkillNode N(string id, int tier, int branch, string name, string ability, string description) =>
            new SkillNode { id = id, tier = tier, branch = branch, name = name, ability = ability, description = description };

        public static SkillNode[] Of(AbilityElement e) => trees.TryGetValue(e, out SkillNode[] t) ? t : new SkillNode[0];

        public static string ElementName(AbilityElement e) => e switch
        {
            AbilityElement.Lightning => "Lightning",
            _ => e.ToString()
        };

        /// <summary>Linha curta de cada elemento (cartão de escolha).</summary>
        public static string ElementBlurb(AbilityElement e) => e switch
        {
            AbilityElement.Fire => "Explosions and burning damage over time.",
            AbilityElement.Water => "Heavy impacts, pressure lances and Wet.",
            AbilityElement.Lightning => "Fast strikes that chain through water.",
            AbilityElement.Air => "Control, lift and suffocation.",
            AbilityElement.Earth => "Defense, quakes and pinning enemies.",
            AbilityElement.Ice => "Spikes, piercing shards and freezing.",
            AbilityElement.Dark => "Blindness and shadow movement.",
            _ => ""
        };

        /// <summary>O símbolo do estado de cada elemento serve de emblema (gota, chama, raio…).</summary>
        public static StatusType Glyph(AbilityElement e) => e switch
        {
            AbilityElement.Fire => StatusType.Burning,
            AbilityElement.Water => StatusType.Wet,
            AbilityElement.Lightning => StatusType.Static,
            AbilityElement.Air => StatusType.Asphyxia,
            AbilityElement.Earth => StatusType.Grounded,
            AbilityElement.Ice => StatusType.Frozen,
            _ => StatusType.Blind
        };
    }
}
