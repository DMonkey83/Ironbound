using Ironbound.Rules.Creatures;

namespace Ironbound.Rules.Encounters;

/// <summary>
/// A creature's place in an encounter: what it rolled, when it next acts, and what it has left
/// to spend this turn.
/// </summary>
/// <remarks>
/// <see cref="NextTurnTick"/> is the heart of the scheduler. Each combatant carries its own round
/// clock rather than sharing a global round boundary, so "until the start of your next turn"
/// means exactly that — and the same arrangement is what a real-time scheduler would need.
/// </remarks>
public sealed class Combatant
{
    internal Combatant(Creature creature, int naturalRoll, int initiative)
    {
        Creature = creature;
        NaturalRoll = naturalRoll;
        Initiative = initiative;
    }

    public Creature Creature { get; }

    /// <summary>The d20 alone, for the log.</summary>
    public int NaturalRoll { get; }

    public int Initiative { get; }

    /// <summary>Absolute tick at which this combatant next acts.</summary>
    public long NextTurnTick { get; internal set; }

    public ActionBudget Budget { get; } = new();

    /// <summary>Unconscious and dead combatants are skipped, but their effects keep running.</summary>
    public bool IsActive => Creature.IsConscious;

    /// <summary>Opportunities spent since this creature's last turn.</summary>
    public int OpportunitiesUsed { get; internal set; }

    /// <summary>Whether it could swing at somebody leaving its reach right now.</summary>
    /// <remarks>
    /// Being dazed, stunned or asleep takes the opportunities away with the turn. A swing at
    /// somebody walking past is still an action, and a creature that can take none of those
    /// cannot take that one either — least of all one that is snoring.
    /// <para>
    /// So does a rogue's slow reactions: somebody she has sneak attacked spends the next round
    /// too off balance to punish anybody.
    /// </para>
    /// </remarks>
    public bool CanTakeOpportunity =>
        IsActive && Creature.CanAct && OpportunitiesUsed < Creature.AttacksOfOpportunityPerRound
        && !Classes.SneakAttack.IsSlowed(Creature);

    /// <summary>Set by ordinary movement. Bars a five-foot step for the rest of the turn.</summary>
    /// <summary>
    /// Whether they have had a turn yet.
    /// </summary>
    /// <remarks>
    /// Until you act you are flat-footed, which is the rule that makes initiative worth caring
    /// about and Improved Initiative worth a feat. It was simply missing: everybody started a
    /// fight with their full armour class regardless of who moved first.
    /// </remarks>
    public bool HasActed { get; internal set; }

    /// <summary>
    /// Whether they walked into this without knowing it was happening.
    /// </summary>
    /// <remarks>
    /// Set when somebody's Stealth beat their Perception. An unaware combatant is flat-footed
    /// and loses their first turn — which is what an ambush <em>is</em>, and what the opening
    /// encounter has been called since long before it could do it.
    /// </remarks>
    public bool IsUnaware { get; internal set; }

    /// <summary>Whether they started the fight unaware, which is what makes round one a surprise round.</summary>
    public bool WasSurprised { get; internal set; }

    /// <summary>Whether a rogue's opportunist talent has been used since her last turn.</summary>
    public bool HasUsedOpportunist { get; internal set; }

    /// <summary>
    /// Caught with their guard down: before their first turn, or surprised entirely — or, for
    /// the length of a round, running flat out without the Run feat to keep their footing.
    /// </summary>
    public bool IsFlatFooted => !HasActed || IsUnaware || IsRunning;

    /// <summary>
    /// Ran last turn without the Run feat, and so has no Dexterity to armour class until its next
    /// turn begins.
    /// </summary>
    public bool IsRunning { get; internal set; }

    /// <summary>Stopped dead by a Stand Still: no more walking this turn.</summary>
    public bool IsHeld { get; internal set; }

    /// <summary>
    /// Followed a foe with Step Up since its last turn. That was an immediate action: this turn
    /// it has no swift action, no five-foot step, and five feet less to walk.
    /// </summary>
    public bool SteppedUp { get; internal set; }

    /// <summary>
    /// Whether it took Step Up's step before this turn began: what is owed out of this turn.
    /// Set from <see cref="SteppedUp"/> as the turn begins.
    /// </summary>
    public bool OwesStep { get; internal set; }

    /// <summary>Lunging this turn: five more feet of reach on its own attacks until the turn ends.</summary>
    public bool IsLunging { get; internal set; }

    /// <summary>
    /// How much difficult ground it has crossed this round as though it were clear: Nimble Moves
    /// buys five feet of it a round, and Acrobatic Steps twenty.
    /// </summary>
    public int EasyGroundUsed { get; internal set; }

    /// <summary>
    /// Whether this one can do anything at all this turn.
    /// </summary>
    /// <remarks>
    /// Both halves of the question in one place. A creature knows whether it is conscious and
    /// whether something has dazed it, but being <em>surprised</em> is a fact about this fight
    /// rather than about the creature, so it lives here — and anything asking "is there a
    /// decision to make?" has to ask both or it will hand somebody a turn they cannot use.
    /// </remarks>
    public bool CanAct => Creature.CanAct && !IsUnaware;

    public bool HasMoved { get; internal set; }

    /// <summary>
    /// Whoever has already had their swing at this creature for walking out of their reach
    /// this turn. Leaving three squares a werewolf threatens is one opportunity, not three,
    /// whatever Combat Reflexes says about how many it may take.
    /// </summary>
    /// <remarks>
    /// Not saved. Reloading in the middle of a turn and walking out of the same reach a second
    /// time would give that one enemy a second swing; that is the whole cost.
    /// </remarks>
    internal HashSet<Creature> WalkedAwayFrom { get; } = [];

    /// <summary>Set by a five-foot step. Bars ordinary movement for the rest of the turn.</summary>
    public bool HasTakenFiveFootStep { get; internal set; }

    /// <summary>
    /// Everything that refreshes at the start of a creature's own turn — which is exactly when
    /// the rules say the opportunity allotment comes back.
    /// </summary>
    internal void BeginTurn()
    {
        Budget.Reset();

        // Standing, but only just: at exactly nought hit points you get one action a round. So
        // does anybody staggered, and anybody fighting on below nought, who is staggered too.
        if (Creature.HitPoints.State == HitPointState.Disabled
            || Creature.HitPoints.IsFightingOn
            || Creature.Conditions.Any(condition => Conditions.ConditionInfo.Of(condition).SingleAction))
        {
            Budget.RestrictToSingleAction();
        }

        OpportunitiesUsed = 0;
        HasUsedOpportunist = false;
        HasMoved = false;
        HasTakenFiveFootStep = false;
        WalkedAwayFrom.Clear();

        // Step Up's step was this turn's immediate action, taken early: no swift action now.
        OwesStep = SteppedUp;
        SteppedUp = false;
        if (OwesStep)
        {
            Budget.SpendSwift();
        }

        IsRunning = false;
        IsHeld = false;
        IsLunging = false;
        EasyGroundUsed = 0;
    }

    public override string ToString() => $"{Creature.Name} (initiative {Initiative})";
}
