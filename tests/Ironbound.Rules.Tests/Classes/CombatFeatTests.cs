using Ironbound.Rules.Classes;
using Ironbound.Rules.Combat;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Dice;
using Ironbound.Rules.Encounters;
using Ironbound.Rules.Encounters.Actions;
using Ironbound.Rules.Feats;

namespace Ironbound.Rules.Tests.Classes;

public class CombatFeatTests
{
    [Fact]
    public void ImprovedCriticalDoublesTheThreatRangeOfItsWeapon()
    {
        var keen = ClassKit.Make("fighter", 8, "\"items\": [\"longsword\", \"greataxe\"], \"feats\": [\"improved-critical:longsword\"]");
        var sword = keen.Attacks.Single(weapon => weapon.Kind == "longsword");
        var axe = keen.Attacks.Single(weapon => weapon.Kind == "greataxe");

        Assert.Equal(17, Martial.Critical(keen, sword).ThreatsOn);
        Assert.Equal(20, Martial.Critical(keen, axe).ThreatsOn);

        // A 17 threatens now; the 15 confirms.
        var strike = Strike.Resolve(keen, sword, ClassKit.Dummy(), new SequenceRandom(17, 15, 4, 4));
        Assert.True(strike.IsCritical);
    }

    [Fact]
    public void WithoutTheFeatASeventeenIsAnOrdinaryHit()
    {
        var plain = ClassKit.Make("fighter", 8, "\"items\": [\"longsword\"]");

        var strike = Strike.Resolve(plain, plain.PrimaryAttack!, ClassKit.Dummy(), new SequenceRandom(17, 4));

        Assert.True(strike.IsHit);
        Assert.False(strike.Attack.Threatened);
    }

    [Fact]
    public void PointBlankShotIsForCloseShotsOnly()
    {
        var archer = ClassKit.Make("fighter", 1, "\"items\": [\"shortbow\"], \"feats\": [\"point-blank-shot\"]");
        var near = ClassKit.Dummy("Near");
        var far = ClassKit.Dummy("Far");
        var field = ClassKit.Field((archer, 0, 0), (near, 6, 0), (far, 0, 7));
        var bow = archer.PrimaryAttack!;

        Assert.Equal(
            Strike.AttackBonus(archer, bow, far, field).Total + 1,
            Strike.AttackBonus(archer, bow, near, field).Total);
        Assert.Equal(1, Strike.DamageBonus(archer, bow, field.DistanceInFeet(archer, near)).Total);
        Assert.Equal(0, Strike.DamageBonus(archer, bow, field.DistanceInFeet(archer, far)).Total);
    }

    [Fact]
    public void PreciseShotIgnoresTheMeleeYourFriendsAreIn()
    {
        var careless = ClassKit.Make("fighter", 2, "\"items\": [\"shortbow\"], \"feats\": [\"point-blank-shot\"]");
        var precise = ClassKit.Make("fighter", 2, "\"items\": [\"shortbow\"], \"feats\": [\"point-blank-shot\", \"precise-shot\"]");
        var friend = ClassKit.Dummy("Friend");
        var foe = ClassKit.Dummy("Foe");
        var field = ClassKit.Field((careless, 0, 0), (precise, 0, 2), (friend, 5, 1), (foe, 6, 1));
        careless.Allegiance = precise.Allegiance = friend.Allegiance = 1;
        foe.Allegiance = 2;

        Assert.Contains(Strike.AttackBonus(careless, careless.PrimaryAttack!, foe, field).Applied,
            entry => entry.Modifier.Source == "Firing into melee");
        Assert.DoesNotContain(Strike.AttackBonus(precise, precise.PrimaryAttack!, foe, field).Applied,
            entry => entry.Modifier.Source == "Firing into melee");
    }

    [Fact]
    public void PreciseShotWantsPointBlankShotFirst()
    {
        var precise = TestContent.Library.GetFeat("precise-shot")!;

        Assert.False(precise.AvailableTo(ClassKit.Make("fighter", 1)));
        Assert.True(precise.AvailableTo(ClassKit.Make("fighter", 1, "\"feats\": [\"point-blank-shot\"]")));
    }

    [Fact]
    public void RapidShotIsAnExtraShotWithEveryShotTwoWorse()
    {
        var quick = ClassKit.Make("fighter", 6, "\"items\": [\"shortbow\"], \"feats\": [\"point-blank-shot\", \"rapid-shot\"]");
        var slow = ClassKit.Make("fighter", 6, "\"items\": [\"shortbow\", \"longsword\"]");

        Assert.Equal([-2, -2, -7], FullAttackAction.Penalties(quick, quick.PrimaryAttack!));
        Assert.Equal([0, -5], FullAttackAction.Penalties(slow, slow.PrimaryAttack!));

        // And a sword is no faster for it.
        var swordsman = ClassKit.Make("fighter", 6, "\"items\": [\"longsword\"], \"feats\": [\"point-blank-shot\", \"rapid-shot\"]");
        Assert.Equal([0, -5], FullAttackAction.Penalties(swordsman, swordsman.PrimaryAttack!));
    }

    [Fact]
    public void VitalStrikeRollsTheDiceTwiceOnASingleAttack()
    {
        var vital = ClassKit.Make("fighter", 6, "\"items\": [\"greatsword\"], \"feats\": [\"vital-strike\"]", [14, 10, 10, 10, 10, 10]);
        var dummy = ClassKit.Dummy(hitPoints: 100);
        var encounter = ClassKit.Fight([vital], [dummy], ClassKit.Field((vital, 1, 1), (dummy, 2, 1)), 10, 3, 3, 4, 4);

        var result = Assert.IsType<AttackActionResult>(encounter.BeginNextTurn()!.Take(new AttackAction(vital.PrimaryAttack!, dummy)));

        // 2d6 of 3 and 3, Strength 14 two-handed is +3 and weapon training in heavy blades +1,
        // then 2d6 again of 4 and 4.
        Assert.True(result.Strike!.Vital);
        Assert.Equal(6 + 3 + 1 + 8, result.Strike.Damage!.Total);
        Assert.Contains("vital strikes", result.Strike.ToString());
    }

    [Fact]
    public void VitalStrikeDiceAreNotMultipliedOnACritical()
    {
        var vital = ClassKit.Make("fighter", 6, "\"items\": [\"greatsword\"], \"feats\": [\"vital-strike\"]", [10, 10, 10, 10, 10, 10]);

        var strike = Strike.Resolve(vital, vital.PrimaryAttack!, ClassKit.Dummy(hitPoints: 100), new SequenceRandom(20, 15, 1, 1, 1, 1, 6, 6), vital: true);

        // Twice the weapon's 2d6 and its weapon training, then the vital strike's own 2d6 once.
        Assert.True(strike.IsCritical);
        Assert.Equal(((2 + 1) * 2) + 12, strike.Damage!.Total);
    }

    [Fact]
    public void CleaveCarriesOnIntoTheNextFoeAndCostsTwoArmour()
    {
        var cleaver = ClassKit.Make("fighter", 1, "\"items\": [\"greatsword\"], \"feats\": [\"power-attack\", \"cleave\"]", [16, 10, 10, 10, 10, 10]);
        var first = ClassKit.Dummy("First");
        var second = ClassKit.Dummy("Second");
        var field = ClassKit.Field((cleaver, 1, 1), (first, 2, 1), (second, 2, 2));
        var armour = cleaver.ArmorClass.Total;
        var encounter = ClassKit.Fight([cleaver], [first, second], field, 15, 3, 3, 15, 3, 3);

        var result = Assert.IsType<FullAttackResult>(encounter.BeginNextTurn()!.Take(new CleaveAction(first)));

        Assert.Equal([first, second], result.Strikes.Select(strike => strike.Target));
        Assert.Equal(armour - 2, cleaver.ArmorClass.Total);
    }

    [Fact]
    public void AMissedCleaveStopsThere()
    {
        var cleaver = ClassKit.Make("fighter", 1, "\"items\": [\"greatsword\"], \"feats\": [\"power-attack\", \"cleave\"]", [16, 10, 10, 10, 10, 10]);
        var first = ClassKit.Dummy("First", armour: 20);
        var second = ClassKit.Dummy("Second");
        var field = ClassKit.Field((cleaver, 1, 1), (first, 2, 1), (second, 2, 2));
        var encounter = ClassKit.Fight([cleaver], [first, second], field, 5);

        var result = Assert.IsType<FullAttackResult>(encounter.BeginNextTurn()!.Take(new CleaveAction(first)));

        Assert.Single(result.Strikes);
    }

    [Fact]
    public void CleaveWantsTheFeat()
    {
        var plain = ClassKit.Make("fighter", 1, "\"items\": [\"greatsword\"]");
        var foe = ClassKit.Dummy();
        var encounter = ClassKit.Fight([plain], [foe], ClassKit.Field((plain, 1, 1), (foe, 2, 1)));

        Assert.False(encounter.BeginNextTurn()!.CanTake(new CleaveAction(foe)));
    }

    [Fact]
    public void ToughnessIsThreeHitPointsOrOneADie()
    {
        var young = ClassKit.Make("fighter", 1);
        var tough = ClassKit.Make("fighter", 1, "\"feats\": [\"toughness\"]");
        var veteran = ClassKit.Make("fighter", 6);
        var toughVeteran = ClassKit.Make("fighter", 6, "\"feats\": [\"toughness\"]");

        Assert.Equal(young.HitPoints.Maximum + 3, tough.HitPoints.Maximum);
        Assert.Equal(veteran.HitPoints.Maximum + 6, toughVeteran.HitPoints.Maximum);
    }

    [Fact]
    public void ToughnessKeepsGrowingWithTheLevels()
    {
        var tough = ClassKit.Make("fighter", 3, "\"feats\": [\"toughness\"]");
        var before = tough.HitPoints.Maximum;

        Levelling.Gain(tough, TestContent.Library.GetClass("fighter")!);

        // Six for the fighter level, two for Constitution, and one more for Toughness.
        Assert.Equal(before + 6 + 2 + 1, tough.HitPoints.Maximum);
    }

    [Fact]
    public void AFeatTakenForAWeaponCanBeTakenAgainForAnother()
    {
        var focus = TestContent.Library.GetFeat("weapon-focus")!;
        var fighter = ClassKit.Make("fighter", 2, "\"items\": [\"longsword\", \"shortbow\"], \"feats\": [\"weapon-focus:longsword\"]");

        Assert.Equal(FeatChoice.Weapon, focus.Takes);
        Assert.True(focus.AvailableTo(fighter));
        Assert.False((focus with { Choice = "longsword" }).AvailableTo(fighter));
        Assert.True((focus with { Choice = "shortbow" }).AvailableTo(fighter));
        Assert.Equal("weapon-focus:shortbow", ClassLevelling.ChooseFor(fighter, focus)!.Key);
    }

    [Fact]
    public void AChoiceNamingAWeaponThatDoesNotExistIsReported()
    {
        var library = ClassKit.Library(("bad.json", """
            { "kind": "creature", "id": "bad", "name": "Bad", "abilities": [10, 10, 10, 10, 10, 10],
              "feats": ["weapon-focus:lightsabre", "dodge:longsword"] }
            """));

        Assert.Contains(library.Problems, problem => problem.Message.Contains("lightsabre"));
        Assert.Contains(library.Problems, problem => problem.Message.Contains("not taken for anything"));
    }
}
