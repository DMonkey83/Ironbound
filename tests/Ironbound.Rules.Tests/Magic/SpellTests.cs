using Ironbound.Rules.Abilities;
using Ironbound.Rules.Combat;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Dice;
using Ironbound.Rules.Encounters;
using Ironbound.Rules.Encounters.Actions;
using Ironbound.Rules.Magic;
using Ironbound.Rules.Maps;
using Ironbound.Rules.Saves;

namespace Ironbound.Rules.Tests.Magic;

public class SpellDiceTests
{
    [Theory]
    [InlineData(1, "1d6")]
    [InlineData(5, "5d6")]
    [InlineData(10, "10d6")]
    [InlineData(20, "10d6")]
    public void AFireballGainsADieALevelToACap(int casterLevel, string expected)
    {
        var dice = SpellDice.PerLevel(sides: 6, maximumDice: 10);

        Assert.Equal(expected, dice.At(casterLevel).ToString());
    }

    [Theory]
    [InlineData(1, "1d4+1")]
    [InlineData(3, "2d4+2")]
    [InlineData(5, "3d4+3")]
    [InlineData(9, "5d4+5")]
    [InlineData(20, "5d4+5")]
    public void MagicMissileGainsAMissileEveryOtherLevel(int casterLevel, string expected)
    {
        var dice = SpellDice.PerLevel(sides: 4, maximumDice: 5, levelsPerDie: 2, flatPerDie: 1);

        Assert.Equal(expected, dice.At(casterLevel).ToString());
    }

    [Fact]
    public void FixedAmountsIgnoreTheCaster()
    {
        var dice = SpellDice.Fixed("4d6");

        Assert.Equal("4d6", dice.At(1).ToString());
        Assert.Equal("4d6", dice.At(20).ToString());
    }

    [Fact]
    public void RejectsNonsense()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SpellDice.PerLevel(0, 5));
        Assert.Throws<ArgumentOutOfRangeException>(() => SpellDice.PerLevel(6, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => SpellDice.PerLevel(6, 5, levelsPerDie: 0));
    }
}

public class SpellcastingTests
{
    internal static Creature Wizard(int casterLevel = 5)
    {
        var wizard = new Creature("Wizard", new AbilityScores(8, 14, 12, 18, 13, 10), 18, 5)
        {
            Allegiance = 1,
        };

        wizard.Spells.CastingAbility = Ability.Intelligence;
        wizard.Spells.CasterLevel = casterLevel;
        wizard.Spells.SetSlots(1, 3).SetSlots(2, 2).SetSlots(3, 2);

        foreach (var spell in Spells.All)
        {
            wizard.Spells.Prepare(spell);
        }

        return wizard;
    }

    [Theory]
    [InlineData(5, 35)]
    [InlineData(1, 25)]
    [InlineData(10, 50)]
    public void CloseRangeGrowsEveryOtherLevel(int casterLevel, int feet)
    {
        Assert.Equal(feet, SpellRange.Close.InFeet(Wizard(casterLevel)));
    }

    [Fact]
    public void TheOtherBandsGrowFaster()
    {
        var wizard = Wizard(5);

        Assert.Equal(150, SpellRange.Medium.InFeet(wizard));
        Assert.Equal(600, SpellRange.Long.InFeet(wizard));
        Assert.Equal(0, SpellRange.Personal.InFeet(wizard));
        Assert.Equal(75, SpellRange.Of(75).InFeet(wizard));
    }

    [Fact]
    public void TouchIsHoweverFarYouCanActuallyReach()
    {
        var wizard = Wizard();
        Assert.Equal(5, SpellRange.Touch.InFeet(wizard));

        wizard.Size = CreatureSize.Large;
        Assert.Equal(10, SpellRange.Touch.InFeet(wizard));
    }

    [Fact]
    public void TheDifficultyClassIsTenPlusLevelPlusYourMind()
    {
        var wizard = Wizard();

        // Intelligence 18 is +4.
        Assert.Equal(15, wizard.Spells.SaveDC(Spells.MagicMissile));
        Assert.Equal(17, wizard.Spells.SaveDC(Spells.Fireball));
    }

    [Fact]
    public void ABuffToTheCastingAbilityRaisesEveryDifficultyClass()
    {
        var wizard = Wizard();
        wizard.Abilities.Intelligence.Modifiers.Add(
            4, Rules.Modifiers.BonusType.Enhancement, "Fox's Cunning");

        Assert.Equal(19, wizard.Spells.SaveDC(Spells.Fireball));
    }

    [Fact]
    public void CastingSpendsASlot()
    {
        var wizard = Wizard();
        Assert.Equal(2, wizard.Spells.SlotsRemaining(3));

        Assert.True(wizard.Spells.Spend(Spells.Fireball));

        Assert.Equal(1, wizard.Spells.SlotsRemaining(3));
        Assert.Equal(2, wizard.Spells.SlotsMaximum(3));
    }

    [Fact]
    public void AnEmptyLevelIsRefusedAndSpendsNothing()
    {
        var wizard = Wizard();
        wizard.Spells.Spend(Spells.Fireball);
        wizard.Spells.Spend(Spells.Fireball);

        Assert.False(wizard.Spells.CanCast(Spells.Fireball));
        Assert.False(wizard.Spells.Spend(Spells.Fireball));
        Assert.Equal(0, wizard.Spells.SlotsRemaining(3));

        // Other levels are untouched.
        Assert.True(wizard.Spells.CanCast(Spells.MagicMissile));
    }

    [Fact]
    public void AnUnpreparedSpellCannotBeCastAtAll()
    {
        var wizard = Wizard();
        var unknown = new Spell("Wish", 9, SpellSchool.Conjuration);

        Assert.False(wizard.Spells.Knows(unknown));
        Assert.False(wizard.Spells.CanCast(unknown));
    }

    [Fact]
    public void RestingRefillsEverything()
    {
        var wizard = Wizard();
        wizard.Spells.Spend(Spells.Fireball);
        wizard.Spells.Spend(Spells.MagicMissile);

        wizard.Spells.Rest();

        Assert.Equal(2, wizard.Spells.SlotsRemaining(3));
        Assert.Equal(3, wizard.Spells.SlotsRemaining(1));
    }
}

public class SpellTargetingTests
{
    private static Creature Goblin(string name, int allegiance = 2) =>
        new(name, new AbilityScores(11, 15, 12, 10, 9, 6), 9, 2) { Allegiance = allegiance };

    [Fact]
    public void ABurstCatchesEverythingInsideAndNothingOutside()
    {
        var field = new Battlefield(20, 20);
        var wizard = SpellcastingTests.Wizard();
        var near = Goblin("Near");
        var far = Goblin("Far");

        field.Place(wizard, 0, 0);
        field.Place(near, 10, 10);
        field.Place(far, 18, 10);

        var caught = Casting.Gather(
            wizard, Spells.Fireball, SpellAim.At(new GridSquare(10, 10)), field);

        Assert.Equal(["Near"], caught.Select(c => c.Name));
    }

    [Fact]
    public void AFireballDoesNotCareWhoseSideYouAreOn()
    {
        var field = new Battlefield(20, 20);
        var wizard = SpellcastingTests.Wizard();
        var friend = new Creature("Fighter", AbilityScores.All(12), 40, 5) { Allegiance = 1 };
        var foe = Goblin("Goblin");

        field.Place(wizard, 0, 0);
        field.Place(friend, 10, 10);
        field.Place(foe, 11, 10);

        var caught = Casting.Gather(
            wizard, Spells.Fireball, SpellAim.At(new GridSquare(10, 10)), field);

        // The whole tactical problem with the spell, in one assertion.
        Assert.Equal(2, caught.Count);
        Assert.Contains(friend, caught);
    }

    [Fact]
    public void BlessPicksOutYourOwnPeopleIncludingYou()
    {
        var field = new Battlefield(20, 20);
        var wizard = SpellcastingTests.Wizard();
        var friend = new Creature("Fighter", AbilityScores.All(12), 40, 5) { Allegiance = 1 };
        var foe = Goblin("Goblin");

        field.Place(wizard, 5, 5);
        field.Place(friend, 6, 5);
        field.Place(foe, 7, 5);

        var caught = Casting.Gather(wizard, Spells.Bless, SpellAim.At(wizard), field);

        Assert.Equal(2, caught.Count);
        Assert.Contains(wizard, caught);
        Assert.Contains(friend, caught);
        Assert.DoesNotContain(foe, caught);
    }

    [Fact]
    public void ASingleTargetSpellHitsOnlyWhatItIsPointedAt()
    {
        var wizard = SpellcastingTests.Wizard();
        var goblin = Goblin("Goblin");

        var caught = Casting.Gather(wizard, Spells.MagicMissile, SpellAim.At(goblin), field: null);

        Assert.Equal([goblin], caught);
    }

    [Fact]
    public void AnEnemyOnlySpellWillNotTargetAFriend()
    {
        var wizard = SpellcastingTests.Wizard();
        var friend = new Creature("Fighter", AbilityScores.All(12), 40, 5) { Allegiance = 1 };

        Assert.Empty(Casting.Gather(wizard, Spells.MagicMissile, SpellAim.At(friend), field: null));
    }

    [Fact]
    public void ABurstWithoutGroundCatchesNothing()
    {
        var wizard = SpellcastingTests.Wizard();

        Assert.Empty(Casting.Gather(
            wizard, Spells.Fireball, SpellAim.At(new GridSquare(1, 1)), field: null));
    }
}

public class SpellResolutionTests
{
    private static Creature Goblin(string name = "Goblin") =>
        new(name, new AbilityScores(11, 15, 12, 10, 9, 6), 9, 2) { Allegiance = 2 };

    [Fact]
    public void MagicMissileSimplyArrives()
    {
        var wizard = SpellcastingTests.Wizard();
        var goblin = Goblin();

        // Three missiles at caster level five: 3d4+3.
        var cast = Casting.Resolve(
            wizard, Spells.MagicMissile, SpellAim.At(goblin), new SequenceRandom(2, 3, 4));

        var hit = Assert.Single(cast.Targets);
        Assert.Null(hit.Attack);
        Assert.Null(hit.Save);
        Assert.Equal(12, hit.Damage);
        Assert.Equal(goblin.HitPoints.Maximum - 12, goblin.HitPoints.Current);
    }

    [Fact]
    public void ARayCanMiss()
    {
        var field = new Battlefield(20, 20);
        var wizard = SpellcastingTests.Wizard();
        var goblin = Goblin();
        field.Place(wizard, 0, 0);
        field.Place(goblin, 3, 0);

        var cast = Casting.Resolve(
            wizard, Spells.ScorchingRay, SpellAim.At(goblin), new SequenceRandom(2), field: field);

        var shot = Assert.Single(cast.Targets);
        Assert.True(shot.Missed);
        Assert.Equal(0, shot.Damage);
        Assert.Equal(goblin.HitPoints.Maximum, goblin.HitPoints.Current);
    }

    [Fact]
    public void ARayThatLandsBurns()
    {
        var wizard = SpellcastingTests.Wizard();
        var goblin = Goblin();

        // A natural 19 against touch armour class 12, then 4d6.
        var cast = Casting.Resolve(
            wizard, Spells.ScorchingRay, SpellAim.At(goblin), new SequenceRandom(19, 3, 3, 3, 3));

        var shot = Assert.Single(cast.Targets);
        Assert.True(shot.Attack!.IsHit);
        Assert.Equal(12, shot.Damage);
    }

    [Fact]
    public void AFireballRollsOnceAndEverybodySavesSeparately()
    {
        var field = new Battlefield(20, 20);
        var wizard = SpellcastingTests.Wizard();
        var lucky = Goblin("Lucky");
        var unlucky = Goblin("Unlucky");

        field.Place(wizard, 0, 0);
        field.Place(lucky, 10, 10);
        field.Place(unlucky, 11, 10);

        // 5d6 rolled once (all fours = 20), then a save each: 20 saves, 2 does not.
        var cast = Casting.Resolve(
            wizard,
            Spells.Fireball,
            SpellAim.At(new GridSquare(10, 10)),
            new SequenceRandom(4, 4, 4, 4, 4, 20, 2),
            field: field);

        Assert.Equal(2, cast.Targets.Count);
        Assert.Equal(17, cast.DifficultyClass);

        var saved = cast.Targets.Single(t => t.Target == lucky);
        var burned = cast.Targets.Single(t => t.Target == unlucky);

        Assert.True(saved.Save!.Succeeded);
        Assert.Equal(10, saved.Damage);      // half of the same twenty
        Assert.Equal(20, burned.Damage);
    }

    [Fact]
    public void ResistanceStillAppliesOnTopOfAHalvedTotal()
    {
        var field = new Battlefield(20, 20);
        var wizard = SpellcastingTests.Wizard();
        var goblin = Goblin();
        goblin.Defenses.Resist(DamageType.Fire, 5);

        field.Place(wizard, 0, 0);
        field.Place(goblin, 10, 10);

        var cast = Casting.Resolve(
            wizard,
            Spells.Fireball,
            SpellAim.At(new GridSquare(10, 10)),
            new SequenceRandom(4, 4, 4, 4, 4, 20),
            field: field);

        // Twenty halved to ten, then five resisted.
        Assert.Equal(5, Assert.Single(cast.Targets).Damage);
    }

    [Fact]
    public void CureLightWoundsPutsHitPointsBack()
    {
        var wizard = SpellcastingTests.Wizard();
        var friend = new Creature("Fighter", AbilityScores.All(12), 40, 5) { Allegiance = 1 };
        friend.HitPoints.Take(20);

        var cast = Casting.Resolve(
            wizard, Spells.CureLightWounds, SpellAim.At(friend), new SequenceRandom(6));

        Assert.Equal(11, Assert.Single(cast.Targets).Healed);
    }

    [Fact]
    public void BlessHangsAnEffectOnEveryoneItTouches()
    {
        var field = new Battlefield(20, 20);
        var wizard = SpellcastingTests.Wizard();
        var friend = new Creature("Fighter", AbilityScores.All(12), 40, 5) { Allegiance = 1 };
        field.Place(wizard, 5, 5);
        field.Place(friend, 6, 5);

        var before = friend.AttackModifiers.Total;

        Casting.Resolve(wizard, Spells.Bless, SpellAim.At(wizard), new SequenceRandom(1), field: field);

        Assert.True(friend.Effects.Has("Bless"));
        Assert.Equal(before + 1, friend.AttackModifiers.Total);

        // One minute per caster level.
        Assert.Equal(Rules.Effects.Duration.Minutes(5), friend.Effects.Find("Bless")!.Duration);
    }
}

public class CastSpellActionTests
{
    private static Creature Goblin(string name = "Goblin") =>
        new(name, new AbilityScores(11, 15, 12, 10, 9, 6), 9, 2) { Allegiance = 2 };

    private static (Encounter Encounter, Battlefield Field, Creature Wizard, Creature Goblin) Duel(
        int goblinX,
        params int[] extraRolls)
    {
        var field = new Battlefield(30, 10);
        var wizard = SpellcastingTests.Wizard();
        var goblin = Goblin();

        field.Place(wizard, 0, 0);
        field.Place(goblin, goblinX, 0);

        var encounter = new Encounter(
            [wizard, goblin], new SequenceRandom([20, 1, .. extraRolls]), rules: null, battlefield: field);

        return (encounter, field, wizard, goblin);
    }

    [Fact]
    public void CastingSpendsTheSlotAndTheStandardAction()
    {
        var (encounter, _, wizard, goblin) = Duel(5, 2, 3, 4);
        var turn = encounter.BeginNextTurn()!;

        var result = turn.Take(CastSpellAction.At(Spells.MagicMissile, goblin));

        Assert.IsType<CastSpellResult>(result);
        Assert.Equal(2, wizard.Spells.SlotsRemaining(1));
        Assert.False(turn.Budget.HasStandard);
    }

    [Fact]
    public void OutOfRangeIsRefusedAndCostsNothing()
    {
        // Scorching ray is close range: 35 feet at caster level five.
        var (encounter, _, wizard, goblin) = Duel(goblinX: 25);
        var turn = encounter.BeginNextTurn()!;

        Assert.Null(turn.Take(CastSpellAction.At(Spells.ScorchingRay, goblin)));
        Assert.Equal(2, wizard.Spells.SlotsRemaining(2));
        Assert.True(turn.Budget.HasStandard);
    }

    [Fact]
    public void WithNoSlotsLeftNothingHappens()
    {
        var (encounter, _, wizard, goblin) = Duel(5);
        wizard.Spells.Spend(Spells.Fireball);
        wizard.Spells.Spend(Spells.Fireball);

        var turn = encounter.BeginNextTurn()!;

        Assert.Null(turn.Take(CastSpellAction.At(Spells.Fireball, new GridSquare(5, 0))));
        Assert.True(turn.Budget.HasStandard);
    }

    [Fact]
    public void CastingBesideSomebodyGivesThemAFreeSwing()
    {
        var (encounter, _, _, goblin) = Duel(goblinX: 1, extraRolls: [15, 3, 2, 3, 4]);
        goblin.Attacks.Add(WeaponAttack.Create("scimitar", 10, "1d6", DamageType.Slashing));

        var turn = encounter.BeginNextTurn()!;

        var result = Assert.IsType<CastSpellResult>(
            turn.Take(CastSpellAction.At(Spells.MagicMissile, goblin))!);

        Assert.Single(result.Opportunities);
        Assert.Contains("provoking 1", result.Description);
    }

    [Fact]
    public void ABurstNeedsSomewhereToAim()
    {
        // Five damage dice, then the goblin's Reflex save.
        var (encounter, _, _, goblin) = Duel(5, 4, 4, 4, 4, 4, 10);
        var turn = encounter.BeginNextTurn()!;

        // Pointed at a creature rather than a square is still a legitimate aim.
        Assert.NotNull(turn.Take(CastSpellAction.At(Spells.Fireball, goblin)));
    }

    [Fact]
    public void RejectsMissingArguments()
    {
        Assert.Throws<ArgumentNullException>(() => new CastSpellAction(null!, default));
    }
}
