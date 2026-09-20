using Ironbound.Rules.Abilities;
using Ironbound.Rules.Combat;
using Ironbound.Rules.Conditions;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Dice;
using Ironbound.Rules.Effects;
using Ironbound.Rules.Encounters;
using Ironbound.Rules.Encounters.Actions;
using Ironbound.Rules.Magic;
using Ironbound.Rules.Maps;
using Ironbound.Rules.Persistence;
using Ironbound.Rules.Saves;

namespace Ironbound.Rules.Tests.Conditions;

public class ConditionRulesTests
{
    [Fact]
    public void BeingShakenCostsTwoOnAttacksAndEverySave()
    {
        var creature = Fighter();
        var before = creature.Saves[Save.Will].Total;

        creature.Effects.Apply(ConditionInfo.Effect(Condition.Shaken, Duration.Rounds(3)));

        Assert.True(creature.Has(Condition.Shaken));
        Assert.Equal(before - 2, creature.Saves[Save.Will].Total);
        Assert.Equal(-2, creature.AttackModifiers.Total);
    }

    [Fact]
    public void PenaltiesFromTwoConditionsBothApply()
    {
        var creature = Fighter();

        creature.Effects.Apply(ConditionInfo.Effect(Condition.Shaken, Duration.Rounds(3)));
        creature.Effects.Apply(ConditionInfo.Effect(Condition.Sickened, Duration.Rounds(3)));

        // Penalties always stack; "highest wins" is a rule about bonuses.
        Assert.Equal(-4, creature.AttackModifiers.Total);
        Assert.Equal(-2, creature.DamageModifiers.Total);
    }

    [Fact]
    public void ItEndsWhenTheClockRunsOut()
    {
        var creature = Fighter();
        creature.Effects.Apply(ConditionInfo.Effect(Condition.Shaken, Duration.Rounds(1)));

        creature.Effects.Advance(Duration.Rounds(1), new SequenceRandom(true, 1));

        Assert.False(creature.Has(Condition.Shaken));
        Assert.Equal(0, creature.AttackModifiers.Total);
    }

    [Fact]
    public void EntanglementSlowsYouAndMakesYouClumsy()
    {
        var creature = Fighter();

        creature.Effects.Apply(ConditionInfo.Effect(Condition.Entangled, Duration.Rounds(3)));

        Assert.Equal(15, creature.CurrentSpeed);                        // half of thirty
        Assert.Equal(10, creature.Abilities[Ability.Dexterity].Score);  // fourteen, less four
    }

    [Fact]
    public void ADazedCreatureCanDoNothingAtAll()
    {
        var creature = Fighter();
        creature.Effects.Apply(ConditionInfo.Effect(Condition.Dazed, Duration.Rounds(1)));

        Assert.False(creature.CanAct);

        // Dazed leaves you upright and defending, unlike stunned.
        Assert.False(creature.DeniesDexterity);
    }

    [Fact]
    public void BeingStunnedAlsoLosesYouYourDexterity()
    {
        var creature = Fighter();
        creature.Effects.Apply(ConditionInfo.Effect(Condition.Stunned, Duration.Rounds(1)));

        Assert.False(creature.CanAct);
        Assert.True(creature.DeniesDexterity);
    }

    [Fact]
    public void ConditionsAreListedOnceEachHoweverManyEffectsImposeThem()
    {
        var creature = Fighter();
        creature.Effects.Apply(ConditionInfo.Effect(Condition.Shaken, Duration.Rounds(3)));
        creature.Effects.Apply(
            new ModifierEffect("Terror", Duration.Rounds(2)) { Condition = Condition.Shaken });

        Assert.Equal([Condition.Shaken], creature.Conditions);
    }

    [Fact]
    public void EveryConditionHasAnEntry()
    {
        foreach (var condition in ConditionInfo.All)
        {
            Assert.Equal(condition, ConditionInfo.Of(condition).Condition);
        }
    }

    internal static Creature Fighter(string name = "Fighter", int allegiance = 1) =>
        new(name, new AbilityScores(16, 14, 14, 10, 10, 10), 40, 6) { Allegiance = allegiance };
}

public class ProneTests
{
    [Fact]
    public void BeingOnTheFloorCostsFourOnMeleeAttacks()
    {
        var creature = ConditionRulesTests.Fighter();
        creature.Effects.Apply(ConditionInfo.Effect(Condition.Prone, Duration.Permanent));

        Assert.True(creature.IsProne);
        Assert.Equal(-4, creature.AttackModifiers.Total);
    }

    [Fact]
    public void ItIsEasierToStabAndHarderToShoot()
    {
        var target = ConditionRulesTests.Fighter("Target", allegiance: 2);
        var sword = WeaponAttack.Melee("longsword", "1d8", DamageType.Slashing);
        var bow = WeaponAttack.Ranged("shortbow", "1d6", DamageType.Piercing, 60);

        Assert.Equal(0, Strike.ProneFor(sword, target));

        target.Effects.Apply(ConditionInfo.Effect(Condition.Prone, Duration.Permanent));

        // The same posture cuts both ways, which is what makes knocking somebody down a choice.
        Assert.Equal(-4, Strike.ProneFor(sword, target));
        Assert.Equal(4, Strike.ProneFor(bow, target));
    }

    [Fact]
    public void ThePostureShowsUpInTheArmourClassTheAttackIsMeasuredAgainst()
    {
        var attacker = ConditionRulesTests.Fighter("Attacker");
        var target = ConditionRulesTests.Fighter("Target", allegiance: 2);
        var sword = WeaponAttack.Melee("longsword", "1d8", DamageType.Slashing);

        var bare = target.ArmorClass.Total;
        target.Effects.Apply(ConditionInfo.Effect(Condition.Prone, Duration.Permanent));

        var swing = Strike.Resolve(attacker, sword, target, new SequenceRandom(true, 11, 3));

        Assert.Equal(bare - 4, swing.Attack.TargetArmorClass);
        Assert.Contains("-4 prone", swing.Attack.ToString());
    }

    [Fact]
    public void StandingUpCostsAMoveActionAndProvokes()
    {
        var (turn, actor, _) = Skirmish();
        actor.Effects.Apply(ConditionInfo.Effect(Condition.Prone, Duration.Permanent));

        var result = Assert.IsType<StandUpResult>(turn.Take(new StandUpAction()));

        Assert.True(result.Stood);
        Assert.False(actor.IsProne);
        Assert.Single(result.Opportunities);
        Assert.False(turn.Budget.HasMove);
        Assert.True(turn.Budget.HasStandard);
    }

    [Fact]
    public void HavingStoodUpYouCannotThenFullAttack()
    {
        var (turn, actor, enemy) = Skirmish();
        actor.Effects.Apply(ConditionInfo.Effect(Condition.Prone, Duration.Permanent));

        turn.Take(new StandUpAction());

        // The real cost of being knocked down is the round, not the four points.
        Assert.False(turn.CanTake(new FullAttackAction(enemy)));
        Assert.True(turn.CanTake(new AttackAction(actor.PrimaryAttack!, enemy)));
    }

    [Fact]
    public void SomebodyAlreadyStandingCannotStandUp()
    {
        var (turn, _, _) = Skirmish();

        Assert.False(turn.CanTake(new StandUpAction()));
    }

    private static (Turn Turn, Creature Actor, Creature Enemy) Skirmish()
    {
        var field = new Battlefield(8, 4);
        var actor = ConditionRulesTests.Fighter("Actor");
        var enemy = ConditionRulesTests.Fighter("Enemy", allegiance: 2);

        actor.Attacks.Add(WeaponAttack.Melee("longsword", "1d8", DamageType.Slashing));
        enemy.Attacks.Add(WeaponAttack.Melee("scimitar", "1d6", DamageType.Slashing));

        field.Place(actor, 0, 0);
        field.Place(enemy, 1, 0);

        var encounter = new Encounter(
            [actor, enemy], new SequenceRandom(true, 20, 1, 12, 4), rules: null, battlefield: field);

        return (encounter.BeginNextTurn()!, actor, enemy);
    }
}

public class UnableToActTests
{
    [Fact]
    public void ADazedCreatureIsRefusedEveryActionAndChargedForNone()
    {
        var field = new Battlefield(8, 4);
        var actor = ConditionRulesTests.Fighter("Actor");
        var enemy = ConditionRulesTests.Fighter("Enemy", allegiance: 2);
        actor.Attacks.Add(WeaponAttack.Melee("longsword", "1d8", DamageType.Slashing));

        field.Place(actor, 0, 0);
        field.Place(enemy, 1, 0);

        var encounter = new Encounter(
            [actor, enemy], new SequenceRandom(true, 20, 1, 12, 4), rules: null, battlefield: field);
        var turn = encounter.BeginNextTurn()!;

        actor.Effects.Apply(ConditionInfo.Effect(Condition.Dazed, Duration.Rounds(1)));

        Assert.False(turn.CanTake(new AttackAction(actor.PrimaryAttack!, enemy)));
        Assert.Null(turn.Take(new AttackAction(actor.PrimaryAttack!, enemy)));
        Assert.True(turn.Budget.HasStandard);
    }
}

public class FearFromAFileTests
{
    [Fact]
    public void CauseFearNamesTheConditionRatherThanTranscribingIt()
    {
        var spell = TestContent.Library.GetSpell("cause-fear")!;
        var bestow = Assert.IsType<Bestow>(spell.Does.Single());

        Assert.Equal(Save.Will, spell.Save);
        Assert.Equal(Condition.Shaken, bestow.Effect.Condition);

        // The four separate -2s come from the condition, so a rules correction lands in one
        // place rather than in every spell that ever inflicted fear.
        Assert.Empty(bestow.Effect.Grants);
    }

    [Fact]
    public void CastingItLeavesTheTargetShaken()
    {
        var caster = ConditionRulesTests.Fighter("Merrin");
        var victim = ConditionRulesTests.Fighter("Goblin", allegiance: 2);
        caster.Spells.CasterLevel = 5;

        // A natural 1 on the Will save, so it lands.
        Casting.Resolve(
            caster,
            TestContent.Library.GetSpell("cause-fear")!,
            SpellAim.At(victim),
            new SequenceRandom(true, 1));

        Assert.True(victim.Has(Condition.Shaken));
        Assert.Equal(Duration.Rounds(5), victim.Effects.Find("Shaken")!.Duration);
        Assert.Equal(-2, victim.AttackModifiers.Total);
    }

    [Fact]
    public void AMadeSaveLeavesThemAlone()
    {
        var caster = ConditionRulesTests.Fighter("Merrin");
        var victim = ConditionRulesTests.Fighter("Goblin", allegiance: 2);
        caster.Spells.CasterLevel = 5;

        Casting.Resolve(
            caster,
            TestContent.Library.GetSpell("cause-fear")!,
            SpellAim.At(victim),
            new SequenceRandom(true, 20));

        Assert.False(victim.Has(Condition.Shaken));
    }

    [Fact]
    public void AConditionSurvivesBeingSavedAndReloaded()
    {
        var creature = ConditionRulesTests.Fighter();
        creature.Effects.Apply(ConditionInfo.Effect(Condition.Stunned, Duration.Rounds(2)));

        var encounter = new Encounter([creature], new SequenceRandom(true, 10));
        var json = GameSave.ToJson(GameSave.Capture(encounter));
        var restored = GameSave
            .Restore(GameSave.FromJson(json), TestContent.Library)
            .Order.Single().Creature;

        // Without the tag the creature reloads merely carrying a -2 and takes its turn as
        // though nothing were wrong.
        Assert.True(restored.Has(Condition.Stunned));
        Assert.False(restored.CanAct);
    }
}
