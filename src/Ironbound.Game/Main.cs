using System.Collections.Generic;
using System.Linq;
using Godot;
using Ironbound.Rules.Abilities;
using Ironbound.Rules.Combat;
using Ironbound.Rules.Conditions;
using Ironbound.Rules.Content;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Encounters;
using Ironbound.Rules.Classes;
using Ironbound.Rules.Encounters.Actions;
using Ironbound.Rules.Feats;
using Ironbound.Rules.Items;
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

		/// <summary>Shout them down: shaken, if the Intimidate check beats their nerve.</summary>
		Demoralize,

		/// <summary>Kneel beside somebody on the floor and stop the bleeding.</summary>
		Help,

		/// <summary>Something off the belt: a potion drunk or given, a flask thrown.</summary>
		Items,
	}

	private static readonly Mode[] ModeOrder =
		[Mode.Move, Mode.Attack, Mode.Full, Mode.Trip, Mode.Shove, Mode.Demoralize, Mode.Help, Mode.Cast, Mode.Items];

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
	private MeshInstance3D _reach;
	private StandardMaterial3D _turnPaint;
	private StandardMaterial3D _cursorPaint;

	private RichTextLabel _log;
	private Label _status;
	private Label _prompt;
	private OptionButton _spells;
	private CheckButton _castDefensively;
	private OptionButton _items;
	private readonly List<BeltStack> _usables = new();
	private Button _endTurn;
	private Button _load;
	private Button _stand;
	private Button _rage;
	private Button _press;
	private Button _rest;
	private Button _loot;
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

		// -- --menu load: straight to the saved games.
		if (menu >= 0 && OS.GetCmdlineUserArgs().ElementAtOrDefault(menu + 1) == "load")
		{
			ShowLoadGame();
			return;
		}

		// -- --load <file>: straight into a saved game, for looking at one somebody sent.
		var load = System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--load");
		if (load >= 0 && OS.GetCmdlineUserArgs().ElementAtOrDefault(load + 1) is { } file)
		{
			LoadSave(new SaveCard(file, SaveKind.Manual, string.Empty, string.Empty, string.Empty, 0));
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

		// -- --character: the character window open from the start, for looking at it in a
		// display nobody can click in.
		if (OS.GetCmdlineUserArgs().Contains("--character"))
		{
			_showSheet.ButtonPressed = true;
			RefreshSheet();
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
			LogText($"{line}\n");
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
				if (_campaign.Collect() is > 0 and var left)
				{
					// Through the queue like every other line, or the looting is reported in
					// round one of a fight that is still being shown.
					Append($"— the fallen have {left} thing(s) on them: click a body to search it, or press Loot —", []);
				}

				// Whatever was thrown or dropped is picked up once the fight is over; the
				// figures take it back in hand when the last blow has been shown.
				Enqueue(0.0, () =>
				{
					foreach (var member in _battle.Party)
					{
						Rearm(member);
					}
				});

				if (_campaign.IsLevel)
				{
					// After the last blow has been shown, not while it is still in the air.
					Enqueue(0.0, () => AfterLevelFight(finished));
					return;
				}

				// A chapter won and its spoils counted: a good place to come back to.
				if (_campaign.State == CampaignState.Between)
				{
					Autosave();
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
				SelectItemsFor(turn.Actor);
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

	/// <summary>
	/// The camp row, shown once nobody is swinging: whether there is anything to search, anybody
	/// ready to level, and the screens that hang off it.
	/// </summary>
	private void RefreshCamp()
	{
		var between = _campaign.State != CampaignState.Fighting;

		if (_between is not null)
		{
			_between.Visible = between;
		}

		if (_hotbar is not null && between)
		{
			_hotbar.Visible = false;
		}

		var spoils = between ? Spoils().Count : 0;
		_loot.Visible = spoils > 0;
		_loot.Text = spoils == 1 ? "Loot (1)" : $"Loot ({spoils})";
		_levelUp.Disabled = !between || !_campaign.Ready.Any();

		// Not a screen you leave open into a fight.
		if (_levelPanel is not null && !between)
		{
			_levelPanel.Visible = false;
		}

		RefreshLevelUp();
		RefreshSheet();
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

		LogText($"\n— chapter {_campaign.Chapter}: {CurrentChapterName()} —\n");
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

		LogText("— the party rests: wounds closed, spells prepared again —\n");
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
		LogText($"— {actor.Name} {now} {Stances.Name(stance)} —\n");

		RefreshControls();
		UpdateStatus();
	}

	/// <summary>Into a rage, or out of one: a free action, as often as the rounds allow.</summary>
	private void OnRage()
	{
		if (StageBusy || _battle.Encounter.Current is not { IsEnded: false } turn)
		{
			return;
		}

		var lines = _battle.Act(new RageAction());
		if (lines.Count == 0)
		{
			Refuse(turn.Actor.IsRaging
				? $"{turn.Actor.Name} cannot stop raging now."
				: $"{turn.Actor.Name} cannot rage now — no rounds left, or still worn out from the last.");
			RefreshControls();
			return;
		}

		Stage(_battle.LastResult, lines);
		if (_figures.TryGetValue(turn.Actor, out var figure))
		{
			Float(figure, turn.Actor.IsRaging ? "RAGE!" : "calms", turn.Actor.IsRaging ? CritColour : MissColour, turn.Actor.IsRaging);
		}

		RefreshFigures();
		PromptTurn();
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
		if (CameraInput(@event) || HudInput(@event) || SelectionKey(@event))
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
		// puts somebody where you pointed. The board is a camp rather than a battlefield. A click
		// on one of the party picks them instead.
		if (_campaign.State != CampaignState.Fighting)
		{
			if (SelectionClick((InputEventMouseButton)@event))
			{
				return;
			}

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
		var action = ActionFor(actor, square);

		// Too far for one move: go as far as it allows, along the path the preview drew.
		if (_mode == Mode.Move && (action is null || !_battle.CanAct(action))
			&& MovePlan(actor, square, out _, out _) is { } shorter)
		{
			action = shorter;
		}

		if (action is null)
		{
			Refuse(WhyNot(actor, square));
			return;
		}

		HidePath();

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
		LogText($"— {why} —\n");
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

		// A bow before a thrown dagger: the dagger, once thrown, lies where it fell until the
		// fight is over, and the bow does not.
		return actor.Attacks
			.Where(weapon => weapon.IsRanged && weapon.IsWithinRange(feet) && !IsOutOfHand(actor, weapon))
			.OrderBy(weapon => weapon.IsThrownUse)
			.FirstOrDefault();
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

			case Mode.Demoralize:
				if (occupant is null || !actor.IsEnemyOf(occupant))
				{
					return "There is nobody there to frighten.";
				}

				if (!field.HasLineOfSight(actor, occupant))
				{
					return $"{actor.Name} cannot see {occupant.Name}.";
				}

				return field.DistanceInFeet(actor, occupant) is { } shout and > DemoralizeAction.RangeFeet
					? $"{occupant.Name} is {shout} ft away; a threat carries {DemoralizeAction.RangeFeet} ft."
					: $"{actor.Name} cannot demoralize {occupant.Name} now.";

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

			case Mode.Items:
				return ItemWhyNot(actor, square, occupant);

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

			case Mode.Demoralize:
				return occupant is not null && actor.IsEnemyOf(occupant)
					? new DemoralizeAction(occupant)
					: null;

			case Mode.Items:
				return ItemActionFor(actor, square, occupant);

			case Mode.Help:
				return occupant is not null && !actor.IsEnemyOf(occupant)
					? new StabiliseAction(occupant)
					: null;

			case Mode.Cast:
				if (SelectedSpell() is not { } spell)
				{
					return null;
				}

				// A power is aimed as its effect says: at a point, at somebody, or — channel
				// energy — at the user, by clicking them.
				// Defensively when the player has said so: no swing drawn, a concentration check
				// instead, and the spell lost if it fails.
				var guarded = _castDefensively.Visible && _castDefensively.ButtonPressed;

				if (SelectedPower() is { } power)
				{
					var use = spell.Target is SelfTarget
						? ReferenceEquals(occupant, actor) ? UsePowerAction.Self(power) : null
						: spell.NeedsAPoint
							? UsePowerAction.At(power, square)
							: occupant is not null ? UsePowerAction.At(power, occupant) : null;

					return guarded && power.Provokes ? use?.AsDefensive() : use;
				}

				var cast = spell.NeedsAPoint
					? CastSpellAction.At(spell, square)
					: occupant is not null ? CastSpellAction.At(spell, occupant) : null;

				return guarded ? cast?.AsDefensive() : cast;

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

	private static GridSquare? _hoverArg;
	private static bool _hoverRead;

	private static GridSquare? Hovering()
	{
		if (!_hoverRead)
		{
			_hoverRead = true;
			var args = OS.GetCmdlineUserArgs();
			var at = System.Array.IndexOf(args, "--hover");
			if (at >= 0 && at + 1 < args.Length && args[at + 1].Split(',') is [var x, var y]
				&& int.TryParse(x, out var hx) && int.TryParse(y, out var hy))
			{
				_hoverArg = new GridSquare(hx, hy);
			}
		}

		return _hoverArg;
	}

	private GridSquare? SquareUnderCursor()
	{
		if (_camera is null || _battle.Battlefield is not { } field)
		{
			return null;
		}

		// -- --hover x,y: the pointer is held over that square, for looking at what hovering
		// draws in a display nobody can move a mouse in.
		if (Hovering() is { } held)
		{
			return held;
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
		TendSelection();
		AnnounceExperience();
		NoticeThings();

		if (_cursor is null)
		{
			return;
		}

		var fighting = _campaign.State == CampaignState.Fighting;

		// The reach belongs to the player's turn, and to the moment it is being decided: not to
		// the enemy's, nor to the beats of a move already made.
		if (_reach is not null)
		{
			_reach.Visible = fighting && _battle.IsPartyTurn && !StageBusy;
		}

		if (StageBusy || (fighting && !_battle.IsPartyTurn) || SquareUnderCursor() is not { } square)
		{
			_cursor.Visible = false;
			HidePath();
			HighlightAt(null);
			return;
		}

		// What the pointer is over glows if a click would use it: between fights only.
		HighlightAt(fighting ? null : square);

		_cursor.Visible = true;
		_cursor.Position = new Vector3(square.X, 0.004f, square.Y);

		// Out of a fight the only thing that can refuse you is somebody standing there — or a
		// door, a crossing or a chest, which a click goes and uses.
		if (!fighting)
		{
			var usable = (_campaign.IsLevel && _campaign.FeatureAt(square) is { } feature && !_campaign.IsUsed(feature.Id))
				|| _campaign.ContainerAt(square) is { IsEmpty: false };
			_cursorPaint.AlbedoColor =
				usable || _battle.Battlefield?.IsFree(square) == true ? LegalColour : IllegalColour;
			// No path between fights: walking about is not a move anybody has to plan.
			HidePath();
			return;
		}

		var actor = _battle.Encounter.Current!.Actor;

		// Has its own memory of what it last drew, keyed by the mode too, so a change of mode
		// under a still pointer is noticed.
		PreviewMove(actor, square);

		// Deciding what a click would mean runs a path search, so only do it when the cursor
		// actually moves to a different square rather than once a frame.
		if (_hovered == square)
		{
			return;
		}

		_hovered = square;
		_cursorPaint.AlbedoColor = CanAct(actor, square) || (_mode == Mode.Move && MovePlan(actor, square, out _, out _) is not null)
			? LegalColour
			: IllegalColour;
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
		_containerNodes.Clear();

		// Whatever was lit was on the board being thrown away.
		_lit = null;
		_litMeshes.Clear();
		_litHidden.Clear();
		_litName = null;
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
		// Where the current actor could get to: a faint wash with a line round its edge.
		_reach = new MeshInstance3D { MaterialOverride = OutlinePaint(), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
		_world.AddChild(_reach);

		// The square under the pointer: an outline, so it frames what is on the square rather
		// than painting over it.
		_cursorPaint = OutlinePaint();
		_cursorPaint.AlbedoColor = LegalColour;
		_cursor = new MeshInstance3D
		{
			Mesh = Outline([new GridSquare(0, 0)], Colors.White, 0.18f, 0.06f),
			MaterialOverride = _cursorPaint,
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			Visible = false,
		};

		_world.AddChild(_cursor);
		BuildPathPreview();

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
		Check(nameof(_items), _items);
		Check(nameof(_stand), _stand);
		Check(nameof(_endTurn), _endTurn);
		Check(nameof(_load), _load);
		Check(nameof(_between), _between);
		Check(nameof(_loot), _loot);
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
		Check(nameof(_levelFeatFor), _levelFeatFor);
		Check(nameof(_levelAbility), _levelAbility);
		Check(nameof(_levelFavoured), _levelFavoured);
		Check(nameof(_castDefensively), _castDefensively);

		foreach (var stance in new[] { Stance.PowerAttack, Stance.CombatExpertise, Stance.FightingDefensively, Stance.DeadlyAim })
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
	/// Whose sheet to show: whoever the party picker names, else whoever is acting.
	/// </summary>
	private Creature Subject()
	{
		// Between fights it is whoever the player has picked, the first of them if several.
		if (Choosing && Selected().FirstOrDefault() is { } picked)
		{
			return picked;
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

		// Weapon Focus in what, Skill Focus in what: shown when the feat picked asks.
		_levelFeatFor = new OptionButton { CustomMinimumSize = new Vector2(160, 0), TooltipText = "What the feat is taken for" };
		picks.AddChild(_levelFeatFor);

		// What the class itself asks for at this level: a fighter's bonus feat, a weapon group, a
		// rogue talent or a rage power. Each picker shows only when the level brings it.
		_levelBonusLabel = new Label { Text = "Bonus feat" };
		picks.AddChild(_levelBonusLabel);
		_levelBonus = new OptionButton { CustomMinimumSize = new Vector2(240, 0) };
		_levelBonus.ItemSelected += _ => RefreshLevelUp();
		picks.AddChild(_levelBonus);
		_levelBonusFor = new OptionButton { CustomMinimumSize = new Vector2(160, 0), TooltipText = "What the bonus feat is taken for" };
		picks.AddChild(_levelBonusFor);

		// Every fourth level, one ability up by one; and a level in the favoured class is worth
		// a hit point or a skill rank on top.
		_levelAbilityLabel = new Label { Text = "Raise" };
		picks.AddChild(_levelAbilityLabel);
		_levelAbility = new OptionButton { CustomMinimumSize = new Vector2(140, 0) };
		picks.AddChild(_levelAbility);

		_levelFavouredLabel = new Label { Text = "Favoured class" };
		picks.AddChild(_levelFavouredLabel);
		_levelFavoured = new OptionButton { CustomMinimumSize = new Vector2(200, 0) };
		picks.AddChild(_levelFavoured);

		_levelGroupLabel = new Label { Text = "Weapon group" };
		picks.AddChild(_levelGroupLabel);
		_levelGroup = new OptionButton { CustomMinimumSize = new Vector2(180, 0) };
		picks.AddChild(_levelGroup);

		_levelTalentLabel = new Label { Text = "Talent" };
		picks.AddChild(_levelTalentLabel);
		_levelTalent = new OptionButton { CustomMinimumSize = new Vector2(240, 0) };
		picks.AddChild(_levelTalent);

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

		var needs = LevelNeedsFor(creature, classes);
		ShowPicker(_levelBonusLabel, _levelBonus, needs?.BonusFeats.Select(feat => feat.Name));
		ShowPicker(_levelGroupLabel, _levelGroup, needs?.WeaponGroups.Select(Capitalised));
		ShowPicker(_levelTalentLabel, _levelTalent, needs?.Talents.Select(talent => talent.Name));

		ShowPicker(null, _levelFeatFor, earnsFeat && _levelFeat.Selected >= 0 && _levelFeat.Selected < feats.Count
			? FeatChoicesFor(creature, feats[_levelFeat.Selected]).Select(Spaced)
			: null);
		ShowPicker(null, _levelBonusFor, needs is not null && Pick(needs.BonusFeats, _levelBonus) is { } bonusFeat
			? FeatChoicesFor(creature, bonusFeat).Select(Spaced)
			: null);

		var raising = needs is { AbilityIncrease: true };
		ShowPicker(_levelAbilityLabel, _levelAbility, raising ? Raisable.Select(ability => ability.ToString()) : null);
		if (raising && _levelAbility.Selected < 0)
		{
			_levelAbility.Selected = System.Array.IndexOf(Raisable, needs.DefaultAbility);
		}

		ShowPicker(_levelFavouredLabel, _levelFavoured, needs is { FavouredClass: true }
			? FavouredOptions(needs).Select(option => option.Words)
			: null);

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

			// What the class table hands out at the level being taken, so "is the next rogue
			// level worth it?" can be answered before the button rather than after.
			var reached = ClassFeatures.ClassLevel(creature, taken.Id) + 1;
			var gains = taken.FeaturesGainedAt(reached).Select(row => FeatureIds.Title(row.Id)).Distinct().ToList();
			if (gains.Count > 0)
			{
				text.Append($"Gains at {taken.Name.ToLowerInvariant()} {reached}: {string.Join(", ", gains)}\n");
			}
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
				foreach (var (name, why) in barred)
				{
					text.Append($"  {name} — {why}\n");
				}
			}
		}

		_levelDetail.Text = text.ToString();
	}

	/// <summary>
	/// Feats the creature cannot take, and why: what it is short of, or what the game has yet to
	/// build for the feat at all.
	/// </summary>
	private IEnumerable<(string Name, string Why)> WithheldFeats(Creature creature) =>
		_campaign.WithheldFeats(creature).Select(entry => (entry.Feat.Name, entry.Why));

	/// <summary>The six abilities, in the order a sheet lists them.</summary>
	private static readonly Ability[] Raisable =
		[Ability.Strength, Ability.Dexterity, Ability.Constitution, Ability.Intelligence, Ability.Wisdom, Ability.Charisma];

	/// <summary>
	/// What a feat that asks for something could be taken for, and that this creature could take
	/// it for: the weapons it carries for Weapon Focus, every skill for Skill Focus.
	/// </summary>
	private static List<string> FeatChoicesFor(Creature creature, FeatDefinition feat)
	{
		if (feat is null || feat.Takes == FeatChoice.None || feat.Choice is not null)
		{
			return [];
		}

		var options = feat.Takes == FeatChoice.Weapon
			? creature.Attacks.Select(weapon => weapon.Kind).OfType<string>().Distinct()
			: FeatChoices.Options(feat.Takes);

		return [.. options.Where(choice => (feat with { Choice = choice }).AvailableTo(creature))];
	}

	/// <summary>The feat as picked, taken for whatever its own picker names.</summary>
	private static FeatDefinition WithChoice(Creature creature, FeatDefinition feat, OptionButton picker)
	{
		var options = FeatChoicesFor(creature, feat);
		return picker.Visible && picker.Selected >= 0 && picker.Selected < options.Count
			? feat with { Choice = options[picker.Selected] }
			: feat;
	}

	/// <summary>A hit point, or a rank in one of the skills that still has room for it.</summary>
	private static List<(string Words, FavouredClassBonus Bonus, Ironbound.Rules.Skills.Skill? Skill)> FavouredOptions(LevelNeeds needs) =>
	[
		("+1 hit point", FavouredClassBonus.HitPoint, null),
		.. needs.FavouredSkills.Select(skill => ($"+1 rank in {Spaced(skill.ToString())}", FavouredClassBonus.SkillRank, (Ironbound.Rules.Skills.Skill?)skill)),
	];

	/// <summary>"SleightOfHand" as "Sleight of hand".</summary>
	private static string Spaced(string name) =>
		Capitalised(System.Text.RegularExpressions.Regex.Replace(name, "(?<=[a-z])([A-Z])", " $1").ToLowerInvariant());

	private OptionButton _levelFeatFor;
	private OptionButton _levelBonusFor;
	private Label _levelAbilityLabel;
	private OptionButton _levelAbility;
	private Label _levelFavouredLabel;
	private OptionButton _levelFavoured;

	private Label _levelBonusLabel;
	private OptionButton _levelBonus;
	private Label _levelGroupLabel;
	private OptionButton _levelGroup;
	private Label _levelTalentLabel;
	private OptionButton _levelTalent;

	/// <summary>What the chosen class asks for at the level being taken, or null before a class is chosen.</summary>
	private LevelNeeds LevelNeedsFor(Creature creature, IReadOnlyList<ClassDefinition> classes) =>
		_levelClass.Selected >= 0 && _levelClass.Selected < classes.Count
			? _campaign.NeedsFor(creature, classes[_levelClass.Selected])
			: null;

	/// <summary>A picker and its label, shown and filled when there is something to pick.</summary>
	private static void ShowPicker(Label label, OptionButton picker, IEnumerable<string> entries)
	{
		var list = entries?.ToList() ?? [];
		if (label is not null)
		{
			label.Visible = list.Count > 0;
		}

		picker.Visible = list.Count > 0;
		Fill(picker, list);
	}

	private static T Pick<T>(IReadOnlyList<T> offered, OptionButton picker) =>
		picker.Visible && picker.Selected >= 0 && picker.Selected < offered.Count ? offered[picker.Selected] : default;

	private static void Fill(OptionButton picker, IEnumerable<string> entries)
	{
		// Only when the list itself changed, so a pick survives a refresh; by content, not
		// count, because Weapon Focus and Skill Focus can offer lists of the same length.
		var wanted = entries.ToList();
		if (picker.ItemCount == wanted.Count
			&& Enumerable.Range(0, wanted.Count).All(i => picker.GetItemText(i) == wanted[i]))
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
				? WithChoice(creature, feats[_levelFeat.Selected], _levelFeatFor)
				: null;

		var taken = classes[_levelClass.Selected];
		var needs = _campaign.NeedsFor(creature, taken);
		var favoured = FavouredOptions(needs);
		var (_, bonusKind, bonusSkill) = needs.FavouredClass && _levelFavoured.Selected >= 0 && _levelFavoured.Selected < favoured.Count
			? favoured[_levelFavoured.Selected]
			: favoured[0];
		var choices = new LevelChoices(
			BonusFeat: Pick(needs.BonusFeats, _levelBonus) is { } bonusFeat ? WithChoice(creature, bonusFeat, _levelBonusFor) : null,
			WeaponGroup: Pick(needs.WeaponGroups, _levelGroup),
			Talent: Pick(needs.Talents, _levelTalent) is { } talent ? talent.Id : null,
			AbilityIncrease: needs.AbilityIncrease && _levelAbility.Selected >= 0 && _levelAbility.Selected < Raisable.Length
				? Raisable[_levelAbility.Selected]
				: null,
			Favoured: needs.FavouredClass ? bonusKind : null,
			FavouredSkill: needs.FavouredClass ? bonusSkill : null);

		if (!_campaign.LevelUp(creature, taken, chosen, choices))
		{
			Refuse(_campaign.LevelRefusal ?? $"{creature.Name} cannot take that level.");
			return;
		}

		var learned = new List<string>();
		if (chosen is not null)
		{
			learned.Add(chosen.Title);
		}

		if (choices.BonusFeat is { } bonus)
		{
			learned.Add(bonus.Title);
		}

		if (choices.AbilityIncrease is { } raised)
		{
			learned.Add($"+1 {raised}");
		}

		if (choices.WeaponGroup is { } group)
		{
			learned.Add($"weapon training ({group})");
		}

		if (choices.Talent is { } talentId && needs.Talents.FirstOrDefault(one => one.Id == talentId) is { Name: { } talentName })
		{
			learned.Add(talentName);
		}

		LogText($"— {creature.Name} is now {creature.Description}"
			+ (learned.Count == 0 ? string.Empty : $", and learns {string.Join(", ", learned)}") + " —\n");

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

	/// <param name="now">From the queue itself: the board has got here, so do it, busy or not.</param>
	private void SelectSpellsFor(Creature actor, bool now = false)
	{
		// Whose spells are listed is part of whose turn it is, and the board may not have got
		// there yet: refilling it at once blanked the wizard's list while the goblins were
		// still being shown taking the turns in between.
		//
		// Queued once, and run when its turn in the queue comes, not asked again then. Asking
		// again re-queued it behind whatever else was waiting, and the items list doing the
		// same made the two take turns behind each other for ever — in one frame, which is the
		// freeze the owner hit the moment a fight opened.
		if (StageBusy && !now)
		{
			Enqueue(0.0, () => SelectSpellsFor(actor, now: true));
			return;
		}

		_spells.Clear();
		_castables.Clear();
		foreach (var spell in Castable(actor))
		{
			_spells.AddItem(SpellEntry(actor, spell));
			_spells.SetItemDisabled(_spells.ItemCount - 1, !actor.Spells.CanCast(spell));
			_castables.Add((spell, null));
		}

		// Class powers — channel energy, a domain's touch, a school's missile — in the same list,
		// with their uses left for the day where a spell shows its slots.
		foreach (var power in actor.Powers)
		{
			_spells.AddItem($"{power.Name} ({actor.UsesLeft(power)}/{actor.UsesPerDay(power)})");
			_spells.SetItemDisabled(_spells.ItemCount - 1, actor.UsesLeft(power) <= 0);
			_castables.Add((power.Effect, power));
		}

		_spells.Disabled = _spells.ItemCount == 0;
		if (_spells.ItemCount > 0)
		{
			_spells.Selected = 0;
		}
	}

	/// <summary>
	/// Every spell the caster could put in the Cast list: what she prepared, the same again
	/// empowered if she has the feat, and the cures a cleric can trade a slot for.
	/// </summary>
	private static IEnumerable<Spell> Castable(Creature actor)
	{
		var book = actor.Spells;
		foreach (var spell in book.Prepared)
		{
			yield return spell;
		}

		// Only where there is a slot two levels up to put it in at all, so a 1st-level wizard
		// with the feat is not shown a list of things she can never cast.
		if (actor.HasFeat(FeatEffect.EmpowerSpell))
		{
			foreach (var spell in book.Prepared.Where(spell => spell.Level > 0 && book.SlotsMaximum(spell.Level + 2) > 0))
			{
				yield return spell.Empower();
			}
		}

		// Only the cures she has a slot of their level or higher to trade for: a 1st-level cleric
		// is not offered cure critical wounds.
		foreach (var spell in book.Spontaneous.Where(spell => !book.Knows(spell) && book.SlotLevels.Any(slot => slot >= spell.Level && book.SlotsMaximum(slot) > 0)))
		{
			yield return spell;
		}
	}

	/// <summary>
	/// A spell's line in the Cast list, with what it would cost: the general slots left at its
	/// level, plus a domain or school slot it may take instead.
	/// </summary>
	private static string SpellEntry(Creature actor, Spell spell)
	{
		var book = actor.Spells;
		var level = spell.SlotLevel;

		if (!book.Knows(spell) && book.KnowsSpontaneously(spell))
		{
			return $"{spell.Name} (in place of a level {level}+ spell)";
		}

		var name = spell.Empowered ? $"{spell.Name}, empowered" : spell.Name;
		var slots = $"{book.SlotsRemaining(level)}";
		if (book.IsSpecialty(spell) && book.SpecialtyMaximum(level) > 0)
		{
			slots += $" + {book.SpecialtyRemaining(level)} {(actor.Choices.School is null ? "domain" : "school")}";
		}

		if (book.Cost(spell) > 1)
		{
			slots += ", costs 2";
		}

		return spell.Empowered ? $"{name} (level {level}: {slots})" : $"{name} ({slots})";
	}

	/// <summary>What the Cast list holds, in its order: a spell, or a power and the spell-shaped
	/// effect that says how it is aimed.</summary>
	private readonly List<(Spell Spell, Power Power)> _castables = [];

	private Spell SelectedSpell() =>
		_battle.Encounter.Current is not null && _spells.Selected >= 0 && _spells.Selected < _castables.Count
			? _castables[_spells.Selected].Spell
			: null;

	/// <summary>
	/// Offers casting defensively only when it would make a difference: a spell that draws
	/// swings, chosen by somebody a foe is standing over. The chance is on the button, because
	/// whether to take the risk is the whole decision.
	/// </summary>
	private void RefreshDefensive()
	{
		if (_castDefensively is null)
		{
			return;
		}

		var turn = _battle.IsPartyTurn && _battle.Encounter.Current is { IsEnded: false } current ? current : null;
		var spell = SelectedSpell();
		var power = SelectedPower();
		var provokes = power is not null ? power.Provokes : spell is not null && !spell.Metamagic.HasFlag(Metamagic.Quicken);

		if (_mode != Mode.Cast || turn is null || spell is null || !provokes
			|| _battle.Battlefield is not { } field || !Threatened(turn.Actor, field))
		{
			_castDefensively.Visible = false;
			return;
		}

		var actor = turn.Actor;
		var casterLevel = power is not null && power.Use != PowerUse.Spell ? power.CasterLevel : actor.Spells.CasterLevel;
		var chance = Concentration.DefensiveChance(actor, casterLevel, spell.EffectiveLevel);

		_castDefensively.Visible = true;
		_castDefensively.Text = $"Defensively {chance}%";
		_castDefensively.TooltipText =
			$"Cast without drawing attacks of opportunity. A concentration check against DC {15 + (2 * spell.EffectiveLevel)} "
			+ $"holds {chance}% of the time; fail it and the spell is lost.";
	}

	/// <summary>Whether a foe who could still swing stands over somebody's square.</summary>
	private bool Threatened(Creature actor, Battlefield field) =>
		field.SquareOf(actor) is { } square
		&& _battle.Encounter.Order.Any(one => one.Creature.IsConscious
			&& one.Creature.IsEnemyOf(actor)
			&& field.Threatens(one.Creature, square));

	/// <summary>
	/// The Items list: whatever is on the actor's belt that can be used in a fight, one line a
	/// stack, "alchemist's fire ×3". Weapons on the belt are drawn by attacking, not from here.
	/// </summary>
	private void SelectItemsFor(Creature actor, bool now = false)
	{
		// As the spell list: queued once, done when the queue gets there.
		if (StageBusy && !now)
		{
			Enqueue(0.0, () => SelectItemsFor(actor, now: true));
			return;
		}

		var keep = SelectedUsable()?.Id;
		_items.Clear();
		_usables.Clear();
		foreach (var stack in Consumables.OnBelt(actor))
		{
			_items.AddItem(stack.Count > 1 ? $"{stack.Item.Name} ×{stack.Count}" : stack.Item.Name);
			_usables.Add(stack);
		}

		_items.Disabled = _items.ItemCount == 0;
		if (_items.ItemCount > 0)
		{
			var again = _usables.FindIndex(stack => stack.Item.Id == keep);
			_items.Selected = again >= 0 ? again : 0;
		}
	}

	private ItemDefinition SelectedUsable() =>
		_battle.Encounter.Current is not null && _items is not null && _items.Selected >= 0 && _items.Selected < _usables.Count
			? _usables[_items.Selected].Item
			: null;

	private Power SelectedPower() =>
		_battle.Encounter.Current is not null && _spells.Selected >= 0 && _spells.Selected < _castables.Count
			? _castables[_spells.Selected].Power
			: null;

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
			LogText($"{header}\n");
		}

		foreach (var line in lines)
		{
			LogText($"      {line}\n");
		}
	}

	/// <summary>Adds to the log panel.</summary>
	private void LogText(string text)
	{
		_log.AddText(text);

		// An autoplayed run is usually being recorded to check something, and a frame every few
		// seconds shows the board but not the dice; the console gets the whole log.
		if (_autoplay)
		{
			GD.PrintRaw(text);
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
		LogText("Initiative\n");
		foreach (var combatant in _battle.Encounter.Order)
		{
			LogText($"      {combatant.Initiative,3}  {combatant.Creature.Name}\n");
		}

		LogText("\n");
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
		_modes[Mode.Demoralize].Disabled = turn is null || !turn.Budget.HasStandard;
		_modes[Mode.Help].Disabled = turn is null || !turn.Budget.HasStandard;
		_modes[Mode.Cast].Disabled = turn is null
			|| !((turn.Budget.HasStandard && Castable(turn.Actor).Any(turn.Actor.Spells.CanCast))
				|| turn.Actor.Powers.Any(power => turn.Actor.UsesLeft(power) > 0 && turn.Budget.CanAfford(power.Cost)));
		_modes[Mode.Move].Disabled = turn is null || !CanStillMove(turn);
		_modes[Mode.Items].Disabled = turn is null || !turn.Budget.HasStandard || Consumables.OnBelt(turn.Actor).Count == 0;
		_items.Disabled = _modes[Mode.Items].Disabled;
		RefreshItemButtons(turn);

		_spells.Disabled = turn is null || _spells.ItemCount == 0;
		_stand.Disabled = turn is null || !turn.CanTake(new StandUpAction());
		_rage.Disabled = turn is null || !turn.CanTake(new RageAction());
		_rage.SetPressedNoSignal(turn?.Actor.IsRaging == true);

		foreach (var (stance, button) in _stances)
		{
			button.Disabled = turn is null || !(turn.Actor.Stances.CanAdopt(stance) || turn.Actor.Stances.IsActive(stance));
			button.ButtonPressed = turn is not null && turn.Actor.Stances.IsActive(stance);
		}

		_press.Disabled = !_campaign.CanAdvance;
		_press.Visible = !_campaign.IsLevel;
		_rest.Disabled = !_campaign.CanRest;
		RefreshCamp();

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
			LogText("— nothing left to spend; end the turn —\n");
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
		RefreshDefensive();

		if (_reach is null)
		{
			return;
		}

		var fighting = _campaign.State == CampaignState.Fighting;
		// On a level's painted ground the grid is a guide, not the picture: half strength, so the
		// reach outline and the path read over it.
		_groundPaint?.SetShaderParameter("strength", fighting ? (_campaign.IsLevel ? 0.45f : 1.0f) : 0.0f);

		if (!fighting
			|| !_battle.NeedsPlayer
			|| _battle.Battlefield is not { } field
			|| _battle.Encounter.Current is not { IsEnded: false } turn)
		{
			_reach.Mesh = null;
			return;
		}

		var colour = _mode switch
		{
			Mode.Move => ReachColour,
			Mode.Cast => SpellColour,
			Mode.Help => HelpColour,
			Mode.Items => SelectedUsable()?.Consumable is { IsPotion: true } ? HelpColour : SpellColour,
			_ => StrikeColour,
		};

		// Only where an answer could be yes. Asking every square of a level the size of the
		// caves ran a path search for each of sixteen hundred squares on every click.
		IEnumerable<GridSquare> candidates;
		var thrown = _mode == Mode.Items && SelectedUsable()?.Consumable is { IsThrown: true, Aim: ThrowAim.Square or ThrowAim.Either };
		if ((_mode == Mode.Move || thrown || (_mode == Mode.Cast && SelectedSpell() is { NeedsAPoint: true }))
			&& field.SquareOf(turn.Actor) is { } at)
		{
			var radius = _mode == Mode.Move ? (turn.Actor.CurrentSpeed / Distance.FeetPerSquare) + 1
				: thrown ? SelectedUsable().Consumable.MaximumRange / Distance.FeetPerSquare
				: 24;
			candidates = Enumerable.Range(System.Math.Max(0, at.X - radius), System.Math.Min(field.Width, at.X + radius + 1) - System.Math.Max(0, at.X - radius))
				.SelectMany(x => Enumerable.Range(System.Math.Max(0, at.Y - radius), System.Math.Min(field.Height, at.Y + radius + 1) - System.Math.Max(0, at.Y - radius))
					.Select(y => new GridSquare(x, y)));
		}
		else
		{
			candidates = _battle.Encounter.Order
				.Select(one => field.SquareOf(one.Creature))
				.OfType<GridSquare>();
		}

		var squares = candidates.Where(square => CanAct(turn.Actor, square)).ToHashSet();
		_reach.Mesh = Outline(squares, new Color(colour, 1f), colour.A * 0.45f, 0.05f);
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
		var actor = open?.Actor ?? Subject() ?? _battle.Party.FirstOrDefault(one => one.IsConscious) ?? _battle.Party.FirstOrDefault();
		Vitals? acting = actor is null ? null : VitalsOf(actor, open is not null);

		// Which buttons the bar offers is part of whose bar it is, so it is decided here, with
		// the face, and shown with it. Two kinds of "no": out of actions is a fact about the
		// turn and stays on screen greyed; never learned it is a fact about the character, and
		// a button nobody can ever press is clutter — a fighter has no Cast. An enemy gets no
		// buttons at all: it is their turn, not an offer.
		var fighting = _campaign.State == CampaignState.Fighting;
		var offered = new Offer(
			Hotbar: fighting && actor is not null && _battle.SideOf(actor) == Ironbound.Simulation.Side.Party,
			Cast: actor is not null && (actor.Spells.Prepared.Count > 0 || actor.Spells.Spontaneous.Count > 0 || actor.Powers.Count > 0),

			// An armed one-shot (a powerful blow waiting for its hit) stays on show, lit.
			Stances: actor is null ? [] : [.. _stances.Keys.Where(stance => actor.Stances.CanAdopt(stance) || actor.Stances.IsActive(stance))],

			// And a third kind of "no": nothing to do it to. Standing up is for somebody on the
			// floor and first aid is for somebody bleeding out on it. As words in a row they
			// were harmless greyed out; as icons they are two permanent dead buttons.
			Stand: actor is not null && actor.IsProne,
			Help: actor is not null && _battle.Party.Any(one =>
				!ReferenceEquals(one, actor)
				&& one.HitPoints.State == HitPointState.Dying
				&& !Ironbound.Rules.Effects.Bleeding.IsStable(one)),

			// Only for somebody who can rage at all; greyed while worn out or out of rounds.
			Rage: actor is not null && actor.RageRoundsPerDay > 0,

			// Only for somebody with something on the belt to use.
			Items: actor is not null && Consumables.OnBelt(actor).Count > 0);
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
		WriteText(SavePath, json);

		GD.Print($"--- saved {json.Length} characters to {ProjectSettings.GlobalizePath(SavePath)} ---");

		_campaign = Campaign.FromJson(ReadText(SavePath), _content);
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
			// Everything the fallen had goes in the bag, and whatever suits somebody better is put on.
			foreach (var container in _campaign.Containers.Where(one => one.IsOpen && !one.IsEmpty).ToList())
			{
				_campaign.TakeAll(container.Id);
			}

			foreach (var member in _campaign.Party)
			{
				foreach (var line in Outfitter.EquipBest(_campaign, member))
				{
					GD.Print($"--- {line} ---");
				}
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
		_campaign.Containers.Where(one => !one.IsEmpty).ToList() is { Count: > 0 } left
			? string.Join("; ", left.Select(one => one.ToString()))
			: "nothing";

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
