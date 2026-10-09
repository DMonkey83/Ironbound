using Ironbound.Rules.Abilities;
using Ironbound.Rules.Classes;
using Ironbound.Rules.Combat;
using Ironbound.Rules.Creatures;

namespace Ironbound.Rules.Encounters.Actions;

/// <summary>
/// A raging barbarian's knockback: a bull rush in place of a swing that does not provoke, and
/// hurts — her Strength bonus in damage on a success.
/// </summary>
/// <remarks>
/// A bull rush in every other respect, and derived from one so that anything which shows a
/// shove shows this as one. "Once a round" comes for free: it replaces the one attack a standard
/// action makes, and a creature has one standard action a round.
/// </remarks>
public sealed class KnockbackAction(Creature target) : BullRushAction(target)
{
    public override string Name => "knockback";

    public override bool CanPerform(ActionContext context) =>
        base.CanPerform(context)
        && Rage.IsRaging(context.Actor)
        && context.Actor.Choices.HasTalent(TalentEffect.Knockback);

    protected override bool Provokes(ActionContext context) => false;

    protected override string Apply(ActionContext context, ManeuverResult check)
    {
        var shoved = base.Apply(context, check);

        if (!check.Succeeded)
        {
            return shoved;
        }

        var strength = Math.Max(0, context.Actor.Abilities[Ability.Strength].Modifier);
        if (strength == 0)
        {
            return shoved;
        }

        // Plain bludgeoning damage, through the target's defences like any other blow.
        var hit = DamagePacket.Weapon(strength.ToString(), DamageType.Bludgeoning).Roll(context.Random);
        var taken = Target.Defenses.Apply(hit, Defense.DamageBypass.None, context.Rules);
        Target.HitPoints.Take(taken.Total);

        var hurt = $"{Target.Name} takes {taken.Total} ({Target.HitPoints})";
        return shoved.Length == 0 ? hurt : $"{shoved}; {hurt}";
    }
}
