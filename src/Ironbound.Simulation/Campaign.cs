using Ironbound.Rules;
using Ironbound.Rules.Content;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Persistence;

namespace Ironbound.Simulation;

/// <summary>Where a campaign stands.</summary>
public enum CampaignState
{
    /// <summary>A fight is open.</summary>
    Fighting,

    /// <summary>The fight is won and there is another one waiting.</summary>
    Between,

    /// <summary>Every encounter is behind them.</summary>
    Won,

    /// <summary>The party is down. There is no next fight.</summary>
    Lost,
}

/// <summary>
/// A run of encounters fought by one party.
/// </summary>
/// <remarks>
/// The party persists and the opposition does not, which is the entire point. A fight on its own
/// is won or lost on its own terms; a sequence is won or lost on what is left when you reach the
/// end. Wounds carry, spells stay spent, and the decision about when to spend the one rest you
/// are allowed is the first real strategic choice in the game.
/// <para>
/// It lives here rather than in the rules because "which side is the player's" is a question the
/// rules deliberately do not answer — the same reason <see cref="Battle"/> is here.
/// </para>
/// </remarks>
public sealed class Campaign
{
    private readonly ContentLibrary _library;
    private readonly RuleOptions? _rules;
    private readonly ulong _seed;
    private readonly Dictionary<string, Creature> _party = new(StringComparer.Ordinal);

    private Campaign(
        ContentLibrary library, CampaignDefinition definition, ulong seed, RuleOptions? rules)
    {
        _library = library;
        _rules = rules;
        _seed = seed;

        Definition = definition;
        RestsRemaining = definition.Rests;
    }

    public static Campaign Begin(
        ContentLibrary library,
        string campaignId,
        ulong seed = 20260920,
        RuleOptions? rules = null)
    {
        ArgumentNullException.ThrowIfNull(library);

        var definition = library.GetCampaign(campaignId)
            ?? throw new ArgumentException($"No campaign called '{campaignId}'.", nameof(campaignId));

        var campaign = new Campaign(library, definition, seed, rules);
        campaign.Advance();

        return campaign;
    }

    public CampaignDefinition Definition { get; }

    /// <summary>Which encounter is being fought, counting from one.</summary>
    public int Chapter { get; private set; }

    public int RestsRemaining { get; private set; }

    /// <summary>The fight currently open, or the last one fought.</summary>
    public Battle Battle { get; private set; } = null!;

    /// <summary>The party as they stand, wounds and spent slots and all.</summary>
    public IReadOnlyList<Creature> Party => Battle.Party;

    public CampaignState State
    {
        get
        {
            if (Battle.Outcome == BattleOutcome.InProgress)
            {
                return CampaignState.Fighting;
            }

            if (Battle.Outcome != BattleOutcome.PartyWon)
            {
                return CampaignState.Lost;
            }

            return Chapter >= Definition.Encounters.Count ? CampaignState.Won : CampaignState.Between;
        }
    }

    public bool CanRest => RestsRemaining > 0 && State == CampaignState.Between;

    public bool CanAdvance => State == CampaignState.Between;

    /// <summary>
    /// Stops to recover: every wound closed, every slot back, every lingering effect gone.
    /// </summary>
    /// <remarks>
    /// Deliberately total. A partial recovery would need a clock the game has no use for yet,
    /// and the interesting question is not <em>how much</em> a rest gives back but whether this
    /// is the moment to spend one.
    /// </remarks>
    public bool Rest()
    {
        if (!CanRest)
        {
            return false;
        }

        RestsRemaining--;

        foreach (var creature in Party)
        {
            creature.Effects.Clear();
            creature.HitPoints.Restore();
            creature.Spells.Rest();
        }

        return true;
    }

    /// <summary>Moves on to the next fight, carrying the party into it as they are.</summary>
    public bool Advance()
    {
        if (Battle is not null && !CanAdvance)
        {
            return false;
        }

        var encounter = Definition.Encounters[Chapter];
        Chapter++;

        // A different stream per chapter, so the second fight does not replay the first one's
        // dice, and so a campaign is still reproducible from one seed.
        Battle = Scenarios.Build(_library, encounter, _seed + (ulong)Chapter, _rules, _party);

        return true;
    }

    /// <summary>The whole run, written down: where it has got to and the fight in progress.</summary>
    public string ToJson() =>
        GameSave.ToJson(GameSave.Capture(
            Battle.Encounter,
            new SavedCampaign(Definition.Id, Chapter, RestsRemaining, _seed)));

    /// <summary>
    /// Reads one back. Refuses a save with no campaign in it rather than inventing one.
    /// </summary>
    public static Campaign FromJson(string json, ContentLibrary library)
    {
        ArgumentNullException.ThrowIfNull(library);

        var save = GameSave.FromJson(json);
        if (save.Campaign is not { } state)
        {
            throw new InvalidDataException("That save is a single fight, not a campaign.");
        }

        var definition = library.GetCampaign(state.Id) ?? throw new InvalidDataException(
            $"The save is of a campaign '{state.Id}', which no content file defines.");

        return Restore(
            library,
            definition,
            state.Chapter,
            state.RestsRemaining,
            state.Seed,
            save.Rules,
            Battle.Restore(save, library));
    }

    /// <summary>Puts a campaign back where a save left it, around an already-restored fight.</summary>
    internal static Campaign Restore(
        ContentLibrary library,
        CampaignDefinition definition,
        int chapter,
        int restsRemaining,
        ulong seed,
        RuleOptions? rules,
        Battle battle)
    {
        var campaign = new Campaign(library, definition, seed, rules)
        {
            Chapter = chapter,
            RestsRemaining = restsRemaining,
            Battle = battle,
        };

        foreach (var creature in battle.Party)
        {
            campaign._party[creature.DefinitionId ?? creature.Name] = creature;
        }

        return campaign;
    }

    public override string ToString() =>
        $"{Definition.Name}: chapter {Chapter} of {Definition.Encounters.Count}, "
        + $"{RestsRemaining} rest(s) left — {State}";
}
