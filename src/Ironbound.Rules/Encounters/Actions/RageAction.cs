using Ironbound.Rules.Classes;

namespace Ironbound.Rules.Encounters.Actions;

/// <summary>
/// Flying into a rage, or coming out of one. A free action either way, on her own turn.
/// </summary>
/// <remarks>
/// One action for both directions because the button is one button: there is never a moment
/// when both make sense. Starting is refused while she is still fatigued from the last one, out
/// of rounds for the day, or not a barbarian at all; ending is always allowed.
/// </remarks>
public sealed class RageAction : GameAction
{
    public override string Name => "rage";

    public override ActionCost Cost => ActionCost.Free;

    public override bool CanPerform(ActionContext context) =>
        Rage.IsRaging(context.Actor) || Rage.CanStart(context.Actor);

    public override ActionResult Perform(ActionContext context)
    {
        var actor = context.Actor;

        if (Rage.IsRaging(actor))
        {
            var rounds = Rage.Current(actor)!.RoundsSoFar;
            Rage.End(actor);

            return new ActionResult(
                this,
                actor,
                $"{actor.Name} lets the rage go after {rounds} round(s), and is fatigued "
                + $"({Rage.RoundsLeft(actor)} of {Rage.RoundsPerDay(actor)} rounds left today)");
        }

        Rage.Start(actor);

        return new ActionResult(
            this,
            actor,
            $"{actor.Name} flies into a rage (+{RageEffect.AbilityBonus} Str and Con, "
            + $"+{RageEffect.WillBonus} Will, {RageEffect.ArmourPenalty} AC; "
            + $"{Rage.RoundsLeft(actor)} of {Rage.RoundsPerDay(actor)} rounds left)");
    }
}
