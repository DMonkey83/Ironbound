using Ironbound.Rules.Abilities;
using Ironbound.Rules.Combat;
using Ironbound.Rules.Content;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Defense;
using Ironbound.Rules.Encounters.Actions;
using Ironbound.Rules.Tests.Classes;

namespace Ironbound.Rules.Tests.Combat;

public class TwoHandsTests
{
    private static ContentLibrary Library => TestContent.Library;

    [Fact]
    public void BothHandsOnTheHaftIsHalfAgainStrength()
    {
        var axe = Library.BuildWeapon("greataxe")!;

        Assert.Equal(AbilityDamageScale.OneAndAHalf, axe.DamageScale);
        Assert.Equal(4, axe.ScaleDamage(3));
    }

    [Fact]
    public void OneHandIsStrengthAsItIs()
    {
        Assert.Equal(AbilityDamageScale.Full, Library.BuildWeapon("longsword")!.DamageScale);
        Assert.Equal(AbilityDamageScale.Full, Library.BuildWeapon("dagger")!.DamageScale);
    }

    [Fact]
    public void AScaleWrittenInTheFileStillWins()
    {
        var library = ContentLibrary.Load([("odd.json", """
            { "kind": "weapon", "id": "odd", "name": "odd", "hands": "two-handed", "scale": "Full" }
            """)]);

        Assert.Equal(AbilityDamageScale.Full, library.BuildWeapon("odd")!.DamageScale);
    }

    [Fact]
    public void PowerAttackIsHalfAgainWithBothHands()
    {
        var karn = Library.BuildCreature("karn")!;
        karn.Stances.Adopt(Stance.PowerAttack);

        var bonus = Strike.DamageBonus(karn, karn.MeleeAttack!);

        // Severity two at base attack six: four with one hand, six with two.
        Assert.Contains(bonus.Entries, entry => entry.Modifier.Source == "Power Attack" && entry.Modifier.Value == 6);
    }

    [Fact]
    public void PowerAttackIsTheUsualTradeWithOneHand()
    {
        var sergeant = Library.BuildCreature("hobgoblin-sergeant")!;
        Assert.True(sergeant.Stances.Adopt(Stance.PowerAttack));

        var bonus = Strike.DamageBonus(sergeant, sergeant.MeleeAttack!);

        Assert.Contains(bonus.Entries, entry => entry.Modifier.Source == "Power Attack" && entry.Modifier.Value == 4);
    }

    [Fact]
    public void PowerAttackDoesNothingForABowInEitherCase()
    {
        var karn = Library.BuildCreature("karn")!;
        karn.Stances.Adopt(Stance.PowerAttack);

        Assert.Equal(0, karn.Stances.DamageBonus(melee: false, twoHanded: true));
    }
}

public class ShieldBehindAGreataxeTests
{
    private static ContentLibrary Library => TestContent.Library;

    [Fact]
    public void KarnsShieldCountsForNothingWhileSheSwingsTheGreataxe()
    {
        var karn = Library.BuildCreature("karn")!;

        Assert.True(karn.Equipment.HasShield);
        Assert.True(karn.Equipment.ShieldSetAside);
        Assert.False(karn.Equipment.ShieldInUse);

        // 10, Dexterity 1, breastplate 6: the light shield's one point is not there.
        Assert.Equal(17, karn.ArmorClass.Total);
        Assert.DoesNotContain(karn.ArmorClass.Explain().Entries, entry => entry.Modifier.Source == "light shield");
    }

    [Fact]
    public void ItCountsAgainOnceTheGreataxeIsOutOfHer()
    {
        var karn = Library.BuildCreature("karn")!;

        karn.Equipment.LetGo(karn.MeleeAttack!);

        Assert.False(karn.Equipment.ShieldSetAside);
        Assert.Equal(18, karn.ArmorClass.Total);
    }

    [Fact]
    public void AOneHandedWeaponLeavesTheShieldAtWork()
    {
        var valeria = Library.BuildCreature("valeria")!;

        Assert.True(valeria.Equipment.ShieldInUse);
        Assert.Contains(valeria.ArmorClass.Explain().Entries, entry => entry.Modifier.Source == "heavy shield");
    }

    [Fact]
    public void ShieldFocusIsWastedBehindAGreataxe()
    {
        var fighter = ClassKit.Make("fighter", 2, """
            "feats": ["shield-focus"], "items": ["heavy-shield", "greataxe"]
            """);

        Assert.Equal(10 + 2, fighter.ArmorClass.Total);   // Dexterity 14 only

        fighter.Equipment.Unequip("greataxe");

        Assert.Equal(10 + 2 + 2 + 1, fighter.ArmorClass.Total);
    }

    [Fact]
    public void TheShieldsCheckPenaltyStaysWhileItsBonusGoes()
    {
        var karn = Library.BuildCreature("karn")!;

        Assert.Equal(-1, karn.Equipment.CheckPenalties.Shield);
    }

    [Fact]
    public void TheItemSaysWhyItIsNotCounted()
    {
        var karn = Library.BuildCreature("karn")!;

        var lines = Library.DescribeItem(Library.GetItem("light-shield")!, karn);

        Assert.Contains(lines, line => line.StartsWith("Not counted while wielding a two-handed weapon"));
    }
}
