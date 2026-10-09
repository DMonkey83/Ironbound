using Ironbound.Rules.Combat;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Defense;

namespace Ironbound.Rules.Encounters.Actions;

/// <summary>
/// Standing your ground and swinging everything you have.
/// </summary>
/// <remarks>
/// The decision the whole positional game turns on. A full attack costs the entire round, so it
/// buys extra swings with the movement you would otherwise have spent — which is why a
/// well-placed fighter is worth so much more than a fast one, and why dragging somebody out of
/// position costs them far more than the five feet suggests.
/// <para>
/// A five-foot step is still allowed, because it is free rather than a move action. That single
/// exception is what keeps a full attack from pinning you to one square.
/// </para>
/// </remarks>
public sealed class FullAttackAction : GameAction
{
    public FullAttackAction(
        Creature target,
        WeaponAttack? weapon = null,
        DefenseOptions defenderState = DefenseOptions.None)
    {
        ArgumentNullException.ThrowIfNull(target);

        Target = target;
        Weapon = weapon;
        DefenderState = defenderState;
    }

    public Creature Target { get; }

    /// <summary>Null means whatever the actor would reach for, decided when the action is taken.</summary>
    public WeaponAttack? Weapon { get; }

    public DefenseOptions DefenderState { get; }

    public override string Name => "full attack";

    public override ActionCost Cost => ActionCost.FullRound;

    public override bool CanPerform(ActionContext context) =>
        Target.IsAlive
        && !ReferenceEquals(Target, context.Actor)
        && WeaponFor(context.Actor) is { } weapon
        && context.Actor.CanAttackWith(weapon)
        && Reaches(context, weapon);

    public override ActionResult Perform(ActionContext context)
    {
        var actor = context.Actor;
        var weapon = WeaponFor(actor)!;

        // Whatever the shot provokes, it provokes once — not once a swing.
        var opportunities = weapon.IsRanged ? Provoke(context) : [];
        var strikes = new List<StrikeResult>();

        var followUps = new List<StrikeResult>();

        if (actor.IsConscious)
        {
            // Manyshot: the first arrow of a full attack with a bow is two.
            var arrows = IsManyshot(actor, weapon) ? 2 : 1;

            foreach (var penalty in Penalties(actor, weapon))
            {
                var strike = Strike.Resolve(
                    actor,
                    weapon,
                    Target,
                    context.Random,
                    DefenderState,
                    context.Rules,
                    context.Encounter.Battlefield,
                    penalty,
                    context.Encounter.IsFlatFootedTo(Target, actor),
                    arrows: arrows);

                arrows = 1;
                context.Encounter.AfterStrike(strike);
                strikes.Add(strike);

                if (strike.IsHit && !weapon.IsRanged)
                {
                    followUps.AddRange(Opportunities.Opportunist(context.Encounter, actor, Target));
                }

                // No point hacking at something already down, and stopping keeps the random
                // stream where a replay expects to find it. Nor in swinging a weapon that has
                // just been thrown, shattered or let go of.
                if (!Target.IsConscious || !actor.CanAttackWith(weapon))
                {
                    break;
                }
            }
        }

        return new FullAttackResult(this, actor, weapon, strikes, opportunities) { FollowUps = followUps };
    }

    /// <summary>Whether this full attack opens with two arrows: Manyshot, and a bow to shoot them from.</summary>
    public static bool IsManyshot(Creature actor, WeaponAttack weapon) =>
        weapon.IsRanged
        && !weapon.IsThrownUse
        && weapon.Groups.Contains("bows")
        && actor.HasFeat(Feats.FeatEffect.Manyshot);

    /// <summary>Rapid Shot's penalty on every shot of a full attack, and the extra one it buys.</summary>
    public const int RapidShotPenalty = -2;

    /// <summary>
    /// The penalty on each swing in order. Rapid Shot puts an extra shot at the front, at the
    /// full bonus, and takes two off every one of them — always worth it, so always taken.
    /// </summary>
    public static IReadOnlyList<int> Penalties(Creature actor, WeaponAttack weapon)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(weapon);

        var penalties = Iteratives.Penalties(actor.BaseAttackBonus);

        if (!weapon.IsRanged || !actor.HasFeat(Feats.FeatEffect.RapidShot))
        {
            return penalties;
        }

        return [.. new[] { 0 }.Concat(penalties).Select(penalty => penalty + RapidShotPenalty)];
    }

    /// <summary>
    /// What the actor swings: whatever was asked for, else a blade if the target is close enough
    /// and a bow otherwise.
    /// </summary>
    private WeaponAttack? WeaponFor(Creature actor)
    {
        if (Weapon is not null)
        {
            return Weapon;
        }

        return actor.MeleeAttack ?? actor.PrimaryAttack;
    }

    private bool Reaches(ActionContext context, WeaponAttack weapon)
    {
        if (context.Encounter.Battlefield is not { } field)
        {
            return true;
        }

        if (!weapon.IsRanged)
        {
            return Movement.Reaches(context, context.Actor, Target, weapon);
        }

        return field.HasLineOfSight(context.Actor, Target)
            && (field.DistanceInFeet(context.Actor, Target) is not { } feet
                || weapon.IsWithinRange(feet));
    }

    private IReadOnlyList<StrikeResult> Provoke(ActionContext context) =>
        context.Encounter.Battlefield?.SquareOf(context.Actor) is { } standing
            ? Opportunities.Provoke(context.Encounter, context.Actor, standing)
            : [];
}

/// <summary>Every swing of a full attack, in the order they were thrown.</summary>
public sealed record FullAttackResult(
    GameAction Action,
    Creature Actor,
    WeaponAttack Weapon,
    IReadOnlyList<StrikeResult> Strikes,
    IReadOnlyList<StrikeResult> Opportunities)
    : ActionResult(Action, Actor, Describe(Actor, Weapon, Strikes, Opportunities))
{
    /// <summary>Swings an ally's opportunist talent took at the target in between.</summary>
    public IReadOnlyList<StrikeResult> FollowUps { get; init; } = [];

    private static string Describe(
        Creature actor,
        WeaponAttack weapon,
        IReadOnlyList<StrikeResult> strikes,
        IReadOnlyList<StrikeResult> opportunities)
    {
        if (strikes.Count == 0)
        {
            return $"{actor.Name} is cut down before swinging";
        }

        var swings = strikes.Count == 1 ? "once" : $"{strikes.Count} times";
        var provoked = opportunities.Count > 0 ? $", provoking {opportunities.Count}" : string.Empty;

        return $"{actor.Name} attacks {swings} with {weapon.Name}{provoked}";
    }
}
