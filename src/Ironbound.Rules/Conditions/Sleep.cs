using Ironbound.Rules.Creatures;
using Ironbound.Rules.Effects;

namespace Ironbound.Rules.Conditions;

/// <summary>
/// Putting a creature to sleep, and waking it again when somebody is careless enough to hurt it.
/// </summary>
/// <remarks>
/// Sleep is an ordinary condition riding on an ordinary effect, so the clock, the save file and
/// the "nothing to decide this turn" check all come for free. The one thing an effect cannot do
/// by itself is notice a blow, which is what <see cref="Sync"/> is for.
/// </remarks>
public static class Sleep
{
    /// <summary>
    /// How long a sleeper lies there if nobody disturbs it: two full rounds.
    /// </summary>
    /// <remarks>
    /// Long enough for every hero to have had two turns, which is how the adventure this was
    /// written for put it. A fight that opens on the stroke of the first round therefore wakes
    /// its sleepers just as the third begins, before anyone has taken a turn in it.
    /// </remarks>
    public static Duration Undisturbed { get; } = Duration.Rounds(2);

    /// <summary>Sends somebody to sleep for as long as they are left alone.</summary>
    public static EffectEvent Fall(Creature creature, Duration? duration = null)
    {
        ArgumentNullException.ThrowIfNull(creature);

        return creature.Effects.Apply(
            ConditionInfo.Effect(Condition.Asleep, duration ?? Undisturbed));
    }

    /// <summary>
    /// Wakes anybody who is asleep and hurt, and says so. Null when there was nothing to do.
    /// </summary>
    /// <remarks>
    /// Called as time moves, beside the check for bleeding and for the same reason: damage
    /// arrives from swords, arrows, spells and falling, and none of them should have to know
    /// that somebody might be napping. The cost is that a sleeper stays asleep until the turn
    /// that wounded it ends, so a second blow in the same turn still finds it defenceless.
    /// <para>
    /// "Hurt" means carrying any damage at all rather than "more than when it dozed off", which
    /// would need the sleep to remember a number and the save file to keep it. Nothing in the
    /// game puts the already wounded to sleep, and a creature in pain is a poor sleeper anyway.
    /// </para>
    /// </remarks>
    public static EffectEvent? Sync(Creature creature)
    {
        ArgumentNullException.ThrowIfNull(creature);

        if (!creature.Has(Condition.Asleep)
            || (creature.HitPoints.Damage == 0 && creature.HitPoints.Nonlethal == 0))
        {
            return null;
        }

        return creature.Effects.Remove(Condition.Asleep) is { } woken
            ? woken with { Description = $"{creature.Name} wakes with a start" }
            : null;
    }
}
