using Ironbound.Rules.Combat;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Feats;
using Ironbound.Rules.Maps;

namespace Ironbound.Rules.Encounters;

/// <summary>
/// What feats do to getting about and to reaching things: easy ground, a lunge's extra five
/// feet, the speed Fleet adds.
/// </summary>
public static class Movement
{
    /// <summary>What Nimble Moves lets a creature cross as though it were clear, each round.</summary>
    public const int NimbleFeet = 5;

    /// <summary>What Acrobatic Steps does instead.</summary>
    public const int AcrobaticFeet = 20;

    /// <summary>Fleet's five feet, each time it is taken.</summary>
    public const int FleetFeet = 5;

    /// <summary>What a lunge adds to reach.</summary>
    public const int LungeFeet = 5;

    /// <summary>The difficult ground a creature can walk as clear each round.</summary>
    public static int EasyGroundFeet(Creature creature)
    {
        ArgumentNullException.ThrowIfNull(creature);

        return creature.HasFeat(FeatEffect.AcrobaticSteps) ? AcrobaticFeet
            : creature.HasFeat(FeatEffect.NimbleMoves) ? NimbleFeet
            : 0;
    }

    /// <summary>
    /// Whether an attacker can strike a target with a weapon this turn: within its reach, or
    /// within five feet more of it while it is lunging.
    /// </summary>
    public static bool Reaches(ActionContext context, Creature attacker, Creature target, WeaponAttack? weapon)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(attacker);
        ArgumentNullException.ThrowIfNull(target);

        if (context.Encounter.Battlefield is not { } field)
        {
            return true;
        }

        if (field.IsWithinReach(attacker, target, weapon))
        {
            return true;
        }

        if (!ReferenceEquals(attacker, context.Actor) || !context.Combatant.IsLunging
            || field.SquareOf(attacker) is not { } from || field.SquareOf(target) is not { } to)
        {
            return false;
        }

        var band = Battlefield.ReachOf(attacker, weapon);
        return !band.IsNone && (band with { Maximum = band.Maximum + LungeFeet }).Covers(from, to);
    }
}
