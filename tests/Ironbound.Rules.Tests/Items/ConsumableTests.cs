using Ironbound.Rules.Abilities;
using Ironbound.Rules.Combat;
using Ironbound.Rules.Content;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Defense;
using Ironbound.Rules.Dice;
using Ironbound.Rules.Effects;
using Ironbound.Rules.Items;
using Ironbound.Rules.Magic;
using Ironbound.Rules.Tests.Classes;
using Ironbound.Rules.Tests.Magic;

namespace Ironbound.Rules.Tests.Items;

public class PotionPricingTests
{
    [Fact]
    public void CureLightWoundsAtCasterLevelOneIsFiftyGold()
    {
        Assert.Equal(50_00, Pricing.Potion(1, 1));
        Assert.Equal(50_00, TestContent.Library.GetItem("potion-of-cure-light-wounds")!.Price);
    }

    [Theory]
    [InlineData(0, 1, 25_00)]
    [InlineData(0, 3, 75_00)]
    [InlineData(1, 1, 50_00)]
    [InlineData(2, 3, 300_00)]
    [InlineData(3, 5, 750_00)]
    public void APotionIsFiftyGoldALevelTimesItsCasterLevelAndACantripHalfThat(int spellLevel, int casterLevel, int copper) =>
        Assert.Equal(copper, Pricing.Potion(spellLevel, casterLevel));

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(2, 3)]
    [InlineData(3, 5)]
    public void TheLeastCasterLevelIsOneLessThanTwiceTheSpellLevel(int spellLevel, int casterLevel) =>
        Assert.Equal(casterLevel, Pricing.MinimumCasterLevel(spellLevel));

    /// <summary>The alchemical items' cost and weight are the table's (Open Game Content).</summary>
    [Theory]
    [InlineData("potion-of-cure-light-wounds", 50_00, 0)]
    [InlineData("potion-of-mage-armor", 50_00, 0)]
    [InlineData("oil-of-magic-weapon", 50_00, 0)]
    [InlineData("acid-flask", 10_00, 1)]
    [InlineData("alchemists-fire", 20_00, 1)]
    [InlineData("tanglefoot-bag", 50_00, 4)]
    [InlineData("thunderstone", 30_00, 1)]
    public void TheShippedConsumablesCostAndWeighWhatTheBookSays(string id, int copper, int pounds)
    {
        var item = TestContent.Library.GetItem(id)!;

        Assert.Equal(copper, item.Price);
        Assert.Equal(pounds, item.Weight);
    }
}

public class ConsumableContentTests
{
    [Theory]
    [InlineData("potion-of-cure-light-wounds", ConsumableKind.Potion)]
    [InlineData("potion-of-mage-armor", ConsumableKind.Potion)]
    [InlineData("oil-of-magic-weapon", ConsumableKind.Oil)]
    [InlineData("acid-flask", ConsumableKind.Splash)]
    [InlineData("alchemists-fire", ConsumableKind.Splash)]
    [InlineData("tanglefoot-bag", ConsumableKind.Tanglefoot)]
    [InlineData("thunderstone", ConsumableKind.Thunderstone)]
    public void AConsumableIsOneStacksAndHangsOnTheBelt(string id, ConsumableKind kind)
    {
        var item = TestContent.Library.GetItem(id)!;

        Assert.Equal(ItemKind.Consumable, item.Kind);
        Assert.True(item.IsConsumable);
        Assert.Equal(kind, item.Consumable!.Kind);
        Assert.True(item.Stackable);
        Assert.Equal(EquipmentSlot.Carried, item.Slot);
    }

    [Fact]
    public void APotionFindsItsSpell()
    {
        var cure = TestContent.Library.GetItem("potion-of-cure-light-wounds")!.Consumable!;
        var armour = TestContent.Library.GetItem("potion-of-mage-armor")!.Consumable!;

        Assert.Same(TestContent.Library.GetSpell("cure-light-wounds"), cure.Spell);
        Assert.Equal(1, cure.CasterLevel);
        Assert.True(cure.IsPotion);
        Assert.False(cure.IsThrown);
        Assert.Equal(ThrowAim.None, cure.Aim);
        Assert.True(cure.Heals);
        Assert.False(armour.Heals);

        // Ten, the spell's level, and half of it.
        Assert.Equal(11, cure.DifficultyClass);
    }

    [Fact]
    public void AThrownOneKnowsHowFarAndAtWhat()
    {
        var fire = TestContent.Library.GetItem("alchemists-fire")!.Consumable!;
        var bag = TestContent.Library.GetItem("tanglefoot-bag")!.Consumable!;
        var stone = TestContent.Library.GetItem("thunderstone")!.Consumable!;

        Assert.True(fire.IsThrown);
        Assert.True(fire.TouchAttack);
        Assert.Equal(ThrowAim.Either, fire.Aim);
        Assert.Equal(50, fire.MaximumRange);
        Assert.Equal(("1d6", DamageType.Fire, 1, "1d6"), (fire.Damage, fire.DamageType, fire.Splash, fire.Burn));

        Assert.Equal(ThrowAim.Creature, bag.Aim);
        Assert.Equal((15, "2d4", 17), (bag.SaveDc, bag.Rounds, bag.EscapeDc));

        Assert.False(stone.TouchAttack);
        Assert.Equal(ThrowAim.Square, stone.Aim);
        Assert.Equal(100, stone.MaximumRange);
        Assert.Equal((10, 15, 1), (stone.RadiusFeet, stone.DifficultyClass, stone.DeafenedHours));
    }

    [Fact]
    public void AnAlchemicalItemWeighsTheSameOnASmallWearer()
    {
        var bag = TestContent.Library.GetItem("tanglefoot-bag")!;
        var leather = TestContent.Library.GetItem("leather-armour")!;

        Assert.Equal(4m, Encumbrance.WornWeight(bag, CreatureSize.Small));
        Assert.Equal(leather.Weight / 2, Encumbrance.WornWeight(leather, CreatureSize.Small));
    }

    [Theory]
    [InlineData("bless", "range of personal")]
    [InlineData("divine-power", "level 4")]
    [InlineData("divine-power", "range of personal")]
    [InlineData("no-such-spell", "no spell called 'no-such-spell'")]
    [InlineData("cure-critical-wounds", "level 4")]
    [InlineData("fireball", "not cast on one creature")]
    [InlineData("cause-fear", "only touches enemies")]
    public void APotionCannotHoldJustAnySpell(string spell, string complaint)
    {
        var library = WithItem($$"""
            { "kind": "item", "id": "bottle", "name": "bottle", "type": "consumable",
              "use": { "kind": "potion", "spell": "{{spell}}", "casterLevel": 7 } }
            """);

        Assert.Contains(library.Problems, problem => problem.Message.Contains(complaint));
    }

    [Fact]
    public void APotionCannotBeBrewedBelowTheLeastCasterLevel()
    {
        var low = WithItem("""
            { "kind": "item", "id": "bottle", "name": "bottle", "type": "consumable",
              "use": { "kind": "potion", "spell": "cure-moderate-wounds", "casterLevel": 2 } }
            """);
        var enough = WithItem("""
            { "kind": "item", "id": "bottle", "name": "bottle", "type": "consumable",
              "use": { "kind": "potion", "spell": "cure-moderate-wounds", "casterLevel": 3 } }
            """);

        Assert.Contains(low.Problems, problem => problem.Field == "use.casterLevel" && problem.Message.Contains("the least is 3"));
        Assert.Empty(enough.Problems);
        Assert.Equal(300_00, enough.GetItem("bottle")!.Price);
    }

    [Fact]
    public void APotionWithAPriceWrittenOnItIsWrong()
    {
        var library = WithItem("""
            { "kind": "item", "id": "bottle", "name": "bottle", "type": "consumable", "cost": 20,
              "use": { "kind": "potion", "spell": "cure-light-wounds" } }
            """);

        Assert.Contains(library.Problems, problem => problem.Field == "cost");

        // Priced from the spell all the same.
        Assert.Equal(50_00, library.GetItem("bottle")!.Price);
    }

    [Theory]
    [InlineData("""{ "kind": "splash", "range": 0, "damage": "1d6", "damageType": "acid", "splash": 1 }""", "use.range")]
    [InlineData("""{ "kind": "splash", "range": 10, "damage": "1dx", "damageType": "acid", "splash": 1 }""", "use.damage")]
    [InlineData("""{ "kind": "splash", "range": 10, "damage": "1d6", "burn": "lots", "splash": 1 }""", "use.burn")]
    [InlineData("""{ "kind": "tanglefoot", "range": 10, "save": 15, "rounds": "2d", "escape": 17 }""", "use.rounds")]
    [InlineData("""{ "kind": "thunderstone", "range": 20, "save": 15 }""", "use")]
    [InlineData("""{ "range": 10 }""", "use.kind")]
    public void SomethingThrownNeedsARangeAndDiceThatRead(string use, string field)
    {
        var library = WithItem($$"""
            { "kind": "item", "id": "flask", "name": "flask", "type": "consumable", "cost": 1, "use": {{use}} }
            """);

        Assert.Contains(library.Problems, problem => problem.Field == field);
    }

    [Fact]
    public void AConsumableHangsOnTheBeltAndNowhereElse()
    {
        var worn = WithItem("""
            { "kind": "item", "id": "flask", "name": "flask", "slot": "Neck", "type": "consumable", "cost": 1,
              "use": { "kind": "splash", "range": 10, "damage": "1d6", "damageType": "acid", "splash": 1 } }
            """);
        var untyped = WithItem("""
            { "kind": "item", "id": "flask", "name": "flask", "type": "consumable", "cost": 1 }
            """);

        Assert.Contains(worn.Problems, problem => problem.Field == "slot");
        Assert.Contains(untyped.Problems, problem => problem.Field == "use");
    }

    [Fact]
    public void AUseIsEnoughToMakeAnItemAConsumable()
    {
        var library = WithItem("""
            { "kind": "item", "id": "flask", "name": "flask", "cost": 1,
              "use": { "kind": "splash", "range": 10, "damage": "1d6", "damageType": "acid", "splash": 1 } }
            """);

        Assert.Empty(library.Problems);
        Assert.Equal(ItemKind.Consumable, library.GetItem("flask")!.Kind);
    }

    /// <summary>Every shipped spell, and one item file — enough for a potion to find its spell.</summary>
    private static ContentLibrary WithItem(string json) => ContentLibrary.Load(
        TestContent.Files().Where(file => file.Source.StartsWith("spells/", StringComparison.Ordinal))
            .Append(("items/zz-test.json", json)));
}

public class PotionMagicTests
{
    private static ConsumableDefinition Use(string id) => TestContent.Library.GetItem(id)!.Consumable!;

    [Fact]
    public void ACureLightWoundsPotionHealsOneDieAndOne()
    {
        var drinker = ClassKit.Dummy("Drinker", hitPoints: 30);
        drinker.HitPoints.Take(20);
        var use = Use("potion-of-cure-light-wounds");

        var cast = Casting.Resolve(drinker, use.Spell!, SpellAim.At(drinker), new SequenceRandom(6), how: Invocation.FromItem(use, "drinks"));

        Assert.Equal(7, Assert.Single(cast.Targets).Healed);
        Assert.Same(drinker, cast.Targets[0].Target);
        Assert.Equal("drinks", cast.Verb);
    }

    [Fact]
    public void AHealingClericsPotionIsNotBlessedThoughHerOwnCureIs()
    {
        var healer = ClassKit.Make("cleric", 6, "\"deity\": \"cihua-couatl\", \"domains\": [\"healing\", \"war\"]");
        healer.HitPoints.Take(30);
        var use = Use("potion-of-cure-light-wounds");

        var potion = Casting.Resolve(healer, use.Spell!, SpellAim.At(healer), new SequenceRandom(4), how: Invocation.FromItem(use, "drinks"));
        var own = Casting.Resolve(healer, use.Spell!, SpellAim.At(healer), new SequenceRandom(4));

        Assert.Equal(5, potion.Targets.Single().Healed);   // 4, and 1 for caster level 1
        Assert.Equal(13, own.Targets.Single().Healed);     // 4 and 5, half as much again
    }

    [Fact]
    public void MagicThatIsNotHerOwnGetsNoIntenseBonus()
    {
        var merrin = TestContent.Library.BuildCreature("merrin")!;
        var target = ClassKit.Dummy("Target", hitPoints: 100);
        merrin.Allegiance = 1;
        target.Allegiance = 2;
        var missile = TestContent.Library.GetSpell("magic-missile")!;

        var borrowed = Casting.Resolve(merrin, missile, SpellAim.At(target), new SequenceRandom(true, 1), how: Invocation.Spell with { OwnMagic = false });

        // Three missiles of 1d4+1, every die a 1, and not the two her intense spells would add.
        Assert.Equal(6, borrowed.Targets.Single().Damage);
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(1, 11)]
    [InlineData(2, 13)]
    [InlineData(3, 14)]
    public void APotionsSaveIsTenItsLevelAndHalfItsLevel(int spellLevel, int dc) =>
        Assert.Equal(dc, Consumables.PotionDc(spellLevel));

    [Fact]
    public void APotionIsCastAtItsOwnLevelWhoeverDrinksIt()
    {
        var use = Use("potion-of-mage-armor");
        var how = Invocation.FromItem(use, "drinks");
        var wizard = SpellcastingTests.Wizard(casterLevel: 5);

        Casting.Resolve(wizard, use.Spell!, SpellAim.At(wizard), new SequenceRandom(1), how: how);

        Assert.Equal((1, 11, false), (how.CasterLevel, how.DifficultyClass, how.OwnMagic));
        Assert.True(wizard.Effects.Has("Mage Armor"));
        Assert.Equal(Duration.Hours(1), wizard.Effects.Find("Mage Armor")!.Remaining);
    }

    [Theory]
    [InlineData(1, 7)]
    [InlineData(3, 9)]
    [InlineData(5, 11)]
    [InlineData(9, 11)]
    public void CureLightWoundsAddsACasterLevelToAtMostFive(int casterLevel, int healed)
    {
        var cleric = SpellcastingTests.Wizard(casterLevel);
        var friend = ClassKit.Dummy("Friend", hitPoints: 40);
        friend.Allegiance = cleric.Allegiance;
        friend.HitPoints.Take(30);

        var cast = Casting.Resolve(cleric, Spells.CureLightWounds, SpellAim.At(friend), new SequenceRandom(6));

        Assert.Equal(healed, cast.Targets.Single().Healed);
    }

    [Fact]
    public void FlatDamageGoesThroughResistanceAndNotReduction()
    {
        var target = ClassKit.Dummy("Target", hitPoints: 30);
        target.Defenses.Resist(DamageType.Fire, 5);
        target.Defenses.Add(new DamageReduction(10, DamageBypass.Silver));

        Assert.Equal(1, Casting.Hurt(target, 6, DamageType.Fire, new SequenceRandom(1)));
        Assert.Equal(6, Casting.Hurt(target, 6, DamageType.Acid, new SequenceRandom(1)));
        Assert.Equal(23, target.HitPoints.Current);
    }
}

public class BeltTests
{
    private static ItemDefinition Item(string id) => TestContent.Library.GetItem(id)!;

    private static Creature Carrying(params string[] ids)
    {
        var creature = ClassKit.Dummy("Carrier");
        foreach (var id in ids)
        {
            TestContent.Library.Equip(creature, Item(id), EquipmentSlot.Carried);
        }

        return creature;
    }

    [Fact]
    public void ThreePotionsAreThreeEntriesAndOneStack()
    {
        var carrier = Carrying(
            "potion-of-cure-light-wounds", "dagger", "acid-flask", "potion-of-cure-light-wounds", "potion-of-cure-light-wounds");

        Assert.Equal(3, carrier.Equipment.Worn.Count(entry => entry.Item.Id == "potion-of-cure-light-wounds"));
        Assert.Equal(
            [new BeltStack(Item("potion-of-cure-light-wounds"), 3), new BeltStack(Item("acid-flask"), 1)],
            Consumables.OnBelt(carrier));
        Assert.Equal(3, Consumables.Count(carrier, Item("potion-of-cure-light-wounds")));
        Assert.Equal(0, Consumables.Count(carrier, Item("thunderstone")));
        Assert.Equal("potion of cure light wounds ×3", Consumables.OnBelt(carrier)[0].ToString());
    }

    [Fact]
    public void UsingOneUpTakesOneAwayForGood()
    {
        var carrier = Carrying("alchemists-fire", "alchemists-fire");
        var fire = Item("alchemists-fire");

        Assert.True(carrier.Equipment.UseUp(fire));
        Assert.Equal(0, carrier.Equipment.Recover());
        Assert.Equal(1, Consumables.Count(carrier, fire));

        Assert.True(carrier.Equipment.UseUp(fire));
        Assert.False(carrier.Equipment.UseUp(fire));
        Assert.Empty(Consumables.OnBelt(carrier));
    }

    [Fact]
    public void WhatHangsOnTheBeltWeighsOnTheLoad()
    {
        var carrier = Carrying();
        var before = Encumbrance.Carried(carrier);

        TestContent.Library.Equip(carrier, Item("tanglefoot-bag"), EquipmentSlot.Carried);
        TestContent.Library.Equip(carrier, Item("tanglefoot-bag"), EquipmentSlot.Carried);
        TestContent.Library.Equip(carrier, Item("potion-of-cure-light-wounds"), EquipmentSlot.Carried);

        Assert.Equal(before + 8, Encumbrance.Carried(carrier));
    }
}

public class ConsumableDescriptionTests
{
    private static IReadOnlyList<string> Describe(string id) =>
        TestContent.Library.DescribeItem(TestContent.Library.GetItem(id)!);

    [Fact]
    public void APotionSaysWhatDrinkingItDoes()
    {
        Assert.Contains("Drink: cure light wounds at caster level 1 (1d8+1).", Describe("potion-of-cure-light-wounds"));
        Assert.Contains("Drink: mage armor at caster level 1 (+4 armour to armour class for 1 hour).", Describe("potion-of-mage-armor"));
        Assert.Contains("50 gp · weighs next to nothing", Describe("potion-of-cure-light-wounds"));
        Assert.Equal("Potion", Describe("potion-of-cure-light-wounds")[0]);
    }

    [Fact]
    public void AFlaskSaysWhatThrowingItDoes()
    {
        Assert.Contains(
            "Thrown: ranged touch, 10 ft increments; 1d6 fire, 1 fire splash, burns 1d6 next round.", Describe("alchemists-fire"));
        Assert.Contains("Thrown: ranged touch, 10 ft increments; 1d6 acid, 1 acid splash.", Describe("acid-flask"));
        Assert.Contains("10 gp · 1 lb", Describe("acid-flask"));
        Assert.Contains(
            "Thrown at a square, 20 ft increments; everyone within 10 ft makes a DC 15 Fortitude save or is deafened for 1 hour.",
            Describe("thunderstone"));
    }
}
