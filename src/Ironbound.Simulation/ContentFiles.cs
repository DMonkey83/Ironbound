using Ironbound.Rules.Content;

namespace Ironbound.Simulation;

/// <summary>
/// Finds content on a real filesystem and hands the text to <see cref="ContentLibrary"/>.
/// </summary>
/// <remarks>
/// Deliberately not part of the rules, and deliberately not the only way in. An exported Godot
/// build keeps its content inside a <c>.pck</c> archive, where <see cref="Directory"/> cannot
/// reach; that build finds its files through Godot and calls
/// <see cref="ContentLibrary.Load"/> itself. This is for everything that does run against a
/// plain directory — the tests, and any tooling that grows later.
/// </remarks>
public static class ContentFiles
{
    private static ContentLibrary? _default;

    /// <summary>
    /// The content shipped beside the running assembly. Loaded once and shared, so treat it as
    /// read-only; anything that wants to add a file of its own should call <see cref="Load"/>.
    /// </summary>
    public static ContentLibrary Default =>
        _default ??= Load(Path.Combine(AppContext.BaseDirectory, "content"));

    public static ContentLibrary Load(string directory) => ContentLibrary.Load(Read(directory));

    /// <summary>
    /// Every JSON file under <paramref name="directory"/>, in a fixed order. The order matters:
    /// duplicate ids are reported against whichever file lost, and a load that shuffled would
    /// report a different one each run.
    /// </summary>
    public static IEnumerable<(string Source, string Json)> Read(string directory)
    {
        ArgumentNullException.ThrowIfNull(directory);

        return Directory.EnumerateFiles(directory, "*.json", SearchOption.AllDirectories)
            .Select(path => (
                Source: Path.GetRelativePath(directory, path).Replace('\\', '/'),
                Path: path))
            .OrderBy(found => found.Source, StringComparer.Ordinal)
            .Select(found => (found.Source, File.ReadAllText(found.Path)));
    }
}
