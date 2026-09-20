using System.Collections.Generic;
using System.Linq;
using Godot;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Persistence;
using Ironbound.Simulation;

/// <summary>
/// The first place the rules engine meets Godot. Everything below this file is engine-free and
/// unit-tested; this one owns the camera, the placeholder bodies and the combat log, and drives
/// the fight one turn at a time.
/// </summary>
/// <remarks>
/// The scene is built in code rather than authored as a .tscn. A scene file is awkward to write
/// by hand, unreadable in a diff, and cannot be checked without opening the editor; this can be
/// reviewed and run headless like any other code.
/// <para>
/// File handling lives here rather than in the rules: <c>GameSave</c> deals only in strings, so
/// it can be tested without touching a disk. Godot's own <see cref="FileAccess"/> is used instead
/// of <c>System.IO</c> because it understands <c>user://</c>, which resolves to the right place
/// on every platform the game might be exported to.
/// </para>
/// </remarks>
public partial class Main : Node3D
{
	/// <summary>Resolves to the per-user data directory; on Linux, under ~/.local/share/godot.</summary>
	private const string SavePath = "user://ironbound.save";

	private static readonly Color PartyColour = new(0.35f, 0.55f, 0.85f);
	private static readonly Color FoeColour = new(0.75f, 0.35f, 0.30f);

	private readonly Dictionary<Creature, Node3D> _figures = new();

	private Battle _battle;
	private IActionSource _actors;
	private Node3D _world;
	private RichTextLabel _log;
	private Label _status;
	private Button _advance;
	private Button _load;

	public override void _Ready()
	{
		Begin(Scenarios.GoblinAmbush());

		BuildInterface();
		RebuildWorld();
		ReportInitiative();
		UpdateStatus();

		if (DisplayServer.GetName() == "headless")
		{
			RunHeadlessAndQuit();
		}
	}

	private void Begin(Battle battle)
	{
		_battle = battle;
		_actors = Scenarios.AutoPilot(_battle);
	}

	// ---- saving ----

	private static bool SaveExists() => FileAccess.FileExists(SavePath);

	private static void Write(string json)
	{
		using var file = FileAccess.Open(SavePath, FileAccess.ModeFlags.Write);
		if (file is null)
		{
			GD.PushError($"Could not write {SavePath}: {FileAccess.GetOpenError()}");
			return;
		}

		file.StoreString(json);
	}

	private static string Read()
	{
		using var file = FileAccess.Open(SavePath, FileAccess.ModeFlags.Read);
		return file?.GetAsText() ?? string.Empty;
	}

	private void OnSave()
	{
		var json = GameSave.ToJson(GameSave.Capture(_battle.Encounter));
		Write(json);

		_log.AddText($"— saved {json.Length} characters to {SavePath} —\n");
		_load.Disabled = false;
	}

	private void OnLoad()
	{
		if (!SaveExists() || Read() is not { Length: > 0 } json)
		{
			return;
		}

		Begin(Battle.Restore(GameSave.FromJson(json)));

		// The bodies were built for the fight that is being replaced, so they go with it.
		RebuildWorld();

		_log.Clear();
		_log.AddText("— loaded —\n");
		ReportInitiative();
		UpdateStatus();
	}

	// ---- the scene ----

	private void RebuildWorld()
	{
		if (_world is not null)
		{
			RemoveChild(_world);
			_world.QueueFree();
		}

		_figures.Clear();
		_world = new Node3D();
		AddChild(_world);

		var field = _battle.Battlefield;
		var width = field?.Width ?? 12;
		var height = field?.Height ?? 12;

		// One Godot unit is one five-foot square, so rules coordinates need no conversion.
		var centre = new Vector3(width / 2f, 0, height / 2f);

		var camera = new Camera3D
		{
			Projection = Camera3D.ProjectionType.Orthogonal,
			Size = Mathf.Max(width, height) * 1.3f,
		};

		_world.AddChild(camera);
		camera.Position = centre + new Vector3(12, 13, 12);
		camera.LookAt(centre);

		var light = new DirectionalLight3D { ShadowEnabled = true };
		_world.AddChild(light);
		light.Position = centre + new Vector3(5, 10, 3);
		light.LookAt(centre);

		var ground = new MeshInstance3D
		{
			Mesh = new PlaneMesh { Size = new Vector2(width, height) },
			MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.20f, 0.22f, 0.20f) },
		};

		_world.AddChild(ground);
		ground.Position = centre;

		Spawn(_battle.Party, PartyColour);
		Spawn(_battle.Foes, FoeColour);
		RefreshFigures();
	}

	private void Spawn(IReadOnlyList<Creature> creatures, Color colour)
	{
		foreach (var creature in creatures)
		{
			var scale = ScaleOf(creature.Size);
			var figure = new MeshInstance3D
			{
				Mesh = new CapsuleMesh { Radius = 0.3f * scale, Height = 1.4f * scale },
				MaterialOverride = new StandardMaterial3D { AlbedoColor = colour },
			};

			_world.AddChild(figure);

			var nameplate = new Label3D
			{
				Text = creature.Name,
				Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
				FontSize = 48,
				PixelSize = 0.005f,
				Position = new Vector3(0, 1.2f, 0),
			};

			figure.AddChild(nameplate);
			_figures[creature] = figure;
		}
	}

	/// <summary>
	/// How big to draw something. Purely presentational: every creature still stands in one
	/// square, so this changes what you see and nothing the rules read.
	/// </summary>
	private static float ScaleOf(CreatureSize size) => size switch
	{
		CreatureSize.Tiny or CreatureSize.Diminutive or CreatureSize.Fine => 0.5f,
		CreatureSize.Small => 0.75f,
		CreatureSize.Large => 1.5f,
		CreatureSize.Huge => 2.0f,
		CreatureSize.Gargantuan or CreatureSize.Colossal => 2.5f,
		_ => 1.0f,
	};

	private void BuildInterface()
	{
		var layer = new CanvasLayer();
		AddChild(layer);

		var panel = new PanelContainer();
		layer.AddChild(panel);
		panel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.BottomWide);
		panel.OffsetTop = -300;

		var margin = new MarginContainer();
		margin.AddThemeConstantOverride("margin_left", 12);
		margin.AddThemeConstantOverride("margin_right", 12);
		margin.AddThemeConstantOverride("margin_top", 8);
		margin.AddThemeConstantOverride("margin_bottom", 8);
		panel.AddChild(margin);

		var rows = new VBoxContainer();
		margin.AddChild(rows);

		_status = new Label();
		rows.AddChild(_status);

		_log = new RichTextLabel
		{
			ScrollFollowing = true,
			SizeFlagsVertical = Control.SizeFlags.ExpandFill,
			CustomMinimumSize = new Vector2(0, 190),
		};

		rows.AddChild(_log);

		var buttons = new HBoxContainer();
		rows.AddChild(buttons);

		_advance = new Button { Text = "Next turn", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		_advance.Pressed += OnAdvance;
		buttons.AddChild(_advance);

		var save = new Button { Text = "Save" };
		save.Pressed += OnSave;
		buttons.AddChild(save);

		_load = new Button { Text = "Load", Disabled = !SaveExists() };
		_load.Pressed += OnLoad;
		buttons.AddChild(_load);
	}

	// ---- driving the fight ----

	private void OnAdvance()
	{
		if (_battle.AdvanceTurn(_actors) is { } turn)
		{
			Append(turn);
			RefreshFigures();
		}

		UpdateStatus();
	}

	private void Append(BattleTurn turn)
	{
		_log.AddText($"[round {turn.Round}] {turn.Actor.Name}\n");
		foreach (var line in turn.Lines)
		{
			_log.AddText($"      {line}\n");
		}
	}

	private void ReportInitiative()
	{
		_log.AddText("Initiative\n");
		foreach (var combatant in _battle.Encounter.Order)
		{
			_log.AddText($"      {combatant.Initiative,3}  {combatant.Creature.Name}\n");
		}

		_log.AddText("\n");
	}

	private void RefreshFigures()
	{
		var field = _battle.Battlefield;

		foreach (var (creature, figure) in _figures)
		{
			if (field?.SquareOf(creature) is { } square)
			{
				figure.Position = new Vector3(
					square.X + 0.5f,
					creature.IsConscious ? 0.7f : 0.3f,
					square.Y + 0.5f);
			}

			// Anyone out of the fight lies down. Cheap, and it reads at a glance.
			figure.Rotation = creature.IsConscious ? Vector3.Zero : new Vector3(Mathf.Pi / 2f, 0, 0);
		}
	}

	private void UpdateStatus()
	{
		var outcome = _battle.Outcome;
		var standing = string.Join("   ", _battle.Encounter.Order.Select(
			combatant => $"{combatant.Creature.Name} {combatant.Creature.HitPoints.Current}/{combatant.Creature.HitPoints.Maximum}"));

		_status.Text = outcome == BattleOutcome.InProgress
			? $"Round {_battle.Round}    {standing}"
			: $"{outcome}    {standing}";

		if (_advance is not null)
		{
			_advance.Disabled = outcome != BattleOutcome.InProgress;
		}
	}

	// ---- the headless proof ----

	/// <summary>
	/// Plays a few turns, writes a save, reads it back, and finishes from the reloaded copy — so
	/// that saving is exercised through the engine's own file handling rather than only in a test.
	/// </summary>
	private void RunHeadlessAndQuit()
	{
		GD.Print("--- initiative ---");
		foreach (var combatant in _battle.Encounter.Order)
		{
			GD.Print($"  {combatant.Initiative,3}  {combatant.Creature.Name}");
		}

		for (var turn = 0; turn < 5; turn++)
		{
			Report(_battle.AdvanceTurn(_actors));
		}

		var json = GameSave.ToJson(GameSave.Capture(_battle.Encounter));
		Write(json);

		GD.Print($"--- saved {json.Length} characters to {ProjectSettings.GlobalizePath(SavePath)} ---");

		Begin(Battle.Restore(GameSave.FromJson(Read())));

		GD.Print($"--- reloaded at round {_battle.Round}, tick {_battle.Encounter.Tick} ---");
		foreach (var combatant in _battle.Encounter.Order)
		{
			GD.Print($"  {combatant.Creature}");
		}

		while (_battle.AdvanceTurn(_actors) is { } turn)
		{
			Report(turn);
		}

		GD.Print($"--- {_battle.Outcome} ---");
		GetTree().Quit();
	}

	private static void Report(BattleTurn turn)
	{
		if (turn is null)
		{
			return;
		}

		GD.Print($"[round {turn.Round}] {turn.Actor.Name}");
		foreach (var line in turn.Lines)
		{
			GD.Print($"      {line}");
		}
	}
}
