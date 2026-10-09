using System.Text.Json.Nodes;
using Ironbound.Rules.Abilities;
using Ironbound.Rules.Classes;
using Ironbound.Rules.Combat;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Dice;
using Ironbound.Rules.Encounters;
using Ironbound.Rules.Items;
using Ironbound.Rules.Persistence;
using Ironbound.Rules.Skills;

namespace Ironbound.Rules.Tests.Persistence;

/// <summary>What the weapons catalogue added to a save, and what an older save is given instead.</summary>
public class WeaponSaveTests
{
    private static Encounter Of(params string[] ids) =>
        new([.. ids.Select(id => TestContent.Library.BuildCreature(id)!)], new SequenceRandom(true, 10));

    private static Creature Back(Encounter encounter, string name, string? json = null) =>
        GameSave.Restore(GameSave.FromJson(json ?? GameSave.ToJson(GameSave.Capture(encounter))), TestContent.Library)
            .Order.Single(combatant => combatant.Creature.Name == name).Creature;

    private static Creature In(Encounter encounter, string name) =>
        encounter.Order.Single(combatant => combatant.Creature.Name == name).Creature;

    [Fact]
    public void ABrokenWeaponIsStillBroken()
    {
        var encounter = Of("orc-sentry");
        var sentry = In(encounter, "Orc Sentry");
        sentry.Equipment.Break(sentry.MeleeAttack!);

        var back = Back(encounter, "Orc Sentry");

        Assert.True(back.Equipment.IsBroken(TestContent.Library.GetItem("dagger")!));
        Assert.Equal(CriticalProfile.Standard, Strike.CriticalFor(back, back.Attacks[0]));
    }

    [Fact]
    public void AThrownDaggerIsStillOnTheFloor()
    {
        var encounter = Of("orc-sentry");
        var sentry = In(encounter, "Orc Sentry");
        sentry.Equipment.LetGo(sentry.Attacks.Single(attack => attack.IsThrownUse));

        var back = Back(encounter, "Orc Sentry");

        Assert.True(back.Equipment.IsOutOfHand(TestContent.Library.GetItem("dagger")!));
        Assert.Null(back.MeleeAttack);
    }

    [Fact]
    public void BothHalvesOfADaggerFindTheirItemAgain()
    {
        var back = Back(Of("orc-sentry"), "Orc Sentry");
        var thrown = back.Attacks.Single(attack => attack.IsThrownUse);

        Assert.Equal(TestContent.Library.GetItem("dagger"), back.Equipment.ItemFor(thrown));
        Assert.Equal(TestContent.Library.GetItem("dagger"), back.Equipment.ItemFor(back.Attacks.Single(attack => !attack.IsThrownUse)));

        Strike.Resolve(back, thrown, TestContent.Library.BuildCreature("goblin")!, new SequenceRandom(2));
        Assert.True(back.Equipment.IsOutOfHand(TestContent.Library.GetItem("dagger")!));
    }

    [Fact]
    public void WhatTheCatalogueSaysComesBackFromTheCatalogue()
    {
        var back = Back(Of("karn", "orc"), "Karn");
        var axe = back.MeleeAttack!;

        Assert.Equal(WeaponCategory.Martial, axe.Category);
        Assert.Equal(WeaponHands.TwoHanded, axe.Hands);
        Assert.True(back.Equipment.ShieldSetAside);
        Assert.Equal(17, back.ArmorClass.Total);
    }

    [Fact]
    public void ADaggerStillCutsEitherWay()
    {
        var back = Back(Of("orc-sentry"), "Orc Sentry");

        Assert.Equal(DamageRule.Either, back.Attacks[0].DamageRule);
        Assert.Equal([DamageType.Piercing, DamageType.Slashing], back.Attacks[0].DamageTypes);
    }

    [Fact]
    public void ASapIsStillNonlethal()
    {
        var sapper = TestContent.Library.BuildCreature("pip")!;
        sapper.Attacks.Add(TestContent.Library.BuildWeapon("sap")!);
        var encounter = new Encounter([sapper], new SequenceRandom(true, 10));

        var back = Back(encounter, "Pip");

        Assert.True(back.Attacks.Single(attack => attack.Kind == "sap").Damage.Components[0].Nonlethal);
    }

    [Fact]
    public void SylwenIsStillAnElfAndHerSensesAreNoKeener()
    {
        var encounter = Of("sylwen");
        var perception = In(encounter, "Sylwen").Skills.Total(Skill.Perception);

        var back = Back(encounter, "Sylwen");

        Assert.Equal("elf", back.Race!.Id);
        Assert.Equal(perception, back.Skills.Total(Skill.Perception));
        Assert.True(Proficiency.IsProficient(back, back.PrimaryAttack!));
    }

    [Fact]
    public void AMonsterStillKnowsItsOwnWeapon()
    {
        var back = Back(Of("ogre"), "Ogre");

        Assert.True(Proficiency.IsProficient(back, back.MeleeAttack!));
        Assert.True(Proficiency.IsProficient(back, TestContent.Library.GetItem("hide-armour")!));
    }

    [Fact]
    public void AThirteenGetsItsRaceFromTheFileAndNothingBrokenOrDropped()
    {
        var encounter = Of("sylwen");
        var perception = In(encounter, "Sylwen").Skills.Total(Skill.Perception);
        var json = JsonNode.Parse(GameSave.ToJson(GameSave.Capture(encounter)))!.AsObject();

        // What a thirteen looked like: no race, no item state, and no racial skill bonus either,
        // because there were no races to give one.
        json["Version"] = 13;
        var sylwen = json["Creatures"]![0]!.AsObject();
        sylwen.Remove("Race");
        foreach (var item in sylwen["Items"]!.AsArray())
        {
            item!.AsObject().Remove("Broken");
            item.AsObject().Remove("OutOfHand");
        }

        foreach (var skill in sylwen["Skills"]!.AsArray())
        {
            var modifiers = skill!["Modifiers"]!.AsArray();
            foreach (var keen in modifiers.Where(modifier => (string?)modifier!["Source"] == "Keen senses").ToList())
            {
                modifiers.Remove(keen);
            }
        }

        var back = Back(encounter, "Sylwen", json.ToJsonString());

        Assert.Equal("elf", back.Race!.Id);
        Assert.Equal(perception, back.Skills.Total(Skill.Perception));
        Assert.All(back.Equipment.Worn, entry => Assert.False(entry.IsBroken || entry.IsOutOfHand));
    }

    [Fact]
    public void ADaggerFromAThirteenLearnsToBeThrown()
    {
        var json = JsonNode.Parse(GameSave.ToJson(GameSave.Capture(Of("orc-sentry"))))!.AsObject();
        json["Version"] = 13;
        var weapons = json["Creatures"]![0]!["Weapons"]!.AsArray();
        foreach (var thrown in weapons.Where(weapon => ((string?)weapon!["Name"])!.EndsWith("(thrown)")).ToList())
        {
            weapons.Remove(thrown);
        }

        var back = Back(Of("orc-sentry"), "Orc Sentry", json.ToJsonString());

        var dagger = back.Attacks.Single(attack => attack.IsThrownUse);
        Assert.Equal("dagger (thrown)", dagger.Name);
        Assert.Equal(TestContent.Library.GetItem("dagger"), back.Equipment.ItemFor(dagger));
    }

    [Fact]
    public void AFourteenWithNoRaceMeansNone()
    {
        var encounter = Of("pip");

        Assert.Null(Back(encounter, "Pip").Race);
    }

    [Fact]
    public void ARaceTheLibraryHasNeverHeardOfIsNotQuietlyDropped()
    {
        var json = JsonNode.Parse(GameSave.ToJson(GameSave.Capture(Of("sylwen"))))!.AsObject();
        json["Creatures"]![0]!["Race"] = "dryad";

        Assert.Throws<InvalidDataException>(() => Back(Of("sylwen"), "Sylwen", json.ToJsonString()));
    }
}
