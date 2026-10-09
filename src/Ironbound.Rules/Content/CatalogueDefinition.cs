namespace Ironbound.Rules.Content;

/// <summary>One line of a catalogue: a thing that can be bought, and what the table says about it.</summary>
/// <param name="Stats">Whatever columns the table had beyond cost and weight, as written:
/// "Crew" "3", "Type" "B". Read by nothing; kept so the catalogue says what the book says.</param>
public sealed record CatalogueEntry(
    string Id,
    string Name,
    double Cost,
    double Weight,
    string Description,
    IReadOnlyDictionary<string, string> Stats);

/// <summary>
/// A table of things that are not weapons and not anything the rules use: ammunition, a gun's
/// powder and bullets, siege ammunition, weapon modifications.
/// </summary>
/// <remarks>
/// The owner asked for everything pasted to be in the game's files. These are, as data only —
/// nothing counts arrows or loads a gun or modifies a sword yet — and keeping them as one list
/// per table rather than a file per arrow is what lets them be complete without a content kind
/// for each, or a hundred files nobody reads. <see cref="Rule"/> holds the one rule a table
/// carries that something will one day need, in words.
/// </remarks>
public sealed record CatalogueDefinition
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public string Description { get; init; } = string.Empty;

    /// <summary>A rule the whole table shares, written out: a modified weapon is a step harder to use.</summary>
    public string Rule { get; init; } = string.Empty;

    public IReadOnlyList<CatalogueEntry> Entries { get; init; } = [];

    public CatalogueEntry? Get(string id) =>
        Entries.FirstOrDefault(entry => string.Equals(entry.Id, id, StringComparison.Ordinal));
}
