namespace Ironbound.Rules.Classes;

/// <summary>
/// The class features the code knows how to do, by the ids class files use for them.
/// </summary>
/// <remarks>
/// Strings in the file and constants here, rather than an enum, because the ids are kebab-case
/// words a person writes in JSON and an enum would need a translation table in each direction.
/// What an enum would have bought — a typo caught at load rather than a feature that silently
/// does nothing — <see cref="Known"/> buys instead: the content loader checks every row against
/// it and names the file.
/// </remarks>
public static class FeatureIds
{
    // ---- fighter ----

    /// <summary>A bonus feat. Its <c>kind</c> parameter says which: <c>combat</c> or <c>wizard</c>.</summary>
    public const string BonusFeat = "bonus-feat";

    public const string Bravery = "bravery";

    public const string ArmorTraining = "armor-training";

    public const string WeaponTraining = "weapon-training";

    // ---- rogue ----

    public const string SneakAttack = "sneak-attack";

    public const string Trapfinding = "trapfinding";

    public const string Evasion = "evasion";

    /// <summary>A pick from a list: <c>rogue-talent</c> or <c>rage-power</c>, named by <c>list</c>.</summary>
    public const string Talent = "talent";

    public const string TrapSense = "trap-sense";

    public const string UncannyDodge = "uncanny-dodge";

    public const string ImprovedUncannyDodge = "improved-uncanny-dodge";

    /// <summary>From here on a rogue's talent picks may be advanced ones.</summary>
    public const string AdvancedTalents = "advanced-talents";

    // ---- cleric ----

    public const string Aura = "aura";

    public const string ChannelEnergy = "channel-energy";

    public const string Domains = "domains";

    public const string Orisons = "orisons";

    public const string SpontaneousCasting = "spontaneous-casting";

    // ---- wizard ----

    public const string ArcaneBond = "arcane-bond";

    public const string ArcaneSchool = "arcane-school";

    public const string Cantrips = "cantrips";

    public const string ScribeScroll = "scribe-scroll";

    // ---- barbarian ----

    public const string FastMovement = "fast-movement";

    public const string Rage = "rage";

    public const string DamageReduction = "damage-reduction";

    /// <summary>The two lists a <see cref="Talent"/> row can pick from.</summary>
    public const string RogueTalents = "rogue-talent";

    public const string RagePowers = "rage-power";

    /// <summary>The two kinds of bonus feat a <see cref="BonusFeat"/> row can hand out.</summary>
    public const string CombatFeat = "combat";

    public const string WizardFeat = "wizard";

    /// <summary>
    /// Every id a class file may use, with the parameters each accepts and the values each
    /// parameter may take. An empty set of values means anything goes.
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlySet<string>>> Known { get; } =
        new Dictionary<string, IReadOnlyDictionary<string, IReadOnlySet<string>>>(StringComparer.Ordinal)
        {
            [BonusFeat] = Accepts(("kind", [CombatFeat, WizardFeat])),
            [Bravery] = Accepts(),
            [ArmorTraining] = Accepts(),
            [WeaponTraining] = Accepts(),
            [SneakAttack] = Accepts(),
            [Trapfinding] = Accepts(),
            [Evasion] = Accepts(),
            [Talent] = Accepts(("list", [RogueTalents, RagePowers])),
            [TrapSense] = Accepts(),
            [UncannyDodge] = Accepts(),
            [ImprovedUncannyDodge] = Accepts(),
            [AdvancedTalents] = Accepts(),
            [Aura] = Accepts(),
            [ChannelEnergy] = Accepts(),
            [Domains] = Accepts(),
            [Orisons] = Accepts(),
            [SpontaneousCasting] = Accepts(),
            [ArcaneBond] = Accepts(),
            [ArcaneSchool] = Accepts(),
            [Cantrips] = Accepts(),
            [ScribeScroll] = Accepts(),
            [FastMovement] = Accepts(),
            [Rage] = Accepts(),
            [DamageReduction] = Accepts(),
        };

    /// <summary>The name a character sheet gives a feature.</summary>
    public static string Title(string id) => id switch
    {
        BonusFeat => "Bonus feats",
        Bravery => "Bravery",
        ArmorTraining => "Armour training",
        WeaponTraining => "Weapon training",
        SneakAttack => "Sneak attack",
        Trapfinding => "Trapfinding",
        Evasion => "Evasion",
        Talent => "Talents",
        TrapSense => "Trap sense",
        UncannyDodge => "Uncanny dodge",
        ImprovedUncannyDodge => "Improved uncanny dodge",
        AdvancedTalents => "Advanced talents",
        Aura => "Aura",
        ChannelEnergy => "Channel energy",
        Domains => "Domains",
        Orisons => "Orisons",
        SpontaneousCasting => "Spontaneous casting",
        ArcaneBond => "Arcane bond",
        ArcaneSchool => "Arcane school",
        Cantrips => "Cantrips",
        ScribeScroll => "Scribe Scroll",
        FastMovement => "Fast movement",
        Rage => "Rage",
        DamageReduction => "Damage reduction",
        _ => id,
    };

    private static IReadOnlyDictionary<string, IReadOnlySet<string>> Accepts(
        params (string Name, string[] Values)[] parameters) =>
        parameters.ToDictionary(
            entry => entry.Name,
            entry => (IReadOnlySet<string>)new HashSet<string>(entry.Values, StringComparer.Ordinal),
            StringComparer.Ordinal);
}
