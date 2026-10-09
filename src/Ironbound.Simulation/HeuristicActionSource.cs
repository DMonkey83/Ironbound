using Ironbound.Rules.Classes;
using Ironbound.Rules.Combat;
using Ironbound.Rules.Conditions;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Dice;
using Ironbound.Rules.Effects;
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

        // Dazed or stunned is not a bad turn, it is no turn. Saying so here keeps the scheduler
        // from asking again and again for an action that can never be legal.
        if (!actor.CanAct)
        {
            return null;
        }

        var playsWell = Competence == 100 || _random.Next(0, 100) < Competence;

        // Standing at exactly nought, anything strenuous reopens the wound and drops you. The
        // only move that costs nothing is not making one, so a creature that intends to still
        // be here next round makes none.
        if (playsWell && actor.HitPoints.State == HitPointState.Disabled)
        {
            return null;
        }

        // Rage first: it is free, it is only worth having before the swing, and the round it
        // starts in is already paid for whether she swings or not.
        if (playsWell && ShouldRage(turn, actor, enemies) && turn.CanTake(new RageAction()))
        {
            return new RageAction();
        }

        // On the floor. Getting up costs the move action and a free swing from anyone standing
        // over you; staying down costs -4 to hit and -4 to armour class against all of them,
        // every round. Standing is almost always the cheaper of the two.
        if (actor.IsProne && turn.Budget.CanAfford(ActionCost.Move))
        {
            return new StandUpAction();
        }

        // A channel that catches two or more hurt friends beats anything else a cleric can do
        // with the turn — and it picks the dying up off the floor along the way.
        if (playsWell && turn.Budget.HasStandard && Channel(turn, actor) is { } channel)
        {
            return channel;
        }

        // A friend on the floor is worth more than a swing. They are losing a point a round and
        // will be dead in a few of them; the enemy will still be there afterwards.
        if (playsWell && turn.Budget.HasStandard && Bleedingout(turn, actor) is { } patient)
        {
            // Rebuke death closes the wound as well as stopping the bleeding, so it comes first.
            if (Touch(turn, actor, PowerRequirement.TargetDying, patient) is { } rebuke)
            {
                return rebuke;
            }

            return new StabiliseAction(patient);
        }

        // Badly hurt in the middle of a rage: renewed vigor is the one heal she has.
        if (playsWell && turn.Budget.HasStandard && actor.IsRaging
            && actor.HitPoints.Current * 2 <= actor.HitPoints.Maximum
            && OwnPower(turn, actor, power => power.Requires == PowerRequirement.Raging
                && power.Effect.Does.OfType<Restore>().Any()) is { } vigour)
        {
            return vigour;
        }

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

        // Out of slots, the bonded object is one more spell; then the school's own missile.
        if (playsWell && turn.Budget.HasStandard && ChoosePower(turn, actor, target) is { } power)
        {
            return power;
        }

        // A cleric with nobody to hit this turn can at least make somebody else hit harder.
        if (playsWell && turn.Budget.HasStandard && Encourage(turn, actor) is { } encourage)
        {
            return encourage;
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

        // Set the dials before swinging. Power Attack is worth it against something whose
        // armour you were going to beat anyway; against something you can barely hit, giving
        // up accuracy for damage you will never land is the worst trade in the game.
        if (playsWell)
        {
            SetStance(actor, target, turn.Encounter.Battlefield);
        }

        // Worth stealing a round from anything that swings more than once: the four points of
        // armour class are incidental, the move action it must spend getting up is the prize.
        // Only when the odds are better than even, since failing badly puts you on the floor.
        if (playsWell
            && turn.Budget.HasStandard
            && !target.IsProne
            && target.AttacksPerFullAttack > 1
            && Maneuvers.Bonus(actor, ManeuverKind.Trip).Total + 11 + Surge(actor) >= Maneuvers.Defense(target)
            && turn.CanTake(new TripAction(target)))
        {
            // A strength surge is worth its once-a-rage on a check the barbarian means to make.
            if (Surge(actor) > 0)
            {
                actor.Stances.Adopt(Stance.StrengthSurge);
            }

            return new TripAction(target);
        }

        // A wizard out of spells has nothing useful left, and that is a legitimate answer.
        var swing = InClose(actor) is { } weapon ? Swing(turn, weapon, target) : null;

        // Declared only when a swing is actually coming: a declaration left standing at the end
        // of the turn would be spent on whatever attack of opportunity came along first.
        if (playsWell && swing is not null)
        {
            Declare(actor, target, turn.Encounter.Battlefield);
        }

        return swing;
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
    /// Decides whether to trade accuracy for damage against this particular target.
    /// </summary>
    /// <remarks>
    /// The threshold is "still better than even odds after the penalty". Below that the
    /// arithmetic turns: two more points of damage on a swing that now misses more often than
    /// it lands is a loss, and it gets worse the harder the target is to hit.
    /// </remarks>
    private static void SetStance(Creature actor, Creature target, Battlefield? field)
    {
        if (!actor.Stances.CanAdopt(Stance.PowerAttack) || actor.MeleeAttack is not { } weapon)
        {
            return;
        }

        var severity = actor.Stances.Severity(Stance.PowerAttack);
        var wasActive = actor.Stances.IsActive(Stance.PowerAttack);

        // Measure the swing without the stance, whichever way it is set right now.
        if (wasActive)
        {
            actor.Stances.Drop(Stance.PowerAttack);
        }

        var bare = Strike.AttackBonus(actor, weapon, target, field).Total;
        var worthIt = bare + 11 - severity >= target.ArmorClass.Total;

        if (worthIt)
        {
            actor.Stances.Adopt(Stance.PowerAttack);
        }
    }

    /// <summary>
    /// Whether to fly into a rage now: an enemy in reach, or close enough to charge — twice her
    /// speed — so the rounds are spent on fighting rather than on walking.
    /// </summary>
    private static bool ShouldRage(Turn turn, Creature actor, IReadOnlyList<Creature> enemies)
    {
        if (actor.IsRaging || !Rage.CanStart(actor))
        {
            return false;
        }

        if (turn.Encounter.Battlefield is not { } field)
        {
            return true;
        }

        return enemies.Any(enemy =>
            field.IsWithinReach(actor, enemy)
            || field.DistanceInFeet(actor, enemy) is { } feet && feet <= actor.CurrentSpeed * 2);
    }

    /// <summary>What a strength surge would add to the next manoeuvre, if one can be declared.</summary>
    private static int Surge(Creature actor) =>
        actor.Stances.IsActive(Stance.StrengthSurge) || actor.Stances.CanAdopt(Stance.StrengthSurge)
            ? actor.Stances.RageBonus(Stance.StrengthSurge)
            : 0;

    /// <summary>
    /// The once-a-rage declarations, made before a swing that is worth them: powerful blow on a
    /// blow likely to land, surprise accuracy on one that needs the help to.
    /// </summary>
    private static void Declare(Creature actor, Creature target, Battlefield? field)
    {
        if (!actor.IsRaging || InClose(actor) is not { } weapon)
        {
            return;
        }

        var bare = Strike.AttackBonus(actor, weapon, target, field).Total;
        var armour = target.ArmorClass.Total;

        // Better than even after everything: the extra damage will probably land.
        if (bare + 11 >= armour && actor.Stances.CanAdopt(Stance.PowerfulBlow))
        {
            actor.Stances.Adopt(Stance.PowerfulBlow);
        }

        // Worse than three in four: the accuracy is worth more here than on an easy swing later.
        if (!actor.Stances.IsActive(Stance.SurpriseAccuracy)
            && bare + 6 < armour
            && actor.Stances.CanAdopt(Stance.SurpriseAccuracy))
        {
            actor.Stances.Adopt(Stance.SurpriseAccuracy);
        }
    }

    /// <summary>
    /// A channel, when it would land on at least two hurt friends and do them more good than it
    /// does the enemies standing among them — it heals everyone in the burst.
    /// </summary>
    private static GameAction? Channel(Turn turn, Creature actor)
    {
        if (turn.Encounter.Battlefield is not { } field
            || field.SquareOf(actor) is not { } middle
            || actor.Powers.FirstOrDefault(power => power.Id == ClassPowers.ChannelPool) is not { } channel
            || !channel.Effect.Does.OfType<Restore>().Any())
        {
            return null;
        }

        var caught = field.CreaturesWithin(middle, ClassPowers.ChannelRadius)
            .Where(other => other.IsAlive && other.HitPoints.Damage > 0)
            .ToList();

        var friends = caught.Where(other => !actor.IsEnemyOf(other)).ToList();
        var foes = caught.Where(actor.IsEnemyOf).ToList();

        if (friends.Count < 2 || friends.Sum(Wound) <= foes.Sum(Wound))
        {
            return null;
        }

        var action = UsePowerAction.Self(channel);
        return turn.CanTake(action) ? action : null;
    }

    private static int Wound(Creature creature) => Math.Max(0, creature.HitPoints.Maximum - creature.HitPoints.Current);

    /// <summary>A touch power of this kind on this ally, if the actor has one and can use it.</summary>
    private static GameAction? Touch(Turn turn, Creature actor, PowerRequirement requires, Creature ally)
    {
        foreach (var power in actor.Powers.Where(power => power.Requires == requires && power.Use == PowerUse.Effect))
        {
            var action = UsePowerAction.At(power, ally);
            if (turn.CanTake(action))
            {
                return action;
            }
        }

        return null;
    }

    /// <summary>One of the actor's own powers on herself, matching a test, if usable.</summary>
    private static GameAction? OwnPower(Turn turn, Creature actor, Func<Power, bool> wanted)
    {
        foreach (var power in actor.Powers.Where(wanted))
        {
            var action = UsePowerAction.Self(power);
            if (turn.CanTake(action))
            {
                return action;
            }
        }

        return null;
    }

    /// <summary>
    /// A power to throw at the enemy once the slots are gone: a spell from the bonded object
    /// first, chosen the way a prepared one would be, then a school's missile or dart.
    /// </summary>
    private GameAction? ChoosePower(Turn turn, Creature actor, Creature target)
    {
        var field = turn.Encounter.Battlefield;

        foreach (var power in actor.Powers.Where(power => power.Use == PowerUse.Spell).OrderByDescending(power => power.Effect.Level))
        {
            if (actor.UsesLeft(power) <= 0)
            {
                continue;
            }

            var spell = power.Effect;
            GameAction? action = null;

            if (spell.Does.OfType<DealDamage>().Any())
            {
                if (spell.Target is BurstTarget burst)
                {
                    if (field is not null && BestCentre(field, actor, spell, burst) is { } centre)
                    {
                        action = UsePowerAction.At(power, centre);
                    }
                }
                else if (spell.Affects == SpellAffects.Enemies)
                {
                    action = UsePowerAction.At(power, target);
                }
            }

            if (action is not null && turn.CanTake(action))
            {
                return action;
            }
        }

        foreach (var power in actor.Powers.Where(power =>
            power.Use == PowerUse.Effect
            && power.Effect.Affects == SpellAffects.Enemies
            && power.Effect.Does.OfType<DealDamage>().Any()))
        {
            var action = UsePowerAction.At(power, target);
            if (turn.CanTake(action))
            {
                return action;
            }
        }

        return null;
    }

    /// <summary>
    /// Battle rage, or anything like it, on an ally who is in the thick of it — but only when
    /// the actor has nobody within reach to swing at herself, so the turn is not given up.
    /// </summary>
    private GameAction? Encourage(Turn turn, Creature actor)
    {
        if (turn.Encounter.Battlefield is not { } field
            || _battle.EnemiesOf(actor).Any(enemy => field.IsWithinReach(actor, enemy)))
        {
            return null;
        }

        foreach (var power in actor.Powers.Where(power =>
            power.Use == PowerUse.Effect
            && power.Effect.Affects == SpellAffects.Allies
            && power.Effect.Does.OfType<Bestow>().Any()))
        {
            var name = power.Effect.Does.OfType<Bestow>().First().Name;

            foreach (var ally in _battle.Party.Concat(_battle.Foes).Where(other =>
                (ReferenceEquals(other, actor) || other.IsAllyOf(actor))
                && other.IsConscious
                && !other.Effects.Has(name)
                && _battle.EnemiesOf(actor).Any(enemy => field.IsWithinReach(other, enemy))))
            {
                var action = UsePowerAction.At(power, ally);
                if (turn.CanTake(action))
                {
                    return action;
                }
            }
        }

        return null;
    }

    /// <summary>The nearest ally bleeding out within arm's reach, if there is one.</summary>
    private static Creature? Bleedingout(Turn turn, Creature actor)
    {
        if (turn.Encounter.Battlefield is not { } field)
        {
            return null;
        }

        foreach (var combatant in turn.Encounter.Order)
        {
            var other = combatant.Creature;

            if (other.IsAllyOf(actor)
                && other.HitPoints.State == HitPointState.Dying
                && !Bleeding.IsStable(other)
                && field.IsWithinReach(actor, other))
            {
                return other;
            }
        }

        return null;
    }

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
                // A rogue goes for whoever her sneak attack works on: flanked, or caught flat-footed.
                if (SneakAttack.Dice(actor) > 0
                    && close.Where(enemy => Strike.FlankingPartner(actor, enemy, field) is not null
                        || turn.Encounter.IsFlatFootedTo(enemy, actor)).ToList() is { Count: > 0 } exposed)
                {
                    return exposed.MinBy(enemy => enemy.HitPoints.Current)!;
                }

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

        foreach (var prepared in actor.Spells.Prepared.OrderByDescending(spell => spell.Level))
        {
            // A damaging spell whose own slots are gone can still go out empowered from a higher
            // one, which is the best use of a slot nothing else of its level wants.
            var spell = !actor.Spells.CanCast(prepared)
                && prepared.Does.OfType<DealDamage>().Any()
                && actor.Spells.CanCast(prepared.Empower())
                    ? prepared.Empower()
                    : prepared;

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

            // A curse is worth a slot exactly once per target: a second Cause Fear on somebody
            // already shaken buys nothing at all, and an AI that cannot see that will spend the
            // whole fight recasting it.
            // Single target only. An area curse aimed at an enemy catches whoever is standing
            // next to them, which in a melee is usually your own front rank.
            if (spell.Target is not BurstTarget
                && spell.Does.OfType<Bestow>().FirstOrDefault() is { Effect.Condition: { } condition }
                && !target.Has(condition)
                && (field is null || InRange(field, actor, spell, target)))
            {
                return CastSpellAction.At(spell, target);
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
