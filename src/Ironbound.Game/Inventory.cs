using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Items;
using Ironbound.Simulation;

/// <summary>
/// The part of <see cref="Main"/> that is the character window: what somebody wears and holds,
/// the party's bag beside it, and the sheet of numbers behind a second tab.
/// </summary>
/// <remarks>
/// After Wrath's inventory, which the owner chose: one bag for the whole party, every
/// character's slots round their own figure, and putting something on as a step of its own. It
/// replaces a dropdown whose Take button handed an item over and pushed the old one back into
/// the sack, so clicking it twice swapped the two for ever.
/// <para>
/// The figure in the middle is the model itself, armed by the same code that arms it on the
/// board, so a sword taken off leaves her hand here before it leaves it there.
/// </para>
/// </remarks>
public partial class Main
{
	private const int TilePixels = 64;

	private enum CharacterTab
	{
		Inventory,
		Sheet,
	}

	private enum BagFilter
	{
		All,
		Weapons,
		Armour,
		Valuables,
		Other,
	}

	/// <summary>Whatever was clicked last: a line of the bag, or something somebody has on.</summary>
	private sealed record Picked(BagEntry Line, EquippedItem Worn);

	// Down the left of the figure and down the right, then along the bottom: the book's body
	// slots, named as the rules name them. A slot the rules do not have yet is drawn empty and
	// takes nothing, so the doll is the right shape before anything goes in its belt or boots.
	private static readonly (string Slot, string Label)[] DollLeft =
		[("Head", "Head"), ("Headband", "Headband"), ("Eyes", "Eyes"), ("Neck", "Neck"), ("Cloak", "Shoulders"), ("Chest", "Chest")];

	private static readonly (string Slot, string Label)[] DollRight =
		[("Armour", "Armour"), ("Body", "Body"), ("Belt", "Belt"), ("Wrists", "Wrists"), ("Hands", "Hands"), ("Feet", "Feet")];

	private static readonly (string Slot, string Label)[] DollBottom =
		[("MainHand", "Main hand"), ("OffHand", "Off hand"), ("Shield", "Shield"), ("Ring", "Ring"), ("Ring", "Ring")];

	private CharacterTab _tab = CharacterTab.Inventory;
	private BagFilter _filter = BagFilter.All;
	private Creature _invWho;
	private Picked _picked;

	private HBoxContainer _invParty;
	private Button _tabInventory;
	private Button _tabSheet;
	private Control _invPage;
	private Control _sheetPage;
	private VBoxContainer _dollLeft;
	private VBoxContainer _dollRight;
	private HBoxContainer _dollBottom;
	private HBoxContainer _dollBelt;
	private TextureRect _dollFigure;
	private SubViewport _dollLens;
	private Creature _dollShown;
	private Label _dollStats;
	private GridContainer _bagGrid;
	private ScrollContainer _bagScroll;
	private readonly Dictionary<BagFilter, Button> _filters = new();
	private Label _bagFooter;
	private RichTextLabel _invDetail;
	private VBoxContainer _invButtons;

	/// <summary>
	/// The character window. Hidden until the sheet button or I opens it; the same button
	/// closes it again.
	/// </summary>
	private void BuildSheet(CanvasLayer layer)
	{
		_sheetPanel = new PanelContainer { Visible = false };
		_sheetPanel.AddThemeStyleboxOverride("panel", Plate(Iron, IronEdge, 2, 4, 10));
		layer.AddChild(_sheetPanel);

		// Above the action bar and clear of the log, so the board's state is still in sight.
		_sheetPanel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		(_sheetPanel.AnchorLeft, _sheetPanel.AnchorRight) = (0.07f, 0.93f);
		(_sheetPanel.AnchorTop, _sheetPanel.AnchorBottom) = (0.03f, 0.745f);
		(_sheetPanel.OffsetLeft, _sheetPanel.OffsetRight, _sheetPanel.OffsetTop, _sheetPanel.OffsetBottom) = (0, 0, 0, 0);

		var rows = new VBoxContainer();
		rows.AddThemeConstantOverride("separation", 8);
		_sheetPanel.AddChild(rows);

		// Whose page, and which page.
		var header = new HBoxContainer();
		header.AddThemeConstantOverride("separation", 8);
		rows.AddChild(header);

		_invParty = new HBoxContainer();
		_invParty.AddThemeConstantOverride("separation", 6);
		header.AddChild(_invParty);
		header.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });

		_tabInventory = TabButton("Inventory", CharacterTab.Inventory);
		header.AddChild(_tabInventory);
		_tabSheet = TabButton("Sheet", CharacterTab.Sheet);
		header.AddChild(_tabSheet);

		var close = Ironclad(new Button { Text = "Close", FocusMode = Control.FocusModeEnum.None });
		close.Pressed += () =>
		{
			_showSheet.ButtonPressed = false;
			RefreshSheet();
		};
		header.AddChild(close);

		BuildInventoryPage(rows);

		var paper = new PanelContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill, Visible = false };
		paper.AddThemeStyleboxOverride("panel", ParchmentPlate(28, 18));
		rows.AddChild(paper);
		_sheetPage = paper;

		_sheet = new RichTextLabel
		{
			BbcodeEnabled = true,
			ScrollFollowing = false,
			SizeFlagsVertical = Control.SizeFlags.ExpandFill,
		};
		_sheet.AddThemeColorOverride("default_color", PageInk);
		paper.AddChild(_sheet);
	}

	private Button TabButton(string text, CharacterTab tab)
	{
		var button = Ironclad(new Button { Text = text, ToggleMode = true, FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(120, 0) });
		button.Pressed += () =>
		{
			_tab = tab;
			RefreshSheet();
		};
		return button;
	}

	private void BuildInventoryPage(VBoxContainer rows)
	{
		var page = new VBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
		page.AddThemeConstantOverride("separation", 8);
		rows.AddChild(page);
		_invPage = page;

		var across = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
		across.AddThemeConstantOverride("separation", 10);
		page.AddChild(across);

		// The doll: slots down either side of the figure, hands and rings beneath, the belt last.
		var doll = new VBoxContainer();
		doll.AddThemeConstantOverride("separation", 6);
		across.AddChild(PaperInIron(doll, 14, 10));

		var figureRow = new HBoxContainer();
		figureRow.AddThemeConstantOverride("separation", 8);
		doll.AddChild(figureRow);

		_dollLeft = new VBoxContainer();
		_dollLeft.AddThemeConstantOverride("separation", 4);
		figureRow.AddChild(_dollLeft);

		_dollFigure = new TextureRect
		{
			CustomMinimumSize = new Vector2(250, 400),
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		figureRow.AddChild(Framed(_dollFigure, 250));

		_dollRight = new VBoxContainer();
		_dollRight.AddThemeConstantOverride("separation", 4);
		figureRow.AddChild(_dollRight);

		_dollBottom = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
		_dollBottom.AddThemeConstantOverride("separation", 6);
		doll.AddChild(_dollBottom);

		var belt = new HBoxContainer();
		belt.AddThemeConstantOverride("separation", 6);

		// A weapon dropped on the belt row is hung there, to hand but not in hand.
		belt.SetDragForwarding(
			Callable.From((Vector2 _) => default(Variant)),
			Callable.From((Vector2 _, Variant data) => data.AsString().StartsWith("bag:", StringComparison.Ordinal)),
			Callable.From((Vector2 _, Variant data) => DropOnDoll(data.AsString(), EquipmentSlot.Carried)));
		belt.AddChild(Words("On the belt", Body, 15, PageInk));
		_dollBelt = new HBoxContainer();
		_dollBelt.AddThemeConstantOverride("separation", 4);
		belt.AddChild(_dollBelt);
		doll.AddChild(belt);

		_dollStats = Words(string.Empty, Body, 15, PageInk);
		doll.AddChild(_dollStats);

		// The bag: what kind of thing, then the things, then what it all weighs.
		var bag = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		bag.AddThemeConstantOverride("separation", 6);
		var bagPaper = PaperInIron(bag, 14, 10);
		bagPaper.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		across.AddChild(bagPaper);

		var heading = new HBoxContainer();
		heading.AddThemeConstantOverride("separation", 6);
		heading.AddChild(Words("The party's bag", Display, 20, PageInk));
		heading.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
		foreach (var filter in Enum.GetValues<BagFilter>())
		{
			var button = new Button { Text = filter.ToString(), ToggleMode = true, FocusMode = Control.FocusModeEnum.None };
			button.Pressed += () =>
			{
				_filter = filter;
				RefreshSheet();
			};
			heading.AddChild(button);
			_filters[filter] = button;
		}

		bag.AddChild(heading);

		// Never rather than Disabled: a disabled direction makes the scroller as wide as the grid,
		// and the grid is laid out to be as wide as the scroller — each widening the other until
		// Godot's message queue ran out. Columns() keeps the grid inside it anyway.
		var scroll = new ScrollContainer
		{
			SizeFlagsVertical = Control.SizeFlags.ExpandFill,
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			HorizontalScrollMode = ScrollContainer.ScrollMode.ShowNever,
		};
		bag.AddChild(scroll);
		_bagScroll = scroll;

		// As many columns as the paper is wide, worked out again whenever it changes size.
		scroll.Resized += () =>
		{
			if (_sheetPanel.Visible && Columns() != _bagGrid.Columns)
			{
				RefreshSheet();
			}
		};

		_bagGrid = new GridContainer { Columns = 12 };
		_bagGrid.AddThemeConstantOverride("h_separation", 6);
		_bagGrid.AddThemeConstantOverride("v_separation", 6);
		scroll.AddChild(_bagGrid);

		// Dropping anything worn onto the bag takes it off.
		scroll.SetDragForwarding(
			Callable.From((Vector2 _) => default(Variant)),
			Callable.From((Vector2 _, Variant data) => data.AsString().StartsWith("worn:", StringComparison.Ordinal)),
			Callable.From((Vector2 _, Variant data) => DropOnBag(data.AsString())));

		_bagFooter = Words(string.Empty, Body, 15, PageInk);
		bag.AddChild(_bagFooter);

		// What was clicked, and what can be done with it.
		var detailRow = new HBoxContainer { CustomMinimumSize = new Vector2(0, 110) };
		detailRow.AddThemeConstantOverride("separation", 10);
		page.AddChild(detailRow);

		_invDetail = new RichTextLabel
		{
			BbcodeEnabled = true,
			FitContent = false,
			ScrollFollowing = false,
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			SizeFlagsVertical = Control.SizeFlags.ExpandFill,
		};
		_invDetail.AddThemeColorOverride("default_color", PageInk);
		var detailPaper = PaperInIron(_invDetail, 14, 8);
		detailPaper.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		detailRow.AddChild(detailPaper);

		_invButtons = new VBoxContainer { CustomMinimumSize = new Vector2(170, 0) };
		_invButtons.AddThemeConstantOverride("separation", 6);
		detailRow.AddChild(_invButtons);
	}

	/// <summary>Redraws the window for whoever it is pointed at, or hides it.</summary>
	private void RefreshSheet()
	{
		if (_sheetPanel is null || _showSheet is null)
		{
			return;
		}

		_sheetPanel.Visible = _showSheet.ButtonPressed;

		if (!_sheetPanel.Visible)
		{
			// Nobody is looking, so nothing needs rendering.
			ReleaseDoll();
			return;
		}

		var creature = Wearer();
		_tabInventory.SetPressedNoSignal(_tab == CharacterTab.Inventory);
		_tabSheet.SetPressedNoSignal(_tab == CharacterTab.Sheet);
		_invPage.Visible = _tab == CharacterTab.Inventory;
		_sheetPage.Visible = _tab == CharacterTab.Sheet;

		RefreshPartyTabs(creature);

		if (creature is null)
		{
			_sheet.Text = "Nobody to look at.";
			return;
		}

		if (_tab == CharacterTab.Sheet)
		{
			ReleaseDoll();
			_sheet.Text = SheetText(creature);
			return;
		}

		RefreshDoll(creature);
		RefreshBag(creature);
		RefreshPicked(creature);
	}

	/// <summary>Whose page is open: whoever the window was last pointed at, else the usual subject.</summary>
	private Creature Wearer() =>
		_invWho is not null && _campaign.Party.Contains(_invWho) ? _invWho : Subject();

	private void RefreshPartyTabs(Creature shown)
	{
		foreach (var child in _invParty.GetChildren())
		{
			child.QueueFree();
		}

		foreach (var member in _campaign.Party)
		{
			var button = new Button
			{
				Text = member.Name,
				ToggleMode = true,
				ButtonPressed = ReferenceEquals(member, shown),
				FocusMode = Control.FocusModeEnum.None,
				CustomMinimumSize = new Vector2(0, 52),
				ExpandIcon = true,
			};

			if (_faces.TryGetValue(member, out var face))
			{
				button.Icon = face.GetTexture();
				button.AddThemeConstantOverride("icon_max_width", 44);
			}

			var who = member;
			button.Pressed += () =>
			{
				_invWho = who;
				_picked = null;
				RefreshSheet();
			};
			_invParty.AddChild(Ironclad(button));
		}
	}

	// ---- the doll ----

	private void RefreshDoll(Creature creature)
	{
		if (!ReferenceEquals(_dollShown, creature) || _dollLens is null)
		{
			ReleaseDoll();
			_dollShown = creature;

			if (!_instant)
			{
				_dollLens = Lens(creature, 250, whole: true);
				_dollFigure.Texture = _dollLens.GetTexture();
			}
		}

		Clear(_dollLeft);
		Clear(_dollRight);
		Clear(_dollBottom);
		Clear(_dollBelt);

		foreach (var (slot, label) in DollLeft)
		{
			_dollLeft.AddChild(SlotTile(creature, slot, label, 0));
		}

		foreach (var (slot, label) in DollRight)
		{
			_dollRight.AddChild(SlotTile(creature, slot, label, 0));
		}

		var rings = 0;
		foreach (var (slot, label) in DollBottom)
		{
			_dollBottom.AddChild(SlotTile(creature, slot, label, slot == "Ring" ? rings++ : 0));
		}

		var belt = creature.Equipment.Worn.Where(worn => worn.Slot == EquipmentSlot.Carried).ToList();
		foreach (var worn in belt)
		{
			_dollBelt.AddChild(WornTile(creature, worn, string.Empty));
		}

		if (belt.Count == 0)
		{
			_dollBelt.AddChild(Words("nothing", Body, 14, PageInk.Lightened(0.35f)));
		}

		// What they carry themselves, and the load they actually move under: their own or the
		// party's, whichever is worse.
		var own = Encumbrance.Load(creature);
		var moving = Encumbrance.Effective(creature, _campaign);
		_dollStats.Text = $"AC {creature.ArmorClass.Total}   ·   {creature.HitPoints.Current}/{creature.HitPoints.Maximum} hp   ·   speed {creature.CurrentSpeed} ft\n"
			+ $"Carrying {Pricing.Pounds(own.Weight)} of {Pricing.Pounds(own.Capacity.Light)} light   ·   moving under a {Encumbrance.Name(moving)} load";
	}

	/// <summary>Lets the figure's little world go: a hidden window renders nothing.</summary>
	private void ReleaseDoll()
	{
		_dollLens?.QueueFree();
		_dollLens = null;
		_dollShown = null;

		if (_dollFigure is not null)
		{
			_dollFigure.Texture = null;
		}
	}

	/// <summary>One slot of the doll: whatever is in it, or its name in faded ink.</summary>
	private Control SlotTile(Creature creature, string slotName, string label, int nth)
	{
		if (!Enum.TryParse<EquipmentSlot>(slotName, out var slot))
		{
			var absent = Tile(null, 0, label, verdict: 0, warn: false, picked: false);
			absent.TooltipText = $"{label}: nothing in the game goes here yet.";
			absent.Modulate = new Color(1, 1, 1, 0.55f);
			return absent;
		}

		var worn = creature.Equipment.Worn.Where(entry => entry.Slot == slot).Skip(nth).FirstOrDefault();
		if (worn is not null)
		{
			return WornTile(creature, worn, label);
		}

		var empty = Tile(null, 0, label, verdict: 0, warn: false, picked: false);
		empty.TooltipText = label;
		empty.SetDragForwarding(
			Callable.From((Vector2 _) => default(Variant)),
			Callable.From((Vector2 _, Variant data) => data.AsString().StartsWith("bag:", StringComparison.Ordinal)),
			Callable.From((Vector2 _, Variant data) => DropOnDoll(data.AsString(), slot)));
		return empty;
	}

	private Control WornTile(Creature creature, EquippedItem worn, string label)
	{
		var lines = _content.DescribeItem(worn.Item, creature);
		var tile = Tile(worn.Item, 1, label, verdict: 0, warn: Unfit(lines), picked: _picked?.Worn == worn, broken: worn.IsBroken);
		tile.TooltipText = string.Join("\n", lines.Prepend(worn.ToString()));
		var key = $"worn:{creature.Equipment.Worn.ToList().IndexOf(worn)}";

		tile.GuiInput += input =>
		{
			if (input is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } click)
			{
				_picked = new Picked(null, worn);
				if (click.DoubleClick)
				{
					TakeOff(creature, worn);
					return;
				}

				RefreshSheet();
			}
		};

		tile.SetDragForwarding(
			Callable.From((Vector2 _) => Drag(tile, worn.Item, key)),
			Callable.From((Vector2 _, Variant data) => data.AsString().StartsWith("bag:", StringComparison.Ordinal)),
			Callable.From((Vector2 _, Variant data) => DropOnDoll(data.AsString(), worn.Slot)));

		return tile;
	}

	// ---- the bag ----

	private void RefreshBag(Creature creature)
	{
		foreach (var (filter, button) in _filters)
		{
			button.SetPressedNoSignal(filter == _filter);
		}

		Clear(_bagGrid);
		_bagGrid.Columns = Columns();

		var lines = BagLines();
		var shown = lines
			.Select((line, index) => (line, index))
			.Where(entry => Passes(entry.line.Item, _filter))
			.ToList();

		foreach (var (line, index) in shown)
		{
			_bagGrid.AddChild(BagTile(creature, line, index));
		}

		// A full row of empty cells after the last item, so the bag reads as a bag with room in
		// it rather than as a list that happens to be short.
		var pad = _bagGrid.Columns - (shown.Count % _bagGrid.Columns) + (shown.Count < _bagGrid.Columns * 4 ? _bagGrid.Columns * (3 - (shown.Count / _bagGrid.Columns)) : 0);
		for (var i = 0; i < pad; i++)
		{
			var cell = Tile(null, 0, string.Empty, verdict: 0, warn: false, picked: false);
			cell.MouseFilter = Control.MouseFilterEnum.Pass;
			_bagGrid.AddChild(cell);
		}

		// The purse, then what it all weighs against what the party can carry between them.
		var bag = _campaign.Bag;
		var load = Encumbrance.PartyLoad(_campaign);
		var purse = bag.Money.IsEmpty ? "no coin" : bag.Money.ToString();
		var things = bag.Count == 0 ? "nothing in the bag" : $"{bag.Count} {(bag.Count == 1 ? "thing" : "things")}, worth {Pricing.Format(bag.Value)}";
		_bagFooter.Text = $"Purse: {purse}   ·   {things}\n{load.Line}";
		_bagFooter.AddThemeColorOverride("font_color", load.Category switch
		{
			LoadCategory.Light => PageInk,
			LoadCategory.Overloaded => InkRed,
			_ => new Color(0.55f, 0.30f, 0.08f),
		});
	}

	private int Columns() =>
		_bagScroll is { Size.X: > 0 } scroll ? Math.Max(6, (int)((scroll.Size.X - 10) / (TilePixels + 6))) : 12;

	/// <summary>The bag in the order the window shows it: weapons, armour, valuables, the rest.</summary>
	private List<BagEntry> BagLines() =>
		[.. _campaign.Bag.Entries
			.OrderBy(line => Order(line.Item))
			.ThenBy(line => line.Item.Name, StringComparer.Ordinal)];

	private static int Order(ItemDefinition item) => item.Kind switch
	{
		ItemKind.Weapon => 0,
		ItemKind.Armour or ItemKind.Shield => 1,
		ItemKind.Wondrous => 2,
		ItemKind.Valuable => 3,
		_ => 4,
	};

	private static bool Passes(ItemDefinition item, BagFilter filter) => filter switch
	{
		BagFilter.Weapons => item.Kind == ItemKind.Weapon,
		BagFilter.Armour => item.Kind is ItemKind.Armour or ItemKind.Shield,
		BagFilter.Valuables => item.Kind == ItemKind.Valuable,
		BagFilter.Other => item.Kind is not (ItemKind.Weapon or ItemKind.Armour or ItemKind.Shield or ItemKind.Valuable),
		_ => true,
	};

	/// <summary>Up, down or level against what the wearer has on now: Wrath's green and red arrows.</summary>
	private int Verdict(Creature creature, ItemDefinition item, out GearComparison compared)
	{
		compared = item.Kind is ItemKind.Valuable or ItemKind.Natural ? null : Outfitter.Compare(_campaign, creature, item);
		return compared?.Verdict switch
		{
			GearVerdict.Upgrade => 1,
			GearVerdict.Downgrade => -1,
			_ => 0,
		};
	}

	private Control BagTile(Creature creature, BagEntry line, int index)
	{
		var lines = _content.DescribeItem(line.Item, creature);
		var verdict = Verdict(creature, line.Item, out var compared);
		var tile = Tile(line.Item, line.Count, string.Empty, verdict, warn: Unfit(lines), picked: _picked?.Line is { } was && was.Matches(line), broken: line.IsBroken);
		var heading = line.Count > 1 ? $"{line.Item.Name} ×{line.Count}" : line.Item.Name;
		var against = compared is { Reason.Length: > 0 } ? [$"For {creature.Name}: {compared.Reason}"] : Array.Empty<string>();
		tile.TooltipText = string.Join("\n", against.Concat(lines).Prepend(heading));
		var key = $"bag:{index}";

		tile.GuiInput += input =>
		{
			if (input is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } click)
			{
				_picked = new Picked(line, null);
				if (click.DoubleClick)
				{
					PutOn(creature, line, null);
					return;
				}

				RefreshSheet();
			}
		};

		tile.SetDragForwarding(
			Callable.From((Vector2 _) => Drag(tile, line.Item, key)),
			Callable.From((Vector2 _, Variant data) => data.AsString().StartsWith("worn:", StringComparison.Ordinal)),
			Callable.From((Vector2 _, Variant data) => DropOnBag(data.AsString())));

		return tile;
	}

	// ---- what was clicked ----

	private void RefreshPicked(Creature creature)
	{
		Clear(_invButtons);

		// What was picked may have left the bag or come off since; forget it rather than
		// describe something that is not there.
		var line = _picked?.Line is { } was ? _campaign.Bag.Find(was) : null;
		var worn = _picked?.Worn is { } on && creature.Equipment.Worn.Contains(on) ? on : null;
		var item = line?.Item ?? worn?.Item;

		if (item is null)
		{
			_picked = null;
			_invDetail.Text = "[i]Click something to see what it is. Double-click, or drag it, to put it on or take it off.[/i]";
			return;
		}

		var text = new System.Text.StringBuilder();
		text.Append($"[b]{Capitalised(item.Name)}[/b]");
		if (line is { Count: > 1 })
		{
			text.Append($" ×{line.Count}");
		}

		if (worn is not null)
		{
			text.Append($"  —  {creature.Name}'s, {SlotWords(worn.Slot)}");
		}

		GearComparison compared = null;
		if (line is not null && Verdict(creature, item, out compared) is var verdict && compared is not null)
		{
			var colour = verdict > 0 ? "#3d7a1f" : verdict < 0 ? "#a5321f" : "#5a4a3a";
			text.Append($"  —  [color={colour}]for {creature.Name}: {compared.Reason}[/color]");
		}

		text.Append('\n');
		foreach (var description in _content.DescribeItem(item, creature))
		{
			text.Append(description.Contains("not proficient", StringComparison.OrdinalIgnoreCase)
				? $"[color=#c0392b]{description}[/color]\n"
				: $"{description}\n");
		}

		if (compared?.Refusal is { } refusal)
		{
			text.Append($"[i]{refusal}[/i]\n");
		}

		_invDetail.Text = text.ToString();

		var fighting = _campaign.State == CampaignState.Fighting;
		Button Action(string words, string tip, Action pressed)
		{
			var button = new Button { Text = words, Disabled = fighting, FocusMode = Control.FocusModeEnum.None };
			button.TooltipText = fighting ? "Not while fighting." : tip;
			button.Pressed += pressed;
			_invButtons.AddChild(button);
			return button;
		}

		if (line is not null)
		{
			if (item.Kind is not (ItemKind.Valuable or ItemKind.Natural))
			{
				var give = Action($"Give to {creature.Name}", $"{creature.Name} puts it on, or takes it in hand.", () => PutOn(creature, line, null));
				give.Disabled |= compared is { CanEquip: false };
			}

			if (item.Kind == ItemKind.Weapon)
			{
				Action("Hang on the belt", $"{creature.Name} carries it on the belt, to hand but not in hand.", () => Stow(creature, line));
			}

			Action(line.Count > 1 ? "Drop one" : "Drop", "Leave it on the ground here. It can be picked up again.", () => DropOne(line));
		}
		else
		{
			Action("Into the bag", $"{creature.Name} takes it off and puts it in the bag.", () => TakeOff(creature, worn));
		}
	}

	private static string SlotWords(EquipmentSlot slot) => slot switch
	{
		EquipmentSlot.Carried => "on the belt",
		EquipmentSlot.MainHand => "in the main hand",
		EquipmentSlot.OffHand => "in the off hand",
		EquipmentSlot.Armour => "worn",
		EquipmentSlot.Shield => "on the arm",
		_ => $"worn ({slot.ToString().ToLowerInvariant()})",
	};

	// ---- doing it ----

	private void PutOn(Creature creature, BagEntry line, EquipmentSlot? slot)
	{
		Did(creature, slot == EquipmentSlot.Carried ? _campaign.Stow(creature, line) : _campaign.Equip(creature, line, slot));
	}

	private void Stow(Creature creature, BagEntry line) => Did(creature, _campaign.Stow(creature, line));

	private void TakeOff(Creature creature, EquippedItem worn)
	{
		if (worn is null)
		{
			return;
		}

		Did(creature, worn.Slot == EquipmentSlot.Carried ? _campaign.Unstow(creature, worn) : _campaign.Unequip(creature, worn));
	}

	private void DropOne(BagEntry line)
	{
		Did(null, _campaign.Drop(line));

		// What was put down is now a pile on the ground, and the ground should show it.
		RefreshContainers();
	}

	/// <summary>Says how it went: the line in the log, or the reason on the prompt.</summary>
	private void Did(Creature creature, GearResult result)
	{
		if (!result)
		{
			Refuse(result.Line);
			return;
		}

		LogText($"— {result.Line} —\n");
		Changed(creature);
	}

	/// <summary>After anything changes hands: the board's figure, the window's figure, the bars.</summary>
	private void Changed(Creature creature)
	{
		_picked = null;
		if (creature is not null)
		{
			Rearm(creature);
		}

		ReleaseDoll();
		RefreshSheet();
		RefreshControls();
		UpdateStatus();
	}

	private Variant Drag(Control tile, ItemDefinition item, string key)
	{
		if (_campaign.State == CampaignState.Fighting)
		{
			return default;
		}

		var preview = new TextureRect
		{
			Texture = ItemIcon(item),
			CustomMinimumSize = new Vector2(TilePixels, TilePixels),
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
			Modulate = new Color(1, 1, 1, 0.8f),
		};
		tile.SetDragPreview(preview);
		return key;
	}

	/// <summary>Something from the bag dropped on a slot: into that slot, or onto the belt.</summary>
	private void DropOnDoll(string key, EquipmentSlot? slot)
	{
		var lines = BagLines();
		if (Wearer() is { } creature && Index(key, "bag:") is { } index && index < lines.Count)
		{
			PutOn(creature, lines[index], slot);
		}
	}

	private void DropOnBag(string key)
	{
		if (Wearer() is { } creature && Index(key, "worn:") is { } index && index < creature.Equipment.Worn.Count)
		{
			TakeOff(creature, creature.Equipment.Worn[index]);
		}
	}

	private static int? Index(string key, string prefix) =>
		key.StartsWith(prefix, StringComparison.Ordinal) && int.TryParse(key.AsSpan(prefix.Length), out var index) ? index : null;

	// ---- tiles ----

	/// <summary>
	/// A square for one thing: its picture, how many, whether it is worse or better than what is
	/// worn, and a red rim when the wearer was never taught to use it. Empty, it is a faded name.
	/// </summary>
	private static PanelContainer Tile(ItemDefinition item, int count, string empty, int verdict, bool warn, bool picked, bool broken = false)
	{
		var tile = new PanelContainer
		{
			CustomMinimumSize = new Vector2(TilePixels, TilePixels),
			MouseFilter = Control.MouseFilterEnum.Stop,
		};

		var rim = picked ? BronzeBright : warn ? Blood : item is null ? new Color(0.45f, 0.38f, 0.30f, 0.55f) : IronEdge;
		var fill = item is null ? new Color(0.16f, 0.13f, 0.11f, 0.30f) : new Color(0.13f, 0.11f, 0.10f, 0.92f);
		var plate = Slot(fill, rim, picked || warn ? 3 : 2);
		plate.SetContentMarginAll(3);
		tile.AddThemeStyleboxOverride("panel", plate);

		if (item is null)
		{
			var name = new Label
			{
				Text = empty,
				HorizontalAlignment = HorizontalAlignment.Center,
				VerticalAlignment = VerticalAlignment.Center,
				AutowrapMode = TextServer.AutowrapMode.WordSmart,
				MouseFilter = Control.MouseFilterEnum.Ignore,
			};
			name.AddThemeFontSizeOverride("font_size", 10);
			name.AddThemeColorOverride("font_color", new Color(0.42f, 0.36f, 0.30f));
			tile.AddChild(name);
			return tile;
		}

		if (ItemIcon(item) is { } icon)
		{
			tile.AddChild(new TextureRect
			{
				Texture = icon,
				ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
				StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
				MouseFilter = Control.MouseFilterEnum.Ignore,
				Modulate = broken ? new Color(0.75f, 0.6f, 0.55f) : Colors.White,
			});
		}
		else
		{
			// Nothing drawn for it yet: its name, small, so it is still a thing and not a hole.
			var words = new Label
			{
				Text = item.Name,
				HorizontalAlignment = HorizontalAlignment.Center,
				VerticalAlignment = VerticalAlignment.Center,
				AutowrapMode = TextServer.AutowrapMode.WordSmart,
				MouseFilter = Control.MouseFilterEnum.Ignore,
			};
			words.AddThemeFontSizeOverride("font_size", 11);
			words.AddThemeColorOverride("font_color", Parchment);
			tile.AddChild(words);
		}

		var marks = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
		tile.AddChild(marks);

		if (count > 1)
		{
			marks.AddChild(Badge($"×{count}", Parchment, Control.LayoutPreset.BottomRight));
		}

		if (verdict != 0)
		{
			marks.AddChild(Badge(verdict > 0 ? "▲" : "▼", verdict > 0 ? new Color(0.45f, 0.85f, 0.35f) : new Color(0.90f, 0.35f, 0.25f), Control.LayoutPreset.TopRight));
		}

		if (broken)
		{
			marks.AddChild(Badge("broken", new Color(0.95f, 0.55f, 0.45f), Control.LayoutPreset.BottomLeft));
		}

		return tile;
	}

	private static Label Badge(string text, Color colour, Control.LayoutPreset corner)
	{
		var badge = new Label { Text = text, MouseFilter = Control.MouseFilterEnum.Ignore };
		badge.AddThemeFontSizeOverride("font_size", 12);
		badge.AddThemeColorOverride("font_color", colour);
		badge.AddThemeColorOverride("font_outline_color", Colors.Black);
		badge.AddThemeConstantOverride("outline_size", 4);
		badge.SetAnchorsAndOffsetsPreset(corner, Control.LayoutPresetMode.Minsize, 2);
		return badge;
	}

	/// <summary>Whether the item's own description says the wearer could not use it properly.</summary>
	private static bool Unfit(IEnumerable<string> lines) =>
		lines.Any(line => line.Contains("not proficient", StringComparison.OrdinalIgnoreCase));

	private static void Clear(Node container)
	{
		foreach (var child in container.GetChildren())
		{
			container.RemoveChild(child);
			child.QueueFree();
		}
	}

	// ---- the sheet tab ----

	private static string SheetText(Creature creature)
	{
		var text = new System.Text.StringBuilder();
		text.Append($"[b]{creature.Name}[/b]\n");

		foreach (var section in CharacterSheet.Of(creature))
		{
			if (section.Lines.Count == 0)
			{
				continue;
			}

			text.Append($"\n[b]{section.Heading}[/b]\n");
			foreach (var line in section.Lines)
			{
				// Fighting with something you were never taught, or in armour you were never
				// taught to wear, costs on every swing; it should not read like the lines around it.
				text.Append(line.Contains("not proficient", StringComparison.OrdinalIgnoreCase)
					? $"  [color=#c0392b]{line}[/color]\n"
					: $"  {line}\n");
			}
		}

		return text.ToString();
	}
}
