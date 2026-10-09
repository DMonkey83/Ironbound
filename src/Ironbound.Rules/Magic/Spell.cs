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

/// <summary>
/// The metamagic feats a spell can be cast with, as flags: an empowered, maximized fireball is
/// both. Each one costs a higher slot, which <see cref="Spell.SlotLevel"/> adds up.
/// </summary>
[Flags]
public enum Metamagic
{
    None = 0,

    /// <summary>Half as much again of every number rolled. Two levels.</summary>
    Empower = 1,

    /// <summary>Lasts twice as long. One level.</summary>
    Extend = 2,

    /// <summary>Every number rolled at its most. Three levels.</summary>
    Maximize = 4,

    /// <summary>Twice the area. Three levels.</summary>
    Widen = 8,

    /// <summary>Twice the range. One level.</summary>
    Enlarge = 16,

    /// <summary>A swift action, and it draws no swing. Four levels.</summary>
    Quicken = 32,

    /// <summary>Cast as a higher-level spell in every respect: <see cref="Spell.Heightened"/> says which.</summary>
    Heighten = 64,
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

/// <summary>What kind of creature a spell's effect is for.</summary>
public enum SpellFilterKind
{
    /// <summary>The living — everything but the undead and constructs. What positive energy heals.</summary>
    Living,

    Undead,

    /// <summary>Outsiders of one subtype: "evil" for Alignment Channel, "fire" for Elemental Channel.</summary>
    Outsider,
}

/// <summary>Who one effect of a spell lands on, when it is not everybody the spell reaches.</summary>
public sealed record SpellFilter(SpellFilterKind Kind, string? Subtype = null)
{
    public static SpellFilter Living { get; } = new(SpellFilterKind.Living);

    public static SpellFilter Undead { get; } = new(SpellFilterKind.Undead);

    public static SpellFilter Outsiders(string subtype) => new(SpellFilterKind.Outsider, subtype);

    public bool Matches(Creature creature) => Kind switch
    {
        SpellFilterKind.Living => CreatureTypes.IsLiving(creature),
        SpellFilterKind.Undead => CreatureTypes.IsUndead(creature),
        _ => Subtype is { } subtype && CreatureTypes.IsOutsiderOf(creature, subtype),
    };

    public override string ToString() => Kind switch
    {
        SpellFilterKind.Living => "the living",
        SpellFilterKind.Undead => "the undead",
        _ => $"{Subtype} outsiders",
    };
}

/// <summary>Something a spell does to whoever it lands on.</summary>
public abstract record SpellEffect
{
    /// <summary>
    /// Who this effect is for, or null for everybody the spell reaches. Channel energy is the
    /// reason: one burst that heals the living and burns the undead is two effects, each for one.
    /// </summary>
    public SpellFilter? Only { get; init; }

    /// <summary>Whether this effect lands on a creature at all.</summary>
    public bool AppliesTo(Creature creature) => Only?.Matches(creature) ?? true;

    /// <summary>Whether it is something a target would rather avoid: damage, or anything hung on it.</summary>
    public bool IsHarmful => this is DealDamage or Bestow;
}

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
    /// Whether spell resistance can stop it. Nearly everything that does something to a foe;
    /// grease, which makes the floor slippery rather than doing anything to anybody, is the
    /// exception in the game so far.
    /// </summary>
    public bool AllowsResistance { get; init; } = true;

    /// <summary>The metamagic it is being cast with, if any.</summary>
    public Metamagic Metamagic { get; init; }

    /// <summary>
    /// Cast with Empower Spell: half as much again of every number it rolls, for a slot two
    /// levels higher. The difficulty class stays the spell's own.
    /// </summary>
    public bool Empowered
    {
        get => Metamagic.HasFlag(Metamagic.Empower);
        init => Metamagic = value ? Metamagic | Metamagic.Empower : Metamagic & ~Metamagic.Empower;
    }

    /// <summary>
    /// The level Heighten Spell raised it to, or nought for a spell cast at its own level. A
    /// heightened spell is that level for its difficulty class and everything else.
    /// </summary>
    public int Heightened { get; init; }

    /// <summary>The level it counts as: its own, or the one it was heightened to.</summary>
    public int EffectiveLevel => Math.Max(Level, Heightened);

    /// <summary>
    /// The level of slot it takes: the level it counts as, and one to four more for each other
    /// metamagic feat on it — Empower's two, Extend's and Enlarge's one, Maximize's and Widen's
    /// three, Quicken's four.
    /// </summary>
    public int SlotLevel => EffectiveLevel
        + (Metamagic.HasFlag(Metamagic.Empower) ? 2 : 0)
        + (Metamagic.HasFlag(Metamagic.Extend) ? 1 : 0)
        + (Metamagic.HasFlag(Metamagic.Maximize) ? 3 : 0)
        + (Metamagic.HasFlag(Metamagic.Widen) ? 3 : 0)
        + (Metamagic.HasFlag(Metamagic.Enlarge) ? 1 : 0)
        + (Metamagic.HasFlag(Metamagic.Quicken) ? 4 : 0);

    /// <summary>The action it takes to cast: a swift one quickened, its own otherwise.</summary>
    public ActionCost CastAs => Metamagic.HasFlag(Metamagic.Quicken) ? ActionCost.Swift : CastingTime;

    public bool Has(string descriptor) => Descriptors.Contains(descriptor, StringComparer.Ordinal);

    /// <summary>The same spell, empowered.</summary>
    public Spell Empower() => this with { Empowered = true };

    /// <summary>The same spell with one more metamagic feat on it.</summary>
    public Spell With(Metamagic metamagic) => this with { Metamagic = Metamagic | metamagic };

    /// <summary>The same spell, heightened to a level higher than its own.</summary>
    public Spell Heighten(int level)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(level, Level);
        return this with { Heightened = level, Metamagic = Metamagic | Metamagic.Heighten };
    }

    /// <summary>Whether this spell needs somewhere to aim rather than someone.</summary>
    public bool NeedsAPoint => Target is BurstTarget or PointTarget;

    /// <summary>
    /// How far it carries as cast: its range, and twice that enlarged — for the three ranges
    /// that grow with the caster, which are the only ones Enlarge Spell can stretch.
    /// </summary>
    public int RangeInFeet(Creature caster, int level)
    {
        var feet = Range.InFeet(caster, level);
        return Metamagic.HasFlag(Metamagic.Enlarge) && Range.Kind is SpellRangeKind.Close or SpellRangeKind.Medium or SpellRangeKind.Long
            ? feet * 2
            : feet;
    }

    /// <summary>Whether it carries from one square to another as cast.</summary>
    public bool Reaches(Creature caster, GridSquare from, GridSquare to, int level) =>
        Range.Kind is SpellRangeKind.Touch or SpellRangeKind.Personal
            ? Range.Reaches(caster, from, to, level)
            : Distance.Between(from, to) <= RangeInFeet(caster, level);

    /// <summary>"empowered, maximized ", or nothing: what the log puts before the spell's name.</summary>
    public string MetamagicWords
    {
        get
        {
            var words = Enum.GetValues<Metamagic>()
                .Where(flag => flag != Metamagic.None && Metamagic.HasFlag(flag))
                .Select(flag => flag switch
                {
                    Metamagic.Empower => "empowered",
                    Metamagic.Extend => "extended",
                    Metamagic.Maximize => "maximized",
                    Metamagic.Widen => "widened",
                    Metamagic.Enlarge => "enlarged",
                    Metamagic.Quicken => "quickened",
                    _ => $"heightened to {Heightened}",
                })
                .ToList();

            return words.Count == 0 ? string.Empty : string.Join(", ", words) + " ";
        }
    }

    public override string ToString() =>
        $"{MetamagicWords}{Name} (level {EffectiveLevel} {School.ToString().ToLowerInvariant()})";
}

/// <summary>Where a spell is being pointed: at somebody, or at a spot on the ground.</summary>
public readonly record struct SpellAim(Creature? Creature, GridSquare? Point)
{
    public static SpellAim At(Creature creature) => new(creature, null);

    public static SpellAim At(GridSquare point) => new(null, point);

    public override string ToString() => Creature?.Name ?? Point?.ToString() ?? "nothing";
}
