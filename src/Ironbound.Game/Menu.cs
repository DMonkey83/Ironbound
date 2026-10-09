using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Ironbound.Simulation;

/// <summary>
/// The part of <see cref="Main"/> that is the front door: the title screen and the choice of
/// adventure.
/// </summary>
/// <remarks>
/// Laid out after Pathfinder: Wrath of the Righteous, which the project owner pointed at as the
/// model, and which the shape of this game already resembles. A full-screen scene with the title
/// over it; the choices in one column down the left, in large engraved serif, quiet until the
/// pointer finds them; and "New Game" opening not a form but a row of illustrated adventures,
/// each with what it is and who goes into it, and one button to begin. The art is our own —
/// rendered from the game's models by <c>tools/render_keyart.py</c> — and so is the typeface's
/// licence; only the arrangement is borrowed.
/// <para>
/// Runs that drive the game from the command line skip all of it: <c>--autoplay</c>,
/// <c>--camera-tour</c>, <c>--pass-turns</c>, the headless check, and <c>--campaign id</c>, which
/// also chooses the adventure. A recording or a test should not have to click a menu.
/// </para>
/// </remarks>
public partial class Main
{
	private const string TitleArt = "res://art/menu/title.png";

	private static Font _display;
	private static Font _displayBold;
	private static Font _body;
	private static Font _bodyItalic;

	private CanvasLayer _menu;

	/// <summary>The engraved display face, for titles and the menu itself.</summary>
	private static Font Display => _display ??= Face("NotoSerifDisplay-SemiBold.ttf");

	private static Font DisplayBold => _displayBold ??= Face("NotoSerifDisplay-Bold.ttf");

	/// <summary>The book face, for anything somebody reads more than three words of.</summary>
	private static Font Body => _body ??= Face("NotoSerif-Regular.ttf");

	private static Font BodyItalic => _bodyItalic ??= Face("NotoSerif-Italic.ttf");

	private static Font Face(string file) =>
		ResourceLoader.Exists($"res://fonts/{file}") ? GD.Load<Font>($"res://fonts/{file}") : ThemeDB.FallbackFont;

	/// <summary>Whether this run goes straight into a fight, and which.</summary>
	private static bool SkipMenu(out string campaign)
	{
		var args = OS.GetCmdlineUserArgs();
		var at = Array.IndexOf(args, "--campaign");
		campaign = at >= 0 && at + 1 < args.Length ? args[at + 1] : CampaignId;

		return at >= 0
			|| DisplayServer.GetName() == "headless"
			|| args.Contains("--autoplay")
			|| args.Contains("--camera-tour")
			|| args.Contains("--pass-turns");
	}

	// ---- the title screen ----

	private void ShowTitle()
	{
		ClearMenu();
		var desk = DeskStage(MenuRoot(null), TitleArt);

		// The menu, written on the book's page. The page's rectangle on the picture is fixed by
		// DESK_PAGE in tools/render_keyart.py: 1230..1860 across, 40..1030 down, the gutter in
		// shadow on its left.
		var column = new VBoxContainer { Position = new Vector2(1350, 230) };
		column.AddThemeConstantOverride("separation", 18);
		desk.AddChild(column);

		var saved = SaveExists();
		column.AddChild(InkButton("Continue", saved, ContinueSaved));
		column.AddChild(InkButton("New Game", true, ShowAdventures));
		column.AddChild(InkButton("Load Game", saved, ContinueSaved));
		column.AddChild(InkButton("Quit", true, () => GetTree().Quit()));

		// The title, on the note lying bottom left (DESK_NOTE: centred on 420,815, 660 by 450,
		// turned three degrees).
		var note = new VBoxContainer
		{
			Position = new Vector2(420 - 290, 815 - 180),
			Size = new Vector2(580, 360),
			PivotOffset = new Vector2(290, 180),
			Rotation = Mathf.DegToRad(-3),
		};
		note.AddThemeConstantOverride("separation", 6);
		desk.AddChild(note);
		note.AddChild(Illuminated("Ironbound", 72));
		note.AddChild(Rule(520));
		var said = new Label
		{
			Text = "Tactics by the rules, five feet at a time.\n\nTwo adventures wait: goblins and worse on the long road, and the old carved caves where an orc warband and the ogre who leads it have made their lair.",
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
			CustomMinimumSize = new Vector2(540, 0),
		};
		said.AddThemeFontOverride("font", BodyItalic);
		said.AddThemeFontSizeOverride("font_size", 22);
		said.AddThemeColorOverride("font_color", PageInk);
		note.AddChild(said);

		var version = Words("early build", Body, 15, PageInk.Lightened(0.25f));
		version.Position = new Vector2(820, 1000);
		version.Rotation = Mathf.DegToRad(14);
		desk.AddChild(version);
	}

	/// <summary>
	/// The desk picture at its own size, 1920 by 1080, centred in the window; anything written on
	/// it is placed in the picture's pixels, so it stays on the paper whatever the window's shape.
	/// </summary>
	private static Control DeskStage(Control root, string art)
	{
		var stage = new Control
		{
			AnchorLeft = 0.5f,
			AnchorRight = 0.5f,
			AnchorTop = 0.5f,
			AnchorBottom = 0.5f,
			OffsetLeft = -960,
			OffsetRight = 960,
			OffsetTop = -540,
			OffsetBottom = 540,
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		root.AddChild(stage);
		if (ResourceLoader.Exists(art))
		{
			var picture = new TextureRect
			{
				Texture = GD.Load<Texture2D>(art),
				ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
				StretchMode = TextureRect.StretchModeEnum.Scale,
				MouseFilter = Control.MouseFilterEnum.Ignore,
			};
			picture.SetAnchorsPreset(Control.LayoutPreset.FullRect);
			stage.AddChild(picture);
		}

		return stage;
	}

	// ---- choosing an adventure ----

	private void ShowAdventures()
	{
		ClearMenu();
		var root = MenuRoot(TitleArt);
		root.AddChild(Shade(new Color(0.02f, 0.015f, 0.01f, 0.86f), 0.0f, 2.0f));

		var page = new VBoxContainer();
		page.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		page.OffsetLeft = 90;
		page.OffsetRight = -90;
		page.OffsetTop = 70;
		page.OffsetBottom = -60;
		page.AddThemeConstantOverride("separation", 18);
		root.AddChild(page);

		var heading = Words("Choose Your Adventure", DisplayBold, 58, BronzeBright);
		heading.AddThemeConstantOverride("outline_size", 6);
		heading.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.8f));
		page.AddChild(heading);
		page.AddChild(Rule(720));

		var row = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill, Alignment = BoxContainer.AlignmentMode.Center };
		row.AddThemeConstantOverride("separation", 40);
		page.AddChild(row);

		var footer = new HBoxContainer();
		footer.AddThemeConstantOverride("separation", 24);
		page.AddChild(footer);

		string chosen = null;
		var cards = new List<(string Id, PanelContainer Card)>();
		var begin = MenuButton("Begin Adventure", false, () => BeginAdventure(chosen));

		void Choose(string id)
		{
			chosen = id;
			foreach (var (cardId, card) in cards)
			{
				card.AddThemeStyleboxOverride("panel", CardPlate(cardId == id));
			}

			begin.Disabled = false;
		}

		foreach (var campaign in _content.Campaigns)
		{
			var card = AdventureCard(campaign.Id, campaign.Name, campaign.Description, Chapters(campaign), () => Choose(campaign.Id));
			cards.Add((campaign.Id, card));
			row.AddChild(card);
		}

		footer.AddChild(MenuButton("Back", true, ShowTitle));
		footer.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
		footer.AddChild(begin);

		if (cards.Count > 0)
		{
			Choose(cards[0].Id);
		}
	}

	private string Chapters(Ironbound.Rules.Content.CampaignDefinition campaign)
	{
		var party = campaign.Encounters.Count > 0 && _content.GetEncounter(campaign.Encounters[0]) is { } opening
			? string.Join(", ", opening.Placements.Where(one => one.Party).Select(one => _content.GetCreature(one.CreatureId)?.Name ?? one.CreatureId))
			: string.Empty;
		var chapters = campaign.Encounters.Count == 1 ? "1 chapter" : $"{campaign.Encounters.Count} chapters";

		// A level is walked, not chaptered: say how many places in it are held against you.
		if (campaign.Level is { } id && _content.GetLevel(id) is { } level)
		{
			var held = level.Areas.Count(area => area.Foes.Count > 0);
			chapters = held == 1 ? "1 guarded place" : $"{held} guarded places";
			party = string.Join(", ", level.Start.Select(one => _content.GetCreature(one.CreatureId)?.Name ?? one.CreatureId));
		}

		var rests = campaign.Rests == 1 ? "1 rest" : $"{campaign.Rests} rests";
		return party.Length == 0 ? $"{chapters} · {rests}" : $"{chapters} · {rests}\n{party}";
	}

	private PanelContainer AdventureCard(string id, string name, string description, string facts, Action pick)
	{
		var card = new PanelContainer { CustomMinimumSize = new Vector2(560, 0), MouseFilter = Control.MouseFilterEnum.Stop, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
		card.AddThemeStyleboxOverride("panel", CardPlate(false));

		var inside = new VBoxContainer();
		inside.AddThemeConstantOverride("separation", 12);
		card.AddChild(inside);

		var art = $"res://art/menu/{id}.png";
		if (ResourceLoader.Exists(art))
		{
			inside.AddChild(new TextureRect
			{
				Texture = GD.Load<Texture2D>(art),
				CustomMinimumSize = new Vector2(540, 304),
				ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
				StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
				MouseFilter = Control.MouseFilterEnum.Ignore,
			});
		}

		var title = Words(name, DisplayBold, 34, BronzeBright);
		title.MouseFilter = Control.MouseFilterEnum.Ignore;
		inside.AddChild(title);

		var said = Words(description.Length > 0 ? description : "An adventure.", Body, 17, Parchment);
		said.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		said.CustomMinimumSize = new Vector2(540, 0);
		said.MouseFilter = Control.MouseFilterEnum.Ignore;
		inside.AddChild(said);

		var small = Words(facts, BodyItalic, 15, Parchment.Darkened(0.25f));
		small.MouseFilter = Control.MouseFilterEnum.Ignore;
		inside.AddChild(small);

		card.GuiInput += input =>
		{
			if (input is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } click)
			{
				pick();
				if (click.DoubleClick)
				{
					BeginAdventure(id);
				}
			}
		};

		return card;
	}

	private static StyleBoxFlat CardPlate(bool chosen)
	{
		var plate = Plate(new Color(0.05f, 0.04f, 0.03f, chosen ? 0.96f : 0.82f), chosen ? BronzeBright : Bronze.Darkened(0.35f), chosen ? 3 : 1, 4, 10);
		if (chosen)
		{
			plate.ShadowColor = new Color(BronzeBright, 0.35f);
			plate.ShadowSize = 14;
		}

		return plate;
	}

	// ---- leaving the menu ----

	private void BeginAdventure(string campaignId)
	{
		if (campaignId is null)
		{
			return;
		}

		ClearMenu();
		StartCampaign(Campaign.Begin(_content, campaignId));
	}

	private void ContinueSaved()
	{
		if (!SaveExists() || Read() is not { Length: > 0 } json)
		{
			return;
		}

		try
		{
			var restored = Campaign.FromJson(json, _content);
			ClearMenu();
			StartCampaign(restored);
		}
		catch (System.IO.InvalidDataException problem)
		{
			// An old save, or one naming content that has been renamed since. The menu stays
			// up; a broken save is not a reason to lose the front door.
			GD.PushError($"Could not load {SavePath}: {problem.Message}");
		}
	}

	private void ClearMenu()
	{
		_menu?.QueueFree();
		_menu = null;
	}

	// ---- parts ----

	private Control MenuRoot(string art)
	{
		_menu = new CanvasLayer { Layer = 10 };
		AddChild(_menu);

		var root = new Control();
		root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		_menu.AddChild(root);

		root.AddChild(new ColorRect { Color = new Color(0.06f, 0.05f, 0.05f), AnchorRight = 1, AnchorBottom = 1 });
		if (art != null && ResourceLoader.Exists(art))
		{
			var picture = new TextureRect
			{
				Texture = GD.Load<Texture2D>(art),
				ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
				StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
				MouseFilter = Control.MouseFilterEnum.Ignore,
			};
			picture.SetAnchorsPreset(Control.LayoutPreset.FullRect);
			root.AddChild(picture);
		}

		return root;
	}

	/// <summary>A darkening from the left edge, solid to <paramref name="solid"/> of the width and
	/// gone by <paramref name="clear"/>.</summary>
	private static TextureRect Shade(Color colour, float solid, float clear)
	{
		var ramp = new Gradient();
		ramp.SetColor(0, colour);
		ramp.SetColor(1, new Color(colour, 0));
		ramp.SetOffset(0, solid);
		ramp.SetOffset(1, Mathf.Min(clear, 1f));
		if (clear > 1f)
		{
			ramp.SetColor(1, new Color(colour, colour.A * 0.85f));
		}

		var shade = new TextureRect
		{
			Texture = new GradientTexture2D { Gradient = ramp, Width = 256, Height = 4 },
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			StretchMode = TextureRect.StretchModeEnum.Scale,
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		shade.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		return shade;
	}

	private static Label Words(string text, Font font, int size, Color colour)
	{
		var label = new Label { Text = text };
		label.AddThemeFontOverride("font", font);
		label.AddThemeFontSizeOverride("font_size", size);
		label.AddThemeColorOverride("font_color", colour);
		return label;
	}

	/// <summary>A thin bronze rule with a lozenge in the middle, under a heading.</summary>
	private static Control Rule(float width)
	{
		var rule = new Control { CustomMinimumSize = new Vector2(width, 22) };
		rule.Draw += () =>
		{
			var y = 11f;
			rule.DrawLine(new Vector2(0, y), new Vector2(width * 0.47f, y), Bronze, 1.5f, true);
			rule.DrawLine(new Vector2(width * 0.53f, y), new Vector2(width, y), Bronze, 1.5f, true);
			var c = new Vector2(width * 0.5f, y);
			rule.DrawColoredPolygon([c + new Vector2(0, -6), c + new Vector2(9, 0), c + new Vector2(0, 6), c + new Vector2(-9, 0)], BronzeBright);
		};
		return rule;
	}

	/// <summary>
	/// One of the menu's words: quiet parchment until the pointer finds it, then gold with a
	/// lozenge beside it. No box, no fill — the words are the buttons, as they are in Wrath.
	/// </summary>
	private static Button MenuButton(string text, bool enabled, Action pressed)
	{
		var button = new Button
		{
			Text = text,
			Flat = true,
			Disabled = !enabled,
			Alignment = HorizontalAlignment.Left,
			FocusMode = Control.FocusModeEnum.None,
			CustomMinimumSize = new Vector2(380, 56),
		};
		button.AddThemeFontOverride("font", Display);
		button.AddThemeFontSizeOverride("font_size", 40);
		button.AddThemeColorOverride("font_color", Parchment);
		button.AddThemeColorOverride("font_hover_color", BronzeBright);
		button.AddThemeColorOverride("font_pressed_color", Colors.White);
		button.AddThemeColorOverride("font_disabled_color", new Color(Parchment, 0.28f));
		button.AddThemeConstantOverride("outline_size", 4);
		button.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.7f));
		foreach (var state in new[] { "normal", "hover", "pressed", "disabled", "focus", "hover_pressed" })
		{
			button.AddThemeStyleboxOverride(state, new StyleBoxEmpty { ContentMarginLeft = 34 });
		}

		button.Draw += () =>
		{
			if (button.IsHovered() && !button.Disabled)
			{
				var c = new Vector2(14, button.Size.Y * 0.5f);
				button.DrawColoredPolygon([c + new Vector2(0, -7), c + new Vector2(8, 0), c + new Vector2(0, 7), c + new Vector2(-8, 0)], BronzeBright);
			}
		};
		button.MouseEntered += button.QueueRedraw;
		button.MouseExited += button.QueueRedraw;
		button.Pressed += pressed;
		return button;
	}

	/// <summary>
	/// A menu word written on paper, as Wrath writes its main menu in the book: dark ink, the
	/// first letter large and red. Pointing at it slides it along and puts a red lozenge before
	/// it; a word that cannot be chosen yet is faded out.
	/// </summary>
	private static Button InkButton(string text, bool enabled, Action pressed)
	{
		var button = new Button
		{
			Flat = true,
			Disabled = !enabled,
			FocusMode = Control.FocusModeEnum.None,
			CustomMinimumSize = new Vector2(460, 86),
			MouseDefaultCursorShape = enabled ? Control.CursorShape.PointingHand : Control.CursorShape.Arrow,
		};
		foreach (var state in new[] { "normal", "hover", "pressed", "disabled", "focus", "hover_pressed" })
		{
			button.AddThemeStyleboxOverride(state, new StyleBoxEmpty());
		}

		var words = Illuminated(text, 52);
		words.MouseFilter = Control.MouseFilterEnum.Ignore;
		words.Position = new Vector2(30, 0);
		words.Modulate = enabled ? Colors.White : new Color(1, 1, 1, 0.32f);
		button.AddChild(words);

		void Lit(bool on)
		{
			words.Position = new Vector2(on ? 42 : 30, 0);
			button.QueueRedraw();
		}

		button.MouseEntered += () => Lit(!button.Disabled);
		button.MouseExited += () => Lit(false);
		button.Draw += () =>
		{
			if (button.IsHovered() && !button.Disabled)
			{
				var c = new Vector2(12, button.Size.Y * 0.58f);
				button.DrawColoredPolygon([c + new Vector2(0, -8), c + new Vector2(8, 0), c + new Vector2(0, 8), c + new Vector2(-8, 0)], InkRed);
			}
		};
		button.Pressed += pressed;
		return button;
	}
}
