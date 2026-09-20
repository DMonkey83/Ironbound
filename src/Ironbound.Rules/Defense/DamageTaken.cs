using Ironbound.Rules.Combat;

namespace Ironbound.Rules.Defense;

/// <summary>What one damage type was reduced to, and why.</summary>
public readonly record struct MitigationEntry(DamageType Type, int Raw, int Taken, string? Note);

/// <summary>
/// The outcome of running a <see cref="DamageRoll"/> past a creature's defences. Keeps the raw
/// numbers alongside the mitigated ones, because "your 24 became 11" is the answer players
/// actually want when a fight is going badly.
/// </summary>
public sealed class DamageTaken
{
    internal DamageTaken(
        IReadOnlyList<MitigationEntry> entries,
        int raw,
        DamageReduction? reduction,
        int absorbedByReduction)
    {
        Entries = entries;
        Raw = raw;
        Reduction = reduction;
        AbsorbedByReduction = absorbedByReduction;
        Total = entries.Sum(e => e.Taken);
    }

    public IReadOnlyList<MitigationEntry> Entries { get; }

    /// <summary>Damage before anything was applied.</summary>
    public int Raw { get; }

    /// <summary>What actually reaches hit points.</summary>
    public int Total { get; }

    /// <summary>The damage reduction that applied, if any was left unbypassed.</summary>
    public DamageReduction? Reduction { get; }

    public int AbsorbedByReduction { get; }

    public bool WasMitigated => Total != Raw;

    public int TakenOf(DamageType type) =>
        Entries.Where(e => e.Type == type).Sum(e => e.Taken);

    public override string ToString()
    {
        if (Entries.Count == 0)
        {
            return "no damage";
        }

        var parts = Entries.Select(entry =>
        {
            var text = entry.Raw == entry.Taken
                ? $"{entry.Taken} {DamageTypes.Name(entry.Type)}"
                : $"{entry.Raw} {DamageTypes.Name(entry.Type)} → {entry.Taken}";

            return entry.Note is null ? text : $"{text} ({entry.Note})";
        });

        return $"{string.Join(", ", parts)} = {Total} taken";
    }
}
