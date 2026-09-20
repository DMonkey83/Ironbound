using Ironbound.Rules.Creatures;
using Ironbound.Rules.Modifiers;

namespace Ironbound.Rules.Effects;

/// <summary>
/// Something happening to a creature over time: a buff, a condition, a poison, an enchantment.
/// </summary>
/// <remarks>
/// The base class owns the one invariant that matters — <b>every modifier an effect grants is
/// taken back when it ends</b>. Subclasses hand modifiers out through <see cref="Grant"/>, which
/// stamps them with the effect's own name and remembers the stack; expiry removes them. That
/// makes <see cref="Effect"/> the only thing in the codebase that should ever call
/// <see cref="ModifierStack.RemoveAllFrom"/>, and it means the source string is written once
/// rather than twice, which is what stops a mistyped name leaving a buff on forever.
/// </remarks>
public abstract class Effect
{
    private readonly List<ModifierStack> _touched = [];

    /// <param name="name">Also the modifier source, so it must read well in a log.</param>
    /// <param name="period">How often <see cref="OnTick"/> fires. Zero for a plain buff.</param>
    protected Effect(string name, Duration duration, Duration period = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (period.IsPermanent)
        {
            throw new ArgumentException("A period must be a finite span.", nameof(period));
        }

        Name = name;
        Duration = duration;
        Period = period;
        TicksRemaining = duration.IsPermanent ? 0 : duration.Ticks;
        TicksUntilPeriod = period.Ticks;
    }

    public string Name { get; }

    public Duration Duration { get; }

    /// <summary>Zero for an effect that only grants modifiers and waits.</summary>
    public Duration Period { get; }

    public bool IsPeriodic => Period.Ticks > 0;

    /// <summary>Meaningless while <see cref="Duration"/> is permanent.</summary>
    public int TicksRemaining { get; private set; }

    internal int TicksUntilPeriod { get; private set; }

    public Duration Remaining => Duration.IsPermanent ? Duration.Permanent : Duration.FromTicks(Math.Max(0, TicksRemaining));

    public bool IsExpired { get; private set; }

    /// <summary>The creature it is attached to, once applied.</summary>
    public Creature? Target { get; private set; }

    protected virtual void OnApply(Creature target)
    {
    }

    /// <summary>Fires once per <see cref="Period"/>. Only called when <see cref="IsPeriodic"/>.</summary>
    protected virtual void OnTick(EffectContext context)
    {
    }

    /// <summary>Runs before granted modifiers are stripped, so state is still readable.</summary>
    protected virtual void OnExpire(Creature target)
    {
    }

    /// <summary>
    /// Hands out a modifier stamped with this effect's name, and remembers where it went.
    /// The only supported way for an effect to change a number.
    /// </summary>
    protected void Grant(ModifierStack stack, int value, BonusType type)
    {
        ArgumentNullException.ThrowIfNull(stack);

        stack.Add(new Modifier(value, type, Name));
        if (!_touched.Contains(stack))
        {
            _touched.Add(stack);
        }
    }

    internal void ApplyTo(Creature target)
    {
        Target = target;
        OnApply(target);
    }

    internal void Tick(EffectContext context) => OnTick(context);

    internal void EndOn(Creature target)
    {
        OnExpire(target);

        foreach (var stack in _touched)
        {
            stack.RemoveAllFrom(Name);
        }

        _touched.Clear();
        IsExpired = true;
    }

    internal bool HasRunOut => !Duration.IsPermanent && TicksRemaining <= 0;

    internal bool ShouldFire => IsPeriodic && TicksUntilPeriod <= 0;

    /// <summary>How far time may jump before this effect needs attention.</summary>
    internal int TicksUntilNextEvent()
    {
        var next = int.MaxValue;

        if (!Duration.IsPermanent)
        {
            next = Math.Min(next, Math.Max(1, TicksRemaining));
        }

        if (IsPeriodic)
        {
            next = Math.Min(next, Math.Max(1, TicksUntilPeriod));
        }

        return next;
    }

    internal void Advance(int ticks)
    {
        if (!Duration.IsPermanent)
        {
            TicksRemaining -= ticks;
        }

        if (IsPeriodic)
        {
            TicksUntilPeriod -= ticks;
        }
    }

    internal void ResetPeriod() => TicksUntilPeriod += Period.Ticks;

    public override string ToString() =>
        Duration.IsPermanent ? Name : $"{Name} ({Remaining} left)";
}
