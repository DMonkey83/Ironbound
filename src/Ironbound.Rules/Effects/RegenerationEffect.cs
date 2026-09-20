using Ironbound.Rules.Combat;

namespace Ironbound.Rules.Effects;

/// <summary>
/// Wounds that close on their own. Unlike fast healing, regeneration changes what damage
/// <em>does</em>: everything except a few named types lands as nonlethal, so the creature drops
/// but does not die. Damage of a suspending type is lethal and stops the healing for a round —
/// which is why a troll needs fire, and why hacking at one achieves nothing.
/// </summary>
public sealed class RegenerationEffect : Effect
{
    private readonly HashSet<DamageType> _suspendedBy;

    public RegenerationEffect(string name, Duration duration, int amount, params DamageType[] suspendedBy)
        : base(name, duration, Duration.Rounds(1))
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(amount, 1);
        ArgumentNullException.ThrowIfNull(suspendedBy);

        Amount = amount;
        _suspendedBy = [.. suspendedBy];
    }

    public int Amount { get; }

    public IReadOnlyCollection<DamageType> SuspendedBy => _suspendedBy;

    /// <summary>True for the round after taking damage of a suspending type.</summary>
    public bool IsSuspended { get; private set; }

    public bool IsSuspendedBy(DamageType type) => _suspendedBy.Contains(type);

    /// <summary>Stops the healing until the next tick. Called by the damage pipeline.</summary>
    internal void Suspend() => IsSuspended = true;

    protected override void OnTick(EffectContext context)
    {
        if (IsSuspended)
        {
            IsSuspended = false;
            context.Report($"{context.Target.Name}: {Name} is suspended this round");
            return;
        }

        // Nonlethal first — it is the damage regeneration is designed to undo.
        var nonlethal = context.Target.HitPoints.HealNonlethal(Amount);
        var lethal = context.Target.HitPoints.Heal(Amount - nonlethal);

        if (nonlethal + lethal > 0)
        {
            context.Report($"{context.Target.Name} regenerates {nonlethal + lethal} ({context.Target.HitPoints})");
        }
    }
}
