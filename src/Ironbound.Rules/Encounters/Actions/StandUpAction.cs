using Ironbound.Rules.Combat;
using Ironbound.Rules.Conditions;
using Ironbound.Rules.Creatures;

namespace Ironbound.Rules.Encounters.Actions;

/// <summary>
/// Getting back on your feet.
/// </summary>
/// <remarks>
/// A move action, and it provokes — which is the whole reason knocking somebody down is worth
/// doing. The victim's choice is ugly either way: stay on the floor at -4 to hit and -4 to
/// armour class against everyone standing over them, or stand up, eat a free attack, and lose
/// the full attack they were going to make. Costing them the round is usually worth more than
/// the four points.
/// </remarks>
public sealed class StandUpAction : GameAction
{
    public override string Name => "stand up";

    public override ActionCost Cost => ActionCost.Move;

    public override bool CanPerform(ActionContext context) => context.Actor.IsProne;

    public override ActionResult Perform(ActionContext context)
    {
        var actor = context.Actor;
        var opportunities = context.Encounter.Battlefield?.SquareOf(actor) is { } standing
            ? Opportunities.Provoke(context.Encounter, actor, standing)
            : [];

        // Cut down halfway up: still on the floor, and the move action is gone.
        if (!actor.IsConscious)
        {
            return new StandUpResult(this, actor, false, opportunities);
        }

        actor.Effects.Remove(Condition.Prone);

        return new StandUpResult(this, actor, true, opportunities);
    }
}

public sealed record StandUpResult(
    GameAction Action,
    Creature Actor,
    bool Stood,
    IReadOnlyList<StrikeResult> Opportunities)
    : ActionResult(Action, Actor, Describe(Actor, Stood, Opportunities))
{
    private static string Describe(
        Creature actor, bool stood, IReadOnlyList<StrikeResult> opportunities)
    {
        var provoked = opportunities.Count > 0 ? $", provoking {opportunities.Count}" : string.Empty;

        return stood
            ? $"{actor.Name} stands up{provoked}"
            : $"{actor.Name} is cut down getting up{provoked}";
    }
}
