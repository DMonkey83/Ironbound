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
	private OptionButton _stash;
	private OptionButton _bearer;
	private Button _give;
	private Button _levelUp;
	private PanelContainer _sheetPanel;
	private RichTextLabel _sheet;
	private Button _showSheet;
	private HFlowContainer _between;
	private RichTextLabel _roster;
	private PanelContainer _logPanel;
	private Button _showLog;
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

	/// <summary>
	/// Whatever the fight logged before anybody had a turn — which is the ambush, if there was
	/// one. It is the reason half the party is about to lose a round, so it had better be said.
	/// </summary>
	private void ReportOpening()
	{
		foreach (var line in _battle.Log)
		{
			_log.AddText($"{line}\n");
		}
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
				if (_campaign.Collect() is > 0 and var taken)
				{
					_log.AddText($"— {taken} thing(s) taken from the fallen —\n");
				}

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

	/// <summary>A little breathing room inside a panel.</summary>
	private static MarginContainer Padded()
	{
		var margin = new MarginContainer();
		margin.AddThemeConstantOverride("margin_left", 12);
		margin.AddThemeConstantOverride("margin_right", 12);
		margin.AddThemeConstantOverride("margin_top", 6);
		margin.AddThemeConstantOverride("margin_bottom", 6);

		return margin;
	}

	/// <summary>Folds the log away, and says so on the button that brings it back.</summary>
	private void RefreshLogPanel()
	{
		if (_logPanel is null || _showLog is null)
		{
			return;
		}

		_logPanel.Visible = _showLog.ButtonPressed;
		_showLog.Text = _showLog.ButtonPressed ? "Hide log" : "Log";
	}

	/// <summary>Fills the two pickers, and greys the lot while anyone is still swinging.</summary>
	private void RefreshStash()
	{
		var between = _campaign.State != CampaignState.Fighting;
		var loot = _campaign.Stash;

		if (_between is not null)
		{
			_between.Visible = between;
		}

		if (_stash.ItemCount != loot.Count || !between)
		{
			_stash.Clear();
			foreach (var item in loot)
			{
				_stash.AddItem(item.Name);
			}

			if (loot.Count > 0)
			{
				_stash.Selected = 0;
			}
		}

		if (_bearer.ItemCount != _campaign.Party.Count)
		{
			_bearer.Clear();
			foreach (var creature in _campaign.Party)
			{
				_bearer.AddItem(creature.Name);
			}

			if (_campaign.Party.Count > 0)
			{
				_bearer.Selected = 0;
			}
		}

		var usable = between && loot.Count > 0;
		_stash.Disabled = !usable;
		_bearer.Disabled = !between;
		_give.Disabled = !usable;
		_levelUp.Disabled = !between || !_campaign.Ready.Any();
		RefreshSheet();
	}

	private void OnTake()
	{
		if (_stash.Selected < 0 || _bearer.Selected < 0
			|| _stash.Selected >= _campaign.Stash.Count
			|| _bearer.Selected >= _campaign.Party.Count)
		{
			return;
		}

		var item = _campaign.Stash[_stash.Selected];
		var bearer = _campaign.Party[_bearer.Selected];

		if (!_campaign.Give(bearer, item.Id))
		{
			Refuse($"{bearer.Name} cannot take the {item.Name}.");
			return;
		}

		_log.AddText($"— {bearer.Name} takes the {item.Name} —\n");
		RefreshControls();
		UpdateStatus();
	}

	private void OnLevelUp()
	{
		if (_bearer.Selected < 0 || _bearer.Selected >= _campaign.Party.Count)
		{
			return;
		}

		var creature = _campaign.Party[_bearer.Selected];

		if (!_campaign.LevelUp(creature))
		{
			Refuse($"{creature.Name} has not earned a level yet.");
			return;
		}

		_log.AddText($"— {creature.Name} is now {creature.Description} —\n");
		RefreshControls();
		UpdateStatus();
	}

	private void OnPressOn()
	{
		if (!_campaign.Advance())
		{
			return;
		}

		Begin(_campaign.Battle);
		RebuildWorld();

		_log.AddText($"\n— chapter {_campaign.Chapter}: {CurrentChapterName()} —\n");
		ReportOpening();
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

	/// <summary>
	/// Builds the heads-up display: a roster along the top, the log down the right where it can
	/// be folded away, and the controls along the bottom.
	/// </summary>
	/// <remarks>
	/// Laid out in thirds of the screen rather than in pixels, so it holds together from a small
	/// window up to a 4K one. The previous arrangement put the log and the controls in one
	/// bottom panel deep enough to bury the half of the battlefield the enemies were standing on.
	/// </remarks>
	private void BuildInterface()
	{
		var layer = new CanvasLayer();
		AddChild(layer);

		BuildRoster(layer);
		BuildLog(layer);
		BuildSheet(layer);
		BuildControls(layer);

		RefreshLogPanel();
		VerifyInterface();
	}

	/// <summary>
	/// Shouts if the interface was built with a hole in it.
	/// </summary>
	/// <remarks>
	/// Added because a refactor dropped half the controls on the floor and nothing noticed. The
	/// headless run builds the interface but never touches it, so it passed cleanly and the
	/// first sign of trouble was a null reference on the opening turn of a real session. This
	/// runs in both paths and names exactly what is missing.
	/// </remarks>
	private void VerifyInterface()
	{
		var missing = new List<string>();

		void Check(string name, GodotObject control)
		{
			if (control is null)
			{
				missing.Add(name);
			}
		}

		Check(nameof(_status), _status);
		Check(nameof(_roster), _roster);
		Check(nameof(_prompt), _prompt);
		Check(nameof(_log), _log);
		Check(nameof(_logPanel), _logPanel);
		Check(nameof(_showLog), _showLog);
		Check(nameof(_spells), _spells);
		Check(nameof(_stand), _stand);
		Check(nameof(_endTurn), _endTurn);
		Check(nameof(_load), _load);
		Check(nameof(_between), _between);
		Check(nameof(_stash), _stash);
		Check(nameof(_bearer), _bearer);
		Check(nameof(_give), _give);
		Check(nameof(_levelUp), _levelUp);
		Check(nameof(_sheetPanel), _sheetPanel);
		Check(nameof(_sheet), _sheet);
		Check(nameof(_showSheet), _showSheet);
		Check(nameof(_rest), _rest);
		Check(nameof(_press), _press);

		foreach (var mode in ModeOrder)
		{
			Check($"mode {mode}", _modes.GetValueOrDefault(mode));
		}

		if (missing.Count > 0)
		{
			GD.PushError($"The interface was built without: {string.Join(", ", missing)}");
		}
	}

	/// <summary>Who is in the fight and how they are doing, across the top.</summary>
	private void BuildRoster(CanvasLayer layer)
	{
		var bar = new PanelContainer();
		layer.AddChild(bar);
		bar.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopWide);
		bar.CustomMinimumSize = new Vector2(0, 72);

		var margin = Padded();
		bar.AddChild(margin);

		var across = new HBoxContainer();
		margin.AddChild(across);

		var lines = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		across.AddChild(lines);

		_status = new Label();
		lines.AddChild(_status);

		_roster = new RichTextLabel
		{
			BbcodeEnabled = true,
			FitContent = true,
			ScrollActive = false,
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
		};

		lines.AddChild(_roster);

		_showLog = new Button
		{
			Text = "Log",
			ToggleMode = true,
			ButtonPressed = true,
			CustomMinimumSize = new Vector2(80, 0),
		};

		_showLog.Toggled += _ => RefreshLogPanel();
		across.AddChild(_showLog);

		_showSheet = new Button
		{
			Text = "Sheet",
			ToggleMode = true,
			CustomMinimumSize = new Vector2(90, 0),
		};

		_showSheet.Toggled += _ => RefreshSheet();
		across.AddChild(_showSheet);
	}

	/// <summary>The combat log, down the right-hand side and foldable out of the way.</summary>
	private void BuildLog(CanvasLayer layer)
	{
		_logPanel = new PanelContainer();
		layer.AddChild(_logPanel);

		// The right quarter, between the roster and the controls.
		_logPanel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		_logPanel.AnchorLeft = 0.74f;
		_logPanel.OffsetLeft = 0;
		_logPanel.OffsetTop = 76;
		_logPanel.AnchorBottom = 0.74f;
		_logPanel.OffsetBottom = 0;

		var margin = Padded();
		_logPanel.AddChild(margin);

		_log = new RichTextLabel
		{
			ScrollFollowing = true,
			SizeFlagsVertical = Control.SizeFlags.ExpandFill,
		};

		margin.AddChild(_log);
	}

	/// <summary>
	/// The character sheet: every derived number beside the parts it was made of.
	/// </summary>
	/// <remarks>
	/// Overlaid on the board rather than docked, because it is read rather than watched — you
	/// open it, work out why the longsword only hits on a fourteen, and close it again.
	/// </remarks>
	private void BuildSheet(CanvasLayer layer)
	{
		_sheetPanel = new PanelContainer { Visible = false };
		layer.AddChild(_sheetPanel);

		_sheetPanel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		_sheetPanel.AnchorLeft = 0.08f;
		_sheetPanel.AnchorRight = 0.72f;
		_sheetPanel.OffsetLeft = 0;
		_sheetPanel.OffsetRight = 0;
		_sheetPanel.OffsetTop = 84;
		_sheetPanel.AnchorBottom = 0.74f;
		_sheetPanel.OffsetBottom = -8;

		var margin = Padded();
		_sheetPanel.AddChild(margin);

		_sheet = new RichTextLabel
		{
			BbcodeEnabled = true,
			ScrollFollowing = false,
			SizeFlagsVertical = Control.SizeFlags.ExpandFill,
		};

		margin.AddChild(_sheet);
	}

	/// <summary>Redraws the sheet for whoever the party picker is pointing at.</summary>
	private void RefreshSheet()
	{
		if (_sheetPanel is null || _showSheet is null)
		{
			return;
		}

		_sheetPanel.Visible = _showSheet.ButtonPressed;
		_showSheet.Text = _showSheet.ButtonPressed ? "Close" : "Sheet";

		if (!_sheetPanel.Visible)
		{
			return;
		}

		var creature = Subject();
		if (creature is null)
		{
			_sheet.Text = "Nobody to look at.";
			return;
		}

		var text = new System.Text.StringBuilder();
		text.Append($"[b]{creature.Name}[/b]\n");

		foreach (var section in CharacterSheet.Of(creature))
		{
			if (section.Lines.Count == 0)
			{
				continue;
			}

			text.Append($"\n[b]{section.Heading}[/b]\n");
			foreach (var line in section.Lines)
			{
				text.Append($"  {line}\n");
			}
		}

		_sheet.Text = text.ToString();
	}

	/// <summary>
	/// Whose sheet to show: whoever the party picker names, else whoever is acting.
	/// </summary>
	private Creature Subject()
	{
		if (_bearer is not null
			&& _bearer.Selected >= 0
			&& _bearer.Selected < _campaign.Party.Count)
		{
			return _campaign.Party[_bearer.Selected];
		}

		return _battle.Encounter.Current is { IsEnded: false } turn
			? turn.Actor
			: _campaign.Party.FirstOrDefault();
	}

	/// <summary>Everything you click to act, along the bottom where it started.</summary>
	private void BuildControls(CanvasLayer layer)
	{
		var panel = new PanelContainer();
		layer.AddChild(panel);
		panel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.BottomWide);
		panel.AnchorTop = 0.74f;
		panel.OffsetTop = 0;
		panel.CustomMinimumSize = new Vector2(0, 190);

		var margin = Padded();
		panel.AddChild(margin);

		var rows = new VBoxContainer();
		margin.AddChild(rows);

		_prompt = new Label();
		rows.AddChild(_prompt);

		// Grouped by when you reach for them: what to do with this turn, what to do with the
		// turn itself, and what to do once the fighting has stopped. Flow containers rather
		// than boxes, so a row wraps instead of running off the edge.
		var actions = new HFlowContainer();
		rows.AddChild(actions);

		foreach (var mode in ModeOrder)
		{
			var button = new Button
			{
				Text = mode == Mode.Full ? "Full attack" : mode.ToString(),
				ToggleMode = true,
				CustomMinimumSize = new Vector2(110, 0),
			};

			button.Pressed += () => ChooseMode(mode);
			actions.AddChild(button);
			_modes[mode] = button;
		}

		_spells = new OptionButton { CustomMinimumSize = new Vector2(220, 0) };
		actions.AddChild(_spells);

		var turn = new HFlowContainer();
		rows.AddChild(turn);

		_stand = new Button { Text = "Stand up" };
		_stand.Pressed += OnStandUp;
		turn.AddChild(_stand);

		_endTurn = new Button { Text = "End turn", CustomMinimumSize = new Vector2(110, 0) };
		_endTurn.Pressed += OnEndTurn;
		turn.AddChild(_endTurn);

		var save = new Button { Text = "Save" };
		save.Pressed += OnSave;
		turn.AddChild(save);

		_load = new Button { Text = "Load", Disabled = !SaveExists() };
		_load.Pressed += OnLoad;
		turn.AddChild(_load);

		// Hidden outright while anyone is still swinging rather than merely greyed: these are
		// not choices you have during a fight, and a row of dead controls is just clutter.
		_between = new HFlowContainer { Visible = false };
		rows.AddChild(_between);

		_between.AddChild(new Label { Text = "Spoils" });

		_stash = new OptionButton { CustomMinimumSize = new Vector2(260, 0) };
		_between.AddChild(_stash);

		_between.AddChild(new Label { Text = "to" });

		_bearer = new OptionButton { CustomMinimumSize = new Vector2(110, 0) };
		_bearer.ItemSelected += _ => RefreshSheet();
		_between.AddChild(_bearer);

		_give = new Button { Text = "Take" };
		_give.Pressed += OnTake;
		_between.AddChild(_give);

		_levelUp = new Button { Text = "Level up" };
		_levelUp.Pressed += OnLevelUp;
		_between.AddChild(_levelUp);

		_rest = new Button { Text = "Rest" };
		_rest.Pressed += OnRest;
		_between.AddChild(_rest);

		_press = new Button { Text = "Press on", CustomMinimumSize = new Vector2(110, 0) };
		_press.Pressed += OnPressOn;
		_between.AddChild(_press);

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
		RefreshStash();

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
		var standing = string.Join("    ", _battle.Encounter.Order.Select(combatant =>
		{
			var creature = combatant.Creature;

			// Hit points alone do not explain a werewolf on nought still swinging at you, so
			// the state that does gets said out loud beside them.
			var trouble = creature.Conditions.Select(condition => condition.ToString()).ToList();
			if (creature.HitPoints.State != HitPointState.Healthy)
			{
				trouble.Insert(0, creature.HitPoints.State.ToString().ToLowerInvariant());
			}

			var note = trouble.Count > 0 ? $" ({string.Join(", ", trouble)})" : string.Empty;
			var acting = _battle.Encounter.Current is { IsEnded: false } open
				&& ReferenceEquals(open.Actor, creature);

			// Dimmed once they are out of it, so a glance up tells you who is still standing.
			var colour = !creature.IsConscious ? "888888"
				: _battle.SideOf(creature) == Ironbound.Simulation.Side.Party ? "8ad4ff"
				: "ff9a5a";

			var text = $"{creature.Name} {creature.HitPoints.Current}/{creature.HitPoints.Maximum}{note}";

			return acting
				? $"[b][color=#{colour}]> {text}[/color][/b]"
				: $"[color=#{colour}]{text}[/color]";
		}));

		var budget = _battle.Encounter.Current is { IsEnded: false } turn
			? $"   ·   {turn.Budget}"
			: string.Empty;

		var earned = _campaign.NextLevelAt is { } next
			? $"   ·   xp {_campaign.Experience:n0} / {next:n0}"
			: $"   ·   xp {_campaign.Experience:n0}";

		_status.Text = $"{_campaign.Definition.Name}   ·   "
			+ $"chapter {_campaign.Chapter} of {_campaign.Definition.Encounters.Count}   ·   "
			+ $"rests {_campaign.RestsRemaining}{earned}   ·   round {_battle.Round}{budget}";

		_roster.Text = standing;
	}

	// ---- the headless proof ----

	/// <summary>
	/// Plays a few turns, writes a save, reads it back, and finishes from the reloaded copy — so
	/// that saving is exercised through the engine's own file handling rather than only in a test.
	/// Everyone is driven by the AI here; there is nobody to click anything.
	/// </summary>
	private void RunHeadlessAndQuit()
	{
		// Exercised rather than merely built: these are the paths a real session hits on its
		// first turn, and running them here is what turns a broken refactor into a failed run
		// instead of a crash in front of whoever opened the game.
		RefreshControls();
		UpdateStatus();
		if (_battle.Party.FirstOrDefault() is { } anybody)
		{
			SelectSpellsFor(anybody);
		}

		foreach (var line in _battle.Log)
		{
			GD.Print($"  {line}");
		}

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

			_campaign.Collect();
			GD.Print($"--- taken from the fallen: {Sack()} ---");
			GD.Print($"--- experience {_campaign.Experience:n0}"
				+ $" (level {_campaign.EarnedLevel}, next at {_campaign.NextLevelAt:n0}) ---");

			// Merrin joined a level behind the fighters, so a shared pool closes the gap.
			foreach (var ready in _campaign.Ready.ToList())
			{
				_campaign.LevelUp(ready);
				GD.Print($"--- {ready.Name} is now {ready.Description} ---");
			}

			// The point of the whole layer: the sergeant's silvered blade is the answer to what
			// is waiting in the clearing, and it only exists because somebody was carrying it.
			if (_campaign.Stash.FirstOrDefault(item => item.Id == "silvered-longsword") is { } silver)
			{
				var bearer = _campaign.Party.First(one => one.IsConscious);
				_campaign.Give(bearer, silver.Id);
				GD.Print($"--- {bearer.Name} takes the {silver.Name} ---");
			}

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

	private string Sack() =>
		_campaign.Stash.Count == 0
			? "nothing"
			: string.Join(", ", _campaign.Stash.Select(item => item.Name));

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
