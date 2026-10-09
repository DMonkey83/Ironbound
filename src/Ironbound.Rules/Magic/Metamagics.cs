using Ironbound.Rules.Creatures;
using Ironbound.Rules.Feats;

namespace Ironbound.Rules.Magic;

/// <summary>Which feat each kind of metamagic is, and whether a caster has them all.</summary>
public static class Metamagics
{
    /// <summary>The feat that lets a caster put this metamagic on a spell.</summary>
    public static FeatEffect FeatFor(Metamagic metamagic) => metamagic switch
    {
        Metamagic.Empower => FeatEffect.EmpowerSpell,
        Metamagic.Extend => FeatEffect.ExtendSpell,
        Metamagic.Maximize => FeatEffect.MaximizeSpell,
        Metamagic.Widen => FeatEffect.WidenSpell,
        Metamagic.Enlarge => FeatEffect.EnlargeSpell,
        Metamagic.Quicken => FeatEffect.QuickenSpell,
        Metamagic.Heighten => FeatEffect.HeightenSpell,
        _ => FeatEffect.None,
    };

    /// <summary>Every single metamagic in a set of them.</summary>
    public static IEnumerable<Metamagic> Each(Metamagic metamagic) =>
        Enum.GetValues<Metamagic>().Where(flag => flag != Metamagic.None && metamagic.HasFlag(flag));

    /// <summary>Whether a creature has the feat for every metamagic on the spell.</summary>
    public static bool Knows(Creature caster, Metamagic metamagic)
    {
        ArgumentNullException.ThrowIfNull(caster);
        return Each(metamagic).All(flag => caster.HasFeat(FeatFor(flag)));
    }

    /// <summary>The metamagic the caster can put on spells, one flag per feat it has.</summary>
    public static IReadOnlyList<Metamagic> Available(Creature caster)
    {
        ArgumentNullException.ThrowIfNull(caster);
        return [.. Each((Metamagic)127).Where(flag => caster.HasFeat(FeatFor(flag)))];
    }
}
