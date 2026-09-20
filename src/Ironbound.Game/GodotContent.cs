using System.Collections.Generic;
using Godot;
using Ironbound.Rules.Content;

/// <summary>
/// Finds the content files through Godot and hands their text to the rules.
/// </summary>
/// <remarks>
/// Godot's <see cref="DirAccess"/> rather than <c>System.IO</c>, and this is the whole reason
/// <see cref="ContentLibrary"/> takes strings instead of paths: in an exported build
/// <c>res://content/</c> is not a directory on disk at all but an offset inside the <c>.pck</c>
/// archive. A loader built on <c>System.IO</c> would work perfectly in the editor and fail on the
/// first export — the worst possible moment to find out.
/// <para>
/// JSON is not a Godot resource type, so packaging relies on the export preset's
/// "non-resource files to export" filter including <c>*.json</c>.
/// </para>
/// </remarks>
public static class GodotContent
{
	public const string Root = "res://content";

	public static ContentLibrary Load()
	{
		var library = ContentLibrary.Load(Read(Root));

		// Loud, and at startup. A content mistake that surfaced three rooms in as a missing
		// spell would be far harder to trace back to the file that caused it.
		foreach (var problem in library.Problems)
		{
			GD.PushError($"Content: {problem}");
		}

		return library;
	}

	/// <summary>
	/// Every JSON file under <paramref name="directory"/> and below it, sorted, because the order
	/// files are read in decides which of two duplicate ids gets reported.
	/// </summary>
	public static IEnumerable<(string Source, string Json)> Read(string directory)
	{
		var found = new List<string>();
		Gather(directory, found);
		found.Sort(System.StringComparer.Ordinal);

		var files = new List<(string Source, string Json)>();
		foreach (var path in found)
		{
			using var file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
			if (file is null)
			{
				GD.PushError($"Content: cannot read {path}: {FileAccess.GetOpenError()}");
				continue;
			}

			files.Add((path[(Root.Length + 1)..], file.GetAsText()));
		}

		return files;
	}

	private static void Gather(string directory, List<string> into)
	{
		using var handle = DirAccess.Open(directory);
		if (handle is null)
		{
			GD.PushError($"Content: cannot open {directory}: {DirAccess.GetOpenError()}");
			return;
		}

		foreach (var name in handle.GetFiles())
		{
			// Exported builds may hand back a remapped name; the original is what opens.
			var file = name.EndsWith(".remap") ? name[..^6] : name;
			if (file.EndsWith(".json"))
			{
				into.Add($"{directory}/{file}");
			}
		}

		foreach (var child in handle.GetDirectories())
		{
			Gather($"{directory}/{child}", into);
		}
	}
}
