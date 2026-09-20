using Ironbound.Rules.Combat;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Defense;

namespace Ironbound.Rules.Encounters.Actions;

/// <summary>Swinging at something. A standard action wrapping <see cref="Strike"/>.</summary>
public sealed class AttackAction : GameAction
{
    public AttackAction(WeaponAttack weapon, Creature target, DefenseOptions defenderState = DefenseOptions.None)
    {
        ArgumentNullException.ThrowIfNull(weapon);
        ArgumentNullException.ThrowIfNull(target);

        Weapon = weapon;
        Target = target;
        DefenderState = defenderState;
    }

    public WeaponAttack Weapon { get; }

    public Creature Target { get; }

    public DefenseOptions DefenderState { get; }

    public override string Name => Weapon.Name;

    public override ActionCost Cost => ActionCost.Standard;

    public override bool CanPerform(ActionContext context) =>
        Target.IsAlive
        && !ReferenceEquals(Target, context.Actor)
        && (context.Encounter.Battlefield is not { } field
            || field.IsWithinReach(context.Actor, Target));

    public override ActionResult Perform(ActionContext context) =>
        new AttackActionResult(
            this,
            context.Actor,
            Strike.Resolve(context.Actor, Weapon, Target, context.Random, DefenderState, context.Rules));
}

/// <summary>An attack's outcome, carrying the strike itself rather than only a log line.</summary>
public sealed record AttackActionResult(GameAction Action, Creature Actor, StrikeResult Strike)
    : ActionResult(Action, Actor, Strike.ToString());
