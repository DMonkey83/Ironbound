using System.Globalization;
using Ironbound.Rules.Combat;
using Ironbound.Rules.Defense;

namespace Ironbound.Rules.Items;

/// <summary>
/// What a weapon or a suit of armour costs once it is made of something unusual, made with care,
/// or enchanted. Every price is in copper pieces, so they add up without rounding.
/// </summary>
/// <remarks>
/// The numbers are the Core Rulebook's (Open Game Content); the arithmetic is ours. Masterwork
/// adds a flat sum; an enhancement implies masterwork and then costs the square of its bonus;
/// alchemical silver costs more the more blade there is to silver; cold iron doubles the base
/// and makes the first enchantment dearer, because enchanting it is harder.
/// </remarks>
public static class Pricing
{
    public const int CopperPerSilver = 10;
    public const int CopperPerGold = 100;
    public const int CopperPerPlatinum = 1000;

    /// <summary>Masterwork on a weapon: 300 gp.</summary>
    public const int MasterworkWeaponGold = 300;

    /// <summary>Masterwork on armour or a shield: 150 gp.</summary>
    public const int MasterworkArmourGold = 150;

    /// <summary>An enhancement on a weapon costs its bonus squared times this.</summary>
    public const int WeaponEnhancementGold = 2000;

    /// <summary>An enhancement on armour or a shield costs its bonus squared times this.</summary>
    public const int ArmourEnhancementGold = 1000;

    /// <summary>What cold iron adds to the first enhancement, on top of the ordinary price.</summary>
    public const int ColdIronEnchantingGold = 2000;

    /// <summary>Gold pieces as copper, rounded to the nearest copper: 0.2 gp is 20 cp.</summary>
    public static int Copper(decimal gold) => (int)Math.Round(gold * CopperPerGold, MidpointRounding.AwayFromZero);

    /// <summary>
    /// What alchemical silver adds, by how much weapon there is: a light one 20 gp, a one-handed
    /// one 90, a two-handed one 180. Ammunition is 2 gp a piece, and has no item here yet.
    /// </summary>
    public static int SilverGold(WeaponHands hands) => hands switch
    {
        WeaponHands.Light => 20,
        WeaponHands.OneHanded => 90,
        _ => 180,
    };

    /// <summary>
    /// A weapon's price in copper: its base, doubled for cold iron, plus silver, masterwork and
    /// enhancement as they apply.
    /// </summary>
    /// <param name="baseGold">The table's cost for an ordinary one.</param>
    /// <param name="hands">How it is held: what silvering it costs. A ranged weapon counts as two-handed.</param>
    public static int Weapon(
        decimal baseGold, WeaponHands hands, DamageBypass materials, int enhancement, bool masterwork)
    {
        var gold = baseGold;

        if ((materials & DamageBypass.ColdIron) != 0)
        {
            gold *= 2;
        }

        if ((materials & DamageBypass.Silver) != 0)
        {
            gold += SilverGold(hands);
        }

        if (masterwork || enhancement > 0)
        {
            gold += MasterworkWeaponGold;
        }

        if (enhancement > 0)
        {
            gold += enhancement * enhancement * WeaponEnhancementGold;

            if ((materials & DamageBypass.ColdIron) != 0)
            {
                gold += ColdIronEnchantingGold;
            }
        }

        return Copper(gold);
    }

    /// <summary>Armour's or a shield's price in copper: its base, plus masterwork and enhancement.</summary>
    public static int Armour(decimal baseGold, int enhancement, bool masterwork)
    {
        var gold = baseGold;

        if (masterwork || enhancement > 0)
        {
            gold += MasterworkArmourGold;
        }

        if (enhancement > 0)
        {
            gold += enhancement * enhancement * ArmourEnhancementGold;
        }

        return Copper(gold);
    }

    /// <summary>
    /// A price written the way a shop would: "105 gp", "2 gp 5 sp", "4 cp", "nothing".
    /// Platinum is left as gold, as prices usually are.
    /// </summary>
    public static string Format(int copper)
    {
        if (copper == 0)
        {
            return "nothing";
        }

        var sign = copper < 0 ? "-" : string.Empty;
        copper = Math.Abs(copper);

        var parts = new List<string>();
        var gold = copper / CopperPerGold;
        var silver = copper % CopperPerGold / CopperPerSilver;
        var rest = copper % CopperPerSilver;

        if (gold > 0)
        {
            parts.Add($"{gold.ToString("#,0", CultureInfo.InvariantCulture)} gp");
        }

        if (silver > 0)
        {
            parts.Add($"{silver} sp");
        }

        if (rest > 0)
        {
            parts.Add($"{rest} cp");
        }

        return sign + string.Join(" ", parts);
    }

    /// <summary>A weight the way the sheet writes it: "4 lb", "½ lb", "12.5 lb".</summary>
    public static string Pounds(decimal pounds) =>
        pounds == 0.5m ? "½ lb" : $"{Math.Round(pounds, 2).ToString("0.##", CultureInfo.InvariantCulture)} lb";
}
