using Ironbound.Rules.Abilities;
using Ironbound.Rules.Defense;
using Ironbound.Rules.Effects;
using Ironbound.Rules.Modifiers;
using Ironbound.Rules.Saves;
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
    private CreatureSize _size = CreatureSize.Medium;

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
        Defenses = new DamageDefenses();
        Effects = new EffectCollection(this);
        Saves = new SavingThrows(abilities);
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

    /// <summary>
    /// How big it is. Setting this immediately moves its armour class and attack bonus, because
    /// size is a property of the creature rather than a bonus from some source — Enlarge Person
    /// changes what you are, and everything derived from it has to follow.
    /// </summary>
    public CreatureSize Size
    {
        get => _size;
        set
        {
            _size = value;
            ArmorClass.SizeModifier = CreatureSizes.Modifier(value);
        }
    }

    /// <summary>How far it can touch, in feet. Tiny and smaller cannot reach out of their square.</summary>
    public int Reach => CreatureSizes.Reach(_size);

    /// <summary>Base movement in feet per round, before anything hurries or hinders it.</summary>
    public int Speed { get; set; } = 30;

    /// <summary>Haste, a monk's fast movement, heavy armour, difficult circumstances.</summary>
    public ModifierStack SpeedModifiers { get; } = new();

    public int CurrentSpeed => Math.Max(0, Speed + SpeedModifiers.Total);

    /// <summary>What stands between a damage roll and <see cref="HitPoints"/>.</summary>
    public DamageDefenses Defenses { get; }

    /// <summary>Fortitude, Reflex and Will.</summary>
    public SavingThrows Saves { get; }

    /// <summary>
    /// What the creature brings to every attack roll it makes: base attack bonus, size, and
    /// buffs like Bless or Prayer. Met with the weapon's own modifiers in one stacking pass.
    /// </summary>
    public ModifierStack AttackModifiers { get; } = new();

    /// <summary>What the creature brings to damage: Inspire Courage, Weapon Specialization.</summary>
    public ModifierStack DamageModifiers { get; } = new();

    /// <summary>Improved Initiative and anything else that decides who moves first.</summary>
    public ModifierStack InitiativeModifiers { get; } = new();

    /// <summary>Buffs, conditions and anything else running on a clock.</summary>
    public EffectCollection Effects { get; }

    public HitPoints HitPoints { get; }

    public bool IsAlive => HitPoints.IsAlive;

    public bool IsConscious => HitPoints.IsConscious;

    public override string ToString() => $"{Name} — {ArmorClass} — {HitPoints}";
}
