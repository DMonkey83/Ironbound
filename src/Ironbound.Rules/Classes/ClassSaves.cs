using Ironbound.Rules.Conditions;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Magic;
using Ironbound.Rules.Modifiers;
using Ironbound.Rules.Saves;

namespace Ironbound.Rules.Classes;

/// <summary>
/// The saving-throw bonuses class features give against particular things rather than in
/// general: a fighter's bravery against fear, a superstitious barbarian's against magic.
/// </summary>
/// <remarks>
/// Situational by nature, so they are handed to the roll as it is made rather than kept on the
/// save's stack — a fighter is not braver against a fireball.
/// </remarks>
public static class ClassSaves
{
    /// <summary>
    /// Whether something counts as fear: it says so, or what it does is shake or frighten.
    /// </summary>
    /// <remarks>
    /// The second half is there so a spell that imposes Shaken counts even if its file forgets
    /// the descriptor. Being made shaken is being made afraid, whatever the paperwork says.
    /// </remarks>
    public static bool IsFear(Spell spell)
    {
        ArgumentNullException.ThrowIfNull(spell);

        return spell.Has("fear")
            || spell.Does.OfType<Bestow>().Any(bestow =>
                bestow.Effect.Condition is Condition.Shaken or Condition.Frightened);
    }

    /// <summary>
    /// Superstition: two, and one more for every four barbarian levels, while raging and only
    /// against magic.
    /// </summary>
    public static int Superstition(Creature creature)
    {
        ArgumentNullException.ThrowIfNull(creature);

        return creature.Choices.HasTalent(TalentEffect.Superstition)
            ? 2 + (ClassFeatures.LevelOf(creature, FeatureIds.Rage) / 4)
            : 0;
    }

    /// <summary>
    /// Everything that helps this creature resist this spell or power on this save, beyond what
    /// is already on the save itself.
    /// </summary>
    /// <remarks>
    /// Every spell and every power counts as magic for superstition: the powers are all
    /// spell-like or supernatural, which is exactly what the rule names.
    /// </remarks>
    public static IEnumerable<Modifier> Against(Creature target, Spell spell, Save save)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(spell);

        if (save == Save.Will && IsFear(spell) && Martial.Bravery(target) is > 0 and var brave)
        {
            yield return new Modifier(brave, BonusType.Untyped, "Bravery");
        }

        if (Rage.IsRaging(target) && Superstition(target) is > 0 and var wary)
        {
            yield return new Modifier(wary, BonusType.Morale, "Superstition");
        }

        // Not a class feature, but the same shape of bonus: an elf's against enchantments.
        foreach (var racial in target.Race?.SaveBonusesAgainst(spell) ?? [])
        {
            yield return racial;
        }
    }
}
