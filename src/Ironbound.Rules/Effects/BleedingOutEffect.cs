using Ironbound.Rules.Abilities;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Dice;

namespace Ironbound.Rules.Effects;

/// <summary>
/// What happens to somebody lying on the floor below nought hit points.
/// </summary>
/// <remarks>
/// A point a round, and a Constitution check each time to stop. Modelled as an effect rather
/// than as a special case in the scheduler because that is exactly what it is — something with
/// a clock, a period, and a report to make — and because the log, the save format and the
/// character sheet then handle it without knowing it exists.
/// <para>
/// Until this, <see cref="HitPointState.Dying"/> was a word the rules used and nothing acted
/// on: a creature at minus three lay there indefinitely and recovered at the next rest. The
/// death's-door band only means something if the door swings.
/// </para>
/// </remarks>
public sealed class BleedingOutEffect : Effect
{
    /// <summary>The name it is filed under, so the same creature never grows two of them.</summary>
    public const string Label = "Bleeding out";

    /// <summary>The check to stop, before the wound itself makes it harder.</summary>
    public const int StabiliseDC = 10;

    public BleedingOutEffect()
        : base(Label, Duration.Permanent, Duration.Rounds(1))
    {
    }

    /// <summary>Whether the bleeding has stopped without the wound closing.</summary>
    public bool IsStable { get; private set; }

    /// <summary>Stops the bleeding — a successful check, or somebody's hands on the wound.</summary>
    public void Stabilise() => IsStable = true;

    /// <summary>Starts it again, as a fresh injury does.</summary>
    public void Reopen() => IsStable = false;

    protected override void OnTick(EffectContext context)
    {
        var target = context.Target;

        // Nothing to say once the bleeding has stopped. It keeps ticking so that a fresh wound
        // can start it again, but a line a round saying somebody is still lying there quietly
        // would bury the fight in the log.
        if (IsStable)
        {
            return;
        }

        target.HitPoints.Take(1);

        if (!target.IsAlive)
        {
            context.Report($"{target.Name} bleeds out and dies");
            return;
        }

        // The check gets harder the further under you are, which is why somebody at minus one
        // usually pulls through and somebody at minus eight usually does not.
        // A Constitution check, and so whatever reaches every ability check reaches it too.
        var roll = context.Random.NextDie(20);
        var constitution = target.AbilityCheck(Ability.Constitution).Total;
        var total = roll + constitution + target.HitPoints.Current;

        if (total >= StabiliseDC)
        {
            IsStable = true;
            context.Report(
                $"{target.Name} bleeds to {target.HitPoints.Current} and stabilises "
                + $"(d20 [{roll}] {constitution:+0;-0;+0} {target.HitPoints.Current:+0;-0;+0} "
                + $"= {total} vs DC {StabiliseDC})");

            return;
        }

        context.Report(
            $"{target.Name} bleeds to {target.HitPoints.Current} "
            + $"(d20 [{roll}] {constitution:+0;-0;+0} {target.HitPoints.Current:+0;-0;+0} "
            + $"= {total} vs DC {StabiliseDC} — still bleeding)");
    }
}

/// <summary>
/// Keeps the bleeding in step with the wound.
/// </summary>
/// <remarks>
/// One place that decides who is bleeding, called wherever time moves. Damage arrives from
/// strikes, spells, poison and falling, and none of those should have to remember to check.
/// </remarks>
public static class Bleeding
{
    /// <summary>Attaches or removes the bleed so it matches the creature's state.</summary>
    public static void Sync(Creature creature)
    {
        ArgumentNullException.ThrowIfNull(creature);

        var bleeding = Find(creature);

        if (creature.HitPoints.State != HitPointState.Dying)
        {
            // Back above nought, or gone. Either way it is no longer bleeding out.
            if (bleeding is not null)
            {
                creature.Effects.Remove(BleedingOutEffect.Label);
            }

            return;
        }

        if (bleeding is null)
        {
            bleeding = new BleedingOutEffect();
            creature.Effects.Apply(bleeding);
        }

        // Diehard: stable the moment the hit points go under, with no check to make. A fresh
        // wound does not reopen it either; only death ends it.
        if (creature.HasFeat(Feats.FeatEffect.Diehard))
        {
            bleeding.Stabilise();
        }
    }

    public static BleedingOutEffect? Find(Creature creature)
    {
        ArgumentNullException.ThrowIfNull(creature);

        return creature.Effects.Find(BleedingOutEffect.Label) as BleedingOutEffect;
    }

    /// <summary>Whether somebody on the floor has stopped losing blood.</summary>
    public static bool IsStable(Creature creature) => Find(creature)?.IsStable ?? false;

    /// <summary>
    /// Healed while dying: any healing at all stops the bleeding, even when it leaves them below
    /// nought. That is the rule, and it is the whole point of a power like Rebuke Death, which
    /// at first level rarely heals enough to lift anybody back to their feet.
    /// </summary>
    public static void Healed(Creature creature)
    {
        ArgumentNullException.ThrowIfNull(creature);

        Sync(creature);
        Find(creature)?.Stabilise();
    }
}
