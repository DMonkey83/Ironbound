using System.Collections.Generic;
using System.Linq;
using Godot;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Maps;
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

		AddChild(camera);
		camera.Position = centre + new Vector3(12, 13, 12);
		camera.LookAt(centre);

		var light = new DirectionalLight3D { ShadowEnabled = true };
		AddChild(light);
		light.Position = centre + new Vector3(5, 10, 3);
		light.LookAt(centre);

		var ground = new MeshInstance3D
		{
			Mesh = new PlaneMesh { Size = new Vector2(width, height) },
			MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.20f, 0.22f, 0.20f) },
		};

		AddChild(ground);
		ground.Position = centre;

		Spawn(_battle.Party, PartyColour);
		Spawn(_battle.Foes, FoeColour);
		RefreshFigures();
	}

	private void Spawn(IReadOnlyList<Loadout> loadouts, Color colour)
	{
		foreach (var loadout in loadouts)
		{
			var figure = new MeshInstance3D
			{
				Mesh = new CapsuleMesh { Radius = 0.3f, Height = 1.4f },
				MaterialOverride = new StandardMaterial3D { AlbedoColor = colour },
			};

			AddChild(figure);

			var nameplate = new Label3D
			{
				Text = loadout.Creature.Name,
				Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
				FontSize = 48,
				PixelSize = 0.005f,
				Position = new Vector3(0, 1.2f, 0),
			};

			figure.AddChild(nameplate);
			_figures[loadout.Creature] = figure;
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
