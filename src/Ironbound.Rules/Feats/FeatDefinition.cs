using Ironbound.Rules.Abilities;
using Ironbound.Rules.Effects;

namespace Ironbound.Rules.Feats;

/// <summary>
/// The handful of feats whose effect the engine has to know about by name.
/// </summary>
/// <remarks>
/// This is the "small effect identifier" the project was designed around, and the line it draws
/// is worth stating: a feat that only hands out numbers needs no entry here at all, because
/// <see cref="FeatDefinition.Grants"/> already says everything about it. An entry is earned only
/// by a feat that changes what the rules <em>do</em> — how many opportunities you get, whether
/// reaching in provokes — which cannot be a modifier however hard one squints.
/// <para>
/// Keeping it an enum rather than a string means a typo in a content file is a load-time problem
/// with the file's name attached, not a feat that silently does nothing for the rest of the game.
/// </para>
/// </remarks>
public enum FeatEffect
{
    /// <summary>No behaviour of its own: the numbers are the whole feat.</summary>
    None,

    /// <summary>An extra attack of opportunity per point of Dexterity bonus.</summary>
    CombatReflexes,

    /// <summary>Tripping no longer provokes, and the check is two better.</summary>
    ImprovedTrip,

    /// <summary>The same, for shoving people about.</summary>
    ImprovedBullRush,

    /// <summary>Lets a creature trade accuracy for damage.</summary>
    PowerAttack,

    /// <summary>Lets a creature trade accuracy for armour class.</summary>
    CombatExpertise,

    /// <summary>+1 to hit with one kind of weapon, named by <see cref="FeatDefinition.Choice"/>.</summary>
    WeaponFocus,

    /// <summary>Dexterity rather than Strength to hit with a light or finesse weapon.</summary>
    WeaponFinesse,

    /// <summary>One more point from whatever shield is on the arm.</summary>
    ShieldFocus,

    /// <summary>Twice the threat range with one kind of weapon.</summary>
    ImprovedCritical,

    /// <summary>+1 to hit and damage with a shot inside thirty feet.</summary>
    PointBlankShot,

    /// <summary>No penalty for shooting into a melee.</summary>
    PreciseShot,

    /// <summary>One more shot on a full attack, every shot at -2.</summary>
    RapidShot,

    /// <summary>A single attack rolls the weapon's dice twice.</summary>
    VitalStrike,

    /// <summary>A hit lets the same swing carry on into the next foe.</summary>
    Cleave,

    /// <summary>Three hit points, and one more for every hit die past the third.</summary>
    Toughness,

    /// <summary>Channel energy that leaves some of the burst out.</summary>
    SelectiveChanneling,

    /// <summary>Half as much again of every variable number, for a slot two levels up.</summary>
    EmpowerSpell,

    /// <summary>Every simple weapon, for the few who start without them.</summary>
    SimpleWeaponProficiency,

    /// <summary>One martial weapon, named by <see cref="FeatDefinition.Choice"/>.</summary>
    MartialWeaponProficiency,

    /// <summary>One exotic weapon, named by <see cref="FeatDefinition.Choice"/>.</summary>
    ExoticWeaponProficiency,

    /// <summary>Light armour without its check penalty on attack rolls.</summary>
    ArmorProficiencyLight,

    ArmorProficiencyMedium,

    ArmorProficiencyHeavy,

    /// <summary>Every shield but the tower shield.</summary>
    ShieldProficiency,

    TowerShieldProficiency,
}

/// <summary>What a feat asks to be told when it is taken, if anything.</summary>
public enum FeatChoice
{
    None,

    /// <summary>A kind of weapon, by its content id: Weapon Focus (longsword).</summary>
    Weapon,
}

/// <summary>
/// What a feat asks of you before it will have you.
/// </summary>
/// <remarks>
/// Prerequisites are most of what makes feat selection a build rather than a shopping list.
/// Power Attack wanting Strength 13 is why the wiry duellist cannot have it, and Improved Trip
/// wanting Combat Expertise is why the trip specialist spends two slots to get there.
/// </remarks>
public sealed record FeatRequirements
{
    /// <summary>Minimum ability scores, by ability.</summary>
    public IReadOnlyDictionary<Ability, int> Abilities { get; init; } =
        new Dictionary<Ability, int>();

    /// <summary>Feats that must already be held, by id.</summary>
    public IReadOnlyList<string> Feats { get; init; } = [];

    public int BaseAttack { get; init; }

    public int Level { get; init; }

    /// <summary>
    /// Class features that must already be had, by feature id: Selective Channeling asks for
    /// <c>channel-energy</c>, which no amount of Charisma can stand in for.
    /// </summary>
    public IReadOnlyList<string> Features { get; init; } = [];

    /// <summary>
    /// Armour the creature must already be trained in, by the words a class's list uses:
    /// Medium Armor Proficiency asks for "light".
    /// </summary>
    /// <remarks>
    /// Training rather than a feat, because the book counts a class's armour training as the
    /// feat: a barbarian has never taken Light Armor Proficiency and still qualifies for Medium.
    /// </remarks>
    public IReadOnlyList<string> Armour { get; init; } = [];

    /// <summary>Whether a creature qualifies, and if not, what it is short of.</summary>
    public IReadOnlyList<string> Unmet(Creatures.Creature creature)
    {
        ArgumentNullException.ThrowIfNull(creature);

        var missing = new List<string>();

        foreach (var (ability, minimum) in Abilities.OrderBy(entry => entry.Key))
        {
            if ((creature.Abilities[ability].Score ?? 0) < minimum)
            {
                missing.Add($"{AbilityInfo.Abbreviate(ability)} {minimum}");
            }
        }

        foreach (var required in Feats.Where(id => !creature.HasFeat(id)))
        {
            missing.Add(required);
        }

        if (creature.BaseAttackBonus < BaseAttack)
        {
            missing.Add($"base attack +{BaseAttack}");
        }

        if (creature.Level < Level)
        {
            missing.Add($"level {Level}");
        }

        foreach (var feature in Features.Where(id => !Classes.ClassFeatures.Has(creature, id)))
        {
            missing.Add(feature);
        }

        foreach (var armour in Armour.Where(word => !Classes.Proficiency.IsTrainedIn(creature, word)))
        {
            missing.Add($"{armour} armour proficiency");
        }

        return missing;
    }

    public override string ToString()
    {
        var parts = Abilities
            .OrderBy(entry => entry.Key)
            .Select(entry => $"{AbilityInfo.Abbreviate(entry.Key)} {entry.Value}")
            .Concat(Feats);

        if (BaseAttack > 0)
        {
            parts = parts.Append($"base attack +{BaseAttack}");
        }

        if (Level > 0)
        {
            parts = parts.Append($"level {Level}");
        }

        parts = parts.Concat(Features).Concat(Armour.Select(word => $"{word} armour proficiency"));

        var written = string.Join(", ", parts);
        return written.Length == 0 ? "none" : written;
    }
}

/// <summary>
/// A feat as written down.
/// </summary>
/// <remarks>
/// Most of them are nothing but a name and a couple of <see cref="ModifierGrant"/>s, which is
/// the point: Dodge, Iron Will and Improved Initiative between them need no C# at all. They are
/// three content files and the stacking engine that already existed.
/// </remarks>
public sealed record FeatDefinition
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    /// <summary>One line, for whatever eventually shows a character sheet.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>The static bonuses it confers, in the ordinary stacking currency.</summary>
    public IReadOnlyList<ModifierGrant> Grants { get; init; } = [];

    /// <summary>What the rules must know about it beyond the numbers, if anything.</summary>
    public FeatEffect Effect { get; init; } = FeatEffect.None;

    /// <summary>What it asks of you first.</summary>
    public FeatRequirements Requires { get; init; } = new();

    /// <summary>
    /// A combat feat: one a fighter may take as a bonus feat, and a rogue through a combat trick.
    /// </summary>
    /// <remarks>
    /// A flag on the feat rather than a list in the fighter's file, because the rulebook marks
    /// the feat and not the class — every class that hands out "a bonus combat feat" means the
    /// same set, and a second list would be a second thing to keep in step.
    /// </remarks>
    public bool Combat { get; init; }

    /// <summary>A metamagic feat, which a wizard may take as a bonus feat.</summary>
    public bool Metamagic { get; init; }

    /// <summary>An item creation feat, which a wizard may also take as a bonus feat.</summary>
    public bool ItemCreation { get; init; }

    /// <summary>What it needs to be told when taken. Weapon Focus needs a weapon.</summary>
    public FeatChoice Takes { get; init; } = FeatChoice.None;

    /// <summary>
    /// What was chosen, once it has been: the weapon id for Weapon Focus. Null on the feat as
    /// written in its file, and on any feat that takes no choice at all.
    /// </summary>
    /// <remarks>
    /// A field on a copy rather than a separate feat file per weapon: <c>feat with { Choice =
    /// "longsword" }</c> is Weapon Focus (longsword), and its <see cref="Id"/> is still
    /// <c>weapon-focus</c>, so a prerequisite naming the feat finds it whichever weapon it was.
    /// </remarks>
    public string? Choice { get; init; }

    /// <summary>The id with the choice folded in — "weapon-focus:longsword" — as a save keeps it.</summary>
    public string Key => Choice is null ? Id : $"{Id}:{Choice}";

    /// <summary>"Weapon Focus (longsword)", or just the name for a feat with no choice.</summary>
    public string Title => Choice is null ? Name : $"{Name} ({Choice})";

    /// <summary>
    /// Whether a creature could take it: qualified, and does not already have it. A feat taken
    /// once per weapon may be taken again, for a different one.
    /// </summary>
    public bool AvailableTo(Creatures.Creature creature)
    {
        ArgumentNullException.ThrowIfNull(creature);

        if (Takes == FeatChoice.None && creature.HasFeat(Id))
        {
            return false;
        }

        if (Choice is not null && creature.Feats.Any(held => held.Key == Key))
        {
            return false;
        }

        return Requires.Unmet(creature).Count == 0;
    }

    /// <summary>Hands the feat's static bonuses to a creature. Called once, when it is built.</summary>
    public void ApplyTo(Creatures.Creature creature)
    {
        ArgumentNullException.ThrowIfNull(creature);

        foreach (var grant in Grants)
        {
            grant.Target.On(creature).Add(grant.Value, grant.Type, Name);
        }
    }

    public override string ToString() => Title;
}
