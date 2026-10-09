using Ironbound.Rules.Abilities;
using Ironbound.Rules.Classes;
using Ironbound.Rules.Combat;
using Ironbound.Rules.Content;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Dice;
using Ironbound.Rules.Encounters;
using Ironbound.Rules.Encounters.Actions;
using Ironbound.Rules.Magic;
using Ironbound.Rules.Maps;

namespace Ironbound.Simulation.Tests;

/// <summary>Experience by challenge rating, shared out among the party, and what exploring is worth.</summary>
public class ChallengeExperienceTests
{
    [Fact]
    public void AFightIsWorthItsFoesRatingsSharedAmongTheParty()
    {
        var run = Campaign.Begin(ChainContent.Library, "caves-of-shadow");
        var opening = run.Experience;

        foreach (var foe in run.Battle.Foes)
        {
            foe.HitPoints.Take(foe.HitPoints.Maximum + 100);
        }

        run.Collect();

        // Two orcs at CR 1/3 are 270 between them, and a party of four takes 67 each.
        Assert.Equal(opening + 67, run.Experience);
    }

    [Fact]
    public void TheLongRoadsAmbushIsWorthTheSergeantMostly()
    {
        var run = Campaign.Begin(ChainContent.Library, "the-long-road");
        var opening = run.Experience;

        foreach (var foe in run.Battle.Foes)
        {
            foe.HitPoints.Take(foe.HitPoints.Maximum + 100);
        }

        run.Collect();

        // Three goblins at 135 and a fighter 6 at CR 5's 1,600: 2,005, split three ways.
        Assert.Equal(opening + 668, run.Experience);
    }

    [Fact]
    public void ExploringALevelIsWorthAboutAThirdOfItsFights()
    {
        var caves = ContentFiles.Default.GetLevel("caves-of-shadow")!;
        var road = ContentFiles.Default.GetLevel("the-long-road")!;

        Assert.Equal(155, caves.Areas.Sum(area => area.Experience) + caves.Features.Sum(feature => feature.Experience));
        Assert.Equal(290, road.Areas.Sum(area => area.Experience) + road.Features.Sum(feature => feature.Experience));

        // The fights: 468 a head in the caves, 868 on the road. A third of each, near enough.
        Assert.InRange(155 * 3, 468 - 30, 468 + 30);
        Assert.InRange(290 * 3, 868 - 30, 868 + 30);
    }

    [Fact]
    public void NeitherShippedPartyLevelsBeforeItsCampaignEnds()
    {
        // Every foe beaten and every room and door and cache found: the most either campaign can pay.
        var caves = Campaign.Begin(ChainContent.Library, "caves-of-shadow");
        var road = Campaign.Begin(ChainContent.Library, "the-long-road");

        Assert.True(caves.Experience + 468 + 155 < caves.NextLevelAt);
        Assert.True(road.Experience + 868 + 290 < road.NextLevelAt);
    }
}

/// <summary>Levels taken in a campaign: the ability increase and the favoured-class bonus, chosen or not.</summary>
public class CampaignLevelChoiceTests
{
    [Fact]
    public void LeftToItselfTheFourthLevelRaisesTheKeyAbilityAndTheFavouredClassAddsAHitPoint()
    {
        var (run, hero, library) = LevelReady.For("fighter", 3);
        var strength = hero.Abilities[Ability.Strength].Base;
        var plain = ContentLibrary.Load(ContentFiles.Read(Path.Combine(AppContext.BaseDirectory, "content")).Append(("p.json", """
            { "kind": "creature", "id": "p", "name": "P", "abilities": [16, 14, 14, 14, 14, 14],
              "classes": [ { "class": "fighter", "level": 3 } ] }
            """))).BuildCreature("p")!;
        Levelling.Gain(plain, library.GetClass("fighter")!);

        Assert.True(run.LevelUp(hero));

        Assert.Equal(strength + 1, hero.Abilities[Ability.Strength].Base);
        Assert.Equal(plain.HitPoints.Maximum + 1, hero.HitPoints.Maximum);
    }

    [Fact]
    public void TheChoicesCanBeMadeAndAreRefusedWhereTheyDoNotApply()
    {
        var (run, hero, library) = LevelReady.For("wizard", 3);
        var wizard = library.GetClass("wizard")!;

        Assert.True(run.NeedsFor(hero, wizard).AbilityIncrease);
        Assert.True(run.LevelUp(hero, wizard, null, new LevelChoices(AbilityIncrease: Ability.Dexterity, Favoured: FavouredClassBonus.SkillRank)));

        Assert.Equal(15, hero.Abilities[Ability.Dexterity].Base);
        Assert.Equal(1, hero.Skills.Ranks(hero.Skills.Trained.First()));

        var (again, other, _) = LevelReady.For("wizard", 2);
        Assert.False(again.LevelUp(other, wizard, null, new LevelChoices(AbilityIncrease: Ability.Dexterity)));
        Assert.Contains("raises no ability score", again.LevelRefusal);
    }
}

/// <summary>What the level-up screen lists under "not available".</summary>
public class WithheldFeatTests
{
    [Fact]
    public void EveryFeatSheCannotTakeIsListedWithItsReason()
    {
        var run = Campaign.Begin(ChainContent.Library, "the-long-road");
        var merrin = run.Party.Single(one => one.Name == "Merrin");

        var withheld = run.WithheldFeats(merrin).ToDictionary(entry => entry.Feat.Id, entry => entry.Why);

        Assert.StartsWith("needs two-weapon fighting", withheld["two-weapon-fighting"]);
        Assert.Equal("needs Str 13", withheld["power-attack"]);
        Assert.DoesNotContain("improved-initiative", withheld.Keys);
        Assert.DoesNotContain("dodge", withheld.Keys);
        Assert.Contains("extra-rage", withheld.Keys);

        // Between them, the two lists are every feat she does not already have.
        var offered = run.FeatsFor(merrin).Select(feat => feat.Id).ToHashSet();
        Assert.Empty(offered.Intersect(withheld.Keys));
        Assert.Equal(
            ChainContent.Library.FeatIds.Count,
            offered.Count + withheld.Count + merrin.Feats.Select(feat => feat.Id).Distinct().Count());
    }
}

/// <summary>The autopilot's use of the new rules: casting defensively, stepping away, Deadly Aim.</summary>
public class FeatAutopilotTests
{
    private static (Battle Battle, HeuristicActionSource Source) Fight(
        IReadOnlyList<(Creature Creature, int X, int Y)> party,
        IReadOnlyList<(Creature Creature, int X, int Y)> foes,
        params GridSquare[] walls)
    {
        var field = new Battlefield(16, 16);
        foreach (var wall in walls)
        {
            field.Block(wall);
        }

        foreach (var (creature, x, y) in party.Concat(foes))
        {
            field.Place(creature, x, y);
        }

        var battle = new Battle(
            party.Select(one => one.Creature), foes.Select(one => one.Creature), new SequenceRandom(true, 10), battlefield: field);

        return (battle, new HeuristicActionSource(battle, new SequenceRandom(true, 1)));
    }

    private static Turn TurnOf(Battle battle, Creature creature)
    {
        for (var tries = 0; tries < 20; tries++)
        {
            var turn = battle.Encounter.BeginNextTurn()!;
            if (ReferenceEquals(turn.Actor, creature))
            {
                return turn;
            }
        }

        throw new InvalidOperationException($"{creature.Name} never had a turn.");
    }

    [Fact]
    public void AThreatenedCasterWhoseCheckIsGoodEnoughCastsDefensively()
    {
        var merrin = ContentFiles.Default.BuildCreature("merrin")!;
        var orc = ContentFiles.Default.BuildCreature("orc")!;
        var (battle, source) = Fight([(merrin, 5, 5)], [(orc, 6, 5)]);

        var action = source.NextAction(TurnOf(battle, merrin));

        // Wizard 5 with Intelligence 18: nine against a first-level spell's seventeen, 65 in 100.
        var cast = Assert.IsType<CastSpellAction>(action);
        Assert.True(cast.Defensively);
    }

    [Fact]
    public void OneWhoseCheckIsPoorCastsAsUsualAndTakesTheSwing()
    {
        // Wizard 1 with Intelligence 17: four against seventeen, 40 in 100. Not good enough.
        var sylwen = ContentFiles.Default.BuildCreature("sylwen")!;
        var orc = ContentFiles.Default.BuildCreature("orc")!;
        var (battle, source) = Fight([(sylwen, 5, 5)], [(orc, 6, 5)]);

        var action = source.NextAction(TurnOf(battle, sylwen));

        var cast = Assert.IsType<CastSpellAction>(action);
        Assert.False(cast.Defensively);
    }

    [Fact]
    public void ACasterNobodyThreatensCastsAsUsual()
    {
        var merrin = ContentFiles.Default.BuildCreature("merrin")!;
        var orc = ContentFiles.Default.BuildCreature("orc")!;
        var (battle, source) = Fight([(merrin, 2, 5)], [(orc, 9, 5)]);

        var action = source.NextAction(TurnOf(battle, merrin));

        Assert.False(Assert.IsType<CastSpellAction>(action).Defensively);
    }

    [Fact]
    public void AnArcherWithDeadlyAimTakesItAgainstSomethingEasyToHit()
    {
        var library = ContentLibrary.Load(ContentFiles.Read(Path.Combine(AppContext.BaseDirectory, "content")).Append(("archer.json", """
            { "kind": "creature", "id": "archer", "name": "Archer", "abilities": [12, 18, 12, 10, 10, 10],
              "classes": [ { "class": "fighter", "level": 4 } ], "items": ["shortbow"], "feats": ["deadly-aim"] }
            """)));
        var archer = library.BuildCreature("archer")!;
        var goblin = ContentFiles.Default.BuildCreature("goblin")!;
        var (battle, source) = Fight([(archer, 2, 5)], [(goblin, 9, 5)]);

        Assert.IsType<FullAttackAction>(source.NextAction(TurnOf(battle, archer)));
        Assert.True(archer.Stances.IsActive(Stance.DeadlyAim));
    }
}
