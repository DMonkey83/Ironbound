using Ironbound.Rules.Combat;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Magic;
using Ironbound.Rules.Maps;

namespace Ironbound.Rules.Encounters.Actions;

/// <summary>
/// Casting a spell. Usually a standard action, and it gives anyone standing next to you a free
/// swing first.
/// </summary>
/// <remarks>
/// Being hit while casting ought to force a concentration check to keep hold of the spell. That
/// is not modelled yet: the spell goes off regardless, unless the caster is knocked out before
/// finishing it.
/// </remarks>
public sealed class CastSpellAction : GameAction
{
    public CastSpellAction(Spell spell, SpellAim aim)
    {
        ArgumentNullException.ThrowIfNull(spell);
        Spell = spell;
        Aim = aim;
    }

    public Spell Spell { get; }

    public SpellAim Aim { get; }

    public override string Name => Spell.Name;

    public override ActionCost Cost => Spell.CastingTime;

    public static CastSpellAction At(Spell spell, Creature target) => new(spell, SpellAim.At(target));

    public static CastSpellAction At(Spell spell, GridSquare point) => new(spell, SpellAim.At(point));

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
        var opportunities = Provoke(context, caster);

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

        caster.Spells.Spend(Spell);

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

        return new CastSpellResult(this, caster, description, cast, opportunities);
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
        return to is not { } destination || Distance.Between(from, destination) <= Spell.Range.InFeet(caster);
    }
}

/// <summary>A spell cast, with anything it provoked on the way out.</summary>
public sealed record CastSpellResult(
    GameAction Action,
    Creature Actor,
    string Description,
    SpellCast? Cast,
    IReadOnlyList<StrikeResult> Opportunities)
    : ActionResult(Action, Actor, Description);
