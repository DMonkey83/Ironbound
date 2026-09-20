using Ironbound.Rules.Dice;
using Ironbound.Rules.Encounters;
using Ironbound.Rules.Encounters.Actions;

namespace Ironbound.Simulation;

/// <summary>
/// An opponent that scores its options with rules of thumb. It never simulates a hypothetical
/// board, which is the deliberate ceiling chosen for this project.
/// </summary>
/// <remarks>
/// <see cref="Competence"/> is the difficulty lever: at 100 it focuses the weakest target and
/// covers up when badly hurt; at 0 it flails at whoever it happens to notice and never guards.
/// Difficulty is expressed as the opponent playing worse rather than as fudged numbers, so the
/// rules stay honest at every setting.
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

        // One standard action a turn, and nothing else worth doing yet.
        if (!turn.Budget.HasStandard)
        {
            return null;
        }

        var actor = turn.Actor;
        var enemies = _battle.EnemiesOf(actor);
        if (enemies.Count == 0)
        {
            return null;
        }

        var playsWell = Competence == 100 || _random.Next(0, 100) < Competence;

        if (playsWell && IsBadlyHurt(actor))
        {
            return new TotalDefenseAction();
        }

        var target = playsWell
            ? enemies.MinBy(enemy => enemy.HitPoints.Current)!
            : enemies[_random.Next(0, enemies.Count)];

        return new AttackAction(_battle.WeaponOf(actor), target);
    }

    private static bool IsBadlyHurt(Rules.Creatures.Creature creature) =>
        creature.HitPoints.Current * 100 <= creature.HitPoints.Maximum * GuardBelowPercent;
}
