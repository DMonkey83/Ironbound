using Ironbound.Rules.Combat;
using Ironbound.Rules.Content;
using Ironbound.Rules.Creatures;

namespace Ironbound.Rules.Tests.Content;

/// <summary>The catalogue the importer writes, as the game ships it.</summary>
public class WeaponCatalogueTests
{
    private static ContentLibrary Library => TestContent.Library;

    private static WeaponDefinition Weapon(string id) =>
        Library.GetWeapon(id) ?? throw new InvalidOperationException($"no weapon '{id}'");

    [Fact]
    public void EveryRowOfTheMainTableIsAWeapon()
    {
        // 288 rows, the unarmed strike, three hand-made natural or giant weapons, and the guns,
        // engines and tail blades of the later tables.
        Assert.True(Library.WeaponIds.Count >= 288 + 4);

        foreach (var id in new[] { "battle-aspergillum", "gnome-battle-ladder", "shuriken", "throwing-shield", "two-bladed-sword", "whip" })
        {
            Assert.NotNull(Library.GetWeapon(id));
        }
    }

    [Fact]
    public void TheOldIdsKeepTheirMeaning()
    {
        foreach (var id in new[]
        {
            "longsword", "greatsword", "greataxe", "dagger", "light-mace", "quarterstaff", "scimitar",
            "shortbow", "shortspear", "short-sword", "light-crossbow", "bite", "rat-bite", "ogre-axe",
        })
        {
            Assert.NotNull(Library.GetWeapon(id));
        }

        Assert.Equal("1d8", Weapon("longsword").Damage);
        Assert.Equal(19, Weapon("longsword").ThreatsOn);
        Assert.Equal(3, Weapon("greataxe").Multiplier);
        Assert.Equal(60, Weapon("shortbow").Range);
    }

    [Fact]
    public void NamesWithACommaPutTheirSecondHalfFirst()
    {
        Assert.Equal("gnome battle ladder", Weapon("gnome-battle-ladder").Name);
        Assert.Equal("collapsible kumade", Weapon("collapsible-kumade").Name);
        Assert.Equal("shuriken", Weapon("shuriken").Name);
    }

    [Fact]
    public void CategoryAndHandsComeFromTheTable()
    {
        Assert.Equal(WeaponCategory.Simple, Weapon("dagger").Category);
        Assert.Equal(WeaponHands.Light, Weapon("dagger").Hands);
        Assert.Equal(WeaponCategory.Martial, Weapon("longsword").Category);
        Assert.Equal(WeaponHands.OneHanded, Weapon("longsword").Hands);
        Assert.Equal(WeaponCategory.Exotic, Weapon("spiked-chain").Category);
        Assert.Equal(WeaponHands.TwoHanded, Weapon("greataxe").Hands);
        Assert.Equal(WeaponHands.Ranged, Weapon("longbow").Hands);
        Assert.Equal(WeaponCategory.Natural, Weapon("bite").Category);
    }

    [Fact]
    public void TheHandMadeNumbersAgreeWithTheBook()
    {
        var staff = Weapon("quarterstaff");
        Assert.Equal(WeaponHands.TwoHanded, staff.Hands);
        Assert.True(staff.Has(WeaponSpecial.Double));
        Assert.Equal("1d6", staff.SecondHead!.DamageMedium);

        Assert.True(Weapon("dagger").IsThrowable);
        Assert.Equal(10, Weapon("dagger").Range);
        Assert.True(Weapon("shortspear").IsThrowable);
        Assert.Equal(20, Weapon("shortspear").Range);
    }

    [Fact]
    public void BothColumnsOfDiceAreKept()
    {
        Assert.Equal("1d6", Weapon("longsword").DamageSmall);
        Assert.Equal("1d8", Weapon("longsword").Damage);
        Assert.Equal("1", Weapon("blowgun").DamageSmall);
        Assert.Equal("1d2", Weapon("blowgun").Damage);
    }

    [Fact]
    public void OrIsEitherAndAndIsBoth()
    {
        Assert.Equal(DamageRule.Either, Weapon("dagger").DamageRule);
        Assert.Equal([DamageType.Piercing, DamageType.Slashing], Weapon("dagger").DamageTypes);

        Assert.Equal(DamageRule.Both, Weapon("morningstar").DamageRule);
        Assert.Equal(DamageRule.Both, Weapon("broken-back-seax").DamageRule);
        Assert.Equal(DamageRule.Either, Weapon("rope-gauntlet").DamageRule);
        Assert.Equal(DamageRule.Either, Weapon("monks-spade").DamageRule);
        Assert.Equal(3, Weapon("monks-spade").DamageTypes.Count);

        Assert.Equal(DamageRule.Single, Weapon("longsword").DamageRule);
        Assert.Equal([DamageType.Fire], Weapon("battle-poi").DamageTypes);
    }

    [Fact]
    public void DisarmOrTripIsBoth() =>
        Assert.True(Weapon("shang-gou").Has(WeaponSpecial.Disarm | WeaponSpecial.Trip));

    [Fact]
    public void ADoubleWeaponKeepsItsOtherEndAndItsOwnCritical()
    {
        var hammer = Weapon("gnome-hooked-hammer");

        Assert.Equal(3, hammer.Multiplier);
        Assert.Equal(4, hammer.SecondHead!.Multiplier);
        Assert.Equal("1d6", hammer.SecondHead.DamageMedium);
    }

    [Fact]
    public void ARangeOnAMeleeRowMeansItCanBeThrown()
    {
        Assert.True(Weapon("trident").IsThrowable);
        Assert.False(Weapon("trident").IsRanged);
        Assert.False(Weapon("longsword").IsThrowable);
    }

    [Fact]
    public void ARangedRowIsShotUnlessItIsThrown()
    {
        Assert.True(Weapon("javelin").Thrown);
        Assert.False(Weapon("longbow").Thrown);
        Assert.Equal(AbilityDamageScale.Full, Weapon("javelin").DamageScale);
        Assert.Equal(AbilityDamageScale.None, Weapon("longbow").DamageScale);
        Assert.Equal(AbilityDamageScale.Full, Weapon("sling").DamageScale);
    }

    [Fact]
    public void CostAndWeightAreNumbers()
    {
        Assert.Equal(15, Weapon("longsword").Cost);
        Assert.Equal(4, Weapon("longsword").Weight);
        Assert.Equal(0.5, Weapon("dart").Cost);
        Assert.Equal(0.5, Weapon("dart").Weight);
        Assert.Equal(0, Weapon("club").Cost);
        Assert.Equal(3, Weapon("light-shield").Cost);
    }

    [Fact]
    public void EveryWeaponHasAGroupButTheOnesTheBooksLeftOut()
    {
        var groupless = Library.WeaponIds.Where(id => Weapon(id).Groups.Count == 0).Order().ToList();

        Assert.Equal(
            ["claw-blades", "long-lash-kobold-tail", "pounder-kobold-tail", "razored-kobold-tail", "spiked-kobold-tail", "sweeper-kobold-tail"],
            groupless);
        Assert.Contains("tribal", Weapon("club").Groups);
    }

    [Fact]
    public void EverySeeTextWeaponSaysWhatItsTextIsAbout()
    {
        foreach (var id in Library.WeaponIds.Where(id => Weapon(id).Has(WeaponSpecial.SeeText)))
        {
            Assert.False(string.IsNullOrWhiteSpace(Weapon(id).Description), id);
        }
    }

    [Fact]
    public void ANetIsWrittenDownButNeverBuilt()
    {
        Assert.False(Weapon("net").Usable);
        Assert.Null(Library.BuildWeapon("net"));
        Assert.NotNull(Library.BuildWeapon("whip"));
    }

    [Fact]
    public void TheUnarmedStrikeIsThereForTheMonkAndNobodyElse()
    {
        var fist = Weapon("unarmed-strike");

        Assert.Equal(WeaponCategory.Simple, fist.Category);
        Assert.Equal(WeaponHands.Light, fist.Hands);
        Assert.Equal("1d3", fist.Damage);
        Assert.Equal("1d2", fist.DamageSmall);
        Assert.True(fist.Has(WeaponSpecial.Nonlethal));

        foreach (var id in Library.CreatureIds)
        {
            Assert.DoesNotContain(Library.BuildCreature(id)!.Attacks, attack => attack.Kind == "unarmed-strike");
        }
    }

    [Fact]
    public void LightWeaponsAndAFewOthersCanBeFinessed()
    {
        Assert.True(Weapon("dagger").Finesse);
        Assert.True(Weapon("rapier").Finesse);
        Assert.True(Weapon("whip").Finesse);
        Assert.False(Weapon("longsword").Finesse);
    }

    [Fact]
    public void GunsAreExoticRangedFirearmsThatMisfire()
    {
        var musket = Weapon("musket");

        Assert.Equal(WeaponCategory.Exotic, musket.Category);
        Assert.Equal(WeaponHands.Ranged, musket.Hands);
        Assert.Equal(WeaponHands.TwoHanded, musket.Grip);
        Assert.Equal(FirearmEra.Early, musket.Firearm);
        Assert.Equal(2, musket.Misfire);
        Assert.Equal(1, musket.Capacity);
        Assert.Equal(["firearms"], musket.Groups);

        Assert.Equal(FirearmEra.Advanced, Weapon("revolver").Firearm);
        Assert.Equal(WeaponHands.OneHanded, Weapon("revolver").Grip);
    }

    [Fact]
    public void ModernGunsSiegeEnginesAndExplosivesAreCatalogueOnly()
    {
        foreach (var id in new[] { "nagant-m1895-revolver", "light-ballista", "large-ram", "aasen-mortar", "m1914-grenade-fragmentation" })
        {
            Assert.False(Weapon(id).Usable, id);
        }

        Assert.Equal(["siege-engines"], Weapon("light-catapult").Groups);
    }

    [Fact]
    public void ATailBladeIsExoticAndSaysWhoItIsFor()
    {
        var tail = Weapon("sweeper-kobold-tail");

        Assert.Equal(WeaponCategory.Exotic, tail.Category);
        Assert.Contains("kobold", tail.Description);
        Assert.True(tail.Usable);
        Assert.False(Weapon("claw-blades").Usable);
    }

    [Fact]
    public void WhatIsNotAWeaponIsInACatalogue()
    {
        Assert.Equal(["ammunition", "firearm-gear", "siege-ammunition", "weapon-modifications"],
            Library.Catalogues.Select(table => table.Id));
        Assert.NotNull(Library.GetCatalogue("ammunition")!.Get("arrows-20"));
        Assert.NotNull(Library.GetCatalogue("firearm-gear")!.Get("black-powder-dose"));
        Assert.Equal(1500, Library.GetCatalogue("weapon-modifications")!.Get("brutally-weighted")!.Cost);
        Assert.Contains("martial", Library.GetCatalogue("weapon-modifications")!.Rule);
    }

    [Theory]
    [InlineData(WeaponCategory.Simple, WeaponCategory.Martial)]
    [InlineData(WeaponCategory.Martial, WeaponCategory.Exotic)]
    [InlineData(WeaponCategory.Exotic, WeaponCategory.Exotic)]
    [InlineData(WeaponCategory.Natural, WeaponCategory.Natural)]
    public void AModifiedWeaponIsAStepHarderToUse(WeaponCategory before, WeaponCategory after) =>
        Assert.Equal(after, WeaponCategories.Raised(before));
}

/// <summary>What the loader says about a weapon file that has something wrong with it.</summary>
public class WeaponFileProblemTests
{
    private static IReadOnlyList<ContentProblem> Problems(string json) =>
        ContentLibrary.Load([("weapon.json", json)]).Problems;

    [Theory]
    [InlineData("\"category\": \"legendary\"", "category")]
    [InlineData("\"hands\": \"three-handed\"", "hands")]
    [InlineData("\"specials\": [\"vorpal\"]", "specials")]
    [InlineData("\"damageTypes\": [\"Sarcasm\"]", "damageTypes")]
    [InlineData("\"damageRule\": \"maybe\"", "damageRule")]
    [InlineData("\"damageMedium\": \"lots\"", "damageMedium")]
    public void AnUnknownValueIsReported(string member, string field)
    {
        var problems = Problems($$"""{ "kind": "weapon", "id": "odd", "name": "odd", {{member}} }""");

        Assert.Contains(problems, problem => problem.Field == field);
    }

    [Fact]
    public void AGoodFileHasNoProblems() =>
        Assert.Empty(Problems("""
            { "kind": "weapon", "id": "pike", "name": "pike", "category": "martial", "hands": "two-handed",
              "damageSmall": "1d6", "damageMedium": "1d8", "damageTypes": ["Piercing", "Slashing"],
              "damageRule": "either", "specials": ["reach", "see-text"], "description": "A long stick." }
            """));

    [Fact]
    public void SeeTextWithNothingToSayIsReported()
    {
        var problems = Problems("""{ "kind": "weapon", "id": "odd", "name": "odd", "specials": ["see-text"] }""");

        Assert.Contains(problems, problem => problem.Field == "description");
    }

    [Fact]
    public void AnItemCannotBeACatalogueOnlyWeapon()
    {
        var library = ContentLibrary.Load([
            ("net.json", """{ "kind": "weapon", "id": "net", "name": "net", "usable": false }"""),
            ("item.json", """{ "kind": "item", "id": "a-net", "name": "a net", "weapon": "net" }"""),
        ]);

        Assert.Contains(library.Problems, problem => problem.Source == "item 'a-net'" && problem.Message.Contains("catalogue only"));
    }

    [Fact]
    public void AnOlderFileStillReads()
    {
        var library = ContentLibrary.Load([("bow.json", """
            { "kind": "weapon", "id": "bow", "name": "bow", "damage": "1d6", "damageType": "Piercing",
              "rangeIncrement": 60, "attackAbility": "Dexterity", "scale": "None" }
            """)]);

        var bow = library.GetWeapon("bow")!;
        Assert.Empty(library.Problems);
        Assert.Equal(WeaponHands.Ranged, bow.Hands);
        Assert.Equal(60, bow.Range);
        Assert.Equal(DamageType.Piercing, bow.DamageType);
        Assert.Equal(60, library.BuildWeapon("bow")!.RangeIncrement);
    }

    [Fact]
    public void AnUnknownRaceIsReported()
    {
        var library = ContentLibrary.Load([("someone.json", """
            { "kind": "creature", "id": "someone", "name": "Someone", "abilities": [10, 10, 10, 10, 10, 10], "race": "dwarf" }
            """)]);

        Assert.Contains(library.Problems, problem => problem.Field == "race" && problem.Message.Contains("dwarf"));
    }

    [Fact]
    public void AClassProficiencyThatIsNotAWordOrAWeaponIsReported()
    {
        var library = ContentLibrary.Load([("odd.json", """
            { "kind": "class", "id": "odd", "name": "Odd",
              "weaponProficiencies": ["simple", "lightsabre"], "armourProficiencies": ["light", "mithral"] }
            """)]);

        Assert.Contains(library.Problems, problem => problem.Field == "weaponProficiencies" && problem.Message.Contains("lightsabre"));
        Assert.Contains(library.Problems, problem => problem.Field == "armourProficiencies" && problem.Message.Contains("mithral"));
    }

    [Fact]
    public void ACatalogueEntryNamedTwiceIsReported()
    {
        var problems = Problems("""
            { "kind": "catalogue", "id": "stuff", "name": "Stuff",
              "entries": [ { "id": "a", "name": "a" }, { "id": "a", "name": "another a" } ] }
            """);

        Assert.Contains(problems, problem => problem.Message.Contains("already an entry"));
    }
}
