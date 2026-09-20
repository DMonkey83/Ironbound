using Ironbound.Rules;
using Ironbound.Rules.Abilities;
using Ironbound.Rules.Content;
using Ironbound.Rules.Combat;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Defense;
using Ironbound.Rules.Dice;
using Ironbound.Rules.Effects;
using Ironbound.Rules.Encounters;
using Ironbound.Rules.Magic;
using Ironbound.Rules.Maps;
using Ironbound.Rules.Modifiers;
using Ironbound.Rules.Persistence;
using Ironbound.Rules.Saves;

namespace Ironbound.Rules.Tests.Persistence;

public class GameSaveTests
{
    private static Creature Hero()
    {
        var hero = new Creature("Valeria", new AbilityScores(18, 14, 14, 10, 12, 10), 32, 5)
        {
            Allegiance = 1,
            Speed = 30,
        };

        hero.AttackModifiers.Add(5, BonusType.Untyped, "Base Attack Bonus");
        hero.ArmorClass.Modifiers.Add(6, BonusType.Armor, "Breastplate");
        hero.Saves.Fortitude.Base = SaveProgression.Good(5);
        hero.Attacks.Add(WeaponAttack.Melee(
            "longsword", "1d8", DamageType.Slashing, new CriticalProfile(19, 2)));

        return hero;
    }

    private static Creature Ogre()
    {
        var ogre = new Creature("Ogre", new AbilityScores(21, 8, 15, 6, 10, 7), 30, 4)
        {
            Allegiance = 2,
            Size = CreatureSize.Large,
            Speed = 40,
            AttacksOfOpportunityPerRound = 2,
        };

        ogre.Defenses.Reduce(5, DamageBypass.Magic).Resist(DamageType.Fire, 10)
            .MakeImmuneTo(DamageType.Cold).MakeVulnerableTo(DamageType.Acid);
        ogre.Attacks.Add(WeaponAttack.Melee("greatclub", "2d8", DamageType.Bludgeoning));

        return ogre;
    }

    private static Encounter Fight(out Battlefield field, out Creature hero, out Creature ogre)
    {
        field = new Battlefield(12, 8);
        field.Block(new GridSquare(5, 5));
        field.MakeDifficult(new GridSquare(4, 4));

        hero = Hero();
        ogre = Ogre();
        field.Place(hero, 2, 2);
        field.Place(ogre, 8, 6);

        return new Encounter(
            [hero, ogre], new PcgRandom(seed: 1234), rules: null, battlefield: field);
    }

    private static Encounter RoundTrip(Encounter encounter) =>
        GameSave.Restore(
            GameSave.FromJson(GameSave.ToJson(GameSave.Capture(encounter))), TestContent.Library);

    [Fact]
    public void TheGroundComesBack()
    {
        var encounter = Fight(out _, out _, out _);

        var field = RoundTrip(encounter).Battlefield!;

        Assert.Equal(12, field.Width);
        Assert.Equal(8, field.Height);
        Assert.True(field.IsBlocked(new GridSquare(5, 5)));
        Assert.True(field.IsDifficult(new GridSquare(4, 4)));
        Assert.False(field.IsBlocked(new GridSquare(0, 0)));
    }

    [Fact]
    public void EverybodyComesBackWhereTheyStood()
    {
        var encounter = Fight(out _, out _, out _);

        var field = RoundTrip(encounter).Battlefield!;
        var names = field.Creatures.ToDictionary(c => c.Name, c => field.SquareOf(c));

        Assert.Equal(new GridSquare(2, 2), names["Valeria"]);
        Assert.Equal(new GridSquare(8, 6), names["Ogre"]);
    }

    [Fact]
    public void WoundsAndPoolsSurvive()
    {
        var encounter = Fight(out _, out var hero, out _);
        hero.HitPoints.Take(13);
        hero.HitPoints.GrantTemporary(6);
        hero.HitPoints.TakeNonlethal(4);
        var before = hero.HitPoints.Current;

        var restored = Restored(encounter, "Valeria");

        Assert.Equal(before, restored.HitPoints.Current);
        Assert.Equal(6, restored.HitPoints.Temporary);
        Assert.Equal(4, restored.HitPoints.Nonlethal);
        Assert.Equal(hero.HitPoints.Maximum, restored.HitPoints.Maximum);
    }

    [Fact]
    public void SizeSpeedAllegianceAndReachSurvive()
    {
        var encounter = Fight(out _, out _, out var ogre);

        var restored = Restored(encounter, "Ogre");

        Assert.Equal(CreatureSize.Large, restored.Size);
        Assert.Equal(10, restored.Reach);
        Assert.Equal(40, restored.Speed);
        Assert.Equal(2, restored.Allegiance);
        Assert.Equal(2, restored.AttacksOfOpportunityPerRound);

        // Size is derived, so armour class has to come back on its own.
        Assert.Equal(ogre.ArmorClass.Total, restored.ArmorClass.Total);
    }

    [Fact]
    public void DefencesSurvive()
    {
        var encounter = Fight(out _, out _, out _);

        var restored = Restored(encounter, "Ogre");

        Assert.Equal(5, restored.Defenses.ReductionAgainst(DamageBypass.None));
        Assert.Equal(0, restored.Defenses.ReductionAgainst(DamageBypass.Magic));
        Assert.Equal(10, restored.Defenses.ResistanceTo(DamageType.Fire));
        Assert.True(restored.Defenses.IsImmuneTo(DamageType.Cold));
        Assert.True(restored.Defenses.IsVulnerableTo(DamageType.Acid));
    }

    [Fact]
    public void WeaponsSurviveWithTheirNumbers()
    {
        var encounter = Fight(out _, out var hero, out _);

        var restored = Restored(encounter, "Valeria");
        var sword = restored.PrimaryAttack!;

        Assert.Equal("longsword", sword.Name);
        Assert.Equal(new CriticalProfile(19, 2), sword.Attack.Critical);
        Assert.Equal("1d8 slashing", sword.Damage.ToString());
        Assert.Equal(
            Strike.AttackBonus(hero, hero.PrimaryAttack!).Total,
            Strike.AttackBonus(restored, sword).Total);
    }

    [Fact]
    public void ARunningBuffSurvivesWithItsClockAndIsNotDoubled()
    {
        var encounter = Fight(out _, out var hero, out _);
        hero.Effects.Apply(new ModifierEffect("Bull's Strength", Duration.Minutes(5))
            .GrantsToAbility(Ability.Strength, 4, BonusType.Enhancement));
        hero.Effects.Advance(Duration.Rounds(10), new SequenceRandom(1));

        var strengthBefore = hero.Abilities.Strength.Score;
        var restored = Restored(encounter, "Valeria");

        // Exactly one copy of the bonus, not two.
        Assert.Equal(strengthBefore, restored.Abilities.Strength.Score);
        Assert.Equal(1, restored.Abilities.Strength.Modifiers.Count);

        var effect = restored.Effects.Find("Bull's Strength")!;
        Assert.Equal(Duration.Minutes(4), effect.Remaining);

        // And it still expires on schedule, which only works if it remembered where it granted.
        restored.Effects.Advance(Duration.Minutes(4), new SequenceRandom(1));
        Assert.Equal(18, restored.Abilities.Strength.Score);
        Assert.Equal(0, restored.Abilities.Strength.Modifiers.Count);
    }

    [Fact]
    public void ABleedSurvivesAndKeepsTicking()
    {
        var encounter = Fight(out _, out var hero, out _);
        hero.Effects.Apply(new DamageOverTimeEffect(
            "Bleeding", Duration.Rounds(5), "1d6", DamageType.Piercing));

        var restored = Restored(encounter, "Valeria");
        var before = restored.HitPoints.Current;

        restored.Effects.Advance(Duration.Rounds(1), new SequenceRandom(4));

        Assert.Equal(before - 4, restored.HitPoints.Current);
    }

    [Fact]
    public void SpellSlotsComeBackPartlySpent()
    {
        var encounter = Fight(out _, out var hero, out _);
        hero.Spells.CastingAbility = Ability.Intelligence;
        hero.Spells.CasterLevel = 5;
        hero.Spells.SetSlots(1, 3).SetSlots(3, 2).Prepare(Spells.Fireball).Prepare(Spells.MagicMissile);
        hero.Spells.Spend(Spells.Fireball);

        var restored = Restored(encounter, "Valeria");

        Assert.Equal(5, restored.Spells.CasterLevel);
        Assert.Equal(1, restored.Spells.SlotsRemaining(3));
        Assert.Equal(2, restored.Spells.SlotsMaximum(3));
        Assert.Equal(3, restored.Spells.SlotsRemaining(1));
        Assert.True(restored.Spells.CanCast(Spells.MagicMissile));
    }

    [Fact]
    public void SpellsAreSavedByIdentifierNotByDisplayName()
    {
        var encounter = Fight(out _, out var hero, out _);
        hero.Spells.CasterLevel = 5;
        hero.Spells.SetSlots(3, 1).Prepare(Spells.Fireball);

        var json = GameSave.ToJson(GameSave.Capture(encounter));

        // Renaming "Fireball" to "Ball of Fire" must not quietly empty every existing spellbook.
        Assert.Contains("\"fireball\"", json);
        Assert.DoesNotContain("\"Fireball\"", json);
    }

    [Fact]
    public void ASpellTheLibraryNoLongerHasIsLoudRatherThanQuiet()
    {
        var encounter = Fight(out _, out var hero, out _);
        hero.Spells.CasterLevel = 5;
        hero.Spells.SetSlots(3, 1).Prepare(Spells.Fireball);

        var save = GameSave.FromJson(GameSave.ToJson(GameSave.Capture(encounter)));
        var thin = ContentLibrary.Load([]);

        // Pre-release, every save in existence is one we can remake in seconds, so a renamed or
        // broken content file should stop the load and say which id it could not find. After
        // release this inverts — see the note in RestoreSpells.
        var error = Assert.Throws<InvalidDataException>(() => GameSave.Restore(save, thin));

        Assert.Contains("fireball", error.Message);
    }

    [Fact]
    public void ACreatureWithNoConstitutionStaysThatWay()
    {
        var field = new Battlefield(6, 6);
        var skeleton = new Creature(
            "Skeleton",
            new AbilityScores(
            [
                new AbilityScore(Ability.Strength, 15),
                new AbilityScore(Ability.Dexterity, 14),
                AbilityScore.NonAbility(Ability.Constitution),
                AbilityScore.NonAbility(Ability.Intelligence),
                new AbilityScore(Ability.Wisdom, 10),
                new AbilityScore(Ability.Charisma, 10),
            ]),
            6,
            1);

        field.Place(skeleton, 1, 1);
        var encounter = new Encounter(
            [skeleton], new PcgRandom(1), rules: null, battlefield: field);

        var restored = RoundTrip(encounter).Order[0].Creature;

        Assert.False(restored.Abilities.Constitution.HasScore);
        Assert.Equal(0, restored.HitPoints.DeathThreshold);
    }

    [Fact]
    public void TheTurnOrderAndTheClockSurvive()
    {
        var encounter = Fight(out _, out _, out _);
        encounter.BeginNextTurn();
        encounter.BeginNextTurn();

        var restored = RoundTrip(encounter);

        Assert.Equal(encounter.Tick, restored.Tick);
        Assert.Equal(encounter.Round, restored.Round);
        Assert.Equal(
            encounter.Order.Select(c => (c.Creature.Name, c.Initiative, c.NextTurnTick)),
            restored.Order.Select(c => (c.Creature.Name, c.Initiative, c.NextTurnTick)));
    }

    [Fact]
    public void TheVeryNextRollIsTheSameRoll()
    {
        var encounter = Fight(out _, out _, out _);
        for (var i = 0; i < 7; i++)
        {
            encounter.Random.NextDie(20);
        }

        var restored = RoundTrip(encounter);

        var original = Enumerable.Range(0, 20).Select(_ => encounter.Random.NextDie(20));
        var reloaded = Enumerable.Range(0, 20).Select(_ => restored.Random.NextDie(20));

        Assert.Equal(original, reloaded);
    }

    [Fact]
    public void OptionsTravelWithTheSave()
    {
        var field = new Battlefield(6, 6);
        var rules = new RuleOptions { DeathsDoor = false, MaximumReductionPercent = 75 };
        var hero = Hero();
        field.Place(hero, 1, 1);

        var encounter = new Encounter([hero], new PcgRandom(1), rules, field);
        var restored = RoundTrip(encounter);

        Assert.False(restored.Rules.DeathsDoor);
        Assert.Equal(75, restored.Rules.MaximumReductionPercent);
    }

    [Fact]
    public void ASaveFromTheFutureIsRefusedRatherThanMisread()
    {
        var encounter = Fight(out _, out _, out _);
        var save = GameSave.Capture(encounter) with { Version = SavedGame.CurrentVersion + 1 };

        Assert.Throws<InvalidDataException>(() => GameSave.Restore(save, TestContent.Library));
    }

    [Fact]
    public void TheFileIsReadable()
    {
        var encounter = Fight(out _, out _, out _);

        var json = GameSave.ToJson(GameSave.Capture(encounter));

        // Enums by name, not by number: a save file somebody has to debug should be legible.
        Assert.Contains("\"Large\"", json);
        Assert.Contains("\"Valeria\"", json);
        Assert.Contains("\"Armor\"", json);
    }

    [Fact]
    public void RejectsMissingArguments()
    {
        Assert.Throws<ArgumentNullException>(() => GameSave.Capture(null!));
        Assert.Throws<ArgumentNullException>(() => GameSave.Restore(null!, TestContent.Library));
    }

    private static Creature Restored(Encounter encounter, string name) =>
        RoundTrip(encounter).Order.Single(c => c.Creature.Name == name).Creature;
}
