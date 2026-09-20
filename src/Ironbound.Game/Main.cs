using System.Collections.Generic;
using System.Linq;
using Godot;
using Ironbound.Rules.Creatures;
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
/// </remarks>
public partial class Main : Node3D
{
	private static readonly Color PartyColour = new(0.35f, 0.55f, 0.85f);
	private static readonly Color FoeColour = new(0.75f, 0.35f, 0.30f);

	private readonly Dictionary<Creature, Node3D> _figures = new();

	private Battle _battle;
	private IActionSource _actors;
	private RichTextLabel _log;
	private Label _status;
	private Button _advance;

	public override void _Ready()
	{
		_battle = Scenarios.GoblinAmbush();
		_actors = Scenarios.AutoPilot(_battle);

		BuildWorld();
		BuildInterface();
		ReportInitiative();
		UpdateStatus();

		if (DisplayServer.GetName() == "headless")
		{
			RunToCompletionAndQuit();
		}
	}

	// ---- the scene ----

	private void BuildWorld()
	{
		var camera = new Camera3D
		{
			Projection = Camera3D.ProjectionType.Orthogonal,
			Size = 12,
		};

		AddChild(camera);
		camera.Position = new Vector3(9, 9, 9);
		camera.LookAt(new Vector3(0, 0.6f, 0));

		var light = new DirectionalLight3D { ShadowEnabled = true };
		AddChild(light);
		light.Position = new Vector3(5, 9, 3);
		light.LookAt(Vector3.Zero);

		var ground = new MeshInstance3D
		{
			Mesh = new PlaneMesh { Size = new Vector2(24, 24) },
			MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.20f, 0.22f, 0.20f) },
		};

		AddChild(ground);

		PlaceRow(_battle.Party, PartyColour, z: -2.0f);
		PlaceRow(_battle.Foes, FoeColour, z: 2.0f);
	}

	private void PlaceRow(IReadOnlyList<Loadout> loadouts, Color colour, float z)
	{
		var spread = 1.8f;
		var start = -(loadouts.Count - 1) * spread / 2f;

		for (var index = 0; index < loadouts.Count; index++)
		{
			var creature = loadouts[index].Creature;

			var figure = new MeshInstance3D
			{
				Mesh = new CapsuleMesh { Radius = 0.35f, Height = 1.6f },
				MaterialOverride = new StandardMaterial3D { AlbedoColor = colour },
			};

			AddChild(figure);
			figure.Position = new Vector3(start + (index * spread), 0.8f, z);

			var nameplate = new Label3D
			{
				Text = creature.Name,
				Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
				FontSize = 48,
				PixelSize = 0.006f,
				Position = new Vector3(0, 1.3f, 0),
			};

			figure.AddChild(nameplate);
			_figures[creature] = figure;
		}
	}

	private void BuildInterface()
	{
		var layer = new CanvasLayer();
		AddChild(layer);

		var panel = new PanelContainer();
		layer.AddChild(panel);
		panel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.BottomWide);
		panel.OffsetTop = -280;

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

		_advance = new Button { Text = "Next turn" };
		_advance.Pressed += OnAdvance;
		rows.AddChild(_advance);
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
		foreach (var (creature, figure) in _figures)
		{
			// Anyone out of the fight lies down. Cheap, and it reads at a glance.
			figure.Rotation = creature.IsConscious ? Vector3.Zero : new Vector3(Mathf.Pi / 2f, 0, 0);
			figure.Position = new Vector3(
				figure.Position.X,
				creature.IsConscious ? 0.8f : 0.35f,
				figure.Position.Z);
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

	private void RunToCompletionAndQuit()
	{
		GD.Print("--- initiative ---");
		foreach (var combatant in _battle.Encounter.Order)
		{
			GD.Print($"  {combatant.Initiative,3}  {combatant.Creature.Name}");
		}

		while (_battle.AdvanceTurn(_actors) is { } turn)
		{
			GD.Print($"[round {turn.Round}] {turn.Actor.Name}");
			foreach (var line in turn.Lines)
			{
				GD.Print($"      {line}");
			}
		}

		GD.Print($"--- {_battle.Outcome} ---");
		foreach (var combatant in _battle.Encounter.Order)
		{
			GD.Print($"  {combatant.Creature}");
		}

		GetTree().Quit();
	}
}
