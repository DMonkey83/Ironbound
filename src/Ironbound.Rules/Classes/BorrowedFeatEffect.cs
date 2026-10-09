using Ironbound.Rules.Creatures;
using Ironbound.Rules.Effects;
using Ironbound.Rules.Feats;

namespace Ironbound.Rules.Classes;

/// <summary>
/// A feat held for as long as the effect lasts: a War cleric's Weapon Master.
/// </summary>
/// <remarks>
/// The feat goes into <see cref="Creature.Feats"/> for real while it lasts, because that is where
/// every rule that asks about a feat looks — Power Attack's stance, Combat Reflexes' count,
/// Weapon Focus's bonus — and none of them should have to know a feat can be borrowed. Its
/// static bonuses go through <see cref="Effect.Grant"/>, so they come off with it.
/// </remarks>
public sealed class BorrowedFeatEffect : Effect
{
    public BorrowedFeatEffect(FeatDefinition feat, Duration duration)
        : base(ClassPowers.WeaponMasterLabel, duration)
    {
        ArgumentNullException.ThrowIfNull(feat);
        Feat = feat;
    }

    public FeatDefinition Feat { get; }

    protected override void OnApply(Creature target)
    {
        target.Feats.Add(Feat);

        foreach (var grant in Feat.Grants)
        {
            Grant(grant.Target.On(target), grant.Value, grant.Type);
        }
    }

    /// <summary>
    /// The feat came back with the creature's own feat list and its bonuses with the stacks, so
    /// all that is missing is knowing where to take them from later.
    /// </summary>
    protected override void OnReattach(Creature target)
    {
        foreach (var grant in Feat.Grants)
        {
            Track(grant.Target.On(target));
        }
    }

    protected override void OnExpire(Creature target)
    {
        var index = -1;
        for (var i = target.Feats.Count - 1; i >= 0; i--)
        {
            if (target.Feats[i].Key == Feat.Key)
            {
                index = i;
                break;
            }
        }

        if (index >= 0)
        {
            target.Feats.RemoveAt(index);
        }
    }
}
