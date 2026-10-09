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

    // ---- the feats batch: appended, so nothing that ever counted them moves ----

    /// <summary>
    /// Two on each skill the feat names, and four on one with ten ranks in it: Alertness,
    /// Acrobatic and the rest of the pairs. The skills are <see cref="FeatDefinition.Skills"/>.
    /// </summary>
    SkillBonus,

    /// <summary>Three on one skill, six once it has ten ranks: <see cref="FeatDefinition.Choice"/> names it.</summary>
    SkillFocus,

    /// <summary>Once a day, a failed Fortitude save rolled again.</summary>
    ImprovedGreatFortitude,

    /// <summary>Once a day, a failed Will save rolled again.</summary>
    ImprovedIronWill,

    /// <summary>Once a day, a failed Reflex save rolled again.</summary>
    ImprovedLightningReflexes,

    /// <summary>Four more armour class against the swings that moving draws.</summary>
    Mobility,

    /// <summary>One more point from the shield, on top of Shield Focus.</summary>
    GreaterShieldFocus,

    /// <summary>Total hit dice in place of base attack in combat manoeuvre defence.</summary>
    DefensiveCombatTraining,

    /// <summary>Stable at once below nought, and still on its feet, staggered, until death.</summary>
    Diehard,

    /// <summary>Hardier against heat, cold, hunger and forced marches — none of which the game has yet.</summary>
    Endurance,

    /// <summary>Cleave that carries on for as long as the swings keep landing.</summary>
    GreatCleave,

    /// <summary>A vital strike rolls the weapon's dice three times.</summary>
    ImprovedVitalStrike,

    /// <summary>A vital strike rolls the weapon's dice four times.</summary>
    GreaterVitalStrike,

    /// <summary>One more on attack rolls with the weapon Weapon Focus was taken for.</summary>
    GreaterWeaponFocus,

    /// <summary>Two more damage with one kind of weapon.</summary>
    WeaponSpecialization,

    /// <summary>Two more again, on top of Weapon Specialization.</summary>
    GreaterWeaponSpecialization,

    /// <summary>Four on the roll that confirms a critical.</summary>
    CriticalFocus,

    /// <summary>Five more feet of reach for a turn, for two points of armour class.</summary>
    Lunge,

    /// <summary>Follows a foe who steps away.</summary>
    StepUp,

    /// <summary>An attack of opportunity spent stopping a foe in its tracks.</summary>
    StandStill,

    /// <summary>Readies a swing at whoever strikes from beyond reach. Needs readied actions.</summary>
    StrikeBack,

    /// <summary>Move, swing, and move on, without the target getting a swing back.</summary>
    SpringAttack,

    /// <summary>One swing at every foe in reach.</summary>
    WhirlwindAttack,

    /// <summary>Two more on a bull rush, and the shoved foe draws swings from the shover's friends.</summary>
    GreaterBullRush,

    /// <summary>Two more on a trip, and the tripped foe draws swings as it falls.</summary>
    GreaterTrip,

    /// <summary>Five points of damage reduction ignored with a focused weapon.</summary>
    PenetratingStrike,

    /// <summary>Ten points of damage reduction ignored with a focused weapon.</summary>
    GreaterPenetratingStrike,

    /// <summary>A full round spent demoralising every foe within thirty feet.</summary>
    DazzlingDisplay,

    /// <summary>A frightened foe it has hit is flat-footed to it.</summary>
    ShatterDefenses,

    /// <summary>Double damage and a point of Constitution bled from a helpless foe.</summary>
    DeadlyStroke,

    /// <summary>Strength as well as Charisma on Intimidate.</summary>
    IntimidatingProwess,

    BleedingCritical,

    SickeningCritical,

    StaggeringCritical,

    StunningCritical,

    TiringCritical,

    ExhaustingCritical,

    BlindingCritical,

    DeafeningCritical,

    /// <summary>Two critical feats' effects on one critical hit.</summary>
    CriticalMastery,

    /// <summary>Ranged attacks ignore anything short of total cover.</summary>
    ImprovedPreciseShot,

    /// <summary>Lets a creature trade ranged accuracy for damage.</summary>
    DeadlyAim,

    /// <summary>The first shot of a full attack with a bow is two arrows.</summary>
    Manyshot,

    /// <summary>Half the penalty for every range increment.</summary>
    FarShot,

    /// <summary>Move and shoot at any point along the way, as one full round.</summary>
    ShotOnTheRun,

    /// <summary>One shot that ignores armour, shield and hide.</summary>
    PinpointTargeting,

    /// <summary>Five more feet of speed out of medium and heavy armour.</summary>
    Fleet,

    /// <summary>Runs at five times speed and keeps Dexterity while doing it.</summary>
    Run,

    /// <summary>Five feet of difficult ground a round walked as though it were clear.</summary>
    NimbleMoves,

    /// <summary>Twenty feet of difficult ground a round walked as though it were clear.</summary>
    AcrobaticSteps,

    /// <summary>One more on the difficulty class of one school's spells.</summary>
    SpellFocus,

    /// <summary>One more again on the same school.</summary>
    GreaterSpellFocus,

    /// <summary>Two on the caster level check against spell resistance.</summary>
    SpellPenetration,

    /// <summary>Two more on the same check.</summary>
    GreaterSpellPenetration,

    /// <summary>Four on concentration to cast defensively.</summary>
    CombatCasting,

    ExtendSpell,

    MaximizeSpell,

    WidenSpell,

    EnlargeSpell,

    QuickenSpell,

    HeightenSpell,

    /// <summary>A swift action that makes the next round's blows magic and a little harder.</summary>
    ArcaneStrike,

    /// <summary>Two more channels a day.</summary>
    ExtraChannel,

    /// <summary>Two more on the difficulty class of channel energy.</summary>
    ImprovedChannel,

    /// <summary>A channel spent making the undead flee.</summary>
    TurnUndead,

    /// <summary>A channel spent taking the undead into service.</summary>
    CommandUndead,

    /// <summary>A channel that heals or harms outsiders of one alignment, and nobody else.</summary>
    AlignmentChannel,

    /// <summary>A channel that heals or harms elementals of one element, and nobody else.</summary>
    ElementalChannel,

    /// <summary>A channel spent through a weapon blow.</summary>
    ChannelSmite,

    /// <summary>Six more rounds of rage a day.</summary>
    ExtraRage,
}

/// <summary>What a feat asks to be told when it is taken, if anything.</summary>
public enum FeatChoice
{
    None,

    /// <summary>A kind of weapon, by its content id: Weapon Focus (longsword).</summary>
    Weapon,

    /// <summary>A skill, by its name in <see cref="Skills.Skill"/>: Skill Focus (Perception).</summary>
    Skill,

    /// <summary>A school of magic: Spell Focus (Evocation).</summary>
    School,

    /// <summary>One of chaos, evil, good or law: Alignment Channel (evil).</summary>
    Alignment,

    /// <summary>One of air, earth, fire or water: Elemental Channel (fire).</summary>
    Element,
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

    /// <summary>
    /// Feats of which any one will do: Improvised Weapon Mastery takes Catch Off-Guard or Throw
    /// Anything. Empty means nothing is asked.
    /// </summary>
    public IReadOnlyList<string> AnyOf { get; init; } = [];

    /// <summary>
    /// Whether the feats in <see cref="Feats"/> that are taken for something must have been
    /// taken for the same thing as this one: Weapon Specialization (longsword) wants Weapon
    /// Focus (longsword), and Weapon Focus (axe) will not do.
    /// </summary>
    public bool SameChoice { get; init; }

    public int BaseAttack { get; init; }

    public int Level { get; init; }

    /// <summary>
    /// Levels in one class in particular, by class id: Weapon Specialization's "4th-level
    /// fighter", which no other class's levels count towards.
    /// </summary>
    public IReadOnlyDictionary<string, int> ClassLevels { get; init; } = new Dictionary<string, int>();

    /// <summary>A caster level, from any class that casts: the item-creation feats ask for one.</summary>
    public int CasterLevel { get; init; }

    /// <summary>The ability to cast arcane spells at all: Arcane Strike.</summary>
    public bool Arcane { get; init; }

    /// <summary>Ranks in a skill: Mounted Combat's one rank of Ride.</summary>
    public IReadOnlyDictionary<Skills.Skill, int> Ranks { get; init; } = new Dictionary<Skills.Skill, int>();

    /// <summary>
    /// Proficiency with the weapon the feat is taken for: Weapon Focus and Improved Critical
    /// both ask it, so nobody focuses on a weapon they cannot use.
    /// </summary>
    public bool Proficient { get; init; }

    /// <summary>How many critical feats must already be held: Critical Mastery's "any two".</summary>
    public int CriticalFeats { get; init; }

    /// <summary>
    /// Which way a cleric must channel: Turn Undead wants positive energy, Command Undead
    /// negative. Null for a feat that does not care.
    /// </summary>
    public Classes.ChannelKind? Channel { get; init; }

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
    public IReadOnlyList<string> Unmet(Creatures.Creature creature) => Unmet(creature, null, null);

    /// <summary>
    /// Whether a creature qualifies for the feat taken for one thing in particular. Without a
    /// choice, a prerequisite that has to match one is met by any.
    /// </summary>
    /// <param name="feat">The feat being asked about, so that a weapon proficiency it needs can
    /// be checked against the weapon it was taken for.</param>
    public IReadOnlyList<string> Unmet(Creatures.Creature creature, string? choice, FeatDefinition? feat = null)
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

        foreach (var required in Feats)
        {
            var held = SameChoice && choice is not null
                ? creature.Feats.Any(owned => owned.Id == required && (owned.Choice is null || owned.Choice == choice))
                : creature.HasFeat(required);

            if (!held)
            {
                missing.Add(SameChoice && choice is not null ? $"{required}:{choice}" : required);
            }
        }

        if (AnyOf.Count > 0 && !AnyOf.Any(creature.HasFeat))
        {
            missing.Add(string.Join(" or ", AnyOf));
        }

        if (creature.BaseAttackBonus < BaseAttack)
        {
            missing.Add($"base attack +{BaseAttack}");
        }

        if (creature.Level < Level)
        {
            missing.Add($"level {Level}");
        }

        foreach (var (classId, level) in ClassLevels.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            if (Classes.ClassFeatures.ClassLevel(creature, classId) < level)
            {
                missing.Add($"{classId} {level}");
            }
        }

        if (CasterLevel > 0 && creature.Spells.CasterLevel < CasterLevel)
        {
            missing.Add($"caster level {CasterLevel}");
        }

        if (Arcane && !Magic.SpellTraditions.CastsArcane(creature))
        {
            missing.Add("arcane spells");
        }

        foreach (var (skill, ranks) in Ranks.OrderBy(entry => entry.Key))
        {
            if (creature.Skills.Ranks(skill) < ranks)
            {
                missing.Add($"{Skills.SkillInfo.Name(skill)} {ranks} rank{(ranks == 1 ? string.Empty : "s")}");
            }
        }

        if (Proficient && choice is not null && feat is not null && !Classes.Proficiency.IsProficientWithKind(creature, choice))
        {
            missing.Add($"proficiency with the {choice}");
        }

        if (CriticalFeats > 0 && creature.Feats.Count(owned => owned.Critical) < CriticalFeats)
        {
            missing.Add($"{CriticalFeats} critical feats");
        }

        if (Channel is { } kind && Classes.ClassPowers.ChannelKindOf(creature) != kind)
        {
            missing.Add($"channel {kind.ToString().ToLowerInvariant()} energy");
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

        if (AnyOf.Count > 0)
        {
            parts = parts.Append(string.Join(" or ", AnyOf));
        }

        if (BaseAttack > 0)
        {
            parts = parts.Append($"base attack +{BaseAttack}");
        }

        if (Level > 0)
        {
            parts = parts.Append($"level {Level}");
        }

        parts = parts.Concat(ClassLevels.OrderBy(entry => entry.Key, StringComparer.Ordinal)
            .Select(entry => $"{entry.Key} {entry.Value}"));

        if (CasterLevel > 0)
        {
            parts = parts.Append($"caster level {CasterLevel}");
        }

        if (Arcane)
        {
            parts = parts.Append("arcane spells");
        }

        parts = parts.Concat(Ranks.OrderBy(entry => entry.Key)
            .Select(entry => $"{Skills.SkillInfo.Name(entry.Key)} {entry.Value} rank{(entry.Value == 1 ? string.Empty : "s")}"));

        if (Proficient)
        {
            parts = parts.Append("proficiency with the weapon");
        }

        if (CriticalFeats > 0)
        {
            parts = parts.Append($"{CriticalFeats} critical feats");
        }

        if (Channel is { } kind)
        {
            parts = parts.Append($"channel {kind.ToString().ToLowerInvariant()} energy");
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

    /// <summary>
    /// A critical feat: one whose effect lands with a confirmed critical hit, and of which only
    /// one may be applied to any one critical without Critical Mastery.
    /// </summary>
    public bool Critical { get; init; }

    /// <summary>
    /// Whether it can be taken more than once for the same thing: Extra Channel, Extra Rage and
    /// Fleet stack with themselves. A feat taken for a weapon or a skill is taken again for
    /// another one whether or not this is set.
    /// </summary>
    public bool Repeatable { get; init; }

    /// <summary>The skills a skill feat helps with, for <see cref="FeatEffect.SkillBonus"/>.</summary>
    public IReadOnlyList<Skills.Skill> Skills { get; init; } = [];

    /// <summary>
    /// Why nobody may take it yet, when that is so: "needs two-weapon fighting". Null for every
    /// feat the game can actually do.
    /// </summary>
    /// <remarks>
    /// The Core Rulebook's whole list is in the content, so that a feat asked for by name is
    /// found and its prerequisites read as they should. The ones whose rules the engine has no
    /// machinery for say so here rather than being missing, or worse, being offered and doing
    /// nothing.
    /// </remarks>
    public string? Unavailable { get; init; }

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

    /// <summary>Whether the game can do what it says at all.</summary>
    public bool IsAvailable => Unavailable is null;

    /// <summary>
    /// Whether a creature could take it: qualified, and does not already have it. A feat taken
    /// once per weapon may be taken again, for a different one.
    /// </summary>
    public bool AvailableTo(Creatures.Creature creature) => WhyNot(creature).Count == 0;

    /// <summary>
    /// Everything standing between a creature and this feat, in words: why the game cannot do
    /// it yet, that it is already held, or the prerequisites it is short of. Empty when it may
    /// be taken.
    /// </summary>
    public IReadOnlyList<string> WhyNot(Creatures.Creature creature)
    {
        ArgumentNullException.ThrowIfNull(creature);

        if (Unavailable is { } reason)
        {
            return [reason];
        }

        if (Takes == FeatChoice.None && !Repeatable && creature.HasFeat(Id))
        {
            return ["already taken"];
        }

        if (Choice is not null && !Repeatable && creature.Feats.Any(held => held.Key == Key))
        {
            return ["already taken"];
        }

        return Requires.Unmet(creature, Choice, this);
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

/// <summary>What each kind of feat choice may be, apart from weapons, which the content decides.</summary>
public static class FeatChoices
{
    /// <summary>The four alignments an Alignment Channel can be taken for.</summary>
    public static IReadOnlyList<string> Alignments { get; } = ["chaos", "evil", "good", "law"];

    /// <summary>The four elements an Elemental Channel can be taken for.</summary>
    public static IReadOnlyList<string> Elements { get; } = ["air", "earth", "fire", "water"];

    /// <summary>Everything a choice of this kind may be, or empty for weapons and for none.</summary>
    public static IReadOnlyList<string> Options(FeatChoice takes) => takes switch
    {
        FeatChoice.Skill => [.. Skills.SkillInfo.All.Select(skill => skill.ToString())],
        FeatChoice.School => [.. Enum.GetNames<Magic.SpellSchool>()],
        FeatChoice.Alignment => Alignments,
        FeatChoice.Element => Elements,
        _ => [],
    };

    /// <summary>
    /// Whether a written choice is one of the options. Weapons are checked against the weapon
    /// files instead, so anything is let through here for them.
    /// </summary>
    public static bool IsValid(FeatChoice takes, string choice) =>
        takes is FeatChoice.Weapon or FeatChoice.None
        || Options(takes).Contains(choice, StringComparer.Ordinal);

    /// <summary>The skill a Skill Focus was taken for, or null if it names none.</summary>
    public static Skills.Skill? SkillOf(FeatDefinition feat) =>
        feat.Takes == FeatChoice.Skill && Enum.TryParse<Skills.Skill>(feat.Choice, out var skill) ? skill : null;

    /// <summary>The school a Spell Focus was taken for, or null if it names none.</summary>
    public static Magic.SpellSchool? SchoolOf(FeatDefinition feat) =>
        feat.Takes == FeatChoice.School && Enum.TryParse<Magic.SpellSchool>(feat.Choice, out var school) ? school : null;
}
