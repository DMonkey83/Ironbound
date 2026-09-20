namespace Ironbound.Rules.Effects;

/// <summary>
/// Hit points back on a clock. Unlike regeneration, it does not change what damage does — it
/// simply heals, and stops being useful once the creature is dead.
/// </summary>
public sealed class FastHealingEffect : Effect
{
    public FastHealingEffect(string name, Duration duration, int amount, Duration? period = null)
        : base(name, duration, period ?? Duration.Rounds(1))
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(amount, 1);
        Amount = amount;
    }

    public int Amount { get; }

    protected override void OnTick(EffectContext context)
    {
        var healed = context.Target.HitPoints.Heal(Amount);
        if (healed > 0)
        {
            context.Report($"{context.Target.Name} heals {healed} from {Name} ({context.Target.HitPoints})");
        }
    }
}
