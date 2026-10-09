using Ironbound.Rules.Feats;
using Ironbound.Rules.Magic;

namespace Ironbound.Rules.Classes;

/// <summary>Which way a cleric channels: healing the living, or harming them.</summary>
public enum ChannelKind
{
    Positive,
    Negative,
}

/// <summary>
/// What a character decided while their class features were being handed out: which weapon
/// group to train in, which talents to take, which god and which domains.
/// </summary>
/// <remarks>
/// Held as resolved definitions rather than ids, exactly as <see cref="Creatures.Creature.Feats"/>
/// and <see cref="Creatures.Creature.Levels"/> are: the rules that act on a choice need the
/// talent's effect and the domain's spells, and asking a library for them at every swing would
/// mean the creature could not be resolved without one to hand. A save writes the ids and the
/// load looks them up again.
/// </remarks>
public sealed class ClassChoices
{
    /// <summary>Fighter weapon groups, in the order they were picked: the first is trained deepest.</summary>
    public IList<string> WeaponGroups { get; } = [];

    /// <summary>Rogue talents and rage powers, in the order they were taken.</summary>
    public IList<TalentDefinition> Talents { get; } = [];

    /// <summary>A cleric's god, or null for none — which is allowed, and lifts the domain check.</summary>
    public DeityDefinition? Deity { get; set; }

    /// <summary>A cleric's two domains.</summary>
    public IList<DomainDefinition> Domains { get; } = [];

    /// <summary>
    /// How a cleric of a neutral god — or none — channels. A good god's cleric channels positive
    /// and an evil one's negative whatever this says.
    /// </summary>
    public ChannelKind? Channel { get; set; }

    /// <summary>A wizard's arcane school, or null for one who has not chosen.</summary>
    public SchoolDefinition? School { get; set; }

    /// <summary>The two schools a specialist gave up, which cost double from then on.</summary>
    public IList<SpellSchool> Opposition { get; } = [];

    /// <summary>Whether a wizard's arcane bond is to an object. The familiar is not modelled.</summary>
    public bool BondedObject { get; set; }

    /// <summary>The combat feat a War cleric's Weapon Master lends her, once she has it.</summary>
    public FeatDefinition? WeaponMasterFeat { get; set; }

    public bool HasTalent(TalentEffect effect) => Talents.Any(talent => talent.Effect == effect);

    public bool HasTalent(string id) =>
        Talents.Any(talent => string.Equals(talent.Id, id, StringComparison.Ordinal));

    public bool HasDomainPower(GrantedPowerEffect effect) =>
        Domains.Any(domain => domain.Powers.Any(power => power.Effect == effect));

    /// <summary>Wipes everything, as a load does before putting the saved choices back.</summary>
    internal void Clear()
    {
        WeaponGroups.Clear();
        Talents.Clear();
        Deity = null;
        Domains.Clear();
        Channel = null;
        School = null;
        Opposition.Clear();
        BondedObject = false;
        WeaponMasterFeat = null;
    }
}
