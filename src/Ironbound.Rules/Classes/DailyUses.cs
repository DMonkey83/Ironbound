namespace Ironbound.Rules.Classes;

/// <summary>
/// How much of each daily allowance has been spent since the last night's rest.
/// </summary>
/// <remarks>
/// Spent rather than remaining, the same choice <see cref="Creatures.HitPoints"/> makes with
/// damage: an allowance is usually derived from an ability score, and a Charisma that changes
/// mid-day should move what is left without anyone recounting it. Pools are named by string —
/// <c>channel-energy</c>, <c>rage</c>, <c>arcane-bond</c> — so several powers can draw on one.
/// </remarks>
public sealed class DailyUses
{
    // Sorted, so a save of the same moment is the same file however the pools were spent.
    private readonly SortedDictionary<string, int> _spent = new(StringComparer.Ordinal);

    /// <summary>Every pool with anything spent from it, by name.</summary>
    public IReadOnlyDictionary<string, int> Spent => _spent;

    public int SpentFrom(string pool) => _spent.GetValueOrDefault(pool);

    public void Spend(string pool, int amount = 1)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pool);
        ArgumentOutOfRangeException.ThrowIfNegative(amount);

        if (amount > 0)
        {
            _spent[pool] = SpentFrom(pool) + amount;
        }
    }

    /// <summary>A night's rest: every pool full again.</summary>
    public void Restore() => _spent.Clear();

    /// <summary>Puts one pool back exactly as a save recorded it.</summary>
    internal void Set(string pool, int spent)
    {
        if (spent > 0)
        {
            _spent[pool] = spent;
        }
        else
        {
            _spent.Remove(pool);
        }
    }

    public override string ToString() =>
        _spent.Count == 0 ? "nothing spent" : string.Join(", ", _spent.Select(entry => $"{entry.Key} {entry.Value}"));
}
