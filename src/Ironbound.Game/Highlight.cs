using System.Collections.Generic;
using System.Linq;
using Godot;
using Ironbound.Rules.Content;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Maps;
using Ironbound.Simulation;
using Container = Ironbound.Simulation.Container;

/// <summary>
/// The part of <see cref="Main"/> that lights up whatever the pointer is over that a click would
/// do something with: a chest, a crate, a body with something on it, a shut door.
/// </summary>
/// <remarks>
/// The owner asked for it, and Wrath does it: a soft gold glow on the thing itself, a rim of
/// light round its edge, and its name over it, so finding the loot in a dim cave is a matter of
/// moving the mouse rather than squinting. Only between fights; in a fight the pointer is for
/// choosing whom to hit, and the cursor already says that.
/// <para>
/// One overlay material on every mesh of the thing, put on and taken off as the pointer comes
/// and goes, so nothing about the models themselves changes.
/// </para>
/// </remarks>
public partial class Main
{
	private static readonly Color GlowColour = new(1.0f, 0.78f, 0.40f);

	private static ShaderMaterial _glow;

	/// <summary>What is lit now, and the meshes it was put on, to take it off again.</summary>
	private Node3D _lit;
	private readonly List<GeometryInstance3D> _litMeshes = new();
	private Label3D _litName;
	private readonly List<Label3D> _litHidden = new();

	/// <summary>
	/// A warm wash with a brighter rim, breathing slowly, added on top of whatever the thing is
	/// made of; and a second pass a hair larger, back faces only, which reads as an outline.
	/// </summary>
	private static ShaderMaterial Glow()
	{
		if (_glow is not null)
		{
			return _glow;
		}

		// Behind a tree or a pillar the thing would vanish, glow and all; a faint silhouette
		// drawn through everything keeps its shape on the screen.
		var through = new ShaderMaterial
		{
			Shader = new Shader
			{
				Code = """
					shader_type spatial;
					render_mode unshaded, cull_back, depth_draw_never, depth_test_disabled;
					uniform vec4 glow : source_color;
					void fragment() {
						ALBEDO = glow.rgb;
						ALPHA = 0.12;
					}
					""",
			},
		};
		through.SetShaderParameter("glow", GlowColour);

		var outline = new ShaderMaterial
		{
			Shader = new Shader
			{
				Code = """
					shader_type spatial;
					render_mode unshaded, cull_front, depth_draw_never;
					uniform vec4 glow : source_color;
					void vertex() {
						VERTEX += NORMAL * 0.012;
					}
					void fragment() {
						ALBEDO = glow.rgb;
						ALPHA = 0.85;
					}
					""",
			},
			NextPass = through,
		};
		outline.SetShaderParameter("glow", GlowColour);

		_glow = new ShaderMaterial
		{
			Shader = new Shader
			{
				Code = """
					shader_type spatial;
					render_mode blend_add, unshaded, cull_back, depth_draw_never;
					uniform vec4 glow : source_color;
					void fragment() {
						float rim = pow(1.0 - clamp(dot(NORMAL, VIEW), 0.0, 1.0), 2.0);
						float breathe = 0.8 + 0.2 * sin(TIME * 3.5);
						ALBEDO = glow.rgb * (0.07 + rim * 0.8) * breathe;
					}
					""",
			},
			NextPass = outline,
		};
		_glow.SetShaderParameter("glow", GlowColour);
		return _glow;
	}

	/// <summary>Called as the pointer moves between fights: lights what is under it, or nothing.</summary>
	private void HighlightAt(GridSquare? square)
	{
		var (thing, name) = square is { } at ? Interactable(at) : (null, null);
		if (ReferenceEquals(thing, _lit))
		{
			return;
		}

		Unlight();
		if (thing is null || !IsInstanceValid(thing))
		{
			return;
		}

		_lit = thing;
		foreach (var mesh in thing.FindChildren("*", "GeometryInstance3D", true, false).OfType<GeometryInstance3D>())
		{
			// The floating marks and labels are already lit; only the thing itself glows.
			if (mesh is Label3D or Sprite3D)
			{
				continue;
			}

			mesh.MaterialOverlay = Glow();
			_litMeshes.Add(mesh);
		}

		// The thing's own name plate, if it has one, gives way to the lit name for as long as it is lit.
		foreach (var plate in thing.GetChildren().OfType<Label3D>().Where(plate => plate.Visible))
		{
			plate.Visible = false;
			_litHidden.Add(plate);
		}

		_litName = new Label3D
		{
			Text = name,
			Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
			NoDepthTest = true,
			FontSize = 44,
			PixelSize = 0.005f,
			Modulate = GlowColour.Lightened(0.3f),
			OutlineModulate = new Color(0.12f, 0.06f, 0.02f),
			OutlineSize = 12,
			Position = new Vector3(0, 1.45f, 0),
		};
		_litName.Scale = Vector3.One * LabelScale();
		thing.AddChild(_litName);
	}

	/// <summary>Takes the glow off whatever had it.</summary>
	private void Unlight()
	{
		foreach (var mesh in _litMeshes.Where(IsInstanceValid))
		{
			mesh.MaterialOverlay = null;
		}

		_litMeshes.Clear();
		foreach (var plate in _litHidden.Where(IsInstanceValid))
		{
			plate.Visible = true;
		}

		_litHidden.Clear();
		if (_litName is not null && IsInstanceValid(_litName))
		{
			_litName.QueueFree();
		}

		_litName = null;
		_lit = null;
	}

	/// <summary>
	/// What on this square a click would use, and what to call it: a container still shut or not
	/// yet empty (a body by its figure), or a door not yet open.
	/// </summary>
	private (Node3D Thing, string Name) Interactable(GridSquare at)
	{
		if (_campaign.ContainerAt(at) is { } container && (!container.IsOpen || !container.IsEmpty))
		{
			var label = container.IsLocked ? $"{Capitalised(container.Name)} (locked)"
				: container.Look == ContainerLook.Body ? $"{Capitalised(container.Name)}: search"
				: Capitalised(container.Name);

			return container.Body is { } body && _figures.TryGetValue(body, out var figure)
				? (figure, label)
				: (_containerNodes.GetValueOrDefault(container.Id), label);
		}

		if (_campaign.IsLevel && _campaign.FeatureAt(at) is { Kind: FeatureKind.Merchant } stall
			&& _campaign.GetMerchant(stall.Id) is { } merchant && _merchantFigures.GetValueOrDefault(merchant.Id) is { } trader)
		{
			return (trader, merchant.IsOpen ? $"{merchant.Name}: trade" : merchant.Name);
		}

		if (_campaign.IsLevel && _campaign.FeatureAt(at) is { Kind: FeatureKind.Door } door && !_campaign.IsUsed(door.Id)
			&& _featureNodes.GetValueOrDefault(door.Id) is { Count: > 0 } slabs)
		{
			// A door is one slab per square; the glow goes on the one pointed at, the name on it.
			var index = door.Squares.ToList().IndexOf(at);
			return (slabs[System.Math.Clamp(index, 0, slabs.Count - 1)], Capitalised(door.Name));
		}

		return (null, null);
	}

	/// <summary>
	/// The fallen of a fight are on the board as themselves until the board is built again — after
	/// a load, say — and then only their glint was left. A body with no figure is laid out here.
	/// </summary>
	private void LayOut(Container body)
	{
		if (body.Body is not { } fallen || _figures.ContainsKey(fallen) || body.Squares.Count == 0 || _world is null)
		{
			return;
		}

		Spawn([fallen], FoeColour);
		if (_figures.TryGetValue(fallen, out var figure))
		{
			var at = body.Squares[0];
			figure.Position = new Vector3(at.X + 0.5f, 0, at.Y + 0.5f);
			Assume(fallen, figure, PostureOf(fallen), instant: true);
		}
	}
}
