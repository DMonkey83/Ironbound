using System.Globalization;
using Ironbound.Rules.Classes;
using Ironbound.Rules.Combat;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Defense;
using Ironbound.Rules.Effects;
using Ironbound.Rules.Items;
using Ironbound.Rules.Maps;

namespace Ironbound.Rules.Content;

/// <summary>
/// Weapons and items written out in short lines, for a tooltip and a character sheet.
/// </summary>
/// <remarks>
/// Here rather than in the Godot layer for the same reason the character sheet is in Simulation:
/// the hard part — which dice a halfling swings, whether this fighter is proficient, why a shield
/// is doing nothing — is rules, and is tested without an engine. The view only has to draw lines.
/// <para>
/// Every special the game does not act on yet says so, rather than being left out: a player who
/// reads "disarm" on a weapon and finds no way to disarm anybody deserves to have been told.
/// </para>
/// </remarks>
public sealed partial class ContentLibrary
{
    /// <summary>
    /// A weapon in a few lines: what it is, what it does, how far, its specials, its price, and —
    /// given somebody to hold it — whether they can use it. Empty for a weapon that does not exist.
    /// </summary>
    public IReadOnlyList<string> DescribeWeapon(string id, Creature? wielder = null)
    {
        ArgumentNullException.ThrowIfNull(id);

        if (GetWeapon(id) is not { } weapon)
        {
            return [];
        }

        var lines = new List<string> { Heading(weapon) };

        if (weapon.Description.Length > 0)
        {
            lines.Add(weapon.Description);
        }

        if (!weapon.Usable)
        {
            lines.Add("Catalogue only — not something anybody can fight with yet");
        }
        else
        {
            lines.Add(DamageLine(weapon, wielder?.Size ?? CreatureSize.Medium));
        }

        if (RangeLine(weapon, wielder) is { } range)
        {
            lines.Add(range);
        }

        lines.AddRange(FirearmLines(weapon));
        lines.AddRange(SpecialLines(weapon));
        lines.Add(PriceLine(weapon.Cost, weapon.Weight));

        if (wielder is not null && weapon.Usable && weapon.Build(size: wielder.Size) is var built
            && !Proficiency.IsProficient(wielder, built))
        {
            lines.Add($"Not proficient: {Proficiency.NonProficientPenalty} to hit");
        }

        return lines;
    }

    /// <summary>
    /// An item in a few lines: a weapon item as its weapon, with whatever this one adds; armour
    /// and shields with their bonus, Dexterity cap and check penalty; anything else with what it
    /// grants. Given a wielder, also whether they are trained for it, and anything wrong with it.
    /// </summary>
    public IReadOnlyList<string> DescribeItem(ItemDefinition item, Creature? wielder = null)
    {
        ArgumentNullException.ThrowIfNull(item);

        var lines = new List<string>();

        if (item.Weapon is { } kind && GetWeapon(kind) is not null)
        {
            lines.AddRange(DescribeWeapon(kind, wielder));

            if (item.Description.Length > 0)
            {
                lines.Insert(1, item.Description);
            }

            if (item.Enhancement != 0)
            {
                lines.Add($"{item.Enhancement:+0;-0} enhancement to hit and to damage, and counts as magic");
            }

            foreach (var quality in Enum.GetValues<DamageBypass>()
                .Where(flag => flag is not (DamageBypass.None or DamageBypass.Magic) && item.Qualities.HasFlag(flag)))
            {
                lines.Add($"{Capitalise(DamageBypasses.Name(quality))} — gets past reduction that only {DamageBypasses.Name(quality)} gets past");
            }
        }
        else
        {
            if (ArmourHeading(item) is { } heading)
            {
                lines.Add(heading);
            }

            if (item.Description.Length > 0)
            {
                lines.Add(item.Description);
            }

            lines.AddRange(item.Grants.Select(Grant));

            if (item.MaxDexterity is { } most)
            {
                lines.Add($"Dexterity bonus at most +{most}");
            }

            if (item.Armour != ArmourCategory.None)
            {
                lines.Add(item.CheckPenalty < 0 ? $"Check penalty {item.CheckPenalty}" : "No check penalty");
            }

            if (item.Armour is ArmourCategory.Medium or ArmourCategory.Heavy)
            {
                lines.Add("Slows the wearer: thirty feet to twenty, twenty to fifteen");
            }

            if (wielder is not null && !Proficiency.IsProficient(wielder, item))
            {
                lines.Add(item.CheckPenalty < 0
                    ? $"Not proficient: its check penalty, {item.CheckPenalty}, falls on attack rolls too"
                    : "Not proficient, though it costs nothing on attack rolls");
            }

            if (item.Armour == ArmourCategory.Shield)
            {
                lines.Add(wielder is not null && wielder.Equipment.Has(item.Id) && wielder.Equipment.ShieldSetAside
                    ? "Not counted while wielding a two-handed weapon — and that is happening now"
                    : "Not counted while wielding a two-handed weapon");
            }
        }

        if (wielder?.Equipment.Worn.FirstOrDefault(entry => ReferenceEquals(entry.Item, item)) is { } worn)
        {
            if (worn.IsBroken)
            {
                lines.Add($"Broken: {Strike.BrokenPenalty} to hit and to damage, and a critical only on a 20");
            }

            if (worn.IsOutOfHand)
            {
                lines.Add("Out of hand until the fight is over");
            }
        }

        return lines;
    }

    /// <summary>"Martial · one-handed · heavy blades".</summary>
    private static string Heading(WeaponDefinition weapon)
    {
        var hands = WeaponHandsInfo.Name(weapon.Hands);
        if (weapon.Grip is { } grip && weapon.Hands == WeaponHands.Ranged)
        {
            hands = $"{hands}, {WeaponHandsInfo.Name(grip)}";
        }

        var parts = new List<string> { Capitalise(WeaponCategories.Name(weapon.Category)), hands };
        if (weapon.Groups.Count > 0)
        {
            parts.Add(string.Join(", ", weapon.Groups.Select(Martial.GroupName)));
        }

        return string.Join(" · ", parts);
    }

    /// <summary>"1d8 slashing (1d6 if Small) · 19–20/×2", the dice for whoever is asking first.</summary>
    private static string DamageLine(WeaponDefinition weapon, CreatureSize size)
    {
        var types = weapon.DamageTypes.Select(DamageTypes.Name).ToList();
        var typed = weapon.DamageRule switch
        {
            DamageRule.Both => string.Join(" and ", types),
            _ => string.Join(" or ", types),
        };

        var dice = weapon.DiceFor(size);
        string other;

        if (weapon.Category == WeaponCategory.Natural)
        {
            other = string.Empty;
        }
        else if (size == CreatureSize.Medium)
        {
            other = $" ({weapon.DiceFor(CreatureSize.Small)} if Small)";
        }
        else
        {
            other = $" ({weapon.Damage} if Medium)";
        }

        return $"{dice} {typed}{other} · {Critical(weapon.ThreatsOn, weapon.Multiplier)}";
    }

    /// <summary>"Thrown, 10 ft increments", "Shot, 60 ft increments", "Reach — threatens 10 ft, not adjacent".</summary>
    private static string? RangeLine(WeaponDefinition weapon, Creature? wielder)
    {
        if (weapon.IsRanged)
        {
            var strength = weapon.DamageScale switch
            {
                AbilityDamageScale.None => "no Strength on damage",
                _ => "Strength on damage",
            };

            return weapon.Thrown
                ? $"Thrown, {weapon.Range} ft increments, five at most; {strength}"
                : $"Shot, {weapon.Range} ft increments, ten at most; {strength}";
        }

        if (weapon.IsThrowable)
        {
            return $"Thrown, {weapon.Range} ft increments, five at most — and out of hand until the fight is over";
        }

        if (weapon.Has(WeaponSpecial.Reach))
        {
            var band = wielder is null
                ? new ReachBand(2 * Distance.FeetPerSquare, 2 * Distance.FeetPerSquare)
                : ReachBand.Of(wielder, weapon.Build(size: wielder.Size));

            return $"Reach — threatens {band}";
        }

        return null;
    }

    private static IEnumerable<string> FirearmLines(WeaponDefinition weapon)
    {
        if (weapon.Firearm == FirearmEra.None || !weapon.Usable)
        {
            yield break;
        }

        var touch = weapon.Firearm == FirearmEra.Early
            ? "against touch AC in its first increment"
            : "against touch AC out to five increments";

        var misfire = weapon.Misfire <= 1 ? "1" : $"1–{weapon.Misfire}";
        var broken = $"1–{weapon.Misfire + Firearms.BrokenMisfireIncrease}";

        yield return $"Firearm ({weapon.Firearm.ToString().ToLowerInvariant()}): {touch}";
        yield return $"Misfires on {misfire} ({broken} while broken); a broken gun that misfires bursts";

        if (weapon.Capacity > 0)
        {
            yield return $"Holds {weapon.Capacity} — reloading is not yet in the game";
        }
    }

    private static IEnumerable<string> SpecialLines(WeaponDefinition weapon)
    {
        foreach (var special in WeaponSpecials.Each(weapon.Specials))
        {
            // A see-text weapon's own line has already said what the text is about.
            if (special == WeaponSpecial.SeeText && weapon.Description.Length > 0)
            {
                continue;
            }

            // Reach has a line of its own already, saying how far.
            if (special == WeaponSpecial.Reach)
            {
                continue;
            }

            yield return special switch
            {
                WeaponSpecial.Trip => "Trip — a failed trip drops the weapon, not you",
                WeaponSpecial.Nonlethal => "Nonlethal — its damage knocks out rather than kills",
                WeaponSpecial.Blocking => $"Blocking — +{Martial.BlockingBonus} shield bonus to AC while fighting defensively",
                WeaponSpecial.Fragile => "Fragile — a natural 1 breaks it, and a second destroys it",
                WeaponSpecial.Double => weapon.SecondHead is { } head
                    ? $"Double — other end {head.DamageMedium}, {Critical(head.ThreatsOn, head.Multiplier)}; "
                        + "fought with one end until two-weapon fighting is in the game"
                    : "Double — fought with one end until two-weapon fighting is in the game",
                WeaponSpecial.SeeText => "See text — not yet in the game",
                _ => $"{Capitalise(WeaponSpecials.Name(special))} — not yet in the game",
            };
        }
    }

    private static string? ArmourHeading(ItemDefinition item) => item.Armour switch
    {
        ArmourCategory.Light => "Light armour",
        ArmourCategory.Medium => "Medium armour",
        ArmourCategory.Heavy => "Heavy armour",
        ArmourCategory.Shield => item.TowerShield ? "Tower shield" : "Shield",
        _ => null,
    };

    private static string Grant(ModifierGrant grant)
    {
        var target = grant.Target.Kind switch
        {
            ModifierTargetKind.ArmorClass => "armour class",
            ModifierTargetKind.Attack => "attack rolls",
            ModifierTargetKind.Damage => "damage",
            ModifierTargetKind.Speed => "speed",
            ModifierTargetKind.Initiative => "initiative",
            ModifierTargetKind.Save => $"{(Saves.Save)grant.Target.Which} saves",
            _ => grant.Target.ToString(),
        };

        var type = grant.Type switch
        {
            Modifiers.BonusType.Untyped => string.Empty,
            Modifiers.BonusType.Armor => " armour",
            Modifiers.BonusType.NaturalArmor => " natural armour",
            _ => $" {Modifiers.BonusTypes.Name(grant.Type)}",
        };
        return $"{grant.Value:+0;-0}{type} to {target}";
    }

    /// <summary>"15 gp · 4 lb", "2 sp · ½ lb"; a weapon of no price says so.</summary>
    private static string PriceLine(double cost, double weight)
    {
        var price = cost switch
        {
            <= 0 => "costs nothing",
            < 0.1 => $"{Number(cost * 100)} cp",
            < 1 => $"{Number(cost * 10)} sp",
            _ => $"{Number(cost)} gp",
        };

        return weight > 0 ? $"{price} · {Number(weight)} lb" : price;
    }

    private static string Number(double value) =>
        value == 0.5 ? "½" : Math.Round(value, 2).ToString("0.##", CultureInfo.InvariantCulture);

    private static string Critical(int threatsOn, int multiplier) =>
        threatsOn == 20 ? $"×{multiplier}" : $"{threatsOn}–20/×{multiplier}";

    private static string Capitalise(string text) =>
        text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];
}
