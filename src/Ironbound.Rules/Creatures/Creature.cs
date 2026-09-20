using Ironbound.Rules.Abilities;
using Ironbound.Rules.Combat;
using Ironbound.Rules.Conditions;
using Ironbound.Rules.Defense;
using Ironbound.Rules.Effects;
using Ironbound.Rules.Feats;
using Ironbound.Rules.Items;
using Ironbound.Rules.Magic;
using Ironbound.Rules.Modifiers;
using Ironbound.Rules.Saves;
using Ironbound.Rules.Dice;

namespace Ironbound.Rules.Creatures;

/// <summary>
/// Anything that can be attacked: its ability scores, the armour class derived from them, and
/// its hit points. A holder, in the same shape as <see cref="ArmorClass"/> and
/// <see cref="AbilityScore"/> — the rules that act on a creature live outside it.
/// </summary>
/// <remarks>
/// Anything derived from what the creature <em>is</em> — its size modifier, its reach, its
/// speed — is computed on demand rather than stored as a modifier, so that changing the creature
/// changes everything downstream at once and nothing has to be refreshed by hand.
/// </remarks>
public sealed class Creature
{
    private CreatureSize _size = CreatureSize.Medium;

    public Creature(
        string name,
        AbilityScores abilities,
        int baseHitPoints,
        int hitDice,
        RuleOptions? rules = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(abilities);

        Name = name;
        Abilities = abilities;
        Rules = rules ?? RuleOptions.Pathfinder;
        ArmorClass = new ArmorClass(abilities.Dexterity);
        Defenses = new DamageDefenses();
        Effects = new EffectCollection(this);
        Saves = new SavingThrows(abilities);
        Spells = new Spellcasting(this);
        Equipment = new Equipment(this);
        HitPoints = new HitPoints(baseHitPoints, hitDice, abilities.Constitution, Rules);
    }

    /// <summary>Builds a creature whose hit points come from its hit die, per the rule options.</summary>
    public static Creature Roll(
        string name,
        AbilityScores abilities,
        int hitDieSides,
        int hitDice,
        IRandomSource random,
        RuleOptions? rules = null)
    {
        rules ??= RuleOptions.Pathfinder;
        return new Creature(
            name,
            abilities,
            HitPoints.RollBase(hitDieSides, hitDice, random, rules),
            hitDice,
            rules);
    }

    public string Name { get; }

    public RuleOptions Rules { get; }

    public AbilityScores Abilities { get; }

    public ArmorClass ArmorClass { get; }

    /// <summary>
    /// How big it is. Setting this immediately moves its armour class and attack bonus, because
    /// size is a property of the creature rather than a bonus from some source — Enlarge Person
    /// changes what you are, and everything derived from it has to follow.
    /// </summary>
    public CreatureSize Size
    {
        get => _size;
        set
        {
            _size = value;
            ArmorClass.SizeModifier = CreatureSizes.Modifier(value);
        }
    }

    /// <summary>How far it can touch, in feet. Tiny and smaller cannot reach out of their square.</summary>
    public int Reach => CreatureSizes.Reach(_size);

    /// <summary>
    /// Which side it is on. Creatures sharing a non-zero allegiance are allies; zero means it
    /// belongs to nobody and is allied with nobody, which keeps unassigned creatures from
    /// accidentally flanking together.
    /// </summary>
    /// <remarks>
    /// This layer used to know nothing about factions. It has to: the rules say "ally" constantly
    /// — flanking, aid another, harmless spells, channel energy, most area effects — and an
    /// engine that cannot express it pays a toll at every one.
    /// </remarks>
    public int Allegiance { get; set; }

    public bool IsAllyOf(Creature other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return Allegiance != 0 && Allegiance == other.Allegiance && !ReferenceEquals(this, other);
    }

    public bool IsEnemyOf(Creature other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return !ReferenceEquals(this, other) && (Allegiance == 0 || Allegiance != other.Allegiance);
    }

    /// <summary>What it is wearing and holding.</summary>
    public Equipment Equipment { get; }

    /// <summary>What it can hit things with. The first is used when something must be chosen for it.</summary>
    public IList<WeaponAttack> Attacks { get; } = [];

    public WeaponAttack? PrimaryAttack => Attacks.Count > 0 ? Attacks[0] : null;

    /// <summary>
    /// The first thing it can swing rather than shoot.
    /// </summary>
    /// <remarks>
    /// Some things only a melee weapon may do, and an attack of opportunity is the one that
    /// matters most: a creature holding nothing but a bow does not get to shoot people for
    /// walking past it. Null means it has no answer to anything that closes.
    /// </remarks>
    public WeaponAttack? MeleeAttack =>
        Attacks.FirstOrDefault(weapon => !weapon.IsRanged);

    /// <summary>
    /// How practised it is at fighting, before strength, size or anything situational.
    /// </summary>
    /// <remarks>
    /// A statistic rather than a modifier, and deliberately so: it decides <em>how many</em>
    /// attacks a full attack is worth, not merely how accurate they are. Buried in
    /// <see cref="AttackModifiers"/> alongside Bless and a magic sword there would be no honest
    /// way to ask that question. It reaches the attack roll through <see cref="Combat.Strike"/>,
    /// the same way size does.
    /// </remarks>
    public int BaseAttackBonus { get; set; }

    /// <summary>How many times a full attack lets it swing.</summary>
    public int AttacksPerFullAttack => Iteratives.Count(BaseAttackBonus);

    /// <summary>
    /// What it has learnt to do. Feats hand out their static bonuses once, when the creature is
    /// built; the few that change how a rule behaves are recognised by
    /// <see cref="FeatDefinition.Effect"/> wherever that rule lives.
    /// </summary>
    public IList<FeatDefinition> Feats { get; } = [];

    public bool HasFeat(string id) =>
        Feats.Any(feat => string.Equals(feat.Id, id, StringComparison.Ordinal));

    public bool HasFeat(FeatEffect effect) =>
        effect != FeatEffect.None && Feats.Any(feat => feat.Effect == effect);

    /// <summary>How many attacks of opportunity it gets before feats. One, for almost everything.</summary>
    public int BaseAttacksOfOpportunity { get; set; } = 1;

    /// <summary>
    /// How many it actually gets between its turns.
    /// </summary>
    /// <remarks>
    /// Derived rather than stored, so Combat Reflexes keeps up with a Dexterity that changes —
    /// a Cat's Grace really should buy the extra swing it promises, and a poison that drains
    /// Dexterity really should take it away again.
    /// </remarks>
    public int AttacksOfOpportunityPerRound => BaseAttacksOfOpportunity
        + (HasFeat(FeatEffect.CombatReflexes)
            ? Math.Max(0, Abilities[Ability.Dexterity].Modifier)
            : 0);

    /// <summary>Base movement in feet per round, before anything hurries or hinders it.</summary>
    public int Speed { get; set; } = 30;

    /// <summary>Haste, a monk's fast movement, heavy armour, difficult circumstances.</summary>
    public ModifierStack SpeedModifiers { get; } = new();

    /// <summary>
    /// Speed after modifiers and after whatever is wrong with it. Entangled halves it, and the
    /// halving is deliberately not a modifier: a percentage that stacked with Haste's flat bonus
    /// in one pass would give the wrong answer whichever order they landed in.
    /// </summary>
    public int CurrentSpeed
    {
        get
        {
            var speed = Math.Max(0, Speed + SpeedModifiers.Total);

            foreach (var condition in Conditions)
            {
                speed = speed * ConditionInfo.Of(condition).SpeedPercent / 100;
            }

            return speed;
        }
    }

    /// <summary>Everything currently wrong with it, by name.</summary>
    public IEnumerable<Condition> Conditions => Effects.Conditions;

    public bool Has(Condition condition) => Effects.Has(condition);

    public bool IsProne => Has(Condition.Prone);

    /// <summary>
    /// Whether it can do anything at all this turn. Being dazed or stunned is not a penalty on
    /// your actions — it is the absence of them.
    /// </summary>
    public bool CanAct
    {
        get
        {
            if (!IsConscious)
            {
                return false;
            }

            foreach (var condition in Conditions)
            {
                if (ConditionInfo.Of(condition).DeniesActions)
                {
                    return false;
                }
            }

            return true;
        }
    }

    /// <summary>Whether it has lost its Dexterity bonus to armour class, as flat-footed does.</summary>
    public bool DeniesDexterity
    {
        get
        {
            foreach (var condition in Conditions)
            {
                if (ConditionInfo.Of(condition).DeniesDexterity)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>What stands between a damage roll and <see cref="HitPoints"/>.</summary>
    public DamageDefenses Defenses { get; }

    /// <summary>Fortitude, Reflex and Will.</summary>
    public SavingThrows Saves { get; }

    /// <summary>What it can cast and how much of it is left.</summary>
    public Spellcasting Spells { get; }

    /// <summary>
    /// What the creature brings to every attack roll it makes: base attack bonus, size, and
    /// buffs like Bless or Prayer. Met with the weapon's own modifiers in one stacking pass.
    /// </summary>
    public ModifierStack AttackModifiers { get; } = new();

    /// <summary>What the creature brings to damage: Inspire Courage, Weapon Specialization.</summary>
    public ModifierStack DamageModifiers { get; } = new();

    /// <summary>Improved Initiative and anything else that decides who moves first.</summary>
    public ModifierStack InitiativeModifiers { get; } = new();

    /// <summary>Buffs, conditions and anything else running on a clock.</summary>
    public EffectCollection Effects { get; }

    public HitPoints HitPoints { get; }

    public bool IsAlive => HitPoints.IsAlive;

    public bool IsConscious => HitPoints.IsConscious;

    public override string ToString() => $"{Name} — {ArmorClass} — {HitPoints}";
}
