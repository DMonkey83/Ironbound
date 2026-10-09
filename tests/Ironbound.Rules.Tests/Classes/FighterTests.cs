using Ironbound.Rules.Classes;
using Ironbound.Rules.Combat;
using Ironbound.Rules.Defense;
using Ironbound.Rules.Dice;
using Ironbound.Rules.Encounters;
using Ironbound.Rules.Encounters.Actions;
using Ironbound.Rules.Magic;
using Ironbound.Rules.Saves;
using Ironbound.Rules.Skills;

namespace Ironbound.Rules.Tests.Classes;

public class FighterTests
{
    private const string Longsword = "\"items\": [\"longsword\"]";

    [Fact]
    public void BraveryHelpsAgainstFearAndNothingElse()
    {
        var fighter = ClassKit.Make("fighter", 6, Longsword);
        var fear = TestContent.Library.GetSpell("cause-fear")!;
        var grease = TestContent.Library.GetSpell("grease")!;

        var against = ClassSaves.Against(fighter, fear, Save.Will).ToList();

        Assert.Equal(2, Assert.Single(against).Value);
        Assert.Empty(ClassSaves.Against(fighter, grease, Save.Reflex));
        Assert.Empty(ClassSaves.Against(fighter, fear, Save.Fortitude));
    }

    [Fact]
    public void BraveryIsCountedInTheSaveAgainstCauseFear()
    {
        var fighter = ClassKit.Make("fighter", 6, Longsword);
        var caster = ClassKit.Make("wizard", 1, "\"spells\": [\"cause-fear\"]");
        caster.Allegiance = 2;
        fighter.Allegiance = 1;

        // A 10 on the d20: Will +2 base +2 Wis, +2 bravery = 16 against DC 13.
        var cast = Casting.Resolve(
            caster, TestContent.Library.GetSpell("cause-fear")!, SpellAim.At(fighter), new SequenceRandom(10));

        var save = cast.Targets.Single().Save!;
        Assert.Contains(save.Bonus.Applied, entry => entry.Modifier.Source == "Bravery");
        Assert.Equal(16, save.Total);
    }

    [Fact]
    public void AFearDescriptorOrAShakingEffectBothCountAsFear()
    {
        var shaking = new Spell("scare", "Scare", 1, SpellSchool.Necromancy)
        {
            Does = [new Bestow(new Ironbound.Rules.Effects.EffectDefinition { Name = "Shaken", Condition = Ironbound.Rules.Conditions.Condition.Shaken })],
        };

        Assert.True(ClassSaves.IsFear(shaking));
        Assert.True(ClassSaves.IsFear(TestContent.Library.GetSpell("cause-fear")!));
        Assert.False(ClassSaves.IsFear(TestContent.Library.GetSpell("fireball")!));
    }

    [Fact]
    public void WeaponTrainingAddsToAttackAndDamageWithItsGroup()
    {
        var trained = ClassKit.Make("fighter", 5, $"{Longsword}, \"weaponTraining\": [\"heavy-blades\"]");
        var sword = trained.PrimaryAttack!;

        Assert.Equal(1, Martial.WeaponTraining(trained, sword));
        Assert.Contains(Strike.AttackBonus(trained, sword).Applied, entry => entry.Modifier.Source == "Weapon training");
        Assert.Contains(Strike.DamageBonus(trained, sword).Applied, entry => entry.Modifier.Source == "Weapon training");
    }

    [Fact]
    public void WeaponTrainingIsNothingOutsideItsGroup()
    {
        var trained = ClassKit.Make("fighter", 5, "\"items\": [\"shortbow\"], \"weaponTraining\": [\"heavy-blades\"]");

        Assert.Equal(0, Martial.WeaponTraining(trained, trained.PrimaryAttack!));
        Assert.DoesNotContain(Strike.AttackBonus(trained, trained.PrimaryAttack!).Applied,
            entry => entry.Modifier.Source == "Weapon training");
    }

    [Fact]
    public void TheFirstGroupGrowsWhenASecondIsTrained()
    {
        var veteran = ClassKit.Make("fighter", 9, "\"weaponTraining\": [\"heavy-blades\", \"bows\"]");

        Assert.Equal(2, Martial.WeaponTraining(veteran, "heavy-blades"));
        Assert.Equal(1, Martial.WeaponTraining(veteran, "bows"));
        Assert.Equal(0, Martial.WeaponTraining(veteran, "axes"));
    }

    [Fact]
    public void AGroupChosenAheadOfTheLevelDoesNothingYet()
    {
        var early = ClassKit.Make("fighter", 4, "\"weaponTraining\": [\"heavy-blades\"]");

        Assert.Equal(0, Martial.WeaponTraining(early, "heavy-blades"));
        Assert.Empty(Martial.TrainedGroups(early));
    }

    [Fact]
    public void AFighterWhoseFileSaysNothingIsGivenTheGroupOfWhatSheHolds()
    {
        var fighter = ClassKit.Make("fighter", 5, "\"items\": [\"greataxe\"]");

        Assert.Equal(["axes"], fighter.Choices.WeaponGroups);
    }

    [Fact]
    public void WeaponTrainingHelpsATripMadeWithTheWeapon()
    {
        var trained = ClassKit.Make("fighter", 5, $"{Longsword}, \"weaponTraining\": [\"heavy-blades\"]");

        Assert.Equal(
            Maneuvers.Bonus(trained).Total + 1,
            Maneuvers.Bonus(trained, ManeuverKind.Trip).Total);

        // A bull rush is made with the body, not the blade.
        Assert.Equal(Maneuvers.Bonus(trained).Total, Maneuvers.Bonus(trained, ManeuverKind.BullRush).Total);
    }

    [Fact]
    public void ArmourTrainingEasesTheArmoursPenaltyAndNotTheShields()
    {
        var untrained = ClassKit.Make("fighter", 2, "\"items\": [\"scale-mail\", \"heavy-shield\"]");
        var trained = ClassKit.Make("fighter", 3, "\"items\": [\"scale-mail\", \"heavy-shield\"]");

        Assert.Equal(-6, Martial.CheckPenalty(untrained));
        Assert.Equal(-5, Martial.CheckPenalty(trained));
    }

    [Fact]
    public void ArmourTrainingRaisesTheDexterityCap()
    {
        var nimble = new[] { 14, 20, 14, 10, 10, 10 };
        var untrained = ClassKit.Make("fighter", 2, "\"items\": [\"breastplate\"]", nimble);
        var trained = ClassKit.Make("fighter", 7, "\"items\": [\"breastplate\"]", nimble);

        Assert.Equal(3, untrained.ArmorClass.MaxDexterityBonus);
        Assert.Equal(5, trained.ArmorClass.MaxDexterityBonus);
        Assert.Equal(untrained.ArmorClass.Total + 2, trained.ArmorClass.Total);
    }

    [Fact]
    public void MediumArmourSlowsAnUntrainedFighterAndNotATrainedOne()
    {
        Assert.Equal(20, ClassKit.Make("fighter", 2, "\"items\": [\"scale-mail\"]").CurrentSpeed);
        Assert.Equal(30, ClassKit.Make("fighter", 3, "\"items\": [\"scale-mail\"]").CurrentSpeed);
    }

    [Fact]
    public void HeavyArmourWaitsForTheSecondStep()
    {
        var plate = ("full-plate.json", """
            { "kind": "item", "id": "full-plate", "name": "full plate", "slot": "Armour",
              "armour": "Heavy", "maxDex": 1, "checkPenalty": -6,
              "grants": [ { "target": "armourClass", "value": 9, "type": "Armor" } ] }
            """);

        Assert.Equal(20, ClassKit.Make("fighter", 6, "\"items\": [\"full-plate\"]", null, plate).CurrentSpeed);
        Assert.Equal(30, ClassKit.Make("fighter", 7, "\"items\": [\"full-plate\"]", null, plate).CurrentSpeed);
    }

    [Fact]
    public void ArmourPenaltyLandsOnStrengthAndDexteritySkillsOnly()
    {
        var hale = TestContent.Library.BuildCreature("hale")!;

        Assert.Contains(hale.Skills.Explain(Skill.Climb).Applied,
            entry => entry.Modifier.Source == "Armour check penalty" && entry.Modifier.Value == -6);
        Assert.DoesNotContain(hale.Skills.Explain(Skill.Heal).Applied,
            entry => entry.Modifier.Source == "Armour check penalty");
    }

    [Fact]
    public void TakingTheArmourOffTakesThePenaltyAndTheCapWithIt()
    {
        var fighter = ClassKit.Make("fighter", 1, "\"items\": [\"scale-mail\"]");

        Assert.Equal(3, fighter.ArmorClass.MaxDexterityBonus);
        Assert.True(fighter.Equipment.Unequip("scale-mail"));

        Assert.Null(fighter.ArmorClass.MaxDexterityBonus);
        Assert.Equal(0, Martial.CheckPenalty(fighter));
        Assert.Equal(30, fighter.CurrentSpeed);
    }

    [Fact]
    public void ReducedSpeedsFollowTheArmourTable()
    {
        Assert.Equal(15, Martial.Reduced(20));
        Assert.Equal(20, Martial.Reduced(30));
        Assert.Equal(30, Martial.Reduced(40));
        Assert.Equal(5, Martial.Reduced(5));
    }

    [Fact]
    public void WeaponFocusIsOneBetterWithItsWeaponOnly()
    {
        var focused = ClassKit.Make("fighter", 1, "\"items\": [\"longsword\", \"shortbow\"], \"feats\": [\"weapon-focus:longsword\"]");
        var sword = focused.Attacks.Single(weapon => weapon.Kind == "longsword");
        var bow = focused.Attacks.Single(weapon => weapon.Kind == "shortbow");

        Assert.Contains(Strike.AttackBonus(focused, sword).Applied, entry => entry.Modifier.Source == "Weapon Focus");
        Assert.DoesNotContain(Strike.AttackBonus(focused, bow).Applied, entry => entry.Modifier.Source == "Weapon Focus");
        Assert.Equal("Weapon Focus (longsword)", focused.Feats.Single(feat => feat.Id == "weapon-focus").Title);
    }

    [Fact]
    public void ShieldFocusNeedsAShieldAndIsNoUseAgainstATouch()
    {
        var withShield = ClassKit.Make("fighter", 1, "\"items\": [\"heavy-shield\"], \"feats\": [\"shield-focus\"]");
        var without = ClassKit.Make("fighter", 1, "\"feats\": [\"shield-focus\"]");
        var plain = ClassKit.Make("fighter", 1, "\"items\": [\"heavy-shield\"]");

        Assert.Equal(plain.ArmorClass.Total + 1, withShield.ArmorClass.Total);
        Assert.Equal(plain.ArmorClass.Touch, withShield.ArmorClass.Touch);
        Assert.Equal(ClassKit.Make("fighter", 1).ArmorClass.Total, without.ArmorClass.Total);
        Assert.Contains("Shield Focus", withShield.ArmorClass.Explain().ToString());
    }

    [Fact]
    public void ValeriaIsWhatTheSpecSaysSheIs()
    {
        var valeria = TestContent.Library.BuildCreature("valeria")!;

        Assert.Equal(2, Martial.Bravery(valeria));
        Assert.Equal(1, Martial.ArmorTraining(valeria));
        Assert.Equal(1, Martial.WeaponTraining(valeria, valeria.PrimaryAttack!));
        Assert.True(Martial.HasFeatFor(valeria, Ironbound.Rules.Feats.FeatEffect.WeaponFocus, valeria.PrimaryAttack!));
        Assert.Contains(ClassFeatures.Describe(valeria), line => line.Name == "Weapon training" && line.Detail == "heavy blades +1");
    }

    [Fact]
    public void TheHobgoblinSergeantTrainsWithHisLongswordToo()
    {
        var sergeant = TestContent.Library.BuildCreature("hobgoblin-sergeant")!;

        Assert.Equal(1, Martial.WeaponTraining(sergeant, sergeant.PrimaryAttack!));
    }

    [Fact]
    public void AShippedLongswordKnowsWhatItIs()
    {
        var silver = TestContent.Library.BuildItemWeapon(TestContent.Library.GetItem("silvered-longsword")!)!;

        Assert.Equal("silvered longsword", silver.Name);
        Assert.Equal("longsword", silver.Kind);
        Assert.Equal(["heavy-blades"], silver.Groups);
        Assert.False(silver.Finesse);
        Assert.True(TestContent.Library.BuildWeapon("short-sword")!.Finesse);
    }

    [Fact]
    public void ACombatFeatIsMarkedAsOne()
    {
        string[] combat =
        [
            "power-attack", "combat-expertise", "combat-reflexes", "dodge", "improved-bull-rush",
            "improved-trip", "improved-initiative", "weapon-specialization", "weapon-focus",
            "weapon-finesse", "shield-focus", "improved-critical", "point-blank-shot",
            "precise-shot", "rapid-shot", "vital-strike", "cleave",
        ];
        string[] general = ["great-fortitude", "iron-will", "lightning-reflexes", "toughness", "selective-channeling", "empower-spell"];

        Assert.All(combat, id => Assert.True(TestContent.Library.GetFeat(id)!.Combat, id));
        Assert.All(general, id => Assert.False(TestContent.Library.GetFeat(id)!.Combat, id));
        Assert.True(TestContent.Library.GetFeat("empower-spell")!.Metamagic);
    }

    [Fact]
    public void TradingStrikesTheOldWayStillWorksForTheUntrained()
    {
        // A creature built by hand with no classes is exactly what it was before class features.
        var a = ClassKit.Dummy("A");
        var b = ClassKit.Dummy("B");
        var encounter = ClassKit.Fight([a], [b], ClassKit.Field((a, 1, 1), (b, 2, 1)), 15, 4);
        var turn = encounter.BeginNextTurn()!;

        var result = Assert.IsType<AttackActionResult>(turn.Take(new AttackAction(a.PrimaryAttack!, b)));

        Assert.True(result.Strike!.IsHit);
        Assert.Equal(0, result.Strike.SneakAttackDice);
        Assert.Empty(result.Strike.Notes);
    }
}
