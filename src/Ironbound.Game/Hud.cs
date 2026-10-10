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
/// Laid out after Pathfinder: Wrath of the Righteous, as the project owner asked: the board gets
/// the whole window and the interface gathers along its foot. In the middle, a strip of parchment
/// in an iron frame holding the acting character and what they can do, and under it the party's
/// portraits in a row; the log on parchment bottom-right; the system buttons in an iron block
/// bottom-left; the campaign and the turn order top-right.
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
	private static readonly Color Iron = new(0.075f, 0.065f, 0.080f, 0.95f);
	private static readonly Color IronSoft = new(0.075f, 0.065f, 0.080f, 0.80f);
	private static readonly Color IronEdge = new(0.38f, 0.31f, 0.26f);

	private enum Glyph
	{
		Move, Attack, Full, Trip, Shove, Help, Cast,
		Stand, EndTurn, PowerAttack, Expertise, Defend,
		Rage, PowerfulBlow, SurpriseAccuracy, StrengthSurge,
		Demoralize, DeadlyAim, Items,
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
	private sealed record Offer(bool Hotbar, bool Cast, IReadOnlyList<Stance> Stances, bool Stand, bool Help, bool Rage, bool Items = false);

	private void ShowOffer(Offer offer)
	{
		_hotbar.Visible = offer.Hotbar;
		_modes[Mode.Cast].Visible = offer.Cast;
		_spells.Visible = offer.Cast;
		_modes[Mode.Help].Visible = offer.Help;
		_modes[Mode.Items].Visible = offer.Items;
		_items.Visible = offer.Items;
		_stand.Visible = offer.Stand;
		_rage.Visible = offer.Rage;

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

	private HBoxContainer _partyRow;
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

		theme.SetStylebox("panel", "PanelContainer", Plate(Iron, IronEdge, 2, 3));
		theme.SetStylebox("panel", "Panel", Plate(Iron, IronEdge, 2, 3));

		// Buttons are slots ruled on paper, as Wrath's hotbar is: pale, square-cornered, inked.
		// The few that sit on iron instead are restyled by Ironclad.
		theme.SetStylebox("normal", "Button", Slot(new Color(0.84f, 0.78f, 0.66f), new Color(0.50f, 0.40f, 0.30f), 1));
		theme.SetStylebox("hover", "Button", Slot(new Color(0.93f, 0.88f, 0.77f), InkRed, 1));
		theme.SetStylebox("pressed", "Button", Slot(new Color(0.74f, 0.63f, 0.50f), InkRed, 2));
		theme.SetStylebox("disabled", "Button", Slot(new Color(0.78f, 0.74f, 0.66f, 0.55f), new Color(0.60f, 0.55f, 0.48f, 0.6f), 1));
		theme.SetStylebox("focus", "Button", new StyleBoxEmpty());

		theme.SetColor("font_color", "Button", PageInk);
		theme.SetColor("font_hover_color", "Button", InkRed);
		theme.SetColor("font_pressed_color", "Button", InkRed);
		theme.SetColor("font_hover_pressed_color", "Button", InkRed);
		theme.SetColor("font_disabled_color", "Button", new Color(0.50f, 0.45f, 0.42f));
		theme.SetStylebox("separator", "VSeparator", new StyleBoxLine { Color = new Color(0.45f, 0.35f, 0.28f, 0.7f), Thickness = 1, Vertical = true });
		theme.SetColor("font_color", "Label", Parchment);
		theme.SetColor("default_color", "RichTextLabel", Parchment);

		foreach (var name in new[] { "normal", "hover", "pressed", "disabled", "focus" })
		{
			theme.SetStylebox(name, "OptionButton", theme.GetStylebox(name, "Button"));
		}

		theme.SetColor("font_color", "OptionButton", PageInk);
		theme.SetColor("font_hover_color", "OptionButton", InkRed);
		return theme;
	}

	private static StyleBoxFlat Slot(Color fill, Color border, int width)
	{
		var box = Plate(fill, border, width, 2, 6);
		box.ShadowSize = 0;
		return box;
	}

	/// <summary>Restyles a button that sits on the iron rather than on the paper.</summary>
	private static Button Ironclad(Button button)
	{
		button.SetMeta("iron", true);
		button.AddThemeStyleboxOverride("normal", Plate(new Color(0.12f, 0.10f, 0.11f), IronEdge, 2, 3, 6));
		button.AddThemeStyleboxOverride("hover", Plate(new Color(0.20f, 0.16f, 0.17f), BronzeBright, 2, 3, 6));
		button.AddThemeStyleboxOverride("pressed", Plate(new Color(0.30f, 0.20f, 0.16f), BronzeBright, 2, 3, 6));
		button.AddThemeStyleboxOverride("disabled", Plate(new Color(0.08f, 0.07f, 0.07f, 0.8f), new Color(0.25f, 0.21f, 0.18f), 2, 3, 6));

		// Words on iron are pale, as the glyphs are; the paper's dark ink would vanish here.
		button.AddThemeColorOverride("font_color", Parchment);
		button.AddThemeColorOverride("font_hover_color", BronzeBright);
		button.AddThemeColorOverride("font_pressed_color", BronzeBright);
		button.AddThemeColorOverride("font_hover_pressed_color", BronzeBright);
		button.AddThemeColorOverride("font_disabled_color", new Color(0.42f, 0.39f, 0.33f));
		return button;
	}

	/// <summary>Parchment in an iron frame: the hotbar's strip and the log.</summary>
	private static PanelContainer PaperInIron(Control inside, int padX, int padY)
	{
		var iron = new PanelContainer();
		iron.AddThemeStyleboxOverride("panel", Plate(Iron, IronEdge, 2, 3, 3));
		var paper = new PanelContainer();
		paper.AddThemeStyleboxOverride("panel", ParchmentPlate(padX, padY));
		iron.AddChild(paper);
		paper.AddChild(inside);
		return iron;
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

		BuildCorner(root);
		BuildToasts(root);
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
			Control.LayoutPreset.CenterBottom or Control.LayoutPreset.CenterTop => Control.GrowDirection.Both,
			_ => Control.GrowDirection.End,
		};

		control.GrowVertical = corner is Control.LayoutPreset.TopLeft or Control.LayoutPreset.TopRight or Control.LayoutPreset.CenterTop
			? Control.GrowDirection.End
			: Control.GrowDirection.Begin;

		control.SetAnchorsAndOffsetsPreset(corner, Control.LayoutPresetMode.Minsize, margin);
	}

	/// <summary>Where the reference has a minimap and a quest list: the campaign, and the turn order.</summary>
	private void BuildCorner(Control root)
	{
		var panel = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
		panel.AddThemeStyleboxOverride("panel", Plate(IronSoft, IronEdge, 2, 3));
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
		_log = new RichTextLabel
		{
			ScrollFollowing = true,
			SizeFlagsVertical = Control.SizeFlags.ExpandFill,
			CustomMinimumSize = new Vector2(330, 220),
		};

		_log.AddThemeFontOverride("normal_font", Body);
		_log.AddThemeFontSizeOverride("normal_font_size", 14);
		_log.AddThemeColorOverride("default_color", PageInk);
		_logPanel = PaperInIron(_log, 16, 12);
		root.AddChild(_logPanel);
		Pin(_logPanel, Control.LayoutPreset.BottomRight);
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

		var across = new HBoxContainer();
		across.AddThemeConstantOverride("separation", 10);
		column.AddChild(PaperInIron(across, 12, 8));

		// The party, in a row of iron frames under the strip, where Wrath keeps its portraits.
		_partyRow = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore, Alignment = BoxContainer.AlignmentMode.Center };
		_partyRow.AddThemeConstantOverride("separation", 6);
		column.AddChild(_partyRow);

		// Who is acting: a face, a name, and the two things they are spending.
		_actorFace = Face(72);
		across.AddChild(Framed(_actorFace, 72));

		var vitals = new VBoxContainer { CustomMinimumSize = new Vector2(180, 0), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
		vitals.AddThemeConstantOverride("separation", 4);
		across.AddChild(vitals);

		_actorName = new Label();
		_actorName.AddThemeFontOverride("font", Display);
		_actorName.AddThemeFontSizeOverride("font_size", 19);
		_actorName.AddThemeColorOverride("font_color", PageInk);
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
			var pip = new Panel { CustomMinimumSize = new Vector2(56, 18), TooltipText = $"{name} action" };
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

		var keys = new[] { Key.Key1, Key.Key2, Key.Key3, Key.Key4, Key.Key5, Key.Key6, Key.Key7, Key.Key8, Key.Key9 };
		var glyphs = new Dictionary<Mode, Glyph>
		{
			[Mode.Move] = Glyph.Move, [Mode.Attack] = Glyph.Attack, [Mode.Full] = Glyph.Full,
			[Mode.Trip] = Glyph.Trip, [Mode.Shove] = Glyph.Shove, [Mode.Help] = Glyph.Help,
			[Mode.Cast] = Glyph.Cast, [Mode.Demoralize] = Glyph.Demoralize, [Mode.Items] = Glyph.Items,
		};

		for (var i = 0; i < ModeOrder.Length; i++)
		{
			var mode = ModeOrder[i];
			var button = Hot(glyphs[mode], mode == Mode.Full ? "Full attack" : mode.ToString(), keys[i], toggle: true);
			button.Pressed += () => ChooseMode(mode);
			bar.AddChild(button);
			_modes[mode] = button;
		}

		_spells = new OptionButton { CustomMinimumSize = new Vector2(160, 0), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
		_spells.ItemSelected += _ => RefreshReach();
		bar.AddChild(_spells);

		// Only there while it would matter: see RefreshDefensive.
		_castDefensively = new CheckButton { Visible = false, FocusMode = Control.FocusModeEnum.None, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
		bar.AddChild(_castDefensively);

		// What the actor has on the belt to use: potions to drink or give, flasks to throw.
		_items = new OptionButton { CustomMinimumSize = new Vector2(170, 0), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, Visible = false };
		_items.ItemSelected += _ => RefreshReach();
		bar.AddChild(_items);

		bar.AddChild(new VSeparator());

		var stanceKeys = new Dictionary<Stance, (Glyph, Key, string)>
		{
			[Stance.PowerAttack] = (Glyph.PowerAttack, Key.Z, "Power Attack"),
			[Stance.CombatExpertise] = (Glyph.Expertise, Key.X, "Combat Expertise"),
			[Stance.FightingDefensively] = (Glyph.Defend, Key.C, "Fight defensively"),
			[Stance.DeadlyAim] = (Glyph.DeadlyAim, Key.M, "Deadly Aim"),

			// The barbarian's once-a-rage tricks: offered only while raging and unspent.
			[Stance.PowerfulBlow] = (Glyph.PowerfulBlow, Key.V, "Powerful blow (next hit)"),
			[Stance.SurpriseAccuracy] = (Glyph.SurpriseAccuracy, Key.B, "Surprise accuracy (next attack)"),
			[Stance.StrengthSurge] = (Glyph.StrengthSurge, Key.N, "Strength surge (next manoeuvre)"),
		};

		foreach (var (stance, (glyph, key, name)) in stanceKeys)
		{
			var button = Hot(glyph, name, key, toggle: true);
			button.Pressed += () => OnStance(stance);
			bar.AddChild(button);
			_stances[stance] = button;
		}

		bar.AddChild(new VSeparator());

		// Rage is a free action, on and off; lit while it lasts.
		_rage = Hot(Glyph.Rage, "Rage", Key.R, toggle: true);
		_rage.Pressed += OnRage;
		bar.AddChild(_rage);

		_stand = Hot(Glyph.Stand, "Stand up", Key.G, toggle: false);
		_stand.Pressed += OnStandUp;
		bar.AddChild(_stand);

		// Tanglefoot and alchemist's fire each leave something to get out of; offered when they do.
		BuildItemButtons(bar);

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
			CustomMinimumSize = new Vector2(760, 0),
		};
		across.AddChild(_between);

		// Whatever the fallen and the opened chests still hold within reach, in one window.
		_loot = new Button { Text = "Loot", Visible = false };
		_loot.Pressed += OnLootNearby;
		_between.AddChild(_loot);

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
		var block = new PanelContainer();
		block.AddThemeStyleboxOverride("panel", Plate(Iron, IronEdge, 2, 3, 6));
		root.AddChild(block);
		Pin(block, Control.LayoutPreset.BottomLeft);
		var row = new GridContainer { Columns = 2 };
		row.AddThemeConstantOverride("h_separation", 6);
		row.AddThemeConstantOverride("v_separation", 6);
		block.AddChild(row);

		_showLog = Hot(Glyph.Log, "Log", Key.L, toggle: true);
		_showLog.ButtonPressed = true;
		_showLog.Toggled += _ => RefreshLogPanel();
		row.AddChild(Ironclad(_showLog));

		_showSheet = Hot(Glyph.Sheet, "Character: inventory and sheet", Key.I, toggle: true);
		_showSheet.Toggled += _ => RefreshSheet();
		row.AddChild(Ironclad(_showSheet));

		var save = Hot(Glyph.Save, "Save", Key.F5, toggle: false);
		save.Pressed += OnSave;
		row.AddChild(Ironclad(save));

		_load = Hot(Glyph.Load, "Load", Key.F9, toggle: false);
		_load.Disabled = !SaveExists();
		_load.Pressed += OnLoad;
		row.AddChild(Ironclad(_load));
	}

	// ---- hot buttons: a drawn glyph, a name, a key ----

	private Button Hot(Glyph glyph, string name, Key key, bool toggle)
	{
		var hint = key == Key.Space ? "Space" : OS.GetKeycodeString(key);
		var button = new Button
		{
			ToggleMode = toggle,
			CustomMinimumSize = new Vector2(56, 56),
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
		var iron = button.HasMeta("iron");
		var ink = button.Disabled ? (iron ? new Color(0.42f, 0.39f, 0.33f) : new Color(0.55f, 0.50f, 0.46f))
			: button.ButtonPressed ? (iron ? BronzeBright : InkRed)
			: iron ? Parchment : PageInk;
		var c = new Vector2(size.X / 2f, size.Y / 2f - 5f);
		const float w = 3f;

		Vector2 P(float x, float y) => c + (new Vector2(x, y) * 0.85f);
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
			case Glyph.Rage:
				// A mouth open in a roar: jaw, teeth, and the heat coming off it.
				Poly(-14, -4, 14, -4, 9, 12, -9, 12);
				Line(-8, -4, -6, 2, 2f); Line(0, -4, 0, 2, 2f); Line(8, -4, 6, 2, 2f);
				Line(-10, -9, -6, -16); Line(0, -9, 0, -17); Line(10, -9, 6, -16);
				break;
			case Glyph.PowerfulBlow:
				Poly(-4, -16, 6, -16, 6, -2, 14, -2, 14, 8, -4, 8);
				Line(-14, 14, -2, 8, 4f);
				break;
			case Glyph.SurpriseAccuracy:
				button.DrawArc(P(0, 0), 13f, 0, Mathf.Tau, 32, ink, 2.5f, true);
				button.DrawArc(P(0, 0), 6f, 0, Mathf.Tau, 24, ink, 2.5f, true);
				Line(-17, 0, 17, 0, 1.5f); Line(0, -17, 0, 17, 1.5f);
				break;
			case Glyph.StrengthSurge:
				Poly(-10, 16, -10, -2, -4, -10, 4, -10, 10, -2, 10, 16);
				Line(-4, -10, -4, -16); Line(4, -10, 4, -16);
				break;
			case Glyph.Defend:
				Shield(true);
				break;
			case Glyph.Demoralize:
				// A head, shouting: the mouth open and the threat carrying off to the right.
				button.DrawArc(P(-6, 0), 11f, 0, Mathf.Tau, 28, ink, 2.5f, true);
				Poly(-2, 2, 6, -1, 6, 7);
				button.DrawArc(P(6, 2), 9f, -0.7f, 0.7f, 10, ink, 2.5f, true);
				button.DrawArc(P(6, 2), 15f, -0.6f, 0.6f, 12, ink, 2.5f, true);
				break;
			case Glyph.Items:
				// A round-bellied flask with a stopper: what comes off the belt.
				button.DrawCircle(P(0, 6), 11f, ink);
				Poly(-4, -4, 4, -4, 4, -12, -4, -12);
				Poly(-6, -12, 6, -12, 6, -16, -6, -16);
				break;
			case Glyph.DeadlyAim:
				// A drawn bow and its arrow, with the point aimed through a mark.
				button.DrawArc(P(-6, 0), 16f, -1.25f, 1.25f, 20, ink, 3f, true);
				Line(-1, -15, -14, 0, 1.5f); Line(-14, 0, -1, 15, 1.5f);
				Line(-14, 0, 13, 0, 2.5f); Poly(17, 0, 10, -4, 10, 4);
				button.DrawArc(P(14, 0), 6f, 0, Mathf.Tau, 20, ink, 1.5f, true);
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
			HorizontalAlignment.Left, -1, 11, button.Disabled ? ink : iron ? Bronze.Lightened(0.35f) : new Color(0.45f, 0.35f, 0.28f));
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
	/// <param name="whole">
	/// The whole figure rather than the face, holding what it holds: the character window's
	/// centrepiece, where taking a sword off should be seen to take it out of her hand.
	/// </param>
	private SubViewport Lens(Creature creature, int pixels, bool whole = false)
	{
		var lens = new SubViewport
		{
			OwnWorld3D = true,
			TransparentBg = true,
			Size = whole ? new Vector2I(pixels, pixels * 8 / 5) : new Vector2I(pixels, pixels),
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

		if (whole)
		{
			Arm(creature, body);
		}
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

			if (whole)
			{
				// Head to toe with a little floor, from a step to her right and a little above,
				// the way Wrath stands its figure in the inventory.
				var middle = new Vector3(0, tall * 0.5f, 0);
				camera.Position = middle + new Vector3(tall * 0.55f, tall * 0.18f, tall * 2.1f);
				camera.LookAt(middle);
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

		if (_partyRow is null || _instant)
		{
			return;
		}

		foreach (var child in _partyRow.GetChildren())
		{
			child.QueueFree();
		}

		_partyRow.AddChild(SelectAllButton());

		foreach (var creature in _battle.Party)
		{
			// A tall portrait in an iron frame, the name across its foot and anything wrong with
			// them across its head, and the bar under it.
			var frame = new Frame { Panel = new PanelContainer { TooltipText = creature.Name } };
			frame.Panel.AddThemeStyleboxOverride("panel", Plate(Iron, IronEdge, 2, 3, 3));
			_partyRow.AddChild(frame.Panel);

			var stack = new VBoxContainer();
			stack.AddThemeConstantOverride("separation", 3);
			frame.Panel.AddChild(stack);

			var picture = new Control { CustomMinimumSize = new Vector2(100, 116), ClipContents = true, MouseFilter = Control.MouseFilterEnum.Ignore };
			stack.AddChild(picture);

			frame.Face = Face(100);
			picture.AddChild(frame.Face);
			frame.Face.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

			var lens = Lens(creature, 160);
			_faces[creature] = lens;
			frame.Face.Texture = lens.GetTexture();

			frame.Name = new Label { Text = creature.Name, HorizontalAlignment = HorizontalAlignment.Center };
			frame.Name.AddThemeFontOverride("font", Display);
			frame.Name.AddThemeFontSizeOverride("font_size", 15);
			frame.Name.AddThemeColorOverride("font_outline_color", Colors.Black);
			frame.Name.AddThemeConstantOverride("outline_size", 5);
			picture.AddChild(frame.Name);
			frame.Name.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.BottomWide);

			frame.Trouble = new Label { HorizontalAlignment = HorizontalAlignment.Center, AutowrapMode = TextServer.AutowrapMode.WordSmart };
			frame.Trouble.AddThemeFontSizeOverride("font_size", 11);
			frame.Trouble.AddThemeColorOverride("font_color", new Color(0.98f, 0.66f, 0.38f));
			frame.Trouble.AddThemeColorOverride("font_outline_color", Colors.Black);
			frame.Trouble.AddThemeConstantOverride("outline_size", 4);
			picture.AddChild(frame.Trouble);
			frame.Trouble.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopWide);

			frame.Health = Bar(Blood, 14);
			frame.Numbers = Overlay(frame.Health);
			frame.Numbers.AddThemeFontSizeOverride("font_size", 11);
			stack.AddChild(frame.Health);

			// A click on a face picks them, Shift adding them to whoever is picked already; a
			// double click opens their sheet as well.
			var who = creature;
			frame.Panel.MouseDefaultCursorShape = Control.CursorShape.PointingHand;
			frame.Panel.GuiInput += input =>
			{
				if (input is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } click)
				{
					// The character window follows whoever is clicked, in a fight as out of one.
					_invWho = who;
					Select(who, click.ShiftPressed);

					if (click.DoubleClick)
					{
						_showSheet.ButtonPressed = true;
					}

					RefreshSheet();
				}
			};

			_frames[creature] = frame;
		}

		// New frames are blank until somebody fills them: a walk, a door or a fight's end that
		// rebuilt them would otherwise leave white faces and empty bars until the next turn.
		UpdateStatus();
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
			// Lit for whoever is acting in a fight, and for whoever is picked between fights.
			var picked = vitals.Acting || (Choosing && Selected().Contains(creature));
			frame.Panel.AddThemeStyleboxOverride("panel", Plate(Iron, picked ? BronzeBright : IronEdge, picked ? 3 : 2, 3, 3));
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
			_actorName.AddThemeColorOverride("font_color", now.Party ? PageInk : InkRed);
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
