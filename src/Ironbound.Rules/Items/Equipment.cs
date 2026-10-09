using Ironbound.Rules.Combat;
using Ironbound.Rules.Creatures;

namespace Ironbound.Rules.Items;

/// <summary>
/// One item, where it actually ended up, and the attack it turned into if it was a weapon.
/// </summary>
/// <remarks>
/// <see cref="Slot"/> can differ from the item's own: a second longsword does not fit in a hand
/// already holding one, so it goes on the belt instead of being refused.
/// </remarks>
public sealed record EquippedItem(ItemDefinition Item, WeaponAttack? Weapon, EquipmentSlot Slot)
{
    /// <summary>Whether it is actually in use, as opposed to merely being carried about.</summary>
    public bool IsWorn => Slot != EquipmentSlot.Carried;

    public override string ToString() => IsWorn ? Item.Name : $"{Item.Name} (stowed)";
}

/// <summary>
/// What a creature is wearing and holding.
/// </summary>
/// <remarks>
/// Equipping applies an item's bonuses under the item's own name and unequipping takes them back
/// with <see cref="Modifiers.ModifierStack.RemoveAllFrom"/> — the second production caller of
/// that method, after <c>Effect</c>. Adding it deliberately: an item is the same shape of thing
/// as a running effect, something that grants modifiers for as long as it lasts, and the
/// alternative is a parallel removal mechanism that can disagree with the first one.
/// <para>
/// A list rather than a dictionary keyed by slot, because the order things were put on is the
/// order they should be listed, saved and enumerated in — and two rings would need special
/// pleading either way.
/// </para>
/// </remarks>
public sealed class Equipment(Creature owner)
{
    private readonly Creature _owner = owner ?? throw new ArgumentNullException(nameof(owner));
    private readonly List<EquippedItem> _worn = [];

    public IReadOnlyList<EquippedItem> Worn => _worn;

    public IEnumerable<ItemDefinition> Items => _worn.Select(entry => entry.Item);

    public bool Has(string id) =>
        _worn.Any(entry => string.Equals(entry.Item.Id, id, StringComparison.Ordinal));

    public IEnumerable<ItemDefinition> InSlot(EquipmentSlot slot) =>
        _worn.Where(entry => entry.Slot == slot).Select(entry => entry.Item);

    public bool HasRoomFor(EquipmentSlot slot) =>
        InSlot(slot).Count() < EquipmentSlots.Capacity(slot);

    /// <summary>
    /// Takes something up. Returns true if it went where it was meant to, false if the slot was
    /// full and it ended up stowed instead.
    /// </summary>
    /// <param name="weapon">Already built from the item's weapon definition, for a weapon.</param>
    /// <remarks>
    /// A stowed weapon still yields an attack. Drawing one is a move action in the rules, and
    /// modelling that needs a notion of what is in your hands right now that nothing yet asks
    /// for — so for the moment a blade on the belt is a blade you can swing.
    /// </remarks>
    public bool Equip(ItemDefinition item, WeaponAttack? weapon = null)
    {
        ArgumentNullException.ThrowIfNull(item);

        var slot = HasRoomFor(item.Slot) ? item.Slot : EquipmentSlot.Carried;

        _worn.Add(new EquippedItem(item, weapon, slot));

        if (slot != EquipmentSlot.Carried)
        {
            item.ApplyTo(_owner);
            Cap(item);
        }

        if (weapon is not null)
        {
            _owner.Attacks.Add(weapon);
        }

        return slot == item.Slot;
    }

    /// <summary>Takes something off again, with everything it was giving.</summary>
    public bool Unequip(string id)
    {
        var index = _worn.FindIndex(entry => string.Equals(entry.Item.Id, id, StringComparison.Ordinal));
        if (index < 0)
        {
            return false;
        }

        var entry = _worn[index];
        _worn.RemoveAt(index);

        if (entry.IsWorn)
        {
            entry.Item.RemoveFrom(_owner);
            _owner.ArmorClass.RemoveDexterityCap(entry.Item.Name);
        }

        if (entry.Weapon is not null)
        {
            _owner.Attacks.Remove(entry.Weapon);
        }

        return true;
    }

    /// <summary>
    /// Reattaches an item to a creature already carrying its numbers, as a save does.
    /// </summary>
    /// <remarks>
    /// No bonuses are applied and no attack is added: both came back with the modifier stacks
    /// and the weapon list. Only the identity is missing, and without it taking the thing off
    /// later would remove nothing.
    /// <para>
    /// The Dexterity cap is the exception, because it is not a modifier and was never saved:
    /// it is put back here from the item, which is the only place it was ever written down.
    /// </para>
    /// </remarks>
    internal void Reattach(ItemDefinition item, WeaponAttack? weapon, EquipmentSlot slot)
    {
        _worn.Add(new EquippedItem(item, weapon, slot));

        if (slot != EquipmentSlot.Carried)
        {
            Cap(item);
        }
    }

    /// <summary>The heaviest body armour being worn, or none.</summary>
    public ArmourCategory ArmourWorn => _worn
        .Where(entry => entry.IsWorn && entry.Item.IsBodyArmour)
        .Select(entry => entry.Item.Armour)
        .DefaultIfEmpty(ArmourCategory.None)
        .Max();

    /// <summary>Whether a shield is on the arm.</summary>
    public bool HasShield => _worn.Any(entry => entry.IsWorn && entry.Item.Armour == ArmourCategory.Shield);

    /// <summary>
    /// The check penalty of everything worn, split into the body armour's and the shield's —
    /// because a fighter's armour training eases the first and not the second.
    /// </summary>
    public (int Armour, int Shield) CheckPenalties => (
        _worn.Where(entry => entry.IsWorn && entry.Item.IsBodyArmour).Sum(entry => entry.Item.CheckPenalty),
        _worn.Where(entry => entry.IsWorn && entry.Item.Armour == ArmourCategory.Shield)
            .Sum(entry => entry.Item.CheckPenalty));

    private void Cap(ItemDefinition item)
    {
        if (item.MaxDexterity is { } most)
        {
            _owner.ArmorClass.CapDexterity(item.Name, most);
        }
    }

    public override string ToString() =>
        _worn.Count == 0 ? "nothing" : string.Join(", ", _worn);
}
