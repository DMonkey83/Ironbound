using Ironbound.Rules.Classes;
using Ironbound.Rules.Combat;
using Ironbound.Rules.Content;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Dice;
using Ironbound.Rules.Effects;
using Ironbound.Rules.Encounters;
using Ironbound.Rules.Encounters.Actions;
using Ironbound.Rules.Feats;
using Ironbound.Rules.Magic;
using Ironbound.Rules.Maps;
using Ironbound.Rules.Saves;

namespace Ironbound.Rules.Tests.Classes;

public class ChannelEnergyTests
{
    private static Creature Hale() => TestContent.Library.BuildCreature("hale")!;

    [Fact]
    public void HaleChannelsPositiveEnergyFourTimesADay()
    {
        var hale = Hale();
        var channel = hale.Powers.First();

        Assert.Equal(ClassPowers.ChannelPool, channel.Id);
        Assert.Equal("Channel Positive Energy", channel.Name);
        Assert.Equal(ActionCost.Standard, channel.Cost);
        Assert.False(channel.Provokes);
        Assert.Equal(4, hale.UsesPerDay(channel));       // three, and one for Charisma 12
        Assert.Equal(11, channel.DifficultyClass);        // ten, half of one, and Charisma
        Assert.IsType<SelfTarget>(channel.Effect.Target);
        Assert.Equal(30, ((SelfTarget)channel.Effect.Target).RadiusFeet);
    }

    [Fact]
    public void AChannelHealsEveryoneInTheBurstFoesIncluded()
    {
        var hale = Hale();
        var friend = ClassKit.Dummy("Friend");
        var foe = ClassKit.Dummy("Foe");
        var distant = ClassKit.Dummy("Distant");
        var field = ClassKit.Field((hale, 2, 2), (friend, 3, 2), (foe, 4, 2), (distant, 11, 11));
        foreach (var hurt in new[] { hale, friend, foe, distant })
        {
            hurt.HitPoints.Take(5);
        }

        var encounter = ClassKit.Fight([hale, friend], [foe, distant], field, 4);
        var result = Assert.IsType<CastSpellResult>(
            encounter.BeginNextTurn()!.Take(UsePowerAction.Self(hale.Powers.First())));

        // One die, rolled once for the whole burst: everybody within thirty feet takes the same 4.
        Assert.Equal([hale, friend, foe], result.Cast!.Targets.Select(hit => hit.Target));
        Assert.All(result.Cast.Targets, hit => Assert.Equal(4, hit.Healed));
        Assert.Equal(5, distant.HitPoints.Damage);
        Assert.Equal(3, hale.UsesLeft(hale.Powers.First()));
        Assert.Contains("uses Channel Positive Energy", result.Description);
        Assert.Equal(field.SquareOf(hale), result.Cast.Aim.Point);
    }

    [Fact]
    public void ItDoesNotProvoke()
    {
        var hale = Hale();
        var foe = ClassKit.Dummy("Foe");
        var field = ClassKit.Field((hale, 2, 2), (foe, 3, 2));
        var encounter = ClassKit.Fight([hale], [foe], field, 4);

        var result = Assert.IsType<CastSpellResult>(
            encounter.BeginNextTurn()!.Take(UsePowerAction.Self(hale.Powers.First())));

        Assert.Empty(result.Opportunities);
    }

    [Fact]
    public void WhenTheUsesAreGoneItIsRefused()
    {
        var hale = Hale();
        hale.DailyUses.Spend(ClassPowers.ChannelPool, 4);
        var encounter = new Encounter([hale], new SequenceRandom(true, 5));

        Assert.Equal(0, hale.UsesLeft(hale.Powers.First()));
        Assert.False(encounter.BeginNextTurn()!.CanTake(UsePowerAction.Self(hale.Powers.First())));
    }

    [Fact]
    public void SelectiveChannelingLeavesTheEnemyOut()
    {
        var picky = ClassKit.Make(
            "cleric", 1, "\"deity\": \"none\", \"feats\": [\"selective-channeling\"]", [10, 10, 10, 10, 14, 16]);
        var foe = ClassKit.Dummy("Foe");
        var friend = ClassKit.Dummy("Friend");
        var field = ClassKit.Field((picky, 2, 2), (foe, 3, 2), (friend, 2, 3));
        foe.HitPoints.Take(5);
        friend.HitPoints.Take(5);

        var encounter = ClassKit.Fight([picky, friend], [foe], field, 6);
        encounter.BeginNextTurn()!.Take(UsePowerAction.Self(picky.Powers.First()));

        Assert.Equal(5, foe.HitPoints.Damage);
        Assert.Equal(0, friend.HitPoints.Damage);
    }

    [Fact]
    public void SelectiveChannelingWantsCharismaAndAChannel()
    {
        var feat = TestContent.Library.GetFeat("selective-channeling")!;

        Assert.False(feat.AvailableTo(ClassKit.Make("fighter", 3, abilities: [10, 10, 10, 10, 10, 16])));
        Assert.False(feat.AvailableTo(ClassKit.Make("cleric", 3, "\"deity\": \"none\"", [10, 10, 10, 10, 14, 10])));
        Assert.True(feat.AvailableTo(ClassKit.Make("cleric", 3, "\"deity\": \"none\"", [10, 10, 10, 10, 14, 14])));
    }

    [Fact]
    public void AnEvilGodsClericChannelsNegativeEnergyAndCastsInflictions()
    {
        var dark = ClassKit.Make("cleric", 1, "\"deity\": \"urgathoa\", \"domains\": [\"war\"]");

        Assert.Equal(ChannelKind.Negative, ClassPowers.ChannelKindOf(dark));
        Assert.Equal("Channel Negative Energy", dark.Powers.First().Name);
        Assert.Contains(dark.Powers.First().Effect.Does, effect => effect is DealDamage);
        Assert.Empty(dark.Spells.Spontaneous);   // no inflict spells in the game yet
    }

    [Fact]
    public void ANegativeChannelHurtsTheLivingWithAWillSaveForHalf()
    {
        var dark = ClassKit.Make("cleric", 3, "\"deity\": \"none\", \"channel\": \"Negative\"");
        var victim = ClassKit.Dummy("Victim");
        var field = ClassKit.Field((dark, 2, 2), (victim, 3, 2));
        var encounter = ClassKit.Fight([dark], [victim], field, 6, 6, 1);

        var result = Assert.IsType<CastSpellResult>(
            encounter.BeginNextTurn()!.Take(UsePowerAction.Self(dark.Powers.First())));

        // Two dice of six, and a natural 1 on the save. She leaves herself out of it: the rule
        // lets her choose, and nobody chooses to be hurt.
        Assert.DoesNotContain(result.Cast!.Targets, target => target.Target == dark);
        var hit = result.Cast.Targets.Single(target => target.Target == victim);
        Assert.Equal(12, hit.Damage);
        Assert.False(hit.Save!.Succeeded);
    }

    [Fact]
    public void ANeutralGodsClericChoosesAndAGoodOnesDoesNot()
    {
        Assert.Equal(ChannelKind.Negative,
            ClassPowers.ChannelKindOf(ClassKit.Make("cleric", 1, "\"deity\": \"pharasma\", \"channel\": \"Negative\"")));
        Assert.Equal(ChannelKind.Positive,
            ClassPowers.ChannelKindOf(ClassKit.Make("cleric", 1, "\"deity\": \"pharasma\"")));
        Assert.Equal(ChannelKind.Positive,
            ClassPowers.ChannelKindOf(ClassKit.Make("cleric", 1, "\"deity\": \"cihua-couatl\", \"channel\": \"Negative\"")));
    }
}

public class DomainTests
{
    private static Creature Hale() => TestContent.Library.BuildCreature("hale")!;

    [Fact]
    public void HaleHasHerGodAndBothDomains()
    {
        var hale = Hale();

        Assert.Equal("cihua-couatl", hale.Choices.Deity!.Id);
        Assert.Equal(["healing", "war"], hale.Choices.Domains.Select(domain => domain.Id));
        Assert.Equal(
            ["channel-energy", "rebuke-death", "battle-rage"],
            hale.Powers.Select(power => power.Id));
    }

    [Fact]
    public void TheDomainSlotHoldsOnlyADomainSpell()
    {
        var hale = Hale();
        var bless = TestContent.Library.GetSpell("bless")!;
        var cure = TestContent.Library.GetSpell("cure-light-wounds")!;

        Assert.False(hale.Spells.IsSpecialty(bless));
        Assert.True(hale.Spells.IsSpecialty(cure));

        // Bless twice empties the general slots; the domain slot is still there for the cure.
        Assert.True(hale.Spells.Spend(bless));
        Assert.True(hale.Spells.Spend(bless));
        Assert.False(hale.Spells.CanCast(bless));
        Assert.True(hale.Spells.CanCast(cure));
        Assert.True(hale.Spells.Spend(cure));
        Assert.Equal(0, hale.Spells.SpecialtyRemaining(1));
    }

    [Fact]
    public void ADomainSpellUsesTheDomainSlotFirst()
    {
        var hale = Hale();

        hale.Spells.Spend(TestContent.Library.GetSpell("cure-light-wounds")!);

        Assert.Equal(0, hale.Spells.SpecialtyRemaining(1));
        Assert.Equal(2, hale.Spells.SlotsRemaining(1));
    }

    [Fact]
    public void ARestRefillsTheDomainSlotToo()
    {
        var hale = Hale();
        hale.Spells.Spend(TestContent.Library.GetSpell("cure-light-wounds")!);

        hale.Spells.Rest();

        Assert.Equal(1, hale.Spells.SpecialtyRemaining(1));
    }

    [Fact]
    public void AGoodClericCastsAnyCureFromAGeneralSlot()
    {
        var cleric = ClassKit.Make("cleric", 3, "\"deity\": \"cihua-couatl\", \"domains\": [\"war\"], \"spells\": [\"bless\"]");
        var moderate = TestContent.Library.GetSpell("cure-moderate-wounds")!;

        Assert.False(cleric.Spells.Knows(moderate));
        Assert.True(cleric.Spells.KnowsSpontaneously(moderate));
        Assert.True(cleric.Spells.CanCast(moderate));

        cleric.Spells.Spend(moderate);

        // Second-level slots: one from the table, one for Wisdom 14. One is gone.
        Assert.Equal(1, cleric.Spells.SlotsRemaining(2));
    }

    [Fact]
    public void ACureCanTakeAHigherSlotButNotALowerOne()
    {
        var cleric = ClassKit.Make("cleric", 3, "\"deity\": \"none\"");
        var light = TestContent.Library.GetSpell("cure-light-wounds")!;
        var serious = TestContent.Library.GetSpell("cure-serious-wounds")!;

        while (cleric.Spells.SlotsRemaining(1) > 0)
        {
            cleric.Spells.Spend(light);
        }

        Assert.True(cleric.Spells.CanCast(light));          // out of a second-level slot now
        Assert.False(cleric.Spells.CanCast(serious));       // third level: no slot that high
    }

    [Fact]
    public void ANonClericHasNoSpontaneousCures()
    {
        var wizard = ClassKit.Make("wizard", 3);

        Assert.Empty(wizard.Spells.Spontaneous);
        Assert.False(wizard.Spells.CanCast(TestContent.Library.GetSpell("cure-light-wounds")!));
    }

    [Fact]
    public void RebukeDeathOnlyReachesTheDying()
    {
        var hale = Hale();
        var friend = ClassKit.Dummy("Friend", hitPoints: 10);
        var field = ClassKit.Field((hale, 2, 2), (friend, 3, 2));
        var encounter = ClassKit.Fight([hale, friend], [], field, 3);
        var rebuke = hale.Powers.Single(power => power.Id == "rebuke-death");
        var turn = encounter.BeginNextTurn()!;

        friend.HitPoints.Take(5);
        Assert.False(turn.CanTake(UsePowerAction.At(rebuke, friend)));

        friend.HitPoints.Take(8);   // minus three
        Assert.True(turn.CanTake(UsePowerAction.At(rebuke, friend)));

        var result = Assert.IsType<CastSpellResult>(turn.Take(UsePowerAction.At(rebuke, friend)));

        // 1d4, plus half of first level: a 3, which leaves her at nought and stable.
        Assert.Equal(3, result.Cast!.Targets.Single().Healed);
        Assert.Equal(0, friend.HitPoints.Current);

        // Three and Wisdom 15's two: five a day, one spent.
        Assert.Equal(5, hale.UsesPerDay(rebuke));
        Assert.Equal(4, hale.UsesLeft(rebuke));
    }

    [Fact]
    public void HealingThatLeavesSomebodyBelowNoughtStillStopsTheBleeding()
    {
        var hale = Hale();
        var friend = ClassKit.Dummy("Friend", hitPoints: 10);
        var field = ClassKit.Field((hale, 2, 2), (friend, 3, 2));
        var encounter = ClassKit.Fight([hale, friend], [], field, 1);
        var rebuke = hale.Powers.Single(power => power.Id == "rebuke-death");
        friend.HitPoints.Take(15);   // minus five

        encounter.BeginNextTurn()!.Take(UsePowerAction.At(rebuke, friend));

        Assert.Equal(-4, friend.HitPoints.Current);
        Assert.True(Bleeding.IsStable(friend));
    }

    [Fact]
    public void BattleRageIsDamageForARound()
    {
        var hale = Hale();
        var friend = ClassKit.Make("fighter", 1, "\"items\": [\"longsword\"]");
        var field = ClassKit.Field((hale, 2, 2), (friend, 3, 2));
        var encounter = ClassKit.Fight([hale, friend], [], field);
        var battleRage = hale.Powers.Single(power => power.Id == "battle-rage");
        var before = Strike.DamageBonus(friend, friend.PrimaryAttack!).Total;

        // The fighter is quicker; Hale's is the second turn.
        encounter.BeginNextTurn();
        var turn = encounter.BeginNextTurn()!;
        Assert.Same(hale, turn.Actor);
        Assert.NotNull(turn.Take(UsePowerAction.At(battleRage, friend)));

        Assert.Equal(before + 1, Strike.DamageBonus(friend, friend.PrimaryAttack!).Total);
        encounter.Advance(Duration.Rounds(1));
        Assert.Equal(before, Strike.DamageBonus(friend, friend.PrimaryAttack!).Total);
    }

    [Fact]
    public void HealersBlessingEmpowersCuresFromSixthLevel()
    {
        var healer = ClassKit.Make("cleric", 6, "\"deity\": \"cihua-couatl\", \"domains\": [\"healing\", \"war\"]");
        var novice = ClassKit.Make("cleric", 5, "\"deity\": \"cihua-couatl\", \"domains\": [\"healing\", \"war\"]");
        var patient = ClassKit.Dummy("Patient", hitPoints: 100);
        var cure = TestContent.Library.GetSpell("cure-light-wounds")!;
        healer.Allegiance = novice.Allegiance = patient.Allegiance = 1;

        patient.HitPoints.Take(50);
        var blessed = Casting.Resolve(healer, cure, SpellAim.At(patient), new SequenceRandom(4)).Targets.Single().Healed;
        var plain = Casting.Resolve(novice, cure, SpellAim.At(patient), new SequenceRandom(4)).Targets.Single().Healed;

        Assert.Equal(9, plain);         // 1d8+5: a caster level a point, to five
        Assert.Equal(13, blessed);      // half as much again
        Assert.Contains(ClassFeatures.Describe(healer), line => line.Name == "Healer's Blessing");
    }

    [Fact]
    public void WeaponMasterLendsTheChosenFeatForARound()
    {
        var master = ClassKit.Make(
            "cleric", 8, "\"deity\": \"cihua-couatl\", \"domains\": [\"war\", \"healing\"], \"weaponMaster\": \"dodge\"");
        var power = master.Powers.Single(power => power.Id == "weapon-master");
        var armour = master.ArmorClass.Total;
        var encounter = new Encounter([master], new SequenceRandom(true, 5));

        Assert.Equal(ActionCost.Swift, power.Cost);
        Assert.Equal(8, master.UsesPerDay(power));

        var result = Assert.IsType<CastSpellResult>(encounter.BeginNextTurn()!.Take(UsePowerAction.Self(power)));

        Assert.True(master.HasFeat("dodge"));
        Assert.Equal(armour + 1, master.ArmorClass.Total);
        Assert.Contains("gains Dodge for a round", result.Cast!.Targets.Single().Applied);

        encounter.Advance(Duration.Rounds(1));
        Assert.False(master.HasFeat("dodge"));
        Assert.Equal(armour, master.ArmorClass.Total);
    }

    [Fact]
    public void WeaponMasterWaitsForEighthLevel()
    {
        var cleric = ClassKit.Make(
            "cleric", 7, "\"deity\": \"cihua-couatl\", \"domains\": [\"war\", \"healing\"], \"weaponMaster\": \"dodge\"");

        Assert.DoesNotContain(cleric.Powers, power => power.Id == "weapon-master");
    }

    [Fact]
    public void TheWarDomainHasNoSecondLevelSpellYet()
    {
        var war = TestContent.Library.GetDomain("war")!;

        Assert.Equal("magic-weapon", war.SpellAt(1));
        Assert.Null(war.SpellAt(2));
        Assert.Equal("flame-strike", war.SpellAt(5));
        Assert.Null(war.SpellAt(6));
    }
}

public class DeityTests
{
    [Fact]
    public void TheCorePantheonIsShipped()
    {
        Assert.Equal(21, TestContent.Library.DeityIds.Count);

        var cihua = TestContent.Library.GetDeity("cihua-couatl")!;
        Assert.Equal(Alignment.NeutralGood, cihua.Alignment);
        Assert.Equal(["good", "healing", "protection", "strength", "war"], cihua.Domains);
        Assert.Equal("shortspear", cihua.FavoredWeapon);
        Assert.Equal("Cihua Couatl (NG)", cihua.ToString());
    }

    [Fact]
    public void AFavouredWeaponTheGameLacksIsWrittenInWords()
    {
        // Every shipped god's weapon is in the catalogue now, so the words path needs a made-up
        // god with a made-up weapon to stay tested: it loads, and says what it says.
        var library = ClassKit.Library(("sunlord.json", """
            { "kind": "deity", "id": "sunlord", "name": "The Sun Lord", "alignment": "LG",
              "domains": ["good", "war"], "favoredWeapon": "flaming sunblade" }
            """));

        Assert.Empty(library.Problems);
        Assert.Equal("flaming sunblade", library.GetDeity("sunlord")!.FavoredWeapon);
        Assert.Null(library.GetWeapon("flaming sunblade"));
        Assert.Equal(Alignment.LawfulGood, library.GetDeity("sunlord")!.Alignment);
    }

    [Fact]
    public void EveryShippedGodsFavouredWeaponIsAWeaponTheGameHas()
    {
        foreach (var deity in TestContent.Library.Deities)
        {
            Assert.True(TestContent.Library.GetWeapon(deity.FavoredWeapon) is not null, $"{deity.Name}: {deity.FavoredWeapon}");
        }

        Assert.Equal("longbow", TestContent.Library.GetDeity("erastil")!.FavoredWeapon);
        Assert.Equal("unarmed-strike", TestContent.Library.GetDeity("irori")!.FavoredWeapon);
        Assert.Equal("spiked-chain", TestContent.Library.GetDeity("zon-kuthon")!.FavoredWeapon);
    }

    [Fact]
    public void HalesSheetNamesHerGodAndHerWeapon()
    {
        var hale = TestContent.Library.BuildCreature("hale")!;
        var line = ClassFeatures.Describe(hale).First();

        Assert.Equal("Deity", line.Name);
        Assert.Equal("Cihua Couatl (NG), favoured weapon shortspear", line.Detail);
    }

    [Fact]
    public void ADomainHerGodDoesNotGrantIsReported()
    {
        var library = ClassKit.Library(("bad-cleric.json", """
            { "kind": "creature", "id": "bad-cleric", "name": "Bad Cleric",
              "abilities": [10, 10, 10, 10, 14, 10],
              "classes": [ { "class": "cleric", "level": 1 } ],
              "deity": "erastil", "domains": ["healing", "war"] }
            """));

        Assert.Equal(2, library.Problems.Count(problem => problem.Message.Contains("Erastil does not grant")));
    }

    [Fact]
    public void AClericWithoutAGodIsReportedAndOneWithNoneIsNot()
    {
        var godless = ClassKit.Library(("godless.json", """
            { "kind": "creature", "id": "godless", "name": "Godless",
              "abilities": [10, 10, 10, 10, 14, 10],
              "classes": [ { "class": "cleric", "level": 1 } ] }
            """));
        var none = ClassKit.Library(("atheist.json", """
            { "kind": "creature", "id": "atheist", "name": "Atheist",
              "abilities": [10, 10, 10, 10, 14, 10],
              "classes": [ { "class": "cleric", "level": 1 } ],
              "deity": "none", "domains": ["healing", "war"] }
            """));

        Assert.Contains(godless.Problems, problem => problem.Field == "deity");
        Assert.Empty(none.Problems);
    }

    [Fact]
    public void AnUnknownGodOrAMalformedAlignmentIsReported()
    {
        var library = ContentLibrary.Load([
            ("odd.json", """{ "kind": "deity", "id": "odd", "name": "Odd", "alignment": "XX" }"""),
            ("follower.json", """
                { "kind": "creature", "id": "follower", "name": "Follower",
                  "abilities": [10, 10, 10, 10, 10, 10], "deity": "nobody" }
                """),
        ]);

        Assert.Contains(library.Problems, problem => problem.Field == "alignment");
        Assert.Contains(library.Problems, problem => problem.Message.Contains("no deity called 'nobody'"));
    }

    [Fact]
    public void AlignmentsReadAndWriteTheBooksWay()
    {
        Assert.Equal(Alignment.ChaoticEvil, Alignments.Parse("CE"));
        Assert.Equal(Alignment.Neutral, Alignments.Parse("n"));
        Assert.Null(Alignments.Parse("good"));
        Assert.Equal("LN", Alignments.Abbreviate(Alignment.LawfulNeutral));
        Assert.True(Alignments.IsGood(Alignment.ChaoticGood));
        Assert.True(Alignments.IsEvil(Alignment.LawfulEvil));
        Assert.False(Alignments.IsGood(Alignment.Neutral));
    }
}

public class NewSpellTests
{
    private static Creature Caster(int level) =>
        ClassKit.Make("cleric", level, "\"deity\": \"none\"", [10, 10, 10, 10, 16, 10]);

    [Theory]
    [InlineData("cure-moderate-wounds", 3, "2d8+3")]
    [InlineData("cure-moderate-wounds", 15, "2d8+10")]
    [InlineData("cure-serious-wounds", 5, "3d8+5")]
    [InlineData("cure-critical-wounds", 9, "4d8+9")]
    [InlineData("breath-of-life", 9, "5d8+9")]
    public void TheCuresAddTheirLevelToACeiling(string id, int level, string expected)
    {
        var restore = Assert.IsType<Restore>(TestContent.Library.GetSpell(id)!.Does.Single());

        Assert.Equal(expected, restore.Amount.At(level).ToString());
    }

    [Fact]
    public void MagicWeaponIsAnEnhancementThatDoesNotStackWithAMagicSword()
    {
        var fighter = ClassKit.Make("fighter", 1, "\"items\": [\"greatsword-plus-one\"]");
        var cleric = Caster(1);
        cleric.Allegiance = fighter.Allegiance = 1;
        var before = Strike.AttackBonus(fighter, fighter.PrimaryAttack!).Total;

        Casting.Resolve(cleric, TestContent.Library.GetSpell("magic-weapon")!, SpellAim.At(fighter), new SequenceRandom(1));

        Assert.Equal(before, Strike.AttackBonus(fighter, fighter.PrimaryAttack!).Total);
    }

    [Fact]
    public void MagicVestmentGrowsEveryFourLevelsAndIgnoresTouch()
    {
        var target = ClassKit.Dummy("Target");
        var cleric = Caster(8);
        cleric.Allegiance = target.Allegiance = 1;

        Casting.Resolve(cleric, TestContent.Library.GetSpell("magic-vestment")!, SpellAim.At(target), new SequenceRandom(1));

        Assert.Equal(12, target.ArmorClass.Total);
        Assert.Equal(10, target.ArmorClass.Touch);
    }

    [Fact]
    public void DivinePowerIsLuckAndTemporaryHitPoints()
    {
        var cleric = Caster(9);
        var attack = cleric.AttackModifiers.Total;

        var cast = Casting.Resolve(cleric, TestContent.Library.GetSpell("divine-power")!, SpellAim.At(cleric), new SequenceRandom(true, 1));

        Assert.Equal(attack + 3, cleric.AttackModifiers.Total);
        Assert.Equal(9, cleric.HitPoints.Temporary);
        Assert.Contains("gains 9 temporary hit points", cast.Targets.Single().Applied);
    }

    [Fact]
    public void MageArmourIsFourPointsOfArmour()
    {
        var wizard = ClassKit.Make("wizard", 1);
        var armour = wizard.ArmorClass.Total;

        Casting.Resolve(wizard, TestContent.Library.GetSpell("mage-armor")!, SpellAim.At(wizard), new SequenceRandom(1));

        Assert.Equal(armour + 4, wizard.ArmorClass.Total);
        Assert.Equal(Duration.Hours(1), wizard.Effects.Find("Mage Armor")!.Duration);
    }

    [Fact]
    public void FlameStrikeIsABurstOfFire()
    {
        var strike = TestContent.Library.GetSpell("flame-strike")!;

        Assert.Equal(10, Assert.IsType<BurstTarget>(strike.Target).RadiusFeet);
        Assert.Equal(Save.Reflex, strike.Save);
        Assert.Equal(SaveOutcome.Half, strike.OnSave);
    }
}
