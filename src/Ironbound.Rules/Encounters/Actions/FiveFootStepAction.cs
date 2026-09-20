using Ironbound.Rules.Creatures;
using Ironbound.Rules.Maps;

namespace Ironbound.Rules.Encounters.Actions;

/// <summary>
/// Shifting one square without giving anyone a free swing at you.
/// </summary>
/// <remarks>
/// The counterplay to attacks of opportunity, and the reason they are a tactical rule rather than
/// simply a punishment. It costs no action at all, but it is exclusive with ordinary movement in
/// both directions: having moved, you cannot step, and having stepped, you cannot move.
/// </remarks>
public sealed class FiveFootStepAction(IReadOnlyList<GridSquare> path) : MoveAction(path)
{
    public override string Name => "five-foot step";

    public override ActionCost Cost => ActionCost.Free;

    /// <summary>The one square next door.</summary>
    public static FiveFootStepAction To(GridSquare from, GridSquare to) => new([from, to]);

    protected override int Allowance(Creature mover) => Distance.FeetPerSquare;

    protected override bool ProvokesLeaving(int stepIndex) => false;

    protected override bool AlreadyMoved(Combatant combatant) =>
        combatant.HasMoved || combatant.HasTakenFiveFootStep;

    protected override void RecordMovement(Combatant combatant) =>
        combatant.HasTakenFiveFootStep = true;

    public override bool CanPerform(ActionContext context)
    {
        if (Path.Count != 2 || !base.CanPerform(context))
        {
            return false;
        }

        // Difficult ground costs ten feet a square, and a five-foot step is exactly five.
        return context.Encounter.Battlefield is { } field && !field.IsDifficult(Path[1]);
    }
}
