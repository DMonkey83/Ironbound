using Ironbound.Rules.Abilities;

namespace Ironbound.Rules.Effects;

/// <summary>
/// An ability bleeding away: so many points of it lost every round until somebody stops it.
/// Deadly Stroke's point of Constitution a round is the one thing that causes it.
/// </summary>
/// <remarks>
/// Each round's loss is a modifier stamped with the effect's name, so ending the effect gives it
/// all back. The book would have the damage stay until it healed in its own time; the engine has
/// no slow healing of ability damage, and the only things that end this — magic that heals, and
/// a night's rest — are the things that would mend the damage too.
/// </remarks>
public sealed class AbilityBleedEffect : Effect
{
    public AbilityBleedEffect(string name, Ability ability, int perRound)
        : base(name, Duration.Permanent, Duration.Rounds(1))
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(perRound);
        Ability = ability;
        PerRound = perRound;
    }

    public Ability Ability { get; }

    /// <summary>How much goes each round.</summary>
    public int PerRound { get; }

    protected override void OnTick(EffectContext context)
    {
        var score = context.Target.Abilities[Ability];
        if (!score.HasScore)
        {
            return;
        }

        Grant(score.Modifiers, -PerRound, Modifiers.BonusType.Untyped);
        context.Report($"{context.Target.Name} bleeds {PerRound} {AbilityInfo.Abbreviate(Ability)} from {Name}");
    }

    protected override void OnReattach(Creatures.Creature target) => Track(target.Abilities[Ability].Modifiers);
}
