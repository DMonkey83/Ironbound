using Ironbound.Rules.Creatures;

namespace Ironbound.Rules.Effects;

/// <summary>Every kind of bleeding wound the rules can open, and the one thing that closes them all.</summary>
public static class Bleeds
{
    /// <summary>What Deadly Stroke's Constitution bleed is filed under.</summary>
    public const string DeadlyStrokeLabel = "Deadly Stroke";

    /// <summary>The names every bleed is filed under: a rogue's, a critical's, a deadly stroke's.</summary>
    public static IReadOnlyList<string> Labels { get; } =
        [Classes.SneakAttack.BleedLabel, Combat.CriticalFeats.BleedLabel, DeadlyStrokeLabel];

    /// <summary>Stops every bleed on a creature, as magical healing does. True if there was one.</summary>
    public static bool Stop(Creature creature)
    {
        ArgumentNullException.ThrowIfNull(creature);

        var stopped = false;
        foreach (var label in Labels)
        {
            stopped |= creature.Effects.Remove(label) is not null;
        }

        return stopped;
    }
}
