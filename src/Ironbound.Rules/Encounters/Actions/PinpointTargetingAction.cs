using Ironbound.Rules.Combat;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Defense;
using Ironbound.Rules.Feats;

namespace Ironbound.Rules.Encounters.Actions;

/// <summary>
/// Pinpoint Targeting: one shot, a standard action, aimed at the gap in the armour — the target
/// gets nothing from its armour, its shield or its hide. Not for anybody who has moved this
/// turn.
/// </summary>
/// <remarks>
/// That is exactly what a touch attack ignores, so it is resolved as one: deflection, dodge,
/// Dexterity and size still count, as the book has them.
/// </remarks>
public sealed class PinpointTargetingAction : GameAction
{
    public PinpointTargetingAction(WeaponAttack weapon, Creature target)
    {
        ArgumentNullException.ThrowIfNull(weapon);
        ArgumentNullException.ThrowIfNull(target);
        Weapon = weapon;
        Target = target;
    }

    public WeaponAttack Weapon { get; }

    public Creature Target { get; }

    public override string Name => "pinpoint targeting";

    public override ActionCost Cost => ActionCost.Standard;

    public override bool CanPerform(ActionContext context) =>
        context.Actor.HasFeat(FeatEffect.PinpointTargeting)
        && Weapon.IsRanged
        && !context.Combatant.HasMoved
        && !context.Combatant.HasTakenFiveFootStep
        && new AttackAction(Weapon, Target).CanPerform(context);

    public override ActionResult Perform(ActionContext context)
    {
        var shot = new AttackAction(Weapon, Target, DefenseOptions.TouchAttack) { AllowsVitalStrike = false }.Perform(context);
        return shot is AttackActionResult result ? result with { Action = this } : shot;
    }
}
