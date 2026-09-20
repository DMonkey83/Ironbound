using Ironbound.Rules.Content;
using Ironbound.Rules.Magic;

namespace Ironbound.Rules.Tests;

/// <summary>
/// The content the game ships, loaded once for the whole test run.
/// </summary>
/// <remarks>
/// The files are copied next to the test assembly by the project file, which is what makes a
/// typo in <c>content/spells/fireball.json</c> a failing test rather than a surprise three rooms
/// into a playthrough.
/// </remarks>
public static class TestContent
{
    private static readonly Lazy<ContentLibrary> Loaded = new(Load);

    public static ContentLibrary Library => Loaded.Value;

    public static string Directory => Path.Combine(AppContext.BaseDirectory, "content");

    /// <summary>Reads the shipped files in a fixed order, as the game does.</summary>
    public static ContentLibrary Load() => ContentLibrary.Load(Files());

    public static IEnumerable<(string Source, string Json)> Files() =>
        System.IO.Directory.EnumerateFiles(Directory, "*.json", SearchOption.AllDirectories)
            .Select(path => (
                Source: Path.GetRelativePath(Directory, path).Replace('\\', '/'),
                Path: path))
            .OrderBy(found => found.Source, StringComparer.Ordinal)
            .Select(found => (found.Source, File.ReadAllText(found.Path)));
}

/// <summary>
/// The shipped spells, by the names the tests already knew them by.
/// </summary>
/// <remarks>
/// This used to be a class in the rules holding five hardcoded spells. It is now a lookup into
/// the content files, which quietly upgrades every spell test: they no longer check that the
/// rules can express a fireball, they check that the fireball the game actually ships behaves.
/// </remarks>
public static class Spells
{
    public static Spell MagicMissile => Get("magic-missile");

    public static Spell CureLightWounds => Get("cure-light-wounds");

    public static Spell ScorchingRay => Get("scorching-ray");

    public static Spell Bless => Get("bless");

    public static Spell Fireball => Get("fireball");

    public static IReadOnlyList<Spell> All =>
        [MagicMissile, CureLightWounds, ScorchingRay, Bless, Fireball];

    private static Spell Get(string id) => TestContent.Library.GetSpell(id)
        ?? throw new InvalidOperationException($"The shipped content has no spell '{id}'.");
}
