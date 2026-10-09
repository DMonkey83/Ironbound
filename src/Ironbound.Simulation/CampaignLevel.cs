using Ironbound.Rules.Abilities;
using Ironbound.Rules.Classes;
using Ironbound.Rules.Combat;
using Ironbound.Rules.Content;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Defense;
using Ironbound.Rules.Dice;
using Ironbound.Rules.Effects;
using Ironbound.Rules.Items;
using Ironbound.Rules.Maps;
using Ironbound.Rules.Persistence;
using Ironbound.Rules.Skills;

namespace Ironbound.Simulation;

/// <summary>
/// What came of trying a door, a bridge or a container.
/// </summary>
/// <param name="Lines">For the log, headline first. Never empty, even for a refusal.</param>
/// <param name="MovedTo">Where the one who tried it ended up, when it carried them somewhere.</param>
/// <param name="Noisy">Something was forced open, loudly. Nothing hears it yet; this is where
/// whatever does will listen.</param>
public sealed record FeatureResult(bool Success, IReadOnlyList<string> Lines, GridSquare? MovedTo, bool Noisy = false);

/// <summary>Experience given for one thing: a fight won, a place found, a door opened.</summary>
public sealed record ExperienceAward(string Why, int Amount);

/// <summary>
/// A campaign played on one level: walked in real time, fought a room at a time.
/// </summary>
/// <remarks>
/// The same campaign underneath — one party, one sack, one rest budget, experience in one pool —
/// and the same <see cref="Battle"/> on top, which is why the Game can keep asking it for the
/// party and the ground whether anybody is fighting or not. Between fights it is a battle with
/// nobody on the other side: an odd thing to call a battle, but it means the screen never has to
/// wonder which of two objects to ask where somebody is standing.
/// <para>
/// The fights are fought on the level's own battlefield, not a copy of it. A door broken open
/// on the way in is open during the fight, and whoever was standing in the doorway when the orcs
/// woke is still standing there.
/// </para>
/// </remarks>
public sealed partial class Campaign
{
    /// <summary>
    /// The dice for everything done between fights: doors, bridges, falls. Its own stream, so
    /// trying a door three times does not change how the next fight's initiative comes out.
    /// </summary>
    public const ulong ExploreStream = 3;

    private readonly HashSet<string> _cleared = new(StringComparer.Ordinal);
    private readonly HashSet<string> _visited = new(StringComparer.Ordinal);
    private readonly HashSet<string> _used = new(StringComparer.Ordinal);
    private readonly List<Waiting> _dormant = [];
    private IRandomSource _explore;

    /// <summary>Somebody in a room nobody has walked into yet, and how the level placed them.</summary>
    private sealed record Waiting(
        Creature Foe, GridSquare Square, AreaDefinition Area, PlacementDefinition Placement);

    public bool IsLevel => Level is not null;

    /// <summary>The level being walked, or null for a run of separate fights.</summary>
    public LevelDefinition? Level { get; private set; }

    /// <summary>
    /// The ground. On a level it is the level's own, and the same object every fight on it is
    /// fought over; otherwise it is the current fight's.
    /// </summary>
    public Battlefield Field =>
        Battle.Battlefield ?? throw new InvalidOperationException("This fight has no ground.");

    /// <summary>The room being fought over, or null while nobody is.</summary>
    public AreaDefinition? CurrentArea { get; private set; }

    /// <summary>
    /// Everyone still waiting in a room the party has not walked into, at the square the level
    /// put them on. They are not on <see cref="Field"/> until their room is entered, so this is
    /// how anything that wants to draw them finds them.
    /// </summary>
    public IReadOnlyList<(Creature Foe, GridSquare Square, AreaDefinition Area)> Dormant =>
        [.. _dormant.Select(one => (one.Foe, one.Square, one.Area))];

    /// <summary>
    /// Whether nobody in a room is a threat any more: its fight won, or it never had one.
    /// </summary>
    public bool IsCleared(string areaId) =>
        _cleared.Contains(areaId) || Level?.GetArea(areaId) is { Foes.Count: 0 };

    public bool IsVisited(string areaId) => _visited.Contains(areaId);

    /// <summary>
    /// Marks a room as walked into. True only the first time, which is when its page is read.
    /// </summary>
    public bool Visit(string areaId)
    {
        if (Level?.GetArea(areaId) is not { } area || !_visited.Add(areaId))
        {
            return false;
        }

        Award($"Discovered {area.Name}", area.Experience);
        return true;
    }

    private readonly List<ExperienceAward> _awards = [];

    /// <summary>
    /// Every award of experience since the last call, oldest first, and forgets them: what the
    /// Game shows as it happens. The total is in <see cref="Experience"/> either way.
    /// </summary>
    public IReadOnlyList<ExperienceAward> TakeAwards()
    {
        var taken = _awards.ToList();
        _awards.Clear();
        return taken;
    }

    private void Award(string why, int amount)
    {
        if (amount <= 0)
        {
            return;
        }

        Experience += amount;
        _awards.Add(new ExperienceAward(why, amount));
    }

    /// <summary>The room a square is in, or null for the passages between them.</summary>
    public AreaDefinition? AreaAt(GridSquare square) =>
        Level?.Areas.FirstOrDefault(area => area.Contains(square));

    /// <summary>
    /// The door, bridge or container on a square — never a container nobody has noticed yet,
    /// which is not there to be clicked.
    /// </summary>
    public FeatureDefinition? FeatureAt(GridSquare square) =>
        Level?.Features.FirstOrDefault(feature => feature.Squares.Contains(square)
            && !(feature.Kind == FeatureKind.Container
                && _containers.FirstOrDefault(container => ReferenceEquals(container.Feature, feature)) is { IsHidden: true }));

    /// <summary>
    /// Whether a feature has been dealt with: a door opened, a bridge tied off, a container
    /// opened and emptied. A container with something still in it is not done with.
    /// </summary>
    public bool IsUsed(string featureId) =>
        _containers.FirstOrDefault(container => string.Equals(container.Feature?.Id, featureId, StringComparison.Ordinal)) is { } container
            ? container.IsOpen && container.IsEmpty
            : _used.Contains(featureId);

    private CampaignState LevelState
    {
        get
        {
            // Checked first, because it is true whatever else is: a party all down is a party
            // that has lost, in a fight or out of one. A fall into the chasm can do it.
            if (!Party.Any(member => member.IsConscious))
            {
                return CampaignState.Lost;
            }

            if (CurrentArea is { } area)
            {
                return Battle.Outcome switch
                {
                    BattleOutcome.InProgress => CampaignState.Fighting,
                    BattleOutcome.PartyWon => area.Final ? CampaignState.Won : CampaignState.Exploring,
                    _ => CampaignState.Lost,
                };
            }

            return Level!.Areas.Any(room => room.Final && _cleared.Contains(room.Id))
                ? CampaignState.Won
                : CampaignState.Exploring;
        }
    }

    /// <summary>
    /// Lays the level out: the ground, the party at the door, and everybody else asleep in
    /// their rooms — built, but not on the board.
    /// </summary>
    private void Open(LevelDefinition level)
    {
        Level = level;

        var field = new Battlefield(level.Width, level.Height);
        for (var y = 0; y < level.Height; y++)
        {
            for (var x = 0; x < level.Width; x++)
            {
                if (level.IsBlockedCell(x, y))
                {
                    field.Block(new GridSquare(x, y));
                }
            }
        }

        var party = new List<Creature>();
        foreach (var placement in level.Start)
        {
            // A missing creature is already in the library's problems, as in Scenarios.Build.
            if (_library.BuildCreature(placement.CreatureId, _rules, placement.Name) is not { } hero)
            {
                continue;
            }

            _party[placement.CreatureId] = hero;
            field.Place(hero, placement.X, placement.Y);
            party.Add(hero);
        }

        foreach (var area in level.Areas)
        {
            Populate(area);
        }

        foreach (var feature in level.Features.Where(feature => feature.Kind == FeatureKind.Container))
        {
            _containers.Add(Build(feature));
        }

        Battle = Peace(party, field);
    }

    /// <summary>Builds a room's occupants and leaves them waiting.</summary>
    private void Populate(AreaDefinition area)
    {
        foreach (var placement in area.Foes)
        {
            if (_library.BuildCreature(placement.CreatureId, _rules, placement.Name) is not { } foe)
            {
                continue;
            }

            // Sides are known before the fight is: anything that asks "is this an enemy?" of a
            // creature it can see drawn should get the same answer it will get once it moves.
            foe.Allegiance = Battle.FoeAllegiance;
            _dormant.Add(new Waiting(foe, new GridSquare(placement.X, placement.Y), area, placement));
        }
    }

    /// <summary>
    /// A battle with nobody on the other side, which is what walking about is. It never has a
    /// turn to open, so its dice are never asked for anything; a fresh stream is as good as any.
    /// </summary>
    private Battle Peace(IEnumerable<Creature> party, Battlefield field) =>
        new(InStartingOrder(party), [], new PcgRandom(_seed, Scenarios.DiceStream), _rules, field);

    /// <summary>
    /// The party in the order the level lists them, rather than whatever order a reloaded
    /// battle's initiative happened to leave them in — so a portrait strip does not reshuffle.
    /// </summary>
    private List<Creature> InStartingOrder(IEnumerable<Creature> party)
    {
        var order = Level!.Start.Select(placement => placement.CreatureId).ToList();

        return [.. party.OrderBy(member =>
        {
            var index = member.DefinitionId is { } id ? order.IndexOf(id) : -1;
            return index < 0 ? int.MaxValue : index;
        })];
    }

    /// <summary>
    /// Moves a party member somewhere free, however far. Walking is the Game's business — it
    /// goes a square at a time along <see cref="Battlefield.FindPath"/> so that it can stop the
    /// moment somebody steps into a room — and this only checks that the square can be stood on.
    /// </summary>
    public bool Walk(Creature member, GridSquare to)
    {
        ArgumentNullException.ThrowIfNull(member);

        Collect();

        // Won is still walking: the last room's spoils are there to be picked up.
        if (State is not (CampaignState.Exploring or CampaignState.Won) || !Party.Contains(member)
            || !member.IsConscious || !Field.IsFree(to) || Encumbrance.Effective(member) == LoadCategory.Overloaded)
        {
            return false;
        }

        Field.Place(member, to);
        LookAbout();
        return true;
    }

    /// <summary>
    /// Whether somebody is carrying too much to walk at all — their own gear, or their share of
    /// the party's. The Game asks before a walk, to say so and to ask for something to be put down.
    /// </summary>
    public bool IsOverloaded(Creature member)
    {
        ArgumentNullException.ThrowIfNull(member);
        return Encumbrance.Effective(member) == LoadCategory.Overloaded;
    }

    /// <summary>
    /// The room somebody has just walked into that still has somebody waiting in it, or null.
    /// The first in the level's own order, if more than one, so the answer never depends on
    /// who moved last.
    /// </summary>
    public AreaDefinition? Alarm()
    {
        if (State != CampaignState.Exploring || Level is not { } level)
        {
            return null;
        }

        var standing = Party
            .Where(member => member.IsConscious)
            .Select(Field.SquareOf)
            .OfType<GridSquare>()
            .ToList();

        return level.Areas.FirstOrDefault(area =>
            !IsCleared(area.Id)
            && _dormant.Any(one => one.Area.Id == area.Id && one.Foe.HitPoints.IsAlive)
            && standing.Any(area.Contains));
    }

    /// <summary>
    /// Wakes a room: its occupants onto the board and a fight begun on the level's own ground.
    /// </summary>
    /// <remarks>
    /// Seeded per room, by the room's place in the file, for the reason each chapter of a run of
    /// fights is seeded by its number: the second fight should not replay the first one's dice,
    /// and the whole campaign should still be reproducible from a single seed. That holds
    /// whichever order the rooms are walked into.
    /// </remarks>
    public bool Engage(string areaId)
    {
        Collect();

        if (State != CampaignState.Exploring || Level is not { } level
            || level.GetArea(areaId) is not { } area || IsCleared(areaId))
        {
            return false;
        }

        var waking = _dormant.Where(one => one.Area.Id == area.Id && one.Foe.HitPoints.IsAlive).ToList();
        if (waking.Count == 0)
        {
            return false;
        }

        var field = Field;
        foreach (var one in waking)
        {
            // Somebody may be standing on the orc's bedroll by now. The orc gets up beside it.
            var square = field.IsFree(one.Square)
                ? one.Square
                : NearestFree(field, one.Square, _ => true) ?? throw new InvalidOperationException(
                    $"There is nowhere on the map to put {one.Foe.Name}.");

            field.Place(one.Foe, square);
        }

        _dormant.RemoveAll(one => one.Area.Id == area.Id);

        var index = 0;
        while (!ReferenceEquals(level.Areas[index], area))
        {
            index++;
        }

        Battle = new Battle(
            InStartingOrder(Party),
            waking.Select(one => one.Foe),
            new PcgRandom(_seed + (ulong)(index + 1), Scenarios.DiceStream),
            _rules,
            field);

        // As Scenarios.Build does it: the hidden get their one chance, then the sleepers sleep.
        Battle.Ambush([.. waking.Where(one => one.Placement.Hidden).Select(one => one.Foe)]);
        Battle.Lull([.. waking.Where(one => one.Placement.Asleep).Select(one => one.Foe)]);

        CurrentArea = area;
        return true;
    }

    /// <summary>
    /// Takes stock of a room that has been won, then puts the party back to walking.
    /// </summary>
    /// <remarks>
    /// The guard is <see cref="CurrentArea"/> itself: it is cleared here and nowhere else, so a
    /// second call finds nothing to collect. A lost fight is left exactly as it ended — there is
    /// nobody left to search anything.
    /// </remarks>
    private int CollectArea()
    {
        if (CurrentArea is not { } area || Battle.Outcome != BattleOutcome.PartyWon)
        {
            return 0;
        }

        Award($"Won {area.Name}", Earned(Battle));

        // A body for each of the fallen where they fell, and the room's own loot as a pile — in
        // the middle of the room, or where the file puts it. Seeded by the room's place in the
        // file, as its fight is.
        var field = Field;
        var index = 0;
        while (!ReferenceEquals(Level!.Areas[index], area))
        {
            index++;
        }

        var middle = OpenGround(field, area.LootAt ?? area.Centre);
        var taken = Bodies(Battle, area.Id, (ulong)(index + 1), middle);
        taken += Pile($"loot:{area.Id}", $"what {area.Name} held", area.Loot, middle);

        // Off the board, so the bodies are not in the way of the walking. Each one is a
        // container now, and the Game draws the fallen from those.
        foreach (var foe in Battle.Foes)
        {
            field.Remove(foe);
        }

        _cleared.Add(area.Id);
        CurrentArea = null;
        Battle = Peace(Battle.Party, field);

        return taken;
    }

    // ---- doors, bridges and caches ----

    /// <summary>
    /// Whether somebody could try a feature now: standing beside it, awake, and with nobody
    /// to fight. For a bridge, they have to be on one bank or the other, not somewhere along it.
    /// </summary>
    public bool CanUse(string featureId, Creature who)
    {
        ArgumentNullException.ThrowIfNull(who);

        return Refusal(Level?.GetFeature(featureId), who) is null;
    }

    /// <summary>
    /// Tries a feature. A failure can be tried again — a door does not lock harder for being
    /// rattled, and somebody who fell into the crevice can climb out and jump again — so the only
    /// thing that limits it is the rest budget and the hit points it costs.
    /// </summary>
    public FeatureResult Use(string featureId, Creature who)
    {
        ArgumentNullException.ThrowIfNull(who);

        Collect();

        var feature = Level?.GetFeature(featureId);
        if (Refusal(feature, who) is { } why)
        {
            return new FeatureResult(false, [why], null);
        }

        return feature!.Kind switch
        {
            FeatureKind.Door => OpenDoor(feature, who),
            FeatureKind.Bridge => Cross(feature, who),
            _ => OpenContainer(GetContainer(feature.Id)!, who),
        };
    }

    private string? Refusal(FeatureDefinition? feature, Creature who)
    {
        if (feature is null)
        {
            return "There is nothing like that here.";
        }

        // A container is opened however often anybody likes, and only once somebody has
        // noticed it: the container's own questions.
        if (feature.Kind == FeatureKind.Container)
        {
            return ContainerRefusal(GetContainer(feature.Id), who);
        }

        if (State is not (CampaignState.Exploring or CampaignState.Won))
        {
            return $"Not now: {feature.Name} will have to wait.";
        }

        if (feature.Kind == FeatureKind.Bridge && IsOverloaded(who))
        {
            return $"{who.Name} is carrying far too much to get across {feature.Name}.";
        }

        if (_used.Contains(feature.Id))
        {
            return $"{Capital(feature.Name)} has already been seen to.";
        }

        if (!Party.Contains(who) || !who.IsConscious)
        {
            return $"{who.Name} is in no state to try {feature.Name}.";
        }

        if (Field.SquareOf(who) is not { } at
            || !feature.Squares.Any(square => Distance.AreAdjacent(square, at)))
        {
            return $"{who.Name} is not close enough to {feature.Name}.";
        }

        if (feature.Kind == FeatureKind.Bridge && BanksOf(feature).SideOf(at) == 0)
        {
            return $"{who.Name} has to be on one side of {feature.Name} or the other.";
        }

        return null;
    }

    /// <summary>
    /// Picks the lock if they know how, and puts a shoulder to it if not.
    /// </summary>
    /// <remarks>
    /// Disable Device is trained-only, which is the whole reason the rogue is in the party: with
    /// no ranks there is no lock-picking at all, only Strength against the door, and a door
    /// that is harder to break than to pick is how that shows.
    /// </remarks>
    private FeatureResult OpenDoor(FeatureDefinition door, Creature who)
    {
        // A Strength check without the skill, so being shaken costs as much here as on a lock.
        var (success, headline, roll, forced) = TryLock(door.Name, door.LockDc, door.BreakDc, who);

        var lines = new List<string> { headline, roll };
        if (success)
        {
            foreach (var square in door.Squares)
            {
                Field.Unblock(square);
            }

            _used.Add(door.Id);
            Award($"Opened {door.Name}", door.Experience);
            AddText(lines, door);
        }

        return new FeatureResult(success, lines, null, success && forced);
    }

    /// <summary>
    /// One crosses, by jumping it or by climbing down and up the far side, and ties the bridge
    /// off for everyone else.
    /// </summary>
    /// <remarks>
    /// Both checks are rolled and the better one counts, because both are the same attempt —
    /// somebody who sees the jump is beyond them climbs instead — and rolling both every time
    /// keeps the dice stream the same length whoever tries it.
    /// </remarks>
    private FeatureResult Cross(FeatureDefinition bridge, Creature who)
    {
        var banks = BanksOf(bridge);
        var from = Field.SquareOf(who)!.Value;
        var side = banks.SideOf(from);

        if (banks.Landing(Field, -side, from) is not { } landing)
        {
            return new FeatureResult(
                false, [$"There is no room on the far side of {bridge.Name}."], null);
        }

        var jump = who.Skills.Check(Skill.Acrobatics, _explore, bridge.JumpDc);
        var climb = who.Skills.Check(Skill.Climb, _explore, bridge.ClimbDc);

        if (jump.Succeeded == true || climb.Succeeded == true)
        {
            Field.Place(who, landing);
            foreach (var square in bridge.Squares)
            {
                Field.Unblock(square);
            }

            _used.Add(bridge.Id);
            Award($"Crossed {bridge.Name}", bridge.Experience);

            var lines = new List<string>
            {
                jump.Succeeded == true
                    ? $"{who.Name} jumps {bridge.Name} and ties it off."
                    : $"{who.Name} climbs across {bridge.Name} and ties it off.",
                jump.ToString(),
                climb.ToString(),
            };

            AddText(lines, bridge);
            return new FeatureResult(true, lines, landing);
        }

        // The ordinary damage path, so a fall that drops somebody below nought leaves them
        // dying exactly as a sword would have.
        var fall = DamagePacket.Weapon(bridge.Fall, DamageType.Bludgeoning).Roll(_explore);
        var taken = who.Defenses.Apply(fall, DamageBypass.None, _rules);
        who.HitPoints.Take(taken.Total);
        Bleeding.Sync(who);

        var fell = new List<string>
        {
            who.IsConscious
                ? $"{who.Name} falls, and climbs back out."
                : $"{who.Name} falls, and does not get up.",
            jump.ToString(),
            climb.ToString(),
            $"  {who.Name} takes {taken}",
        };

        return new FeatureResult(false, fell, null);
    }

    private static void AddText(List<string> lines, FeatureDefinition feature)
    {
        if (feature.Text.Length > 0)
        {
            lines.Add(feature.Text);
        }
    }

    private static string Capital(string name) =>
        name.Length == 0 ? name : char.ToUpperInvariant(name[0]) + name[1..];

    private Banks BanksOf(FeatureDefinition bridge) => Banks.Of(bridge, Level!);

    // ---- saving ----

    private SavedLevel? CaptureLevel()
    {
        if (Level is not { } level)
        {
            return null;
        }

        var state = _explore.Capture();

        // Sorted, because a set's order is not a promise and two saves of the same moment
        // should be the same file.
        return new SavedLevel(
            level.Id,
            [.. _cleared.Order(StringComparer.Ordinal)],
            [.. _visited.Order(StringComparer.Ordinal)],
            [.. _used.Order(StringComparer.Ordinal)],
            CurrentArea?.Id,
            state.A,
            state.B);
    }

    /// <summary>
    /// Puts a level back around a restored battle: what the party has done to the place, and
    /// everyone still waiting in a room they have not walked into.
    /// </summary>
    private void Resume(SavedLevel saved, SavedContainer[]? containers)
    {
        var level = _library.GetLevel(saved.Id) ?? throw new InvalidDataException(
            $"The save is of a level '{saved.Id}', which no content file defines.");

        Level = level;
        _cleared.UnionWith(saved.Cleared);
        _visited.UnionWith(saved.Visited);
        _used.UnionWith(saved.Used);
        _explore = PcgRandom.FromState(new RandomState(saved.RandomA, saved.RandomB));

        CurrentArea = saved.Current is { } current
            ? level.GetArea(current) ?? throw new InvalidDataException(
                $"The save is mid-fight in a room '{current}', which the level does not have.")
            : null;

        var field = Field;

        // The saved ground already has them open. Opening them again costs nothing, and means a
        // save whose ground was written before a door was counted still comes back right.
        foreach (var id in saved.Used)
        {
            var feature = level.GetFeature(id) ?? throw new InvalidDataException(
                $"The save has used a feature '{id}', which the level does not have.");

            if (feature.Kind is FeatureKind.Door or FeatureKind.Bridge)
            {
                foreach (var square in feature.Squares)
                {
                    field.Unblock(square);
                }
            }
        }

        RestoreContainers(containers, level);

        foreach (var area in level.Areas)
        {
            if (!_cleared.Contains(area.Id) && area.Id != CurrentArea?.Id)
            {
                Populate(area);
            }
        }

        if (CurrentArea is null)
        {
            Battle = Peace(Battle.Party, field);
        }
    }

    /// <summary>
    /// The closest free square to <paramref name="from"/> that can be walked to from it, or null.
    /// </summary>
    /// <remarks>
    /// Breadth first over open ground rather than nearest as the crow flies, so nobody is put
    /// down on the far side of a wall. Squares are tried in a fixed order, so the same board
    /// always gives the same answer.
    /// </remarks>
    internal static GridSquare? NearestFree(
        Battlefield field, GridSquare from, Func<GridSquare, bool> allowed) =>
        NearestFree(field, [from], allowed);

    internal static GridSquare? NearestFree(
        Battlefield field, IEnumerable<GridSquare> from, Func<GridSquare, bool> allowed)
    {
        var seen = new HashSet<GridSquare>();
        var queue = new Queue<GridSquare>();

        foreach (var start in from)
        {
            if (field.IsPassable(start) && allowed(start) && seen.Add(start))
            {
                queue.Enqueue(start);
            }
        }

        while (queue.TryDequeue(out var square))
        {
            if (field.IsFree(square))
            {
                return square;
            }

            for (var dy = -1; dy <= 1; dy++)
            {
                for (var dx = -1; dx <= 1; dx++)
                {
                    var next = new GridSquare(square.X + dx, square.Y + dy);
                    if (field.IsPassable(next) && allowed(next) && seen.Add(next))
                    {
                        queue.Enqueue(next);
                    }
                }
            }
        }

        return null;
    }
}

/// <summary>
/// Which way a bridge runs, and so which bank of the chasm somebody is on.
/// </summary>
/// <remarks>
/// Worked out from the squares rather than written in the file: a bridge two squares wide in x
/// crosses in x. A single square is ambiguous, so it looks at the map — a bridge crosses the way
/// the chasm is <em>not</em> running.
/// </remarks>
internal readonly record struct Banks(bool AcrossX, int Low, int High, IReadOnlyList<GridSquare> Squares)
{
    public static Banks Of(FeatureDefinition bridge, LevelDefinition level)
    {
        var squares = bridge.Squares;
        var minX = squares.Min(square => square.X);
        var maxX = squares.Max(square => square.X);
        var minY = squares.Min(square => square.Y);
        var maxY = squares.Max(square => square.Y);

        bool acrossX;
        if (maxX - minX != maxY - minY)
        {
            acrossX = maxX - minX > maxY - minY;
        }
        else
        {
            var row = squares[0].Y;
            acrossX = level.CellAt(minX - 1, row) != LevelCell.Chasm
                && level.CellAt(maxX + 1, row) != LevelCell.Chasm;
        }

        return acrossX
            ? new Banks(true, minX, maxX, squares)
            : new Banks(false, minY, maxY, squares);
    }

    /// <summary>-1 on the low bank, +1 on the high one, 0 alongside the bridge itself.</summary>
    public int SideOf(GridSquare square)
    {
        var along = AcrossX ? square.X : square.Y;
        return along < Low ? -1 : along > High ? 1 : 0;
    }

    /// <summary>
    /// Where somebody crossing to <paramref name="side"/> comes down: the free square nearest
    /// the end of the bridge, on that bank and reachable from it. The end opposite where they
    /// set off from is tried first, so they land across from where they jumped.
    /// </summary>
    public GridSquare? Landing(Battlefield field, int side, GridSquare from)
    {
        if (side == 0)
        {
            return null;
        }

        var edge = side < 0 ? Low - 1 : High + 1;
        var across = AcrossX;
        var ends = Squares
            .Select(square => across ? new GridSquare(edge, square.Y) : new GridSquare(square.X, edge))
            .Distinct()
            .OrderBy(end => Distance.Between(end, from))
            .ToList();

        var banks = this;
        return Campaign.NearestFree(field, ends, square => banks.SideOf(square) == side);
    }
}
