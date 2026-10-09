using Ironbound.Rules.Conditions;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Dice;
using Ironbound.Rules.Items;
using Ironbound.Rules.Saves;

namespace Ironbound.Rules.Classes;

/// <summary>
/// The ways a rogue — and a barbarian, for some of them — stays alive: evasion, trap sense,
/// trapfinding, and the once-a-day escapes from a blow that should have dropped her.
/// </summary>
public static class RogueDefences
{
    /// <summary>
    /// Levels in whatever classes grant sneak attack: what "rogue level" means to a talent.
    /// </summary>
    public static int RogueLevel(Creature creature) => ClassFeatures.LevelOf(creature, FeatureIds.SneakAttack);

    /// <summary>
    /// Whether evasion can work right now: light armour or none, and not lying helpless. Asleep
    /// is the engine's nearest thing to helpless.
    /// </summary>
    private static bool Unencumbered(Creature creature) =>
        creature.Equipment.ArmourWorn is ArmourCategory.None or ArmourCategory.Light
        && !creature.Has(Condition.Asleep);

    public static bool HasEvasion(Creature creature)
    {
        ArgumentNullException.ThrowIfNull(creature);

        return (ClassFeatures.Has(creature, FeatureIds.Evasion)
                || creature.Choices.HasTalent(TalentEffect.ImprovedEvasion))
            && Unencumbered(creature);
    }

    public static bool HasImprovedEvasion(Creature creature)
    {
        ArgumentNullException.ThrowIfNull(creature);

        return creature.Choices.HasTalent(TalentEffect.ImprovedEvasion) && Unencumbered(creature);
    }

    /// <summary>
    /// What is left of a half-on-a-Reflex-save effect after evasion: nothing on a success, and
    /// with improved evasion half even on a failure. Anything else passes through untouched.
    /// </summary>
    /// <param name="rolled">The full amount, before any halving.</param>
    public static int Evade(Creature target, SavingThrowResult save, int rolled)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(save);

        if (save.Save != Save.Reflex)
        {
            return save.Succeeded ? rolled / 2 : rolled;
        }

        if (save.Succeeded)
        {
            return HasEvasion(target) ? 0 : rolled / 2;
        }

        return HasImprovedEvasion(target) ? rolled / 2 : rolled;
    }

    /// <summary>
    /// Half the rogue's level, at least one, on Disable Device — and on Perception to find a
    /// trap, which the game has none of yet.
    /// </summary>
    public static int Trapfinding(Creature creature) =>
        ClassFeatures.Has(creature, FeatureIds.Trapfinding)
            ? ClassFeatures.HalfMinOne(ClassFeatures.LevelOf(creature, FeatureIds.Trapfinding))
            : 0;

    /// <summary>
    /// Reflex and dodge armour class against traps: one at third level, one more every three.
    /// Recorded and shown; there are no traps for it to apply to.
    /// </summary>
    public static int TrapSense(Creature creature) => ClassFeatures.Rank(creature, FeatureIds.TrapSense);

    /// <summary>
    /// Resiliency, as somebody is about to drop below nought: once a day, temporary hit points
    /// equal to the rogue's level. Returns how many, and spends the day's use; nought otherwise.
    /// </summary>
    /// <remarks>
    /// Granted a moment before the blow lands rather than a moment after, which comes to the same
    /// thing: the rule's temporary hit points count towards the total she has just dropped
    /// below, and these are taken off the blow that dropped her. The minute they last is not
    /// kept — temporary hit points here last until they are spent.
    /// </remarks>
    internal static int Resiliency(Creature creature)
    {
        if (!creature.Choices.HasTalent(TalentEffect.Resiliency)
            || ClassPowers.Left(creature, ClassPowers.ResiliencyPool) <= 0)
        {
            return 0;
        }

        creature.DailyUses.Spend(ClassPowers.ResiliencyPool);
        return Math.Max(1, RogueLevel(creature));
    }

    /// <summary>
    /// Defensive roll against a blow that would drop her: once a day, a Reflex save with the
    /// damage as its difficulty, and half the damage on a success. Not while she cannot react.
    /// </summary>
    /// <returns>The damage she actually takes, and the save if one was rolled.</returns>
    public static (int Damage, SavingThrowResult? Save) DefensiveRoll(
        Creature target, int damage, bool dexterityDenied, IRandomSource random, RuleOptions rules)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(random);

        var dropping = target.HitPoints.Current - Math.Max(0, damage - target.HitPoints.Temporary) <= 0;

        if (!dropping
            || dexterityDenied
            || !target.IsConscious
            || !target.Choices.HasTalent(TalentEffect.DefensiveRoll)
            || ClassPowers.Left(target, ClassPowers.DefensiveRollPool) <= 0)
        {
            return (damage, null);
        }

        target.DailyUses.Spend(ClassPowers.DefensiveRollPool);
        var save = target.Saves.Attempt(Save.Reflex, damage, random, rules);

        return (save.Succeeded ? damage / 2 : damage, save);
    }
}
