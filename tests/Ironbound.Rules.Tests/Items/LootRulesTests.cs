using Ironbound.Rules.Abilities;
using Ironbound.Rules.Combat;
using Ironbound.Rules.Content;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Defense;
using Ironbound.Rules.Encounters.Actions;
using Ironbound.Rules.Items;
using Ironbound.Rules.Maps;
using Ironbound.Rules.Skills;
using Ironbound.Rules.Tests.Classes;
using Xunit.Abstractions;

namespace Ironbound.Rules.Tests.Items;

public class PricingTests
{
    [Fact]
    public void SilverAddsByHowMuchBladeThereIs()
    {
        Assert.Equal(105_00, Pricing.Weapon(15, WeaponHands.OneHanded, DamageBypass.Silver, 0, false));
        Assert.Equal(22_00, Pricing.Weapon(2, WeaponHands.Light, DamageBypass.Silver, 0, false));
        Assert.Equal(230_00, Pricing.Weapon(50, WeaponHands.TwoHanded, DamageBypass.Silver, 0, false));
    }

    [Fact]
    public void ColdIronDoublesTheBaseAndMakesTheFirstEnchantmentDearer()
    {
        Assert.Equal(40_00, Pricing.Weapon(20, WeaponHands.TwoHanded, DamageBypass.ColdIron, 0, false));

        // Thirty for the blade, three hundred for masterwork, two thousand for the +1 and two
        // thousand more for doing it to cold iron.
        Assert.Equal(4330_00, Pricing.Weapon(15, WeaponHands.OneHanded, DamageBypass.ColdIron, 1, false));
    }

    [Fact]
    public void AnEnhancementImpliesMasterworkAndCostsItsSquare()
    {
        Assert.Equal(2350_00, Pricing.Weapon(50, WeaponHands.TwoHanded, DamageBypass.None, 1, false));
        Assert.Equal(8315_00, Pricing.Weapon(15, WeaponHands.OneHanded, DamageBypass.None, 2, false));
        Assert.Equal(315_00, Pricing.Weapon(15, WeaponHands.OneHanded, DamageBypass.None, 0, true));
    }

    [Fact]
    public void ArmourIsCheaperToImprove()
    {
        Assert.Equal(250_00, Pricing.Armour(100, 0, true));
        Assert.Equal(1250_00, Pricing.Armour(100, 1, false));
        Assert.Equal(4350_00, Pricing.Armour(200, 2, false));
    }

    [Theory]
    [InlineData(105_00, "105 gp")]
    [InlineData(2_05, "2 gp 5 cp")]
    [InlineData(15, "1 sp 5 cp")]
    [InlineData(2350_00, "2,350 gp")]
    [InlineData(0, "nothing")]
    public void APriceReadsAsAShopWouldWriteIt(int copper, string written) =>
        Assert.Equal(written, Pricing.Format(copper));

    [Fact]
    public void TheShippedItemsArePricedFromWhatTheyAre()
    {
        var library = TestContent.Library;

        Assert.Equal(105_00, library.GetItem("silvered-longsword")!.Price);
        Assert.Equal(2350_00, library.GetItem("greatsword-plus-one")!.Price);
        Assert.Equal(40_00, library.GetItem("cold-iron-greataxe")!.Price);
        Assert.Equal(15_00, library.GetItem("longsword")!.Price);
        Assert.Equal(2000_00, library.GetItem("ring-of-protection")!.Price);
        Assert.Equal(50_00, library.GetItem("bloodstone")!.Price);
    }

    [Fact]
    public void AnItemSaysWhatItCostsAndWeighs()
    {
        var library = TestContent.Library;

        Assert.Contains("105 gp · 4 lb", library.DescribeItem(library.GetItem("silvered-longsword")!));
        Assert.Contains("50 gp · 30 lb", library.DescribeItem(library.GetItem("scale-mail")!));
        Assert.Contains("50 gp · weighs next to nothing", library.DescribeItem(library.GetItem("bloodstone")!));
    }
}

public class ItemKindTests
{
    [Theory]
    [InlineData("longsword", ItemKind.Weapon)]
    [InlineData("bite", ItemKind.Natural)]
    [InlineData("chain-shirt", ItemKind.Armour)]
    [InlineData("light-shield", ItemKind.Shield)]
    [InlineData("ring-of-protection", ItemKind.Wondrous)]
    [InlineData("bloodstone", ItemKind.Valuable)]
    public void AnItemIsWhatItIs(string id, ItemKind kind) =>
        Assert.Equal(kind, TestContent.Library.GetItem(id)!.Kind);

    [Theory]
    [InlineData("dagger", true)]
    [InlineData("studded-leather", true)]
    [InlineData("bloodstone", true)]
    [InlineData("greatsword-plus-one", false)]
    [InlineData("ring-of-protection", false)]
    [InlineData("bite", false)]
    public void PlainGearAndValuablesStack(string id, bool stacks) =>
        Assert.Equal(stacks, TestContent.Library.GetItem(id)!.Stackable);

    [Fact]
    public void AWeaponWeighsWhatItsWeaponDoes()
    {
        var library = TestContent.Library;

        Assert.Equal(4m, library.GetItem("silvered-longsword")!.Weight);
        Assert.Equal(8m, library.GetItem("greatsword-plus-one")!.Weight);
        Assert.Equal(0m, library.GetItem("bite")!.Weight);
    }

    [Fact]
    public void AWeaponFileThatComesAfterItsItemStillPricesIt()
    {
        var library = ContentLibrary.Load([
            ("items/a.json", """{ "kind": "item", "id": "blade", "name": "blade", "slot": "MainHand", "weapon": "zz-blade", "qualities": ["Silver"] }"""),
            ("weapons/zz.json", """{ "kind": "weapon", "id": "zz-blade", "name": "blade", "category": "martial", "hands": "one-handed", "damageMedium": "1d8", "cost": 15, "weight": 4 }"""),
        ]);

        Assert.Empty(library.Problems);
        Assert.Equal(105_00, library.GetItem("blade")!.Price);
        Assert.Equal(ItemKind.Weapon, library.GetItem("blade")!.Kind);
    }

    /// <summary>
    /// The armour table's cost and weight for each shipped piece, by its row (the numbers are Open
    /// Game Content). The shields are the steel ones.
    /// </summary>
    public static TheoryData<string, string, int, int> Armour => new()
    {
        { "leather-armour", "Leather", 10, 15 },
        { "studded-leather", "Studded leather", 25, 20 },
        { "chain-shirt", "Chain shirt", 100, 25 },
        { "hide-armour", "Hide", 15, 25 },
        { "scale-mail", "Scale mail", 50, 30 },
        { "breastplate", "Breastplate", 200, 30 },
        { "light-shield", "Light steel", 9, 6 },
        { "heavy-shield", "Heavy steel", 20, 15 },
    };

    [Theory]
    [MemberData(nameof(Armour))]
    public void ArmourWeighsAndCostsWhatTheTableSays(string id, string row, int gold, int pounds)
    {
        var item = TestContent.Library.GetItem(id)!;

        Assert.Equal(gold * 100, item.Price);
        Assert.Equal(pounds, item.Weight);

        // And the table itself, where the reference files are to hand: they are not in the
        // repository, so a fresh clone checks the numbers above and nothing more.
        if (ReferenceTable() is { } table)
        {
            var columns = table.Single(line => line.StartsWith(row + "|", StringComparison.Ordinal)).Split('|');
            Assert.Equal($"{gold} gp", columns[1]);
            Assert.StartsWith($"{pounds} lb", columns[^1]);
        }
    }

    private static string[]? ReferenceTable()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var path = Path.Combine(directory.FullName, "reference", "pf1e", "armor.psv");
            if (File.Exists(path))
            {
                return File.ReadAllLines(path);
            }
        }

        return null;
    }
}

public class CarryingCapacityTests
{
    [Theory]
    [InlineData(1, 3, 6, 10)]
    [InlineData(8, 26, 53, 80)]
    [InlineData(10, 33, 66, 100)]
    [InlineData(16, 76, 153, 230)]
    [InlineData(18, 100, 200, 300)]
    [InlineData(29, 466, 933, 1400)]
    [InlineData(30, 532, 1064, 1600)]
    [InlineData(40, 2128, 4256, 6400)]
    public void ByStrength(int strength, int light, int medium, int heavy) =>
        Assert.Equal(new CarryingCapacity(light, medium, heavy), Encumbrance.Capacity(strength));

    [Fact]
    public void NoStrengthCarriesNothing() =>
        Assert.Equal(CarryingCapacity.None, Encumbrance.Capacity(0));

    [Theory]
    [InlineData(CreatureSize.Small, false, 24.75, 49.5, 75)]
    [InlineData(CreatureSize.Large, false, 66, 132, 200)]
    [InlineData(CreatureSize.Medium, true, 49.5, 99, 150)]
    [InlineData(CreatureSize.Small, true, 33, 66, 100)]
    [InlineData(CreatureSize.Large, true, 99, 198, 300)]
    [InlineData(CreatureSize.Huge, true, 198, 396, 600)]
    public void BySizeAndLegs(CreatureSize size, bool quadruped, double light, double medium, double heavy) =>
        Assert.Equal(
            new CarryingCapacity((decimal)light, (decimal)medium, (decimal)heavy),
            Encumbrance.Capacity(10, size, quadruped));

    [Fact]
    public void TheDireRatIsOnFourLegs()
    {
        var rat = TestContent.Library.BuildCreature("dire-rat")!;

        Assert.True(rat.Quadruped);
        Assert.Equal(33m, Encumbrance.Capacity(rat).Light);
    }

    [Fact]
    public void ArmourMadeForAHalflingWeighsHalf()
    {
        var leather = TestContent.Library.GetItem("leather-armour")!;
        var ring = TestContent.Library.GetItem("bloodstone")!;

        Assert.Equal(7.5m, Encumbrance.WornWeight(leather, CreatureSize.Small));
        Assert.Equal(30m, Encumbrance.WornWeight(leather, CreatureSize.Large));
        Assert.Equal(ring.Weight, Encumbrance.WornWeight(ring, CreatureSize.Small));
    }

    [Fact]
    public void AFighterInScaleCarriesALightLoad()
    {
        var aldric = TestContent.Library.BuildCreature("aldric")!;
        var pip = TestContent.Library.BuildCreature("pip")!;

        Assert.Equal(new LoadReport(38m, new CarryingCapacity(76, 153, 230), LoadCategory.Light), Encumbrance.Load(aldric));
        Assert.Equal(8.5m, Encumbrance.Load(pip).Weight);
    }

    [Fact]
    public void TooMuchOnOnesOwnBackIsTooMuch()
    {
        var sylwen = TestContent.Library.BuildCreature("sylwen")!;
        TestContent.Library.Equip(sylwen, TestContent.Library.GetItem("breastplate")!, EquipmentSlot.Carried);

        // Strength 8: twenty-six pounds light, fifty-three medium. Thirty-three is medium.
        Assert.Equal(LoadCategory.Medium, Encumbrance.Load(sylwen).Category);
        Assert.Equal(LoadCategory.Medium, Encumbrance.Effective(sylwen));
    }
}

public class LoadEffectTests
{
    private static Creature Nimble()
    {
        var nimble = new Creature("Nimble", new AbilityScores(10, 18, 10, 10, 10, 10), 10, 1);
        return nimble;
    }

    [Fact]
    public void AMediumLoadCapsDexterityAtThreeAndSaysSo()
    {
        var nimble = Nimble();
        var before = nimble.ArmorClass.Total;

        nimble.SharedLoad = () => LoadCategory.Medium;

        Assert.Equal(before - 1, nimble.ArmorClass.Total);
        Assert.Equal(3, nimble.ArmorClass.MaxDexterityBonus);
        Assert.Contains("Dexterity (medium load: max Dex +3)", nimble.ArmorClass.Explain().ToString());
    }

    [Fact]
    public void AHeavyOneAtOne()
    {
        var nimble = Nimble();
        nimble.SharedLoad = () => LoadCategory.Heavy;

        Assert.Equal(1, nimble.ArmorClass.MaxDexterityBonus);
    }

    [Fact]
    public void ArmourAndLoadDoNotStackTheWorseCounts()
    {
        var library = TestContent.Library;
        var karn = library.BuildCreature("karn")!;
        var climb = karn.Skills.Total(Skill.Climb);

        // A breastplate's and a light shield's −5 is worse than a medium load's −3, so nothing
        // changes...
        karn.SharedLoad = () => LoadCategory.Medium;
        Assert.Equal(climb, karn.Skills.Total(Skill.Climb));
        Assert.Contains("Armour check penalty", karn.Skills.Explain(Skill.Climb).ToString());

        // ...and a heavy load's −6 is worse than the armour's, so it replaces it.
        karn.SharedLoad = () => LoadCategory.Heavy;
        Assert.Equal(climb - 1, karn.Skills.Total(Skill.Climb));
        Assert.Contains("Heavy load check penalty", karn.Skills.Explain(Skill.Climb).ToString());
        Assert.DoesNotContain("Armour check penalty", karn.Skills.Explain(Skill.Climb).ToString());
    }

    [Fact]
    public void SlowedIsSlowedByArmourOrWeightOrBoth()
    {
        var valeria = TestContent.Library.BuildCreature("valeria")!;
        Assert.Equal(30, valeria.CurrentSpeed);

        valeria.SharedLoad = () => LoadCategory.Medium;
        Assert.Equal(20, valeria.CurrentSpeed);

        // Karn's forty in a breastplate is thirty, and a medium load on top changes nothing;
        // a heavy one takes his fast movement too.
        var karn = TestContent.Library.BuildCreature("karn")!;
        Assert.Equal(30, karn.CurrentSpeed);
        karn.SharedLoad = () => LoadCategory.Medium;
        Assert.Equal(30, karn.CurrentSpeed);
        karn.SharedLoad = () => LoadCategory.Heavy;
        Assert.Equal(20, karn.CurrentSpeed);
    }

    [Fact]
    public void AHeavyLoadRunsThreeTimesSpeed()
    {
        var valeria = TestContent.Library.BuildCreature("valeria")!;
        Assert.Equal(4, RunAction.Multiple(valeria));

        valeria.SharedLoad = () => LoadCategory.Heavy;
        Assert.Equal(3, RunAction.Multiple(valeria));
    }

    private static IReadOnlyList<GridSquare> Line(int from, int to) =>
        [.. Enumerable.Range(from, to - from + 1).Select(x => new GridSquare(x, 1))];

    [Fact]
    public void OverloadedIsFiveFeetForTheWholeRound()
    {
        var mover = ClassKit.Make("fighter", 1);
        mover.SharedLoad = () => LoadCategory.Overloaded;
        var field = new Battlefield(20, 4);
        field.Place(mover, 0, 1);
        var encounter = ClassKit.Fight([mover], [], field);
        var turn = encounter.BeginNextTurn()!;

        Assert.Equal(5, mover.CurrentSpeed);
        Assert.False(turn.CanTake(new MoveAction(Line(0, 2))));
        Assert.False(turn.CanTake(new RunAction(Line(0, 1))));
        Assert.False(turn.CanTake(FiveFootStepAction.To(new GridSquare(0, 1), new GridSquare(1, 1))));

        Assert.NotNull(turn.Take(new MoveAction(Line(0, 1))));

        // The stagger was the whole round: nothing is left to act with.
        Assert.False(turn.Budget.HasStandard);
        Assert.False(turn.Budget.HasMove);
    }
}

public class LootContentTests
{
    [Fact]
    public void ACreaturesPurseIsDiceByCoin()
    {
        var orc = TestContent.Library.GetCreature("orc")!;

        Assert.Equal([(Coin.Silver, "2d6"), (Coin.Copper, "3d10")], orc.Purse.Coins);
        Assert.Equal(TreasureKind.NpcGear, orc.Treasure);
        Assert.Equal(TreasureKind.None, TestContent.Library.GetCreature("dire-rat")!.Treasure);
    }

    [Fact]
    public void APurseThatIsNotDiceIsReported()
    {
        var library = ContentLibrary.Load(TestContent.Files().Append(("bad.json", """
            { "kind": "creature", "id": "miser", "name": "Miser", "cr": 1,
              "abilities": [10, 10, 10, 10, 10, 10], "purse": { "gp": "lots", "xp": "1d4" }, "treasure": "heaps" }
            """)));

        Assert.Contains(library.Problems, problem => problem.Field == "purse" && problem.Message.Contains("'lots'"));
        Assert.Contains(library.Problems, problem => problem.Field == "purse" && problem.Message.Contains("'xp'"));
        Assert.Contains(library.Problems, problem => problem.Field == "treasure");
    }

    [Fact]
    public void AContainerReadsItsLookCoinsLockAndHiding()
    {
        var caves = TestContent.Library.GetLevel("caves-of-shadow")!;

        var box = caves.GetFeature("gorrums-strongbox")!;
        Assert.Equal(FeatureKind.Container, box.Kind);
        Assert.Equal(ContainerLook.Strongbox, box.Look);
        Assert.True(box.Locked);
        Assert.Equal(20, box.LockDc);
        Assert.Equal(23, box.BreakDc);
        Assert.Contains(new LootDefinition("bloodstone", 2), box.Loot);

        var sack = caves.GetFeature("storeroom-sack")!;
        Assert.Equal(new Money(0, 0, 400, 600), sack.Coins);
        Assert.Equal(20m, sack.Coins.Weight);
        Assert.False(sack.Locked);

        Assert.Equal(15, caves.GetFeature("junction-niche")!.HiddenDc);
        Assert.Null(caves.GetFeature("tobins-cart")!.HiddenDc);
    }

    [Fact]
    public void ADoorCannotHoldAnything()
    {
        var library = ContentLibrary.Load(TestContent.Files().Append(("level.json", """
            { "kind": "level", "id": "box", "name": "Box",
              "map": [ "#+#", "#.#", "###" ],
              "start": [ { "creature": "pip", "x": 1, "y": 1 } ],
              "areas": [ { "id": "room", "name": "Room", "x": 1, "y": 1, "foes": [ { "creature": "goblin", "x": 1, "y": 1, "name": "G" } ], "final": true } ],
              "features": [ { "id": "door", "kind": "door", "name": "door", "squares": [ { "x": 1, "y": 0 } ], "loot": ["dagger"] } ] }
            """)));

        Assert.Contains(library.Problems, problem => problem.Field == "features.door.loot");
    }

    [Fact]
    public void NobodyLeavesTeethLyingAboutAsLoot()
    {
        var library = ContentLibrary.Load(TestContent.Files().Append(("level.json", """
            { "kind": "level", "id": "box", "name": "Box",
              "map": [ "###", "#.#", "###" ],
              "start": [ { "creature": "pip", "x": 1, "y": 1 } ],
              "areas": [ { "id": "room", "name": "Room", "x": 1, "y": 1, "loot": ["bite"],
                "foes": [ { "creature": "goblin", "x": 1, "y": 1, "name": "G" } ], "final": true } ] }
            """)));

        Assert.Contains(library.Problems, problem => problem.Field == "areas.room.loot" && problem.Message.Contains("natural"));
    }

    [Fact]
    public void AContainerInSolidRockIsStillReported()
    {
        var library = ContentLibrary.Load(TestContent.Files().Append(("level.json", """
            { "kind": "level", "id": "box", "name": "Box",
              "map": [ "#####", "#.###", "#####", "#####" ],
              "start": [ { "creature": "pip", "x": 1, "y": 1 } ],
              "areas": [ { "id": "room", "name": "Room", "x": 1, "y": 1,
                "foes": [ { "creature": "goblin", "x": 1, "y": 1, "name": "G" } ], "final": true } ],
              "features": [ { "id": "deep", "kind": "container", "name": "deep", "squares": [ { "x": 3, "y": 3 } ] } ] }
            """)));

        Assert.Contains(library.Problems, problem => problem.Field == "features.deep");
    }
}

public class TreasureYardstickTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(new[] { 1, 1, 1, 1 }, 1)]
    [InlineData(new[] { 6, 6, 5 }, 5)]
    [InlineData(new[] { 3, 3, 3, 3, 3, 3 }, 4)]
    [InlineData(new[] { 1 }, 1)]
    public void TheAveragePartyLevelIsTheBooks(int[] levels, int expected) =>
        Assert.Equal(expected, Treasure.AveragePartyLevel(levels));

    [Fact]
    public void TheTableIsTheMediumTrack()
    {
        Assert.Equal(260_00, Treasure.PerEncounter(1));
        Assert.Equal(1550_00, Treasure.PerEncounter(5));
        Assert.Equal(67000_00, Treasure.PerEncounter(20));
    }

    [Theory]
    [InlineData("caves-of-shadow", 1, 4)]
    [InlineData("the-long-road", 5, 2)]
    public void EachLevelIsMeasuredAndTheMeasureIsOnlyInformation(string id, int partyLevel, int fights)
    {
        var library = TestContent.Library;
        var report = library.TreasureOf(library.GetLevel(id)!);

        foreach (var line in report.Lines())
        {
            output.WriteLine(line);
        }

        Assert.Equal(partyLevel, report.PartyLevel);
        Assert.Equal(fights * Treasure.PerEncounter(partyLevel), report.Yardstick);
        Assert.True(report.Placed > 0);

        // Whatever it says, it is not a problem.
        Assert.Empty(library.Problems);
    }

    [Fact]
    public void GorrumsDenIsAboutTheYardstick()
    {
        var library = TestContent.Library;
        var den = library.TreasureOf(library.GetLevel("caves-of-shadow")!).Areas.Single(area => area.AreaId == "ogre-den");

        Assert.InRange(den.Placed, den.Yardstick * 9 / 10, den.Yardstick * 13 / 10);
    }
}
