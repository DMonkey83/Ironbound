using Ironbound.Rules.Combat;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Magic;

namespace Ironbound.Rules.Items;

/// <summary>What sort of thing is used up by using it.</summary>
public enum ConsumableKind
{
    /// <summary>A spell in a bottle. Whoever drinks it is both caster and target.</summary>
    Potion,

    /// <summary>A potion rubbed on rather than drunk. The book counts it as a potion in every other way.</summary>
    Oil,

    /// <summary>Acid, alchemist's fire: a flask thrown to break on somebody and splash those beside them.</summary>
    Splash,

    /// <summary>A bag of glue thrown at somebody to stick them in place.</summary>
    Tanglefoot,

    /// <summary>A stone thrown at the ground to go off with a deafening bang.</summary>
    Thunderstone,
}

/// <summary>What a thrown item may be aimed at.</summary>
public enum ThrowAim
{
    /// <summary>Not thrown at all.</summary>
    None,

    Creature,

    /// <summary>A spot on the ground, armour class 5. A creature can be clicked to aim at its square.</summary>
    Square,

    /// <summary>Either: a splash weapon can be thrown straight at somebody or at a square near them.</summary>
    Either,
}

/// <summary>
/// What a consumable does when it is used, as its item file writes it under <c>"use"</c>.
/// </summary>
/// <remarks>
/// One record for every kind rather than a family of them, because the fields are few and a
/// file is easier to check when every kind reads the same names. Which fields count depends on
/// <see cref="Kind"/>: a potion has a spell and a caster level; a splash weapon a range, damage
/// and splash; a tanglefoot bag a save, a duration and an escape; a thunderstone a range, a
/// radius, a save and how long the deafness lasts.
/// </remarks>
public sealed record ConsumableDefinition
{
    /// <summary>The most range increments anything thrown can carry.</summary>
    public const int MaximumIncrements = 5;

    public ConsumableKind Kind { get; init; }

    /// <summary>The spell in a potion or an oil, by id. Null for anything else.</summary>
    public string? SpellId { get; init; }

    /// <summary>That spell, found when the content was loaded. Null until then, or if there is none.</summary>
    public Spell? Spell { get; init; }

    /// <summary>The level it was brewed at: what its numbers scale with, and part of its price.</summary>
    public int CasterLevel { get; init; } = 1;

    /// <summary>The range increment of something thrown, in feet.</summary>
    public int RangeIncrement { get; init; }

    /// <summary>A direct hit's dice: "1d6".</summary>
    public string? Damage { get; init; }

    public DamageType DamageType { get; init; } = DamageType.Untyped;

    /// <summary>What everybody else within five feet of where it lands takes.</summary>
    public int Splash { get; init; }

    /// <summary>What a direct hit burns for a round later, unless put out: alchemist's fire's 1d6.</summary>
    public string? Burn { get; init; }

    /// <summary>The save its file wrote: the tanglefoot bag's Reflex, the thunderstone's Fortitude.</summary>
    public int SaveDc { get; init; }

    /// <summary>How long it holds, as dice of rounds: the tanglefoot bag's "2d4".</summary>
    public string? Rounds { get; init; }

    /// <summary>What Strength or Escape Artist has to beat to tear free of a tanglefoot bag.</summary>
    public int EscapeDc { get; init; }

    /// <summary>How far a thunderstone's bang carries, in feet.</summary>
    public int RadiusFeet { get; init; }

    /// <summary>How many hours a thunderstone deafens for.</summary>
    public int DeafenedHours { get; init; }

    /// <summary>A potion or an oil: a spell, used on oneself.</summary>
    public bool IsPotion => Kind is ConsumableKind.Potion or ConsumableKind.Oil;

    public bool IsThrown => Kind is ConsumableKind.Splash or ConsumableKind.Tanglefoot or ConsumableKind.Thunderstone;

    /// <summary>Whether a throw at a creature is against its touch armour class.</summary>
    public bool TouchAttack => Kind is ConsumableKind.Splash or ConsumableKind.Tanglefoot;

    public ThrowAim Aim => Kind switch
    {
        ConsumableKind.Splash => ThrowAim.Either,
        ConsumableKind.Tanglefoot => ThrowAim.Creature,
        ConsumableKind.Thunderstone => ThrowAim.Square,
        _ => ThrowAim.None,
    };

    /// <summary>Whether its spell puts hit points back: what a healing potion is.</summary>
    public bool Heals => Spell?.Does.OfType<Restore>().Any() == true;

    /// <summary>The farthest it can be thrown, in feet: five increments. Nought for anything not thrown.</summary>
    public int MaximumRange => IsThrown ? RangeIncrement * MaximumIncrements : 0;

    /// <summary>
    /// The difficulty class of a save against it: a potion's from its spell's level, as
    /// <see cref="Consumables.PotionDc"/> works it out, and anything else's as its file wrote.
    /// </summary>
    public int DifficultyClass => IsPotion && Spell is { } spell ? Consumables.PotionDc(spell.Level) : SaveDc;
}

/// <summary>So many of one consumable on somebody's belt, for a quick-slot bar to show as one button.</summary>
public sealed record BeltStack(ItemDefinition Item, int Count)
{
    /// <summary>"potion of cure light wounds ×3".</summary>
    public override string ToString() => Count > 1 ? $"{Item.Name} ×{Count}" : Item.Name;
}

/// <summary>
/// What a creature has hung on its belt to use, and the book's numbers for potions.
/// </summary>
/// <remarks>
/// The belt is <see cref="EquipmentSlot.Carried"/>: one entry for each thing hung on it, three
/// potions being three entries, so using one takes one away. Only what is on the belt can be
/// used in a fight; anything in the party's bag has to be hung there first, between fights.
/// </remarks>
public static class Consumables
{
    /// <summary>
    /// Every consumable on a creature's belt, one stack for each kind of item, in the order the
    /// first of each was hung there.
    /// </summary>
    public static IReadOnlyList<BeltStack> OnBelt(Creature creature)
    {
        ArgumentNullException.ThrowIfNull(creature);

        var stacks = new List<BeltStack>();
        foreach (var entry in creature.Equipment.Worn.Where(entry => IsOnBelt(entry) && entry.Item.IsConsumable))
        {
            var index = stacks.FindIndex(stack => SameItem(stack.Item, entry.Item));
            if (index >= 0)
            {
                stacks[index] = stacks[index] with { Count = stacks[index].Count + 1 };
            }
            else
            {
                stacks.Add(new BeltStack(entry.Item, 1));
            }
        }

        return stacks;
    }

    /// <summary>How many of an item a creature has on its belt.</summary>
    public static int Count(Creature creature, ItemDefinition item)
    {
        ArgumentNullException.ThrowIfNull(creature);
        ArgumentNullException.ThrowIfNull(item);

        return creature.Equipment.Worn.Count(entry => IsOnBelt(entry) && SameItem(entry.Item, item));
    }

    /// <summary>
    /// The save against a potion: ten, plus the spell's level, plus the modifier of the least
    /// score that could cast it (ten plus its level), which is half the level rounded down.
    /// </summary>
    public static int PotionDc(int spellLevel)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(spellLevel);
        return 10 + spellLevel + (spellLevel / 2);
    }

    /// <summary>Whether two definitions are the same item, by id: three potions are one stack.</summary>
    internal static bool SameItem(ItemDefinition a, ItemDefinition b) =>
        string.Equals(a.Id, b.Id, StringComparison.Ordinal);

    internal static bool IsOnBelt(EquippedItem entry) =>
        entry.Slot == EquipmentSlot.Carried && !entry.IsOutOfHand;
}
