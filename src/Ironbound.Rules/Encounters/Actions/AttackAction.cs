using Ironbound.Rules.Combat;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Defense;

namespace Ironbound.Rules.Encounters.Actions;

/// <summary>Swinging at something, or shooting it. A standard action wrapping <see cref="Strike"/>.</summary>
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
        && context.Actor.CanAttackWith(Weapon)
        && CanReach(context);

    public override ActionResult Perform(ActionContext context)
    {
        // Drawing a bow in somebody's face is as careless as casting: the swing comes first, and
        // it can drop you before you loose. A melee swing provokes nothing — it is the expected
        // thing to be doing in a threatened square.
        var opportunities = Weapon.IsRanged ? Provoke(context) : [];

        if (!context.Actor.IsConscious)
        {
            return new AttackActionResult(this, context.Actor, null, opportunities);
        }

        // Vital Strike belongs to exactly this action — one attack, at the full bonus — and
        // costs nothing, so anybody with the feat always uses it.
        var strike = Strike.Resolve(
            context.Actor,
            Weapon,
            Target,
            context.Random,
            DefenderState,
            context.Rules,
            context.Encounter.Battlefield,
            flatFooted: context.Encounter.IsFlatFootedTo(Target, context.Actor),
            vital: context.Actor.HasFeat(Feats.FeatEffect.VitalStrike));

        var followUps = strike.IsHit && !Weapon.IsRanged
            ? Opportunities.Opportunist(context.Encounter, context.Actor, Target)
            : [];

        return new AttackActionResult(this, context.Actor, strike, opportunities) { FollowUps = followUps };
    }

    /// <summary>
    /// A melee weapon has to be able to touch the target; a ranged one has to be able to see it,
    /// and to carry that far.
    /// </summary>
    private bool CanReach(ActionContext context)
    {
        if (context.Encounter.Battlefield is not { } field)
        {
            return true;
        }

        if (!Weapon.IsRanged)
        {
            return field.IsWithinReach(context.Actor, Target, Weapon);
        }

        return field.HasLineOfSight(context.Actor, Target)
            && (field.DistanceInFeet(context.Actor, Target) is not { } feet
                || Weapon.IsWithinRange(feet));
    }

    private IReadOnlyList<StrikeResult> Provoke(ActionContext context) =>
        context.Encounter.Battlefield?.SquareOf(context.Actor) is { } standing
            ? Opportunities.Provoke(context.Encounter, context.Actor, standing)
            : [];
}

/// <summary>
/// An attack's outcome, carrying the strike itself rather than only a log line.
/// </summary>
/// <remarks>
/// <see cref="Strike"/> is null when the attacker was cut down by an opportunity before the shot
/// went off — the action happened, the attack never did.
/// </remarks>
public sealed record AttackActionResult(
    GameAction Action,
    Creature Actor,
    StrikeResult? Strike,
    IReadOnlyList<StrikeResult> Opportunities)
    : ActionResult(Action, Actor, Describe(Actor, Action, Strike, Opportunities))
{
    /// <summary>Swings an ally's opportunist talent took at the target afterwards.</summary>
    public IReadOnlyList<StrikeResult> FollowUps { get; init; } = [];

    private static string Describe(
        Creature actor,
        GameAction action,
        StrikeResult? strike,
        IReadOnlyList<StrikeResult> opportunities)
    {
        if (strike is null)
        {
            return $"{actor.Name} is cut down drawing {action.Name}";
        }

        return opportunities.Count > 0
            ? $"{strike}, provoking {opportunities.Count}"
            : strike.ToString();
    }
}
