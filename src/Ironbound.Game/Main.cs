using System.Collections.Generic;
using System.Linq;
using Godot;
using Ironbound.Rules.Combat;
using Ironbound.Rules.Conditions;
using Ironbound.Rules.Content;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Encounters;
using Ironbound.Rules.Classes;
using Ironbound.Rules.Encounters.Actions;
using Ironbound.Rules.Feats;
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
	/// <summary>The one save slot — except for the headless proof, which writes its own file so
	/// that checking the build never overwrites somebody's game.</summary>
	private static string SavePath => DisplayServer.GetName() == "headless" ? "user://headless-proof.save" : "user://ironbound.save";

	/// <summary>The run the game opens on. Picking between them is a menu this has no need of yet.</summary>
	private const string CampaignId = "the-long-road";

	private static readonly Color PartyColour = new(0.35f, 0.55f, 0.85f);
	private static readonly Color FoeColour = new(0.75f, 0.35f, 0.30f);
	private static readonly Color PillarColour = new(0.38f, 0.36f, 0.34f);

	/// <summary>
	/// How far the grid floats above the floor. Terrain tiles put their top face at zero, and a
	/// grid drawn at exactly zero would z-fight with every one of them.
	/// </summary>
	private const float GridHeight = 0.006f;

	// Bright enough to find at a glance, and coloured by side so "is it my move?" needs no
	// reading. The ring sits under whoever is acting.
	private static readonly Color ActivePartyColour = new(0.45f, 0.85f, 1.00f);
	private static readonly Color ActiveFoeColour = new(1.00f, 0.55f, 0.30f);
	private static readonly Color LegalColour = new(0.35f, 0.75f, 0.40f, 0.45f);
	private static readonly Color IllegalColour = new(0.75f, 0.30f, 0.30f, 0.35f);

	// Where a click would land, coloured by what kind of click it is. Faint throughout: these
	// are hints, not the subject.
	private static readonly Color ReachColour = new(0.40f, 0.62f, 0.90f, 0.20f);
	private static readonly Color StrikeColour = new(0.90f, 0.45f, 0.35f, 0.26f);
	private static readonly Color SpellColour = new(0.70f, 0.50f, 0.95f, 0.24f);
	private static readonly Color HelpColour = new(0.45f, 0.85f, 0.50f, 0.24f);

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

		/// <summary>Kneel beside somebody on the floor and stop the bleeding.</summary>
		Help,
	}

	private static readonly Mode[] ModeOrder =
		[Mode.Move, Mode.Attack, Mode.Full, Mode.Trip, Mode.Shove, Mode.Help, Mode.Cast];

	private readonly Dictionary<Creature, Node3D> _figures = new();
	private readonly Dictionary<Creature, Label3D> _nameplates = new();

	/// <summary>Loaded once and instanced many times, so three goblins are one file read.</summary>
	private readonly Dictionary<string, PackedScene> _models = new();

	private ContentLibrary _content;
	private Campaign _campaign;
	private Battle _battle;
	private IActionSource _enemies;
	private Node3D _world;
	private Camera3D _camera;
	private MeshInstance3D _cursor;
	private MeshInstance3D _turnMarker;
	private ShaderMaterial _groundPaint;
	private MultiMeshInstance3D _reach;
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
	private PanelContainer _levelPanel;
	private OptionButton _levelClass;
	private OptionButton _levelFeat;
	private Button _levelTake;
	private Button _levelClose;
	private RichTextLabel _levelDetail;
	private readonly Dictionary<Stance, Button> _stances = new();
	private HFlowContainer _between;
	private RichTextLabel _roster;
	private PanelContainer _logPanel;
	private Button _showLog;
	private readonly Dictionary<Mode, Button> _modes = new();

	private bool _autoplay;
	private bool _passTurns;
	private Mode _mode = Mode.Move;
	private GridSquare? _hovered;

	public override void _Ready()
	{
		_content = GodotContent.Load();

		// The front door, unless the command line already said which fight to run.
		if (SkipMenu(out var campaignId))
		{
			StartCampaign(Campaign.Begin(_content, campaignId));
			return;
		}

		// -- --menu adventures: straight to the choice of adventure, for looking at it.
		var menu = System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--menu");
		if (menu >= 0 && OS.GetCmdlineUserArgs().ElementAtOrDefault(menu + 1) == "adventures")
		{
			ShowAdventures();
			return;
		}

		ShowTitle();
	}

	/// <summary>Everything that used to happen at start-up, now that start-up has a menu in front of it.</summary>
	private void StartCampaign(Campaign campaign)
	{
		_campaign = campaign;
		Begin(_campaign.Battle);

		_instant = DisplayServer.GetName() == "headless";

		// godot --path . -- --autoplay: the party is played by the same heuristic as the enemy,
		// so a whole fight can be watched — or recorded with --write-movie — without a click.
		_autoplay = OS.GetCmdlineUserArgs().Contains("--autoplay");

		// -- --pass-turns: the party does nothing and ends each turn after a pause. For looking
		// at the interface on everybody's turn without a hand on the mouse.
		_passTurns = OS.GetCmdlineUserArgs().Contains("--pass-turns");

		BuildInterface();
		BuildSky();
		RebuildWorld();

		if (DisplayServer.GetName() == "headless")
		{
			RunHeadlessAndQuit();
			return;
		}

		GetViewport().SizeChanged += OnViewportResized;

		if (OS.GetCmdlineUserArgs().Contains("--camera-tour"))
		{
			RunCameraTour();
		}

		// Walking a level, nobody has rolled for anything yet; the order comes with the first fight.
		if (!Exploring)
		{
			ReportInitiative();
		}

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
				var finished = _campaign.CurrentArea;
				if (_campaign.Collect() is > 0 and var taken)
				{
					// Through the queue like every other line, or the looting is reported in
					// round one of a fight that is still being shown.
					Append($"— {taken} thing(s) taken from the fallen —", []);
				}

				if (_campaign.IsLevel)
				{
					// After the last blow has been shown, not while it is still in the air.
					Enqueue(0.0, () => AfterLevelFight(finished));
					return;
				}

				Prompt(Verdict());
				RefreshFigures();
				RefreshControls();
				return;
			}

			Append($"[round {turn.Round}] {turn.Actor.Name}", turn.Lines);
			StageTurn(turn.Actor, _battle.IsPartyTurn);

			// Nothing to decide and nobody to ask. The turn still opens, so effects tick and
			// the dying keep dying, but it closes itself rather than waiting on a click.
			if (!turn.Combatant.CanAct)
			{
				Append(null, new List<string> { Idle(turn.Combatant) });
				_battle.EndTurn();
				RefreshFigures();
				continue;
			}

			if (_battle.NeedsPlayer && !_autoplay)
			{
				_hovered = null;
				SelectSpellsFor(turn.Actor);
				PromptTurn();
				RefreshFigures();
				RefreshControls();

				if (_passTurns)
				{
					var passing = turn.Actor;
					GetTree().CreateTimer(2.4).Timeout += () =>
					{
						if (_battle.Encounter.Current is { IsEnded: false } still
							&& ReferenceEquals(still.Actor, passing)
							&& !StageBusy)
						{
							OnEndTurn();
						}
					};
				}

				return;
			}

			// Say whose turn it is for the enemy too. Left alone, the prompt went on naming the
			// last player to act all the way through the goblins' round.
			Prompt($"{turn.Actor.Name} acts.");

			RunEnemyTurn();
			RefreshFigures();
		}
	}

	/// <summary>What to say once the fighting stops, which depends on what is left of the run.</summary>
	private string Verdict() => _campaign.State switch
	{
		CampaignState.Exploring => _campaign.CanRest
			? "Click to walk; click a door, a crossing or a chest to use it. You have a rest in hand."
			: "Click to walk; click a door, a crossing or a chest to use it.",
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

		// The log takes a quarter of the screen with it. If the player has not taken the camera,
		// re-frame the board in what is left.
		OnViewportResized();
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

		if (_hotbar is not null && between)
		{
			_hotbar.Visible = false;
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

		// Not a screen you leave open into a fight.
		if (_levelPanel is not null && !between)
		{
			_levelPanel.Visible = false;
		}

		RefreshLevelUp();
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

		// In her hand now, not at the start of the next chapter.
		Rearm(bearer);
		RefreshControls();
		UpdateStatus();
	}

	/// <summary>Opens the level-up screen rather than levelling on the spot.</summary>
	private void OnLevelUp()
	{
		if (Subject() is not { } creature || !_campaign.CanLevel(creature))
		{
			Refuse("Nobody here has earned a level yet.");
			return;
		}

		_levelPanel.Visible = true;
		_levelClass.Clear();
		_levelFeat.Clear();
		RefreshLevelUp();
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

	/// <summary>The fight being looked at, as its content file wrote it.</summary>
	private EncounterDefinition CurrentEncounter()
	{
		var chapters = _campaign.Definition.Encounters;
		var index = _campaign.Chapter - 1;

		return index >= 0 && index < chapters.Count ? _content.GetEncounter(chapters[index]) : null;
	}

	/// <summary>What the fight is fought on, if its file says. Null is a bare floor.</summary>
	private TerrainDefinition CurrentTerrain() =>
		CurrentEncounter()?.Terrain is { Length: > 0 } id ? _content.GetTerrain(id) : null;

	private string CurrentChapterName() => CurrentEncounter()?.Name ?? "the next fight";

	/// <summary>Why somebody is doing nothing this turn, in as few words as carry the meaning.</summary>
	private static string Idle(Combatant combatant)
	{
		var creature = combatant.Creature;

		if (combatant.IsUnaware)
		{
			return $"{creature.Name} is taken by surprise and loses the turn";
		}

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

			Stage(_battle.LastResult, lines);
		}

		_battle.EndTurn();
	}

	private void OnStance(Stance stance)
	{
		if (_battle.Encounter.Current is not { IsEnded: false } turn)
		{
			return;
		}

		var actor = turn.Actor;

		if (!actor.Stances.Toggle(stance))
		{
			Refuse($"{actor.Name} has not the training for {Stances.Name(stance)}.");
			RefreshControls();
			return;
		}

		var now = actor.Stances.IsActive(stance) ? "takes up" : "drops";
		_log.AddText($"— {actor.Name} {now} {Stances.Name(stance)} —\n");

		RefreshControls();
		UpdateStatus();
	}

	private void OnStandUp()
	{
		if (StageBusy)
		{
			return;
		}

		var lines = _battle.Act(new StandUpAction());
		if (lines.Count == 0)
		{
			Refuse("Nobody is on the floor.");
			return;
		}

		Stage(_battle.LastResult, lines);
		RefreshFigures();
		PromptTurn();
		RefreshControls();
		_hovered = null;
	}

	private void OnEndTurn()
	{
		if (StageBusy)
		{
			return;
		}

		_battle.EndTurn();
		StartNextTurn();
	}

	// ---- what the player clicks ----

	public override void _UnhandledInput(InputEvent @event)
	{
		if (_campaign is null)
		{
			return;
		}

		// The camera first: wheel, middle-drag and its keys are never a move or an attack. Being
		// *unhandled* input, a wheel over the log panel has already been eaten by the log.
		if (CameraInput(@event) || HudInput(@event))
		{
			return;
		}

		if (@event is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }
			|| StageBusy
			|| SquareUnderCursor() is not { } square)
		{
			return;
		}

		// Between chapters there is no turn, no budget and nothing to provoke, so a click just
		// puts somebody where you pointed. The board is a camp rather than a battlefield.
		if (_campaign.State != CampaignState.Fighting)
		{
			if (_campaign.IsLevel)
			{
				Travel(square);
			}
			else
			{
				Wander(square);
			}

			return;
		}

		if (!_battle.IsPartyTurn)
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

		Stage(_battle.LastResult, lines);
		RefreshFigures();
		PromptTurn();
		RefreshControls();
		_hovered = null;
	}

	/// <summary>
	/// Walks whoever the party selector names to a square, with none of the rules attached.
	/// </summary>
	/// <remarks>
	/// Walls and other people still stop you, because walking through a pillar is not freedom,
	/// it is a bug. Nothing carries into the next chapter — <c>Press on</c> rebuilds the board
	/// from the encounter file — so this is for looking around rather than for arranging a line.
	/// </remarks>
	private void Wander(GridSquare square)
	{
		if (_battle.Battlefield is not { } field || Subject() is not { } walker)
		{
			return;
		}

		if (!field.IsFree(square))
		{
			Refuse("Something is already there.");
			return;
		}

		var from = field.SquareOf(walker);
		field.Remove(walker);
		field.Place(walker, square);

		if (from is { } start && !_instant)
		{
			StageWalk(walker, [start, square]);
		}

		RefreshFigures();
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
			case Mode.Help:
				if (occupant is null || actor.IsEnemyOf(occupant))
				{
					return "There is nobody of yours there.";
				}

				return occupant.HitPoints.State == HitPointState.Dying
					? $"{actor.Name} cannot reach {occupant.Name}."
					: $"{occupant.Name} is not bleeding out.";

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

			case Mode.Help:
				return occupant is not null && !actor.IsEnemyOf(occupant)
					? new StabiliseAction(occupant)
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

		if (GroundUnder(GetViewport().GetMousePosition()) is not { } hit)
		{
			return null;
		}

		var square = new GridSquare(Mathf.FloorToInt(hit.X), Mathf.FloorToInt(hit.Z));

		return field.Contains(square) ? square : null;
	}

	public override void _Process(double delta)
	{
		// Nothing to run until the menu has started something.
		if (_campaign is null)
		{
			return;
		}

		PlayStage(delta);
		CameraKeys(delta);
		StepWalkers(delta);
		TendLevel(delta);
		AnnounceExperience();

		if (_cursor is null)
		{
			return;
		}

		var fighting = _campaign.State == CampaignState.Fighting;

		if (StageBusy || (fighting && !_battle.IsPartyTurn) || SquareUnderCursor() is not { } square)
		{
			_cursor.Visible = false;
			return;
		}

		_cursor.Visible = true;
		_cursor.Position = new Vector3(square.X + 0.5f, 0.02f, square.Y + 0.5f);

		// Out of a fight the only thing that can refuse you is somebody standing there.
		if (!fighting)
		{
			_cursorPaint.AlbedoColor =
				_battle.Battlefield?.IsFree(square) == true ? LegalColour : IllegalColour;

			return;
		}

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

		// Whatever was walking or being read belongs to the run being replaced.
		_walkers.Clear();
		_held = false;
		_page?.QueueFree();
		_page = null;

		// The bodies were built for the fight that is being replaced, so they go with it.
		RebuildWorld();

		_log.Clear();
		_log.AddText("— loaded —\n");
		if (!Exploring)
		{
			ReportInitiative();
		}

		StartNextTurn();
	}

	// ---- the scene ----

	/// <summary>
	/// The ground, with its grid.
	/// </summary>
	/// <remarks>
	/// Every distance in the rules is counted in five-foot squares and until now not one of them
	/// was visible, so reach, movement and a fireball's radius all had to be guessed at. Falls
	/// back to the old flat colour if the shader will not load, because a board you can play on
	/// beats no board at all.
	/// </remarks>
	private static Material GroundPaint()
	{
		if (GD.Load<Shader>("res://grid.gdshader") is { } shader)
		{
			return new ShaderMaterial { Shader = shader };
		}

		GD.PushError("Content: grid.gdshader would not load; falling back to a bare floor.");
		return new StandardMaterial3D { AlbedoColor = new Color(0.20f, 0.22f, 0.20f) };
	}

	/// <summary>
	/// The daylight the flat-shaded models need to be worth looking at.
	/// </summary>
	/// <remarks>
	/// A child of the scene rather than of <c>_world</c>, so it survives every
	/// <see cref="RebuildWorld"/>. There is one sun and no ambient term without it, which means
	/// every face turned away from that sun renders pure black — fine for grey capsules, ruinous
	/// for a model whose whole shading budget is one flat colour per material.
	/// </remarks>
	private void BuildSky()
	{
		var air = new Godot.Environment
		{
			BackgroundMode = Godot.Environment.BGMode.Color,
			BackgroundColor = new Color(0.07f, 0.08f, 0.11f),

			// A colour rather than a sky: cheap, and it does the one job wanted of it.
			AmbientLightSource = Godot.Environment.AmbientSource.Color,
			AmbientLightColor = new Color(0.58f, 0.62f, 0.72f),

			// Enough to keep a face turned away from the sun readable, and no more: the ground
			// models are pale, and ambient on top of full sun washed them out to white paper.
			AmbientLightEnergy = 0.35f,
			TonemapMode = Godot.Environment.ToneMapper.Filmic,
		};

		_air = air;
		AddChild(new WorldEnvironment { Environment = air });
	}

	/// <summary>
	/// A mesh named by a content file, whichever way Godot chose to import it.
	/// </summary>
	/// <remarks>
	/// The same <c>.obj</c> arrives as a bare <see cref="Mesh"/> or as a whole
	/// <see cref="PackedScene"/> depending on which importer claimed the file, and which one that
	/// is depends on editor settings rather than on anything in this repository. Handling both
	/// costs three lines. Anything that will not load returns null and the caller falls back to
	/// the plain shapes, because a board you can play on beats no board at all.
	/// </remarks>
	private static Mesh Model(string path)
	{
		if (string.IsNullOrEmpty(path))
		{
			return null;
		}

		if (!ResourceLoader.Exists(path))
		{
			GD.PushError($"Content: no model at {path}; falling back to plain shapes.");
			return null;
		}

		switch (GD.Load(path))
		{
			case Mesh mesh:
				return Earthly(mesh);

			case PackedScene scene:
				var root = scene.Instantiate();
				var found = FirstMesh(root);
				root.QueueFree();
				return found is null ? null : Earthly(found);

			default:
				GD.PushError($"Content: {path} loaded, but it is not a model.");
				return null;
		}
	}

	/// <summary>
	/// Takes the polish off a model that arrived claiming to be made of metal.
	/// </summary>
	/// <remarks>
	/// Wavefront's <c>Ks</c> is a specular colour, and Godot's importer reads its brightest
	/// channel as metalness. Every material in this art pack sets <c>Ks</c> to pure white, so
	/// sand, bark and leaves all import as polished metal — which has no diffuse term at all, and
	/// with a flat colour for a sky there is nothing for it to reflect instead. The result is a
	/// rock that reads as a hole in the floor.
	/// <para>
	/// Corrected here rather than by editing the <c>.mtl</c> files, so that anything else dropped
	/// into <c>art/</c> later is corrected too, and the art stays exactly as it was received.
	/// </para>
	/// </remarks>
	private static Mesh Earthly(Mesh mesh)
	{
		for (var surface = 0; surface < mesh.GetSurfaceCount(); surface++)
		{
			if (mesh.SurfaceGetMaterial(surface) is StandardMaterial3D material)
			{
				material.Metallic = 0f;
				material.Roughness = 0.9f;
			}
		}

		return mesh;
	}

	private static Mesh FirstMesh(Node node)
	{
		if (node is MeshInstance3D { Mesh: { } mesh })
		{
			return mesh;
		}

		foreach (var child in node.GetChildren())
		{
			if (FirstMesh(child) is { } found)
			{
				return found;
			}
		}

		return null;
	}

	/// <summary>
	/// Lays one ground mesh on every square, as a single multimesh.
	/// </summary>
	/// <remarks>
	/// A sixteen-by-twelve board is a hundred and ninety-two tiles and one draw call, which is
	/// why the identical ground goes through a <see cref="MultiMesh"/> and the handful of props
	/// do not.
	/// </remarks>
	private void LayTiles(Mesh tile, int width, int height)
	{
		var box = tile.GetAabb();
		var middle = box.GetCenter();

		// Models arrive at whatever size they were modelled at. Scaled to exactly one square,
		// because a square is five feet and every distance in the rules is counted in them.
		var footprint = Mathf.Max(box.Size.X, box.Size.Z);
		var scale = footprint > 0f ? 1f / footprint : 1f;

		// Sunk until the top face is y=0, which is where the figures, the hover cursor and the
		// turn ring all already expect the floor to be.
		var sunk = -box.End.Y * scale;

		var tiles = new MultiMeshInstance3D
		{
			Multimesh = new MultiMesh
			{
				TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
				Mesh = tile,
				InstanceCount = width * height,
			},
		};

		_world.AddChild(tiles);

		for (var y = 0; y < height; y++)
		{
			for (var x = 0; x < width; x++)
			{
				var placed = new Transform3D(
					Basis.Identity.Scaled(Vector3.One * scale),
					new Vector3(
						x + 0.5f - (middle.X * scale),
						sunk,
						y + 0.5f - (middle.Z * scale)));

				tiles.Multimesh.SetInstanceTransform((y * width) + x, placed);
			}
		}
	}

	/// <summary>
	/// Whatever stands on a square nothing can walk through: the terrain's own prop, or the grey
	/// box that stood there before there was any art.
	/// </summary>
	private void SpawnPillar(int x, int y, Mesh prop)
	{
		if (prop is null)
		{
			var pillar = new MeshInstance3D
			{
				Mesh = new BoxMesh { Size = new Vector3(0.9f, 2.2f, 0.9f) },
				MaterialOverride = new StandardMaterial3D { AlbedoColor = PillarColour },
			};

			_world.AddChild(pillar);
			pillar.Position = new Vector3(x + 0.5f, 1.1f, y + 0.5f);
			return;
		}

		var box = prop.GetAabb();
		var footprint = Mathf.Max(box.Size.X, box.Size.Z);

		// Trimmed to fit its square, never grown: a rock modelled smaller than five feet across
		// is a rock that small, and stretching it would be a lie about what blocks the line.
		var scale = footprint > 0.95f ? 0.95f / footprint : 1f;

		var piece = new MeshInstance3D { Mesh = prop };
		_world.AddChild(piece);

		piece.Scale = Vector3.One * scale;
		piece.Position = new Vector3(x + 0.5f, -box.Position.Y * scale, y + 0.5f);

		// Turned by a repeatable eighth, so two rocks in a row are not the same rock twice.
		// Derived from the square rather than rolled, because a reloaded save must look the same.
		piece.RotateY(Mathf.DegToRad((((x * 37) + (y * 61)) % 8) * 45f));
	}

	private void RebuildWorld()
	{
		if (_world is not null)
		{
			RemoveChild(_world);
			_world.QueueFree();
		}

		_figures.Clear();
		_nameplates.Clear();
		ClearStage();
		_world = new Node3D();
		AddChild(_world);

		var field = _battle.Battlefield;
		var width = field?.Width ?? 12;
		var height = field?.Height ?? 12;

		// One Godot unit is one five-foot square, so rules coordinates need no conversion.
		var centre = new Vector3(width / 2f, 0, height / 2f);

		_camera = new Camera3D { Projection = Camera3D.ProjectionType.Orthogonal };
		_world.AddChild(_camera);

		// Where it sits and how far in is the rig's business, and outlives this node.
		MountCamera(width, height);

		var light = new DirectionalLight3D { ShadowEnabled = true };
		_world.AddChild(light);
		_sun = light;
		if (_air is not null)
		{
			// Daylight again; a cave level dims it in LightLevel.
			_air.AmbientLightEnergy = 0.35f;
			_air.AmbientLightColor = new Color(0.58f, 0.62f, 0.72f);
		}

		light.Position = centre + new Vector3(5, 10, 3);
		light.LookAt(centre);

		// What the fight is fought on, if the encounter file says. Both are null for a bare
		// floor, and every path below falls back to the shapes that were there before.
		var level = _campaign.IsLevel;
		var terrain = level ? null : CurrentTerrain();
		var tile = Model(terrain?.Ground);
		var prop = Model(terrain?.Blocked);

		if (level)
		{
			BuildLevel();
		}
		else if (tile is not null)
		{
			LayTiles(tile, width, height);
		}

		var paint = GroundPaint();
		_groundPaint = paint as ShaderMaterial;

		var ground = new MeshInstance3D
		{
			Mesh = new PlaneMesh { Size = new Vector2(width, height) },
			MaterialOverride = paint,

			// Without the shader this plane is an opaque slab, and an opaque slab laid over the
			// terrain would hide it. Losing the grid is survivable; losing the ground is not.
			Visible = _groundPaint is not null || (tile is null && !level),
		};

		_world.AddChild(ground);
		_gridPlane = ground;

		// Over the terrain rather than instead of it: the lines keep their alpha and everything
		// between them turns to glass.
		_groundPaint?.SetShaderParameter("floor_alpha", tile is null && !level ? 1.0f : 0.0f);
		ground.Position = centre + new Vector3(0, GridHeight, 0);

		// Where the current actor could get to. One draw call however many squares light up.
		_reach = new MultiMeshInstance3D
		{
			Multimesh = new MultiMesh
			{
				TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
				Mesh = new PlaneMesh { Size = new Vector2(0.92f, 0.92f) },
				InstanceCount = 0,
			},
			MaterialOverride = new StandardMaterial3D
			{
				AlbedoColor = ReachColour,
				Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			},
		};

		_world.AddChild(_reach);

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

		// Cover the player cannot see is cover the player will call a bug. A level drew its own.
		if (field is not null && !level)
		{
			for (var x = 0; x < width; x++)
			{
				for (var y = 0; y < height; y++)
				{
					if (field.IsBlocked(new GridSquare(x, y)))
					{
						SpawnPillar(x, y, prop);
					}
				}
			}
		}

		Spawn(_battle.Party, PartyColour);
		Spawn(_battle.Foes, FoeColour);
		RebuildFrames();
		RefreshFigures();
	}

	private void Spawn(IReadOnlyList<Creature> creatures, Color colour)
	{
		foreach (var creature in creatures)
		{
			// A holder whose origin is the ground the creature stands on. Capsules are built
			// around their middle and models around their feet, and without this every caller
			// would have to know which of the two it was looking at.
			var figure = new Node3D();
			_world.AddChild(figure);

			var body = Model(creature) ?? Placeholder(creature, colour);
			figure.AddChild(body);
			Arm(creature, body);

			var nameplate = new Label3D
			{
				Text = creature.Name,
				Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
				FontSize = 48,
				PixelSize = 0.005f,

				// Above the head rather than a fixed height, or it sits inside the chest of
				// anything larger than a person.
				Position = new Vector3(0, CapsuleHeight(creature) + 0.35f, 0),

				// The models are textured and cannot be tinted by side without ruining them, so
				// the nameplate carries what the capsule's colour used to: whose turn it serves.
				Modulate = colour.Lightened(0.35f),
			};

			figure.AddChild(nameplate);
			_figures[creature] = figure;
			_nameplates[creature] = nameplate;
			nameplate.Scale = Vector3.One * LabelScale();
			Idle(creature);
		}
	}

	/// <summary>
	/// Which way a figure is turned: towards the nearest enemy still in the fight.
	/// </summary>
	/// <remarks>
	/// Nearest rather than whoever it last swung at, because it wants an answer on every frame
	/// and for everybody, including the three creatures who have not acted yet.
	/// <para>
	/// Null when there is nobody to face — between chapters, or once one side is finished — and
	/// the caller keeps whatever the figure was already looking at. Snapping the whole party
	/// round to due north the moment the last goblin drops looks like a bug.
	/// </para>
	/// <para>
	/// A yaw rather than <see cref="Node3D.LookAt"/>: that aims a node's -Z, and these models
	/// were built facing the other way. Capsules are round and do not care either way.
	/// </para>
	/// </remarks>
	private float? Facing(Creature creature)
	{
		if (_battle.Battlefield is not { } field || field.SquareOf(creature) is not { } here)
		{
			return null;
		}

		GridSquare? quarry = null;
		var closest = int.MaxValue;

		// Both sides in a fixed order, so two equally close enemies are not a coin toss that
		// comes up differently on the next frame.
		foreach (var other in _battle.Party.Concat(_battle.Foes))
		{
			if (other.Allegiance == creature.Allegiance
				|| !other.IsConscious
				|| field.SquareOf(other) is not { } there
				|| field.DistanceInFeet(creature, other) is not { } away
				|| away >= closest)
			{
				continue;
			}

			closest = away;
			quarry = there;
		}

		if (quarry is not { } target || (target.X == here.X && target.Y == here.Y))
		{
			return null;
		}

		// A node yawed by this much has its +Z pointing down the line between the squares.
		return Mathf.Atan2(target.X - here.X, target.Y - here.Y);
	}

	/// <summary>The grey capsule, for anything whose file names no model.</summary>
	private static MeshInstance3D Placeholder(Creature creature, Color colour) => new()
	{
		Mesh = new CapsuleMesh
		{
			Radius = 0.3f * ScaleOf(creature.Size),
			Height = CapsuleHeight(creature),
		},
		MaterialOverride = new StandardMaterial3D { AlbedoColor = colour },

		// Built around its middle, lifted so its base is the holder's origin.
		Position = new Vector3(0, 0.5f * CapsuleHeight(creature), 0),
	};

	/// <summary>Loads a model once, and complains once if the content names one that is not there.</summary>
	private PackedScene Scene(string path, string namedBy)
	{
		if (!_models.TryGetValue(path, out var scene))
		{
			scene = ResourceLoader.Exists(path) ? GD.Load<PackedScene>(path) : null;
			if (scene is null)
			{
				GD.PushError($"Content: {namedBy} names {path}, which will not load.");
			}

			_models[path] = scene;
		}

		return scene;
	}

	/// <summary>
	/// The model a creature's own file names, sized to the square it stands in.
	/// </summary>
	/// <remarks>
	/// A whole scene rather than a bare mesh, because a character brings a skeleton and an
	/// <see cref="AnimationPlayer"/> with it and both are lost by pulling the mesh out.
	/// <para>
	/// Scaled to the height the capsule would have been rather than to a number in the content
	/// file: the art arrives at whatever size it was modelled at, and the size the game wants is
	/// already derived from <see cref="CreatureSize"/>. Two sources for one number is one too
	/// many.
	/// </para>
	/// </remarks>
	private Node3D Model(Creature creature)
	{
		if (creature.DefinitionId is not { } id
			|| _content.GetCreature(id)?.Model is not { Length: > 0 } path)
		{
			return null;
		}

		if (Scene(path, $"creature '{id}'")?.Instantiate() is not Node3D model)
		{
			return null;
		}

		var box = Extent(model, Transform3D.Identity);
		if (box.Size.Y <= 0)
		{
			GD.PushError($"Content: {path} has no geometry to measure.");
			model.QueueFree();
			return null;
		}

		var scale = CapsuleHeight(creature) / box.Size.Y;
		var middle = box.GetCenter();

		model.Scale = Vector3.One * scale;
		model.Position = new Vector3(
			-middle.X * scale, -box.Position.Y * scale, -middle.Z * scale);

		HonourVertexColours(model);
		Breathe(model);
		return model;
	}

	/// <summary>
	/// Switches on vertex colour for any surface that carries it.
	/// </summary>
	/// <remarks>
	/// The goblins' hide has no texture: its shading is baked into the vertex colours, and the
	/// material's own albedo is plain white. Godot's glTF importer brings the colours in and then
	/// leaves the material ignoring them, so without this every goblin is a chalk statue — and
	/// nothing on the Blender side of the pipeline can show you that.
	/// </remarks>
	private static void HonourVertexColours(Node node)
	{
		if (node is MeshInstance3D { Mesh: { } mesh })
		{
			for (var surface = 0; surface < mesh.GetSurfaceCount(); surface++)
			{
				var painted = mesh.SurfaceGetArrays(surface)[(int)Mesh.ArrayType.Color];

				if (painted.VariantType != Variant.Type.Nil
					&& painted.AsColorArray().Length > 0
					&& mesh.SurfaceGetMaterial(surface) is BaseMaterial3D material)
				{
					material.VertexColorUseAsAlbedo = true;
				}
			}
		}

		foreach (var child in node.GetChildren())
		{
			HonourVertexColours(child);
		}
	}

	/// <summary>Every mesh in a scene, in the scene's own space.</summary>
	private static Aabb Extent(Node node, Transform3D inherited)
	{
		var here = node is Node3D spatial ? inherited * spatial.Transform : inherited;
		var box = new Aabb();
		var found = false;

		if (node is MeshInstance3D { Mesh: not null } instance)
		{
			box = here * instance.Mesh.GetAabb();
			found = true;
		}

		foreach (var child in node.GetChildren())
		{
			var inner = Extent(child, here);
			if (inner.Size == Vector3.Zero)
			{
				continue;
			}

			box = found ? box.Merge(inner) : inner;
			found = true;
		}

		return box;
	}

	/// <summary>
	/// Sets a model idling, so a party standing still does not look like a party of statues.
	/// </summary>
	/// <remarks>
	/// One clip, chosen by name and left looping. Matching the animation to what the creature is
	/// actually doing — swinging, casting, falling over — is a layer of its own, and this is the
	/// cheapest thing that stops the board looking dead while that layer does not exist.
	/// </remarks>
	private static void Breathe(Node model)
	{
		if (Animations(model) is not { } player)
		{
			return;
		}

		var clips = player.GetAnimationList();
		var idle = System.Array.Find(clips, name => name.Contains("idle_combat"))
			?? System.Array.Find(clips, name => name.Contains("idle"));

		if (idle is null)
		{
			return;
		}

		// glTF brings its clips in one-shot; an idle that plays once and stops is worse than
		// none at all, because it stops halfway through a breath.
		if (player.GetAnimation(idle) is { } clip)
		{
			clip.LoopMode = Animation.LoopModeEnum.Linear;
		}

		player.Play(idle);
	}

	private static AnimationPlayer Animations(Node node)
	{
		if (node is AnimationPlayer player)
		{
			return player;
		}

		foreach (var child in node.GetChildren())
		{
			if (Animations(child) is { } found)
			{
				return found;
			}
		}

		return null;
	}

	/// <summary>How tall the capsule standing in for a creature is drawn.</summary>
	private static float CapsuleHeight(Creature creature) => 1.4f * ScaleOf(creature.Size);

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

		BuildHud(layer);

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
		Check(nameof(_levelPanel), _levelPanel);
		Check(nameof(_levelClass), _levelClass);
		Check(nameof(_levelFeat), _levelFeat);
		Check(nameof(_levelTake), _levelTake);
		Check(nameof(_levelClose), _levelClose);
		Check(nameof(_levelDetail), _levelDetail);

		foreach (var stance in new[] { Stance.PowerAttack, Stance.CombatExpertise, Stance.FightingDefensively })
		{
			Check($"stance {stance}", _stances.GetValueOrDefault(stance));
		}
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
		_sheetPanel.AnchorLeft = 0.20f;
		_sheetPanel.AnchorRight = 0.80f;
		_sheetPanel.OffsetLeft = 0;
		_sheetPanel.OffsetRight = 0;
		_sheetPanel.AnchorTop = 0.05f;
		_sheetPanel.OffsetTop = 0;
		_sheetPanel.AnchorBottom = 0.82f;
		_sheetPanel.OffsetBottom = 0;

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

	/// <summary>
	/// The level-up screen: which class takes the level, and which feat comes with it.
	/// </summary>
	/// <remarks>
	/// The same overlay shape as the character sheet, for the same reason — it is read and
	/// decided rather than watched. It opens only between chapters, because that is the only
	/// time anybody can level.
	/// </remarks>
	private void BuildLevelUp(CanvasLayer layer)
	{
		_levelPanel = new PanelContainer { Visible = false };
		layer.AddChild(_levelPanel);

		_levelPanel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		_levelPanel.AnchorLeft = 0.24f;
		_levelPanel.AnchorRight = 0.76f;
		_levelPanel.OffsetLeft = 0;
		_levelPanel.OffsetRight = 0;
		_levelPanel.AnchorTop = 0.08f;
		_levelPanel.OffsetTop = 0;
		_levelPanel.AnchorBottom = 0.80f;
		_levelPanel.OffsetBottom = 0;

		var margin = Padded();
		_levelPanel.AddChild(margin);

		var rows = new VBoxContainer();
		margin.AddChild(rows);

		var picks = new HFlowContainer();
		rows.AddChild(picks);

		picks.AddChild(new Label { Text = "Class" });

		_levelClass = new OptionButton { CustomMinimumSize = new Vector2(200, 0) };
		_levelClass.ItemSelected += _ => RefreshLevelUp();
		picks.AddChild(_levelClass);

		picks.AddChild(new Label { Text = "Feat" });

		_levelFeat = new OptionButton { CustomMinimumSize = new Vector2(280, 0) };
		_levelFeat.ItemSelected += _ => RefreshLevelUp();
		picks.AddChild(_levelFeat);

		_levelTake = new Button { Text = "Take the level", CustomMinimumSize = new Vector2(150, 0) };
		_levelTake.Pressed += OnTakeLevel;
		picks.AddChild(_levelTake);

		_levelClose = new Button { Text = "Not yet" };
		_levelClose.Pressed += () => { _levelPanel.Visible = false; RefreshControls(); };
		picks.AddChild(_levelClose);

		_levelDetail = new RichTextLabel
		{
			BbcodeEnabled = true,
			SizeFlagsVertical = Control.SizeFlags.ExpandFill,
		};

		rows.AddChild(_levelDetail);
	}

	/// <summary>Fills the pickers for whoever the party selector names, and says what is on offer.</summary>
	private void RefreshLevelUp()
	{
		if (_levelPanel is null || !_levelPanel.Visible)
		{
			return;
		}

		if (Subject() is not { } creature || !_campaign.CanLevel(creature))
		{
			_levelDetail.Text = "Nobody here has earned a level.";
			_levelTake.Disabled = true;
			return;
		}

		var classes = _campaign.ClassesFor(creature).ToList();
		var feats = _campaign.FeatsFor(creature).ToList();
		var earnsFeat = _campaign.NextLevelGrantsFeat(creature);

		Fill(_levelClass, classes.Select(taken => taken.Name));
		Fill(_levelFeat, earnsFeat ? feats.Select(feat => feat.Name) : []);

		_levelFeat.Disabled = !earnsFeat || feats.Count == 0;
		_levelTake.Disabled = false;

		var text = new System.Text.StringBuilder();
		text.Append($"[b]{creature.Name}[/b] — {creature.Description}, taking level {creature.Level + 1}\n\n");

		if (_levelClass.Selected >= 0 && _levelClass.Selected < classes.Count)
		{
			var taken = classes[_levelClass.Selected];
			text.Append($"[b]{taken.Name}[/b]: d{taken.HitDie}, {taken.Attack} base attack");
			text.Append(taken.GoodSaves.Count > 0
				? $", good {string.Join(" and ", taken.GoodSaves)}\n"
				: "\n");
		}

		text.Append(earnsFeat
			? $"\nA feat comes with this level. {feats.Count} available.\n"
			: "\nNo feat at this level — the next one is at "
				+ $"{creature.Level + (Levelling.GrantsFeatAt(creature.Level + 2) ? 2 : 3)}.\n");

		if (earnsFeat)
		{
			// The ones they cannot have, and exactly what they are short of. Far more useful
			// than a list that silently omits them — "where is Power Attack?" has an answer.
			var barred = WithheldFeats(creature).ToList();

			if (barred.Count > 0)
			{
				text.Append("\n[b]Not available[/b]\n");
				foreach (var (name, missing) in barred)
				{
					text.Append($"  {name} — needs {missing}\n");
				}
			}
		}

		_levelDetail.Text = text.ToString();
	}

	/// <summary>Feats the creature does not qualify for, and what each one is waiting on.</summary>
	private IEnumerable<(string Name, string Missing)> WithheldFeats(Creature creature) =>
		_content.FeatIds
			.Select(_content.GetFeat)
			.OfType<FeatDefinition>()
			.Where(feat => !creature.HasFeat(feat.Id) && feat.Requires.Unmet(creature).Count > 0)
			.OrderBy(feat => feat.Name, System.StringComparer.Ordinal)
			.Select(feat => (feat.Name, string.Join(", ", feat.Requires.Unmet(creature))));

	private static void Fill(OptionButton picker, IEnumerable<string> entries)
	{
		var wanted = entries.ToList();
		if (picker.ItemCount == wanted.Count)
		{
			return;
		}

		picker.Clear();
		foreach (var entry in wanted)
		{
			picker.AddItem(entry);
		}

		if (wanted.Count > 0)
		{
			picker.Selected = 0;
		}
	}

	private void OnTakeLevel()
	{
		if (Subject() is not { } creature)
		{
			return;
		}

		var classes = _campaign.ClassesFor(creature).ToList();
		var feats = _campaign.FeatsFor(creature).ToList();

		if (_levelClass.Selected < 0 || _levelClass.Selected >= classes.Count)
		{
			return;
		}

		var chosen = _campaign.NextLevelGrantsFeat(creature)
			&& _levelFeat.Selected >= 0
			&& _levelFeat.Selected < feats.Count
				? feats[_levelFeat.Selected]
				: null;

		if (!_campaign.LevelUp(creature, classes[_levelClass.Selected], chosen))
		{
			Refuse($"{creature.Name} cannot take that level.");
			return;
		}

		_log.AddText($"— {creature.Name} is now {creature.Description}"
			+ (chosen is null ? string.Empty : $", and learns {chosen.Name}") + " —\n");

		_levelPanel.Visible = false;
		RefreshControls();
		UpdateStatus();
	}

	/// <summary>Everything you click to act, along the bottom where it started.</summary>
	private void ChooseMode(Mode mode)
	{
		_mode = mode;
		_hovered = null;
		foreach (var (which, button) in _modes)
		{
			button.ButtonPressed = which == mode;
		}

		// The reachable squares are a Move-mode thing, so they come and go with the mode.
		RefreshReach();
	}

	private void SelectSpellsFor(Creature actor)
	{
		// Whose spells are listed is part of whose turn it is, and the board may not have got
		// there yet: refilling it at once blanked the wizard's list while the goblins were
		// still being shown taking the turns in between.
		if (StageBusy)
		{
			Enqueue(0.0, () => SelectSpellsFor(actor));
			return;
		}

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
		// Behind whatever is still playing. The rules are already three goblins further on, and
		// a log that ran ahead of the board would read out the ending while the fight was on.
		if (StageBusy)
		{
			Enqueue(0.0, () => Write(header, lines));
			return;
		}

		Write(header, lines);
	}

	private void Write(string header, IReadOnlyList<string> lines)
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
		if (StageBusy)
		{
			Enqueue(0.0, () => _prompt.Text = text);
		}
		else
		{
			_prompt.Text = text;
		}

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
		// Which buttons are live is a fact about where the fight has got to, and while beats
		// are playing the board has not got there yet. The queue calls this again as it drains.
		if (StageBusy)
		{
			_settleWhenDone = true;
			return;
		}

		var playing = _battle.IsPartyTurn;
		var turn = playing && _battle.Encounter.Current is { IsEnded: false } current ? current : null;

		_endTurn.Disabled = !playing;

		// Greyed out rather than merely refused. Finding out that the standard action is gone by
		// clicking and being told no is the interface making the player do its remembering.
		_modes[Mode.Attack].Disabled = turn is null || !turn.Budget.HasStandard;
		_modes[Mode.Full].Disabled = turn is null || !turn.Budget.CanAfford(ActionCost.FullRound);
		_modes[Mode.Trip].Disabled = turn is null || !turn.Budget.HasStandard;
		_modes[Mode.Shove].Disabled = turn is null || !turn.Budget.HasStandard;
		_modes[Mode.Help].Disabled = turn is null || !turn.Budget.HasStandard;
		_modes[Mode.Cast].Disabled = turn is null
			|| !turn.Budget.HasStandard
			|| !turn.Actor.Spells.Prepared.Any(turn.Actor.Spells.CanCast);
		_modes[Mode.Move].Disabled = turn is null || !CanStillMove(turn);

		_spells.Disabled = turn is null || _spells.ItemCount == 0;
		_stand.Disabled = turn is null || !turn.CanTake(new StandUpAction());

		foreach (var (stance, button) in _stances)
		{
			button.Disabled = turn is null || !turn.Actor.Stances.CanAdopt(stance);
			button.ButtonPressed = turn is not null && turn.Actor.Stances.IsActive(stance);
		}

		_press.Disabled = !_campaign.CanAdvance;
		_press.Visible = !_campaign.IsLevel;
		_rest.Disabled = !_campaign.CanRest;
		RefreshStash();

		// Being left holding a mode that can no longer do anything is its own small trap. Move
		// first, because a five-foot step outlives everything else.
		if (_modes[_mode].Disabled || !_modes[_mode].Visible)
		{
			foreach (var mode in ModeOrder)
			{
				if (!_modes[mode].Disabled && _modes[mode].Visible)
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

	/// <summary>
	/// Brings the board into line with the rules — now, or once what is playing has finished.
	/// </summary>
	/// <remarks>
	/// Every caller means "something changed, show it". While beats are queued the showing is
	/// already in hand, and snapping figures to where the rules have got to would teleport them
	/// to the end of an animation that has not started yet.
	/// </remarks>
	private void RefreshFigures()
	{
		if (StageBusy)
		{
			_settleWhenDone = true;
			return;
		}

		SnapFigures();
	}

	private void SnapFigures()
	{
		var field = _battle.Battlefield;

		foreach (var (creature, figure) in _figures)
		{
			if (field?.SquareOf(creature) is { } square)
			{
				figure.Position = new Vector3(square.X + 0.5f, figure.Position.Y, square.Y + 0.5f);
			}

			figure.Rotation = new Vector3(
				figure.Rotation.X, Facing(creature) ?? figure.Rotation.Y, 0f);

			// Up, flat on the floor or dead — by clip where the model has one, by tipping the
			// holder over where it does not. The origin is the creature's feet, so standing is
			// y=0 whatever it is drawn with.
			Assume(creature, figure, PostureOf(creature), instant: true);
		}

		RefreshTurnMarker();
		RefreshReach();
	}

	/// <summary>
	/// Lights the squares the current actor could actually walk to, and turns the grid off once
	/// there is nobody left to walk.
	/// </summary>
	/// <remarks>
	/// Every square is offered to the rules exactly as a click would be, rather than measured
	/// against a budget worked out here. That is not fastidiousness: a creature that has taken
	/// its five-foot step still has a move action in the bank, so a budget check says yes while
	/// <c>MoveAction</c> says no, and the highlight ends up promising ground the click refuses.
	/// <para>
	/// It follows the mode for nothing, because <c>ActionFor</c> already does: in Move mode it
	/// is the ground you can cross, in Cast mode the squares a spell will reach, in Attack mode
	/// whoever is close enough to hit. The colour says which.
	/// </para>
	/// </remarks>
	private void RefreshReach()
	{
		if (_reach is null)
		{
			return;
		}

		var fighting = _campaign.State == CampaignState.Fighting;
		_groundPaint?.SetShaderParameter("strength", fighting ? 1.0f : 0.0f);

		if (!fighting
			|| !_battle.NeedsPlayer
			|| _battle.Battlefield is not { } field
			|| _battle.Encounter.Current is not { IsEnded: false } turn)
		{
			_reach.Multimesh.InstanceCount = 0;
			return;
		}

		if (_reach.MaterialOverride is StandardMaterial3D paint)
		{
			paint.AlbedoColor = _mode switch
			{
				Mode.Move => ReachColour,
				Mode.Cast => SpellColour,
				Mode.Help => HelpColour,
				_ => StrikeColour,
			};
		}

		var squares = new List<GridSquare>();

		for (var x = 0; x < field.Width; x++)
		{
			for (var y = 0; y < field.Height; y++)
			{
				var square = new GridSquare(x, y);

				if (CanAct(turn.Actor, square))
				{
					squares.Add(square);
				}
			}
		}

		_reach.Multimesh.InstanceCount = squares.Count;

		for (var i = 0; i < squares.Count; i++)
		{
			_reach.Multimesh.SetInstanceTransform(
				i,
				new Transform3D(
					Basis.Identity,
					new Vector3(squares[i].X + 0.5f, 0.015f, squares[i].Y + 0.5f)));
		}
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
		var standing = string.Join("\n", _battle.Encounter.Order.Select(combatant =>
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

		var earned = _campaign.NextLevelAt is { } next
			? $"xp {_campaign.Experience:n0} / {next:n0}"
			: $"xp {_campaign.Experience:n0}";

		// A level has rooms rather than chapters: where the party is, and whether it is a fight.
		var where = _campaign.IsLevel
			? (_campaign.CurrentArea is { } room ? $"{room.Name}   ·   round {_battle.Round}"
				: _battle.Party.Select(one => _battle.Battlefield?.SquareOf(one)).FirstOrDefault(s => s is not null) is { } here
					&& _campaign.AreaAt(here) is { } area ? $"{area.Name}   ·   exploring" : "exploring")
			: $"chapter {_campaign.Chapter} of {_campaign.Definition.Encounters.Count}   ·   round {_battle.Round}";
		var headline = $"{_campaign.Definition.Name}\n"
			+ $"{where}\n"
			+ $"rests {_campaign.RestsRemaining}   ·   {earned}";

		// The faces, the acting character and the action pips, snapshotted with the text so
		// they all tell the same moment of the fight.
		var open = _battle.Encounter.Current is { IsEnded: false } current ? current : null;
		var party = _battle.Party.ToDictionary(
			creature => creature,
			creature => VitalsOf(creature, open is not null && ReferenceEquals(open.Actor, creature)));
		// Between fights there is no turn, so the bar shows whoever leads the party rather than
		// being left on the face of the last goblin to die.
		var actor = open?.Actor ?? _battle.Party.FirstOrDefault(one => one.IsConscious) ?? _battle.Party.FirstOrDefault();
		Vitals? acting = actor is null ? null : VitalsOf(actor, open is not null);

		// Which buttons the bar offers is part of whose bar it is, so it is decided here, with
		// the face, and shown with it. Two kinds of "no": out of actions is a fact about the
		// turn and stays on screen greyed; never learned it is a fact about the character, and
		// a button nobody can ever press is clutter — a fighter has no Cast. An enemy gets no
		// buttons at all: it is their turn, not an offer.
		var fighting = _campaign.State == CampaignState.Fighting;
		var offered = new Offer(
			Hotbar: fighting && actor is not null && _battle.SideOf(actor) == Ironbound.Simulation.Side.Party,
			Cast: actor is not null && actor.Spells.Prepared.Count > 0,
			Stances: actor is null ? [] : [.. _stances.Keys.Where(actor.Stances.CanAdopt)],

			// And a third kind of "no": nothing to do it to. Standing up is for somebody on the
			// floor and first aid is for somebody bleeding out on it. As words in a row they
			// were harmless greyed out; as icons they are two permanent dead buttons.
			Stand: actor is not null && actor.IsProne,
			Help: actor is not null && _battle.Party.Any(one =>
				!ReferenceEquals(one, actor)
				&& one.HitPoints.State == HitPointState.Dying
				&& !Ironbound.Rules.Effects.Bleeding.IsStable(one)));
		(bool, bool, bool)? pips = open is null || !_battle.IsPartyTurn
			? null
			: (open.Budget.HasStandard, open.Budget.HasMove, open.Budget.HasSwift);

		// Written down now, shown when the board catches up. The rules are already at the end
		// of the enemy's turn; a roster that followed them would announce the kill, and then
		// "chapter won", while the blow that did it was still three beats away.
		void Show()
		{
			_status.Text = headline;
			_roster.Text = standing;
			ShowVitals(party, actor, acting, pips);
			ShowOffer(offered);
		}

		if (StageBusy)
		{
			Enqueue(0.0, Show);
			return;
		}

		Show();
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
