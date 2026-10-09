using Ironbound.Rules.Combat;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Effects;
using Ironbound.Rules.Feats;
using Ironbound.Rules.Maps;
using Ironbound.Rules.Modifiers;

namespace Ironbound.Rules.Encounters.Actions;

/// <summary>
/// Cleave: one swing, and if it lands, the same swing carried on into a second foe beside the
/// first. Two points of armour class are the price, until the cleaver's next turn.
/// </summary>
/// <remarks>
/// A standard action, so it competes with a single attack rather than a full one — which is the
/// whole design of the feat: something to do with a turn spent moving that is better than one
/// swing when the enemy is bunched up.
/// <para>
/// Reported as a <see cref="FullAttackResult"/>, because that is what it looks like: one creature
/// swinging more than once. Anything that shows a flurry of strikes shows this.
/// </para>
/// </remarks>
public sealed class CleaveAction : GameAction
{
    public const int ArmourPenalty = -2;

    public const string EffectName = "Cleave";

    public CleaveAction(Creature target, WeaponAttack? weapon = null)
    {
        ArgumentNullException.ThrowIfNull(target);
        Target = target;
        Weapon = weapon;
    }

    public Creature Target { get; }

    public WeaponAttack? Weapon { get; }

    public override string Name => "cleave";

    public override ActionCost Cost => ActionCost.Standard;

    public override bool CanPerform(ActionContext context) =>
        context.Actor.HasFeat(FeatEffect.Cleave)
        && Target.IsAlive
        && !ReferenceEquals(Target, context.Actor)
        && WeaponFor(context.Actor) is { IsRanged: false } weapon
        && context.Actor.CanAttackWith(weapon)
        && (context.Encounter.Battlefield is not { } field || field.IsWithinReach(context.Actor, Target, weapon));

    public override ActionResult Perform(ActionContext context)
    {
        var actor = context.Actor;
        var weapon = WeaponFor(actor)!;
        var field = context.Encounter.Battlefield;

        actor.Effects.Apply(new ModifierEffect(EffectName, Duration.Rounds(1))
            .GrantsToArmorClass(ArmourPenalty, BonusType.Untyped));

        var strikes = new List<StrikeResult> { Swing(context, actor, weapon, Target) };

        if (strikes[0].IsHit && field is not null && NextFoe(field, actor, weapon) is { } second)
        {
            strikes.Add(Swing(context, actor, weapon, second));
        }

        return new FullAttackResult(this, actor, weapon, strikes, []);
    }

    private static StrikeResult Swing(ActionContext context, Creature actor, WeaponAttack weapon, Creature target) =>
        Strike.Resolve(
            actor,
            weapon,
            target,
            context.Random,
            rules: context.Rules,
            field: context.Encounter.Battlefield,
            flatFooted: context.Encounter.IsFlatFootedTo(target, actor));

    /// <summary>A foe standing next to the first and within the cleaver's reach, in placement order.</summary>
    private Creature? NextFoe(Battlefield field, Creature actor, WeaponAttack weapon)
    {
        if (field.SquareOf(Target) is not { } first)
        {
            return null;
        }

        return field.Creatures.FirstOrDefault(other =>
            !ReferenceEquals(other, Target)
            && other.IsConscious
            && actor.IsEnemyOf(other)
            && field.SquareOf(other) is { } square
            && Distance.AreAdjacent(square, first)
            && field.IsWithinReach(actor, other, weapon));
    }

    private WeaponAttack? WeaponFor(Creature actor) => Weapon ?? actor.MeleeAttack;
}
