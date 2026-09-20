using Ironbound.Rules.Abilities;
using Ironbound.Rules.Combat;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Magic;
using Ironbound.Rules.Modifiers;
using Ironbound.Rules.Saves;

namespace Ironbound.Rules.Content;

/// <summary>Something wrong with a content file, named precisely enough to go and fix it.</summary>
public sealed record ContentProblem(string Source, string Field, string Message)
{
    public override string ToString() => $"{Source}: {Field}: {Message}";
}

/// <summary>
/// A weapon as written down. Held as a definition rather than an instance because a
/// <see cref="WeaponAttack"/> owns modifier stacks — two goblins must not share one scimitar.
/// </summary>
public sealed record WeaponDefinition
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public string Damage { get; init; } = "1d6";

    public DamageType DamageType { get; init; } = DamageType.Bludgeoning;

    public int ThreatsOn { get; init; } = 20;

    public int Multiplier { get; init; } = 2;

    public AbilityDamageScale Scale { get; init; } = AbilityDamageScale.Full;

    public Ability AttackAbility { get; init; } = Ability.Strength;

    public Ability DamageAbility { get; init; } = Ability.Strength;

    public int Enhancement { get; init; }

    /// <summary>Zero for a melee weapon; anything else makes it a ranged one.</summary>
    public int RangeIncrement { get; init; }

    public int MaximumIncrements { get; init; } = WeaponAttack.ProjectileIncrements;

    public WeaponAttack Build()
    {
        var weapon = RangeIncrement > 0
            ? WeaponAttack.Ranged(
                Name,
                Damage,
                DamageType,
                RangeIncrement,
                new CriticalProfile(ThreatsOn, Multiplier),
                MaximumIncrements,
                Scale,
                AttackAbility,
                DamageAbility)
            : WeaponAttack.Melee(
                Name,
                Damage,
                DamageType,
                new CriticalProfile(ThreatsOn, Multiplier),
                Scale,
                AttackAbility,
                DamageAbility);

        if (Enhancement != 0)
        {
            weapon.Attack.Modifiers.Add(Enhancement, BonusType.Enhancement, Name);
            weapon.DamageModifiers.Add(Enhancement, BonusType.Enhancement, Name);
        }

        return weapon;
    }
}

/// <summary>How many spell slots of one level a creature has.</summary>
public readonly record struct SlotDefinition(int Level, int Count);

/// <summary>A creature as written down, ready to be stamped out as many times as needed.</summary>
public sealed record CreatureDefinition
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    /// <summary>Strength, Dexterity, Constitution, Intelligence, Wisdom, Charisma.</summary>
    public required IReadOnlyList<int> Abilities { get; init; }

    public int HitPoints { get; init; } = 8;

    public int HitDice { get; init; } = 1;

    public int Level { get; init; } = 1;

    public CreatureSize Size { get; init; } = CreatureSize.Medium;

    public int Speed { get; init; } = 30;

    public int BaseAttack { get; init; }

    public int Armour { get; init; }

    public int NaturalArmour { get; init; }

    public Save? GoodSave { get; init; }

    public IReadOnlyList<string> Weapons { get; init; } = [];

    public Ability CastingAbility { get; init; } = Ability.Intelligence;

    public int CasterLevel { get; init; }

    public IReadOnlyList<SlotDefinition> Slots { get; init; } = [];

    public IReadOnlyList<string> Spells { get; init; } = [];

    public Creature Build(ContentLibrary library, RuleOptions? rules = null, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(library);

        var scores = new AbilityScores(
            Abilities[0], Abilities[1], Abilities[2], Abilities[3], Abilities[4], Abilities[5]);

        var creature = new Creature(name ?? Name, scores, HitPoints, HitDice, rules)
        {
            Size = Size,
            Speed = Speed,
        };

        if (BaseAttack != 0)
        {
            creature.AttackModifiers.Add(BaseAttack, BonusType.Untyped, "Base Attack Bonus");
        }

        if (Armour != 0)
        {
            creature.ArmorClass.Modifiers.Add(Armour, BonusType.Armor, "Armour");
        }

        if (NaturalArmour != 0)
        {
            creature.ArmorClass.Modifiers.Add(NaturalArmour, BonusType.NaturalArmor, "Hide");
        }

        foreach (var save in SaveInfo.All)
        {
            creature.Saves[save].Base = save == GoodSave
                ? SaveProgression.Good(Level)
                : SaveProgression.Poor(Level);
        }

        foreach (var weapon in Weapons)
        {
            if (library.BuildWeapon(weapon) is { } built)
            {
                creature.Attacks.Add(built);
            }
        }

        creature.Spells.CastingAbility = CastingAbility;
        creature.Spells.CasterLevel = CasterLevel;

        foreach (var slot in Slots)
        {
            creature.Spells.SetSlots(slot.Level, slot.Count);
        }

        foreach (var spell in Spells)
        {
            if (library.GetSpell(spell) is { } known)
            {
                creature.Spells.Prepare(known);
            }
        }

        return creature;
    }
}

/// <summary>
/// Where one creature starts, and whose side it is on. <paramref name="Name"/> overrides the
/// definition's, so three copies of one goblin can be told apart.
/// </summary>
public readonly record struct PlacementDefinition(
    string CreatureId,
    int X,
    int Y,
    bool Party,
    string? Name = null);

/// <summary>
/// A fight as written down: the ground, and who stands where on it.
/// </summary>
/// <remarks>
/// Deliberately stops at data. Turning it into a playable battle needs the notion of sides, which
/// lives a layer up in <c>Ironbound.Simulation</c> — the rules know about allegiance, not about
/// which allegiance happens to be the player's.
/// </remarks>
public sealed record EncounterDefinition
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public int Width { get; init; } = 16;

    public int Height { get; init; } = 12;

    public IReadOnlyList<PlacementDefinition> Blocked { get; init; } = [];

    public IReadOnlyList<PlacementDefinition> Difficult { get; init; } = [];

    public IReadOnlyList<PlacementDefinition> Placements { get; init; } = [];
}
