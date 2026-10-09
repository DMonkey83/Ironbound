using Ironbound.Rules.Classes;
using Ironbound.Rules.Combat;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Effects;
using Ironbound.Rules.Feats;
using Ironbound.Rules.Magic;
using Ironbound.Rules.Maps;

namespace Ironbound.Rules.Encounters.Actions;

/// <summary>
/// Using a class power: channelling, a domain's touch, a school's missile, a rage power.
/// </summary>
/// <remarks>
/// Resolved by <see cref="Casting"/> exactly as a spell is, and reported as the same
/// <see cref="CastSpellResult"/>, so whatever already shows a spell — the log, a projectile, a
/// burst, a number rising off somebody — shows a power without learning anything new. The
/// differences are all on the way in: a daily pool instead of a slot, a difficulty class of its
/// own, and no attack of opportunity for the supernatural ones.
/// </remarks>
public sealed class UsePowerAction : GameAction
{
    public UsePowerAction(Power power, SpellAim aim, bool defensively = false)
    {
        ArgumentNullException.ThrowIfNull(power);
        Power = power;
        Aim = aim;
        Defensively = defensively;
    }

    public Power Power { get; }

    public SpellAim Aim { get; }

    /// <summary>
    /// Used without dropping one's guard, as a spell can be cast: a power that would draw a swing
    /// — a spell-like one, a spell from the bonded object — draws none, for a concentration
    /// check at fifteen plus twice its level. Means nothing for a power that never provokes.
    /// </summary>
    public bool Defensively { get; }

    /// <summary>The same use, made defensively.</summary>
    public UsePowerAction AsDefensive() => new(Power, Aim, defensively: true);

    public override string Name => Power.Name;

    public override ActionCost Cost => Power.Cost;

    public static UsePowerAction At(Power power, Creature target) => new(power, SpellAim.At(target));

    public static UsePowerAction At(Power power, GridSquare point) => new(power, SpellAim.At(point));

    /// <summary>On the user: channel energy's burst, renewed vigor, a guarded stance.</summary>
    public static UsePowerAction Self(Power power) => new(power, default);

    public override bool CanPerform(ActionContext context)
    {
        var actor = context.Actor;

        // Asked of the creature as it is now, rather than trusted from whenever the power was
        // handed out: the uses, the dice and the range may all have moved since.
        if (Current(actor) is not { } power || !actor.IsConscious)
        {
            return false;
        }

        if (actor.UsesLeft(power) < Math.Max(1, power.UseCost))
        {
            return false;
        }

        var raging = Rage.IsRaging(actor);
        if (power.Requires == PowerRequirement.Raging ? !raging : raging)
        {
            return false;
        }

        if (power.Use == PowerUse.Spell && !actor.Spells.Spellbook.Any(spell => spell.Id == power.Effect.Id))
        {
            return false;
        }

        if (power.Effect.Target is SelfTarget)
        {
            return true;
        }

        if (power.Effect.NeedsAPoint ? Aim.Point is null && Aim.Creature is null : Aim.Creature is null)
        {
            return false;
        }

        if (power.Requires == PowerRequirement.TargetDying
            && Aim.Creature is not { HitPoints.State: HitPointState.Dying })
        {
            return false;
        }

        if (power.Use == PowerUse.Teleport)
        {
            return CanLand(context, power);
        }

        return WithinRange(context, actor, power);
    }

    public override ActionResult Perform(ActionContext context)
    {
        var actor = context.Actor;
        var power = Current(actor)!;
        var field = context.Encounter.Battlefield;

        var defensive = Defensively && power.Provokes;
        var opportunities = power.Provokes && !defensive && field?.SquareOf(actor) is { } standing
            ? Opportunities.Provoke(context.Encounter, actor, standing)
            : [];

        var spent = power.Use == PowerUse.Teleport ? StepCost(field, actor) : power.UseCost;

        // Cut down mid-use: the use is gone with the moment, as a spell's slot is.
        if (!actor.IsConscious)
        {
            actor.DailyUses.Spend(power.Pool, spent);
            return new CastSpellResult(
                this, actor, $"{actor.Name} is cut down using {power.Name}", null, opportunities);
        }

        actor.DailyUses.Spend(power.Pool, spent);

        // A power that is cast as a spell is — the ones that draw swings, spell-like abilities
        // and the bonded object's spell — has to be held on to as a spell does. Supernatural
        // ones such as channel energy never needed to be.
        var checks = power.Provokes
            ? Concentration.Hold(
                actor,
                power.Use == PowerUse.Spell ? actor.Spells.CasterLevel : power.CasterLevel,
                power.Effect.EffectiveLevel,
                defensive,
                CastSpellAction.Wounds(opportunities),
                context.Random)
            : [];

        // Only a real spell is spoken; a spell-like ability needs no words to fumble.
        var fumbled = !Concentration.Lost(checks) && power.Use == PowerUse.Spell && CastSpellAction.Fumbles(actor, context.Random);

        if (Concentration.Lost(checks) || fumbled)
        {
            return new CastSpellResult(
                this, actor, CastSpellAction.Lost(actor, power.Effect, opportunities, checks), null, opportunities)
            {
                Concentration = checks,
                Lost = true,
            };
        }

        var cast = power.Use switch
        {
            PowerUse.Teleport => Teleport(actor, power, field!),
            PowerUse.BorrowFeat => Borrow(actor, power),
            PowerUse.Command => Command(actor, power, field, context),
            _ => Casting.Resolve(
                actor,
                power.Effect,
                AimFor(actor, power, field),
                context.Random,
                context.Rules,
                field,
                Invocation.Of(power) with { Excluded = Excluded(actor, power, field) }),
        };

        var description = cast.ToString();
        if (opportunities.Count > 0)
        {
            description += $", provoking {opportunities.Count}";
        }

        if (defensive)
        {
            description += ", used defensively";
        }

        return new CastSpellResult(this, actor, description, cast, opportunities) { Concentration = checks };
    }

    private Power? Current(Creature actor) =>
        actor.Powers.FirstOrDefault(power => power.Id == Power.Id);

    /// <summary>
    /// Where a power on oneself is said to be aimed: the user's own square for a burst around
    /// her, so whatever draws it draws a burst there, and the user herself otherwise.
    /// </summary>
    private SpellAim AimFor(Creature actor, Power power, Battlefield? field)
    {
        if (power.Effect.Target is not SelfTarget self)
        {
            return Aim;
        }

        return self.RadiusFeet > 0 && field?.SquareOf(actor) is { } square
            ? SpellAim.At(square)
            : SpellAim.At(actor);
    }

    /// <summary>
    /// Who a channel leaves out. The cleric may always choose whether to include herself, and
    /// does so exactly when it heals. Selective Channeling leaves out up to her Charisma bonus
    /// more — her enemies when it heals, her friends when it harms.
    /// </summary>
    private static IReadOnlyCollection<Creature> Excluded(Creature actor, Power power, Battlefield? field)
    {
        // Every channel, the feats' as well as the plain one, draws on the channel pool.
        if (power.Pool != ClassPowers.ChannelPool || power.Effect.Target is not SelfTarget { RadiusFeet: > 0 } around)
        {
            return [];
        }

        // Whether it heals the living, which is what decides whose side is left out of it.
        var heals = power.Effect.Does.OfType<Restore>().Any(mend => mend.Only is null or { Kind: SpellFilterKind.Living })
            || power.Effect.Does.All(effect => effect is Restore);
        var excluded = heals ? new List<Creature>() : [actor];

        if (actor.HasFeat(FeatEffect.SelectiveChanneling) && field?.SquareOf(actor) is { } middle)
        {
            var allowed = Math.Max(0, actor.Abilities[Abilities.Ability.Charisma].Modifier);

            excluded.AddRange(field.CreaturesWithin(middle, around.RadiusFeet)
                .Where(other => !ReferenceEquals(other, actor))
                .Where(other => heals ? actor.IsEnemyOf(other) : !actor.IsEnemyOf(other))
                .Take(allowed));
        }

        return excluded;
    }

    private SpellCast Teleport(Creature actor, Power power, Battlefield field)
    {
        var to = Aim.Point ?? field.SquareOf(Aim.Creature!)!.Value;
        field.Place(actor, to);

        return new SpellCast(actor, power.Effect, SpellAim.At(to), 0, []) { Verb = "uses" };
    }

    /// <summary>
    /// Command Undead: each undead foe in the burst saves or changes sides, nearest first, for as
    /// long as the cleric's level in hit dice will hold — counting any she commands already.
    /// </summary>
    /// <remarks>
    /// The book has a controlled undead that is intelligent try again each day; the game's day
    /// is a rest, which ends every effect anyway, and a side once changed stays changed here.
    /// </remarks>
    private static SpellCast Command(Creature actor, Power power, Battlefield? field, ActionContext context)
    {
        var results = new List<SpellTargetResult>();
        var held = context.Encounter.Order
            .Select(combatant => combatant.Creature)
            .Where(other => other.IsAllyOf(actor) && CreatureTypes.IsUndead(other))
            .Sum(other => other.HitPoints.HitDice);

        var middle = field?.SquareOf(actor);
        var candidates = Casting.Gather(actor, power.Effect, SpellAim.At(actor), field)
            .Where(other => CreatureTypes.IsUndead(other) && actor.IsEnemyOf(other) && other.IsAlive)
            .OrderBy(other => middle is { } centre && field!.SquareOf(other) is { } at ? Distance.Between(centre, at) : 0);

        foreach (var undead in candidates)
        {
            if (held + undead.HitPoints.HitDice > power.CasterLevel)
            {
                results.Add(new SpellTargetResult(undead, null, null, 0, 0, ["too strong to hold as well"]));
                continue;
            }

            var save = Saves.SaveRerolls.Attempt(undead, Saves.Save.Will, power.DifficultyClass, context.Random, context.Rules);
            if (save.Succeeded)
            {
                results.Add(new SpellTargetResult(undead, null, save, 0, 0, []));
                continue;
            }

            undead.Allegiance = actor.Allegiance;
            held += undead.HitPoints.HitDice;
            results.Add(new SpellTargetResult(undead, null, save, 0, 0, [$"now serves {actor.Name}"]));
        }

        return new SpellCast(actor, power.Effect, SpellAim.At(actor), power.DifficultyClass, results) { Verb = "uses" };
    }

    private static SpellCast Borrow(Creature actor, Power power)
    {
        var feat = actor.Choices.WeaponMasterFeat!;
        actor.Effects.Apply(new BorrowedFeatEffect(feat, Duration.Rounds(1)));

        return new SpellCast(
            actor,
            power.Effect,
            SpellAim.At(actor),
            0,
            [new SpellTargetResult(actor, null, null, 0, 0, [$"gains {feat.Title} for a round"])])
        {
            Verb = "uses",
        };
    }

    /// <summary>How far a teleport carries her, in feet, which is what it costs.</summary>
    private int StepCost(Battlefield? field, Creature actor)
    {
        if (field?.SquareOf(actor) is not { } from)
        {
            return 0;
        }

        var to = Aim.Point ?? (Aim.Creature is { } at ? field.SquareOf(at) : null);
        return to is { } destination ? Distance.Between(from, destination) : 0;
    }

    private bool CanLand(ActionContext context, Power power)
    {
        if (context.Encounter.Battlefield is not { } field || Aim.Point is not { } to)
        {
            return false;
        }

        var cost = StepCost(field, context.Actor);
        return field.IsFree(to) && cost > 0 && cost <= context.Actor.UsesLeft(power);
    }

    private bool WithinRange(ActionContext context, Creature actor, Power power)
    {
        if (context.Encounter.Battlefield is not { } field || field.SquareOf(actor) is not { } from)
        {
            return true;
        }

        var to = Aim.Point ?? (Aim.Creature is { } at ? field.SquareOf(at) : null);
        return to is not { } destination
            || power.Effect.Range.Reaches(actor, from, destination, power.Use == PowerUse.Spell
                ? actor.Spells.CasterLevel
                : power.CasterLevel);
    }
}
