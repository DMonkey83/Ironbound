using System.Text.Json;
using Ironbound.Rules.Abilities;
using Ironbound.Rules.Classes;
using Ironbound.Rules.Combat;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Defense;
using Ironbound.Rules.Dice;
using Ironbound.Rules.Effects;
using Ironbound.Rules.Feats;
using Ironbound.Rules.Items;
using Ironbound.Rules.Magic;
using Ironbound.Rules.Maps;
using Ironbound.Rules.Modifiers;
using Ironbound.Rules.Saves;
using Ironbound.Rules.Skills;

namespace Ironbound.Rules.Content;

/// <summary>
/// Everything the game is made of, read from files.
/// </summary>
/// <remarks>
/// Takes JSON <em>text</em> rather than paths, exactly as the save layer does. That is not
/// fastidiousness: in an exported Godot build the content lives inside a <c>.pck</c> archive and
/// is not a filesystem path at all, so a loader that opened directories would work in the editor
/// and fail the moment the game was packaged. It also means every test here needs no files.
/// <para>
/// Problems are collected rather than thrown, so one mistyped field does not hide the nine after
/// it. Whatever loads the content should check <see cref="Problems"/> and complain loudly.
/// </para>
/// </remarks>
public sealed class ContentLibrary
{
    private readonly Dictionary<string, Spell> _spells = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ClassDefinition> _classes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, FeatDefinition> _feats = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ItemDefinition> _items = new(StringComparer.Ordinal);
    private readonly Dictionary<string, WeaponDefinition> _weapons = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CreatureDefinition> _creatures = new(StringComparer.Ordinal);
    private readonly Dictionary<string, EncounterDefinition> _encounters = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CampaignDefinition> _campaigns = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TerrainDefinition> _terrains = new(StringComparer.Ordinal);
    private readonly Dictionary<string, LevelDefinition> _levels = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TalentDefinition> _talents = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DomainDefinition> _domains = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SchoolDefinition> _schools = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DeityDefinition> _deities = new(StringComparer.Ordinal);
    private readonly List<ContentProblem> _problems = [];

    /// <summary>
    /// Trailing commas and comments, both of which strict JSON forbids and both of which a file
    /// edited by a person wants. The comments matter most: the spells used to be C# and carried
    /// notes saying which parts were simplified from the rulebook, and a format with nowhere to
    /// put those would have quietly thrown them away.
    /// </summary>
    private static readonly JsonDocumentOptions Lenient = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
    };

    /// <summary>Reads every file and then checks that everything they refer to exists.</summary>
    public static ContentLibrary Load(IEnumerable<(string Source, string Json)> files)
    {
        ArgumentNullException.ThrowIfNull(files);

        var library = new ContentLibrary();
        foreach (var (source, json) in files)
        {
            library.Add(source, json);
        }

        library.Validate();
        return library;
    }

    public IReadOnlyList<ContentProblem> Problems => _problems;

    public bool IsValid => _problems.Count == 0;

    public IReadOnlyCollection<string> SpellIds => _spells.Keys;

    public IReadOnlyCollection<string> ClassIds => _classes.Keys;

    public IReadOnlyCollection<string> FeatIds => _feats.Keys;

    public IReadOnlyCollection<string> ItemIds => _items.Keys;

    public IReadOnlyCollection<string> WeaponIds => _weapons.Keys;

    public IReadOnlyCollection<string> CreatureIds => _creatures.Keys;

    public IReadOnlyCollection<string> EncounterIds => _encounters.Keys;

    public IReadOnlyCollection<string> CampaignIds => _campaigns.Keys;

    public IReadOnlyCollection<string> TerrainIds => _terrains.Keys;

    public IReadOnlyCollection<string> LevelIds => _levels.Keys;

    public IReadOnlyCollection<string> TalentIds => _talents.Keys;

    public IReadOnlyCollection<string> DomainIds => _domains.Keys;

    public IReadOnlyCollection<string> SchoolIds => _schools.Keys;

    public IReadOnlyCollection<string> DeityIds => _deities.Keys;

    /// <summary>Every rogue talent and rage power, ordered by id so a list never reshuffles.</summary>
    public IReadOnlyList<TalentDefinition> Talents =>
        [.. _talents.Values.OrderBy(talent => talent.Id, StringComparer.Ordinal)];

    /// <summary>Every god, ordered by name.</summary>
    public IReadOnlyList<DeityDefinition> Deities =>
        [.. _deities.Values.OrderBy(deity => deity.Name, StringComparer.Ordinal)];

    /// <summary>Every level, ordered by id so the list is the same every launch.</summary>
    public IReadOnlyList<LevelDefinition> Levels =>
        [.. _levels.Values.OrderBy(level => level.Id, StringComparer.Ordinal)];

    /// <summary>
    /// Every campaign, ordered by name and then id, for a menu to offer.
    /// </summary>
    /// <remarks>
    /// Sorted rather than handed over in whatever order the dictionary keeps, because a picker
    /// whose entries swapped places between launches would be a bug nobody could reproduce.
    /// The id breaks ties so that two campaigns somebody happened to give the same name still
    /// come out the same way round every time.
    /// </remarks>
    public IReadOnlyList<CampaignDefinition> Campaigns =>
        [.. _campaigns.Values
            .OrderBy(run => run.Name, StringComparer.Ordinal)
            .ThenBy(run => run.Id, StringComparer.Ordinal)];

    public Spell? GetSpell(string id) => _spells.GetValueOrDefault(id);

    public EncounterDefinition? GetEncounter(string id) => _encounters.GetValueOrDefault(id);

    public TerrainDefinition? GetTerrain(string id) => _terrains.GetValueOrDefault(id);

    public CampaignDefinition? GetCampaign(string id) => _campaigns.GetValueOrDefault(id);

    public LevelDefinition? GetLevel(string id) => _levels.GetValueOrDefault(id);

    public CreatureDefinition? GetCreature(string id) => _creatures.GetValueOrDefault(id);

    public ClassDefinition? GetClass(string id) => _classes.GetValueOrDefault(id);

    public FeatDefinition? GetFeat(string id) => _feats.GetValueOrDefault(id);

    public ItemDefinition? GetItem(string id) => _items.GetValueOrDefault(id);

    public TalentDefinition? GetTalent(string id) => _talents.GetValueOrDefault(id);

    public DomainDefinition? GetDomain(string id) => _domains.GetValueOrDefault(id);

    public SchoolDefinition? GetSchool(string id) => _schools.GetValueOrDefault(id);

    public DeityDefinition? GetDeity(string id) => _deities.GetValueOrDefault(id);

    public WeaponDefinition? GetWeapon(string id) => _weapons.GetValueOrDefault(id);

    /// <summary>
    /// A feat as a creature file or a save names it: "weapon-focus", or "weapon-focus:longsword"
    /// with the choice made. Null when the feat does not exist.
    /// </summary>
    public FeatDefinition? GetFeatWithChoice(string key)
    {
        ArgumentNullException.ThrowIfNull(key);

        var split = key.IndexOf(':', StringComparison.Ordinal);
        if (split < 0)
        {
            return GetFeat(key);
        }

        return GetFeat(key[..split]) is { } feat ? feat with { Choice = key[(split + 1)..] } : null;
    }

    public WeaponAttack? BuildWeapon(string id, string? name = null) =>
        _weapons.GetValueOrDefault(id)?.Build(name);

    /// <summary>
    /// The attack a weapon item makes, named for the item rather than the kind of weapon — so a
    /// silvered longsword says so in the log every time it swings.
    /// </summary>
    public WeaponAttack? BuildItemWeapon(ItemDefinition item)
    {
        ArgumentNullException.ThrowIfNull(item);

        return item.Weapon is { } weapon && BuildWeapon(weapon, item.Name) is { } built
            ? item.Dress(built)
            : null;
    }

    public Creature? BuildCreature(string id, RuleOptions? rules = null, string? name = null) =>
        _creatures.GetValueOrDefault(id)?.Build(this, rules, name);

    /// <summary>Reads one file. <paramref name="source"/> only ever appears in problems.</summary>
    public void Add(string source, string json)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(json);

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json, Lenient);
        }
        catch (JsonException error)
        {
            _problems.Add(new ContentProblem(source, "(file)", error.Message));
            return;
        }

        using (document)
        {
            var reader = new Reader(document.RootElement, source, _problems);
            var kind = reader.String("kind");

            switch (kind)
            {
                case "spell":
                    Keep(_spells, reader, ReadSpell(reader), spell => spell.Id, "spell");
                    break;

                case "class":
                    Keep(_classes, reader, ReadClass(reader), taken => taken.Id, "class");
                    break;

                case "feat":
                    Keep(_feats, reader, ReadFeat(reader), feat => feat.Id, "feat");
                    break;

                case "item":
                    Keep(_items, reader, ReadItem(reader), item => item.Id, "item");
                    break;

                case "weapon":
                    Keep(_weapons, reader, ReadWeapon(reader), weapon => weapon.Id, "weapon");
                    break;

                case "creature":
                    Keep(_creatures, reader, ReadCreature(reader), creature => creature.Id, "creature");
                    break;

                case "campaign":
                    Keep(_campaigns, reader, ReadCampaign(reader), run => run.Id, "campaign");
                    break;

                case "encounter":
                    Keep(_encounters, reader, ReadEncounter(reader), encounter => encounter.Id, "encounter");
                    break;

                case "terrain":
                    Keep(_terrains, reader, ReadTerrain(reader), terrain => terrain.Id, "terrain");
                    break;

                case "level":
                    Keep(_levels, reader, ReadLevel(reader), level => level.Id, "level");
                    break;

                case "talent":
                    Keep(_talents, reader, ReadTalent(reader), talent => talent.Id, "talent");
                    break;

                case "domain":
                    Keep(_domains, reader, ReadDomain(reader), domain => domain.Id, "domain");
                    break;

                case "school":
                    Keep(_schools, reader, ReadSchool(reader), school => school.Id, "school");
                    break;

                case "deity":
                    Keep(_deities, reader, ReadDeity(reader), deity => deity.Id, "deity");
                    break;

                case "":
                    break;

                default:
                    reader.Problem("kind", $"'{kind}' is not something this game has.");
                    break;
            }
        }
    }

    /// <summary>Checks that every id referred to by something actually exists.</summary>
    public void Validate()
    {
        foreach (var creature in _creatures.Values)
        {
            foreach (var weapon in creature.Weapons.Where(id => !_weapons.ContainsKey(id)))
            {
                _problems.Add(new ContentProblem(
                    $"creature '{creature.Id}'", "weapons", $"no weapon called '{weapon}'."));
            }

            foreach (var taken in creature.Classes.Where(c => !_classes.ContainsKey(c.ClassId)))
            {
                _problems.Add(new ContentProblem(
                    $"creature '{creature.Id}'", "classes", $"no class called '{taken.ClassId}'."));
            }

            foreach (var item in creature.Items.Where(id => !_items.ContainsKey(id)))
            {
                _problems.Add(new ContentProblem(
                    $"creature '{creature.Id}'", "items", $"no item called '{item}'."));
            }

            foreach (var feat in creature.Feats)
            {
                ValidateFeatKey($"creature '{creature.Id}'", "feats", feat);
            }

            ValidateChoices(creature);

            foreach (var spell in creature.Spells.Where(id => !_spells.ContainsKey(id)))
            {
                _problems.Add(new ContentProblem(
                    $"creature '{creature.Id}'", "spells", $"no spell called '{spell}'."));
            }
        }

        // A sibling of the creature loop, not a child of it. Nested, the same dangling
        // prerequisite was reported once per creature in the library, and never reported at
        // all in a library that happened to contain no creatures.
        foreach (var feat in _feats.Values)
        {
            foreach (var wanted in feat.Requires.Feats.Where(id => !_feats.ContainsKey(id)))
            {
                _problems.Add(new ContentProblem(
                    $"feat '{feat.Id}'", "requires", $"no feat called '{wanted}'."));
            }
        }

        foreach (var taken in _classes.Values)
        {
            ValidateClass(taken);
        }

        foreach (var talent in _talents.Values)
        {
            foreach (var wanted in talent.Requires.Where(id => !_talents.ContainsKey(id)))
            {
                _problems.Add(new ContentProblem(
                    $"talent '{talent.Id}'", "requires", $"no talent called '{wanted}'."));
            }
        }

        foreach (var domain in _domains.Values)
        {
            foreach (var spell in domain.Spells.Where(id => id.Length > 0 && !_spells.ContainsKey(id)))
            {
                _problems.Add(new ContentProblem(
                    $"domain '{domain.Id}'", "spells", $"no spell called '{spell}'."));
            }
        }

        foreach (var weapon in _weapons.Values)
        {
            foreach (var group in weapon.Groups.Where(group => !Martial.WeaponGroups.Contains(group)))
            {
                _problems.Add(new ContentProblem(
                    $"weapon '{weapon.Id}'", "groups", $"'{group}' is not a weapon group."));
            }
        }

        foreach (var item in _items.Values)
        {
            if (item.Weapon is { } weapon && !_weapons.ContainsKey(weapon))
            {
                _problems.Add(new ContentProblem(
                    $"item '{item.Id}'", "weapon", $"no weapon called '{weapon}'."));
            }
        }

        foreach (var run in _campaigns.Values)
        {
            foreach (var id in run.Encounters.Where(id => !_encounters.ContainsKey(id)))
            {
                _problems.Add(new ContentProblem(
                    $"campaign '{run.Id}'", "encounters", $"no encounter called '{id}'."));
            }

            if (run.Level is { } level && !_levels.ContainsKey(level))
            {
                _problems.Add(new ContentProblem(
                    $"campaign '{run.Id}'", "level", $"no level called '{level}'."));
            }
        }

        foreach (var level in _levels.Values)
        {
            ValidateLevel(level);
        }

        foreach (var encounter in _encounters.Values)
        {
            if (encounter.Terrain.Length > 0 && !_terrains.ContainsKey(encounter.Terrain))
            {
                _problems.Add(new ContentProblem(
                    $"encounter '{encounter.Id}'",
                    "terrain",
                    $"no terrain called '{encounter.Terrain}'."));
            }

            foreach (var placement in encounter.Placements
                .Where(p => !_creatures.ContainsKey(p.CreatureId)))
            {
                _problems.Add(new ContentProblem(
                    $"encounter '{encounter.Id}'",
                    "placements",
                    $"no creature called '{placement.CreatureId}'."));
            }

            foreach (var item in encounter.Loot.Where(id => !_items.ContainsKey(id)))
            {
                _problems.Add(new ContentProblem(
                    $"encounter '{encounter.Id}'", "loot", $"no item called '{item}'."));
            }
        }
    }

    /// <summary>A class's feature table: every row an id the code knows, with parameters it accepts.</summary>
    private void ValidateClass(ClassDefinition taken)
    {
        var source = $"class '{taken.Id}'";

        foreach (var row in taken.Features)
        {
            if (!FeatureIds.Known.TryGetValue(row.Id, out var accepts))
            {
                _problems.Add(new ContentProblem(source, "features", $"'{row.Id}' is not a class feature this game has."));
                continue;
            }

            foreach (var (name, value) in row.Parameters)
            {
                if (!accepts.TryGetValue(name, out var allowed))
                {
                    _problems.Add(new ContentProblem(source, "features", $"'{row.Id}' takes no '{name}'."));
                }
                else if (allowed.Count > 0 && !allowed.Contains(value))
                {
                    _problems.Add(new ContentProblem(
                        source, "features", $"'{row.Id}' {name} '{value}' is not one of {string.Join(", ", allowed)}."));
                }
            }

            if (row.Level < 1)
            {
                _problems.Add(new ContentProblem(source, "features", $"'{row.Id}' is at level {row.Level}."));
            }
        }
    }

    /// <summary>"weapon-focus" or "weapon-focus:longsword": the feat exists, and so does what it was taken for.</summary>
    private void ValidateFeatKey(string source, string field, string key)
    {
        var split = key.IndexOf(':', StringComparison.Ordinal);
        var id = split < 0 ? key : key[..split];

        if (!_feats.TryGetValue(id, out var feat))
        {
            _problems.Add(new ContentProblem(source, field, $"no feat called '{id}'."));
            return;
        }

        if (split >= 0 && feat.Takes == FeatChoice.Weapon && !_weapons.ContainsKey(key[(split + 1)..]))
        {
            _problems.Add(new ContentProblem(source, field, $"no weapon called '{key[(split + 1)..]}' for {feat.Name}."));
        }
        else if (split >= 0 && feat.Takes == FeatChoice.None)
        {
            _problems.Add(new ContentProblem(source, field, $"{feat.Name} is not taken for anything in particular."));
        }
    }

    /// <summary>
    /// What a creature chose for its class features: everything named exists, a cleric's domains
    /// are her god's, a specialist gives up two schools that are not her own.
    /// </summary>
    /// <remarks>
    /// A deity's alignment domains — Good, Law and the rest — would also want the cleric's own
    /// alignment to match. Creatures have no alignment yet, so that half is not checked.
    /// </remarks>
    private void ValidateChoices(CreatureDefinition creature)
    {
        var source = $"creature '{creature.Id}'";
        void Problem(string field, string message) => _problems.Add(new ContentProblem(source, field, message));

        foreach (var group in creature.WeaponTraining.Where(group => !Martial.WeaponGroups.Contains(group)))
        {
            Problem("weaponTraining", $"'{group}' is not a weapon group.");
        }

        foreach (var talent in creature.Talents.Where(id => !_talents.ContainsKey(id)))
        {
            Problem("talents", $"no talent called '{talent}'.");
        }

        DeityDefinition? deity = null;
        if (creature.Deity is { } god && god != "none" && !_deities.TryGetValue(god, out deity))
        {
            Problem("deity", $"no deity called '{god}'.");
        }

        var cleric = creature.Classes.Any(level =>
            _classes.TryGetValue(level.ClassId, out var taken)
            && taken.Features.Any(row => row.Id == FeatureIds.Domains));

        if (cleric && creature.Deity is null)
        {
            Problem("deity", "a cleric needs a god, or \"none\".");
        }

        if (creature.Domains.Count > 2)
        {
            Problem("domains", $"a cleric has two domains, not {creature.Domains.Count}.");
        }

        foreach (var domain in creature.Domains)
        {
            if (!_domains.ContainsKey(domain))
            {
                Problem("domains", $"no domain called '{domain}'.");
            }
            else if (deity is not null && !deity.Offers(domain))
            {
                Problem("domains", $"{deity.Name} does not grant the {domain} domain.");
            }
        }

        if (creature.School is { } school)
        {
            if (!_schools.TryGetValue(school, out var found))
            {
                Problem("school", $"no arcane school called '{school}'.");
            }
            else if (found.IsUniversalist && creature.Opposition.Count > 0)
            {
                Problem("opposition", "a universalist gives up no schools.");
            }
            else if (!found.IsUniversalist && creature.Opposition.Count != 2)
            {
                Problem("opposition", $"a specialist gives up two schools, not {creature.Opposition.Count}.");
            }
            else if (found.School is { } own && creature.Opposition.Contains(own))
            {
                Problem("opposition", $"{found.Name} cannot be its own opposition school.");
            }
        }
        else if (creature.Opposition.Count > 0)
        {
            Problem("opposition", "only a specialist with a school gives any up.");
        }

        foreach (var spell in creature.Spellbook.Where(id => !_spells.ContainsKey(id)))
        {
            Problem("spellbook", $"no spell called '{spell}'.");
        }

        if (creature.ArcaneBond is { } bond && bond != "object")
        {
            Problem("arcaneBond", $"'{bond}' is not a bond the game has; only 'object' is.");
        }

        if (creature.WeaponMaster is { } master)
        {
            if (!_feats.TryGetValue(master, out var feat))
            {
                Problem("weaponMaster", $"no feat called '{master}'.");
            }
            else if (!feat.Combat)
            {
                Problem("weaponMaster", $"{feat.Name} is not a combat feat.");
            }
        }
    }

    private void Keep<T>(
        Dictionary<string, T> into,
        Reader reader,
        T? value,
        Func<T, string> identify,
        string what)
        where T : class
    {
        if (value is null)
        {
            return;
        }

        var id = identify(value);
        if (string.IsNullOrWhiteSpace(id))
        {
            reader.Problem("id", $"every {what} needs one.");
            return;
        }

        if (!into.TryAdd(id, value))
        {
            reader.Problem("id", $"there is already a {what} called '{id}'.");
        }
    }

    // ---- reading each kind ----

    private Spell? ReadSpell(Reader reader)
    {
        var id = reader.String("id");
        if (id.Length == 0)
        {
            return null;
        }

        var does = new List<SpellEffect>();
        foreach (var entry in reader.Array("does"))
        {
            var part = new Reader(entry, reader.Source, _problems);

            if (part.Has("damage"))
            {
                does.Add(new DealDamage(
                    ReadDice(part.Object("damage")),
                    part.Enum("type", DamageType.Untyped)));
            }
            else if (part.Has("heal"))
            {
                does.Add(new Restore(ReadDice(part.Object("heal"))));
            }
            else if (part.Has("temporary"))
            {
                does.Add(new Bolster(ReadDice(part.Object("temporary"))));
            }
            else if (part.Has("effect"))
            {
                does.Add(new Bestow(ReadEffect(part.Object("effect"))));
            }
            else
            {
                part.Problem("does", "needs one of 'damage', 'heal', 'temporary' or 'effect'.");
            }
        }

        var target = reader.Object("target");

        return new Spell(
            id,
            reader.StringOr("name", id),
            reader.Int("level"),
            reader.Enum("school", SpellSchool.Evocation))
        {
            Range = ReadRange(reader.StringOr("range", "Close"), reader),
            Target = target.Has("burst") ? new BurstTarget(target.Int("burst"))
                : target.StringOr("shape", "single") switch
                {
                    "ray" => new RayTarget(),
                    "self" => new SelfTarget { RadiusFeet = target.Int("radius") },
                    "point" => new PointTarget(),
                    _ => new SingleTarget(),
                },
            Descriptors = [.. reader.Array("descriptors").Select(e => e.GetString() ?? string.Empty)],
            Affects = reader.Enum("affects", SpellAffects.Enemies),
            Save = reader.Has("save") ? reader.Enum("save", Save.Reflex) : null,
            OnSave = reader.Enum("onSave", SaveOutcome.Negates),
            Does = does,
        };
    }

    private static SpellRange ReadRange(string range, Reader reader) => range.ToLowerInvariant() switch
    {
        "personal" => SpellRange.Personal,
        "touch" => SpellRange.Touch,
        "close" => SpellRange.Close,
        "medium" => SpellRange.Medium,
        "long" => SpellRange.Long,
        _ when int.TryParse(range, out var feet) => SpellRange.Of(feet),
        _ => Complain(reader, range),
    };

    private static SpellRange Complain(Reader reader, string range)
    {
        reader.Problem("range", $"'{range}' is not a range. Try personal, touch, close, medium, long or a number.");
        return SpellRange.Close;
    }

    private static SpellDice ReadDice(Reader reader)
    {
        var dice = reader.Has("fixed")
            ? SpellDice.Fixed(reader.StringOr("fixed", "1d6"))
            : SpellDice.PerLevel(
                reader.Int("perLevel", 6),
                reader.Int("maxDice", 10),
                reader.Int("levelsPerDie", 1),
                reader.Int("flatPerDie", 0));

        // "plusPerLevel": the cure spells' +1 a caster level, to "plusMaximum".
        return reader.Has("plusPerLevel")
            ? dice.PlusPerLevel(reader.Int("plusPerLevel"), reader.Int("plusMaximum"))
            : dice;
    }

    private EffectDefinition ReadEffect(Reader reader)
    {
        var grants = new List<ModifierGrant>();
        foreach (var entry in reader.Array("grants"))
        {
            var grant = new Reader(entry, reader.Source, _problems);
            grants.Add(new ModifierGrant(
                ReadTarget(grant),
                grant.Int("value"),
                grant.Enum("type", BonusType.Untyped)));
        }

        return new EffectDefinition
        {
            Name = reader.StringOr("name", "effect"),
            Kind = reader.Enum("kind", EffectKind.Modifier),
            Condition = reader.Has("condition")
                ? reader.Enum("condition", Conditions.Condition.Shaken)
                : null,
            Permanent = reader.Bool("permanent"),
            DurationTicks = reader.Int("rounds") * Duration.TicksPerRound
                + (reader.Int("minutes") * Duration.TicksPerMinute),
            TicksPerLevel = reader.Int("roundsPerLevel") * Duration.TicksPerRound
                + (reader.Int("minutesPerLevel") * Duration.TicksPerMinute)
                + (reader.Int("hoursPerLevel") * Duration.TicksPerHour),
            ScaleEvery = reader.Int("scaleEvery"),
            ScaleMaximum = reader.Int("scaleMaximum"),
            PeriodTicks = reader.Int("periodRounds") * Duration.TicksPerRound,
            Grants = grants,
            Amount = reader.Has("amount") ? reader.StringOr("amount", "1d6") : null,
            DamageType = reader.Enum("damageType", DamageType.Untyped),
            Heal = reader.Int("heal"),
            SuspendedBy = [.. reader.Array("suspendedBy")
                .Select(e => System.Enum.TryParse<DamageType>(e.GetString(), true, out var t) ? t : DamageType.Untyped)],
        };
    }

    private static ModifierTarget ReadTarget(Reader reader)
    {
        var target = reader.StringOr("target", "attack");

        if (target.StartsWith("ability.", StringComparison.OrdinalIgnoreCase)
            && System.Enum.TryParse<Ability>(target[8..], true, out var ability))
        {
            return ModifierTarget.Ability(ability);
        }

        if (target.StartsWith("save.", StringComparison.OrdinalIgnoreCase)
            && System.Enum.TryParse<Save>(target[5..], true, out var save))
        {
            return ModifierTarget.Save(save);
        }

        return target.ToLowerInvariant() switch
        {
            "armorclass" or "armourclass" or "ac" => ModifierTarget.ArmorClass,
            "damage" => ModifierTarget.Damage,
            "speed" => ModifierTarget.Speed,
            "initiative" => ModifierTarget.Initiative,
            _ => ModifierTarget.Attack,
        };
    }

    private ClassDefinition? ReadClass(Reader reader)
    {
        var id = reader.String("id");
        if (id.Length == 0)
        {
            return null;
        }

        var good = new List<Save>();
        foreach (var entry in reader.Array("goodSaves"))
        {
            if (System.Enum.TryParse<Save>(entry.GetString(), true, out var save))
            {
                good.Add(save);
            }
            else
            {
                reader.Problem("goodSaves", $"'{entry.GetString()}' is not a saving throw.");
            }
        }

        var classSkills = new List<Skill>();
        foreach (var entry in reader.Array("classSkills"))
        {
            if (System.Enum.TryParse<Skill>(entry.GetString(), true, out var skill))
            {
                classSkills.Add(skill);
            }
            else
            {
                reader.Problem("classSkills", $"'{entry.GetString()}' is not a skill.");
            }
        }

        var slots = new List<IReadOnlyList<int>>();
        foreach (var row in reader.Array("spellSlots"))
        {
            slots.Add(row.ValueKind == JsonValueKind.Array
                ? [.. row.EnumerateArray().Select(each => each.TryGetInt32(out var n) ? n : 0)]
                : []);
        }

        var features = new List<ClassFeatureDefinition>();
        foreach (var entry in reader.Array("features"))
        {
            var row = new Reader(entry, reader.Source, _problems);
            var parameters = new Dictionary<string, string>(StringComparer.Ordinal);

            if (entry.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in entry.EnumerateObject())
                {
                    if (property.Name is not ("level" or "id"))
                    {
                        parameters[property.Name] = property.Value.ValueKind == JsonValueKind.String
                            ? property.Value.GetString() ?? string.Empty
                            : property.Value.ToString();
                    }
                }
            }

            features.Add(new ClassFeatureDefinition(row.Int("level", 1), row.String("id"), parameters));
        }

        return new ClassDefinition
        {
            Id = id,
            Name = reader.StringOr("name", id),
            HitDie = reader.Int("hitDie", 8),
            Features = features,
            Attack = reader.Enum("attack", AttackProgression.ThreeQuarters),
            GoodSaves = good,
            Casting = reader.Enum("casting", CasterProgression.None),
            CastingAbility = reader.Enum("castingAbility", Ability.Intelligence),
            ClassSkills = classSkills,
            SpellSlots = slots,
        };
    }

    private FeatDefinition? ReadFeat(Reader reader)
    {
        var id = reader.String("id");
        if (id.Length == 0)
        {
            return null;
        }

        var grants = new List<ModifierGrant>();
        foreach (var entry in reader.Array("grants"))
        {
            var grant = new Reader(entry, reader.Source, _problems);
            grants.Add(new ModifierGrant(
                ReadTarget(grant), grant.Int("value"), grant.Enum("type", BonusType.Untyped)));
        }

        var wants = reader.Object("requires");
        var minimums = new Dictionary<Ability, int>();

        foreach (var ability in AbilityInfo.All)
        {
            var key = AbilityInfo.Abbreviate(ability).ToLowerInvariant();
            if (wants.Has(key))
            {
                minimums[ability] = wants.Int(key);
            }
        }

        return new FeatDefinition
        {
            Id = id,
            Requires = new FeatRequirements
            {
                Abilities = minimums,
                Feats = [.. wants.Array("feats").Select(e => e.GetString() ?? string.Empty)],
                BaseAttack = wants.Int("baseAttack"),
                Level = wants.Int("level"),
                Features = [.. wants.Array("features").Select(e => e.GetString() ?? string.Empty)],
            },
            Name = reader.StringOr("name", id),
            Description = reader.StringOr("description", string.Empty),
            Grants = grants,
            Effect = reader.Enum("effect", FeatEffect.None),
            Combat = reader.Bool("combat"),
            Metamagic = reader.Bool("metamagic"),
            ItemCreation = reader.Bool("itemCreation"),
            Takes = reader.Enum("takes", FeatChoice.None),
        };
    }

    private ItemDefinition? ReadItem(Reader reader)
    {
        var id = reader.String("id");
        if (id.Length == 0)
        {
            return null;
        }

        var grants = new List<ModifierGrant>();
        foreach (var entry in reader.Array("grants"))
        {
            var grant = new Reader(entry, reader.Source, _problems);
            grants.Add(new ModifierGrant(
                ReadTarget(grant), grant.Int("value"), grant.Enum("type", BonusType.Untyped)));
        }

        var qualities = DamageBypass.None;
        foreach (var entry in reader.Array("qualities"))
        {
            if (System.Enum.TryParse<DamageBypass>(entry.GetString(), true, out var quality))
            {
                qualities |= quality;
            }
            else
            {
                reader.Problem("qualities", $"'{entry.GetString()}' is not a material or alignment.");
            }
        }

        return new ItemDefinition
        {
            Id = id,
            Name = reader.StringOr("name", id),
            Description = reader.StringOr("description", string.Empty),
            Slot = reader.Enum("slot", EquipmentSlot.Carried),
            Model = reader.StringOr("model", string.Empty),
            Grants = grants,
            Weapon = reader.Has("weapon") ? reader.StringOr("weapon", string.Empty) : null,
            Enhancement = reader.Int("enhancement"),
            Qualities = qualities,
            Armour = reader.Enum("armour", ArmourCategory.None),
            MaxDexterity = reader.Has("maxDex") ? reader.Int("maxDex") : null,
            CheckPenalty = reader.Int("checkPenalty"),
        };
    }

    private WeaponDefinition? ReadWeapon(Reader reader)
    {
        var id = reader.String("id");
        return id.Length == 0 ? null : new WeaponDefinition
        {
            Id = id,
            Name = reader.StringOr("name", id),
            Damage = reader.StringOr("damage", "1d6"),
            DamageType = reader.Enum("damageType", DamageType.Bludgeoning),
            ThreatsOn = reader.Int("threatsOn", 20),
            Multiplier = reader.Int("multiplier", 2),
            Scale = reader.Enum("scale", AbilityDamageScale.Full),
            AttackAbility = reader.Enum("attackAbility", Ability.Strength),
            DamageAbility = reader.Enum("damageAbility", Ability.Strength),
            Enhancement = reader.Int("enhancement"),
            RangeIncrement = reader.Int("rangeIncrement"),
            MaximumIncrements = reader.Int("maximumIncrements", WeaponAttack.ProjectileIncrements),
            Groups = [.. reader.Array("groups").Select(e => e.GetString() ?? string.Empty)],
            Finesse = reader.Bool("finesse"),
        };
    }

    private CreatureDefinition? ReadCreature(Reader reader)
    {
        var id = reader.String("id");
        if (id.Length == 0)
        {
            return null;
        }

        var abilities = reader.Array("abilities").Select(e => e.GetInt32()).ToArray();
        if (abilities.Length != AbilityInfo.Count)
        {
            reader.Problem("abilities", $"needs {AbilityInfo.Count} numbers, found {abilities.Length}.");
            abilities = [10, 10, 10, 10, 10, 10];
        }

        var slots = new List<SlotDefinition>();
        foreach (var entry in reader.Array("slots"))
        {
            var slot = new Reader(entry, reader.Source, _problems);
            slots.Add(new SlotDefinition(slot.Int("level"), slot.Int("count")));
        }

        var ranks = new List<SkillRankDefinition>();
        foreach (var entry in reader.Array("skills"))
        {
            var written = new Reader(entry, reader.Source, _problems);
            ranks.Add(new SkillRankDefinition(
                written.Enum("skill", Skill.Perception), written.Int("ranks")));
        }

        var reductions = new List<ReductionDefinition>();
        foreach (var entry in reader.Array("reduction"))
        {
            var written = new Reader(entry, reader.Source, _problems);
            var bypass = DamageBypass.None;

            foreach (var quality in written.Array("bypassedBy"))
            {
                if (System.Enum.TryParse<DamageBypass>(quality.GetString(), true, out var flag))
                {
                    bypass |= flag;
                }
                else
                {
                    written.Problem("bypassedBy", $"'{quality.GetString()}' gets past nothing.");
                }
            }

            reductions.Add(new ReductionDefinition(
                written.Int("amount"), bypass, written.Enum("mode", BypassMode.Any)));
        }

        var classes = new List<ClassLevelDefinition>();
        foreach (var entry in reader.Array("classes"))
        {
            var taken = new Reader(entry, reader.Source, _problems);
            classes.Add(new ClassLevelDefinition(taken.String("class"), taken.Int("level", 1)));
        }

        return new CreatureDefinition
        {
            Id = id,
            Name = reader.StringOr("name", id),
            Abilities = abilities,
            Classes = classes,
            HitPoints = reader.Has("hitPoints") ? reader.Int("hitPoints") : null,
            HitDice = reader.Int("hitDice", 1),
            Level = reader.Int("level", 1),
            Size = reader.Enum("size", CreatureSize.Medium),
            Model = reader.StringOr("model", string.Empty),
            Speed = reader.Int("speed", 30),
            BaseAttack = reader.Int("baseAttack"),
            Armour = reader.Int("armour"),
            NaturalArmour = reader.Int("naturalArmour"),
            GoodSave = reader.Has("goodSave") ? reader.Enum("goodSave", Save.Fortitude) : null,
            Weapons = [.. reader.Array("weapons").Select(e => e.GetString() ?? string.Empty)],
            Feats = [.. reader.Array("feats").Select(e => e.GetString() ?? string.Empty)],
            Items = [.. reader.Array("items").Select(e => e.GetString() ?? string.Empty)],
            Reductions = reductions,
            SkillRanks = ranks,
            CastingAbility = reader.Has("castingAbility")
                ? reader.Enum("castingAbility", Ability.Intelligence)
                : null,
            CasterLevel = reader.Int("casterLevel"),
            Slots = slots,
            Spells = [.. reader.Array("spells").Select(e => e.GetString() ?? string.Empty)],
            Spellbook = [.. reader.Array("spellbook").Select(e => e.GetString() ?? string.Empty)],
            WeaponTraining = [.. reader.Array("weaponTraining").Select(e => e.GetString() ?? string.Empty)],
            Talents = [.. reader.Array("talents").Select(e => e.GetString() ?? string.Empty)],
            Deity = reader.Has("deity") ? reader.StringOr("deity", string.Empty) : null,
            Domains = [.. reader.Array("domains").Select(e => e.GetString() ?? string.Empty)],
            Channel = reader.Has("channel") ? reader.Enum("channel", ChannelKind.Positive) : null,
            School = reader.Has("school") ? reader.StringOr("school", string.Empty) : null,
            Opposition = [.. reader.Array("opposition")
                .Select(e => System.Enum.TryParse<SpellSchool>(e.GetString(), true, out var school) ? (SpellSchool?)school : null)
                .Select((school, index) =>
                {
                    if (school is null)
                    {
                        reader.Problem("opposition", $"entry {index + 1} is not a school of magic.");
                    }

                    return school;
                })
                .OfType<SpellSchool>()],
            ArcaneBond = reader.Has("arcaneBond") ? reader.StringOr("arcaneBond", "object") : null,
            WeaponMaster = reader.Has("weaponMaster") ? reader.StringOr("weaponMaster", string.Empty) : null,
        };
    }

    private TalentDefinition? ReadTalent(Reader reader)
    {
        var id = reader.String("id");
        if (id.Length == 0)
        {
            return null;
        }

        var list = reader.String("list");
        if (list.Length > 0 && list is not (FeatureIds.RogueTalents or FeatureIds.RagePowers))
        {
            reader.Problem("list", $"'{list}' is not a list of talents. Try {FeatureIds.RogueTalents} or {FeatureIds.RagePowers}.");
        }

        if (!reader.Has("effect"))
        {
            reader.Problem("effect", "is required: it is what the code knows the talent by.");
        }

        return new TalentDefinition
        {
            Id = id,
            Name = reader.StringOr("name", id),
            Description = reader.StringOr("description", string.Empty),
            List = list,
            Effect = reader.Enum("effect", TalentEffect.BleedingAttack),
            Advanced = reader.Bool("advanced"),
            MinimumLevel = reader.Int("minimumLevel"),
            Requires = [.. reader.Array("requires").Select(e => e.GetString() ?? string.Empty)],
        };
    }

    private List<GrantedPower> ReadGrantedPowers(Reader reader)
    {
        var powers = new List<GrantedPower>();
        foreach (var entry in reader.Array("powers"))
        {
            var power = new Reader(entry, reader.Source, _problems);
            if (!power.Has("effect"))
            {
                power.Problem("powers", "every granted power needs an 'effect'.");
                continue;
            }

            powers.Add(new GrantedPower(power.Int("level", 1), power.Enum("effect", GrantedPowerEffect.RebukeDeath)));
        }

        return powers;
    }

    private DomainDefinition? ReadDomain(Reader reader)
    {
        var id = reader.String("id");

        return id.Length == 0 ? null : new DomainDefinition
        {
            Id = id,
            Name = reader.StringOr("name", id),
            Description = reader.StringOr("description", string.Empty),
            Spells = [.. reader.Array("spells").Select(e => e.ValueKind == JsonValueKind.String
                ? e.GetString() ?? string.Empty
                : string.Empty)],
            Powers = ReadGrantedPowers(reader),
        };
    }

    private SchoolDefinition? ReadSchool(Reader reader)
    {
        var id = reader.String("id");

        return id.Length == 0 ? null : new SchoolDefinition
        {
            Id = id,
            Name = reader.StringOr("name", id),
            Description = reader.StringOr("description", string.Empty),
            School = reader.Has("school") ? reader.Enum("school", SpellSchool.Evocation) : null,
            Powers = ReadGrantedPowers(reader),
        };
    }

    private DeityDefinition? ReadDeity(Reader reader)
    {
        var id = reader.String("id");
        if (id.Length == 0)
        {
            return null;
        }

        var written = reader.String("alignment");
        var alignment = Alignments.Parse(written);
        if (alignment is null && written.Length > 0)
        {
            reader.Problem("alignment", $"'{written}' is not an alignment. Try LG, NG, CG, LN, N, CN, LE, NE or CE.");
        }

        return new DeityDefinition
        {
            Id = id,
            Name = reader.StringOr("name", id),
            Alignment = alignment ?? Alignment.Neutral,
            Domains = [.. reader.Array("domains").Select(e => (e.GetString() ?? string.Empty).ToLowerInvariant())],
            FavoredWeapon = reader.StringOr("favoredWeapon", string.Empty),
            Description = reader.StringOr("description", string.Empty),
        };
    }

    private CampaignDefinition? ReadCampaign(Reader reader)
    {
        var id = reader.String("id");

        return id.Length == 0 ? null : new CampaignDefinition
        {
            Id = id,
            Name = reader.StringOr("name", id),
            Description = reader.StringOr("description", string.Empty),
            Encounters = [.. reader.Array("encounters").Select(e => e.GetString() ?? string.Empty)],
            Rests = reader.Int("rests", 1),
            Level = reader.Has("level") ? reader.StringOr("level", string.Empty) : null,
        };
    }

    private EncounterDefinition? ReadEncounter(Reader reader)
    {
        var id = reader.String("id");
        if (id.Length == 0)
        {
            return null;
        }

        var placements = new List<PlacementDefinition>();
        foreach (var entry in reader.Array("placements"))
        {
            var placement = new Reader(entry, reader.Source, _problems);
            placements.Add(new PlacementDefinition(
                placement.String("creature"),
                placement.Int("x"),
                placement.Int("y"),
                placement.Bool("party"),
                placement.Has("name") ? placement.StringOr("name", string.Empty) : null,
                placement.Bool("hidden"),
                placement.Bool("asleep")));
        }

        return new EncounterDefinition
        {
            Id = id,
            Name = reader.StringOr("name", id),
            Width = reader.Int("width", 16),
            Height = reader.Int("height", 12),
            Terrain = reader.StringOr("terrain", string.Empty),
            Blocked = [.. Squares(reader, "blocked")],
            Difficult = [.. Squares(reader, "difficult")],
            Placements = placements,
            Intro = reader.StringOr("intro", string.Empty),
            Loot = [.. reader.Array("loot").Select(e => e.GetString() ?? string.Empty)],
        };
    }

    private TerrainDefinition? ReadTerrain(Reader reader)
    {
        var id = reader.String("id");

        return id.Length == 0 ? null : new TerrainDefinition
        {
            Id = id,
            Name = reader.StringOr("name", id),
            Ground = reader.StringOr("ground", string.Empty),
            Blocked = reader.StringOr("blocked", string.Empty),
        };
    }

    private LevelDefinition? ReadLevel(Reader reader)
    {
        var id = reader.String("id");
        if (id.Length == 0)
        {
            return null;
        }

        var areas = new List<AreaDefinition>();
        foreach (var entry in reader.Array("areas"))
        {
            var area = new Reader(entry, reader.Source, _problems);
            var areaId = area.String("id");

            areas.Add(new AreaDefinition
            {
                Id = areaId,
                Name = area.StringOr("name", areaId),
                Intro = area.StringOr("intro", string.Empty),
                Outro = area.StringOr("outro", string.Empty),
                X = area.Int("x"),
                Y = area.Int("y"),
                Width = area.Int("width", 1),
                Height = area.Int("height", 1),
                Final = area.Bool("final"),
                Foes = [.. Placements(area, "foes", party: false)],
                Loot = [.. area.Array("loot").Select(e => e.GetString() ?? string.Empty)],
                Experience = area.Int("xp", AreaDefinition.DefaultExperience),
            });
        }

        var features = new List<FeatureDefinition>();
        foreach (var entry in reader.Array("features"))
        {
            var feature = new Reader(entry, reader.Source, _problems);
            var featureId = feature.String("id");
            var kind = feature.Enum("kind", FeatureKind.Cache);

            features.Add(new FeatureDefinition
            {
                Id = featureId,
                Kind = kind,
                Name = feature.StringOr("name", featureId),
                Text = feature.StringOr("text", string.Empty),
                Squares = [.. Squares(feature, "squares").Select(at => new GridSquare(at.X, at.Y))],
                LockDc = feature.Int("lockDc", 15),
                BreakDc = feature.Int("breakDc", 16),
                JumpDc = feature.Int("jumpDc", 10),
                ClimbDc = feature.Int("climbDc", 15),
                Fall = feature.StringOr("fall", "2d6"),
                Loot = [.. feature.Array("loot").Select(e => e.GetString() ?? string.Empty)],
                Experience = feature.Int("xp", FeatureDefinition.DefaultExperience(kind)),
            });
        }

        return new LevelDefinition
        {
            Id = id,
            Name = reader.StringOr("name", id),
            Map = [.. reader.Array("map").Select(e => e.ValueKind == JsonValueKind.String
                ? e.GetString() ?? string.Empty
                : string.Empty)],
            StoneTerrain = reader.StringOr("stone", string.Empty),
            GrassTerrain = reader.StringOr("grass", string.Empty),
            Start = [.. Placements(reader, "start", party: true)],
            Areas = areas,
            Features = features,
        };
    }

    private List<PlacementDefinition> Placements(Reader reader, string field, bool party)
    {
        var placements = new List<PlacementDefinition>();
        foreach (var entry in reader.Array(field))
        {
            var placement = new Reader(entry, reader.Source, _problems);
            placements.Add(new PlacementDefinition(
                placement.String("creature"),
                placement.Int("x"),
                placement.Int("y"),
                party,
                placement.Has("name") ? placement.StringOr("name", string.Empty) : null,
                placement.Bool("hidden"),
                placement.Bool("asleep")));
        }

        return placements;
    }

    /// <summary>
    /// Everything about a level that can be wrong without being a JSON mistake.
    /// </summary>
    /// <remarks>
    /// Most of it is the map and the lists disagreeing: a door written where the map has a
    /// wall, an orc standing in a tree. Each of those loads quietly and then misbehaves twenty
    /// minutes into a playthrough, which is exactly what checking at load time is for.
    /// </remarks>
    private void ValidateLevel(LevelDefinition level)
    {
        var source = $"level '{level.Id}'";
        void Problem(string field, string message) =>
            _problems.Add(new ContentProblem(source, field, message));

        if (level.Map.Count == 0 || level.Width == 0)
        {
            Problem("map", "is empty.");
            return;
        }

        for (var y = 0; y < level.Map.Count; y++)
        {
            var row = level.Map[y];
            if (row.Length != level.Width)
            {
                Problem("map", $"row {y} is {row.Length} long; the first row is {level.Width}.");
            }

            for (var x = 0; x < row.Length; x++)
            {
                if (LevelDefinition.Parse(row[x]) is null)
                {
                    Problem("map", $"'{row[x]}' at ({x}, {y}) is not in the legend.");
                }
            }
        }

        foreach (var (field, terrain) in new[] { ("stone", level.StoneTerrain), ("grass", level.GrassTerrain) })
        {
            if (terrain.Length > 0 && !_terrains.ContainsKey(terrain))
            {
                Problem(field, $"no terrain called '{terrain}'.");
            }
        }

        if (level.Start.Count == 0)
        {
            Problem("start", "nobody starts the level.");
        }

        // Somewhere to stand, checked the same way for the party and for everybody waiting.
        void Stand(string field, PlacementDefinition placement)
        {
            if (!_creatures.ContainsKey(placement.CreatureId))
            {
                Problem(field, $"no creature called '{placement.CreatureId}'.");
            }

            if (level.IsBlockedCell(placement.X, placement.Y))
            {
                Problem(field, $"'{placement.CreatureId}' at ({placement.X}, {placement.Y}) is "
                    + $"standing in {level.CellAt(placement.X, placement.Y)}.");
            }
        }

        foreach (var placement in level.Start)
        {
            Stand("start", placement);
        }

        // Names are what a save file tells creatures apart by, so two foes called "Orc" would
        // reload as one. The whole level rather than each room, because the party is in every
        // fight and a room's occupants are listed for drawing alongside everybody else's.
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var placement in level.Start.Concat(level.Areas.SelectMany(area => area.Foes)))
        {
            var name = placement.Name ?? _creatures.GetValueOrDefault(placement.CreatureId)?.Name;
            if (name is not null && !names.Add(name))
            {
                Problem("areas", $"more than one creature is called '{name}'.");
            }
        }

        var areaIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var area in level.Areas)
        {
            if (!areaIds.Add(area.Id))
            {
                Problem("areas", $"there is already an area called '{area.Id}'.");
            }

            if (area.Width < 1 || area.Height < 1 || area.X < 0 || area.Y < 0
                || area.X + area.Width > level.Width || area.Y + area.Height > level.Height)
            {
                Problem($"areas.{area.Id}", "is not inside the map.");
            }

            foreach (var foe in area.Foes)
            {
                Stand($"areas.{area.Id}.foes", foe);
            }

            foreach (var item in area.Loot.Where(id => !_items.ContainsKey(id)))
            {
                Problem($"areas.{area.Id}.loot", $"no item called '{item}'.");
            }

            if (area.Final && area.Foes.Count == 0)
            {
                Problem($"areas.{area.Id}", "is final but has nobody in it to beat.");
            }
        }

        if (!level.Areas.Any(area => area.Final))
        {
            Problem("areas", "none is final, so the level can never be won.");
        }

        var featureIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var feature in level.Features)
        {
            var field = $"features.{feature.Id}";

            if (!featureIds.Add(feature.Id))
            {
                Problem("features", $"there is already a feature called '{feature.Id}'.");
            }

            if (feature.Squares.Count == 0)
            {
                Problem(field, "has no squares.");
            }

            foreach (var square in feature.Squares)
            {
                var cell = level.CellAt(square);
                var inside = square.X >= 0 && square.Y >= 0
                    && square.X < level.Width && square.Y < level.Height;

                var fits = feature.Kind switch
                {
                    FeatureKind.Door => cell == LevelCell.Door,
                    FeatureKind.Bridge => cell == LevelCell.Chasm,

                    // Furniture is the obvious place, but a cache in a niche in the wall is
                    // fine too, as long as somebody can stand beside it.
                    _ => cell is LevelCell.Bed or LevelCell.Table or LevelCell.Crate
                        || (LevelDefinition.IsBlocked(cell) && Beside(level, square)),
                };

                if (!inside)
                {
                    Problem(field, $"{square} is off the map.");
                }
                else if (!fits)
                {
                    Problem(field, $"a {feature.Kind.ToString().ToLowerInvariant()} cannot be at "
                        + $"{square}, which the map has as {cell}.");
                }
            }

            if (!DiceExpression.TryParse(feature.Fall, out _))
            {
                Problem($"{field}.fall", $"'{feature.Fall}' is not dice.");
            }

            foreach (var item in feature.Loot.Where(id => !_items.ContainsKey(id)))
            {
                Problem($"{field}.loot", $"no item called '{item}'.");
            }
        }
    }

    /// <summary>Whether any of the eight squares around one is open floor.</summary>
    private static bool Beside(LevelDefinition level, GridSquare square)
    {
        for (var dx = -1; dx <= 1; dx++)
        {
            for (var dy = -1; dy <= 1; dy++)
            {
                if ((dx != 0 || dy != 0) && !level.IsBlockedCell(square.X + dx, square.Y + dy))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private IEnumerable<PlacementDefinition> Squares(Reader reader, string field) =>
        reader.Array(field).Select(entry =>
        {
            var square = new Reader(entry, reader.Source, _problems);
            return new PlacementDefinition(string.Empty, square.Int("x"), square.Int("y"), false);
        });

    /// <summary>
    /// A thin wrapper over a JSON object that records a problem instead of throwing, so a file
    /// with three mistakes reports three mistakes.
    /// </summary>
    private readonly struct Reader(JsonElement element, string source, List<ContentProblem> problems)
    {
        public string Source => source;

        public bool Has(string name) => element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(name, out _);

        public void Problem(string field, string message) =>
            problems.Add(new ContentProblem(source, field, message));

        public string String(string name)
        {
            if (Has(name) && element.GetProperty(name).GetString() is { } value)
            {
                return value;
            }

            Problem(name, "is required.");
            return string.Empty;
        }

        public string StringOr(string name, string fallback) =>
            Has(name) ? element.GetProperty(name).GetString() ?? fallback : fallback;

        public int Int(string name, int fallback = 0) =>
            Has(name) && element.GetProperty(name).TryGetInt32(out var value) ? value : fallback;

        public bool Bool(string name, bool fallback = false) =>
            Has(name) && element.GetProperty(name).ValueKind is JsonValueKind.True or JsonValueKind.False
                ? element.GetProperty(name).GetBoolean()
                : fallback;

        public T Enum<T>(string name, T fallback)
            where T : struct, Enum
        {
            if (!Has(name))
            {
                return fallback;
            }

            var text = element.GetProperty(name).GetString();
            if (System.Enum.TryParse<T>(text, ignoreCase: true, out var value))
            {
                return value;
            }

            Problem(name, $"'{text}' is not one of {string.Join(", ", System.Enum.GetNames<T>())}.");
            return fallback;
        }

        public JsonElement[] Array(string name) =>
            Has(name) && element.GetProperty(name).ValueKind == JsonValueKind.Array
                ? [.. element.GetProperty(name).EnumerateArray()]
                : [];

        public Reader Object(string name) => Has(name)
            ? new Reader(element.GetProperty(name), source, problems)
            : new Reader(default, source, problems);
    }
}
