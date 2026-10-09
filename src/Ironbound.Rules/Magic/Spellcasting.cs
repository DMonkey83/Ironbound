using Ironbound.Rules.Abilities;
using Ironbound.Rules.Classes;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Feats;

namespace Ironbound.Rules.Magic;

/// <summary>
/// What a creature can cast, and how much of it is left today.
/// </summary>
/// <remarks>
/// Slot counts are set by hand rather than derived from a class table — levels and classes are a
/// system of their own. What matters here is that casting costs something finite, because a
/// caster with unlimited fireballs makes every encounter the same encounter.
/// <para>
/// A slot is a slot of a level, not a particular spell written into it each morning: anything
/// prepared of that level may be cast from it. That is a simplification of how a wizard
/// prepares, and it is the reason the cleric's spontaneous cures cost so little to add — her
/// slots were already, in effect, spontaneous.
/// </para>
/// <para>
/// The specialty slots are the exception the rules insist on. A cleric's domain slot holds only
/// a domain spell and a specialist wizard's school slot only a spell of her school, so they are
/// counted apart and spent first by whatever qualifies, which keeps the general slots for
/// everything else.
/// </para>
/// </remarks>
public sealed class Spellcasting
{
    private readonly Creature _owner;
    private readonly Dictionary<int, int> _maximum = [];
    private readonly Dictionary<int, int> _remaining = [];
    private readonly Dictionary<int, int> _specialtyMaximum = [];
    private readonly Dictionary<int, int> _specialtyRemaining = [];
    private readonly List<Spell> _prepared = [];
    private readonly List<Spell> _spellbook = [];
    private readonly List<Spell> _spontaneous = [];

    internal Spellcasting(Creature owner) => _owner = owner;

    /// <summary>Intelligence for a wizard, Wisdom for a cleric, Charisma for a sorcerer.</summary>
    public Ability CastingAbility { get; set; } = Ability.Intelligence;

    /// <summary>Drives range, damage scaling and very little else so far.</summary>
    public int CasterLevel { get; set; }

    public IReadOnlyList<Spell> Prepared => _prepared;

    public bool CanCastAnything => _prepared.Count > 0;

    /// <summary>
    /// What a wizard has written down, which is more than she prepares: what her bonded object
    /// can cast once a day without a slot. A creature file that says nothing gets its prepared
    /// list, which is the spec's default and the honest one for a character who only knows
    /// what she carries.
    /// </summary>
    public IReadOnlyList<Spell> Spellbook => _spellbook;

    /// <summary>
    /// What may be cast without being prepared, from a general slot of its level or higher: a
    /// good cleric's cures, an evil one's inflictions.
    /// </summary>
    public IReadOnlyList<Spell> Spontaneous => _spontaneous;

    public Spellcasting Prepare(Spell spell)
    {
        ArgumentNullException.ThrowIfNull(spell);
        _prepared.Add(spell);
        return this;
    }

    /// <summary>Writes a spell into the book, once.</summary>
    public Spellcasting Inscribe(Spell spell)
    {
        ArgumentNullException.ThrowIfNull(spell);

        if (!_spellbook.Any(written => written.Id == spell.Id))
        {
            _spellbook.Add(spell);
        }

        return this;
    }

    /// <summary>Sets the spells that may be cast spontaneously, replacing any there were.</summary>
    public Spellcasting SetSpontaneous(IEnumerable<Spell> spells)
    {
        ArgumentNullException.ThrowIfNull(spells);

        _spontaneous.Clear();
        _spontaneous.AddRange(spells);
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

    /// <summary>Sets a level's domain or school slots, refilling them.</summary>
    public Spellcasting SetSpecialtySlots(int level, int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(level);
        ArgumentOutOfRangeException.ThrowIfNegative(count);

        _specialtyMaximum[level] = count;
        _specialtyRemaining[level] = count;
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

        Raise(_maximum, _remaining, level, maximum);
        return this;
    }

    /// <summary>The same, for domain or school slots.</summary>
    public Spellcasting RaiseSpecialtySlots(int level, int maximum)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(level);
        ArgumentOutOfRangeException.ThrowIfNegative(maximum);

        Raise(_specialtyMaximum, _specialtyRemaining, level, maximum);
        return this;
    }

    private static void Raise(Dictionary<int, int> maxima, Dictionary<int, int> remaining, int level, int maximum)
    {
        var had = maxima.GetValueOrDefault(level);
        if (maximum <= had)
        {
            return;
        }

        remaining[level] = remaining.GetValueOrDefault(level) + (maximum - had);
        maxima[level] = maximum;
    }

    /// <summary>Which spell levels this caster has general slots for at all.</summary>
    public IReadOnlyCollection<int> SlotLevels => _maximum.Keys;

    /// <summary>Which spell levels this caster has a domain or school slot for.</summary>
    public IReadOnlyCollection<int> SpecialtyLevels => _specialtyMaximum.Keys;

    public int SlotsMaximum(int level) => _maximum.GetValueOrDefault(level);

    public int SpecialtyMaximum(int level) => _specialtyMaximum.GetValueOrDefault(level);

    public int SpecialtyRemaining(int level) => _specialtyRemaining.GetValueOrDefault(level);

    /// <summary>Puts a level's slots back mid-day, as a save recorded them.</summary>
    internal void RestoreSlots(int level, int maximum, int remaining)
    {
        _maximum[level] = maximum;
        _remaining[level] = remaining;
    }

    internal void RestoreSpecialtySlots(int level, int maximum, int remaining)
    {
        _specialtyMaximum[level] = maximum;
        _specialtyRemaining[level] = remaining;
    }

    /// <summary>Forgets every slot, as a load does before working them out again.</summary>
    internal void ClearSlots()
    {
        _maximum.Clear();
        _remaining.Clear();
        _specialtyMaximum.Clear();
        _specialtyRemaining.Clear();
    }

    public int SlotsRemaining(int level) => _remaining.GetValueOrDefault(level);

    /// <summary>
    /// Whether it is prepared. By id rather than by equality, so the empowered copy of a
    /// prepared spell is still the spell she prepared.
    /// </summary>
    public bool Knows(Spell spell) => _prepared.Any(prepared => prepared.Id == spell.Id);

    /// <summary>Whether it is one she can cast without preparing it.</summary>
    public bool KnowsSpontaneously(Spell spell) => _spontaneous.Any(cure => cure.Id == spell.Id);

    /// <summary>
    /// How many general slots it costs: two for a spell of a school the wizard gave up, which
    /// is the opposition rule translated from "prepared in two slots" to a slot model that does
    /// not prepare.
    /// </summary>
    public int Cost(Spell spell)
    {
        ArgumentNullException.ThrowIfNull(spell);
        return _owner.Choices.Opposition.Contains(spell.School) ? 2 : 1;
    }

    /// <summary>
    /// Whether a domain or school slot may hold it: one of her domains' spells, or a spell of her
    /// school. The domain's own list decides, at the level the domain lists it.
    /// </summary>
    public bool IsSpecialty(Spell spell)
    {
        ArgumentNullException.ThrowIfNull(spell);

        if (_owner.Choices.School is { School: { } school } && spell.School == school)
        {
            return true;
        }

        return _owner.Choices.Domains.Any(domain => domain.SpellAt(spell.Level) == spell.Id);
    }

    public bool CanCast(Spell spell)
    {
        ArgumentNullException.ThrowIfNull(spell);
        return Plan(spell) is not null;
    }

    /// <summary>Spends a slot. Returns false and changes nothing when there is none to spend.</summary>
    public bool Spend(Spell spell)
    {
        if (Plan(spell) is not { } plan)
        {
            return false;
        }

        var (level, specialty, count) = plan;
        var pool = specialty ? _specialtyRemaining : _remaining;
        pool[level] = pool[level] - count;

        return true;
    }

    /// <summary>
    /// Which slot a cast would come out of, or null when there is none. One place decides, so
    /// that asking and spending can never disagree.
    /// </summary>
    private (int Level, bool Specialty, int Count)? Plan(Spell spell)
    {
        // A raging barbarian does not cast, however many slots she has; a spell empowered by
        // somebody without the feat is not a spell anybody can cast.
        if (Rage.IsRaging(_owner) || (spell.Empowered && !_owner.HasFeat(FeatEffect.EmpowerSpell)))
        {
            return null;
        }

        var level = spell.SlotLevel;

        if (Knows(spell))
        {
            if (IsSpecialty(spell) && SpecialtyRemaining(level) > 0)
            {
                return (level, true, 1);
            }

            var cost = Cost(spell);
            if (SlotsRemaining(level) >= cost)
            {
                return (level, false, cost);
            }
        }

        // A cure in place of something prepared: the lowest general slot of its level or
        // higher, which is "lose a prepared spell of the same or a higher level".
        if (KnowsSpontaneously(spell))
        {
            foreach (var candidate in _maximum.Keys.Where(slot => slot >= level).Order())
            {
                if (SlotsRemaining(candidate) > 0)
                {
                    return (candidate, false, 1);
                }
            }
        }

        return null;
    }

    /// <summary>A night's sleep.</summary>
    public void Rest()
    {
        foreach (var (level, count) in _maximum)
        {
            _remaining[level] = count;
        }

        foreach (var (level, count) in _specialtyMaximum)
        {
            _specialtyRemaining[level] = count;
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
        if (_maximum.Count == 0 && _specialtyMaximum.Count == 0)
        {
            return "no spellcasting";
        }

        var slots = _maximum.Keys.Order().Select(level =>
            $"{SlotsRemaining(level)}/{_maximum[level]}"
            + (SpecialtyMaximum(level) > 0 ? $"+{SpecialtyRemaining(level)}/{SpecialtyMaximum(level)}" : string.Empty));

        return $"caster level {CasterLevel}, slots {string.Join(" ", slots)}";
    }
}
