using Ironbound.Rules.Combat;
using Ironbound.Rules.Defense;
using Ironbound.Rules.Effects;
using Ironbound.Rules.Modifiers;

namespace Ironbound.Rules.Items;

/// <summary>Where a thing is worn or held.</summary>
public enum EquipmentSlot
{
    /// <summary>Carried, but not in use. Nothing it grants applies.</summary>
    Carried,

    MainHand,
    OffHand,
    Armour,
    Shield,
    Head,
    Neck,
    Cloak,
    Ring,
}

public static class EquipmentSlots
{
    /// <summary>How many things fit. Two rings; one of everything else.</summary>
    public static int Capacity(EquipmentSlot slot) => slot switch
    {
        EquipmentSlot.Carried => int.MaxValue,
        EquipmentSlot.Ring => 2,
        _ => 1,
    };
}

/// <summary>How much armour something is, which decides what it costs the wearer.</summary>
public enum ArmourCategory
{
    /// <summary>Not armour at all: a sword, a ring, a cloak.</summary>
    None,

    Light,

    /// <summary>Slows the wearer, unless a fighter has trained in it.</summary>
    Medium,

    Heavy,

    /// <summary>Carried on the arm. Has a check penalty and no effect on speed.</summary>
    Shield,
}

/// <summary>
/// A thing that can be owned, worn or wielded.
/// </summary>
/// <remarks>
/// The last of the four content kinds the project set out to keep in files, and the one that
/// makes damage reduction mean something. The rules have known how to say "DR 10/silver" since
/// the defence layer; until now there was no way for anyone to <em>acquire</em> a silver sword,
/// which made the whole mechanism a wall with no door.
/// <para>
/// A weapon item names a <see cref="Weapon"/> rather than restating its dice, so a silvered
/// longsword and an ordinary one cannot disagree about what a longsword does.
/// </para>
/// </remarks>
public sealed record ItemDefinition
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public string Description { get; init; } = string.Empty;

    public EquipmentSlot Slot { get; init; } = EquipmentSlot.Carried;

    /// <summary>
    /// What it looks like in somebody's hand, or empty for things that are not held.
    /// </summary>
    /// <remarks>
    /// A path the rules hold and never open, as a creature's and a terrain's are. It lives on the
    /// item rather than on the weapon kind because the silvered longsword and the plain one are
    /// the same kind of weapon and should not look the same: which blade she is holding is the
    /// whole point of the werewolf fight.
    /// </remarks>
    public string Model { get; init; } = string.Empty;

    /// <summary>What wearing it is worth, in the ordinary stacking currency.</summary>
    public IReadOnlyList<ModifierGrant> Grants { get; init; } = [];

    /// <summary>The weapon definition this is an instance of, if it is a weapon at all.</summary>
    public string? Weapon { get; init; }

    /// <summary>A +1 sword's +1: attack, damage, and counting as magic against reduction.</summary>
    public int Enhancement { get; init; }

    /// <summary>What it is made of or aligned with, for getting past damage reduction.</summary>
    public DamageBypass Qualities { get; init; }

    public bool IsWeapon => Weapon is not null;

    /// <summary>Light, medium or heavy armour, a shield, or nothing of the sort.</summary>
    public ArmourCategory Armour { get; init; } = ArmourCategory.None;

    /// <summary>
    /// The most Dexterity bonus that still reaches armour class through it, or null when it
    /// does not care. A breastplate's three is why the nimble do not wear one.
    /// </summary>
    public int? MaxDexterity { get; init; }

    /// <summary>
    /// What it costs on Strength- and Dexterity-based skills while worn: zero or negative.
    /// Climbing in scale mail is hard, and this is the number that says how hard.
    /// </summary>
    public int CheckPenalty { get; init; }

    /// <summary>Body armour, as opposed to a shield or anything else.</summary>
    public bool IsBodyArmour => Armour is ArmourCategory.Light or ArmourCategory.Medium or ArmourCategory.Heavy;

    /// <summary>
    /// Everything this item lets an attack slip past: what it is made of, plus magic if it is
    /// enchanted at all.
    /// </summary>
    public DamageBypass AllQualities =>
        Enhancement > 0 ? Qualities | DamageBypass.Magic : Qualities;

    /// <summary>Hands over the static bonuses. Called when the item is put on.</summary>
    public void ApplyTo(Creatures.Creature creature)
    {
        ArgumentNullException.ThrowIfNull(creature);

        foreach (var grant in Grants)
        {
            grant.Target.On(creature).Add(grant.Value, grant.Type, Name);
        }
    }

    /// <summary>Takes them back. Everything it added was filed under the item's own name.</summary>
    public void RemoveFrom(Creatures.Creature creature)
    {
        ArgumentNullException.ThrowIfNull(creature);

        foreach (var grant in Grants)
        {
            grant.Target.On(creature).RemoveAllFrom(Name);
        }
    }

    /// <summary>Finishes a weapon built from <see cref="Weapon"/> with this item's own qualities.</summary>
    public WeaponAttack Dress(WeaponAttack weapon)
    {
        ArgumentNullException.ThrowIfNull(weapon);

        weapon.Qualities |= AllQualities;

        if (Enhancement != 0)
        {
            weapon.Attack.Modifiers.Add(Enhancement, BonusType.Enhancement, Name);
            weapon.DamageModifiers.Add(Enhancement, BonusType.Enhancement, Name);
        }

        return weapon;
    }

    public override string ToString() => Name;
}
