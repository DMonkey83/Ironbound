using Ironbound.Rules.Creatures;
using Ironbound.Rules.Feats;
using Ironbound.Rules.Modifiers;

namespace Ironbound.Rules.Combat;

/// <summary>A way of fighting that trades one thing for another.</summary>
public enum Stance
{
    /// <summary>Swing harder and hit less often. Melee only.</summary>
    PowerAttack,

    /// <summary>Fight cannily: accuracy for armour class.</summary>
    CombatExpertise,

    /// <summary>Cover up. Costs a great deal of accuracy and needs no training at all.</summary>
    FightingDefensively,
}

/// <summary>
/// The dials a fighter sets before swinging.
/// </summary>
/// <remarks>
/// What turns an attack from a roll into a decision. Every one of them is a trade, and the right
/// answer changes with the target: Power Attack against something with poor armour and plenty of
/// hit points, Combat Expertise against something that hits harder than you do.
/// <para>
/// Held as flags on the creature rather than as effects with clocks, because a stance is a
/// standing intention rather than something that happened to you. The armour-class half is a
/// modifier on the stack, since it is not weapon-dependent; the attack and damage halves are
/// read by <see cref="Strike"/>, because Power Attack applies to a sword and not to a bow and
/// only the resolver knows which is being used.
/// </para>
/// </remarks>
public sealed class Stances(Creature owner)
{
    private readonly Creature _owner = owner ?? throw new ArgumentNullException(nameof(owner));
    private readonly HashSet<Stance> _active = [];

    /// <summary>What fighting defensively costs and buys, whoever you are.</summary>
    public const int DefensivePenalty = -4;

    public const int DefensiveBonus = 2;

    public IEnumerable<Stance> Active => _active.OrderBy(stance => stance);

    public bool IsActive(Stance stance) => _active.Contains(stance);

    /// <summary>Whether the creature has the training a stance calls for.</summary>
    public bool CanAdopt(Stance stance) => stance switch
    {
        Stance.PowerAttack => _owner.HasFeat(FeatEffect.PowerAttack),
        Stance.CombatExpertise => _owner.HasFeat(FeatEffect.CombatExpertise),
        _ => true,
    };

    /// <summary>
    /// How steep the trade is. Both trained stances sharpen with skill at arms: one more point
    /// given and taken for every four points of base attack.
    /// </summary>
    public int Severity(Stance stance) => stance switch
    {
        Stance.FightingDefensively => -DefensivePenalty,
        _ => Math.Clamp(1 + (_owner.BaseAttackBonus / 4), 1, 5),
    };

    /// <summary>What the attack roll gives up.</summary>
    public int AttackPenalty(bool melee)
    {
        var penalty = 0;

        if (melee && IsActive(Stance.PowerAttack))
        {
            penalty -= Severity(Stance.PowerAttack);
        }

        if (IsActive(Stance.CombatExpertise))
        {
            penalty -= Severity(Stance.CombatExpertise);
        }

        if (IsActive(Stance.FightingDefensively))
        {
            penalty += DefensivePenalty;
        }

        return penalty;
    }

    /// <summary>What a melee swing gains for it. Nothing at all with a bow in your hands.</summary>
    public int DamageBonus(bool melee) =>
        melee && IsActive(Stance.PowerAttack) ? Severity(Stance.PowerAttack) * 2 : 0;

    /// <summary>Takes it up. Returns false if the creature has not the training for it.</summary>
    public bool Adopt(Stance stance)
    {
        if (!CanAdopt(stance) || !_active.Add(stance))
        {
            return false;
        }

        Ward(stance);
        return true;
    }

    /// <summary>Drops it, and gives back whatever it was holding.</summary>
    public bool Drop(Stance stance)
    {
        if (!_active.Remove(stance))
        {
            return false;
        }

        _owner.ArmorClass.Modifiers.RemoveAllFrom(Name(stance));
        return true;
    }

    public bool Toggle(Stance stance) => IsActive(stance) ? Drop(stance) : Adopt(stance);

    /// <summary>Everything at once, as ending a fight does.</summary>
    public void Clear()
    {
        foreach (var stance in _active.ToList())
        {
            Drop(stance);
        }
    }

    /// <summary>
    /// Puts a stance back on somebody who already carries what it gave them, as a save does.
    /// </summary>
    /// <remarks>
    /// No warding: the armour-class modifier came back with the stack, exactly as a feat's and
    /// an item's do. Adding it again here would quietly double it.
    /// </remarks>
    internal void Reattach(Stance stance) => _active.Add(stance);

    public static string Name(Stance stance) => stance switch
    {
        Stance.PowerAttack => "Power Attack",
        Stance.CombatExpertise => "Combat Expertise",
        _ => "Fighting Defensively",
    };

    /// <summary>
    /// The armour-class half, which is an ordinary modifier because armour class does not
    /// depend on what you happen to be holding.
    /// </summary>
    private void Ward(Stance stance)
    {
        var bonus = stance switch
        {
            Stance.CombatExpertise => Severity(stance),
            Stance.FightingDefensively => DefensiveBonus,
            _ => 0,
        };

        if (bonus != 0)
        {
            // Dodge, so two of them stack and so being caught flat-footed loses you both.
            _owner.ArmorClass.Modifiers.Add(bonus, BonusType.Dodge, Name(stance));
        }
    }

    public override string ToString() =>
        _active.Count == 0 ? "none" : string.Join(", ", Active.Select(Name));
}
