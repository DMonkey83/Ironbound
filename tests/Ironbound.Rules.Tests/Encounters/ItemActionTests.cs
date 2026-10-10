using Ironbound.Rules.Abilities;
using Ironbound.Rules.Combat;
using Ironbound.Rules.Conditions;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Defense;
using Ironbound.Rules.Dice;
using Ironbound.Rules.Effects;
using Ironbound.Rules.Encounters;
using Ironbound.Rules.Encounters.Actions;
using Ironbound.Rules.Items;
using Ironbound.Rules.Maps;
using Ironbound.Rules.Persistence;
using Ironbound.Rules.Skills;
using Ironbound.Rules.Tests.Classes;

namespace Ironbound.Rules.Tests.Encounters;

/// <summary>Shared pieces for the consumable action tests.</summary>
internal static class Belt
{
    public const string Cure = "potion-of-cure-light-wounds";
    public const string Acid = "acid-flask";
    public const string Fire = "alchemists-fire";
    public const string Bag = "tanglefoot-bag";
    public const string Stone = "thunderstone";

    public static ItemDefinition Item(string id) => TestContent.Library.GetItem(id)!;

    /// <summary>Hangs things on a creature's belt and hands it back.</summary>
    public static Creature Hang(Creature creature, params string[] ids)
    {
        foreach (var id in ids)
        {
            Assert.True(TestContent.Library.Equip(creature, Item(id), EquipmentSlot.Carried));
        }

        return creature;
    }

    /// <summary>A dummy with Dexterity 14 — two to hit with anything thrown — and these on its belt.</summary>
    public static Creature Thrower(params string[] ids) => Hang(ClassKit.Dummy("Thrower", dexterity: 14), ids);

    public static int Count(Creature creature, string id) => Consumables.Count(creature, Item(id));

    public static ThrowResult Throw(Turn turn, ThrowItemAction action) => Assert.IsType<ThrowResult>(turn.Take(action));
}

public class DrinkPotionTests
{
    [Fact]
    public void DrinkingIsAStandardActionThatProvokesHealsAndUsesExactlyOne()
    {
        var drinker = Belt.Hang(ClassKit.Dummy("Drinker", hitPoints: 30), Belt.Cure, Belt.Cure);
        drinker.HitPoints.Take(20);
        var orc = ClassKit.Dummy("Orc");
        var field = ClassKit.Field((drinker, 4, 4), (orc, 5, 4));

        // The orc's swing: a 2, a miss. The potion's d8: 5.
        var turn = ClassKit.Fight([drinker], [orc], field, 2, 5).BeginNextTurn()!;
        var result = Assert.IsType<PotionResult>(turn.Take(new DrinkPotionAction(Belt.Item(Belt.Cure))));

        Assert.False(Assert.Single(result.Opportunities).IsHit);
        Assert.True(result.Used);
        Assert.Same(drinker, result.Drinker);
        Assert.Equal(6, result.Outcome!.Healed);   // 5, and 1 for caster level 1
        Assert.Equal(16, drinker.HitPoints.Current);
        Assert.Equal(1, Belt.Count(drinker, Belt.Cure));
        Assert.Equal((false, true), (turn.Budget.HasStandard, turn.Budget.HasMove));
        Assert.Equal("Drinker drinks the potion of cure light wounds, provoking 1", result.Description);
    }

    [Fact]
    public void CutDownByTheSwingItDrewTheDrinkerUsesNothing()
    {
        var drinker = Belt.Hang(ClassKit.Dummy("Drinker", hitPoints: 30), Belt.Cure, Belt.Cure);
        drinker.HitPoints.Take(29);
        var orc = ClassKit.Dummy("Orc");
        var field = ClassKit.Field((drinker, 4, 4), (orc, 5, 4));

        // A 15 hits; the club's 6 puts the drinker down before the stopper is out.
        var turn = ClassKit.Fight([drinker], [orc], field, 15, 6).BeginNextTurn()!;
        var result = Assert.IsType<PotionResult>(turn.Take(new DrinkPotionAction(Belt.Item(Belt.Cure))));

        Assert.False(drinker.IsConscious);
        Assert.False(result.Used);
        Assert.Null(result.Cast);
        Assert.Equal(2, Belt.Count(drinker, Belt.Cure));
        Assert.Equal("Drinker is cut down drinking the potion of cure light wounds, provoking 1", result.Description);
    }

    [Fact]
    public void OnlyWhatIsOnTheBeltAndIsAPotionCanBeDrunk()
    {
        var drinker = Belt.Hang(ClassKit.Dummy("Drinker"), Belt.Acid);
        var turn = ClassKit.Fight([drinker], [ClassKit.Dummy("Orc")], null).BeginNextTurn()!;

        Assert.False(turn.CanTake(new DrinkPotionAction(Belt.Item(Belt.Cure))));
        Assert.False(turn.CanTake(new DrinkPotionAction(Belt.Item(Belt.Acid))));
    }

    [Fact]
    public void AnOilIsAppliedAndItsMagicLandsOnTheOneWhoAppliesIt()
    {
        var fighter = Belt.Hang(ClassKit.Dummy("Fighter"), "oil-of-magic-weapon");
        var turn = ClassKit.Fight([fighter], [ClassKit.Dummy("Orc")], null).BeginNextTurn()!;

        var result = Assert.IsType<PotionResult>(turn.Take(new DrinkPotionAction(Belt.Item("oil-of-magic-weapon"))));

        Assert.Equal("Fighter applies the oil of magic weapon", result.Description);
        Assert.True(fighter.Effects.Has("Magic Weapon"));
        Assert.Equal(0, Belt.Count(fighter, "oil-of-magic-weapon"));
    }
}

public class AdministerPotionTests
{
    [Fact]
    public void GivingAPotionToAFallenFriendTakesTheRoundAndTheGiversOwnPotion()
    {
        var giver = Belt.Hang(ClassKit.Dummy("Giver"), Belt.Cure);
        var patient = Belt.Hang(ClassKit.Dummy("Patient", hitPoints: 20), Belt.Cure);
        patient.HitPoints.Take(22);
        var field = ClassKit.Field((giver, 4, 4), (patient, 5, 4), (ClassKit.Dummy("Orc"), 10, 10));
        var encounter = ClassKit.Fight([giver, patient], [field.Creatures[2]], field, 4);
        var turn = encounter.BeginNextTurn()!;

        var result = Assert.IsType<PotionResult>(turn.Take(new AdministerPotionAction(Belt.Item(Belt.Cure), patient)));

        Assert.Same(patient, result.Drinker);
        Assert.Same(patient, result.Outcome!.Target);
        Assert.Equal(3, patient.HitPoints.Current);   // -2, and 4 and 1
        Assert.True(patient.IsConscious);
        Assert.Equal(0, Belt.Count(giver, Belt.Cure));
        Assert.Equal(1, Belt.Count(patient, Belt.Cure));
        Assert.Equal((false, false), (turn.Budget.HasStandard, turn.Budget.HasMove));
        Assert.Equal("Giver gives Patient the potion of cure light wounds", result.Description);
    }

    [Fact]
    public void OnlyAFallenFriendWithinReachCanBeGivenOne()
    {
        var giver = Belt.Hang(ClassKit.Dummy("Giver"), Belt.Cure, Belt.Acid);
        var awake = ClassKit.Dummy("Awake");
        var distant = ClassKit.Dummy("Distant");
        var foe = ClassKit.Dummy("Foe");
        var field = ClassKit.Field((giver, 4, 4), (awake, 5, 4), (distant, 8, 8), (foe, 3, 4));
        var turn = ClassKit.Fight([giver, awake, distant], [foe], field).BeginNextTurn()!;
        var cure = Belt.Item(Belt.Cure);

        distant.HitPoints.Take(62);
        foe.HitPoints.Take(62);

        Assert.False(turn.CanTake(new AdministerPotionAction(cure, awake)));
        Assert.False(turn.CanTake(new AdministerPotionAction(cure, distant)));
        Assert.False(turn.CanTake(new AdministerPotionAction(cure, foe)));
        Assert.False(turn.CanTake(new AdministerPotionAction(cure, giver)));

        awake.HitPoints.Take(62);

        Assert.True(turn.CanTake(new AdministerPotionAction(cure, awake)));
        Assert.False(turn.CanTake(new AdministerPotionAction(Belt.Item(Belt.Acid), awake)));

        // A full round: not once the move has gone.
        turn.Budget.Spend(ActionCost.Move);
        Assert.False(turn.CanTake(new AdministerPotionAction(cure, awake)));
    }
}

public class ThrowItemTests
{
    [Fact]
    public void AFlaskIsThrownAtTouchArmourClass()
    {
        var thrower = Belt.Thrower(Belt.Acid);
        var orc = ClassKit.Dummy("Orc", armour: 6);
        var field = ClassKit.Field((thrower, 0, 0), (orc, 2, 0));

        // 8, and Dexterity's 2, is 10: short of the orc's 16, enough for its touch 10. Then 4 acid.
        var turn = ClassKit.Fight([thrower], [orc], field, 8, 4).BeginNextTurn()!;
        var result = Belt.Throw(turn, ThrowItemAction.At(Belt.Item(Belt.Acid), orc));

        Assert.True(result.IsHit);
        Assert.Equal(10, result.Attack!.TargetArmorClass);
        Assert.Same(orc, result.Target);
        Assert.Equal(new GridSquare(2, 0), result.Landed);
        Assert.Equal(56, orc.HitPoints.Current);
        Assert.Equal((orc, 4, true), (result.Effects[0].Creature, result.Effects[0].Damage, result.Effects[0].Direct));
        Assert.Equal(0, Belt.Count(thrower, Belt.Acid));
        Assert.False(turn.Budget.HasStandard);
        // The orc has not had a turn yet: flat-footed, which costs a Dexterity of 10 nothing.
        Assert.Equal("Thrower throws the flask of acid at Orc: d20 [8] +2 = 10 vs touch flat-footed AC 10 — hit", result.Description);
        Assert.Equal("Orc: 4 acid damage (56/60 hp)", result.Effects[0].ToString());
    }

    [Theory]
    [InlineData(2, 0)]
    [InlineData(3, -2)]
    [InlineData(5, -4)]
    public void EveryRangeIncrementPastTheFirstCostsTwo(int x, int penalty)
    {
        var thrower = Belt.Thrower(Belt.Acid);
        var orc = ClassKit.Dummy("Orc");
        var field = ClassKit.Field((thrower, 0, 0), (orc, x, 0));
        ClassKit.Fight([thrower], [orc], field);

        Assert.Equal(2 + penalty, ThrowItemAction.AttackBonus(thrower, Belt.Item(Belt.Acid), orc, field).Total);
    }

    [Fact]
    public void TheThrowItselfTakesTheRangePenalty()
    {
        var thrower = Belt.Thrower(Belt.Acid);
        var orc = ClassKit.Dummy("Orc");
        var field = ClassKit.Field((thrower, 0, 0), (orc, 5, 0));

        var result = Belt.Throw(ClassKit.Fight([thrower], [orc], field, 12, 1).BeginNextTurn()!, ThrowItemAction.At(Belt.Item(Belt.Acid), orc));

        Assert.Equal(-2, result.Attack!.Bonus.Total);
        Assert.Contains(result.Attack.Bonus.Applied, entry => entry.Modifier is { Source: "Range (25 ft)", Value: -4 });
    }

    [Fact]
    public void NothingIsThrownPastFiveIncrements()
    {
        var thrower = Belt.Thrower(Belt.Acid, Belt.Stone);
        var orc = ClassKit.Dummy("Orc");
        var field = new Battlefield(30, 3);
        field.Place(thrower, 0, 1);
        field.Place(orc, 10, 1);
        var turn = ClassKit.Fight([thrower], [orc], field).BeginNextTurn()!;
        var acid = Belt.Item(Belt.Acid);
        var stone = Belt.Item(Belt.Stone);

        Assert.True(turn.CanTake(ThrowItemAction.At(acid, orc)));
        Assert.True(turn.CanTake(ThrowItemAction.At(acid, new GridSquare(10, 0))));
        Assert.True(turn.CanTake(ThrowItemAction.At(stone, new GridSquare(20, 1))));
        Assert.False(turn.CanTake(ThrowItemAction.At(stone, new GridSquare(21, 1))));

        field.Place(orc, 11, 1);

        Assert.False(turn.CanTake(ThrowItemAction.At(acid, orc)));
        Assert.False(turn.CanTake(ThrowItemAction.At(acid, new GridSquare(11, 0))));
    }

    [Fact]
    public void EachThingIsThrownOnlyAtWhatItCanBeThrownAt()
    {
        var thrower = Belt.Thrower(Belt.Acid, Belt.Bag, Belt.Stone);
        var orc = ClassKit.Dummy("Orc");
        var field = ClassKit.Field((thrower, 0, 0), (orc, 3, 0));
        field.Block(new GridSquare(2, 5));
        var turn = ClassKit.Fight([thrower], [orc], field).BeginNextTurn()!;

        Assert.True(turn.CanTake(ThrowItemAction.At(Belt.Item(Belt.Acid), orc)));
        Assert.True(turn.CanTake(ThrowItemAction.At(Belt.Item(Belt.Acid), new GridSquare(3, 3))));
        Assert.True(turn.CanTake(ThrowItemAction.At(Belt.Item(Belt.Bag), orc)));
        Assert.False(turn.CanTake(ThrowItemAction.At(Belt.Item(Belt.Bag), new GridSquare(3, 3))));
        Assert.True(turn.CanTake(ThrowItemAction.At(Belt.Item(Belt.Stone), orc)));
        Assert.True(turn.CanTake(ThrowItemAction.At(Belt.Item(Belt.Stone), new GridSquare(3, 3))));

        // Not into a wall, not at oneself, not with nothing on the belt, not lying down.
        Assert.False(turn.CanTake(ThrowItemAction.At(Belt.Item(Belt.Stone), new GridSquare(2, 5))));
        Assert.False(turn.CanTake(ThrowItemAction.At(Belt.Item(Belt.Acid), thrower)));
        Assert.False(turn.CanTake(ThrowItemAction.At(Belt.Item(Belt.Fire), orc)));
        Assert.False(turn.CanTake(ThrowItemAction.At(Belt.Item(Belt.Cure), orc)));

        thrower.Effects.Apply(ConditionInfo.Effect(Condition.Prone, Duration.Permanent));
        Assert.False(turn.CanTake(ThrowItemAction.At(Belt.Item(Belt.Acid), orc)));
    }

    [Fact]
    public void NotWithoutALineToTheTarget()
    {
        var thrower = Belt.Thrower(Belt.Acid);
        var orc = ClassKit.Dummy("Orc");
        // Off the level: a line along the seam between two wall squares counts as clear.
        var field = ClassKit.Field((thrower, 0, 5), (orc, 4, 7));
        for (var y = 0; y < 12; y++)
        {
            field.Block(new GridSquare(2, y));
        }

        var turn = ClassKit.Fight([thrower], [orc], field).BeginNextTurn()!;

        Assert.False(turn.CanTake(ThrowItemAction.At(Belt.Item(Belt.Acid), orc)));
    }

    [Fact]
    public void NobodyNeedsTrainingToThrowAFlask()
    {
        var wizard = ClassKit.Make("wizard", 1);
        var orc = ClassKit.Dummy("Orc");
        var field = ClassKit.Field((wizard, 0, 0), (orc, 2, 0));
        ClassKit.Fight([wizard], [orc], field);

        var bonus = ThrowItemAction.AttackBonus(wizard, Belt.Item(Belt.Acid), orc, field);

        // Dexterity 14 and nothing else: no -4 for a weapon the wizard was never taught.
        Assert.Equal(2, bonus.Total);
        Assert.DoesNotContain(bonus.Entries, entry => entry.Modifier.Value < 0);
    }

    [Fact]
    public void AFlankingRogueThrowsNoSneakAttack()
    {
        var rogue = Belt.Hang(ClassKit.Make("rogue", 1, "\"items\": [\"short-sword\"]"), Belt.Acid);
        var ally = ClassKit.Dummy("Ally");
        var orc = ClassKit.Dummy("Orc");
        var field = ClassKit.Field((rogue, 4, 5), (orc, 5, 5), (ally, 6, 5));

        // The orc goes first, so it is not flat-footed and only the flank could give a sneak
        // attack. Its swing at the thrower: a 1. The throw: 14, less 4 for firing into the melee
        // the ally is in, against touch 10; and 3 acid — and no sneak attack die, or the
        // sequence would run dry.
        var encounter = ClassKit.Fight([orc], [rogue, ally], field, 1, 14, 3);
        encounter.BeginNextTurn()!.End();
        var turn = encounter.BeginNextTurn()!;
        Assert.Same(ally, Strike.FlankingPartner(rogue, orc, field));

        var result = Belt.Throw(turn, ThrowItemAction.At(Belt.Item(Belt.Acid), orc));

        Assert.True(result.IsHit);
        Assert.DoesNotContain(result.Attack!.Bonus.Applied, entry => entry.Modifier.Source.StartsWith("Flanking", StringComparison.Ordinal));
        Assert.Equal(3, result.Effects.Single(effect => effect.Direct).Damage);
        Assert.Equal(57, orc.HitPoints.Current);
    }

    [Fact]
    public void NoStanceRidesOnAThrowButFightingDefensivelyStillCosts()
    {
        var fighter = Belt.Hang(
            ClassKit.Make("fighter", 4, "\"feats\": [\"power-attack\", \"deadly-aim\"]", [16, 16, 14, 10, 10, 10]), Belt.Acid);
        var orc = ClassKit.Dummy("Orc");
        var field = ClassKit.Field((fighter, 0, 0), (orc, 2, 0));
        ClassKit.Fight([fighter], [orc], field);
        var plain = ThrowItemAction.AttackBonus(fighter, Belt.Item(Belt.Acid), orc, field).Total;

        fighter.Stances.Adopt(Stance.PowerAttack);
        fighter.Stances.Adopt(Stance.DeadlyAim);
        Assert.Equal(plain, ThrowItemAction.AttackBonus(fighter, Belt.Item(Belt.Acid), orc, field).Total);

        fighter.Stances.Adopt(Stance.FightingDefensively);
        Assert.Equal(plain - 4, ThrowItemAction.AttackBonus(fighter, Belt.Item(Belt.Acid), orc, field).Total);
    }

    [Fact]
    public void AThrownFlaskIsNotThereToPickUpAfterwards()
    {
        var thrower = Belt.Hang(ClassKit.Dummy("Thrower"), Belt.Acid, "dagger");
        var orc = ClassKit.Dummy("Orc");
        var field = ClassKit.Field((thrower, 0, 0), (orc, 2, 0));

        Belt.Throw(ClassKit.Fight([thrower], [orc], field, 2, 4).BeginNextTurn()!, ThrowItemAction.At(Belt.Item(Belt.Acid), orc));

        Assert.Equal(0, thrower.Equipment.Recover());
        Assert.Equal(0, Belt.Count(thrower, Belt.Acid));
        Assert.DoesNotContain(thrower.Equipment.Worn, entry => entry.Item.Id == Belt.Acid);
        Assert.DoesNotContain(thrower.Attacks, attack => attack.Name.Contains("acid", StringComparison.Ordinal));
    }
}

public class SplashTests
{
    [Fact]
    public void TheSplashCatchesEverybodyElseBesideTheTargetAndNotTheTarget()
    {
        var thrower = Belt.Thrower(Belt.Acid);
        var orc = ClassKit.Dummy("Orc");
        var friend = ClassKit.Dummy("Friend");
        var far = ClassKit.Dummy("Far");
        var field = ClassKit.Field((thrower, 0, 0), (orc, 3, 0), (friend, 4, 1), (far, 6, 0));

        var turn = ClassKit.Fight([thrower, friend], [orc, far], field, 15, 4).BeginNextTurn()!;
        var result = Belt.Throw(turn, ThrowItemAction.At(Belt.Item(Belt.Acid), orc));

        Assert.Equal(56, orc.HitPoints.Current);
        Assert.Equal(59, friend.HitPoints.Current);
        Assert.Equal(60, far.HitPoints.Current);
        Assert.Equal([(orc, 4, true), (friend, 1, false)], result.Effects.Select(effect => (effect.Creature, effect.Damage, effect.Direct)));
        Assert.Equal("Friend: 1 acid splash (59/60 hp)", result.Effects[1].ToString());
    }

    [Fact]
    public void AThrowerNextToTheTargetIsSplashedToo()
    {
        var thrower = Belt.Thrower(Belt.Acid);
        var orc = ClassKit.Dummy("Orc");
        var field = ClassKit.Field((thrower, 2, 0), (orc, 3, 0));

        // The orc's swing at the thrower for throwing in its reach: a 1. Then 15 and 4.
        var turn = ClassKit.Fight([thrower], [orc], field, 1, 15, 4).BeginNextTurn()!;
        var result = Belt.Throw(turn, ThrowItemAction.At(Belt.Item(Belt.Acid), orc));

        Assert.Single(result.Opportunities);
        Assert.Equal(59, thrower.HitPoints.Current);
        Assert.Contains("provoking 1", result.Description);
    }

    [Fact]
    public void ResistanceCountsAndDamageReductionDoesNot()
    {
        var thrower = Belt.Thrower(Belt.Fire);
        var orc = ClassKit.Dummy("Orc");
        orc.Defenses.Resist(DamageType.Fire, 2);
        orc.Defenses.Add(new DamageReduction(10, DamageBypass.Silver));
        var field = ClassKit.Field((thrower, 0, 0), (orc, 2, 0));

        var result = Belt.Throw(ClassKit.Fight([thrower], [orc], field, 15, 5).BeginNextTurn()!, ThrowItemAction.At(Belt.Item(Belt.Fire), orc));

        Assert.Equal(3, result.Effects[0].Damage);
        Assert.Equal(57, orc.HitPoints.Current);
    }

    [Fact]
    public void ACriticalDoublesTheFlasksOwnDiceAndNothingElse()
    {
        var thrower = Belt.Thrower(Belt.Fire);
        var orc = ClassKit.Dummy("Orc");
        var friend = ClassKit.Dummy("Friend");
        var field = ClassKit.Field((thrower, 0, 0), (orc, 3, 0), (friend, 4, 0));

        // A 20, confirmed with a 15; the flask's d6 twice, 3 and 4.
        var turn = ClassKit.Fight([thrower, friend], [orc], field, 20, 15, 3, 4).BeginNextTurn()!;
        var result = Belt.Throw(turn, ThrowItemAction.At(Belt.Item(Belt.Fire), orc));

        Assert.True(result.Attack!.IsCritical);
        Assert.Equal(7, result.Effects[0].Damage);
        Assert.Equal(1, result.Effects[1].Damage);
        Assert.Equal("1d6", Assert.IsType<DamageOverTimeEffect>(orc.Effects.Find("Alchemist's fire")).Amount.ToString());
    }

    [Fact]
    public void AlchemistsFireBurnsOnceARoundLaterAfterTheTargetHasHadItsTurn()
    {
        var thrower = Belt.Thrower(Belt.Fire);
        var orc = ClassKit.Dummy("Orc");
        var field = ClassKit.Field((thrower, 0, 0), (orc, 3, 0));

        // The throw: 15 and 3. The burn, a round on: 5. Nothing else, ever.
        var encounter = ClassKit.Fight([thrower], [orc], field, 15, 3, 5);
        var result = Belt.Throw(encounter.BeginNextTurn()!, ThrowItemAction.At(Belt.Item(Belt.Fire), orc));
        Assert.Contains("burning: 1d6 more next round unless put out", result.Effects[0].Applied);
        Assert.True(PutOutFlamesAction.IsBurning(orc));

        var orcs = encounter.BeginNextTurn()!;
        Assert.Same(orc, orcs.Actor);
        Assert.Equal(57, orc.HitPoints.Current);
        Assert.True(orcs.CanTake(new PutOutFlamesAction()));

        var next = encounter.BeginNextTurn()!;
        Assert.Same(thrower, next.Actor);
        Assert.Equal(52, orc.HitPoints.Current);
        Assert.Contains(next.Events, happened => happened.Description.StartsWith("Orc takes 5 from Alchemist's fire", StringComparison.Ordinal));
        Assert.False(PutOutFlamesAction.IsBurning(orc));

        encounter.BeginNextTurn();
        encounter.BeginNextTurn();
        encounter.BeginNextTurn();
        Assert.Equal(52, orc.HitPoints.Current);
    }

    [Fact]
    public void PuttingTheFlamesOutStopsTheBurn()
    {
        var thrower = Belt.Thrower(Belt.Fire);
        var orc = ClassKit.Dummy("Orc");
        var field = ClassKit.Field((thrower, 0, 0), (orc, 3, 0));

        // The throw: 15 and 3. The orc's Reflex: 13, and 2 for rolling on the ground.
        var encounter = ClassKit.Fight([thrower], [orc], field, 15, 3, 13);
        Belt.Throw(encounter.BeginNextTurn()!, ThrowItemAction.At(Belt.Item(Belt.Fire), orc));

        var orcs = encounter.BeginNextTurn()!;
        var rolled = Assert.IsType<PutOutFlamesResult>(orcs.Take(new PutOutFlamesAction()));

        Assert.True(rolled.PutOut);
        Assert.Equal(15, rolled.Save.Total);
        Assert.Equal("Orc rolls on the ground and puts the flames out", rolled.Description);
        Assert.Equal((false, false), (orcs.Budget.HasStandard, orcs.Budget.HasMove));
        Assert.False(PutOutFlamesAction.IsBurning(orc));

        encounter.BeginNextTurn();
        encounter.BeginNextTurn();
        Assert.Equal(57, orc.HitPoints.Current);
    }

    [Fact]
    public void FailingToPutItOutLeavesItBurning()
    {
        var thrower = Belt.Thrower(Belt.Fire);
        var orc = ClassKit.Dummy("Orc");
        var field = ClassKit.Field((thrower, 0, 0), (orc, 3, 0));

        var encounter = ClassKit.Fight([thrower], [orc], field, 15, 3, 12, 5);
        Belt.Throw(encounter.BeginNextTurn()!, ThrowItemAction.At(Belt.Item(Belt.Fire), orc));
        var rolled = Assert.IsType<PutOutFlamesResult>(encounter.BeginNextTurn()!.Take(new PutOutFlamesAction()));

        Assert.False(rolled.PutOut);
        Assert.Equal("Orc rolls on the ground but is still burning", rolled.Description);

        encounter.BeginNextTurn();
        Assert.Equal(52, orc.HitPoints.Current);
    }

    [Fact]
    public void NobodyPutsOutAFireThatIsNotThere()
    {
        var orc = ClassKit.Dummy("Orc");
        var turn = ClassKit.Fight([orc], [ClassKit.Dummy("Other")], null).BeginNextTurn()!;

        Assert.False(turn.CanTake(new PutOutFlamesAction()));
    }
}

public class MissAndSquareTests
{
    private static readonly GridSquare West = new(0, 5);
    private static readonly GridSquare Middle = new(5, 5);

    [Theory]
    [InlineData(1, 3, 5)]
    [InlineData(2, 3, 3)]
    [InlineData(3, 5, 3)]
    [InlineData(4, 7, 3)]
    [InlineData(5, 7, 5)]
    [InlineData(6, 7, 7)]
    [InlineData(7, 5, 7)]
    [InlineData(8, 3, 7)]
    public void AMissGoesClockwiseRoundTheAimPointFromShort(int die, int x, int y) =>
        Assert.Equal(new GridSquare(x, y), ThrowItemAction.Deviate(new Battlefield(12, 12), West, Middle, die, 2));

    [Fact]
    public void ShortIsWhicheverOfTheEightWaysIsNearestTheThrower()
    {
        var field = new Battlefield(12, 12);

        // Two up and five across is nearer level than diagonal; three up and five across is not.
        Assert.Equal(new GridSquare(4, 5), ThrowItemAction.Deviate(field, new GridSquare(0, 3), Middle, 1, 1));
        Assert.Equal(new GridSquare(4, 4), ThrowItemAction.Deviate(field, new GridSquare(0, 2), Middle, 1, 1));
    }

    [Fact]
    public void AMissStopsAtAWallOrTheEdgeOfTheMap()
    {
        var field = new Battlefield(12, 12);
        field.Block(new GridSquare(8, 5));

        Assert.Equal(new GridSquare(7, 5), ThrowItemAction.Deviate(field, West, Middle, 5, 4));
        Assert.Equal(new GridSquare(11, 2), ThrowItemAction.Deviate(field, new GridSquare(0, 2), new GridSquare(9, 2), 5, 4));
    }

    [Fact]
    public void AMissedFlaskStillBreaksSomewhereAndSplashesThere()
    {
        var thrower = Belt.Thrower(Belt.Acid);
        var friend = ClassKit.Dummy("Friend");
        var orc = ClassKit.Dummy("Orc");
        var field = ClassKit.Field((thrower, 0, 5), (friend, 1, 6), (orc, 4, 5));

        // Twenty feet, so two squares: a 3 misses, and the d8's 1 drops it two short.
        var turn = ClassKit.Fight([thrower, friend], [orc], field, 3, 1).BeginNextTurn()!;
        var result = Belt.Throw(turn, ThrowItemAction.At(Belt.Item(Belt.Acid), orc));

        Assert.False(result.IsHit);
        Assert.Equal(1, result.DeviationDie);
        Assert.Equal(new GridSquare(2, 5), result.Landed);
        Assert.Equal([(friend, 1, false)], result.Effects.Select(effect => (effect.Creature, effect.Damage, effect.Direct)));
        Assert.Equal(60, orc.HitPoints.Current);
        Assert.Equal(0, Belt.Count(thrower, Belt.Acid));
        Assert.EndsWith("— miss; it falls short (d8 [1]) and lands at (2, 5)", result.Description);
    }

    [Fact]
    public void ASquareIsArmourClassFiveAndItsNeighboursAreSplashed()
    {
        var thrower = Belt.Thrower(Belt.Acid);
        var orc = ClassKit.Dummy("Orc", armour: 10);
        var field = ClassKit.Field((thrower, 0, 0), (orc, 2, 3));

        var turn = ClassKit.Fight([thrower], [orc], field, 3).BeginNextTurn()!;
        var result = Belt.Throw(turn, ThrowItemAction.At(Belt.Item(Belt.Acid), new GridSquare(1, 2)));

        Assert.True(result.IsHit);
        Assert.Null(result.Target);
        Assert.Equal(ThrowItemAction.GroundArmorClass, result.Attack!.TargetArmorClass);
        Assert.Equal(new GridSquare(1, 2), result.Landed);
        Assert.Equal([(orc, 1, false)], result.Effects.Select(effect => (effect.Creature, effect.Damage, effect.Direct)));
        Assert.Equal("Thrower throws the flask of acid at (1, 2): d20 [3] +2 = 5 vs AC 5 — hit", result.Description);
    }

    [Fact]
    public void ANaturalOneMissesEvenASquare()
    {
        var thrower = Belt.Hang(ClassKit.Dummy("Thrower", dexterity: 20), Belt.Acid);
        var orc = ClassKit.Dummy("Orc");
        var field = ClassKit.Field((thrower, 0, 0), (orc, 8, 8));

        // A 1 and five for Dexterity would be 6 — but it is a 1. Ten feet is one square long.
        var turn = ClassKit.Fight([thrower], [orc], field, 1, 5).BeginNextTurn()!;
        var result = Belt.Throw(turn, ThrowItemAction.At(Belt.Item(Belt.Acid), new GridSquare(1, 2)));

        Assert.False(result.IsHit);
        Assert.Equal(new GridSquare(2, 3), result.Landed);
    }
}

public class TanglefootTests
{
    /// <summary>A thrower, the orc it throws at, and a friend of the thrower's next to the orc.</summary>
    private static (Encounter Encounter, Creature Thrower, Creature Orc, Creature Friend, Battlefield Field) Setup(params int[] rolls)
    {
        var thrower = Belt.Thrower(Belt.Bag);
        var orc = ClassKit.Dummy("Orc");
        var friend = ClassKit.Dummy("Friend");
        var field = ClassKit.Field((thrower, 0, 0), (orc, 3, 3), (friend, 4, 3));
        return (ClassKit.Fight([thrower, friend], [orc], field, rolls), thrower, orc, friend, field);
    }

    [Fact]
    public void ABagEntanglesAndOnAFailedSaveSticksFast()
    {
        // 15 hits; 2d4 of 2 and 3 is five rounds; the Reflex, 4, less 2 for the goo's Dexterity.
        var (encounter, _, orc, friend, field) = Setup(15, 2, 3, 4);
        var speed = orc.CurrentSpeed;
        var attack = orc.AttackModifiers.Total;

        var result = Belt.Throw(encounter.BeginNextTurn()!, ThrowItemAction.At(Belt.Item(Belt.Bag), orc));

        var stuck = Assert.Single(result.Effects);
        Assert.True(stuck.Save!.Failed);
        Assert.Equal(2, stuck.Save.Total);
        Assert.Equal(["entangled for 5 rounds", "stuck fast"], stuck.Applied);
        Assert.True(orc.Has(Condition.Entangled));
        Assert.True(orc.Has(Condition.Anchored));
        Assert.Equal(attack - 2, orc.AttackModifiers.Total);
        Assert.Equal(6, orc.Abilities[Ability.Dexterity].Score);
        Assert.Equal(0, orc.CurrentSpeed);
        Assert.NotEqual(0, speed);

        encounter.BeginNextTurn();
        var orcs = encounter.BeginNextTurn()!;
        Assert.Same(orc, orcs.Actor);

        Assert.False(orcs.CanTake(new MoveAction([new(3, 3), new(3, 4)])));
        Assert.False(orcs.CanTake(FiveFootStepAction.To(new(3, 3), new(3, 4))));
        Assert.False(orcs.CanTake(new RunAction([new(3, 3), new(3, 4), new(3, 5)])));
        Assert.False(orcs.CanTake(new WithdrawAction([new(3, 3), new(3, 4)])));

        // Stuck is not helpless: the friend beside it can still be hit.
        Assert.True(orcs.CanTake(new AttackAction(orc.MeleeAttack!, friend)));

        // Nor can it crawl, which costs five feet whatever the speed.
        orc.Effects.Apply(ConditionInfo.Effect(Condition.Prone, Duration.Permanent));
        Assert.False(orcs.CanTake(new MoveAction([new(3, 3), new(3, 4)])));
        Assert.Equal(new GridSquare(3, 3), field.SquareOf(orc));
    }

    [Fact]
    public void MakingTheSaveLeavesItEntangledAtHalfSpeed()
    {
        // The Reflex: 19, less 2, is 17.
        var (encounter, _, orc, _, _) = Setup(15, 2, 3, 19);

        Belt.Throw(encounter.BeginNextTurn()!, ThrowItemAction.At(Belt.Item(Belt.Bag), orc));
        encounter.BeginNextTurn();
        var orcs = encounter.BeginNextTurn()!;

        Assert.True(orc.Has(Condition.Entangled));
        Assert.False(orc.Has(Condition.Anchored));
        Assert.Equal(15, orc.CurrentSpeed);
        Assert.True(orcs.CanTake(new MoveAction([new(3, 3), new(3, 4), new(3, 5), new(3, 6)])));
        Assert.False(orcs.CanTake(new MoveAction([new(3, 3), new(3, 4), new(3, 5), new(3, 6), new(3, 7)])));
    }

    [Fact]
    public void SomethingHugeTearsThroughTheGoo()
    {
        // Only the attack is rolled: no rounds, no save.
        var (encounter, _, orc, _, _) = Setup(15);
        orc.Size = CreatureSize.Huge;

        var result = Belt.Throw(encounter.BeginNextTurn()!, ThrowItemAction.At(Belt.Item(Belt.Bag), orc));

        Assert.True(result.IsHit);
        Assert.Equal(["too big to be held"], result.Effects.Single().Applied);
        Assert.False(orc.Has(Condition.Entangled));
        Assert.False(orc.Has(Condition.Anchored));
    }

    [Fact]
    public void AMissedBagIsSimplyGone()
    {
        var (encounter, thrower, orc, _, _) = Setup(2);

        var result = Belt.Throw(encounter.BeginNextTurn()!, ThrowItemAction.At(Belt.Item(Belt.Bag), orc));

        Assert.False(result.IsHit);
        Assert.Null(result.Landed);
        Assert.Null(result.DeviationDie);
        Assert.Empty(result.Effects);
        Assert.Equal(0, Belt.Count(thrower, Belt.Bag));
    }

    [Fact]
    public void TheGooAndTheGlueGoTogether()
    {
        // Two rounds of it.
        var (encounter, _, orc, _, _) = Setup(15, 1, 1, 2);
        Belt.Throw(encounter.BeginNextTurn()!, ThrowItemAction.At(Belt.Item(Belt.Bag), orc));

        Assert.Equal(orc.Effects.Find(ThrowItemAction.TangledName)!.Remaining, orc.Effects.Find(ThrowItemAction.StuckName)!.Remaining);

        encounter.Advance(Duration.Rounds(1));
        Assert.True(orc.Has(Condition.Entangled) && orc.Has(Condition.Anchored));

        encounter.Advance(Duration.Rounds(1));
        Assert.False(orc.Has(Condition.Entangled) || orc.Has(Condition.Anchored));
        Assert.Equal(30, orc.CurrentSpeed);
    }

    [Fact]
    public void BreakingFreeUnsticksTheFeetAndLeavesTheGoo()
    {
        // Stuck on the Reflex's 2; then the Strength check: 17 against 17.
        var (encounter, _, orc, _, _) = Setup(15, 2, 3, 2, 17);
        Belt.Throw(encounter.BeginNextTurn()!, ThrowItemAction.At(Belt.Item(Belt.Bag), orc));
        encounter.BeginNextTurn();
        var orcs = encounter.BeginNextTurn()!;

        var freed = Assert.IsType<BreakFreeResult>(orcs.Take(new BreakFreeAction()));

        Assert.True(freed.Freed);
        Assert.Equal("Orc Strength: d20 [17] +0 = 17 vs DC 17 — success", freed.Roll);
        Assert.Equal("Orc tears free of the goo", freed.Description);
        Assert.False(orc.Has(Condition.Anchored));
        Assert.True(orc.Has(Condition.Entangled));
        Assert.Equal(15, orc.CurrentSpeed);
        Assert.Equal((false, false), (orcs.Budget.HasStandard, orcs.Budget.HasMove));
    }

    [Fact]
    public void FailingToBreakFreeLeavesItStuck()
    {
        var (encounter, _, orc, _, _) = Setup(15, 2, 3, 2, 16);
        Belt.Throw(encounter.BeginNextTurn()!, ThrowItemAction.At(Belt.Item(Belt.Bag), orc));
        encounter.BeginNextTurn();

        var stuck = Assert.IsType<BreakFreeResult>(encounter.BeginNextTurn()!.Take(new BreakFreeAction()));

        Assert.False(stuck.Freed);
        Assert.True(orc.Has(Condition.Anchored));
        Assert.Equal("Orc strains against the goo and stays stuck fast", stuck.Description);
    }

    [Fact]
    public void AnEscapeArtistSlipsOutRatherThanHaulingFree()
    {
        var (encounter, _, orc, _, _) = Setup(15, 2, 3, 2, 15);
        orc.Skills.SetRanks(Skill.EscapeArtist, 4);
        Belt.Throw(encounter.BeginNextTurn()!, ThrowItemAction.At(Belt.Item(Belt.Bag), orc));
        encounter.BeginNextTurn();

        var freed = Assert.IsType<BreakFreeResult>(encounter.BeginNextTurn()!.Take(new BreakFreeAction()));

        // Four ranks, less 2 for the goo's Dexterity: better than no Strength at all.
        Assert.True(freed.Freed);
        Assert.Equal("Orc Escape Artist: d20 [15] +2 = 17 vs DC 17 — success", freed.Roll);
    }

    [Fact]
    public void NobodyBreaksFreeOfNothing()
    {
        var turn = ClassKit.Fight([ClassKit.Dummy("Free")], [ClassKit.Dummy("Other")], null).BeginNextTurn()!;

        Assert.False(turn.CanTake(new BreakFreeAction()));
    }

    [Fact]
    public void NobodyRunsWhileEntangled()
    {
        var runner = ClassKit.Dummy("Runner");
        var field = ClassKit.Field((runner, 0, 0), (ClassKit.Dummy("Other"), 11, 11));
        var turn = ClassKit.Fight([runner], [field.Creatures[1]], field).BeginNextTurn()!;
        var run = new RunAction([new(0, 0), new(1, 0), new(2, 0)]);

        Assert.True(turn.CanTake(run));

        runner.Effects.Apply(ConditionInfo.Effect(Condition.Entangled, Duration.Rounds(2)));

        Assert.False(turn.CanTake(run));
        Assert.True(turn.CanTake(new MoveAction([new(0, 0), new(1, 0), new(2, 0)])));
    }
}

public class ThunderstoneTests
{
    [Fact]
    public void ItDeafensEverybodyWithinTenFeetWhoFailsFriendOrFoe()
    {
        var thrower = Belt.Thrower(Belt.Stone);
        var near = ClassKit.Dummy("Near");
        var friend = ClassKit.Dummy("Friend");
        var far = ClassKit.Dummy("Far");
        var steady = ClassKit.Dummy("Steady");
        var field = ClassKit.Field((thrower, 0, 0), (near, 5, 6), (friend, 6, 7), (far, 8, 5), (steady, 4, 4));

        // Thirty-five feet is the second increment of twenty: 10 and 0 against 5. Then the
        // Fortitude saves, in the order they came onto the field: 3, 5 and 18.
        var turn = ClassKit.Fight([thrower, friend], [near, far, steady], field, 10, 3, 5, 18).BeginNextTurn()!;
        var result = Belt.Throw(turn, ThrowItemAction.At(Belt.Item(Belt.Stone), new GridSquare(5, 5)));

        Assert.True(result.IsHit);
        Assert.Equal([near, friend, steady], result.Effects.Select(effect => effect.Creature));
        Assert.True(near.Has(Condition.Deafened));
        Assert.True(friend.Has(Condition.Deafened));
        Assert.False(steady.Has(Condition.Deafened));
        Assert.False(far.Has(Condition.Deafened));
        Assert.False(thrower.Has(Condition.Deafened));
        Assert.Equal(Duration.Hours(1), near.Effects.Find(ThrowItemAction.DeafenedName)!.Remaining);
        Assert.Equal(["deafened for 1 hour"], result.Effects[0].Applied);
        Assert.StartsWith("Steady: Fortitude save: d20 [18]", result.Effects[2].ToString());
    }

    [Fact]
    public void ThrownAtSomebodyItGoesOffAtTheirFeet()
    {
        var thrower = Belt.Thrower(Belt.Stone);
        var orc = ClassKit.Dummy("Orc", armour: 10);
        var field = ClassKit.Field((thrower, 0, 0), (orc, 3, 0));

        var turn = ClassKit.Fight([thrower], [orc], field, 4, 1).BeginNextTurn()!;
        var result = Belt.Throw(turn, ThrowItemAction.At(Belt.Item(Belt.Stone), orc));

        Assert.Null(result.Target);
        Assert.Equal(new GridSquare(3, 0), result.Aimed);
        Assert.Equal(ThrowItemAction.GroundArmorClass, result.Attack!.TargetArmorClass);
        Assert.True(orc.Has(Condition.Deafened));
    }

    [Fact]
    public void AMissedStoneGoesOffWhereItLands()
    {
        var thrower = Belt.Thrower(Belt.Stone);
        var orc = ClassKit.Dummy("Orc");
        var field = ClassKit.Field((thrower, 0, 5), (orc, 11, 5));

        // A 1 misses; the d8's 5 carries it one increment — one square — long; the orc, seven
        // squares on, is far out of earshot, and so is the thrower.
        var turn = ClassKit.Fight([thrower], [orc], field, 1, 5).BeginNextTurn()!;
        var result = Belt.Throw(turn, ThrowItemAction.At(Belt.Item(Belt.Stone), new GridSquare(3, 5)));

        Assert.Equal(new GridSquare(4, 5), result.Landed);
        Assert.Empty(result.Effects);
    }
}

public class ConsumableSaveTests
{
    private static Encounter RoundTrip(Encounter encounter) =>
        GameSave.Restore(GameSave.FromJson(GameSave.ToJson(GameSave.Capture(encounter))), TestContent.Library);

    [Fact]
    public void StuckFastAndBurningBothSurviveASave()
    {
        var thrower = Belt.Thrower(Belt.Bag, Belt.Fire);
        var orc = ClassKit.Dummy("Orc");
        var field = ClassKit.Field((thrower, 0, 0), (orc, 3, 0));
        var encounter = ClassKit.Fight([thrower], [orc], field, 15, 2, 3, 2);
        var turn = encounter.BeginNextTurn()!;
        Belt.Throw(turn, ThrowItemAction.At(Belt.Item(Belt.Bag), orc));
        orc.Effects.Apply(new DamageOverTimeEffect("Alchemist's fire", Duration.Rounds(1), "1d6", DamageType.Fire));

        var order = RoundTrip(encounter).Order;
        var restored = order.Single(combatant => combatant.Creature.Name == "Orc").Creature;

        Assert.True(restored.Has(Condition.Anchored));
        Assert.True(restored.Has(Condition.Entangled));
        Assert.Equal(0, restored.CurrentSpeed);
        Assert.True(PutOutFlamesAction.IsBurning(restored));
        Assert.Equal(
            orc.Effects.Find(ThrowItemAction.StuckName)!.Remaining,
            restored.Effects.Find(ThrowItemAction.StuckName)!.Remaining);
        Assert.Equal((0, 1), (Belt.Count(order[0].Creature, Belt.Bag), Belt.Count(order[0].Creature, Belt.Fire)));
    }

    [Fact]
    public void DamageOnAClockComesBackWithItsCondition()
    {
        var sick = ClassKit.Dummy("Sick");
        var encounter = ClassKit.Fight([sick], [ClassKit.Dummy("Other")], ClassKit.Field((sick, 0, 0)));
        sick.Effects.Apply(new DamageOverTimeEffect("Wyvern poison", Duration.Rounds(6), "1d2", DamageType.Untyped)
        {
            Condition = Condition.Sickened,
        });

        var restored = RoundTrip(encounter).Order.Single(combatant => combatant.Creature.Name == "Sick").Creature;

        Assert.True(restored.Has(Condition.Sickened));
        Assert.IsType<DamageOverTimeEffect>(restored.Effects.Find("Wyvern poison"));
    }
}
