using Ironbound.Rules.Combat;
using Ironbound.Rules.Dice;

namespace Ironbound.Rules.Effects;

public enum EffectKind
{
    /// <summary>Grants modifiers and takes them back. Buffs, debuffs, ability damage.</summary>
    Modifier,

    DamageOverTime,
    FastHealing,
    Regeneration,
}

/// <summary>
/// A description of an effect, as opposed to a running one.
/// </summary>
/// <remarks>
/// This is what lets a spell be written down. A spell that hands out a blessing used to hold a
/// <c>Func&lt;int, Effect&gt;</c> — perfectly readable in C# and impossible to put in a content
/// file, which made it the last thing standing between spells and data.
/// <para>
/// Kept separate from <c>SavedEffect</c> on purpose: this describes a kind of effect, that one
/// captures a particular running instance with its clocks part-spent.
/// </para>
/// </remarks>
public sealed record EffectDefinition
{
    public required string Name { get; init; }

    public EffectKind Kind { get; init; } = EffectKind.Modifier;

    /// <summary>Ignores the two tick fields entirely.</summary>
    public bool Permanent { get; init; }

    /// <summary>A flat span, before any per-level part.</summary>
    public int DurationTicks { get; init; }

    /// <summary>Added once per caster level — how "one minute per level" is expressed.</summary>
    public int TicksPerLevel { get; init; }

    /// <summary>How often it fires. Zero leaves the effect's own default alone.</summary>
    public int PeriodTicks { get; init; }

    public IReadOnlyList<ModifierGrant> Grants { get; init; } = [];

    /// <summary>Dice for the kinds that roll something.</summary>
    public string? Amount { get; init; }

    public DamageType DamageType { get; init; } = DamageType.Untyped;

    /// <summary>Points per tick, for the healing kinds.</summary>
    public int Heal { get; init; }

    public IReadOnlyList<DamageType> SuspendedBy { get; init; } = [];

    public Duration DurationFor(int casterLevel) => Permanent
        ? Duration.Permanent
        : Duration.FromTicks(DurationTicks + (TicksPerLevel * Math.Max(0, casterLevel)));

    /// <summary>Mints a fresh effect. Each target needs its own, with its own clock.</summary>
    public Effect Build(int casterLevel)
    {
        var duration = DurationFor(casterLevel);
        Duration? period = PeriodTicks > 0 ? Duration.FromTicks(PeriodTicks) : null;

        switch (Kind)
        {
            case EffectKind.DamageOverTime:
                return new DamageOverTimeEffect(
                    Name, duration, DiceExpression.Parse(Amount ?? "0"), DamageType, period);

            case EffectKind.FastHealing:
                return new FastHealingEffect(Name, duration, Math.Max(1, Heal), period);

            case EffectKind.Regeneration:
                return new RegenerationEffect(
                    Name, duration, Math.Max(1, Heal), [.. SuspendedBy]);

            default:
                var effect = new ModifierEffect(Name, duration);
                foreach (var grant in Grants)
                {
                    effect.Grants(grant.Value, grant.Type, grant.Target);
                }

                return effect;
        }
    }
}
