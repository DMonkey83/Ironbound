using Ironbound.Rules.Defense;
using Ironbound.Rules.Modifiers;

namespace Ironbound.Rules.Combat;

public enum AttackOutcome
{
    Miss,
    Hit,
    CriticalHit,
}

/// <summary>
/// Everything that happened when one attack was resolved. Immutable, and complete enough to
/// render the combat log and to drive damage — the presentation layer reads this rather than
/// asking the rules engine follow-up questions.
/// </summary>
public sealed record AttackResult
{
    /// <summary>The d20 itself, before any bonus.</summary>
    public required int NaturalRoll { get; init; }

    /// <summary>How the attack bonus was arrived at, including anything that failed to stack.</summary>
    public required ModifierBreakdown Bonus { get; init; }

    /// <summary>Natural roll plus the attack bonus.</summary>
    public required int Total { get; init; }

    /// <summary>The armour class actually used, after <see cref="Options"/> was applied.</summary>
    public required int TargetArmorClass { get; init; }

    /// <summary>Which defences the attack bypassed, and whether the defender could react.</summary>
    public required DefenseOptions Options { get; init; }

    /// <summary>
    /// What the defender gained by standing behind something. Already folded into
    /// <see cref="TargetArmorClass"/>; kept separately only so the log can say so out loud.
    /// </summary>
    public int Cover { get; init; }

    /// <summary>
    /// What the defender's posture was worth: -4 against a swing, +4 against a shot. Already
    /// folded into <see cref="TargetArmorClass"/>, like <see cref="Cover"/>.
    /// </summary>
    public int Prone { get; init; }

    public required AttackOutcome Outcome { get; init; }

    /// <summary>The roll landed in the weapon's threat range and hit, so a confirmation was rolled.</summary>
    public required bool Threatened { get; init; }

    /// <summary>Null when nothing threatened and no confirmation roll was made.</summary>
    public int? ConfirmationNatural { get; init; }

    public int? ConfirmationTotal { get; init; }

    /// <summary>The weapon's multiplier on a confirmed critical, otherwise 1 — so damage can
    /// multiply unconditionally instead of branching.</summary>
    public required int CriticalMultiplier { get; init; }

    public bool IsHit => Outcome != AttackOutcome.Miss;

    public bool IsCritical => Outcome == AttackOutcome.CriticalHit;

    /// <summary>A natural 1 or 20, which decided the result without consulting the total.</summary>
    public bool IsAutomatic => NaturalRoll is 1 or 20;

    /// <summary>Threatened but the confirmation failed: an ordinary hit.</summary>
    public bool ThreatenedButNotConfirmed => Threatened && Outcome != AttackOutcome.CriticalHit;

    /// <summary>"AC", "touch AC", "flat-footed AC", "touch flat-footed AC".</summary>
    public static string DescribeArmorClass(DefenseOptions options)
    {
        var touch = (options & DefenseOptions.TouchAttack) != 0 ? "touch " : string.Empty;
        var denied = (options & DefenseOptions.DexterityDenied) != 0 ? "flat-footed " : string.Empty;
        return $"{touch}{denied}AC";
    }

    public override string ToString()
    {
        var label = DescribeArmorClass(Options);
        var bonus = Bonus.Total.ToString("+0;-0;+0");

        var text = NaturalRoll switch
        {
            1 => "d20 [1] — automatic miss",
            20 => "d20 [20] — automatic hit",
            _ => $"d20 [{NaturalRoll}] {bonus} = {Total} vs {label} {TargetArmorClass}"
                 + (Cover > 0 ? $" (+{Cover} cover)" : string.Empty)
                 + (Prone != 0 ? $" ({Prone:+0;-0} prone)" : string.Empty)
                 + " — " + (IsHit ? "hit" : "miss"),
        };

        if (Threatened)
        {
            text += ", threat";
        }

        if (ConfirmationNatural is not { } confirmation)
        {
            return text;
        }

        var verdict = IsCritical ? $"critical hit (x{CriticalMultiplier})" : "not confirmed";
        var detail = confirmation is 1 or 20
            ? $"d20 [{confirmation}]"
            : $"d20 [{confirmation}] {bonus} = {ConfirmationTotal} vs {label} {TargetArmorClass}";

        return $"{text}; confirm {detail} — {verdict}";
    }
}
