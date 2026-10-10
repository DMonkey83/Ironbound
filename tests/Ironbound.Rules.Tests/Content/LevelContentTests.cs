using Ironbound.Rules.Content;
using Ironbound.Rules.Maps;

namespace Ironbound.Rules.Tests.Content;

public class ShippedLevelTests
{
    private static ContentLibrary Library => TestContent.Library;

    [Theory]
    [InlineData("caves-of-shadow", 36, 46, 4)]
    [InlineData("the-long-road", 52, 16, 3)]
    public void BothLevelsLoadAndTheirCampaignsAreSetInThem(string id, int width, int height, int party)
    {
        Assert.Empty(Library.Problems);

        var level = Library.GetLevel(id)!;

        Assert.Equal(width, level.Width);
        Assert.Equal(height, level.Height);
        Assert.Equal(party, level.Start.Count);
        Assert.All(level.Start, placement => Assert.True(placement.Party));
        Assert.Single(level.Areas, area => area.Final);
        Assert.Equal(id, Library.GetCampaign(id)!.Level);
    }

    [Fact]
    public void TheyAreListedInOrder()
    {
        Assert.Equal(
            ["caves-of-shadow", "the-long-road"],
            Library.Levels.Select(level => level.Id));
    }

    [Fact]
    public void TheCavesHaveTheirDoorsBridgeContainersAndTobin()
    {
        var caves = Library.GetLevel("caves-of-shadow")!;

        Assert.Equal(
            [FeatureKind.Bridge, FeatureKind.Door, FeatureKind.Door,
                FeatureKind.Container, FeatureKind.Container, FeatureKind.Container,
                FeatureKind.Container, FeatureKind.Container, FeatureKind.Merchant],
            caves.Features.Select(feature => feature.Kind));
        Assert.Equal(17, caves.GetFeature("den-door")!.BreakDc);
        Assert.Equal(
            [new LootDefinition("greatsword-plus-one"), new LootDefinition("potion-of-cure-light-wounds")],
            caves.GetFeature("tobins-cart")!.Loot);
        Assert.Equal(ContainerLook.Crate, caves.GetFeature("tobins-cart")!.Look);
    }
}

public class LevelParsingTests
{
    private const string Level = """
        { "kind": "level", "id": "yard", "name": "The Yard",
          "stone": "flagstones", "grass": "lawn",
          "map": [
            "#####",
            "#.,T#",
            "#R~+#",
            "#btc#",
            "#####" ],
          "start": [ { "creature": "hero", "x": 1, "y": 1 } ],
          "areas": [
            { "id": "lawn", "name": "The Lawn", "x": 1, "y": 1, "width": 2, "height": 1,
              "intro": "Grass.", "outro": "Quiet.", "final": true, "loot": ["key"], "xp": 120,
              "foes": [ { "creature": "hero", "x": 2, "y": 1, "name": "Rival",
                          "hidden": true, "asleep": true } ] } ],
          "features": [
            { "id": "gate", "kind": "door", "name": "the gate", "squares": [ { "x": 3, "y": 2 } ],
              "lockDc": 20 },
            { "id": "gap", "kind": "bridge", "name": "the gap", "squares": [ { "x": 2, "y": 2 } ] },
            { "id": "box", "kind": "cache", "name": "the box", "text": "A key.",
              "squares": [ { "x": 3, "y": 3 } ], "loot": ["key"], "xp": 5 } ] }
        """;

    private const string Hero = """
        { "kind": "creature", "id": "hero", "name": "Hero", "abilities": [10, 10, 10, 10, 10, 10] }
        """;

    private const string Key = """{ "kind": "item", "id": "key", "name": "key" }""";

    private const string Terrains = """
        { "kind": "terrain", "id": "flagstones", "name": "Flagstones" }
        """;

    private const string Lawn = """{ "kind": "terrain", "id": "lawn", "name": "Lawn" }""";

    private static LevelDefinition Yard() =>
        ContentParsingTests.From(Level, Hero, Key, Terrains, Lawn).GetLevel("yard")!;

    [Fact]
    public void TheMapIsReadALetterAtATime()
    {
        var yard = Yard();

        Assert.Equal(5, yard.Width);
        Assert.Equal(5, yard.Height);
        Assert.Equal(LevelCell.Stone, yard.CellAt(1, 1));
        Assert.Equal(LevelCell.Grass, yard.CellAt(2, 1));
        Assert.Equal(LevelCell.Tree, yard.CellAt(3, 1));
        Assert.Equal(LevelCell.Rock, yard.CellAt(1, 2));
        Assert.Equal(LevelCell.Chasm, yard.CellAt(2, 2));
        Assert.Equal(LevelCell.Door, yard.CellAt(3, 2));
        Assert.Equal(LevelCell.Bed, yard.CellAt(1, 3));
        Assert.Equal(LevelCell.Table, yard.CellAt(2, 3));
        Assert.Equal(LevelCell.Crate, yard.CellAt(3, 3));
        Assert.Equal(LevelCell.Wall, yard.CellAt(0, 0));
    }

    [Fact]
    public void OffTheEdgeIsWall()
    {
        var yard = Yard();

        Assert.Equal(LevelCell.Wall, yard.CellAt(-1, 2));
        Assert.Equal(LevelCell.Wall, yard.CellAt(2, 99));
        Assert.True(yard.IsBlockedCell(-1, 2));
    }

    [Fact]
    public void OnlyFloorCanBeStoodOn()
    {
        var yard = Yard();
        var open = new List<(int, int)>();

        for (var y = 0; y < yard.Height; y++)
        {
            for (var x = 0; x < yard.Width; x++)
            {
                if (!yard.IsBlockedCell(x, y))
                {
                    open.Add((x, y));
                }
            }
        }

        Assert.Equal([(1, 1), (2, 1)], open);
    }

    [Fact]
    public void GrassGroundIsUnderGrassAndTrees()
    {
        var yard = Yard();

        Assert.Equal("lawn", yard.TerrainAt(2, 1));
        Assert.Equal("lawn", yard.TerrainAt(3, 1));
        Assert.Equal("flagstones", yard.TerrainAt(1, 1));
        Assert.Equal("flagstones", yard.TerrainAt(1, 2));
    }

    [Fact]
    public void AreasCarryTheirStoryFoesAndLoot()
    {
        var lawn = Yard().GetArea("lawn")!;

        Assert.Equal("The Lawn", lawn.Name);
        Assert.Equal("Grass.", lawn.Intro);
        Assert.Equal("Quiet.", lawn.Outro);
        Assert.True(lawn.Final);
        Assert.Equal([new LootDefinition("key")], lawn.Loot);

        var rival = Assert.Single(lawn.Foes);
        Assert.Equal(new PlacementDefinition("hero", 2, 1, false, "Rival", true, true), rival);
    }

    [Fact]
    public void AnAreaContainsItsRectangleAndNothingElse()
    {
        var lawn = Yard().GetArea("lawn")!;

        Assert.True(lawn.Contains(new GridSquare(1, 1)));
        Assert.True(lawn.Contains(new GridSquare(2, 1)));
        Assert.False(lawn.Contains(new GridSquare(3, 1)));
        Assert.False(lawn.Contains(new GridSquare(1, 2)));
        Assert.False(lawn.Contains(new GridSquare(0, 1)));
    }

    [Fact]
    public void FeaturesTakeTheRulebookNumbersUnlessTheySayOtherwise()
    {
        var yard = Yard();
        var gate = yard.GetFeature("gate")!;
        var gap = yard.GetFeature("gap")!;

        Assert.Equal(FeatureKind.Door, gate.Kind);
        Assert.Equal(20, gate.LockDc);
        Assert.Equal(16, gate.BreakDc);
        Assert.Equal([new GridSquare(3, 2)], gate.Squares);

        Assert.Equal(FeatureKind.Bridge, gap.Kind);
        Assert.Equal(10, gap.JumpDc);
        Assert.Equal(15, gap.ClimbDc);
        Assert.Equal("2d6", gap.Fall);

        Assert.Equal("A key.", yard.GetFeature("box")!.Text);
    }

    [Fact]
    public void FindingAndDoingThingsIsWorthWhatTheFileSaysOrTheDefault()
    {
        var yard = Yard();

        Assert.Equal(120, yard.GetArea("lawn")!.Experience);
        Assert.Equal(5, yard.GetFeature("box")!.Experience);
        Assert.Equal(FeatureDefinition.DefaultExperience(FeatureKind.Door), yard.GetFeature("gate")!.Experience);
        Assert.Equal(FeatureDefinition.DefaultExperience(FeatureKind.Bridge), yard.GetFeature("gap")!.Experience);
        Assert.True(FeatureDefinition.DefaultExperience(FeatureKind.Bridge) > FeatureDefinition.DefaultExperience(FeatureKind.Door));
    }

    [Fact]
    public void ACampaignCanBeSetInOne()
    {
        var library = ContentParsingTests.From(Level, Hero, Key, Terrains, Lawn, """
            { "kind": "campaign", "id": "run", "name": "Run", "level": "yard" }
            """);

        Assert.Equal("yard", library.GetCampaign("run")!.Level);
    }

    [Fact]
    public void AndOneWithoutALevelHasNone()
    {
        var library = ContentParsingTests.From("""
            { "kind": "campaign", "id": "run", "name": "Run" }
            """);

        Assert.Null(library.GetCampaign("run")!.Level);
    }
}

public class LevelProblemTests
{
    [Fact]
    public void ACampaignSetInALevelThatDoesNotExistIsReported()
    {
        var problems = Problems("""
            { "kind": "campaign", "id": "run", "name": "Run", "level": "nowhere" }
            """);

        var problem = Assert.Single(problems);
        Assert.Equal("campaign 'run'", problem.Source);
        Assert.Contains("nowhere", problem.Message);
    }

    [Fact]
    public void ALetterOutsideTheLegendIsReported()
    {
        Assert.Contains(Problems(Level(map: """ "###", "#?#", "###" """)),
            problem => problem.Field == "map" && problem.Message.Contains("'?'"));
    }

    [Fact]
    public void ARaggedRowIsReported()
    {
        Assert.Contains(Problems(Level(map: """ "###", "#.", "###" """)),
            problem => problem.Field == "map" && problem.Message.Contains("row 1"));
    }

    [Fact]
    public void ACreatureNobodyDefinedIsReported()
    {
        Assert.Contains(Problems(Level(foe: "dragon")),
            problem => problem.Message.Contains("no creature called 'dragon'"));
    }

    [Fact]
    public void SoIsSomebodyStandingInAWall()
    {
        Assert.Contains(Problems(Level(foeAt: "0, 0")),
            problem => problem.Field.Contains("foes") && problem.Message.Contains("Wall"));
    }

    [Fact]
    public void LootThatDoesNotExistIsReported()
    {
        Assert.Contains(Problems(Level(features: """
            { "id": "box", "kind": "cache", "name": "box", "squares": [ { "x": 0, "y": 1 } ],
              "loot": ["nothing"] }
            """)),
            problem => problem.Message.Contains("no item called 'nothing'"));
    }

    [Fact]
    public void ADoorWhereTheMapHasNoDoorIsReported()
    {
        Assert.Contains(Problems(Level(features: """
            { "id": "door", "kind": "door", "name": "door", "squares": [ { "x": 1, "y": 1 } ] }
            """)),
            problem => problem.Field == "features.door" && problem.Message.Contains("Stone"));
    }

    [Fact]
    public void ABridgeOverSolidGroundIsReported()
    {
        Assert.Contains(Problems(Level(features: """
            { "id": "gap", "kind": "bridge", "name": "gap", "squares": [ { "x": 0, "y": 0 } ] }
            """)),
            problem => problem.Field == "features.gap");
    }

    [Fact]
    public void AContainerOnOpenFloorIsFine()
    {
        // A sack by the door, a chest in the corner: not every container is furniture the map
        // draws. The old "cache" still loads as one.
        Assert.Empty(Problems(Level(features: """
            { "id": "box", "kind": "cache", "name": "box", "squares": [ { "x": 1, "y": 1 } ] }
            """)));
    }

    [Fact]
    public void ButACacheInAWallBesideTheFloorIsFine()
    {
        Assert.Empty(Problems(Level(features: """
            { "id": "niche", "kind": "cache", "name": "niche", "squares": [ { "x": 0, "y": 1 } ] }
            """)));
    }

    [Fact]
    public void ALevelNobodyCanWinIsReported()
    {
        Assert.Contains(Problems(Level(final: false)),
            problem => problem.Message.Contains("never be won"));
    }

    [Fact]
    public void TwoFoesOfOneNameAreReported()
    {
        Assert.Contains(Problems(Level(foeName: "Hero")),
            problem => problem.Message.Contains("more than one creature is called 'Hero'"));
    }

    private static IReadOnlyList<ContentProblem> Problems(params string[] files) =>
        ContentLibrary.Load(files.Select((json, index) => ($"file{index}.json", json)).Append((
            "hero.json",
            """{ "kind": "creature", "id": "hero", "name": "Hero", "abilities": [10, 10, 10, 10, 10, 10] }""")))
            .Problems;

    /// <summary>A small, valid level, with one thing at a time made wrong.</summary>
    private static string Level(
        string map = """ "#####", "#...#", "#####" """,
        string foe = "hero",
        string foeAt = "3, 1",
        string foeName = "Villain",
        string features = "",
        bool final = true)
    {
        var (x, y) = (foeAt.Split(',')[0].Trim(), foeAt.Split(',')[1].Trim());

        return $$"""
            { "kind": "level", "id": "test", "name": "Test",
              "map": [ {{map}} ],
              "start": [ { "creature": "hero", "x": 1, "y": 1 } ],
              "areas": [
                { "id": "room", "name": "Room", "x": 0, "y": 0, "width": 5, "height": 3,
                  "final": {{(final ? "true" : "false")}},
                  "foes": [ { "creature": "{{foe}}", "x": {{x}}, "y": {{y}}, "name": "{{foeName}}" } ] } ],
              "features": [ {{features}} ] }
            """;
    }
}
