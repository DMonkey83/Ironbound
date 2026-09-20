using Ironbound.Rules.Abilities;

namespace Ironbound.Rules.Saves;

/// <summary>The three saving throws.</summary>
public enum Save
{
    /// <summary>Endurance: poison, disease, paralysis, anything that attacks the body.</summary>
    Fortitude = 0,

    /// <summary>Getting out of the way: fireballs, pits, breath weapons.</summary>
    Reflex,

    /// <summary>Resolve: charms, fear, illusions, anything that attacks the mind.</summary>
    Will,
}

public static class SaveInfo
{
    public static IReadOnlyList<Save> All { get; } = [Save.Fortitude, Save.Reflex, Save.Will];

    public const int Count = 3;

    public static Ability AbilityFor(Save save) => save switch
    {
        Save.Fortitude => Ability.Constitution,
        Save.Reflex => Ability.Dexterity,
        Save.Will => Ability.Wisdom,
        _ => throw new ArgumentOutOfRangeException(nameof(save)),
    };

    public static string Name(Save save) => save.ToString();

    public static string Abbreviate(Save save) => save switch
    {
        Save.Fortitude => "Fort",
        Save.Reflex => "Ref",
        Save.Will => "Will",
        _ => throw new ArgumentOutOfRangeException(nameof(save)),
    };
}

/// <summary>
/// The two base-save tables. Class content really, but they are short, unambiguous and every
/// creature needs them, so they live here rather than being retyped per class.
/// </summary>
public static class SaveProgression
{
    /// <summary>A class's strong save: 2 at first level, rising every other level.</summary>
    public static int Good(int level)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(level);
        return level == 0 ? 0 : 2 + (level / 2);
    }

    /// <summary>A class's weak save: nothing at first level, rising every third.</summary>
    public static int Poor(int level)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(level);
        return level / 3;
    }
}
