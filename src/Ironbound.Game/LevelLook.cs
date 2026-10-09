using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Ironbound.Rules.Content;
using Ironbound.Rules.Maps;

/// <summary>
/// The part of <see cref="Main"/> that makes a level look like a place: the ground and the rock
/// as one sculpted surface, torches in the rooms, and a lantern carried by the party.
/// </summary>
/// <remarks>
/// The first cut laid one flat tile per square and stood a box on every wall square, and the
/// caves came out as a green field beside a row of grey bricks. A level is read from above at an
/// angle, at a distance, and what reads from there is shape and light: hollows in a dark mass of
/// rock, lit where somebody lives. So the walls are a heightfield that climbs out of the floor
/// and darkens as it rises, the floor is painted by a shader rather than tiled, and the light
/// comes from fires.
/// <para>
/// Nothing here is a fact the rules care about. A wall square is a wall whatever height it is
/// drawn, and the grid the fights are fought on is laid over all of it unchanged.
/// </para>
/// </remarks>
public partial class Main
{
	/// <summary>Squares of mountain drawn round the edge of the map, so it does not end in a cliff over nothing.</summary>
	private const int Margin = 4;

	/// <summary>Squares to a side of each piece the ground is cut into. Small enough that no
	/// piece is touched by more lights than the compatibility renderer will draw on one mesh.</summary>
	private const int Chunk = 12;

	private readonly List<(OmniLight3D Light, float Energy, float Phase)> _torches = new();
	private OmniLight3D _lantern;
	private DirectionalLight3D _sun;
	private Godot.Environment _air;
	private MeshInstance3D _gridPlane;

	/// <summary>Whether this level is underground: a third or more of it rock.</summary>
	private static bool IsCave(LevelDefinition level)
	{
		var walls = 0;
		for (var y = 0; y < level.Height; y++)
		{
			for (var x = 0; x < level.Width; x++)
			{
				walls += level.CellAt(x, y) == LevelCell.Wall ? 1 : 0;
			}
		}

		return walls * 3 >= level.Width * level.Height;
	}

	// ---- the ground ----

	private enum Lie { Flat, Raised, Sunk }

	private static Lie LieOf(LevelCell cell) => cell switch
	{
		LevelCell.Wall or LevelCell.Rock => Lie.Raised,
		LevelCell.Chasm => Lie.Sunk,
		_ => Lie.Flat,
	};

	/// <summary>
	/// Builds the level's ground as a heightfield, two points to a square, cut into chunks.
	/// </summary>
	/// <remarks>
	/// A point is on the floor if any square it touches is floor, so every walkable square stays
	/// flat to its edges and the rock rises only inside the wall squares — a face-on wall climbs
	/// from its edge to a ridge half a square in. Rock further from any floor stands higher, and
	/// a lone standing rock on open ground comes out as a boulder. A chasm is the same in reverse.
	/// </remarks>
	private void BuildGround(LevelDefinition level)
	{
		var w = level.Width + (Margin * 2);
		var h = level.Height + (Margin * 2);

		LevelCell Cell(int cx, int cy) => level.CellAt(cx - Margin, cy - Margin);

		// How many squares each raised square is from the nearest open one.
		var depth = new int[w, h];
		var queue = new Queue<(int X, int Y)>();
		for (var y = 0; y < h; y++)
		{
			for (var x = 0; x < w; x++)
			{
				if (LieOf(Cell(x, y)) == Lie.Raised)
				{
					depth[x, y] = int.MaxValue;
				}
				else
				{
					queue.Enqueue((x, y));
				}
			}
		}

		while (queue.Count > 0)
		{
			var (x, y) = queue.Dequeue();
			for (var dy = -1; dy <= 1; dy++)
			{
				for (var dx = -1; dx <= 1; dx++)
				{
					var nx = x + dx;
					var ny = y + dy;
					if (nx >= 0 && ny >= 0 && nx < w && ny < h && depth[nx, ny] == int.MaxValue)
					{
						depth[nx, ny] = depth[x, y] + 1;
						queue.Enqueue((nx, ny));
					}
				}
			}
		}

		// The points: (2w + 1) by (2h + 1), half a square apart.
		var pw = (w * 2) + 1;
		var ph = (h * 2) + 1;
		var height = new float[pw, ph];
		var green = new float[pw, ph];

		for (var j = 0; j < ph; j++)
		{
			for (var i = 0; i < pw; i++)
			{
				var flat = false;
				var sunk = true;
				var nearest = int.MaxValue;
				var boulder = true;
				var grass = 0;
				var open = 0;

				foreach (var cx in Touching(i, w))
				{
					foreach (var cy in Touching(j, h))
					{
						var cell = Cell(cx, cy);
						switch (LieOf(cell))
						{
							case Lie.Flat:
								flat = true;
								sunk = false;
								open++;
								grass += LevelDefinition.IsGrassGround(cell) ? 1 : 0;
								break;
							case Lie.Raised:
								sunk = false;
								nearest = Math.Min(nearest, depth[cx, cy] == int.MaxValue ? 6 : depth[cx, cy]);
								boulder &= cell == LevelCell.Rock;
								break;
							case Lie.Sunk:
								open++;
								break;
						}
					}
				}

				var jitter = Jitter(i, j);
				height[i, j] = flat ? 0f
					: sunk ? -0.9f
					: nearest == int.MaxValue ? 0f
					: boulder ? 0.55f + (0.25f * jitter)
					: nearest switch
					{
						1 => 0.50f + (0.25f * jitter),
						2 => 0.80f + (0.25f * jitter),
						_ => 1.0f + (0.2f * jitter),
					};

				// Grass by what the open squares round the point are; under rock, by the nearest
				// open ground's guess, which is whatever the map's edge row says.
				green[i, j] = open > 0 ? grass / (float)open
					: LevelDefinition.IsGrassGround(level.CellAt(Math.Clamp((i / 2) - Margin, 0, level.Width - 1), Math.Clamp((j / 2) - Margin, 0, level.Height - 1))) ? 1f : 0f;
			}
		}

		Material material = new StandardMaterial3D { VertexColorUseAsAlbedo = true };
		if (GD.Load<Shader>("res://level_ground.gdshader") is { } shader)
		{
			var painted = new ShaderMaterial { Shader = shader };
			painted.SetShaderParameter("paved", IsCave(level) ? 1.0f : 0.0f);
			material = painted;
		}

		for (var cy = 0; cy < h; cy += Chunk)
		{
			for (var cx = 0; cx < w; cx += Chunk)
			{
				var piece = GroundPiece(height, green, cx * 2, cy * 2, Math.Min((cx + Chunk) * 2, pw - 1), Math.Min((cy + Chunk) * 2, ph - 1));
				var instance = new MeshInstance3D { Mesh = piece, MaterialOverride = material };
				_world.AddChild(instance);
				instance.Position = new Vector3(-Margin, 0, -Margin);
			}
		}
	}

	/// <summary>The squares, along one axis, that the point at half-square index <paramref name="i"/> lies on the edge or inside of.</summary>
	private static IEnumerable<int> Touching(int i, int count)
	{
		if (i % 2 == 1)
		{
			yield return i / 2;
			yield break;
		}

		if (i / 2 - 1 >= 0)
		{
			yield return (i / 2) - 1;
		}

		if (i / 2 < count)
		{
			yield return i / 2;
		}
	}

	/// <summary>A repeatable number in 0..1 for a point, so the rock is the same rock every load.</summary>
	private static float Jitter(int i, int j)
	{
		var n = (uint)((i * 73856093) ^ (j * 19349663));
		n = (n ^ (n >> 13)) * 1274126177u;
		return (n & 0xffff) / 65535f;
	}

	private static ArrayMesh GroundPiece(float[,] height, float[,] green, int i0, int j0, int i1, int j1)
	{
		var pw = height.GetLength(0);
		var ph = height.GetLength(1);
		var across = i1 - i0 + 1;
		var verts = new List<Vector3>();
		var normals = new List<Vector3>();
		var colours = new List<Color>();
		var indices = new List<int>();

		float H(int i, int j) => height[Math.Clamp(i, 0, pw - 1), Math.Clamp(j, 0, ph - 1)];

		for (var j = j0; j <= j1; j++)
		{
			for (var i = i0; i <= i1; i++)
			{
				verts.Add(new Vector3(i * 0.5f, H(i, j), j * 0.5f));

				// From the whole field rather than the piece, so neighbouring pieces agree at the seam.
				var dx = (H(i + 1, j) - H(i - 1, j)) / 1.0f;
				var dz = (H(i, j + 1) - H(i, j - 1)) / 1.0f;
				normals.Add(new Vector3(-dx, 1f, -dz).Normalized());
				colours.Add(new Color(green[i, j], 0, 0));
			}
		}

		for (var j = 0; j < j1 - j0; j++)
		{
			for (var i = 0; i < i1 - i0; i++)
			{
				var a = (j * across) + i;
				var b = a + 1;
				var c = a + across;
				var d = c + 1;

				// Diagonals alternated, so ridges do not all lean the same way.
				if (((i + j) & 1) == 0)
				{
					indices.AddRange([a, b, d, a, d, c]);
				}
				else
				{
					indices.AddRange([a, b, c, b, d, c]);
				}
			}
		}

		var arrays = new Godot.Collections.Array();
		arrays.Resize((int)Mesh.ArrayType.Max);
		arrays[(int)Mesh.ArrayType.Vertex] = verts.ToArray();
		arrays[(int)Mesh.ArrayType.Normal] = normals.ToArray();
		arrays[(int)Mesh.ArrayType.Color] = colours.ToArray();
		arrays[(int)Mesh.ArrayType.Index] = indices.ToArray();

		var mesh = new ArrayMesh();
		mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
		return mesh;
	}

	// ---- the light ----

	/// <summary>
	/// Night outside and dark underground: a dim cool sun and ambient, a torch on the wall of
	/// every room that is under the rock, and the party's own lantern. Open-air levels keep the
	/// daylight they always had.
	/// </summary>
	private void LightLevel(LevelDefinition level)
	{
		_torches.Clear();
		_lantern = null;
		var cave = IsCave(level);

		if (_air is not null)
		{
			_air.AmbientLightEnergy = cave ? 0.22f : 0.35f;
			_air.AmbientLightColor = cave ? new Color(0.45f, 0.52f, 0.70f) : new Color(0.58f, 0.62f, 0.72f);
		}

		if (_sun is not null && cave)
		{
			_sun.LightEnergy = 0.35f;
			_sun.LightColor = new Color(0.62f, 0.72f, 1.0f);
		}

		if (!cave)
		{
			return;
		}

		foreach (var area in level.Areas)
		{
			if (Sconce(level, area) is not { } at)
			{
				continue;
			}

			// On the wall at the head of the room, a little out from it.
			var light = new OmniLight3D
			{
				LightColor = new Color(1.0f, 0.62f, 0.30f),
				LightEnergy = 2.2f,
				OmniRange = Math.Max(area.Width, area.Height) * 0.75f + 3f,
				OmniAttenuation = 1.2f,
			};
			_world.AddChild(light);
			light.Position = new Vector3(at.X + 0.5f, 1.3f, at.Y + 0.25f);
			_torches.Add((light, light.LightEnergy, (at.X * 1.7f) + at.Y));

			var post = new MeshInstance3D
			{
				Mesh = new CylinderMesh { TopRadius = 0.04f, BottomRadius = 0.05f, Height = 0.9f },
				MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.20f, 0.13f, 0.08f), Roughness = 0.9f },
			};
			_world.AddChild(post);
			post.Position = new Vector3(at.X + 0.5f, 0.45f, at.Y + 0.15f);

			var flame = new MeshInstance3D
			{
				Mesh = new SphereMesh { Radius = 0.09f, Height = 0.26f },
				MaterialOverride = new StandardMaterial3D
				{
					AlbedoColor = new Color(1.0f, 0.7f, 0.3f),
					EmissionEnabled = true,
					Emission = new Color(1.0f, 0.55f, 0.18f),
					EmissionEnergyMultiplier = 4f,
					ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
				},
			};
			_world.AddChild(flame);
			flame.Position = new Vector3(at.X + 0.5f, 0.98f, at.Y + 0.15f);
		}

		_lantern = new OmniLight3D
		{
			LightColor = new Color(1.0f, 0.86f, 0.62f),
			LightEnergy = 1.4f,
			OmniRange = 6.5f,
			OmniAttenuation = 1.1f,
		};
		_world.AddChild(_lantern);
		MoveLantern();
	}

	/// <summary>
	/// Where a room's torch goes: the floor square against its top wall nearest the middle, or
	/// failing that any floor square against a wall. Null for a room out under the sky.
	/// </summary>
	private static GridSquare? Sconce(LevelDefinition level, AreaDefinition area)
	{
		var floor = new List<GridSquare>();
		var grass = 0;
		for (var y = area.Y; y < area.Y + area.Height; y++)
		{
			for (var x = area.X; x < area.X + area.Width; x++)
			{
				var cell = level.CellAt(x, y);
				if (LieOf(cell) == Lie.Flat && !LevelDefinition.IsBlocked(cell))
				{
					floor.Add(new GridSquare(x, y));
					grass += LevelDefinition.IsGrassGround(cell) ? 1 : 0;
				}
			}
		}

		if (floor.Count == 0 || grass * 2 > floor.Count)
		{
			return null;
		}

		var middle = area.X + (area.Width / 2f);
		var against = floor
			.Where(at => level.CellAt(at.X, at.Y - 1) == LevelCell.Wall)
			.OrderBy(at => at.Y)
			.ThenBy(at => Math.Abs(at.X + 0.5f - middle))
			.ToList();
		return against.Count > 0 ? against[0] : floor[0];
	}

	private void MoveLantern()
	{
		if (_lantern is not null && IsInstanceValid(_lantern) && PartyCentre() is { } centre)
		{
			_lantern.Position = centre + new Vector3(0, 2.0f, 0);
		}
	}

	/// <summary>Every frame in a level: the lantern follows the party, the torches flicker, and the
	/// fight's trappings — the grid, the names — show only while there is a fight.</summary>
	private void TendLevel(double delta)
	{
		if (_campaign is not { IsLevel: true })
		{
			return;
		}

		MoveLantern();
		FollowRoute(delta);
		var t = (float)Time.GetTicksMsec() / 1000f;
		foreach (var (light, energy, phase) in _torches)
		{
			if (IsInstanceValid(light))
			{
				light.LightEnergy = energy * (0.88f + (0.08f * Mathf.Sin((t * 7.3f) + phase)) + (0.05f * Mathf.Sin((t * 13.1f) + (phase * 2f))));
			}
		}

		var exploring = Exploring;
		if (_gridPlane is not null && IsInstanceValid(_gridPlane))
		{
			_gridPlane.Visible = !exploring;
		}

		foreach (var plate in _nameplates.Values)
		{
			if (IsInstanceValid(plate))
			{
				plate.Visible = !exploring;
			}
		}
	}

	// ---- a walk laid down on the command line ----

	private Queue<GridSquare> _route;
	private double _routeWait;

	/// <summary>
	/// <c>-- --explore 6,36 5,25 17,23</c>: the party walks to each square in turn, using any door,
	/// bridge or cache named, once the last walk, page and fight are done. With <c>--autoplay</c>
	/// the fights play themselves, so a whole level can be watched or recorded without a hand on
	/// the mouse — which is how its look and its rooms are checked.
	/// </summary>
	private void FollowRoute(double delta)
	{
		if (_route is null)
		{
			_route = new Queue<GridSquare>();
			var args = OS.GetCmdlineUserArgs();
			var at = Array.IndexOf(args, "--explore");
			for (var i = at + 1; at >= 0 && i < args.Length && !args[i].StartsWith("--", StringComparison.Ordinal); i++)
			{
				foreach (var pair in args[i].Split(' ', StringSplitOptions.RemoveEmptyEntries))
				{
					var xy = pair.Split(',');
					if (xy.Length == 2 && int.TryParse(xy[0], out var x) && int.TryParse(xy[1], out var y))
					{
						_route.Enqueue(new GridSquare(x, y));
					}
				}
			}
		}

		if (_route.Count == 0 || !Exploring || _held || StageBusy || _walkers.Count > 0)
		{
			_routeWait = 0;
			return;
		}

		_routeWait += delta;
		if (_routeWait < 1.0)
		{
			return;
		}

		_routeWait = 0;
		var next = _route.Peek();

		// A door or a crossing is tried until it gives; anything else is walked to once.
		if (_campaign.FeatureAt(next) is not { } feature || _campaign.IsUsed(feature.Id))
		{
			_route.Dequeue();
		}

		Travel(next);
	}

	// ---- experience, as it is earned ----

	private VBoxContainer _toasts;
	private int _levelAnnounced;

	private void BuildToasts(Control root)
	{
		_toasts = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore, Alignment = BoxContainer.AlignmentMode.Begin };
		_toasts.AddThemeConstantOverride("separation", 6);
		root.AddChild(_toasts);
		Pin(_toasts, Control.LayoutPreset.CenterTop, 18);
	}

	/// <summary>
	/// Every frame: anything the rules have given experience for since last time — a fight won,
	/// a place found, a door opened — goes in the log and up on a slip of paper at the top of the
	/// screen, as Wrath announces it. Held back while the board is still playing the moment it
	/// was earned, so the award for a fight does not appear before its last blow lands.
	/// </summary>
	private void AnnounceExperience()
	{
		if (_campaign is null || _toasts is null || StageBusy)
		{
			return;
		}

		var awards = _campaign.TakeAwards();
		foreach (var award in awards)
		{
			_log.AddText($"— {award.Why}: +{award.Amount} experience —\n");
			Toast($"+{award.Amount} experience", award.Why);
		}

		if (awards.Count > 0)
		{
			UpdateStatus();
		}

		if (_levelAnnounced == 0)
		{
			_levelAnnounced = _campaign.EarnedLevel;
		}
		else if (_campaign.EarnedLevel > _levelAnnounced)
		{
			_levelAnnounced = _campaign.EarnedLevel;
			Toast($"Level {_campaign.EarnedLevel} earned", "Choose Level up when you are ready.");
		}
	}

	private void Toast(string headline, string why)
	{
		var lines = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
		lines.AddThemeConstantOverride("separation", 0);
		var top = Words(headline.ToUpperInvariant(), Display, 20, InkRed);
		top.HorizontalAlignment = HorizontalAlignment.Center;
		lines.AddChild(top);
		var under = Words(why, BodyItalic, 16, PageInk);
		under.HorizontalAlignment = HorizontalAlignment.Center;
		lines.AddChild(under);

		var slip = PaperInIron(lines, 22, 6);
		slip.MouseFilter = Control.MouseFilterEnum.Ignore;
		slip.CustomMinimumSize = new Vector2(340, 0);
		slip.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
		_toasts.AddChild(slip);

		// Four at most: a fight that ends with a door and a discovery is three, and a fourth is
		// already old news.
		while (_toasts.GetChildCount() > 4)
		{
			_toasts.GetChild(0).Free();
		}

		slip.Modulate = new Color(1, 1, 1, 0);
		var fade = slip.CreateTween();
		fade.TweenProperty(slip, "modulate:a", 1f, 0.25);
		fade.TweenInterval(3.5);
		fade.TweenProperty(slip, "modulate:a", 0f, 0.6);
		fade.TweenCallback(Callable.From(slip.QueueFree));
	}
}
