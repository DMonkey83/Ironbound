using Ironbound.Rules.Combat;
using Ironbound.Rules.Effects;
using Ironbound.Rules.Modifiers;
using Ironbound.Rules.Saves;

namespace Ironbound.Rules.Magic;

/// <summary>
/// A handful of spells, written out.
/// </summary>
/// <remarks>
/// Sample content rather than a library. Each is a direct transcription of its rulebook entry,
/// which is the point of the declarative shape — when spells move to content files, these become
/// the first five rows and this class goes away.
/// </remarks>
public static class Spells
{
    /// <summary>Unerring force. No attack roll, no save: it simply arrives.</summary>
    public static Spell MagicMissile { get; } = new("Magic Missile", 1, SpellSchool.Evocation)
    {
        Range = SpellRange.Medium,
        Target = new SingleTarget(),
        Affects = SpellAffects.Enemies,
        Does = [new DealDamage(
            SpellDice.PerLevel(sides: 4, maximumDice: 5, levelsPerDie: 2, flatPerDie: 1),
            DamageType.Force)],
    };

    /// <summary>
    /// A healing touch.
    /// </summary>
    /// <remarks>
    /// Simplified: the rules scale the flat part with caster level to a maximum of five, and this
    /// simply uses the maximum. Scaling a flat bonus needs another dial on <see cref="SpellDice"/>
    /// that nothing else wants yet.
    /// </remarks>
    public static Spell CureLightWounds { get; } = new("Cure Light Wounds", 1, SpellSchool.Conjuration)
    {
        Range = SpellRange.Touch,
        Target = new SingleTarget(),
        Affects = SpellAffects.Allies,
        Does = [new Restore(SpellDice.Fixed("1d8+5"))],
    };

    /// <summary>
    /// A beam of fire needing a ranged touch attack.
    /// </summary>
    /// <remarks>
    /// Simplified: the rules grant a second ray at seventh level and a third at eleventh, each
    /// needing its own attack roll. This fires one.
    /// </remarks>
    public static Spell ScorchingRay { get; } = new("Scorching Ray", 2, SpellSchool.Evocation)
    {
        Range = SpellRange.Close,
        Target = new RayTarget(),
        Affects = SpellAffects.Enemies,
        Does = [new DealDamage(SpellDice.Fixed("4d6"), DamageType.Fire)],
    };

    /// <summary>Heart in the fight: a morale bonus to everyone on your side nearby.</summary>
    public static Spell Bless { get; } = new("Bless", 1, SpellSchool.Enchantment)
    {
        Range = SpellRange.Personal,
        Target = new BurstTarget(RadiusFeet: 50),
        Affects = SpellAffects.Allies,
        Does = [new Bestow("Bless", level => new ModifierEffect("Bless", Duration.Minutes(level))
            .GrantsToAttack(1, BonusType.Morale))],
    };

    /// <summary>
    /// The one everybody knows. Note <see cref="SpellAffects.Everyone"/>: it does not care whose
    /// side you are on, which is the whole tactical problem with it.
    /// </summary>
    public static Spell Fireball { get; } = new("Fireball", 3, SpellSchool.Evocation)
    {
        Range = SpellRange.Long,
        Target = new BurstTarget(RadiusFeet: 20),
        Affects = SpellAffects.Everyone,
        Save = Save.Reflex,
        OnSave = SaveOutcome.Half,
        Does = [new DealDamage(SpellDice.PerLevel(sides: 6, maximumDice: 10), DamageType.Fire)],
    };

    public static IReadOnlyList<Spell> All { get; } =
        [MagicMissile, CureLightWounds, ScorchingRay, Bless, Fireball];
}
