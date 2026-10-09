using Ironbound.Rules.Classes;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Dice;
using Ironbound.Rules.Encounters;
using Ironbound.Rules.Encounters.Actions;
using Ironbound.Rules.Magic;
using Ironbound.Rules.Maps;

namespace Ironbound.Rules.Tests.Classes;

public class ArcaneSchoolTests
{
    private static Creature Merrin() => TestContent.Library.BuildCreature("merrin")!;

    private static Creature Sylwen() => TestContent.Library.BuildCreature("sylwen")!;

    [Fact]
    public void MerrinIsAnEvokerWithASchoolSlotAtEveryLevel()
    {
        var merrin = Merrin();

        Assert.Equal("evocation", merrin.Choices.School!.Id);
        Assert.Equal([SpellSchool.Enchantment, SpellSchool.Illusion], merrin.Choices.Opposition);
        Assert.Equal([1, 2, 3], merrin.Spells.SpecialtyLevels.Order());
        Assert.All([1, 2, 3], level => Assert.Equal(1, merrin.Spells.SpecialtyMaximum(level)));
    }

    [Fact]
    public void TheSchoolSlotHoldsOnlyHerSchool()
    {
        var merrin = Merrin();

        Assert.True(merrin.Spells.IsSpecialty(TestContent.Library.GetSpell("magic-missile")!));
        Assert.False(merrin.Spells.IsSpecialty(TestContent.Library.GetSpell("grease")!));

        merrin.Spells.Spend(TestContent.Library.GetSpell("grease")!);
        Assert.Equal(1, merrin.Spells.SpecialtyRemaining(1));
        Assert.Equal(3, merrin.Spells.SlotsRemaining(1));
    }

    [Fact]
    public void AnOppositionSpellCostsTwoSlots()
    {
        var enchanter = ClassKit.Make(
            "wizard", 1,
            "\"school\": \"evocation\", \"opposition\": [\"Enchantment\", \"Necromancy\"], \"spells\": [\"cause-fear\"]",
            [10, 10, 10, 18, 10, 10]);
        var fear = TestContent.Library.GetSpell("cause-fear")!;

        // One from the table and one from Intelligence 18: exactly enough for one cause fear.
        Assert.Equal(2, enchanter.Spells.Cost(fear));
        Assert.True(enchanter.Spells.CanCast(fear));
        Assert.True(enchanter.Spells.Spend(fear));
        Assert.Equal(0, enchanter.Spells.SlotsRemaining(1));
        Assert.False(enchanter.Spells.CanCast(fear));
    }

    [Fact]
    public void WithOneSlotLeftAnOppositionSpellCannotBeCastAtAll()
    {
        var wizard = ClassKit.Make(
            "wizard", 1,
            "\"school\": \"evocation\", \"opposition\": [\"Enchantment\", \"Necromancy\"], \"spells\": [\"cause-fear\", \"grease\"]",
            [10, 10, 10, 18, 10, 10]);

        wizard.Spells.Spend(TestContent.Library.GetSpell("grease")!);

        Assert.False(wizard.Spells.CanCast(TestContent.Library.GetSpell("cause-fear")!));
        Assert.True(wizard.Spells.CanCast(TestContent.Library.GetSpell("grease")!));
    }

    [Fact]
    public void AUniversalistHasNoSchoolSlotAndGivesNothingUp()
    {
        var generalist = ClassKit.Make("wizard", 3, "\"school\": \"universalist\"");

        Assert.Empty(generalist.Spells.SpecialtyLevels);
        Assert.Equal(1, generalist.Spells.Cost(TestContent.Library.GetSpell("cause-fear")!));
        Assert.DoesNotContain(generalist.Powers, power => power.Use == PowerUse.Effect);
    }

    [Fact]
    public void ASpecialistMustGiveUpTwoSchoolsNotHerOwn()
    {
        var one = ClassKit.Library(("one.json", """
            { "kind": "creature", "id": "one", "name": "One", "abilities": [10, 10, 10, 16, 10, 10],
              "classes": [ { "class": "wizard", "level": 1 } ],
              "school": "evocation", "opposition": ["Illusion"] }
            """));
        var own = ClassKit.Library(("own.json", """
            { "kind": "creature", "id": "own", "name": "Own", "abilities": [10, 10, 10, 16, 10, 10],
              "classes": [ { "class": "wizard", "level": 1 } ],
              "school": "evocation", "opposition": ["Evocation", "Illusion"] }
            """));
        var universal = ClassKit.Library(("uni.json", """
            { "kind": "creature", "id": "uni", "name": "Uni", "abilities": [10, 10, 10, 16, 10, 10],
              "classes": [ { "class": "wizard", "level": 1 } ],
              "school": "universalist", "opposition": ["Evocation", "Illusion"] }
            """));

        Assert.Contains(one.Problems, problem => problem.Message.Contains("two schools"));
        Assert.Contains(own.Problems, problem => problem.Message.Contains("its own opposition"));
        Assert.Contains(universal.Problems, problem => problem.Message.Contains("universalist"));
    }

    [Fact]
    public void IntenseSpellsAddsHalfHerLevelOnceASpell()
    {
        var merrin = Merrin();
        var target = ClassKit.Dummy("Target", hitPoints: 100);
        merrin.Allegiance = 1;
        target.Allegiance = 2;

        // Three missiles of 1d4+1, every die a 1: six, and two for intense spells — once.
        var cast = Casting.Resolve(merrin, TestContent.Library.GetSpell("magic-missile")!, SpellAim.At(target), new SequenceRandom(true, 1));

        Assert.Equal(8, cast.Targets.Single().Damage);
        Assert.Equal(2, ClassPowers.IntenseBonus(merrin));
    }

    [Fact]
    public void ABurstGivesEveryoneInItTheBonus()
    {
        var merrin = Merrin();
        var first = ClassKit.Dummy("First", hitPoints: 100);
        var second = ClassKit.Dummy("Second", hitPoints: 100);
        var field = ClassKit.Field((merrin, 0, 0), (first, 8, 8), (second, 9, 8));
        merrin.Allegiance = 1;
        first.Allegiance = second.Allegiance = 2;

        // Five dice of 1, then two failed saves.
        var cast = Casting.Resolve(
            merrin, TestContent.Library.GetSpell("fireball")!, SpellAim.At(new GridSquare(8, 8)),
            new SequenceRandom(1, 1, 1, 1, 1, 2, 2), field: field);

        Assert.All(cast.Targets, hit => Assert.Equal(7, hit.Damage));
    }

    [Fact]
    public void AnotherSchoolsWizardGetsNoBonus()
    {
        var sylwen = Sylwen();

        Assert.Equal(0, ClassPowers.IntenseBonus(sylwen));
    }

    [Fact]
    public void ForceMissileIsAD4AndTheIntenseBonusSevenTimesADay()
    {
        var merrin = Merrin();
        var missile = merrin.Powers.Single(power => power.Id == "force-missile");

        Assert.Equal(7, merrin.UsesPerDay(missile));    // three, and Intelligence 18's four
        Assert.Equal("1d4+2", Assert.IsType<DealDamage>(missile.Effect.Does.Single()).Amount.At(5).ToString());
        Assert.True(missile.Provokes);
    }

    [Fact]
    public void ForceMissileArrivesWithoutAnAttackRoll()
    {
        var merrin = Merrin();
        var target = ClassKit.Dummy("Target", armour: 30);
        var field = ClassKit.Field((merrin, 0, 0), (target, 6, 0));
        var encounter = ClassKit.Fight([merrin], [target], field, 3);
        var missile = merrin.Powers.Single(power => power.Id == "force-missile");

        var result = Assert.IsType<CastSpellResult>(encounter.BeginNextTurn()!.Take(UsePowerAction.At(missile, target)));

        Assert.Null(result.Cast!.Targets.Single().Attack);
        Assert.Equal(5, target.HitPoints.Damage);       // 3, and the intense two — not counted twice
        Assert.Equal(6, merrin.UsesLeft(missile));
    }

    [Fact]
    public void AcidDartIsARangedTouchWithinThirtyFeet()
    {
        var sylwen = Sylwen();
        var dart = sylwen.Powers.Single(power => power.Id == "acid-dart");
        var near = ClassKit.Dummy("Near");
        var far = ClassKit.Dummy("Far");
        var field = ClassKit.Field((sylwen, 0, 0), (near, 6, 0), (far, 7, 0));
        var encounter = ClassKit.Fight([sylwen], [near, far], field, 15, 4);
        var turn = encounter.BeginNextTurn()!;

        Assert.Equal(6, sylwen.UsesPerDay(dart));    // three, and Intelligence 17's three
        Assert.IsType<RayTarget>(dart.Effect.Target);
        Assert.False(turn.CanTake(UsePowerAction.At(dart, far)));

        var result = Assert.IsType<CastSpellResult>(turn.Take(UsePowerAction.At(dart, near)));
        var hit = result.Cast!.Targets.Single();

        Assert.True(hit.Attack!.IsHit);
        Assert.Equal(4, hit.Damage);                 // 1d6, and nothing more at first level
    }

    [Fact]
    public void SylwensSchoolSlotIsForGreaseOrMageArmour()
    {
        var sylwen = Sylwen();

        Assert.Equal(2, sylwen.Spells.SlotsMaximum(1));
        Assert.Equal(1, sylwen.Spells.SpecialtyMaximum(1));
        Assert.True(sylwen.Spells.IsSpecialty(TestContent.Library.GetSpell("grease")!));
        Assert.True(sylwen.Spells.IsSpecialty(TestContent.Library.GetSpell("mage-armor")!));
        Assert.False(sylwen.Spells.IsSpecialty(TestContent.Library.GetSpell("magic-missile")!));
    }

    [Fact]
    public void DimensionalStepsTeleportsAndPaysInFeet()
    {
        var conjurer = ClassKit.Make("wizard", 8, "\"school\": \"conjuration\", \"opposition\": [\"Enchantment\", \"Necromancy\"]");
        var steps = conjurer.Powers.Single(power => power.Id == "dimensional-steps");
        var field = ClassKit.Field((conjurer, 0, 0));
        var encounter = ClassKit.Fight([conjurer], [], field);
        var turn = encounter.BeginNextTurn()!;

        Assert.Equal(240, conjurer.UsesPerDay(steps));
        Assert.Equal(ActionCost.Move, steps.Cost);
        Assert.True(steps.Effect.NeedsAPoint);

        Assert.NotNull(turn.Take(UsePowerAction.At(steps, new GridSquare(4, 0))));

        Assert.Equal(new GridSquare(4, 0), field.SquareOf(conjurer));
        Assert.Equal(220, conjurer.UsesLeft(steps));
    }

    [Fact]
    public void DimensionalStepsWillNotLandOnSomebody()
    {
        var conjurer = ClassKit.Make("wizard", 8, "\"school\": \"conjuration\", \"opposition\": [\"Enchantment\", \"Necromancy\"]");
        var blocker = ClassKit.Dummy("Blocker");
        var field = ClassKit.Field((conjurer, 0, 0), (blocker, 2, 0));
        var encounter = ClassKit.Fight([conjurer], [blocker], field);
        var steps = conjurer.Powers.Single(power => power.Id == "dimensional-steps");

        Assert.False(encounter.BeginNextTurn()!.CanTake(UsePowerAction.At(steps, new GridSquare(2, 0))));
    }

    [Fact]
    public void BelowEighthLevelThereAreNoSteps()
    {
        var conjurer = ClassKit.Make("wizard", 7, "\"school\": \"conjuration\", \"opposition\": [\"Enchantment\", \"Necromancy\"]");

        Assert.DoesNotContain(conjurer.Powers, power => power.Id == "dimensional-steps");
    }
}

public class ArcaneBondTests
{
    private static Creature Merrin() => TestContent.Library.BuildCreature("merrin")!;

    [Fact]
    public void TheSpellbookDefaultsToWhatSheHasPrepared()
    {
        var merrin = Merrin();

        Assert.Equal(merrin.Spells.Prepared.Select(spell => spell.Id), merrin.Spells.Spellbook.Select(spell => spell.Id));
    }

    [Fact]
    public void AWrittenSpellbookCanHoldMoreThanSheCarries()
    {
        var bookish = ClassKit.Make("wizard", 1, "\"spells\": [\"magic-missile\"], \"spellbook\": [\"magic-missile\", \"grease\"]");

        Assert.Equal(["magic-missile", "grease"], bookish.Spells.Spellbook.Select(spell => spell.Id));
        Assert.Contains(bookish.Powers, power => power.Id == "arcane-bond:grease");
    }

    [Fact]
    public void EverySpellSheCanCastIsABondedPowerSharingOneUse()
    {
        var merrin = Merrin();
        var bonded = merrin.Powers.Where(power => power.Use == PowerUse.Spell).ToList();

        Assert.Equal(5, bonded.Count);
        Assert.All(bonded, power => Assert.Equal(ClassPowers.ArcaneBondPool, power.Pool));
        Assert.All(bonded, power => Assert.Equal(1, merrin.UsesPerDay(power)));
    }

    [Fact]
    public void ABondedSpellCostsNoSlotAndOnlyOneADay()
    {
        var merrin = Merrin();
        var target = ClassKit.Dummy("Target", hitPoints: 100);
        var field = ClassKit.Field((merrin, 0, 0), (target, 5, 0));
        var encounter = ClassKit.Fight([merrin], [target], field, 1, 1, 1);
        var missile = merrin.Powers.Single(power => power.Id == "arcane-bond:magic-missile");
        var slots = merrin.Spells.SlotsRemaining(1);
        var turn = encounter.BeginNextTurn()!;

        var result = Assert.IsType<CastSpellResult>(turn.Take(UsePowerAction.At(missile, target)));

        Assert.Contains("casts Magic Missile", result.Description);
        Assert.Equal(slots, merrin.Spells.SlotsRemaining(1));
        Assert.Equal(0, merrin.UsesLeft(missile));
        Assert.All(merrin.Powers.Where(power => power.Use == PowerUse.Spell), power => Assert.Equal(0, merrin.UsesLeft(power)));
    }

    [Fact]
    public void SpellsOfALevelSheCannotCastAreNotOffered()
    {
        var novice = ClassKit.Make("wizard", 1, "\"spells\": [\"magic-missile\"], \"spellbook\": [\"magic-missile\", \"fireball\"]");

        Assert.DoesNotContain(novice.Powers, power => power.Id == "arcane-bond:fireball");
    }

    [Fact]
    public void BothWizardsHaveTheirBond()
    {
        Assert.True(Merrin().Choices.BondedObject);
        Assert.True(TestContent.Library.BuildCreature("sylwen")!.Choices.BondedObject);
        Assert.Contains(ClassFeatures.Describe(Merrin()), line => line.Name == "Arcane bond" && line.Detail.Contains("1 of 1"));
    }
}

public class EmpowerSpellTests
{
    [Fact]
    public void MerrinTookEmpowerSpellAtFifth()
    {
        var merrin = TestContent.Library.BuildCreature("merrin")!;

        Assert.True(merrin.HasFeat(Ironbound.Rules.Feats.FeatEffect.EmpowerSpell));
        Assert.Contains(ClassFeatures.Describe(merrin), line => line.Name == "Wizard bonus feats");
    }

    [Fact]
    public void AnEmpoweredSpellTakesASlotTwoLevelsUp()
    {
        var merrin = TestContent.Library.BuildCreature("merrin")!;
        var missile = TestContent.Library.GetSpell("magic-missile")!.Empower();

        Assert.Equal(3, missile.SlotLevel);
        Assert.True(merrin.Spells.CanCast(missile));
        merrin.Spells.Spend(missile);

        // An evocation, so the school slot at third goes first.
        Assert.Equal(0, merrin.Spells.SpecialtyRemaining(3));
        Assert.Equal(1, merrin.Spells.SpecialtyRemaining(1));
    }

    [Fact]
    public void ItRollsHalfAsMuchAgainButTheIntenseBonusIsNotEmpowered()
    {
        var merrin = TestContent.Library.BuildCreature("merrin")!;
        var target = ClassKit.Dummy("Target", hitPoints: 100);
        merrin.Allegiance = 1;
        target.Allegiance = 2;

        var cast = Casting.Resolve(
            merrin, TestContent.Library.GetSpell("magic-missile")!.Empower(), SpellAim.At(target), new SequenceRandom(true, 2));

        // Three missiles of 2+1 is nine; half again is thirteen; and intense spells' two.
        Assert.Equal(15, cast.Targets.Single().Damage);
        Assert.Contains("empowered Magic Missile", cast.ToString());
    }

    [Fact]
    public void NobodyWithoutTheFeatCanEmpower()
    {
        var sylwen = TestContent.Library.BuildCreature("sylwen")!;

        Assert.False(sylwen.Spells.CanCast(TestContent.Library.GetSpell("magic-missile")!.Empower()));
    }

    [Fact]
    public void TheDifficultyStaysTheSpells()
    {
        var merrin = TestContent.Library.BuildCreature("merrin")!;
        var fireball = TestContent.Library.GetSpell("fireball")!;

        Assert.Equal(merrin.Spells.SaveDC(fireball), merrin.Spells.SaveDC(fireball.Empower()));
    }

    [Fact]
    public void ACastSpellActionTakesAnEmpoweredSpell()
    {
        var merrin = TestContent.Library.BuildCreature("merrin")!;
        var target = ClassKit.Dummy("Target", hitPoints: 100);
        var field = ClassKit.Field((merrin, 0, 0), (target, 5, 0));
        var encounter = ClassKit.Fight([merrin], [target], field, 1, 1, 1);

        var result = Assert.IsType<CastSpellResult>(encounter.BeginNextTurn()!.Take(
            CastSpellAction.At(TestContent.Library.GetSpell("magic-missile")!.Empower(), target)));

        Assert.Contains("empowered", result.Description);
        Assert.Equal(0, merrin.Spells.SpecialtyRemaining(3));
    }
}
