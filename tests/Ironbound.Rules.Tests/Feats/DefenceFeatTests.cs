using Ironbound.Rules.Classes;
using Ironbound.Rules.Combat;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Defense;
using Ironbound.Rules.Dice;
using Ironbound.Rules.Effects;
using Ironbound.Rules.Encounters;
using Ironbound.Rules.Encounters.Actions;
using Ironbound.Rules.Feats;
using Ironbound.Rules.Magic;
using Ironbound.Rules.Maps;
using Ironbound.Rules.Saves;
using Ironbound.Rules.Tests.Classes;

namespace Ironbound.Rules.Tests.Feats;

/// <summary>The save rerolls, and the feats that keep a creature on its feet.</summary>
public class SaveRerollTests
{
    private static Creature Nimble(int extraHitPoints = 0) => ClassKit.Make(
        "fighter",
        1,
        $"\"feats\": [\"lightning-reflexes\", \"improved-lightning-reflexes\"], \"hitPoints\": {10 + extraHitPoints}");

    private static SpellCast Fireball(Creature target, params int[] rolls)
    {
        var merrin = TestContent.Library.BuildCreature("merrin")!;
        merrin.Allegiance = 1;
        target.Allegiance = 2;
        var field = ClassKit.Field((merrin, 0, 0), (target, 8, 8));

        return Casting.Resolve(merrin, Spells.Fireball, SpellAim.At(target), new SequenceRandom(rolls), field: field);
    }

    [Fact]
    public void AFailureThatWouldDropItIsRolledAgainOnceADay()
    {
        var target = Nimble();

        // Thirty damage against twelve hit points, a 2 on the save, and a 19 on the second go.
        var hit = Fireball(target, 6, 6, 6, 6, 6, 2, 19).Targets.Single();

        Assert.True(hit.Save!.Succeeded);
        Assert.False(hit.Save.FirstTry!.Succeeded);
        // Thirty, and Merrin's two for an evoker's intense spells, halved.
        Assert.Equal(16, hit.Damage);
        Assert.Contains("rerolled", hit.Save.ToString());
        Assert.False(SaveRerolls.CanReroll(target, Save.Reflex));

        // Spent for the day: the next failure stands.
        var again = Fireball(target, 1, 1, 1, 1, 1, 2).Targets.Single();
        Assert.Null(again.Save!.FirstTry);
        Assert.False(again.Save.Succeeded);
    }

    [Fact]
    public void AFailureThatDoesNotMatterKeepsTheRerollForLater()
    {
        var target = Nimble(extraHitPoints: 100);

        var hit = Fireball(target, 1, 1, 1, 1, 1, 2).Targets.Single();

        Assert.False(hit.Save!.Succeeded);
        Assert.Null(hit.Save.FirstTry);
        Assert.True(SaveRerolls.CanReroll(target, Save.Reflex));
    }

    [Fact]
    public void ARestBringsItBack()
    {
        var target = Nimble();
        Fireball(target, 6, 6, 6, 6, 6, 2, 19);

        target.DailyUses.Restore();

        Assert.True(SaveRerolls.CanReroll(target, Save.Reflex));
    }

    [Fact]
    public void EachSaveHasItsOwnFeat()
    {
        Assert.Equal(FeatEffect.ImprovedGreatFortitude, SaveRerolls.FeatFor(Save.Fortitude));
        Assert.Equal(FeatEffect.ImprovedIronWill, SaveRerolls.FeatFor(Save.Will));
        Assert.False(SaveRerolls.CanReroll(Nimble(), Save.Will));
    }
}

public class DefenceFeatTests
{
    [Fact]
    public void MobilityIsFourAgainstTheSwingAWalkDraws()
    {
        var nimble = ClassKit.Make("fighter", 4, "\"feats\": [\"dodge\", \"mobility\"]", [12, 14, 12, 10, 10, 10]);

        Assert.Equal(nimble.ArmorClass.Total + 4, nimble.ArmorClass.Value(DefenseOptions.Moving));
        Assert.Equal(nimble.ArmorClass.FlatFooted, nimble.ArmorClass.Value(DefenseOptions.Moving | DefenseOptions.DexterityDenied));
    }

    [Fact]
    public void MobilityTurnsAHitIntoAMissWhenWalkingPast()
    {
        var nimble = ClassKit.Make("fighter", 4, "\"feats\": [\"dodge\", \"mobility\"]", [12, 14, 12, 10, 10, 10]);
        var guard = ClassKit.Dummy("Guard");
        var field = ClassKit.Field((nimble, 2, 2), (guard, 3, 3));

        // The guard swings once as the walker leaves; the roll would beat the plain armour class
        // and not the one with Mobility in it.
        var needed = nimble.ArmorClass.Total - Strike.AttackBonus(guard, guard.MeleeAttack!).Total;
        var encounter = ClassKit.Fight([nimble], [guard], field, needed + 1);
        var walk = new MoveAction([new GridSquare(2, 2), new GridSquare(1, 2), new GridSquare(0, 2)]);

        // A round in first, so that nobody is flat-footed for not having acted yet.
        encounter.BeginNextTurn();
        encounter.BeginNextTurn();
        var result = Assert.IsType<MoveActionResult>(encounter.BeginNextTurn()!.Take(walk));

        var swing = Assert.Single(result.Opportunities);
        Assert.False(swing.IsHit);
    }

    [Fact]
    public void GreaterShieldFocusIsOneMoreOnTopOfShieldFocus()
    {
        var focused = ClassKit.Make("fighter", 8, "\"items\": [\"heavy-shield\", \"longsword\"], \"feats\": [\"shield-focus\"]");
        var greater = ClassKit.Make("fighter", 8, "\"items\": [\"heavy-shield\", \"longsword\"], \"feats\": [\"shield-focus\", \"greater-shield-focus\"]");

        Assert.Equal(focused.ArmorClass.Total + 1, greater.ArmorClass.Total);
        Assert.Equal(focused.ArmorClass.Touch, greater.ArmorClass.Touch);
    }

    [Fact]
    public void DefensiveCombatTrainingCountsHitDiceAsBaseAttack()
    {
        var wizard = ClassKit.Make("wizard", 6);
        var trained = ClassKit.Make("wizard", 6, "\"feats\": [\"defensive-combat-training\"]");

        Assert.Equal(Maneuvers.Defense(wizard) + 3, Maneuvers.Defense(trained));
    }

    [Fact]
    public void ImprovedTripDefendsAgainstTripsAndNothingElse()
    {
        var valeria = TestContent.Library.BuildCreature("valeria")!;

        Assert.Equal(Maneuvers.Defense(valeria) + 2, Maneuvers.Defense(valeria, ManeuverKind.Trip));
        Assert.Equal(Maneuvers.Defense(valeria), Maneuvers.Defense(valeria, ManeuverKind.BullRush));
    }

    [Fact]
    public void DiehardStaysUpStableAndStaggeredBelowNought()
    {
        var stubborn = ClassKit.Make("fighter", 3, "\"items\": [\"longsword\"], \"feats\": [\"endurance\", \"diehard\"]");
        var foe = ClassKit.Dummy("Foe");
        var field = ClassKit.Field((stubborn, 2, 2), (foe, 3, 2));
        stubborn.HitPoints.Take(stubborn.HitPoints.Current + 3);
        Bleeding.Sync(stubborn);

        Assert.Equal(HitPointState.Dying, stubborn.HitPoints.State);
        Assert.True(stubborn.IsConscious);
        Assert.True(stubborn.HitPoints.IsFightingOn);
        Assert.True(Bleeding.IsStable(stubborn));

        var encounter = ClassKit.Fight([stubborn], [foe], field, 2);
        var turn = encounter.BeginNextTurn()!;
        Assert.True(turn.Budget.IsSingleAction);

        var before = stubborn.HitPoints.Current;
        turn.Take(new AttackAction(stubborn.MeleeAttack!, foe));

        // A standard action is strenuous: a hit point for it.
        Assert.Equal(before - 1, stubborn.HitPoints.Current);
    }

    [Fact]
    public void WithoutDiehardBelowNoughtIsDownAndBleeding()
    {
        var plain = ClassKit.Make("fighter", 3);
        plain.HitPoints.Take(plain.HitPoints.Current + 3);
        Bleeding.Sync(plain);

        Assert.False(plain.IsConscious);
        Assert.False(Bleeding.IsStable(plain));
    }

    [Fact]
    public void DiehardDoesNotStopDeath()
    {
        var stubborn = ClassKit.Make("fighter", 3, "\"feats\": [\"endurance\", \"diehard\"]");
        stubborn.HitPoints.Take(stubborn.HitPoints.Current + 30);

        Assert.False(stubborn.IsAlive);
        Assert.False(stubborn.IsConscious);
    }

    [Fact]
    public void EnduranceIsTheWayToDiehardAndNothingElseYet()
    {
        var feat = TestContent.Library.GetFeat("diehard")!;

        Assert.Contains("endurance", feat.WhyNot(ClassKit.Make("fighter", 1)));
        Assert.True(feat.AvailableTo(ClassKit.Make("fighter", 1, "\"feats\": [\"endurance\"]")));
    }
}
