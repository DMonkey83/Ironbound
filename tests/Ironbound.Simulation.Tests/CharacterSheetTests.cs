using Ironbound.Rules.Combat;
using Ironbound.Rules.Conditions;
using Ironbound.Rules.Effects;
using Ironbound.Rules.Modifiers;

namespace Ironbound.Simulation.Tests;

public class CharacterSheetTests
{
    [Fact]
    public void EverySectionIsThereEvenWhenEmpty()
    {
        var sheet = CharacterSheet.Of(ContentFiles.Default.BuildCreature("valeria")!);

        Assert.Equal(
            ["Who", "Abilities", "Defence", "Saving throws", "Attacks", "Gear and training", "Magic", "Conditions"],
            sheet.Select(section => section.Heading));
    }

    [Fact]
    public void TheWrittenFormLeavesOutWhatIsEmpty()
    {
        var written = CharacterSheet.Describe(ContentFiles.Default.BuildCreature("valeria")!);

        // A fighter has no magic, and a blank heading is worse than none.
        Assert.DoesNotContain("Magic", written);
        Assert.Contains("Fighter 6", written);
    }

    [Fact]
    public void ADerivedNumberAppearsBesideThePartsItWasMadeOf()
    {
        var valeria = ContentFiles.Default.BuildCreature("valeria")!;
        var defence = Section(valeria, "Defence");

        // This is what the whole Explain() habit was for: not "AC 19" but why.
        Assert.Contains(defence, line => line.Contains("Armour class 19"));
        Assert.Contains(defence, line => line.Contains("chain shirt") && line.Contains("heavy shield"));
        Assert.Contains(defence, line => line.Contains("Dodge"));
    }

    [Fact]
    public void ATotalNeverDisagreesWithItsOwnBreakdown()
    {
        var valeria = ContentFiles.Default.BuildCreature("valeria")!;

        Assert.Equal(Maneuvers.Defense(valeria), Maneuvers.DefenseBonus(valeria).Total);
        Assert.Contains(
            Section(valeria, "Defence"),
            line => line.StartsWith("Manoeuvre defence 23") && line.EndsWith("= 23"));
    }

    [Fact]
    public void ASuppressedBonusIsStillShown()
    {
        var valeria = ContentFiles.Default.BuildCreature("valeria")!;

        // A second, smaller armour bonus does not stack — and the sheet should say so rather
        // than quietly omitting it, because "where did my ring go?" is the question.
        valeria.ArmorClass.Modifiers.Add(2, BonusType.Armor, "Bracers");

        Assert.Equal(19, valeria.ArmorClass.Total);
        Assert.Contains(Section(valeria, "Defence"), line => line.Contains("Bracers"));
    }

    [Fact]
    public void ACasterGetsSlotsAndSaveDifficulties()
    {
        var magic = Section(ContentFiles.Default.BuildCreature("merrin")!, "Magic");

        Assert.Contains(magic, line => line.Contains("Caster level 5"));
        Assert.Contains(magic, line => line.Contains("Level 3 slots: 2 of 2"));
        Assert.Contains(magic, line => line.Contains("Fireball") && line.Contains("DC 17"));
    }

    [Fact]
    public void SpentSlotsShowAsSpent()
    {
        var merrin = ContentFiles.Default.BuildCreature("merrin")!;
        merrin.Spells.Spend(merrin.Spells.Prepared.First(spell => spell.Level == 3));

        Assert.Contains(Section(merrin, "Magic"), line => line.Contains("Level 3 slots: 1 of 2"));
    }

    [Fact]
    public void ARangedWeaponSaysHowFarRatherThanHowClose()
    {
        var attacks = Section(ContentFiles.Default.BuildCreature("goblin-archer")!, "Attacks");

        Assert.Contains(attacks, line => line.Contains("shortbow") && line.Contains("range 60 ft"));
        Assert.Contains(attacks, line => line.Contains("scimitar") && line.Contains("reach 5 ft"));
    }

    [Fact]
    public void WhatIsWrongWithSomebodyIsListedWithHowLongItLasts()
    {
        var karn = ContentFiles.Default.BuildCreature("karn")!;
        karn.Effects.Apply(ConditionInfo.Effect(Condition.Shaken, Duration.Rounds(3)));

        var afflictions = Section(karn, "Conditions");

        Assert.Contains(afflictions, line => line == "Shaken");
        Assert.Contains(afflictions, line => line.Contains("remaining"));
    }

    [Fact]
    public void GearAndTrainingCoversBothWornAndStowed()
    {
        var gear = Section(ContentFiles.Default.BuildCreature("goblin-archer")!, "Gear and training");

        Assert.Contains(gear, line => line.Contains("shortbow") && line.Contains("MainHand"));
        Assert.Contains(gear, line => line.Contains("scimitar") && line.Contains("stowed"));
    }

    [Fact]
    public void FeatsAreListedWithWhatTheyDo()
    {
        var gear = Section(ContentFiles.Default.BuildCreature("valeria")!, "Gear and training");

        Assert.Contains(gear, line => line.StartsWith("Combat Reflexes —"));
    }

    [Fact]
    public void SomethingWithDamageReductionSaysSo()
    {
        var defence = Section(ContentFiles.Default.BuildCreature("werewolf")!, "Defence");

        Assert.Contains(defence, line => line.Contains("Damage reduction") && line.Contains("silver"));
    }

    [Fact]
    public void ItRefusesNothingRatherThanReturningNonsense() =>
        Assert.Throws<ArgumentNullException>(() => CharacterSheet.Of(null!));

    private static IReadOnlyList<string> Section(Rules.Creatures.Creature creature, string heading) =>
        CharacterSheet.Of(creature).Single(section => section.Heading == heading).Lines;
}
