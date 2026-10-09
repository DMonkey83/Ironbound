using Ironbound.Rules.Abilities;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Feats;
using Ironbound.Rules.Modifiers;

namespace Ironbound.Rules.Skills;

/// <summary>
/// What feats add to skill checks: the paired skill feats, Skill Focus, and Intimidating
/// Prowess.
/// </summary>
/// <remarks>
/// Worked out at the moment of the check rather than written into a stack when the feat is
/// taken, because every one of them changes with the creature: the pairs and Skill Focus grow
/// at ten ranks, and Intimidating Prowess follows Strength.
/// </remarks>
public static class SkillFeats
{
    /// <summary>What each of the paired feats gives, Alertness and Acrobatic and the rest.</summary>
    public const int PairBonus = 2;

    /// <summary>What a pair gives instead on a skill with ten ranks in it.</summary>
    public const int PairBonusAtTenRanks = 4;

    public const int FocusBonus = 3;

    public const int FocusBonusAtTenRanks = 6;

    /// <summary>The ranks at which the skill feats double.</summary>
    public const int RanksToImprove = 10;

    public static ModifierStack Modifiers(Creature creature, Skill skill)
    {
        ArgumentNullException.ThrowIfNull(creature);

        var stack = new ModifierStack();
        var trained = creature.Skills.Ranks(skill) >= RanksToImprove;

        foreach (var feat in creature.Feats)
        {
            switch (feat.Effect)
            {
                // Feat bonuses are untyped, so Alertness and Skill Focus on Perception stack.
                case FeatEffect.SkillBonus when feat.Skills.Contains(skill):
                    stack.Add(trained ? PairBonusAtTenRanks : PairBonus, BonusType.Untyped, feat.Name);
                    break;

                case FeatEffect.SkillFocus when FeatChoices.SkillOf(feat) == skill:
                    stack.Add(trained ? FocusBonusAtTenRanks : FocusBonus, BonusType.Untyped, feat.Title);
                    break;
            }
        }

        // Strength on top of Charisma, not instead of it: the bully who is also persuasive.
        if (skill == Skill.Intimidate
            && creature.HasFeat(FeatEffect.IntimidatingProwess)
            && creature.Abilities[Ability.Strength].Modifier is not 0 and var strength)
        {
            stack.Add(strength, BonusType.Untyped, "Intimidating Prowess");
        }

        return stack;
    }
}
