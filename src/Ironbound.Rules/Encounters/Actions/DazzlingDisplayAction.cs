using Ironbound.Rules.Classes;
using Ironbound.Rules.Combat;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Feats;
using Ironbound.Rules.Skills;

namespace Ironbound.Rules.Encounters.Actions;

/// <summary>
/// Dazzling Display: a whole round spent flourishing the weapon its wielder has focused on,
/// and one Intimidate check to demoralise every foe within thirty feet who can see it.
/// </summary>
public sealed class DazzlingDisplayAction : GameAction
{
    public override string Name => "dazzling display";

    public override ActionCost Cost => ActionCost.FullRound;

    public override bool CanPerform(ActionContext context) =>
        context.Actor.HasFeat(FeatEffect.DazzlingDisplay) && FocusedWeapon(context.Actor) is not null;

    public override ActionResult Perform(ActionContext context)
    {
        var actor = context.Actor;
        var foes = context.Encounter.Order
            .Select(combatant => combatant.Creature)
            .Where(other => other.IsConscious
                && actor.IsEnemyOf(other)
                && DemoralizeAction.CanSee(context.Encounter.Battlefield, actor, other))
            .ToList();

        // One roll for the whole display, met against each foe's own difficulty.
        var roll = actor.Skills.Check(Skill.Intimidate, context.Random);
        var checks = new List<SkillCheck>();
        var outcomes = new List<string>();

        foreach (var foe in foes)
        {
            var dc = DemoralizeAction.DifficultyFor(foe);
            var against = roll with { Difficulty = dc };
            checks.Add(against);
            outcomes.Add(DemoralizeAction.Shake(foe, against, dc));
        }

        return new SkillActionResult(this, actor, checks, outcomes);
    }

    /// <summary>The weapon in hand that the actor has Weapon Focus in, if any: what it flourishes.</summary>
    public static WeaponAttack? FocusedWeapon(Creature actor) =>
        new[] { actor.MeleeAttack, actor.PrimaryAttack }
            .OfType<WeaponAttack>()
            .FirstOrDefault(weapon => Martial.HasFeatFor(actor, FeatEffect.WeaponFocus, weapon));
}
