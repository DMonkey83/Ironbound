using Ironbound.Rules.Abilities;
using Ironbound.Rules.Dice;

namespace Ironbound.Rules.Creatures;

/// <summary>How close a creature is to the end of it.</summary>
public enum HitPointState
{
    Healthy,

    /// <summary>Exactly 0: conscious but barely, and one exertion away from dying.</summary>
    Disabled,

    /// <summary>Below 0 and losing blood. Unconscious, and needs stabilising.</summary>
    Dying,

    Dead,
}

/// <summary>How a single packet of damage was absorbed.</summary>
public readonly record struct DamageApplication(int ToTemporary, int ToHitPoints)
{
    public int Total => ToTemporary + ToHitPoints;
}

/// <summary>
/// A creature's hit points.
/// </summary>
/// <remarks>
/// Stores <see cref="Damage"/> taken rather than a current total, because Constitution is not
/// fixed: bear's endurance raises the maximum, ability drain lowers it. Deriving
/// <see cref="Current"/> means a Constitution swing moves the maximum and the current together,
/// correctly and for free — a stored current would need clamping and guesswork on every change,
/// and would quietly lose hit points every time a buff came and went.
/// </remarks>
public sealed class HitPoints
{
    private int _base;

    /// <param name="baseHitPoints">Total from hit dice, before Constitution.</param>
    /// <param name="hitDice">Number of hit dice, which is how often Constitution is counted.</param>
    public HitPoints(int baseHitPoints, int hitDice, AbilityScore constitution, RuleOptions? rules = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(baseHitPoints);
        ArgumentOutOfRangeException.ThrowIfLessThan(hitDice, 1);
        ArgumentNullException.ThrowIfNull(constitution);

        _base = baseHitPoints;
        HitDice = hitDice;
        Constitution = constitution;
        Rules = rules ?? RuleOptions.Pathfinder;
    }

    /// <summary>
    /// Rolls the hit points for a creature under the configured
    /// <see cref="RuleOptions.HitPointGeneration"/>. The first hit die is always maximum, as
    /// characters are built everywhere that matters.
    /// </summary>
    public static int RollBase(int hitDieSides, int hitDice, IRandomSource random, RuleOptions? rules = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(hitDieSides, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(hitDice, 1);
        ArgumentNullException.ThrowIfNull(random);
        rules ??= RuleOptions.Pathfinder;

        var total = hitDieSides;
        for (var die = 2; die <= hitDice; die++)
        {
            total += rules.HitPointGeneration switch
            {
                HitPointGeneration.Rolled => random.NextDie(hitDieSides),
                HitPointGeneration.Maximum => hitDieSides,
                _ => (hitDieSides / 2) + 1,
            };
        }

        return total;
    }

    public RuleOptions Rules { get; }

    public int HitDice { get; private set; }

    public AbilityScore Constitution { get; }

    /// <summary>Total from hit dice before Constitution. Moves on level-up, not in combat.</summary>
    public int Base
    {
        get => _base;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            _base = value;
        }
    }

    /// <summary>
    /// Adds a hit die and what it rolled, as gaining a level does.
    /// </summary>
    /// <remarks>
    /// Damage is untouched on purpose: a wounded character who levels up is a wounded character
    /// with a higher ceiling, not a healed one. The Constitution contribution follows the new
    /// die count by itself, because <see cref="Maximum"/> was never stored.
    /// </remarks>
    public void GainHitDie(int rolled)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(rolled);

        HitDice++;
        Base += rolled;
    }

    /// <summary>Never below 1, however ruinous the Constitution penalty.</summary>
    public int Maximum => Math.Max(1, _base + (Constitution.Modifier * HitDice));

    /// <summary>Damage taken. May exceed <see cref="Maximum"/>, which is what dying looks like.</summary>
    public int Damage { get; private set; }

    public int Current => Maximum - Damage;

    /// <summary>
    /// A separate pool, spent before real hit points, never restored by healing, and
    /// highest-wins rather than cumulative. Not a modifier: it is consumed, not applied.
    /// </summary>
    public int Temporary { get; private set; }

    /// <summary>Fatigue, subdual, a pommel to the head. Tracked apart from lethal damage.</summary>
    public int Nonlethal { get; private set; }

    /// <summary>
    /// The current total at which the creature is gone. Zero when death's door is switched off,
    /// and zero for a creature with no Constitution score — a skeleton is destroyed, not dying.
    /// </summary>
    public int DeathThreshold =>
        Rules.DeathsDoor && Constitution.Score is { } score ? -score : 0;

    public HitPointState State
    {
        get
        {
            if (Current <= DeathThreshold)
            {
                return HitPointState.Dead;
            }

            return Current switch
            {
                0 => HitPointState.Disabled,
                < 0 => HitPointState.Dying,
                _ => HitPointState.Healthy,
            };
        }
    }

    public bool IsAlive => State != HitPointState.Dead;

    /// <summary>
    /// Upright and able to act. Disabled still counts; dying does not — unless the creature has
    /// the grit to fight on below nought, which Diehard is.
    /// </summary>
    public bool IsConscious =>
        (State is HitPointState.Healthy or HitPointState.Disabled || IsFightingOn) && !IsUnconsciousFromNonlethal;

    /// <summary>
    /// Below nought and still on its feet: Diehard's choice to act as though disabled rather
    /// than fall. One action a round, and anything strenuous costs a hit point.
    /// </summary>
    /// <remarks>
    /// The book makes it a choice, taken the moment the hit points go below nought. It is always
    /// taken here: falling is the other option, and nobody who took the feat took it to fall.
    /// </remarks>
    public bool IsFightingOn => State == HitPointState.Dying && FightsOn?.Invoke() == true;

    /// <summary>Asked whether a creature below nought stays on its feet: Diehard answers.</summary>
    internal Func<bool>? FightsOn { get; init; }

    /// <summary>Nonlethal damage exactly equal to current hit points: still up, but staggered.</summary>
    public bool IsStaggeredByNonlethal => Current > 0 && Nonlethal == Current;

    /// <summary>Nonlethal damage past current hit points: out cold, but not dying.</summary>
    public bool IsUnconsciousFromNonlethal => Current > 0 && Nonlethal > Current;

    /// <summary>
    /// Asked once, as a blow is about to take a creature from nought or above to below it, for
    /// temporary hit points to put in its way. Whatever it hands back is granted before the blow
    /// lands. A rogue's resiliency is the one thing that answers.
    /// </summary>
    internal Func<int>? Dropping { get; init; }

    /// <summary>Applies damage, spending temporary hit points first.</summary>
    public DamageApplication Take(int amount)
    {
        if (amount <= 0)
        {
            return default;
        }

        if (Dropping is { } reserve
            && Current >= 0
            && Current - (amount - Math.Min(Temporary, amount)) < 0
            && reserve() is > 0 and var granted)
        {
            GrantTemporary(granted);
        }

        var absorbed = Math.Min(Temporary, amount);
        Temporary -= absorbed;
        Damage += amount - absorbed;

        return new DamageApplication(absorbed, amount - absorbed);
    }

    /// <summary>Nonlethal damage never kills; it accumulates until the creature drops.</summary>
    public int TakeNonlethal(int amount)
    {
        if (amount <= 0)
        {
            return 0;
        }

        Nonlethal += amount;
        return amount;
    }

    /// <summary>
    /// Heals lethal damage, returning how much landed. Nothing heals the dead, and temporary
    /// hit points are not restored — they were never really there.
    /// </summary>
    public int Heal(int amount)
    {
        if (amount <= 0 || State == HitPointState.Dead)
        {
            return 0;
        }

        var healed = Math.Min(amount, Damage);
        Damage -= healed;
        return healed;
    }

    public int HealNonlethal(int amount)
    {
        if (amount <= 0)
        {
            return 0;
        }

        var healed = Math.Min(amount, Nonlethal);
        Nonlethal -= healed;
        return healed;
    }

    /// <summary>Grants temporary hit points. Two sources do not stack; the larger pool wins.</summary>
    public void GrantTemporary(int amount)
    {
        if (amount > Temporary)
        {
            Temporary = amount;
        }
    }

    public void RemoveTemporary() => Temporary = 0;

    /// <summary>Puts the pools back exactly as a save recorded them.</summary>
    internal void Restore(int damage, int temporary, int nonlethal)
    {
        Damage = damage;
        Temporary = temporary;
        Nonlethal = nonlethal;
    }

    /// <summary>A full night's rest: all damage gone. Temporary hit points are not granted back.</summary>
    public void Restore()
    {
        Damage = 0;
        Nonlethal = 0;
    }

    public override string ToString()
    {
        var text = $"{Current}/{Maximum} hp";

        if (Temporary > 0)
        {
            text += $" (+{Temporary} temp)";
        }

        if (Nonlethal > 0)
        {
            text += $" ({Nonlethal} nonlethal)";
        }

        return State == HitPointState.Healthy ? text : $"{text}, {State.ToString().ToLowerInvariant()}";
    }
}
