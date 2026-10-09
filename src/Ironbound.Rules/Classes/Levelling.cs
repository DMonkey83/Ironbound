using Ironbound.Rules.Creatures;
using Ironbound.Rules.Saves;

namespace Ironbound.Rules.Classes;

/// <summary>
/// How much experience each level costs, and what gaining one does to somebody.
/// </summary>
/// <remarks>
/// The medium track, which is the default pace, as the Core Rulebook's Table 3–1 has it all the
/// way to twentieth. The numbers matter less than the shape: each level costs roughly half again
/// what the last one did, so a party that skips a fight does not merely fall behind, it falls
/// behind at an accelerating rate. That is the arithmetic behind "come back when you are
/// stronger" being a real answer rather than a rude one.
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
        155_000,
        220_000,
        315_000,
        445_000,
        635_000,
        890_000,
        1_300_000,
        1_800_000,
        2_550_000,
        3_600_000,
    ];

    public static int Maximum => Thresholds.Length;

    /// <summary>The experience somebody of this level is assumed to have already earned.</summary>
    public static int ThresholdFor(int level) =>
        Thresholds[Math.Clamp(level, 1, Thresholds.Length) - 1];

    /// <summary>
    /// Whether reaching this level comes with a feat.
    /// </summary>
    /// <remarks>
    /// An opportunity rather than a running budget. Counting feats owed against feats held does
    /// not work: a creature written in a content file carries feats it never earned — Valeria
    /// has five at sixth level, where three odd levels have passed — and subtracting one from
    /// the other would tell her she is four in arrears.
    /// </remarks>
    public static bool GrantsFeatAt(int level) => level % 2 == 1;

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
    /// What defeating something is worth, in total, before it is shared out: the Bestiary's
    /// experience for its challenge rating.
    /// </summary>
    /// <remarks>
    /// A goblin is CR 1/3 and worth 135 whether it is the first goblin of a level or the
    /// fiftieth, and an ogre is worth six of them. The rating comes from the creature's file, or
    /// from its class levels by the Bestiary's rule, so the award is a fact about the foe rather
    /// than a formula about it.
    /// </remarks>
    public static int Award(Creature defeated)
    {
        ArgumentNullException.ThrowIfNull(defeated);
        return defeated.Challenge.Experience;
    }

    /// <summary>
    /// One character's share of an award: the total split evenly among everyone in the party,
    /// rounded down, as the book divides it.
    /// </summary>
    /// <remarks>
    /// The campaign keeps one experience number for the whole party rather than one each, and
    /// that number is a single character's. So a fight worth 405 to a party of four moves it by
    /// 101 — what each of the four earned, not what they earned between them.
    /// </remarks>
    public static int Share(int total, int partySize)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(total);
        return partySize <= 0 ? total : total / partySize;
    }

    /// <summary>
    /// Whether reaching this character level raises an ability score by one: fourth, eighth,
    /// twelfth, sixteenth and twentieth.
    /// </summary>
    public static bool GrantsAbilityIncreaseAt(int level) => level > 0 && level % 4 == 0;

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

        // Toughness is three hit points or one a die, whichever is more: past the third die,
        // every new one brings another.
        if (creature.HasFeat(Feats.FeatEffect.Toughness) && creature.HitPoints.HitDice > 3)
        {
            creature.HitPoints.Base += 1;
        }

        // Raised rather than set: a wizard who levels mid-day gets the new slots and keeps the
        // ones they spent this morning spent.
        var casting = creature.Abilities[creature.Spells.CastingAbility].Modifier;

        foreach (var (level, count) in Progression.SlotsFor(creature.Levels, casting))
        {
            creature.Spells.RaiseSlots(level, count);
        }

        // Whatever the level brings that needs no choosing: a domain or school slot at a new
        // spell level, a barbarian's damage reduction. The choices are the caller's.
        ClassFeatures.Grow(creature);

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
