using Ironbound.Rules.Classes;
using Ironbound.Rules.Combat;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Dice;
using Ironbound.Rules.Encounters;
using Ironbound.Rules.Encounters.Actions;
using Ironbound.Rules.Magic;
using Ironbound.Rules.Maps;

namespace Ironbound.Simulation.Tests;

public class ClassAutopilotTests
{
    private static Creature Build(string id, string? name = null) => ContentFiles.Default.BuildCreature(id, name: name)!;

    private static (Battle Battle, HeuristicActionSource Source) Fight(
        IReadOnlyList<(Creature Creature, int X, int Y)> party,
        IReadOnlyList<(Creature Creature, int X, int Y)> foes,
        int size = 30)
    {
        var field = new Battlefield(size, size);
        foreach (var (creature, x, y) in party.Concat(foes))
        {
            field.Place(creature, x, y);
        }

        var battle = new Battle(
            party.Select(one => one.Creature), foes.Select(one => one.Creature), new SequenceRandom(true, 10), battlefield: field);

        return (battle, new HeuristicActionSource(battle, new SequenceRandom(true, 1)));
    }

    /// <summary>Spends every slot — school slots included — that these spells can be cast from.</summary>
    private static void Exhaust(Creature caster, params string[] spells)
    {
        foreach (var id in spells)
        {
            var spell = ContentFiles.Default.GetSpell(id)!;
            while (caster.Spells.Spend(spell))
            {
            }
        }
    }

    /// <summary>Skips ahead to somebody's turn, the others passing theirs.</summary>
    private static Turn TurnOf(Battle battle, Creature creature)
    {
        for (var tries = 0; tries < 20; tries++)
        {
            var turn = battle.Encounter.BeginNextTurn()!;
            if (ReferenceEquals(turn.Actor, creature))
            {
                return turn;
            }
        }

        throw new InvalidOperationException($"{creature.Name} never had a turn.");
    }

    [Fact]
    public void ABarbarianRagesWithAnEnemyInChargeRange()
    {
        var karn = Build("karn");
        var orc = Build("orc");
        var (battle, source) = Fight([(karn, 1, 1)], [(orc, 9, 1)]);

        var action = source.NextAction(TurnOf(battle, karn));

        Assert.IsType<RageAction>(action);
    }

    [Fact]
    public void ButNotWithEveryEnemyFarAway()
    {
        var karn = Build("karn");
        var orc = Build("orc");
        var (battle, source) = Fight([(karn, 0, 0)], [(orc, 25, 25)]);

        var action = source.NextAction(TurnOf(battle, karn));

        Assert.IsNotType<RageAction>(action);
        Assert.False(karn.IsRaging);
    }

    [Fact]
    public void NorWhenShesStillFatigued()
    {
        var karn = Build("karn");
        var orc = Build("orc");
        Rage.Start(karn);
        Rage.End(karn);
        var (battle, source) = Fight([(karn, 1, 1)], [(orc, 2, 1)]);

        Assert.IsNotType<RageAction>(source.NextAction(TurnOf(battle, karn)));
    }

    [Fact]
    public void ARagingBarbarianDeclaresAPowerfulBlowOnASwingLikelyToLand()
    {
        var karn = Build("karn");
        var orc = Build("orc");
        Rage.Start(karn);
        var (battle, source) = Fight([(karn, 1, 1)], [(orc, 2, 1)]);

        var action = source.NextAction(TurnOf(battle, karn));

        Assert.IsType<FullAttackAction>(action);
        Assert.True(karn.Stances.IsActive(Stance.PowerfulBlow));

        // The orc is easy to hit, so the accuracy is kept for a harder swing.
        Assert.False(karn.Stances.IsActive(Stance.SurpriseAccuracy));
    }

    [Fact]
    public void AClericChannelsWhenTwoFriendsNearbyAreHurt()
    {
        var hale = Build("hale");
        var pip = Build("pip");
        var aldric = Build("aldric");
        var orc = Build("orc");
        pip.HitPoints.Take(4);
        aldric.HitPoints.Take(4);
        var (battle, source) = Fight([(hale, 2, 2), (pip, 3, 2), (aldric, 2, 3)], [(orc, 20, 20)]);

        var action = Assert.IsType<UsePowerAction>(source.NextAction(TurnOf(battle, hale)));

        Assert.Equal(ClassPowers.ChannelPool, action.Power.Id);
    }

    [Fact]
    public void ButNotForOneScratch()
    {
        var hale = Build("hale");
        var pip = Build("pip");
        var orc = Build("orc");
        pip.HitPoints.Take(4);
        var (battle, source) = Fight([(hale, 2, 2), (pip, 3, 2)], [(orc, 20, 20)]);

        Assert.IsNotType<UsePowerAction>(source.NextAction(TurnOf(battle, hale)));
    }

    [Fact]
    public void NorWhenItWouldDoTheEnemyMoreGood()
    {
        var hale = Build("hale");
        var pip = Build("pip");
        var aldric = Build("aldric");
        var ogre = Build("ogre");
        pip.HitPoints.Take(1);
        aldric.HitPoints.Take(1);
        ogre.HitPoints.Take(20);
        var (battle, source) = Fight([(hale, 2, 2), (pip, 3, 2), (aldric, 2, 3)], [(ogre, 4, 4)]);

        Assert.False(source.NextAction(TurnOf(battle, hale)) is UsePowerAction { Power.Id: ClassPowers.ChannelPool });
    }

    [Fact]
    public void AClericRebukesDeathForAFriendOnTheFloor()
    {
        var hale = Build("hale");
        var pip = Build("pip");
        var orc = Build("orc");
        pip.HitPoints.Take(pip.HitPoints.Current + 2);
        var (battle, source) = Fight([(hale, 2, 2), (pip, 3, 2)], [(orc, 20, 20)]);

        var action = Assert.IsType<UsePowerAction>(source.NextAction(TurnOf(battle, hale)));

        Assert.Equal("rebuke-death", action.Power.Id);
    }

    [Fact]
    public void ABattleRageGoesToAFriendInTheThickOfIt()
    {
        var hale = Build("hale");
        var aldric = Build("aldric");
        var orc = Build("orc");
        var (battle, source) = Fight([(hale, 2, 2), (aldric, 3, 2)], [(orc, 4, 2)]);

        // Bless first, as it always has; then, with nobody in reach of her own mace, a touch.
        hale.Effects.Apply(new Rules.Effects.ModifierEffect("Bless", Rules.Effects.Duration.Minutes(1)));
        hale.Spells.Spend(ContentFiles.Default.GetSpell("bless")!);
        hale.Spells.Spend(ContentFiles.Default.GetSpell("bless")!);

        var action = source.NextAction(TurnOf(battle, hale));

        var power = Assert.IsType<UsePowerAction>(action);
        Assert.Equal("battle-rage", power.Power.Id);
        Assert.Same(aldric, power.Aim.Creature);
    }

    [Fact]
    public void AWizardOutOfSlotsReachesForHerBondThenHerMissile()
    {
        var merrin = Build("merrin");
        var orc = Build("orc");
        Exhaust(merrin, "magic-missile", "scorching-ray", "fireball");

        var (battle, source) = Fight([(merrin, 1, 1)], [(orc, 8, 1)]);
        var turn = TurnOf(battle, merrin);

        var bonded = Assert.IsType<UsePowerAction>(source.NextAction(turn));
        Assert.Equal(PowerUse.Spell, bonded.Power.Use);

        merrin.DailyUses.Spend(ClassPowers.ArcaneBondPool);
        var missile = Assert.IsType<UsePowerAction>(source.NextAction(turn));
        Assert.Equal("force-missile", missile.Power.Id);
    }

    [Fact]
    public void AConjurerOutOfSlotsThrowsAcid()
    {
        var sylwen = Build("sylwen");
        var orc = Build("orc");
        sylwen.Spells.Spend(ContentFiles.Default.GetSpell("magic-missile")!);
        sylwen.Spells.Spend(ContentFiles.Default.GetSpell("magic-missile")!);
        sylwen.Spells.Spend(ContentFiles.Default.GetSpell("mage-armor")!);
        sylwen.DailyUses.Spend(ClassPowers.ArcaneBondPool);
        var (battle, source) = Fight([(sylwen, 1, 1)], [(orc, 5, 1)]);

        var action = Assert.IsType<UsePowerAction>(source.NextAction(TurnOf(battle, sylwen)));

        Assert.Equal("acid-dart", action.Power.Id);
    }

    [Fact]
    public void WithSlotsLeftAWizardStillCastsHerSpells()
    {
        var merrin = Build("merrin");
        var orc = Build("orc");
        var (battle, source) = Fight([(merrin, 1, 1)], [(orc, 8, 1)]);

        Assert.IsType<CastSpellAction>(source.NextAction(TurnOf(battle, merrin)));
    }

    [Fact]
    public void OutOfFirstLevelSlotsAMissileGoesOutEmpowered()
    {
        var merrin = Build("merrin");
        var orc = Build("orc");
        Exhaust(merrin, "magic-missile", "scorching-ray");

        var (battle, source) = Fight([(merrin, 1, 1)], [(orc, 8, 1)]);

        var cast = Assert.IsType<CastSpellAction>(source.NextAction(TurnOf(battle, merrin)));
        Assert.True(cast.Spell.Empowered);
        Assert.Equal("magic-missile", cast.Spell.Id);
    }

    [Fact]
    public void ARogueGoesForTheFoeHerSneakAttackWorksOn()
    {
        var pip = Build("pip");
        var aldric = Build("aldric");
        var weak = Build("orc", "Weak Orc");
        var flanked = Build("orc", "Flanked Orc");
        weak.HitPoints.Take(4);

        // Pip between two orcs; Aldric opposite her across the healthier one.
        var (battle, source) = Fight([(pip, 5, 5), (aldric, 7, 5)], [(weak, 5, 6), (flanked, 6, 5)]);

        // Pip's second turn, by when both orcs have acted and neither is flat-footed: only the
        // flank tells them apart.
        TurnOf(battle, pip);
        var action = source.NextAction(TurnOf(battle, pip));

        var target = action switch
        {
            FullAttackAction full => full.Target,
            AttackAction single => single.Target,
            _ => null,
        };

        Assert.Same(flanked, target);
    }
}
