using Ironbound.Rules.Effects;
using Ironbound.Rules.Feats;
using Ironbound.Rules.Modifiers;

namespace Ironbound.Rules.Encounters.Actions;

/// <summary>
/// Lunge: five more feet of reach for this turn's melee attacks, paid for with two points of
/// armour class until the lunger's next turn.
/// </summary>
/// <remarks>
/// Free, but it has to be decided before any attack is made, as the book says — so it is refused
/// once the turn has swung at anything. The reach is the turn's and goes when the turn ends; it
/// does not lengthen the reach anybody walking past is threatened at.
/// </remarks>
public sealed class LungeAction : GameAction
{
    public const string EffectName = "Lunge";

    public const int ArmourPenalty = -2;

    public override string Name => "lunge";

    public override ActionCost Cost => ActionCost.Free;

    public override bool CanPerform(ActionContext context) =>
        context.Actor.HasFeat(FeatEffect.Lunge)
        && context.Actor.MeleeAttack is not null
        && !context.Combatant.IsLunging
        && !context.Turn.Taken.Any(taken =>
            taken is AttackActionResult or FullAttackResult or ManeuverActionResult);

    public override ActionResult Perform(ActionContext context)
    {
        context.Combatant.IsLunging = true;
        context.Actor.Effects.Apply(new ModifierEffect(EffectName, Duration.Rounds(1))
            .GrantsToArmorClass(ArmourPenalty, BonusType.Untyped));

        return new ActionResult(this, context.Actor, $"{context.Actor.Name} lunges: five more feet of reach this turn, two less armour class");
    }
}
