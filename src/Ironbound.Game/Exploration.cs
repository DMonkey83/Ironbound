using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Ironbound.Rules.Content;
using Ironbound.Rules.Items;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Maps;
using Ironbound.Simulation;

/// <summary>
/// The part of <see cref="Main"/> that is a campaign's level: one connected place the party
/// walks through in real time, which turns into a turn-based fight only where something is
/// waiting for them.
/// </summary>
/// <remarks>
/// The project owner asked for exactly this, after Wrath of the Righteous: "no turn-based, that
/// turns on only when the fight begins". Before it, a campaign was a chain of separate boards and
/// the journey between them was a button. Now the cave mouth, the tunnel, the junction, the
/// crevice, the orcs' lair and the ogre's den are one map, and getting from one to the next is
/// walking, climbing, picking a lock.
/// <para>
/// The rules own every fact — who is where, which rooms are cleared, which doors are open — and
/// this layer only draws them and moves the figures between the squares the rules agree to.
/// Each step is taken in the rules before it is drawn, so the moment a foot lands in a guarded
/// room the rules know, and the walk stops there and the fight begins with everyone exactly
/// where they stood.
/// </para>
/// </remarks>
public partial class Main
{
	/// <summary>Seconds to cross one five-foot square: a brisk walk, not the fight's dash.</summary>
	private const float WalkSeconds = 0.22f;

	private sealed class Walker
	{
		public List<GridSquare> Path = [];
		public int Next;
		public float T;
		public Vector3 From;
		public Vector3 To;
		public bool Stepping;
		public Action Arrived;
		public GridSquare Goal;
		public float Waited;
		public int Detours;

		/// <summary>The square the figure is on, which runs ahead of the rules' while it passes a friend.</summary>
		public GridSquare At;

		/// <summary>This step goes through a friend's square: the figure moves, the rules wait.</summary>
		public bool Passing;
	}

	private readonly Dictionary<Creature, Walker> _walkers = new();
	private readonly Dictionary<string, List<Node3D>> _featureNodes = new();

	/// <summary>Set while a page is up: nobody walks while the player is reading.</summary>
	private bool _held;

	// Won is still walking: the last room's spoils, Gorrum's strongbox among them, are there to
	// be picked up after the last blow.
	private bool Exploring => _campaign is { IsLevel: true, State: CampaignState.Exploring or CampaignState.Won };

	// ---- drawing the level ----

	/// <summary>Builds everything the level is made of; RebuildWorld calls it instead of laying
	/// one encounter's tiles and pillars.</summary>
	private void BuildLevel()
	{
		var level = _campaign.Level!;
		_featureNodes.Clear();

		var grass = level.GrassTerrain is { Length: > 0 } g ? _content.GetTerrain(g) : null;
		var treeProp = Model(grass?.Blocked);

		BuildGround(level);
		LightLevel(level);

		for (var y = 0; y < level.Height; y++)
		{
			for (var x = 0; x < level.Width; x++)
			{
				var square = new GridSquare(x, y);
				switch (level.CellAt(x, y))
				{
					case LevelCell.Tree:
						SpawnPillar(x, y, treeProp);
						break;
					case LevelCell.Bed:
						Furniture(square, "bed", new Vector3(0.75f, 0.22f, 0.95f), new Color(0.32f, 0.24f, 0.18f));
						break;
					case LevelCell.Table:
						Furniture(square, "table", new Vector3(0.9f, 0.62f, 0.9f), new Color(0.36f, 0.24f, 0.14f));
						break;
					case LevelCell.Crate when _campaign.ContainerAt(square) is null && !IsMerchantSquare(square):
						Furniture(square, "crate-b", new Vector3(0.82f, 0.72f, 0.82f), new Color(0.42f, 0.30f, 0.18f));
						break;
				}
			}
		}

		foreach (var feature in level.Features)
		{
			DrawFeature(feature);
		}

		_containerNodes.Clear();
		RefreshContainers();
		DrawMerchants();

		// The rooms' occupants, waiting where the level put them. They are not on the rules'
		// board until their room is entered, so they are placed here by hand.
		var dormant = _campaign.Dormant;
		Spawn([.. dormant.Select(one => one.Foe)], FoeColour);
		foreach (var (foe, square, _) in dormant)
		{
			if (_figures.TryGetValue(foe, out var figure))
			{
				figure.Position = new Vector3(square.X + 0.5f, 0, square.Y + 0.5f);

				// Turned toward the way in, more or less: the party comes from the south.
				figure.Rotation = new Vector3(0, ((square.X * 13) + (square.Y * 7)) % 4 * 0.4f, 0);
			}
		}
	}

	private static IEnumerable<GridSquare> Neighbours(GridSquare at)
	{
		for (var dx = -1; dx <= 1; dx++)
		{
			for (var dy = -1; dy <= 1; dy++)
			{
				if (dx != 0 || dy != 0)
				{
					yield return new GridSquare(at.X + dx, at.Y + dy);
				}
			}
		}
	}

	/// <summary>
	/// A piece of furniture from art/props, turned by a repeatable quarter so a dormitory of beds
	/// is not a parade; or, without the model, a box of the right size and colour.
	/// </summary>
	private void Furniture(GridSquare at, string model, Vector3 size, Color colour)
	{
		if (PropModel(model) is { } piece)
		{
			_world.AddChild(piece);
			piece.Position = new Vector3(at.X + 0.5f, 0, at.Y + 0.5f);
			piece.RotateY(Mathf.DegToRad((((at.X * 31) + (at.Y * 17)) % 4) * 90f));
			return;
		}

		Furniture(at, size, colour);
	}

	private void Furniture(GridSquare at, Vector3 size, Color colour)
	{
		var piece = new MeshInstance3D
		{
			Mesh = new BoxMesh { Size = size },
			MaterialOverride = new StandardMaterial3D { AlbedoColor = colour, Roughness = 0.9f },
		};
		_world.AddChild(piece);
		piece.Position = new Vector3(at.X + 0.5f, size.Y / 2f, at.Y + 0.5f);
	}

	/// <summary>Doors while shut, the bridge once it is tied off; caches stay as their furniture.</summary>
	private void DrawFeature(FeatureDefinition feature)
	{
		if (_featureNodes.Remove(feature.Id, out var old))
		{
			foreach (var node in old)
			{
				node.QueueFree();
			}
		}

		var nodes = new List<Node3D>();
		var used = _campaign.IsUsed(feature.Id);

		if (feature.Kind == FeatureKind.Door && !used)
		{
			// One slab per door square, turned across whichever way the doorway runs.
			var across = feature.Squares.Count > 1 && feature.Squares[0].Y == feature.Squares[1].Y;
			foreach (var at in feature.Squares)
			{
				var door = new MeshInstance3D
				{
					Mesh = new BoxMesh { Size = across ? new Vector3(1f, 1.1f, 0.2f) : new Vector3(0.2f, 1.1f, 1f) },
					MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.30f, 0.18f, 0.10f), Roughness = 0.85f },
				};
				_world.AddChild(door);
				door.Position = new Vector3(at.X + 0.5f, 0.55f, at.Y + 0.5f);
				nodes.Add(door);
			}
		}
		else if (feature.Kind == FeatureKind.Bridge && used)
		{
			var planks = new StandardMaterial3D { AlbedoColor = new Color(0.40f, 0.27f, 0.15f), Roughness = 0.9f };
			foreach (var at in feature.Squares)
			{
				var plank = new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(1f, 0.06f, 0.8f) }, MaterialOverride = planks };
				_world.AddChild(plank);
				plank.Position = new Vector3(at.X + 0.5f, -0.02f, at.Y + 0.5f);
				nodes.Add(plank);
			}
		}

		_featureNodes[feature.Id] = nodes;
	}

	// ---- walking ----

	/// <summary>
	/// A click on the level, outside a fight: walk there, the selected member in front and the
	/// rest finding squares round them — or, if the click was on a door, a bridge or a cache,
	/// go to it and use it.
	/// </summary>
	private void Travel(GridSquare target)
	{
		if (_held || !Exploring || _battle.Battlefield is not { } field)
		{
			return;
		}

		// Whoever is picked walks; the first of them leads, and is the one who uses a feature.
		var walking = Selected().Where(one => field.SquareOf(one) is not null).ToList();
		var leader = walking.FirstOrDefault();
		if (leader is null)
		{
			return;
		}

		// Too much on somebody's back to take a step: say so, and say where to put it down.
		if (walking.FirstOrDefault(_campaign.IsOverloaded) is { } stuck)
		{
			// The party's load, when that is what it is — three down and the one left standing
			// with everybody's kit — rather than blaming whoever happened to be picked.
			var party = Encumbrance.PartyLoad(_campaign);
			Refuse(Encumbrance.Load(stuck).Category == LoadCategory.Overloaded
				? $"{stuck.Name} is carrying too much to move. Open the character window (I) and drop something."
				: $"{party.Line} Rest, or drop something from the bag (I).");
			return;
		}

		// A body or a pile on the floor: go and search it. Containers that belong to the level
		// are features, and go the way doors do.
		if (_campaign.ContainerAt(target) is { Feature: null, IsEmpty: false } loose)
		{
			GoOpen(loose, leader, field);
			return;
		}

		if (_campaign.FeatureAt(target) is { } feature && !_campaign.IsUsed(feature.Id))
		{
			GoUse(feature, leader, field);
			return;
		}

		if (!field.IsPassable(target))
		{
			Refuse("There is no way through there.");
			return;
		}

		// Formation: the leader takes the square clicked, the others the nearest free squares
		// round it, in the order of how far each already is behind — so nobody crosses the room
		// to take a place someone else is standing next to.
		var claimed = new HashSet<GridSquare> { target };
		var plan = new List<(Creature Who, GridSquare To)> { (leader, target) };
		var others = walking
			.Where(one => !ReferenceEquals(one, leader))
			.OrderBy(one => Apart(field.SquareOf(one)!.Value, target))
			.ToList();
		var around = Ring(field, target, others.Count, claimed);
		for (var i = 0; i < others.Count && i < around.Count; i++)
		{
			plan.Add((others[i], around[i]));
		}

		foreach (var (who, to) in plan)
		{
			Send(who, to, field, null);
		}
	}

	private static int Apart(GridSquare a, GridSquare b) => Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));

	/// <summary>The nearest free squares spreading out from <paramref name="centre"/>, by walking
	/// rather than by straight line, so a place behind a wall is not "near".</summary>
	private static List<GridSquare> Ring(Battlefield field, GridSquare centre, int count, HashSet<GridSquare> claimed)
	{
		var found = new List<GridSquare>();
		var seen = new HashSet<GridSquare> { centre };
		var queue = new Queue<GridSquare>();
		queue.Enqueue(centre);
		while (queue.Count > 0 && found.Count < count)
		{
			var at = queue.Dequeue();
			foreach (var next in Neighbours(at))
			{
				if (!seen.Add(next) || !field.IsPassable(next))
				{
					continue;
				}

				queue.Enqueue(next);
				if (field.IsFree(next) && claimed.Add(next))
				{
					found.Add(next);
					if (found.Count == count)
					{
						break;
					}
				}
			}
		}

		return found;
	}

	/// <summary>Starts somebody walking a path the rules found for them. Replaces any walk they
	/// were already on.</summary>
	private void Send(Creature who, GridSquare to, Battlefield field, Action arrived)
	{
		if (field.SquareOf(who) is not { } from)
		{
			return;
		}

		if (from == to)
		{
			_walkers.Remove(who);
			arrived?.Invoke();
			return;
		}

		if (Route(field, from, to) is not { Count: > 0 } path)
		{
			if (arrived is not null)
			{
				Refuse($"{who.Name} cannot find a way there.");
			}

			return;
		}

		_walkers[who] = new Walker { Path = path, Arrived = arrived, Goal = to, At = from };
		PlayClip(who, true, "run", "walk");
	}

	/// <summary>Called every frame: moves everyone on a walk a little further along it.</summary>
	private void StepWalkers(double delta)
	{
		if (_held || _walkers.Count == 0 || !Exploring || _battle.Battlefield is not { } field)
		{
			return;
		}

		foreach (var (who, walker) in _walkers.ToList())
		{
			if (!_figures.TryGetValue(who, out var figure))
			{
				_walkers.Remove(who);
				continue;
			}

			if (!walker.Stepping)
			{
				if (walker.Next >= walker.Path.Count)
				{
					Finish(who, walker, arrived: true);
					continue;
				}

				var next = walker.Path[walker.Next];
				var last = walker.Next == walker.Path.Count - 1;
				var friend = field.OccupantOf(next) is { } there && !ReferenceEquals(there, who) && _campaign.Party.Contains(there)
					? there
					: null;

				// The goal itself taken by a friend who is staying put: stop beside them instead,
				// rather than wait for somebody who is not going anywhere.
				if (last && friend is not null && !_walkers.ContainsKey(friend)
					&& Ring(field, walker.Goal, 1, [walker.Goal]) is [var beside, ..]
					&& Route(field, walker.At, beside) is { Count: > 0 } instead)
				{
					walker.Path = instead;
					walker.Goal = beside;
					walker.Next = 0;
					continue;
				}

				// A friend's square on the way is walked through, as the rules allow in a fight:
				// only the figure moves, and the rules catch up on the next free square. Nobody
				// ever ends a step sharing a square.
				walker.Passing = friend is not null && !last;

				// The step is taken in the rules first. If somebody is in the way — another of the
				// party, still walking — wait a moment for them to clear it, then go round them;
				// and if there is no way round, stop here.
				if (!walker.Passing && !_campaign.Walk(who, next))
				{
					walker.Waited += (float)delta;
					if (walker.Waited < 0.6f)
					{
						continue;
					}

					walker.Waited = 0;
					if (++walker.Detours <= 4 && Route(field, walker.At, walker.Goal) is { Count: > 0 } around)
					{
						walker.Path = around;
						walker.Next = 0;
						continue;
					}

					Finish(who, walker, arrived: false);
					continue;
				}

				walker.Waited = 0;

				walker.From = figure.Position;
				walker.To = new Vector3(next.X + 0.5f, 0, next.Y + 0.5f);
				walker.T = 0;
				walker.Stepping = true;

				var heading = walker.To - walker.From;
				if (heading.LengthSquared() > 0.0001f)
				{
					figure.Rotation = new Vector3(0, Mathf.Atan2(heading.X, heading.Z), 0);
				}
			}

			walker.T += (float)delta / WalkSeconds;
			figure.Position = walker.From.Lerp(walker.To, Mathf.Min(walker.T, 1f));

			if (walker.T < 1f)
			{
				continue;
			}

			walker.Stepping = false;
			var landed = walker.Path[walker.Next];
			walker.Next++;
			walker.At = landed;

			if (ReferenceEquals(who, Subject()) || _walkers.Count == 1)
			{
				Follow(figure);
			}

			// Passing through a friend is not arriving anywhere: the rules have not moved them.
			if (!walker.Passing && Landed(who, landed))
			{
				// Something happened — a fight, a page — and every walk stops where it is.
				return;
			}
		}
	}

	private void Finish(Creature who, Walker walker, bool arrived)
	{
		_walkers.Remove(who);
		Settle(who);
		Idle(who);
		if (arrived)
		{
			walker.Arrived?.Invoke();
		}
		else if (walker.Arrived is not null)
		{
			Refuse($"{who.Name} cannot get through.");
		}
	}

	/// <summary>
	/// The way from one square to another for somebody walking about, not fighting: the steps
	/// after <paramref name="from"/>, ending on <paramref name="to"/>, or null.
	/// </summary>
	/// <remarks>
	/// The fight's pathfinder lets a creature pass through its friends' squares, as the rules
	/// do. A walk is taken a square at a time in the rules, and a square with somebody on it
	/// cannot be stood on even for a moment, so the walk went as far as the first friend in the
	/// way and stopped there — next to a door it could not reach. This one goes round them.
	/// Diagonals do not cut a wall's corner.
	/// </remarks>
	private static List<GridSquare> Walkway(Battlefield field, GridSquare from, GridSquare to, Func<Creature, bool> through = null)
	{
		if (from == to || !field.IsPassable(to) || (through is not null && !field.IsFree(to)))
		{
			return null;
		}

		var came = new Dictionary<GridSquare, GridSquare> { [from] = from };
		var queue = new Queue<GridSquare>();
		queue.Enqueue(from);
		while (queue.Count > 0)
		{
			var at = queue.Dequeue();
			if (at == to)
			{
				var path = new List<GridSquare>();
				for (var step = to; step != from; step = came[step])
				{
					path.Add(step);
				}

				path.Reverse();
				return path;
			}

			// Straight steps first, so a walk goes along a corridor rather than zig-zagging.
			foreach (var (dx, dy) in Steps)
			{
				var next = new GridSquare(at.X + dx, at.Y + dy);
				if (came.ContainsKey(next) || !field.IsPassable(next)
					|| (!field.IsFree(next) && next != to && !(through is not null && field.OccupantOf(next) is { } held && through(held))))
				{
					continue;
				}

				if (dx != 0 && dy != 0
					&& (!field.IsPassable(new GridSquare(at.X + dx, at.Y)) || !field.IsPassable(new GridSquare(at.X, at.Y + dy))))
				{
					continue;
				}

				came[next] = at;
				queue.Enqueue(next);
			}
		}

		return null;
	}

	private static readonly (int, int)[] Steps = [(1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (1, -1), (-1, 1), (-1, -1)];

	/// <summary>
	/// The way somewhere, going round friends where it can and through them where it cannot —
	/// one standing on the rope bridge used to cut the level in two. The last square is always
	/// one nobody is standing on.
	/// </summary>
	private List<GridSquare> Route(Battlefield field, GridSquare from, GridSquare to) =>
		Walkway(field, from, to) ?? Walkway(field, from, to, one => _campaign.Party.Contains(one));

	/// <summary>Puts a figure back on the square the rules have it on, wherever its walk left it.</summary>
	private void Settle(Creature who)
	{
		if (_figures.TryGetValue(who, out var figure) && _battle.Battlefield?.SquareOf(who) is { } square)
		{
			figure.Position = new Vector3(square.X + 0.5f, 0, square.Y + 0.5f);
		}
	}

	/// <summary>Everyone stops on the square the rules have them on.</summary>
	private void StopWalkers()
	{
		foreach (var who in _walkers.Keys.ToList())
		{
			// Mid-step the rules are already on the next square; mid-pass they are still a
			// square or two back. Either way the figure goes where the rules have it.
			Settle(who);
			Idle(who);
		}

		_walkers.Clear();
	}

	/// <summary>
	/// A foot has come down on <paramref name="square"/>. Reads the room it is in: a first visit
	/// to a quiet room opens its page; a room with something alive in it starts the fight.
	/// Returns whether the walking has to stop.
	/// </summary>
	private bool Landed(Creature who, GridSquare square)
	{
		if (_campaign.Alarm() is { } guarded)
		{
			StopWalkers();
			_campaign.Visit(guarded.Id);
			Pause();
			ShowPage("Combat", guarded.Name, guarded.Intro, "Fight", () =>
			{
				Resume();
				Engage(guarded);
			});
			return true;
		}

		if (_campaign.AreaAt(square) is { } room && room.Foes.Count == 0 && _campaign.Visit(room.Id) && room.Intro.Length > 0)
		{
			StopWalkers();
			Pause();
			ShowPage(string.Empty, room.Name, room.Intro, "Go on", Resume);
			return true;
		}

		return false;
	}

	private void Pause() => _held = !Unattended();

	private void Resume() => _held = false;

	// ---- the fight, and after it ----

	private void Engage(AreaDefinition area)
	{
		if (!_campaign.Engage(area.Id))
		{
			return;
		}

		Begin(_campaign.Battle);
		LogText($"\n— {area.Name} —\n");
		ReportOpening();
		RebuildFrames();
		RefreshFigures();
		RefreshControls();
		ReportInitiative();

		// The fight as it opens, initiative rolled and nobody moved: what a lost fight goes back to.
		Autosave();
		StartNextTurn();
	}

	/// <summary>
	/// The rules have closed a room's fight: back to walking with the same people where they stand,
	/// the bodies left where they fell, and the room's last word if it has one.
	/// </summary>
	private void AfterLevelFight(AreaDefinition finished)
	{
		Begin(_campaign.Battle);

		// Only after a fight that was fought: walking into a level runs this once with none,
		// and saved a second copy of the start beside the new adventure's own.
		if (finished is not null)
		{
			Autosave();
		}
		RebuildFrames();
		RefreshContainers();
		Prompt(Spoils().Count > 0 ? "The fallen can be searched: click a body, or press Loot." : Verdict());

		// Nobody is there to click the bodies on an autoplay run, so the spoils are taken for them,
		// and somebody left bleeding is a rest, as a player would take one.
		if (Unattended() && Spoils() is { Count: > 0 } spoils)
		{
			ShowLoot(spoils, "Spoils");
		}

		if (_autoplay && _campaign.CanRest && _campaign.Party.Any(member => !member.IsConscious))
		{
			_campaign.Rest();
			LogText("— the party rests —\n");
			RefreshFigures();
		}

		// With the rest spent, the wounded drink what they found instead, as a player would.
		if (_autoplay)
		{
			foreach (var line in Outfitter.PatchUp(_campaign))
			{
				LogText($"— {line} —\n");
			}
		}
		RefreshFigures();
		RefreshControls();

		if (_campaign.State == CampaignState.Won)
		{
			ShowPage("Victory", finished?.Name ?? _campaign.Definition.Name,
				finished?.Outro is { Length: > 0 } last ? last : $"{_campaign.Definition.Name} is done.", "Close", () => { });
			return;
		}

		if (finished?.Outro is { Length: > 0 } outro)
		{
			Pause();
			ShowPage(string.Empty, finished.Name, outro, "Go on", Resume);
		}
	}

	// ---- doors, the bridge, caches ----

	/// <summary>The leader walks to the nearest square beside the feature and uses it on arrival.</summary>
	private void GoUse(FeatureDefinition feature, Creature who, Battlefield field)
	{
		if (_campaign.CanUse(feature.Id, who))
		{
			UseFeature(feature, who);
			return;
		}

		var from = field.SquareOf(who);
		var beside = feature.Squares
			.SelectMany(Neighbours)
			.Where(field.IsFree)
			.Distinct()
			.Select(square => (square, path: from is { } f ? Route(field, f, square) : null))
			.Where(one => one.path is { Count: > 0 })
			.OrderBy(one => one.path!.Count)
			.Select(one => (GridSquare?)one.square)
			.FirstOrDefault();

		if (beside is not { } stand)
		{
			Refuse($"{who.Name} cannot get to {feature.Name}.");
			return;
		}

		Send(who, stand, field, () =>
		{
			if (_campaign.CanUse(feature.Id, who))
			{
				UseFeature(feature, who);
			}
			else
			{
				// Say why rather than stand there: the rules' refusal is the reason.
				Refuse(_campaign.Use(feature.Id, who).Lines[0]);
			}
		});
	}

	private void UseFeature(FeatureDefinition feature, Creature who)
	{
		var result = _campaign.Use(feature.Id, who);
		foreach (var line in result.Lines)
		{
			LogText($"{line}\n");
		}

		// A merchant's greeting is a speech, said in the trade window; it does not fit the prompt.
		if (result.Lines.Count > 0 && !(result.Success && feature.Kind == FeatureKind.Merchant))
		{
			Prompt(result.Lines[0]);
		}

		if (result.MovedTo is { } landed && _figures.TryGetValue(who, out var figure))
		{
			// Over the chasm in an arc, rather than a slide across thin air.
			var to = new Vector3(landed.X + 0.5f, 0, landed.Y + 0.5f);
			var from = figure.Position;
			var leap = figure.CreateTween();
			leap.TweenMethod(Callable.From<float>(t =>
			{
				figure.Position = from.Lerp(to, t) + new Vector3(0, Mathf.Sin(t * Mathf.Pi) * 0.9f, 0);
			}), 0f, 1f, 0.55f);
		}

		DrawFeature(feature);
		RefreshContainers();
		RebuildFrames();
		RefreshFigures();
		RefreshControls();

		// A merchant greeted: the trade window, the greeting already in the log.
		if (result.Success && feature.Kind == FeatureKind.Merchant && _campaign.GetMerchant(feature.Id) is { } merchant)
		{
			ShowTrade(merchant);
			return;
		}

		// A container opened: its page the first time, if it has one, and then what is in it.
		if (result.Success && feature.Kind == FeatureKind.Container && _campaign.GetContainer(feature.Id) is { } box)
		{
			var heading = Capitalised(feature.Name);
			if (feature.Text.Length > 0 && result.Lines.Contains(feature.Text))
			{
				Pause();
				ShowPage(string.Empty, heading, feature.Text, "Go on", () =>
				{
					Resume();
					ShowLoot([box], heading);
				});
			}
			else
			{
				ShowLoot([box], heading);
			}

			return;
		}

		if (result.Success && feature.Text.Length > 0 && feature.Kind != FeatureKind.Door)
		{
			Pause();
			ShowPage(string.Empty, Capitalised(feature.Name), feature.Text, "Go on", Resume);
		}
	}

	private static string Capitalised(string text) =>
		text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];
}
