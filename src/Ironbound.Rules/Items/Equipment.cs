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
/// <param name="Thrown">The same weapon thrown, for one that can be: "dagger (thrown)".</param>
public sealed record EquippedItem(
    ItemDefinition Item, WeaponAttack? Weapon, EquipmentSlot Slot, WeaponAttack? Thrown = null)
{
    /// <summary>Whether it is actually in use, as opposed to merely being carried about.</summary>
    public bool IsWorn => Slot != EquipmentSlot.Carried;

    /// <summary>
    /// Thrown, or dropped after a trip that went wrong: lying somewhere on the field until the
    /// fight is over. Neither of its attacks can be made meanwhile.
    /// </summary>
    public bool IsOutOfHand { get; internal set; }

    /// <summary>Cracked: −2 to hit and to damage, and a critical only on a natural 20.</summary>
    public bool IsBroken { get; internal set; }

    /// <summary>Whether this is the item an attack came from.</summary>
    public bool Makes(WeaponAttack attack) =>
        ReferenceEquals(Weapon, attack) || ReferenceEquals(Thrown, attack);

    public override string ToString()
    {
        var name = IsBroken ? $"{Item.Name} (broken)" : Item.Name;

        return IsOutOfHand ? $"{name} (out of hand)"
            : IsWorn ? name
            : $"{name} (stowed)";
    }
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

    // Attacks whose item was destroyed mid-swing. Only ever asked about by whoever still holds
    // the attack — a full attack halfway through its swings — so it never needs saving.
    private readonly HashSet<WeaponAttack> _destroyed = new(ReferenceEqualityComparer.Instance);

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
    /// <param name="thrown">The same weapon thrown, for one that can be.</param>
    /// <remarks>
    /// A stowed weapon still yields an attack. Drawing one is a move action in the rules, and
    /// modelling that needs a notion of what is in your hands right now that nothing yet asks
    /// for — so for the moment a blade on the belt is a blade you can swing.
    /// </remarks>
    public bool Equip(ItemDefinition item, WeaponAttack? weapon = null, WeaponAttack? thrown = null) =>
        Equip(item, null, weapon, thrown);

    /// <summary>
    /// Takes something up into a chosen slot — a sword into the off hand, a ring onto the second
    /// finger — or onto the belt with <see cref="EquipmentSlot.Carried"/>. A slot already full
    /// puts it on the belt instead, as the plain overload does; it is the caller's business to
    /// make room first. Returns whether it went where it was asked to.
    /// </summary>
    /// <param name="slot">Where it should go, or null for the item's own slot.</param>
    /// <param name="broken">Whether it comes broken, as something taken back out of a bag can.</param>
    public bool Equip(
        ItemDefinition item,
        EquipmentSlot? slot,
        WeaponAttack? weapon = null,
        WeaponAttack? thrown = null,
        bool broken = false)
    {
        ArgumentNullException.ThrowIfNull(item);

        var wanted = slot ?? item.Slot;
        var landed = HasRoomFor(wanted) ? wanted : EquipmentSlot.Carried;

        _worn.Add(new EquippedItem(item, weapon, landed, thrown) { IsBroken = broken });
        var slotUsed = landed;

        if (slotUsed != EquipmentSlot.Carried)
        {
            item.ApplyTo(_owner);
            Cap(item);
        }

        if (weapon is not null)
        {
            _owner.Attacks.Add(weapon);
        }

        if (thrown is not null)
        {
            _owner.Attacks.Add(thrown);
        }

        return slotUsed == wanted;
    }

    /// <summary>Takes something off again, with everything it was giving.</summary>
    public bool Unequip(string id)
    {
        var index = _worn.FindIndex(entry => string.Equals(entry.Item.Id, id, StringComparison.Ordinal));
        if (index < 0)
        {
            return false;
        }

        Remove(_worn[index]);
        return true;
    }

    /// <summary>
    /// Takes off one particular entry — the broken dagger rather than the whole one beside it.
    /// False if the creature is not wearing it.
    /// </summary>
    public bool Unequip(EquippedItem entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (!_worn.Contains(entry))
        {
            return false;
        }

        Remove(entry);
        return true;
    }

    /// <summary>What everything on it weighs, sized for it: see <see cref="Encumbrance.WornWeight"/>.</summary>
    public decimal Weight => Encumbrance.Carried(_owner);

    private void Remove(EquippedItem entry)
    {
        _worn.Remove(entry);

        if (entry.IsWorn)
        {
            entry.Item.RemoveFrom(_owner);
            _owner.ArmorClass.RemoveDexterityCap(entry.Item.Name);
        }

        if (entry.Weapon is not null)
        {
            _owner.Attacks.Remove(entry.Weapon);
        }

        if (entry.Thrown is not null)
        {
            _owner.Attacks.Remove(entry.Thrown);
        }
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
    internal void Reattach(
        ItemDefinition item,
        WeaponAttack? weapon,
        EquipmentSlot slot,
        WeaponAttack? thrown = null,
        bool broken = false,
        bool outOfHand = false)
    {
        _worn.Add(new EquippedItem(item, weapon, slot, thrown) { IsBroken = broken, IsOutOfHand = outOfHand });

        if (slot != EquipmentSlot.Carried)
        {
            Cap(item);
        }
    }

    /// <summary>
    /// The item an attack came from, or null for one that came from no item: a bite written into
    /// a creature's file, or anything built by hand.
    /// </summary>
    public ItemDefinition? ItemFor(WeaponAttack attack) => EntryFor(attack)?.Item;

    /// <summary>The equipped entry an attack came from, with its broken and out-of-hand state.</summary>
    public EquippedItem? EntryFor(WeaponAttack attack)
    {
        ArgumentNullException.ThrowIfNull(attack);
        return _worn.FirstOrDefault(entry => entry.Makes(attack));
    }

    /// <summary>
    /// Whether an item is out of the creature's hands: thrown, or let go of. With two of the same
    /// item it is true while either is, which is the only answer an item alone can give; ask
    /// <see cref="EntryFor"/> with the attack to tell them apart.
    /// </summary>
    public bool IsOutOfHand(ItemDefinition item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return _worn.Any(entry => ReferenceEquals(entry.Item, item) && entry.IsOutOfHand);
    }

    /// <summary>Whether an item has the broken condition. The same caveat as <see cref="IsOutOfHand(ItemDefinition)"/>.</summary>
    public bool IsBroken(ItemDefinition item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return _worn.Any(entry => ReferenceEquals(entry.Item, item) && entry.IsBroken);
    }

    /// <summary>Whether the item this attack came from is out of hand. False for an attack from no item.</summary>
    public bool IsOutOfHand(WeaponAttack attack) => EntryFor(attack)?.IsOutOfHand == true;

    /// <summary>Whether the item this attack came from is broken. False for an attack from no item.</summary>
    public bool IsBroken(WeaponAttack attack) => EntryFor(attack)?.IsBroken == true;

    /// <summary>Whether this attack can be made at all right now: its item has not left the hand, or the world.</summary>
    public bool CanUse(WeaponAttack attack) => !IsOutOfHand(attack) && !_destroyed.Contains(attack);

    /// <summary>
    /// Lets go of the item an attack came from — thrown at somebody, or dropped to keep one's
    /// feet. Both its uses are gone until <see cref="Recover"/>. False when it came from no item,
    /// which therefore never leaves the hand: a bite cannot be thrown away.
    /// </summary>
    public bool LetGo(WeaponAttack attack)
    {
        if (EntryFor(attack) is not { IsOutOfHand: false } entry)
        {
            return false;
        }

        entry.IsOutOfHand = true;
        return true;
    }

    /// <summary>
    /// Everything thrown or dropped comes back, as it does once a fight is over and there is time
    /// to walk about picking things up. Returns how many came back.
    /// </summary>
    public int Recover()
    {
        var recovered = 0;
        foreach (var entry in _worn.Where(entry => entry.IsOutOfHand))
        {
            entry.IsOutOfHand = false;
            recovered++;
        }

        return recovered;
    }

    /// <summary>Gives the item an attack came from the broken condition. False if it had none, or already had it.</summary>
    public bool Break(WeaponAttack attack)
    {
        if (EntryFor(attack) is not { IsBroken: false } entry)
        {
            return false;
        }

        entry.IsBroken = true;
        return true;
    }

    /// <summary>
    /// Destroys the item an attack came from: gone from the equipment, its attacks gone with it,
    /// and nothing left to loot. False if it came from no item.
    /// </summary>
    public bool Destroy(WeaponAttack attack)
    {
        if (EntryFor(attack) is not { } entry)
        {
            return false;
        }

        Remove(entry);

        foreach (var gone in new[] { entry.Weapon, entry.Thrown }.OfType<WeaponAttack>())
        {
            _destroyed.Add(gone);
        }

        return true;
    }

    /// <summary>
    /// Whether a shield is on the arm and doing nothing, because the creature's melee weapon wants
    /// both hands — Karn's light shield behind her greataxe.
    /// </summary>
    /// <remarks>
    /// Asked of whatever the creature would swing right now, so letting go of the greataxe puts
    /// the shield back to work. A buckler would be the exception, strapped to the forearm; there
    /// is none in the game yet.
    /// </remarks>
    public bool ShieldSetAside =>
        HasShield && _owner.MeleeAttack is { Hands: WeaponHands.TwoHanded };

    /// <summary>A shield on the arm that is actually being used.</summary>
    public bool ShieldInUse => HasShield && !ShieldSetAside;

    /// <summary>
    /// Whether a modifier is a worn shield's own bonus while that shield is set aside: the one
    /// thing armour class leaves out for it.
    /// </summary>
    internal bool IsSetAside(Modifiers.Modifier modifier) =>
        modifier.Type == Modifiers.BonusType.Shield
        && ShieldSetAside
        && _worn.Any(entry => entry.IsWorn
            && entry.Item.Armour == ArmourCategory.Shield
            && string.Equals(entry.Item.Name, modifier.Source, StringComparison.Ordinal));

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
