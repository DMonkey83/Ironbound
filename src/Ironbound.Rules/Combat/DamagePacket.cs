using System.Collections;
using Ironbound.Rules.Dice;

namespace Ironbound.Rules.Combat;

/// <summary>
/// Everything an attack deals, before it is rolled: the weapon's damage plus whatever else
/// rides along. Held as separate <see cref="DamageComponent"/>s rather than one total,
/// because criticals and mitigation both treat the strands differently.
/// </summary>
public sealed class DamagePacket : IEnumerable<DamageComponent>
{
    /// <summary>
    /// A hit always deals at least this much per component. The rules floor a damage result
    /// at 1 so a Strength penalty cannot reduce a hit to nothing; applying it per component
    /// is equivalent in every realistic case, since only the weapon's own strand ever carries
    /// a penalty.
    /// </summary>
    public const int MinimumPerComponent = 1;

    private readonly List<DamageComponent> _components = [];

    public DamagePacket()
    {
    }

    public DamagePacket(IEnumerable<DamageComponent> components)
    {
        ArgumentNullException.ThrowIfNull(components);
        _components.AddRange(components);
    }

    public static DamagePacket Of(params DamageComponent[] components) => new(components);

    /// <summary>A plain weapon with no extras: "1d8+4 slashing".</summary>
    public static DamagePacket Weapon(string amount, DamageType type) =>
        Of(DamageComponent.Weapon(amount, type));

    public IReadOnlyList<DamageComponent> Components => _components;

    public int Count => _components.Count;

    public void Add(DamageComponent component) => _components.Add(component);

    public void Add(DiceExpression amount, DamageType type, bool multipliedOnCritical = true) =>
        Add(new DamageComponent(amount, type, multipliedOnCritical));

    /// <summary>Drops every component of a type — how a suppressed flaming enchantment goes away.</summary>
    public int RemoveAll(DamageType type) => _components.RemoveAll(c => c.Type == type);

    public int Minimum => _components.Sum(c => Math.Max(MinimumPerComponent, c.Amount.Minimum));

    public int Maximum => _components.Sum(c => Math.Max(MinimumPerComponent, c.Amount.Maximum));

    /// <summary>
    /// Expected damage from an ordinary hit. Ignores the minimum-per-component floor, which
    /// only bites when a component can roll below 1 — rare, and an underestimate when it happens.
    /// </summary>
    public double Average => AverageAt(1);

    /// <inheritdoc cref="Average"/>
    public double AverageAt(int criticalMultiplier)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(criticalMultiplier, 1);
        return _components.Sum(c => c.Amount.Average * Repeats(c, criticalMultiplier));
    }

    /// <param name="criticalMultiplier">Take this straight from
    /// <see cref="AttackResult.CriticalMultiplier"/>: 1 on an ordinary hit, the weapon's
    /// multiplier on a confirmed critical. Multiplied components are rolled afresh that many
    /// times rather than having their total multiplied, which is what the rules ask for.</param>
    public DamageRoll Roll(IRandomSource random, int criticalMultiplier = 1)
    {
        ArgumentNullException.ThrowIfNull(random);
        ArgumentOutOfRangeException.ThrowIfLessThan(criticalMultiplier, 1);

        var entries = new List<DamageEntry>(_components.Count * criticalMultiplier);
        foreach (var component in _components)
        {
            var repeats = Repeats(component, criticalMultiplier);
            for (var iteration = 0; iteration < repeats; iteration++)
            {
                var roll = component.Amount.Roll(random);
                entries.Add(new DamageEntry(
                    component.Type,
                    Math.Max(MinimumPerComponent, roll.Total),
                    roll,
                    iteration));
            }
        }

        return new DamageRoll(entries, criticalMultiplier);
    }

    private static int Repeats(DamageComponent component, int criticalMultiplier) =>
        component.MultipliedOnCritical ? criticalMultiplier : 1;

    public IEnumerator<DamageComponent> GetEnumerator() => _components.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public override string ToString() =>
        _components.Count == 0 ? "no damage" : string.Join(" + ", _components);
}
