using Ironbound.Rules.Combat;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Maps;

namespace Ironbound.Rules.Encounters.Actions;

/// <summary>
/// Walking somewhere. Costs a move action and no more than the creature's speed in ground.
/// </summary>
/// <remarks>
/// Movement needs no budget of its own: a move action buys up to your speed, and a double move is
/// simply two move actions — which the standard-spent-as-a-move rule already allows.
/// <para>
/// The path is walked one square at a time rather than jumped, because leaving a threatened
/// square provokes an attack of opportunity and that attack has to land while the mover is still
/// standing in the square being left.
/// </para>
/// </remarks>
public class MoveAction : GameAction
{
    /// <param name="path">Squares walked, beginning with the one the creature already stands in.</param>
    public MoveAction(IReadOnlyList<GridSquare> path)
    {
        ArgumentNullException.ThrowIfNull(path);
        Path = path;
    }

    public IReadOnlyList<GridSquare> Path { get; }

    public GridSquare? Destination => Path.Count > 0 ? Path[^1] : null;

    public override string Name => "move";

    public override ActionCost Cost => ActionCost.Move;

    /// <summary>
    /// Finds as much of the way towards <paramref name="target"/> as this turn's speed pays for,
    /// or null when there is nowhere worth going.
    /// </summary>
    public static MoveAction? Towards(Battlefield field, Creature mover, Creature target)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(mover);
        ArgumentNullException.ThrowIfNull(target);

        var path = field.FindApproach(mover, target, mover.CurrentSpeed);
        return path.Count > 1 ? new MoveAction(path) : null;
    }

    /// <summary>How much ground this kind of movement may cover.</summary>
    protected virtual int Allowance(Creature mover) => mover.CurrentSpeed;

    /// <summary>Whether leaving the square at <paramref name="stepIndex"/> is careless.</summary>
    protected virtual bool ProvokesLeaving(int stepIndex) => true;

    /// <summary>Whether the creature has already used up its movement for the turn.</summary>
    protected virtual bool AlreadyMoved(Combatant combatant) => combatant.HasTakenFiveFootStep;

    protected virtual void RecordMovement(Combatant combatant) => combatant.HasMoved = true;

    public override bool CanPerform(ActionContext context)
    {
        if (context.Encounter.Battlefield is not { } field || Path.Count < 2)
        {
            return false;
        }

        if (AlreadyMoved(context.Combatant))
        {
            return false;
        }

        if (field.SquareOf(context.Actor) != Path[0] || !field.IsFree(Path[^1]))
        {
            return false;
        }

        for (var i = 1; i < Path.Count; i++)
        {
            // Every step has to be to a touching square that is actually walkable.
            if (!Distance.AreAdjacent(Path[i - 1], Path[i]) || !field.IsPassable(Path[i]))
            {
                return false;
            }
        }

        return field.PathCost(Path) <= Allowance(context.Actor);
    }

    public override ActionResult Perform(ActionContext context)
    {
        var field = context.Encounter.Battlefield!;
        var actor = context.Actor;

        var travelled = new List<GridSquare> { Path[0] };
        var opportunities = new List<StrikeResult>();

        // Where it could actually come to rest. Squares squeezed past an ally are walked over
        // but never stood on, so they are not candidates.
        var landed = Path[0];
        var spent = 0;
        var diagonals = 0;

        for (var i = 1; i < Path.Count; i++)
        {
            if (ProvokesLeaving(i - 1))
            {
                opportunities.AddRange(Opportunities.Provoke(context.Encounter, actor, Path[i - 1]));

                // Cut down before getting out of the square. It goes no further.
                if (!actor.IsConscious)
                {
                    break;
                }
            }

            spent += field.StepCost(Path[i - 1], Path[i], ref diagonals);
            travelled.Add(Path[i]);

            if (field.IsFree(Path[i]))
            {
                landed = Path[i];
            }
        }

        field.Place(actor, landed);
        RecordMovement(context.Combatant);

        var description = $"{actor.Name} {Name}s to {landed} ({spent} ft of {Allowance(actor)})";
        if (opportunities.Count > 0)
        {
            description += $", provoking {opportunities.Count}";
        }

        if (travelled.Count < Path.Count)
        {
            description += " — stopped short";
        }

        return new MoveActionResult(this, actor, description, travelled, opportunities);
    }
}

/// <summary>A move, with the ground actually covered and any swings it drew on the way.</summary>
public sealed record MoveActionResult(
    GameAction Action,
    Creature Actor,
    string Description,
    IReadOnlyList<GridSquare> Travelled,
    IReadOnlyList<StrikeResult> Opportunities)
    : ActionResult(Action, Actor, Description)
{
    /// <summary>True when an attack of opportunity stopped the move before its destination.</summary>
    public bool WasInterrupted => !Actor.IsConscious;
}
