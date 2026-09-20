using Ironbound.Rules.Creatures;

namespace Ironbound.Rules.Combat;

/// <summary>
/// One creature hitting — or missing — another: the attack roll, the damage if any, how it was
/// absorbed, and what it did to the target. Composed of the existing result objects rather than
/// flattening them, so nothing is lost on the way to the combat log.
/// </summary>
public sealed record StrikeResult
{
    public required Creature Attacker { get; init; }

    public required Creature Target { get; init; }

    public required WeaponAttack Weapon { get; init; }

    public required AttackResult Attack { get; init; }

    /// <summary>Null on a miss — no damage dice are rolled at all.</summary>
    public DamageRoll? Damage { get; init; }

    /// <summary>How the damage split between temporary and real hit points. Null on a miss.</summary>
    public DamageApplication? Applied { get; init; }

    public required HitPointState StateBefore { get; init; }

    public required HitPointState StateAfter { get; init; }

    public bool IsHit => Attack.IsHit;

    public bool IsCritical => Attack.IsCritical;

    /// <summary>Damage that reached actual hit points, ignoring any temporary pool.</summary>
    public int DamageDealt => Applied?.ToHitPoints ?? 0;

    /// <summary>This blow is what killed it.</summary>
    public bool Killed => StateAfter == HitPointState.Dead && StateBefore != HitPointState.Dead;

    /// <summary>This blow is what took it out of the fight.</summary>
    public bool Dropped =>
        StateBefore == HitPointState.Healthy && StateAfter != HitPointState.Healthy;

    public override string ToString()
    {
        var text = $"{Attacker.Name} attacks {Target.Name} ({Weapon.Name}): {Attack}";

        if (Damage is null)
        {
            return text;
        }

        text += $"; {Damage}";

        if (Applied is { ToTemporary: > 0 } applied)
        {
            text += $" ({applied.ToTemporary} absorbed)";
        }

        return $"{text}; {Target.Name} {Target.HitPoints}";
    }
}
