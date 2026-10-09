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

/// <summary>
/// The caster — and, with a radius, everyone within it of the caster: a burst that goes where
/// she goes and is never aimed anywhere else.
/// </summary>
/// <remarks>
/// Channel energy is the reason for the radius. It is a thirty-foot burst, but centred on the
/// cleric and nowhere else, so asking the player where to put it would be asking a question with
/// one answer. A burst on oneself is still "self" as far as aiming goes.
/// </remarks>
public sealed record SelfTarget : SpellTarget
{
    /// <summary>Zero for the caster alone; otherwise how far around her it reaches, in feet.</summary>
    public int RadiusFeet { get; init; }
}

/// <summary>A spot on the ground and nobody in particular: where a teleport lands.</summary>
public sealed record PointTarget : SpellTarget;

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

/// <summary>
/// Temporary hit points. Spent before real ones, and never healed back.
/// </summary>
/// <remarks>
/// The engine's temporary hit points have no clock of their own, so these last until they are
/// used up or the next rest rather than for the spell's duration. Divine Power is the one spell
/// that hands them out, and the difference is a handful of points that linger.
/// </remarks>
public sealed record Bolster(SpellDice Amount) : SpellEffect;

/// <param name="Effect">Described rather than constructed, so the spell can live in a file.
/// A fresh copy is built per target, at the caster's level, because durations are usually
/// written per level.</param>
public sealed record Bestow(EffectDefinition Effect) : SpellEffect
{
    public string Name => Effect.Name;
}

/// <summary>
/// A spell, written down.
/// </summary>
/// <remarks>
/// Declarative on purpose. Spells are the most content-heavy thing in a d20 game and are
/// overwhelmingly regular — target something, perhaps roll to hit, perhaps allow a save, then
/// deal damage or hang an effect on it. Writing them as data keeps a spell the same size as its
/// rulebook entry, and turns into a content file later with very little violence.
/// </remarks>
/// <param name="Id">Stable and lower-case. Saves refer to this, so it survives the display
/// name being reworded.</param>
public sealed record Spell(string Id, string Name, int Level, SpellSchool School)
{
    public SpellRange Range { get; init; } = SpellRange.Close;

    public SpellTarget Target { get; init; } = new SingleTarget();

    public SpellAffects Affects { get; init; } = SpellAffects.Enemies;

    /// <summary>Null when the spell allows no save at all.</summary>
    public Saves.Save? Save { get; init; }

    public SaveOutcome OnSave { get; init; } = SaveOutcome.Negates;

    public IReadOnlyList<SpellEffect> Does { get; init; } = [];

    public ActionCost CastingTime { get; init; } = ActionCost.Standard;

    /// <summary>
    /// What kind of magic it is, in words: <c>fear</c>, <c>cure</c>. Rules elsewhere ask —
    /// bravery against anything that frightens, a cleric's spontaneous cures.
    /// </summary>
    /// <remarks>
    /// <c>cure</c> is not a Pathfinder descriptor; the book calls them "cure spells" by name. It
    /// is a tag here so a spell's family is a fact in its file rather than a guess at its id.
    /// </remarks>
    public IReadOnlyList<string> Descriptors { get; init; } = [];

    /// <summary>
    /// Cast with Empower Spell: half as much again of every number it rolls, for a slot two
    /// levels higher. The difficulty class stays the spell's own.
    /// </summary>
    public bool Empowered { get; init; }

    /// <summary>The level of slot it takes: its own, or two more empowered.</summary>
    public int SlotLevel => Level + (Empowered ? 2 : 0);

    public bool Has(string descriptor) => Descriptors.Contains(descriptor, StringComparer.Ordinal);

    /// <summary>The same spell, empowered.</summary>
    public Spell Empower() => this with { Empowered = true };

    /// <summary>Whether this spell needs somewhere to aim rather than someone.</summary>
    public bool NeedsAPoint => Target is BurstTarget or PointTarget;

    public override string ToString() =>
        $"{(Empowered ? "empowered " : string.Empty)}{Name} (level {Level} {School.ToString().ToLowerInvariant()})";
}

/// <summary>Where a spell is being pointed: at somebody, or at a spot on the ground.</summary>
public readonly record struct SpellAim(Creature? Creature, GridSquare? Point)
{
    public static SpellAim At(Creature creature) => new(creature, null);

    public static SpellAim At(GridSquare point) => new(null, point);

    public override string ToString() => Creature?.Name ?? Point?.ToString() ?? "nothing";
}
