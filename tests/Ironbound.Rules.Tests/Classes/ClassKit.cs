using Ironbound.Rules.Abilities;
using Ironbound.Rules.Combat;
using Ironbound.Rules.Content;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Encounters;
using Ironbound.Rules.Maps;
using Ironbound.Rules.Modifiers;

namespace Ironbound.Rules.Tests.Classes;

/// <summary>
/// Builds the characters the class-feature tests need out of the shipped class files, so every
/// test checks the classes the game actually ships rather than copies of them.
/// </summary>
internal static class ClassKit
{
    /// <summary>The shipped content plus whatever extra files a test adds.</summary>
    public static ContentLibrary Library(params (string Source, string Json)[] extra) =>
        extra.Length == 0 ? TestContent.Library : ContentLibrary.Load(TestContent.Files().Concat(extra));

    /// <summary>
    /// A creature of one class and level, built from the shipped class file, with whatever else
    /// its file should say written as extra JSON members.
    /// </summary>
    public static Creature Make(
        string classId,
        int level,
        string extra = "",
        int[]? abilities = null,
        params (string Source, string Json)[] files)
    {
        abilities ??= [14, 14, 14, 14, 14, 14];
        var json = $$"""
            {
              "kind": "creature", "id": "subject", "name": "Subject",
              "abilities": [{{string.Join(", ", abilities)}}],
              "classes": [ { "class": "{{classId}}", "level": {{level}} } ]
              {{(extra.Length > 0 ? "," + extra : string.Empty)}}
            }
            """;

        var loaded = ContentLibrary.Load(TestContent.Files()
            .Concat(files)
            .Append(("subject.json", json)));

        Assert.Empty(loaded.Problems);
        return loaded.BuildCreature("subject")!;
    }

    /// <summary>A plain creature with no classes at all: a training dummy.</summary>
    public static Creature Dummy(string name = "Dummy", int armour = 0, int hitPoints = 60, int dexterity = 10)
    {
        var dummy = new Creature(name, new AbilityScores(10, dexterity, 10, 10, 10, 10), hitPoints, 1);
        if (armour != 0)
        {
            dummy.ArmorClass.Modifiers.Add(armour, BonusType.Armor, "Armour");
        }

        dummy.Attacks.Add(WeaponAttack.Melee("club", "1d6", DamageType.Bludgeoning));
        return dummy;
    }

    /// <summary>Puts creatures on an open field at the squares given, in order.</summary>
    public static Battlefield Field(params (Creature Creature, int X, int Y)[] placed)
    {
        var field = new Battlefield(12, 12);
        foreach (var (creature, x, y) in placed)
        {
            field.Place(creature, x, y);
        }

        return field;
    }

    /// <summary>Two sides, the first acting first: initiative rolls of 20 for the first side and 1 for the second.</summary>
    public static Encounter Fight(
        IReadOnlyList<Creature> first,
        IReadOnlyList<Creature> second,
        Battlefield? field,
        params int[] rolls)
    {
        foreach (var creature in first)
        {
            creature.Allegiance = 1;
        }

        foreach (var creature in second)
        {
            creature.Allegiance = 2;
        }

        var initiative = first.Select(_ => 20).Concat(second.Select(_ => 1));
        return new Encounter(first.Concat(second), new Ironbound.Rules.Dice.SequenceRandom([.. initiative, .. rolls]), battlefield: field);
    }
}
