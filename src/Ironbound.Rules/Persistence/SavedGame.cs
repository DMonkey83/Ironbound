using Ironbound.Rules.Abilities;
using Ironbound.Rules.Combat;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Defense;
using Ironbound.Rules.Dice;
using Ironbound.Rules.Effects;
using Ironbound.Rules.Modifiers;

namespace Ironbound.Rules.Persistence;

/// <summary>
/// The shape of a save file.
/// </summary>
/// <remarks>
/// Written out by hand rather than reflected off the live objects, and made only of primitives,
/// enums and arrays. A save file is a contract with every game anyone has already saved: deriving
/// it from whatever fields happen to exist would turn ordinary refactoring into a
/// save-compatibility break, and nobody notices that until someone's campaign will not load.
/// <para>
/// <see cref="SavedGame.Version"/> is the hook for migrating old files when the shape changes.
/// </para>
/// </remarks>
public sealed record SavedGame(
    int Version,
    RuleOptions Rules,
    ulong RandomA,
    ulong RandomB,
    long Tick,
    SavedBattlefield? Ground,
    SavedCreature[] Creatures,
    SavedCombatant[] Order)
{
    public const int CurrentVersion = 6;
}

public sealed record SavedBattlefield(
    int Width,
    int Height,
    SavedSquare[] Blocked,
    SavedSquare[] Difficult);

public readonly record struct SavedSquare(int X, int Y);

public readonly record struct SavedDuration(int Ticks, bool Permanent);

public readonly record struct SavedModifier(int Value, BonusType Type, string Source);

public sealed record SavedCombatant(
    string Name,
    int NaturalRoll,
    int Initiative,
    long NextTurnTick,
    int OpportunitiesUsed,
    bool HasMoved,
    bool HasTakenFiveFootStep,
    bool HasStandard,
    bool HasMove,
    bool HasSwift);

public sealed record SavedHitPoints(
    int Base,
    int HitDice,
    int Damage,
    int Temporary,
    int Nonlethal);

public sealed record SavedAbility(int Base, bool Present, SavedModifier[] Modifiers);

public sealed record SavedSave(int Base, SavedModifier[] Modifiers);

public sealed record SavedReduction(int Amount, DamageBypass BypassedBy, BypassMode Mode);

public sealed record SavedResistance(DamageType Type, int Amount);

public sealed record SavedDefenses(
    SavedReduction[] Reductions,
    SavedResistance[] Resistances,
    DamageType[] Immunities,
    DamageType[] Vulnerabilities);

public sealed record SavedSlot(int Level, int Maximum, int Remaining);

public sealed record SavedSpellcasting(
    Ability CastingAbility,
    int CasterLevel,
    SavedSlot[] Slots,
    string[] Prepared);

public sealed record SavedDamageComponent(string Amount, DamageType Type, bool MultipliedOnCritical);

/// <summary>An equipped item: which one, and where it actually ended up.</summary>
public sealed record SavedItem(string Id, Items.EquipmentSlot Slot);

public sealed record SavedWeapon(
    string Name,
    int ThreatsOn,
    int Multiplier,
    bool TargetsTouchArmorClass,
    DamageBypass Qualities,
    Ability? AttackAbility,
    Ability? DamageAbility,
    AbilityDamageScale DamageScale,
    SavedModifier[] AttackModifiers,
    SavedModifier[] DamageModifiers,
    SavedDamageComponent[] Damage,
    int RangeIncrement,
    int MaximumIncrements);

/// <summary>
/// A running effect.
/// </summary>
/// <remarks>
/// A flat union rather than polymorphic JSON: every kind's fields sit side by side and unused
/// ones are null. Clumsier to read than a type hierarchy and far easier to version, which is the
/// trade a save format wants.
/// </remarks>
public sealed record SavedEffect(
    string Kind,
    string Name,
    SavedDuration Duration,
    SavedDuration Period,
    int TicksRemaining,
    int TicksUntilPeriod,
    SavedModifier[]? Grants,
    ModifierTargetKind[]? GrantTargets,
    int[]? GrantTargetWhich,
    string? Amount,
    DamageType? DamageType,
    int? Heal,
    DamageType[]? SuspendedBy,
    bool Suspended,
    Conditions.Condition? Condition);

public sealed record SavedCreature(
    string Name,
    int Allegiance,
    CreatureSize Size,
    int Speed,
    int BaseAttackBonus,
    int BaseAttacksOfOpportunity,
    string[] Feats,
    SavedItem[] Items,
    SavedAbility[] Abilities,
    SavedHitPoints HitPoints,
    SavedSave[] Saves,
    SavedModifier[] ArmorClass,
    SavedModifier[] Attack,
    SavedModifier[] Damage,
    SavedModifier[] Initiative,
    SavedModifier[] SpeedModifiers,
    SavedDefenses Defenses,
    SavedSpellcasting Spells,
    SavedWeapon[] Weapons,
    SavedEffect[] Effects,
    SavedSquare? Square);
