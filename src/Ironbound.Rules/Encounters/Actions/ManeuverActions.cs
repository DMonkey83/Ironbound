using Ironbound.Rules.Combat;
using Ironbound.Rules.Conditions;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Effects;
using Ironbound.Rules.Maps;

namespace Ironbound.Rules.Encounters.Actions;

/// <summary>
/// What every maneuver has in common: reach the target, provoke for trying, then one check.
/// </summary>
/// <remarks>
/// The provocation is the reason maneuvers are a gamble rather than a free option. Reaching in
/// to grab somebody's leg means dropping your guard, and the feats that remove the provocation
/// are among the most sought-after in the game precisely because of it.
/// </remarks>
public abstract class ManeuverAction : GameAction
{
    protected ManeuverAction(Creature target)
    {
        ArgumentNullException.ThrowIfNull(target);
        Target = target;
    }

    public Creature Target { get; }

    public abstract ManeuverKind Kind { get; }

    public override ActionCost Cost => ActionCost.Standard;

    public override bool CanPerform(ActionContext context) =>
        Target.IsAlive
        && !ReferenceEquals(Target, context.Actor)
        && (context.Encounter.Battlefield is not { } field || Reaches(context, field, context.Actor));

    /// <summary>
    /// Whether the target is close enough. A trip is made with the weapon in hand and goes as far
    /// as it does, a polearm's ten feet included — and a lunge's five more, since a trip is made
    /// in place of a melee attack; anything made with the body needs the body there.
    /// </summary>
    protected virtual bool Reaches(ActionContext context, Battlefield field, Creature actor) =>
        Kind == ManeuverKind.Trip
            ? Movement.Reaches(context, actor, Target, actor.MeleeAttack)
            : field.IsWithinTouch(actor, Target);

    public override ActionResult Perform(ActionContext context)
    {
        var actor = context.Actor;
        var opportunities = Provoke(context);

        if (!actor.IsConscious)
        {
            return new ManeuverActionResult(this, actor, null, opportunities, string.Empty);
        }

        Provoked.Clear();
        var check = Maneuvers.Attempt(actor, Target, context.Random, Kind);
        var consequence = Apply(context, check);

        return new ManeuverActionResult(this, actor, check, opportunities, consequence) { Drawn = [.. Provoked] };
    }

    /// <summary>What happens once the check is known. Returns a phrase for the log, or empty.</summary>
    protected abstract string Apply(ActionContext context, ManeuverResult check);

    /// <summary>
    /// Swings the target drew by what the manoeuvre did to it — going down under Greater Trip,
    /// being driven back under Greater Bull Rush. Handed back with the result.
    /// </summary>
    protected List<StrikeResult> Provoked { get; } = [];

    /// <summary>
    /// The free swing reaching in costs you — unless you have practised this particular
    /// maneuver, which is most of why those feats are worth taking.
    /// </summary>
    /// <summary>Whether trying it gives anyone a free swing. Knockback says not.</summary>
    protected virtual bool Provokes(ActionContext context) => !context.Actor.HasFeat(Maneuvers.ImprovedBy(Kind));

    private IReadOnlyList<StrikeResult> Provoke(ActionContext context)
    {
        if (!Provokes(context))
        {
            return [];
        }

        return context.Encounter.Battlefield?.SquareOf(context.Actor) is { } standing
            ? Opportunities.Provoke(context.Encounter, context.Actor, standing)
            : [];
    }
}

/// <summary>
/// Putting somebody on the floor.
/// </summary>
/// <remarks>
/// Worth far more than the four points of armour class it buys: a creature that has to stand up
/// spends its move action doing it, and a creature that has spent its move action cannot full
/// attack. Against anything with a second swing, tripping it costs it half its damage for the
/// round — and it provokes again on the way up.
/// </remarks>
public sealed class TripAction(Creature target) : ManeuverAction(target)
{
    public override string Name => "trip";

    public override ManeuverKind Kind => ManeuverKind.Trip;

    public override bool CanPerform(ActionContext context) =>
        base.CanPerform(context) && !Target.IsProne;

    protected override string Apply(ActionContext context, ManeuverResult check)
    {
        if (check.Succeeded)
        {
            Target.Effects.Apply(ConditionInfo.Effect(Condition.Prone, Duration.Permanent));

            // Greater Trip: going down draws a swing from everyone standing over it, the one who
            // tripped it included.
            if (context.Actor.HasFeat(Feats.FeatEffect.GreaterTrip)
                && context.Encounter.Battlefield?.SquareOf(Target) is { } fallen)
            {
                var swings = Opportunities.Provoke(context.Encounter, Target, fallen);
                Provoked.AddRange(swings);

                if (swings.Count > 0)
                {
                    return $"{Target.Name} is knocked prone, provoking {swings.Count} as it falls";
                }
            }

            return $"{Target.Name} is knocked prone";
        }

        // Miss by ten and you have overreached: the leg you grabbed takes you down with it —
        // unless the trip was made with a weapon built for it, which the wielder lets go of
        // instead, and keeps her feet.
        if (check.Backfired)
        {
            if (context.Actor.MeleeAttack is { } weapon
                && weapon.Has(WeaponSpecial.Trip)
                && context.Actor.Equipment.LetGo(weapon))
            {
                return $"{context.Actor.Name} lets go of the {context.Actor.Equipment.ItemFor(weapon)!.Name} rather than fall";
            }

            context.Actor.Effects.Apply(ConditionInfo.Effect(Condition.Prone, Duration.Permanent));
            return $"{context.Actor.Name} overbalances and falls prone";
        }

        return string.Empty;
    }
}

/// <summary>
/// Shoving somebody backwards, five feet and another five for every five points over.
/// </summary>
/// <remarks>
/// The answer to a line that will not break: it does not care about armour class, it undoes
/// flanking, and it drags a caster out of range without a single point of damage. It stops dead
/// against a wall or another creature, which is what makes a corridor worth holding.
/// </remarks>
public class BullRushAction(Creature target) : ManeuverAction(target)
{
    public const int FeetPerIncrement = 5;

    public override string Name => "bull rush";

    public override ManeuverKind Kind => ManeuverKind.BullRush;

    protected override string Apply(ActionContext context, ManeuverResult check)
    {
        if (!check.Succeeded || context.Encounter.Battlefield is not { } field)
        {
            return string.Empty;
        }

        return Shove(context, check, field);
    }

    /// <summary>The push itself, for anything that pushes the same way and then does more.</summary>
    protected string Shove(ActionContext context, ManeuverResult check, Battlefield field)
    {

        if (field.SquareOf(context.Actor) is not { } from || field.SquareOf(Target) is not { } standing)
        {
            return string.Empty;
        }

        var pushed = Push(field, from, standing, 1 + check.ExtraIncrements);

        if (pushed == standing)
        {
            return $"{Target.Name} holds its ground";
        }

        // Greater Bull Rush: being driven back draws a swing from every friend of the shover
        // whose reach it is driven out of — and not from the shover.
        if (context.Actor.HasFeat(Feats.FeatEffect.GreaterBullRush))
        {
            var actor = context.Actor;
            var swings = Opportunities.Provoke(context.Encounter, Target, standing, who: other => !ReferenceEquals(other, actor));
            Provoked.AddRange(swings);

            if (swings.Count > 0)
            {
                return $"{Target.Name} is driven back to {pushed}, provoking {swings.Count}";
            }
        }

        return $"{Target.Name} is driven back to {pushed}";
    }

    /// <summary>
    /// Walks the target directly away from the shover, stopping at whatever it backs into.
    /// </summary>
    private static GridSquare Push(Battlefield field, GridSquare from, GridSquare standing, int increments)
    {
        var stepX = Math.Sign(standing.X - from.X);
        var stepY = Math.Sign(standing.Y - from.Y);

        // Directly away means the same direction the shove came from; a target in your own
        // square has nowhere to be pushed.
        if (stepX == 0 && stepY == 0)
        {
            return standing;
        }

        var occupant = field.OccupantOf(standing);
        var at = standing;

        for (var step = 0; step < increments; step++)
        {
            var next = new GridSquare(at.X + stepX, at.Y + stepY);
            if (!field.IsFree(next))
            {
                break;
            }

            at = next;
        }

        if (at != standing && occupant is not null)
        {
            field.Remove(occupant);
            field.Place(occupant, at);
        }

        return at;
    }
}

/// <summary>A maneuver's outcome: the check, what it provoked, and what it did.</summary>
public sealed record ManeuverActionResult(
    GameAction Action,
    Creature Actor,
    ManeuverResult? Check,
    IReadOnlyList<StrikeResult> Opportunities,
    string Consequence)
    : ActionResult(Action, Actor, Describe(Actor, Action, Check, Opportunities, Consequence))
{
    /// <summary>Swings the target drew by what was done to it: Greater Trip, Greater Bull Rush.</summary>
    public IReadOnlyList<StrikeResult> Drawn { get; init; } = [];

    private static string Describe(
        Creature actor,
        GameAction action,
        ManeuverResult? check,
        IReadOnlyList<StrikeResult> opportunities,
        string consequence)
    {
        if (check is null)
        {
            return $"{actor.Name} is cut down attempting to {action.Name}";
        }

        var provoked = opportunities.Count > 0 ? $", provoking {opportunities.Count}" : string.Empty;
        var outcome = consequence.Length > 0 ? $"; {consequence}" : string.Empty;

        return $"{check}{provoked}{outcome}";
    }
}
