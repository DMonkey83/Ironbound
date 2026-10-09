using Ironbound.Rules.Abilities;
using Ironbound.Rules.Classes;
using Ironbound.Rules.Combat;
using Ironbound.Rules.Content;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Dice;
using Ironbound.Rules.Items;
using Ironbound.Rules.Maps;
using Ironbound.Rules.Persistence;
using Ironbound.Rules.Skills;

namespace Ironbound.Simulation;

/// <summary>Somebody spotting something hidden: a loose stone, a sack behind a rock.</summary>
public sealed record Notice(Container Container, Creature Who, string Line);

/// <summary>
/// The party's bag, the containers lying about, and the gear going between them.
/// </summary>
/// <remarks>
/// Taking and wearing are two steps. Taking moves things from a chest or a body into the bag the
/// whole party shares; putting something on is done for one character from that bag, and
/// whatever it replaces goes back into the bag in plain sight. Nothing is ever lost on the way
/// and nothing is ever made twice: every move takes from one place before it puts in another.
/// </remarks>
public sealed partial class Campaign
{
    /// <summary>
    /// The dice for the coins on the fallen. Their own stream, seeded per room or per chapter, so
    /// a replay finds the same coins and rolling them does not move a single door's dice.
    /// </summary>
    public const ulong TreasureStream = 4;

    private readonly PartyBag _bag = new();
    private readonly List<Container> _containers = [];
    private readonly List<Notice> _notices = [];

    /// <summary>What the party carries between them that nobody is wearing.</summary>
    public PartyBag Bag => _bag;

    /// <summary>
    /// Every container the party can see: chests and crates on the level, the bodies of the
    /// fallen, what was left lying about. Hidden ones nobody has noticed are not in it.
    /// </summary>
    public IReadOnlyList<Container> Containers => [.. _containers.Where(container => !container.IsHidden)];

    /// <summary>The container with this id, if the party can see it.</summary>
    public Container? GetContainer(string id) =>
        _containers.FirstOrDefault(container =>
            !container.IsHidden && string.Equals(container.Id, id, StringComparison.Ordinal));

    /// <summary>The container on a square, if the party can see one there.</summary>
    public Container? ContainerAt(GridSquare square) =>
        _containers.FirstOrDefault(container => !container.IsHidden && container.Squares.Contains(square));

    /// <summary>
    /// Everything somebody has noticed since the last call, oldest first, and forgets them: what
    /// the Game shows as it happens, as <see cref="TakeAwards"/> does for experience.
    /// </summary>
    public IReadOnlyList<Notice> TakeNotices()
    {
        var taken = _notices.ToList();
        _notices.Clear();
        return taken;
    }

    /// <summary>The content the campaign is played from, for weighing gear against itself.</summary>
    internal ContentLibrary Library => _library;

    /// <summary>Why this item could not be put on this creature in its own slot right now, or null.</summary>
    internal string? WhyNotEquip(Creature creature, ItemDefinition item) =>
        GearRefusal(creature) ?? SlotRefusal(creature, item, item.Slot);

    /// <summary>Whether gear can be handled: nobody is fighting, and the party has not lost.</summary>
    private bool AtLeisure => State is not (CampaignState.Fighting or CampaignState.Lost);

    private string? GearRefusal(Creature creature)
    {
        if (!AtLeisure)
        {
            return State == CampaignState.Fighting
                ? "Not in the middle of a fight: changing gear will have to wait."
                : "There is nobody left to carry anything.";
        }

        return Party.Contains(creature) ? null : $"{creature.Name} is not one of the party.";
    }

    // ---- equipping ----

    /// <summary>
    /// Moves one item from the bag into a slot — its own, or the one asked for: a sword into the
    /// off hand, a ring onto the second finger. Whatever was there goes back into the bag.
    /// </summary>
    /// <remarks>
    /// Refused in a fight, for anybody outside the party, for something that does not go in that
    /// slot, and for a two-handed weapon while the off hand or the shield arm is in use — or a
    /// shield while both hands are on one. That last is refused with the reason rather than the
    /// shield being quietly taken off: which of the two to give up is the player's call.
    /// </remarks>
    public GearResult Equip(Creature creature, BagEntry entry, EquipmentSlot? slot = null)
    {
        ArgumentNullException.ThrowIfNull(creature);
        ArgumentNullException.ThrowIfNull(entry);

        if (GearRefusal(creature) is { } why)
        {
            return GearResult.Refused(why);
        }

        if (_bag.Find(entry) is not { } held)
        {
            return GearResult.Refused($"There is no {entry.Item.Name} in the bag.");
        }

        var item = held.Item;
        var target = slot ?? item.Slot;

        if (SlotRefusal(creature, item, target) is { } refused)
        {
            return GearResult.Refused(refused);
        }

        // Out of the bag before anything else moves, so that a swap never holds both.
        _bag.Remove(held);

        var displaced = creature.Equipment.HasRoomFor(target)
            ? null
            : creature.Equipment.Worn.FirstOrDefault(worn => worn.Slot == target);

        var primary = displaced?.Weapon is { } old ? creature.Attacks.IndexOf(old) : -1;

        if (displaced is not null)
        {
            creature.Equipment.Unequip(displaced);
            _bag.Add(displaced.Item, 1, displaced.IsBroken);
        }

        _library.Equip(creature, item, target, held.IsBroken);

        // A weapon in the main hand is the one swung first: where the old one stood, or at the
        // front of the list for an empty hand. An off hand's goes to the back.
        if (target == EquipmentSlot.MainHand
            && creature.Equipment.Worn.LastOrDefault(worn => ReferenceEquals(worn.Item, item)) is { Weapon: { } weapon } placed)
        {
            MoveTo(creature, weapon, primary < 0 ? 0 : primary);
            if (placed.Thrown is { } thrown)
            {
                MoveTo(creature, thrown, creature.Attacks.IndexOf(weapon) + 1);
            }
        }

        var line = $"{creature.Name} takes up the {item.Name}";
        if (displaced is not null)
        {
            line += $" and puts the {displaced.Item.Name} in the bag";
        }

        if (!Proficiency.IsProficient(creature, item) || IsUntrainedWeapon(creature, item))
        {
            line += " — not proficient";
        }

        return GearResult.Done(line + ".");
    }

    /// <summary>Takes something off and puts it in the bag. A natural weapon cannot be taken off.</summary>
    public GearResult Unequip(Creature creature, EquippedItem worn)
    {
        ArgumentNullException.ThrowIfNull(creature);
        ArgumentNullException.ThrowIfNull(worn);

        if (GearRefusal(creature) is { } why)
        {
            return GearResult.Refused(why);
        }

        if (worn.Item.IsNatural)
        {
            return GearResult.Refused($"{creature.Name} cannot put down a {worn.Item.Name}.");
        }

        if (!creature.Equipment.Unequip(worn))
        {
            return GearResult.Refused($"{creature.Name} has no {worn.Item.Name} to take off.");
        }

        _bag.Add(worn.Item, 1, worn.IsBroken);
        return GearResult.Done($"{creature.Name} puts the {worn.Item.Name} in the bag.");
    }

    /// <summary>Takes off whatever is in a slot — the first ring, for the ring slot.</summary>
    public GearResult Unequip(Creature creature, EquipmentSlot slot)
    {
        ArgumentNullException.ThrowIfNull(creature);

        return creature.Equipment.Worn.FirstOrDefault(worn => worn.Slot == slot && !worn.Item.IsNatural) is { } found
            ? Unequip(creature, found)
            : GearResult.Refused($"{creature.Name} has nothing there.");
    }

    /// <summary>
    /// Hangs something from the bag on a character's belt: still theirs, counted on their own
    /// load, and — a weapon — still something they can swing, since drawing is not modelled.
    /// </summary>
    public GearResult Stow(Creature creature, BagEntry entry)
    {
        ArgumentNullException.ThrowIfNull(creature);
        ArgumentNullException.ThrowIfNull(entry);

        if (GearRefusal(creature) is { } why)
        {
            return GearResult.Refused(why);
        }

        if (_bag.Find(entry) is not { } held)
        {
            return GearResult.Refused($"There is no {entry.Item.Name} in the bag.");
        }

        _bag.Remove(held);
        _library.Equip(creature, held.Item, EquipmentSlot.Carried, held.IsBroken);

        return GearResult.Done($"{creature.Name} hangs the {held.Item.Name} on the belt.");
    }

    /// <summary>Moves something from a character's belt back into the bag.</summary>
    public GearResult Unstow(Creature creature, EquippedItem stowed)
    {
        ArgumentNullException.ThrowIfNull(creature);
        ArgumentNullException.ThrowIfNull(stowed);

        return stowed.Slot != EquipmentSlot.Carried
            ? GearResult.Refused($"The {stowed.Item.Name} is not on {creature.Name}'s belt.")
            : Unequip(creature, stowed);
    }

    /// <summary>
    /// Leaves so many of something on the ground where the leader stands, as a pile anybody can
    /// pick up again. Nothing dropped is ever destroyed.
    /// </summary>
    public GearResult Drop(BagEntry entry, int count = 1)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (!AtLeisure)
        {
            return GearResult.Refused("Not now.");
        }

        if (_bag.Find(entry) is null || _bag.Remove(entry, count) is false)
        {
            return GearResult.Refused($"The bag holds fewer than {count} {entry.Item.Name}.");
        }

        if (DroppedPile() is not { } pile)
        {
            _bag.Add(entry.Item, count, entry.IsBroken);
            return GearResult.Refused("There is nowhere to put it down.");
        }

        pile.Put(entry.Item, count, entry.IsBroken);
        return GearResult.Done($"The party leaves {(entry with { Count = count })} on the ground.");
    }

    /// <summary>Leaves coins on the ground where the leader stands. They weigh fifty to the pound.</summary>
    public GearResult DropCoins(Money coins)
    {
        if (!AtLeisure)
        {
            return GearResult.Refused("Not now.");
        }

        if (coins.IsEmpty || !_bag.RemoveCoins(coins))
        {
            return GearResult.Refused("The purse does not hold that many.");
        }

        if (DroppedPile() is not { } pile)
        {
            _bag.AddCoins(coins);
            return GearResult.Refused("There is nowhere to put them down.");
        }

        pile.Money += coins;
        return GearResult.Done($"The party leaves {coins} on the ground.");
    }

    /// <summary>The pile at the leader's feet, made if there is none yet.</summary>
    private Container? DroppedPile()
    {
        var leader = Party.FirstOrDefault(member => member.IsConscious && Battle.Battlefield?.SquareOf(member) is not null);
        if (leader is null || Battle.Battlefield?.SquareOf(leader) is not { } at)
        {
            return null;
        }

        var id = $"dropped:{at.X},{at.Y}";
        if (_containers.FirstOrDefault(container => container.Id == id) is { } existing)
        {
            return existing;
        }

        var pile = new Container(id, "what the party left", ContainerLook.Pile, [at]);
        _containers.Add(pile);
        return pile;
    }

    /// <summary>Why an item cannot go in a slot on this creature, or null when it can.</summary>
    private string? SlotRefusal(Creature creature, ItemDefinition item, EquipmentSlot target)
    {
        if (item.IsNatural || item.Kind == ItemKind.Valuable || target == EquipmentSlot.Carried)
        {
            return target == EquipmentSlot.Carried
                ? $"Hang the {item.Name} on the belt instead."
                : $"The {item.Name} is not something to wear or wield.";
        }

        var weapon = item.Weapon is { } kind ? _library.GetWeapon(kind) : null;
        var twoHanded = weapon is { Hands: WeaponHands.TwoHanded };

        if (item.IsWeapon)
        {
            if (target is not (EquipmentSlot.MainHand or EquipmentSlot.OffHand))
            {
                return $"The {item.Name} is held in a hand.";
            }

            if (target == EquipmentSlot.OffHand)
            {
                if (twoHanded || weapon is { Hands: WeaponHands.Ranged })
                {
                    return $"The {item.Name} needs both hands.";
                }

                if (Worn(creature, EquipmentSlot.Shield) is { } shield)
                {
                    return $"{creature.Name}'s off arm carries the {shield.Item.Name}.";
                }

                if (Worn(creature, EquipmentSlot.MainHand)?.Weapon is { Hands: WeaponHands.TwoHanded } both)
                {
                    return $"Both {creature.Name}'s hands are on the {both.Name}.";
                }
            }

            if (target == EquipmentSlot.MainHand && twoHanded)
            {
                if (Worn(creature, EquipmentSlot.Shield) is { } shield)
                {
                    return $"The {item.Name} needs both hands, and {creature.Name} has the {shield.Item.Name} on the off arm. Take it off first.";
                }

                if (Worn(creature, EquipmentSlot.OffHand) is { } other)
                {
                    return $"The {item.Name} needs both hands, and {creature.Name} has the {other.Item.Name} in the off hand. Put it away first.";
                }
            }

            return null;
        }

        if (target != item.Slot || item.Slot == EquipmentSlot.Carried)
        {
            return $"The {item.Name} does not go there.";
        }

        if (item.Armour == ArmourCategory.Shield)
        {
            if (Worn(creature, EquipmentSlot.OffHand) is { } other)
            {
                return $"{creature.Name} has the {other.Item.Name} in the off hand. Put it away first.";
            }

            if (Worn(creature, EquipmentSlot.MainHand)?.Weapon is { Hands: WeaponHands.TwoHanded } both)
            {
                return $"Both {creature.Name}'s hands are on the {both.Name}. Put it away first.";
            }
        }

        return null;
    }

    private static EquippedItem? Worn(Creature creature, EquipmentSlot slot) =>
        creature.Equipment.Worn.FirstOrDefault(worn => worn.Slot == slot);

    private bool IsUntrainedWeapon(Creature creature, ItemDefinition item) =>
        item.IsWeapon && _library.BuildItemWeapon(item, creature.Size) is { } built
        && !Proficiency.IsProficient(creature, built);

    private static void MoveTo(Creature creature, WeaponAttack attack, int index)
    {
        if (!creature.Attacks.Remove(attack))
        {
            return;
        }

        creature.Attacks.Insert(Math.Clamp(index, 0, creature.Attacks.Count), attack);
    }

    // ---- containers ----

    /// <summary>
    /// Opens a container: walks nothing and takes nothing, only lifts the lid — picking the lock
    /// or forcing it first, the way a door is. Searching a body or a pile always works.
    /// </summary>
    /// <remarks>
    /// Whoever tries has to be beside it, or on it for a sack on the floor; the Game walks the
    /// leader there first, as it does for a door. A container written into a level gives its
    /// experience the first time it is opened, as a cache did when it was searched.
    /// </remarks>
    public FeatureResult Open(string containerId, Creature who)
    {
        ArgumentNullException.ThrowIfNull(who);

        Collect();

        var container = GetContainer(containerId);
        if (ContainerRefusal(container, who) is { } why)
        {
            return new FeatureResult(false, [why], null);
        }

        return OpenContainer(container!, who);
    }

    private string? ContainerRefusal(Container? container, Creature who)
    {
        if (container is null || container.IsHidden)
        {
            return "There is nothing like that here.";
        }

        if (!AtLeisure)
        {
            return $"Not now: {container.Name} will have to wait.";
        }

        if (!Party.Contains(who) || !who.IsConscious)
        {
            return $"{who.Name} is in no state to open {container.Name}.";
        }

        if (Battle.Battlefield?.SquareOf(who) is not { } at || !container.IsWithinReach(at))
        {
            return $"{who.Name} is not close enough to {container.Name}.";
        }

        return null;
    }

    private FeatureResult OpenContainer(Container container, Creature who)
    {
        var lines = new List<string>();
        var noisy = false;

        if (container.IsLocked)
        {
            var (opened, headline, roll, forced) = TryLock(container.Name, container.LockDc, container.BreakDc, who);
            lines.Add(headline);
            lines.Add(roll);

            if (!opened)
            {
                return new FeatureResult(false, lines, null);
            }

            container.IsLocked = false;
            noisy = forced;
        }

        var first = !container.IsOpen;
        container.IsOpen = true;

        lines.Add(container.Look == ContainerLook.Body
            ? $"{who.Name} searches {container.Name}."
            : $"{who.Name} opens {container.Name}.");

        if (first && container.Feature is { } feature)
        {
            Award($"Searched {feature.Name}", feature.Experience);
            AddText(lines, feature);
        }

        lines.Add(container.IsEmpty ? "There is nothing in it." : $"In it: {Describe(container)}.");

        return new FeatureResult(true, lines, null, noisy);
    }

    private static string Describe(Container container) =>
        string.Join(", ", container.Contents.Select(entry => entry.ToString())
            .Append(container.Money.IsEmpty ? null : container.Money.ToString())
            .OfType<string>());

    /// <summary>
    /// A lock against somebody: Disable Device if they are trained in it, their Strength against
    /// the thing if not. Forcing something open makes a noise; nothing hears it yet.
    /// </summary>
    private (bool Opened, string Headline, string Roll, bool Forced) TryLock(string name, int lockDc, int breakDc, Creature who)
    {
        if (who.Skills.Ranks(Skill.DisableDevice) > 0)
        {
            var check = who.Skills.Check(Skill.DisableDevice, _explore, lockDc);
            var success = check.Succeeded == true;
            return (
                success,
                success ? $"{who.Name} picks the lock of {name}." : $"{who.Name} cannot pick the lock of {name}.",
                check.ToString(),
                false);
        }

        var natural = _explore.NextDie(20);
        var strength = who.AbilityCheck(Ability.Strength).Total;
        var total = natural + strength;
        var forced = total >= breakDc;

        return (
            forced,
            forced ? $"{who.Name} forces {name}." : $"{Capital(name)} holds against {who.Name}.",
            $"{who.Name} Strength: d20 [{natural}] {strength:+0;-0;+0} = {total} "
                + $"vs DC {breakDc} — {(forced ? "success" : "failure")}",
            forced);
    }

    /// <summary>Takes so many of one thing out of an open container and into the bag.</summary>
    public GearResult Take(string containerId, BagEntry entry, int count = 1)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (TakeRefusal(containerId) is { } why)
        {
            return GearResult.Refused(why);
        }

        var container = GetContainer(containerId)!;
        if (count < 1 || container.CountOf(entry) < count)
        {
            return GearResult.Refused(container.CountOf(entry) == 0
                ? $"There is no {entry.Item.Name} in {container.Name}."
                : $"There are not {count} of the {entry.Item.Name} in {container.Name}.");
        }

        container.Take(entry, count);
        _bag.Add(entry.Item, count, entry.IsBroken);

        return GearResult.Done($"Into the bag: {entry with { Count = count }}.");
    }

    /// <summary>Takes the coins out of an open container.</summary>
    public GearResult TakeCoins(string containerId)
    {
        if (TakeRefusal(containerId) is { } why)
        {
            return GearResult.Refused(why);
        }

        var container = GetContainer(containerId)!;
        if (container.Money.IsEmpty)
        {
            return GearResult.Refused($"There are no coins in {container.Name}.");
        }

        var coins = container.Money;
        container.Money = Money.None;
        _bag.AddCoins(coins);

        return GearResult.Done($"Into the purse: {coins}.");
    }

    /// <summary>Takes everything out of an open container, coins and all.</summary>
    public GearResult TakeAll(string containerId)
    {
        if (TakeRefusal(containerId) is { } why)
        {
            return GearResult.Refused(why);
        }

        var container = GetContainer(containerId)!;
        if (container.IsEmpty)
        {
            return GearResult.Refused($"{Capital(container.Name)} is empty.");
        }

        var taken = Describe(container);
        foreach (var entry in container.Contents.ToList())
        {
            container.Take(entry, entry.Count);
            _bag.Add(entry);
        }

        _bag.AddCoins(container.Money);
        container.Money = Money.None;

        return GearResult.Done($"From {container.Name}: {taken}.");
    }

    /// <summary>
    /// Empties every open container the party can see within so many squares of somewhere — the
    /// loot-everything-round-me after a fight. A shut chest is left shut: somebody has to open it.
    /// </summary>
    public GearResult TakeAllNearby(GridSquare square, int radius)
    {
        if (!AtLeisure)
        {
            return GearResult.Refused("Not now.");
        }

        var lines = new List<string>();
        foreach (var container in Containers.Where(container => container.IsOpen && !container.IsEmpty
            && container.Squares.Any(at => Distance.Steps(at, square) <= radius)))
        {
            lines.Add(TakeAll(container.Id).Line);
        }

        return lines.Count == 0
            ? GearResult.Refused("There is nothing left to take nearby.")
            : GearResult.Done(string.Join(" ", lines));
    }

    private string? TakeRefusal(string containerId)
    {
        if (!AtLeisure)
        {
            return "Not now.";
        }

        if (GetContainer(containerId) is not { } container)
        {
            return "There is nothing like that here.";
        }

        return container.IsOpen ? null : $"{Capital(container.Name)} has to be opened first.";
    }

    // ---- making them ----

    /// <summary>
    /// Lays out what a won fight left behind: a body for each of the fallen where they fell,
    /// holding whatever they had that was not teeth, and the coins in their pockets.
    /// </summary>
    /// <param name="scope">The room or chapter, to keep body ids apart.</param>
    /// <param name="stream">Which seeded stream rolls the purses: the room's or the chapter's.</param>
    private int Bodies(Battle battle, string scope, ulong stream, GridSquare fallback)
    {
        var dice = new PcgRandom(_seed + stream, TreasureStream);
        var laid = 0;

        foreach (var fallen in battle.Foes.Where(foe => !foe.IsConscious))
        {
            // Whatever was thrown or dropped is picked up first: out of hand is a fight's state,
            // and never reaches a bag or a body.
            fallen.Equipment.Recover();

            var square = battle.Battlefield?.SquareOf(fallen) ?? fallback;
            var body = new Container($"body:{scope}:{fallen.Name}", fallen.Name, ContainerLook.Body, [square], body: fallen);

            // Enumerated into a list first: unequipping walks the same collection.
            foreach (var worn in fallen.Equipment.Worn.Where(worn => !worn.Item.IsNatural).ToList())
            {
                fallen.Equipment.Unequip(worn);
                body.Put(worn.Item, 1, worn.IsBroken);
                laid++;
            }

            if (fallen.DefinitionId is { } id && _library.GetCreature(id) is { Purse.IsEmpty: false } definition)
            {
                body.Money = definition.Purse.Roll(dice);
            }

            _containers.Add(body);
        }

        return laid;
    }

    /// <summary>What a room or an encounter had lying about, as a pile on the floor.</summary>
    private int Pile(string id, string name, IEnumerable<LootDefinition> loot, GridSquare at)
    {
        var pile = new Container(id, name, ContainerLook.Pile, [at]);
        var laid = 0;

        foreach (var found in loot)
        {
            if (_library.GetItem(found.ItemId) is { } item && pile.Put(item, found.Count))
            {
                laid += found.Count;
            }
        }

        if (laid > 0)
        {
            _containers.Add(pile);
        }

        return laid;
    }

    /// <summary>A container for a level's feature, filled as the file says.</summary>
    private Container Build(FeatureDefinition feature)
    {
        var container = new Container(feature.Id, feature.Name, feature.Look, feature.Squares, feature)
        {
            IsLocked = feature.Locked,
            Money = feature.Coins,
        };

        foreach (var found in feature.Loot)
        {
            if (_library.GetItem(found.ItemId) is { } item)
            {
                container.Put(item, found.Count);
            }
        }

        return container;
    }

    /// <summary>
    /// The nearest square to <paramref name="from"/> that is open ground, breadth first, as a
    /// pile is put down on: somebody may stand on it.
    /// </summary>
    private static GridSquare OpenGround(Battlefield field, GridSquare from)
    {
        if (field.IsPassable(from))
        {
            return from;
        }

        var seen = new HashSet<GridSquare> { from };
        var queue = new Queue<GridSquare>([from]);

        while (queue.TryDequeue(out var square))
        {
            for (var dy = -1; dy <= 1; dy++)
            {
                for (var dx = -1; dx <= 1; dx++)
                {
                    var next = new GridSquare(square.X + dx, square.Y + dy);
                    if (!field.Contains(next) || !seen.Add(next))
                    {
                        continue;
                    }

                    if (field.IsPassable(next))
                    {
                        return next;
                    }

                    queue.Enqueue(next);
                }
            }
        }

        return from;
    }

    // ---- noticing ----

    /// <summary>
    /// Gives everybody who has come within ten feet of a hidden container their one look for it.
    /// A success shows it to the whole party; a failure leaves it hidden from that one for good,
    /// until searching again is a thing anybody can do.
    /// </summary>
    private void LookAbout()
    {
        if (!AtLeisure || Battle.Battlefield is not { } field)
        {
            return;
        }

        foreach (var container in _containers.Where(container => container.IsHidden))
        {
            foreach (var member in Party.Where(member => member.IsConscious))
            {
                if (container.HasRolled(member)
                    || field.SquareOf(member) is not { } at
                    || !container.Squares.Any(square => Distance.Between(square, at) <= FeatureDefinition.NoticeFeet))
                {
                    continue;
                }

                container.Roll(member);
                var check = member.Skills.Check(Skill.Perception, _explore, container.HiddenDc);

                if (check.Succeeded == true)
                {
                    container.IsNoticed = true;
                    _notices.Add(new Notice(container, member, $"{member.Name} notices {container.Name}. ({check})"));
                    break;
                }
            }
        }
    }

    // ---- load ----

    /// <summary>
    /// What the party carries between them against what they can: every member's own gear, the
    /// bag and its coins, against the sum of the limits of everybody still on their feet.
    /// </summary>
    /// <remarks>
    /// Whoever is down carries nothing: their limits leave the sum. Between fights their gear is
    /// carried for them and counts on the others; in a fight it is lying where they fell, on them,
    /// and counts on nobody.
    /// </remarks>
    internal PartyLoadReport MeasureLoad()
    {
        var fighting = Battle.Outcome == BattleOutcome.InProgress && Battle.Foes.Count > 0;
        var weight = _bag.Weight;
        var capacity = CarryingCapacity.None;
        var carrying = new List<Creature>();
        var down = new List<Creature>();

        foreach (var member in Party)
        {
            if (member.IsConscious)
            {
                weight += Encumbrance.Carried(member);
                capacity += Encumbrance.Capacity(member);
                carrying.Add(member);
            }
            else
            {
                down.Add(member);
                if (!fighting)
                {
                    weight += Encumbrance.Carried(member);
                }
            }
        }

        return new PartyLoadReport(weight, capacity, capacity.Classify(weight), carrying, down);
    }

    /// <summary>Hands every member the party's load to weigh against their own.</summary>
    private void ShareLoad()
    {
        foreach (var member in Party)
        {
            member.SharedLoad = () => MeasureLoad().Category;
        }
    }

    // ---- saving ----

    private static SavedBagEntry Capture(BagEntry entry) => new(entry.Item.Id, entry.Count, entry.IsBroken);

    private static SavedMoney Capture(Money money) => new(money.Platinum, money.Gold, money.Silver, money.Copper);

    private static Money Restore(SavedMoney? money) =>
        money is null ? Money.None : new Money(money.Platinum, money.Gold, money.Silver, money.Copper);

    private static SavedContainer Capture(Container container) => new(
        container.Id,
        container.Name,
        container.Look,
        [.. container.Squares.Select(square => new SavedSquare(square.X, square.Y))],
        [.. container.Contents.Select(Capture)],
        Capture(container.Money),
        container.IsOpen,
        container.IsLocked,
        container.IsNoticed,
        [.. container.Rolled.Order(StringComparer.Ordinal)],
        container.Body?.DefinitionId,
        container.Body?.Name);

    private ItemDefinition Item(string id) => _library.GetItem(id) ?? throw new InvalidDataException(
        $"The save has an item '{id}', which no content file defines.");

    /// <summary>
    /// Fills the bag from a save: its own entries, or — from a save before sixteen — whatever was
    /// in the old sack, each a line of its own until it stacks. A natural weapon an old sack
    /// held by mistake is left out.
    /// </summary>
    private void RestoreBag(SavedCampaign state)
    {
        if (state.Bag is { } entries)
        {
            foreach (var entry in entries)
            {
                _bag.Add(Item(entry.Id), entry.Count, entry.Broken);
            }
        }
        else
        {
            foreach (var id in state.Stash)
            {
                _bag.Add(Item(id));
            }
        }

        _bag.AddCoins(Restore(state.Purse));
    }

    /// <summary>
    /// Puts the containers back. A level's own come back from its file, then take on what the
    /// save says of them; a save from before sixteen has none, so a cache it had searched comes
    /// back open and empty, and anything else as the file fills it. Bodies and piles come back
    /// only from a save that had them.
    /// </summary>
    private void RestoreContainers(SavedContainer[]? saved, LevelDefinition? level = null)
    {
        var byId = (saved ?? []).ToDictionary(container => container.Id, StringComparer.Ordinal);

        foreach (var feature in level?.Features.Where(feature => feature.Kind == FeatureKind.Container) ?? [])
        {
            var container = Build(feature);

            if (byId.Remove(feature.Id, out var state))
            {
                Fill(container, state);
            }
            else if (saved is null && _used.Contains(feature.Id))
            {
                container.Empty();
                container.IsOpen = true;
                container.IsLocked = false;
            }

            _containers.Add(container);
        }

        foreach (var state in (saved ?? []).Where(container => byId.ContainsKey(container.Id)))
        {
            var body = state.BodyOf is { } definition ? Fallen(definition, state.BodyName) : null;
            var container = new Container(
                state.Id,
                state.Name,
                state.Look,
                [.. state.Squares.Select(square => new GridSquare(square.X, square.Y))],
                body: body);

            Fill(container, state);
            _containers.Add(container);
        }
    }

    private void Fill(Container container, SavedContainer state)
    {
        container.Empty();
        foreach (var entry in state.Contents)
        {
            container.Put(Item(entry.Id), entry.Count, entry.Broken);
        }

        container.Money = Restore(state.Coins);
        container.IsOpen = state.Open;
        container.IsLocked = state.Locked;
        container.IsNoticed = state.Noticed;

        foreach (var name in state.Rolled)
        {
            container.Roll(name);
        }
    }

    /// <summary>
    /// A body built again from its creature's file, for the Game to draw: on the other side,
    /// past saving, and with nothing on it but its teeth — what it carried is in the container.
    /// </summary>
    private Creature? Fallen(string definition, string? name)
    {
        if (_library.BuildCreature(definition, _rules, name) is not { } body)
        {
            return null;
        }

        foreach (var worn in body.Equipment.Worn.Where(worn => !worn.Item.IsNatural).ToList())
        {
            body.Equipment.Unequip(worn);
        }

        body.Allegiance = Battle.FoeAllegiance;
        body.HitPoints.Take(body.HitPoints.Maximum + body.Abilities[Ability.Constitution].Score.GetValueOrDefault(10) + 1);
        return body;
    }
}
