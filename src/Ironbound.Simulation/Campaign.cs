using Ironbound.Rules;
using Ironbound.Rules.Classes;
using Ironbound.Rules.Content;
using Ironbound.Rules.Dice;
using Ironbound.Rules.Feats;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Items;
using Ironbound.Rules.Maps;
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

        campaign.ShareLoad();
        campaign.LookAbout();

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

            // Channels, rage rounds, the bonded spell — and the fatigue clearing the effects
            // just left behind on anybody who was still raging.
            ClassFeatures.Rest(creature);
        }

        return true;
    }

    /// <summary>
    /// What one fight was worth to each of the party: every foe put down, at the Bestiary's
    /// experience for its challenge rating, shared out evenly among everybody in the party.
    /// </summary>
    /// <remarks>
    /// Shared among the whole party, the fallen included. The book gives a share to whoever took
    /// part, and the campaign keeps one number for all of them, so there is nobody to leave out.
    /// </remarks>
    private static int Earned(Battle battle) => Levelling.Share(
        battle.Foes.Where(foe => !foe.IsConscious).Sum(Levelling.Award),
        battle.Party.Count);

    /// <summary>
    /// Takes stock of a fight that is over: experience for it, and a body for each of the fallen
    /// where they fell, holding what they carried and the coins in their pockets — and what the
    /// room had lying about, as a pile. Returns how many things were laid out to be taken.
    /// Idempotent: a chapter is collected once, however many times anybody asks.
    /// </summary>
    /// <remarks>
    /// Nothing goes into the bag by itself any more. Taking is a step of its own —
    /// <see cref="TakeAll"/> and its kin — so that what is in the bag is what somebody chose to
    /// pick up, and the bag can be too heavy to carry.
    /// </remarks>
    public int Collect()
    {
        // Whatever else is or is not collected, a fight that is over is over for the rage too,
        // and every dagger thrown and every weapon dropped is walked over to and picked up.
        // (Before the first chapter there is no fight at all, and nothing to end.)
        if ((IsLevel || Chapter > 0) && State != CampaignState.Fighting)
        {
            foreach (var member in Party)
            {
                ClassFeatures.EndFight(member);
                member.Equipment.Recover();
            }
        }

        if (IsLevel)
        {
            var laid = CollectArea();
            LookAbout();
            return laid;
        }

        if (_lootedChapter >= Chapter || State == CampaignState.Fighting)
        {
            return 0;
        }

        _lootedChapter = Chapter;
        var taken = 0;

        // Experience comes off the same moment: the chapter is over, and this is taking stock
        // of it. Awarded for anyone put down, whether or not they were carrying anything.
        Award($"Chapter {Chapter} won", Earned(Battle));

        // Anyone who cannot stop you, not only the outright dead. A hobgoblin bleeding out at
        // -12 is in no position to object, and leaving his sword on him because the rules call
        // him "dying" rather than "dead" would be a distinction the player would read as a bug.
        var centre = Battle.Battlefield is { } field
            ? OpenGround(field, new GridSquare(field.Width / 2, field.Height / 2))
            : new GridSquare(0, 0);

        if (State != CampaignState.Lost)
        {
            taken += Bodies(Battle, $"chapter-{Chapter}", (ulong)Chapter, centre);
        }

        // What was lying about rather than being carried. Under the same once-only guard as the
        // bodies, and that guard is saved, so reloading is not a way to find the sword twice.
        if (State != CampaignState.Lost && Scene is { } scene)
        {
            taken += Pile($"loot:chapter-{Chapter}", $"what {scene.Name} left lying about", scene.Loot, centre);
        }

        return taken;
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
    public bool LevelUp(Creature creature, ClassDefinition taken, FeatDefinition? feat = null) =>
        LevelUp(creature, taken, feat, new LevelChoices());

    /// <summary>
    /// Takes a level with the class features it brings chosen too: a fighter's bonus feat and
    /// weapon group, a rogue's talent, a barbarian's rage power, a wizard's bonus feat.
    /// </summary>
    /// <remarks>
    /// Anything the level needs and the choices leave out is picked sensibly — which is how a
    /// headless run, and the overload without choices, level at all. Anything chosen that the
    /// level does not offer is refused, and nothing changes; <see cref="LevelRefusal"/> says why.
    /// </remarks>
    public bool LevelUp(Creature creature, ClassDefinition taken, FeatDefinition? feat, LevelChoices choices)
    {
        ArgumentNullException.ThrowIfNull(taken);
        ArgumentNullException.ThrowIfNull(choices);
        LevelRefusal = null;

        if (!CanLevel(creature) || _library.GetClass(taken.Id) is null)
        {
            LevelRefusal = $"{creature.Name} cannot take a level now.";
            return false;
        }

        // A feat taken for a weapon, offered without one, is taken for the one in her hand.
        if (feat is { Takes: not FeatChoice.None, Choice: null } open)
        {
            feat = ClassLevelling.ChooseFor(creature, open);

            if (feat is null)
            {
                LevelRefusal = $"{creature.Name} carries nothing left to take {open.Name} for.";
                return false;
            }
        }

        if (feat is not null
            && (!Levelling.GrantsFeatAt(creature.Level + 1) || !feat.AvailableTo(creature)))
        {
            LevelRefusal = $"{feat.Title} is not a feat {creature.Name} can take at this level.";
            return false;
        }

        var (resolved, refusal) = ClassLevelling.Resolve(creature, taken, choices, _library);
        if (resolved is null)
        {
            LevelRefusal = refusal;
            return false;
        }

        if (feat is not null && resolved.BonusFeat is { } bonus && bonus.Key == feat.Key)
        {
            LevelRefusal = $"{feat.Title} cannot be both the level's feat and its bonus feat.";
            return false;
        }

        if (!Levelling.Gain(creature, taken, _rules))
        {
            LevelRefusal = $"{creature.Name} is at the top of the table.";
            return false;
        }

        if (feat is not null)
        {
            // Both halves, exactly once: the list is the identity and ApplyTo is the only thing
            // that pushes the bonuses into the stacks, and it is not idempotent.
            ClassFeatures.Take(creature, feat);
        }

        ClassLevelling.Apply(creature, resolved, _library);
        return true;
    }

    /// <summary>Why the last <see cref="LevelUp(Creature, ClassDefinition, FeatDefinition?, LevelChoices)"/> was refused, or null.</summary>
    public string? LevelRefusal { get; private set; }

    /// <summary>
    /// What a level in this class would ask to be chosen — bonus feats, weapon groups, talents —
    /// with everything on offer. Empty lists where the level asks for nothing.
    /// </summary>
    public LevelNeeds NeedsFor(Creature creature, ClassDefinition taken)
    {
        ArgumentNullException.ThrowIfNull(creature);
        ArgumentNullException.ThrowIfNull(taken);

        return ClassLevelling.NeedsFor(creature, taken, _library);
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

    /// <summary>
    /// Every feat this creature cannot take, and in words why not: what the game has yet to
    /// build for it, or the prerequisites it is short of. What a level-up screen lists under
    /// "not available", in name order. Feats it already holds are left out.
    /// </summary>
    public IEnumerable<(FeatDefinition Feat, string Why)> WithheldFeats(Creature creature)
    {
        ArgumentNullException.ThrowIfNull(creature);

        return _library.FeatIds
            .Select(_library.GetFeat)
            .OfType<FeatDefinition>()
            .Where(feat => feat.Repeatable || feat.Takes != FeatChoice.None || !creature.HasFeat(feat.Id))
            .Select(feat => (Feat: feat, Why: feat.WhyNot(creature)))
            .Where(entry => entry.Why.Count > 0 && !(entry.Why.Count == 1 && entry.Why[0] == "already taken"))
            .OrderBy(entry => entry.Feat.Name, StringComparer.Ordinal)
            .Select(entry => (entry.Feat, entry.Feat.IsAvailable
                ? $"needs {string.Join(", ", entry.Why)}"
                : entry.Why[0]));
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

        // The bodies and piles of the last board stay on it: whatever was not picked up is left
        // behind with the place.
        _containers.Clear();

        var encounter = Definition.Encounters[Chapter];
        Chapter++;

        // A different stream per chapter, so the second fight does not replay the first one's
        // dice, and so a campaign is still reproducible from one seed.
        Battle = Scenarios.Build(_library, encounter, _seed + (ulong)Chapter, _rules, _party);
        ShareLoad();

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
                [],
                _lootedChapter,
                Experience,
                CaptureLevel(),
                [.. _bag.Entries.Select(Capture)],
                Capture(_bag.Money),
                [.. _containers.Select(Capture)])));

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
            state.LootedChapter,
            state.Experience);

        campaign.RestoreBag(state);

        if (state.Level is { } level)
        {
            campaign.Resume(level, state.Containers);
        }
        else
        {
            campaign.RestoreContainers(state.Containers);
        }

        campaign.ShareLoad();
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
