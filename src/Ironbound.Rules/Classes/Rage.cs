using Ironbound.Rules.Abilities;
using Ironbound.Rules.Conditions;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Effects;
using Ironbound.Rules.Skills;

namespace Ironbound.Rules.Classes;

/// <summary>
/// A barbarian's rage: how long it can last today, starting and stopping it, and the price.
/// </summary>
/// <remarks>
/// The allowance is counted in rounds rather than rages, which is the Pathfinder rule and the
/// one that makes it a decision: every round spent raging at a goblin is a round not spent on
/// the ogre. Rounds are only ever charged when a rage ends, from how long it actually ran, so
/// nothing here has to tick.
/// </remarks>
public static class Rage
{
    /// <summary>The daily pool it draws on, in rounds.</summary>
    public const string Pool = "rage";

    /// <summary>The name the fatigue afterwards is filed under.</summary>
    public const string FatigueLabel = "Fatigued";

    /// <summary>
    /// Four, plus Constitution, plus two for every barbarian level past the first.
    /// </summary>
    /// <remarks>
    /// The Constitution is the score as it stands without anything temporary on it — rage's
    /// own +4 above all — because the rule says temporary increases do not lengthen the day,
    /// and a rage that paid for itself would be a rage that never ended.
    /// </remarks>
    public static int RoundsPerDay(Creature creature)
    {
        ArgumentNullException.ThrowIfNull(creature);

        if (!ClassFeatures.Has(creature, FeatureIds.Rage))
        {
            return 0;
        }

        var constitution = creature.Abilities[Ability.Constitution];
        var modifier = constitution.HasScore ? AbilityScore.ModifierFor(constitution.Base) : 0;
        var level = ClassFeatures.LevelOf(creature, FeatureIds.Rage);

        return Math.Max(0, 4 + modifier + (2 * (level - 1)));
    }

    /// <summary>What is left of today's rounds, counting the one a running rage is in.</summary>
    public static int RoundsLeft(Creature creature)
    {
        ArgumentNullException.ThrowIfNull(creature);

        var running = Current(creature)?.RoundsSoFar ?? 0;
        return Math.Max(0, RoundsPerDay(creature) - creature.DailyUses.SpentFrom(Pool) - running);
    }

    public static RageEffect? Current(Creature creature)
    {
        ArgumentNullException.ThrowIfNull(creature);
        return creature.Effects.Find(RageEffect.Label) as RageEffect;
    }

    public static bool IsRaging(Creature creature) => Current(creature) is not null;

    /// <summary>
    /// Whether a rage could begin now: the class, a round left, awake, and not still worn out
    /// from the last one.
    /// </summary>
    public static bool CanStart(Creature creature)
    {
        ArgumentNullException.ThrowIfNull(creature);

        return creature.IsConscious
            && !IsRaging(creature)
            && !creature.Has(Condition.Fatigued)
            && RoundsLeft(creature) > 0;
    }

    /// <summary>Begins a rage, or returns null when one cannot begin.</summary>
    public static EffectEvent? Start(Creature creature)
    {
        if (!CanStart(creature))
        {
            return null;
        }

        // A fresh rage, so every once-a-rage power is there to be used again.
        creature.Stances.BeginRage();

        return creature.Effects.Apply(new RageEffect(RoundsLeft(creature)));
    }

    /// <summary>Ends a rage by choice — or because its owner can no longer hold it.</summary>
    public static EffectEvent? End(Creature creature)
    {
        ArgumentNullException.ThrowIfNull(creature);
        return creature.Effects.Remove(RageEffect.Label);
    }

    /// <summary>
    /// Ends the rage of anybody who has dropped. Called as time moves, beside bleeding and
    /// sleep, for the same reason: nothing that knocks somebody out should have to know.
    /// </summary>
    public static EffectEvent? Sync(Creature creature)
    {
        ArgumentNullException.ThrowIfNull(creature);

        return IsRaging(creature) && !creature.IsConscious ? End(creature) : null;
    }

    /// <summary>
    /// What ending costs: the rounds against the day, the once-a-rage powers put away, and
    /// twice as many rounds of fatigue as the rage lasted.
    /// </summary>
    internal static void Ended(Creature creature, int rounds)
    {
        creature.DailyUses.Spend(Pool, rounds);
        creature.Stances.EndRage();

        if (creature.IsAlive)
        {
            creature.Effects.Apply(ConditionInfo.Effect(Condition.Fatigued, Duration.Rounds(2 * rounds)));
        }
    }

    /// <summary>
    /// Whether rage forbids this skill: anything run on Charisma, Dexterity or Intelligence
    /// that takes patience, which is all of them but the four the rule names.
    /// </summary>
    public static bool Forbids(Skill skill) =>
        SkillInfo.AbilityFor(skill) is Ability.Charisma or Ability.Dexterity or Ability.Intelligence
        && skill is not (Skill.Acrobatics or Skill.Intimidate);
}
