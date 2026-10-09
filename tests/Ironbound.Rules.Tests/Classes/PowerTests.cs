using Ironbound.Rules.Classes;
using Ironbound.Rules.Content;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Dice;
using Ironbound.Rules.Encounters;
using Ironbound.Rules.Encounters.Actions;
using Ironbound.Rules.Magic;

namespace Ironbound.Rules.Tests.Classes;

public class PowerTests
{
    [Fact]
    public void PowersComeInAStableOrder()
    {
        var hale = TestContent.Library.BuildCreature("hale")!;

        Assert.Equal(hale.Powers.Select(power => power.Id), hale.Powers.Select(power => power.Id));
        Assert.Equal(["channel-energy", "rebuke-death", "battle-rage"], hale.Powers.Select(power => power.Id));
    }

    [Fact]
    public void TheyAreRebuiltSoTheyGrowWithTheLevel()
    {
        var cleric = ClassKit.Make("cleric", 2, "\"deity\": \"none\"");
        var before = Assert.IsType<Restore>(cleric.Powers.First().Effect.Does.Single()).Amount.ToString();

        Levelling.Gain(cleric, TestContent.Library.GetClass("cleric")!);

        Assert.Equal("1d6", before);
        Assert.Equal("2d6", Assert.IsType<Restore>(cleric.Powers.First().Effect.Does.Single()).Amount.ToString());
    }

    [Fact]
    public void AFighterHasNoPowersAndADummyNone()
    {
        Assert.Empty(TestContent.Library.BuildCreature("valeria")!.Powers);
        Assert.Empty(ClassKit.Dummy().Powers);
    }

    [Fact]
    public void UsingAPowerIsReportedAsASpellIsWithThePowersName()
    {
        var hale = TestContent.Library.BuildCreature("hale")!;
        var encounter = new Encounter([hale], new SequenceRandom(true, 3));

        var result = encounter.BeginNextTurn()!.Take(UsePowerAction.Self(hale.Powers.First()));

        var cast = Assert.IsType<CastSpellResult>(result);
        Assert.Equal("Channel Positive Energy", cast.Action.Name);
        Assert.Equal("Channel Positive Energy", cast.Cast!.Spell.Name);
        Assert.Equal("uses", cast.Cast.Verb);
    }

    [Fact]
    public void APowerNoLongerHeldCannotBeUsed()
    {
        var hale = TestContent.Library.BuildCreature("hale")!;
        var stranger = TestContent.Library.BuildCreature("valeria")!;
        var encounter = new Encounter([stranger], new SequenceRandom(true, 3));

        Assert.False(encounter.BeginNextTurn()!.CanTake(UsePowerAction.Self(hale.Powers.First())));
    }

    [Fact]
    public void ASpellLikePowerProvokesAndASupernaturalOneDoesNot()
    {
        var merrin = TestContent.Library.BuildCreature("merrin")!;
        var foe = ClassKit.Dummy("Foe", hitPoints: 100);
        var field = ClassKit.Field((merrin, 1, 1), (foe, 2, 1));
        var encounter = ClassKit.Fight([merrin], [foe], field, 1, 1, 1);
        var missile = merrin.Powers.Single(power => power.Id == "force-missile");

        // The foe swings first (1: a miss), then the missile's d4 is the next 1.
        var result = Assert.IsType<CastSpellResult>(encounter.BeginNextTurn()!.Take(UsePowerAction.At(missile, foe)));

        Assert.Single(result.Opportunities);
    }

    [Fact]
    public void ARestRefillsEveryPool()
    {
        var merrin = TestContent.Library.BuildCreature("merrin")!;
        var missile = merrin.Powers.Single(power => power.Id == "force-missile");
        merrin.DailyUses.Spend("force-missile", 3);
        merrin.DailyUses.Spend(ClassPowers.ArcaneBondPool);

        ClassFeatures.Rest(merrin);

        Assert.Equal(7, merrin.UsesLeft(missile));
        Assert.Empty(merrin.DailyUses.Spent);
    }

    [Fact]
    public void AnUnknownPoolHoldsNothing()
    {
        var merrin = TestContent.Library.BuildCreature("merrin")!;

        Assert.Equal(0, ClassPowers.PerDay(merrin, "wishes"));
        Assert.Equal(0, ClassPowers.Left(merrin, "wishes"));
    }

    [Fact]
    public void PowersHaveSomethingToSayForThemselves()
    {
        var merrin = TestContent.Library.BuildCreature("merrin")!;

        Assert.All(merrin.Powers, power => Assert.False(string.IsNullOrWhiteSpace(power.Description)));
        Assert.Equal("Force Missile", merrin.Powers.First().ToString());
    }
}

public class DescribeTests
{
    [Fact]
    public void EachClassReadsAsTheSpecWroteIt()
    {
        var pip = ClassFeatures.Describe(TestContent.Library.BuildCreature("pip")!);
        var karn = ClassFeatures.Describe(TestContent.Library.BuildCreature("karn")!);
        var valeria = ClassFeatures.Describe(TestContent.Library.BuildCreature("valeria")!);

        Assert.Contains(new FeatureLine("Sneak attack", "+1d6"), pip);
        Assert.Contains(new FeatureLine("Rage", "17 of 17 rounds left"), karn);
        Assert.Contains(new FeatureLine("Weapon training", "heavy blades +1"), valeria);
        Assert.Contains(new FeatureLine("Rage powers", "powerful blow, surprise accuracy, strength surge"), karn);
        Assert.Contains(new FeatureLine("Fast movement", "+10 ft"), karn);
    }

    [Fact]
    public void ALiveRageSaysSo()
    {
        var karn = TestContent.Library.BuildCreature("karn")!;
        Rage.Start(karn);

        Assert.Contains(new FeatureLine("Rage", "16 of 17 rounds left, raging"), ClassFeatures.Describe(karn));
    }

    [Fact]
    public void ALineReadsAsANameAndADetail()
    {
        Assert.Equal("Sneak attack: +1d6", new FeatureLine("Sneak attack", "+1d6").ToString());
        Assert.Equal("Uncanny dodge", new FeatureLine("Uncanny dodge", string.Empty).ToString());
    }

    [Fact]
    public void EveryFeatureIsOneLineHoweverManyRowsItHas()
    {
        var rogue = ClassFeatures.Describe(ClassKit.Make("rogue", 9));

        Assert.Single(rogue, line => line.Name == "Sneak attack");
        Assert.Contains(new FeatureLine("Sneak attack", "+5d6"), rogue);
        Assert.Contains(new FeatureLine("Trap sense", "+3 Reflex and AC against traps"), rogue);
    }

    [Fact]
    public void RecordOnlyFeaturesSaySo()
    {
        var sylwen = ClassFeatures.Describe(TestContent.Library.BuildCreature("sylwen")!);
        var hale = ClassFeatures.Describe(TestContent.Library.BuildCreature("hale")!);

        Assert.Contains(sylwen, line => line.Name == "Summoner's charm" && line.Detail.Contains("recorded only"));
        Assert.Contains(sylwen, line => line.Name == "Scribe Scroll");
        Assert.Contains(sylwen, line => line.Name == "Cantrips");
        Assert.Contains(hale, line => line.Name == "Orisons");
        Assert.Contains(hale, line => line.Name == "Aura" && line.Detail.StartsWith("NG"));
    }
}

public class ClassLevellingTests
{
    private static ContentLibrary Library => TestContent.Library;

    [Fact]
    public void AFightersEvenLevelOffersACombatFeat()
    {
        var fighter = ClassKit.Make("fighter", 1, "\"items\": [\"longsword\"]", [16, 14, 14, 10, 10, 10]);

        var needs = ClassLevelling.NeedsFor(fighter, Library.GetClass("fighter")!, Library);

        Assert.NotEmpty(needs.BonusFeats);
        Assert.All(needs.BonusFeats, feat => Assert.True(feat.Combat));
        Assert.DoesNotContain(needs.BonusFeats, feat => feat.Id == "toughness");
        Assert.Empty(needs.WeaponGroups);
        Assert.Empty(needs.Talents);
    }

    [Fact]
    public void AFightersFifthOffersAWeaponGroup()
    {
        var fighter = ClassKit.Make("fighter", 4);

        var needs = ClassLevelling.NeedsFor(fighter, Library.GetClass("fighter")!, Library);

        Assert.Empty(needs.BonusFeats);
        Assert.Contains("heavy-blades", needs.WeaponGroups);
        Assert.Equal(Martial.WeaponGroups.Count, needs.WeaponGroups.Count);
    }

    [Fact]
    public void ARoguesSecondOffersTalentsButNotAdvancedOnes()
    {
        var rogue = ClassKit.Make("rogue", 1);

        var needs = ClassLevelling.NeedsFor(rogue, Library.GetClass("rogue")!, Library);

        Assert.Contains(needs.Talents, talent => talent.Id == "bleeding-attack");
        Assert.DoesNotContain(needs.Talents, talent => talent.Id == "crippling-strike");
        Assert.DoesNotContain(needs.Talents, talent => talent.Id == "powerful-blow");
    }

    [Fact]
    public void ARoguesTenthOffersTheAdvancedOnesToo()
    {
        var rogue = ClassKit.Make("rogue", 9);

        var needs = ClassLevelling.NeedsFor(rogue, Library.GetClass("rogue")!, Library);

        Assert.Contains(needs.Talents, talent => talent.Id == "crippling-strike");
    }

    [Fact]
    public void AWizardsFifthOffersAWizardsFeatAndHerSixthNothing()
    {
        var fourth = ClassKit.Make("wizard", 4);
        var fifth = ClassKit.Make("wizard", 5);

        Assert.Equal(["empower-spell"], ClassLevelling.NeedsFor(fourth, Library.GetClass("wizard")!, Library).BonusFeats.Select(feat => feat.Id));
        Assert.False(ClassLevelling.NeedsFor(fifth, Library.GetClass("wizard")!, Library).Any);
    }

    [Fact]
    public void AnotherClassesLevelOffersThatClasssChoices()
    {
        var wizard = ClassKit.Make("wizard", 4);

        // Her first level of fighter, not her fifth of anything: a combat feat.
        var needs = ClassLevelling.NeedsFor(wizard, Library.GetClass("fighter")!, Library);

        Assert.NotEmpty(needs.BonusFeats);
        Assert.Equal(1, ClassLevelling.NextLevelIn(wizard, Library.GetClass("fighter")!));
    }

    [Fact]
    public void NothingChosenMeansSomethingSensibleIsPicked()
    {
        var barbarian = ClassKit.Make("barbarian", 1, "\"items\": [\"greataxe\"]");

        var (choices, refusal) = ClassLevelling.Resolve(barbarian, Library.GetClass("barbarian")!, new LevelChoices(), Library);

        Assert.Null(refusal);
        Assert.Equal("powerful-blow", choices!.Talent);
    }

    [Fact]
    public void AFighterWithNothingChosenTakesWeaponFocusInHerBlade()
    {
        var fighter = ClassKit.Make("fighter", 1, "\"items\": [\"longsword\"]", [16, 14, 14, 10, 10, 10]);

        var (choices, _) = ClassLevelling.Resolve(fighter, Library.GetClass("fighter")!, new LevelChoices(), Library);

        Assert.Equal("weapon-focus:longsword", choices!.BonusFeat!.Key);
    }

    [Fact]
    public void AChoiceTheLevelDoesNotOfferIsRefused()
    {
        var fighter = ClassKit.Make("fighter", 1);
        var fighterClass = Library.GetClass("fighter")!;

        Assert.NotNull(ClassLevelling.Resolve(fighter, fighterClass, new LevelChoices(Talent: "bleeding-attack"), Library).Refusal);
        Assert.NotNull(ClassLevelling.Resolve(fighter, fighterClass, new LevelChoices(WeaponGroup: "bows"), Library).Refusal);
        Assert.NotNull(ClassLevelling.Resolve(fighter, fighterClass, new LevelChoices(Library.GetFeat("iron-will")), Library).Refusal);

        var third = ClassKit.Make("fighter", 2);
        Assert.NotNull(ClassLevelling.Resolve(third, fighterClass, new LevelChoices(Library.GetFeat("dodge")), Library).Refusal);
    }

    [Fact]
    public void ACombatTrickBuysACombatFeat()
    {
        var rogue = ClassKit.Make("rogue", 1);
        var rogueClass = Library.GetClass("rogue")!;

        var (choices, refusal) = ClassLevelling.Resolve(
            rogue, rogueClass, new LevelChoices(Library.GetFeat("dodge"), Talent: "combat-trick"), Library);

        Assert.Null(refusal);
        Assert.Equal("dodge", choices!.BonusFeat!.Id);

        var (_, notCombat) = ClassLevelling.Resolve(
            rogue, rogueClass, new LevelChoices(Library.GetFeat("iron-will"), Talent: "combat-trick"), Library);
        Assert.NotNull(notCombat);
    }

    [Fact]
    public void ApplyingTakesEverything()
    {
        var fighter = ClassKit.Make("fighter", 4, "\"items\": [\"longsword\"]");
        var fighterClass = Library.GetClass("fighter")!;
        var (choices, _) = ClassLevelling.Resolve(fighter, fighterClass, new LevelChoices(WeaponGroup: "heavy-blades"), Library);

        Levelling.Gain(fighter, fighterClass);
        ClassLevelling.Apply(fighter, choices!, Library);

        Assert.Equal(1, Martial.WeaponTraining(fighter, fighter.PrimaryAttack!));
    }
}
