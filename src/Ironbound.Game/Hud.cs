using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Ironbound.Rules.Combat;
using Ironbound.Rules.Creatures;

/// <summary>
/// The part of <see cref="Main"/> that is the frame round the board: who is here, whose turn it
/// is, what they can do, and what just happened.
/// </summary>
/// <remarks>
/// It used to be three slabs — a bar across the top, a column down the right, a block along the
/// bottom — that between them covered half the screen and said everything in text. This is the
/// other arrangement: the board gets the whole window, and the interface sits in its corners.
/// Party portraits top-left, the campaign and the turn order top-right, the acting character and
/// their actions bottom-centre, the log bottom-left, the system buttons bottom-right.
/// <para>
/// Every control the rest of <c>Main</c> drives is still the same field it always was. Only
/// where they live and what they look like changed, which is why the turn logic, the animation
/// queue and the per-character button rules came through without being touched.
/// </para>
/// </remarks>
public partial class Main
{
	private static readonly Color Ink = new(0.055f, 0.045f, 0.040f, 0.90f);
	private static readonly Color InkSoft = new(0.055f, 0.045f, 0.040f, 0.72f);
	private static readonly Color Bronze = new(0.62f, 0.45f, 0.22f);
	private static readonly Color BronzeBright = new(0.95f, 0.76f, 0.38f);
	private static readonly Color Parchment = new(0.90f, 0.85f, 0.74f);
	private static readonly Color Blood = new(0.70f, 0.13f, 0.11f);
	private static readonly Color FoeBlood = new(0.78f, 0.36f, 0.16f);

	private enum Glyph
	{
		Move, Attack, Full, Trip, Shove, Help, Cast,
		Stand, EndTurn, PowerAttack, Expertise, Defend,
		Log, Sheet, Save, Load,
	}

	/// <summary>One party member's frame: a face, a name, a bar, and what is wrong with them.</summary>
	private sealed class Frame
	{
		public PanelContainer Panel;
		public Label Name;
		public ProgressBar Health;
		public Label Numbers;
		public Label Trouble;
		public TextureRect Face;
	}

	/// <summary>Which buttons the bar offers whoever it is showing.</summary>
	private sealed record Offer(bool Hotbar, bool Cast, IReadOnlyList<Stance> Stances, bool Stand, bool Help);

	private void ShowOffer(Offer offer)
	{
		_hotbar.Visible = offer.Hotbar;
		_modes[Mode.Cast].Visible = offer.Cast;
		_spells.Visible = offer.Cast;
		_modes[Mode.Help].Visible = offer.Help;
		_stand.Visible = offer.Stand;

		// Left holding a mode that has just vanished — the friend was stabilised, say — is a
		// click that does nothing. Back to Move, which is always there.
		if (!_modes[_mode].Visible)
		{
			ChooseMode(Mode.Move);
		}

		foreach (var (stance, button) in _stances)
		{
			button.Visible = offer.Stances.Contains(stance);
		}
	}

	/// <summary>What a frame should say, written down when the rules say it and shown later.</summary>
	private readonly record struct Vitals(
		string Name, int Current, int Maximum, string Trouble, bool Acting, bool Conscious, bool Party);

	private readonly Dictionary<Creature, Frame> _frames = new();
	private readonly Dictionary<Creature, SubViewport> _faces = new();
	private readonly Dictionary<Key, Action> _hotkeys = new();

	private VBoxContainer _partyColumn;
	private Control _hotbar;
	private TextureRect _actorFace;
	private SubViewport _actorLens;
	private Creature _actorShown;
	private Label _actorName;
	private ProgressBar _actorHealth;
	private Label _actorNumbers;
	private readonly List<Panel> _pips = new();

	// ---- the look ----

	private static StyleBoxFlat Plate(Color fill, Color border, int width = 2, int radius = 6, int pad = 8)
	{
		var box = new StyleBoxFlat
		{
			BgColor = fill,
			BorderColor = border,
			ShadowColor = new Color(0, 0, 0, 0.45f),
			ShadowSize = 6,
		};

		box.SetBorderWidthAll(width);
		box.SetCornerRadiusAll(radius);
		box.SetContentMarginAll(pad);
		return box;
	}

	/// <summary>
	/// One theme for everything, so a button made anywhere looks like it belongs here.
	/// </summary>
	private static Theme HudTheme()
	{
		var theme = new Theme();

		theme.SetStylebox("panel", "PanelContainer", Plate(Ink, Bronze));
		theme.SetStylebox("panel", "Panel", Plate(Ink, Bronze));

		theme.SetStylebox("normal", "Button", Plate(new Color(0.13f, 0.10f, 0.08f, 0.95f), Bronze, 2, 5, 6));
		theme.SetStylebox("hover", "Button", Plate(new Color(0.22f, 0.16f, 0.10f, 0.97f), BronzeBright, 2, 5, 6));
		theme.SetStylebox("pressed", "Button", Plate(new Color(0.36f, 0.22f, 0.08f, 0.98f), BronzeBright, 3, 5, 6));
		theme.SetStylebox("disabled", "Button", Plate(new Color(0.08f, 0.07f, 0.06f, 0.80f), new Color(0.25f, 0.21f, 0.16f), 2, 5, 6));
		theme.SetStylebox("focus", "Button", new StyleBoxEmpty());

		theme.SetColor("font_color", "Button", Parchment);
		theme.SetColor("font_hover_color", "Button", Colors.White);
		theme.SetColor("font_pressed_color", "Button", BronzeBright);
		theme.SetColor("font_disabled_color", "Button", new Color(0.45f, 0.42f, 0.36f));
		theme.SetColor("font_color", "Label", Parchment);
		theme.SetColor("default_color", "RichTextLabel", Parchment);

		foreach (var name in new[] { "normal", "hover", "pressed", "disabled", "focus" })
		{
			theme.SetStylebox(name, "OptionButton", theme.GetStylebox(name, "Button"));
		}

		theme.SetColor("font_color", "OptionButton", Parchment);
		return theme;
	}

	private static ProgressBar Bar(Color fill, float height)
	{
		var bar = new ProgressBar
		{
			ShowPercentage = false,
			MinValue = 0,
			MaxValue = 1,
			Value = 1,
			CustomMinimumSize = new Vector2(0, height),
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
		};

		bar.AddThemeStyleboxOverride("background", Plate(new Color(0.03f, 0.025f, 0.02f, 0.95f), new Color(0.22f, 0.17f, 0.10f), 1, 3, 0));
		bar.AddThemeStyleboxOverride("fill", Plate(fill, fill.Lightened(0.25f), 1, 3, 0));
		return bar;
	}

	// ---- building it ----

	/// <summary>Replaces the roster bar, the log column and the control block.</summary>
	private void BuildHud(CanvasLayer layer)
	{
		var root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, Theme = HudTheme() };
		layer.AddChild(root);
		root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

		BuildPartyColumn(root);
		BuildCorner(root);
		BuildLogPanel(root);
		BuildActionBar(root);
		BuildSystemButtons(root);

		BuildSheet(layer);
		BuildLevelUp(layer);
		_sheetPanel.Theme = root.Theme;
		_levelPanel.Theme = root.Theme;
	}

	/// <summary>
	/// Pins a panel to a corner or an edge and lets it grow away from it.
	/// </summary>
	/// <remarks>
	/// Anchors, not positions. Setting <c>Position</c> on an anchored control fights the layout
	/// and loses — the first version of this HUD ended up stacked in the top-left corner with
	/// the action bar off the bottom of the screen. The grow directions matter as much as the
	/// anchor: these panels are sized by their contents, which arrive after they are pinned.
	/// </remarks>
	private static void Pin(Control control, Control.LayoutPreset corner, int margin = 16)
	{
		control.GrowHorizontal = corner switch
		{
			Control.LayoutPreset.TopRight or Control.LayoutPreset.BottomRight => Control.GrowDirection.Begin,
			Control.LayoutPreset.CenterBottom => Control.GrowDirection.Both,
			_ => Control.GrowDirection.End,
		};

		control.GrowVertical = corner is Control.LayoutPreset.TopLeft or Control.LayoutPreset.TopRight
			? Control.GrowDirection.End
			: Control.GrowDirection.Begin;

		control.SetAnchorsAndOffsetsPreset(corner, Control.LayoutPresetMode.Minsize, margin);
	}

	private void BuildPartyColumn(Control root)
	{
		_partyColumn = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
		_partyColumn.AddThemeConstantOverride("separation", 8);
		root.AddChild(_partyColumn);
		Pin(_partyColumn, Control.LayoutPreset.TopLeft);
	}

	/// <summary>Where the reference has a minimap and a quest list: the campaign, and the turn order.</summary>
	private void BuildCorner(Control root)
	{
		var panel = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
		panel.AddThemeStyleboxOverride("panel", Plate(InkSoft, Bronze));
		root.AddChild(panel);
		panel.CustomMinimumSize = new Vector2(330, 0);
		Pin(panel, Control.LayoutPreset.TopRight);

		var lines = new VBoxContainer();
		panel.AddChild(lines);

		_status = new Label { HorizontalAlignment = HorizontalAlignment.Center };
		_status.AddThemeColorOverride("font_color", BronzeBright);
		lines.AddChild(_status);

		lines.AddChild(new HSeparator());

		_roster = new RichTextLabel
		{
			BbcodeEnabled = true,
			FitContent = true,
			ScrollActive = false,
			MouseFilter = Control.MouseFilterEnum.Ignore,
			CustomMinimumSize = new Vector2(310, 0),
		};

		lines.AddChild(_roster);
	}

	private void BuildLogPanel(Control root)
	{
		_logPanel = new PanelContainer();
		_logPanel.AddThemeStyleboxOverride("panel", Plate(InkSoft, Bronze));
		root.AddChild(_logPanel);
		_logPanel.CustomMinimumSize = new Vector2(470, 270);
		Pin(_logPanel, Control.LayoutPreset.BottomLeft);

		// Lifted clear of the action bar, whose left end reaches under where this would sit.
		_logPanel.OffsetTop -= 118;
		_logPanel.OffsetBottom -= 118;

		_log = new RichTextLabel
		{
			ScrollFollowing = true,
			SizeFlagsVertical = Control.SizeFlags.ExpandFill,
			CustomMinimumSize = new Vector2(450, 250),
		};

		_log.AddThemeFontSizeOverride("normal_font_size", 14);
		_logPanel.AddChild(_log);
	}

	private void BuildActionBar(Control root)
	{
		var column = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
		column.AddThemeConstantOverride("separation", 6);
		root.AddChild(column);
		Pin(column, Control.LayoutPreset.CenterBottom);

		_prompt = new Label { HorizontalAlignment = HorizontalAlignment.Center };
		_prompt.AddThemeColorOverride("font_outline_color", Colors.Black);
		_prompt.AddThemeConstantOverride("outline_size", 6);
		column.AddChild(_prompt);

		var panel = new PanelContainer();
		column.AddChild(panel);

		var across = new HBoxContainer();
		across.AddThemeConstantOverride("separation", 12);
		panel.AddChild(across);

		// Who is acting: a face, a name, and the two things they are spending.
		_actorFace = Face(96);
		across.AddChild(Framed(_actorFace, 96));

		var vitals = new VBoxContainer { CustomMinimumSize = new Vector2(210, 0) };
		vitals.AddThemeConstantOverride("separation", 4);
		across.AddChild(vitals);

		_actorName = new Label();
		_actorName.AddThemeColorOverride("font_color", BronzeBright);
		vitals.AddChild(_actorName);

		_actorHealth = Bar(Blood, 22);
		_actorNumbers = Overlay(_actorHealth);
		vitals.AddChild(_actorHealth);

		// Pathfinder has no mana. What a turn actually spends is a standard, a move and a swift
		// action, so that is what sits where the reference keeps its blue bar.
		var pips = new HBoxContainer();
		pips.AddThemeConstantOverride("separation", 5);
		vitals.AddChild(pips);
		foreach (var name in new[] { "Standard", "Move", "Swift" })
		{
			var pip = new Panel { CustomMinimumSize = new Vector2(66, 18), TooltipText = $"{name} action" };
			var label = new Label { Text = name, HorizontalAlignment = HorizontalAlignment.Center };
			label.AddThemeFontSizeOverride("font_size", 11);
			pip.AddChild(label);
			label.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
			pips.AddChild(pip);
			_pips.Add(pip);
		}

		across.AddChild(new VSeparator());

		// The turn's own buttons. Swapped for the camp row once the fighting stops.
		var bar = new HBoxContainer();
		bar.AddThemeConstantOverride("separation", 6);
		across.AddChild(bar);
		_hotbar = bar;

		var keys = new[] { Key.Key1, Key.Key2, Key.Key3, Key.Key4, Key.Key5, Key.Key6, Key.Key7 };
		var glyphs = new Dictionary<Mode, Glyph>
		{
			[Mode.Move] = Glyph.Move, [Mode.Attack] = Glyph.Attack, [Mode.Full] = Glyph.Full,
			[Mode.Trip] = Glyph.Trip, [Mode.Shove] = Glyph.Shove, [Mode.Help] = Glyph.Help,
			[Mode.Cast] = Glyph.Cast,
		};

		for (var i = 0; i < ModeOrder.Length; i++)
		{
			var mode = ModeOrder[i];
			var button = Hot(glyphs[mode], mode == Mode.Full ? "Full attack" : mode.ToString(), keys[i], toggle: true);
			button.Pressed += () => ChooseMode(mode);
			bar.AddChild(button);
			_modes[mode] = button;
		}

		_spells = new OptionButton { CustomMinimumSize = new Vector2(190, 0), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
		_spells.ItemSelected += _ => RefreshReach();
		bar.AddChild(_spells);

		bar.AddChild(new VSeparator());

		var stanceKeys = new Dictionary<Stance, (Glyph, Key, string)>
		{
			[Stance.PowerAttack] = (Glyph.PowerAttack, Key.Z, "Power Attack"),
			[Stance.CombatExpertise] = (Glyph.Expertise, Key.X, "Combat Expertise"),
			[Stance.FightingDefensively] = (Glyph.Defend, Key.C, "Fight defensively"),
		};

		foreach (var (stance, (glyph, key, name)) in stanceKeys)
		{
			var button = Hot(glyph, name, key, toggle: true);
			button.Pressed += () => OnStance(stance);
			bar.AddChild(button);
			_stances[stance] = button;
		}

		bar.AddChild(new VSeparator());

		_stand = Hot(Glyph.Stand, "Stand up", Key.G, toggle: false);
		_stand.Pressed += OnStandUp;
		bar.AddChild(_stand);

		_endTurn = Hot(Glyph.EndTurn, "End turn", Key.Space, toggle: false);
		_endTurn.Pressed += OnEndTurn;
		bar.AddChild(_endTurn);

		// Camp: what there is to do once nobody is swinging at anybody.
		// Wide enough to sit on one line: a flow container given no width wraps after every
		// control, and the camp row came out as a column four buttons tall.
		_between = new HFlowContainer
		{
			Visible = false,
			SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
			CustomMinimumSize = new Vector2(860, 0),
		};
		across.AddChild(_between);

		_between.AddChild(new Label { Text = "Spoils" });
		_stash = new OptionButton { CustomMinimumSize = new Vector2(240, 0) };
		_between.AddChild(_stash);
		_between.AddChild(new Label { Text = "to" });
		_bearer = new OptionButton { CustomMinimumSize = new Vector2(110, 0) };
		_bearer.ItemSelected += _ => RefreshSheet();
		_between.AddChild(_bearer);

		_give = new Button { Text = "Take" };
		_give.Pressed += OnTake;
		_between.AddChild(_give);

		_levelUp = new Button { Text = "Level up" };
		_levelUp.Pressed += OnLevelUp;
		_between.AddChild(_levelUp);

		_rest = new Button { Text = "Rest" };
		_rest.Pressed += OnRest;
		_between.AddChild(_rest);

		_press = new Button { Text = "Press on", CustomMinimumSize = new Vector2(110, 0) };
		_press.Pressed += OnPressOn;
		_between.AddChild(_press);

		ChooseMode(Mode.Move);
	}

	private void BuildSystemButtons(Control root)
	{
		var row = new HBoxContainer();
		row.AddThemeConstantOverride("separation", 6);
		root.AddChild(row);
		Pin(row, Control.LayoutPreset.BottomRight);

		_showLog = Hot(Glyph.Log, "Log", Key.L, toggle: true);
		_showLog.ButtonPressed = true;
		_showLog.Toggled += _ => RefreshLogPanel();
		row.AddChild(_showLog);

		_showSheet = Hot(Glyph.Sheet, "Character sheet", Key.I, toggle: true);
		_showSheet.Toggled += _ => RefreshSheet();
		row.AddChild(_showSheet);

		var save = Hot(Glyph.Save, "Save", Key.F5, toggle: false);
		save.Pressed += OnSave;
		row.AddChild(save);

		_load = Hot(Glyph.Load, "Load", Key.F9, toggle: false);
		_load.Disabled = !SaveExists();
		_load.Pressed += OnLoad;
		row.AddChild(_load);
	}

	// ---- hot buttons: a drawn glyph, a name, a key ----

	private Button Hot(Glyph glyph, string name, Key key, bool toggle)
	{
		var hint = key == Key.Space ? "Space" : OS.GetKeycodeString(key);
		var button = new Button
		{
			ToggleMode = toggle,
			CustomMinimumSize = new Vector2(64, 64),
			TooltipText = $"{name}  [{hint}]",
			FocusMode = Control.FocusModeEnum.None,
		};

		button.Draw += () => DrawGlyph(button, glyph, hint);

		// Through the button rather than round it, so a hidden or spent action cannot be
		// reached from the keyboard either.
		_hotkeys[key] = () =>
		{
			if (!button.IsVisibleInTree() || button.Disabled)
			{
				return;
			}

			if (toggle)
			{
				button.ButtonPressed = !button.ButtonPressed;
			}

			button.EmitSignal(BaseButton.SignalName.Pressed);
		};

		return button;
	}

	/// <summary>Number keys and the rest. True if the key was one of ours.</summary>
	private bool HudInput(InputEvent @event)
	{
		if (@event is InputEventKey { Pressed: true, Echo: false } key
			&& _hotkeys.TryGetValue(key.Keycode, out var press))
		{
			press();
			return true;
		}

		return false;
	}

	/// <summary>
	/// The icons, drawn rather than loaded: a few strokes each, in the button's own colour.
	/// </summary>
	/// <remarks>
	/// There is no icon art and nothing to license this way. They are deliberately plain — a
	/// sword is two lines and a crossguard — because at sixty-four pixels plain reads, and
	/// because the day real icons exist this is the one method they replace.
	/// </remarks>
	private static void DrawGlyph(Button button, Glyph glyph, string hint)
	{
		var size = button.Size;
		var ink = button.Disabled ? new Color(0.42f, 0.39f, 0.33f) : button.ButtonPressed ? BronzeBright : Parchment;
		var c = new Vector2(size.X / 2f, size.Y / 2f - 5f);
		const float w = 3f;

		Vector2 P(float x, float y) => c + new Vector2(x, y);
		void Line(float x1, float y1, float x2, float y2, float width = w) => button.DrawLine(P(x1, y1), P(x2, y2), ink, width, true);
		void Poly(params float[] xy)
		{
			var points = new Vector2[xy.Length / 2];
			for (var i = 0; i < points.Length; i++)
			{
				points[i] = P(xy[i * 2], xy[(i * 2) + 1]);
			}

			button.DrawColoredPolygon(points, ink);
		}

		void Sword(float x1, float y1, float x2, float y2)
		{
			Line(x1, y1, x2, y2);
			var along = new Vector2(x2 - x1, y2 - y1).Normalized();
			var across = new Vector2(-along.Y, along.X) * 7f;
			var guard = new Vector2(x1, y1) + (along * 9f);
			Line(guard.X - across.X, guard.Y - across.Y, guard.X + across.X, guard.Y + across.Y);
		}

		void Shield(bool solid)
		{
			var outline = new[] { -13f, -15f, 13f, -15f, 13f, 2f, 0f, 17f, -13f, 2f };
			if (solid)
			{
				Poly(outline);
				return;
			}

			for (var i = 0; i < outline.Length; i += 2)
			{
				var j = (i + 2) % outline.Length;
				Line(outline[i], outline[i + 1], outline[j], outline[j + 1]);
			}
		}

		switch (glyph)
		{
			case Glyph.Move:
				Line(-14, 12, -2, 0); Line(-2, 0, -14, -12);
				Line(0, 12, 12, 0); Line(12, 0, 0, -12);
				break;
			case Glyph.Attack:
				Sword(-13, 14, 14, -14);
				break;
			case Glyph.Full:
				Sword(-13, 14, 14, -14); Sword(13, 14, -14, -14);
				break;
			case Glyph.Trip:
				Line(-16, 15, 16, 15);
				Line(-10, -14, 2, 2); Line(2, 2, 12, 12);
				Poly(12, 12, 3, 9, 9, 3);
				break;
			case Glyph.Shove:
				Line(-16, 0, 2, 0); Poly(8, 0, -2, -8, -2, 8);
				Line(13, -14, 13, 14, 5f);
				break;
			case Glyph.Help:
				Line(0, -14, 0, 14, 7f); Line(-14, 0, 14, 0, 7f);
				break;
			case Glyph.Cast:
				Poly(0, -17, 4, -4, 17, 0, 4, 4, 0, 17, -4, 4, -17, 0, -4, -4);
				break;
			case Glyph.Stand:
				Line(-16, 15, 16, 15);
				Line(0, 10, 0, -10); Poly(0, -17, -8, -7, 8, -7);
				break;
			case Glyph.EndTurn:
				Poly(-14, -13, 2, 0, -14, 13); Poly(0, -13, 14, 0, 0, 13);
				Line(16, -13, 16, 13, 4f);
				break;
			case Glyph.PowerAttack:
				Poly(0, -17, 5, -6, 16, -9, 9, 0, 16, 9, 5, 6, 0, 17, -5, 6, -16, 9, -9, 0, -16, -9, -5, -6);
				break;
			case Glyph.Expertise:
				Shield(false); Line(-6, 8, 7, -9);
				break;
			case Glyph.Defend:
				Shield(true);
				break;
			case Glyph.Log:
				Line(-13, -10, 13, -10); Line(-13, -2, 13, -2); Line(-13, 6, 6, 6); Line(-13, 14, 10, 14);
				break;
			case Glyph.Sheet:
				button.DrawCircle(P(0, -8), 7f, ink);
				Poly(-13, 16, -9, 3, 9, 3, 13, 16);
				break;
			case Glyph.Save:
				Line(0, -15, 0, 3); Poly(0, 10, -8, 0, 8, 0);
				Line(-14, 8, -14, 16); Line(-14, 16, 14, 16); Line(14, 16, 14, 8);
				break;
			case Glyph.Load:
				Line(0, 8, 0, -8); Poly(0, -16, -8, -6, 8, -6);
				Line(-14, 8, -14, 16); Line(-14, 16, 14, 16); Line(14, 16, 14, 8);
				break;
		}

		var font = button.GetThemeDefaultFont();
		var width = font.GetStringSize(hint, HorizontalAlignment.Left, -1, 11).X;
		button.DrawString(font, new Vector2((size.X - width) / 2f, size.Y - 6f), hint,
			HorizontalAlignment.Left, -1, 11, button.Disabled ? ink : Bronze.Lightened(0.35f));
	}

	// ---- faces: portraits rendered from the models themselves ----

	private static TextureRect Face(int pixels) => new()
	{
		CustomMinimumSize = new Vector2(pixels, pixels),
		ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
		StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
		MouseFilter = Control.MouseFilterEnum.Ignore,
	};

	private static PanelContainer Framed(Control inside, int pixels)
	{
		var frame = new PanelContainer { CustomMinimumSize = new Vector2(pixels + 8, pixels + 8) };
		frame.AddThemeStyleboxOverride("panel", Plate(new Color(0.10f, 0.085f, 0.075f), BronzeBright, 2, 8, 3));
		frame.AddChild(inside);
		return frame;
	}

	private static Label Overlay(ProgressBar bar)
	{
		var label = new Label { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
		label.AddThemeFontSizeOverride("font_size", 13);
		label.AddThemeColorOverride("font_outline_color", Colors.Black);
		label.AddThemeConstantOverride("outline_size", 4);
		bar.AddChild(label);
		label.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		return label;
	}

	/// <summary>
	/// A small world of its own with one copy of the creature in it and a camera on its face.
	/// </summary>
	/// <remarks>
	/// There is no portrait art, and there does not need to be: every creature already has a
	/// head. This way a goblin, a werewolf and whatever is modelled next all get a portrait the
	/// moment they get a model, it breathes with the same idle clip, and it can never disagree
	/// with the figure on the board about what somebody looks like.
	/// </remarks>
	private SubViewport Lens(Creature creature, int pixels)
	{
		var lens = new SubViewport
		{
			OwnWorld3D = true,
			TransparentBg = true,
			Size = new Vector2I(pixels, pixels),
			RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
			Msaa3D = Viewport.Msaa.Msaa4X,
		};

		AddChild(lens);

		lens.AddChild(new WorldEnvironment
		{
			Environment = new Godot.Environment
			{
				BackgroundMode = Godot.Environment.BGMode.Color,
				BackgroundColor = new Color(0.10f, 0.085f, 0.075f),
				AmbientLightSource = Godot.Environment.AmbientSource.Color,
				AmbientLightColor = new Color(0.75f, 0.68f, 0.58f),
				AmbientLightEnergy = 0.55f,
			},
		});

		var key = new DirectionalLight3D { LightEnergy = 1.25f, LightColor = new Color(1.0f, 0.90f, 0.76f) };
		lens.AddChild(key);
		key.RotationDegrees = new Vector3(-35, 150, 0);

		var body = Model(creature) ?? Placeholder(creature, _battle.SideOf(creature) == Ironbound.Simulation.Side.Party ? PartyColour : FoeColour);
		lens.AddChild(body);
		if (Animations(body) is { } player)
		{
			var clips = player.GetAnimationList();
			var idle = Array.Find(clips, c => c.Contains("idle_combat")) ?? Array.Find(clips, c => c.Contains("idle"));
			if (idle is not null)
			{
				player.GetAnimation(idle).LoopMode = Animation.LoopModeEnum.Linear;
				player.Play(idle);
			}
		}

		// Aim at the head: the named bone if the model has one, the top of the figure if not.
		// Measured from the *pose*, not the rest pose — the goblins stoop and the werewolf
		// carries his head a foot forward of where his skeleton was built — and re-aimed once
		// the idle clip has had a frame to take hold.
		var tall = CapsuleHeight(creature);
		var camera = new Camera3D { Fov = 30f, Current = true };
		lens.AddChild(camera);

		void Aim()
		{
			if (!IsInstanceValid(camera) || !IsInstanceValid(body))
			{
				return;
			}

			var head = new Vector3(0, tall * 0.88f, 0);
			var size = tall * 0.20f;

			foreach (var node in body.FindChildren("*", "Skeleton3D", true, false))
			{
				if (node is Skeleton3D skeleton && skeleton.FindBone("head") is var bone and >= 0)
				{
					// The bone's origin is the top of the neck; the face is above and ahead of it.
					var pose = skeleton.GlobalTransform * skeleton.GetBoneGlobalPose(bone);
					head = pose.Origin + new Vector3(0, tall * 0.075f, tall * 0.03f);
					break;
				}
			}

			// Models face +Z, so the camera stands in front on that side: close, a little to
			// one side, level with the eyes. A face, not a passport photograph.
			camera.Position = head + new Vector3(size * 0.75f, 0f, size * 2.7f);
			camera.LookAt(head);
		}

		Aim();
		GetTree().CreateTimer(0.25).Timeout += Aim;

		return lens;
	}

	/// <summary>Builds the party's frames for whoever is in this fight. Called by RebuildWorld.</summary>
	private void RebuildFrames()
	{
		foreach (var lens in _faces.Values)
		{
			lens.QueueFree();
		}

		_faces.Clear();
		_frames.Clear();
		_actorShown = null;
		_actorLens?.QueueFree();
		_actorLens = null;

		if (_partyColumn is null || _instant)
		{
			return;
		}

		foreach (var child in _partyColumn.GetChildren())
		{
			child.QueueFree();
		}

		foreach (var creature in _battle.Party)
		{
			var frame = new Frame { Panel = new PanelContainer() };
			_partyColumn.AddChild(frame.Panel);

			var across = new HBoxContainer();
			across.AddThemeConstantOverride("separation", 8);
			frame.Panel.AddChild(across);

			frame.Face = Face(72);
			across.AddChild(Framed(frame.Face, 72));

			var lens = Lens(creature, 144);
			_faces[creature] = lens;
			frame.Face.Texture = lens.GetTexture();

			var lines = new VBoxContainer { CustomMinimumSize = new Vector2(150, 0) };
			lines.AddThemeConstantOverride("separation", 3);
			across.AddChild(lines);

			frame.Name = new Label { Text = creature.Name };
			lines.AddChild(frame.Name);

			frame.Health = Bar(Blood, 18);
			frame.Numbers = Overlay(frame.Health);
			lines.AddChild(frame.Health);

			frame.Trouble = new Label();
			frame.Trouble.AddThemeFontSizeOverride("font_size", 12);
			frame.Trouble.AddThemeColorOverride("font_color", new Color(0.95f, 0.62f, 0.35f));
			lines.AddChild(frame.Trouble);

			// A click on a face is "tell me about them": it picks them and opens their sheet.
			var who = creature;
			frame.Panel.GuiInput += input =>
			{
				if (input is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
				{
					var index = _campaign.Party.ToList().IndexOf(who);
					if (index >= 0 && index < _bearer.ItemCount)
					{
						_bearer.Selected = index;
					}

					_showSheet.ButtonPressed = true;
					RefreshSheet();
				}
			};

			_frames[creature] = frame;
		}
	}

	// ---- keeping it true ----

	private Vitals VitalsOf(Creature creature, bool acting)
	{
		var trouble = creature.Conditions.Select(condition => condition.ToString()).ToList();
		if (creature.HitPoints.State != HitPointState.Healthy)
		{
			trouble.Insert(0, creature.HitPoints.State.ToString().ToLowerInvariant());
		}

		return new Vitals(
			creature.Name, creature.HitPoints.Current, creature.HitPoints.Maximum,
			string.Join(", ", trouble), acting, creature.IsConscious,
			_battle.SideOf(creature) == Ironbound.Simulation.Side.Party);
	}

	/// <summary>
	/// Shows a snapshot: every frame, the acting character's panel, and the three action pips.
	/// Called from the same place the roster text is, so it rides the animation queue with it.
	/// </summary>
	private void ShowVitals(
		IReadOnlyDictionary<Creature, Vitals> party, Creature actor, Vitals? acting, (bool Standard, bool Move, bool Swift)? budget)
	{
		foreach (var (creature, vitals) in party)
		{
			if (!_frames.TryGetValue(creature, out var frame))
			{
				continue;
			}

			frame.Health.Value = vitals.Maximum > 0 ? Mathf.Clamp((float)vitals.Current / vitals.Maximum, 0f, 1f) : 0f;
			frame.Numbers.Text = $"{vitals.Current} / {vitals.Maximum}";
			frame.Trouble.Text = vitals.Trouble;
			frame.Face.Modulate = vitals.Conscious ? Colors.White : new Color(0.45f, 0.40f, 0.40f);
			frame.Panel.AddThemeStyleboxOverride("panel", Plate(Ink, vitals.Acting ? BronzeBright : Bronze, vitals.Acting ? 3 : 2));
		}

		if (acting is { } now && actor is not null)
		{
			if (!ReferenceEquals(actor, _actorShown))
			{
				_actorLens?.QueueFree();
				_actorLens = Lens(actor, 192);
				_actorFace.Texture = _actorLens.GetTexture();
				_actorShown = actor;
			}

			_actorName.Text = now.Name;
			_actorName.AddThemeColorOverride("font_color", now.Party ? BronzeBright : new Color(1.0f, 0.62f, 0.38f));
			_actorHealth.Value = now.Maximum > 0 ? Mathf.Clamp((float)now.Current / now.Maximum, 0f, 1f) : 0f;
			_actorHealth.AddThemeStyleboxOverride("fill", Plate(now.Party ? Blood : FoeBlood, (now.Party ? Blood : FoeBlood).Lightened(0.25f), 1, 3, 0));
			_actorNumbers.Text = $"{now.Current} / {now.Maximum}";
		}

		var lit = new[] { budget?.Standard ?? false, budget?.Move ?? false, budget?.Swift ?? false };
		for (var i = 0; i < _pips.Count; i++)
		{
			_pips[i].AddThemeStyleboxOverride("panel", lit[i]
				? Plate(new Color(0.16f, 0.30f, 0.52f), new Color(0.45f, 0.68f, 0.95f), 1, 4, 0)
				: Plate(new Color(0.05f, 0.05f, 0.06f), new Color(0.20f, 0.20f, 0.22f), 1, 4, 0));
			_pips[i].Modulate = lit[i] ? Colors.White : new Color(1, 1, 1, 0.55f);
		}
	}
}
