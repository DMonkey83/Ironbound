using Ironbound.Rules.Abilities;
using Ironbound.Rules.Combat;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Tests.Classes;

namespace Ironbound.Rules.Tests.Combat;

public class WeaponSizeTests
{
    [Theory]
    [InlineData("1d2", "1d3")]
    [InlineData("1d3", "1d4")]
    [InlineData("1d4", "1d6")]
    [InlineData("1d6", "1d8")]
    [InlineData("1d8", "2d6")]
    [InlineData("1d10", "2d8")]
    [InlineData("1d12", "3d6")]
    [InlineData("2d4", "2d6")]
    [InlineData("2d6", "3d6")]
    [InlineData("2d8", "3d8")]
    [InlineData("2d10", "4d8")]
    public void MediumToLargeIsTheBooksTable(string medium, string large) =>
        Assert.Equal(large, WeaponDice.Larger(medium));

    [Theory]
    [InlineData("1d8", "1d6", "1d4")]
    [InlineData("1d10", "1d8", "1d6")]
    [InlineData("2d6", "1d10", "1d8")]
    [InlineData("2d4", "1d6", "1d4")]
    [InlineData("1d4", "1d3", "1d2")]
    [InlineData("1d3", "1d2", "1")]
    public void TinyIsAStepDownFromSmall(string medium, string small, string tiny) =>
        Assert.Equal(tiny, WeaponDice.For(CreatureSize.Tiny, medium, small));

    [Fact]
    public void HugeIsTwoStepsUpFromMedium() =>
        Assert.Equal("3d6", WeaponDice.For(CreatureSize.Huge, "1d8"));

    [Fact]
    public void SmallAndMediumAreReadStraightFromTheTable()
    {
        Assert.Equal("1d8", WeaponDice.For(CreatureSize.Medium, "1d8", "1d6"));
        Assert.Equal("1d6", WeaponDice.For(CreatureSize.Small, "1d8", "1d6"));
    }

    [Fact]
    public void WithoutASmallColumnSmallIsAStepDown() =>
        Assert.Equal("1d6", WeaponDice.For(CreatureSize.Small, "1d8"));

    [Fact]
    public void DiceTheTableDoesNotListAreLeftAlone()
    {
        Assert.Equal("5d5", WeaponDice.Larger("5d5"));
        Assert.Equal("1", WeaponDice.Smaller("1"));
    }

    [Fact]
    public void AHalflingsShortSwordIsAHalflingsShortSword()
    {
        var pip = TestContent.Library.BuildCreature("pip")!;

        Assert.Equal("1d4", pip.MeleeAttack!.Damage.Components[0].Amount.ToString());
    }

    [Fact]
    public void AGoblinsScimitarAndBowAreSmall()
    {
        var archer = TestContent.Library.BuildCreature("goblin-archer")!;

        Assert.All(archer.Attacks, attack => Assert.Equal("1d4", attack.Damage.Components[0].Amount.ToString()));
    }

    [Fact]
    public void AMediumWielderSwingsTheMediumDice()
    {
        var valeria = TestContent.Library.BuildCreature("valeria")!;

        Assert.Equal("1d8", valeria.MeleeAttack!.Damage.Components[0].Amount.ToString());
    }

    [Fact]
    public void TheOgresAxeHitsLikeTheBookOgresClub()
    {
        var ogre = TestContent.Library.BuildCreature("ogre")!;
        var axe = ogre.MeleeAttack!;

        Assert.Equal("2d8", axe.Damage.Components[0].Amount.ToString());
        Assert.Equal(new CriticalProfile(20, 2), axe.Attack.Critical);
        Assert.Equal(WeaponHands.TwoHanded, axe.Hands);
        Assert.Equal(AbilityDamageScale.OneAndAHalf, axe.DamageScale);
    }

    [Fact]
    public void ALargeWielderGetsALargeWeapon()
    {
        var weapon = TestContent.Library.BuildWeapon("longsword", size: CreatureSize.Large)!;

        Assert.Equal("2d6", weapon.Damage.Components[0].Amount.ToString());
    }

    [Fact]
    public void NaturalAttacksKeepTheDiceTheirStatBlockGave()
    {
        var rat = TestContent.Library.BuildCreature("dire-rat")!;

        Assert.Equal(CreatureSize.Small, rat.Size);
        Assert.Equal("1d4", rat.PrimaryAttack!.Damage.Components[0].Amount.ToString());
        Assert.Equal("1d8", TestContent.Library.BuildWeapon("bite", size: CreatureSize.Large)!.Damage.Components[0].Amount.ToString());
    }

    [Fact]
    public void AnItemHandedToASmallCreatureIsSmall()
    {
        var halfling = ClassKit.Make("rogue", 1, "\"size\": \"Small\"");
        TestContent.Library.Equip(halfling, TestContent.Library.GetItem("longsword")!);

        Assert.Equal("1d6", halfling.MeleeAttack!.Damage.Components[0].Amount.ToString());
    }

    [Fact]
    public void ACreatureBuiltByHandStillGetsMediumDiceByDefault()
    {
        var weapon = TestContent.Library.BuildWeapon("longsword")!;
        var someone = new Creature("Someone", AbilityScores.All(10), 10, 1);
        someone.Attacks.Add(weapon);

        Assert.Equal("1d8", weapon.Damage.Components[0].Amount.ToString());
    }
}
