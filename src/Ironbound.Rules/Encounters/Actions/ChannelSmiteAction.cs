using Ironbound.Rules.Classes;
using Ironbound.Rules.Combat;
using Ironbound.Rules.Feats;

namespace Ironbound.Rules.Encounters.Actions;

/// <summary>
/// Channel Smite: a swift action and one use of channel energy, put into the next melee blow.
/// If it lands on something the energy hurts — the undead for positive energy, the living for
/// negative — that thing takes the channel's damage as well, Will for half.
/// </summary>
/// <remarks>
/// The channel is spent here, on declaring it; a miss wastes it, as the book says. The blow
/// itself is whatever melee attack comes next this turn, which is why it is a declaration
/// rather than an attack of its own.
/// </remarks>
public sealed class ChannelSmiteAction : GameAction
{
    public override string Name => "channel smite";

    public override ActionCost Cost => ActionCost.Swift;

    public override bool CanPerform(ActionContext context) =>
        context.Actor.HasFeat(FeatEffect.ChannelSmite)
        && context.Actor.MeleeAttack is not null
        && !context.Actor.Stances.IsActive(Stance.ChannelSmite)
        && ClassPowers.ChannelKindOf(context.Actor) is not null
        && ClassPowers.Left(context.Actor, ClassPowers.ChannelPool) > 0;

    public override ActionResult Perform(ActionContext context)
    {
        var actor = context.Actor;
        actor.DailyUses.Spend(ClassPowers.ChannelPool);
        actor.Stances.DeclareSmite();

        return new ActionResult(this, actor, $"{actor.Name} channels energy into the next blow");
    }
}
