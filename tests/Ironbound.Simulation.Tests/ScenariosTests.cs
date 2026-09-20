using Ironbound.Rules.Abilities;
using Ironbound.Rules.Conditions;
using Ironbound.Rules.Content;
using Ironbound.Rules.Dice;
using Ironbound.Rules.Effects;
using Ironbound.Rules.Encounters;
using Ironbound.Rules.Encounters.Actions;
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
        Assert.Equal(
            ["Goblin 1", "Goblin 2", "Goblin Archer", "Sergeant Grask"],
            battle.Foes.Select(c => c.Name));

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
    public void ThePillarsInTheAmbushAreRealEnoughToHideBehind()
    {
        var field = Scenarios.GoblinAmbush().Encounter.Battlefield!;

        // Straight through the pillar at (6,4): the shot exists, but it is a worse one.
        Assert.True(field.HasLineOfSight(new GridSquare(4, 4), new GridSquare(9, 4)));
        Assert.True(field.HasCover(new GridSquare(4, 4), new GridSquare(9, 4)));

        // And well clear of both pillars, nothing is in the way.
        Assert.False(field.HasCover(new GridSquare(1, 10), new GridSquare(14, 10)));
    }

    [Fact]
    public void TheArcherCarriesABowItReachesForAndABladeItFallsBackOn()
    {
        var archer = ContentFiles.Default.BuildCreature("goblin-archer")!;

        Assert.True(archer.PrimaryAttack!.IsRanged);
        Assert.Equal(60, archer.PrimaryAttack.RangeIncrement);
        Assert.Equal("scimitar", archer.MeleeAttack!.Name);
    }

    [Fact]
    public void AFightAtSixthLevelActuallyProducesSecondSwings()
    {
        var battle = Scenarios.GoblinAmbush();
        battle.RunToCompletion(Scenarios.AutoPilot(battle));

        // Not a unit test of Iteratives — a check that the AI ever reaches the position where a
        // full attack is the right call. It went a whole layer without doing so, because it kept
        // walking past whoever was in front of it to reach the weakest enemy on the field.
        Assert.Contains(battle.Log, line => line.Contains("attacks 2 times"));
    }

    [Fact]
    public void ACasterInRangeCursesBeforeItStartsThrowingDarts()
    {
        var (source, turn) = Duel(cursed: false);

        var action = Assert.IsType<CastSpellAction>(source.NextAction(turn));

        // Before this layer a non-damaging spell was skipped outright, so the caster reached
        // straight past the curse for the dart.
        Assert.Equal("cause-fear", action.Spell.Id);
    }

    [Fact]
    public void ItDoesNotCurseTheSameTargetTwice()
    {
        var (source, turn) = Duel(cursed: true);

        // A second Cause Fear on somebody already shaken buys nothing at all, so the caster
        // falls through to the spell that does something.
        var action = Assert.IsType<CastSpellAction>(source.NextAction(turn));

        Assert.Equal("magic-missile", action.Spell.Id);
    }

    /// <summary>A caster who knows only a curse and a dart, twenty feet from one goblin.</summary>
    private static (HeuristicActionSource Source, Turn Turn) Duel(bool cursed)
    {
        var library = ContentFiles.Default;
        var field = new Battlefield(10, 6);

        var caster = new Creature("Caster", AbilityScores.All(14), 20, 5) { Allegiance = 1 };
        caster.Spells.CasterLevel = 5;
        caster.Spells.SetSlots(1, 4);
        caster.Spells.Prepare(library.GetSpell("cause-fear")!);
        caster.Spells.Prepare(library.GetSpell("magic-missile")!);

        var goblin = library.BuildCreature("goblin")!;
        if (cursed)
        {
            goblin.Effects.Apply(ConditionInfo.Effect(Condition.Shaken, Duration.Rounds(5)));
        }

        field.Place(caster, 0, 0);
        field.Place(goblin, 4, 0);   // 20 ft: inside Cause Fear's 35

        var battle = new Battle(
            [caster], [goblin], new SequenceRandom(true, 20, 1), rules: null, battlefield: field);

        return (
            new HeuristicActionSource(battle, new SequenceRandom(true, 1)),
            battle.Encounter.BeginNextTurn()!);
    }

    [Fact]
    public void AHeroWhoHasBeenCutDownIsNotHandedBackToThePlayer()
    {
        var battle = Scenarios.GoblinAmbush();

        // Open turns until it is Valeria's, then drop her where she stands.
        BattleTurn? turn;
        while ((turn = battle.BeginTurn()) is not null && turn.Actor.Name != "Valeria")
        {
            battle.EndTurn();
        }

        Assert.NotNull(turn);
        Assert.True(battle.NeedsPlayer);

        turn!.Actor.HitPoints.Take(turn.Actor.HitPoints.Maximum + 20);

        // Still the party's turn, still open — but there is nothing to decide, so asking the
        // player would mean making them click past a corpse once a round.
        Assert.True(battle.IsPartyTurn);
        Assert.False(battle.NeedsPlayer);
    }

    [Fact]
    public void NorIsOneWhoHasBeenStunned()
    {
        var battle = Scenarios.GoblinAmbush();
        var turn = battle.BeginTurn()!;

        Assert.True(battle.NeedsPlayer);

        turn.Actor.Effects.Apply(ConditionInfo.Effect(Condition.Stunned, Duration.Rounds(1)));

        Assert.False(battle.NeedsPlayer);
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
