using Ironbound.Rules.Abilities;
using Ironbound.Rules.Combat;
using Ironbound.Rules.Conditions;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Dice;
using Ironbound.Rules.Effects;
using Ironbound.Rules.Encounters;
using Ironbound.Rules.Encounters.Actions;
using Ironbound.Rules.Magic;
using Ironbound.Rules.Skills;
using Ironbound.Rules.Tests.Classes;

namespace Ironbound.Rules.Tests.Conditions;

/// <summary>Staggered, dazzled, deafened and exhausted: the conditions the critical feats inflict.</summary>
public class BatchConditionTests
{
    [Fact]
    public void StaggeredIsOneMoveOrStandardActionAndNothingTakingTheWholeRound()
    {
        var fighter = ClassKit.Make("fighter", 6, "\"items\": [\"longsword\"]");
        var foe = ClassKit.Dummy("Foe");
        var field = ClassKit.Field((fighter, 2, 2), (foe, 3, 2));
        fighter.Effects.Apply(ConditionInfo.Effect(Condition.Staggered, Duration.Rounds(3)));
        var encounter = ClassKit.Fight([fighter], [foe], field, 15, 4);

        var turn = encounter.BeginNextTurn()!;

        Assert.True(turn.Budget.IsSingleAction);
        Assert.False(turn.CanTake(new FullAttackAction(foe)));
        Assert.NotNull(turn.Take(new AttackAction(fighter.MeleeAttack!, foe)));
        Assert.False(turn.Budget.CanAfford(ActionCost.Move));

        // Not the disabled rule: a staggered swing costs no hit points.
        Assert.Equal(fighter.HitPoints.Maximum, fighter.HitPoints.Current);
    }

    [Fact]
    public void DazzledIsOneOffAttacksAndOffLooking()
    {
        var rogue = ClassKit.Make("rogue", 1, "\"items\": [\"short-sword\"]");
        var attack = Strike.AttackBonus(rogue, rogue.MeleeAttack!).Total;
        var perception = rogue.Skills.Total(Skill.Perception);

        rogue.Effects.Apply(ConditionInfo.Effect(Condition.Dazzled, Duration.Rounds(2)));

        Assert.Equal(attack - 1, Strike.AttackBonus(rogue, rogue.MeleeAttack!).Total);
        Assert.Equal(perception - 1, rogue.Skills.Total(Skill.Perception));
    }

    [Fact]
    public void DeafenedIsFourOffInitiativeAndPerception()
    {
        var rogue = ClassKit.Make("rogue", 1);
        var initiative = Initiative.Bonus(rogue);
        var perception = rogue.Skills.Total(Skill.Perception);

        rogue.Effects.Apply(ConditionInfo.Effect(Condition.Deafened, Duration.Permanent));

        Assert.Equal(initiative - 4, Initiative.Bonus(rogue));
        Assert.Equal(perception - 4, rogue.Skills.Total(Skill.Perception));
    }

    [Theory]
    [InlineData(20, true)]
    [InlineData(21, false)]
    public void ADeafenedCasterFumblesOneSpokenSpellInFive(int percentile, bool lost)
    {
        var wizard = ClassKit.Make("wizard", 1, "\"spells\": [\"magic-missile\"]", [10, 14, 10, 16, 10, 10]);
        var foe = ClassKit.Dummy("Foe");
        var field = ClassKit.Field((wizard, 2, 2), (foe, 6, 2));
        wizard.Effects.Apply(ConditionInfo.Effect(Condition.Deafened, Duration.Permanent));
        var encounter = ClassKit.Fight([wizard], [foe], field, percentile, 3);
        var slots = wizard.Spells.SlotsRemaining(1);

        var result = Assert.IsType<CastSpellResult>(
            encounter.BeginNextTurn()!.Take(CastSpellAction.At(Spells.MagicMissile, foe)));

        Assert.Equal(lost, result.Lost);
        Assert.Equal(slots - 1, wizard.Spells.SlotsRemaining(1));
        Assert.Equal(lost, foe.HitPoints.Damage == 0);
    }

    [Fact]
    public void ExhaustedIsSixOffStrengthAndDexterityAndHalfSpeed()
    {
        var fighter = ClassKit.Make("fighter", 1, abilities: [16, 14, 14, 10, 10, 10]);
        var speed = fighter.CurrentSpeed;

        fighter.Effects.Apply(ConditionInfo.Effect(Condition.Exhausted, Duration.Permanent));

        Assert.Equal(10, fighter.Abilities[Ability.Strength].Score);
        Assert.Equal(8, fighter.Abilities[Ability.Dexterity].Score);
        Assert.Equal(speed / 2, fighter.CurrentSpeed);
    }

    [Fact]
    public void TheNewConditionsComeAfterTheOldOnes()
    {
        // Saves name conditions as words; nothing should ever find an old one moved.
        Assert.Equal(
            [Condition.Fatigued, Condition.Staggered, Condition.Dazzled, Condition.Deafened, Condition.Exhausted, Condition.Anchored],
            ConditionInfo.All.SkipWhile(condition => condition != Condition.Fatigued));
    }
}
