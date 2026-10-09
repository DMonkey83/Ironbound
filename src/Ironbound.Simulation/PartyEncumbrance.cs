using Ironbound.Rules.Creatures;
using Ironbound.Rules.Items;

namespace Ironbound.Simulation;

/// <summary>
/// What the whole party carries against what the whole party can.
/// </summary>
/// <param name="Carrying">Everybody on their feet, whose Strength counts.</param>
/// <param name="NotCarrying">Everybody down, whose does not.</param>
public sealed record PartyLoadReport(
    decimal Weight,
    CarryingCapacity Capacity,
    LoadCategory Category,
    IReadOnlyList<Creature> Carrying,
    IReadOnlyList<Creature> NotCarrying)
{
    /// <summary>The line for the log or the inventory screen.</summary>
    public string Line
    {
        get
        {
            var line = $"The party carries {Pricing.Pounds(Weight)}: a {Encumbrance.Name(Category)} load "
                + $"({Capacity}).";

            if (Category == LoadCategory.Overloaded)
            {
                line += " Nobody can walk until something is put down.";
            }

            if (NotCarrying.Count > 0)
            {
                line += $" {string.Join(" and ", NotCarrying.Select(member => member.Name))} "
                    + $"{(NotCarrying.Count == 1 ? "is" : "are")} down and carrying nothing; the rest share it out.";
            }

            return line;
        }
    }

    public override string ToString() => Line;
}

/// <summary>
/// The party's half of <see cref="Encumbrance"/>, which the rules cannot see: the bag belongs to
/// a campaign.
/// </summary>
/// <remarks>
/// The party load reads as the bag shared out by Strength. Everybody's own gear and the bag
/// together, against everybody's limits added up: so a full bag slows the whole party at once,
/// as Wrath's does, and each character then carries the worse of their own load and the party's.
/// </remarks>
public static class PartyEncumbrance
{
    extension(Encumbrance)
    {
        /// <summary>Everything the party carries, bag and coins included, against all their limits.</summary>
        public static PartyLoadReport PartyLoad(Campaign campaign)
        {
            ArgumentNullException.ThrowIfNull(campaign);
            return campaign.MeasureLoad();
        }

        /// <summary>
        /// The load that counts for one of the party: the worse of their own and the party's.
        /// Somebody outside the party only has their own.
        /// </summary>
        public static LoadCategory Effective(Creature creature, Campaign campaign)
        {
            ArgumentNullException.ThrowIfNull(creature);
            ArgumentNullException.ThrowIfNull(campaign);

            var own = Encumbrance.Load(creature).Category;
            return campaign.Party.Contains(creature)
                ? Encumbrance.Worse(own, campaign.MeasureLoad().Category)
                : own;
        }
    }
}
