using System.Text.Json.Nodes;
using Ironbound.Rules.Abilities;
using Ironbound.Rules.Classes;
using Ironbound.Rules.Combat;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Dice;
using Ironbound.Rules.Effects;
using Ironbound.Rules.Encounters;
using Ironbound.Rules.Persistence;

namespace Ironbound.Rules.Tests.Classes;

public class ClassSaveTests
{
    private static Encounter Party(out Creature hale, out Creature karn, out Creature merrin, out Creature valeria)
    {
        hale = TestContent.Library.BuildCreature("hale")!;
        karn = TestContent.Library.BuildCreature("karn")!;
        merrin = TestContent.Library.BuildCreature("merrin")!;
        valeria = TestContent.Library.BuildCreature("valeria")!;

        return new Encounter([hale, karn, merrin, valeria], new SequenceRandom(true, 10));
    }

    private static Creature Back(Encounter encounter, string name, string? json = null) =>
        GameSave.Restore(GameSave.FromJson(json ?? GameSave.ToJson(GameSave.Capture(encounter))), TestContent.Library)
            .Order.Single(combatant => combatant.Creature.Name == name).Creature;

    [Fact]
    public void ThisIsVersionSeventeenAndElevenIsStillRead()
    {
        // Fifteen since the feats: the stacks every skill check and every ability check share,
        // and the combatant state the movement feats leave between turns. Sixteen since the
        // party's bag and the level's containers took the place of the sack. Seventeen since the
        // merchants, whose stock is saved.
        Assert.Equal(17, SavedGame.CurrentVersion);
        Assert.Equal(11, SavedGame.OldestReadable);
    }

    [Fact]
    public void ChoicesComeBackAsTheyWere()
    {
        var encounter = Party(out _, out _, out _, out _);

        var hale = Back(encounter, "Hale");
        var karn = Back(encounter, "Karn");
        var merrin = Back(encounter, "Merrin");
        var valeria = Back(encounter, "Valeria");

        Assert.Equal("cihua-couatl", hale.Choices.Deity!.Id);
        Assert.Equal(["healing", "war"], hale.Choices.Domains.Select(domain => domain.Id));
        Assert.Equal(["powerful-blow", "surprise-accuracy", "strength-surge"], karn.Choices.Talents.Select(talent => talent.Id));
        Assert.Equal("evocation", merrin.Choices.School!.Id);
        Assert.True(merrin.Choices.BondedObject);
        Assert.Equal(["heavy-blades"], valeria.Choices.WeaponGroups);
        Assert.Contains(valeria.Feats, feat => feat.Key == "weapon-focus:longsword");
        Assert.Equal("longsword", valeria.PrimaryAttack!.Kind);
        Assert.Equal(["heavy-blades"], valeria.PrimaryAttack.Groups);
    }

    [Fact]
    public void WhatWasSpentStaysSpent()
    {
        var encounter = Party(out var hale, out _, out var merrin, out _);
        hale.DailyUses.Spend(ClassPowers.ChannelPool, 2);
        hale.Spells.Spend(TestContent.Library.GetSpell("cure-light-wounds")!);
        merrin.DailyUses.Spend(ClassPowers.ArcaneBondPool);

        var back = Back(encounter, "Hale");
        var merrinBack = Back(encounter, "Merrin");

        Assert.Equal(2, back.UsesLeft(back.Powers.First()));
        Assert.Equal(0, back.Spells.SpecialtyRemaining(1));
        Assert.Equal(2, back.Spells.SlotsRemaining(1));
        Assert.Equal(1, back.Spells.SpecialtyMaximum(1));
        Assert.Equal(0, merrinBack.UsesLeft(merrinBack.Powers.Single(power => power.Id == "arcane-bond:fireball")));
        Assert.Equal(merrin.Spells.Spellbook.Select(spell => spell.Id), merrinBack.Spells.Spellbook.Select(spell => spell.Id));
        Assert.Equal(["cure-light-wounds", "cure-moderate-wounds", "cure-serious-wounds", "cure-critical-wounds"],
            back.Spells.Spontaneous.Select(spell => spell.Id));
    }

    [Fact]
    public void ARageInProgressSurvivesWithoutBeingDoubled()
    {
        var encounter = Party(out _, out var karn, out _, out _);
        Rage.Start(karn);
        encounter.Advance(Duration.Rounds(2));
        karn.Stances.Adopt(Stance.PowerfulBlow);
        Strike.Resolve(karn, karn.PrimaryAttack!, ClassKit.Dummy(hitPoints: 100), new SequenceRandom(true, 15, 6));
        karn.Stances.Adopt(Stance.SurpriseAccuracy);

        var back = Back(encounter, "Karn");

        Assert.True(back.IsRaging);
        Assert.Equal(karn.RageRoundsLeft, back.RageRoundsLeft);
        Assert.Equal(karn.Abilities[Ability.Strength].Score, back.Abilities[Ability.Strength].Score);
        Assert.Equal(karn.HitPoints.Maximum, back.HitPoints.Maximum);
        Assert.True(back.Stances.IsSpent(Stance.PowerfulBlow));
        Assert.False(back.Stances.CanAdopt(Stance.PowerfulBlow));
        Assert.True(back.Stances.IsActive(Stance.SurpriseAccuracy));

        // And it still ends, with everything it gave taken back exactly once — and the fatigue's
        // two taken off as well.
        Rage.End(back);
        Assert.Equal(TestContent.Library.BuildCreature("karn")!.Abilities[Ability.Strength].Score - 2,
            back.Abilities[Ability.Strength].Score);
        Assert.True(back.Has(Ironbound.Rules.Conditions.Condition.Fatigued));
    }

    [Fact]
    public void ABorrowedFeatComesBackAndStillGoes()
    {
        var master = ClassKit.Make(
            "cleric", 8, "\"deity\": \"cihua-couatl\", \"domains\": [\"war\", \"healing\"], \"weaponMaster\": \"dodge\"");
        var encounter = new Encounter([master], new SequenceRandom(true, 5));
        master.Effects.Apply(new BorrowedFeatEffect(TestContent.Library.GetFeat("dodge")!, Duration.Rounds(1)));
        var armour = master.ArmorClass.Total;

        var restoredEncounter = GameSave.Restore(GameSave.FromJson(GameSave.ToJson(GameSave.Capture(encounter))), TestContent.Library);
        var back = restoredEncounter.Order.Single().Creature;

        Assert.True(back.HasFeat("dodge"));
        Assert.Equal(armour, back.ArmorClass.Total);

        restoredEncounter.Advance(Duration.Rounds(1));
        Assert.False(back.HasFeat("dodge"));
        Assert.Equal(armour - 1, back.ArmorClass.Total);
    }

    [Fact]
    public void ASaveFromBeforeClassFeaturesIsBroughtUpToDate()
    {
        var encounter = Party(out var hale, out _, out _, out _);
        hale.Spells.Spend(TestContent.Library.GetSpell("bless")!);
        var root = JsonNode.Parse(GameSave.ToJson(GameSave.Capture(encounter)))!.AsObject();

        // What a twelve looked like: no class features, no specialty slots or spellbooks, no
        // weapon kinds, and Hale's three first-level slots counted together.
        root["Version"] = 12;
        foreach (var creature in root["Creatures"]!.AsArray().Select(node => node!.AsObject()))
        {
            creature.Remove("Features");
            creature["Spells"]!.AsObject().Remove("Specialty");
            creature["Spells"]!.AsObject().Remove("Spellbook");

            foreach (var weapon in creature["Weapons"]!.AsArray().Select(node => node!.AsObject()))
            {
                weapon.Remove("Kind");
                weapon.Remove("Groups");
                weapon.Remove("Finesse");
            }

            if ((string?)creature["Name"] == "Hale")
            {
                creature["Spells"]!["Slots"] = JsonNode.Parse("""[ { "Level": 1, "Maximum": 3, "Remaining": 2 } ]""");
            }
        }

        var json = root.ToJsonString();
        var oldHale = Back(encounter, "Hale", json);
        var oldKarn = Back(encounter, "Karn", json);
        var oldValeria = Back(encounter, "Valeria", json);
        var oldMerrin = Back(encounter, "Merrin", json);

        // The same three slots, one spent, now dealt the new way.
        Assert.Equal(2, oldHale.Spells.SlotsMaximum(1));
        Assert.Equal(1, oldHale.Spells.SlotsRemaining(1));
        Assert.Equal(1, oldHale.Spells.SpecialtyRemaining(1));

        // Choices from the files; every pool full; nobody raging.
        Assert.Equal(["healing", "war"], oldHale.Choices.Domains.Select(domain => domain.Id));
        Assert.Equal(4, oldHale.UsesLeft(oldHale.Powers.First()));
        Assert.Equal(3, oldKarn.Choices.Talents.Count);
        Assert.Equal(17, oldKarn.RageRoundsLeft);
        Assert.False(oldKarn.IsRaging);
        Assert.Equal("longsword", oldValeria.PrimaryAttack!.Kind);
        Assert.Equal(1, Martial.WeaponTraining(oldValeria, oldValeria.PrimaryAttack));
        Assert.Equal(5, oldMerrin.Spells.Spellbook.Count);
    }

    [Fact]
    public void ACombatantsSurpriseIsRemembered()
    {
        var encounter = Party(out _, out var karn, out _, out _);
        encounter.Surprise(karn);

        var restored = GameSave.Restore(GameSave.FromJson(GameSave.ToJson(GameSave.Capture(encounter))), TestContent.Library);

        Assert.True(restored.IsSurpriseRound);
    }
}
