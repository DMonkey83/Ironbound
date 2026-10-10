using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Ironbound.Rules.Items;
using Ironbound.Rules.Maps;
using Ironbound.Simulation;
using Pricing = Ironbound.Rules.Items.Pricing;

/// <summary>
/// The part of <see cref="Main"/> that is the merchants: Tobin by his overturned cart at the cave
/// mouth and Wenna's cart where the Long Road starts to climb, drawn where the level puts them,
/// walked up to and clicked like a chest, and the window you trade in.
/// </summary>
/// <remarks>
/// The owner chose merchants in the world rather than a trader button, and book prices: the
/// party buys at the price, sells at half, gems and art at their full worth, and no single thing
/// fetches more than the place can pay. The window is the loot window's paper laid out in two
/// columns — what they have, what you have — so clicking means one thing on each side.
/// </remarks>
public partial class Main
{
	private readonly Dictionary<string, Node3D> _merchantFigures = new();
	private CanvasLayer _tradeLayer;
	private string _trading;

	/// <summary>Squares that hold a merchant's stall or the merchant: no crate is drawn on them.</summary>
	private bool IsMerchantSquare(GridSquare square) =>
		_campaign.Merchants.Any(merchant => merchant.Squares.Contains(square));

	/// <summary>Each merchant's cart and the merchant beside it, with a name over them that stays up.</summary>
	private void DrawMerchants()
	{
		_merchantFigures.Clear();
		foreach (var merchant in _campaign.Merchants)
		{
			var stall = merchant.Squares.Where(square => square != merchant.StandsAt).ToList();
			if (stall.Count > 0 && PropModel(merchant.Feature.Stall) is { } cart)
			{
				_world.AddChild(cart);
				var middle = Middle(stall);
				cart.Position = new Vector3(middle.X, 0, middle.Y);

				// Lengthways along the squares it covers.
				if (stall.Count > 1 && stall[0].Y == stall[^1].Y)
				{
					cart.RotateY(Mathf.Pi / 2);
				}
			}

			var holder = new Node3D { Name = $"Merchant_{merchant.Id}" };
			_world.AddChild(holder);
			var at = merchant.StandsAt;
			holder.Position = new Vector3(at.X + 0.5f, 0, at.Y + 0.5f);
			holder.AddChild(MerchantFigure(merchant));

			// Facing away from the cart, towards whoever comes to trade.
			if (stall.Count > 0)
			{
				var away = new Vector2(at.X + 0.5f, at.Y + 0.5f) - Middle(stall);
				holder.Rotation = new Vector3(0, Mathf.Atan2(away.X, away.Y), 0);
			}

			var plate = new Label3D
			{
				Text = merchant.Name,
				Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
				FontSize = 44,
				PixelSize = 0.005f,
				Modulate = new Color(0.95f, 0.88f, 0.70f),
				OutlineModulate = new Color(0.10f, 0.06f, 0.03f),
				OutlineSize = 10,
				Position = new Vector3(0, 1.75f, 0),
			};
			plate.Scale = Vector3.One * LabelScale();
			holder.AddChild(plate);
			_merchantFigures[merchant.Id] = holder;
		}
	}

	/// <summary>The merchant's own model if there is one yet, idling; a plain robed shape if not.</summary>
	private static Node3D MerchantFigure(Merchant merchant)
	{
		var path = merchant.Feature.Model;
		if (path.Length > 0 && ResourceLoader.Exists(path) && GD.Load<PackedScene>(path).Instantiate() is Node3D model)
		{
			if (model.FindChildren("*", "AnimationPlayer", true, false).OfType<AnimationPlayer>().FirstOrDefault() is { } player)
			{
				var idle = player.GetAnimationList().FirstOrDefault(clip => clip.Contains("idle", StringComparison.OrdinalIgnoreCase));
				if (idle is not null)
				{
					player.GetAnimation(idle).LoopMode = Animation.LoopModeEnum.Linear;
					player.Play(idle);
				}
			}

			return model;
		}

		var robe = new MeshInstance3D
		{
			Mesh = new CapsuleMesh { Radius = 0.22f, Height = 1.35f },
			MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.45f, 0.36f, 0.26f), Roughness = 0.9f },
			Position = new Vector3(0, 0.675f, 0),
		};
		return robe;
	}

	// ---- the window ----

	/// <summary>Opens the trade window on a merchant somebody is standing beside.</summary>
	private void ShowTrade(Merchant merchant)
	{
		// Nobody is there to haggle on an autoplay run: the shops are for players.
		if (Unattended() && !OS.GetCmdlineUserArgs().Contains("--show-trade"))
		{
			return;
		}

		_trading = merchant.Id;
		RefreshTrade();
	}

	private void CloseTrade()
	{
		_tradeLayer?.QueueFree();
		_tradeLayer = null;
		_trading = null;
		RefreshSheet();
		RefreshControls();
		UpdateStatus();
	}

	private void RefreshTrade()
	{
		_tradeLayer?.QueueFree();
		_tradeLayer = null;
		if (_trading is null || _campaign.GetMerchant(_trading) is not { } merchant)
		{
			return;
		}

		_tradeLayer = new CanvasLayer { Layer = 18 };
		AddChild(_tradeLayer);

		var root = new Control { MouseFilter = Control.MouseFilterEnum.Stop };
		root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		_tradeLayer.AddChild(root);
		root.AddChild(new ColorRect { Color = new Color(0, 0, 0, 0.45f), AnchorRight = 1, AnchorBottom = 1, MouseFilter = Control.MouseFilterEnum.Ignore });

		var centre = new CenterContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
		centre.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		root.AddChild(centre);

		var sheet = new PanelContainer { CustomMinimumSize = new Vector2(1180, 0) };
		sheet.AddThemeStyleboxOverride("panel", ParchmentPlate(40, 30));
		centre.AddChild(sheet);

		var column = new VBoxContainer();
		column.AddThemeConstantOverride("separation", 10);
		sheet.AddChild(column);
		column.AddChild(Illuminated(merchant.Name, 40));
		column.AddChild(Rule(1090));

		// What they say on being greeted, in their own voice, where the trading is done.
		if (merchant.Feature.Text.Length > 0)
		{
			var greeting = Words(merchant.Feature.Text, BodyItalic, 18, PageInk);
			greeting.AutowrapMode = TextServer.AutowrapMode.WordSmart;
			greeting.CustomMinimumSize = new Vector2(1090, 0);
			column.AddChild(greeting);
		}

		var who = Wearer();
		var across = new HBoxContainer();
		across.AddThemeConstantOverride("separation", 24);
		column.AddChild(across);

		var limit = Pricing.Format(merchant.PurchaseLimit);
		across.AddChild(TradeSide(
			"For sale",
			merchant.Stock,
			line => $"{Pricing.Format(Trade.Asking(line.Item))}",
			line => _campaign.WhyNotBuy(merchant.Id, line),
			(line, all) => Traded(_campaign.Buy(merchant.Id, line, all ? line.Count : 1)),
			who,
			"Click to buy one; Shift-click to buy them all."));

		across.AddChild(TradeSide(
			"Your bag",
			BagLines(),
			line => _campaign.OfferFor(merchant.Id, line) is > 0 and var offer ? Pricing.Format(offer) : "—",
			line => _campaign.WhyNotSell(merchant.Id, line),
			(line, all) => Traded(_campaign.Sell(merchant.Id, line, all ? line.Count : 1)),
			who,
			$"Click to sell one; Shift-click to sell them all. {merchant.Name} pays half what a thing is worth, a gem or a piece of art its full worth, and at most {limit} for any one thing."));

		var purse = _campaign.Bag.Money.IsEmpty ? "no coin" : _campaign.Bag.Money.ToString();
		var load = Encumbrance.PartyLoad(_campaign);
		var footer = Words($"Purse: {purse}\n{load.Line}", Body, 15, load.Category == LoadCategory.Light ? PageInk : InkRed);
		footer.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		footer.CustomMinimumSize = new Vector2(1090, 0);
		column.AddChild(footer);

		var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
		column.AddChild(buttons);
		var close = PlateButton("Close");
		close.Pressed += CloseTrade;
		buttons.AddChild(close);
	}

	/// <summary>
	/// One column of the window: a heading, the things as tiles with what each costs or fetches
	/// in the corner, and why not, in the tooltip, for anything that cannot change hands.
	/// </summary>
	private Control TradeSide(
		string heading,
		IReadOnlyList<BagEntry> lines,
		Func<BagEntry, string> price,
		Func<BagEntry, string> whyNot,
		Action<BagEntry, bool> trade,
		Ironbound.Rules.Creatures.Creature who,
		string hint)
	{
		var side = new VBoxContainer { CustomMinimumSize = new Vector2(530, 0) };
		side.AddThemeConstantOverride("separation", 6);
		side.AddChild(Words(heading, Display, 22, PageInk));

		var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(530, 330), HorizontalScrollMode = ScrollContainer.ScrollMode.ShowNever };
		side.AddChild(scroll);
		var grid = new GridContainer { Columns = 7 };
		grid.AddThemeConstantOverride("h_separation", 6);
		grid.AddThemeConstantOverride("v_separation", 6);
		scroll.AddChild(grid);

		if (lines.Count == 0)
		{
			side.AddChild(Words("Nothing.", BodyItalic, 18, PageInk.Lightened(0.3f)));
		}

		foreach (var line in lines)
		{
			var described = who is null ? _content.DescribeItem(line.Item) : _content.DescribeItem(line.Item, who);
			var refused = whyNot(line);
			var tile = Tile(line.Item, line.Count, string.Empty, verdict: 0, warn: Unfit(described), picked: false, broken: line.IsBroken);
			if (refused is not null)
			{
				tile.Modulate = new Color(1, 1, 1, 0.55f);
			}

			// The price under the tile rather than on it, where the count already sits.
			var cell = new VBoxContainer();
			cell.AddThemeConstantOverride("separation", 2);
			cell.AddChild(tile);
			var cost = Words(price(line), Body, 13, refused is null ? PageInk : PageInk.Lightened(0.4f));
			cost.HorizontalAlignment = HorizontalAlignment.Center;
			cost.CustomMinimumSize = new Vector2(TilePixels, 0);
			cell.AddChild(cost);

			tile.TooltipText = string.Join("\n", described
				.Prepend(line.Count > 1 ? $"{line.Item.Name} ×{line.Count}" : line.Item.Name)
				.Append(refused ?? hint));
			tile.MouseDefaultCursorShape = refused is null ? Control.CursorShape.PointingHand : Control.CursorShape.Arrow;

			var chosen = line;
			tile.GuiInput += input =>
			{
				if (input is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } click)
				{
					trade(chosen, click.ShiftPressed);
				}
			};
			grid.AddChild(cell);
		}

		return side;
	}

	private void Traded(GearResult result)
	{
		if (!result)
		{
			Refuse(result.Line);
			return;
		}

		LogText($"— {result.Line} —\n");
		RefreshTrade();
		RefreshSheet();
		UpdateStatus();
	}
}
