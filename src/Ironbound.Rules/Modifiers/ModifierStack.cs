using System.Collections;

namespace Ironbound.Rules.Modifiers;

/// <summary>
/// A bag of <see cref="Modifier"/>s that resolves to one number under d20 stacking
/// rules. Owned by whatever it modifies (armour class, an attack bonus, a save).
/// Insertion order is preserved and decides ties, so the total is deterministic.
/// </summary>
public sealed class ModifierStack : IEnumerable<Modifier>
{
    private readonly List<Modifier> _modifiers = [];
    private int _total;
    private bool _dirty = true;

    public int Count => _modifiers.Count;

    public IReadOnlyList<Modifier> Modifiers => _modifiers;

    /// <summary>Sum of every modifier that survives the stacking rules.</summary>
    public int Total
    {
        get
        {
            if (_dirty)
            {
                _total = Resolve(null, filter: null);
                _dirty = false;
            }

            return _total;
        }
    }

    /// <summary>
    /// Resolves the stacking rules across several stacks at once, as one pass.
    /// </summary>
    /// <remarks>
    /// Not the same as adding their totals, and the difference is a real bug: a Magic Weapon
    /// spell granting +1 enhancement and a +1 sword granting +1 enhancement must come to +1, not
    /// +2. Suppression only works if every modifier is weighed against every other one, so the
    /// merge has to happen before the rules run, not after.
    /// </remarks>
    public static ModifierBreakdown Combine(params ModifierStack[] stacks)
    {
        ArgumentNullException.ThrowIfNull(stacks);

        var merged = new ModifierStack();
        foreach (var stack in stacks)
        {
            if (stack is null)
            {
                continue;
            }

            foreach (var modifier in stack._modifiers)
            {
                merged.Add(modifier);
            }
        }

        return merged.Explain();
    }

    public void Add(Modifier modifier)
    {
        _modifiers.Add(modifier);
        _dirty = true;
    }

    public void Add(int value, BonusType type, string source) => Add(new Modifier(value, type, source));

    /// <summary>Drops every modifier granted by <paramref name="source"/>. Returns how many went.</summary>
    public int RemoveAllFrom(string source)
    {
        var removed = _modifiers.RemoveAll(m => string.Equals(m.Source, source, StringComparison.Ordinal));
        if (removed > 0)
        {
            _dirty = true;
        }

        return removed;
    }

    public void Clear()
    {
        if (_modifiers.Count == 0)
        {
            return;
        }

        _modifiers.Clear();
        _dirty = true;
    }

    /// <summary>
    /// Total over only the modifiers <paramref name="filter"/> accepts. The filter runs
    /// before the stacking rules, so an excluded modifier cannot suppress an included one:
    /// a touch attack that ignores your +6 armour still sees your +4 mage armour.
    /// </summary>
    public int TotalWhere(Func<Modifier, bool> filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        return Resolve(null, filter);
    }

    /// <summary>The same total as <see cref="Total"/>, with the reasoning attached.</summary>
    /// <param name="filter">Optional; excluded modifiers are left out of the breakdown entirely,
    /// because "does not apply here" is a different statement from "lost to a bigger bonus".</param>
    public ModifierBreakdown Explain(Func<Modifier, bool>? filter = null)
    {
        var entries = new List<ModifierEntry>(_modifiers.Count);
        return new ModifierBreakdown(entries, Resolve(entries, filter));
    }

    private int Resolve(List<ModifierEntry>? entries, Func<Modifier, bool>? filter)
    {
        var total = 0;
        for (var i = 0; i < _modifiers.Count; i++)
        {
            var modifier = _modifiers[i];
            if (filter is not null && !filter(modifier))
            {
                continue;
            }

            var winner = FindSuppressor(i, modifier, filter);
            if (winner is null)
            {
                total += modifier.Value;
                entries?.Add(new ModifierEntry(modifier, Applied: true, SuppressedBy: null));
            }
            else
            {
                entries?.Add(new ModifierEntry(modifier, Applied: false, _modifiers[winner.Value].Source));
            }
        }

        return total;
    }

    /// <summary>
    /// Index of the modifier that crowds <paramref name="modifier"/> out, or null if it applies.
    /// Equal values tie-break on insertion order so exactly one of them counts.
    /// </summary>
    private int? FindSuppressor(int index, Modifier modifier, Func<Modifier, bool>? filter)
    {
        // Penalties are outside the stacking rules entirely: they all apply.
        if (modifier.IsPenalty)
        {
            return null;
        }

        var rule = BonusTypes.RuleFor(modifier.Type);
        if (rule == StackingRule.Stacks)
        {
            return null;
        }

        for (var i = 0; i < _modifiers.Count; i++)
        {
            if (i == index)
            {
                continue;
            }

            var other = _modifiers[i];
            if (other.Type != modifier.Type || other.IsPenalty)
            {
                continue;
            }

            if (filter is not null && !filter(other))
            {
                continue;
            }

            if (rule == StackingRule.StacksPerSource
                && !string.Equals(other.Source, modifier.Source, StringComparison.Ordinal))
            {
                continue;
            }

            if (other.Value > modifier.Value || (other.Value == modifier.Value && i < index))
            {
                return i;
            }
        }

        return null;
    }

    public IEnumerator<Modifier> GetEnumerator() => _modifiers.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public override string ToString() => Explain().ToString();
}
