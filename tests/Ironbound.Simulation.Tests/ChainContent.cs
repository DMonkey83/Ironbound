using System.Text.Json;
using System.Text.Json.Nodes;
using Ironbound.Rules.Content;

namespace Ironbound.Simulation.Tests;

/// <summary>
/// The shipped content with every campaign's level taken out, so each plays as its chain of
/// encounters.
/// </summary>
/// <remarks>
/// The shipped campaigns are played on levels now, but most of what the campaign tests check —
/// wounds carrying from one fight to the next, the rest budget, the werewolf and the silver —
/// is about the fights and not the walking between them. Taking the level out keeps those tests
/// on the same creatures and the same encounters the game ships, rather than on copies that
/// could quietly drift from them.
/// </remarks>
public static class ChainContent
{
    private static readonly Lazy<ContentLibrary> Loaded = new(Load);

    private static readonly JsonDocumentOptions Lenient = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
    };

    public static ContentLibrary Library => Loaded.Value;

    private static ContentLibrary Load() => ContentLibrary.Load(
        ContentFiles.Read(Path.Combine(AppContext.BaseDirectory, "content"))
            .Select(file => file.Source.StartsWith("campaigns/", StringComparison.Ordinal)
                ? (file.Source, WithoutLevel(file.Json))
                : file));

    private static string WithoutLevel(string json)
    {
        var campaign = JsonNode.Parse(json, documentOptions: Lenient)!.AsObject();
        campaign.Remove("level");
        return campaign.ToJsonString();
    }
}
