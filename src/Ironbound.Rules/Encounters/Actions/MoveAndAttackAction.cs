using Ironbound.Rules.Combat;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Feats;
using Ironbound.Rules.Maps;

namespace Ironbound.Rules.Encounters.Actions;

/// <summary>
/// A full round spent walking and striking once somewhere along the way: Spring Attack with a
/// blade, Shot on the Run with a bow. One walk, no further than the creature's speed, broken by
/// one attack at the square named by <see cref="AttackAt"/>.
/// </summary>
/// <remarks>
/// Reported as a <see cref="MoveAndAttackResult"/> that carries both halves of the walk and the
/// attack between them, so that whatever draws a walk and whatever draws a swing can each find
/// theirs.
/// </remarks>
public abstract class MoveAndAttackAction : GameAction
{
    protected MoveAndAttackAction(IReadOnlyList<GridSquare> path, int attackAt, Creature target, WeaponAttack? weapon)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(target);

        Path = path;
        AttackAt = attackAt;
        Target = target;
        Weapon = weapon;
    }

    /// <summary>Every square walked, starting with the one the creature stands in.</summary>
    public IReadOnlyList<GridSquare> Path { get; }

    /// <summary>The index in <see cref="Path"/> of the square the attack is made from.</summary>
    public int AttackAt { get; }

    public Creature Target { get; }

    /// <summary>Null for whatever the creature would swing or shoot.</summary>
    public WeaponAttack? Weapon { get; }

    public override ActionCost Cost => ActionCost.FullRound;

    /// <summary>The feat the action is.</summary>
    protected abstract FeatEffect Feat { get; }

    /// <summary>What it attacks with when nothing was named.</summary>
    protected abstract WeaponAttack? DefaultWeapon(Creature actor);

    /// <summary>Who may swing at it for walking: Spring Attack spares it the one it attacks.</summary>
    protected virtual Func<Creature, bool>? Provokes(Creature actor) => null;

    /// <summary>Anything more the particular feat asks of the walk.</summary>
    protected virtual bool Allows(ActionContext context, Battlefield field) => true;

    public override bool CanPerform(ActionContext context)
    {
        var actor = context.Actor;
        var combatant = context.Combatant;

        if (!actor.HasFeat(Feat)
            || context.Encounter.Battlefield is not { } field
            || Path.Count < 2
            || AttackAt < 0
            || AttackAt >= Path.Count
            || combatant.HasMoved
            || combatant.HasTakenFiveFootStep
            || combatant.IsHeld
            || actor.IsProne
            || !Target.IsAlive
            || ReferenceEquals(Target, actor)
            || (Weapon ?? DefaultWeapon(actor)) is not { } weapon
            || !actor.CanAttackWith(weapon)
            || field.SquareOf(actor) != Path[0]
            || !field.IsFree(Path[^1]))
        {
            return false;
        }

        for (var i = 1; i < Path.Count; i++)
        {
            if (!Distance.AreAdjacent(Path[i - 1], Path[i]) || !field.IsPassable(Path[i]))
            {
                return false;
            }
        }

        // The attack is made from a square it actually stands in.
        if (AttackAt > 0 && !field.IsFree(Path[AttackAt]))
        {
            return false;
        }

        var easy = Math.Max(0, Movement.EasyGroundFeet(actor) - combatant.EasyGroundUsed);
        var owed = combatant.OwesStep ? Distance.FeetPerSquare : 0;
        if (field.PathCost(Path, easy, out _) > actor.CurrentSpeed - owed)
        {
            return false;
        }

        return CanAttackFrom(field, actor, weapon, Path[AttackAt]) && Allows(context, field);
    }

    /// <summary>Whether the weapon reaches the target from that square.</summary>
    private bool CanAttackFrom(Battlefield field, Creature actor, WeaponAttack weapon, GridSquare from)
    {
        if (field.SquareOf(Target) is not { } at)
        {
            return false;
        }

        if (!weapon.IsRanged)
        {
            return Battlefield.ReachOf(actor, weapon).Covers(from, at);
        }

        return field.HasLineOfSight(from, at) && weapon.IsWithinRange(Distance.Between(from, at));
    }

    public override ActionResult Perform(ActionContext context)
    {
        var actor = context.Actor;
        var weapon = Weapon ?? DefaultWeapon(actor)!;

        var before = AttackAt > 0 ? Walk(context, Path.Take(AttackAt + 1).ToList()) : null;
        ActionResult? attack = null;
        MoveActionResult? after = null;

        if (actor.IsConscious && !context.Combatant.IsHeld && Target.IsAlive)
        {
            attack = new AttackAction(weapon, Target) { AllowsVitalStrike = false }.Perform(context);
        }

        if (actor.IsConscious && !context.Combatant.IsHeld && AttackAt < Path.Count - 1)
        {
            after = Walk(context, Path.Skip(AttackAt).ToList());
        }

        context.Combatant.HasMoved = true;
        return new MoveAndAttackResult(this, actor, before, attack as AttackActionResult, after);
    }

    private MoveActionResult Walk(ActionContext context, IReadOnlyList<GridSquare> leg) =>
        (MoveActionResult)new Leg(leg, Provokes(context.Actor)).Perform(context);

    /// <summary>
    /// One leg of the walk: an ordinary move with the checking done by the action as a whole,
    /// since neither leg alone is what the feat measures.
    /// </summary>
    private sealed class Leg(IReadOnlyList<GridSquare> path, Func<Creature, bool>? provokes) : MoveAction(path)
    {
        protected override Func<Creature, bool>? Provokes(ActionContext context) => provokes;
    }
}

/// <summary>Both legs of a walk and the one attack made between them. Any of the three may be missing.</summary>
public sealed record MoveAndAttackResult(
    GameAction Action,
    Creature Actor,
    MoveActionResult? Before,
    AttackActionResult? Attack,
    MoveActionResult? After)
    : ActionResult(Action, Actor, Describe(Before, Attack, After))
{
    private static string Describe(MoveActionResult? before, AttackActionResult? attack, MoveActionResult? after) =>
        string.Join("; ", new[] { before?.Description, attack?.Description, after?.Description }.OfType<string>());
}

/// <summary>
/// Spring Attack: walk, swing once, walk on, without the one struck getting a swing back for
/// any of it. At least ten feet before the swing, and never at somebody it started the turn
/// standing next to.
/// </summary>
public sealed class SpringAttackAction(IReadOnlyList<GridSquare> path, int attackAt, Creature target, WeaponAttack? weapon = null)
    : MoveAndAttackAction(path, attackAt, target, weapon)
{
    /// <summary>How far it has to have moved before the blow.</summary>
    public const int RunUpFeet = 10;

    public override string Name => "spring attack";

    protected override FeatEffect Feat => FeatEffect.SpringAttack;

    protected override WeaponAttack? DefaultWeapon(Creature actor) => actor.MeleeAttack;

    protected override Func<Creature, bool>? Provokes(Creature actor) => other => !ReferenceEquals(other, Target);

    protected override bool Allows(ActionContext context, Battlefield field) =>
        (Weapon ?? DefaultWeapon(context.Actor)) is { IsRanged: false }
        && field.SquareOf(Target) is { } at
        && !Distance.AreAdjacent(Path[0], at)
        && field.PathCost(Path.Take(AttackAt + 1).ToList()) >= RunUpFeet;
}

/// <summary>
/// Shot on the Run: walk, shoot once at any point along the way, and walk on. The walk draws
/// swings as any walk does, and so does a shot loosed in somebody's reach.
/// </summary>
public sealed class ShotOnTheRunAction(IReadOnlyList<GridSquare> path, int attackAt, Creature target, WeaponAttack? weapon = null)
    : MoveAndAttackAction(path, attackAt, target, weapon)
{
    public override string Name => "shot on the run";

    protected override FeatEffect Feat => FeatEffect.ShotOnTheRun;

    protected override WeaponAttack? DefaultWeapon(Creature actor) =>
        actor.Attacks.FirstOrDefault(weapon => weapon.IsRanged && !weapon.IsThrownUse && actor.CanAttackWith(weapon));

    protected override bool Allows(ActionContext context, Battlefield field) =>
        (Weapon ?? DefaultWeapon(context.Actor)) is { IsRanged: true };
}
