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

public class SneakAttackTests
{
    private static Creature Rogue(int level = 1, string talents = "") =>
        ClassKit.Make("rogue", level, "\"items\": [\"short-sword\"]" + (talents.Length > 0 ? $", \"talents\": [{talents}]" : string.Empty));

    [Fact]
    public void AFlankedTargetTakesTheExtraDice()
    {
        var rogue = Rogue();
        var ally = ClassKit.Dummy("Ally");
        var target = ClassKit.Dummy("Target");
        var field = ClassKit.Field((rogue, 4, 5), (target, 5, 5), (ally, 6, 5));
        rogue.Allegiance = ally.Allegiance = 1;
        target.Allegiance = 2;

        // 15 to hit; 3 on the short sword; 5 on the sneak attack die.
        var strike = Strike.Resolve(rogue, rogue.PrimaryAttack!, target, new SequenceRandom(15, 3, 5), field: field);

        Assert.True(strike.IsHit);
        Assert.Equal(1, strike.SneakAttackDice);
        Assert.Equal(3 + 2 + 5, strike.Damage!.Total);   // die, Strength 14, sneak attack
        Assert.Contains("+1d6 sneak attack", strike.ToString());
    }

    [Fact]
    public void AFlatFootedTargetTakesThemToo()
    {
        var rogue = Rogue(3);
        var target = ClassKit.Dummy("Target");
        rogue.Allegiance = 1;
        target.Allegiance = 2;

        var strike = Strike.Resolve(rogue, rogue.PrimaryAttack!, target, new SequenceRandom(15, 3, 1, 1), flatFooted: true);

        Assert.Equal(2, strike.SneakAttackDice);
        Assert.Equal(3 + 2 + 1 + 1, strike.Damage!.Total);
    }

    [Fact]
    public void ATargetFacingHerSquarelyTakesNoExtra()
    {
        var rogue = Rogue();
        var target = ClassKit.Dummy("Target");
        rogue.Allegiance = 1;
        target.Allegiance = 2;

        var strike = Strike.Resolve(rogue, rogue.PrimaryAttack!, target, new SequenceRandom(15, 3));

        Assert.Equal(0, strike.SneakAttackDice);
        Assert.DoesNotContain("sneak attack", strike.ToString());
    }

    [Fact]
    public void ATargetThatCannotSeeHerComingCountsAsDeniedItsDexterity()
    {
        var rogue = Rogue();
        var target = ClassKit.Dummy("Target");
        target.Effects.Apply(ConditionInfo.Effect(Condition.Blinded, Duration.Rounds(5)));

        var strike = Strike.Resolve(rogue, rogue.PrimaryAttack!, target, new SequenceRandom(15, 3, 6));

        Assert.Equal(1, strike.SneakAttackDice);
    }

    [Fact]
    public void ACriticalDoesNotMultiplyTheSneakAttack()
    {
        var rogue = Rogue();
        var target = ClassKit.Dummy("Target");

        // 20 threatens, 15 confirms; the short sword rolls twice, the sneak die once.
        var strike = Strike.Resolve(rogue, rogue.PrimaryAttack!, target, new SequenceRandom(20, 15, 3, 3, 6), flatFooted: true);

        Assert.True(strike.IsCritical);
        Assert.Equal((3 + 2) * 2 + 6, strike.Damage!.Total);
    }

    [Fact]
    public void AShotOnlySneakAttacksInsideThirtyFeet()
    {
        var rogue = ClassKit.Make("rogue", 1, "\"items\": [\"shortbow\"]");
        var near = ClassKit.Dummy("Near");
        var far = ClassKit.Dummy("Far");
        var field = ClassKit.Field((rogue, 0, 0), (near, 6, 0), (far, 7, 0));

        Assert.True(SneakAttack.Applies(rogue, rogue.PrimaryAttack!, true, false, field.DistanceInFeet(rogue, near)));
        Assert.False(SneakAttack.Applies(rogue, rogue.PrimaryAttack!, true, false, field.DistanceInFeet(rogue, far)));
    }

    [Fact]
    public void UncannyDodgeMeansNeverCaughtFlatFooted()
    {
        var rogue = Rogue();
        var wary = ClassKit.Make("rogue", 4, "\"items\": [\"short-sword\"]", [10, 18, 10, 10, 10, 10]);

        // 18 + 2 against armour class 14 with her Dexterity kept: a hit, and a sneak attack die
        // would need a third roll that the sequence does not have.
        var strike = Strike.Resolve(rogue, rogue.PrimaryAttack!, wary, new SequenceRandom(18, 3), flatFooted: true);

        Assert.True(strike.IsHit);
        Assert.Equal(0, strike.SneakAttackDice);
        Assert.Equal(Ironbound.Rules.Defense.DefenseOptions.None, strike.Attack.Options);
    }

    [Fact]
    public void ButUncannyDodgeIsNoHelpAsleep()
    {
        var rogue = Rogue();
        var wary = ClassKit.Make("rogue", 4);
        wary.Effects.Apply(ConditionInfo.Effect(Condition.Asleep, Duration.Rounds(2)));

        var strike = Strike.Resolve(rogue, rogue.PrimaryAttack!, wary, new SequenceRandom(15, 3, 4));

        Assert.Equal(1, strike.SneakAttackDice);
    }

    [Fact]
    public void ImprovedUncannyDodgeTurnsAFlankAwayFromALesserRogue()
    {
        var rogue = Rogue(3);
        var ally = ClassKit.Dummy("Ally");
        var barbarian = ClassKit.Make("barbarian", 5);
        var field = ClassKit.Field((rogue, 4, 5), (barbarian, 5, 5), (ally, 6, 5));
        rogue.Allegiance = ally.Allegiance = 1;
        barbarian.Allegiance = 2;

        Assert.True(UncannyDodge.HasImproved(barbarian));
        Assert.Null(Strike.FlankingPartner(rogue, barbarian, field));
        Assert.DoesNotContain(Strike.AttackBonus(rogue, rogue.PrimaryAttack!, barbarian, field).Applied,
            entry => entry.Modifier.Source.StartsWith("Flanking"));
    }

    [Fact]
    public void ButARogueFourLevelsBetterStillFlanks()
    {
        var rogue = Rogue(9);
        var ally = ClassKit.Dummy("Ally");
        var barbarian = ClassKit.Make("barbarian", 5);
        var field = ClassKit.Field((rogue, 4, 5), (barbarian, 5, 5), (ally, 6, 5));
        rogue.Allegiance = ally.Allegiance = 1;
        barbarian.Allegiance = 2;

        Assert.Same(ally, Strike.FlankingPartner(rogue, barbarian, field));
    }

    [Fact]
    public void UncannyDodgeFromTwoClassesIsImproved()
    {
        var both = ClassKit.Make("rogue", 4);
        both.Levels.Add(new ClassLevel(TestContent.Library.GetClass("barbarian")!, 2));

        Assert.True(UncannyDodge.HasImproved(both));
        Assert.Equal(6, UncannyDodge.Levels(both));
    }

    [Fact]
    public void BleedingAttackLeavesABleed()
    {
        var rogue = Rogue(3, "\"bleeding-attack\"");
        var target = ClassKit.Dummy("Target");

        var strike = Strike.Resolve(rogue, rogue.PrimaryAttack!, target, new SequenceRandom(15, 3, 1, 1), flatFooted: true);

        var bleed = Assert.IsType<DamageOverTimeEffect>(target.Effects.Find(SneakAttack.BleedLabel));
        Assert.Equal("2", bleed.Amount.ToString());
        Assert.Contains(strike.Notes, note => note.Contains("bleeds 2"));
    }

    [Fact]
    public void HealingStopsTheBleed()
    {
        var target = ClassKit.Dummy("Target");
        target.HitPoints.Take(5);
        target.Effects.Apply(new DamageOverTimeEffect(SneakAttack.BleedLabel, Duration.Permanent, DiceExpression.Constant(1), Ironbound.Rules.Combat.DamageType.Untyped));
        var cleric = ClassKit.Make("cleric", 1, "\"deity\": \"none\", \"spells\": [\"cure-light-wounds\"]");
        cleric.Allegiance = target.Allegiance = 1;

        var cast = Casting.Resolve(cleric, TestContent.Library.GetSpell("cure-light-wounds")!, SpellAim.At(target), new SequenceRandom(4));

        Assert.False(target.Effects.Has(SneakAttack.BleedLabel));
        Assert.Contains("stops bleeding", cast.Targets.Single().Applied);
    }

    [Fact]
    public void WithoutTheTalentThereIsNoBleed()
    {
        var rogue = Rogue(3);
        var target = ClassKit.Dummy("Target");

        Strike.Resolve(rogue, rogue.PrimaryAttack!, target, new SequenceRandom(15, 3, 1, 1), flatFooted: true);

        Assert.False(target.Effects.Has(SneakAttack.BleedLabel));
    }

    [Fact]
    public void SlowReactionsTakesAwayTheTargetsAttacksOfOpportunity()
    {
        var rogue = Rogue(2, "\"slow-reactions\"");
        var victim = ClassKit.Dummy("Victim");
        var field = ClassKit.Field((rogue, 4, 4), (victim, 5, 4));
        var encounter = ClassKit.Fight([rogue], [victim], field, 15, 3, 6);

        encounter.BeginNextTurn()!.Take(new AttackAction(rogue.PrimaryAttack!, victim));

        Assert.True(SneakAttack.IsSlowed(victim));
        Assert.False(encounter.CombatantFor(victim)!.CanTakeOpportunity);
    }

    [Fact]
    public void CripplingStrikeDealsStrengthDamageThatStacks()
    {
        var rogue = Rogue(10, "\"crippling-strike\"");
        var target = ClassKit.Dummy("Target", hitPoints: 200);
        var strength = target.Abilities[Ability.Strength].Score;

        // To hit, the blade, then five sneak attack dice — twice over.
        var dice = new SequenceRandom(true, 15, 1, 1, 1, 1, 1, 1);
        Strike.Resolve(rogue, rogue.PrimaryAttack!, target, dice, flatFooted: true);
        Strike.Resolve(rogue, rogue.PrimaryAttack!, target, dice, flatFooted: true);

        Assert.Equal(strength - 4, target.Abilities[Ability.Strength].Score);
    }

    [Fact]
    public void SurpriseAttackMakesEveryoneFlatFootedInTheSurpriseRound()
    {
        var rogue = Rogue(2, "\"surprise-attack\"");
        var plain = Rogue(2);
        var victim = ClassKit.Dummy("Victim");
        var bystander = ClassKit.Dummy("Bystander");
        var encounter = ClassKit.Fight([rogue, plain], [victim, bystander], null);

        encounter.Surprise(bystander);

        // Round one: both rogues, then the victim, take their turns; once the bystander's begins,
        // the victim has acted and is no longer flat-footed to anybody without the talent.
        encounter.BeginNextTurn();
        encounter.BeginNextTurn();
        encounter.BeginNextTurn();
        Assert.Same(bystander, encounter.BeginNextTurn()!.Actor);

        Assert.Equal(1, encounter.Round);
        Assert.True(encounter.IsSurpriseRound);
        Assert.True(encounter.IsFlatFootedTo(victim, rogue));
        Assert.False(encounter.IsFlatFootedTo(victim, plain));
    }

    [Fact]
    public void ThereIsNoSurpriseRoundWithoutASurprise()
    {
        var rogue = Rogue(2, "\"surprise-attack\"");
        var victim = ClassKit.Dummy("Victim");
        var encounter = ClassKit.Fight([rogue], [victim], null);

        Assert.False(encounter.IsSurpriseRound);
    }
}

public class RogueTests
{
    [Fact]
    public void TrapfindingIsHalfHerLevelOnDisableDevice()
    {
        var first = ClassKit.Make("rogue", 1, "\"skills\": [ { \"skill\": \"DisableDevice\", \"ranks\": 1 } ]");
        var sixth = ClassKit.Make("rogue", 6, "\"skills\": [ { \"skill\": \"DisableDevice\", \"ranks\": 1 } ]");

        Assert.Equal(1, RogueDefences.Trapfinding(first));
        Assert.Equal(3, RogueDefences.Trapfinding(sixth));
        Assert.Contains(first.Skills.Explain(Skill.DisableDevice).Applied, entry => entry.Modifier.Source == "Trapfinding");
        Assert.DoesNotContain(first.Skills.Explain(Skill.Stealth).Applied, entry => entry.Modifier.Source == "Trapfinding");
    }

    [Fact]
    public void PipPicksLocksOneBetterThanBefore()
    {
        var pip = TestContent.Library.BuildCreature("pip")!;

        // 4 ranks, 3 for a class skill, 3 Dexterity, and the one trapfinding adds.
        Assert.Equal(11, pip.Skills.Total(Skill.DisableDevice));
    }

    private static (Creature Caster, Spell Fireball) Fireball()
    {
        var caster = ClassKit.Make("wizard", 5, "\"spells\": [\"fireball\"]");
        caster.Allegiance = 2;
        return (caster, TestContent.Library.GetSpell("fireball")!);
    }

    [Fact]
    public void EvasionTurnsASavedHalfIntoNothing()
    {
        var (caster, fireball) = Fireball();
        var rogue = ClassKit.Make("rogue", 2, "\"items\": [\"leather-armour\"]");
        rogue.Allegiance = 1;
        var field = ClassKit.Field((rogue, 5, 5), (caster, 0, 0));

        // 5d6 all sixes, then a natural 20 on the save.
        var cast = Casting.Resolve(caster, fireball, SpellAim.At(field.SquareOf(rogue)!.Value), new SequenceRandom(6, 6, 6, 6, 6, 20), field: field);

        Assert.Equal(0, cast.Targets.Single(hit => hit.Target == rogue).Damage);
    }

    [Fact]
    public void ButNotInMediumArmour()
    {
        var (caster, fireball) = Fireball();
        var rogue = ClassKit.Make("rogue", 2, "\"items\": [\"scale-mail\"]");
        rogue.Allegiance = 1;
        var field = ClassKit.Field((rogue, 5, 5), (caster, 0, 0));

        var cast = Casting.Resolve(caster, fireball, SpellAim.At(field.SquareOf(rogue)!.Value), new SequenceRandom(6, 6, 6, 6, 6, 20), field: field);

        Assert.Equal(15, cast.Targets.Single(hit => hit.Target == rogue).Damage);
    }

    [Fact]
    public void NorAsleep()
    {
        var rogue = ClassKit.Make("rogue", 2);
        rogue.Effects.Apply(ConditionInfo.Effect(Condition.Asleep, Duration.Rounds(3)));

        Assert.False(RogueDefences.HasEvasion(rogue));
    }

    [Fact]
    public void AFailedSaveIsStillFullDamageWithPlainEvasion()
    {
        var rogue = ClassKit.Make("rogue", 2);
        var failed = rogue.Saves.Attempt(Save.Reflex, 30, new SequenceRandom(2));

        Assert.Equal(20, RogueDefences.Evade(rogue, failed, 20));
    }

    [Fact]
    public void ImprovedEvasionHalvesEvenAFailure()
    {
        var rogue = ClassKit.Make("rogue", 10, "\"talents\": [\"improved-evasion\"]");
        var failed = rogue.Saves.Attempt(Save.Reflex, 40, new SequenceRandom(2));
        var made = rogue.Saves.Attempt(Save.Reflex, 5, new SequenceRandom(19));

        Assert.Equal(10, RogueDefences.Evade(rogue, failed, 20));
        Assert.Equal(0, RogueDefences.Evade(rogue, made, 20));
    }

    [Fact]
    public void EvasionIsNoHelpAgainstAWillSave()
    {
        var rogue = ClassKit.Make("rogue", 2);
        var made = rogue.Saves.Attempt(Save.Will, 5, new SequenceRandom(19));

        Assert.Equal(10, RogueDefences.Evade(rogue, made, 20));
    }

    [Fact]
    public void FinesseRogueHandsOverWeaponFinesse()
    {
        var rogue = ClassKit.Make("rogue", 2, "\"items\": [\"short-sword\"], \"talents\": [\"finesse-rogue\"]", [8, 18, 10, 10, 10, 10]);

        Assert.True(rogue.HasFeat(Ironbound.Rules.Feats.FeatEffect.WeaponFinesse));
        Assert.Contains(Strike.AttackBonus(rogue, rogue.PrimaryAttack!).Applied, entry => entry.Modifier.Source == "Dex");
    }

    [Fact]
    public void WeaponFinesseIsNoHelpWithAHeavyBladeOrAStrongerArm()
    {
        var nimble = ClassKit.Make("rogue", 2, "\"items\": [\"longsword\"], \"feats\": [\"weapon-finesse\"]", [8, 18, 10, 10, 10, 10]);
        var strong = ClassKit.Make("rogue", 2, "\"items\": [\"short-sword\"], \"feats\": [\"weapon-finesse\"]", [18, 12, 10, 10, 10, 10]);

        Assert.Equal(Ability.Strength, Martial.AttackAbility(nimble, nimble.PrimaryAttack!));
        Assert.Equal(Ability.Strength, Martial.AttackAbility(strong, strong.PrimaryAttack!));
    }

    [Fact]
    public void TheRoguesWeaponTrainingIsWeaponFocusInHerBlade()
    {
        var rogue = ClassKit.Make("rogue", 2, "\"items\": [\"short-sword\"], \"talents\": [\"rogue-weapon-training\"]");

        Assert.Contains(rogue.Feats, feat => feat.Key == "weapon-focus:short-sword");
    }

    [Fact]
    public void ResiliencyCatchesHerOnTheWayDownOnceADay()
    {
        var rogue = ClassKit.Make("rogue", 4, "\"talents\": [\"resiliency\"]");
        rogue.HitPoints.Take(rogue.HitPoints.Maximum - 2);

        // Two left; a blow of five would leave her at -3. Four temporary hit points take it to +1.
        rogue.HitPoints.Take(5);

        Assert.Equal(1, rogue.HitPoints.Current);
        Assert.Equal(1, rogue.DailyUses.SpentFrom(ClassPowers.ResiliencyPool));

        rogue.HitPoints.Take(5);
        Assert.Equal(-4, rogue.HitPoints.Current);
    }

    [Fact]
    public void ResiliencyWaitsForABlowThatWouldDropHer()
    {
        var rogue = ClassKit.Make("rogue", 4, "\"talents\": [\"resiliency\"]");

        rogue.HitPoints.Take(3);

        Assert.Equal(0, rogue.DailyUses.SpentFrom(ClassPowers.ResiliencyPool));
        Assert.Equal(0, rogue.HitPoints.Temporary);
    }

    [Fact]
    public void DefensiveRollHalvesTheBlowThatWouldDropHer()
    {
        var rogue = ClassKit.Make("rogue", 10, "\"talents\": [\"defensive-roll\"]", [10, 18, 10, 10, 10, 10]);
        rogue.HitPoints.Take(rogue.HitPoints.Maximum - 5);

        // A 19 on the save against the ten damage.
        var (damage, save) = RogueDefences.DefensiveRoll(rogue, 10, false, new SequenceRandom(19), RuleOptions.Pathfinder);

        Assert.True(save!.Succeeded);
        Assert.Equal(5, damage);

        var (again, none) = RogueDefences.DefensiveRoll(rogue, 10, false, new SequenceRandom(19), RuleOptions.Pathfinder);
        Assert.Null(none);
        Assert.Equal(10, again);
    }

    [Fact]
    public void DefensiveRollNeedsHerToSeeItComing()
    {
        var rogue = ClassKit.Make("rogue", 10, "\"talents\": [\"defensive-roll\"]");
        rogue.HitPoints.Take(rogue.HitPoints.Maximum - 5);

        var (damage, save) = RogueDefences.DefensiveRoll(rogue, 10, true, new SequenceRandom(19), RuleOptions.Pathfinder);

        Assert.Null(save);
        Assert.Equal(10, damage);
    }

    [Fact]
    public void OpportunistSwingsAtAFoeHerAllyHasJustHit()
    {
        var rogue = ClassKit.Make("rogue", 10, "\"items\": [\"short-sword\"], \"talents\": [\"opportunist\"]");
        var ally = ClassKit.Dummy("Ally");
        var foe = ClassKit.Dummy("Foe", hitPoints: 200);
        var field = ClassKit.Field((ally, 4, 4), (foe, 5, 4), (rogue, 5, 5));
        // Ally hits with a 15 and rolls 3; the rogue's swing is a 15, her blade a 3, and five
        // sneak attack dice, because the foe has not acted yet and is flat-footed.
        var encounter = ClassKit.Fight([ally, rogue], [foe], field, 15, 3, 15, 3, 1, 1, 1, 1, 1);

        // The rogue is quicker and goes first; the ally's turn is the second.
        encounter.BeginNextTurn();
        var turn = encounter.BeginNextTurn()!;
        Assert.Same(ally, turn.Actor);

        var result = Assert.IsType<AttackActionResult>(turn.Take(new AttackAction(ally.PrimaryAttack!, foe)));

        Assert.True(result.Strike!.IsHit);
        var follow = Assert.Single(result.FollowUps);
        Assert.Same(rogue, follow.Attacker);
        Assert.True(encounter.CombatantFor(rogue)!.HasUsedOpportunist);
    }
}
