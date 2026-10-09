using Ironbound.Rules.Combat;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Feats;

namespace Ironbound.Rules.Encounters.Actions;

/// <summary>
/// Whirlwind Attack: the whole round given to one swing, at the full bonus, at every foe in
/// reach.
/// </summary>
/// <remarks>
/// Reported as a full attack, because that is what it looks like — one creature swinging more
/// than once — so whatever shows a flurry shows this. The book forbids adding Cleave's or Great
/// Cleave's extra swings to it; nothing here would, since those are actions of their own.
/// </remarks>
public sealed class WhirlwindAttackAction : GameAction
{
    public override string Name => "whirlwind attack";

    public override ActionCost Cost => ActionCost.FullRound;

    public override bool CanPerform(ActionContext context) =>
        context.Actor.HasFeat(FeatEffect.WhirlwindAttack)
        && context.Actor.MeleeAttack is { } weapon
        && Foes(context, weapon).Any();

    public override ActionResult Perform(ActionContext context)
    {
        var actor = context.Actor;
        var weapon = actor.MeleeAttack!;
        var strikes = new List<StrikeResult>();

        foreach (var foe in Foes(context, weapon).ToList())
        {
            if (!actor.IsConscious || !actor.CanAttackWith(weapon))
            {
                break;
            }

            var strike = Strike.Resolve(
                actor,
                weapon,
                foe,
                context.Random,
                rules: context.Rules,
                field: context.Encounter.Battlefield,
                flatFooted: context.Encounter.IsFlatFootedTo(foe, actor));

            context.Encounter.AfterStrike(strike);
            strikes.Add(strike);
        }

        return new FullAttackResult(this, actor, weapon, strikes, []);
    }

    /// <summary>Every conscious foe within reach, in initiative order so a replay swings the same way.</summary>
    private static IEnumerable<Creature> Foes(ActionContext context, WeaponAttack weapon) =>
        context.Encounter.Order
            .Select(combatant => combatant.Creature)
            .Where(other => other.IsConscious
                && context.Actor.IsEnemyOf(other)
                && context.Encounter.Battlefield is not null
                && Movement.Reaches(context, context.Actor, other, weapon));
}
