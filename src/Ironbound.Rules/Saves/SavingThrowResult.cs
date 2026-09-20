using Ironbound.Rules.Modifiers;

namespace Ironbound.Rules.Saves;

/// <summary>
/// One saving throw, rolled. Like <c>AttackResult</c>, it carries enough to render the log and
/// to answer "why did I fail that?" without asking the rules engine anything else.
/// </summary>
public sealed record SavingThrowResult
{
    public required Save Save { get; init; }

    public required int NaturalRoll { get; init; }

    public required ModifierBreakdown Bonus { get; init; }

    public required int Total { get; init; }

    public required int DifficultyClass { get; init; }

    public required bool Succeeded { get; init; }

    public bool Failed => !Succeeded;

    /// <summary>
    /// The natural roll settled it without consulting the total. Not simply "rolled a 1 or a 20"
    /// — a ruleset with those floors switched off produces a 20 that still has to beat the DC.
    /// </summary>
    public required bool DecidedByNaturalRoll { get; init; }

    /// <summary>How much the roll beat the difficulty class by; negative when it failed.</summary>
    public int Margin => Total - DifficultyClass;

    public override string ToString()
    {
        var name = SaveInfo.Name(Save);
        var verdict = Succeeded ? "success" : "failure";

        return DecidedByNaturalRoll
            ? $"{name} save: d20 [{NaturalRoll}] — automatic {verdict}"
            : $"{name} save: d20 [{NaturalRoll}] {Bonus.Total:+0;-0;+0} = {Total} "
              + $"vs DC {DifficultyClass} — {verdict}";
    }
}
