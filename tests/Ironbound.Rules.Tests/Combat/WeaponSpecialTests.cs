using Ironbound.Rules.Abilities;
using Ironbound.Rules.Classes;
using Ironbound.Rules.Combat;
using Ironbound.Rules.Conditions;
using Ironbound.Rules.Content;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Defense;
using Ironbound.Rules.Dice;
using Ironbound.Rules.Encounters;
using Ironbound.Rules.Encounters.Actions;
using Ironbound.Rules.Items;
using Ironbound.Rules.Maps;
using Ironbound.Rules.Modifiers;
using Ironbound.Rules.Tests.Classes;

namespace Ironbound.Rules.Tests.Combat;

/// <summary>Shared ways of arming somebody with whatever the catalogue has.</summary>
internal static class Arms
{
    public static ContentLibrary Library => TestContent.Library;

    public static ItemDefinition Item(string weapon) => Library.GetItem(weapon) ?? new ItemDefinition
    {
        Id = weapon, Name = weapon.Replace('-', ' '), Slot = EquipmentSlot.MainHand, Weapon = weapon,
    };

    /// <summary>Strength 10 and nothing else, so every number is the weapon's own.</summary>
    public static Creature Holding(string weapon, string name = "Wielder")
    {
        var creature = new Creature(name, AbilityScores.All(10), 30, 2);
        Library.Equip(creature, Item(weapon));
        return creature;
    }

    public static Creature Target(string name = "Target", int hitPoints = 60) =>
        new(name, AbilityScores.All(10), hitPoints, 2);
}

public class DamageTypeChoiceTests
{
    [Fact]
    public void APOrSWeaponIsSwungAsWhicheverGetsThrough()
    {
        var dagger = Arms.Library.BuildWeapon("dagger")!;
        var pierceable = Arms.Target();
        pierceable.Defenses.Reduce(5, DamageBypass.Piercing);
        var cuttable = Arms.Target();
        cuttable.Defenses.Reduce(5, DamageBypass.Slashing);

        Assert.Equal(DamageType.Piercing, Strike.DamageTypeAgainst(dagger, pierceable));
        Assert.Equal(DamageType.Slashing, Strike.DamageTypeAgainst(dagger, cuttable));
    }

    [Fact]
    public void ResistanceAndImmunityDecideWhenReductionDoesNot()
    {
        var dagger = Arms.Library.BuildWeapon("dagger")!;
        var immune = Arms.Target();
        immune.Defenses.MakeImmuneTo(DamageType.Piercing);

        Assert.Equal(DamageType.Slashing, Strike.DamageTypeAgainst(dagger, immune));
        Assert.Equal(DamageType.Piercing, Strike.DamageTypeAgainst(dagger, Arms.Target()));
    }

    [Fact]
    public void AWeaponWithOneTypeHasNoChoiceToMake()
    {
        Assert.Null(Strike.DamageTypeAgainst(Arms.Library.BuildWeapon("longsword")!, Arms.Target()));
        Assert.Null(Strike.DamageTypeAgainst(Arms.Library.BuildWeapon("morningstar")!, Arms.Target()));
    }

    [Fact]
    public void TheChoiceIsWhatTheBlowLands()
    {
        var wielder = Arms.Holding("dagger");
        var target = Arms.Target();
        target.Defenses.Reduce(5, DamageBypass.Slashing);

        // A 15 hits; the 3 is the dagger's d4.
        var strike = Strike.Resolve(wielder, wielder.MeleeAttack!, target, new SequenceRandom(15, 3));

        Assert.Equal(3, strike.Damage!.AmountOf(DamageType.Slashing));
        Assert.Equal(3, strike.Taken!.Total);
    }

    [Fact]
    public void BAndPGetsPastWhatEitherWould()
    {
        var morningstar = Arms.Holding("morningstar");
        var club = Arms.Holding("club");
        var target = Arms.Target();
        target.Defenses.Reduce(5, DamageBypass.Piercing);

        var bothWays = Strike.Resolve(morningstar, morningstar.MeleeAttack!, target, new SequenceRandom(15, 7));
        var oneWay = Strike.Resolve(club, club.MeleeAttack!, target, new SequenceRandom(15, 6));

        Assert.Equal(7, bothWays.Taken!.Total);
        Assert.Equal(1, oneWay.Taken!.Total);
    }
}

public class TripWeaponTests
{
    private static (Creature Tripper, Creature Target, Encounter Encounter) Setup(string weapon)
    {
        var tripper = Arms.Holding(weapon, "Tripper");

        // Strength 14 and nothing to swing: a CMD of 12 against a check that will come up 1.
        var target = new Creature("Target", new AbilityScores(14, 10, 10, 10, 10, 10), 30, 2);
        var field = ClassKit.Field((tripper, 5, 5), (target, 6, 5));

        return (tripper, target, ClassKit.Fight([tripper], [target], field, 1));
    }

    [Fact]
    public void ABotchedTripWithATripWeaponCostsTheWeaponAndNotYourFeet()
    {
        var (tripper, target, encounter) = Setup("light-flail");
        var flail = tripper.MeleeAttack!;

        var result = (ManeuverActionResult)encounter.BeginNextTurn()!.Take(new TripAction(target))!;

        Assert.True(result.Check!.Backfired);
        Assert.False(tripper.IsProne);
        Assert.True(tripper.Equipment.IsOutOfHand(flail));
        Assert.Contains("lets go", result.Consequence);
    }

    [Fact]
    public void ABotchedTripWithAnythingElseStillPutsYouOnTheFloor()
    {
        var (tripper, target, encounter) = Setup("longsword");

        encounter.BeginNextTurn()!.Take(new TripAction(target));

        Assert.True(tripper.IsProne);
        Assert.False(tripper.Equipment.IsOutOfHand(tripper.Attacks[0]));
    }
}

public class NonlethalWeaponTests
{
    [Fact]
    public void ASapKnocksOutRatherThanKills()
    {
        var wielder = Arms.Holding("sap");
        var target = Arms.Target();

        var strike = Strike.Resolve(wielder, wielder.MeleeAttack!, target, new SequenceRandom(15, 4));

        Assert.True(strike.IsHit);
        Assert.Equal(0, target.HitPoints.Damage);
        Assert.Equal(4, target.HitPoints.Nonlethal);
        Assert.Equal(4, strike.NonlethalDealt);
    }

    [Fact]
    public void ALongswordDoesNot()
    {
        var wielder = Arms.Holding("longsword");
        var target = Arms.Target();

        Strike.Resolve(wielder, wielder.MeleeAttack!, target, new SequenceRandom(15, 4));

        Assert.Equal(4, target.HitPoints.Damage);
        Assert.Equal(0, target.HitPoints.Nonlethal);
    }

    [Fact]
    public void TheFlagIsOnTheDamage()
    {
        Assert.True(Arms.Library.BuildWeapon("sap")!.Damage.Components[0].Nonlethal);
        Assert.False(Arms.Library.BuildWeapon("club")!.Damage.Components[0].Nonlethal);
    }
}

public class BlockingWeaponTests
{
    [Fact]
    public void ATonfaIsOneMoreShieldWhileFightingDefensively()
    {
        var wielder = Arms.Holding("tonfa");
        var before = wielder.ArmorClass.Total;

        wielder.Stances.Adopt(Stance.FightingDefensively);

        Assert.Equal(before + Stances.DefensiveBonus + Martial.BlockingBonus, wielder.ArmorClass.Total);
    }

    [Fact]
    public void ButNotInTotalDefenceWhichIsNotFightingWithIt()
    {
        var wielder = Arms.Holding("tonfa");
        var before = wielder.ArmorClass.Total;
        var encounter = ClassKit.Fight([wielder], [Arms.Target()], null);

        encounter.BeginNextTurn()!.Take(new TotalDefenseAction());

        Assert.Equal(before + TotalDefenseAction.DodgeBonus, wielder.ArmorClass.Total);
    }

    [Fact]
    public void ButNotOtherwise()
    {
        var tonfa = Arms.Holding("tonfa");
        var club = Arms.Holding("club");

        Assert.Equal(club.ArmorClass.Total, tonfa.ArmorClass.Total);

        club.Stances.Adopt(Stance.FightingDefensively);
        Assert.Equal(10 + Stances.DefensiveBonus, club.ArmorClass.Total);
    }
}

public class FragileWeaponTests
{
    [Fact]
    public void ANaturalOneCracksIt()
    {
        var wielder = Arms.Holding("dogslicer");
        var slicer = wielder.MeleeAttack!;

        var strike = Strike.Resolve(wielder, slicer, Arms.Target(), new SequenceRandom(1));

        Assert.True(wielder.Equipment.IsBroken(slicer));
        Assert.Contains(strike.Notes, note => note.Contains("cracks"));
    }

    [Fact]
    public void AnyOtherRollLeavesItWhole()
    {
        var wielder = Arms.Holding("dogslicer");

        Strike.Resolve(wielder, wielder.MeleeAttack!, Arms.Target(), new SequenceRandom(2));

        Assert.False(wielder.Equipment.IsBroken(wielder.MeleeAttack!));
    }

    [Fact]
    public void ASturdyWeaponShrugsOffANaturalOne()
    {
        var wielder = Arms.Holding("short-sword");

        Strike.Resolve(wielder, wielder.MeleeAttack!, Arms.Target(), new SequenceRandom(1));

        Assert.False(wielder.Equipment.IsBroken(wielder.MeleeAttack!));
    }

    [Fact]
    public void BrokenIsTwoWorseEitherWayAndCriticalOnlyOnATwenty()
    {
        var wielder = Arms.Holding("dogslicer");
        var slicer = wielder.MeleeAttack!;
        var attack = Strike.AttackBonus(wielder, slicer).Total;
        var damage = Strike.DamageBonus(wielder, slicer).Total;

        wielder.Equipment.Break(slicer);

        Assert.Equal(attack + Strike.BrokenPenalty, Strike.AttackBonus(wielder, slicer).Total);
        Assert.Equal(damage + Strike.BrokenPenalty, Strike.DamageBonus(wielder, slicer).Total);
        Assert.Equal(CriticalProfile.Standard, Strike.CriticalFor(wielder, slicer));
        Assert.Equal(new CriticalProfile(19, 2), slicer.Attack.Critical);
    }

    [Fact]
    public void ABrokenOneThatRollsAnotherOneIsGone()
    {
        var wielder = Arms.Holding("dogslicer");
        var slicer = wielder.MeleeAttack!;
        wielder.Equipment.Break(slicer);

        var strike = Strike.Resolve(wielder, slicer, Arms.Target(), new SequenceRandom(1));

        Assert.Empty(wielder.Equipment.Worn);
        Assert.Empty(wielder.Attacks);
        Assert.False(wielder.CanAttackWith(slicer));
        Assert.Contains(strike.Notes, note => note.Contains("shatters"));
    }
}

public class DoubleWeaponTests
{
    [Fact]
    public void AQuarterstaffIsFoughtWithOneEndInBothHands()
    {
        var staff = Arms.Library.BuildWeapon("quarterstaff")!;

        Assert.True(staff.Has(WeaponSpecial.Double));
        Assert.Equal(WeaponHands.TwoHanded, staff.Hands);
        Assert.Equal(AbilityDamageScale.OneAndAHalf, staff.DamageScale);
        Assert.Equal("1d6", staff.Damage.Components.Single().Amount.ToString());
    }

    [Fact]
    public void TheOtherEndIsWrittenDownForLater() =>
        Assert.Equal("1d8", Arms.Library.GetWeapon("double-spear")!.SecondHead!.DamageMedium);
}

public class FirearmTests
{
    private static Creature Gunner(string gun) => Arms.Holding(gun, "Gunner");

    private static Creature Armoured()
    {
        var target = Arms.Target();
        target.ArmorClass.Modifiers.Add(8, BonusType.Armor, "plate");
        return target;
    }

    [Fact]
    public void AnEarlyGunGoesThroughArmourInItsFirstIncrement()
    {
        var gunner = Gunner("musket");
        var target = Armoured();
        var field = ClassKit.Field((gunner, 1, 1), (target, 1, 8));   // 35 ft

        var strike = Strike.Resolve(gunner, gunner.PrimaryAttack!, target, new SequenceRandom(11, 5), field: field);

        Assert.True((strike.Attack.Options & DefenseOptions.TouchAttack) != 0);
        Assert.Equal(10, strike.Attack.TargetArmorClass);
    }

    [Fact]
    public void ButNotBeyondIt()
    {
        var gunner = Gunner("musket");
        var target = Armoured();
        var field = ClassKit.Field((gunner, 0, 0), (target, 0, 9));   // 45 ft

        var strike = Strike.Resolve(gunner, gunner.PrimaryAttack!, target, new SequenceRandom(11), field: field);

        Assert.Equal(DefenseOptions.None, strike.Attack.Options & DefenseOptions.TouchAttack);
    }

    [Fact]
    public void AnAdvancedGunGoesThroughOutToFiveIncrements()
    {
        var revolver = Arms.Library.BuildWeapon("revolver")!;

        Assert.True(Firearms.TargetsTouch(revolver, 100));
        Assert.False(Firearms.TargetsTouch(revolver, 105));
        Assert.False(Firearms.TargetsTouch(Arms.Library.BuildWeapon("longbow")!, 10));
    }

    [Fact]
    public void AMisfireMissesWhateverTheRollAndBreaksTheGun()
    {
        var gunner = Gunner("musket");
        gunner.BaseAttackBonus = 20;

        var strike = Strike.Resolve(gunner, gunner.PrimaryAttack!, Arms.Target(), new SequenceRandom(2));

        Assert.True(strike.Misfired);
        Assert.False(strike.IsHit);
        Assert.True(gunner.Equipment.IsBroken(gunner.PrimaryAttack!));
    }

    [Fact]
    public void ABrokenGunMisfiresOnFourMoreNumbers()
    {
        var musket = Arms.Library.BuildWeapon("musket")!;

        Assert.Equal(2, Firearms.MisfireRange(musket, broken: false));
        Assert.Equal(6, Firearms.MisfireRange(musket, broken: true));
        Assert.False(Firearms.Misfires(musket, 3, broken: false));
        Assert.True(Firearms.Misfires(musket, 6, broken: true));
        Assert.Equal(0, Firearms.MisfireRange(Arms.Library.BuildWeapon("longbow")!, broken: true));
    }

    [Fact]
    public void ABrokenGunThatMisfiresBurstsInTheHand()
    {
        var gunner = Gunner("musket");
        var musket = gunner.PrimaryAttack!;
        gunner.Equipment.Break(musket);
        var health = gunner.HitPoints.Current;

        // A 5 misfires a broken musket; the Reflex save fails on a 2; the 1d12 comes up 8.
        var strike = Strike.Resolve(gunner, musket, Arms.Target(), new SequenceRandom(5, 2, 8));

        Assert.True(strike.Misfired);
        Assert.Empty(gunner.Equipment.Worn);
        Assert.Equal(health - 8, gunner.HitPoints.Current);
        Assert.Contains(strike.Notes, note => note.Contains("bursts"));
    }

    [Fact]
    public void ASavedBurstIsHalf()
    {
        var gunner = Gunner("musket");
        var musket = gunner.PrimaryAttack!;
        gunner.Equipment.Break(musket);
        var health = gunner.HitPoints.Current;

        Strike.Resolve(gunner, musket, Arms.Target(), new SequenceRandom(5, 19, 8));

        Assert.Equal(health - 4, gunner.HitPoints.Current);
    }

    [Fact]
    public void ABowNeverMisfires()
    {
        var archer = Arms.Holding("longbow");

        var strike = Strike.Resolve(archer, archer.PrimaryAttack!, Arms.Target(), new SequenceRandom(1));

        Assert.False(strike.Misfired);
        Assert.False(archer.Equipment.IsBroken(archer.PrimaryAttack!));
    }
}

public class DescribeWeaponTests
{
    private static ContentLibrary Library => TestContent.Library;

    [Fact]
    public void ALongswordInAFewLines()
    {
        Assert.Equal(
            ["Martial · one-handed · heavy blades", "1d8 slashing (1d6 if Small) · 19–20/×2", "15 gp · 4 lb"],
            Library.DescribeWeapon("longsword"));
    }

    [Fact]
    public void AHalflingReadsHerOwnDiceFirst()
    {
        var pip = Library.BuildCreature("pip")!;

        Assert.Contains("1d4 piercing (1d6 if Medium) · 19–20/×2", Library.DescribeWeapon("short-sword", pip));
    }

    [Fact]
    public void AThrowableWeaponSaysHowFar() =>
        Assert.Contains(Library.DescribeWeapon("dagger"), line => line.StartsWith("Thrown, 10 ft increments"));

    [Fact]
    public void AReachWeaponSaysWhere()
    {
        Assert.Contains("Reach — threatens 10 ft, not adjacent", Library.DescribeWeapon("longspear"));

        var ogre = Library.BuildCreature("ogre")!;
        Assert.Contains("Reach — threatens 15-20 ft, not within 10 ft", Library.DescribeWeapon("longspear", ogre));
    }

    [Fact]
    public void WhatTheGameDoesNotDoYetSaysSo()
    {
        var lines = Library.DescribeWeapon("guisarme");

        Assert.Contains("Trip — a failed trip drops the weapon, not you", lines);
        Assert.Contains(Library.DescribeWeapon("ranseur"), line => line == "Disarm — not yet in the game");
        Assert.Contains(Library.DescribeWeapon("katana"), line => line == "Deadly — not yet in the game");
        Assert.Contains(Library.DescribeWeapon("quarterstaff"), line => line.StartsWith("Double — other end 1d6"));
    }

    [Fact]
    public void ASeeTextWeaponSaysWhatItsTextIsAboutInsteadOfSeeText()
    {
        var lines = Library.DescribeWeapon("boar-spear");

        Assert.Contains(Library.GetWeapon("boar-spear")!.Description, lines);
        Assert.DoesNotContain("See text — not yet in the game", lines);
    }

    [Fact]
    public void NotProficientIsOnlySaidToSomebodyWhoIsNot()
    {
        var merrin = Library.BuildCreature("merrin")!;

        Assert.Contains("Not proficient: -4 to hit", Library.DescribeWeapon("longsword", merrin));
        Assert.DoesNotContain("Not proficient: -4 to hit", Library.DescribeWeapon("quarterstaff", merrin));
        Assert.DoesNotContain("Not proficient: -4 to hit", Library.DescribeWeapon("longsword"));
    }

    [Fact]
    public void ACatalogueOnlyWeaponSaysItCannotBeUsed() =>
        Assert.Contains("Catalogue only — not something anybody can fight with yet", Library.DescribeWeapon("net"));

    [Fact]
    public void AGunSaysHowItMisfires() =>
        Assert.Contains("Misfires on 1–2 (1–6 while broken); a broken gun that misfires bursts", Library.DescribeWeapon("musket"));

    [Fact]
    public void NothingIsSaidAboutAWeaponThatDoesNotExist() =>
        Assert.Empty(Library.DescribeWeapon("lightsabre"));

    [Fact]
    public void ArmourSaysWhatItGivesAndWhatItCosts()
    {
        var lines = Library.DescribeItem(Library.GetItem("scale-mail")!);

        Assert.Equal("Medium armour", lines[0]);
        Assert.Contains("+4 armour to armour class", lines);
        Assert.Contains("Dexterity bonus at most +3", lines);
        Assert.Contains("Check penalty -4", lines);
    }

    [Fact]
    public void ArmourSomebodyIsNotTrainedInSaysWhatThatCosts()
    {
        var merrin = Library.BuildCreature("merrin")!;

        Assert.Contains("Not proficient: its check penalty, -4, falls on attack rolls too",
            Library.DescribeItem(Library.GetItem("scale-mail")!, merrin));
    }

    [Fact]
    public void AWeaponItemIsItsWeaponAndWhateverItAdds()
    {
        var lines = Library.DescribeItem(Library.GetItem("silvered-longsword")!);

        Assert.Equal("Martial · one-handed · heavy blades", lines[0]);
        Assert.Contains(lines, line => line.StartsWith("Silver —"));
        Assert.Contains(Library.DescribeItem(Library.GetItem("greatsword-plus-one")!), line => line.StartsWith("+1 enhancement"));
    }

    [Fact]
    public void ABrokenOrThrownItemSaysSo()
    {
        var sentry = Library.BuildCreature("orc-sentry")!;
        var dagger = Library.GetItem("dagger")!;

        sentry.Equipment.Break(sentry.MeleeAttack!);
        sentry.Equipment.LetGo(sentry.MeleeAttack!);

        var lines = Library.DescribeItem(dagger, sentry);
        Assert.Contains(lines, line => line.StartsWith("Broken:"));
        Assert.Contains("Out of hand until the fight is over", lines);
    }
}
