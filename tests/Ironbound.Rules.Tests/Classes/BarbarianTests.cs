using Ironbound.Rules.Abilities;
using Ironbound.Rules.Classes;
using Ironbound.Rules.Combat;
using Ironbound.Rules.Conditions;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Dice;
using Ironbound.Rules.Effects;
using Ironbound.Rules.Encounters;
using Ironbound.Rules.Encounters.Actions;
using Ironbound.Rules.Magic;
using Ironbound.Rules.Saves;
using Ironbound.Rules.Skills;

namespace Ironbound.Rules.Tests.Classes;

public class RageTests
{
    private static Creature Karn() => TestContent.Library.BuildCreature("karn")!;

    [Fact]
    public void KarnRagesSeventeenRoundsADay()
    {
        // Four, plus Constitution 16, plus two for each of five levels past the first.
        Assert.Equal(17, Karn().RageRoundsPerDay);
        Assert.Equal(17, Karn().RageRoundsLeft);
    }

    [Fact]
    public void RageMakesHerStrongerTougherSteadierAndEasierToHit()
    {
        var karn = Karn();
        var strength = karn.Abilities[Ability.Strength].Score;
        var will = karn.Saves[Save.Will].Total;
        var armour = karn.ArmorClass.Total;
        var hitPoints = karn.HitPoints.Maximum;

        Assert.NotNull(Rage.Start(karn));

        Assert.True(karn.IsRaging);
        Assert.Equal(strength + 4, karn.Abilities[Ability.Strength].Score);
        Assert.Equal(will + 2, karn.Saves[Save.Will].Total);
        Assert.Equal(armour - 2, karn.ArmorClass.Total);
        Assert.Equal(hitPoints + (2 * 6), karn.HitPoints.Maximum);   // two a hit die, not temporary
        Assert.Equal(0, karn.HitPoints.Temporary);
    }

    [Fact]
    public void TheConstitutionThatCountsIsTheOneWithoutTheRage()
    {
        var karn = Karn();
        Rage.Start(karn);

        // +4 Constitution from the rage would be two more rounds if it counted. It does not.
        Assert.Equal(17, karn.RageRoundsPerDay);
    }

    [Fact]
    public void EndingItLeavesHerFatiguedForTwiceAsLong()
    {
        var karn = Karn();
        var encounter = new Encounter([karn], new SequenceRandom(true, 10));
        Rage.Start(karn);
        encounter.Advance(Duration.Rounds(3));

        Assert.Equal(4, Rage.Current(karn)!.RoundsSoFar);
        Rage.End(karn);

        Assert.False(karn.IsRaging);
        Assert.True(karn.Has(Condition.Fatigued));
        Assert.Equal(Duration.Rounds(8), karn.Effects.Find(Rage.FatigueLabel)!.Duration);
        Assert.Equal(13, karn.RageRoundsLeft);
    }

    [Fact]
    public void FatigueIsTwoLessStrengthAndDexterity()
    {
        var karn = Karn();
        var strength = karn.Abilities[Ability.Strength].Score;
        var dexterity = karn.Abilities[Ability.Dexterity].Score;

        karn.Effects.Apply(ConditionInfo.Effect(Condition.Fatigued, Duration.Rounds(2)));

        Assert.Equal(strength - 2, karn.Abilities[Ability.Strength].Score);
        Assert.Equal(dexterity - 2, karn.Abilities[Ability.Dexterity].Score);
    }

    [Fact]
    public void NobodyRagesWhileFatigued()
    {
        var karn = Karn();
        Rage.Start(karn);
        Rage.End(karn);

        Assert.False(Rage.CanStart(karn));
        Assert.Null(Rage.Start(karn));
    }

    [Fact]
    public void TheRoundsRunOutAndSoDoesTheRage()
    {
        var karn = Karn();
        var encounter = new Encounter([karn], new SequenceRandom(true, 10));
        Rage.Start(karn);

        encounter.Advance(Duration.Rounds(17));

        Assert.False(karn.IsRaging);
        Assert.Equal(0, karn.RageRoundsLeft);
        Assert.True(karn.Has(Condition.Fatigued));
        Assert.False(Rage.CanStart(karn));
    }

    [Fact]
    public void FallingEndsTheRage()
    {
        var karn = Karn();
        var encounter = new Encounter([karn], new SequenceRandom(true, 10));
        Rage.Start(karn);

        karn.HitPoints.Take(karn.HitPoints.Current + 1);
        var events = encounter.Advance(Duration.FromTicks(1));

        Assert.False(karn.IsRaging);
        Assert.Contains(events, happened => happened.Description.Contains("rage ends"));
    }

    [Fact]
    public void TheHitPointsItGaveAreTakenBackWithIt()
    {
        var karn = Karn();
        Rage.Start(karn);
        karn.HitPoints.Take(karn.HitPoints.Maximum - 5);

        Rage.End(karn);

        // Twelve fewer at the top, and the damage still there: five became minus seven.
        Assert.Equal(-7, karn.HitPoints.Current);
        Assert.Equal(HitPointState.Dying, karn.HitPoints.State);
    }

    [Fact]
    public void RestingGivesEveryRoundBack()
    {
        var karn = Karn();
        Rage.Start(karn);
        karn.Effects.Clear();

        ClassFeatures.Rest(karn);

        Assert.Equal(17, karn.RageRoundsLeft);
        Assert.False(karn.Has(Condition.Fatigued));
        Assert.True(Rage.CanStart(karn));
    }

    [Fact]
    public void LeavingAFightEndsTheRageAndShedsTheFatigue()
    {
        var karn = Karn();
        Rage.Start(karn);

        ClassFeatures.EndFight(karn);

        Assert.False(karn.IsRaging);
        Assert.False(karn.Has(Condition.Fatigued));
        Assert.Equal(16, karn.RageRoundsLeft);
    }

    [Fact]
    public void RageActionIsFreeAndWorksBothWays()
    {
        var karn = Karn();
        var encounter = new Encounter([karn], new SequenceRandom(true, 10));
        var turn = encounter.BeginNextTurn()!;

        Assert.Equal(ActionCost.Free, new RageAction().Cost);
        Assert.NotNull(turn.Take(new RageAction()));
        Assert.True(karn.IsRaging);
        Assert.True(turn.Budget.HasStandard);

        Assert.NotNull(turn.Take(new RageAction()));
        Assert.False(karn.IsRaging);

        // Fatigued now, so a third press is refused.
        Assert.False(turn.CanTake(new RageAction()));
    }

    [Fact]
    public void OnlyABarbarianCanRage()
    {
        var fighter = ClassKit.Make("fighter", 6);
        var encounter = new Encounter([fighter], new SequenceRandom(true, 10));

        Assert.Equal(0, fighter.RageRoundsPerDay);
        Assert.False(encounter.BeginNextTurn()!.CanTake(new RageAction()));
    }

    [Fact]
    public void RageForbidsSpellsAndPatientSkills()
    {
        var raging = ClassKit.Make("barbarian", 1, "\"skills\": [ { \"skill\": \"Stealth\", \"ranks\": 1 }, { \"skill\": \"Acrobatics\", \"ranks\": 1 } ]");
        raging.Spells.SetSlots(1, 1).Prepare(TestContent.Library.GetSpell("magic-missile")!);
        Rage.Start(raging);

        Assert.False(raging.Spells.CanCast(TestContent.Library.GetSpell("magic-missile")!));
        Assert.False(raging.Skills.CanAttempt(Skill.Stealth));
        Assert.True(raging.Skills.CanAttempt(Skill.Acrobatics));
        Assert.True(raging.Skills.CanAttempt(Skill.Intimidate));
        Assert.True(raging.Skills.Check(Skill.Stealth, new SequenceRandom(10), 5).Untrained);
    }

    [Fact]
    public void FastMovementComesBeforeTheArmour()
    {
        var karn = Karn();

        // Thirty and ten is forty, and a breastplate makes forty thirty.
        Assert.Equal(30, karn.CurrentSpeed);
        Assert.Equal(40, ClassKit.Make("barbarian", 1).CurrentSpeed);
        Assert.Equal(30, ClassKit.Make("fighter", 1).CurrentSpeed);
    }

    [Fact]
    public void KarnHasTheDefencesOfHisLevel()
    {
        var karn = Karn();

        Assert.True(UncannyDodge.Has(karn));
        Assert.True(UncannyDodge.HasImproved(karn));
        Assert.Equal(2, RogueDefences.TrapSense(karn));
        Assert.Contains(ClassFeatures.Describe(karn), line => line.Name == "Rage" && line.Detail == "17 of 17 rounds left");
    }
}

public class RagePowerTests
{
    private static Creature Karn() => TestContent.Library.BuildCreature("karn")!;

    [Fact]
    public void TheDeclarationsWaitForARage()
    {
        var karn = Karn();

        Assert.False(karn.Stances.CanAdopt(Stance.PowerfulBlow));
        Assert.False(karn.Stances.Adopt(Stance.SurpriseAccuracy));

        Rage.Start(karn);

        Assert.True(karn.Stances.CanAdopt(Stance.PowerfulBlow));
        Assert.True(karn.Stances.CanAdopt(Stance.SurpriseAccuracy));
        Assert.True(karn.Stances.CanAdopt(Stance.StrengthSurge));
    }

    [Fact]
    public void NorDoesAnybodyWithoutThePower()
    {
        var other = ClassKit.Make("barbarian", 2, "\"talents\": [\"swift-foot\"]");
        Rage.Start(other);

        Assert.False(other.Stances.CanAdopt(Stance.PowerfulBlow));
    }

    [Fact]
    public void PowerfulBlowAddsToOneBlowAndIsGone()
    {
        var karn = Karn();
        var target = ClassKit.Dummy("Target", hitPoints: 300);
        Rage.Start(karn);
        Assert.True(karn.Stances.Adopt(Stance.PowerfulBlow));

        var first = Strike.Resolve(karn, karn.PrimaryAttack!, target, new SequenceRandom(true, 15, 6));
        var second = Strike.Resolve(karn, karn.PrimaryAttack!, target, new SequenceRandom(true, 15, 6));

        // Two at sixth level: one, and one more for four levels.
        Assert.Equal(second.Damage!.Total + 2, first.Damage!.Total);
        Assert.Contains("powerful blow +2", first.ToString());
        Assert.False(karn.Stances.IsActive(Stance.PowerfulBlow));
        Assert.False(karn.Stances.CanAdopt(Stance.PowerfulBlow));
    }

    [Fact]
    public void AMissWastesIt()
    {
        var karn = Karn();
        var target = ClassKit.Dummy("Target");
        Rage.Start(karn);
        karn.Stances.Adopt(Stance.PowerfulBlow);

        var strike = Strike.Resolve(karn, karn.PrimaryAttack!, target, new SequenceRandom(1));

        Assert.False(strike.IsHit);
        Assert.True(karn.Stances.IsSpent(Stance.PowerfulBlow));
        Assert.Contains("powerful blow wasted", strike.ToString());
    }

    [Fact]
    public void SurpriseAccuracyIsAMoraleBonusOnOneRoll()
    {
        var karn = Karn();
        var target = ClassKit.Dummy("Target");
        Rage.Start(karn);
        var bare = Strike.AttackBonus(karn, karn.PrimaryAttack!, target).Total;

        karn.Stances.Adopt(Stance.SurpriseAccuracy);
        Assert.Equal(bare + 2, Strike.AttackBonus(karn, karn.PrimaryAttack!, target).Total);

        Strike.Resolve(karn, karn.PrimaryAttack!, target, new SequenceRandom(true, 15, 6));

        Assert.Equal(bare, Strike.AttackBonus(karn, karn.PrimaryAttack!, target).Total);
        Assert.True(karn.Stances.IsSpent(Stance.SurpriseAccuracy));
    }

    [Fact]
    public void StrengthSurgeIsHerLevelOnTheNextManoeuvreAndNotOnASwing()
    {
        var karn = Karn();
        var target = ClassKit.Dummy("Target", hitPoints: 300);
        Rage.Start(karn);
        var bare = Maneuvers.Bonus(karn, ManeuverKind.BullRush).Total;
        karn.Stances.Adopt(Stance.StrengthSurge);

        Strike.Resolve(karn, karn.PrimaryAttack!, target, new SequenceRandom(true, 15, 6));
        Assert.True(karn.Stances.IsActive(Stance.StrengthSurge));

        Assert.Equal(bare + 6, Maneuvers.Bonus(karn, ManeuverKind.BullRush).Total);
        Maneuvers.Attempt(karn, target, new SequenceRandom(10), ManeuverKind.BullRush);

        Assert.True(karn.Stances.IsSpent(Stance.StrengthSurge));
        Assert.Equal(bare, Maneuvers.Bonus(karn, ManeuverKind.BullRush).Total);
    }

    [Fact]
    public void EachIsOnceARageAndANewRageGivesThemBack()
    {
        var karn = Karn();
        var target = ClassKit.Dummy("Target", hitPoints: 300);

        Rage.Start(karn);
        karn.Stances.Adopt(Stance.PowerfulBlow);
        Strike.Resolve(karn, karn.PrimaryAttack!, target, new SequenceRandom(true, 15, 6));
        Assert.False(karn.Stances.CanAdopt(Stance.PowerfulBlow));

        Rage.End(karn);
        ClassFeatures.Rest(karn);
        Rage.Start(karn);

        Assert.True(karn.Stances.CanAdopt(Stance.PowerfulBlow));
    }

    [Fact]
    public void ADeclarationNotUsedGoesWhenTheRageDoes()
    {
        var karn = Karn();
        Rage.Start(karn);
        karn.Stances.Adopt(Stance.PowerfulBlow);

        Rage.End(karn);

        Assert.False(karn.Stances.IsActive(Stance.PowerfulBlow));
    }

    [Fact]
    public void TheirNamesAreTheirNames()
    {
        Assert.Equal("Powerful Blow", Stances.Name(Stance.PowerfulBlow));
        Assert.Equal("Surprise Accuracy", Stances.Name(Stance.SurpriseAccuracy));
        Assert.Equal("Strength Surge", Stances.Name(Stance.StrengthSurge));
        Assert.True(Stances.IsOneShot(Stance.StrengthSurge));
        Assert.False(Stances.IsOneShot(Stance.PowerAttack));
    }

    [Fact]
    public void GuardedStanceIsADodgeBonusAgainstSwordsAndNotArrows()
    {
        var guard = ClassKit.Make("barbarian", 6, "\"talents\": [\"guarded-stance\", \"swift-foot\", \"superstition\"]");
        Rage.Start(guard);
        var stance = guard.Powers.Single(power => power.Id == "guarded-stance");
        var encounter = new Encounter([guard], new SequenceRandom(true, 10));
        var before = guard.ArmorClass.Value(Ironbound.Rules.Defense.DefenseOptions.Melee);

        Assert.Equal(ActionCost.Move, stance.Cost);
        Assert.NotNull(encounter.BeginNextTurn()!.Take(UsePowerAction.Self(stance)));

        Assert.Equal(before + 2, guard.ArmorClass.Value(Ironbound.Rules.Defense.DefenseOptions.Melee));
        Assert.Equal(guard.ArmorClass.Total, guard.ArmorClass.Value());
        Assert.Equal(
            guard.ArmorClass.Value(Ironbound.Rules.Defense.DefenseOptions.DexterityDenied),
            guard.ArmorClass.Value(Ironbound.Rules.Defense.DefenseOptions.DexterityDenied | Ironbound.Rules.Defense.DefenseOptions.Melee));
    }

    [Fact]
    public void SwiftFootIsFiveFeetWhileRaging()
    {
        var swift = ClassKit.Make("barbarian", 2, "\"talents\": [\"swift-foot\"]");

        Assert.Equal(40, swift.CurrentSpeed);
        Rage.Start(swift);
        Assert.Equal(45, swift.CurrentSpeed);
    }

    [Fact]
    public void SuperstitionHelpsAgainstMagicOnlyWhileRaging()
    {
        var wary = ClassKit.Make("barbarian", 4, "\"talents\": [\"superstition\", \"swift-foot\"]");
        var fireball = TestContent.Library.GetSpell("fireball")!;

        Assert.Empty(ClassSaves.Against(wary, fireball, Save.Reflex));

        Rage.Start(wary);
        var bonus = Assert.Single(ClassSaves.Against(wary, fireball, Save.Reflex));
        Assert.Equal(3, bonus.Value);   // two, and one more at fourth level
    }

    [Fact]
    public void RenewedVigorHealsOnceADayAndOnlyInARage()
    {
        var vigorous = ClassKit.Make("barbarian", 4, "\"talents\": [\"renewed-vigor\", \"swift-foot\"]");
        var vigor = vigorous.Powers.Single(power => power.Id == "renewed-vigor");
        vigorous.HitPoints.Take(20);
        var encounter = new Encounter([vigorous], new SequenceRandom(true, 5));
        var turn = encounter.BeginNextTurn()!;

        Assert.False(turn.CanTake(UsePowerAction.Self(vigor)));
        turn.Take(new RageAction());

        var result = Assert.IsType<CastSpellResult>(turn.Take(UsePowerAction.Self(vigor)));

        // 1d8 rolled 5, and Constitution 14 raging is 18: +4.
        Assert.Equal(9, result.Cast!.Targets.Single().Healed);
        Assert.Equal(0, vigorous.UsesLeft(vigor));
    }

    [Fact]
    public void RenewedVigorWaitsForFourthLevel()
    {
        var library = TestContent.Library;
        var young = ClassKit.Make("barbarian", 2);

        Assert.DoesNotContain(
            ClassLevelling.TalentsOf(young, library.GetClass("barbarian")!, 2, library, FeatureIds.RagePowers),
            talent => talent.Id == "renewed-vigor");
        Assert.Contains(
            ClassLevelling.TalentsOf(young, library.GetClass("barbarian")!, 4, library, FeatureIds.RagePowers),
            talent => talent.Id == "renewed-vigor");
    }

    [Fact]
    public void KnockbackShovesWithoutProvokingAndHurts()
    {
        var bully = ClassKit.Make("barbarian", 2, "\"talents\": [\"knockback\"]", [18, 10, 14, 10, 10, 10]);
        var victim = ClassKit.Dummy("Victim");
        var watcher = ClassKit.Dummy("Watcher");
        var field = ClassKit.Field((bully, 4, 4), (victim, 5, 4), (watcher, 4, 5));
        // A 2 on the check: 2 + 2 base attack + 6 raging Strength meets the victim's 10 exactly,
        // which is one square of shove.
        var encounter = ClassKit.Fight([bully], [victim, watcher], field, 2);
        var turn = encounter.BeginNextTurn()!;

        Assert.False(turn.CanTake(new KnockbackAction(victim)));
        turn.Take(new RageAction());

        var result = Assert.IsType<ManeuverActionResult>(turn.Take(new KnockbackAction(victim)));

        Assert.True(result.Check!.Succeeded);
        Assert.Empty(result.Opportunities);
        Assert.Equal(new Ironbound.Rules.Maps.GridSquare(6, 4), field.SquareOf(victim));

        // Strength 18 raging is 22: six damage.
        Assert.Equal(6, victim.HitPoints.Damage);
    }

    [Fact]
    public void KarnsPowersAreWhatTheSpecPicked()
    {
        Assert.Equal(
            ["powerful-blow", "surprise-accuracy", "strength-surge"],
            Karn().Choices.Talents.Select(talent => talent.Id));
    }
}
