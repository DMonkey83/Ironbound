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
                _total = Resolve(null);
                _dirty = false;
            }

            return _total;
        }
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

    /// <summary>The same total as <see cref="Total"/>, with the reasoning attached.</summary>
    public ModifierBreakdown Explain()
    {
        var entries = new List<ModifierEntry>(_modifiers.Count);
        return new ModifierBreakdown(entries, Resolve(entries));
    }

    private int Resolve(List<ModifierEntry>? entries)
    {
        var total = 0;
        for (var i = 0; i < _modifiers.Count; i++)
        {
            var modifier = _modifiers[i];
            var winner = FindSuppressor(i, modifier);
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
    private int? FindSuppressor(int index, Modifier modifier)
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
