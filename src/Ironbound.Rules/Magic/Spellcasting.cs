using Ironbound.Rules.Abilities;
using Ironbound.Rules.Creatures;

namespace Ironbound.Rules.Magic;

/// <summary>
/// What a creature can cast, and how much of it is left today.
/// </summary>
/// <remarks>
/// Slot counts are set by hand rather than derived from a class table — levels and classes are a
/// system of their own. What matters here is that casting costs something finite, because a
/// caster with unlimited fireballs makes every encounter the same encounter.
/// </remarks>
public sealed class Spellcasting
{
    private readonly Creature _owner;
    private readonly Dictionary<int, int> _maximum = [];
    private readonly Dictionary<int, int> _remaining = [];
    private readonly List<Spell> _prepared = [];

    internal Spellcasting(Creature owner) => _owner = owner;

    /// <summary>Intelligence for a wizard, Wisdom for a cleric, Charisma for a sorcerer.</summary>
    public Ability CastingAbility { get; set; } = Ability.Intelligence;

    /// <summary>Drives range, damage scaling and very little else so far.</summary>
    public int CasterLevel { get; set; }

    public IReadOnlyList<Spell> Prepared => _prepared;

    public bool CanCastAnything => _prepared.Count > 0;

    public Spellcasting Prepare(Spell spell)
    {
        ArgumentNullException.ThrowIfNull(spell);
        _prepared.Add(spell);
        return this;
    }

    public Spellcasting SetSlots(int level, int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(level);
        ArgumentOutOfRangeException.ThrowIfNegative(count);

        _maximum[level] = count;
        _remaining[level] = count;
        return this;
    }

    /// <summary>
    /// Raises a level's allowance, granting only the difference.
    /// </summary>
    /// <remarks>
    /// Not <see cref="SetSlots"/>, which refills. Gaining a level should hand over the new
    /// slots and leave the ones you spent this morning exactly as empty as you left them.
    /// </remarks>
    public Spellcasting RaiseSlots(int level, int maximum)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(level);
        ArgumentOutOfRangeException.ThrowIfNegative(maximum);

        var had = SlotsMaximum(level);
        if (maximum <= had)
        {
            return this;
        }

        _remaining[level] = SlotsRemaining(level) + (maximum - had);
        _maximum[level] = maximum;

        return this;
    }

    /// <summary>Which spell levels this caster has slots for at all.</summary>
    public IReadOnlyCollection<int> SlotLevels => _maximum.Keys;

    public int SlotsMaximum(int level) => _maximum.GetValueOrDefault(level);

    /// <summary>Puts a level's slots back mid-day, as a save recorded them.</summary>
    internal void RestoreSlots(int level, int maximum, int remaining)
    {
        _maximum[level] = maximum;
        _remaining[level] = remaining;
    }

    public int SlotsRemaining(int level) => _remaining.GetValueOrDefault(level);

    public bool Knows(Spell spell) => _prepared.Contains(spell);

    public bool CanCast(Spell spell)
    {
        ArgumentNullException.ThrowIfNull(spell);
        return Knows(spell) && SlotsRemaining(spell.Level) > 0;
    }

    /// <summary>Spends a slot. Returns false and changes nothing when there is none to spend.</summary>
    public bool Spend(Spell spell)
    {
        if (!CanCast(spell))
        {
            return false;
        }

        _remaining[spell.Level] = _remaining[spell.Level] - 1;
        return true;
    }

    /// <summary>A night's sleep.</summary>
    public void Rest()
    {
        foreach (var (level, count) in _maximum)
        {
            _remaining[level] = count;
        }
    }

    /// <summary>Ten, plus the spell's level, plus how clever or devout or forceful you are.</summary>
    public int SaveDC(Spell spell)
    {
        ArgumentNullException.ThrowIfNull(spell);
        return 10 + spell.Level + _owner.Abilities[CastingAbility].Modifier;
    }

    public override string ToString()
    {
        if (_maximum.Count == 0)
        {
            return "no spellcasting";
        }

        var slots = _maximum.Keys.Order().Select(level => $"{SlotsRemaining(level)}/{_maximum[level]}");
        return $"caster level {CasterLevel}, slots {string.Join(" ", slots)}";
    }
}
