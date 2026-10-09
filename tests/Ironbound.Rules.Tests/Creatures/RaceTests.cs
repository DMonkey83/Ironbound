using Ironbound.Rules.Abilities;
using Ironbound.Rules.Conditions;
using Ironbound.Rules.Content;
using Ironbound.Rules.Dice;
using Ironbound.Rules.Magic;
using Ironbound.Rules.Modifiers;
using Ironbound.Rules.Skills;
using Ironbound.Rules.Tests.Classes;

namespace Ironbound.Rules.Tests.Creatures;

public class RaceTests
{
    private static ContentLibrary Library => TestContent.Library;

    // An enchantment that sends its target to sleep, and one that only dazes. Neither ships: the
    // game has no sleep spell yet, which is exactly why the immunity needs a test of its own.
    private static readonly (string, string)[] Enchantments =
    [
        ("doze.json", """
            { "kind": "spell", "id": "doze", "name": "Doze", "level": 1, "school": "Enchantment",
              "range": "close", "descriptors": ["sleep", "mind-affecting"], "save": "Will", "onSave": "Negates",
              "does": [ { "effect": { "name": "Doze", "condition": "Asleep", "rounds": 10 } } ] }
            """),
        ("befuddle.json", """
            { "kind": "spell", "id": "befuddle", "name": "Befuddle", "level": 1, "school": "Enchantment",
              "range": "close", "descriptors": ["mind-affecting"], "save": "Will", "onSave": "Negates",
              "does": [ { "effect": { "name": "Befuddle", "condition": "Dazed", "rounds": 1 } } ] }
            """),
    ];

    [Fact]
    public void SylwenIsAnElf()
    {
        var sylwen = Library.BuildCreature("sylwen")!;

        Assert.Equal("elf", sylwen.Race!.Id);
        Assert.Equal("Elf", sylwen.Race.Name);
    }

    [Fact]
    public void NobodyElseHasARace()
    {
        foreach (var id in Library.CreatureIds.Where(id => id != "sylwen"))
        {
            Assert.Null(Library.BuildCreature(id)!.Race);
        }
    }

    [Fact]
    public void KeenSensesAreTwoMoreOnPerception()
    {
        var elf = ClassKit.Make("wizard", 1, "\"race\": \"elf\"");
        var human = ClassKit.Make("wizard", 1);

        Assert.Equal(human.Skills.Total(Skill.Perception) + 2, elf.Skills.Total(Skill.Perception));
        Assert.Contains(elf.Skills.Modifiers(Skill.Perception),
            modifier => modifier.Type == BonusType.Racial && modifier.Source == "Keen senses");
    }

    [Fact]
    public void TheAbilityAdjustmentsAreWrittenDownAndNotApplied()
    {
        var elf = ClassKit.Make("wizard", 1, "\"race\": \"elf\"", abilities: [10, 16, 12, 16, 10, 10]);

        Assert.Equal(2, elf.Race!.AbilityAdjustments[Ability.Dexterity]);
        Assert.Equal(-2, elf.Race.AbilityAdjustments[Ability.Constitution]);
        Assert.Equal(16, elf.Abilities.Dexterity.Score);
        Assert.Equal(12, elf.Abilities.Constitution.Score);
    }

    [Fact]
    public void AnElfDoesNotFallAsleep()
    {
        var library = ClassKit.Library(Enchantments);
        var caster = ClassKit.Make("wizard", 1, files: Enchantments);
        var elf = ClassKit.Make("wizard", 1, "\"race\": \"elf\"", files: Enchantments);

        // Every Will save fails; it makes no difference to an elf.
        var cast = Casting.Resolve(caster, library.GetSpell("doze")!, SpellAim.At(elf), SequenceRandom.Always(1));

        Assert.False(elf.Has(Condition.Asleep));
        Assert.Contains("immune (elf)", cast.Targets.Single().Applied);
        Assert.Null(cast.Targets.Single().Save);
    }

    [Fact]
    public void AHumanDoes()
    {
        var library = ClassKit.Library(Enchantments);
        var caster = ClassKit.Make("wizard", 1, files: Enchantments);
        var human = ClassKit.Make("wizard", 1, files: Enchantments);

        Casting.Resolve(caster, library.GetSpell("doze")!, SpellAim.At(human), SequenceRandom.Always(1));

        Assert.True(human.Has(Condition.Asleep));
    }

    [Fact]
    public void AnElfSavesBetterAgainstEnchantment()
    {
        var library = ClassKit.Library(Enchantments);
        var caster = ClassKit.Make("wizard", 1, files: Enchantments);
        var elf = ClassKit.Make("wizard", 1, "\"race\": \"elf\"", files: Enchantments);

        var save = Casting.Resolve(caster, library.GetSpell("befuddle")!, SpellAim.At(elf), SequenceRandom.Always(10))
            .Targets.Single().Save!;

        Assert.Contains(save.Bonus.Entries,
            entry => entry.Modifier.Source == "Elven immunities" && entry.Modifier.Value == 2 && entry.Modifier.Type == BonusType.Racial);
    }

    [Fact]
    public void ButNotAgainstAnythingElse()
    {
        var caster = ClassKit.Make("wizard", 1);
        var elf = ClassKit.Make("wizard", 1, "\"race\": \"elf\"");

        var save = Casting.Resolve(caster, Library.GetSpell("cause-fear")!, SpellAim.At(elf), SequenceRandom.Always(10))
            .Targets.Single().Save!;

        Assert.DoesNotContain(save.Bonus.Entries, entry => entry.Modifier.Source == "Elven immunities");
    }

    [Fact]
    public void TheRestIsWrittenDownForLater()
    {
        var elf = Library.GetRace("elf")!;

        Assert.Equal(["low-light vision"], elf.Senses);
        Assert.Equal(2, elf.SpellResistanceChecks);
        Assert.Equal(2, elf.IdentifyItems);
        Assert.Contains(elf.Traits, trait => trait.Name == "Low-light vision");
    }

    [Fact]
    public void ARaceThatNamesAWeaponTheGameLacksIsReported()
    {
        var library = ClassKit.Library(("orc.json", """
            { "kind": "race", "id": "half-orc", "name": "Half-orc", "weaponFamiliarity": ["orc-greataxe"] }
            """));

        Assert.Contains(library.Problems, problem => problem.Source == "race 'half-orc'" && problem.Message.Contains("orc-greataxe"));
    }
}
