using Ironbound.Rules.Content;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Maps;

namespace Ironbound.Simulation.Tests;

public class ScenariosTests
{
    [Fact]
    public void TheShippedContentLoadsWithNothingWrongWithIt() =>
        Assert.Empty(ContentFiles.Default.Problems);

    [Fact]
    public void TheOpeningFightComesOutOfTheFilesTheWayItUsedToComeOutOfTheCode()
    {
        var battle = Scenarios.GoblinAmbush();

        Assert.Equal(["Valeria", "Karn", "Merrin"], battle.Party.Select(c => c.Name));
        Assert.Equal(["Goblin 1", "Goblin 2", "Goblin 3"], battle.Foes.Select(c => c.Name));

        var field = battle.Encounter.Battlefield!;
        Assert.Equal(new GridSquare(3, 4), field.SquareOf(battle.Party[0]));
        Assert.Equal(new GridSquare(10, 3), field.SquareOf(battle.Foes[0]));
        Assert.Equal((16, 12), (field.Width, field.Height));
    }

    [Fact]
    public void ThreeGoblinsOffOneDefinitionAreThreeGoblins()
    {
        var battle = Scenarios.GoblinAmbush();

        battle.Foes[0].HitPoints.Take(5);

        // One definition, three creatures: wounding one must not wound the others.
        Assert.Equal(5, battle.Foes[0].HitPoints.Damage);
        Assert.Equal(0, battle.Foes[1].HitPoints.Damage);
        Assert.NotSame(battle.Foes[0].PrimaryAttack, battle.Foes[1].PrimaryAttack);
    }

    [Fact]
    public void AnEncounterThatIsNotInTheFilesSaysSoRatherThanBuildingAnEmptyFight()
    {
        var error = Assert.Throws<ArgumentException>(
            () => Scenarios.Build(ContentFiles.Default, "dragon-lair"));

        Assert.Contains("dragon-lair", error.Message);
    }

    [Fact]
    public void TheGroundComesOutOfTheFileToo()
    {
        var library = ContentLibrary.Load([
            ("rubble.json", """
                { "kind": "creature", "id": "goblin", "name": "Goblin",
                  "abilities": [11, 15, 12, 10, 9, 6], "hitPoints": 9, "hitDice": 2 }
                """),
            ("fight.json", """
                { "kind": "encounter", "id": "rubble", "name": "Rubble", "width": 8, "height": 8,
                  "blocked": [ { "x": 2, "y": 2 } ], "difficult": [ { "x": 3, "y": 3 } ],
                  "placements": [ { "creature": "goblin", "x": 1, "y": 1 } ] }
                """),
        ]);

        var field = Scenarios.Build(library, "rubble").Encounter.Battlefield!;

        Assert.True(field.IsBlocked(new GridSquare(2, 2)));
        Assert.True(field.IsDifficult(new GridSquare(3, 3)));
    }

    [Fact]
    public void ReadingTheFilesTwiceGivesTheSameOrderBothTimes()
    {
        // Content order decides spell target order and which duplicate id gets reported, so a
        // loader that shuffled would make a save load differently from the run that wrote it.
        Assert.Equal(
            ContentFiles.Read(Path.Combine(AppContext.BaseDirectory, "content")).Select(f => f.Source),
            ContentFiles.Read(Path.Combine(AppContext.BaseDirectory, "content")).Select(f => f.Source));
    }
}
