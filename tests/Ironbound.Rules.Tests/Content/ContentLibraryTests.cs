using Ironbound.Rules.Abilities;
using Ironbound.Rules.Combat;
using Ironbound.Rules.Content;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Dice;
using Ironbound.Rules.Effects;
using Ironbound.Rules.Magic;
using Ironbound.Rules.Modifiers;
using Ironbound.Rules.Saves;

namespace Ironbound.Rules.Tests.Content;

public class ContentParsingTests
{
    [Fact]
    public void ASpellIsReadWithItsSchoolRangeShapeAndSave()
    {
        var library = From("""
            { "kind": "spell", "id": "fireball", "name": "Fireball", "level": 3,
              "school": "Evocation", "range": "long",
              "target": { "burst": 20 }, "affects": "Everyone",
              "save": "Reflex", "onSave": "Half",
              "does": [ { "damage": { "perLevel": 6, "maxDice": 10 }, "type": "Fire" } ] }
            """);

        var spell = Assert.IsType<Spell>(library.GetSpell("fireball"));

        Assert.Empty(library.Problems);
        Assert.Equal("Fireball", spell.Name);
        Assert.Equal(3, spell.Level);
        Assert.Equal(SpellSchool.Evocation, spell.School);
        Assert.Equal(SpellRange.Long, spell.Range);
        Assert.Equal(20, Assert.IsType<BurstTarget>(spell.Target).RadiusFeet);
        Assert.Equal(SpellAffects.Everyone, spell.Affects);
        Assert.Equal(Save.Reflex, spell.Save);
        Assert.Equal(SaveOutcome.Half, spell.OnSave);
        Assert.Equal("10d6", Assert.IsType<DealDamage>(spell.Does[0]).Amount.At(20).ToString());
    }

    [Fact]
    public void ASpellWithNoSaveEntryHasNoSave()
    {
        var library = From("""
            { "kind": "spell", "id": "magic-missile", "level": 1, "school": "Evocation",
              "target": { "shape": "single" },
              "does": [ { "damage": { "perLevel": 4, "maxDice": 5, "levelsPerDie": 2, "flatPerDie": 1 } } ] }
            """);

        Assert.Null(library.GetSpell("magic-missile")!.Save);
    }

    [Fact]
    public void ARangeCanBeANumberOfFeet()
    {
        var library = From("""
            { "kind": "spell", "id": "reach", "level": 1, "school": "Evocation", "range": "45",
              "target": { "shape": "ray" }, "does": [] }
            """);

        Assert.Empty(library.Problems);
        Assert.Equal(45, library.GetSpell("reach")!.Range.Feet);
    }

    [Fact]
    public void AWeaponIsReadWithItsCriticalProfile()
    {
        var weapon = Assert.IsType<WeaponAttack>(From("""
            { "kind": "weapon", "id": "scimitar", "name": "scimitar", "damage": "1d6",
              "damageType": "Slashing", "threatsOn": 18, "multiplier": 2 }
            """).BuildWeapon("scimitar"));

        Assert.Equal("scimitar", weapon.Name);
        Assert.Equal(18, weapon.Attack.Critical.ThreatsOn);
        Assert.Equal(2, weapon.Attack.Critical.Multiplier);
    }

    [Fact]
    public void EachBuiltWeaponIsItsOwn()
    {
        var library = From("""
            { "kind": "weapon", "id": "scimitar", "name": "scimitar", "damage": "1d6" }
            """);

        var mine = library.BuildWeapon("scimitar")!;
        var yours = library.BuildWeapon("scimitar")!;
        mine.Attack.Modifiers.Add(5, BonusType.Enhancement, "Blessing");

        // Two goblins must not share one scimitar: a modifier stack is state, not description.
        Assert.Equal(5, mine.Attack.Modifiers.Total);
        Assert.Equal(0, yours.Attack.Modifiers.Total);
    }

    [Fact]
    public void ACreatureIsReadWithSizeSpeedArmourSavesAndWeapons()
    {
        var goblin = Assert.IsType<Creature>(From(
            """
            { "kind": "weapon", "id": "scimitar", "name": "scimitar", "damage": "1d6",
              "damageType": "Slashing", "threatsOn": 18 }
            """,
            """
            { "kind": "creature", "id": "goblin", "name": "Goblin",
              "abilities": [11, 15, 12, 10, 9, 6], "hitPoints": 9, "hitDice": 2, "level": 2,
              "size": "Small", "speed": 20, "baseAttack": 1, "armour": 3,
              "goodSave": "Fortitude", "weapons": ["scimitar"] }
            """).BuildCreature("goblin"));

        Assert.Equal("Goblin", goblin.Name);
        Assert.Equal(CreatureSize.Small, goblin.Size);
        Assert.Equal(20, goblin.Speed);
        Assert.Equal(11, goblin.Abilities[Ability.Strength].Score);
        Assert.Equal(SaveProgression.Good(2), goblin.Saves.Fortitude.Base);
        Assert.Equal(SaveProgression.Poor(2), goblin.Saves.Will.Base);
        Assert.Equal("scimitar", goblin.PrimaryAttack!.Name);
    }

    [Fact]
    public void ACasterIsReadWithItsSlotsAndPreparedSpells()
    {
        var merrin = From(
            """
            { "kind": "spell", "id": "fireball", "name": "Fireball", "level": 3,
              "school": "Evocation", "target": { "burst": 20 }, "does": [] }
            """,
            """
            { "kind": "creature", "id": "merrin", "name": "Merrin",
              "abilities": [8, 14, 12, 18, 13, 10], "hitPoints": 18, "hitDice": 5, "level": 5,
              "castingAbility": "Intelligence", "casterLevel": 5,
              "slots": [ { "level": 3, "count": 1 } ], "spells": ["fireball"] }
            """).BuildCreature("merrin")!;

        Assert.Equal(5, merrin.Spells.CasterLevel);
        Assert.Equal(Ability.Intelligence, merrin.Spells.CastingAbility);
        Assert.Equal(1, merrin.Spells.SlotsRemaining(3));
        Assert.True(merrin.Spells.CanCast(merrin.Spells.Prepared.Single()));
    }

    [Fact]
    public void AnEncounterIsReadWithItsGroundAndItsSides()
    {
        var encounter = From("""
            { "kind": "encounter", "id": "ambush", "name": "Ambush", "width": 20, "height": 14,
              "blocked": [ { "x": 5, "y": 5 } ], "difficult": [ { "x": 6, "y": 6 } ],
              "placements": [
                { "creature": "valeria", "x": 3, "y": 4, "party": true },
                { "creature": "goblin", "x": 10, "y": 3, "name": "Goblin 1" } ] }
            """, expectProblems: true).GetEncounter("ambush")!;

        Assert.Equal((20, 14), (encounter.Width, encounter.Height));
        Assert.Equal((5, 5), (encounter.Blocked[0].X, encounter.Blocked[0].Y));
        Assert.Equal((6, 6), (encounter.Difficult[0].X, encounter.Difficult[0].Y));
        Assert.True(encounter.Placements[0].Party);
        Assert.Null(encounter.Placements[0].Name);
        Assert.Equal("Goblin 1", encounter.Placements[1].Name);
    }

    [Fact]
    public void AFileCanCarryCommentsExplainingItself()
    {
        var library = From("""
            // Simplified: the rules grant a second ray at seventh level.
            { "kind": "spell", "id": "scorching-ray", "level": 2, "school": "Evocation",
              "target": { "shape": "ray" },
              "does": [
                { "damage": { "fixed": "4d6" }, "type": "Fire" } // one ray, not three
              ], }
            """);

        // Strict JSON allows neither the comments nor that trailing comma. Content is written by
        // a person, and the note saying why a number is what it is belongs beside the number.
        Assert.Equal("4d6", Assert.IsType<DealDamage>(
            library.GetSpell("scorching-ray")!.Does[0]).Amount.At(20).ToString());
    }

    [Fact]
    public void FieldsTheGameDoesNotKnowAreIgnored()
    {
        var library = From("""
            { "kind": "weapon", "id": "scimitar", "name": "scimitar", "damage": "1d6",
              "flavour": "notched", "weightInPounds": 4 }
            """);

        Assert.Empty(library.Problems);
        Assert.NotNull(library.BuildWeapon("scimitar"));
    }

    internal static ContentLibrary From(params string[] files) => From(false, files);

    internal static ContentLibrary From(string json, bool expectProblems) =>
        From(expectProblems, [json]);

    private static ContentLibrary From(bool expectProblems, string[] files)
    {
        var library = ContentLibrary.Load(
            files.Select((json, index) => ($"file{index}.json", json)));

        if (!expectProblems)
        {
            Assert.Empty(library.Problems);
        }

        return library;
    }
}

public class ContentDiagnosticsTests
{
    [Fact]
    public void AFileThatIsNotJsonNamesTheFileAndTheOtherFilesStillLoad()
    {
        var library = ContentLibrary.Load([
            ("broken.json", "{ not json at all"),
            ("scimitar.json", """{ "kind": "weapon", "id": "scimitar", "name": "scimitar" }"""),
        ]);

        // One typo must not hide the nine files after it.
        var problem = Assert.Single(library.Problems);
        Assert.Equal("broken.json", problem.Source);
        Assert.Equal("(file)", problem.Field);
        Assert.NotNull(library.BuildWeapon("scimitar"));
    }

    [Fact]
    public void AMissingRequiredFieldNamesTheFileAndTheField()
    {
        var library = ContentLibrary.Load([
            ("nameless.json", """{ "kind": "weapon", "damage": "1d6" }"""),
        ]);

        var problem = Assert.Single(library.Problems);
        Assert.Equal("nameless.json", problem.Source);
        Assert.Equal("id", problem.Field);
        Assert.Contains("required", problem.Message);
    }

    [Fact]
    public void AnUnknownKindIsReportedRatherThanSkippedQuietly()
    {
        var library = ContentLibrary.Load([
            ("feat.json", """{ "kind": "feat", "id": "power-attack" }"""),
        ]);

        Assert.Equal("kind", Assert.Single(library.Problems).Field);
    }

    [Fact]
    public void AnEnumValueThatDoesNotExistIsReportedWithTheOnesThatDo()
    {
        var library = ContentLibrary.Load([
            ("axe.json", """{ "kind": "weapon", "id": "axe", "name": "axe", "damageType": "Sarcasm" }"""),
        ]);

        var problem = Assert.Single(library.Problems);
        Assert.Equal("damageType", problem.Field);
        Assert.Contains("Slashing", problem.Message);
    }

    [Fact]
    public void ARangeThatIsNotARangeSaysWhatARangeLooksLike()
    {
        var library = ContentLibrary.Load([
            ("spell.json",
             """{ "kind": "spell", "id": "x", "level": 1, "school": "Evocation", "range": "yonder", "does": [] }"""),
        ]);

        Assert.Equal("range", Assert.Single(library.Problems).Field);
    }

    [Fact]
    public void ATwoOfAKindIdentifierIsReported()
    {
        var library = ContentLibrary.Load([
            ("a.json", """{ "kind": "weapon", "id": "axe", "name": "axe" }"""),
            ("b.json", """{ "kind": "weapon", "id": "axe", "name": "other axe" }"""),
        ]);

        var problem = Assert.Single(library.Problems);
        Assert.Equal("b.json", problem.Source);
        Assert.Contains("already", problem.Message);
    }

    [Fact]
    public void ACreatureCitingAWeaponThatDoesNotExistIsReported()
    {
        var library = ContentLibrary.Load([
            ("goblin.json", """
                { "kind": "creature", "id": "goblin", "name": "Goblin",
                  "abilities": [11, 15, 12, 10, 9, 6], "weapons": ["halberd"] }
                """),
        ]);

        var problem = Assert.Single(library.Problems);
        Assert.Equal("creature 'goblin'", problem.Source);
        Assert.Contains("halberd", problem.Message);

        // Reported, not fatal: the goblin still exists, it is simply unarmed.
        Assert.Empty(library.BuildCreature("goblin")!.Attacks);
    }

    [Fact]
    public void ACreatureCitingASpellThatDoesNotExistIsReported()
    {
        var library = ContentLibrary.Load([
            ("wizard.json", """
                { "kind": "creature", "id": "wizard", "name": "Wizard",
                  "abilities": [8, 14, 12, 18, 13, 10], "spells": ["wish"] }
                """),
        ]);

        Assert.Contains("wish", Assert.Single(library.Problems).Message);
    }

    [Fact]
    public void AnEncounterPlacingACreatureThatDoesNotExistIsReported()
    {
        var library = ContentLibrary.Load([
            ("fight.json", """
                { "kind": "encounter", "id": "fight", "name": "Fight",
                  "placements": [ { "creature": "dragon", "x": 1, "y": 1 } ] }
                """),
        ]);

        var problem = Assert.Single(library.Problems);
        Assert.Equal("encounter 'fight'", problem.Source);
        Assert.Contains("dragon", problem.Message);
    }

    [Fact]
    public void WrongAbilityScoreCountIsReportedAndDoesNotCrashTheBuild()
    {
        var library = ContentLibrary.Load([
            ("half.json", """{ "kind": "creature", "id": "half", "name": "Half", "abilities": [10, 10] }"""),
        ]);

        Assert.Equal("abilities", Assert.Single(library.Problems).Field);
        Assert.Equal(10, library.BuildCreature("half")!.Abilities[Ability.Charisma].Score);
    }

    [Fact]
    public void ProblemsReadLikeSomethingYouCanGoAndFix()
    {
        var library = ContentLibrary.Load([
            ("nameless.json", """{ "kind": "weapon", "damage": "1d6" }"""),
        ]);

        Assert.Equal("nameless.json: id: is required.", library.Problems[0].ToString());
        Assert.False(library.IsValid);
    }
}

public class ShippedContentTests
{
    [Fact]
    public void TheContentTheGameShipsLoadsWithNothingWrongWithIt()
    {
        // The test that makes a content mistake a failing build rather than a surprise three
        // rooms into a playthrough.
        Assert.Empty(TestContent.Library.Problems);
    }

    [Theory]
    [InlineData("fireball")]
    [InlineData("magic-missile")]
    [InlineData("scorching-ray")]
    [InlineData("cure-light-wounds")]
    [InlineData("bless")]
    public void EverySpellTheGameNeedsIsThere(string id) =>
        Assert.NotNull(TestContent.Library.GetSpell(id));

    [Theory]
    [InlineData("valeria")]
    [InlineData("karn")]
    [InlineData("merrin")]
    [InlineData("goblin")]
    public void EveryCreatureTheOpeningFightNeedsIsThere(string id) =>
        Assert.NotNull(TestContent.Library.GetCreature(id));

    [Theory]
    [InlineData("longsword")]
    [InlineData("greataxe")]
    [InlineData("quarterstaff")]
    [InlineData("scimitar")]
    public void EveryWeaponTheOpeningFightNeedsIsThere(string id) =>
        Assert.NotNull(TestContent.Library.BuildWeapon(id));

    [Fact]
    public void TheOpeningFightStandsWhereItAlwaysDid()
    {
        var ambush = TestContent.Library.GetEncounter("goblin-ambush")!;

        Assert.Equal((16, 12), (ambush.Width, ambush.Height));
        Assert.Equal(3, ambush.Placements.Count(placement => placement.Party));
        Assert.Equal(3, ambush.Placements.Count(placement => !placement.Party));
        Assert.Equal(
            ["Goblin 1", "Goblin 2", "Goblin 3"],
            ambush.Placements.Where(p => !p.Party).Select(p => p.Name));
    }

    [Fact]
    public void ValeriaComesOutOfTheFilesAsSheCameOutOfTheCode()
    {
        var valeria = TestContent.Library.BuildCreature("valeria")!;

        Assert.Equal(42, valeria.HitPoints.Maximum);   // 32 written down, +2 a die for Con 14
        Assert.Equal(18, valeria.ArmorClass.Total);    // 10 + 6 armour + 2 Dexterity
        Assert.Equal("longsword", valeria.PrimaryAttack!.Name);
        Assert.Equal(19, valeria.PrimaryAttack.Attack.Critical.ThreatsOn);
    }

    [Fact]
    public void BlessCarriesItsEffectWithNoDelegateInSight()
    {
        var bless = TestContent.Library.GetSpell("bless")!;
        var bestow = Assert.IsType<Bestow>(bless.Does.Single());

        Assert.Equal("Bless", bestow.Name);
        Assert.Equal(EffectKind.Modifier, bestow.Effect.Kind);

        // A minute a level, and a morale bonus to attack — the rulebook entry, in a file.
        Assert.Equal(Duration.Minutes(5), bestow.Effect.DurationFor(casterLevel: 5));
        var grant = Assert.Single(bestow.Effect.Grants);
        Assert.Equal(ModifierTarget.Attack, grant.Target);
        Assert.Equal((1, BonusType.Morale), (grant.Value, grant.Type));
    }
}
