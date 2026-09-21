using Ironbound.Rules.Abilities;
using Ironbound.Rules.Classes;
using Ironbound.Rules.Combat;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Defense;
using Ironbound.Rules.Items;
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

    public WeaponAttack Build(string? name = null)
    {
        name ??= Name;
        var weapon = RangeIncrement > 0
            ? WeaponAttack.Ranged(
                name,
                Damage,
                DamageType,
                RangeIncrement,
                new CriticalProfile(ThreatsOn, Multiplier),
                MaximumIncrements,
                Scale,
                AttackAbility,
                DamageAbility)
            : WeaponAttack.Melee(
                name,
                Damage,
                DamageType,
                new CriticalProfile(ThreatsOn, Multiplier),
                Scale,
                AttackAbility,
                DamageAbility);

        if (Enhancement != 0)
        {
            weapon.Attack.Modifiers.Add(Enhancement, BonusType.Enhancement, name);
            weapon.DamageModifiers.Add(Enhancement, BonusType.Enhancement, name);
        }

        return weapon;
    }
}

/// <summary>
/// Damage reduction as written down: "DR 10/silver" is ten, bypassed by silver.
/// </summary>
/// <remarks>
/// The defence layer has understood this since it was written. Until items existed it was a
/// wall with no door — a creature could be immune to the party's weapons with no way for them
/// ever to acquire the right one.
/// </remarks>
public readonly record struct ReductionDefinition(int Amount, DamageBypass BypassedBy, BypassMode Mode);

/// <summary>So many levels of a class, named by id until the library can resolve it.</summary>
public readonly record struct ClassLevelDefinition(string ClassId, int Level);

/// <summary>How many spell slots of one level a creature has.</summary>
public readonly record struct SlotDefinition(int Level, int Count);

/// <summary>A creature as written down, ready to be stamped out as many times as needed.</summary>
public sealed record CreatureDefinition
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    /// <summary>Strength, Dexterity, Constitution, Intelligence, Wisdom, Charisma.</summary>
    public required IReadOnlyList<int> Abilities { get; init; }

    /// <summary>Written down, or null to work it out from the classes.</summary>
    public int? HitPoints { get; init; }

    public int HitDice { get; init; } = 1;

    public int Level { get; init; } = 1;

    /// <summary>What it has levels in. Empty for something built from raw numbers instead.</summary>
    public IReadOnlyList<ClassLevelDefinition> Classes { get; init; } = [];

    public CreatureSize Size { get; init; } = CreatureSize.Medium;

    public int Speed { get; init; } = 30;

    public int BaseAttack { get; init; }

    public int Armour { get; init; }

    public int NaturalArmour { get; init; }

    public Save? GoodSave { get; init; }

    public IReadOnlyList<string> Weapons { get; init; } = [];

    /// <summary>Feats it has taken, by id.</summary>
    public IReadOnlyList<string> Feats { get; init; } = [];

    /// <summary>Items it is wearing and holding, by id. Equipped in the order written.</summary>
    public IReadOnlyList<string> Items { get; init; } = [];

    /// <summary>What it shrugs off, and what gets through anyway.</summary>
    public IReadOnlyList<ReductionDefinition> Reductions { get; init; } = [];

    public Ability? CastingAbility { get; init; }

    public int CasterLevel { get; init; }

    public IReadOnlyList<SlotDefinition> Slots { get; init; } = [];

    public IReadOnlyList<string> Spells { get; init; } = [];

    public Creature Build(ContentLibrary library, RuleOptions? rules = null, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(library);

        var scores = new AbilityScores(
            Abilities[0], Abilities[1], Abilities[2], Abilities[3], Abilities[4], Abilities[5]);

        var levels = new List<ClassLevel>();
        foreach (var taken in Classes)
        {
            // A class the library never heard of is already in Problems; carrying on with the
            // levels that do resolve beats refusing to build the creature at all.
            if (library.GetClass(taken.ClassId) is { } definition)
            {
                levels.Add(new ClassLevel(definition, taken.Level));
            }
        }

        var classed = levels.Count > 0;
        var level = classed ? Progression.TotalLevel(levels) : Level;
        var hitPoints = HitPoints ?? (classed ? Progression.HitPointsBase(levels, rules) : 8);

        var creature = new Creature(
            name ?? Name, scores, hitPoints, classed ? level : HitDice, rules)
        {
            DefinitionId = Id,
            Size = Size,
            Speed = Speed,
        };

        creature.BaseAttackBonus = classed ? Progression.BaseAttack(levels) : BaseAttack;

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
            creature.Saves[save].Base = classed
                ? Progression.SaveBase(save, levels)
                : save == GoodSave
                    ? SaveProgression.Good(level)
                    : SaveProgression.Poor(level);
        }

        // Before the weapons, because a feat that changes what a weapon does should already be
        // in place by the time one is handed over.
        foreach (var feat in Feats)
        {
            if (library.GetFeat(feat) is { } taken)
            {
                creature.Feats.Add(taken);
                taken.ApplyTo(creature);
            }
        }

        foreach (var reduction in Reductions)
        {
            creature.Defenses.Reduce(reduction.Amount, reduction.BypassedBy, reduction.Mode);
        }

        foreach (var id in Items)
        {
            if (library.GetItem(id) is { } item)
            {
                creature.Equipment.Equip(item, library.BuildItemWeapon(item));
            }
        }

        foreach (var weapon in Weapons)
        {
            if (library.BuildWeapon(weapon) is { } built)
            {
                creature.Attacks.Add(built);
            }
        }

        var caster = Progression.Caster(levels);

        creature.Spells.CastingAbility =
            CastingAbility ?? caster?.Class.CastingAbility ?? Ability.Intelligence;
        creature.Spells.CasterLevel =
            CasterLevel != 0 ? CasterLevel : Progression.CasterLevel(levels);

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
/// A run of fights, in order, and how much respite there is along the way.
/// </summary>
/// <remarks>
/// The smallest thing that turns a fight into a game. A single encounter is won or lost on its
/// own terms; a sequence of them is won or lost on what you have left when you reach the end,
/// which is the resource game the whole ruleset is built around. Spell slots only matter when
/// there is a next fight to have wanted them for.
/// </remarks>
public sealed record CampaignDefinition
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    /// <summary>The encounters, in the order they are fought.</summary>
    public IReadOnlyList<string> Encounters { get; init; } = [];

    /// <summary>
    /// How many times the party may stop and recover everything.
    /// </summary>
    /// <remarks>
    /// A budget rather than a cooldown, because a rest that costs nothing is one you always
    /// take, and a decision you always make the same way is not a decision. One rest across
    /// three fights is a question worth asking.
    /// </remarks>
    public int Rests { get; init; } = 1;
}

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
