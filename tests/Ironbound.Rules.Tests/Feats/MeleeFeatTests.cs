using Ironbound.Rules.Classes;
using Ironbound.Rules.Combat;
using Ironbound.Rules.Conditions;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Defense;
using Ironbound.Rules.Dice;
using Ironbound.Rules.Effects;
using Ironbound.Rules.Encounters;
using Ironbound.Rules.Encounters.Actions;
using Ironbound.Rules.Feats;
using Ironbound.Rules.Maps;
using Ironbound.Rules.Modifiers;
using Ironbound.Rules.Tests.Classes;

namespace Ironbound.Rules.Tests.Feats;

/// <summary>The melee feats: the cleaves, the vital strikes, the focus and specialization lines.</summary>
public class MeleeFeatTests
{
    private static Creature Fighter(int level, string feats, string items = "\"longsword\"") =>
        ClassKit.Make("fighter", level, $"\"items\": [{items}], \"feats\": [{feats}]", [16, 12, 14, 13, 10, 10]);

    [Theory]
    [InlineData(false, 2)]
    [InlineData(true, 3)]
    public void GreatCleaveCarriesOnFromFoeToFoeWhereCleaveStopsAtTwo(bool great, int swings)
    {
        var feats = great ? "\"power-attack\", \"cleave\", \"great-cleave\"" : "\"power-attack\", \"cleave\"";
        var fighter = Fighter(6, feats);
        var first = ClassKit.Dummy("First");
        var second = ClassKit.Dummy("Second");
        var third = ClassKit.Dummy("Third");
        var field = ClassKit.Field((fighter, 2, 2), (first, 3, 1), (second, 3, 2), (third, 3, 3));
        var encounter = ClassKit.Fight([fighter], [first, second, third], field, 15, 4, 15, 4, 15, 4);

        var result = Assert.IsType<FullAttackResult>(encounter.BeginNextTurn()!.Take(new CleaveAction(first)));

        Assert.Equal(swings, result.Strikes.Count);
        Assert.Equal(new[] { first, second, third }[..swings], result.Strikes.Select(strike => strike.Target));
    }

    [Theory]
    [InlineData("\"vital-strike\"", 1)]
    [InlineData("\"vital-strike\", \"improved-vital-strike\"", 2)]
    [InlineData("\"vital-strike\", \"improved-vital-strike\", \"greater-vital-strike\"", 3)]
    public void TheVitalStrikesRollTheDiceTwiceThreeTimesAndFour(string feats, int extra)
    {
        var fighter = Fighter(16, feats);
        var foe = ClassKit.Dummy();

        Assert.Equal(extra, Martial.VitalStrikeSets(fighter));

        var strike = Strike.Resolve(fighter, fighter.MeleeAttack!, foe, SequenceRandom.Always(5), vital: true);
        Assert.Equal(1 + extra, strike.Damage!.Entries.Count);
    }

    [Fact]
    public void GreaterWeaponFocusIsOneMoreWithTheSameWeapon()
    {
        var focused = Fighter(8, "\"weapon-focus:longsword\"", "\"longsword\", \"greataxe\"");
        var greater = Fighter(8, "\"weapon-focus:longsword\", \"greater-weapon-focus:longsword\"", "\"longsword\", \"greataxe\"");
        var axe = (Creature who) => who.Attacks.Single(weapon => weapon.Kind == "greataxe");

        Assert.Equal(Strike.AttackBonus(focused, focused.MeleeAttack!).Total + 1, Strike.AttackBonus(greater, greater.MeleeAttack!).Total);
        Assert.Equal(Strike.AttackBonus(focused, axe(focused)).Total, Strike.AttackBonus(greater, axe(greater)).Total);
    }

    [Fact]
    public void SpecializationIsTwoWithItsWeaponAndTheGreaterTwoMore()
    {
        var plain = Fighter(12, "\"weapon-focus:longsword\"", "\"longsword\", \"greataxe\"");
        var special = Fighter(12, "\"weapon-focus:longsword\", \"weapon-specialization:longsword\"", "\"longsword\", \"greataxe\"");
        var greater = Fighter(
            12,
            "\"weapon-focus:longsword\", \"greater-weapon-focus:longsword\", \"weapon-specialization:longsword\", \"greater-weapon-specialization:longsword\"",
            "\"longsword\", \"greataxe\"");
        var axe = (Creature who) => who.Attacks.Single(weapon => weapon.Kind == "greataxe");

        Assert.Equal(Strike.DamageBonus(plain, plain.MeleeAttack!).Total + 2, Strike.DamageBonus(special, special.MeleeAttack!).Total);
        Assert.Equal(Strike.DamageBonus(plain, plain.MeleeAttack!).Total + 4, Strike.DamageBonus(greater, greater.MeleeAttack!).Total);
        Assert.Equal(Strike.DamageBonus(plain, axe(plain)).Total, Strike.DamageBonus(special, axe(special)).Total);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void CriticalFocusIsFourOnTheConfirmationAndOnlyThere(bool focus, bool confirmed)
    {
        var fighter = Fighter(9, focus ? "\"critical-focus\"" : string.Empty);
        var bonus = Strike.AttackBonus(fighter, fighter.MeleeAttack!).Total;

        // Armour a confirmation roll of 10 falls four short of.
        var foe = ClassKit.Dummy(armour: bonus + 4);

        var strike = Strike.Resolve(fighter, fighter.MeleeAttack!, foe, new SequenceRandom(19, 10, 4, 4, 4, 4));

        Assert.True(strike.IsHit);
        Assert.Equal(confirmed, strike.IsCritical);
        Assert.Equal(10 + bonus + (focus ? 4 : 0), strike.Attack.ConfirmationTotal);
    }

    [Fact]
    public void PenetratingStrikeIgnoresFiveOfABypassableReductionWithAFocusedWeapon()
    {
        var fighter = Fighter(12, "\"weapon-focus:longsword\", \"penetrating-strike\"");
        var wolf = ClassKit.Dummy("Wolf", hitPoints: 200);
        wolf.Defenses.Reduce(10, DamageBypass.Silver);
        var stone = ClassKit.Dummy("Stone", hitPoints: 200);
        stone.Defenses.Reduce(10);

        Assert.Equal(5, Martial.Penetration(fighter, fighter.MeleeAttack!));

        var bitten = Strike.Resolve(fighter, fighter.MeleeAttack!, wolf, new SequenceRandom(15, 8));
        var blunted = Strike.Resolve(fighter, fighter.MeleeAttack!, stone, new SequenceRandom(15, 8));

        Assert.Equal(5, bitten.Taken!.AbsorbedByReduction);
        Assert.Equal(10, blunted.Taken!.AbsorbedByReduction);
    }

    [Fact]
    public void ArcaneStrikeMakesTheBladeMagicAndAPointHarder()
    {
        var wizard = ClassKit.Make("wizard", 5, "\"items\": [\"quarterstaff\"], \"feats\": [\"arcane-strike\"]");
        var golem = ClassKit.Dummy("Golem", hitPoints: 200);
        golem.Defenses.Reduce(5, DamageBypass.Magic);
        var field = ClassKit.Field((wizard, 2, 2), (golem, 3, 2));
        var encounter = ClassKit.Fight([wizard], [golem], field, 15, 4);
        var turn = encounter.BeginNextTurn()!;
        var before = Strike.DamageBonus(wizard, wizard.MeleeAttack!).Total;

        Assert.NotNull(turn.Take(new ArcaneStrikeAction()));
        Assert.Equal(ActionCost.Swift, new ArcaneStrikeAction().Cost);

        // One, and one more for five arcane caster levels.
        Assert.Equal(before + 2, Strike.DamageBonus(wizard, wizard.MeleeAttack!).Total);

        var result = Assert.IsType<AttackActionResult>(turn.Take(new AttackAction(wizard.MeleeAttack!, golem)));
        Assert.Equal(0, result.Strike!.Taken!.AbsorbedByReduction);
    }
}

/// <summary>Lunge, Step Up, Stand Still, Spring Attack and Whirlwind Attack: feats about where people stand.</summary>
public class PositionFeatTests
{
    private static Creature Fighter(int level, string feats, int dexterity = 14) =>
        ClassKit.Make("fighter", level, $"\"items\": [\"longsword\"], \"feats\": [{feats}]", [16, dexterity, 14, 13, 10, 10]);

    [Fact]
    public void ALungeReachesFiveFeetFurtherForTheTurnAndCostsTwoArmour()
    {
        var fighter = Fighter(6, "\"lunge\"");
        var foe = ClassKit.Dummy();
        var field = ClassKit.Field((fighter, 2, 2), (foe, 4, 2));
        var encounter = ClassKit.Fight([fighter], [foe], field, 15, 4);
        var turn = encounter.BeginNextTurn()!;
        var armour = fighter.ArmorClass.Total;

        Assert.False(turn.CanTake(new AttackAction(fighter.MeleeAttack!, foe)));
        Assert.NotNull(turn.Take(new LungeAction()));
        Assert.Equal(armour - 2, fighter.ArmorClass.Total);
        Assert.NotNull(turn.Take(new AttackAction(fighter.MeleeAttack!, foe)));

        // The reach was the turn's; the armour class stays down until the next one.
        turn.End();
        Assert.False(encounter.CombatantFor(fighter)!.IsLunging);
        Assert.Equal(armour - 2, fighter.ArmorClass.Total);
    }

    [Fact]
    public void ALungeHasToBeDecidedBeforeAnyAttack()
    {
        var fighter = Fighter(6, "\"lunge\"");
        var foe = ClassKit.Dummy();
        var field = ClassKit.Field((fighter, 2, 2), (foe, 3, 2));
        var encounter = ClassKit.Fight([fighter], [foe], field, 15, 4);
        var turn = encounter.BeginNextTurn()!;

        turn.Take(new AttackAction(fighter.MeleeAttack!, foe));

        Assert.False(turn.CanTake(new LungeAction()));
    }

    [Fact]
    public void StepUpFollowsAFoeThatStepsAwayAndCostsTheNextTurnsStep()
    {
        var fighter = Fighter(1, "\"step-up\"");
        var foe = ClassKit.Dummy();
        var field = ClassKit.Field((foe, 2, 2), (fighter, 3, 2));
        var encounter = ClassKit.Fight([foe], [fighter], field);

        var result = encounter.BeginNextTurn()!.Take(FiveFootStepAction.To(new GridSquare(2, 2), new GridSquare(1, 2)));

        Assert.Contains("steps up", result!.Description);
        Assert.True(Distance.AreAdjacent(field.SquareOf(fighter)!.Value, field.SquareOf(foe)!.Value));

        var next = encounter.BeginNextTurn()!;
        Assert.Same(fighter, next.Actor);
        Assert.False(next.Budget.HasSwift);
        Assert.False(next.CanTake(FiveFootStepAction.To(field.SquareOf(fighter)!.Value, new GridSquare(2, 4))));
    }

    [Fact]
    public void WithoutStepUpTheFoeIsLeftBehind()
    {
        var fighter = Fighter(1, string.Empty);
        var foe = ClassKit.Dummy();
        var field = ClassKit.Field((foe, 2, 2), (fighter, 3, 2));
        var encounter = ClassKit.Fight([foe], [fighter], field);

        encounter.BeginNextTurn()!.Take(FiveFootStepAction.To(new GridSquare(2, 2), new GridSquare(1, 2)));

        Assert.Equal(new GridSquare(3, 2), field.SquareOf(fighter));
    }

    [Fact]
    public void StandStillSpendsTheSwingStoppingTheWalker()
    {
        var guard = Fighter(6, "\"combat-reflexes\", \"stand-still\"");
        var walker = ClassKit.Dummy("Walker");
        var field = ClassKit.Field((walker, 2, 2), (guard, 3, 3));
        var encounter = ClassKit.Fight([walker], [guard], field, 15);
        var walk = new MoveAction([new GridSquare(2, 2), new GridSquare(1, 2), new GridSquare(0, 2)]);

        encounter.BeginNextTurn();
        encounter.BeginNextTurn();
        var turn = encounter.BeginNextTurn()!;
        var result = Assert.IsType<MoveActionResult>(turn.Take(walk));

        Assert.Empty(result.Opportunities);
        Assert.True(Assert.Single(result.Held).Succeeded);
        Assert.Equal(new GridSquare(2, 2), field.SquareOf(walker));
        Assert.True(result.WasInterrupted);
        Assert.False(turn.CanTake(new MoveAction([new GridSquare(2, 2), new GridSquare(1, 2)])));
    }

    [Fact]
    public void SpringAttackWalksStrikesAndWalksOnWithoutTheTargetSwingingBack()
    {
        var dancer = Fighter(4, "\"dodge\", \"mobility\", \"spring-attack\"", 14);
        var target = ClassKit.Dummy("Target");
        var field = ClassKit.Field((dancer, 0, 2), (target, 3, 2));
        var encounter = ClassKit.Fight([dancer], [target], field, 15, 4);
        var path = new[] { new GridSquare(0, 2), new GridSquare(1, 2), new GridSquare(2, 2), new GridSquare(2, 3), new GridSquare(1, 4) };

        var result = Assert.IsType<MoveAndAttackResult>(
            encounter.BeginNextTurn()!.Take(new SpringAttackAction(path, 2, target)));

        Assert.True(result.Attack!.Strike!.IsHit);
        Assert.Empty(result.After!.Opportunities);
        Assert.Equal(new GridSquare(1, 4), field.SquareOf(dancer));
    }

    [Fact]
    public void SpringAttackNeedsARunUpAndAFoeNotAlreadyBesideIt()
    {
        var dancer = Fighter(4, "\"dodge\", \"mobility\", \"spring-attack\"");
        var target = ClassKit.Dummy("Target");
        var field = ClassKit.Field((dancer, 1, 2), (target, 3, 2));
        var encounter = ClassKit.Fight([dancer], [target], field);
        var turn = encounter.BeginNextTurn()!;

        // Five feet before the swing is not ten.
        Assert.False(turn.CanTake(new SpringAttackAction([new GridSquare(1, 2), new GridSquare(2, 2), new GridSquare(2, 3)], 1, target)));

        field.Place(dancer, new GridSquare(2, 2));
        Assert.False(turn.CanTake(new SpringAttackAction([new GridSquare(2, 2), new GridSquare(1, 2), new GridSquare(2, 1), new GridSquare(2, 2)], 2, target)));
    }

    [Fact]
    public void WhirlwindAttackSwingsOnceAtEveryFoeInReach()
    {
        var whirl = ClassKit.Make(
            "fighter", 4, "\"items\": [\"longsword\"], \"feats\": [\"combat-expertise\", \"dodge\", \"mobility\", \"spring-attack\", \"whirlwind-attack\"]",
            [16, 14, 14, 13, 10, 10]);
        var north = ClassKit.Dummy("North");
        var south = ClassKit.Dummy("South");
        var far = ClassKit.Dummy("Far");
        var field = ClassKit.Field((whirl, 3, 3), (north, 3, 2), (south, 3, 4), (far, 8, 8));
        var encounter = ClassKit.Fight([whirl], [north, south, far], field, 15, 4, 15, 4);

        var result = Assert.IsType<FullAttackResult>(encounter.BeginNextTurn()!.Take(new WhirlwindAttackAction()));

        Assert.Equal([north, south], result.Strikes.Select(strike => strike.Target));
    }
}

/// <summary>Greater Trip and Greater Bull Rush, and the attacks of opportunity they draw.</summary>
public class GreaterManeuverTests
{
    [Fact]
    public void GreaterTripIsTwoMoreAndTheFallDrawsSwings()
    {
        var valeria = TestContent.Library.BuildCreature("valeria")!;
        var friend = ClassKit.Make("fighter", 1, "\"items\": [\"longsword\"]");
        var foe = ClassKit.Dummy("Foe");
        var field = ClassKit.Field((valeria, 2, 2), (foe, 3, 2), (friend, 4, 2));

        Assert.Contains(Maneuvers.Bonus(valeria, ManeuverKind.Trip).Entries, entry => entry.Modifier.Source == "Greater Trip");

        // Valeria's trip, then her swing as it goes down, then the friend's.
        var encounter = ClassKit.Fight([valeria, friend], [foe], field, 15, 15, 4, 15, 4);
        var result = Assert.IsType<ManeuverActionResult>(encounter.BeginNextTurn()!.Take(new TripAction(foe)));

        Assert.True(result.Check!.Succeeded);
        Assert.Equal(2, result.Drawn.Count);
        Assert.Contains("provoking 2 as it falls", result.Description);
    }

    [Fact]
    public void GreaterBullRushDrawsSwingsFromTheShoversFriendsAndNotFromTheShover()
    {
        var karn = ClassKit.Make(
            "fighter", 6, "\"items\": [\"longsword\"], \"feats\": [\"power-attack\", \"improved-bull-rush\", \"greater-bull-rush\"]",
            [18, 12, 14, 10, 10, 10]);
        // Slower than Karn, so that the turn taken first is his.
        var friend = ClassKit.Make("fighter", 1, "\"items\": [\"longsword\"]", [14, 10, 14, 10, 10, 10]);
        var foe = ClassKit.Dummy("Foe");
        var field = ClassKit.Field((karn, 2, 2), (foe, 3, 2), (friend, 4, 3));
        var encounter = ClassKit.Fight([karn, friend], [foe], field, 15, 15, 4);

        var result = Assert.IsType<ManeuverActionResult>(encounter.BeginNextTurn()!.Take(new BullRushAction(foe)));

        Assert.True(result.Check!.Succeeded);
        var swing = Assert.Single(result.Drawn);
        Assert.Same(friend, swing.Attacker);
    }
}

/// <summary>Intimidate and the feats built on it: demoralise, Dazzling Display, Shatter Defenses, Deadly Stroke.</summary>
public class IntimidationFeatTests
{
    [Fact]
    public void DemoralizeIsIntimidateAgainstTenPlusHitDicePlusWisdom()
    {
        var foe = ClassKit.Make("warrior", 3, abilities: [10, 10, 10, 10, 14, 10]);

        Assert.Equal(15, DemoralizeAction.DifficultyFor(foe));
    }

    [Fact]
    public void ADemoraliseShakesForARoundAndOneMoreEveryFiveOver()
    {
        var valeria = TestContent.Library.BuildCreature("valeria")!;
        var foe = ClassKit.Dummy("Foe");
        var field = ClassKit.Field((valeria, 2, 2), (foe, 5, 2));
        var dc = DemoralizeAction.DifficultyFor(foe);
        var bonus = valeria.Skills.Total(Ironbound.Rules.Skills.Skill.Intimidate);

        // Ten over: three rounds.
        var encounter = ClassKit.Fight([valeria], [foe], field, dc - bonus + 10);
        var result = Assert.IsType<SkillActionResult>(encounter.BeginNextTurn()!.Take(new DemoralizeAction(foe)));

        Assert.True(foe.Has(Condition.Shaken));
        Assert.Equal(Duration.Rounds(3).Ticks, foe.Effects.Find(DemoralizeAction.EffectName)!.TicksRemaining);
        Assert.Contains("shaken for 3 round(s)", result.Description);
    }

    [Fact]
    public void ASecondDemoraliseLengthensTheFirstAndNeverFrightens()
    {
        var foe = ClassKit.Dummy("Foe");
        var check = new Ironbound.Rules.Skills.SkillCheck
        {
            Skill = Ironbound.Rules.Skills.Skill.Intimidate,
            Actor = foe,
            NaturalRoll = 20,
            Bonus = new ModifierStack().Explain(),
            Difficulty = 11,
            Untrained = false,
        };

        DemoralizeAction.Shake(foe, check, 11);
        DemoralizeAction.Shake(foe, check, 11);

        Assert.Equal(Duration.Rounds(4).Ticks, foe.Effects.Find(DemoralizeAction.EffectName)!.TicksRemaining);
        Assert.False(foe.Has(Condition.Frightened));
    }

    [Fact]
    public void ADazzlingDisplayShakesEveryFoeInSightWithOneRoll()
    {
        var valeria = TestContent.Library.BuildCreature("valeria")!;
        valeria.Feats.Add(TestContent.Library.GetFeat("dazzling-display")!);
        var near = ClassKit.Dummy("Near");
        var also = ClassKit.Dummy("Also");
        var far = ClassKit.Dummy("Far");
        var field = new Battlefield(20, 20);
        field.Place(valeria, 2, 2);
        field.Place(near, 4, 2);
        field.Place(also, 2, 5);
        field.Place(far, 15, 15);
        var encounter = ClassKit.Fight([valeria], [near, also, far], field, 20);

        var turn = encounter.BeginNextTurn()!;
        var result = Assert.IsType<SkillActionResult>(turn.Take(new DazzlingDisplayAction()));

        Assert.Equal(2, result.Checks.Count);
        Assert.True(near.Has(Condition.Shaken));
        Assert.True(also.Has(Condition.Shaken));
        Assert.False(far.Has(Condition.Shaken));
        Assert.False(turn.Budget.HasMove);
    }

    [Fact]
    public void ADazzlingDisplayWantsTheFocusedWeaponInHand()
    {
        var karn = TestContent.Library.BuildCreature("karn")!;
        karn.Feats.Add(TestContent.Library.GetFeat("dazzling-display")!);

        Assert.Null(DazzlingDisplayAction.FocusedWeapon(karn));
    }

    [Fact]
    public void ShatteredDefencesLeaveAFrightenedFoeFlatFootedToWhoeverHitIt()
    {
        var fighter = ClassKit.Make(
            "fighter", 6, "\"items\": [\"longsword\"], \"feats\": [\"weapon-focus:longsword\", \"dazzling-display\", \"shatter-defenses\"]");
        var other = ClassKit.Make("fighter", 1, "\"items\": [\"longsword\"]");
        var foe = ClassKit.Dummy("Foe");
        foe.Effects.Apply(ConditionInfo.Effect(Condition.Shaken, Duration.Rounds(10)));
        var field = ClassKit.Field((fighter, 2, 2), (foe, 3, 2), (other, 3, 3));
        var encounter = ClassKit.Fight([fighter, other], [foe], field, 15, 4);

        // Everybody has had a turn, so nobody is flat-footed for want of one.
        encounter.BeginNextTurn();
        encounter.BeginNextTurn();
        encounter.BeginNextTurn();
        var turn = encounter.BeginNextTurn()!;
        Assert.Same(fighter, turn.Actor);
        Assert.False(encounter.IsFlatFootedTo(foe, fighter));

        turn.Take(new AttackAction(fighter.MeleeAttack!, foe));

        Assert.True(encounter.IsFlatFootedTo(foe, fighter));
        Assert.False(encounter.IsFlatFootedTo(foe, other));

        // It lasts through the end of the attacker's next turn, and no further.
        turn.End();
        encounter.BeginNextTurn()!.End();
        encounter.BeginNextTurn()!.End();
        Assert.True(encounter.IsFlatFootedTo(foe, fighter));
        encounter.BeginNextTurn()!.End();
        Assert.False(encounter.IsFlatFootedTo(foe, fighter));
    }

    [Fact]
    public void DeadlyStrokeDoublesTheDamageAndBleedsConstitution()
    {
        var fighter = ClassKit.Make(
            "fighter", 11,
            "\"items\": [\"longsword\"], \"feats\": [\"weapon-focus:longsword\", \"greater-weapon-focus:longsword\", \"dazzling-display\", \"shatter-defenses\", \"deadly-stroke\"]");
        var foe = ClassKit.Dummy("Foe", hitPoints: 200);
        foe.Effects.Apply(ConditionInfo.Effect(Condition.Stunned, Duration.Rounds(2)));
        var field = ClassKit.Field((fighter, 2, 2), (foe, 3, 2));
        var encounter = ClassKit.Fight([fighter], [foe], field, 15, 4, 4);

        var result = Assert.IsType<AttackActionResult>(encounter.BeginNextTurn()!.Take(new DeadlyStrokeAction(foe)));

        var bonus = Strike.DamageBonus(fighter, fighter.MeleeAttack!).Total;
        Assert.Equal(2 * (4 + bonus), result.Strike!.Damage!.Total);
        Assert.True(foe.Effects.Has(Bleeds.DeadlyStrokeLabel));

        var constitution = foe.Abilities[Ironbound.Rules.Abilities.Ability.Constitution].Score;
        foe.Effects.Advance(Duration.Rounds(1), new SequenceRandom(true, 10));
        Assert.Equal(constitution - 1, foe.Abilities[Ironbound.Rules.Abilities.Ability.Constitution].Score);
    }

    [Fact]
    public void DeadlyStrokeWantsAHelplessFoe()
    {
        var fighter = ClassKit.Make(
            "fighter", 11,
            "\"items\": [\"longsword\"], \"feats\": [\"weapon-focus:longsword\", \"greater-weapon-focus:longsword\", \"deadly-stroke\"]");
        var foe = ClassKit.Dummy("Foe");
        var field = ClassKit.Field((fighter, 2, 2), (foe, 3, 2));
        var encounter = ClassKit.Fight([fighter], [foe], field);

        encounter.BeginNextTurn();
        encounter.BeginNextTurn();

        Assert.False(encounter.BeginNextTurn()!.CanTake(new DeadlyStrokeAction(foe)));
    }
}
