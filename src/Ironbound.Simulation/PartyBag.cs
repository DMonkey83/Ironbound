using Ironbound.Rules.Items;

namespace Ironbound.Simulation;

/// <summary>
/// So many of one item, and what has happened to it: a line in the party's bag or in a container.
/// </summary>
/// <remarks>
/// A value rather than a handle. Whoever holds one — a loot window, an inventory screen — passes
/// it back to say which line they mean, and the bag finds the line that matches it by item and
/// state; the count it carries is only what there was when it was read.
/// </remarks>
/// <param name="IsBroken">Cracked, and still cracked whoever carries it next. A broken dagger
/// never shares a line with a whole one.</param>
public sealed record BagEntry(ItemDefinition Item, int Count = 1, bool IsBroken = false)
{
    public string Id => Item.Id;

    /// <summary>What the whole line weighs, at Medium weight.</summary>
    public decimal Weight => Item.Weight * Count;

    /// <summary>What the whole line is worth, in copper.</summary>
    public int Value => Item.Price * Count;

    /// <summary>The same item in the same state, whatever the count.</summary>
    public bool Matches(BagEntry other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return string.Equals(Item.Id, other.Item.Id, StringComparison.Ordinal) && IsBroken == other.IsBroken;
    }

    /// <summary>"dagger ×2", "longsword (broken)".</summary>
    public override string ToString() =>
        $"{Item.Name}{(IsBroken ? " (broken)" : string.Empty)}{(Count > 1 ? $" ×{Count}" : string.Empty)}";
}

/// <summary>
/// A list of bag entries that stacks what stacks: what a bag and a container both are inside.
/// </summary>
internal sealed class EntryList
{
    private readonly List<BagEntry> _entries = [];

    public IReadOnlyList<BagEntry> Entries => _entries;

    public int Items => _entries.Sum(entry => entry.Count);

    /// <summary>Puts so many of an item in. A natural weapon is refused: nobody can carry teeth.</summary>
    public bool Add(ItemDefinition item, int count = 1, bool broken = false)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (item.IsNatural || count < 1)
        {
            return false;
        }

        if (item.Stackable)
        {
            var index = _entries.FindIndex(entry =>
                string.Equals(entry.Item.Id, item.Id, StringComparison.Ordinal) && entry.IsBroken == broken);

            if (index >= 0)
            {
                _entries[index] = _entries[index] with { Count = _entries[index].Count + count };
                return true;
            }

            _entries.Add(new BagEntry(item, count, broken));
            return true;
        }

        // A thing that does not stack is a line of its own each time, however alike two are.
        for (var i = 0; i < count; i++)
        {
            _entries.Add(new BagEntry(item, 1, broken));
        }

        return true;
    }

    /// <summary>The line that matches, or null.</summary>
    public BagEntry? Find(BagEntry wanted) => _entries.FirstOrDefault(entry => entry.Matches(wanted));

    /// <summary>How many of this item, in this state, are here in all.</summary>
    public int CountOf(BagEntry wanted) => _entries.Where(entry => entry.Matches(wanted)).Sum(entry => entry.Count);

    /// <summary>
    /// Takes so many out of the matching lines. All or nothing: false, and nothing moved, when
    /// there are not that many.
    /// </summary>
    public bool Remove(BagEntry wanted, int count = 1)
    {
        ArgumentNullException.ThrowIfNull(wanted);

        if (count < 1 || CountOf(wanted) < count)
        {
            return false;
        }

        var left = count;
        for (var i = 0; i < _entries.Count && left > 0;)
        {
            var entry = _entries[i];
            if (!entry.Matches(wanted))
            {
                i++;
                continue;
            }

            var taken = Math.Min(left, entry.Count);
            left -= taken;

            if (taken == entry.Count)
            {
                _entries.RemoveAt(i);
            }
            else
            {
                _entries[i] = entry with { Count = entry.Count - taken };
                i++;
            }
        }

        return true;
    }

    public void Clear() => _entries.Clear();
}

/// <summary>
/// Everything the party carries that nobody is wearing: one bag for all of them, and the coins.
/// </summary>
/// <remarks>
/// Shared, as Wrath's is. Taking something from a chest puts it here; putting it on is a separate
/// step, on somebody's own inventory, which is what stops a sword and the one it replaced from
/// trading places for ever. It weighs what it holds, coins included, and that weight is shared
/// out among the party by Strength — see <see cref="PartyEncumbrance"/>.
/// </remarks>
public sealed class PartyBag
{
    private readonly EntryList _entries = new();

    public IReadOnlyList<BagEntry> Entries => _entries.Entries;

    /// <summary>The coins, as they were found.</summary>
    public Money Money { get; private set; }

    /// <summary>What the coins are worth, in copper pieces.</summary>
    public int Purse => Money.Value;

    /// <summary>Everything in it, coins included, in pounds.</summary>
    public decimal Weight => Entries.Sum(entry => entry.Weight) + Money.Weight;

    /// <summary>What the items are worth, in copper. The coins are <see cref="Purse"/>.</summary>
    public int Value => Entries.Sum(entry => entry.Value);

    /// <summary>How many items, counting each of a stack.</summary>
    public int Count => _entries.Items;

    /// <summary>Puts something in. False, and nothing added, for a natural weapon.</summary>
    public bool Add(ItemDefinition item, int count = 1, bool broken = false) => _entries.Add(item, count, broken);

    /// <summary>Puts a whole line in, state and all.</summary>
    public bool Add(BagEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return _entries.Add(entry.Item, entry.Count, entry.IsBroken);
    }

    /// <summary>Takes so many of a line out. False, and nothing taken, when there are not that many.</summary>
    public bool Remove(BagEntry entry, int count = 1) => _entries.Remove(entry, count);

    public bool Contains(string id) =>
        Entries.Any(entry => string.Equals(entry.Item.Id, id, StringComparison.Ordinal));

    /// <summary>How many of an item, whole or broken.</summary>
    public int CountOf(string id) =>
        Entries.Where(entry => string.Equals(entry.Item.Id, id, StringComparison.Ordinal)).Sum(entry => entry.Count);

    /// <summary>The first line holding an item, whole ones first, or null.</summary>
    public BagEntry? Find(string id) =>
        Entries.Where(entry => string.Equals(entry.Item.Id, id, StringComparison.Ordinal))
            .OrderBy(entry => entry.IsBroken)
            .FirstOrDefault();

    /// <summary>The line matching one somebody is holding on to, with its count as it is now.</summary>
    public BagEntry? Find(BagEntry entry) => _entries.Find(entry);

    public void AddCoins(Money coins) => Money += coins;

    /// <summary>Takes coins out. False, and nothing taken, when there are not that many of each kind.</summary>
    public bool RemoveCoins(Money coins)
    {
        if (!Money.Covers(coins))
        {
            return false;
        }

        Money -= coins;
        return true;
    }

    internal void Clear()
    {
        _entries.Clear();
        Money = Money.None;
    }

    public override string ToString() =>
        Entries.Count == 0 && Money.IsEmpty
            ? "nothing"
            : string.Join(", ", Entries.Select(entry => entry.ToString()).Append(Money.IsEmpty ? null : Money.ToString()).OfType<string>());
}

/// <summary>
/// What came of handling gear between fights: putting something on, taking it off, taking it
/// from a chest. Reads as a bool, for whoever only wants to know whether it worked.
/// </summary>
/// <param name="Line">What happened, for the log — or why it did not.</param>
public sealed record GearResult(bool Success, string Line)
{
    public static implicit operator bool(GearResult result) => result?.Success == true;

    internal static GearResult Done(string line) => new(true, line);

    internal static GearResult Refused(string why) => new(false, why);

    public override string ToString() => Line;
}
