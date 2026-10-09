namespace Ironbound.Rules.Combat;

/// <summary>
/// What a weapon with more than one damage type does with them.
/// </summary>
/// <remarks>
/// The tables write "P or S" for a dagger and "B and P" for a morningstar, and the two mean
/// different things against damage reduction. A dagger is one or the other, whichever the
/// wielder likes; a morningstar is both at once, so it gets past reduction that either type
/// would.
/// </remarks>
public enum DamageRule
{
    /// <summary>One type, the usual case.</summary>
    Single,

    /// <summary>"P or S": the wielder picks whichever does best against this target.</summary>
    Either,

    /// <summary>"B and P": every type at once, for getting past damage reduction.</summary>
    Both,
}
