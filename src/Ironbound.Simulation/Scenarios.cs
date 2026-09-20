using Ironbound.Rules;
using Ironbound.Rules.Abilities;
using Ironbound.Rules.Combat;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Dice;
using Ironbound.Rules.Maps;
using Ironbound.Rules.Modifiers;
using Ironbound.Rules.Saves;

namespace Ironbound.Simulation;

/// <summary>
/// Ready-made fights. One definition, used by the tests and by the game, so the two cannot drift
/// apart — and every one of them is reproducible from its seed.
/// </summary>
public static class Scenarios
{
    /// <summary>Stream 1 is the dice; stream 2 is what the AI is thinking. They never interfere.</summary>
    public const ulong DiceStream = 1;

    public const ulong ThoughtStream = 2;

    public static Battle GoblinAmbush(ulong seed = 20260920, RuleOptions? rules = null)
    {
        var valeria = Warrior("Valeria", new AbilityScores(18, 14, 14, 10, 12, 10), 32, 5, 5, 6, rules);
        valeria.Attacks.Add(WeaponAttack.Melee(
            "longsword", "1d8", DamageType.Slashing, new CriticalProfile(19, 2)));

        var karn = Warrior("Karn", new AbilityScores(16, 12, 16, 8, 13, 8), 40, 5, 5, 7, rules);
        karn.Attacks.Add(WeaponAttack.Melee(
            "greataxe", "1d12", DamageType.Slashing, new CriticalProfile(20, 3),
            AbilityDamageScale.OneAndAHalf));

        var goblins = Enumerable.Range(1, 3)
            .Select(index =>
            {
                var goblin = Warrior(
                    $"Goblin {index}", new AbilityScores(11, 15, 12, 10, 9, 6), 9, 2, 1, 3, rules);
                goblin.Size = CreatureSize.Small;
                goblin.Speed = 20;
                goblin.Attacks.Add(WeaponAttack.Melee(
                    "scimitar", "1d6", DamageType.Slashing, new CriticalProfile(18, 2)));
                return goblin;
            })
            .ToArray();

        // Two ranks facing each other across thirty-odd feet, so the first round is spent closing.
        var field = new Battlefield(16, 12);
        field.Place(valeria, 3, 4);
        field.Place(karn, 3, 6);
        for (var index = 0; index < goblins.Length; index++)
        {
            field.Place(goblins[index], 10, 3 + (index * 2));
        }

        return new Battle([valeria, karn], goblins, new PcgRandom(seed, DiceStream), rules, field);
    }

    /// <summary>An AI to drive a battle, thinking on its own stream.</summary>
    public static IActionSource AutoPilot(Battle battle, ulong seed = 20260920, int competence = 100) =>
        new HeuristicActionSource(battle, new PcgRandom(seed, ThoughtStream), competence);

    private static Creature Warrior(
        string name,
        AbilityScores abilities,
        int hitPoints,
        int level,
        int baseAttackBonus,
        int armour,
        RuleOptions? rules)
    {
        var creature = new Creature(name, abilities, hitPoints, level, rules);
        creature.AttackModifiers.Add(baseAttackBonus, BonusType.Untyped, "Base Attack Bonus");
        creature.ArmorClass.Modifiers.Add(armour, BonusType.Armor, "Armour");
        creature.Saves.Fortitude.Base = SaveProgression.Good(level);
        creature.Saves.Reflex.Base = SaveProgression.Poor(level);
        creature.Saves.Will.Base = SaveProgression.Poor(level);
        return creature;
    }
}
