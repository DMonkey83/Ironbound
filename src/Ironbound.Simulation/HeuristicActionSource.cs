using Ironbound.Rules.Combat;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Dice;
using Ironbound.Rules.Encounters;
using Ironbound.Rules.Encounters.Actions;
using Ironbound.Rules.Magic;
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
        if (enemies.Count == 0)
        {
            return null;
        }

        var playsWell = Competence == 100 || _random.Next(0, 100) < Competence;

        if (playsWell && IsBadlyHurt(actor) && turn.Budget.HasStandard)
        {
            return new TotalDefenseAction();
        }

        var target = playsWell
            ? Choose(turn, actor, enemies)
            : enemies[_random.Next(0, enemies.Count)];

        if (playsWell && turn.Budget.HasStandard && ChooseSpell(turn, actor, target) is { } cast)
        {
            return cast;
        }

        // Having just thrown a spell, a caster does not then stroll into the front line.
        if (turn.Taken.Any(taken => taken is CastSpellResult))
        {
            return null;
        }

        // Nor, having just loosed an arrow, does an archer. Walking into reach would trade a shot
        // it already has for a swing it does not, and hand out a free attack on the way in.
        if (turn.Taken.Any(taken =>
            taken is AttackActionResult { Strike.Weapon.IsRanged: true }
                or FullAttackResult { Weapon.IsRanged: true }))
        {
            return null;
        }

        // An archer with a clear shot has no business charging. Closing would cost the shot and
        // hand out a free swing on the way in, and the line only gets worse once the melee it is
        // firing into closes around the target.
        if (playsWell
            && turn.Budget.HasStandard
            && turn.Encounter.Battlefield is { } range
            && !range.IsWithinReach(actor, target)
            && BestShot(range, actor, target) is { } bow)
        {
            return Swing(turn, bow, target);
        }

        if (turn.Encounter.Battlefield is { } field && !field.IsWithinReach(actor, target))
        {
            var walk = turn.Budget.CanAfford(ActionCost.Move)
                ? MoveAction.Towards(field, actor, target)
                : null;

            if (playsWell)
            {
                // Closing and flanking at once, for free: nothing beats it.
                if (Step(field, actor, target, mustFlank: true) is { } flankingStep)
                {
                    return flankingStep;
                }

                // A walk that ends on the far side beats a free step that ends on the near one:
                // arriving badly placed means never moving again this fight.
                if (walk?.Destination is { } destination
                    && field.WouldFlankFrom(destination, actor, target))
                {
                    return walk;
                }

                // Otherwise a free step to close is better value than spending the move action.
                if (Step(field, actor, target, mustFlank: false) is { } step)
                {
                    return step;
                }
            }

            return walk;
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

        // A wizard out of spells has nothing useful left, and that is a legitimate answer.
        return InClose(actor) is { } weapon ? Swing(turn, weapon, target) : null;
    }

    /// <summary>
    /// Everything it has, if it can still spare the whole round for it.
    /// </summary>
    /// <remarks>
    /// Always worth it once the actor is standing where it wants to be: a full attack is never
    /// fewer swings than a single one, and by this point the movement it costs has either been
    /// spent or was not wanted. Below a base attack bonus of six the two are identical, which is
    /// exactly why levelling into a second attack feels like something.
    /// </remarks>
    private static GameAction? Swing(Turn turn, WeaponAttack weapon, Creature target)
    {
        if (turn.Budget.CanAfford(ActionCost.FullRound))
        {
            return new FullAttackAction(target, weapon);
        }

        return turn.Budget.HasStandard ? new AttackAction(weapon, target) : null;
    }

    /// <summary>
    /// The best shot available from where the actor is standing, or null if there is none.
    /// </summary>
    /// <remarks>
    /// Least range penalty wins. Ties go to whichever the creature carries first, which keeps the
    /// choice reproducible — picking arbitrarily between two equal bows would be one more thing
    /// that could diverge after a save is reloaded.
    /// </remarks>
    private static WeaponAttack? BestShot(Battlefield field, Creature actor, Creature target)
    {
        if (!field.HasLineOfSight(actor, target))
        {
            return null;
        }

        var feet = field.DistanceInFeet(actor, target) ?? 0;

        return actor.Attacks
            .Where(weapon => weapon.IsRanged && weapon.IsWithinRange(feet))
            .MinBy(weapon => -weapon.RangePenalty(feet));
    }

    /// <summary>
    /// What to swing once something has closed. An archer with a blade draws it: firing in
    /// somebody's face provokes, and costs four besides.
    /// </summary>
    private static WeaponAttack? InClose(Creature actor) =>
        actor.MeleeAttack ?? actor.PrimaryAttack;

    /// <summary>
    /// Who to go after: the weakest of whoever is already in reach, else the weakest anywhere.
    /// </summary>
    /// <remarks>
    /// The preference for somebody already in front of you is not politeness. Walking off to
    /// reach a slightly weaker target gives up the whole round's worth of swings a full attack
    /// would have bought, and hands the one you turned your back on a free hit on the way. A
    /// fighter who shops around for the softest enemy on the field is a fighter who never lands
    /// a second attack.
    /// </remarks>
    private static Creature Choose(Turn turn, Creature actor, IReadOnlyList<Creature> enemies)
    {
        if (turn.Encounter.Battlefield is { } field)
        {
            var close = enemies.Where(enemy => field.IsWithinReach(actor, enemy)).ToList();
            if (close.Count > 0)
            {
                return close.MinBy(enemy => enemy.HitPoints.Current)!;
            }
        }

        return enemies.MinBy(enemy => enemy.HitPoints.Current)!;
    }

    /// <summary>
    /// Whether there is a spell worth casting right now, and where to point it.
    /// </summary>
    /// <remarks>
    /// Deliberately shallow: buff first if the buff is missing, then drop an area spell if it
    /// catches a crowd and none of your own, then fall back to something single-target. Healing
    /// is left out entirely — knowing when to heal rather than attack needs more judgement than a
    /// rule of thumb can carry.
    /// </remarks>
    private GameAction? ChooseSpell(Turn turn, Creature actor, Creature target)
    {
        var field = turn.Encounter.Battlefield;

        foreach (var spell in actor.Spells.Prepared.OrderByDescending(spell => spell.Level))
        {
            if (!actor.Spells.CanCast(spell))
            {
                continue;
            }

            if (spell.Affects == SpellAffects.Allies)
            {
                if (spell.Does.OfType<Bestow>().FirstOrDefault() is { } bestow
                    && !actor.Effects.Has(bestow.Name))
                {
                    return CastSpellAction.At(spell, actor);
                }

                continue;
            }

            if (!spell.Does.OfType<DealDamage>().Any())
            {
                continue;
            }

            if (spell.Target is BurstTarget burst)
            {
                if (field is not null && BestCentre(field, actor, spell, burst) is { } centre)
                {
                    return CastSpellAction.At(spell, centre);
                }

                continue;
            }

            if (field is null || InRange(field, actor, spell, target))
            {
                return CastSpellAction.At(spell, target);
            }
        }

        return null;
    }

    /// <summary>
    /// The best place to drop a burst: most enemies caught, and — the part that matters — not one
    /// of your own people anywhere inside it.
    /// </summary>
    private static GridSquare? BestCentre(
        Battlefield field,
        Creature caster,
        Spell spell,
        BurstTarget burst)
    {
        GridSquare? best = null;
        var most = 1;

        foreach (var candidate in field.Creatures.Where(caster.IsEnemyOf).ToArray())
        {
            if (field.SquareOf(candidate) is not { } centre
                || !InRange(field, caster, spell, centre))
            {
                continue;
            }

            var caught = field.CreaturesWithin(centre, burst.RadiusFeet);
            if (caught.Any(inside => ReferenceEquals(inside, caster) || caster.IsAllyOf(inside)))
            {
                continue;
            }

            var enemies = caught.Count(caster.IsEnemyOf);
            if (enemies > most)
            {
                most = enemies;
                best = centre;
            }
        }

        return best;
    }

    private static bool InRange(Battlefield field, Creature caster, Spell spell, Creature target) =>
        field.SquareOf(target) is { } square && InRange(field, caster, spell, square);

    private static bool InRange(Battlefield field, Creature caster, Spell spell, GridSquare square) =>
        field.SquareOf(caster) is not { } from
        || Distance.Between(from, square) <= spell.Range.InFeet(caster);

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

                if (field.WouldFlankFrom(candidate, actor, target))
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

    private static bool IsBadlyHurt(Creature creature) =>
        creature.HitPoints.Current * 100 <= creature.HitPoints.Maximum * GuardBelowPercent;
}
