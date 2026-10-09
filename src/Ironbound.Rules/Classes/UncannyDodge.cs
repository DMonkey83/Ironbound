using Ironbound.Rules.Creatures;

namespace Ironbound.Rules.Classes;

/// <summary>
/// A rogue's or barbarian's sense for danger: never caught flat-footed, and — improved — never
/// really flanked.
/// </summary>
/// <remarks>
/// Uncanny dodge removes only the <em>flat-footed</em> reasons for losing Dexterity: not having
/// acted yet, being surprised, an invisible attacker. Being stunned, asleep or blinded is a
/// condition rather than being caught unawares, and the rule leaves those alone.
/// </remarks>
public static class UncannyDodge
{
    /// <summary>How many more levels a rogue needs than the defender to flank her anyway.</summary>
    public const int FlankingMargin = 4;

    /// <summary>
    /// Whether the creature has it at all — from any class that grants it.
    /// </summary>
    public static bool Has(Creature creature) => ClassFeatures.Has(creature, FeatureIds.UncannyDodge);

    /// <summary>
    /// Whether the creature has the improved version: the feature itself, or uncanny dodge from
    /// two different classes, which the rule turns into improved for free.
    /// </summary>
    public static bool HasImproved(Creature creature)
    {
        ArgumentNullException.ThrowIfNull(creature);

        if (ClassFeatures.Has(creature, FeatureIds.ImprovedUncannyDodge))
        {
            return true;
        }

        return creature.Levels.Count(taken =>
            taken.Class.FeaturesAt(taken.Level).Any(feature => feature.Id == FeatureIds.UncannyDodge)) >= 2;
    }

    /// <summary>
    /// The levels counted against a would-be flanker: every level in a class that grants uncanny
    /// dodge, added up, because the rule stacks them for exactly this.
    /// </summary>
    public static int Levels(Creature creature) => ClassFeatures.LevelOf(creature, FeatureIds.UncannyDodge);

    /// <summary>
    /// Whether an attacker can flank this defender at all. Only a rogue with four more levels
    /// than the defender's uncanny dodge levels gets past improved uncanny dodge.
    /// </summary>
    public static bool CanBeFlankedBy(Creature defender, Creature attacker)
    {
        ArgumentNullException.ThrowIfNull(defender);
        ArgumentNullException.ThrowIfNull(attacker);

        if (!HasImproved(defender))
        {
            return true;
        }

        // "A rogue" is read as levels in whatever classes grant sneak attack, which is what
        // the rule is protecting against and keeps the class's name out of the code.
        return ClassFeatures.LevelOf(attacker, FeatureIds.SneakAttack) >= Levels(defender) + FlankingMargin;
    }
}
