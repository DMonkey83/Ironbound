using Ironbound.Rules.Creatures;
using Ironbound.Rules.Saves;

namespace Ironbound.Rules.Classes;

/// <summary>
/// How much experience each level costs, and what gaining one does to somebody.
/// </summary>
/// <remarks>
/// The medium track, which is the default pace. The numbers matter less than the shape: each
/// level costs roughly half again what the last one did, so a party that skips a fight does not
/// merely fall behind, it falls behind at an accelerating rate. That is the arithmetic behind
/// "come back when you are stronger" being a real answer rather than a rude one.
/// </remarks>
public static class Levelling
{
    /// <summary>Experience needed to reach each level, starting at second.</summary>
    private static readonly int[] Thresholds =
    [
        0,        // first: you start here
        2_000,
        5_000,
        9_000,
        15_000,
        23_000,
        35_000,
        51_000,
        75_000,
        105_000,
        145_000,
        200_000,
        275_000,
        375_000,
        515_000,
        710_000,
        970_000,
        1_329_000,
        1_800_000,
        2_400_000,
    ];

    public static int Maximum => Thresholds.Length;

    /// <summary>The experience somebody of this level is assumed to have already earned.</summary>
    public static int ThresholdFor(int level) =>
        Thresholds[Math.Clamp(level, 1, Thresholds.Length) - 1];

    /// <summary>The level a given amount of experience is worth.</summary>
    public static int LevelFor(int experience)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(experience);

        var level = 1;
        while (level < Thresholds.Length && experience >= Thresholds[level])
        {
            level++;
        }

        return level;
    }

    /// <summary>What it takes to reach the next one, or null at the top of the table.</summary>
    public static int? NextThreshold(int experience)
    {
        var level = LevelFor(experience);
        return level >= Thresholds.Length ? null : Thresholds[level];
    }

    /// <summary>
    /// What defeating something is worth. Derived from its level rather than written down,
    /// because a number in every creature file is one more thing to get wrong.
    /// </summary>
    public static int Award(Creature defeated)
    {
        ArgumentNullException.ThrowIfNull(defeated);

        // Roughly the published award for a creature of that level, without the table: it
        // doubles every two levels, which is what keeps early fights from being worth nothing
        // and late ones from being worth everything.
        var level = Math.Max(1, defeated.Level);
        return 100 * level * level;
    }

    /// <summary>
    /// Adds a level of a class to somebody who already exists.
    /// </summary>
    /// <remarks>
    /// Everything is recomputed from the new list rather than added to the old numbers. That
    /// matters for saves in particular: a second class's good save is not "plus two", it is a
    /// different progression summed with the first, and adding deltas would drift apart from
    /// what the same character would be if built from scratch.
    /// </remarks>
    public static bool Gain(Creature creature, ClassDefinition taken, RuleOptions? rules = null)
    {
        ArgumentNullException.ThrowIfNull(creature);
        ArgumentNullException.ThrowIfNull(taken);
        rules ??= creature.Rules;

        if (creature.Level >= Maximum)
        {
            return false;
        }

        var existing = creature.Levels.ToList().FindIndex(
            level => ReferenceEquals(level.Class, taken)
                || string.Equals(level.Class.Id, taken.Id, StringComparison.Ordinal));

        if (existing >= 0)
        {
            creature.Levels[existing] = creature.Levels[existing] with
            {
                Level = creature.Levels[existing].Level + 1,
            };
        }
        else
        {
            creature.Levels.Add(new ClassLevel(taken, 1));
        }

        creature.BaseAttackBonus = Progression.BaseAttack(creature.Levels);

        foreach (var save in SaveInfo.All)
        {
            creature.Saves[save].Base = Progression.SaveBase(save, creature.Levels);
        }

        creature.Spells.CasterLevel = Progression.CasterLevel(creature.Levels);
        creature.HitPoints.GainHitDie(PerLevel(taken, rules));

        return true;
    }

    /// <summary>
    /// The hit points a level of this class is worth, by the same rule the builder uses.
    /// </summary>
    /// <remarks>
    /// Rolled is treated as average here for the same reason it is when a creature is built:
    /// levelling would otherwise consume the random source, and what a character is worth would
    /// depend on how many other things had levelled first.
    /// </remarks>
    private static int PerLevel(ClassDefinition taken, RuleOptions rules) =>
        rules.HitPointGeneration == HitPointGeneration.Maximum
            ? taken.HitDie
            : (taken.HitDie / 2) + 1;
}
