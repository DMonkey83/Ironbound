using Ironbound.Rules.Abilities;
using Ironbound.Rules.Conditions;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Dice;
using Ironbound.Rules.Effects;
using Ironbound.Rules.Maps;
using Ironbound.Rules.Skills;

namespace Ironbound.Rules.Encounters.Actions;

/// <summary>
/// Demoralising a foe with Intimidate: shaken for a round, and a round more for every five the
/// check beats the difficulty by.
/// </summary>
/// <remarks>
/// A standard action against somebody within thirty feet who can see you. The difficulty is ten,
/// plus the target's hit dice, plus its Wisdom modifier. Doing it again to somebody already
/// shaken by it only makes it last longer; it never makes them frightened.
/// </remarks>
public sealed class DemoralizeAction : GameAction
{
    /// <summary>How close the target has to be.</summary>
    public const int RangeFeet = 30;

    /// <summary>What the shaken is filed under, so a second demoralise lengthens the first.</summary>
    public const string EffectName = "Demoralized";

    public DemoralizeAction(Creature target)
    {
        ArgumentNullException.ThrowIfNull(target);
        Target = target;
    }

    public Creature Target { get; }

    public override string Name => "demoralize";

    public override ActionCost Cost => ActionCost.Standard;

    public override bool CanPerform(ActionContext context) =>
        Target.IsConscious
        && context.Actor.IsEnemyOf(Target)
        && CanSee(context.Encounter.Battlefield, context.Actor, Target);

    public override ActionResult Perform(ActionContext context)
    {
        var (check, outcome) = Frighten(context.Actor, Target, context.Random);
        return new SkillActionResult(this, context.Actor, [check], [outcome]);
    }

    /// <summary>Ten, the target's hit dice, and its Wisdom: what an Intimidate check has to meet.</summary>
    public static int DifficultyFor(Creature target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return 10 + target.HitPoints.HitDice + target.Abilities[Ability.Wisdom].Modifier;
    }

    /// <summary>
    /// Whether the target is close enough and has the actor in sight: within thirty feet and a
    /// line of sight. Without a map, everybody can see everybody.
    /// </summary>
    public static bool CanSee(Battlefield? field, Creature actor, Creature target) =>
        field is null
        || (field.DistanceInFeet(actor, target) is not { } feet || feet <= RangeFeet)
            && field.HasLineOfSight(target, actor)
            && !target.Has(Condition.Blinded);

    /// <summary>One check against one target, and its shaking if it lands.</summary>
    internal static (SkillCheck Check, string Outcome) Frighten(Creature actor, Creature target, IRandomSource random)
    {
        var dc = DifficultyFor(target);
        var check = actor.Skills.Check(Skill.Intimidate, random, dc);
        return (check, Shake(target, check, dc));
    }

    /// <summary>Shakes the target for as long as a check that beat <paramref name="dc"/> says.</summary>
    public static string Shake(Creature target, SkillCheck check, int dc)
    {
        if (check.Untrained || check.Total < dc)
        {
            return $"{target.Name} is not impressed";
        }

        var rounds = 1 + ((check.Total - dc) / 5);

        // Lengthened, never deepened: a second demoralise adds to the first rather than making
        // the target frightened.
        var ticks = Duration.Rounds(rounds).Ticks;
        if (target.Effects.Find(EffectName) is { } running)
        {
            ticks += running.TicksRemaining;
        }

        target.Effects.Apply(ConditionInfo.Effect(Condition.Shaken, Duration.FromTicks(ticks), EffectName));
        return $"{target.Name} is shaken for {rounds} round(s)";
    }
}

/// <summary>One or more skill checks made as an action, and what each did.</summary>
public sealed record SkillActionResult(
    GameAction Action,
    Creature Actor,
    IReadOnlyList<SkillCheck> Checks,
    IReadOnlyList<string> Outcomes)
    : ActionResult(Action, Actor, Describe(Checks, Outcomes))
{
    private static string Describe(IReadOnlyList<SkillCheck> checks, IReadOnlyList<string> outcomes) =>
        checks.Count == 0
            ? "nobody is close enough to see it"
            : string.Join("; ", checks.Zip(outcomes, (check, outcome) => $"{check} — {outcome}"));
}
