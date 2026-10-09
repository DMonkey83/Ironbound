using Ironbound.Rules.Abilities;
using Ironbound.Rules.Classes;
using Ironbound.Rules.Conditions;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Dice;
using Ironbound.Rules.Effects;
using Ironbound.Rules.Encounters;
using Ironbound.Rules.Encounters.Actions;
using Ironbound.Rules.Magic;
using Ironbound.Rules.Maps;
using Ironbound.Rules.Saves;
using Ironbound.Rules.Tests.Classes;

namespace Ironbound.Rules.Tests.Feats;

/// <summary>Concentration, casting defensively, and Combat Casting.</summary>
public class ConcentrationTests
{
    private static Creature Wizard(string feats = "") =>
        ClassKit.Make("wizard", 1, $"\"spells\": [\"magic-missile\"], \"feats\": [{feats}]", [10, 14, 10, 16, 10, 10]);

    [Fact]
    public void TheDifficultiesAreTheBooks()
    {
        Assert.Equal(17, Concentration.DefensiveDifficulty(1));
        Assert.Equal(21, Concentration.DefensiveDifficulty(3));
        Assert.Equal(17, Concentration.InjuredDifficulty(6, 1));
        Assert.Equal(16, Concentration.EntangledDifficulty(1));
    }

    [Fact]
    public void TheBonusIsCasterLevelAndCastingAbilityAndCombatCastingOnlyWhenDefensive()
    {
        var plain = Wizard();
        var trained = Wizard("\"combat-casting\"");

        Assert.Equal(4, Concentration.Bonus(plain, 1, ConcentrationReason.Defensive).Total);
        Assert.Equal(8, Concentration.Bonus(trained, 1, ConcentrationReason.Defensive).Total);
        Assert.Equal(4, Concentration.Bonus(trained, 1, ConcentrationReason.Injured).Total);
        Assert.Equal(40, Concentration.DefensiveChance(plain, 1, 1));
        Assert.Equal(60, Concentration.DefensiveChance(trained, 1, 1));
    }

    [Theory]
    [InlineData(13, false)]
    [InlineData(12, true)]
    public void CastingDefensivelyDrawsNoSwingButCanLoseTheSpell(int roll, bool lost)
    {
        var wizard = Wizard();
        var orc = ClassKit.Make("warrior", 1, "\"items\": [\"shortspear\"]");
        var field = ClassKit.Field((wizard, 2, 2), (orc, 3, 2));
        var encounter = ClassKit.Fight([wizard], [orc], field, roll, 3);
        var slots = wizard.Spells.SlotsRemaining(1);

        var result = Assert.IsType<CastSpellResult>(
            encounter.BeginNextTurn()!.Take(CastSpellAction.At(Spells.MagicMissile, orc).AsDefensive()));

        Assert.Empty(result.Opportunities);
        Assert.Equal(lost, result.Lost);
        Assert.Equal(ConcentrationReason.Defensive, Assert.Single(result.Concentration).Reason);
        Assert.Equal(slots - 1, wizard.Spells.SlotsRemaining(1));
        Assert.Contains(lost ? "loses Magic Missile" : "cast defensively", result.Description);
    }

    [Fact]
    public void AnOrdinaryCastInReachDrawsTheSwingAndTheWoundHasToBeConcentratedThrough()
    {
        var wizard = Wizard();
        var orc = ClassKit.Make("warrior", 1, "\"items\": [\"shortspear\"]");
        var field = ClassKit.Field((wizard, 2, 2), (orc, 3, 2));

        // The orc's spear hits for 1d6+1; then a concentration roll of 2 cannot hold against it.
        var encounter = ClassKit.Fight([wizard], [orc], field, 18, 3, 2);

        var result = Assert.IsType<CastSpellResult>(encounter.BeginNextTurn()!.Take(CastSpellAction.At(Spells.MagicMissile, orc)));

        Assert.Single(result.Opportunities);
        var check = Assert.Single(result.Concentration);
        Assert.Equal(ConcentrationReason.Injured, check.Reason);
        Assert.Equal(10 + result.Opportunities[0].Taken!.Total + 1, check.Difficulty);
        Assert.True(result.Lost);
    }

    [Fact]
    public void ACasterEntangledHasToConcentrateToCastAtAll()
    {
        var wizard = Wizard();
        wizard.Effects.Apply(ConditionInfo.Effect(Condition.Entangled, Duration.Rounds(3)));
        var foe = ClassKit.Dummy();
        var field = ClassKit.Field((wizard, 2, 2), (foe, 7, 2));
        var encounter = ClassKit.Fight([wizard], [foe], field, 12, 3);

        var result = Assert.IsType<CastSpellResult>(encounter.BeginNextTurn()!.Take(CastSpellAction.At(Spells.MagicMissile, foe)));

        // Twelve and four is sixteen: just enough.
        var check = Assert.Single(result.Concentration);
        Assert.Equal(ConcentrationReason.Entangled, check.Reason);
        Assert.Equal(16, check.Difficulty);
        Assert.False(result.Lost);
    }

    [Fact]
    public void ASpellLikeAbilityCanBeUsedDefensivelyToo()
    {
        var sylwen = TestContent.Library.BuildCreature("sylwen")!;
        var orc = ClassKit.Make("warrior", 1, "\"items\": [\"shortspear\"]");
        var field = ClassKit.Field((sylwen, 2, 2), (orc, 3, 2));
        var dart = sylwen.Powers.Single(power => power.Id == "acid-dart");
        var encounter = ClassKit.Fight([sylwen], [orc], field, 19, 15, 3);

        var result = Assert.IsType<CastSpellResult>(encounter.BeginNextTurn()!.Take(UsePowerAction.At(dart, orc).AsDefensive()));

        Assert.Empty(result.Opportunities);
        Assert.Equal(15, Assert.Single(result.Concentration).Difficulty);
        Assert.Contains("used defensively", result.Description);
    }

    [Fact]
    public void ChannelEnergyIsSupernaturalAndNeedsNoConcentration()
    {
        var hale = TestContent.Library.BuildCreature("hale")!;
        var orc = ClassKit.Make("warrior", 1, "\"items\": [\"shortspear\"]");
        hale.HitPoints.Take(3);
        var field = ClassKit.Field((hale, 2, 2), (orc, 3, 2));
        var encounter = ClassKit.Fight([hale], [orc], field, 4);

        var result = Assert.IsType<CastSpellResult>(encounter.BeginNextTurn()!.Take(UsePowerAction.Self(hale.Powers.First())));

        Assert.Empty(result.Concentration);
    }
}

/// <summary>The metamagic feats, spell focus, spell resistance and the casting floor.</summary>
public class SpellFeatTests
{
    private static Creature Wizard(int level, string feats, int intelligence = 18) =>
        ClassKit.Make(
            "wizard", level, $"\"spells\": [\"magic-missile\", \"scorching-ray\", \"fireball\", \"cause-fear\"], \"feats\": [{feats}]",
            [10, 14, 10, intelligence, 10, 10]);

    [Fact]
    public void EachMetamagicTakesItsOwnHigherSlot()
    {
        var missile = Spells.MagicMissile;

        Assert.Equal(2, missile.With(Metamagic.Extend).SlotLevel);
        Assert.Equal(2, missile.With(Metamagic.Enlarge).SlotLevel);
        Assert.Equal(3, missile.Empower().SlotLevel);
        Assert.Equal(4, missile.With(Metamagic.Maximize).SlotLevel);
        Assert.Equal(4, missile.With(Metamagic.Widen).SlotLevel);
        Assert.Equal(5, missile.With(Metamagic.Quicken).SlotLevel);
        Assert.Equal(6, missile.Empower().With(Metamagic.Maximize).SlotLevel);
        Assert.Equal(3, missile.Heighten(3).SlotLevel);
        Assert.Equal(3, missile.Heighten(3).EffectiveLevel);
    }

    [Fact]
    public void AMetamagicSpellNeedsItsFeat()
    {
        var plain = Wizard(9, string.Empty);
        var trained = Wizard(9, "\"maximize-spell\"");

        Assert.False(plain.Spells.CanCast(Spells.MagicMissile.With(Metamagic.Maximize)));
        Assert.True(trained.Spells.CanCast(Spells.MagicMissile.With(Metamagic.Maximize)));
        Assert.False(trained.Spells.CanCast(Spells.MagicMissile.With(Metamagic.Maximize | Metamagic.Extend)));
    }

    [Fact]
    public void AMaximizedSpellRollsItsDiceAtTheirTop()
    {
        var wizard = Wizard(1, "\"maximize-spell\"");
        var foe = ClassKit.Dummy();

        var cast = Casting.Resolve(wizard, Spells.MagicMissile.With(Metamagic.Maximize), SpellAim.At(foe), new SequenceRandom(1));

        Assert.Equal(5, cast.Targets.Single().Damage);
        Assert.StartsWith("maximized", cast.Spell.MetamagicWords);
    }

    [Fact]
    public void EmpoweredAndMaximizedIsTheTopAndHalfWhatWasRolled()
    {
        var wizard = Wizard(1, "\"maximize-spell\", \"empower-spell\"");
        var foe = ClassKit.Dummy();

        var cast = Casting.Resolve(wizard, Spells.MagicMissile.With(Metamagic.Maximize).Empower(), SpellAim.At(foe), new SequenceRandom(3));

        Assert.Equal(5 + 2, cast.Targets.Single().Damage);
    }

    [Fact]
    public void AnExtendedSpellLastsTwiceAsLong()
    {
        var wizard = Wizard(3, "\"extend-spell\"");
        var foe = ClassKit.Dummy();
        var fear = TestContent.Library.GetSpell("cause-fear")!;

        Casting.Resolve(wizard, fear.With(Metamagic.Extend), SpellAim.At(foe), new SequenceRandom(1));

        Assert.Equal(Duration.Rounds(6).Ticks, foe.Effects.Find("Shaken")!.TicksRemaining);
    }

    [Fact]
    public void AWidenedBurstCatchesTwiceAsFar()
    {
        var wizard = Wizard(5, "\"widen-spell\"");
        var near = ClassKit.Dummy("Near", hitPoints: 200);
        var far = ClassKit.Dummy("Far", hitPoints: 200);
        var field = new Battlefield(30, 3);
        field.Place(wizard, 0, 0);
        field.Place(near, 20, 0);
        field.Place(far, 27, 0);

        var plain = Casting.Gather(wizard, Spells.Fireball, SpellAim.At(near), field);
        var widened = Casting.Gather(wizard, Spells.Fireball.With(Metamagic.Widen), SpellAim.At(near), field);

        Assert.DoesNotContain(far, plain);
        Assert.Contains(far, widened);
    }

    [Fact]
    public void AnEnlargedSpellReachesTwiceAsFar()
    {
        var wizard = Wizard(1, "\"enlarge-spell\"");

        Assert.Equal(110, Spells.MagicMissile.RangeInFeet(wizard, 1));
        Assert.Equal(220, Spells.MagicMissile.With(Metamagic.Enlarge).RangeInFeet(wizard, 1));
    }

    [Fact]
    public void AQuickenedSpellIsASwiftActionAndDrawsNoSwing()
    {
        var wizard = Wizard(9, "\"quicken-spell\"");
        var orc = ClassKit.Make("warrior", 1, "\"items\": [\"shortspear\"]");
        var field = ClassKit.Field((wizard, 2, 2), (orc, 3, 2));
        var encounter = ClassKit.Fight([wizard], [orc], field, 3, 3, 3, 3, 3);
        var turn = encounter.BeginNextTurn()!;

        var quick = CastSpellAction.At(Spells.MagicMissile.With(Metamagic.Quicken), orc);
        Assert.Equal(ActionCost.Swift, quick.Cost);

        var result = Assert.IsType<CastSpellResult>(turn.Take(quick));
        Assert.Empty(result.Opportunities);
        Assert.True(turn.Budget.HasStandard);
    }

    [Fact]
    public void AHeightenedSpellCountsAtItsNewLevelForItsDifficulty()
    {
        var wizard = Wizard(5, "\"heighten-spell\"");
        var fear = TestContent.Library.GetSpell("cause-fear")!;

        Assert.Equal(wizard.Spells.SaveDC(fear) + 2, wizard.Spells.SaveDC(fear.Heighten(3)));
        Assert.True(wizard.Spells.CanCast(fear.Heighten(3)));
    }

    [Fact]
    public void SpellFocusIsOneOnItsSchoolAndGreaterSpellFocusAnother()
    {
        var plain = Wizard(5, string.Empty);
        var focused = Wizard(5, "\"spell-focus:Evocation\"");
        var greater = Wizard(5, "\"spell-focus:Evocation\", \"greater-spell-focus:Evocation\"");
        var fear = TestContent.Library.GetSpell("cause-fear")!;

        Assert.Equal(plain.Spells.SaveDC(Spells.Fireball) + 1, focused.Spells.SaveDC(Spells.Fireball));
        Assert.Equal(plain.Spells.SaveDC(Spells.Fireball) + 2, greater.Spells.SaveDC(Spells.Fireball));
        Assert.Equal(plain.Spells.SaveDC(fear), greater.Spells.SaveDC(fear));
    }

    [Theory]
    [InlineData("", 13, true)]
    [InlineData("\"spell-penetration\"", 13, false)]
    [InlineData("\"spell-penetration\", \"greater-spell-penetration\"", 11, false)]
    public void SpellResistanceIsBeatenByACasterLevelCheck(string feats, int roll, bool resisted)
    {
        var wizard = Wizard(1, feats);
        wizard.Allegiance = 1;
        var demon = ClassKit.Dummy("Demon");
        demon.Allegiance = 2;
        demon.SpellResistance = 15;

        var hit = Casting.Resolve(wizard, Spells.MagicMissile, SpellAim.At(demon), new SequenceRandom(roll, 3)).Targets.Single();

        Assert.Equal(resisted, hit.Damage == 0);
        Assert.Equal(resisted, hit.Applied.Any(line => line.Contains("resists")));
    }

    [Fact]
    public void GreaseIgnoresSpellResistance()
    {
        Assert.False(TestContent.Library.GetSpell("grease")!.AllowsResistance);
        Assert.True(Spells.Fireball.AllowsResistance);
    }

    [Theory]
    [InlineData(11, false)]
    [InlineData(12, true)]
    public void ACasterNeedsTenPlusTheSpellsLevelInHerCastingScore(int intelligence, bool casts)
    {
        var wizard = Wizard(3, string.Empty, intelligence);

        Assert.True(wizard.Spells.SlotsMaximum(2) > 0);
        Assert.Equal(casts, wizard.Spells.CanCast(Spells.ScorchingRay));
        Assert.True(wizard.Spells.CanCast(Spells.MagicMissile));
    }

    [Theory]
    [InlineData(4, 1, 1)]
    [InlineData(4, 4, 1)]
    [InlineData(4, 5, 0)]
    [InlineData(5, 1, 2)]
    [InlineData(5, 5, 1)]
    [InlineData(1, 1, 1)]
    [InlineData(0, 1, 0)]
    public void BonusSpellsFollowTheBooksTable(int modifier, int level, int bonus) =>
        Assert.Equal(bonus, Progression.BonusSlots(modifier, level));
}

/// <summary>The channel feats, and channel energy against the undead it was always meant to burn.</summary>
public class ChannelFeatTests
{
    private static Creature Undead(string name = "Skeleton", int hitDice = 1)
    {
        var undead = new Creature(name, new AbilityScores(10, 10, 10, 10, 10, 10), 30, hitDice) { Type = CreatureType.Undead };
        return undead;
    }

    private static Creature Cleric(string feats, bool negative = false) => ClassKit.Make(
        "cleric", 5, $"\"deity\": \"none\"{(negative ? ", \"channel\": \"Negative\"" : string.Empty)}, \"feats\": [{feats}]",
        [10, 10, 10, 10, 14, 14]);

    [Fact]
    public void PositiveEnergyHealsTheLivingAndBurnsTheUndeadWithOneRoll()
    {
        var cleric = Cleric(string.Empty);
        var friend = ClassKit.Dummy("Friend");
        friend.HitPoints.Take(20);
        var skeleton = Undead();
        var field = ClassKit.Field((cleric, 2, 2), (friend, 3, 2), (skeleton, 4, 2));
        var encounter = ClassKit.Fight([cleric, friend], [skeleton], field, 3, 3, 3, 2);

        var result = Assert.IsType<CastSpellResult>(encounter.BeginNextTurn()!.Take(UsePowerAction.Self(cleric.Powers.First())));

        Assert.Equal(9, result.Cast!.Targets.Single(hit => hit.Target == friend).Healed);
        var burned = result.Cast.Targets.Single(hit => hit.Target == skeleton);
        Assert.Equal(9, burned.Damage);
        Assert.False(burned.Save!.Succeeded);
        Assert.Null(result.Cast.Targets.Single(hit => hit.Target == friend).Save);
    }

    [Fact]
    public void NegativeEnergyHealsTheUndead()
    {
        var cleric = Cleric(string.Empty, negative: true);
        var skeleton = Undead();
        skeleton.HitPoints.Take(20);
        var field = ClassKit.Field((cleric, 2, 2), (skeleton, 3, 2));
        var encounter = ClassKit.Fight([cleric], [skeleton], field, 3, 3, 3);

        var result = Assert.IsType<CastSpellResult>(encounter.BeginNextTurn()!.Take(UsePowerAction.Self(cleric.Powers.First())));

        Assert.Equal(9, result.Cast!.Targets.Single(hit => hit.Target == skeleton).Healed);
    }

    [Fact]
    public void ExtraChannelIsTwoMoreADayEachTime()
    {
        var plain = Cleric(string.Empty);
        var extra = Cleric("\"extra-channel\"");
        var twice = Cleric("\"extra-channel\", \"extra-channel\"");

        Assert.Equal(plain.UsesPerDay(plain.Powers.First()) + 2, extra.UsesPerDay(extra.Powers.First()));
        Assert.Equal(plain.UsesPerDay(plain.Powers.First()) + 4, twice.UsesPerDay(twice.Powers.First()));
    }

    [Fact]
    public void ImprovedChannelIsTwoOnTheDifficulty()
    {
        Assert.Equal(
            ClassPowers.ChannelDifficulty(Cleric(string.Empty)) + 2,
            ClassPowers.ChannelDifficulty(Cleric("\"improved-channel\"")));
    }

    [Fact]
    public void TurnUndeadSendsTheUndeadRunningAndTouchesNobodyElse()
    {
        var cleric = Cleric("\"turn-undead\"");
        var skeleton = Undead();
        var goblin = ClassKit.Dummy("Goblin");
        var field = ClassKit.Field((cleric, 2, 2), (skeleton, 3, 2), (goblin, 2, 3));
        var turn = cleric.Powers.Single(power => power.Id == ClassPowers.TurnUndeadId);
        var encounter = ClassKit.Fight([cleric], [skeleton, goblin], field, 2);

        var result = Assert.IsType<CastSpellResult>(encounter.BeginNextTurn()!.Take(UsePowerAction.Self(turn)));

        Assert.True(skeleton.Has(Condition.Frightened));
        Assert.False(goblin.Has(Condition.Frightened));
        Assert.DoesNotContain(result.Cast!.Targets, hit => hit.Target == goblin);
        Assert.Equal(ClassPowers.ChannelPool, turn.Pool);
        Assert.Equal(cleric.UsesPerDay(cleric.Powers.First()) - 1, cleric.UsesLeft(cleric.Powers.First()));
    }

    [Fact]
    public void CommandUndeadTakesTheUndeadIntoServiceUpToTheClericsLevelInHitDice()
    {
        var cleric = Cleric("\"command-undead\"", negative: true);
        var small = Undead("Small", 2);
        var large = Undead("Large", 4);
        var field = ClassKit.Field((cleric, 2, 2), (small, 3, 2), (large, 2, 3));
        var command = cleric.Powers.Single(power => power.Id == ClassPowers.CommandUndeadId);
        var encounter = ClassKit.Fight([cleric], [small, large], field, 2, 2);

        encounter.BeginNextTurn()!.Take(UsePowerAction.Self(command));

        // Two hit dice and then four would be six, past a fifth-level cleric's five.
        Assert.True(small.IsAllyOf(cleric) ^ large.IsAllyOf(cleric));
        Assert.Equal(
            small.IsAllyOf(cleric) ? 2 : 4,
            new[] { small, large }.Where(undead => undead.IsAllyOf(cleric)).Sum(undead => undead.HitPoints.HitDice));
    }

    [Fact]
    public void AlignmentChannelHealsOrHarmsOneKindOfOutsiderAndNobodyElse()
    {
        var cleric = Cleric("\"alignment-channel:evil\"");
        var demon = new Creature("Demon", new AbilityScores(10, 10, 10, 10, 10, 10), 30, 3) { Type = CreatureType.Outsider };
        demon.Subtypes.Add("evil");
        var friend = ClassKit.Dummy("Friend");
        friend.HitPoints.Take(10);
        var field = ClassKit.Field((cleric, 2, 2), (demon, 3, 2), (friend, 2, 3));
        var harm = cleric.Powers.Single(power => power.Id == "harm-evil-outsiders");
        var encounter = ClassKit.Fight([cleric, friend], [demon], field, 3, 3, 3, 2);

        var result = Assert.IsType<CastSpellResult>(encounter.BeginNextTurn()!.Take(UsePowerAction.Self(harm)));

        Assert.Equal(9, result.Cast!.Targets.Single().Damage);
        Assert.Same(demon, result.Cast.Targets.Single().Target);
        Assert.Equal(10, friend.HitPoints.Damage);
        Assert.Contains(cleric.Powers, power => power.Id == "heal-evil-outsiders");
    }

    [Fact]
    public void ElementalChannelIsTakenForAnElement()
    {
        var cleric = Cleric("\"elemental-channel:fire\"");

        Assert.Contains(cleric.Powers, power => power.Id == "harm-fire-outsiders");
    }

    [Fact]
    public void ChannelSmiteSpendsAChannelOnTheNextBlowAndBurnsTheUndead()
    {
        var cleric = ClassKit.Make("cleric", 5, "\"deity\": \"none\", \"items\": [\"light-mace\"], \"feats\": [\"channel-smite\"]", [14, 10, 10, 10, 14, 14]);
        var skeleton = Undead();
        var field = ClassKit.Field((cleric, 2, 2), (skeleton, 3, 2));
        var encounter = ClassKit.Fight([cleric], [skeleton], field, 18, 3, 3, 3, 3, 2);
        var turn = encounter.BeginNextTurn()!;
        var uses = cleric.UsesLeft(cleric.Powers.First());

        Assert.NotNull(turn.Take(new ChannelSmiteAction()));
        Assert.Equal(uses - 1, cleric.UsesLeft(cleric.Powers.First()));

        var result = Assert.IsType<AttackActionResult>(turn.Take(new AttackAction(cleric.MeleeAttack!, skeleton)));

        Assert.Contains(result.Strike!.Notes, note => note.StartsWith("channel smite: 9 positive", StringComparison.Ordinal));
        Assert.False(cleric.Stances.IsActive(Ironbound.Rules.Combat.Stance.ChannelSmite));
    }

    [Fact]
    public void ExtraRageIsSixMoreRoundsEachTime()
    {
        var plain = ClassKit.Make("barbarian", 3);
        var extra = ClassKit.Make("barbarian", 3, "\"feats\": [\"extra-rage\", \"extra-rage\"]");

        Assert.Equal(plain.RageRoundsPerDay + 12, extra.RageRoundsPerDay);
    }
}
