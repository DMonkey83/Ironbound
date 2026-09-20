using System.Text;
using Ironbound.Rules.Dice;

namespace Ironbound.Rules.Combat;

/// <summary>
/// One rolled strand of damage. <paramref name="Iteration"/> counts repeats on a critical,
/// so the log can show both swings of a x2 rather than one doubled number.
/// </summary>
public readonly record struct DamageEntry(DamageType Type, int Amount, DiceRoll Roll, int Iteration);

/// <summary>
/// The damage an attack actually dealt, kept split by type because mitigation is applied
/// per type — resistance to fire says nothing about the slashing half of the same swing.
/// </summary>
public sealed class DamageRoll
{
    private readonly Dictionary<DamageType, int> _byType = [];
    private readonly List<DamageType> _order = [];

    internal DamageRoll(IReadOnlyList<DamageEntry> entries, int criticalMultiplier)
    {
        Entries = entries;
        CriticalMultiplier = criticalMultiplier;

        foreach (var entry in entries)
        {
            if (!_byType.TryAdd(entry.Type, entry.Amount))
            {
                _byType[entry.Type] += entry.Amount;
            }
            else
            {
                _order.Add(entry.Type);
            }

            Total += entry.Amount;
        }
    }

    public IReadOnlyList<DamageEntry> Entries { get; }

    public int Total { get; }

    /// <summary>The multiplier this was rolled at; 1 for an ordinary hit.</summary>
    public int CriticalMultiplier { get; }

    /// <summary>Damage per type, which is the shape mitigation will consume.</summary>
    public IReadOnlyDictionary<DamageType, int> ByType => _byType;

    /// <summary>Types in the order they first appeared, for a stable log line.</summary>
    public IReadOnlyList<DamageType> Types => _order;

    public int AmountOf(DamageType type) => _byType.GetValueOrDefault(type);

    public override string ToString()
    {
        var text = new StringBuilder();
        foreach (var type in _order)
        {
            if (text.Length > 0)
            {
                text.Append(" + ");
            }

            var rolls = Entries.Where(e => e.Type == type).Select(e => e.Roll.ToString());
            text.Append($"{_byType[type]} {DamageTypes.Name(type)} ({string.Join(", ", rolls)})");
        }

        return text.Length == 0 ? "0 damage" : $"{text} = {Total} damage";
    }
}
