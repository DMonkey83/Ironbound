using Ironbound.Rules.Content;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Items;
using Ironbound.Rules.Maps;

namespace Ironbound.Simulation;

/// <summary>
/// Something with things in it: a crate, a chest, a cart, a sack, a pile on the floor, or
/// somebody who fell.
/// </summary>
/// <remarks>
/// One idea for all of them, because they all ask the same questions — what is in it, can it be
/// opened, has anybody noticed it — and a loot window that had to tell a chest from a corpse
/// would be two loot windows. Opening one only opens it; taking is its own step, item by item
/// or all at once, and nothing taken can be taken again, because it is no longer there.
/// </remarks>
public sealed class Container
{
    private readonly EntryList _contents = new();
    private readonly HashSet<string> _rolled = new(StringComparer.Ordinal);

    internal Container(
        string id,
        string name,
        ContainerLook look,
        IReadOnlyList<GridSquare> squares,
        FeatureDefinition? feature = null,
        Creature? body = null)
    {
        Id = id;
        Name = name;
        Look = look;
        Squares = squares;
        Feature = feature;
        Body = body;

        // Bodies and piles have no lid. A sack or a chest is shut until somebody opens it, and so
        // is anything a level places, whatever it looks like: opening it is finding it.
        IsOpen = feature is null && look is ContainerLook.Body or ContainerLook.Pile;
    }

    public string Id { get; }

    /// <summary>"the crates", "Orc Brute".</summary>
    public string Name { get; }

    public ContainerLook Look { get; }

    public IReadOnlyList<GridSquare> Squares { get; }

    public IReadOnlyList<BagEntry> Contents => _contents.Entries;

    /// <summary>What the coins in it are worth, in copper pieces.</summary>
    public int Coins => Money.Value;

    /// <summary>The coins themselves, kind by kind.</summary>
    public Money Money { get; internal set; }

    /// <summary>Opened at least once: the lid is up. A body or a pile is always open.</summary>
    public bool IsOpen { get; internal set; }

    public bool IsEmpty => Contents.Count == 0 && Money.IsEmpty;

    /// <summary>Locked until somebody picks it or breaks it.</summary>
    public bool IsLocked { get; internal set; }

    /// <summary>Not noticed by anybody yet: not drawn, not clickable, not in <see cref="Campaign.Containers"/>.</summary>
    public bool IsHidden => HiddenDc is not null && !IsNoticed;

    /// <summary>The fallen, for a body; null for anything else.</summary>
    public Creature? Body { get; }

    /// <summary>The level feature it is, for one written into a level; null for a body or a pile.</summary>
    public FeatureDefinition? Feature { get; }

    /// <summary>What it is worth, items and coins, in copper.</summary>
    public int Value => Contents.Sum(entry => entry.Value) + Coins;

    /// <summary>What it all weighs, coins included.</summary>
    public decimal Weight => Contents.Sum(entry => entry.Weight) + Money.Weight;

    /// <summary>The Perception DC to notice it, or null for one in plain sight.</summary>
    public int? HiddenDc => Feature?.HiddenDc;

    public int LockDc => Feature?.LockDc ?? 0;

    public int BreakDc => Feature?.BreakDc ?? 0;

    internal bool IsNoticed { get; set; }

    /// <summary>Who has had their one look for it, by name.</summary>
    internal IReadOnlySet<string> Rolled => _rolled;

    internal bool HasRolled(Creature who) => _rolled.Contains(who.Name);

    internal void Roll(Creature who) => _rolled.Add(who.Name);

    internal void Roll(string name) => _rolled.Add(name);

    internal bool Put(ItemDefinition item, int count = 1, bool broken = false) => _contents.Add(item, count, broken);

    internal bool Put(BagEntry entry) => _contents.Add(entry.Item, entry.Count, entry.IsBroken);

    internal bool Take(BagEntry entry, int count) => _contents.Remove(entry, count);

    internal BagEntry? Find(BagEntry entry) => _contents.Find(entry);

    internal int CountOf(BagEntry entry) => _contents.CountOf(entry);

    internal void Empty()
    {
        _contents.Clear();
        Money = Money.None;
    }

    /// <summary>Whether somebody standing here can reach into it: beside it, or on it for one on the floor.</summary>
    public bool IsWithinReach(GridSquare from) =>
        Squares.Any(square => square == from || Distance.AreAdjacent(square, from));

    public override string ToString() =>
        $"{Name}: {(IsEmpty ? "empty" : string.Join(", ", Contents.Select(entry => entry.ToString()).Append(Money.IsEmpty ? null : Money.ToString()).OfType<string>()))}";
}
