using Ironbound.Rules.Abilities;
using Ironbound.Rules.Combat;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Dice;
using Ironbound.Rules.Encounters;
using Ironbound.Rules.Feats;
using Ironbound.Rules.Modifiers;
using Ironbound.Rules.Persistence;
using Ironbound.Rules.Tests.Content;

namespace Ironbound.Rules.Tests.Combat;

public class StanceTrainingTests
{
    [Fact]
    public void YouCannotPowerAttackWithoutTheFeat()
    {
        var brute = Fighter(baseAttack: 6);

        Assert.False(brute.Stances.CanAdopt(Stance.PowerAttack));
        Assert.False(brute.Stances.Adopt(Stance.PowerAttack));
        Assert.Empty(brute.Stances.Active);
    }

    [Fact]
    public void CoveringUpNeedsNoTrainingAtAll()
    {
        var anybody = Fighter(baseAttack: 0);

        Assert.True(anybody.Stances.CanAdopt(Stance.FightingDefensively));
        Assert.True(anybody.Stances.Adopt(Stance.FightingDefensively));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(4, 2)]
    [InlineData(6, 2)]
    [InlineData(8, 3)]
    [InlineData(20, 5)]
    public void TheTradeSteepensWithSkillAndThenStops(int baseAttack, int severity)
    {
        var brute = Fighter(baseAttack, "power-attack");

        Assert.Equal(severity, brute.Stances.Severity(Stance.PowerAttack));
    }

    internal static Creature Fighter(int baseAttack, params string[] feats)
    {
        var creature = new Creature("Brute", new AbilityScores(18, 14, 14, 10, 10, 10), 50, 6)
        {
            Allegiance = 1,
            BaseAttackBonus = baseAttack,
        };

        foreach (var id in feats)
        {
            creature.Feats.Add(TestContent.Library.GetFeat(id)!);
        }

        creature.Attacks.Add(WeaponAttack.Melee("greataxe", "1d12", DamageType.Slashing));

        return creature;
    }
}

public class PowerAttackTests
{
    [Fact]
    public void ItGivesUpAccuracyAndGainsTwiceAsMuchDamage()
    {
        var brute = StanceTrainingTests.Fighter(6, "power-attack");
        var axe = brute.PrimaryAttack!;

        var attack = Strike.AttackBonus(brute, axe).Total;
        var damage = Strike.DamageBonus(brute, axe).Total;

        brute.Stances.Adopt(Stance.PowerAttack);

        // Severity two at base attack six: two off the swing, four onto the blow.
        Assert.Equal(attack - 2, Strike.AttackBonus(brute, axe).Total);
        Assert.Equal(damage + 4, Strike.DamageBonus(brute, axe).Total);
    }

    [Fact]
    public void ABowGainsNothingAndGivesUpNothing()
    {
        var brute = StanceTrainingTests.Fighter(6, "power-attack");
        var bow = WeaponAttack.Ranged("shortbow", "1d6", DamageType.Piercing, 60);
        brute.Attacks.Add(bow);

        var attack = Strike.AttackBonus(brute, bow).Total;
        var damage = Strike.DamageBonus(brute, bow).Total;

        brute.Stances.Adopt(Stance.PowerAttack);

        // You cannot lean into a shot. This is why the trade lives with the resolver rather
        // than on the creature's own modifier stack.
        Assert.Equal(attack, Strike.AttackBonus(brute, bow).Total);
        Assert.Equal(damage, Strike.DamageBonus(brute, bow).Total);
    }

    [Fact]
    public void DroppingItGivesEverythingBack()
    {
        var brute = StanceTrainingTests.Fighter(6, "power-attack");
        var axe = brute.PrimaryAttack!;
        var attack = Strike.AttackBonus(brute, axe).Total;

        brute.Stances.Adopt(Stance.PowerAttack);
        brute.Stances.Drop(Stance.PowerAttack);

        Assert.Equal(attack, Strike.AttackBonus(brute, axe).Total);
        Assert.Empty(brute.Stances.Active);
    }

    [Fact]
    public void TheBreakdownSaysWhereThePenaltyCameFrom()
    {
        var brute = StanceTrainingTests.Fighter(6, "power-attack");
        brute.Stances.Adopt(Stance.PowerAttack);

        Assert.Contains("Power Attack", Strike.AttackBonus(brute, brute.PrimaryAttack!).ToString());
    }
}

public class DefensiveStanceTests
{
    [Fact]
    public void CombatExpertiseBuysArmourClassWithAccuracy()
    {
        var duellist = StanceTrainingTests.Fighter(8, "combat-expertise");
        var armour = duellist.ArmorClass.Total;
        var attack = Strike.AttackBonus(duellist, duellist.PrimaryAttack!).Total;

        duellist.Stances.Adopt(Stance.CombatExpertise);

        // Severity three at base attack eight, both ways.
        Assert.Equal(armour + 3, duellist.ArmorClass.Total);
        Assert.Equal(attack - 3, Strike.AttackBonus(duellist, duellist.PrimaryAttack!).Total);
    }

    [Fact]
    public void FightingDefensivelyIsTheSameTradeAtAWorseRate()
    {
        var anybody = StanceTrainingTests.Fighter(6);
        var armour = anybody.ArmorClass.Total;
        var attack = Strike.AttackBonus(anybody, anybody.PrimaryAttack!).Total;

        anybody.Stances.Adopt(Stance.FightingDefensively);

        Assert.Equal(armour + 2, anybody.ArmorClass.Total);
        Assert.Equal(attack - 4, Strike.AttackBonus(anybody, anybody.PrimaryAttack!).Total);
    }

    [Fact]
    public void TwoOfThemStackBecauseTheyAreDodgeBonuses()
    {
        var duellist = StanceTrainingTests.Fighter(8, "combat-expertise");
        var armour = duellist.ArmorClass.Total;

        duellist.Stances.Adopt(Stance.CombatExpertise);
        duellist.Stances.Adopt(Stance.FightingDefensively);

        Assert.Equal(armour + 5, duellist.ArmorClass.Total);
    }

    [Fact]
    public void AndBothAreLostWhenCaughtFlatFooted()
    {
        var duellist = StanceTrainingTests.Fighter(8, "combat-expertise");
        duellist.Stances.Adopt(Stance.CombatExpertise);
        duellist.Stances.Adopt(Stance.FightingDefensively);

        // Dodge bonuses go when you cannot react, which is the whole point of the type.
        Assert.Equal(
            duellist.ArmorClass.Total - 5,
            duellist.ArmorClass.FlatFooted + duellist.Abilities[Ability.Dexterity].Modifier);
    }

    [Fact]
    public void DroppingItTakesBackOnlyItsOwn()
    {
        var duellist = StanceTrainingTests.Fighter(8, "combat-expertise");
        duellist.ArmorClass.Modifiers.Add(1, BonusType.Dodge, "Dodge");

        var armour = duellist.ArmorClass.Total;

        duellist.Stances.Adopt(Stance.CombatExpertise);
        duellist.Stances.Drop(Stance.CombatExpertise);

        Assert.Equal(armour, duellist.ArmorClass.Total);
    }
}

public class StancePersistenceTests
{
    [Fact]
    public void AStanceSurvivesASaveWithoutDoublingItsArmour()
    {
        var duellist = StanceTrainingTests.Fighter(8, "combat-expertise");
        duellist.Stances.Adopt(Stance.CombatExpertise);

        var armour = duellist.ArmorClass.Total;
        var encounter = new Encounter([duellist], new SequenceRandom(true, 10));

        var restored = GameSave
            .Restore(
                GameSave.FromJson(GameSave.ToJson(GameSave.Capture(encounter))),
                TestContent.Library)
            .Order.Single().Creature;

        Assert.True(restored.Stances.IsActive(Stance.CombatExpertise));
        Assert.Equal(armour, restored.ArmorClass.Total);

        // And it can still be taken off afterwards, which is what the identity was for.
        restored.Stances.Drop(Stance.CombatExpertise);
        Assert.Equal(armour - 3, restored.ArmorClass.Total);
    }
}
