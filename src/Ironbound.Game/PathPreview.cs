using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Encounters;
using Ironbound.Rules.Encounters.Actions;
using Ironbound.Rules.Maps;
using Ironbound.Simulation;

/// <summary>
/// The part of <see cref="Main"/> that shows where a move will go before it is made: a glowing
/// pipe along the ground from the mover's feet, through the squares the rules will walk them,
/// to where they will stop.
/// </summary>
/// <remarks>
/// The project owner's words: "when I move the mouse where to move, I should see the path where
/// the character will go." Before this the only answer was a green or red square under the cursor
/// and a field of tinted squares, which said whether a click was allowed but not what it would do
/// — which way round the pillar, whether through the goblin's reach, where a too-long move ends.
/// <para>
/// The path drawn is the one the click will send, built by the same code: in a fight the rules'
/// own path and the longest part of it this turn pays for. A stretch beyond this move is drawn on in red, so a far click shows both how far
/// you get now and where the rest would go; a square whose leaving draws an attack of opportunity
/// carries a warning mark. And a click on a square too far away now walks as far as the move
/// allows along that path instead of being refused.
/// </para>
/// <para>
/// Fights only. Between them the party just walks, and the owner did not want a line drawn for it.
/// </para>
/// </remarks>
public partial class Main
{
	private static readonly Color PathGo = new(0.55f, 0.85f, 1.0f, 0.95f);
	private static readonly Color PathBeyond = new(0.95f, 0.30f, 0.25f, 0.70f);
	private static readonly Color PathWarn = new(1.0f, 0.35f, 0.20f, 1.0f);

	private const float PipeRadius = 0.045f;
	private const float PipeHeight = 0.07f;

	private MeshInstance3D _pathPipe;
	private MeshInstance3D _pathEnd;
	private StandardMaterial3D _pathEndPaint;
	private MultiMeshInstance3D _pathWarnings;
	private Label3D _pathLabel;
	private (GridSquare Square, GridSquare From, int Mode)? _pathShown;

	/// <summary>Builds the pieces once per board; RebuildWorld calls it after the cursor.</summary>
	private void BuildPathPreview()
	{
		_pathShown = null;

		var shader = GD.Load<Shader>("res://path.gdshader");
		_pathPipe = new MeshInstance3D
		{
			MaterialOverride = shader is null
				? new StandardMaterial3D { VertexColorUseAsAlbedo = true, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded }
				: new ShaderMaterial { Shader = shader },
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			Visible = false,
		};
		_world.AddChild(_pathPipe);

		_pathEndPaint = new StandardMaterial3D
		{
			AlbedoColor = PathGo,
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
		};
		_pathEnd = new MeshInstance3D
		{
			Mesh = new TorusMesh { InnerRadius = 0.30f, OuterRadius = 0.38f, Rings = 32, RingSegments = 6 },
			MaterialOverride = _pathEndPaint,
			Scale = new Vector3(1f, 0.25f, 1f),
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			Visible = false,
		};
		_world.AddChild(_pathEnd);

		// A small upright diamond over each square whose leaving provokes.
		_pathWarnings = new MultiMeshInstance3D
		{
			Multimesh = new MultiMesh
			{
				TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
				Mesh = new PrismMesh { Size = new Vector3(0.16f, 0.16f, 0.04f) },
				InstanceCount = 0,
			},
			MaterialOverride = new StandardMaterial3D
			{
				AlbedoColor = PathWarn,
				ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			},
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		};
		_world.AddChild(_pathWarnings);

		_pathLabel = new Label3D
		{
			Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
			FontSize = 40,
			PixelSize = 0.004f,
			OutlineSize = 10,
			NoDepthTest = true,
			Visible = false,
		};
		_world.AddChild(_pathLabel);
	}

	private void HidePath()
	{
		_pathShown = null;
		if (_pathPipe is null || !IsInstanceValid(_pathPipe))
		{
			return;
		}

		_pathPipe.Visible = false;
		_pathEnd.Visible = false;
		_pathLabel.Visible = false;
		_pathWarnings.Multimesh.InstanceCount = 0;
	}

	// ---- in a fight ----

	/// <summary>
	/// What a click on <paramref name="square"/> in Move mode will do, and the whole path it
	/// follows: the move the rules accept along the longest part of the path this turn pays for,
	/// or null if not even one step of it can be taken. <paramref name="reach"/> is the index in
	/// <paramref name="path"/> where the move stops.
	/// </summary>
	private GameAction MovePlan(Creature actor, GridSquare square, out IReadOnlyList<GridSquare> path, out int reach)
	{
		path = [];
		reach = 0;
		if (_battle.Battlefield is not { } field || field.SquareOf(actor) is not { } from || from == square)
		{
			return null;
		}

		path = field.FindPath(from, square, actor);
		if (path.Count < 2)
		{
			return null;
		}

		for (var k = path.Count - 1; k >= 1; k--)
		{
			var part = path.Take(k + 1).ToList();

			// The same choice ActionFor makes: one square is a five-foot step if it can be,
			// otherwise a move.
			GameAction[] tries = part.Count == 2 && field.PathCost(part) <= Distance.FeetPerSquare
				? [new FiveFootStepAction(part), new MoveAction(part)]
				: [new MoveAction(part)];

			foreach (var action in tries)
			{
				if (_battle.CanAct(action))
				{
					reach = k;
					return action;
				}
			}
		}

		return null;
	}

	/// <summary>Draws the fight's move from the actor to the hovered square. Called when the hovered square changes.</summary>
	private void PreviewMove(Creature actor, GridSquare square)
	{
		if (_mode != Mode.Move || _battle.Battlefield is not { } field || field.SquareOf(actor) is not { } from)
		{
			HidePath();
			return;
		}

		var key = (square, from, (int)_mode);
		if (_pathShown == key)
		{
			return;
		}

		var action = MovePlan(actor, square, out var path, out var reach);
		if (path.Count < 2)
		{
			HidePath();
			return;
		}

		_pathShown = key;

		// Where leaving draws a swing: any square before the stop that an enemy on its feet
		// threatens. A five-foot step provokes nothing.
		var warn = new List<GridSquare>();
		if (action is not FiveFootStepAction)
		{
			var enemies = _battle.Encounter.Order
				.Select(one => one.Creature)
				.Where(one => one.IsConscious && actor.IsEnemyOf(one))
				.ToList();
			for (var i = 0; i < reach; i++)
			{
				if (enemies.Any(enemy => field.Threatens(enemy, path[i])))
				{
					warn.Add(path[i]);
				}
			}
		}

		var feet = reach > 0 ? field.PathCost(path.Take(reach + 1).ToList()) : 0;
		var label = action is null ? "too far"
			: reach < path.Count - 1 ? $"{feet} ft — stops here"
			: action is FiveFootStepAction ? "5-ft step"
			: $"{feet} ft";
		if (warn.Count > 0)
		{
			label += warn.Count == 1 ? " · provokes" : $" · provokes ×{warn.Count}";
		}

		DrawPath(path, reach, PathGo, label, warn);
	}

	// ---- the drawing ----

	/// <summary>
	/// Lays the pipe along <paramref name="path"/>: drawn in <paramref name="go"/> as far as
	/// index <paramref name="reach"/> and in red past it, rounded through the corners, ending in a
	/// ring where the walk stops.
	/// </summary>
	private void DrawPath(IReadOnlyList<GridSquare> path, int reach, Color go, string label, IReadOnlyList<GridSquare> warnings)
	{
		var points = path.Select(at => new Vector3(at.X + 0.5f, PipeHeight, at.Y + 0.5f)).ToList();

		// Catmull-Rom through the square centres: a turn is a curve, not an elbow. Each sample
		// remembers which step of the path it is on, for its colour.
		var samples = new List<(Vector3 At, int Step)>();
		const int per = 8;
		for (var i = 0; i < points.Count - 1; i++)
		{
			var p0 = points[Math.Max(i - 1, 0)];
			var p1 = points[i];
			var p2 = points[i + 1];
			var p3 = points[Math.Min(i + 2, points.Count - 1)];
			for (var s = 0; s < per; s++)
			{
				var t = s / (float)per;
				var t2 = t * t;
				var t3 = t2 * t;
				var at = 0.5f * ((2f * p1) + ((-p0 + p2) * t) + (((2f * p0) - (5f * p1) + (4f * p2) - p3) * t2) + ((-p0 + (3f * p1) - (3f * p2) + p3) * t3));
				samples.Add((at, i));
			}
		}

		samples.Add((points[^1], points.Count - 1));

		// Start a little out from the mover's feet rather than inside them.
		while (samples.Count > 2 && samples[0].At.DistanceTo(points[0]) < 0.28f && samples[1].At.DistanceTo(points[0]) < 0.28f)
		{
			samples.RemoveAt(0);
		}

		var verts = new List<Vector3>();
		var colours = new List<Color>();
		var uvs = new List<Vector2>();
		var indices = new List<int>();
		const int sides = 8;
		var along = 0f;
		var total = 0f;
		for (var i = 1; i < samples.Count; i++)
		{
			total += samples[i].At.DistanceTo(samples[i - 1].At);
		}

		for (var i = 0; i < samples.Count; i++)
		{
			var at = samples[i].At;
			if (i > 0)
			{
				along += at.DistanceTo(samples[i - 1].At);
			}

			var ahead = samples[Math.Min(i + 1, samples.Count - 1)].At - samples[Math.Max(i - 1, 0)].At;
			var tangent = ahead.LengthSquared() > 1e-6f ? ahead.Normalized() : Vector3.Forward;
			var side = tangent.Cross(Vector3.Up).Normalized();

			// Tapered into a point over the last third of a square: the head of the snake.
			var left = total - along;
			var radius = PipeRadius * Mathf.Clamp(left / 0.33f, 0.15f, 1f);
			var colour = samples[i].Step < reach || (samples[i].Step == reach && i == samples.Count - 1 && reach == path.Count - 1) ? go : PathBeyond;

			for (var r = 0; r <= sides; r++)
			{
				var angle = Mathf.Pi * r / sides;
				var offset = (side * Mathf.Cos(angle)) + (Vector3.Up * Mathf.Sin(angle));
				verts.Add(at + (offset * radius));
				colours.Add(colour);
				uvs.Add(new Vector2(along, r / (float)sides));
			}
		}

		for (var i = 0; i < samples.Count - 1; i++)
		{
			for (var r = 0; r < sides; r++)
			{
				var a = (i * (sides + 1)) + r;
				var b = a + sides + 1;
				indices.AddRange([a, b, a + 1, a + 1, b, b + 1]);
			}
		}

		var arrays = new Godot.Collections.Array();
		arrays.Resize((int)Mesh.ArrayType.Max);
		arrays[(int)Mesh.ArrayType.Vertex] = verts.ToArray();
		arrays[(int)Mesh.ArrayType.Color] = colours.ToArray();
		arrays[(int)Mesh.ArrayType.TexUV] = uvs.ToArray();
		arrays[(int)Mesh.ArrayType.Index] = indices.ToArray();
		var mesh = new ArrayMesh();
		if (verts.Count > 0)
		{
			mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
		}

		_pathPipe.Mesh = mesh;
		_pathPipe.Visible = verts.Count > 0;

		// The ring goes where the walk stops, which is not the cursor when the move falls short.
		var stop = path[Math.Clamp(reach, 0, path.Count - 1)];
		_pathEnd.Visible = reach > 0;
		_pathEnd.Position = new Vector3(stop.X + 0.5f, 0.03f, stop.Y + 0.5f);
		_pathEndPaint.AlbedoColor = go;

		_pathWarnings.Multimesh.InstanceCount = warnings.Count;
		for (var i = 0; i < warnings.Count; i++)
		{
			_pathWarnings.Multimesh.SetInstanceTransform(i, new Transform3D(
				Basis.Identity,
				new Vector3(warnings[i].X + 0.5f, 0.35f, warnings[i].Y + 0.5f)));
		}

		_pathLabel.Visible = label is not null;
		if (label is not null)
		{
			var end = path[^1];
			_pathLabel.Text = label;
			_pathLabel.Modulate = reach == path.Count - 1 ? Colors.White : new Color(1f, 0.75f, 0.70f);
			_pathLabel.Position = new Vector3(end.X + 0.5f, 0.55f, end.Y + 0.5f);
		}
	}

	// ---- the outlines: the cursor's square and the ground within reach ----

	/// <summary>
	/// A set of squares drawn as a faint wash with a bright line round its edge — where the
	/// reach of a move, a blade or a spell ends — rather than as a field of tinted tiles.
	/// </summary>
	private static ArrayMesh Outline(IReadOnlyCollection<GridSquare> squares, Color colour, float wash, float width)
	{
		var set = squares as HashSet<GridSquare> ?? [.. squares];
		var verts = new List<Vector3>();
		var colours = new List<Color>();
		var fill = new Color(colour, wash);
		var edge = new Color(colour, Mathf.Max(colour.A, 0.85f));

		void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color paint)
		{
			verts.AddRange([a, b, c, a, c, d]);
			for (var k = 0; k < 6; k++)
			{
				colours.Add(paint);
			}
		}

		foreach (var at in set)
		{
			float x0 = at.X, z0 = at.Y, x1 = at.X + 1, z1 = at.Y + 1;
			const float y = 0.016f;
			if (wash > 0f)
			{
				Quad(new Vector3(x0, y, z0), new Vector3(x1, y, z0), new Vector3(x1, y, z1), new Vector3(x0, y, z1), fill);
			}

			// A line along every side that faces out of the set.
			const float ye = 0.018f;
			if (!set.Contains(new GridSquare(at.X, at.Y - 1)))
			{
				Quad(new Vector3(x0, ye, z0), new Vector3(x1, ye, z0), new Vector3(x1, ye, z0 + width), new Vector3(x0, ye, z0 + width), edge);
			}

			if (!set.Contains(new GridSquare(at.X, at.Y + 1)))
			{
				Quad(new Vector3(x0, ye, z1 - width), new Vector3(x1, ye, z1 - width), new Vector3(x1, ye, z1), new Vector3(x0, ye, z1), edge);
			}

			if (!set.Contains(new GridSquare(at.X - 1, at.Y)))
			{
				Quad(new Vector3(x0, ye, z0), new Vector3(x0 + width, ye, z0), new Vector3(x0 + width, ye, z1), new Vector3(x0, ye, z1), edge);
			}

			if (!set.Contains(new GridSquare(at.X + 1, at.Y)))
			{
				Quad(new Vector3(x1 - width, ye, z0), new Vector3(x1, ye, z0), new Vector3(x1, ye, z1), new Vector3(x1 - width, ye, z1), edge);
			}
		}

		var mesh = new ArrayMesh();
		if (verts.Count == 0)
		{
			return mesh;
		}

		var arrays = new Godot.Collections.Array();
		arrays.Resize((int)Mesh.ArrayType.Max);
		arrays[(int)Mesh.ArrayType.Vertex] = verts.ToArray();
		arrays[(int)Mesh.ArrayType.Color] = colours.ToArray();
		mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
		return mesh;
	}

	private static StandardMaterial3D OutlinePaint() => new()
	{
		VertexColorUseAsAlbedo = true,
		ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
		Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
		CullMode = BaseMaterial3D.CullModeEnum.Disabled,
	};
}
