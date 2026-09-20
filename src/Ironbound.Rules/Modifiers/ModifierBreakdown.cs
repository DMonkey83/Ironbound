using System.Text;

namespace Ironbound.Rules.Modifiers;

/// <summary>
/// The full resolution of a <see cref="ModifierStack"/>: every modifier it holds,
/// whether it counted, and the resulting total. Feeds the combat log.
/// </summary>
public sealed class ModifierBreakdown(IReadOnlyList<ModifierEntry> entries, int total)
{
    public IReadOnlyList<ModifierEntry> Entries { get; } = entries;

    public int Total { get; } = total;

    public IEnumerable<ModifierEntry> Applied => Entries.Where(e => e.Applied);

    public IEnumerable<ModifierEntry> Suppressed => Entries.Where(e => !e.Applied);

    public override string ToString()
    {
        var text = new StringBuilder();
        foreach (var entry in Entries)
        {
            if (text.Length > 0)
            {
                text.Append(' ');
            }

            text.Append(entry.Applied
                ? entry.Modifier.ToString()
                : $"[{entry.Modifier} superseded by {entry.SuppressedBy}]");
        }

        return text.Length == 0 ? "= 0" : $"{text} = {Total}";
    }
}
