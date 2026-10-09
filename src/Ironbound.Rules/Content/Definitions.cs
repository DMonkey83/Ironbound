using Ironbound.Rules.Abilities;
using Ironbound.Rules.Classes;
using Ironbound.Rules.Combat;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Defense;
using Ironbound.Rules.Items;
using Ironbound.Rules.Magic;
using Ironbound.Rules.Maps;
using Ironbound.Rules.Modifiers;
using Ironbound.Rules.Saves;
using Ironbound.Rules.Skills;

namespace Ironbound.Rules.Content;

/// <summary>Something wrong with a content file, named precisely enough to go and fix it.</summary>
public sealed record ContentProblem(string Source, string Field, string Message)
{
    public override string ToString() => $"{Source}: {Field}: {Message}";
}

/// <summary>
/// A weapon as written down. Held as a definition rather than an instance because a
/// <see cref="WeaponAttack"/> owns modifier stacks — two goblins must not share one scimitar.
/// </summary>
public sealed record WeaponDefinition
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public string Damage { get; init; } = "1d6";

    public DamageType DamageType { get; init; } = DamageType.Bludgeoning;

    public int ThreatsOn { get; init; } = 20;

    public int Multiplier { get; init; } = 2;

    public AbilityDamageScale Scale { get; init; } = AbilityDamageScale.Full;

    public Ability AttackAbility { get; init; } = Ability.Strength;

    public Ability DamageAbility { get; init; } = Ability.Strength;

    public int Enhancement { get; init; }

    /// <summary>Zero for a melee weapon; anything else makes it a ranged one.</summary>
    public int RangeIncrement { get; init; }

    public int MaximumIncrements { get; init; } = WeaponAttack.ProjectileIncrements;

    /// <summary>The fighter weapon groups it belongs to, by id.</summary>
    public IReadOnlyList<string> Groups { get; init; } = [];

    /// <summary>Light, natural, or otherwise a weapon Weapon Finesse can aim with Dexterity.</summary>
    public bool Finesse { get; init; }

    public WeaponAttack Build(string? name = null)
    {
        name ??= Name;
        var built = RangeIncrement > 0
            ? WeaponAttack.Ranged(
                name,
                Damage,
                DamageType,
                RangeIncrement,
                new CriticalProfile(ThreatsOn, Multiplier),
                MaximumIncrements,
                Scale,
                AttackAbility,
                DamageAbility)
            : WeaponAttack.Melee(
                name,
                Damage,
                DamageType,
                new CriticalProfile(ThreatsOn, Multiplier),
                Scale,
                AttackAbility,
                DamageAbility);

        // Init-only, and the factory methods know nothing of content ids, so the identity is
        // stamped on a copy of the shell that shares the very same attack and damage objects.
        var weapon = new WeaponAttack(built.Name, built.Attack, built.Damage)
        {
            Kind = Id,
            Groups = Groups,
            Finesse = Finesse,
            Qualities = built.Qualities,
            AttackAbility = built.AttackAbility,
            DamageAbility = built.DamageAbility,
            DamageScale = built.DamageScale,
            RangeIncrement = built.RangeIncrement,
            MaximumIncrements = built.MaximumIncrements,
        };

        if (Enhancement != 0)
        {
            weapon.Attack.Modifiers.Add(Enhancement, BonusType.Enhancement, name);
            weapon.DamageModifiers.Add(Enhancement, BonusType.Enhancement, name);
        }

        return weapon;
    }
}

/// <summary>
/// Damage reduction as written down: "DR 10/silver" is ten, bypassed by silver.
/// </summary>
/// <remarks>
/// The defence layer has understood this since it was written. Until items existed it was a
/// wall with no door — a creature could be immune to the party's weapons with no way for them
/// ever to acquire the right one.
/// </remarks>
public readonly record struct ReductionDefinition(int Amount, DamageBypass BypassedBy, BypassMode Mode);

/// <summary>Points spent in one skill.</summary>
public readonly record struct SkillRankDefinition(Skill Skill, int Ranks);

/// <summary>So many levels of a class, named by id until the library can resolve it.</summary>
public readonly record struct ClassLevelDefinition(string ClassId, int Level);

/// <summary>How many spell slots of one level a creature has.</summary>
public readonly record struct SlotDefinition(int Level, int Count);

/// <summary>A creature as written down, ready to be stamped out as many times as needed.</summary>
public sealed record CreatureDefinition
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    /// <summary>Strength, Dexterity, Constitution, Intelligence, Wisdom, Charisma.</summary>
    public required IReadOnlyList<int> Abilities { get; init; }

    /// <summary>Written down, or null to work it out from the classes.</summary>
    public int? HitPoints { get; init; }

    public int HitDice { get; init; } = 1;

    public int Level { get; init; } = 1;

    /// <summary>What it has levels in. Empty for something built from raw numbers instead.</summary>
    public IReadOnlyList<ClassLevelDefinition> Classes { get; init; } = [];

    public CreatureSize Size { get; init; } = CreatureSize.Medium;

    /// <summary>
    /// The model to draw it with, or empty for the placeholder shape.
    /// </summary>
    /// <remarks>
    /// A string the rules hold and never open, exactly as <see cref="TerrainDefinition"/>'s are.
    /// Appearance belongs in the creature's file rather than in a table in the scene code, for
    /// the same reason its hit dice do: adding a monster should be adding a file.
    /// </remarks>
    public string Model { get; init; } = string.Empty;

    public int Speed { get; init; } = 30;

    public int BaseAttack { get; init; }

    public int Armour { get; init; }

    public int NaturalArmour { get; init; }

    public Save? GoodSave { get; init; }

    public IReadOnlyList<string> Weapons { get; init; } = [];

    /// <summary>Feats it has taken, by id.</summary>
    public IReadOnlyList<string> Feats { get; init; } = [];

    /// <summary>Items it is wearing and holding, by id. Equipped in the order written.</summary>
    public IReadOnlyList<string> Items { get; init; } = [];

    /// <summary>What it shrugs off, and what gets through anyway.</summary>
    public IReadOnlyList<ReductionDefinition> Reductions { get; init; } = [];

    /// <summary>What it has trained at.</summary>
    public IReadOnlyList<SkillRankDefinition> SkillRanks { get; init; } = [];

    public Ability? CastingAbility { get; init; }

    public int CasterLevel { get; init; }

    public IReadOnlyList<SlotDefinition> Slots { get; init; } = [];

    public IReadOnlyList<string> Spells { get; init; } = [];

    /// <summary>What a wizard has written down, by id. Empty means the same as what she prepares.</summary>
    public IReadOnlyList<string> Spellbook { get; init; } = [];

    /// <summary>A fighter's weapon groups, by id, in the order picked: the first is trained deepest.</summary>
    public IReadOnlyList<string> WeaponTraining { get; init; } = [];

    /// <summary>Rogue talents and rage powers, by id, in the order taken.</summary>
    public IReadOnlyList<string> Talents { get; init; } = [];

    /// <summary>A cleric's god by id, "none" for a cleric of no god, or null for anybody else.</summary>
    public string? Deity { get; init; }

    /// <summary>A cleric's two domains, by id.</summary>
    public IReadOnlyList<string> Domains { get; init; } = [];

    /// <summary>How a cleric of a neutral god channels. A good or evil god decides for her.</summary>
    public ChannelKind? Channel { get; init; }

    /// <summary>A wizard's arcane school, by id.</summary>
    public string? School { get; init; }

    /// <summary>The two schools a specialist gave up.</summary>
    public IReadOnlyList<SpellSchool> Opposition { get; init; } = [];

    /// <summary>What a wizard's arcane bond is to: "object", the only one there is.</summary>
    public string? ArcaneBond { get; init; }

    /// <summary>The combat feat a War cleric's Weapon Master lends her, by id.</summary>
    public string? WeaponMaster { get; init; }

    public Creature Build(ContentLibrary library, RuleOptions? rules = null, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(library);

        var scores = new AbilityScores(
            Abilities[0], Abilities[1], Abilities[2], Abilities[3], Abilities[4], Abilities[5]);

        var levels = new List<ClassLevel>();
        foreach (var taken in Classes)
        {
            // A class the library never heard of is already in Problems; carrying on with the
            // levels that do resolve beats refusing to build the creature at all.
            if (library.GetClass(taken.ClassId) is { } definition)
            {
                levels.Add(new ClassLevel(definition, taken.Level));
            }
        }

        var classed = levels.Count > 0;
        var level = classed ? Progression.TotalLevel(levels) : Level;
        var hitPoints = HitPoints ?? (classed ? Progression.HitPointsBase(levels, rules) : 8);

        var creature = new Creature(
            name ?? Name, scores, hitPoints, classed ? level : HitDice, rules)
        {
            DefinitionId = Id,
            Size = Size,
            Speed = Speed,
        };

        foreach (var taken in levels)
        {
            creature.Levels.Add(taken);
        }

        creature.BaseAttackBonus = classed ? Progression.BaseAttack(levels) : BaseAttack;

        if (Armour != 0)
        {
            creature.ArmorClass.Modifiers.Add(Armour, BonusType.Armor, "Armour");
        }

        if (NaturalArmour != 0)
        {
            creature.ArmorClass.Modifiers.Add(NaturalArmour, BonusType.NaturalArmor, "Hide");
        }

        foreach (var save in SaveInfo.All)
        {
            creature.Saves[save].Base = classed
                ? Progression.SaveBase(save, levels)
                : save == GoodSave
                    ? SaveProgression.Good(level)
                    : SaveProgression.Poor(level);
        }

        // Before the weapons, because a feat that changes what a weapon does should already be
        // in place by the time one is handed over.
        foreach (var feat in Feats)
        {
            if (library.GetFeatWithChoice(feat) is { } taken)
            {
                ClassFeatures.Take(creature, taken);
            }
        }

        foreach (var rank in SkillRanks)
        {
            creature.Skills.SetRanks(rank.Skill, rank.Ranks);
        }

        foreach (var reduction in Reductions)
        {
            creature.Defenses.Reduce(reduction.Amount, reduction.BypassedBy, reduction.Mode);
        }

        foreach (var id in Items)
        {
            if (library.GetItem(id) is { } item)
            {
                creature.Equipment.Equip(item, library.BuildItemWeapon(item));
            }
        }

        foreach (var weapon in Weapons)
        {
            if (library.BuildWeapon(weapon) is { } built)
            {
                creature.Attacks.Add(built);
            }
        }

        var caster = Progression.Caster(levels);

        creature.Spells.CastingAbility =
            CastingAbility ?? caster?.Class.CastingAbility ?? Ability.Intelligence;
        creature.Spells.CasterLevel =
            CasterLevel != 0 ? CasterLevel : Progression.CasterLevel(levels);

        // Written-down slots win, for a monster whose magic is not a class. Otherwise the
        // class tables decide, which is what lets levelling up change them.
        if (Slots.Count > 0)
        {
            foreach (var slot in Slots)
            {
                creature.Spells.SetSlots(slot.Level, slot.Count);
            }
        }
        else if (classed)
        {
            var casting = creature.Spells.CastingAbility;

            foreach (var (spellLevel, count) in
                Progression.SlotsFor(levels, creature.Abilities[casting].Modifier))
            {
                creature.Spells.SetSlots(spellLevel, count);
            }
        }

        foreach (var spell in Spells)
        {
            if (library.GetSpell(spell) is { } known)
            {
                creature.Spells.Prepare(known);
            }
        }

        ChooseFor(creature, library);
        ClassFeatures.Establish(creature, library);

        return creature;
    }

    /// <summary>
    /// Hands the creature what its file chose for its class features. Anything the library has
    /// never heard of is already in its problems and is left out here.
    /// </summary>
    /// <remarks>
    /// Also how a save from before class features is brought up to date: its creatures are
    /// given whatever their files choose now.
    /// </remarks>
    internal void ChooseFor(Creature creature, ContentLibrary library)
    {
        var choices = creature.Choices;

        foreach (var group in WeaponTraining)
        {
            choices.WeaponGroups.Add(group);
        }

        foreach (var talent in Talents.Select(library.GetTalent).OfType<TalentDefinition>())
        {
            choices.Talents.Add(talent);
        }

        choices.Deity = Deity is { } god ? library.GetDeity(god) : null;

        foreach (var domain in Domains.Select(library.GetDomain).OfType<DomainDefinition>())
        {
            choices.Domains.Add(domain);
        }

        choices.Channel = Channel;
        choices.School = School is { } school ? library.GetSchool(school) : null;

        foreach (var opposed in Opposition)
        {
            choices.Opposition.Add(opposed);
        }

        choices.BondedObject = ArcaneBond == "object";
        choices.WeaponMasterFeat = WeaponMaster is { } feat ? library.GetFeat(feat) : null;

        foreach (var spell in Spellbook.Select(library.GetSpell).OfType<Spell>())
        {
            creature.Spells.Inscribe(spell);
        }
    }
}

/// <summary>
/// Where one creature starts, and whose side it is on. <paramref name="Name"/> overrides the
/// definition's, so three copies of one goblin can be told apart.
/// </summary>
/// <param name="Hidden">Lying in wait: one Stealth check against everybody's Perception.</param>
/// <param name="Asleep">
/// Caught napping: no actions and no Dexterity until it is hurt or the third round begins. A
/// fact about where the fight finds this creature, which is why it is written beside the square
/// rather than in the creature's own file — the same orc is wide awake in the next room.
/// </param>
public readonly record struct PlacementDefinition(
    string CreatureId,
    int X,
    int Y,
    bool Party,
    string? Name = null,
    bool Hidden = false,
    bool Asleep = false);

/// <summary>
/// A run of fights, in order, and how much respite there is along the way.
/// </summary>
/// <remarks>
/// The smallest thing that turns a fight into a game. A single encounter is won or lost on its
/// own terms; a sequence of them is won or lost on what you have left when you reach the end,
/// which is the resource game the whole ruleset is built around. Spell slots only matter when
/// there is a next fight to have wanted them for.
/// </remarks>
public sealed record CampaignDefinition
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    /// <summary>
    /// A line or two saying what the run is about, for whatever offers a choice of them.
    /// </summary>
    /// <remarks>
    /// Written in the file rather than composed from the encounters, because "four fights in
    /// some caves" is a fact and not a reason to pick it.
    /// </remarks>
    public string Description { get; init; } = string.Empty;

    /// <summary>The encounters, in the order they are fought.</summary>
    public IReadOnlyList<string> Encounters { get; init; } = [];

    /// <summary>
    /// The level it is played on, by id, or null for a run of separate fights.
    /// </summary>
    /// <remarks>
    /// When there is one it wins and <see cref="Encounters"/> is not read. Both may sit in the
    /// same file on purpose: the chain of fights is still the quickest way to play a campaign's
    /// battles back to back, which is what a balance test wants, and keeping it costs a line.
    /// </remarks>
    public string? Level { get; init; }

    /// <summary>
    /// How many times the party may stop and recover everything.
    /// </summary>
    /// <remarks>
    /// A budget rather than a cooldown, because a rest that costs nothing is one you always
    /// take, and a decision you always make the same way is not a decision. One rest across
    /// three fights is a question worth asking.
    /// </remarks>
    public int Rests { get; init; } = 1;
}

/// <summary>
/// What a battlefield is made of, as a set of model names.
/// </summary>
/// <remarks>
/// The rules layer holds these strings and never opens them: it has no notion of a file, let
/// alone of a mesh, and the whole point of keeping it free of Godot is that it stays that way.
/// What it does own is the <em>decision</em> — that this fight is fought among trees and that one
/// among rocks — because that belongs in the encounter file beside the width, the height and the
/// blocked squares, not in a switch statement in the scene code.
/// <para>
/// A model is named per battlefield feature rather than per square, so a terrain is three lines
/// of JSON and not a hand-painted map. Squares the game has no art for simply fall back to what
/// was drawn before: an untextured floor and a grey box.
/// </para>
/// </remarks>
public sealed record TerrainDefinition
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    /// <summary>The model laid on every ordinary square.</summary>
    public string Ground { get; init; } = string.Empty;

    /// <summary>What stands on a square nobody can walk through.</summary>
    public string Blocked { get; init; } = string.Empty;
}

/// <summary>
/// A fight as written down: the ground, and who stands where on it.
/// </summary>
/// <remarks>
/// Deliberately stops at data. Turning it into a playable battle needs the notion of sides, which
/// lives a layer up in <c>Ironbound.Simulation</c> — the rules know about allegiance, not about
/// which allegiance happens to be the player's.
/// </remarks>
public sealed record EncounterDefinition
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public int Width { get; init; } = 16;

    public int Height { get; init; } = 12;

    /// <summary>The terrain it is fought on, by id. Empty means the bare floor.</summary>
    public string Terrain { get; init; } = string.Empty;

    public IReadOnlyList<PlacementDefinition> Blocked { get; init; } = [];

    public IReadOnlyList<PlacementDefinition> Difficult { get; init; } = [];

    public IReadOnlyList<PlacementDefinition> Placements { get; init; } = [];

    /// <summary>
    /// What is read aloud as the chapter opens, or empty for a fight that needs no preamble.
    /// </summary>
    /// <remarks>
    /// The rules never look at it. It lives here because this is the one file that knows what
    /// the fight is <em>about</em> — and because an adventure is mostly the walking between
    /// fights: a cut bridge, a locked storeroom, a ledge you look down from. None of those is a
    /// battle, and a tactics game that has nowhere to say them loses the story between rooms.
    /// </remarks>
    public string Intro { get; init; } = string.Empty;

    /// <summary>
    /// Items found once the fight is won, by id, over and above whatever the fallen carried.
    /// </summary>
    /// <remarks>
    /// The storeroom behind the barracks, the strongbox under the bed. Stripping the bodies only
    /// finds what a foe was holding, and a good deal of what is worth finding in a dungeon is
    /// lying about rather than being swung at you. Only a win finds it: nobody searches the
    /// room they were driven out of.
    /// </remarks>
    public IReadOnlyList<string> Loot { get; init; } = [];
}

/// <summary>What one character of a level's map stands for.</summary>
/// <remarks>
/// A letter per square rather than lists of coordinates, because a level is drawn far more often
/// than it is computed: a person editing the file can see the room, and a stray character is a
/// typo they can find by eye.
/// </remarks>
public enum LevelCell
{
    /// <summary><c>#</c>, and anything off the edge of the map.</summary>
    Wall,

    /// <summary><c>.</c></summary>
    Stone,

    /// <summary><c>,</c></summary>
    Grass,

    /// <summary><c>T</c>: blocked, on grass.</summary>
    Tree,

    /// <summary><c>R</c>: a standing rock, blocked, on stone.</summary>
    Rock,

    /// <summary><c>~</c>: blocked until a bridge across it is made good.</summary>
    Chasm,

    /// <summary><c>+</c>: blocked until somebody opens it.</summary>
    Door,

    /// <summary><c>b</c></summary>
    Bed,

    /// <summary><c>t</c></summary>
    Table,

    /// <summary><c>c</c>: a crate, a barrel, a cart.</summary>
    Crate,
}

/// <summary>
/// One connected place, walked in real time and fought over a room at a time.
/// </summary>
/// <remarks>
/// The other way of stringing fights together. A campaign of separate encounters cuts from one
/// battlefield to the next and the walking between them happens off the page; a level keeps the
/// walking, which is where the cut bridge and the locked storeroom live. The fights themselves
/// are unchanged — a room's occupants wake when the party walks in, and from then on it is the
/// same turn-based battle as any other, fought on the same ground.
/// </remarks>
public sealed record LevelDefinition
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    /// <summary>Rows of characters, row 0 at y 0. Every row the same length.</summary>
    public IReadOnlyList<string> Map { get; init; } = [];

    public int Width => Map.Count == 0 ? 0 : Map[0].Length;

    public int Height => Map.Count;

    /// <summary>The terrain drawn on stone ground, by id.</summary>
    public string StoneTerrain { get; init; } = string.Empty;

    /// <summary>The terrain drawn on grass ground, by id.</summary>
    public string GrassTerrain { get; init; } = string.Empty;

    /// <summary>Where the party stands as the level opens.</summary>
    public IReadOnlyList<PlacementDefinition> Start { get; init; } = [];

    public IReadOnlyList<AreaDefinition> Areas { get; init; } = [];

    public IReadOnlyList<FeatureDefinition> Features { get; init; } = [];

    /// <summary>The kind of square a map character stands for, or null for one the legend lacks.</summary>
    public static LevelCell? Parse(char written) => written switch
    {
        '#' => LevelCell.Wall,
        '.' => LevelCell.Stone,
        ',' => LevelCell.Grass,
        'T' => LevelCell.Tree,
        'R' => LevelCell.Rock,
        '~' => LevelCell.Chasm,
        '+' => LevelCell.Door,
        'b' => LevelCell.Bed,
        't' => LevelCell.Table,
        'c' => LevelCell.Crate,
        _ => null,
    };

    /// <summary>
    /// What is at a square. Off the map, or a character the legend does not know, reads as
    /// wall: the loader has already complained about the second, and treating both as solid
    /// means nobody can walk out through a typo.
    /// </summary>
    public LevelCell CellAt(int x, int y)
    {
        if (y < 0 || y >= Map.Count || x < 0 || x >= Map[y].Length)
        {
            return LevelCell.Wall;
        }

        return Parse(Map[y][x]) ?? LevelCell.Wall;
    }

    public LevelCell CellAt(GridSquare square) => CellAt(square.X, square.Y);

    /// <summary>Whether nobody may stand there as the level is written, before any door opens.</summary>
    public bool IsBlockedCell(int x, int y) => IsBlocked(CellAt(x, y));

    public bool IsBlockedCell(GridSquare square) => IsBlockedCell(square.X, square.Y);

    public static bool IsBlocked(LevelCell cell) => cell is not (LevelCell.Stone or LevelCell.Grass);

    /// <summary>
    /// Whether a square's ground is grass rather than stone: grass under the trees, stone
    /// under everything else, walls included.
    /// </summary>
    public static bool IsGrassGround(LevelCell cell) => cell is LevelCell.Grass or LevelCell.Tree;

    /// <summary>The terrain id for the ground at a square.</summary>
    public string TerrainAt(int x, int y) =>
        IsGrassGround(CellAt(x, y)) ? GrassTerrain : StoneTerrain;

    public AreaDefinition? GetArea(string id) =>
        Areas.FirstOrDefault(area => string.Equals(area.Id, id, StringComparison.Ordinal));

    public FeatureDefinition? GetFeature(string id) =>
        Features.FirstOrDefault(feature => string.Equals(feature.Id, id, StringComparison.Ordinal));
}

/// <summary>
/// A room of a level: a rectangle that means something, and whoever is waiting in it.
/// </summary>
/// <remarks>
/// A rectangle because that is what a person can write down and check by eye. Rooms in a cave
/// are not rectangles, but the area only has to cover the room's floor, not trace its walls —
/// the walls are in the map already.
/// </remarks>
public sealed record AreaDefinition
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    /// <summary>Read the first time anybody walks in.</summary>
    public string Intro { get; init; } = string.Empty;

    /// <summary>Read once its occupants are dealt with.</summary>
    public string Outro { get; init; } = string.Empty;

    public int X { get; init; }

    public int Y { get; init; }

    public int Width { get; init; } = 1;

    public int Height { get; init; } = 1;

    /// <summary>Clearing this one wins the level.</summary>
    public bool Final { get; init; }

    /// <summary>Who is waiting, at their squares on the level's map.</summary>
    public IReadOnlyList<PlacementDefinition> Foes { get; init; } = [];

    /// <summary>Found once the room is won, over and above what the fallen carried.</summary>
    public IReadOnlyList<string> Loot { get; init; } = [];

    /// <summary>
    /// Experience for finding the place at all, given the first time anybody sets foot in it —
    /// whatever is waiting there, which earns its own when it is beaten.
    /// </summary>
    public int Experience { get; init; } = DefaultExperience;

    /// <summary>What finding a place is worth when its file does not say.</summary>
    public const int DefaultExperience = 50;

    public bool Contains(GridSquare square) =>
        square.X >= X && square.Y >= Y && square.X < X + Width && square.Y < Y + Height;
}

public enum FeatureKind
{
    /// <summary>Shut until somebody picks it or breaks it.</summary>
    Door,

    /// <summary>Down until somebody gets across and ties it off.</summary>
    Bridge,

    /// <summary>Something worth searching, searched once.</summary>
    Cache,
}

/// <summary>
/// Something in a level that is not a fight but still asks somebody to do something.
/// </summary>
/// <remarks>
/// The difficulty numbers live here rather than in the rules because they are facts about this
/// door and this chasm. The defaults are the rulebook's ordinary ones — a good lock, a sturdy
/// wooden door, a ten-foot gap — so a file only says something when this one is unusual.
/// </remarks>
public sealed record FeatureDefinition
{
    public required string Id { get; init; }

    public required FeatureKind Kind { get; init; }

    public required string Name { get; init; }

    /// <summary>What is read once it has been dealt with.</summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>
    /// Where it is. A door's squares are its doorway, a bridge's the chasm it spans, a cache's
    /// the furniture it is in.
    /// </summary>
    public IReadOnlyList<GridSquare> Squares { get; init; } = [];

    public int LockDc { get; init; } = 15;

    public int BreakDc { get; init; } = 16;

    public int JumpDc { get; init; } = 10;

    public int ClimbDc { get; init; } = 15;

    /// <summary>What a failed crossing costs, in dice.</summary>
    public string Fall { get; init; } = "2d6";

    /// <summary>What a cache holds, by item id.</summary>
    public IReadOnlyList<string> Loot { get; init; } = [];

    /// <summary>Experience for dealing with it: the door opened, the chasm crossed, the cache found.</summary>
    public int Experience { get; init; }

    /// <summary>What dealing with one of these is worth when its file does not say: a crossing
    /// that can break a leg is worth more than a door.</summary>
    public static int DefaultExperience(FeatureKind kind) => kind switch
    {
        FeatureKind.Bridge => 100,
        FeatureKind.Door => 50,
        _ => 25,
    };
}
