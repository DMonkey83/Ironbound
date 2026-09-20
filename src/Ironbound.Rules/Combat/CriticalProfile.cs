namespace Ironbound.Rules.Combat;

/// <summary>
/// A weapon's critical behaviour: the lowest natural roll that threatens, and how much
/// damage a confirmed critical deals. A longsword is 19-20/x2, a scythe 20/x4.
/// </summary>
public readonly record struct CriticalProfile
{
    public const int MinimumThreatsOn = 2;
    public const int MaximumMultiplier = 10;

    /// <param name="threatsOn">Lowest natural d20 that threatens: 20 for most weapons, 18 for a falchion.</param>
    /// <param name="multiplier">Damage multiplier on a confirmed critical.</param>
    public CriticalProfile(int threatsOn, int multiplier)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(threatsOn, MinimumThreatsOn);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(threatsOn, 20);
        ArgumentOutOfRangeException.ThrowIfLessThan(multiplier, 2);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(multiplier, MaximumMultiplier);

        ThreatsOn = threatsOn;
        Multiplier = multiplier;
    }

    /// <summary>20/x2 — what a weapon does unless it says otherwise.</summary>
    public static CriticalProfile Standard { get; } = new(20, 2);

    public int ThreatsOn { get; }

    public int Multiplier { get; }

    /// <summary>How many natural rolls threaten: 1 for 20, 2 for 19-20, 3 for 18-20.</summary>
    public int ThreatRangeWidth => 21 - ThreatsOn;

    public bool Threatens(int naturalRoll) => naturalRoll >= ThreatsOn;

    /// <summary>
    /// Doubles the threat range, as Improved Critical and a keen weapon do: 20 becomes
    /// 19-20, 19-20 becomes 17-20, 18-20 becomes 15-20. The two do not stack, so whoever
    /// applies this is responsible for only doing it once.
    /// </summary>
    public CriticalProfile Widened() => new(21 - (ThreatRangeWidth * 2), Multiplier);

    public override string ToString() =>
        ThreatsOn == 20 ? $"20/x{Multiplier}" : $"{ThreatsOn}-20/x{Multiplier}";
}
