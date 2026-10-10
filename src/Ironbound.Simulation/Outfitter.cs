using Ironbound.Rules.Abilities;
using Ironbound.Rules.Classes;
using Ironbound.Rules.Combat;
using Ironbound.Rules.Content;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Defense;
using Ironbound.Rules.Effects;
using Ironbound.Rules.Items;
using Ironbound.Rules.Modifiers;

namespace Ironbound.Simulation;

/// <summary>Whether something would be better than what is worn in its place.</summary>
public enum GearVerdict
{
    /// <summary>Better on this creature's own numbers: a green arrow.</summary>
    Upgrade,

    /// <summary>Worse: a red arrow.</summary>
    Downgrade,

    /// <summary>Better at one thing and worse at another, or simply different: no arrow.</summary>
    Sidegrade,
}

/// <summary>
/// One item set against what a creature has in its slot now.
/// </summary>
/// <param name="Current">What it would replace, or null for an empty slot.</param>
/// <param name="Reason">A few words for the tooltip: "+2 to hit, not proficient: −4".</param>
/// <param name="Refusal">Why it could not be put on as things stand — a shield in the way of a
/// greatsword — or null when it could.</param>
public sealed record GearComparison(
    ItemDefinition Item,
    EquipmentSlot Slot,
    ItemDefinition? Current,
    GearVerdict Verdict,
    string Reason,
    string? Refusal = null)
{
    public bool CanEquip => Refusal is null;

    public override string ToString() => $"{Item.Name}: {Verdict} — {Reason}";
}

/// <summary>
/// Weighs gear for somebody, as Wrath's green and red arrows do, and puts on whatever is
/// plainly better: what the autopilot does after every loot.
/// </summary>
/// <remarks>
/// Judged on the creature's own numbers, not the item's: a +1 greatsword is two better for the
/// fighter and four worse for the rogue who never learned to swing one. Weapons are compared on
/// attack bonus and average damage, proficiency counted; armour and shields on the armour class
/// they would actually give, Dexterity cap and all; anything else on what it grants.
/// </remarks>
public static class Outfitter
{
    public static GearComparison Compare(Campaign campaign, Creature creature, ItemDefinition item)
    {
        ArgumentNullException.ThrowIfNull(campaign);
        return Compare(creature, item, campaign.Library, campaign.WhyNotEquip(creature, item));
    }

    /// <summary>
    /// The comparison on numbers alone, with nothing asked of a campaign: what the item would do
    /// for this creature against what it has in that slot now.
    /// </summary>
    public static GearComparison Compare(Creature creature, ItemDefinition item, ContentLibrary library, string? refusal = null)
    {
        ArgumentNullException.ThrowIfNull(creature);
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(library);

        var slot = item.Slot;
        var current = creature.Equipment.Worn
            .FirstOrDefault(worn => worn.Slot == slot && !worn.Item.IsNatural);

        if (item.IsNatural || item.Kind == ItemKind.Valuable || slot == EquipmentSlot.Carried)
        {
            return new GearComparison(item, slot, null, GearVerdict.Sidegrade, "not something to wear or wield", refusal);
        }

        return item.Kind switch
        {
            ItemKind.Weapon => CompareWeapon(creature, item, current, library, refusal),
            ItemKind.Armour or ItemKind.Shield => CompareArmour(creature, item, current, refusal),
            _ => CompareGrants(item, current, refusal),
        };
    }

    /// <summary>
    /// Puts on everything in the bag that is plainly better for this creature, one swap at a
    /// time, until nothing is. Conservative on purpose: an upgrade has to be better on every
    /// count and usable as things stand — so a creature never trades armour it cannot wear for
    /// other armour it cannot wear, and a two-handed weapon and a shield are never swapped for
    /// each other back and forth. Returns what was done.
    /// </summary>
    public static IReadOnlyList<string> EquipBest(Campaign campaign, Creature creature)
    {
        ArgumentNullException.ThrowIfNull(campaign);
        ArgumentNullException.ThrowIfNull(creature);

        var done = new List<string>();

        // Bounded, though each swap is a strict improvement and so cannot cycle.
        for (var round = 0; round < 16; round++)
        {
            var best = campaign.Bag.Entries
                .Where(entry => !entry.IsBroken)
                .Select(entry => (Entry: entry, Comparison: Compare(campaign, creature, entry.Item)))
                .FirstOrDefault(one => one.Comparison is { Verdict: GearVerdict.Upgrade, CanEquip: true });

            if (best.Entry is null)
            {
                break;
            }

            var result = campaign.Equip(creature, best.Entry);
            if (!result.Success)
            {
                break;
            }

            done.Add(result.Line);
        }

        return done;
    }

    // ---- the belt ----

    /// <summary>
    /// The armour class a creature's own weapon is weighed against when deciding who carries the
    /// flasks: about what the foes of the first few levels wear.
    /// </summary>
    public const int ReferenceArmorClass = 15;

    /// <summary>The touch armour class a thrown flask is weighed against, for the same purpose.</summary>
    public const int ReferenceTouchArmorClass = 11;

    /// <summary>
    /// What a flask is reckoned to do on a hit, for the same purpose: alchemist's fire's 1d6, and
    /// 1d6 more as it burns.
    /// </summary>
    public const double FlaskYardstick = 7;

    /// <summary>
    /// Hangs what this creature should carry into the next fight on its belt, from the bag: one
    /// healing potion, and a share of the flasks, bags and stones if its own weapon is weak.
    /// Returns what was done.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The plan is the party's, and each creature takes its own part of it, so it comes out the
    /// same whichever order the party is asked in. Healing potions go one to a member, to those
    /// who cannot heal anybody first — a cleric has her own cures — in party order. Anything to
    /// throw is shared round among the members whose own weapon would do less than a flask:
    /// those with most to gain first, each kind starting where the last left off.
    /// </para>
    /// <para>
    /// Nothing is hung that would put its carrier in a heavier load, and nothing already on a
    /// belt is taken off. Potions of other spells — mage armor, an oil of magic weapon — are left
    /// in the bag for the player.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<string> HangConsumables(Campaign campaign, Creature creature)
    {
        ArgumentNullException.ThrowIfNull(campaign);
        ArgumentNullException.ThrowIfNull(creature);

        var done = new List<string>();
        if (!campaign.Party.Contains(creature) || !creature.IsAlive)
        {
            return done;
        }

        if (!Consumables.OnBelt(creature).Any(stack => IsHealingPotion(stack.Item)))
        {
            // Everybody still without one, ahead of this creature in the queue.
            var ahead = campaign.Party
                .Where(one => one.IsAlive && !Consumables.OnBelt(one).Any(stack => IsHealingPotion(stack.Item)))
                .OrderBy(one => HeuristicActionSource.IsHealer(one) ? 1 : 0)
                .TakeWhile(one => !ReferenceEquals(one, creature))
                .Count();

            var potions = Sound(campaign).Where(entry => IsHealingPotion(entry.Item)).ToList();
            if (potions.Sum(entry => entry.Count) > ahead && campaign.Stow(creature, potions[0], 1) is { Success: true } hung)
            {
                done.Add(hung.Line);
            }
        }

        var throwers = campaign.Party
            .Where(one => one.IsAlive && IsWeakInMelee(one))
            .OrderByDescending(one => ThrowExpectation(one) - MeleeExpectation(one))
            .ToList();

        var rank = throwers.IndexOf(creature);
        if (rank < 0)
        {
            return done;
        }

        // Every kind there is to share, in a fixed order, whether it is in the bag or hung already.
        var kinds = Sound(campaign).Select(entry => entry.Item)
            .Concat(throwers.SelectMany(one => Consumables.OnBelt(one).Select(stack => stack.Item)))
            .Where(item => item.Consumable is { IsThrown: true })
            .DistinctBy(item => item.Id)
            .OrderBy(item => item.Id, StringComparer.Ordinal)
            .ToList();

        var dealt = 0;
        foreach (var item in kinds)
        {
            var entry = Sound(campaign).FirstOrDefault(found => found.Id == item.Id);
            var inBag = entry?.Count ?? 0;
            var pool = inBag + throwers.Sum(one => Consumables.Count(one, item));

            // Dealt round like cards, starting with whoever the last kind would have come to next.
            var place = ((rank - (dealt % throwers.Count)) + throwers.Count) % throwers.Count;
            var share = (pool / throwers.Count) + (place < pool % throwers.Count ? 1 : 0);
            dealt += pool;

            var wanted = Math.Min(inBag, share - Consumables.Count(creature, item));
            while (wanted > 0 && !Fits(creature, item, wanted))
            {
                wanted--;
            }

            if (wanted > 0 && entry is not null && campaign.Stow(creature, entry, wanted) is { Success: true } hung)
            {
                done.Add(hung.Line);
            }
        }

        return done;
    }

    /// <summary>
    /// Between fights, with no rest left to fall back on: healing potions into everybody below
    /// half their hit points, the worst hurt first, until they are back above half or the potions
    /// run out — their own belt's, then the bag's, then a friend's. The belts are filled again from
    /// the bag afterwards. Returns what was done.
    /// </summary>
    /// <remarks>
    /// While a rest remains it does nothing: a rest heals everything and costs no potions, and
    /// whether to spend it is a separate decision.
    /// </remarks>
    public static IReadOnlyList<string> PatchUp(Campaign campaign)
    {
        ArgumentNullException.ThrowIfNull(campaign);

        var done = new List<string>();
        if (campaign.RestsRemaining > 0 || campaign.State is CampaignState.Fighting or CampaignState.Lost)
        {
            return done;
        }

        foreach (var one in campaign.Party
            .Where(member => member.IsAlive)
            .OrderBy(member => (double)member.HitPoints.Current / member.HitPoints.Maximum)
            .ToList())
        {
            while (one.HitPoints.Current * 2 < one.HitPoints.Maximum && HealingPotionFor(campaign, one) is { } potion)
            {
                var drunk = campaign.Drink(one, potion);
                if (!drunk.Success)
                {
                    break;
                }

                done.Add(drunk.Line);
            }
        }

        foreach (var one in campaign.Party)
        {
            done.AddRange(HangConsumables(campaign, one));
        }

        return done;
    }

    /// <summary>
    /// A healing potion this creature can drink now: off its own belt, from the bag, or — moved
    /// into the bag first — off the belt of whichever friend is least hurt.
    /// </summary>
    private static ItemDefinition? HealingPotionFor(Campaign campaign, Creature creature)
    {
        if (Consumables.OnBelt(creature).FirstOrDefault(stack => IsHealingPotion(stack.Item)) is { } own)
        {
            return own.Item;
        }

        if (Sound(campaign).FirstOrDefault(entry => IsHealingPotion(entry.Item)) is { } bagged)
        {
            return bagged.Item;
        }

        foreach (var friend in campaign.Party
            .Where(other => !ReferenceEquals(other, creature))
            .OrderByDescending(other => (double)other.HitPoints.Current / other.HitPoints.Maximum))
        {
            var worn = friend.Equipment.Worn.FirstOrDefault(entry =>
                entry.Slot == EquipmentSlot.Carried && !entry.IsOutOfHand && IsHealingPotion(entry.Item));

            if (worn is not null && campaign.Unstow(friend, worn).Success)
            {
                return worn.Item;
            }
        }

        return null;
    }

    private static bool IsHealingPotion(ItemDefinition item) =>
        item.Consumable is { Kind: ConsumableKind.Potion, Heals: true };

    /// <summary>What is in the bag and not broken.</summary>
    private static IEnumerable<BagEntry> Sound(Campaign campaign) =>
        campaign.Bag.Entries.Where(entry => !entry.IsBroken);

    /// <summary>Whether so many more of something leave its carrier's own load no heavier a category than now.</summary>
    private static bool Fits(Creature creature, ItemDefinition item, int count)
    {
        var load = Encumbrance.Load(creature);
        return load.Capacity.Classify(load.Weight + (item.Weight * count)) <= load.Category;
    }

    /// <summary>Whether a flask in this creature's hand would do more than its own weapon.</summary>
    public static bool IsWeakInMelee(Creature creature)
    {
        ArgumentNullException.ThrowIfNull(creature);
        return MeleeExpectation(creature) < ThrowExpectation(creature);
    }

    /// <summary>
    /// A round of the creature's melee weapon against <see cref="ReferenceArmorClass"/>: every swing
    /// of a full attack, at its chance to hit times its average damage. Nought with none.
    /// </summary>
    private static double MeleeExpectation(Creature creature)
    {
        if (creature.MeleeAttack is not { } weapon || !creature.CanAttackWith(weapon))
        {
            return 0;
        }

        var damage = weapon.Damage.Average + Strike.DamageBonus(creature, weapon).Total;
        var worth = 0.0;

        for (var swing = 0; swing < creature.AttacksPerFullAttack; swing++)
        {
            var bonus = Strike.AttackBonus(creature, weapon, iterativePenalty: -Iteratives.Step * swing).Total;
            worth += Chance(bonus, ReferenceArmorClass) * damage;
        }

        return worth;
    }

    /// <summary>
    /// A flask from this creature against <see cref="ReferenceTouchArmorClass"/>: base attack,
    /// Dexterity and size — what any throw starts from — and <see cref="FlaskYardstick"/> on a hit.
    /// </summary>
    private static double ThrowExpectation(Creature creature)
    {
        var bonus = creature.BaseAttackBonus
            + creature.Abilities[Ability.Dexterity].Modifier
            + CreatureSizes.Modifier(creature.Size);

        return Chance(bonus, ReferenceTouchArmorClass) * FlaskYardstick;
    }

    private static double Chance(int bonus, int armour) => Math.Clamp(21 + bonus - armour, 1, 19) / 20.0;

    private static GearComparison CompareWeapon(
        Creature creature, ItemDefinition item, EquippedItem? current, ContentLibrary library, string? refusal)
    {
        if (library.BuildItemWeapon(item, creature.Size) is not { } weapon)
        {
            return new GearComparison(item, item.Slot, current?.Item, GearVerdict.Sidegrade, "not a weapon anybody can fight with", refusal);
        }

        var proficient = Proficiency.IsProficient(creature, weapon);
        var attack = Strike.AttackBonus(creature, weapon).Total;
        var damage = weapon.Damage.Average + Strike.DamageBonus(creature, weapon).Total;
        var untrained = proficient ? string.Empty : $", not proficient: {Proficiency.NonProficientPenalty}";

        if (current?.Weapon is not { } held)
        {
            return new GearComparison(item, item.Slot, current?.Item, GearVerdict.Upgrade,
                $"{attack:+0;-0;+0} to hit, {damage:0.#} damage{untrained}", refusal);
        }

        // A bow against a blade is a different job, not a better or worse one.
        if (weapon.IsRanged != held.IsRanged)
        {
            return new GearComparison(item, item.Slot, current.Item, GearVerdict.Sidegrade,
                weapon.IsRanged ? $"shoots rather than strikes{untrained}" : $"strikes rather than shoots{untrained}",
                refusal);
        }

        var hit = attack - Strike.AttackBonus(creature, held).Total;
        var harm = damage - (held.Damage.Average + Strike.DamageBonus(creature, held).Total);
        if (current.IsBroken)
        {
            // Already counted in the broken one's numbers; said so the reason reads true.
            untrained += ", the old one is broken";
        }

        var gained = item.AllQualities & ~current.Item.AllQualities;
        var lost = current.Item.AllQualities & ~item.AllQualities;

        var parts = new List<string>();
        if (hit != 0)
        {
            parts.Add($"{hit:+0;-0} to hit");
        }

        if (Math.Abs(harm) >= 0.05)
        {
            parts.Add($"{harm:+0.#;-0.#} damage");
        }

        foreach (var quality in Flags(gained))
        {
            parts.Add($"gets past {DamageBypasses.Name(quality)}");
        }

        foreach (var quality in Flags(lost))
        {
            parts.Add($"no longer {DamageBypasses.Name(quality)}");
        }

        if (parts.Count == 0)
        {
            parts.Add("no different");
        }

        var better = hit > 0 || harm > 0.05 || gained != DamageBypass.None;
        var worse = hit < 0 || harm < -0.05 || lost != DamageBypass.None;

        // Proficiency counts for more than its four points: a weapon this creature was never
        // taught is never an upgrade on one it was, whatever its dice.
        if (!proficient && Proficiency.IsProficient(creature, held))
        {
            (better, worse) = (false, true);
        }

        var verdict = better && !worse ? GearVerdict.Upgrade
            : worse && !better ? GearVerdict.Downgrade
            : GearVerdict.Sidegrade;

        return new GearComparison(item, item.Slot, current.Item, verdict, string.Join(", ", parts) + untrained, refusal);
    }

    private static GearComparison CompareArmour(Creature creature, ItemDefinition item, EquippedItem? current, string? refusal)
    {
        var dexterity = creature.Abilities[Ability.Dexterity].Modifier;
        var training = Martial.ArmorTraining(creature);

        int Gives(ItemDefinition? armour)
        {
            if (armour is null)
            {
                return 0;
            }

            var bonus = armour.Grants
                .Where(grant => grant.Target.Kind == ModifierTargetKind.ArmorClass)
                .Sum(grant => grant.Value);

            // What Dexterity it would let through, against what is let through without it. A
            // shield caps nothing.
            var cap = armour.MaxDexterity is { } most ? most + Math.Max(0, training) : (int?)null;
            var lost = cap is { } limit && dexterity > limit ? dexterity - limit : 0;

            return bonus - lost;
        }

        var proficient = Proficiency.IsProficient(creature, item);
        var gain = Gives(item) - Gives(current?.Item);
        var parts = new List<string> { $"{gain:+0;-0;+0} AC" };

        if (item.MaxDexterity is { } max)
        {
            parts.Add($"max Dex +{max}");
        }

        var slower = item.Armour is ArmourCategory.Medium or ArmourCategory.Heavy
            && (current?.Item.Armour ?? ArmourCategory.None) is not (ArmourCategory.Medium or ArmourCategory.Heavy)
            && training < (item.Armour == ArmourCategory.Medium ? 1 : 2);

        if (slower)
        {
            parts.Add("slower");
        }

        if (!proficient)
        {
            parts.Add($"not proficient: {Math.Min(0, item.CheckPenalty)} on attacks");
        }

        var verdict = !proficient || gain < 0 ? GearVerdict.Downgrade
            : gain > 0 && !slower ? GearVerdict.Upgrade
            : GearVerdict.Sidegrade;

        return new GearComparison(item, item.Slot, current?.Item, verdict, string.Join(", ", parts), refusal);
    }

    private static GearComparison CompareGrants(ItemDefinition item, EquippedItem? current, string? refusal)
    {
        var gain = item.Grants.Sum(grant => grant.Value) - (current?.Item.Grants.Sum(grant => grant.Value) ?? 0);

        var verdict = gain > 0 ? GearVerdict.Upgrade : gain < 0 ? GearVerdict.Downgrade : GearVerdict.Sidegrade;
        var reason = item.Grants.Count == 0
            ? "does nothing in a fight"
            : string.Join(", ", item.Grants.Select(grant => $"{grant.Value:+0;-0} {BonusTypes.Name(grant.Type)}"));

        return new GearComparison(item, item.Slot, current?.Item, verdict, reason, refusal);
    }

    private static IEnumerable<DamageBypass> Flags(DamageBypass flags) =>
        Enum.GetValues<DamageBypass>().Where(flag => flag != DamageBypass.None && (flags & flag) == flag);
}
