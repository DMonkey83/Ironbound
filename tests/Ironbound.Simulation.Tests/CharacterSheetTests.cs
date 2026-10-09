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
            [
                "Who", "Abilities", "Defence", "Saving throws", "Attacks", "Class features", "Skills",
                "Gear and training", "Magic", "Conditions",
            ],
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

        // This is what the whole Explain() habit was for: not "AC 20" but why. (Nineteen before
        // class features gave her the Shield Focus a fighter 6 is owed.)
        Assert.Contains(defence, line => line.Contains("Armour class 20"));
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

        Assert.Equal(20, valeria.ArmorClass.Total);
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
        var fireball = merrin.Spells.Prepared.First(spell => spell.Level == 3);

        // An evocation comes out of her school slot first, then the general ones.
        merrin.Spells.Spend(fireball);
        Assert.Contains(Section(merrin, "Magic"), line => line.Contains("Level 3 slots: 2 of 2, school 0 of 1"));

        merrin.Spells.Spend(fireball);
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

public class ClassFeatureSheetTests
{
    private static IReadOnlyList<string> Features(string id) =>
        CharacterSheet.Of(ContentFiles.Default.BuildCreature(id)!).Single(section => section.Heading == "Class features").Lines;

    [Fact]
    public void HaleNamesHerGodAndCountsHerChannels()
    {
        var lines = Features("hale");

        Assert.Equal("Deity: Cihua Couatl (NG), favoured weapon shortspear", lines[0]);
        Assert.Contains("Domains: Healing, War", lines);
        Assert.Contains("Channel Positive Energy: 4 of 4 left today", lines);
        Assert.Contains("Rebuke Death: 5 of 5 left today", lines);
    }

    [Fact]
    public void KarnsRageIsCountedInRounds()
    {
        var lines = Features("karn");

        Assert.Contains("Rage: 17 of 17 rounds left", lines);
        Assert.Contains("Rage powers: powerful blow, surprise accuracy, strength surge", lines);
    }

    [Fact]
    public void TheBondedSpellsAreOneAllowance()
    {
        var lines = Features("merrin");

        Assert.Contains(lines, line => line.StartsWith("Bonded object: 1 of 1 left today — Fireball, Scorching Ray"));
        Assert.Contains("Force Missile: 7 of 7 left today", lines);
        Assert.Contains("Intense spells: +2 damage from evocation spells", lines);
    }

    [Fact]
    public void AFighterHasFeaturesAndNoPools()
    {
        var lines = Features("valeria");

        Assert.Contains("Bravery: +2 Will against fear", lines);
        Assert.DoesNotContain(lines, line => line.Contains("left today"));
    }

    [Fact]
    public void AMonsterWithNoClassHasNothingHere()
    {
        Assert.Empty(Features("ogre"));
        Assert.DoesNotContain("Class features", CharacterSheet.Describe(ContentFiles.Default.BuildCreature("ogre")!));
    }

    [Fact]
    public void ADomainSlotIsShownBesideTheGeneralOnes()
    {
        var magic = CharacterSheet.Of(ContentFiles.Default.BuildCreature("hale")!).Single(section => section.Heading == "Magic").Lines;

        Assert.Contains("Level 1 slots: 2 of 2, domain 1 of 1", magic);
    }

    [Fact]
    public void AFeatTakenForAWeaponSaysWhichWeapon()
    {
        var gear = CharacterSheet.Of(ContentFiles.Default.BuildCreature("valeria")!).Single(section => section.Heading == "Gear and training").Lines;

        Assert.Contains(gear, line => line.StartsWith("Weapon Focus (longsword)"));
    }
}

public class ClassFeaturesInPlayTests
{
    [Fact]
    public void KarnRagesInTheAmbush()
    {
        var battle = Scenarios.GoblinAmbush();
        battle.RunToCompletion(Scenarios.AutoPilot(battle));

        Assert.Contains(battle.Log, line => line.Contains("Karn flies into a rage"));
    }

    [Fact]
    public void AcrossManySeedsEveryCasterAndTheRogueUseWhatTheyHave()
    {
        var used = new HashSet<string>();

        for (var seed = 1UL; seed <= 30; seed++)
        {
            foreach (var id in new[] { "cave-mouth", "orc-lair", "guard-post", "ogre-den" })
            {
                var battle = Scenarios.Build(ContentFiles.Default, id, seed);
                battle.RunToCompletion(Scenarios.AutoPilot(battle, seed), 400);

                foreach (var line in battle.Log)
                {
                    if (line.Contains("sneak attack")) used.Add("sneak attack");
                    if (line.Contains("uses Acid Dart")) used.Add("acid dart");
                    if (line.Contains("uses Channel Positive Energy")) used.Add("channel");
                }
            }
        }

        Assert.Equal(["acid dart", "channel", "sneak attack"], used.Order());
    }
}
