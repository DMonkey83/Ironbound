using Ironbound.Rules.Abilities;
using Ironbound.Rules.Creatures;

namespace Ironbound.Rules.Items;

/// <summary>How much somebody is carrying, measured against what they can.</summary>
public enum LoadCategory
{
    Light,

    /// <summary>Slowed, Dexterity to armour class capped at +3, −3 on checks, running ×4.</summary>
    Medium,

    /// <summary>Slowed, Dexterity capped at +1, −6 on checks, running ×3.</summary>
    Heavy,

    /// <summary>More than heavy: five feet a round as a whole round's work, and no walking at all between fights.</summary>
    Overloaded,
}

/// <summary>The most somebody can carry at each load, in pounds.</summary>
public sealed record CarryingCapacity(decimal Light, decimal Medium, decimal Heavy)
{
    public static CarryingCapacity None { get; } = new(0, 0, 0);

    /// <summary>Which load a weight comes to.</summary>
    public LoadCategory Classify(decimal pounds) =>
        pounds <= Light ? LoadCategory.Light
        : pounds <= Medium ? LoadCategory.Medium
        : pounds <= Heavy ? LoadCategory.Heavy
        : LoadCategory.Overloaded;

    public static CarryingCapacity operator +(CarryingCapacity a, CarryingCapacity b) =>
        new(a.Light + b.Light, a.Medium + b.Medium, a.Heavy + b.Heavy);

    public override string ToString() =>
        $"light to {Pricing.Pounds(Light)}, medium to {Pricing.Pounds(Medium)}, heavy to {Pricing.Pounds(Heavy)}";
}

/// <summary>A weight against a capacity, and what that comes to.</summary>
public sealed record LoadReport(decimal Weight, CarryingCapacity Capacity, LoadCategory Category)
{
    public override string ToString() =>
        $"{Encumbrance.Name(Category)} load, {Pricing.Pounds(Weight)} of {Pricing.Pounds(Capacity.Light)} light";
}

/// <summary>
/// The book's carrying capacity and what a heavier load does.
/// </summary>
/// <remarks>
/// Table 7-4 of the Core Rulebook, the numbers being Open Game Content: a limit for each of
/// light, medium and heavy by Strength from 1 to 29, and four times as much for every ten points
/// above that. Size multiplies it, and a creature on four legs carries more than one on two.
/// <para>
/// A creature's own load is what it wears and what hangs on its belt. The bag the party shares
/// is a campaign's, not the rules', so it reaches a creature through <see cref="Creature.SharedLoad"/>:
/// whatever the campaign says the whole party's load is, which counts if it is the worse of the
/// two. Medium and heavy loads do what medium and heavy armour do, and do not stack with it —
/// each figure is the worse of the two.
/// </para>
/// </remarks>
public static class Encumbrance
{
    /// <summary>The heaviest light, medium and heavy loads for Strength 1 to 29, in pounds.</summary>
    private static readonly (int Light, int Medium, int Heavy)[] Table =
    [
        (0, 0, 0),
        (3, 6, 10),
        (6, 13, 20),
        (10, 20, 30),
        (13, 26, 40),
        (16, 33, 50),
        (20, 40, 60),
        (23, 46, 70),
        (26, 53, 80),
        (30, 60, 90),
        (33, 66, 100),
        (38, 76, 115),
        (43, 86, 130),
        (50, 100, 150),
        (58, 116, 175),
        (66, 133, 200),
        (76, 153, 230),
        (86, 173, 260),
        (100, 200, 300),
        (116, 233, 350),
        (133, 266, 400),
        (153, 306, 460),
        (173, 346, 520),
        (200, 400, 600),
        (233, 466, 700),
        (266, 533, 800),
        (306, 613, 920),
        (346, 693, 1040),
        (400, 800, 1200),
        (466, 933, 1400),
    ];

    /// <summary>What a medium load caps Dexterity to armour class at.</summary>
    public const int MediumMaxDexterity = 3;

    /// <summary>What a heavy load caps it at.</summary>
    public const int HeavyMaxDexterity = 1;

    public const int MediumCheckPenalty = -3;

    public const int HeavyCheckPenalty = -6;

    /// <summary>
    /// The limits for a Strength score, a size and a number of legs. Strength 0 carries nothing.
    /// </summary>
    public static CarryingCapacity Capacity(int strength, CreatureSize size = CreatureSize.Medium, bool quadruped = false)
    {
        if (strength <= 0)
        {
            return CarryingCapacity.None;
        }

        // Every ten points past 29 is four times as much again: Strength 30 carries what 20 does,
        // four times over.
        var factor = 1m;
        while (strength >= Table.Length)
        {
            strength -= 10;
            factor *= 4;
        }

        factor *= SizeFactor(size, quadruped);
        var (light, medium, heavy) = Table[strength];

        return new CarryingCapacity(light * factor, medium * factor, heavy * factor);
    }

    /// <summary>A creature's own limits, from its Strength as it stands now — a rage counts.</summary>
    public static CarryingCapacity Capacity(Creature creature)
    {
        ArgumentNullException.ThrowIfNull(creature);

        return creature.Abilities[Ability.Strength].Score is { } strength
            ? Capacity(strength, creature.Size, creature.Quadruped)
            : CarryingCapacity.None;
    }

    /// <summary>
    /// How much more than a Medium biped a creature carries: a Small one three quarters, a Large
    /// one twice, and a quadruped half as much again as a biped of its size (a Small one the same
    /// as a Medium biped).
    /// </summary>
    public static decimal SizeFactor(CreatureSize size, bool quadruped = false)
    {
        var biped = size switch
        {
            CreatureSize.Fine => 0.125m,
            CreatureSize.Diminutive => 0.25m,
            CreatureSize.Tiny => 0.5m,
            CreatureSize.Small => 0.75m,
            CreatureSize.Medium => 1m,
            CreatureSize.Large => 2m,
            CreatureSize.Huge => 4m,
            CreatureSize.Gargantuan => 8m,
            _ => 16m,
        };

        if (!quadruped)
        {
            return biped;
        }

        return size switch
        {
            CreatureSize.Fine => 0.25m,
            CreatureSize.Diminutive => 0.5m,
            CreatureSize.Tiny => 0.75m,
            CreatureSize.Small => 1m,
            _ => biped * 1.5m,
        };
    }

    /// <summary>
    /// What an item weighs on a wearer of this size. Gear is made for whoever wears it, so a
    /// halfling's leather armour weighs half a man's, and an ogre's axe twice a man's; the price
    /// stays as it is. Only what is worn or carried is sized: an item in the bag counts at its
    /// Medium weight, because nothing tracks what size an item was made for.
    /// </summary>
    public static decimal WornWeight(ItemDefinition item, CreatureSize wearer)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (item.Kind is not (ItemKind.Weapon or ItemKind.Armour or ItemKind.Shield))
        {
            return item.Weight;
        }

        return item.Weight * wearer switch
        {
            CreatureSize.Fine or CreatureSize.Diminutive or CreatureSize.Tiny => 0.1m,
            CreatureSize.Small => 0.5m,
            CreatureSize.Medium => 1m,
            CreatureSize.Large => 2m,
            CreatureSize.Huge => 5m,
            CreatureSize.Gargantuan => 8m,
            _ => 12m,
        };
    }

    /// <summary>What a creature has on it: everything worn and everything on its belt, natural weapons aside.</summary>
    public static decimal Carried(Creature creature)
    {
        ArgumentNullException.ThrowIfNull(creature);

        var total = 0m;
        foreach (var entry in creature.Equipment.Worn)
        {
            total += WornWeight(entry.Item, creature.Size);
        }

        return total;
    }

    /// <summary>A creature's own load: its gear against its own Strength.</summary>
    public static LoadReport Load(Creature creature)
    {
        var capacity = Capacity(creature);
        var weight = Carried(creature);

        return new LoadReport(weight, capacity, capacity.Classify(weight));
    }

    /// <summary>
    /// The load that actually counts: the worse of the creature's own and the one its party
    /// shares, if it is in one.
    /// </summary>
    public static LoadCategory Effective(Creature creature)
    {
        ArgumentNullException.ThrowIfNull(creature);

        var own = Load(creature).Category;
        var shared = creature.SharedLoad?.Invoke() ?? LoadCategory.Light;

        return Worse(own, shared);
    }

    public static LoadCategory Worse(LoadCategory a, LoadCategory b) => a > b ? a : b;

    /// <summary>The most Dexterity to armour class a load lets through, or null for a light one.</summary>
    public static int? MaxDexterity(LoadCategory load) => load switch
    {
        LoadCategory.Light => null,
        LoadCategory.Medium => MediumMaxDexterity,
        _ => HeavyMaxDexterity,
    };

    /// <summary>What a load costs on Strength- and Dexterity-based checks: nothing, −3 or −6.</summary>
    public static int CheckPenalty(LoadCategory load) => load switch
    {
        LoadCategory.Light => 0,
        LoadCategory.Medium => MediumCheckPenalty,
        _ => HeavyCheckPenalty,
    };

    /// <summary>Whether a load slows its bearer as medium armour does: thirty feet to twenty.</summary>
    public static bool Slows(LoadCategory load) => load != LoadCategory.Light;

    /// <summary>"light", "medium", "heavy", "overloaded".</summary>
    public static string Name(LoadCategory load) => load switch
    {
        LoadCategory.Light => "light",
        LoadCategory.Medium => "medium",
        LoadCategory.Heavy => "heavy",
        _ => "overloaded",
    };
}
