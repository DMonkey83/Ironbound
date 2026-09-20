using Ironbound.Rules;
using Ironbound.Rules.Abilities;
using Ironbound.Rules.Combat;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Defense;
using Ironbound.Rules.Dice;
using Ironbound.Rules.Modifiers;

namespace Ironbound.Rules.Tests.Defense;

public class DamageReductionTests
{
    [Theory]
    [InlineData(DamageBypass.None, false)]
    [InlineData(DamageBypass.Silver, true)]
    [InlineData(DamageBypass.Magic, false)]
    [InlineData(DamageBypass.Silver | DamageBypass.Magic, true)]
    public void ASingleQualityIsEnoughByDefault(DamageBypass qualities, bool bypassed)
    {
        var reduction = new DamageReduction(10, DamageBypass.Silver);

        Assert.Equal(bypassed, reduction.IsBypassedBy(qualities));
        Assert.Equal(bypassed ? 0 : 10, reduction.Against(qualities));
    }

    [Theory]
    [InlineData(DamageBypass.Silver, false)]
    [InlineData(DamageBypass.Good, false)]
    [InlineData(DamageBypass.Silver | DamageBypass.Good, true)]
    public void AllModeDemandsEveryQuality(DamageBypass qualities, bool bypassed)
    {
        var reduction = new DamageReduction(
            10, DamageBypass.Good | DamageBypass.Silver, BypassMode.All);

        Assert.Equal(bypassed, reduction.IsBypassedBy(qualities));
    }

    [Theory]
    [InlineData(DamageBypass.Silver, true)]
    [InlineData(DamageBypass.Good, true)]
    public void AnyModeTakesEitherQuality(DamageBypass qualities, bool bypassed)
    {
        var reduction = new DamageReduction(10, DamageBypass.Good | DamageBypass.Silver);

        Assert.Equal(bypassed, reduction.IsBypassedBy(qualities));
    }

    [Fact]
    public void ReductionWithNoBypassStopsEverything()
    {
        var reduction = new DamageReduction(15);

        var everything = DamageBypass.Magic | DamageBypass.Silver | DamageBypass.Adamantine
            | DamageBypass.ColdIron | DamageBypass.Good | DamageBypass.Epic;

        Assert.False(reduction.IsBypassedBy(everything));
        Assert.Equal(15, reduction.Against(everything));
    }

    [Theory]
    [InlineData(10, DamageBypass.Silver, BypassMode.Any, "DR 10/silver")]
    [InlineData(5, DamageBypass.Bludgeoning, BypassMode.Any, "DR 5/bludgeoning")]
    [InlineData(15, DamageBypass.None, BypassMode.Any, "DR 15/—")]
    [InlineData(10, DamageBypass.Silver | DamageBypass.Good, BypassMode.All, "DR 10/silver and good")]
    [InlineData(10, DamageBypass.Silver | DamageBypass.Good, BypassMode.Any, "DR 10/silver or good")]
    [InlineData(10, DamageBypass.ColdIron, BypassMode.Any, "DR 10/cold iron")]
    public void FormatsLikeAStatBlock(int amount, DamageBypass bypass, BypassMode mode, string text)
    {
        Assert.Equal(text, new DamageReduction(amount, bypass, mode).ToString());
    }

    [Fact]
    public void RejectsNegativeReduction()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new DamageReduction(-1));
    }
}

public class DamageMitigationTests
{
    private static DamageRoll Damage(params (int Amount, DamageType Type)[] parts)
    {
        var packet = new DamagePacket();
        foreach (var (amount, type) in parts)
        {
            packet.Add(DiceExpression.Constant(amount), type);
        }

        // Every component is a constant, so no dice are actually rolled.
        return packet.Roll(new SequenceRandom(1));
    }

    private static DamageRoll Slashing(int amount) => Damage((amount, DamageType.Slashing));

    // ---- bypass ----

    [Fact]
    public void TheSkeletonsMaceLesson()
    {
        var skeleton = new DamageDefenses().Reduce(5, DamageBypass.Bludgeoning);

        Assert.Equal(9, skeleton.Apply(Slashing(14)).Total);
        Assert.Equal(14, skeleton.Apply(Damage((14, DamageType.Bludgeoning))).Total);
    }

    [Fact]
    public void TheWerewolfsSilverLesson()
    {
        var werewolf = new DamageDefenses().Reduce(10, DamageBypass.Silver);

        Assert.Equal(4, werewolf.Apply(Slashing(14)).Total);
        Assert.Equal(14, werewolf.Apply(Slashing(14), DamageBypass.Silver).Total);
    }

    [Fact]
    public void TheBestApplicableReductionAppliesAndTheyDoNotStack()
    {
        var demon = new DamageDefenses()
            .Reduce(5)
            .Reduce(10, DamageBypass.ColdIron);

        // Nothing bypassed: the larger one applies, and the two never add up to 15.
        Assert.Equal(10, demon.ReductionAgainst(DamageBypass.None));
        Assert.Equal(10, demon.Apply(Slashing(20)).Total);

        // Cold iron defeats the bigger one, so the flat 5 is what remains.
        Assert.Equal(5, demon.ReductionAgainst(DamageBypass.ColdIron));
        Assert.Equal(15, demon.Apply(Slashing(20), DamageBypass.ColdIron).Total);
    }

    [Fact]
    public void ADamageTypeCountsAsItsOwnBypassWithoutBeingDeclared()
    {
        var skeleton = new DamageDefenses().Reduce(5, DamageBypass.Bludgeoning);

        // The mace never says it is blunt; the damage type carries it.
        Assert.Equal(7, skeleton.Apply(Damage((7, DamageType.Bludgeoning))).Total);
    }

    // ---- once per hit, not per die ----

    [Fact]
    public void ReductionIsSubtractedOncePerHitNotPerAttack()
    {
        var werewolf = new DamageDefenses().Reduce(10, DamageBypass.Silver);

        // The rogue: five separate hits of 6 each lose everything.
        var manySmall = Enumerable.Range(0, 5).Sum(_ => werewolf.Apply(Slashing(6)).Total);
        Assert.Equal(0, manySmall);

        // The greatsword: the same 30 damage in one hit loses only 10.
        Assert.Equal(20, werewolf.Apply(Slashing(30)).Total);
    }

    [Fact]
    public void ACriticalsRepeatedRollsAreReducedOnceBetweenThem()
    {
        var werewolf = new DamageDefenses().Reduce(10, DamageBypass.Silver);
        var packet = DamagePacket.Weapon("7", DamageType.Slashing);

        var roll = packet.Roll(new SequenceRandom(1), criticalMultiplier: 2);
        Assert.Equal(14, roll.Total);

        // One subtraction of 10, not one per iteration — which would have left nothing.
        Assert.Equal(4, werewolf.Apply(roll).Total);
    }

    [Fact]
    public void SneakAttackSurvivesReductionFarBetterThanAPlainHit()
    {
        var werewolf = new DamageDefenses().Reduce(10, DamageBypass.Silver);

        var plain = DamagePacket.Weapon("5", DamageType.Piercing);
        var sneaky = DamagePacket.Of(
            DamageComponent.Weapon("5", DamageType.Piercing),
            DamageComponent.Extra("11", DamageType.Piercing));

        Assert.Equal(0, werewolf.Apply(plain.Roll(new SequenceRandom(1))).Total);
        Assert.Equal(6, werewolf.Apply(sneaky.Roll(new SequenceRandom(1))).Total);
    }

    [Fact]
    public void ReductionDrainsAcrossPhysicalTypesInOrder()
    {
        var defenses = new DamageDefenses().Reduce(10);

        var taken = defenses.Apply(Damage(
            (6, DamageType.Slashing),
            (8, DamageType.Piercing)));

        Assert.Equal(0, taken.TakenOf(DamageType.Slashing));
        Assert.Equal(4, taken.TakenOf(DamageType.Piercing));
        Assert.Equal(4, taken.Total);
        Assert.Equal(10, taken.AbsorbedByReduction);
    }

    // ---- energy ----

    [Fact]
    public void ResistanceSubtractsFromItsOwnTypeOnly()
    {
        var defenses = new DamageDefenses().Resist(DamageType.Fire, 5);

        var taken = defenses.Apply(Damage((21, DamageType.Slashing), (12, DamageType.Fire)));

        Assert.Equal(21, taken.TakenOf(DamageType.Slashing));
        Assert.Equal(7, taken.TakenOf(DamageType.Fire));
    }

    [Fact]
    public void ReductionNeverTouchesEnergyDamage()
    {
        var defenses = new DamageDefenses().Reduce(10);

        Assert.Equal(12, defenses.Apply(Damage((12, DamageType.Fire))).Total);
    }

    [Fact]
    public void TwoResistancesToOneTypeDoNotStack()
    {
        var defenses = new DamageDefenses()
            .Resist(DamageType.Fire, 5)
            .Resist(DamageType.Fire, 3);

        Assert.Equal(5, defenses.ResistanceTo(DamageType.Fire));

        defenses.Resist(DamageType.Fire, 10);
        Assert.Equal(10, defenses.ResistanceTo(DamageType.Fire));
    }

    [Fact]
    public void ImmunityZeroesTheTypeOutright()
    {
        var defenses = new DamageDefenses().MakeImmuneTo(DamageType.Fire);

        var taken = defenses.Apply(Damage((21, DamageType.Slashing), (100, DamageType.Fire)));

        Assert.Equal(0, taken.TakenOf(DamageType.Fire));
        Assert.Equal(21, taken.Total);
    }

    [Theory]
    [InlineData(10, 15)]
    [InlineData(7, 10)]
    [InlineData(1, 1)]
    public void VulnerabilityAddsHalfAgainRoundedDown(int raw, int expected)
    {
        var troll = new DamageDefenses().MakeVulnerableTo(DamageType.Fire);

        Assert.Equal(expected, troll.Apply(Damage((raw, DamageType.Fire))).Total);
    }

    [Fact]
    public void VulnerabilityIsAppliedBeforeResistance()
    {
        var oddity = new DamageDefenses()
            .MakeVulnerableTo(DamageType.Fire)
            .Resist(DamageType.Fire, 5);

        // 10 becomes 15, then resistance takes 5. The other order would give 7.
        Assert.Equal(10, oddity.Apply(Damage((10, DamageType.Fire))).Total);
    }

    [Fact]
    public void AFlamingSwordAgainstAFireImmuneWerewolf()
    {
        var werewolf = new DamageDefenses()
            .Reduce(10, DamageBypass.Silver)
            .MakeImmuneTo(DamageType.Fire);

        var taken = werewolf.Apply(Damage((21, DamageType.Slashing), (6, DamageType.Fire)));

        Assert.Equal(11, taken.TakenOf(DamageType.Slashing));
        Assert.Equal(0, taken.TakenOf(DamageType.Fire));
        Assert.Equal(11, taken.Total);
        Assert.Equal(27, taken.Raw);
        Assert.True(taken.WasMitigated);
    }

    [Fact]
    public void NoDefencesChangeNothing()
    {
        var taken = new DamageDefenses().Apply(Damage((21, DamageType.Slashing), (6, DamageType.Fire)));

        Assert.Equal(27, taken.Total);
        Assert.False(taken.WasMitigated);
        Assert.Null(taken.Reduction);
    }

    // ---- the easier-mode knob ----

    [Fact]
    public void TheDefaultIsTheRulesAsWrittenAndCanRemoveAHitEntirely()
    {
        Assert.Equal(100, RuleOptions.Pathfinder.MaximumReductionPercent);

        var werewolf = new DamageDefenses().Reduce(10, DamageBypass.Silver);
        Assert.Equal(0, werewolf.Apply(Slashing(5)).Total);
    }

    [Fact]
    public void CappingReductionOnlyRescuesTheSmallHits()
    {
        var werewolf = new DamageDefenses().Reduce(10, DamageBypass.Silver);
        var gentler = new RuleOptions { MaximumReductionPercent = 75 };

        // The shortsword: 75% of 5 is 3, so 2 gets through instead of nothing.
        Assert.Equal(2, werewolf.Apply(Slashing(5), DamageBypass.None, gentler).Total);

        // The greatsword is untouched: 75% of 14 is 10, which the DR was anyway.
        Assert.Equal(4, werewolf.Apply(Slashing(14), DamageBypass.None, gentler).Total);
        Assert.Equal(4, werewolf.Apply(Slashing(14)).Total);
    }

    [Fact]
    public void TheCapNeverHelpsAgainstReductionTheWeaponAlreadyBeats()
    {
        var werewolf = new DamageDefenses().Reduce(10, DamageBypass.Silver);
        var gentler = new RuleOptions { MaximumReductionPercent = 75 };

        Assert.Equal(5, werewolf.Apply(Slashing(5), DamageBypass.Silver, gentler).Total);
    }

    // ---- reporting ----

    [Fact]
    public void ExplainsWhatHappenedToEachType()
    {
        var werewolf = new DamageDefenses()
            .Reduce(10, DamageBypass.Silver)
            .MakeImmuneTo(DamageType.Fire);

        var taken = werewolf.Apply(Damage((21, DamageType.Slashing), (6, DamageType.Fire)));

        Assert.Equal(
            "21 slashing → 11 (DR 10/silver), 6 fire → 0 (immune) = 11 taken",
            taken.ToString());
    }

    [Fact]
    public void DefencesReadLikeAStatBlock()
    {
        var werewolf = new DamageDefenses()
            .Reduce(10, DamageBypass.Silver)
            .MakeImmuneTo(DamageType.Fire)
            .Resist(DamageType.Cold, 5)
            .MakeVulnerableTo(DamageType.Acid);

        Assert.Equal(
            "DR 10/silver; immune to fire; resist cold 5; vulnerable to acid",
            werewolf.ToString());

        Assert.Equal("no defences", new DamageDefenses().ToString());
    }

    [Fact]
    public void RejectsMissingArguments()
    {
        Assert.Throws<ArgumentNullException>(() => new DamageDefenses().Apply(null!));
        Assert.Throws<ArgumentNullException>(() => new DamageDefenses().Add(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DamageDefenses().Resist(DamageType.Fire, -1));
    }
}

public class StrikeMitigationTests
{
    private static Creature Fighter() =>
        new("Fighter", new AbilityScores(18, 14, 14, 10, 12, 10), 40, 6);

    private static Creature Goblin() =>
        new("Goblin", new AbilityScores(11, 15, 11, 10, 9, 6), 5, 1);

    private static WeaponAttack Longsword() =>
        WeaponAttack.Create("longsword", 9, "1d8+6", DamageType.Slashing);

    [Fact]
    public void MitigationHappensBeforeHitPointsMove()
    {
        var goblin = Goblin();
        goblin.Defenses.Reduce(5);

        var result = Strike.Resolve(Fighter(), Longsword(), goblin, new SequenceRandom(3, 1));

        Assert.Equal(7, result.Damage!.Total);
        Assert.Equal(2, result.Taken!.Total);
        Assert.Equal(2, result.DamageDealt);
        Assert.Equal(3, goblin.HitPoints.Current);
    }

    [Fact]
    public void ASilveredWeaponGetsThroughWhereSteelDoesNot()
    {
        var steel = Longsword();
        var silvered = Longsword();
        silvered.Qualities = DamageBypass.Silver;

        var withSteel = Goblin();
        withSteel.Defenses.Reduce(10, DamageBypass.Silver);
        var withSilver = Goblin();
        withSilver.Defenses.Reduce(10, DamageBypass.Silver);

        Assert.Equal(0, Strike.Resolve(Fighter(), steel, withSteel, new SequenceRandom(3, 1)).DamageDealt);
        Assert.Equal(7, Strike.Resolve(Fighter(), silvered, withSilver, new SequenceRandom(3, 1)).DamageDealt);
    }

    [Fact]
    public void AMissStillRollsNothingAndMitigatesNothing()
    {
        var goblin = Goblin();
        goblin.Defenses.Reduce(5);
        var random = new SequenceRandom(2);

        var result = Strike.Resolve(Fighter(), Longsword(), goblin, random);

        Assert.Null(result.Damage);
        Assert.Null(result.Taken);
        Assert.Equal(1, random.Consumed);
    }

    [Fact]
    public void TheLogLineShowsRawDamageAndWhatSurvivedIt()
    {
        var goblin = Goblin();
        goblin.Defenses.Reduce(5);

        Assert.Equal(
            "Fighter attacks Goblin (longsword): d20 [3] +9 = 12 vs AC 12 — hit; "
            + "7 slashing (1d8+6: [1] +6 = 7) = 7 damage; "
            + "7 slashing → 2 (DR 5/—) = 2 taken; Goblin 3/5 hp",
            Strike.Resolve(Fighter(), Longsword(), goblin, new SequenceRandom(3, 1)).ToString());
    }

    [Fact]
    public void AnUnmitigatedHitDoesNotCluttersTheLog()
    {
        Assert.DoesNotContain(
            "taken",
            Strike.Resolve(Fighter(), Longsword(), Goblin(), new SequenceRandom(3, 1)).ToString());
    }
}
