namespace Ironbound.Rules.Creatures;

/// <summary>
/// What kind of thing a creature is, in the Bestiary's terms. Channel energy is the first rule
/// that asks: positive energy heals the living and burns the undead.
/// </summary>
/// <remarks>
/// Humanoid by default, because every creature in the game until now has been one or near
/// enough — the dire rat and the ogre are written as what they are.
/// </remarks>
public enum CreatureType
{
    Humanoid,
    Aberration,
    Animal,
    Construct,
    Dragon,
    Fey,
    MagicalBeast,
    MonstrousHumanoid,
    Ooze,
    Outsider,
    Plant,
    Undead,
    Vermin,
}

public static class CreatureTypes
{
    /// <summary>
    /// Whether positive energy heals it and negative energy harms it: everything but the
    /// undead and the constructs, which are not alive in the sense the energies care about.
    /// </summary>
    public static bool IsLiving(Creature creature)
    {
        ArgumentNullException.ThrowIfNull(creature);
        return creature.Type is not (CreatureType.Undead or CreatureType.Construct);
    }

    public static bool IsUndead(Creature creature)
    {
        ArgumentNullException.ThrowIfNull(creature);
        return creature.Type == CreatureType.Undead;
    }

    /// <summary>An outsider of one subtype — "evil", "fire" — which is what the channel feats aim at.</summary>
    public static bool IsOutsiderOf(Creature creature, string subtype)
    {
        ArgumentNullException.ThrowIfNull(creature);
        return creature.Type == CreatureType.Outsider && creature.Subtypes.Contains(subtype);
    }
}
