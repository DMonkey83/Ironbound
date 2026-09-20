using Ironbound.Rules.Creatures;
using Ironbound.Rules.Dice;
using Ironbound.Rules.Encounters;
using Ironbound.Rules.Encounters.Actions;
using Ironbound.Rules.Maps;

namespace Ironbound.Simulation;

/// <summary>
/// An opponent that scores its options with rules of thumb. It never simulates a hypothetical
/// board, which is the deliberate ceiling chosen for this project.
/// </summary>
/// <remarks>
/// <see cref="Competence"/> is the difficulty lever: at 100 it focuses the weakest target, steps
/// rather than walks when a step will do, and covers up when badly hurt; at 0 it flails at
/// whoever it happens to notice. Difficulty is expressed as the opponent playing worse rather
/// than as fudged numbers, so the rules stay honest at every setting.
/// </remarks>
public sealed class HeuristicActionSource : IActionSource
{
    /// <summary>Below this share of its hit points, a competent creature stops swinging.</summary>
    public const int GuardBelowPercent = 25;

    private readonly Battle _battle;
    private readonly IRandomSource _random;

    /// <param name="random">Its own stream, separate from the combat dice, so that thinking
    /// harder never changes what the dice do.</param>
    public HeuristicActionSource(Battle battle, IRandomSource random, int competence = 100)
    {
        ArgumentNullException.ThrowIfNull(battle);
        ArgumentNullException.ThrowIfNull(random);
        ArgumentOutOfRangeException.ThrowIfNegative(competence);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(competence, 100);

        _battle = battle;
        _random = random;
        Competence = competence;
    }

    public int Competence { get; }

    public GameAction? NextAction(Turn turn)
    {
        ArgumentNullException.ThrowIfNull(turn);

        var actor = turn.Actor;
        var enemies = _battle.EnemiesOf(actor);
        if (enemies.Count == 0 || actor.PrimaryAttack is not { } weapon)
        {
            return null;
        }

        var playsWell = Competence == 100 || _random.Next(0, 100) < Competence;

        if (playsWell && IsBadlyHurt(actor) && turn.Budget.HasStandard)
        {
            return new TotalDefenseAction();
        }

        var target = playsWell
            ? enemies.MinBy(enemy => enemy.HitPoints.Current)!
            : enemies[_random.Next(0, enemies.Count)];

        if (turn.Encounter.Battlefield is { } field && !field.IsWithinReach(actor, target))
        {
            // One square short: a five-foot step closes it for free and keeps the swing. Walking
            // would spend the move action and, worse, hand anyone nearby a free attack.
            if (playsWell && Step(field, actor, target, mustFlank: false) is { } step)
            {
                return step;
            }

            return turn.Budget.CanAfford(ActionCost.Move)
                ? MoveAction.Towards(field, actor, target)
                : null;
        }

        // Already in reach, but standing in the wrong place. A five-foot step costs nothing and
        // arriving on the far side is worth two on this swing and every one after it.
        if (playsWell
            && turn.Encounter.Battlefield is { } ground
            && !turn.Combatant.HasMoved
            && !turn.Combatant.HasTakenFiveFootStep
            && ground.FindFlankingPartner(actor, target) is null
            && Step(ground, actor, target, mustFlank: true) is { } reposition)
        {
            return reposition;
        }

        return turn.Budget.HasStandard ? new AttackAction(weapon, target) : null;
    }

    /// <summary>
    /// A single free square next door that keeps the target in reach. Prefers one that puts an
    /// ally directly opposite; with <paramref name="mustFlank"/> it will accept nothing else.
    /// </summary>
    private static FiveFootStepAction? Step(
        Battlefield field,
        Creature actor,
        Creature target,
        bool mustFlank)
    {
        if (field.SquareOf(actor) is not { } from || field.SquareOf(target) is not { } to)
        {
            return null;
        }

        GridSquare? plain = null;

        for (var dx = -1; dx <= 1; dx++)
        {
            for (var dy = -1; dy <= 1; dy++)
            {
                if (dx == 0 && dy == 0)
                {
                    continue;
                }

                var candidate = new GridSquare(from.X + dx, from.Y + dy);
                if (!field.IsFree(candidate)
                    || field.IsDifficult(candidate)
                    || Distance.Between(candidate, to) > actor.Reach)
                {
                    continue;
                }

                if (WouldFlank(field, actor, target, candidate, to))
                {
                    return FiveFootStepAction.To(from, candidate);
                }

                if (!mustFlank)
                {
                    plain ??= candidate;
                }
            }
        }

        return plain is { } fallback ? FiveFootStepAction.To(from, fallback) : null;
    }

    /// <summary>
    /// Whether standing in <paramref name="candidate"/> would put an ally on the far side of the
    /// target. Worked out from the geometry rather than by moving anybody to find out.
    /// </summary>
    private static bool WouldFlank(
        Battlefield field,
        Creature actor,
        Creature target,
        GridSquare candidate,
        GridSquare targetSquare)
    {
        var opposite = new GridSquare(
            (2 * targetSquare.X) - candidate.X,
            (2 * targetSquare.Y) - candidate.Y);

        return field.OccupantOf(opposite) is { } ally
            && ally.IsAllyOf(actor)
            && ally.IsEnemyOf(target)
            && field.Threatens(ally, targetSquare);
    }

    private static bool IsBadlyHurt(Creature creature) =>
        creature.HitPoints.Current * 100 <= creature.HitPoints.Maximum * GuardBelowPercent;
}
