using Ironbound.Rules.Conditions;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Dice;
using Ironbound.Rules.Feats;
using Ironbound.Rules.Modifiers;

namespace Ironbound.Rules.Magic;

/// <summary>Why a caster had to keep hold of a spell.</summary>
public enum ConcentrationReason
{
    /// <summary>Casting in somebody's reach without giving them an opening: 15 + twice the level.</summary>
    Defensive,

    /// <summary>Hurt in the middle of it: 10 + the damage + the level.</summary>
    Injured,

    /// <summary>Tangled up in something: 15 + the level.</summary>
    Entangled,
}

/// <summary>One concentration check, with everything needed to explain it.</summary>
public sealed record ConcentrationCheck(
    Creature Caster,
    ConcentrationReason Reason,
    int NaturalRoll,
    ModifierBreakdown Bonus,
    int Difficulty)
{
    public int Total => NaturalRoll + Bonus.Total;

    public bool Succeeded => Total >= Difficulty;

    public override string ToString()
    {
        var why = Reason switch
        {
            ConcentrationReason.Defensive => "casting defensively",
            ConcentrationReason.Injured => "hurt while casting",
            _ => "entangled",
        };

        return $"{Caster.Name} concentration ({why}): d20 [{NaturalRoll}] {Bonus.Total:+0;-0;+0} = {Total} "
            + $"vs DC {Difficulty} — {(Succeeded ? "keeps the spell" : "loses the spell")}";
    }
}

/// <summary>
/// Keeping hold of a spell when something is trying to make you let go of it: a d20, the caster
/// level, and the ability the spells are cast with.
/// </summary>
/// <remarks>
/// Not a skill check and not an ability check, so being shaken does not reach it — the book has
/// it as a check of its own. Combat Casting's four count only when casting defensively, which is
/// the one situation here it is for.
/// </remarks>
public static class Concentration
{
    public const int CombatCastingBonus = 4;

    /// <summary>The chance one in five of fumbling a spoken spell when deafened, as a percentage.</summary>
    public const int DeafenedFailurePercent = 20;

    /// <summary>Fifteen plus twice the spell's level: what casting defensively has to meet.</summary>
    public static int DefensiveDifficulty(int spellLevel) => 15 + (2 * spellLevel);

    /// <summary>Ten, the damage taken, and the spell's level.</summary>
    public static int InjuredDifficulty(int damage, int spellLevel) => 10 + damage + spellLevel;

    /// <summary>Fifteen plus the spell's level.</summary>
    public static int EntangledDifficulty(int spellLevel) => 15 + spellLevel;

    /// <summary>The bonus on the check: caster level, casting ability, and Combat Casting when it counts.</summary>
    public static ModifierBreakdown Bonus(Creature caster, int casterLevel, ConcentrationReason reason)
    {
        ArgumentNullException.ThrowIfNull(caster);

        var stack = new ModifierStack();

        if (casterLevel != 0)
        {
            stack.Add(casterLevel, BonusType.Untyped, "Caster level");
        }

        var ability = caster.Spells.CastingAbility;
        if (caster.Abilities[ability].Modifier is not 0 and var modifier)
        {
            stack.Add(modifier, BonusType.Untyped, Abilities.AbilityInfo.Abbreviate(ability));
        }

        if (reason == ConcentrationReason.Defensive && caster.HasFeat(FeatEffect.CombatCasting))
        {
            stack.Add(CombatCastingBonus, BonusType.Untyped, "Combat Casting");
        }

        return stack.Explain();
    }

    public static ConcentrationCheck Check(
        Creature caster, int casterLevel, ConcentrationReason reason, int difficulty, IRandomSource random)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(random);

        return new ConcentrationCheck(caster, reason, random.NextDie(20), Bonus(caster, casterLevel, reason), difficulty);
    }

    /// <summary>
    /// The chance, as a percentage, of a defensive cast holding: what the autopilot weighs
    /// against the swing an ordinary cast would draw.
    /// </summary>
    public static int DefensiveChance(Creature caster, int casterLevel, int spellLevel)
    {
        ArgumentNullException.ThrowIfNull(caster);

        var needed = DefensiveDifficulty(spellLevel) - Bonus(caster, casterLevel, ConcentrationReason.Defensive).Total;
        return Math.Clamp((21 - needed) * 5, 0, 100);
    }

    /// <summary>
    /// Everything that can make a caster lose a spell at the moment of casting it, rolled in
    /// order: casting defensively, the damage from any swing it drew, being entangled. The first
    /// failure loses the spell, and nothing after it is rolled.
    /// </summary>
    /// <param name="damage">What the swings drawn by casting dealt, all told.</param>
    public static IReadOnlyList<ConcentrationCheck> Hold(
        Creature caster, int casterLevel, int spellLevel, bool defensively, int damage, IRandomSource random)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(random);

        var checks = new List<ConcentrationCheck>();

        bool Roll(ConcentrationReason reason, int difficulty)
        {
            var check = Check(caster, casterLevel, reason, difficulty, random);
            checks.Add(check);
            return check.Succeeded;
        }

        _ = (!defensively || Roll(ConcentrationReason.Defensive, DefensiveDifficulty(spellLevel)))
            && (damage <= 0 || Roll(ConcentrationReason.Injured, InjuredDifficulty(damage, spellLevel)))
            && (!caster.Has(Condition.Entangled) || Roll(ConcentrationReason.Entangled, EntangledDifficulty(spellLevel)));

        return checks;
    }

    /// <summary>Whether any of a set of checks lost the spell.</summary>
    public static bool Lost(IEnumerable<ConcentrationCheck> checks) => checks.Any(check => !check.Succeeded);
}
