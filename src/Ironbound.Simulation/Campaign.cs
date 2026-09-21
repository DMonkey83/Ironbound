using Ironbound.Rules;
using Ironbound.Rules.Classes;
using Ironbound.Rules.Content;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Items;
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
    private readonly List<ItemDefinition> _stash = [];
    private int _lootedChapter;

    private Campaign(
        ContentLibrary library, CampaignDefinition definition, ulong seed, RuleOptions? rules)
    {
        _library = library;
        _rules = rules;
        _seed = seed;

        Definition = definition;
        RestsRemaining = definition.Rests;
    }

    /// <summary>
    /// What the party has earned. A shared pool rather than a tally each: the roster is fixed,
    /// so splitting it would be the same numbers with more arithmetic.
    /// </summary>
    public int Experience { get; private set; }

    /// <summary>The level that much experience is worth.</summary>
    public int EarnedLevel => Levelling.LevelFor(Experience);

    /// <summary>What the next level costs, or null once the table runs out.</summary>
    public int? NextLevelAt => Levelling.NextThreshold(Experience);

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

        // Start them where their levels say they already are, or the opening fight would read
        // as though six levels of experience had never happened.
        campaign.Experience = Levelling.ThresholdFor(
            campaign.Party.Count == 0 ? 1 : campaign.Party.Max(one => one.Level));

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

    /// <summary>
    /// What has been taken from the fallen and not yet given to anybody.
    /// </summary>
    /// <remarks>
    /// The other half of the wall that damage reduction builds. The rules have been able to say
    /// "DR 10/silver" for some time and items have been able to answer it, but until something
    /// put a silvered blade in the party's hands the answer was one the player could never
    /// reach. Loot is how a gate becomes a puzzle instead of a dead end.
    /// </remarks>
    public IReadOnlyList<ItemDefinition> Stash => _stash;

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

    /// <summary>
    /// Strips the fallen of everything they were carrying. Idempotent: a chapter is looted once,
    /// however many times anybody asks.
    /// </summary>
    public int Collect()
    {
        if (_lootedChapter >= Chapter || State == CampaignState.Fighting)
        {
            return 0;
        }

        _lootedChapter = Chapter;
        var taken = 0;

        // Experience comes off the same moment: the chapter is over, and this is taking stock
        // of it. Awarded for anyone put down, whether or not they were carrying anything.
        Experience += Battle.Foes
            .Where(foe => !foe.IsConscious)
            .Sum(Levelling.Award);

        // Anyone who cannot stop you, not only the outright dead. A hobgoblin bleeding out at
        // -12 is in no position to object, and leaving his sword on him because the rules call
        // him "dying" rather than "dead" would be a distinction the player would read as a bug.
        foreach (var fallen in Battle.Foes.Where(foe => !foe.IsConscious))
        {
            // Enumerated into a list first: unequipping walks the same collection.
            foreach (var item in fallen.Equipment.Items.ToList())
            {
                fallen.Equipment.Unequip(item.Id);
                _stash.Add(item);
                taken++;
            }
        }

        return taken;
    }

    /// <summary>
    /// Hands something from the stash to somebody. Refuses mid-fight: rummaging through a sack
    /// is not a thing you do while a hobgoblin is swinging at you.
    /// </summary>
    public bool Give(Creature creature, string itemId)
    {
        ArgumentNullException.ThrowIfNull(creature);

        if (State == CampaignState.Fighting || !Party.Contains(creature))
        {
            return false;
        }

        var index = _stash.FindIndex(item => string.Equals(item.Id, itemId, StringComparison.Ordinal));
        if (index < 0)
        {
            return false;
        }

        var found = _stash[index];
        _stash.RemoveAt(index);

        // Taking a sword means putting it in your hand. Without this the better blade is stowed
        // behind the worse one, the creature keeps swinging the wrong thing, and the player is
        // left wondering why the silver they went to the trouble of finding does nothing.
        if (!creature.Equipment.HasRoomFor(found.Slot)
            && creature.Equipment.InSlot(found.Slot).FirstOrDefault() is { } displaced)
        {
            creature.Equipment.Unequip(displaced.Id);
            _stash.Add(displaced);
        }

        creature.Equipment.Equip(found, _library.BuildItemWeapon(found));

        return true;
    }

    /// <summary>Takes something back off somebody and returns it to the sack.</summary>
    public bool Reclaim(Creature creature, string itemId)
    {
        ArgumentNullException.ThrowIfNull(creature);

        if (State == CampaignState.Fighting || _library.GetItem(itemId) is not { } item)
        {
            return false;
        }

        if (!creature.Equipment.Unequip(itemId))
        {
            return false;
        }

        _stash.Add(item);
        return true;
    }

    /// <summary>Whether somebody has fallen behind what the party has earned.</summary>
    public bool CanLevel(Creature creature)
    {
        ArgumentNullException.ThrowIfNull(creature);

        return State != CampaignState.Fighting
            && Party.Contains(creature)
            && creature.Levels.Count > 0
            && creature.Level < EarnedLevel;
    }

    /// <summary>Anyone who could take a level right now.</summary>
    public IEnumerable<Creature> Ready => Party.Where(CanLevel);

    /// <summary>
    /// Takes one level, in the class they already have most of.
    /// </summary>
    /// <remarks>
    /// Automatic rather than a choice, which is a simplification and not a small one: picking a
    /// class, a feat and a spell is most of what levelling up <em>is</em> in this ruleset. It
    /// wants a screen of its own, and the arithmetic underneath it has to work first.
    /// </remarks>
    public bool LevelUp(Creature creature)
    {
        if (!CanLevel(creature))
        {
            return false;
        }

        return Levelling.Gain(creature, creature.Levels.MaxBy(level => level.Level).Class, _rules);
    }

    /// <summary>Moves on to the next fight, carrying the party into it as they are.</summary>
    public bool Advance()
    {
        if (Battle is not null && !CanAdvance)
        {
            return false;
        }

        Collect();

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
            new SavedCampaign(
                Definition.Id,
                Chapter,
                RestsRemaining,
                _seed,
                [.. _stash.Select(item => item.Id)],
                _lootedChapter,
                Experience)));

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
            Battle.Restore(save, library),
            state.Stash,
            state.LootedChapter,
            state.Experience);
    }

    /// <summary>Puts a campaign back where a save left it, around an already-restored fight.</summary>
    internal static Campaign Restore(
        ContentLibrary library,
        CampaignDefinition definition,
        int chapter,
        int restsRemaining,
        ulong seed,
        RuleOptions? rules,
        Battle battle,
        IReadOnlyList<string> stash,
        int lootedChapter,
        int experience)
    {
        var campaign = new Campaign(library, definition, seed, rules)
        {
            Chapter = chapter,
            RestsRemaining = restsRemaining,
            Battle = battle,
            _lootedChapter = lootedChapter,
            Experience = experience,
        };

        foreach (var id in stash)
        {
            campaign._stash.Add(library.GetItem(id) ?? throw new InvalidDataException(
                $"The save has an item '{id}' in the sack, which no content file defines."));
        }

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
