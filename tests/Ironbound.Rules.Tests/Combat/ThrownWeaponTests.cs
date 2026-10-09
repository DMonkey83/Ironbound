using Ironbound.Rules.Abilities;
using Ironbound.Rules.Combat;
using Ironbound.Rules.Content;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Dice;
using Ironbound.Rules.Encounters.Actions;
using Ironbound.Rules.Items;
using Ironbound.Rules.Tests.Classes;

namespace Ironbound.Rules.Tests.Combat;

public class ThrownWeaponTests
{
    private static ContentLibrary Library => TestContent.Library;

    private static ItemDefinition Item(string weapon) => Library.GetItem(weapon) ?? new ItemDefinition
    {
        Id = weapon, Name = weapon.Replace('-', ' '), Slot = EquipmentSlot.MainHand, Weapon = weapon,
    };

    private static Creature Armed(params string[] weapons)
    {
        var creature = new Creature("Thrower", new AbilityScores(14, 16, 10, 10, 10, 10), 20, 2);
        foreach (var weapon in weapons)
        {
            Library.Equip(creature, Item(weapon));
        }

        return creature;
    }

    [Fact]
    public void ADaggerIsTwoAttacksOneOfThemThrown()
    {
        var thrower = Armed("dagger");

        Assert.Equal(["dagger", "dagger (thrown)"], thrower.Attacks.Select(attack => attack.Name));

        var thrown = thrower.Attacks[1];
        Assert.True(thrown.IsThrownUse);
        Assert.False(thrower.Attacks[0].IsThrownUse);
        Assert.Equal(10, thrown.RangeIncrement);
        Assert.Equal(WeaponAttack.ThrownIncrements, thrown.MaximumIncrements);
        Assert.Equal("dagger", thrown.Kind);
    }

    [Fact]
    public void ThrownItIsAimedWithDexterityAndPushedWithStrength()
    {
        var thrower = Armed("dagger");
        var thrown = thrower.Attacks[1];

        Assert.Equal(Ability.Dexterity, thrown.AttackAbility);
        Assert.Equal(Ability.Strength, thrown.DamageAbility);
        Assert.Equal(AbilityDamageScale.Full, thrown.DamageScale);
        Assert.Equal(3, Strike.AttackBonus(thrower, thrown).Total - thrower.BaseAttackBonus);
        Assert.Equal(2, Strike.DamageBonus(thrower, thrown).Total);
    }

    [Fact]
    public void BothUsesComeFromTheOneItem()
    {
        var thrower = Armed("dagger");
        var dagger = Library.GetItem("dagger")!;

        Assert.Same(dagger, thrower.Equipment.ItemFor(thrower.Attacks[0]));
        Assert.Same(dagger, thrower.Equipment.ItemFor(thrower.Attacks[1]));
        Assert.Null(thrower.Equipment.ItemFor(WeaponAttack.Melee("fist", "1d3", DamageType.Bludgeoning)));
    }

    [Fact]
    public void ThrowingItPutsItOutOfHandAndTakesBothUsesWithIt()
    {
        var thrower = Armed("dagger");
        var target = new Creature("Target", AbilityScores.All(10), 20, 2);
        var dagger = Library.GetItem("dagger")!;

        var strike = Strike.Resolve(thrower, thrower.Attacks[1], target, new SequenceRandom(2));

        Assert.False(strike.IsHit);
        Assert.True(thrower.Equipment.IsOutOfHand(dagger));
        Assert.Contains(strike.Notes, note => note.Contains("out of hand"));
        Assert.Null(thrower.MeleeAttack);
        Assert.Null(thrower.PrimaryAttack);
        Assert.False(thrower.CanAttackWith(thrower.Attacks[0]));
    }

    [Fact]
    public void AnAttackWithAWeaponOutOfHandIsRefused()
    {
        var thrower = Armed("dagger");
        var target = new Creature("Target", AbilityScores.All(10), 20, 2);
        var encounter = ClassKit.Fight([thrower], [target], null);
        thrower.Equipment.LetGo(thrower.Attacks[0]);

        var turn = encounter.BeginNextTurn()!;

        Assert.False(turn.CanTake(new AttackAction(thrower.Attacks[0], target)));
        Assert.False(turn.CanTake(new AttackAction(thrower.Attacks[1], target)));
    }

    [Fact]
    public void TheOtherWeaponIsStillThere()
    {
        var thrower = Armed("dagger", "short-sword");

        thrower.Equipment.LetGo(thrower.Attacks[1]);

        Assert.Equal("short sword", thrower.MeleeAttack!.Name);
    }

    [Fact]
    public void EverythingComesBackWhenItIsPickedUp()
    {
        var thrower = Armed("dagger");
        thrower.Equipment.LetGo(thrower.Attacks[1]);

        Assert.Equal(1, thrower.Equipment.Recover());

        Assert.False(thrower.Equipment.IsOutOfHand(Library.GetItem("dagger")!));
        Assert.Equal("dagger", thrower.MeleeAttack!.Name);
    }

    [Fact]
    public void AJavelinIsThrownLikeAnArrowIsShot()
    {
        var thrower = Armed("javelin");
        var target = new Creature("Target", AbilityScores.All(10), 20, 2);

        var javelin = Assert.Single(thrower.Attacks);
        Assert.True(javelin.IsRanged);
        Assert.False(javelin.IsThrownUse);
        Assert.Equal(WeaponAttack.ThrownIncrements, javelin.MaximumIncrements);
        Assert.Equal(AbilityDamageScale.Full, javelin.DamageScale);

        Strike.Resolve(thrower, javelin, target, new SequenceRandom(2));

        Assert.True(thrower.CanAttackWith(javelin));
    }

    [Fact]
    public void ABowGetsNoStrengthAndASlingGetsAllOfIt()
    {
        var archer = Armed("shortbow", "sling");

        Assert.Equal(0, Strike.DamageBonus(archer, archer.Attacks[0]).Total);
        Assert.Equal(2, Strike.DamageBonus(archer, archer.Attacks[1]).Total);
    }

    [Fact]
    public void AFullAttackDoesNotThrowTheSameDaggerTwice()
    {
        var thrower = ClassKit.Make("fighter", 6, "\"items\": [\"dagger\"]");
        var target = new Creature("Target", AbilityScores.All(10), 60, 2);
        var encounter = ClassKit.Fight([thrower], [target], null, Enumerable.Repeat(2, 20).ToArray());
        var thrown = thrower.Attacks.Single(attack => attack.IsThrownUse);

        var result = (FullAttackResult)encounter.BeginNextTurn()!.Take(new FullAttackAction(target, thrown))!;

        Assert.Equal(2, thrower.AttacksPerFullAttack);
        Assert.Single(result.Strikes);
    }

    [Fact]
    public void ADaggerWrittenIntoAFileAsAWeaponIsNotThrown()
    {
        var library = ClassKit.Library(("knife-thing.json", """
            { "kind": "creature", "id": "knife-thing", "name": "Knife Thing", "abilities": [10, 10, 10, 10, 10, 10],
              "weapons": ["dagger"] }
            """));

        var thing = library.BuildCreature("knife-thing")!;

        Assert.Single(thing.Attacks);
    }

    [Fact]
    public void TheOrcSentryCanThrowItsKnife()
    {
        var sentry = Library.BuildCreature("orc-sentry")!;

        Assert.Contains(sentry.Attacks, attack => attack.Name == "dagger (thrown)");
        Assert.Contains(Library.BuildCreature("orc")!.Attacks, attack => attack.Name == "shortspear (thrown)");
    }
}
