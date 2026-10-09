using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;
using Ironbound.Simulation;

/// <summary>
/// The part of <see cref="Main"/> that keeps saved games: a quick save, a few autosaves taken as
/// the story goes, saves made on purpose, and the list the title screen and F9 both read.
/// </summary>
/// <remarks>
/// There used to be one file. Continue and Load Game both opened it, so Load had nothing to show,
/// and Continue went back to the last time anybody pressed F5 rather than to the last game
/// played. The owner found both. Now every save sits in its own file under <c>user://saves</c>
/// with a small card beside it saying what it is, and Continue takes the newest of them all.
/// <para>
/// The card is a separate file so the save itself stays exactly what <see cref="Campaign.ToJson"/>
/// writes, versioned by the rules and nothing else. The old single save is still listed, read
/// in place and never moved, so nothing anybody played is lost.
/// </para>
/// </remarks>
public partial class Main
{
	/// <summary>Autosaves kept before the oldest is written over.</summary>
	private const int Autosaves = 3;

	private enum SaveKind
	{
		Quick,
		Auto,
		Manual,
	}

	/// <summary>What the list shows for one save, and where it is.</summary>
	private sealed record SaveCard(string Path, SaveKind Kind, string Campaign, string Place, string Party, long SavedAt, bool Legacy = false);

	/// <summary>The card as it is written beside a save.</summary>
	private sealed record SaveMeta(string Kind, string Campaign, string Place, string Party, long SavedAt);

	private static string SaveFolder => DisplayServer.GetName() == "headless" ? "user://headless-saves" : "user://saves";

	/// <summary>The one file there used to be: listed, loaded, never written again.</summary>
	private static string LegacySave => SavePath;

	private bool SaveExists() => Saves().Count > 0;

	/// <summary>Every save there is, newest first.</summary>
	private List<SaveCard> Saves()
	{
		var cards = new List<SaveCard>();

		if (DirAccess.Open(SaveFolder) is { } folder)
		{
			foreach (var file in folder.GetFiles().Where(name => name.EndsWith(".save", StringComparison.Ordinal)))
			{
				var path = $"{SaveFolder}/{file}";
				if (ReadText(MetaPath(path)) is { Length: > 0 } text && Meta(text) is { } meta)
				{
					cards.Add(new SaveCard(
						path,
						Enum.TryParse<SaveKind>(meta.Kind, out var kind) ? kind : SaveKind.Manual,
						meta.Campaign,
						meta.Place,
						meta.Party,
						meta.SavedAt));
				}
				else
				{
					cards.Add(new SaveCard(path, SaveKind.Manual, AdventureIn(path), string.Empty, string.Empty, (long)FileAccess.GetModifiedTime(path)));
				}
			}
		}

		if (FileAccess.FileExists(LegacySave))
		{
			cards.Add(new SaveCard(LegacySave, SaveKind.Quick, AdventureIn(LegacySave), "from an earlier build", string.Empty,
				(long)FileAccess.GetModifiedTime(LegacySave), Legacy: true));
		}

		return [.. cards.OrderByDescending(card => card.SavedAt)];
	}

	/// <summary>
	/// The adventure a save with no card belongs to, read out of the save itself: the old single
	/// save was written before there were cards.
	/// </summary>
	private string AdventureIn(string path)
	{
		try
		{
			using var save = JsonDocument.Parse(ReadText(path));
			if (save.RootElement.TryGetProperty("Campaign", out var campaign)
				&& campaign.TryGetProperty("Id", out var id)
				&& _content.GetCampaign(id.GetString() ?? string.Empty) is { } adventure)
			{
				return adventure.Name;
			}
		}
		catch (JsonException)
		{
			// Not a save anybody can read; it is still listed, so it can be seen and ignored.
		}

		return "A saved game";
	}

	private static SaveMeta Meta(string text)
	{
		try
		{
			return JsonSerializer.Deserialize<SaveMeta>(text);
		}
		catch (JsonException)
		{
			return null;
		}
	}

	private static string MetaPath(string savePath) => $"{savePath[..^".save".Length]}.card";

	private static string ReadText(string path)
	{
		using var file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
		return file?.GetAsText() ?? string.Empty;
	}

	private static bool WriteText(string path, string text)
	{
		using var file = FileAccess.Open(path, FileAccess.ModeFlags.Write);
		if (file is null)
		{
			GD.PushError($"Could not write {path}: {FileAccess.GetOpenError()}");
			return false;
		}

		file.StoreString(text);
		return true;
	}

	/// <summary>
	/// Whether this run is somebody playing, as opposed to a script: anything started with
	/// arguments of its own — an autoplay, a capture, a tour — leaves the player's saves alone.
	/// </summary>
	private static bool Playing() =>
		DisplayServer.GetName() != "headless" && OS.GetCmdlineUserArgs().Length == 0;

	/// <summary>Writes the running game to a save of the given kind, with its card.</summary>
	private bool SaveAs(SaveKind kind)
	{
		if (_campaign is null)
		{
			return false;
		}

		DirAccess.MakeDirRecursiveAbsolute(SaveFolder);

		var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
		var path = kind switch
		{
			SaveKind.Quick => $"{SaveFolder}/quicksave.save",
			SaveKind.Auto => $"{SaveFolder}/{NextAutosave()}.save",
			_ => $"{SaveFolder}/save-{now}.save",
		};

		var json = _campaign.ToJson();
		if (!WriteText(path, json))
		{
			return false;
		}

		var meta = new SaveMeta(
			kind.ToString(),
			_campaign.Definition.Name,
			Whereabouts(),
			string.Join("  ·  ", _campaign.Party.Select(member => $"{member.Name} {member.Level}")),
			now);
		WriteText(MetaPath(path), JsonSerializer.Serialize(meta));

		if (_load is not null)
		{
			_load.Disabled = false;
		}

		return true;
	}

	/// <summary>The autosave slot to write: an empty one, else the one written longest ago.</summary>
	private static string NextAutosave()
	{
		var slots = Enumerable.Range(1, Autosaves).Select(n => $"autosave-{n}").ToList();
		var free = slots.FirstOrDefault(slot => !FileAccess.FileExists($"{SaveFolder}/{slot}.save"));
		return free ?? slots.OrderBy(slot => FileAccess.GetModifiedTime($"{SaveFolder}/{slot}.save")).First();
	}

	/// <summary>
	/// Saves as the story goes, the way Wrath does: as a fight begins, once it is won, at the start
	/// of an adventure and on the way out. Only for somebody actually playing.
	/// </summary>
	private void Autosave()
	{
		if (Playing() && _campaign is { State: not CampaignState.Lost })
		{
			SaveAs(SaveKind.Auto);
		}
	}

	/// <summary>Where the party is, in a few words: the room, or the chapter.</summary>
	private string Whereabouts()
	{
		if (!_campaign.IsLevel)
		{
			return $"Chapter {Math.Max(1, _campaign.Chapter)} of {_campaign.Definition.Encounters.Count}";
		}

		if (_campaign.CurrentArea is { } fighting)
		{
			return $"{fighting.Name}, fighting";
		}

		return _battle.Party.Select(one => _battle.Battlefield?.SquareOf(one)).FirstOrDefault(square => square is not null) is { } here
			&& _campaign.AreaAt(here) is { } area
				? area.Name
				: _campaign.Level?.Name ?? string.Empty;
	}

	public override void _Notification(int what)
	{
		// The window closed with a game running: that is where Continue should pick up.
		if (what == NotificationWMCloseRequest && _menu is null)
		{
			Autosave();
		}
	}

	private static string When(long savedAt)
	{
		var at = DateTimeOffset.FromUnixTimeSeconds(savedAt).ToLocalTime();
		var today = DateTimeOffset.Now.Date;
		return at.Date == today ? $"today, {at:HH:mm}"
			: at.Date == today.AddDays(-1) ? $"yesterday, {at:HH:mm}"
			: $"{at:d MMM}, {at:HH:mm}";
	}

	private static string KindWords(SaveCard card) => card.Kind switch
	{
		SaveKind.Quick => "Quicksave",
		SaveKind.Auto => "Autosave",
		_ => "Saved",
	};

	// ---- in the game: F5 and F9 ----

	private void OnSave()
	{
		if (SaveAs(SaveKind.Quick))
		{
			LogText("— quicksaved —\n");
		}
	}

	private void OnLoad() => ShowSaves();

	/// <summary>Loads a save over whatever is running. False, and the running game untouched, if it cannot be read.</summary>
	private bool LoadSave(SaveCard card)
	{
		if (ReadText(card.Path) is not { Length: > 0 } json)
		{
			return false;
		}

		Campaign restored;
		try
		{
			restored = Campaign.FromJson(json, _content);
		}
		catch (System.IO.InvalidDataException problem)
		{
			// A save written by an older build, or one naming content that has since been renamed.
			// Refusing it loudly is deliberate; taking the running game down with it is not.
			GD.PushError($"Could not load {card.Path}: {problem.Message}");
			Refuse($"That save cannot be read: {problem.Message}");
			return false;
		}

		if (_menu is not null || _campaign is null)
		{
			ClearMenu();
			StartCampaign(restored);
			return true;
		}

		_campaign = restored;
		Begin(_campaign.Battle);

		// Whatever was walking, picked or being read belongs to the run being replaced.
		_walkers.Clear();
		_selection.Clear();
		_held = false;
		_page?.QueueFree();
		_page = null;
		_invWho = null;
		_picked = null;

		// The bodies were built for the fight that is being replaced, so they go with it.
		RebuildWorld();

		_log.Clear();
		LogText("— loaded —\n");
		if (!Exploring)
		{
			ReportInitiative();
		}

		StartNextTurn();
		return true;
	}

	/// <summary>
	/// The saves, laid over the board on a page: a new save at the top, then every save there is to
	/// load, newest first.
	/// </summary>
	private void ShowSaves()
	{
		_page?.QueueFree();
		_page = new CanvasLayer { Layer = 20 };
		AddChild(_page);

		var root = new Control { MouseFilter = Control.MouseFilterEnum.Stop };
		root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		_page.AddChild(root);
		root.AddChild(new ColorRect { Color = new Color(0, 0, 0, 0.55f), AnchorRight = 1, AnchorBottom = 1, MouseFilter = Control.MouseFilterEnum.Ignore });

		var sheet = new PanelContainer { CustomMinimumSize = new Vector2(860, 0) };
		sheet.AddThemeStyleboxOverride("panel", ParchmentPlate(48, 36));
		var centre = new CenterContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
		centre.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		root.AddChild(centre);
		centre.AddChild(sheet);

		var column = new VBoxContainer();
		column.AddThemeConstantOverride("separation", 10);
		sheet.AddChild(column);
		column.AddChild(Illuminated("Saved games", 44));
		column.AddChild(Rule(760));

		void Close()
		{
			_page?.QueueFree();
			_page = null;
		}

		var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
		buttons.AddThemeConstantOverride("separation", 12);

		var fresh = PlateButton("Save new");
		fresh.Disabled = _campaign is null;
		fresh.Pressed += () =>
		{
			if (SaveAs(SaveKind.Manual))
			{
				LogText("— saved —\n");
			}

			ShowSaves();
		};
		buttons.AddChild(fresh);

		var back = PlateButton("Close");
		back.Pressed += Close;
		buttons.AddChild(back);

		column.AddChild(SaveList(560, card =>
		{
			Close();
			LoadSave(card);
		}));
		column.AddChild(buttons);
	}

	/// <summary>
	/// The saves as a scrolling list of lines in ink, each one loaded by a click: what and where,
	/// then when and who.
	/// </summary>
	private ScrollContainer SaveList(float height, Action<SaveCard> load)
	{
		var scroll = new ScrollContainer
		{
			CustomMinimumSize = new Vector2(0, height),
			HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
		};
		var list = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		list.AddThemeConstantOverride("separation", 4);
		scroll.AddChild(list);

		var cards = Saves();
		if (cards.Count == 0)
		{
			list.AddChild(Words("Nothing saved yet.", BodyItalic, 22, PageInk));
		}

		foreach (var card in cards)
		{
			var line = new Button
			{
				Flat = true,
				FocusMode = Control.FocusModeEnum.None,
				CustomMinimumSize = new Vector2(0, 70),
				SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
				MouseDefaultCursorShape = Control.CursorShape.PointingHand,
			};
			foreach (var state in new[] { "normal", "pressed", "focus", "hover_pressed", "disabled" })
			{
				line.AddThemeStyleboxOverride(state, new StyleBoxEmpty());
			}

			line.AddThemeStyleboxOverride("hover", Plate(new Color(0.55f, 0.42f, 0.30f, 0.18f), new Color(0, 0, 0, 0), 0, 3, 6));

			var words = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
			words.AddThemeConstantOverride("separation", 0);
			words.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect, Control.LayoutPresetMode.KeepSize, 8);
			var title = Words(card.Place.Length > 0 ? $"{card.Campaign}  —  {card.Place}" : card.Campaign, Display, 24, PageInk);
			title.MouseFilter = Control.MouseFilterEnum.Ignore;
			words.AddChild(title);
			var under = Words(
				string.Join("   ·   ", new[] { KindWords(card), When(card.SavedAt), card.Party }.Where(part => part.Length > 0)),
				Body, 16, PageInk.Lightened(0.25f));
			under.MouseFilter = Control.MouseFilterEnum.Ignore;
			words.AddChild(under);
			line.AddChild(words);

			line.Pressed += () => load(card);
			list.AddChild(line);
		}

		return scroll;
	}
}
