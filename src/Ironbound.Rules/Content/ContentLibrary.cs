using System.Text.Json;
using Ironbound.Rules.Abilities;
using Ironbound.Rules.Classes;
using Ironbound.Rules.Combat;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Defense;
using Ironbound.Rules.Effects;
using Ironbound.Rules.Feats;
using Ironbound.Rules.Items;
using Ironbound.Rules.Magic;
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

    public Spell? GetSpell(string id) => _spells.GetValueOrDefault(id);

    public EncounterDefinition? GetEncounter(string id) => _encounters.GetValueOrDefault(id);

    public CampaignDefinition? GetCampaign(string id) => _campaigns.GetValueOrDefault(id);

    public CreatureDefinition? GetCreature(string id) => _creatures.GetValueOrDefault(id);

    public ClassDefinition? GetClass(string id) => _classes.GetValueOrDefault(id);

    public FeatDefinition? GetFeat(string id) => _feats.GetValueOrDefault(id);

    public ItemDefinition? GetItem(string id) => _items.GetValueOrDefault(id);

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

            foreach (var feat in creature.Feats.Where(id => !_feats.ContainsKey(id)))
            {
                _problems.Add(new ContentProblem(
                    $"creature '{creature.Id}'", "feats", $"no feat called '{feat}'."));
            }

            foreach (var spell in creature.Spells.Where(id => !_spells.ContainsKey(id)))
            {
                _problems.Add(new ContentProblem(
                    $"creature '{creature.Id}'", "spells", $"no spell called '{spell}'."));
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
        }

        foreach (var encounter in _encounters.Values)
        {
            foreach (var placement in encounter.Placements
                .Where(p => !_creatures.ContainsKey(p.CreatureId)))
            {
                _problems.Add(new ContentProblem(
                    $"encounter '{encounter.Id}'",
                    "placements",
                    $"no creature called '{placement.CreatureId}'."));
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
            else if (part.Has("effect"))
            {
                does.Add(new Bestow(ReadEffect(part.Object("effect"))));
            }
            else
            {
                part.Problem("does", "needs one of 'damage', 'heal' or 'effect'.");
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
                    "self" => new SelfTarget(),
                    _ => new SingleTarget(),
                },
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

    private static SpellDice ReadDice(Reader reader) => reader.Has("fixed")
        ? SpellDice.Fixed(reader.StringOr("fixed", "1d6"))
        : SpellDice.PerLevel(
            reader.Int("perLevel", 6),
            reader.Int("maxDice", 10),
            reader.Int("levelsPerDie", 1),
            reader.Int("flatPerDie", 0));

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
                + (reader.Int("minutesPerLevel") * Duration.TicksPerMinute),
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

        return new ClassDefinition
        {
            Id = id,
            Name = reader.StringOr("name", id),
            HitDie = reader.Int("hitDie", 8),
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

        return new FeatDefinition
        {
            Id = id,
            Name = reader.StringOr("name", id),
            Description = reader.StringOr("description", string.Empty),
            Grants = grants,
            Effect = reader.Enum("effect", FeatEffect.None),
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
            Grants = grants,
            Weapon = reader.Has("weapon") ? reader.StringOr("weapon", string.Empty) : null,
            Enhancement = reader.Int("enhancement"),
            Qualities = qualities,
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
        };
    }

    private CampaignDefinition? ReadCampaign(Reader reader)
    {
        var id = reader.String("id");

        return id.Length == 0 ? null : new CampaignDefinition
        {
            Id = id,
            Name = reader.StringOr("name", id),
            Encounters = [.. reader.Array("encounters").Select(e => e.GetString() ?? string.Empty)],
            Rests = reader.Int("rests", 1),
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
                placement.Bool("hidden")));
        }

        return new EncounterDefinition
        {
            Id = id,
            Name = reader.StringOr("name", id),
            Width = reader.Int("width", 16),
            Height = reader.Int("height", 12),
            Blocked = [.. Squares(reader, "blocked")],
            Difficult = [.. Squares(reader, "difficult")],
            Placements = placements,
        };
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
