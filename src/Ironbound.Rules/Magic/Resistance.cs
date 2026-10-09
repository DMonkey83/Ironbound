using Ironbound.Rules.Creatures;
using Ironbound.Rules.Dice;
using Ironbound.Rules.Feats;

namespace Ironbound.Rules.Magic;

/// <summary>One caster level check against a creature's spell resistance.</summary>
public sealed record ResistanceCheck(Creature Caster, Creature Target, int NaturalRoll, int Bonus, int Resistance)
{
    public int Total => NaturalRoll + Bonus;

    public bool Succeeded => Total >= Resistance;

    public override string ToString() =>
        $"{Caster.Name} caster level check: d20 [{NaturalRoll}] {Bonus:+0;-0;+0} = {Total} vs SR {Resistance} — "
        + (Succeeded ? "gets through" : $"{Target.Name} resists");
}

/// <summary>
/// Spell resistance: a d20 plus the caster's level against the target's number, before the spell
/// does anything to it. Spell Penetration adds two, and the greater feat two more.
/// </summary>
public static class Resistance
{
    public const int PenetrationBonus = 2;

    /// <summary>What the caster adds to the d20: her level, and the penetration feats.</summary>
    public static int Bonus(Creature caster, int casterLevel)
    {
        ArgumentNullException.ThrowIfNull(caster);

        return casterLevel
            + (caster.HasFeat(FeatEffect.SpellPenetration) ? PenetrationBonus : 0)
            + (caster.HasFeat(FeatEffect.GreaterSpellPenetration) ? PenetrationBonus : 0);
    }

    public static ResistanceCheck Check(Creature caster, Creature target, int casterLevel, IRandomSource random)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(random);

        return new ResistanceCheck(caster, target, random.NextDie(20), Bonus(caster, casterLevel), target.SpellResistance);
    }
}
