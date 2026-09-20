namespace Ironbound.Rules.Combat;

/// <summary>
/// How many swings a full attack is worth, and what each one after the first costs in accuracy.
/// </summary>
/// <remarks>
/// This is the mechanical heart of levelling up in a d20 game, and the reason a base attack bonus
/// is worth more than the number suggests. A fighter at +6 does not merely hit slightly more
/// often than one at +5 — they swing twice. Everything else about class progression is arithmetic
/// on top of that step.
/// <para>
/// It is also what makes standing still a decision. The extra swings only come from a full
/// attack, which costs the whole round, so every step taken is an attack given up.
/// </para>
/// </remarks>
public static class Iteratives
{
    /// <summary>Each extra attack comes five points down from the one before.</summary>
    public const int Step = 5;

    /// <summary>Four, at +16. Nothing in the core rules goes past it from base attack alone.</summary>
    public const int Maximum = 4;

    /// <summary>
    /// How many attacks a full attack yields: a second at +6, a third at +11, a fourth at +16.
    /// Always at least one, since even the feeblest creature gets to swing.
    /// </summary>
    public static int Count(int baseAttackBonus) => baseAttackBonus < Step + 1
        ? 1
        : Math.Min(Maximum, 1 + ((baseAttackBonus - 1) / Step));

    /// <summary>
    /// The accuracy penalty on each attack in order: nothing, then -5, -10, -15. Returned as a
    /// list rather than computed at each call site so the sequence has exactly one definition.
    /// </summary>
    public static IReadOnlyList<int> Penalties(int baseAttackBonus)
    {
        var count = Count(baseAttackBonus);
        var penalties = new int[count];

        for (var index = 0; index < count; index++)
        {
            penalties[index] = -index * Step;
        }

        return penalties;
    }
}
