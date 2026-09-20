using Ironbound.Rules.Abilities;
using Ironbound.Rules.Combat;
using Ironbound.Rules.Content;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Defense;
using Ironbound.Rules.Dice;
using Ironbound.Rules.Encounters;
using Ironbound.Rules.Items;
using Ironbound.Rules.Modifiers;
using Ironbound.Rules.Persistence;
using Ironbound.Rules.Tests.Content;

namespace Ironbound.Rules.Tests.Items;

public class EquippingTests
{
    [Fact]
    public void WearingSomethingHandsOverItsBonusUnderItsOwnName()
    {
        var fighter = Fighter();
        var shirt = TestContent.Library.GetItem("chain-shirt")!;

        Assert.True(fighter.Equipment.Equip(shirt));

        Assert.Equal(4, fighter.ArmorClass.Modifiers.Total);
        Assert.Contains("chain shirt", fighter.ArmorClass.Modifiers.ToString());
    }

    [Fact]
    public void TakingItOffTakesTheBonusWithIt()
    {
        var fighter = Fighter();
        fighter.Equipment.Equip(TestContent.Library.GetItem("chain-shirt")!);

        Assert.True(fighter.Equipment.Unequip("chain-shirt"));

        Assert.Equal(0, fighter.ArmorClass.Modifiers.Total);
        Assert.False(fighter.Equipment.Has("chain-shirt"));
    }

    [Fact]
    public void ArmourAndAShieldStackBecauseTheyAreDifferentThings()
    {
        var fighter = Fighter();
        fighter.Equipment.Equip(TestContent.Library.GetItem("chain-shirt")!);
        fighter.Equipment.Equip(TestContent.Library.GetItem("heavy-shield")!);

        Assert.Equal(6, fighter.ArmorClass.Modifiers.Total);
    }

    [Fact]
    public void TwoRingsFitAndAThirdDoesNot()
    {
        var fighter = Fighter();
        var ring = TestContent.Library.GetItem("ring-of-protection")!;

        Assert.True(fighter.Equipment.Equip(ring));
        Assert.True(fighter.Equipment.Equip(ring));
        Assert.False(fighter.Equipment.Equip(ring));

        // The third is stowed rather than refused, and a stowed ring protects nobody.
        Assert.Equal(2, fighter.Equipment.InSlot(EquipmentSlot.Ring).Count());
    }

    [Fact]
    public void ASecondSwordGoesOnTheBeltRatherThanVanishing()
    {
        var fighter = Fighter();
        var first = TestContent.Library.GetItem("longsword")!;
        var second = TestContent.Library.GetItem("silvered-longsword")!;

        Assert.True(fighter.Equipment.Equip(first, TestContent.Library.BuildItemWeapon(first)));
        Assert.False(fighter.Equipment.Equip(second, TestContent.Library.BuildItemWeapon(second)));

        // Stowed, but still something you can swing — drawing it is a later layer.
        Assert.Equal(2, fighter.Attacks.Count);
        Assert.Single(fighter.Equipment.InSlot(EquipmentSlot.MainHand));
        Assert.Contains("stowed", fighter.Equipment.ToString());
    }

    [Fact]
    public void AStowedThingGrantsNothing()
    {
        var fighter = Fighter();
        var shirt = TestContent.Library.GetItem("chain-shirt")!;

        fighter.Equipment.Equip(shirt);
        fighter.Equipment.Equip(shirt);   // no room; stowed

        Assert.Equal(4, fighter.ArmorClass.Modifiers.Total);
    }

    [Fact]
    public void UnequippingAWeaponTakesTheAttackAwayToo()
    {
        var fighter = Fighter();
        var sword = TestContent.Library.GetItem("longsword")!;
        fighter.Equipment.Equip(sword, TestContent.Library.BuildItemWeapon(sword));

        Assert.Single(fighter.Attacks);

        fighter.Equipment.Unequip("longsword");

        Assert.Empty(fighter.Attacks);
    }

    internal static Creature Fighter(string name = "Fighter", int allegiance = 1) =>
        new(name, new AbilityScores(18, 14, 14, 10, 10, 10), 50, 6)
        {
            Allegiance = allegiance,
            BaseAttackBonus = 6,
        };
}

public class WeaponItemTests
{
    [Fact]
    public void AWeaponItemNamesItsKindRatherThanRestatingIt()
    {
        var silvered = TestContent.Library.GetItem("silvered-longsword")!;
        var weapon = TestContent.Library.BuildItemWeapon(silvered)!;

        // Same dice as an ordinary longsword, because it *is* one.
        Assert.Equal("longsword", silvered.Weapon);
        Assert.Equal("silvered longsword", weapon.Name);
        Assert.Equal(19, weapon.Attack.Critical.ThreatsOn);
    }

    [Fact]
    public void ItCarriesWhatItIsMadeOf()
    {
        var weapon = TestContent.Library.BuildItemWeapon(
            TestContent.Library.GetItem("silvered-longsword")!)!;

        Assert.True(weapon.Qualities.HasFlag(DamageBypass.Silver));
        Assert.False(weapon.Qualities.HasFlag(DamageBypass.Magic));
    }

    [Fact]
    public void AnEnchantedWeaponIsMagicWhetherOrNotItSaysSo()
    {
        var library = ContentParsingTests.From(
            """{ "kind": "weapon", "id": "longsword", "name": "longsword", "damage": "1d8" }""",
            """
            { "kind": "item", "id": "flame-tongue", "name": "Flame Tongue", "slot": "MainHand",
              "weapon": "longsword", "enhancement": 2 }
            """);

        var item = library.GetItem("flame-tongue")!;
        var weapon = library.BuildItemWeapon(item)!;

        Assert.True(weapon.Qualities.HasFlag(DamageBypass.Magic));
        Assert.Equal(2, weapon.Attack.Modifiers.Total);
        Assert.Equal(2, weapon.DamageModifiers.Total);
    }

    [Fact]
    public void AnItemNamingAWeaponThatDoesNotExistIsReported()
    {
        var library = ContentLibrary.Load([
            ("blade.json", """
                { "kind": "item", "id": "blade", "name": "Blade", "slot": "MainHand",
                  "weapon": "chainsaw" }
                """),
        ]);

        var problem = Assert.Single(library.Problems);
        Assert.Equal("item 'blade'", problem.Source);
        Assert.Contains("chainsaw", problem.Message);
    }
}

public class SilverAndTheWallTests
{
    [Fact]
    public void ContentCanFinallySayDamageReduction()
    {
        var werewolf = TestContent.Library.BuildCreature("werewolf")!;
        var reduction = Assert.Single(werewolf.Defenses.Reductions);

        Assert.Equal(10, reduction.Amount);
        Assert.True(reduction.BypassedBy.HasFlag(DamageBypass.Silver));
    }

    [Fact]
    public void AnOrdinarySwordDoesNothingAtAllToIt()
    {
        var (valeria, werewolf) = Duel("longsword");

        // 1d8+4 against DR 10. Averaging eight and a half, almost every blow leaves nothing
        // behind at all; the very best she can roll gets two points through.
        var strike = Strike.Resolve(
            valeria, valeria.PrimaryAttack!, werewolf, new SequenceRandom(true, 15, 4));

        Assert.True(strike.Attack.IsHit);
        Assert.Equal(0, strike.DamageDealt);
    }

    [Fact]
    public void TheSilveredOneInTheSameHandDoesFullDamage()
    {
        var (valeria, werewolf) = Duel("silvered-longsword");

        var strike = Strike.Resolve(
            valeria, valeria.PrimaryAttack!, werewolf, new SequenceRandom(true, 15, 4));

        // Same swing, same roll, same creature. The only difference is what it is made of.
        Assert.Equal(8, strike.DamageDealt);
    }

    [Fact]
    public void TheWholeExchangeSurvivesSwappingWeaponsMidFight()
    {
        var (valeria, werewolf) = Duel("longsword");
        var silver = TestContent.Library.GetItem("silvered-longsword")!;

        Assert.True(valeria.Equipment.Unequip("longsword"));
        valeria.Equipment.Equip(silver, TestContent.Library.BuildItemWeapon(silver));

        var strike = Strike.Resolve(
            valeria, valeria.PrimaryAttack!, werewolf, new SequenceRandom(true, 15, 4));

        Assert.Equal(8, strike.DamageDealt);
    }

    private static (Creature Valeria, Creature Werewolf) Duel(string blade)
    {
        var valeria = EquippingTests.Fighter("Valeria");
        var item = TestContent.Library.GetItem(blade)!;
        valeria.Equipment.Equip(item, TestContent.Library.BuildItemWeapon(item));

        var werewolf = TestContent.Library.BuildCreature("werewolf")!;
        werewolf.Allegiance = 2;

        return (valeria, werewolf);
    }
}

public class ShippedEquipmentTests
{
    [Fact]
    public void TheCastWearsThingsRatherThanCarryingAMagicNumber()
    {
        var valeria = TestContent.Library.BuildCreature("valeria")!;

        Assert.Equal(19, valeria.ArmorClass.Total);   // unchanged by the conversion
        Assert.Equal(
            ["chain shirt", "heavy shield", "longsword"],
            valeria.Equipment.Items.Select(item => item.Name));
    }

    [Fact]
    public void TheArcherHoldsItsBowAndStowsItsBlade()
    {
        var archer = TestContent.Library.BuildCreature("goblin-archer")!;

        Assert.Equal(["shortbow"], archer.Equipment.InSlot(EquipmentSlot.MainHand).Select(i => i.Name));
        Assert.Equal("scimitar", archer.MeleeAttack!.Name);
    }

    [Fact]
    public void EquipmentSurvivesASaveWithoutDoublingAnything()
    {
        var valeria = TestContent.Library.BuildCreature("valeria")!;
        var armour = valeria.ArmorClass.Total;

        var encounter = new Encounter([valeria], new SequenceRandom(true, 10));
        var restored = GameSave
            .Restore(GameSave.FromJson(GameSave.ToJson(GameSave.Capture(encounter))), TestContent.Library)
            .Order.Single().Creature;

        Assert.Equal(armour, restored.ArmorClass.Total);
        Assert.Equal(3, restored.Equipment.Worn.Count);
        Assert.Single(restored.Attacks);

        // And the identity came back, so taking it off still works.
        Assert.True(restored.Equipment.Unequip("chain-shirt"));
        Assert.Equal(armour - 4, restored.ArmorClass.Total);
    }

    [Fact]
    public void AStowedThingComesBackStowed()
    {
        var archer = TestContent.Library.BuildCreature("goblin-archer")!;

        var encounter = new Encounter([archer], new SequenceRandom(true, 10));
        var restored = GameSave
            .Restore(GameSave.FromJson(GameSave.ToJson(GameSave.Capture(encounter))), TestContent.Library)
            .Order.Single().Creature;

        Assert.Single(restored.Equipment.InSlot(EquipmentSlot.MainHand));
        Assert.Contains("stowed", restored.Equipment.ToString());
    }

    [Fact]
    public void TheMoonlitClearingIsShippedAndTheThingInItHasTeeth()
    {
        var encounter = TestContent.Library.GetEncounter("moonlit-clearing")!;

        Assert.Equal(3, encounter.Placements.Count(p => p.Party));
        Assert.Equal("Grey Fang", encounter.Placements.Single(p => !p.Party).Name);
    }
}
