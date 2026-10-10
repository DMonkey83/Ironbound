using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Ironbound.Rules.Content;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Items;
using Ironbound.Rules.Maps;
using Ironbound.Simulation;
using Container = Ironbound.Simulation.Container;

/// <summary>
/// The part of <see cref="Main"/> that is loot in the world: crates, chests, sacks and the fallen
/// drawn where they are, walked to and opened, and the window that empties them into the bag.
/// </summary>
/// <remarks>
/// The owner asked for "better loot logic, crates": the old Take button handed an item over and
/// pushed the one it replaced back into the sack, so clicking it twice swapped them for ever.
/// Now finding and wearing are two steps. Opening something shows what is in it, and a thing
/// taken is gone from where it was, so there is nothing to take twice; putting it on is the
/// character window's business.
/// <para>
/// A container glints while there is something in it, or while it is still shut and might have.
/// An open, empty one is just furniture again.
/// </para>
/// </remarks>
public partial class Main
{
	/// <summary>How far round the party the Loot button looks, in squares.</summary>
	private const int LootReach = 12;

	private readonly Dictionary<string, Node3D> _containerNodes = new();
	private static readonly Dictionary<string, PackedScene> PropScenes = new();

	private CanvasLayer _lootLayer;
	private List<string> _lootShown = [];
	private string _lootHeading = string.Empty;

	// ---- on the board ----

	/// <summary>Draws every container the party can see, and forgets any it no longer can.</summary>
	private void RefreshContainers()
	{
		if (_world is null || _campaign is null)
		{
			return;
		}

		var visible = _campaign.Containers.ToDictionary(container => container.Id, StringComparer.Ordinal);
		foreach (var (id, node) in _containerNodes.ToList())
		{
			if (!visible.ContainsKey(id) || !IsInstanceValid(node))
			{
				if (IsInstanceValid(node))
				{
					node.QueueFree();
				}

				_containerNodes.Remove(id);
			}
		}

		foreach (var container in visible.Values)
		{
			DrawContainer(container);
		}
	}

	/// <summary>Puts a container on the board if it is not there yet, and shows it as it now is.</summary>
	private void DrawContainer(Container container)
	{
		if (_world is null)
		{
			return;
		}

		// The model a container wants changes once with its state — a sack opened spills its
		// coin — so a holder whose model is the wrong one is built again.
		var file = PropFile(container);
		if (_containerNodes.TryGetValue(container.Id, out var stale) && IsInstanceValid(stale)
			&& stale.GetMeta("file", string.Empty).AsString() != file)
		{
			stale.QueueFree();
			_containerNodes.Remove(container.Id);
		}

		if (!_containerNodes.TryGetValue(container.Id, out var holder) || !IsInstanceValid(holder))
		{
			holder = new Node3D { Name = $"Container_{container.Id}" };
			holder.SetMeta("file", file);
			_world.AddChild(holder);
			var middle = Middle(container.Squares);
			holder.Position = new Vector3(middle.X, 0, middle.Y);

			// The fallen are on the board as themselves, laid out again if the board was rebuilt;
			// they only need the glint.
			if (container.Look == ContainerLook.Body)
			{
				LayOut(container);
			}

			var height = 0.55f;
			if (container.Look != ContainerLook.Body)
			{
				var prop = PropFor(container, file);
				holder.AddChild(prop);

				// Where the model says its glint goes, a little above it; or over a stand-in's top.
				height = prop.FindChild("FX_Glint", true, false) is Node3D mark
					? mark.Position.Y + 0.3f
					: 1.15f;

				// A niche is set into the wall it is in: turned to face the open floor beside it.
				if (file == "niche")
				{
					holder.Rotation = new Vector3(0, FacingOut(container.Squares[0]), 0);
				}
			}

			holder.AddChild(Glint(height));
			_containerNodes[container.Id] = holder;
		}

		ShowState(holder, container);
	}

	/// <summary>Which model a container is drawn with, by its look and, for a few, its state and place.</summary>
	private string PropFile(Container container)
	{
		var at = container.Squares.FirstOrDefault();
		return container.Look switch
		{
			ContainerLook.Body => string.Empty,
			ContainerLook.WeaponRack => "weapon-rack",

			// Spilling its coin once it is open, until the coin is gone.
			ContainerLook.Sack => container.IsOpen && !container.IsEmpty ? "sack-open" : "sack",

			// Something left in a hole in the rock is a niche, not a heap on the floor.
			ContainerLook.Pile when _campaign.Level?.IsBlockedCell(at.X, at.Y) == true => "niche",
			_ => container.Look.ToString().ToLowerInvariant(),
		};
	}

	/// <summary>The turn that faces a model at <paramref name="square"/> towards open floor beside it (its front is +Z).</summary>
	private float FacingOut(GridSquare square)
	{
		foreach (var (dx, dy, turn) in new[] { (0, 1, 0f), (1, 0, Mathf.Pi / 2), (0, -1, Mathf.Pi), (-1, 0, -Mathf.Pi / 2) })
		{
			if (_campaign.Level?.IsBlockedCell(square.X + dx, square.Y + dy) == false)
			{
				return turn;
			}
		}

		return 0f;
	}

	private static Vector2 Middle(IReadOnlyList<GridSquare> squares) =>
		squares.Count == 0
			? Vector2.Zero
			: new Vector2((float)squares.Average(at => at.X) + 0.5f, (float)squares.Average(at => at.Y) + 0.5f);

	/// <summary>The model for a container's look, from art/props, or a stand-in until there is one.</summary>
	private static Node3D PropFor(Container container, string file) =>
		PropModel(file) is { } model ? model : StandIn(container.Look);

	/// <summary>A prop from art/props by name, or null if nobody has made it.</summary>
	private static Node3D PropModel(string file)
	{
		var path = $"res://art/props/{file}.glb";
		if (!PropScenes.TryGetValue(path, out var scene))
		{
			scene = ResourceLoader.Exists(path) ? GD.Load<PackedScene>(path) : null;
			PropScenes[path] = scene;
		}

		if (scene?.Instantiate() is Node3D model)
		{
			model.Name = "Prop";
			return model;
		}

		return null;
	}

	/// <summary>
	/// A shape and a colour per kind of container, with a lid where it has one, so the board makes
	/// sense before the modelled props arrive.
	/// </summary>
	private static Node3D StandIn(ContainerLook look)
	{
		var root = new Node3D { Name = "Prop" };

		MeshInstance3D Piece(Mesh mesh, Color colour, Vector3 at, string name = null)
		{
			var piece = new MeshInstance3D
			{
				Mesh = mesh,
				MaterialOverride = new StandardMaterial3D { AlbedoColor = colour, Roughness = 0.85f },
			};
			if (name is not null)
			{
				piece.Name = name;
			}

			piece.Position = at;
			return piece;
		}

		var wood = new Color(0.42f, 0.30f, 0.18f);
		switch (look)
		{
			case ContainerLook.Chest:
			case ContainerLook.Strongbox:
				var iron = look == ContainerLook.Strongbox;
				var body = iron ? new Color(0.24f, 0.20f, 0.18f) : new Color(0.36f, 0.22f, 0.12f);
				root.AddChild(Piece(new BoxMesh { Size = new Vector3(0.8f, 0.42f, 0.55f) }, body, new Vector3(0, 0.21f, 0)));

				// The lid hinges on its back edge: a pivot there, the board hanging forward of it.
				var lid = new Node3D { Name = "Lid", Position = new Vector3(0, 0.42f, -0.275f) };
				lid.AddChild(Piece(new BoxMesh { Size = new Vector3(0.82f, 0.14f, 0.57f) }, body.Lightened(0.08f), new Vector3(0, 0.07f, 0.285f)));
				root.AddChild(lid);
				root.AddChild(Piece(new BoxMesh { Size = new Vector3(0.12f, 0.14f, 0.05f) }, new Color(0.70f, 0.52f, 0.22f), new Vector3(0, 0.36f, 0.29f), "Lock"));
				break;
			case ContainerLook.Barrel:
				root.AddChild(Piece(new CylinderMesh { TopRadius = 0.30f, BottomRadius = 0.30f, Height = 0.8f }, wood, new Vector3(0, 0.4f, 0)));
				break;
			case ContainerLook.Sack:
			case ContainerLook.Pile:
				var small = look == ContainerLook.Pile;
				var bulk = new SphereMesh { Radius = small ? 0.22f : 0.32f, Height = small ? 0.25f : 0.55f };
				root.AddChild(Piece(bulk, new Color(0.55f, 0.45f, 0.30f), new Vector3(0, small ? 0.12f : 0.27f, 0)));
				break;
			case ContainerLook.Cart:
				root.AddChild(Piece(new BoxMesh { Size = new Vector3(0.95f, 0.45f, 0.9f) }, wood.Darkened(0.1f), new Vector3(0, 0.45f, 0)));
				break;
			case ContainerLook.WeaponRack:
				root.AddChild(Piece(new BoxMesh { Size = new Vector3(0.9f, 0.9f, 0.18f) }, wood, new Vector3(0, 0.45f, 0)));
				break;
			default:
				root.AddChild(Piece(new BoxMesh { Size = new Vector3(0.82f, 0.62f, 0.82f) }, wood, new Vector3(0, 0.31f, 0)));
				root.AddChild(Piece(new BoxMesh { Size = new Vector3(0.84f, 0.10f, 0.84f) }, wood.Lightened(0.06f), new Vector3(0, 0.67f, 0), "Lid"));
				break;
		}

		return root;
	}

	/// <summary>A small gold mark over anything worth searching, turning slowly, seen through walls.</summary>
	private Label3D Glint(float height)
	{
		var glint = new Label3D
		{
			Name = "Glint",
			Text = "✦",
			FontSize = 64,
			PixelSize = 0.004f,
			Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
			NoDepthTest = true,
			Modulate = new Color(1.0f, 0.82f, 0.38f),
			OutlineModulate = new Color(0.25f, 0.12f, 0.02f),
			OutlineSize = 10,
			Position = new Vector3(0, height, 0),
		};

		var bob = glint.CreateTween().SetLoops();
		bob.TweenProperty(glint, "position:y", height + 0.12f, 0.9).SetTrans(Tween.TransitionType.Sine);
		bob.TweenProperty(glint, "position:y", height, 0.9).SetTrans(Tween.TransitionType.Sine);
		return glint;
	}

	/// <summary>The lid up or down, the contents there or gone, the lock on or off, the glint lit or not.</summary>
	private static void ShowState(Node3D holder, Container container)
	{
		if (holder.FindChild("Glint", false, false) is Node3D glint)
		{
			glint.Visible = !container.IsOpen || !container.IsEmpty;
		}

		if (holder.FindChild("Lid", true, false) is Node3D lid)
		{
			if (!lid.HasMeta("closed"))
			{
				lid.SetMeta("closed", lid.Transform);
			}

			// The models put each lid's origin on its hinge: a chest's back edge, which tips up and
			// back, or a niche's stone plug, whose bottom edge tips it out towards the room.
			var closed = lid.GetMeta("closed").AsTransform3D();
			lid.Transform = !container.IsOpen ? closed
				: container.Look is ContainerLook.Chest or ContainerLook.Strongbox
					? closed.RotatedLocal(Vector3.Right, Mathf.DegToRad(-100f))
				: holder.GetMeta("file", string.Empty).AsString() == "niche"
					? closed.RotatedLocal(Vector3.Right, Mathf.DegToRad(80f))

					// A lid with no hinge is lifted off and leant against the side.
					: closed.Translated(new Vector3(0.45f, -0.25f, 0.1f)).RotatedLocal(Vector3.Forward, Mathf.DegToRad(70f));
		}

		if (holder.FindChild("Contents", true, false) is Node3D contents)
		{
			contents.Visible = !container.IsEmpty;
		}

		if (holder.FindChild("Lock", true, false) is Node3D padlock)
		{
			padlock.Visible = container.IsLocked;
		}
	}

	/// <summary>Anything somebody has just spotted: a line in the log, and the thing on the board.</summary>
	private void NoticeThings()
	{
		if (_campaign is not { IsLevel: true })
		{
			return;
		}

		foreach (var notice in _campaign.TakeNotices())
		{
			LogText($"— {notice.Line} —\n");
			Prompt(notice.Line);
			DrawContainer(notice.Container);
		}
	}

	// ---- walking to it ----

	/// <summary>The leader goes to a body or a pile and searches it, as they would a door.</summary>
	private void GoOpen(Container container, Creature who, Battlefield field)
	{
		if (field.SquareOf(who) is { } here && container.IsWithinReach(here))
		{
			OpenAndShow(container, who);
			return;
		}

		var from = field.SquareOf(who);
		var stand = container.Squares
			.Concat(container.Squares.SelectMany(Neighbours))
			.Where(field.IsFree)
			.Distinct()
			.Select(square => (square, path: from is { } f ? Route(field, f, square) : null))
			.Where(one => one.path is { Count: > 0 })
			.OrderBy(one => one.path!.Count)
			.Select(one => (GridSquare?)one.square)
			.FirstOrDefault();

		if (stand is not { } there)
		{
			Refuse($"{who.Name} cannot get to {container.Name}.");
			return;
		}

		Send(who, there, field, () => OpenAndShow(container, who));
	}

	/// <summary>Opens it, says how that went, and shows what is inside.</summary>
	private void OpenAndShow(Container container, Creature who)
	{
		var result = _campaign.Open(container.Id, who);
		foreach (var line in result.Lines)
		{
			LogText($"{line}\n");
		}

		RefreshContainers();
		if (!result.Success)
		{
			Refuse(result.Lines.FirstOrDefault() ?? $"{container.Name} will not open.");
			return;
		}

		ShowLoot([container], Capitalised(container.Name));
	}

	// ---- the window ----

	/// <summary>Everything the party could search from where it stands, open and not yet empty.</summary>
	private List<Container> Spoils()
	{
		if (_campaign is null)
		{
			return [];
		}

		var near = _campaign.IsLevel && _battle.Battlefield is { } field
			? _campaign.Party.Where(member => member.IsConscious).Select(member => field.SquareOf(member)).OfType<GridSquare>().ToList()
			: null;

		return [.. _campaign.Containers.Where(container => container.IsOpen && !container.IsEmpty
			&& (near is null || container.Squares.Any(at => near.Any(here => Apart(at, here) <= LootReach))))];
	}

	private void OnLootNearby()
	{
		var spoils = Spoils();
		if (spoils.Count == 0)
		{
			Refuse("There is nothing left to take nearby.");
			return;
		}

		ShowLoot(spoils, spoils.Count == 1 ? Capitalised(spoils[0].Name) : "Spoils");
	}

	/// <summary>
	/// Opens the window on some containers. Nobody is reading on an autoplay run, so there it takes
	/// everything at once and hands the best of it out, as a player would — unless the run was
	/// started with <c>--show-loot</c>, to look at the window.
	/// </summary>
	private void ShowLoot(IReadOnlyList<Container> containers, string heading)
	{
		if (Unattended() && !OS.GetCmdlineUserArgs().Contains("--show-loot"))
		{
			foreach (var container in containers)
			{
				TakeWorthwhile(container);
			}

			Outfit();
			RefreshContainers();
			return;
		}

		_lootShown = [.. containers.Select(container => container.Id).Distinct()];
		_lootHeading = heading;
		RefreshLoot();
	}

	/// <summary>
	/// What a sensible player takes when nobody is there to choose: the coin, the valuables, the
	/// potions and flasks, and anything somebody would be better off with. Four suits of orc
	/// leather stay on the orcs.
	/// </summary>
	private void TakeWorthwhile(Container container)
	{
		if (!container.Money.IsEmpty && _campaign.TakeCoins(container.Id) is { Success: true } coins)
		{
			LogText($"— {coins.Line} —\n");
		}

		foreach (var entry in container.Contents.ToList())
		{
			var wanted = entry.Item.Kind is ItemKind.Valuable or ItemKind.Consumable
				|| _campaign.Party.Any(member => member.IsConscious
					&& entry.Item.Kind != ItemKind.Natural
					&& Outfitter.Compare(_campaign, member, entry.Item).Verdict == GearVerdict.Upgrade);

			if (wanted && _campaign.Take(container.Id, entry, entry.Count) is { Success: true } taken)
			{
				LogText($"— {taken.Line} —\n");
			}
		}
	}

	/// <summary>Autoplay's half of the character window: everybody puts on whatever suits them better.</summary>
	private void Outfit()
	{
		foreach (var member in _campaign.Party)
		{
			foreach (var line in Outfitter.EquipBest(_campaign, member))
			{
				LogText($"— {line} —\n");
			}

			Rearm(member);
		}
	}

	private void CloseLoot()
	{
		_lootLayer?.QueueFree();
		_lootLayer = null;
		_lootShown = [];
		RefreshContainers();
		RefreshControls();
	}

	private void RefreshLoot()
	{
		_lootLayer?.QueueFree();
		_lootLayer = null;
		if (_lootShown.Count == 0)
		{
			return;
		}

		_lootLayer = new CanvasLayer { Layer = 18 };
		AddChild(_lootLayer);

		var root = new Control { MouseFilter = Control.MouseFilterEnum.Stop };
		root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		_lootLayer.AddChild(root);
		root.AddChild(new ColorRect { Color = new Color(0, 0, 0, 0.45f), AnchorRight = 1, AnchorBottom = 1, MouseFilter = Control.MouseFilterEnum.Ignore });

		var centre = new CenterContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
		centre.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		root.AddChild(centre);

		var sheet = new PanelContainer { CustomMinimumSize = new Vector2(820, 0) };
		sheet.AddThemeStyleboxOverride("panel", ParchmentPlate(40, 30));
		centre.AddChild(sheet);

		var column = new VBoxContainer();
		column.AddThemeConstantOverride("separation", 10);
		sheet.AddChild(column);
		column.AddChild(Illuminated(_lootHeading, 40));
		column.AddChild(Rule(730));

		var scroll = new ScrollContainer
		{
			CustomMinimumSize = new Vector2(0, Math.Min(500, 170 * _lootShown.Count)),
			HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
		};
		column.AddChild(scroll);
		var list = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		list.AddThemeConstantOverride("separation", 12);
		scroll.AddChild(list);

		var who = Wearer();
		var anything = false;
		foreach (var id in _lootShown)
		{
			if (_campaign.GetContainer(id) is not { } container)
			{
				continue;
			}

			anything |= !container.IsEmpty;
			list.AddChild(LootSection(container, who, several: _lootShown.Count > 1));
		}

		var load = Encumbrance.PartyLoad(_campaign);
		var footer = Words(load.Line, Body, 15, load.Category == LoadCategory.Light ? PageInk : InkRed);
		footer.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		footer.CustomMinimumSize = new Vector2(730, 0);
		column.AddChild(footer);

		var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
		buttons.AddThemeConstantOverride("separation", 12);
		column.AddChild(buttons);

		var all = PlateButton("Take all");
		all.Disabled = !anything;
		all.Pressed += TakeAllShown;
		buttons.AddChild(all);

		var close = PlateButton("Close");
		close.Pressed += CloseLoot;
		buttons.AddChild(close);
	}

	/// <summary>One container in the window: its name, then everything in it as tiles to click.</summary>
	private Control LootSection(Container container, Creature who, bool several)
	{
		var section = new VBoxContainer();
		section.AddThemeConstantOverride("separation", 6);

		if (several)
		{
			section.AddChild(Words(Capitalised(container.Name), Display, 22, PageInk));
		}

		if (container.IsEmpty)
		{
			section.AddChild(Words("Nothing left.", BodyItalic, 18, PageInk.Lightened(0.3f)));
			return section;
		}

		var grid = new GridContainer { Columns = 10 };
		grid.AddThemeConstantOverride("h_separation", 6);
		grid.AddThemeConstantOverride("v_separation", 6);
		section.AddChild(grid);

		foreach (var entry in container.Contents)
		{
			var lines = who is null ? _content.DescribeItem(entry.Item) : _content.DescribeItem(entry.Item, who);
			var verdict = who is null ? 0 : Verdict(who, entry.Item, out var compared);
			var tile = Tile(entry.Item, entry.Count, string.Empty, verdict, warn: Unfit(lines), picked: false, broken: entry.IsBroken);
			tile.TooltipText = string.Join("\n", lines.Prepend(entry.Count > 1 ? $"{entry.Item.Name} ×{entry.Count}" : entry.Item.Name).Append("Click to take."));
			tile.MouseDefaultCursorShape = Control.CursorShape.PointingHand;
			var taking = entry;
			tile.GuiInput += input =>
			{
				if (input is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
				{
					Took(_campaign.Take(container.Id, taking, taking.Count));
				}
			};
			grid.AddChild(tile);
		}

		if (!container.Money.IsEmpty)
		{
			var coins = new Button { Text = $"Take the coins: {container.Money}", FocusMode = Control.FocusModeEnum.None, SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin };
			coins.Pressed += () => Took(_campaign.TakeCoins(container.Id));
			section.AddChild(coins);
		}

		return section;
	}

	private void Took(GearResult result)
	{
		if (!result)
		{
			Refuse(result.Line);
			return;
		}

		LogText($"— {result.Line} —\n");
		RefreshLoot();
		RefreshContainers();
		RefreshSheet();
		RefreshControls();
	}

	private void TakeAllShown()
	{
		foreach (var id in _lootShown)
		{
			if (_campaign.GetContainer(id) is { IsEmpty: false } && _campaign.TakeAll(id) is { Success: true } taken)
			{
				LogText($"— {taken.Line} —\n");
			}
		}

		CloseLoot();
		RefreshSheet();
		UpdateStatus();

		var load = Encumbrance.PartyLoad(_campaign);
		if (load.Category != LoadCategory.Light)
		{
			Prompt(load.Line);
		}
	}
}
