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
    /// <param name="who">Who may take one, when not everybody may: Greater Bull Rush lets the
    /// shover's friends swing at the foe it drives back, and not the shover.</param>
    /// <param name="stopped">Where a Stand Still that held the mover is written, for a walk that
    /// has to know to stop. Null when nothing is walking.</param>
    public static IReadOnlyList<StrikeResult> Provoke(
        Encounter encounter,
        Creature mover,
        GridSquare leaving,
        bool walking = false,
        Func<Creature, bool>? who = null,
        List<ManeuverResult>? stopped = null)
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
                || already?.Contains(threatener) == true
                || who?.Invoke(threatener) == false)
            {
                continue;
            }

            already?.Add(threatener);
            combatant.OpportunitiesUsed++;

            // Stand Still: the swing spent instead on a manoeuvre that stops the walker dead.
            if (walking && stopped is not null && StandsStill(field, threatener, mover, leaving))
            {
                var check = Maneuvers.Attempt(threatener, mover, encounter.Random, ManeuverKind.StandStill);
                stopped.Add(check);

                if (check.Succeeded)
                {
                    break;
                }

                continue;
            }

            // Mobility is four of dodge against exactly this: the swing a walk draws.
            var strike = Strike.Resolve(
                threatener,
                weapon,
                mover,
                encounter.Random,
                walking ? DefenseOptions.Moving : DefenseOptions.None,
                encounter.Rules,
                field,
                flatFooted: encounter.IsFlatFootedTo(mover, threatener));

            encounter.AfterStrike(strike);
            taken.Add(strike);

            // Dropped before it could get away. Nobody else gets a swing at a falling target.
            if (!mover.IsConscious)
            {
                break;
            }
        }

        return taken;
    }

    /// <summary>
    /// Whether a creature with Stand Still spends this opportunity on it: the walker is going past
    /// a square next to it, and the check is likelier to hold it than not.
    /// </summary>
    /// <remarks>
    /// Better than even odds or a swing instead: a failed Stand Still is an opportunity thrown
    /// away, and a swing at least does damage. Nobody is asked, as nobody is asked about any
    /// attack of opportunity.
    /// </remarks>
    private static bool StandsStill(Battlefield field, Creature threatener, Creature mover, GridSquare leaving) =>
        threatener.HasFeat(Feats.FeatEffect.StandStill)
        && field.SquareOf(threatener) is { } standing
        && Distance.AreAdjacent(standing, leaving)
        && Maneuvers.Bonus(threatener, ManeuverKind.StandStill).Total + 11 >= Maneuvers.Defense(mover, ManeuverKind.StandStill);

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
