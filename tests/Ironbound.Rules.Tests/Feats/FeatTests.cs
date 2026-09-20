using Ironbound.Rules.Abilities;
using Ironbound.Rules.Combat;
using Ironbound.Rules.Content;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Dice;
using Ironbound.Rules.Encounters;
using Ironbound.Rules.Encounters.Actions;
using Ironbound.Rules.Feats;
using Ironbound.Rules.Maps;
using Ironbound.Rules.Modifiers;
using Ironbound.Rules.Persistence;
using Ironbound.Rules.Saves;
using Ironbound.Rules.Tests.Content;

namespace Ironbound.Rules.Tests.Feats;

public class FeatsFromFilesTests
{
    [Fact]
    public void AFeatThatIsOnlyNumbersNeedsNoCodeAtAll()
    {
        var library = ContentParsingTests.From("""
            { "kind": "feat", "id": "dodge", "name": "Dodge",
              "description": "+1 dodge bonus to armour class.",
              "grants": [ { "target": "armourClass", "value": 1, "type": "Dodge" } ] }
            """);

        var dodge = Assert.IsType<FeatDefinition>(library.GetFeat("dodge"));

        Assert.Equal("Dodge", dodge.Name);
        Assert.Equal(FeatEffect.None, dodge.Effect);

        var grant = Assert.Single(dodge.Grants);
        Assert.Equal(BonusType.Dodge, grant.Type);
    }

    [Fact]
    public void TakingItHandsOverTheBonusUnderTheFeatsOwnName()
    {
        var creature = Fighter();
        TestContent.Library.GetFeat("dodge")!.ApplyTo(creature);

        Assert.Equal(1, creature.ArmorClass.Modifiers.Total);
        Assert.Contains("Dodge", creature.ArmorClass.Modifiers.ToString());
    }

    [Fact]
    public void ACreatureCitingAFeatThatDoesNotExistIsReported()
    {
        var library = ContentLibrary.Load([
            ("hero.json", """
                { "kind": "creature", "id": "hero", "name": "Hero",
                  "abilities": [10, 10, 10, 10, 10, 10], "feats": ["power-attack"] }
                """),
        ]);

        var problem = Assert.Single(library.Problems);
        Assert.Equal("feats", problem.Field);
        Assert.Contains("power-attack", problem.Message);
    }

    [Fact]
    public void ACreatureBuiltFromAFileComesOutWithItsFeatsApplied()
    {
        var library = ContentParsingTests.From(
            """
            { "kind": "feat", "id": "iron-will", "name": "Iron Will",
              "grants": [ { "target": "save.Will", "value": 2, "type": "Untyped" } ] }
            """,
            """
            { "kind": "creature", "id": "monk", "name": "Monk",
              "abilities": [10, 10, 10, 10, 10, 10], "feats": ["iron-will"] }
            """);

        var monk = library.BuildCreature("monk")!;

        Assert.True(monk.HasFeat("iron-will"));
        Assert.Equal(2, monk.Saves[Save.Will].Modifiers.Total);
    }

    [Fact]
    public void AnUnknownEffectNameIsReportedRatherThanSilentlyDoingNothing()
    {
        var library = ContentLibrary.Load([
            ("feat.json", """{ "kind": "feat", "id": "x", "name": "X", "effect": "Telekinesis" }"""),
        ]);

        // The whole reason the identifier is an enum: a typo here would otherwise be a feat
        // that quietly did nothing for the rest of the game.
        Assert.Equal("effect", Assert.Single(library.Problems).Field);
    }

    internal static Creature Fighter(string name = "Fighter", int dexterity = 14, int allegiance = 1) =>
        new(name, new AbilityScores(16, dexterity, 14, 10, 10, 10), 50, 6)
        {
            Allegiance = allegiance,
            BaseAttackBonus = 6,
        };
}

public class CombatReflexesTests
{
    [Fact]
    public void ItBuysAnOpportunityPerPointOfDexterity()
    {
        var fighter = FeatsFromFilesTests.Fighter(dexterity: 18);

        Assert.Equal(1, fighter.AttacksOfOpportunityPerRound);

        fighter.Feats.Add(TestContent.Library.GetFeat("combat-reflexes")!);

        Assert.Equal(5, fighter.AttacksOfOpportunityPerRound);   // one, plus four
    }

    [Fact]
    public void ItKeepsUpWithADexterityThatChanges()
    {
        var fighter = FeatsFromFilesTests.Fighter(dexterity: 18);
        fighter.Feats.Add(TestContent.Library.GetFeat("combat-reflexes")!);

        fighter.Abilities[Ability.Dexterity].Modifiers.Add(-6, BonusType.Untyped, "Poison");

        // Derived rather than stored, so the poison takes the extra swings away again.
        Assert.Equal(2, fighter.AttacksOfOpportunityPerRound);
    }

    [Fact]
    public void ClumsinessNeverCostsYouTheOneEverybodyGets()
    {
        var fighter = FeatsFromFilesTests.Fighter(dexterity: 6);
        fighter.Feats.Add(TestContent.Library.GetFeat("combat-reflexes")!);

        Assert.Equal(1, fighter.AttacksOfOpportunityPerRound);
    }

    [Fact]
    public void TheExtraOpportunitiesAreActuallyTaken()
    {
        var field = new Battlefield(12, 4);
        var guard = FeatsFromFilesTests.Fighter("Guard", dexterity: 18, allegiance: 2);
        guard.Attacks.Add(WeaponAttack.Melee("longsword", "1d8", DamageType.Slashing));
        guard.Feats.Add(TestContent.Library.GetFeat("combat-reflexes")!);

        var runner = FeatsFromFilesTests.Fighter("Runner");

        // Beside the route rather than across it: you cannot walk through somebody.
        field.Place(guard, 3, 1);
        field.Place(runner, 0, 0);

        var encounter = new Encounter(
            [runner, guard],
            new SequenceRandom(true, 20, 1, 5, 5, 5, 5, 5, 5),
            rules: null,
            battlefield: field);
        var turn = encounter.BeginNextTurn()!;

        // Running the length of the guard's reach leaves three threatened squares behind, so
        // three swings for anyone who can afford them and one for anyone who cannot.
        var path = field.FindPath(new GridSquare(0, 0), new GridSquare(6, 0), runner);
        var result = Assert.IsType<MoveActionResult>(turn.Take(new MoveAction(path)));

        Assert.True(result.Opportunities.Count > 1);
        Assert.Equal(guard, result.Opportunities[0].Attacker);
    }
}

public class ImprovedManeuverTests
{
    [Fact]
    public void PractisingATripIsWorthTwoOnTheCheck()
    {
        var fighter = FeatsFromFilesTests.Fighter();
        var bare = Maneuvers.Bonus(fighter, ManeuverKind.Trip).Total;

        fighter.Feats.Add(TestContent.Library.GetFeat("improved-trip")!);

        Assert.Equal(bare + 2, Maneuvers.Bonus(fighter, ManeuverKind.Trip).Total);

        // And only for the maneuver it was practised on.
        Assert.Equal(bare, Maneuvers.Bonus(fighter, ManeuverKind.BullRush).Total);
    }

    [Fact]
    public void AndTheFreeSwingItWouldHaveCostYou()
    {
        var (turn, karn, goblin) = Skirmish(withFeat: false);
        var exposed = Assert.IsType<ManeuverActionResult>(turn.Take(new TripAction(goblin)));

        Assert.Single(exposed.Opportunities);

        var (guarded, practised, other) = Skirmish(withFeat: true);
        var safe = Assert.IsType<ManeuverActionResult>(guarded.Take(new TripAction(other)));

        // The reason the feat is worth a slot: reaching in no longer opens your guard.
        Assert.Empty(safe.Opportunities);
        Assert.NotNull(practised);
    }

    [Fact]
    public void ShovingIsImprovedByItsOwnFeatAndNotTheOtherOne()
    {
        var fighter = FeatsFromFilesTests.Fighter();
        fighter.Feats.Add(TestContent.Library.GetFeat("improved-bull-rush")!);

        Assert.Equal(FeatEffect.ImprovedBullRush, Maneuvers.ImprovedBy(ManeuverKind.BullRush));
        Assert.True(fighter.HasFeat(FeatEffect.ImprovedBullRush));
        Assert.False(fighter.HasFeat(FeatEffect.ImprovedTrip));
    }

    private static (Turn Turn, Creature Karn, Creature Goblin) Skirmish(bool withFeat)
    {
        var field = new Battlefield(10, 4);
        var karn = FeatsFromFilesTests.Fighter("Karn");
        var goblin = FeatsFromFilesTests.Fighter("Goblin", allegiance: 2);

        karn.Attacks.Add(WeaponAttack.Melee("greataxe", "1d12", DamageType.Slashing));
        goblin.Attacks.Add(WeaponAttack.Melee("scimitar", "1d6", DamageType.Slashing));

        if (withFeat)
        {
            karn.Feats.Add(TestContent.Library.GetFeat("improved-trip")!);
        }

        field.Place(karn, 0, 0);
        field.Place(goblin, 1, 0);

        int[] scripted = withFeat ? [20, 1, 18] : [20, 1, 12, 4, 18];

        var encounter = new Encounter(
            [karn, goblin], new SequenceRandom(true, scripted), rules: null, battlefield: field);

        return (encounter.BeginNextTurn()!, karn, goblin);
    }
}

public class ShippedFeatTests
{
    [Theory]
    [InlineData("dodge")]
    [InlineData("combat-reflexes")]
    [InlineData("improved-trip")]
    [InlineData("improved-bull-rush")]
    [InlineData("improved-initiative")]
    [InlineData("iron-will")]
    [InlineData("great-fortitude")]
    [InlineData("lightning-reflexes")]
    [InlineData("weapon-specialization")]
    public void EveryFeatTheContentNeedsIsThere(string id) =>
        Assert.NotNull(TestContent.Library.GetFeat(id));

    [Fact]
    public void MostOfThemAreNothingButNumbers()
    {
        var pure = TestContent.Library.FeatIds
            .Select(id => TestContent.Library.GetFeat(id)!)
            .Count(feat => feat.Effect == FeatEffect.None);

        // The point of the design: only a minority earn an entry in the engine.
        Assert.True(pure > TestContent.Library.FeatIds.Count - pure);
    }

    [Fact]
    public void ValeriaGetsWhatHerFeatsPromise()
    {
        var valeria = TestContent.Library.BuildCreature("valeria")!;

        Assert.Equal(19, valeria.ArmorClass.Total);              // Dodge
        Assert.Equal(3, valeria.AttacksOfOpportunityPerRound);   // Combat Reflexes, Dex 14
        Assert.Equal(2, valeria.DamageModifiers.Total);          // Weapon Specialization
        Assert.True(valeria.HasFeat(FeatEffect.ImprovedTrip));
    }

    [Fact]
    public void FeatsSurviveASaveAndAreNotAppliedTwiceOnTheWayBack()
    {
        var valeria = TestContent.Library.BuildCreature("valeria")!;
        var armour = valeria.ArmorClass.Total;

        var encounter = new Encounter([valeria], new SequenceRandom(true, 10));
        var restored = GameSave
            .Restore(GameSave.FromJson(GameSave.ToJson(GameSave.Capture(encounter))), TestContent.Library)
            .Order.Single().Creature;

        // The bonuses ride back in the modifier stacks like any other; re-applying the feats
        // here would quietly double every one of them.
        Assert.Equal(armour, restored.ArmorClass.Total);
        Assert.True(restored.HasFeat(FeatEffect.ImprovedTrip));
        Assert.Equal(3, restored.AttacksOfOpportunityPerRound);
    }
}
