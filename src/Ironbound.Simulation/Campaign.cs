using Ironbound.Rules;
using Ironbound.Rules.Classes;
using Ironbound.Rules.Content;
using Ironbound.Rules.Dice;
using Ironbound.Rules.Feats;
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

    /// <summary>
    /// Walking a level with nobody to fight. Only a campaign played on a level is ever in it;
    /// it is to a level what <see cref="Between"/> is to a run of fights.
    /// </summary>
    Exploring,
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
public sealed partial class Campaign
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
        _explore = new PcgRandom(seed, ExploreStream);
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

        if (definition.Level is { Length: > 0 } levelId)
        {
            campaign.Open(library.GetLevel(levelId) ?? throw new ArgumentException(
                $"The campaign '{campaignId}' is set in a level '{levelId}', which no content file defines.",
                nameof(campaignId)));
        }
        else
        {
            campaign.Advance();
        }

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

    /// <summary>The written-down form of that fight: its name, its intro, what it hides.</summary>
    public EncounterDefinition? Scene =>
        Chapter >= 1 && Chapter <= Definition.Encounters.Count
            ? _library.GetEncounter(Definition.Encounters[Chapter - 1])
            : null;

    /// <summary>What to read aloud as this chapter opens. Empty when it has nothing to say.</summary>
    public string Intro => Scene?.Intro ?? string.Empty;

    /// <summary>The party as they stand, wounds and spent slots and all.</summary>
    public IReadOnlyList<Creature> Party => Battle.Party;

    public CampaignState State
    {
        get
        {
            if (IsLevel)
            {
                return LevelState;
            }

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

    public bool CanRest =>
        RestsRemaining > 0 && State is CampaignState.Between or CampaignState.Exploring;

    /// <summary>Never on a level: there is no next fight to cut to, only somewhere to walk.</summary>
    public bool CanAdvance => !IsLevel && State == CampaignState.Between;

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
    /// Strips the fallen of everything they were carrying, and searches the room if the fight
    /// was won. Idempotent: a chapter is looted once, however many times anybody asks.
    /// </summary>
    public int Collect()
    {
        if (IsLevel)
        {
            return CollectArea();
        }

        if (_lootedChapter >= Chapter || State == CampaignState.Fighting)
        {
            return 0;
        }

        _lootedChapter = Chapter;
        var taken = 0;

        // Experience comes off the same moment: the chapter is over, and this is taking stock
        // of it. Awarded for anyone put down, whether or not they were carrying anything.
        Award($"Chapter {Chapter} won", Battle.Foes
            .Where(foe => !foe.IsConscious)
            .Sum(Levelling.Award));

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

        // What was lying about rather than being carried. Under the same once-only guard as the
        // bodies, and that guard is saved, so reloading is not a way to find the sword twice.
        if (State != CampaignState.Lost && Scene is { } scene)
        {
            foreach (var found in scene.Loot.Select(_library.GetItem).OfType<ItemDefinition>())
            {
                _stash.Add(found);
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

        return LevelUp(creature, creature.Levels.MaxBy(level => level.Level).Class);
    }

    /// <summary>
    /// Takes a level in a chosen class, and a chosen feat if the new level comes with one.
    /// </summary>
    /// <remarks>
    /// The feat is an opportunity attached to this particular level rather than a balance owed:
    /// offered when the new total is odd, taken now or not at all. Passing one on an even level
    /// is refused rather than quietly ignored, because silently dropping somebody's choice is
    /// worse than telling them it was not theirs to make.
    /// </remarks>
    public bool LevelUp(Creature creature, ClassDefinition taken, FeatDefinition? feat = null)
    {
        ArgumentNullException.ThrowIfNull(taken);

        if (!CanLevel(creature) || _library.GetClass(taken.Id) is null)
        {
            return false;
        }

        if (feat is not null
            && (!Levelling.GrantsFeatAt(creature.Level + 1) || !feat.AvailableTo(creature)))
        {
            return false;
        }

        if (!Levelling.Gain(creature, taken, _rules))
        {
            return false;
        }

        if (feat is not null)
        {
            // Both halves, exactly once: the list is the identity and ApplyTo is the only thing
            // that pushes the bonuses into the stacks, and it is not idempotent.
            creature.Feats.Add(feat);
            feat.ApplyTo(creature);
        }

        return true;
    }

    /// <summary>Every class the level could be taken in, by name so a picker does not shuffle.</summary>
    public IEnumerable<ClassDefinition> ClassesFor(Creature creature)
    {
        ArgumentNullException.ThrowIfNull(creature);

        return _library.ClassIds
            .Select(_library.GetClass)
            .OfType<ClassDefinition>()
            .OrderBy(taken => taken.Name, StringComparer.Ordinal);
    }

    /// <summary>
    /// The feats this creature could actually take: not already held, and nothing outstanding.
    /// </summary>
    public IEnumerable<FeatDefinition> FeatsFor(Creature creature)
    {
        ArgumentNullException.ThrowIfNull(creature);

        return _library.FeatIds
            .Select(_library.GetFeat)
            .OfType<FeatDefinition>()
            .Where(feat => feat.AvailableTo(creature))
            .OrderBy(feat => feat.Name, StringComparer.Ordinal);
    }

    /// <summary>Whether the next level somebody takes will come with a feat.</summary>
    public bool NextLevelGrantsFeat(Creature creature)
    {
        ArgumentNullException.ThrowIfNull(creature);

        return Levelling.GrantsFeatAt(creature.Level + 1);
    }

    /// <summary>Moves on to the next fight, carrying the party into it as they are.</summary>
    public bool Advance()
    {
        if (IsLevel || (Battle is not null && !CanAdvance))
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
                Experience,
                CaptureLevel())));

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

        var campaign = Restore(
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

        if (state.Level is { } level)
        {
            campaign.Resume(level);
        }

        return campaign;
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

    public override string ToString() => IsLevel
        ? $"{Definition.Name}: {_cleared.Count} room(s) cleared, "
            + $"{RestsRemaining} rest(s) left — {State}"
        : $"{Definition.Name}: chapter {Chapter} of {Definition.Encounters.Count}, "
        + $"{RestsRemaining} rest(s) left — {State}";
}
