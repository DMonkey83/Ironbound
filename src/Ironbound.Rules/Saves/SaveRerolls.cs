using Ironbound.Rules.Creatures;
using Ironbound.Rules.Dice;
using Ironbound.Rules.Feats;
using Ironbound.Rules.Modifiers;

namespace Ironbound.Rules.Saves;

/// <summary>
/// Improved Great Fortitude, Improved Iron Will and Improved Lightning Reflexes: once a day, a
/// failed save of that kind rolled again, the second roll standing whatever it says.
/// </summary>
/// <remarks>
/// The book lets the creature decide after seeing the failure. Nothing can be asked in the
/// middle of somebody else's spell — the scheduler never blocks on a decision — so the rules
/// decide for everyone, the way the autopilot would: the reroll is spent on a failure that
/// matters, one that would put the creature down or out of the fight, and kept for later on
/// one that would not.
/// </remarks>
public static class SaveRerolls
{
    /// <summary>The daily pool each save's reroll draws on.</summary>
    public static string PoolFor(Save save) => save switch
    {
        Save.Fortitude => "improved-great-fortitude",
        Save.Reflex => "improved-lightning-reflexes",
        _ => "improved-iron-will",
    };

    /// <summary>The feat that buys a reroll of this save.</summary>
    public static FeatEffect FeatFor(Save save) => save switch
    {
        Save.Fortitude => FeatEffect.ImprovedGreatFortitude,
        Save.Reflex => FeatEffect.ImprovedLightningReflexes,
        _ => FeatEffect.ImprovedIronWill,
    };

    /// <summary>The pool of a reroll feat, if the pool name is one: for the daily allowance.</summary>
    public static bool IsPool(string pool) =>
        pool is "improved-great-fortitude" or "improved-lightning-reflexes" or "improved-iron-will";

    /// <summary>How many rerolls of this kind a day: one with the feat, none without.</summary>
    public static int PerDay(Creature creature, string pool)
    {
        ArgumentNullException.ThrowIfNull(creature);

        foreach (var save in SaveInfo.All)
        {
            if (PoolFor(save) == pool)
            {
                return creature.HasFeat(FeatFor(save)) ? 1 : 0;
            }
        }

        return 0;
    }

    /// <summary>Whether a reroll of this save is still to hand today.</summary>
    public static bool CanReroll(Creature creature, Save save)
    {
        ArgumentNullException.ThrowIfNull(creature);

        return creature.HasFeat(FeatFor(save))
            && creature.DailyUses.SpentFrom(PoolFor(save)) < PerDay(creature, PoolFor(save));
    }

    /// <summary>
    /// Rolls a save, and if it fails and the failure is <paramref name="serious"/>, spends the
    /// day's reroll on a second roll — the one that stands.
    /// </summary>
    public static SavingThrowResult Attempt(
        Creature target,
        Save save,
        int difficultyClass,
        IRandomSource random,
        RuleOptions? rules = null,
        IEnumerable<Modifier>? situational = null,
        bool serious = true)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(random);

        var first = target.Saves.Attempt(save, difficultyClass, random, rules, situational);
        if (first.Succeeded || !serious || !CanReroll(target, save))
        {
            return first;
        }

        target.DailyUses.Spend(PoolFor(save), 1);
        return target.Saves.Attempt(save, difficultyClass, random, rules, situational) with { FirstTry = first };
    }
}
