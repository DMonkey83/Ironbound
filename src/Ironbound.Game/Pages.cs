using System;
using Godot;

/// <summary>
/// The part of <see cref="Main"/> that tells the story between the fights: a page of parchment
/// laid over the board, in ink, with a heading and a button to read on.
/// </summary>
/// <remarks>
/// After Wrath's event pages, which the project owner pointed at: dark ink on old paper, small
/// capitals for the kicker, the first letter of the heading in red, a bronze-ruled button at the
/// foot. It carries everything the campaign files say in prose — the merchant's warning, the cut
/// rope bridge, the storeroom — which until now went into the log in one grey line, if anywhere.
/// <para>
/// The fight under it waits: nothing moves until the page is dismissed, so an enemy that wins
/// initiative does not take its turn while the player is still reading why it is there.
/// </para>
/// </remarks>
public partial class Main
{
	private static readonly Color PageInk = new(0.20f, 0.12f, 0.15f);
	private static readonly Color InkRed = new(0.55f, 0.09f, 0.11f);

	private CanvasLayer _page;

	/// <summary>Whether this run is one nobody is reading: scripted, recorded, or headless.</summary>
	private static bool Unattended()
	{
		var args = OS.GetCmdlineUserArgs();
		return DisplayServer.GetName() == "headless"
			|| Array.IndexOf(args, "--autoplay") >= 0
			|| Array.IndexOf(args, "--camera-tour") >= 0
			|| Array.IndexOf(args, "--pass-turns") >= 0;
	}

	/// <summary>
	/// Lays a page over everything, and calls <paramref name="then"/> when it is put away — at
	/// once, with no page, if there is nothing to read or nobody to read it.
	/// </summary>
	private void ShowPage(string kicker, string heading, string body, string button, Action then)
	{
		if (string.IsNullOrWhiteSpace(body) || Unattended())
		{
			then();
			return;
		}

		_page?.QueueFree();
		_page = new CanvasLayer { Layer = 20 };
		AddChild(_page);

		var root = new Control { MouseFilter = Control.MouseFilterEnum.Stop };
		root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		_page.AddChild(root);
		root.AddChild(new ColorRect { Color = new Color(0, 0, 0, 0.55f), AnchorRight = 1, AnchorBottom = 1, MouseFilter = Control.MouseFilterEnum.Ignore });

		var sheet = new PanelContainer { CustomMinimumSize = new Vector2(860, 0) };
		sheet.AddThemeStyleboxOverride("panel", ParchmentPlate(48, 40));
		var centre = new CenterContainer();
		centre.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		centre.MouseFilter = Control.MouseFilterEnum.Ignore;
		root.AddChild(centre);
		centre.AddChild(sheet);

		var column = new VBoxContainer();
		column.AddThemeConstantOverride("separation", 14);
		sheet.AddChild(column);

		if (kicker.Length > 0)
		{
			column.AddChild(Words(kicker.ToUpperInvariant(), Display, 18, InkRed));
		}

		column.AddChild(Illuminated(heading, 46));
		column.AddChild(Rule(760));

		var text = new RichTextLabel
		{
			BbcodeEnabled = false,
			FitContent = true,
			ScrollActive = false,
			Text = body,
			CustomMinimumSize = new Vector2(760, 0),
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		text.AddThemeFontOverride("normal_font", Body);
		text.AddThemeFontSizeOverride("normal_font_size", 21);
		text.AddThemeColorOverride("default_color", PageInk);
		text.AddThemeConstantOverride("line_separation", 6);
		column.AddChild(text);

		var foot = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
		column.AddChild(foot);
		var go = PlateButton(button);
		go.Pressed += () =>
		{
			_page?.QueueFree();
			_page = null;
			then();
		};
		foot.AddChild(go);
	}

	/// <summary>A heading with its first letter set large and red, as the illuminated capitals
	/// on Wrath's pages are.</summary>
	private static Control Illuminated(string heading, int size)
	{
		var line = new HBoxContainer();
		line.AddThemeConstantOverride("separation", 0);
		if (heading.Length == 0)
		{
			return line;
		}

		var initial = Words(heading[..1], DisplayBold, (int)(size * 1.35f), InkRed);
		var rest = Words(heading[1..], Display, size, PageInk);
		rest.SizeFlagsVertical = Control.SizeFlags.ShrinkEnd;
		line.AddChild(initial);
		line.AddChild(rest);
		return line;
	}

	/// <summary>The paper itself: the rendered parchment, stretched, with a dark rim.</summary>
	private static StyleBox ParchmentPlate(int padX, int padY)
	{
		const string paper = "res://art/menu/parchment.png";
		if (ResourceLoader.Exists(paper))
		{
			return new StyleBoxTexture
			{
				Texture = GD.Load<Texture2D>(paper),
				ContentMarginLeft = padX,
				ContentMarginRight = padX,
				ContentMarginTop = padY,
				ContentMarginBottom = padY,
				ModulateColor = new Color(1, 1, 1, 0.98f),
			};
		}

		return Plate(new Color(0.88f, 0.83f, 0.72f), new Color(0.25f, 0.18f, 0.20f), 3, 4, padX);
	}

	/// <summary>The button Wrath puts on paper: a small dark plate in muted plum, small capitals
	/// in pale ink.</summary>
	private static Button PlateButton(string text)
	{
		var button = new Button { Text = text.ToUpperInvariant(), FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(180, 46) };
		button.AddThemeFontOverride("font", Display);
		button.AddThemeFontSizeOverride("font_size", 20);
		button.AddThemeColorOverride("font_color", new Color(0.93f, 0.88f, 0.92f));
		button.AddThemeColorOverride("font_hover_color", Colors.White);
		var plum = new Color(0.33f, 0.24f, 0.33f);
		button.AddThemeStyleboxOverride("normal", Plate(plum, new Color(0.16f, 0.10f, 0.15f), 2, 3, 12));
		button.AddThemeStyleboxOverride("hover", Plate(plum.Lightened(0.15f), BronzeBright, 2, 3, 12));
		button.AddThemeStyleboxOverride("pressed", Plate(plum.Darkened(0.2f), BronzeBright, 2, 3, 12));
		button.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
		return button;
	}
}
