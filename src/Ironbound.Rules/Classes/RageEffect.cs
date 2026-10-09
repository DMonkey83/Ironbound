using Ironbound.Rules.Abilities;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Effects;
using Ironbound.Rules.Modifiers;
using Ironbound.Rules.Saves;

namespace Ironbound.Rules.Classes;

/// <summary>
/// A barbarian in the middle of a rage: stronger, tougher, steadier of will, and easier to hit.
/// </summary>
/// <remarks>
/// An effect because that is exactly what it is — modifiers that arrive together and leave
/// together — and because the effect clock is the encounter's clock, which is what the rules
/// count rage in. Its duration is however many rounds were left in the day when it began, so a
/// rage that is never ended by hand runs out precisely when the allowance does, with no
/// bookkeeping a round at a time.
/// <para>
/// The +4 Constitution is a modifier on the ability like any other, so the two hit points a die
/// arrive and leave with it for free — and, because hit points store damage rather than a
/// current total, a barbarian who was hanging on by those two a die can drop when it ends.
/// That is the rule, and it falls out of the arithmetic rather than being written anywhere.
/// </para>
/// </remarks>
public sealed class RageEffect : Effect
{
    /// <summary>The name it is filed under, so one creature never rages twice over.</summary>
    public const string Label = "Rage";

    public const int AbilityBonus = 4;

    public const int WillBonus = 2;

    public const int ArmourPenalty = -2;

    /// <param name="rounds">What is left of today's allowance. One round at the least: starting
    /// a rage spends the round it starts in.</param>
    public RageEffect(int rounds)
        : this(Duration.Rounds(Math.Max(1, rounds)))
    {
    }

    internal RageEffect(Duration duration)
        : base(Label, duration)
    {
    }

    /// <summary>
    /// Rounds begun so far, counting the one it started in. What it costs from the day's
    /// allowance when it ends, and what the fatigue afterwards is measured against.
    /// </summary>
    public int RoundsSoFar
    {
        get
        {
            var total = Duration.Ticks / Duration.TicksPerRound;
            var elapsed = Math.Max(0, Duration.Ticks - TicksRemaining);
            return Math.Clamp(1 + (elapsed / Duration.TicksPerRound), 1, Math.Max(1, total));
        }
    }

    protected override void OnApply(Creature target)
    {
        Grant(target.Abilities[Ability.Strength].Modifiers, AbilityBonus, BonusType.Morale);
        Grant(target.Abilities[Ability.Constitution].Modifiers, AbilityBonus, BonusType.Morale);
        Grant(target.Saves[Save.Will].Modifiers, WillBonus, BonusType.Morale);
        Grant(target.ArmorClass.Modifiers, ArmourPenalty, BonusType.Untyped);
    }

    protected override void OnReattach(Creature target)
    {
        Track(target.Abilities[Ability.Strength].Modifiers);
        Track(target.Abilities[Ability.Constitution].Modifiers);
        Track(target.Saves[Save.Will].Modifiers);
        Track(target.ArmorClass.Modifiers);
    }

    protected override void OnExpire(Creature target) => Rage.Ended(target, RoundsSoFar);
}
