using Ironbound.Rules.Abilities;
using Ironbound.Rules.Classes;
using Ironbound.Rules.Combat;
using Ironbound.Rules.Conditions;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Effects;
using Ironbound.Rules.Feats;

namespace Ironbound.Rules.Encounters.Actions;

/// <summary>
/// Deadly Stroke: one blow, a standard action, with the weapon its wielder has Greater Weapon
/// Focus in, at a foe who is stunned or flat-footed. If it lands it does double damage and
/// opens a wound that bleeds a point of Constitution a round.
/// </summary>
public sealed class DeadlyStrokeAction : GameAction
{
    public DeadlyStrokeAction(Creature target)
    {
        ArgumentNullException.ThrowIfNull(target);
        Target = target;
    }

    public Creature Target { get; }

    public override string Name => "deadly stroke";

    public override ActionCost Cost => ActionCost.Standard;

    public override bool CanPerform(ActionContext context) =>
        context.Actor.HasFeat(FeatEffect.DeadlyStroke)
        && Weapon(context.Actor) is { } weapon
        && Target.IsAlive
        && (Target.Has(Condition.Stunned) || context.Encounter.IsFlatFootedTo(Target, context.Actor))
        && Movement.Reaches(context, context.Actor, Target, weapon);

    public override ActionResult Perform(ActionContext context)
    {
        var actor = context.Actor;
        var weapon = Weapon(actor)!;

        var strike = Strike.Resolve(
            actor,
            weapon,
            Target,
            context.Random,
            rules: context.Rules,
            field: context.Encounter.Battlefield,
            flatFooted: context.Encounter.IsFlatFootedTo(Target, actor),
            doubled: true);

        context.Encounter.AfterStrike(strike);

        if (strike.IsHit && Target.IsAlive)
        {
            Target.Effects.Apply(new AbilityBleedEffect(Bleeds.DeadlyStrokeLabel, Ability.Constitution, 1));
            strike = strike with { Notes = [.. strike.Notes, $"{Target.Name} bleeds a point of Constitution a round"] };
        }

        return new AttackActionResult(this, actor, strike, []);
    }

    /// <summary>The melee weapon in hand that its wielder has Greater Weapon Focus in.</summary>
    private static WeaponAttack? Weapon(Creature actor) =>
        actor.MeleeAttack is { } weapon && Martial.HasFeatFor(actor, FeatEffect.GreaterWeaponFocus, weapon) ? weapon : null;
}
