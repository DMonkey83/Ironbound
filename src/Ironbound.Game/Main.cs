using System.Collections.Generic;
using System.Linq;
using Godot;
using Ironbound.Rules.Combat;
using Ironbound.Rules.Conditions;
using Ironbound.Rules.Content;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Encounters;
using Ironbound.Rules.Encounters.Actions;
using Ironbound.Rules.Magic;
using Ironbound.Rules.Maps;
using Ironbound.Rules.Persistence;
using Ironbound.Simulation;

/// <summary>
/// The first place the rules engine meets Godot: the camera, the placeholder bodies, the combat
/// log, and the person playing.
/// </summary>
/// <remarks>
/// The scene is built in code rather than authored as a .tscn — easier to review, and it can be
/// run headless like any other code.
/// <para>
/// The party is driven by clicks and the enemy by <see cref="HeuristicActionSource"/>, through
/// the same <c>Battle</c> methods. Nothing here blocks: a turn is opened, actions are taken
/// whenever they happen to arrive, and the turn is closed when the player says so. That is what
/// the non-blocking scheduler was built for, and it is why a person and an AI can share one loop.
/// </para>
/// </remarks>
public partial class Main : Node3D
{
	/// <summary>Resolves to the per-user data directory; on Linux, under ~/.local/share/godot.</summary>
	private const string SavePath = "user://ironbound.save";

	/// <summary>The run the game opens on. Picking between them is a menu this has no need of yet.</summary>
	private const string CampaignId = "the-long-road";

	private static readonly Color PartyColour = new(0.35f, 0.55f, 0.85f);
	private static readonly Color FoeColour = new(0.75f, 0.35f, 0.30f);
	private static readonly Color PillarColour = new(0.38f, 0.36f, 0.34f);

	// Bright enough to find at a glance, and coloured by side so "is it my move?" needs no
	// reading. The ring sits under whoever is acting.
	private static readonly Color ActivePartyColour = new(0.45f, 0.85f, 1.00f);
	private static readonly Color ActiveFoeColour = new(1.00f, 0.55f, 0.30f);
	private static readonly Color LegalColour = new(0.35f, 0.75f, 0.40f, 0.45f);
	private static readonly Color IllegalColour = new(0.75f, 0.30f, 0.30f, 0.35f);

	private enum Mode
	{
		Move,
		Attack,
		Cast,

		/// <summary>Everything you have, at the cost of going anywhere.</summary>
		Full,

		/// <summary>Put them on the floor and make them spend a round getting up.</summary>
		Trip,

		/// <summary>Drive them backwards, out of position and out of your way.</summary>
		Shove,
	}

	private static readonly Mode[] ModeOrder =
		[Mode.Move, Mode.Attack, Mode.Full, Mode.Trip, Mode.Shove, Mode.Cast];

	private readonly Dictionary<Creature, Node3D> _figures = new();

	private ContentLibrary _content;
	private Campaign _campaign;
	private Battle _battle;
	private IActionSource _enemies;
	private Node3D _world;
	private Camera3D _camera;
	private MeshInstance3D _cursor;
	private MeshInstance3D _turnMarker;
	private StandardMaterial3D _turnPaint;
	private StandardMaterial3D _cursorPaint;

	private RichTextLabel _log;
	private Label _status;
	private Label _prompt;
	private OptionButton _spells;
	private Button _endTurn;
	private Button _load;
	private Button _stand;
	private Button _press;
	private Button _rest;
	private readonly Dictionary<Mode, Button> _modes = new();

	private Mode _mode = Mode.Move;
	private GridSquare? _hovered;

	public override void _Ready()
	{
		_content = GodotContent.Load();
		_campaign = Campaign.Begin(_content, CampaignId);
		Begin(_campaign.Battle);

		BuildInterface();
		RebuildWorld();

		if (DisplayServer.GetName() == "headless")
		{
			RunHeadlessAndQuit();
			return;
		}

		ReportInitiative();
		StartNextTurn();
	}

	private void Begin(Battle battle)
	{
		_battle = battle;
		_enemies = Scenarios.AutoPilot(_battle);
	}

	// ---- whose turn it is ----

	/// <summary>
	/// Opens turns and lets the enemy take its own, stopping as soon as it is somebody's turn who
	/// needs asking.
	/// </summary>
	private void StartNextTurn()
	{
		while (true)
		{
			if (_battle.BeginTurn() is not { } turn)
			{
				Prompt(Verdict());
				RefreshFigures();
				RefreshControls();
				return;
			}

			Append($"[round {turn.Round}] {turn.Actor.Name}", turn.Lines);

			// Nothing to decide and nobody to ask. The turn still opens, so effects tick and
			// the dying keep dying, but it closes itself rather than waiting on a click.
			if (!turn.Actor.CanAct)
			{
				Append(null, new List<string> { Idle(turn.Actor) });
				_battle.EndTurn();
				RefreshFigures();
				continue;
			}

			if (_battle.NeedsPlayer)
			{
				_hovered = null;
				SelectSpellsFor(turn.Actor);
				PromptTurn();
				RefreshFigures();
				RefreshControls();
				return;
			}

			RunEnemyTurn();
			RefreshFigures();
		}
	}

	/// <summary>What to say once the fighting stops, which depends on what is left of the run.</summary>
	private string Verdict() => _campaign.State switch
	{
		CampaignState.Between => _campaign.CanRest
			? $"Chapter {_campaign.Chapter} won. Press on, or spend your one rest."
			: $"Chapter {_campaign.Chapter} won. Press on — there is no rest left.",
		CampaignState.Won => $"{_campaign.Definition.Name} is finished. Every fight won.",
		CampaignState.Lost => "The party is down. The road ends here.",
		_ => "Nobody left standing.",
	};

	private void OnPressOn()
	{
		if (!_campaign.Advance())
		{
			return;
		}

		Begin(_campaign.Battle);
		RebuildWorld();

		_log.AddText($"\n— chapter {_campaign.Chapter}: {CurrentChapterName()} —\n");
		ReportInitiative();
		StartNextTurn();
	}

	private void OnRest()
	{
		if (!_campaign.Rest())
		{
			return;
		}

		_log.AddText("— the party rests: wounds closed, spells prepared again —\n");
		Prompt(Verdict());
		RefreshFigures();
		RefreshControls();
	}

	private string CurrentChapterName() =>
		_content.GetEncounter(_campaign.Definition.Encounters[_campaign.Chapter - 1])?.Name
		?? "the next fight";

	/// <summary>Why somebody is doing nothing this turn, in as few words as carry the meaning.</summary>
	private static string Idle(Creature creature)
	{
		if (!creature.IsAlive)
		{
			return $"{creature.Name} is dead";
		}

		if (!creature.IsConscious)
		{
			return $"{creature.Name} is down and cannot act";
		}

		var stopped = creature.Conditions
			.Where(condition => ConditionInfo.Of(condition).DeniesActions)
			.ToList();

		return stopped.Count > 0
			? $"{creature.Name} is {string.Join(" and ", stopped).ToLowerInvariant()} and loses the turn"
			: $"{creature.Name} can do nothing";
	}

	private void RunEnemyTurn()
	{
		while (_battle.Encounter.Current is { IsEnded: false } turn
			&& _enemies.NextAction(turn) is { } action)
		{
			var lines = _battle.Act(action);
			if (lines.Count == 0)
			{
				break;
			}

			Append(null, lines);
		}

		_battle.EndTurn();
	}

	private void OnStandUp()
	{
		var lines = _battle.Act(new StandUpAction());
		if (lines.Count == 0)
		{
			Refuse("Nobody is on the floor.");
			return;
		}

		Append(null, lines);
		RefreshFigures();
		PromptTurn();
		RefreshControls();
		_hovered = null;
	}

	private void OnEndTurn()
	{
		_battle.EndTurn();
		StartNextTurn();
	}

	// ---- what the player clicks ----

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }
			|| !_battle.IsPartyTurn
			|| SquareUnderCursor() is not { } square)
		{
			return;
		}

		var actor = _battle.Encounter.Current!.Actor;

		if (ActionFor(actor, square) is not { } action)
		{
			Refuse(WhyNot(actor, square));
			return;
		}

		var lines = _battle.Act(action);

		if (lines.Count == 0)
		{
			// The cursor should already have been red, so reaching here means something changed
			// between the hover and the click. Say which of the two reasons it was: "no action
			// left" and "cannot reach" are very different complaints.
			Refuse(_battle.Encounter.Current is { } turn && !turn.Budget.CanAfford(action.Cost)
				? $"{actor.Name} has no {action.Cost.ToString().ToLowerInvariant()} action left."
				: $"{actor.Name} cannot {action.Name} from there.");
			return;
		}

		Append(null, lines);
		RefreshFigures();
		PromptTurn();
		RefreshControls();
		_hovered = null;
	}

	/// <summary>
	/// Whether clicking this square would actually do something. Asks the rules rather than
	/// re-deriving an answer here, so the cursor cannot promise what the click will not deliver.
	/// </summary>
	private bool CanAct(Creature actor, GridSquare square) =>
		ActionFor(actor, square) is { } action && _battle.CanAct(action);

	/// <summary>Turns a refusal down without swallowing it. The log is where the player is looking.</summary>
	private void Refuse(string why)
	{
		Prompt(why);
		_log.AddText($"— {why} —\n");
	}

	/// <summary>
	/// Which weapon a click on an enemy means. Distance decides, because the player should not
	/// have to: something in reach gets the blade, something across the room gets the bow, and an
	/// archer who has been charged draws the scimitar rather than firing into a face.
	/// </summary>
	private static WeaponAttack WeaponFor(Battlefield field, Creature actor, Creature target)
	{
		if (field.IsWithinReach(actor, target))
		{
			return actor.MeleeAttack ?? actor.PrimaryAttack;
		}

		if (!field.HasLineOfSight(actor, target))
		{
			return null;
		}

		var feet = field.DistanceInFeet(actor, target) ?? 0;

		foreach (var weapon in actor.Attacks)
		{
			if (weapon.IsRanged && weapon.IsWithinRange(feet))
			{
				return weapon;
			}
		}

		return null;
	}

	/// <summary>
	/// Why a click achieved nothing, in terms of the fiction rather than the code.
	/// </summary>
	/// <remarks>
	/// "Cannot do that from here" is true of every refusal and useful for none of them. The
	/// commonest one by far is reaching for a full attack before closing, and a player who is
	/// told the distance learns the rule; a player told nothing concludes the game is broken.
	/// </remarks>
	private string WhyNot(Creature actor, GridSquare square)
	{
		var field = _battle.Battlefield;
		if (field is null)
		{
			return $"{actor.Name} cannot do that from here.";
		}

		var occupant = field.OccupantOf(square);

		switch (_mode)
		{
			case Mode.Trip:
			case Mode.Shove:
				if (occupant is null || !actor.IsEnemyOf(occupant))
				{
					return "There is nobody there to lay hands on.";
				}

				if (_mode == Mode.Trip && occupant.IsProne)
				{
					return $"{occupant.Name} is already on the floor.";
				}

				return field.DistanceInFeet(actor, occupant) is { } gap
					? $"{actor.Name} cannot reach {occupant.Name}, {gap} ft away."
					: $"{actor.Name} cannot reach {occupant.Name}.";

			case Mode.Attack:
			case Mode.Full:
				if (occupant is null)
				{
					return "There is nobody there to attack.";
				}

				if (!actor.IsEnemyOf(occupant))
				{
					return $"{occupant.Name} is on your own side.";
				}

				if (actor.Attacks.Count == 0)
				{
					return $"{actor.Name} has nothing to attack with.";
				}

				var feet = field.DistanceInFeet(actor, occupant);

				if (!field.HasLineOfSight(actor, occupant))
				{
					return $"{actor.Name} cannot see {occupant.Name}.";
				}

				return actor.MeleeAttack is { } blade && feet is { } away
					? $"{actor.Name}'s {blade.Name} does not reach {occupant.Name}, {away} ft away — move closer first."
					: $"{actor.Name} cannot reach {occupant.Name} from here.";

			case Mode.Cast:
				return SelectedSpell() is null
					? $"{actor.Name} has no spell selected."
					: $"{actor.Name} cannot target that square.";

			default:
				return field.SquareOf(actor) == square
					? $"{actor.Name} is already there."
					: $"{actor.Name} cannot find a way to that square.";
		}
	}

	/// <summary>What clicking a square means, given the mode the player has chosen.</summary>
	private GameAction ActionFor(Creature actor, GridSquare square)
	{
		var field = _battle.Battlefield;
		if (field is null)
		{
			return null;
		}

		var occupant = field.OccupantOf(square);

		switch (_mode)
		{
			case Mode.Attack:
				return occupant is not null && actor.IsEnemyOf(occupant)
					&& WeaponFor(field, actor, occupant) is { } weapon
						? new AttackAction(weapon, occupant)
						: null;

			case Mode.Full:
				return occupant is not null && actor.IsEnemyOf(occupant)
					&& WeaponFor(field, actor, occupant) is { } everything
						? new FullAttackAction(occupant, everything)
						: null;

			case Mode.Trip:
				return occupant is not null && actor.IsEnemyOf(occupant)
					? new TripAction(occupant)
					: null;

			case Mode.Shove:
				return occupant is not null && actor.IsEnemyOf(occupant)
					? new BullRushAction(occupant)
					: null;

			case Mode.Cast:
				if (SelectedSpell() is not { } spell)
				{
					return null;
				}

				return spell.NeedsAPoint
					? CastSpellAction.At(spell, square)
					: occupant is not null ? CastSpellAction.At(spell, occupant) : null;

			default:
				if (field.SquareOf(actor) is not { } from)
				{
					return null;
				}

				var path = field.FindPath(from, square, actor);

				// A single square away is a five-foot step, which is free and provokes nothing.
				return path.Count switch
				{
					< 2 => null,
					2 when field.PathCost(path) <= Distance.FeetPerSquare =>
						new FiveFootStepAction(path),
					_ => new MoveAction(path),
				};
		}
	}

	private GridSquare? SquareUnderCursor()
	{
		if (_camera is null || _battle.Battlefield is not { } field)
		{
			return null;
		}

		var mouse = GetViewport().GetMousePosition();
		var from = _camera.ProjectRayOrigin(mouse);
		var direction = _camera.ProjectRayNormal(mouse);

		if (Mathf.IsZeroApprox(direction.Y))
		{
			return null;
		}

		// Straight onto the ground plane. An orthographic camera over flat ground needs no physics.
		var distance = -from.Y / direction.Y;
		if (distance < 0)
		{
			return null;
		}

		var hit = from + (direction * distance);
		var square = new GridSquare(Mathf.FloorToInt(hit.X), Mathf.FloorToInt(hit.Z));

		return field.Contains(square) ? square : null;
	}

	public override void _Process(double delta)
	{
		if (_cursor is null)
		{
			return;
		}

		if (!_battle.IsPartyTurn || SquareUnderCursor() is not { } square)
		{
			_cursor.Visible = false;
			return;
		}

		_cursor.Visible = true;
		_cursor.Position = new Vector3(square.X + 0.5f, 0.02f, square.Y + 0.5f);

		// Deciding what a click would mean runs a path search, so only do it when the cursor
		// actually moves to a different square rather than once a frame.
		if (_hovered == square)
		{
			return;
		}

		_hovered = square;
		var actor = _battle.Encounter.Current!.Actor;
		_cursorPaint.AlbedoColor = CanAct(actor, square) ? LegalColour : IllegalColour;
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
		var json = _campaign.ToJson();
		Write(json);

		_log.AddText($"— saved {json.Length} characters —\n");
		_load.Disabled = false;
	}

	private void OnLoad()
	{
		if (!SaveExists() || Read() is not { Length: > 0 } json)
		{
			return;
		}

		Campaign restored;
		try
		{
			restored = Campaign.FromJson(json, _content);
		}
		catch (System.IO.InvalidDataException problem)
		{
			// A save written by an older build, or one naming content that has since been renamed.
			// Refusing it loudly is deliberate; taking the running fight down with it is not, so
			// the current battle is left exactly as it was.
			GD.PushError($"Could not load {SavePath}: {problem.Message}");
			_log.AddText($"— could not load: {problem.Message} —\n");
			return;
		}

		_campaign = restored;
		Begin(_campaign.Battle);

		// The bodies were built for the fight that is being replaced, so they go with it.
		RebuildWorld();

		_log.Clear();
		_log.AddText("— loaded —\n");
		ReportInitiative();
		StartNextTurn();
	}

	// ---- the scene ----

	private void SpawnPillar(int x, int y)
	{
		var pillar = new MeshInstance3D
		{
			Mesh = new BoxMesh { Size = new Vector3(0.9f, 2.2f, 0.9f) },
			MaterialOverride = new StandardMaterial3D { AlbedoColor = PillarColour },
		};

		_world.AddChild(pillar);
		pillar.Position = new Vector3(x + 0.5f, 1.1f, y + 0.5f);
	}

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

		_camera = new Camera3D
		{
			Projection = Camera3D.ProjectionType.Orthogonal,
			Size = Mathf.Max(width, height) * 1.3f,
		};

		_world.AddChild(_camera);
		_camera.Position = centre + new Vector3(12, 13, 12);
		_camera.LookAt(centre);

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

		_cursorPaint = new StandardMaterial3D
		{
			AlbedoColor = LegalColour,
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
		};

		_cursor = new MeshInstance3D
		{
			Mesh = new PlaneMesh { Size = new Vector2(0.95f, 0.95f) },
			MaterialOverride = _cursorPaint,
			Visible = false,
		};

		_world.AddChild(_cursor);

		_turnPaint = new StandardMaterial3D
		{
			AlbedoColor = ActivePartyColour,
			EmissionEnabled = true,
			Emission = ActivePartyColour,
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
		};

		_turnMarker = new MeshInstance3D
		{
			Mesh = new TorusMesh { InnerRadius = 0.36f, OuterRadius = 0.48f },
			MaterialOverride = _turnPaint,
			Visible = false,
		};

		_world.AddChild(_turnMarker);

		// Cover the player cannot see is cover the player will call a bug.
		if (field is not null)
		{
			for (var x = 0; x < width; x++)
			{
				for (var y = 0; y < height; y++)
				{
					if (field.IsBlocked(new GridSquare(x, y)))
					{
						SpawnPillar(x, y);
					}
				}
			}
		}

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
		panel.OffsetTop = -320;

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

		_prompt = new Label();
		rows.AddChild(_prompt);

		_log = new RichTextLabel
		{
			ScrollFollowing = true,
			SizeFlagsVertical = Control.SizeFlags.ExpandFill,
			CustomMinimumSize = new Vector2(0, 170),
		};

		rows.AddChild(_log);

		var buttons = new HBoxContainer();
		rows.AddChild(buttons);

		foreach (var mode in ModeOrder)
		{
			var button = new Button
			{
				Text = mode == Mode.Full ? "Full attack" : mode.ToString(),
				ToggleMode = true,
				SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			};

			button.Pressed += () => ChooseMode(mode);
			buttons.AddChild(button);
			_modes[mode] = button;
		}

		_spells = new OptionButton { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		buttons.AddChild(_spells);

		_stand = new Button { Text = "Stand up" };
		_stand.Pressed += OnStandUp;
		buttons.AddChild(_stand);

		_press = new Button { Text = "Press on" };
		_press.Pressed += OnPressOn;
		buttons.AddChild(_press);

		_rest = new Button { Text = "Rest" };
		_rest.Pressed += OnRest;
		buttons.AddChild(_rest);

		_endTurn = new Button { Text = "End turn" };
		_endTurn.Pressed += OnEndTurn;
		buttons.AddChild(_endTurn);

		var save = new Button { Text = "Save" };
		save.Pressed += OnSave;
		buttons.AddChild(save);

		_load = new Button { Text = "Load", Disabled = !SaveExists() };
		_load.Pressed += OnLoad;
		buttons.AddChild(_load);

		ChooseMode(Mode.Move);
	}

	private void ChooseMode(Mode mode)
	{
		_mode = mode;
		_hovered = null;
		foreach (var (which, button) in _modes)
		{
			button.ButtonPressed = which == mode;
		}
	}

	private void SelectSpellsFor(Creature actor)
	{
		_spells.Clear();
		foreach (var spell in actor.Spells.Prepared)
		{
			_spells.AddItem($"{spell.Name} ({actor.Spells.SlotsRemaining(spell.Level)})");
			_spells.SetItemDisabled(_spells.ItemCount - 1, !actor.Spells.CanCast(spell));
		}

		_spells.Disabled = _spells.ItemCount == 0;
		if (_spells.ItemCount > 0)
		{
			_spells.Selected = 0;
		}
	}

	private Spell SelectedSpell()
	{
		if (_battle.Encounter.Current is not { } turn || _spells.Selected < 0)
		{
			return null;
		}

		var prepared = turn.Actor.Spells.Prepared;
		return _spells.Selected < prepared.Count ? prepared[_spells.Selected] : null;
	}

	// ---- reporting ----

	private void Append(string header, IReadOnlyList<string> lines)
	{
		if (header is not null)
		{
			_log.AddText($"{header}\n");
		}

		foreach (var line in lines)
		{
			_log.AddText($"      {line}\n");
		}
	}

	private void Prompt(string text)
	{
		_prompt.Text = text;
		UpdateStatus();
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

	private void RefreshControls()
	{
		var playing = _battle.IsPartyTurn;
		var turn = playing && _battle.Encounter.Current is { IsEnded: false } current ? current : null;

		_endTurn.Disabled = !playing;

		// Greyed out rather than merely refused. Finding out that the standard action is gone by
		// clicking and being told no is the interface making the player do its remembering.
		_modes[Mode.Attack].Disabled = turn is null || !turn.Budget.HasStandard;
		_modes[Mode.Full].Disabled = turn is null || !turn.Budget.CanAfford(ActionCost.FullRound);
		_modes[Mode.Trip].Disabled = turn is null || !turn.Budget.HasStandard;
		_modes[Mode.Shove].Disabled = turn is null || !turn.Budget.HasStandard;
		_modes[Mode.Cast].Disabled = turn is null || !turn.Budget.HasStandard || _spells.ItemCount == 0;
		_modes[Mode.Move].Disabled = turn is null || !CanStillMove(turn);

		_spells.Disabled = turn is null || _spells.ItemCount == 0;
		_stand.Disabled = turn is null || !turn.CanTake(new StandUpAction());
		_press.Disabled = !_campaign.CanAdvance;
		_rest.Disabled = !_campaign.CanRest;

		// Being left holding a mode that can no longer do anything is its own small trap. Move
		// first, because a five-foot step outlives everything else.
		if (_modes[_mode].Disabled)
		{
			foreach (var mode in ModeOrder)
			{
				if (!_modes[mode].Disabled)
				{
					ChooseMode(mode);
					break;
				}
			}
		}
	}

	/// <summary>
	/// Whether clicking the ground could still achieve anything: a walk needs the move action, but
	/// a five-foot step is free and survives having spent it.
	/// </summary>
	private static bool CanStillMove(Turn turn) =>
		turn.Budget.HasMove || (!turn.Combatant.HasMoved && !turn.Combatant.HasTakenFiveFootStep);

	/// <summary>Says whose turn it is and, plainly, what they have left to spend on it.</summary>
	private void PromptTurn()
	{
		if (_battle.Encounter.Current is not { IsEnded: false } turn)
		{
			return;
		}

		var left = new List<string>();
		if (turn.Budget.HasStandard)
		{
			left.Add("standard");
		}

		if (CanStillMove(turn))
		{
			left.Add(turn.Budget.HasMove ? "move" : "five-foot step");
		}

		if (turn.Budget.HasSwift)
		{
			left.Add("swift");
		}

		if (left.Count == 0)
		{
			Prompt($"{turn.Actor.Name} has nothing left — end the turn.");
			_log.AddText("— nothing left to spend; end the turn —\n");
			return;
		}

		Prompt($"{turn.Actor.Name}'s turn — {string.Join(", ", left)} remaining.");
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
					creature.IsConscious && !creature.IsProne ? 0.7f : 0.3f,
					square.Y + 0.5f);
			}

			// Anyone off their feet lies down, whether they chose it or not. Cheap, and it
			// reads at a glance.
			var down = !creature.IsConscious || creature.IsProne;
			figure.Rotation = down ? new Vector3(Mathf.Pi / 2f, 0, 0) : Vector3.Zero;
		}

		RefreshTurnMarker();
	}

	/// <summary>Puts the ring under whoever is acting, and hides it when nobody is.</summary>
	private void RefreshTurnMarker()
	{
		if (_turnMarker is null)
		{
			return;
		}

		if (_battle.Encounter.Current is not { IsEnded: false } turn
			|| _battle.Battlefield?.SquareOf(turn.Actor) is not { } square)
		{
			_turnMarker.Visible = false;
			return;
		}

		var colour = _battle.IsPartyTurn ? ActivePartyColour : ActiveFoeColour;
		_turnPaint.AlbedoColor = colour;
		_turnPaint.Emission = colour;

		_turnMarker.Position = new Vector3(square.X + 0.5f, 0.04f, square.Y + 0.5f);
		_turnMarker.Visible = true;
	}

	private void UpdateStatus()
	{
		var standing = string.Join("   ", _battle.Encounter.Order.Select(combatant =>
		{
			var creature = combatant.Creature;
			var wrong = string.Join(", ", creature.Conditions);
			var note = wrong.Length > 0 ? $" ({wrong})" : string.Empty;
			var acting = _battle.Encounter.Current is { IsEnded: false } open
				&& ReferenceEquals(open.Actor, creature);

			return (acting ? "> " : string.Empty)
				+ $"{creature.Name} {creature.HitPoints.Current}/{creature.HitPoints.Maximum}{note}";
		}));

		var budget = _battle.Encounter.Current is { IsEnded: false } turn
			? $"    [{turn.Budget}]"
			: string.Empty;

		_status.Text = $"Chapter {_campaign.Chapter}/{_campaign.Definition.Encounters.Count}"
			+ $"  rests {_campaign.RestsRemaining}    Round {_battle.Round}{budget}    {standing}";
	}

	// ---- the headless proof ----

	/// <summary>
	/// Plays a few turns, writes a save, reads it back, and finishes from the reloaded copy — so
	/// that saving is exercised through the engine's own file handling rather than only in a test.
	/// Everyone is driven by the AI here; there is nobody to click anything.
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
			Report(_battle.AdvanceTurn(_enemies));
		}

		var json = _campaign.ToJson();
		Write(json);

		GD.Print($"--- saved {json.Length} characters to {ProjectSettings.GlobalizePath(SavePath)} ---");

		_campaign = Campaign.FromJson(Read(), _content);
		Begin(_campaign.Battle);

		GD.Print($"--- reloaded at round {_battle.Round}, tick {_battle.Encounter.Tick} ---");

		while (true)
		{
			while (_battle.AdvanceTurn(_enemies) is { } turn)
			{
				Report(turn);
			}

			GD.Print($"--- chapter {_campaign.Chapter}: {_battle.Outcome} ---");

			if (!_campaign.CanAdvance)
			{
				break;
			}

			// The proof the whole layer exists for: the same people, carrying their wounds and
			// their spent slots, walking into the next fight.
			GD.Print($"--- chapter over with {Remaining()} ---");

			// Somebody bleeding out is not a decision, it is an answer. A player gets to weigh
			// the one rest against what is coming; this is a demonstration, so it takes it.
			if (_campaign.CanRest && _campaign.Party.Any(one => !one.IsConscious))
			{
				_campaign.Rest();
				GD.Print($"--- the party rests: {Remaining()} ---");
			}

			_campaign.Advance();
			Begin(_campaign.Battle);
			GD.Print($"--- chapter {_campaign.Chapter}: {CurrentChapterName()} ---");
		}

		GD.Print($"--- {_campaign.State} ---");
		GetTree().Quit();
	}

	private string Remaining() => string.Join(
		", ",
		_campaign.Party.Select(c => $"{c.Name} {c.HitPoints.Current}/{c.HitPoints.Maximum}"));

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
