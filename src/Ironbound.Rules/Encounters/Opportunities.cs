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
    public static IReadOnlyList<StrikeResult> Provoke(
        Encounter encounter,
        Creature mover,
        GridSquare leaving)
    {
        ArgumentNullException.ThrowIfNull(encounter);
        ArgumentNullException.ThrowIfNull(mover);

        if (encounter.Battlefield is not { } field)
        {
            return [];
        }

        var taken = new List<StrikeResult>();

        // In initiative order, so a replay resolves them in the same sequence every time.
        foreach (var combatant in encounter.Order)
        {
            var threatener = combatant.Creature;

            if (!threatener.IsEnemyOf(mover)
                || !combatant.CanTakeOpportunity
                || threatener.MeleeAttack is not { } weapon
                || !field.Threatens(threatener, leaving))
            {
                continue;
            }

            combatant.OpportunitiesUsed++;
            taken.Add(Strike.Resolve(
                threatener,
                weapon,
                mover,
                encounter.Random,
                DefenseOptions.None,
                encounter.Rules,
                field));

            // Dropped before it could get away. Nobody else gets a swing at a falling target.
            if (!mover.IsConscious)
            {
                break;
            }
        }

        return taken;
    }
}
