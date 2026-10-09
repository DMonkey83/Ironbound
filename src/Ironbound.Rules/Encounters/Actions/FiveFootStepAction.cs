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
    protected override bool CanStagger => false;

    public override string Name => "five-foot step";

    public override ActionCost Cost => ActionCost.Free;

    /// <summary>The one square next door.</summary>
    public static FiveFootStepAction To(GridSquare from, GridSquare to) => new([from, to]);

    protected override int Allowance(Creature mover) => Distance.FeetPerSquare;

    protected override bool ProvokesLeaving(int stepIndex) => false;

    protected override bool AlreadyMoved(Combatant combatant) =>
        combatant.HasMoved || combatant.HasTakenFiveFootStep || combatant.OwesStep;

    /// <summary>A five-foot step is five feet whatever is owed; it is barred outright instead.</summary>
    protected override int Owed(Combatant combatant) => 0;

    protected override void RecordMovement(Combatant combatant) =>
        combatant.HasTakenFiveFootStep = true;

    public override ActionResult Perform(ActionContext context)
    {
        var result = (MoveActionResult)base.Perform(context);
        var followed = StepUp(context);

        return followed.Count == 0
            ? result
            : result with { Description = $"{result.Description}; {string.Join("; ", followed)}" };
    }

    /// <summary>
    /// Step Up: whoever has the feat and was standing next to the one who just stepped away
    /// steps after it, so long as it ends beside it again. Taken whenever it can be, as an
    /// attack of opportunity is — nobody is asked in the middle of somebody else's turn.
    /// </summary>
    /// <remarks>
    /// It is an immediate action, so it costs the follower its next swift action and its next
    /// five-foot step, and five feet off its next walk; and only one a round.
    /// </remarks>
    private List<string> StepUp(ActionContext context)
    {
        var followed = new List<string>();
        if (context.Encounter.Battlefield is not { } field || field.SquareOf(context.Actor) is not { } landed)
        {
            return followed;
        }

        foreach (var combatant in context.Encounter.Order)
        {
            var follower = combatant.Creature;

            if (!follower.HasFeat(Feats.FeatEffect.StepUp)
                || !follower.IsEnemyOf(context.Actor)
                || !combatant.CanAct
                || follower.IsProne
                || combatant.SteppedUp
                || follower.MeleeAttack is null
                || field.SquareOf(follower) is not { } standing
                || !Distance.AreAdjacent(standing, Path[0])
                || Distance.AreAdjacent(standing, landed))
            {
                continue;
            }

            if (Neighbours(standing).Cast<GridSquare?>().FirstOrDefault(square =>
                field.IsFree(square!.Value)
                && !field.IsDifficult(square.Value)
                && Distance.AreAdjacent(square.Value, landed)) is not { } after)
            {
                continue;
            }

            field.Place(follower, after);
            combatant.SteppedUp = true;
            followed.Add($"{follower.Name} steps up to {after}");
        }

        return followed;
    }

    private static IEnumerable<GridSquare> Neighbours(GridSquare square)
    {
        for (var dy = -1; dy <= 1; dy++)
        {
            for (var dx = -1; dx <= 1; dx++)
            {
                if (dx != 0 || dy != 0)
                {
                    yield return new GridSquare(square.X + dx, square.Y + dy);
                }
            }
        }
    }

    public override bool CanPerform(ActionContext context)
    {
        if (Path.Count != 2 || !base.CanPerform(context))
        {
            return false;
        }

        // A five-foot step is a careful step, and nobody steps anywhere from flat on their
        // back. Without this the free, unprovoking step was a better crawl than the crawl.
        if (context.Actor.IsProne)
        {
            return false;
        }

        // Difficult ground costs ten feet a square, and a five-foot step is exactly five —
        // unless Nimble Moves makes that square clear ground for this one step.
        return context.Encounter.Battlefield is { } field
            && (!field.IsDifficult(Path[1])
                || Movement.EasyGroundFeet(context.Actor) - context.Combatant.EasyGroundUsed >= Distance.FeetPerSquare);
    }
}
