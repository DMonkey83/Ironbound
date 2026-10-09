using Ironbound.Rules.Abilities;
using Ironbound.Rules.Combat;
using Ironbound.Rules.Content;
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
    SavedCombatant[] Order,
    SavedCampaign? Campaign = null)
{
    public const int CurrentVersion = 16;

    /// <summary>
    /// The oldest version still read. Twelve only added a field that eleven did without, so an
    /// eleven reads as a twelve that never had a level in it — which is exactly what it was.
    /// Thirteen added class features; an older file is given what its creatures' own content
    /// files choose, with every daily pool full and nobody raging. Fourteen added the weapon
    /// catalogue and races: an older file has nothing broken, everything in hand, and the race
    /// its creatures' files give them. Fifteen added the stacks every skill check and every
    /// ability check share, and what the movement feats leave on a combatant between turns: an
    /// older file has nothing on either, which is what it had. Sixteen replaced the sack with the
    /// party's bag and the level's containers: an older file's sack is poured into the bag, its
    /// searched caches come back open and empty, and the bodies of rooms already won do not
    /// exist — whatever they carried was in the sack already.
    /// </summary>
    public const int OldestReadable = 11;
}

/// <summary>Where a run of encounters had got to.</summary>
/// <param name="Stash">The old sack, by item id: read from a save before sixteen and written empty since.</param>
/// <param name="Level">Null for a run of separate fights, which is every save before twelve.</param>
/// <param name="Bag">The party's bag. Null before sixteen.</param>
/// <param name="Purse">The party's coins. Null before sixteen.</param>
/// <param name="Containers">Every container the party knows of or has yet to find, bodies and
/// piles included. Null before sixteen.</param>
public sealed record SavedCampaign(
    string Id,
    int Chapter,
    int RestsRemaining,
    ulong Seed,
    string[] Stash,
    int LootedChapter,
    int Experience,
    SavedLevel? Level = null,
    SavedBagEntry[]? Bag = null,
    SavedMoney? Purse = null,
    SavedContainer[]? Containers = null);

/// <summary>So many of one item in a bag or a container, broken or not.</summary>
public sealed record SavedBagEntry(string Id, int Count, bool Broken = false);

/// <summary>Coins by kind.</summary>
public sealed record SavedMoney(int Platinum, int Gold, int Silver, int Copper);

/// <summary>
/// A container as the party has left it: what is still in it, and whether it has been opened,
/// unlocked and noticed.
/// </summary>
/// <param name="Rolled">Who has had their one look for a hidden one, by name.</param>
/// <param name="BodyOf">For a body, the creature file it was built from, to draw it again.</param>
/// <param name="BodyName">For a body, the name it went by.</param>
public sealed record SavedContainer(
    string Id,
    string Name,
    ContainerLook Look,
    SavedSquare[] Squares,
    SavedBagEntry[] Contents,
    SavedMoney Coins,
    bool Open,
    bool Locked,
    bool Noticed,
    string[] Rolled,
    string? BodyOf = null,
    string? BodyName = null);

/// <summary>
/// How far through a level the party has got.
/// </summary>
/// <remarks>
/// Only what the level file cannot say for itself. Who was waiting in an unvisited room is
/// already written down there, so it is rebuilt rather than saved; what the party has done to
/// the place — rooms emptied, doors broken, the bridge tied off — is not, so it is kept here.
/// </remarks>
/// <param name="Current">The room being fought in, or null while exploring.</param>
/// <param name="RandomA">The exploring dice, as <see cref="SavedGame.RandomA"/> is the fight's.</param>
public sealed record SavedLevel(
    string Id,
    string[] Cleared,
    string[] Visited,
    string[] Used,
    string? Current,
    ulong RandomA,
    ulong RandomB);

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
    bool HasActed,
    bool IsUnaware,
    bool HasStandard,
    bool HasMove,
    bool HasSwift,
    bool WasSurprised = false,
    bool HasUsedOpportunist = false,
    bool IsRunning = false,
    bool SteppedUp = false,
    bool OwesStep = false,
    bool IsLunging = false,
    bool IsHeld = false,
    int EasyGroundUsed = 0);

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

/// <param name="Specialty">Domain or school slots. Null in a save from before thirteen, which
/// had them counted in with the rest.</param>
/// <param name="Spellbook">What a wizard has written down. Null before thirteen.</param>
public sealed record SavedSpellcasting(
    Ability CastingAbility,
    int CasterLevel,
    SavedSlot[] Slots,
    string[] Prepared,
    SavedSlot[]? Specialty = null,
    string[]? Spellbook = null);

/// <param name="Nonlethal">A sap's or a whip's damage. False in a save before fourteen, which had none.</param>
public sealed record SavedDamageComponent(string Amount, DamageType Type, bool MultipliedOnCritical, bool Nonlethal = false);

/// <summary>Ranks in one skill, and anything else stacked on it.</summary>
public sealed record SavedSkill(Skills.Skill Skill, int Ranks, SavedModifier[] Modifiers);

/// <summary>So many levels of a class, by id.</summary>
public sealed record SavedClassLevel(string ClassId, int Level);

/// <summary>An equipped item: which one, where it actually ended up, and what has happened to it.</summary>
/// <param name="Broken">Cracked on a natural 1 or a misfire. False before fourteen.</param>
/// <param name="OutOfHand">Thrown or dropped and not yet picked up. False before fourteen.</param>
public sealed record SavedItem(string Id, Items.EquipmentSlot Slot, bool Broken = false, bool OutOfHand = false);

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
    int MaximumIncrements,
    string? Kind = null,
    string[]? Groups = null,
    bool Finesse = false);

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
    Conditions.Condition? Condition,
    string? Detail = null);

/// <summary>How much of one daily pool had been spent.</summary>
public readonly record struct SavedPool(string Pool, int Spent);

/// <summary>
/// What a creature chose for its class features, and how much of its day it has spent.
/// </summary>
/// <remarks>
/// Ids rather than definitions, like feats and levels: the load looks them up again, so a god or
/// a talent renamed in its file is renamed in every save.
/// </remarks>
public sealed record SavedFeatures(
    string[] WeaponGroups,
    string[] Talents,
    string? Deity,
    string[] Domains,
    Classes.ChannelKind? Channel,
    string? School,
    Magic.SpellSchool[] Opposition,
    bool BondedObject,
    string? WeaponMasterFeat,
    SavedPool[] Spent,
    Combat.Stance[] RageSpent);

public sealed record SavedCreature(
    string Name,
    string? DefinitionId,
    int Allegiance,
    CreatureSize Size,
    int Speed,
    int BaseAttackBonus,
    int BaseAttacksOfOpportunity,
    string[] Feats,
    SavedItem[] Items,
    SavedClassLevel[] Levels,
    SavedSkill[] Skills,
    Combat.Stance[] Stances,
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
    SavedSquare? Square,
    SavedFeatures? Features = null,
    string? Race = null,
    SavedModifier[]? SkillChecks = null,
    SavedModifier[]? AbilityChecks = null);
