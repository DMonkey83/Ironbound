using Ironbound.Rules.Combat;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Dice;
using Ironbound.Rules.Magic;
using Ironbound.Rules.Maps;

namespace Ironbound.Rules.Encounters.Actions;

/// <summary>
/// Casting a spell. Usually a standard action, and it gives anyone standing next to you a free
/// swing first — unless it is cast defensively, which trades the swing for a concentration
/// check, or quickened, which draws none.
/// </summary>
/// <remarks>
/// A caster hurt by the swing her casting drew has to keep hold of the spell — ten, plus the
/// damage, plus its level — and one who is entangled has to as well. Failing any of it loses
/// the spell and the slot together. A deafened caster fumbles one spoken spell in five, and
/// every spell in the game is spoken.
/// </remarks>
public sealed class CastSpellAction : GameAction
{
    public CastSpellAction(Spell spell, SpellAim aim, bool defensively = false)
    {
        ArgumentNullException.ThrowIfNull(spell);
        Spell = spell;
        Aim = aim;
        Defensively = defensively;
    }

    public Spell Spell { get; }

    public SpellAim Aim { get; }

    /// <summary>
    /// Cast without dropping one's guard: no attack of opportunity, but a concentration check —
    /// fifteen plus twice the spell's level — to keep hold of it.
    /// </summary>
    public bool Defensively { get; }

    public override string Name => Spell.Name;

    public override ActionCost Cost => Spell.CastAs;

    public static CastSpellAction At(Spell spell, Creature target) => new(spell, SpellAim.At(target));

    public static CastSpellAction At(Spell spell, GridSquare point) => new(spell, SpellAim.At(point));

    /// <summary>The same cast, made defensively.</summary>
    public CastSpellAction AsDefensive() => new(Spell, Aim, defensively: true);

    public override bool CanPerform(ActionContext context)
    {
        var caster = context.Actor;

        if (!caster.Spells.CanCast(Spell))
        {
            return false;
        }

        if (Spell.Target is SelfTarget)
        {
            return true;
        }

        // Something to aim at, of the right kind.
        if (Spell.NeedsAPoint)
        {
            if (Aim is { Point: null, Creature: null })
            {
                return false;
            }
        }
        else if (Aim.Creature is null)
        {
            return false;
        }

        return WithinRange(context, caster);
    }

    public override ActionResult Perform(ActionContext context)
    {
        var caster = context.Actor;
        var quickened = Spell.Metamagic.HasFlag(Metamagic.Quicken);
        var opportunities = Defensively || quickened ? [] : Provoke(context, caster);

        // Cut down mid-incantation: the spell is lost, and so is the slot.
        if (!caster.IsConscious)
        {
            caster.Spells.Spend(Spell);
            return new CastSpellResult(
                this,
                caster,
                $"{caster.Name} is cut down casting {Spell.Name}",
                null,
                opportunities);
        }

        var checks = Concentration.Hold(
            caster,
            caster.Spells.CasterLevel,
            Spell.EffectiveLevel,
            Defensively && !quickened,
            Wounds(opportunities),
            context.Random);

        caster.Spells.Spend(Spell);

        var fumbled = !Concentration.Lost(checks) && Fumbles(caster, context.Random);

        if (Concentration.Lost(checks) || fumbled)
        {
            return new CastSpellResult(
                this,
                caster,
                Lost(caster, Spell, opportunities, checks),
                null,
                opportunities)
            {
                Concentration = checks,
                Lost = true,
            };
        }

        var cast = Casting.Resolve(
            caster,
            Spell,
            Aim,
            context.Random,
            context.Rules,
            context.Encounter.Battlefield);

        var description = cast.ToString();
        if (opportunities.Count > 0)
        {
            description += $", provoking {opportunities.Count}";
        }

        if (Defensively)
        {
            description += ", cast defensively";
        }

        return new CastSpellResult(this, caster, description, cast, opportunities) { Concentration = checks };
    }

    /// <summary>What the swings a cast drew took off the caster, all told: what she has to concentrate through.</summary>
    internal static int Wounds(IReadOnlyList<StrikeResult> opportunities) =>
        opportunities.Sum(strike => strike.Taken?.Total ?? 0);

    /// <summary>
    /// A deafened caster's one-in-five fumble of a spoken spell. Rolled only for the deafened,
    /// so everybody else's dice are where they always were.
    /// </summary>
    internal static bool Fumbles(Creature caster, IRandomSource random) =>
        caster.Has(Conditions.Condition.Deafened) && random.NextDie(100) <= Concentration.DeafenedFailurePercent;

    internal static string Lost(Creature caster, Spell spell, IReadOnlyList<StrikeResult> opportunities, IReadOnlyList<ConcentrationCheck> checks)
    {
        var why = checks.LastOrDefault(check => !check.Succeeded)?.ToString()
            ?? $"{caster.Name} cannot hear the words";
        var provoked = opportunities.Count > 0 ? $", provoking {opportunities.Count}" : string.Empty;

        return $"{caster.Name} loses {spell.Name}{provoked}: {why}";
    }

    private static IReadOnlyList<StrikeResult> Provoke(ActionContext context, Creature caster)
    {
        if (context.Encounter.Battlefield?.SquareOf(caster) is not { } standing)
        {
            return [];
        }

        return Opportunities.Provoke(context.Encounter, caster, standing);
    }

    private bool WithinRange(ActionContext context, Creature caster)
    {
        if (context.Encounter.Battlefield is not { } field)
        {
            return true;
        }

        if (field.SquareOf(caster) is not { } from)
        {
            return true;
        }

        var to = Aim.Point ?? (Aim.Creature is { } at ? field.SquareOf(at) : null);
        return to is not { } destination || Spell.Reaches(caster, from, destination, caster.Spells.CasterLevel);
    }
}

/// <summary>A spell cast, with anything it provoked on the way out.</summary>
public sealed record CastSpellResult(
    GameAction Action,
    Creature Actor,
    string Description,
    SpellCast? Cast,
    IReadOnlyList<StrikeResult> Opportunities)
    : ActionResult(Action, Actor, Description)
{
    /// <summary>Every concentration check the cast needed, in the order they were rolled.</summary>
    public IReadOnlyList<ConcentrationCheck> Concentration { get; init; } = [];

    /// <summary>Whether the spell was lost — a failed concentration, a deafened fumble — with its slot.</summary>
    public bool Lost { get; init; }
}
