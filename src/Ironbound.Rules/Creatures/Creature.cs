using Ironbound.Rules.Abilities;
using Ironbound.Rules.Defense;
using Ironbound.Rules.Dice;

namespace Ironbound.Rules.Creatures;

/// <summary>
/// Anything that can be attacked: its ability scores, the armour class derived from them, and
/// its hit points. A holder, in the same shape as <see cref="ArmorClass"/> and
/// <see cref="AbilityScore"/> — the rules that act on a creature live outside it.
/// </summary>
/// <remarks>
/// Size is not computed here. A creature's size modifier is an ordinary
/// <see cref="Modifiers.BonusType.Size"/> modifier added by whatever builds the creature, which
/// keeps this class from growing a second, parallel way to change a number.
/// </remarks>
public sealed class Creature
{
    public Creature(
        string name,
        AbilityScores abilities,
        int baseHitPoints,
        int hitDice,
        RuleOptions? rules = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(abilities);

        Name = name;
        Abilities = abilities;
        Rules = rules ?? RuleOptions.Pathfinder;
        ArmorClass = new ArmorClass(abilities.Dexterity);
        HitPoints = new HitPoints(baseHitPoints, hitDice, abilities.Constitution, Rules);
    }

    /// <summary>Builds a creature whose hit points come from its hit die, per the rule options.</summary>
    public static Creature Roll(
        string name,
        AbilityScores abilities,
        int hitDieSides,
        int hitDice,
        IRandomSource random,
        RuleOptions? rules = null)
    {
        rules ??= RuleOptions.Pathfinder;
        return new Creature(
            name,
            abilities,
            HitPoints.RollBase(hitDieSides, hitDice, random, rules),
            hitDice,
            rules);
    }

    public string Name { get; }

    public RuleOptions Rules { get; }

    public AbilityScores Abilities { get; }

    public ArmorClass ArmorClass { get; }

    public HitPoints HitPoints { get; }

    public bool IsAlive => HitPoints.IsAlive;

    public bool IsConscious => HitPoints.IsConscious;

    public override string ToString() => $"{Name} — {ArmorClass} — {HitPoints}";
}
