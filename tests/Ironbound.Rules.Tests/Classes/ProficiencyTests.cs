using Ironbound.Rules.Abilities;
using Ironbound.Rules.Classes;
using Ironbound.Rules.Combat;
using Ironbound.Rules.Content;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Feats;
using Ironbound.Rules.Items;

namespace Ironbound.Rules.Tests.Classes;

public class WeaponProficiencyTests
{
    private static ContentLibrary Library => TestContent.Library;

    private static WeaponAttack Held(Creature creature, string item)
    {
        Library.Equip(creature, Library.GetItem(item) ?? new ItemDefinition
        {
            Id = item, Name = item, Slot = EquipmentSlot.MainHand, Weapon = item,
        });
        return creature.Attacks.Last(attack => !attack.IsThrownUse);
    }

    [Fact]
    public void AFighterIsTrainedWithEveryMartialWeapon()
    {
        var fighter = ClassKit.Make("fighter", 1);

        Assert.True(Proficiency.IsProficient(fighter, Held(fighter, "greataxe")));
        Assert.True(Proficiency.IsProficient(fighter, Held(fighter, "dagger")));
    }

    [Fact]
    public void AWizardIsTrainedWithHerFiveWeaponsAndNothingElse()
    {
        var wizard = ClassKit.Make("wizard", 1);

        Assert.True(Proficiency.IsProficient(wizard, Held(wizard, "quarterstaff")));
        Assert.True(Proficiency.IsProficient(wizard, Held(wizard, "light-crossbow")));
        Assert.True(Proficiency.IsProficient(wizard, Held(wizard, "club")));
        Assert.False(Proficiency.IsProficient(wizard, Held(wizard, "longsword")));
        Assert.False(Proficiency.IsProficient(wizard, Held(wizard, "spear")));
    }

    [Fact]
    public void ARogueHasHerFewMartialOnes()
    {
        var rogue = ClassKit.Make("rogue", 1);

        Assert.True(Proficiency.IsProficient(rogue, Held(rogue, "rapier")));
        Assert.True(Proficiency.IsProficient(rogue, Held(rogue, "shortbow")));
        Assert.False(Proficiency.IsProficient(rogue, Held(rogue, "longsword")));
    }

    [Fact]
    public void AClericIsTrainedWithHerGodsWeapon()
    {
        var iomedaean = ClassKit.Make("cleric", 1, "\"deity\": \"iomedae\"");
        var couatlan = ClassKit.Make("cleric", 1, "\"deity\": \"cihua-couatl\"");

        Assert.True(Proficiency.IsProficient(iomedaean, Held(iomedaean, "longsword")));
        Assert.False(Proficiency.IsProficient(couatlan, Held(couatlan, "longsword")));
        Assert.True(Proficiency.IsProficient(couatlan, Held(couatlan, "light-mace")));
    }

    [Fact]
    public void NobodyIsTrainedWithAnExoticWeaponWithoutTheFeat()
    {
        var fighter = ClassKit.Make("fighter", 1);
        var trained = ClassKit.Make("fighter", 1, "\"feats\": [\"exotic-weapon-proficiency:bastard-sword\"]");

        Assert.False(Proficiency.IsProficient(fighter, Held(fighter, "bastard-sword")));
        Assert.True(Proficiency.IsProficient(trained, Held(trained, "bastard-sword")));
        Assert.False(Proficiency.IsProficient(trained, Held(trained, "spiked-chain")));
    }

    [Fact]
    public void MartialWeaponProficiencyIsForOneWeapon()
    {
        var wizard = ClassKit.Make("wizard", 1, "\"feats\": [\"martial-weapon-proficiency:longsword\"]");

        Assert.True(Proficiency.IsProficient(wizard, Held(wizard, "longsword")));
        Assert.False(Proficiency.IsProficient(wizard, Held(wizard, "battleaxe")));
    }

    [Fact]
    public void SimpleWeaponProficiencyIsForAllOfThem()
    {
        var wizard = ClassKit.Make("wizard", 1, "\"feats\": [\"simple-weapon-proficiency\"]");

        Assert.True(Proficiency.IsProficient(wizard, Held(wizard, "spear")));
        Assert.True(Proficiency.IsProficient(wizard, Held(wizard, "morningstar")));
        Assert.False(Proficiency.IsProficient(wizard, Held(wizard, "longsword")));
    }

    [Fact]
    public void AMonsterKnowsSimpleWeaponsAndWhateverItsStatBlockCarries()
    {
        var ogre = Library.BuildCreature("ogre")!;

        Assert.True(Proficiency.IsProficient(ogre, ogre.MeleeAttack!));
        Assert.True(Proficiency.IsProficient(ogre, Held(ogre, "club")));
        Assert.False(Proficiency.IsProficient(ogre, Held(ogre, "longsword")));
    }

    [Fact]
    public void NaturalAttacksNeedNoTraining()
    {
        var werewolf = Library.BuildCreature("werewolf")!;

        Assert.True(Proficiency.IsProficient(werewolf, werewolf.PrimaryAttack!));
    }

    [Fact]
    public void SomethingBuiltByHandIsNeverAsked()
    {
        var someone = new Creature("Someone", AbilityScores.All(10), 10, 1);

        Assert.True(Proficiency.IsProficient(someone, WeaponAttack.Melee("club", "1d6", DamageType.Bludgeoning)));
        Assert.True(Proficiency.IsProficient(someone, Library.BuildWeapon("spiked-chain")!));
    }

    [Fact]
    public void FourOffEveryAttackRollAndTheBreakdownSaysWhy()
    {
        var wizard = ClassKit.Make("wizard", 1);
        var sword = Held(wizard, "longsword");

        var bonus = Strike.AttackBonus(wizard, sword);

        // Strength 14 and nothing else, less four.
        Assert.Equal(2 - 4, bonus.Total);
        Assert.Contains(bonus.Entries, entry => entry.Modifier.Source == "not proficient" && entry.Modifier.Value == -4);
        Assert.DoesNotContain(Strike.AttackBonus(wizard, Held(wizard, "dagger")).Entries,
            entry => entry.Modifier.Source == "not proficient");
    }

    [Fact]
    public void ATripMadeWithAnUntrainedWeaponIsWorseAndABullRushIsNot()
    {
        var wizard = ClassKit.Make("wizard", 1);
        var before = Maneuvers.Bonus(wizard, ManeuverKind.Trip).Total;

        Held(wizard, "longsword");

        Assert.Equal(before - 4, Maneuvers.Bonus(wizard, ManeuverKind.Trip).Total);
        Assert.Equal(before, Maneuvers.Bonus(wizard, ManeuverKind.BullRush).Total);
    }

    [Fact]
    public void TheOnlyWeaponTheFeatPicksIsOneTheCreatureCannotUse()
    {
        var wizard = ClassKit.Make("wizard", 1, "\"items\": [\"dagger\", \"longsword\"]");
        var feat = Library.GetFeat("martial-weapon-proficiency")!;

        Assert.Equal("martial-weapon-proficiency:longsword", ClassLevelling.ChooseFor(wizard, feat)!.Key);
        Assert.True(Proficiency.WouldHelp(wizard, feat));

        var fighter = ClassKit.Make("fighter", 1, "\"items\": [\"longsword\"]");
        Assert.Null(ClassLevelling.ChooseFor(fighter, feat));
        Assert.False(Proficiency.WouldHelp(fighter, feat));
    }

    [Fact]
    public void AnElfKnowsTheBowsAndBladesOfHerPeople()
    {
        var elf = ClassKit.Make("wizard", 1, "\"race\": \"elf\"");
        var human = ClassKit.Make("wizard", 1);

        Assert.True(Proficiency.IsProficient(elf, Held(elf, "shortbow")));
        Assert.True(Proficiency.IsProficient(elf, Held(elf, "composite-longbow")));
        Assert.True(Proficiency.IsProficient(elf, Held(elf, "rapier")));
        Assert.False(Proficiency.IsProficient(human, Held(human, "shortbow")));
    }

    [Fact]
    public void AnElvenWeaponIsMartialToAnElf()
    {
        var elvenFighter = ClassKit.Make("fighter", 1, "\"race\": \"elf\"");
        var humanFighter = ClassKit.Make("fighter", 1);
        var elvenWizard = ClassKit.Make("wizard", 1, "\"race\": \"elf\"");

        Assert.True(Proficiency.IsProficient(elvenFighter, Held(elvenFighter, "elven-curve-blade")));
        Assert.False(Proficiency.IsProficient(humanFighter, Held(humanFighter, "elven-curve-blade")));

        // Martial is still more than a wizard knows.
        Assert.False(Proficiency.IsProficient(elvenWizard, Held(elvenWizard, "elven-curve-blade")));
    }

    [Fact]
    public void SylwenNeedsHerRaceForHerBow()
    {
        var sylwen = Library.BuildCreature("sylwen")!;
        var bow = sylwen.Attacks.Single(attack => attack.Kind == "shortbow");

        Assert.True(Proficiency.IsProficient(sylwen, bow));

        sylwen.Race = null;
        Assert.False(Proficiency.IsProficient(sylwen, bow));
    }

    [Fact]
    public void EverybodyTheGameShipsIsTrainedWithWhatTheyCarry()
    {
        foreach (var id in Library.CreatureIds)
        {
            var creature = Library.BuildCreature(id)!;

            Assert.All(creature.Attacks, attack => Assert.True(Proficiency.IsProficient(creature, attack), $"{id}: {attack.Name}"));
            Assert.All(creature.Equipment.Items, item => Assert.True(Proficiency.IsProficient(creature, item), $"{id}: {item.Name}"));
        }
    }

    [Fact]
    public void AProficiencyFeatForTheWrongKindOfWeaponIsReported()
    {
        var library = ClassKit.Library(("odd.json", """
            { "kind": "creature", "id": "odd", "name": "Odd", "abilities": [10, 10, 10, 10, 10, 10],
              "classes": [ { "class": "fighter", "level": 2 } ],
              "feats": ["martial-weapon-proficiency:dagger", "exotic-weapon-proficiency:longsword"] }
            """));

        Assert.Contains(library.Problems, problem => problem.Message.Contains("dagger is simple"));
        Assert.Contains(library.Problems, problem => problem.Message.Contains("longsword is martial"));
    }

    [Fact]
    public void ExoticWeaponProficiencyWantsSomeSkillAtArmsFirst()
    {
        var feat = Library.GetFeat("exotic-weapon-proficiency")!;

        Assert.False(feat.AvailableTo(ClassKit.Make("wizard", 1)));
        Assert.True(feat.AvailableTo(ClassKit.Make("fighter", 1)));
    }

    [Fact]
    public void TheProficiencyFeatsAreCombatFeatsAFighterCanBeOffered()
    {
        foreach (var id in new[]
        {
            "simple-weapon-proficiency", "martial-weapon-proficiency", "exotic-weapon-proficiency",
            "armor-proficiency-light", "armor-proficiency-medium", "armor-proficiency-heavy",
            "shield-proficiency", "tower-shield-proficiency",
        })
        {
            Assert.True(Library.GetFeat(id)!.Combat, id);
        }

        Assert.Equal(FeatChoice.Weapon, Library.GetFeat("martial-weapon-proficiency")!.Takes);
        Assert.Equal(FeatChoice.Weapon, Library.GetFeat("exotic-weapon-proficiency")!.Takes);
    }
}

public class ArmourProficiencyTests
{
    private static ContentLibrary Library => TestContent.Library;

    [Theory]
    [InlineData("fighter", "breastplate", true)]
    [InlineData("fighter", "heavy-shield", true)]
    [InlineData("barbarian", "breastplate", true)]
    [InlineData("rogue", "leather-armour", true)]
    [InlineData("rogue", "breastplate", false)]
    [InlineData("rogue", "light-shield", false)]
    [InlineData("wizard", "chain-shirt", false)]
    [InlineData("cleric", "scale-mail", true)]
    public void EachClassWearsWhatItWasTrainedIn(string classId, string item, bool trained)
    {
        var creature = ClassKit.Make(classId, 1, classId == "cleric" ? "\"deity\": \"iomedae\"" : string.Empty);

        Assert.Equal(trained, Proficiency.IsProficient(creature, Library.GetItem(item)!));
    }

    [Fact]
    public void AnythingThatIsNotArmourNeedsNoTraining() =>
        Assert.True(Proficiency.IsProficient(ClassKit.Make("wizard", 1), Library.GetItem("ring-of-protection")!));

    [Fact]
    public void UntrainedArmourPutsItsCheckPenaltyOnEveryAttackRoll()
    {
        var wizard = ClassKit.Make("wizard", 1, "\"items\": [\"scale-mail\", \"quarterstaff\"]");

        var bonus = Strike.AttackBonus(wizard, wizard.MeleeAttack!);

        Assert.Contains(bonus.Entries, entry => entry.Modifier.Source == "not proficient with scale mail" && entry.Modifier.Value == -4);
        Assert.Equal(2 - 4, bonus.Total);
    }

    [Fact]
    public void AndOnManoeuvres()
    {
        var plain = ClassKit.Make("wizard", 1);
        var armoured = ClassKit.Make("wizard", 1, "\"items\": [\"scale-mail\"]");

        Assert.Equal(Maneuvers.Bonus(plain, ManeuverKind.BullRush).Total - 4, Maneuvers.Bonus(armoured, ManeuverKind.BullRush).Total);
    }

    [Fact]
    public void TheFeatTakesThePenaltyAway()
    {
        var wizard = ClassKit.Make("wizard", 1, "\"feats\": [\"armor-proficiency-light\"], \"items\": [\"chain-shirt\", \"quarterstaff\"]");

        Assert.True(Proficiency.IsProficient(wizard, Library.GetItem("chain-shirt")!));
        Assert.Equal(2, Strike.AttackBonus(wizard, wizard.MeleeAttack!).Total);
    }

    [Fact]
    public void TrainedArmourCostsNothingOnAttacks()
    {
        var fighter = ClassKit.Make("fighter", 1, "\"items\": [\"scale-mail\", \"longsword\"]");

        Assert.DoesNotContain(Strike.AttackBonus(fighter, fighter.MeleeAttack!).Entries,
            entry => entry.Modifier.Source.StartsWith("not proficient", StringComparison.Ordinal));
    }

    [Fact]
    public void AMonsterIsTrainedInWhatItWears()
    {
        var ogre = Library.BuildCreature("ogre")!;

        Assert.True(Proficiency.IsProficient(ogre, Library.GetItem("hide-armour")!));
        Assert.False(Proficiency.IsProficient(ogre, Library.GetItem("breastplate")!));
    }

    [Fact]
    public void ArmourFeatsAskForTheLighterTrainingFromAnywhere()
    {
        var medium = Library.GetFeat("armor-proficiency-medium")!;
        var tower = Library.GetFeat("tower-shield-proficiency")!;

        // A rogue's light armour comes from her class, not a feat, and still counts.
        Assert.True(medium.AvailableTo(ClassKit.Make("rogue", 1)));
        Assert.False(medium.AvailableTo(ClassKit.Make("wizard", 1)));
        Assert.True(medium.AvailableTo(ClassKit.Make("wizard", 1, "\"feats\": [\"armor-proficiency-light\"]")));

        Assert.True(tower.AvailableTo(ClassKit.Make("cleric", 1, "\"deity\": \"iomedae\"")));
        Assert.False(tower.AvailableTo(ClassKit.Make("rogue", 1)));
    }

    [Fact]
    public void ATowerShieldNeedsItsOwnTraining()
    {
        var tower = new ItemDefinition
        {
            Id = "tower", Name = "tower shield", Slot = EquipmentSlot.Shield,
            Armour = ArmourCategory.Shield, TowerShield = true, CheckPenalty = -10,
        };

        Assert.True(Proficiency.IsProficient(ClassKit.Make("fighter", 1), tower));
        Assert.False(Proficiency.IsProficient(ClassKit.Make("barbarian", 1), tower));
    }

    [Fact]
    public void AnArmourFeatIsOnlyPickedForSomebodyWhoNeedsIt()
    {
        var light = Library.GetFeat("armor-proficiency-light")!;

        Assert.True(Proficiency.WouldHelp(ClassKit.Make("wizard", 1, "\"items\": [\"chain-shirt\"]"), light));
        Assert.False(Proficiency.WouldHelp(ClassKit.Make("wizard", 1), light));
        Assert.False(Proficiency.WouldHelp(ClassKit.Make("fighter", 1, "\"items\": [\"chain-shirt\"]"), light));
    }

    [Fact]
    public void AFighterLeftToChooseNeverTakesAProficiencyFeatForNothing()
    {
        var fighter = ClassKit.Make("fighter", 1, "\"items\": [\"chain-shirt\", \"longsword\"]");

        for (var i = 0; i < 12 && ClassLevelling.DefaultFeat(fighter, Library, FeatureIds.CombatFeat) is { } feat; i++)
        {
            Assert.False(Proficiency.IsProficiencyFeat(feat.Effect), feat.Title);
            ClassFeatures.Take(fighter, feat);
        }
    }

    [Fact]
    public void TheSheetLineSaysWhatEachClassTrains()
    {
        Assert.Equal("Trained with simple and martial weapons; light armour, medium armour, heavy armour, shields and tower shields",
            Proficiency.Describe(ClassKit.Make("fighter", 1)));
        Assert.Equal("Trained with club, dagger, heavy crossbow, light crossbow and quarterstaff; no armour",
            Proficiency.Describe(ClassKit.Make("wizard", 1)));
        Assert.Equal(string.Empty, Proficiency.Describe(Library.BuildCreature("ogre")!));
    }
}
