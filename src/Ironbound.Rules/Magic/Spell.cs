using Ironbound.Rules.Combat;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Effects;
using Ironbound.Rules.Encounters;
using Ironbound.Rules.Maps;

namespace Ironbound.Rules.Magic;

public enum SpellSchool
{
    Abjuration,
    Conjuration,
    Divination,
    Enchantment,
    Evocation,
    Illusion,
    Necromancy,
    Transmutation,
}

/// <summary>Who in the target area the spell actually touches.</summary>
public enum SpellAffects
{
    /// <summary>Friend and foe alike. A fireball does not care whose side you are on.</summary>
    Everyone,

    Enemies,
    Allies,
    Caster,
}

/// <summary>What a successful saving throw buys the target.</summary>
public enum SaveOutcome
{
    /// <summary>Nothing happens at all.</summary>
    Negates,

    /// <summary>Half the damage, rounded down.</summary>
    Half,

    /// <summary>The save does not help. Rare, but real.</summary>
    None,
}

/// <summary>What shape the spell comes in.</summary>
public abstract record SpellTarget;

/// <summary>The caster, and nobody else.</summary>
public sealed record SelfTarget : SpellTarget;

/// <summary>One creature within range. No attack roll.</summary>
public sealed record SingleTarget : SpellTarget;

/// <summary>One creature, but you have to hit it: an attack roll against touch armour class.</summary>
public sealed record RayTarget : SpellTarget;

/// <summary>Everything within a radius of a point.</summary>
public sealed record BurstTarget(int RadiusFeet) : SpellTarget;

/// <summary>Something a spell does to whoever it lands on.</summary>
public abstract record SpellEffect;

public sealed record DealDamage(SpellDice Amount, DamageType Type) : SpellEffect;

public sealed record Restore(SpellDice Amount) : SpellEffect;

/// <param name="Create">Built fresh per target — each needs its own copy with its own clock —
/// and handed the caster level, because most durations are written per level.</param>
public sealed record Bestow(string Name, Func<int, Effect> Create) : SpellEffect;

/// <summary>
/// A spell, written down.
/// </summary>
/// <remarks>
/// Declarative on purpose. Spells are the most content-heavy thing in a d20 game and are
/// overwhelmingly regular — target something, perhaps roll to hit, perhaps allow a save, then
/// deal damage or hang an effect on it. Writing them as data keeps a spell the same size as its
/// rulebook entry, and turns into a content file later with very little violence.
/// </remarks>
public sealed record Spell(string Name, int Level, SpellSchool School)
{
    public SpellRange Range { get; init; } = SpellRange.Close;

    public SpellTarget Target { get; init; } = new SingleTarget();

    public SpellAffects Affects { get; init; } = SpellAffects.Enemies;

    /// <summary>Null when the spell allows no save at all.</summary>
    public Saves.Save? Save { get; init; }

    public SaveOutcome OnSave { get; init; } = SaveOutcome.Negates;

    public IReadOnlyList<SpellEffect> Does { get; init; } = [];

    public ActionCost CastingTime { get; init; } = ActionCost.Standard;

    /// <summary>Whether this spell needs somewhere to aim rather than someone.</summary>
    public bool NeedsAPoint => Target is BurstTarget;

    public override string ToString() => $"{Name} (level {Level} {School.ToString().ToLowerInvariant()})";
}

/// <summary>Where a spell is being pointed: at somebody, or at a spot on the ground.</summary>
public readonly record struct SpellAim(Creature? Creature, GridSquare? Point)
{
    public static SpellAim At(Creature creature) => new(creature, null);

    public static SpellAim At(GridSquare point) => new(null, point);

    public override string ToString() => Creature?.Name ?? Point?.ToString() ?? "nothing";
}
