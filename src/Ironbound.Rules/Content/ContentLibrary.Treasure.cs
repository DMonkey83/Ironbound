using Ironbound.Rules.Items;
using Ironbound.Rules.Maps;

namespace Ironbound.Rules.Content;

/// <summary>One room's treasure against the book's yardstick, all in copper pieces.</summary>
/// <param name="Placed">What is there to be found: the fallen's gear and their purses on average,
/// the room's loot, and every container in it.</param>
/// <param name="Yardstick">What the treasure table gives an encounter at the party's level, or
/// nought for a room with nobody to fight.</param>
public sealed record TreasureLine(
    string AreaId, string Name, int Placed, int Yardstick, IReadOnlyList<string> Parts)
{
    public override string ToString() => Yardstick > 0
        ? $"{Name}: {Pricing.Format(Placed)} placed against {Pricing.Format(Yardstick)} "
            + $"({Placed * 100 / Math.Max(1, Yardstick)}%)"
        : $"{Name}: {Pricing.Format(Placed)} placed, no fight to measure it by";
}

/// <summary>A whole level's treasure against the yardstick.</summary>
public sealed record TreasureReport(
    string LevelId, int PartyLevel, IReadOnlyList<TreasureLine> Areas, int Placed, int Yardstick)
{
    public IEnumerable<string> Lines()
    {
        yield return $"{LevelId}: average party level {PartyLevel}, medium track, "
            + $"{Pricing.Format(Treasure.PerEncounter(PartyLevel))} an encounter";

        foreach (var area in Areas)
        {
            yield return "  " + area;
        }

        yield return $"  In all: {Pricing.Format(Placed)} placed against {Pricing.Format(Yardstick)}";
    }
}

/// <summary>The book's treasure table, medium track.</summary>
public static class Treasure
{
    /// <summary>Table 12-5 of the Core Rulebook, medium track, gold pieces an encounter for an average party level of 1 to 20.</summary>
    private static readonly int[] MediumGold =
    [
        260, 550, 800, 1150, 1550, 2000, 2600, 3350, 4250, 5450,
        7000, 9000, 11600, 15000, 19500, 25000, 32000, 41000, 53000, 67000,
    ];

    /// <summary>What one encounter is worth at this average party level, in copper pieces.</summary>
    public static int PerEncounter(int partyLevel) =>
        MediumGold[Math.Clamp(partyLevel, 1, MediumGold.Length) - 1] * Pricing.CopperPerGold;

    /// <summary>
    /// The book's average party level: the levels added up and shared out, rounded, then one more
    /// for a party of six or more and one less for three or fewer. Never below one.
    /// </summary>
    public static int AveragePartyLevel(IReadOnlyList<int> levels)
    {
        ArgumentNullException.ThrowIfNull(levels);

        if (levels.Count == 0)
        {
            return 1;
        }

        var average = (int)Math.Round((decimal)levels.Sum() / levels.Count, MidpointRounding.AwayFromZero);
        var adjusted = levels.Count >= 6 ? average + 1 : levels.Count <= 3 ? average - 1 : average;

        return Math.Max(1, adjusted);
    }
}

public sealed partial class ContentLibrary
{
    /// <summary>
    /// Adds up what each room of a level holds against the treasure table — information, never a
    /// problem. The GM places the treasure; the table is a yardstick.
    /// </summary>
    /// <remarks>
    /// The party level is the book's average party level for whoever starts the level. A room's
    /// treasure is its fallen's gear at its price and their purses at their average, its loot,
    /// and every container standing in it; containers outside every room are listed as their own
    /// line. Only a room with somebody to fight in it is measured.
    /// </remarks>
    public TreasureReport TreasureOf(LevelDefinition level)
    {
        ArgumentNullException.ThrowIfNull(level);

        var levels = level.Start
            .Select(placement => GetCreature(placement.CreatureId))
            .OfType<CreatureDefinition>()
            .Select(creature => creature.Classes.Count > 0 ? creature.Classes.Sum(taken => taken.Level) : creature.Level)
            .ToList();

        var partyLevel = Treasure.AveragePartyLevel(levels);
        var lines = new List<TreasureLine>();
        var placedFeatures = new HashSet<string>(StringComparer.Ordinal);

        // Each container counted once, in the room it stands in, or failing that the room whose
        // wall it is a niche in.
        var home = level.Features
            .Where(feature => feature.Kind == FeatureKind.Container)
            .ToDictionary(
                feature => feature.Id,
                feature => (level.Areas.FirstOrDefault(area => feature.Squares.Any(area.Contains))
                    ?? level.Areas.FirstOrDefault(area => feature.Squares.Any(square => Touches(area, square))))?.Id,
                StringComparer.Ordinal);

        foreach (var area in level.Areas)
        {
            var parts = new List<string>();
            var placed = 0m;

            foreach (var foe in area.Foes)
            {
                if (GetCreature(foe.CreatureId) is not { } creature)
                {
                    continue;
                }

                var gear = creature.Items.Select(GetItem).OfType<ItemDefinition>().Where(item => !item.IsNatural).Sum(item => item.Price);
                var purse = creature.Purse.Average;
                placed += gear + purse;
                parts.Add($"{foe.Name ?? creature.Name}: gear {Pricing.Format(gear)}, purse about {Pricing.Format((int)purse)}");
            }

            var loot = Value(area.Loot);
            if (loot > 0)
            {
                placed += loot;
                parts.Add($"loot {Pricing.Format(loot)}");
            }

            foreach (var feature in level.Features.Where(feature => feature.Kind == FeatureKind.Container
                && home.GetValueOrDefault(feature.Id) == area.Id))
            {
                placedFeatures.Add(feature.Id);

                var held = Value(feature.Loot) + feature.Coins.Value;
                placed += held;
                parts.Add($"{feature.Name}: {Pricing.Format(held)}");
            }

            lines.Add(new TreasureLine(
                area.Id, area.Name, (int)placed, area.Foes.Count > 0 ? Treasure.PerEncounter(partyLevel) : 0, parts));
        }

        var outside = level.Features
            .Where(feature => feature.Kind == FeatureKind.Container && !placedFeatures.Contains(feature.Id))
            .ToList();

        if (outside.Count > 0)
        {
            var held = outside.Sum(feature => Value(feature.Loot) + feature.Coins.Value);
            lines.Add(new TreasureLine(
                string.Empty,
                "Between the rooms",
                held,
                0,
                [.. outside.Select(feature => $"{feature.Name}: {Pricing.Format(Value(feature.Loot) + feature.Coins.Value)}")]));
        }

        return new TreasureReport(
            level.Id, partyLevel, lines, lines.Sum(line => line.Placed), lines.Sum(line => line.Yardstick));
    }

    private int Value(IEnumerable<LootDefinition> loot) =>
        loot.Sum(found => (GetItem(found.ItemId)?.Price ?? 0) * found.Count);

    /// <summary>Whether a square in the wall is a niche of this room: right against its floor.</summary>
    private static bool Touches(AreaDefinition area, GridSquare square) =>
        square.X >= area.X - 1 && square.Y >= area.Y - 1
        && square.X <= area.X + area.Width && square.Y <= area.Y + area.Height;
}
