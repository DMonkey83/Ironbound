using Ironbound.Rules.Creatures;
using Ironbound.Rules.Dice;
using Ironbound.Rules.Modifiers;

namespace Ironbound.Rules.Skills;

/// <summary>One skill check, with everything needed to explain it.</summary>
public sealed record SkillCheck
{
    public required Skill Skill { get; init; }

    public required Creature Actor { get; init; }

    public required int NaturalRoll { get; init; }

    public required ModifierBreakdown Bonus { get; init; }

    /// <summary>What it was up against, when it was against a number rather than somebody.</summary>
    public int? Difficulty { get; init; }

    /// <summary>True when the creature was not allowed to try at all.</summary>
    public required bool Untrained { get; init; }

    public int Total => Untrained ? 0 : NaturalRoll + Bonus.Total;

    public bool? Succeeded => Difficulty is { } dc && !Untrained ? Total >= dc : null;

    public override string ToString()
    {
        var name = SkillInfo.Name(Skill);

        if (Untrained)
        {
            return $"{Actor.Name} cannot attempt {name} untrained";
        }

        var bonus = Bonus.Total.ToString("+0;-0;+0");
        var against = Difficulty is { } dc
            ? $" vs DC {dc} — {(Succeeded == true ? "success" : "failure")}"
            : string.Empty;

        return $"{Actor.Name} {name}: d20 [{NaturalRoll}] {bonus} = {Total}{against}";
    }
}

/// <summary>
/// What a creature is good at, and how well.
/// </summary>
/// <remarks>
/// Deliberately the same shape as <c>SavingThrows</c>: a stored base — ranks, here — plus a
/// live ability modifier plus a modifier stack, totalled on demand. Nothing is cached, so a
/// Dexterity drain reaches Stealth on the very next check.
/// <para>
/// Ranks are not assigned automatically on level-up. Spending skill points is a choice, and a
/// choice wants a screen; what the rules need first is for the arithmetic to be right.
/// </para>
/// </remarks>
public sealed class SkillSet(Creature owner)
{
    private readonly Creature _owner = owner ?? throw new ArgumentNullException(nameof(owner));
    private readonly Dictionary<Skill, int> _ranks = [];
    private readonly Dictionary<Skill, ModifierStack> _modifiers = [];

    /// <summary>Every skill the creature has put a point into, in a fixed order.</summary>
    public IEnumerable<Skill> Trained =>
        SkillInfo.All.Where(skill => _ranks.GetValueOrDefault(skill) > 0);

    public int Ranks(Skill skill) => _ranks.GetValueOrDefault(skill);

    public SkillSet SetRanks(Skill skill, int ranks)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(ranks);

        _ranks[skill] = ranks;
        return this;
    }

    /// <summary>Circumstance bonuses, gear, spells — anything that is not a rank.</summary>
    public ModifierStack Modifiers(Skill skill)
    {
        if (!_modifiers.TryGetValue(skill, out var stack))
        {
            stack = new ModifierStack();
            _modifiers[skill] = stack;
        }

        return stack;
    }

    /// <summary>Whether any of the creature's classes counts this as one of its own.</summary>
    public bool IsClassSkill(Skill skill) =>
        _owner.Levels.Any(level => level.Class.ClassSkills.Contains(skill));

    /// <summary>Whether the creature may attempt it at all.</summary>
    public bool CanAttempt(Skill skill) =>
        !SkillInfo.TrainedOnly(skill) || Ranks(skill) > 0;

    /// <summary>The whole bonus, with every part named.</summary>
    public ModifierBreakdown Explain(Skill skill)
    {
        var innate = new ModifierStack();
        var ranks = Ranks(skill);

        if (ranks > 0)
        {
            innate.Add(ranks, BonusType.Untyped, "Ranks");

            // Only once a rank has been spent: the three is for being trained in something your
            // class cares about, not for the class alone.
            if (IsClassSkill(skill))
            {
                innate.Add(SkillInfo.ClassSkillBonus, BonusType.Untyped, "Class skill");
            }
        }

        var ability = SkillInfo.AbilityFor(skill);
        var modifier = _owner.Abilities[ability].Modifier;

        if (modifier != 0)
        {
            innate.Add(modifier, BonusType.Untyped, Abilities.AbilityInfo.Abbreviate(ability));
        }

        return ModifierStack.Combine(innate, Modifiers(skill));
    }

    public int Total(Skill skill) => Explain(skill).Total;

    /// <summary>
    /// Rolls one. No natural-twenty floor and no natural-one trapdoor: a skill check is not an
    /// attack roll, and the absence of that mercy is what lets a lock stay shut.
    /// </summary>
    public SkillCheck Check(Skill skill, IRandomSource random, int? difficulty = null)
    {
        ArgumentNullException.ThrowIfNull(random);

        if (!CanAttempt(skill))
        {
            return new SkillCheck
            {
                Skill = skill,
                Actor = _owner,
                NaturalRoll = 0,
                Bonus = Explain(skill),
                Difficulty = difficulty,
                Untrained = true,
            };
        }

        return new SkillCheck
        {
            Skill = skill,
            Actor = _owner,
            NaturalRoll = random.NextDie(20),
            Bonus = Explain(skill),
            Difficulty = difficulty,
            Untrained = false,
        };
    }

    public override string ToString() =>
        Trained.Any()
            ? string.Join(", ", Trained.Select(skill => $"{SkillInfo.Name(skill)} {Total(skill):+0;-0;+0}"))
            : "no training";
}
