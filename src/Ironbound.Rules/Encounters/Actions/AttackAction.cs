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

        return new AttackActionResult(
            this,
            context.Actor,
            Strike.Resolve(
                context.Actor,
                Weapon,
                Target,
                context.Random,
                DefenderState,
                context.Rules,
                context.Encounter.Battlefield),
            opportunities);
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
            return field.IsWithinReach(context.Actor, Target);
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
