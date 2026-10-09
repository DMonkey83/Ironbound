namespace Ironbound.Rules.Classes;

/// <summary>The nine alignments, as a deity's entry gives them.</summary>
public enum Alignment
{
    LawfulGood,
    NeutralGood,
    ChaoticGood,
    LawfulNeutral,
    Neutral,
    ChaoticNeutral,
    LawfulEvil,
    NeutralEvil,
    ChaoticEvil,
}

public static class Alignments
{
    /// <summary>"LG", "N", "CE" — the way every deity table in the books writes them.</summary>
    public static Alignment? Parse(string? written) => written?.Trim().ToUpperInvariant() switch
    {
        "LG" => Alignment.LawfulGood,
        "NG" => Alignment.NeutralGood,
        "CG" => Alignment.ChaoticGood,
        "LN" => Alignment.LawfulNeutral,
        "N" or "TN" => Alignment.Neutral,
        "CN" => Alignment.ChaoticNeutral,
        "LE" => Alignment.LawfulEvil,
        "NE" => Alignment.NeutralEvil,
        "CE" => Alignment.ChaoticEvil,
        _ => null,
    };

    public static string Abbreviate(Alignment alignment) => alignment switch
    {
        Alignment.LawfulGood => "LG",
        Alignment.NeutralGood => "NG",
        Alignment.ChaoticGood => "CG",
        Alignment.LawfulNeutral => "LN",
        Alignment.Neutral => "N",
        Alignment.ChaoticNeutral => "CN",
        Alignment.LawfulEvil => "LE",
        Alignment.NeutralEvil => "NE",
        _ => "CE",
    };

    public static bool IsGood(Alignment alignment) =>
        alignment is Alignment.LawfulGood or Alignment.NeutralGood or Alignment.ChaoticGood;

    public static bool IsEvil(Alignment alignment) =>
        alignment is Alignment.LawfulEvil or Alignment.NeutralEvil or Alignment.ChaoticEvil;
}

/// <summary>
/// A god, as far as a cleric's rules care: an alignment, the domains on offer, a weapon.
/// </summary>
/// <remarks>
/// The domains are ids and are not checked against the domain files, because most of a
/// pantheon's domains have no file yet — Erastil offers Animal and Plant, and the engine has
/// neither. What is checked is the other direction: a cleric's chosen domains have to be on her
/// god's list.
/// </remarks>
public sealed record DeityDefinition
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public Alignment Alignment { get; init; } = Alignment.Neutral;

    /// <summary>Domain ids, lower case: "healing", "war", "good".</summary>
    public IReadOnlyList<string> Domains { get; init; } = [];

    /// <summary>
    /// A weapon id, which every shipped god's now is: it is what makes a cleric of this god
    /// proficient with it. Free text still loads, for a god whose weapon the game lacks, and is
    /// then a name and nothing more.
    /// </summary>
    public string FavoredWeapon { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public bool Offers(string domainId) => Domains.Contains(domainId, StringComparer.Ordinal);

    public override string ToString() => $"{Name} ({Alignments.Abbreviate(Alignment)})";
}
