using Ironbound.Rules.Combat;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Defense;
using Ironbound.Rules.Maps;

namespace Ironbound.Rules.Encounters;

/// <summary>
/// Attacks of opportunity: free swings taken at somebody for doing something careless within
/// reach.
/// </summary>
/// <remarks>
/// Nobody is asked whether to take one. Asking would mean blocking on a decision in the middle
/// of another creature's turn, which is exactly what the non-blocking scheduler forbids — so an
/// opportunity is taken whenever one is available. That is also what the CRPGs this is modelled
/// on do.
/// <para>
/// Only enemies take them. Strictly, anyone who threatens the square may; allies choosing to
/// maul each other is not a rule worth honouring.
/// </para>
/// <para>
/// A bow is no use here. Whoever takes the swing takes it with something they can swing.
/// </para>
/// </remarks>
public static class Opportunities
{
    /// <summary>
    /// Resolves every opportunity provoked by <paramref name="mover"/> leaving
    /// <paramref name="leaving"/>. The mover is expected to still be standing there.
    /// </summary>
    /// <param name="walking">True when it is the walk itself that provokes. Moving out of
    /// several squares one enemy threatens is one opportunity for that enemy all turn, however
    /// many attacks of opportunity it has to spend.</param>
    public static IReadOnlyList<StrikeResult> Provoke(
        Encounter encounter,
        Creature mover,
        GridSquare leaving,
        bool walking = false)
    {
        ArgumentNullException.ThrowIfNull(encounter);
        ArgumentNullException.ThrowIfNull(mover);

        if (encounter.Battlefield is not { } field)
        {
            return [];
        }

        var taken = new List<StrikeResult>();
        var already = walking
            ? encounter.Order.FirstOrDefault(entry => ReferenceEquals(entry.Creature, mover))?.WalkedAwayFrom
            : null;

        // In initiative order, so a replay resolves them in the same sequence every time.
        foreach (var combatant in encounter.Order)
        {
            var threatener = combatant.Creature;

            if (!threatener.IsEnemyOf(mover)
                || !combatant.CanTakeOpportunity
                || threatener.MeleeAttack is not { } weapon
                || !field.Threatens(threatener, leaving)
                || already?.Contains(threatener) == true)
            {
                continue;
            }

            already?.Add(threatener);
            combatant.OpportunitiesUsed++;
            taken.Add(Strike.Resolve(
                threatener,
                weapon,
                mover,
                encounter.Random,
                DefenseOptions.None,
                encounter.Rules,
                field,
                flatFooted: encounter.IsFlatFootedTo(mover, threatener)));

            // Dropped before it could get away. Nobody else gets a swing at a falling target.
            if (!mover.IsConscious)
            {
                break;
            }
        }

        return taken;
    }

    /// <summary>
    /// A rogue's opportunist talent: once a round, a free swing at a foe an ally has just struck
    /// in melee. Taken out of her ordinary allowance of attacks of opportunity.
    /// </summary>
    /// <remarks>
    /// Asked after every melee blow that lands, by whatever landed it. Like every opportunity it
    /// is taken whenever it is available rather than offered, for the same reason: asking would
    /// mean stopping in the middle of somebody else's turn.
    /// </remarks>
    public static IReadOnlyList<StrikeResult> Opportunist(Encounter encounter, Creature striker, Creature struck)
    {
        ArgumentNullException.ThrowIfNull(encounter);
        ArgumentNullException.ThrowIfNull(striker);
        ArgumentNullException.ThrowIfNull(struck);

        if (encounter.Battlefield is not { } field || field.SquareOf(struck) is not { } square || !struck.IsConscious)
        {
            return [];
        }

        var taken = new List<StrikeResult>();

        foreach (var combatant in encounter.Order)
        {
            var rogue = combatant.Creature;

            if (!rogue.IsAllyOf(striker)
                || !rogue.IsEnemyOf(struck)
                || combatant.HasUsedOpportunist
                || !combatant.CanTakeOpportunity
                || !rogue.Choices.HasTalent(Classes.TalentEffect.Opportunist)
                || rogue.MeleeAttack is not { } weapon
                || !field.Threatens(rogue, square))
            {
                continue;
            }

            combatant.HasUsedOpportunist = true;
            combatant.OpportunitiesUsed++;
            taken.Add(Strike.Resolve(
                rogue,
                weapon,
                struck,
                encounter.Random,
                DefenseOptions.None,
                encounter.Rules,
                field,
                flatFooted: encounter.IsFlatFootedTo(struck, rogue)));

            if (!struck.IsConscious)
            {
                break;
            }
        }

        return taken;
    }
}
