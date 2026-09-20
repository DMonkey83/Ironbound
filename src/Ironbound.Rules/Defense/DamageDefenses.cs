using Ironbound.Rules.Combat;

namespace Ironbound.Rules.Defense;

/// <summary>
/// Everything that stands between a damage roll and a creature's hit points: damage reduction,
/// energy resistance, immunity and vulnerability.
/// </summary>
/// <remarks>
/// Order of operations, which is the part that is easy to get wrong:
/// immunity zeroes a type outright; vulnerability then adds half again; resistance subtracts
/// per type; damage reduction subtracts <em>once</em> from the attack's whole physical total.
/// Two kinds of reduction never stack — the best applicable one applies — and neither do two
/// resistances to the same energy.
/// </remarks>
public sealed class DamageDefenses
{
    private readonly List<DamageReduction> _reductions = [];
    private readonly Dictionary<DamageType, int> _resistances = [];
    private readonly HashSet<DamageType> _immunities = [];
    private readonly HashSet<DamageType> _vulnerabilities = [];

    public IReadOnlyList<DamageReduction> Reductions => _reductions;

    public IReadOnlyDictionary<DamageType, int> Resistances => _resistances;

    public IReadOnlyCollection<DamageType> Immunities => _immunities;

    public IReadOnlyCollection<DamageType> Vulnerabilities => _vulnerabilities;

    public void Add(DamageReduction reduction)
    {
        ArgumentNullException.ThrowIfNull(reduction);
        _reductions.Add(reduction);
    }

    public DamageDefenses Reduce(int amount, DamageBypass bypassedBy = DamageBypass.None,
        BypassMode mode = BypassMode.Any)
    {
        Add(new DamageReduction(amount, bypassedBy, mode));
        return this;
    }

    /// <summary>Grants energy resistance. Two sources do not stack; the larger applies.</summary>
    public DamageDefenses Resist(DamageType type, int amount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(amount);
        if (amount > ResistanceTo(type))
        {
            _resistances[type] = amount;
        }

        return this;
    }

    public int ResistanceTo(DamageType type) => _resistances.GetValueOrDefault(type);

    public DamageDefenses MakeImmuneTo(DamageType type)
    {
        _immunities.Add(type);
        return this;
    }

    public bool IsImmuneTo(DamageType type) => _immunities.Contains(type);

    /// <summary>Half again as much damage from this type. Trolls and fire.</summary>
    public DamageDefenses MakeVulnerableTo(DamageType type)
    {
        _vulnerabilities.Add(type);
        return this;
    }

    public bool IsVulnerableTo(DamageType type) => _vulnerabilities.Contains(type);

    /// <summary>The best reduction that this attack cannot get past, or 0 if it gets past them all.</summary>
    public int ReductionAgainst(DamageBypass qualities) => BestReduction(qualities).Amount;

    /// <param name="qualities">What the attack carries — silver, magic, alignment. The physical
    /// damage types present are added automatically, so a mace need not declare itself blunt.</param>
    /// <param name="rules">Defaults to <see cref="RuleOptions.Pathfinder"/>.</param>
    public DamageTaken Apply(
        DamageRoll roll,
        DamageBypass qualities = DamageBypass.None,
        RuleOptions? rules = null)
    {
        ArgumentNullException.ThrowIfNull(roll);
        rules ??= RuleOptions.Pathfinder;

        var effective = qualities;
        foreach (var type in roll.Types)
        {
            effective |= DamageBypasses.Of(type);
        }

        var (best, pool) = BestReduction(effective);

        // First pass: immunity, vulnerability and resistance, all of which are per type.
        var adjusted = new List<Adjusted>(roll.Types.Count);
        var physicalTotal = 0;

        foreach (var type in roll.Types)
        {
            var raw = roll.AmountOf(type);

            if (_immunities.Contains(type))
            {
                adjusted.Add(new Adjusted(type, raw, 0, ["immune"], IsPhysical: false));
                continue;
            }

            var notes = new List<string>();
            var amount = raw;

            if (_vulnerabilities.Contains(type))
            {
                amount += amount / 2;
                notes.Add("vulnerable");
            }

            var physical = DamageTypes.IsPhysical(type);
            if (!physical && ResistanceTo(type) is > 0 and var resisted)
            {
                amount = Math.Max(0, amount - resisted);
                notes.Add($"resist {DamageTypes.Name(type)} {resisted}");
            }

            if (physical)
            {
                physicalTotal += amount;
            }

            adjusted.Add(new Adjusted(type, raw, amount, notes, physical));
        }

        if (rules.MaximumReductionPercent < 100)
        {
            pool = Math.Min(pool, physicalTotal * rules.MaximumReductionPercent / 100);
        }

        // Second pass: one subtraction of damage reduction, drained across the physical types.
        var absorbed = 0;
        var entries = new List<MitigationEntry>(adjusted.Count);

        foreach (var item in adjusted)
        {
            var amount = item.Amount;

            if (item.IsPhysical && pool > 0 && best is not null)
            {
                var stopped = Math.Min(pool, amount);
                amount -= stopped;
                pool -= stopped;
                absorbed += stopped;
                item.Notes.Add(best.ToString());
            }

            entries.Add(new MitigationEntry(
                item.Type,
                item.Raw,
                Math.Max(0, amount),
                item.Notes.Count == 0 ? null : string.Join(", ", item.Notes)));
        }

        return new DamageTaken(entries, roll.Total, absorbed > 0 ? best : null, absorbed);
    }

    private readonly record struct Adjusted(
        DamageType Type,
        int Raw,
        int Amount,
        List<string> Notes,
        bool IsPhysical);

    private (DamageReduction? Reduction, int Amount) BestReduction(DamageBypass qualities)
    {
        DamageReduction? best = null;
        var amount = 0;

        foreach (var candidate in _reductions)
        {
            var against = candidate.Against(qualities);
            if (against > amount)
            {
                amount = against;
                best = candidate;
            }
        }

        return (best, amount);
    }

    public override string ToString()
    {
        var parts = new List<string>();
        parts.AddRange(_reductions.Select(r => r.ToString()));
        parts.AddRange(_immunities.Select(t => $"immune to {DamageTypes.Name(t)}"));
        parts.AddRange(_resistances.Select(r => $"resist {DamageTypes.Name(r.Key)} {r.Value}"));
        parts.AddRange(_vulnerabilities.Select(t => $"vulnerable to {DamageTypes.Name(t)}"));

        return parts.Count == 0 ? "no defences" : string.Join("; ", parts);
    }
}
