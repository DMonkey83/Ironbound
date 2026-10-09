using Ironbound.Rules.Combat;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Dice;
using Ironbound.Rules.Encounters;
using Ironbound.Rules.Encounters.Actions;
using Ironbound.Rules.Items;
using Ironbound.Rules.Maps;

namespace Ironbound.Simulation.Tests;

/// <summary>What the autopilot does with a polearm, a dagger it might throw, and a javelin.</summary>
public class WeaponAutopilotTests
{
    private static Creature Build(string id, string? name = null) => ContentFiles.Default.BuildCreature(id, name: name)!;

    private static ItemDefinition Item(string weapon) => ContentFiles.Default.GetItem(weapon) ?? new ItemDefinition
    {
        Id = weapon, Name = weapon.Replace('-', ' '), Slot = EquipmentSlot.MainHand, Weapon = weapon,
    };

    /// <summary>Takes away whatever the creature was holding and gives it these instead.</summary>
    private static Creature Rearm(Creature creature, params string[] weapons)
    {
        foreach (var held in creature.Equipment.Items.Where(item => item.IsWeapon).ToList())
        {
            creature.Equipment.Unequip(held.Id);
        }

        foreach (var weapon in weapons)
        {
            ContentFiles.Default.Equip(creature, Item(weapon));
        }

        return creature;
    }

    private static (Battle Battle, HeuristicActionSource Source) Fight(
        IReadOnlyList<(Creature Creature, int X, int Y)> party,
        IReadOnlyList<(Creature Creature, int X, int Y)> foes)
    {
        var field = new Battlefield(30, 30);
        foreach (var (creature, x, y) in party.Concat(foes))
        {
            field.Place(creature, x, y);
        }

        var battle = new Battle(
            party.Select(one => one.Creature), foes.Select(one => one.Creature), new SequenceRandom(true, 10), battlefield: field);

        return (battle, new HeuristicActionSource(battle, new SequenceRandom(true, 1)));
    }

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
    public void APolearmStepsBackToTheEndOfItsHaft()
    {
        var aldric = Rearm(Build("aldric"), "longspear");
        var orc = Build("orc");
        var (battle, source) = Fight([(aldric, 5, 5)], [(orc, 6, 5)]);

        var step = Assert.IsType<FiveFootStepAction>(source.NextAction(TurnOf(battle, aldric)));

        Assert.Equal(10, Battlefield.ReachOf(aldric).FeetBetween(step.Destination!.Value, new GridSquare(6, 5)));
    }

    [Fact]
    public void FromTheEndOfItItSwings()
    {
        var aldric = Rearm(Build("aldric"), "longspear");
        var orc = Build("orc");
        var (battle, source) = Fight([(aldric, 5, 5)], [(orc, 7, 5)]);

        var action = source.NextAction(TurnOf(battle, aldric));

        Assert.True(action is FullAttackAction or AttackAction);
    }

    [Fact]
    public void ACreatureWithOneWeaponNeverThrowsIt()
    {
        var orc = Build("orc");
        var valeria = Build("valeria");
        var (battle, source) = Fight([(valeria, 1, 1)], [(orc, 1, 13)]);

        var action = source.NextAction(TurnOf(battle, orc));

        Assert.IsType<MoveAction>(action);
    }

    [Fact]
    public void ASpareBladeAndNobodyInReachIsWhenItThrows()
    {
        var orc = Rearm(Build("orc"), "shortspear", "dagger");
        var valeria = Build("valeria");
        var (battle, source) = Fight([(valeria, 1, 1)], [(orc, 1, 13)]);

        var action = source.NextAction(TurnOf(battle, orc));

        var weapon = action switch
        {
            AttackAction attack => attack.Weapon,
            FullAttackAction full => full.Weapon,
            _ => null,
        };

        Assert.NotNull(weapon);
        Assert.True(weapon.IsThrownUse);
        Assert.Equal("shortspear (thrown)", weapon.Name);
    }

    [Fact]
    public void NotWhenSomebodyIsAMoveAway()
    {
        var orc = Rearm(Build("orc"), "shortspear", "dagger");
        var valeria = Build("valeria");
        var (battle, source) = Fight([(valeria, 1, 1)], [(orc, 1, 6)]);

        var action = source.NextAction(TurnOf(battle, orc));

        Assert.False(action is AttackAction { Weapon.IsThrownUse: true } or FullAttackAction { Weapon.IsThrownUse: true });
    }

    [Fact]
    public void AJavelinIsThrownLikeAnArrowIsShot()
    {
        var orc = Rearm(Build("orc"), "javelin");
        var valeria = Build("valeria");
        var (battle, source) = Fight([(valeria, 1, 1)], [(orc, 1, 13)]);

        var action = source.NextAction(TurnOf(battle, orc));

        Assert.True(action is AttackAction { Weapon.Name: "javelin" } or FullAttackAction { Weapon.Name: "javelin" });
    }

    [Fact]
    public void OnceThrownItReachesForTheOtherBlade()
    {
        var orc = Rearm(Build("orc"), "shortspear", "dagger");
        orc.Equipment.LetGo(orc.Attacks.First(attack => attack.Kind == "shortspear"));

        Assert.Equal("dagger", orc.MeleeAttack!.Name);
    }
}

public class ThrownWeaponCampaignTests
{
    [Fact]
    public void EverythingThrownIsPickedUpOnceTheFightIsOver()
    {
        var run = Campaign.Begin(ChainContent.Library, "the-long-road");
        var merrin = run.Party.Single(one => one.Name == "Merrin");
        ContentFiles.Default.Equip(merrin, ContentFiles.Default.GetItem("dagger")!);
        merrin.Equipment.LetGo(merrin.Attacks.Single(attack => attack.IsThrownUse));

        foreach (var foe in run.Battle.Foes)
        {
            foe.HitPoints.Take(foe.HitPoints.Maximum + 100);
        }

        run.Collect();

        Assert.False(merrin.Equipment.IsOutOfHand(ContentFiles.Default.GetItem("dagger")!));
    }

    [Fact]
    public void NotWhileItIsStillGoingOn()
    {
        var run = Campaign.Begin(ChainContent.Library, "the-long-road");
        var merrin = run.Party.Single(one => one.Name == "Merrin");
        ContentFiles.Default.Equip(merrin, ContentFiles.Default.GetItem("dagger")!);
        merrin.Equipment.LetGo(merrin.Attacks.Single(attack => attack.IsThrownUse));

        run.Collect();

        Assert.True(merrin.Equipment.IsOutOfHand(ContentFiles.Default.GetItem("dagger")!));
    }
}
