using Ironbound.Rules.Combat;
using Ironbound.Rules.Defense;
using Ironbound.Rules.Dice;

namespace Ironbound.Rules.Effects;

/// <summary>
/// Damage on a clock: bleed, burning, a poison's ongoing harm. Runs through the target's
/// defences like any other damage, so resistance and immunity apply.
/// </summary>
public sealed class DamageOverTimeEffect : Effect
{
    private readonly DamagePacket _packet;

    public DamageOverTimeEffect(
        string name,
        Duration duration,
        DiceExpression amount,
        DamageType type,
        Duration? period = null)
        : base(name, duration, period ?? Duration.Rounds(1))
    {
        ArgumentNullException.ThrowIfNull(amount);
        Amount = amount;
        DamageType = type;
        _packet = DamagePacket.Of(DamageComponent.Weapon(amount, type));
    }

    public DamageOverTimeEffect(
        string name,
        Duration duration,
        string amount,
        DamageType type,
        Duration? period = null)
        : this(name, duration, DiceExpression.Parse(amount), type, period)
    {
    }

    /// <summary>How much it deals each time, kept so the effect can be written to a save.</summary>
    public DiceExpression Amount { get; }

    public DamageType DamageType { get; }

    protected override void OnTick(EffectContext context)
    {
        var rolled = _packet.Roll(context.Random);
        var taken = context.Target.Defenses.Apply(rolled, DamageBypass.None, context.Target.Rules);
        var applied = context.Target.HitPoints.Take(taken.Total);

        context.Report(applied.Total == 0
            ? $"{context.Target.Name} shrugs off {Name} ({taken})"
            : $"{context.Target.Name} takes {applied.Total} from {Name} ({taken})");
    }
}
