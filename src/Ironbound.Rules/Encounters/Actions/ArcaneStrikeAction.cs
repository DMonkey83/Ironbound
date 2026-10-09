using Ironbound.Rules.Creatures;
using Ironbound.Rules.Effects;
using Ironbound.Rules.Feats;
using Ironbound.Rules.Magic;

namespace Ironbound.Rules.Encounters.Actions;

/// <summary>
/// Arcane Strike: a swift action that puts a little of a caster's magic into her weapons for a
/// round — one more damage, and another for every five arcane caster levels, and blows that
/// count as magic against damage reduction.
/// </summary>
/// <remarks>
/// A round from the moment it is taken, which is until the start of her next turn: long enough
/// for this turn's swings and for any attack of opportunity before she acts again, as the book
/// means "for one round".
/// </remarks>
public sealed class ArcaneStrikeAction : GameAction
{
    /// <summary>The name its effect is filed under, and what the damage line calls it.</summary>
    public const string EffectName = "Arcane Strike";

    /// <summary>The most it can ever be worth, at twentieth level.</summary>
    public const int MaximumBonus = 5;

    public override string Name => EffectName;

    public override ActionCost Cost => ActionCost.Swift;

    public override bool CanPerform(ActionContext context) =>
        context.Actor.HasFeat(FeatEffect.ArcaneStrike)
        && SpellTraditions.CastsArcane(context.Actor)
        && !context.Actor.Effects.Has(EffectName);

    public override ActionResult Perform(ActionContext context)
    {
        var actor = context.Actor;
        actor.Effects.Apply(new ModifierEffect(EffectName, Duration.Rounds(1)));

        return new ActionResult(
            this,
            actor,
            $"{actor.Name} channels arcane power into {actor.Name}'s weapons: +{BonusFor(actor)} damage, and magic, for a round");
    }

    /// <summary>What it is worth to this caster: one, and one more every five arcane caster levels.</summary>
    public static int BonusFor(Creature creature) =>
        Math.Min(MaximumBonus, 1 + (SpellTraditions.ArcaneCasterLevel(creature) / 5));

    /// <summary>What it adds to a blow right now: nothing unless one is running.</summary>
    public static int BonusOf(Creature creature)
    {
        ArgumentNullException.ThrowIfNull(creature);
        return IsActive(creature) ? BonusFor(creature) : 0;
    }

    /// <summary>Whether the creature's weapons are counting as magic this round.</summary>
    public static bool IsActive(Creature creature)
    {
        ArgumentNullException.ThrowIfNull(creature);
        return creature.Effects.Has(EffectName);
    }
}
