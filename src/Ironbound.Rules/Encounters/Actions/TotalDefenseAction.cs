using Ironbound.Rules.Effects;
using Ironbound.Rules.Modifiers;

namespace Ironbound.Rules.Encounters.Actions;

/// <summary>
/// Giving up the attack to cover up: +4 dodge to armour class until the start of your next turn.
/// </summary>
/// <remarks>
/// Small, but it is the thing that proves the per-combatant round clock works. The effect lasts
/// one round from the moment it is applied, which under this scheduler is exactly the actor's own
/// next turn — it covers every other combatant's turn in between and no longer.
/// </remarks>
public sealed class TotalDefenseAction : GameAction
{
    public const int DodgeBonus = 4;

    public const string EffectName = "Total Defence";

    public override string Name => "total defence";

    public override ActionCost Cost => ActionCost.Standard;

    public override ActionResult Perform(ActionContext context)
    {
        context.Actor.Effects.Apply(new ModifierEffect(EffectName, Duration.Rounds(1))
            .GrantsToArmorClass(DodgeBonus, BonusType.Dodge));

        return new ActionResult(
            this,
            context.Actor,
            $"{context.Actor.Name} fights defensively (+{DodgeBonus} dodge until their next turn)");
    }
}
