using Ironbound.Rules.Creatures;
using Ironbound.Rules.Defense;

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

    /// <summary>What the target's defences left of <see cref="Damage"/>. Null on a miss.</summary>
    public DamageTaken? Taken { get; init; }

    /// <summary>How the damage split between temporary and real hit points. Null on a miss.</summary>
    public DamageApplication? Applied { get; init; }

    /// <summary>Damage that regeneration turned aside into nonlethal rather than real wounds.</summary>
    public int NonlethalDealt { get; init; }

    public required HitPointState StateBefore { get; init; }

    public required HitPointState StateAfter { get; init; }

    /// <summary>
    /// The target's hit points as they stood the moment this strike landed.
    /// </summary>
    /// <remarks>
    /// Captured rather than read back from the creature, because a full attack resolves several
    /// strikes before anything renders them: reading live made both swings of a two-attack round
    /// report the same total, which read as though the second had done nothing.
    /// </remarks>
    public required string TargetAfter { get; init; }

    /// <summary>
    /// How many d6 of sneak attack rode on this hit: precision damage, already in
    /// <see cref="Damage"/> and never multiplied by a critical. Nought when there was none.
    /// </summary>
    public int SneakAttackDice { get; init; }

    /// <summary>Whether this was a Vital Strike, with the weapon's dice rolled twice.</summary>
    public bool Vital { get; init; }

    /// <summary>
    /// What else happened on the way: a declared rage power used, a defensive roll, the bleed a
    /// sneak attack left. Each a short phrase the log can append.
    /// </summary>
    public IReadOnlyList<string> Notes { get; init; } = [];

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
        var verb = Vital ? "vital strikes" : "attacks";
        var text = $"{Attacker.Name} {verb} {Target.Name} ({Weapon.Name}): {Attack}";

        if (Damage is null)
        {
            return Notes.Count == 0 ? text : $"{text}; {string.Join("; ", Notes)}";
        }

        text += $"; {Damage}";

        if (SneakAttackDice > 0)
        {
            text += $" (+{SneakAttackDice}d6 sneak attack)";
        }

        if (Taken is { WasMitigated: true } mitigated)
        {
            text += $"; {mitigated}";
        }

        if (Applied is { ToTemporary: > 0 } applied)
        {
            text += $" ({applied.ToTemporary} absorbed)";
        }

        if (NonlethalDealt > 0)
        {
            text += $"; {NonlethalDealt} of it nonlethal";
        }

        foreach (var note in Notes)
        {
            text += $"; {note}";
        }

        return $"{text}; {Target.Name} {TargetAfter}";
    }
}
