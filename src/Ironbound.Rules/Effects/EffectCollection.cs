using Ironbound.Rules.Creatures;
using Ironbound.Rules.Dice;

namespace Ironbound.Rules.Effects;

/// <summary>
/// The effects currently on one creature, and the thing that moves them through time.
/// </summary>
/// <remarks>
/// Names are unique here: applying an effect whose name is already present replaces it, which is
/// the refresh behaviour a player expects from recasting a spell, and it keeps the modifier
/// source unambiguous. Two effects that genuinely coexist — Bull's Strength and a Belt of Giant
/// Strength — have different names and are sorted out by the bonus-type stacking rules instead.
/// </remarks>
public sealed class EffectCollection
{
    private readonly Creature _owner;
    private readonly List<Effect> _effects = [];

    internal EffectCollection(Creature owner) => _owner = owner;

    /// <summary>In the order they were applied, which is also the order they resolve in.</summary>
    public IReadOnlyList<Effect> Active => _effects;

    public int Count => _effects.Count;

    /// <summary>The regeneration in force, if any. The damage pipeline asks before applying a blow.</summary>
    public RegenerationEffect? Regeneration =>
        _effects.OfType<RegenerationEffect>().FirstOrDefault();

    public bool Has(string name) => Find(name) is not null;

    public Effect? Find(string name) =>
        _effects.FirstOrDefault(e => string.Equals(e.Name, name, StringComparison.Ordinal));

    /// <summary>Applies an effect, replacing any of the same name.</summary>
    public EffectEvent Apply(Effect effect)
    {
        ArgumentNullException.ThrowIfNull(effect);

        Remove(effect.Name);
        _effects.Add(effect);
        effect.ApplyTo(_owner);

        return new EffectEvent(
            EffectEventKind.Applied,
            effect,
            $"{_owner.Name} gains {effect.Name} ({effect.Duration})");
    }

    /// <summary>Takes an effect away early — dispelled, suppressed, or its source destroyed.</summary>
    public EffectEvent? Remove(string name)
    {
        if (Find(name) is not { } effect)
        {
            return null;
        }

        _effects.Remove(effect);
        effect.EndOn(_owner);

        return new EffectEvent(EffectEventKind.Removed, effect, $"{_owner.Name} loses {effect.Name}");
    }

    public void Clear()
    {
        foreach (var effect in _effects.ToArray())
        {
            Remove(effect.Name);
        }
    }

    /// <summary>
    /// Moves every effect forward. Jumps from one event boundary to the next rather than
    /// stepping tick by tick, so an eight-hour rest costs a handful of iterations instead of
    /// 288,000 — while still firing every periodic exactly as often as it should.
    /// </summary>
    public IReadOnlyList<EffectEvent> Advance(Duration elapsed, IRandomSource random)
    {
        ArgumentNullException.ThrowIfNull(random);
        if (elapsed.IsPermanent)
        {
            throw new ArgumentException("Time cannot advance by a permanent duration.", nameof(elapsed));
        }

        var events = new List<EffectEvent>();
        var remaining = elapsed.Ticks;

        while (remaining > 0 && _effects.Count > 0)
        {
            var step = remaining;
            foreach (var effect in _effects)
            {
                step = Math.Min(step, effect.TicksUntilNextEvent());
            }

            step = Math.Clamp(step, 1, remaining);

            foreach (var effect in _effects)
            {
                effect.Advance(step);
            }

            // Periodics fire before expiries, so an effect that ticks on the very tick it ends
            // still gets that last tick.
            foreach (var effect in _effects.ToArray())
            {
                while (effect.ShouldFire)
                {
                    effect.ResetPeriod();

                    var context = new EffectContext(_owner, random, effect.Period);
                    effect.Tick(context);

                    foreach (var report in context.Reports)
                    {
                        events.Add(new EffectEvent(EffectEventKind.Ticked, effect, report));
                    }
                }
            }

            foreach (var effect in _effects.ToArray())
            {
                if (!effect.HasRunOut)
                {
                    continue;
                }

                _effects.Remove(effect);
                effect.EndOn(_owner);
                events.Add(new EffectEvent(
                    EffectEventKind.Expired,
                    effect,
                    $"{_owner.Name}: {effect.Name} ends"));
            }

            remaining -= step;
        }

        return events;
    }

    public override string ToString() =>
        _effects.Count == 0 ? "no effects" : string.Join(", ", _effects);
}
