using Ironbound.Rules.Abilities;
using Ironbound.Rules.Combat;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Dice;
using Ironbound.Rules.Modifiers;

namespace Ironbound.Rules.Content;

/// <summary>
/// The other end of a double weapon: its own dice and its own critical.
/// </summary>
/// <remarks>
/// Written down and not yet swung. Fighting with both ends is two-weapon fighting, which the game
/// does not have; until it does, a quarterstaff is fought with as one two-handed weapon, its
/// first end.
/// </remarks>
public sealed record WeaponHead(string DamageMedium, string? DamageSmall, int ThreatsOn, int Multiplier);

/// <summary>
/// A weapon as written down — one row of the equipment tables. Held as a definition rather than
/// an instance because a <see cref="WeaponAttack"/> owns modifier stacks — two goblins must not
/// share one scimitar.
/// </summary>
/// <remarks>
/// Most of these come from the importer in <c>tools/import_weapons.py</c>, a few are written by
/// hand (a bite, an ogre's axe), and every one of them says the same things: how much training
/// it wants, how it is held, its dice for a Small and a Medium wielder, and what its Special
/// column says. Anything the engine cannot fight with at all — a net, a cannon — is kept with
/// <see cref="Usable"/> false, so the catalogue is complete without the attack lists filling up
/// with things that would do nothing.
/// </remarks>
public sealed record WeaponDefinition
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public WeaponCategory Category { get; init; } = WeaponCategory.Simple;

    public WeaponHands Hands { get; init; } = WeaponHands.OneHanded;

    /// <summary>The dice for a Medium wielder: <c>damageMedium</c>, or <c>damage</c> in an older file.</summary>
    public string Damage { get; init; } = "1d6";

    /// <summary>The dice for a Small wielder, or null to step down from <see cref="Damage"/>.</summary>
    public string? DamageSmall { get; init; }

    /// <summary>Every type it deals, first one first.</summary>
    public IReadOnlyList<DamageType> DamageTypes { get; init; } = [DamageType.Bludgeoning];

    /// <summary>The first of <see cref="DamageTypes"/>: what its damage is rolled as.</summary>
    public DamageType DamageType => DamageTypes.Count > 0 ? DamageTypes[0] : DamageType.Bludgeoning;

    public DamageRule DamageRule { get; init; } = DamageRule.Single;

    public int ThreatsOn { get; init; } = 20;

    public int Multiplier { get; init; } = 2;

    /// <summary>
    /// How much Strength reaches damage, when the file says so outright. Null — the usual case —
    /// works it out from how the weapon is held; see <see cref="DamageScale"/>.
    /// </summary>
    public AbilityDamageScale? Scale { get; init; }

    /// <summary>What aims it when the file says so; null for Strength in melee and Dexterity at range.</summary>
    public Ability? AttackAbility { get; init; }

    public Ability DamageAbility { get; init; } = Ability.Strength;

    public int Enhancement { get; init; }

    /// <summary>
    /// The range increment in feet. On a ranged weapon it is how far the arrow or the javelin
    /// goes; on a melee weapon it means the weapon can also be thrown that far.
    /// </summary>
    public int Range { get; init; }

    /// <summary>Thrown rather than shot: a dagger or a javelin, not a bow.</summary>
    public bool Thrown { get; init; }

    /// <summary>How many increments it carries, when the file overrides the usual ten or five.</summary>
    public int? MaximumIncrements { get; init; }

    public WeaponSpecial Specials { get; init; }

    /// <summary>The other end of a double weapon, or null.</summary>
    public WeaponHead? SecondHead { get; init; }

    /// <summary>The fighter weapon groups it belongs to, by id.</summary>
    public IReadOnlyList<string> Groups { get; init; } = [];

    /// <summary>Light, natural, or otherwise a weapon Weapon Finesse can aim with Dexterity.</summary>
    public bool Finesse { get; init; }

    /// <summary>In gold pieces. Two silver is 0.2.</summary>
    public double Cost { get; init; }

    /// <summary>In pounds.</summary>
    public double Weight { get; init; }

    /// <summary>One line in our own words: what the book's paragraph does, and whether the game does it.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>False for something the engine cannot fight with at all. Never built into an attack.</summary>
    public bool Usable { get; init; } = true;

    public FirearmEra Firearm { get; init; }

    /// <summary>The highest natural roll that misfires. Guns only.</summary>
    public int Misfire { get; init; }

    /// <summary>How many shots it holds. Data only: reloading is not modelled.</summary>
    public int Capacity { get; init; }

    /// <summary>The radius in feet a burst gun would catch. Data only; see <see cref="Firearms"/>.</summary>
    public int ExplosionRadius { get; init; }

    /// <summary>One or two hands, for a ranged weapon whose table says: a pistol or a musket.</summary>
    public WeaponHands? Grip { get; init; }

    public bool Has(WeaponSpecial special) => special != WeaponSpecial.None && (Specials & special) == special;

    /// <summary>Shot or thrown, and only that.</summary>
    public bool IsRanged => Hands == WeaponHands.Ranged && Range > 0;

    /// <summary>A melee weapon that can also be thrown: the dagger, the spear, the trident.</summary>
    public bool IsThrowable => Hands != WeaponHands.Ranged && Thrown && Range > 0;

    /// <summary>
    /// How much Strength reaches its damage when the file does not say: half again in both hands,
    /// all of it from a thrown weapon, none from a bow or a crossbow. A sling is the exception the
    /// books make, and its file says so with an explicit <see cref="Scale"/>.
    /// </summary>
    public AbilityDamageScale DamageScale => Scale ?? Hands switch
    {
        WeaponHands.TwoHanded => AbilityDamageScale.OneAndAHalf,
        WeaponHands.Ranged => Thrown ? AbilityDamageScale.Full : AbilityDamageScale.None,
        _ => AbilityDamageScale.Full,
    };

    /// <summary>The dice a creature of this size swings. Natural attacks keep their file's dice.</summary>
    public string DiceFor(CreatureSize size) =>
        Category == WeaponCategory.Natural ? Damage : WeaponDice.For(size, Damage, DamageSmall);

    /// <summary>The attack it makes in the hands of a creature of <paramref name="size"/>.</summary>
    public WeaponAttack Build(string? name = null, CreatureSize size = CreatureSize.Medium)
    {
        var ranged = IsRanged;
        var weapon = Shell(name ?? Name, size);

        return Finish(new WeaponAttack(weapon.Name, weapon.Attack, weapon.Damage)
        {
            Kind = Id,
            Groups = Groups,
            Finesse = Finesse,
            Category = Category,
            Hands = Hands,
            Specials = Specials,
            DamageTypes = DamageTypes,
            DamageRule = DamageRule,
            Firearm = Firearm,
            Misfire = Misfire,
            AttackAbility = AttackAbility ?? (ranged ? Ability.Dexterity : Ability.Strength),
            DamageAbility = DamageAbility,
            DamageScale = DamageScale,
            RangeIncrement = ranged ? Range : 0,
            MaximumIncrements = MaximumIncrements
                ?? (Thrown ? WeaponAttack.ThrownIncrements : WeaponAttack.ProjectileIncrements),
        });
    }

    /// <summary>
    /// The same weapon leaving the hand: "dagger (thrown)", Dexterity to hit, Strength to damage,
    /// five increments at most. Null for anything that cannot be thrown.
    /// </summary>
    public WeaponAttack? BuildThrown(string? name = null, CreatureSize size = CreatureSize.Medium)
    {
        if (!IsThrowable)
        {
            return null;
        }

        var shell = Shell($"{name ?? Name} (thrown)", size);

        return Finish(new WeaponAttack(shell.Name, shell.Attack, shell.Damage)
        {
            Kind = Id,
            Groups = Groups,
            Finesse = Finesse,
            Category = Category,
            Hands = Hands,
            Specials = Specials,
            DamageTypes = DamageTypes,
            DamageRule = DamageRule,
            IsThrownUse = true,
            AttackAbility = Ability.Dexterity,
            DamageAbility = DamageAbility,
            DamageScale = Scale ?? AbilityDamageScale.Full,
            RangeIncrement = Range,
            MaximumIncrements = MaximumIncrements ?? WeaponAttack.ThrownIncrements,
        });
    }

    /// <summary>Every attack it makes: the weapon, and the weapon thrown if it can be.</summary>
    public IReadOnlyList<WeaponAttack> BuildUses(string? name = null, CreatureSize size = CreatureSize.Medium) =>
        BuildThrown(name, size) is { } thrown ? [Build(name, size), thrown] : [Build(name, size)];

    private (string Name, Attack Attack, DamagePacket Damage) Shell(string name, CreatureSize size)
    {
        var attack = new Attack { Critical = new CriticalProfile(ThreatsOn, Multiplier) };
        var component = new DamageComponent(DiceExpression.Parse(DiceFor(size)), DamageType, multipliedOnCritical: true)
        {
            Nonlethal = Has(WeaponSpecial.Nonlethal),
        };

        return (name, attack, DamagePacket.Of(component));
    }

    private WeaponAttack Finish(WeaponAttack weapon)
    {
        if (Enhancement != 0)
        {
            weapon.Attack.Modifiers.Add(Enhancement, BonusType.Enhancement, weapon.Name);
            weapon.DamageModifiers.Add(Enhancement, BonusType.Enhancement, weapon.Name);
        }

        return weapon;
    }
}
