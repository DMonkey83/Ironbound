namespace Ironbound.Rules.Defense;

/// <summary>
/// Flat protection against physical damage: "DR 10/silver", "DR 10/good and silver", "DR 15/—".
/// Subtracted once from the whole of an attack's physical damage, not per die and not per
/// component — which is why a rogue's sneak attack survives it far better than a plain hit.
/// </summary>
public sealed record DamageReduction
{
    public DamageReduction(
        int amount,
        DamageBypass bypassedBy = DamageBypass.None,
        BypassMode mode = BypassMode.Any)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(amount);
        Amount = amount;
        BypassedBy = bypassedBy;
        Mode = mode;
    }

    public int Amount { get; }

    /// <summary>Qualities that get through. <see cref="DamageBypass.None"/> means nothing does.</summary>
    public DamageBypass BypassedBy { get; }

    public BypassMode Mode { get; }

    public bool IsBypassedBy(DamageBypass qualities)
    {
        if (BypassedBy == DamageBypass.None)
        {
            return false;
        }

        return Mode == BypassMode.All
            ? (qualities & BypassedBy) == BypassedBy
            : (qualities & BypassedBy) != DamageBypass.None;
    }

    /// <summary>How much this actually stops against an attack carrying these qualities.</summary>
    public int Against(DamageBypass qualities) => IsBypassedBy(qualities) ? 0 : Amount;

    public override string ToString() => $"DR {Amount}/{DamageBypasses.Describe(BypassedBy, Mode)}";
}
