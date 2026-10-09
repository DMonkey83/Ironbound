using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Ironbound.Rules.Combat;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Encounters;
using Ironbound.Rules.Encounters.Actions;
using Ironbound.Rules.Magic;
using Ironbound.Rules.Maps;

/// <summary>
/// The part of <see cref="Main"/> that shows what happened instead of just arriving at it.
/// </summary>
/// <remarks>
/// The rules resolve an action the instant it is taken, and that stays true: nothing here slows
/// them down or asks them to wait. What changes is the screen. Each result is turned into a
/// queue of <em>beats</em> — walk this path, swing at that goblin, the number floats up, the body
/// falls — which play one after another while input waits.
/// <para>
/// A beat snapshots everything it needs at the moment its action resolves, because by the time
/// it plays the rules may be three actions further on. That is what lets a goblin killed by the
/// second blow of a full attack fall on the second blow: the first beat was told he was still
/// standing, because when it was written down, he was.
/// </para>
/// </remarks>
public partial class Main
{
	/// <summary>Seconds a figure takes to cross one five-foot square.</summary>
	private const float StepSeconds = 0.13f;

	private static readonly Color HurtColour = new(1.00f, 0.36f, 0.30f);
	private static readonly Color CritColour = new(1.00f, 0.80f, 0.25f);
	private static readonly Color MissColour = new(0.78f, 0.80f, 0.84f);
	private static readonly Color HealColour = new(0.45f, 0.95f, 0.55f);
	private static readonly Color SpellGlow = new(0.80f, 0.50f, 1.00f);
	private static readonly Color FireGlow = new(1.00f, 0.55f, 0.15f);

	private sealed record Beat(Action Play, double Seconds);

	private readonly Queue<Beat> _stage = new();
	private readonly HashSet<Creature> _fallen = new();
	private double _beatLeft;
	private bool _settleWhenDone;

	/// <summary>True in a headless run, where there is nobody to show anything to.</summary>
	private bool _instant;

	private bool StageBusy => _stage.Count > 0 || _beatLeft > 0;

	private void Enqueue(double seconds, Action play) => _stage.Enqueue(new Beat(play, seconds));

	/// <summary>Runs the queue. Called once a frame.</summary>
	private void PlayStage(double delta)
	{
		if (_beatLeft > 0)
		{
			_beatLeft -= delta;
			if (_beatLeft > 0)
			{
				return;
			}
		}

		// Zero-length beats — log lines, the turn ring — run straight through, so text never
		// costs a frame and always lands between the right two pieces of action.
		while (_stage.Count > 0 && _beatLeft <= 0)
		{
			var beat = _stage.Dequeue();
			beat.Play();
			_beatLeft = beat.Seconds;
		}

		if (_stage.Count == 0 && _beatLeft <= 0 && _settleWhenDone)
		{
			_settleWhenDone = false;
			SnapFigures();
			RefreshControls();
		}
	}

	private void ClearStage()
	{
		_stage.Clear();
		_fallen.Clear();
		_beatLeft = 0;
		_settleWhenDone = false;
	}

	// ---- turning a result into beats ----

	/// <summary>
	/// Shows an action, and says it, in step.
	/// </summary>
	/// <remarks>
	/// The log lines used to be written first and the beats queued after them, so "hit; 13
	/// slashing; Sergeant Grask 8/52" was on screen before the sword had moved. Each beat now
	/// writes the text of the thing it shows at the moment it shows it: a strike's line on the
	/// impact, a spell's results at the burst. The text is the same <c>ToString()</c> the log
	/// was already built from, so nothing here depends on the order <c>Battle</c> lists things in.
	/// </remarks>
	private void Stage(ActionResult result, IReadOnlyList<string> lines)
	{
		if (_instant || result is null)
		{
			Append(null, lines);
			return;
		}

		switch (result)
		{
			case MoveActionResult move:
				Say(move.Description);
				StageStrikes(move.Opportunities, indent: true);
				StageWalk(move.Actor, move.Travelled);
				break;

			case AttackActionResult attack:
				StageStrikes(attack.Opportunities, indent: true);
				if (attack.Strike is { } strike)
				{
					// For a single attack the description *is* the strike, so it waits for it.
					StageStrike(strike, PostureOf(strike.Target), attack.Description, indent: false);
				}
				else
				{
					Say(attack.Description);
				}

				break;

			case FullAttackResult full:
				Say(full.Description);
				StageStrikes(full.Opportunities, indent: true);
				StageStrikes(full.Strikes, indent: true);
				break;

			case CastSpellResult cast:
				Say(cast.Description);
				StageStrikes(cast.Opportunities, indent: true);
				if (cast.Cast is { } spell)
				{
					StageSpell(spell);
				}

				break;

			case ManeuverActionResult { Action: ManeuverAction { Target: var victim }, Check: { } check } maneuver:
				StageStrikes(maneuver.Opportunities, indent: true);
				if (maneuver.Action is BullRushAction)
				{
					StageShove(maneuver, victim, check);
				}
				else
				{
					StageTrip(maneuver, victim, check);
				}

				break;

			default:
				Append(null, lines);
				break;
		}

		StageReconcile();

		// The roster as it stands now that this action is over, queued behind the beats that
		// show it. This is what makes hit points drop on the blow rather than before it.
		UpdateStatus();
	}

	/// <summary>A line of the log, in its place in the queue.</summary>
	private void Say(string line, bool indent = false) =>
		Enqueue(0.0, () => WriteLine(line, indent));

	/// <summary>A line of the log, now — for the middle of a beat, where the queue cannot reach.</summary>
	private void WriteLine(string line, bool indent) =>
		LogText(indent ? $"        {line}\n" : $"      {line}\n");

	/// <summary>
	/// Puts everybody where the rules now say they are, and in the posture they are now in.
	/// </summary>
	/// <remarks>
	/// The catch-all after every action. Most of the time it finds nothing to do; it is what
	/// slides a shoved goblin back five feet, lays a tripped one down and stands a risen one up
	/// without each of those needing a beat of its own.
	/// </remarks>
	private void StageReconcile()
	{
		var field = _battle.Battlefield;
		var snapshot = _figures.Keys
			.Select(creature => (creature, square: field?.SquareOf(creature), posture: PostureOf(creature)))
			.ToList();

		Enqueue(0.0, () =>
		{
			foreach (var (creature, square, posture) in snapshot)
			{
				if (!_figures.TryGetValue(creature, out var figure) || square is not { } at)
				{
					continue;
				}

				var home = new Vector3(at.X + 0.5f, figure.Position.Y, at.Y + 0.5f);
				if (figure.Position.DistanceTo(home) > 0.05f)
				{
					figure.CreateTween().TweenProperty(figure, "position", home, 0.18f)
						.SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
				}

				Assume(creature, figure, posture, instant: false);
			}
		});
	}

	private void StageWalk(Creature walker, IReadOnlyList<GridSquare> path)
	{
		if (path.Count < 2)
		{
			return;
		}

		// Still on the floor after moving means it crawled: slowly, and without the run clip,
		// which played lying down looks like somebody being dragged off by the ankles.
		var crawling = PostureOf(walker) == Posture.Prone;
		var pace = crawling ? StepSeconds * 4f : StepSeconds;

		var seconds = 0.0;
		for (var i = 1; i < path.Count; i++)
		{
			seconds += pace * StepLength(path[i - 1], path[i]);
		}

		Enqueue(seconds, () =>
		{
			if (!_figures.TryGetValue(walker, out var figure))
			{
				return;
			}

			Follow(figure);
			if (!crawling)
			{
				PlayClip(walker, loop: true, "run", "walk");
			}

			var tween = figure.CreateTween();
			for (var i = 1; i < path.Count; i++)
			{
				var from = path[i - 1];
				var to = path[i];

				tween.TweenCallback(Callable.From(() => Turn(figure, to.X - from.X, to.Y - from.Y)));
				tween.TweenProperty(
					figure, "position", new Vector3(to.X + 0.5f, figure.Position.Y, to.Y + 0.5f),
					pace * StepLength(from, to));
			}

			tween.TweenCallback(Callable.From(() => Idle(walker)));
		});
	}

	private static float StepLength(GridSquare from, GridSquare to) =>
		new Vector2(to.X - from.X, to.Y - from.Y).Length();

	/// <summary>Moves the turn ring when the playback reaches that turn, not when the rules do.</summary>
	private void StageTurn(Creature actor, bool party)
	{
		if (_instant)
		{
			return;
		}

		Enqueue(StageBusy ? 0.15 : 0.0, () =>
		{
			if (_turnMarker is null || !_figures.TryGetValue(actor, out var figure))
			{
				return;
			}

			var colour = party ? ActivePartyColour : ActiveFoeColour;
			_turnPaint.AlbedoColor = colour;
			_turnPaint.Emission = colour;
			_turnMarker.Position = new Vector3(figure.Position.X, 0.04f, figure.Position.Z);
			_turnMarker.Visible = true;
		});
	}

	private void StageStrikes(IReadOnlyList<StrikeResult> strikes, bool indent)
	{
		for (var i = 0; i < strikes.Count; i++)
		{
			var strike = strikes[i];

			// Whether this is the blow that puts them down. Only the last one in the list that
			// lands on a given target can be, and only if they are down now that it is over.
			var last = !strikes.Skip(i + 1).Any(later => ReferenceEquals(later.Target, strike.Target));
			var posture = last ? PostureOf(strike.Target) : Posture.Standing;

			StageStrike(strike, posture, strike.ToString(), indent);
		}
	}

	private void StageStrike(StrikeResult strike, Posture after, string line, bool indent)
	{
		var attacker = strike.Attacker;
		var target = strike.Target;
		var ranged = strike.Weapon.IsRanged;
		var swing = ClipSeconds(attacker, 0.45, AttackClips(strike.Weapon));
		var flight = ranged ? 0.20 : 0.0;
		var impact = (swing * 0.55) + flight;

		Enqueue(Math.Max(swing, impact + 0.25), () =>
		{
			if (!_figures.TryGetValue(attacker, out var from) || !_figures.TryGetValue(target, out var to))
			{
				return;
			}

			Follow(to);
			Turn(from, to.Position.X - from.Position.X, to.Position.Z - from.Position.Z);
			PlayClip(attacker, loop: false, AttackClips(strike.Weapon));

			var beat = from.CreateTween();
			beat.TweenInterval(swing * 0.55);

			if (ranged)
			{
				beat.TweenCallback(Callable.From(() => Launch(from, to, flight, new Color(0.85f, 0.75f, 0.55f), 0.035f)));
				beat.TweenInterval(flight);
			}

			beat.TweenCallback(Callable.From(() =>
			{
				WriteLine(line, indent);

				if (strike.Attack.IsHit)
				{
					var dealt = (strike.Applied?.Total ?? 0) + strike.NonlethalDealt;
					Float(to, strike.Attack.IsCritical ? $"{dealt}!" : $"{dealt}", strike.Attack.IsCritical ? CritColour : HurtColour, strike.Attack.IsCritical);
					Flinch(to, from);
					Assume(target, to, after, instant: false);
				}
				else
				{
					Float(to, "miss", MissColour, false);
				}
			}));

			beat.TweenInterval(Math.Max(0.05, swing * 0.45));
			beat.TweenCallback(Callable.From(() => Idle(attacker)));
		});
	}

	/// <summary>
	/// A trip: in low, and whoever loses their feet loses them on the contact.
	/// </summary>
	/// <remarks>
	/// Both postures are written down now, the attacker's as well — a trip that goes badly
	/// enough puts the one who tried it on the floor instead, and he should fall on the beat
	/// like anybody else rather than being tidied up afterwards.
	/// </remarks>
	private void StageTrip(ManeuverActionResult maneuver, Creature victim, ManeuverResult check)
	{
		var actor = maneuver.Actor;
		var line = maneuver.Description;
		var victimAfter = PostureOf(victim);
		var actorAfter = PostureOf(actor);
		var clips = new[] { "trip", $"cast_{Grip(actor.PrimaryAttack)}", "attack_melee", "attack" };
		var seconds = ClipSeconds(actor, 0.45, clips);

		Enqueue(seconds + 0.15, () =>
		{
			if (!_figures.TryGetValue(actor, out var from) || !_figures.TryGetValue(victim, out var to))
			{
				WriteLine(line, indent: false);
				return;
			}

			Follow(to);
			Turn(from, to.Position.X - from.Position.X, to.Position.Z - from.Position.Z);
			PlayClip(actor, loop: false, clips);

			// In under their guard and out again, low, while the clip does the sweeping.
			var home = from.Position;
			var tween = from.CreateTween();
			tween.TweenProperty(from, "position", home.Lerp(to.Position, 0.35f), seconds * 0.5f).SetEase(Tween.EaseType.In);
			tween.TweenCallback(Callable.From(() =>
			{
				WriteLine(line, indent: false);

				if (check.Succeeded)
				{
					Float(to, "tripped", CritColour, false);
					Flinch(to, from);
					Assume(victim, to, victimAfter, instant: false);
				}
				else
				{
					Float(to, check.Backfired ? "reversed" : "keeps footing", MissColour, false);
					Assume(actor, from, actorAfter, instant: false);
				}
			}));
			tween.TweenProperty(from, "position", home, seconds * 0.5f).SetEase(Tween.EaseType.Out);
			tween.TweenCallback(Callable.From(() => Idle(actor)));
		});
	}

	/// <summary>
	/// A shove: shoulder in, and the one shoved goes back as far as the rules sent them —
	/// on the contact, pushed by it, rather than sliding off on their own a moment later.
	/// </summary>
	private void StageShove(ManeuverActionResult maneuver, Creature victim, ManeuverResult check)
	{
		var actor = maneuver.Actor;
		var line = maneuver.Description;
		var landing = _battle.Battlefield?.SquareOf(victim);
		var victimAfter = PostureOf(victim);
		var clips = new[] { "shove", $"cast_{Grip(actor.PrimaryAttack)}", "attack_melee", "attack" };
		var seconds = ClipSeconds(actor, 0.45, clips);

		Enqueue(seconds + 0.30, () =>
		{
			if (!_figures.TryGetValue(actor, out var from) || !_figures.TryGetValue(victim, out var to))
			{
				WriteLine(line, indent: false);
				return;
			}

			Follow(to);
			Turn(from, to.Position.X - from.Position.X, to.Position.Z - from.Position.Z);
			PlayClip(actor, loop: false, clips);

			var home = from.Position;
			var tween = from.CreateTween();
			tween.TweenProperty(from, "position", home.Lerp(to.Position, 0.5f), seconds * 0.45f).SetEase(Tween.EaseType.In);
			tween.TweenCallback(Callable.From(() =>
			{
				WriteLine(line, indent: false);

				if (check.Succeeded && landing is { } square)
				{
					Float(to, "driven back", CritColour, false);

					var rest = new Vector3(square.X + 0.5f, to.Position.Y, square.Y + 0.5f);
					to.CreateTween().TweenProperty(to, "position", rest, 0.25f)
						.SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
					Assume(victim, to, victimAfter, instant: false);
				}
				else
				{
					Float(to, "holds firm", MissColour, false);
					Flinch(to, from);
				}
			}));
			tween.TweenProperty(from, "position", home, seconds * 0.55f).SetEase(Tween.EaseType.Out);
			tween.TweenCallback(Callable.From(() => Idle(actor)));
		});
	}

	private void StageSpell(SpellCast cast)
	{
		var caster = cast.Caster;
		var results = cast.Targets.ToList();

		// Worded now, shown when the spell lands. Each line ends with the target's hit points,
		// read off the creature as it is turned into text; by the time the bolt arrives the
		// rules are turns further on, and a mage armour cast at full health was being reported
		// with the wound she took two rounds later.
		var worded = results.Select(r => (Result: r, Line: r.ToString())).ToList();
		var postures = results.ToDictionary(r => r.Target, r => PostureOf(r.Target));
		var harmful = results.Any(r => r.Damage > 0 || r.Attack is not null);
		var glow = harmful ? FireGlow : SpellGlow;
		var wind = ClipSeconds(caster, 0.45, SpellClips);
		const double flight = 0.28;

		Enqueue(wind * 0.6 + flight + 0.45, () =>
		{
			if (!_figures.TryGetValue(caster, out var from))
			{
				return;
			}

			// Where it goes: the square aimed at, the creature aimed at, or failing both the
			// middle of whoever it touched.
			Vector3? centre = cast.Aim.Point is { } point
				? new Vector3(point.X + 0.5f, 0.6f, point.Y + 0.5f)
				: cast.Aim.Creature is { } aimed && _figures.TryGetValue(aimed, out var there)
					? there.Position + new Vector3(0, 0.8f, 0)
					: null;

			if (centre is { } facing)
			{
				Turn(from, facing.X - from.Position.X, facing.Z - from.Position.Z);
			}

			Follow(from);
			PlayClip(caster, loop: false, SpellClips);

			var beat = from.CreateTween();
			beat.TweenInterval(wind * 0.6);

			if (centre is { } mark && mark.DistanceTo(from.Position) > 0.9f)
			{
				beat.TweenCallback(Callable.From(() => LaunchAt(from, mark, (float)flight, glow, 0.16f)));
				beat.TweenInterval(flight);
			}

			beat.TweenCallback(Callable.From(() =>
			{
				if (cast.Aim.Point is not null && centre is { } burst)
				{
					var reach = results
						.Select(r => _figures.TryGetValue(r.Target, out var f) ? f.Position.DistanceTo(burst) : 0f)
						.DefaultIfEmpty(1.5f).Max();

					Burst(burst, Mathf.Max(1.5f, reach + 0.6f), glow);
				}

				foreach (var (result, line) in worded)
				{
					WriteLine(line, indent: true);

					if (!_figures.TryGetValue(result.Target, out var figure))
					{
						continue;
					}

					if (result.Missed)
					{
						Float(figure, "miss", MissColour, false);
					}
					else if (result.Healed > 0)
					{
						Float(figure, $"+{result.Healed}", HealColour, false);
					}
					else if (result.Damage > 0)
					{
						Float(figure, $"{result.Damage}", HurtColour, false);
						Flinch(figure, from);
					}
					else if (result.Applied.Count > 0)
					{
						Float(figure, result.Applied[0], SpellGlow, false);
					}

					Assume(result.Target, figure, postures[result.Target], instant: false);
				}
			}));

			beat.TweenInterval(0.3);
			beat.TweenCallback(Callable.From(() => Idle(caster)));
		});
	}

	// ---- posture: standing, lying, dead ----

	private enum Posture
	{
		Standing,
		Prone,
		Down,
	}

	private static Posture PostureOf(Creature creature) =>
		!creature.IsConscious ? Posture.Down : creature.IsProne ? Posture.Prone : Posture.Standing;

	/// <summary>
	/// Puts a figure into a posture: up, flat on the floor, or dead.
	/// </summary>
	/// <remarks>
	/// A model with a death clip dies by playing it, and is then left alone — the clip already
	/// ends on the ground, and tipping the holder over as well would bury it. Anything without
	/// one, and anyone merely tripped, is laid down by rotating the holder, as before.
	/// </remarks>
	private void Assume(Creature creature, Node3D figure, Posture posture, bool instant)
	{
		var dies = posture == Posture.Down && HasClip(creature, "death");

		if (posture == Posture.Down)
		{
			if (_fallen.Add(creature) && dies)
			{
				PlayClip(creature, loop: false, "death");
				if (instant && Animations(figure) is { } player)
				{
					player.Advance(10.0);
				}
			}
		}
		else if (_fallen.Remove(creature))
		{
			Idle(creature);
		}

		var lying = posture != Posture.Standing && !dies;
		var height = lying ? 0.3f * ScaleOf(creature.Size) : 0f;
		var tilt = lying ? Mathf.Pi / 2f : 0f;

		if (instant)
		{
			figure.Position = new Vector3(figure.Position.X, height, figure.Position.Z);
			figure.Rotation = new Vector3(tilt, figure.Rotation.Y, 0f);
			return;
		}

		if (!Mathf.IsEqualApprox(figure.Rotation.X, tilt) || !Mathf.IsEqualApprox(figure.Position.Y, height))
		{
			var tween = figure.CreateTween().SetParallel();
			tween.TweenProperty(figure, "rotation:x", tilt, 0.22f);
			tween.TweenProperty(figure, "position:y", height, 0.22f);
		}
	}

	// ---- clips ----

	private static readonly string[] SpellClips = ["cast_unarmed_magic", "cast_main_hand_wand", "cast_two_handed_staff", "attack"];

	/// <summary>
	/// Which family of clips suits what a creature is holding.
	/// </summary>
	/// <remarks>
	/// The human pack names its animations by grip — <c>idle_combat_two_handed_melee</c>,
	/// <c>cast_main_hand_melee</c> (its word for an attack) — so the weapon picks the clip. The
	/// goblins have one of each, which the fallbacks at the end of every list catch.
	/// </remarks>
	private static string Grip(WeaponAttack weapon)
	{
		var name = weapon?.Name.ToLowerInvariant() ?? string.Empty;

		if (name.Contains("crossbow"))
		{
			return "two_handed_crossbow";
		}

		if (weapon?.IsRanged == true)
		{
			return "two_handed_bow";
		}

		if (name.Contains("staff"))
		{
			return "two_handed_staff";
		}

		return name.Contains("great") || name.Contains("two-handed") ? "two_handed_melee" : "main_hand_melee";
	}

	private static string[] AttackClips(WeaponAttack weapon) =>
		[$"cast_{Grip(weapon)}", "attack_melee", "attack", "cast_main_hand_melee"];

	private void Idle(Creature creature)
	{
		if (_fallen.Contains(creature))
		{
			return;
		}

		PlayClip(creature, loop: true, $"idle_combat_{Grip(creature.PrimaryAttack)}", "idle_combat", "idle");
	}

	private bool HasClip(Creature creature, params string[] wanted) =>
		_figures.TryGetValue(creature, out var figure)
		&& Animations(figure) is { } player
		&& FindClip(player, wanted) is not null;

	private double ClipSeconds(Creature creature, double otherwise, params string[] wanted) =>
		_figures.TryGetValue(creature, out var figure)
		&& Animations(figure) is { } player
		&& FindClip(player, wanted) is { } clip
			? Math.Clamp(player.GetAnimation(clip).Length, 0.30, 0.90)
			: otherwise;

	private void PlayClip(Creature creature, bool loop, params string[] wanted)
	{
		// The fallen stay down. A beat staged before they fell — their own swing at somebody
		// running past, shown after the blow that dropped them — would otherwise play over the
		// death clip and stand a corpse back up, frozen on the last frame of an attack.
		if (_fallen.Contains(creature) && Array.IndexOf(wanted, "death") < 0)
		{
			return;
		}

		if (!_figures.TryGetValue(creature, out var figure)
			|| Animations(figure) is not { } player
			|| FindClip(player, wanted) is not { } clip)
		{
			return;
		}

		// glTF brings every clip in one-shot, and a run that plays once is a slide.
		player.GetAnimation(clip).LoopMode = loop ? Animation.LoopModeEnum.Linear : Animation.LoopModeEnum.None;

		if (loop && player.CurrentAnimation == clip && player.IsPlaying())
		{
			return;
		}

		player.Play(clip, customBlend: 0.12);
	}

	/// <summary>
	/// The first wanted name any clip ends with, and failing that the first any clip contains.
	/// Ending-with first, so <c>cast_main_hand_melee</c> is not beaten by its own <c>_2</c>.
	/// </summary>
	private static string FindClip(AnimationPlayer player, IEnumerable<string> wanted)
	{
		var clips = player.GetAnimationList();

		foreach (var name in wanted)
		{
			var found = Array.Find(clips, clip => clip.EndsWith(name, StringComparison.Ordinal))
				?? Array.Find(clips, clip => clip.Contains(name, StringComparison.Ordinal));

			if (found is not null)
			{
				return found;
			}
		}

		return null;
	}

	// ---- small effects ----

	/// <summary>Turns a figure to face along (dx, dz), keeping whatever tilt it has.</summary>
	private static void Turn(Node3D figure, float dx, float dz)
	{
		if (Mathf.IsZeroApprox(dx) && Mathf.IsZeroApprox(dz))
		{
			return;
		}

		figure.Rotation = new Vector3(figure.Rotation.X, Mathf.Atan2(dx, dz), 0f);
	}

	/// <summary>A number, or a word, that rises off somebody and fades.</summary>
	private void Float(Node3D over, string text, Color colour, bool loud)
	{
		var label = new Label3D
		{
			Text = text,
			Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
			NoDepthTest = true,
			FontSize = loud ? 96 : 72,
			OutlineSize = 18,
			PixelSize = 0.005f,
			Modulate = colour,
			Position = over.Position + new Vector3(0, 2.0f, 0),
			Scale = Vector3.One * LabelScale(),
		};

		_world.AddChild(label);

		var tween = label.CreateTween().SetParallel();
		tween.TweenProperty(label, "position:y", label.Position.Y + 0.9f, 0.9f)
			.SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
		tween.TweenProperty(label, "modulate:a", 0f, 0.9f).SetEase(Tween.EaseType.In);

		// The outline has an alpha of its own. Fade only the fill and every number ends its life
		// as a black smudge hanging over the fight.
		tween.TweenProperty(label, "outline_modulate:a", 0f, 0.9f).SetEase(Tween.EaseType.In);
		tween.Chain().TweenCallback(Callable.From(label.QueueFree));
	}

	/// <summary>Knocked back a hand's breadth by whoever hit them, and straight back again.</summary>
	private static void Flinch(Node3D struck, Node3D by)
	{
		var away = struck.Position - by.Position;
		away.Y = 0;

		if (away.LengthSquared() < 0.0001f)
		{
			return;
		}

		var home = struck.Position;
		var tween = struck.CreateTween();
		tween.TweenProperty(struck, "position", home + (away.Normalized() * 0.14f), 0.05f);
		tween.TweenProperty(struck, "position", home, 0.14f);
	}

	private void Launch(Node3D from, Node3D to, double seconds, Color colour, float size) =>
		LaunchAt(from, to.Position + new Vector3(0, 0.8f, 0), (float)seconds, colour, size);

	private void LaunchAt(Node3D from, Vector3 mark, float seconds, Color colour, float size)
	{
		var missile = new MeshInstance3D
		{
			Mesh = new SphereMesh { Radius = size, Height = size * 2f, RadialSegments = 10, Rings = 6 },
			MaterialOverride = new StandardMaterial3D
			{
				AlbedoColor = colour,
				EmissionEnabled = true,
				Emission = colour,
				EmissionEnergyMultiplier = 2.5f,
			},
			Position = from.Position + new Vector3(0, 0.9f, 0),
		};

		_world.AddChild(missile);

		var tween = missile.CreateTween();
		tween.TweenProperty(missile, "position", mark, seconds);
		tween.TweenCallback(Callable.From(missile.QueueFree));
	}

	/// <summary>A sphere of light that swells to the size of what it hit, and goes.</summary>
	private void Burst(Vector3 at, float radius, Color colour)
	{
		var paint = new StandardMaterial3D
		{
			AlbedoColor = new Color(colour, 0.55f),
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			EmissionEnabled = true,
			Emission = colour,
			EmissionEnergyMultiplier = 2.0f,
			CullMode = BaseMaterial3D.CullModeEnum.Disabled,
		};

		var ball = new MeshInstance3D
		{
			Mesh = new SphereMesh { Radius = 1f, Height = 2f },
			MaterialOverride = paint,
			Position = at,
			Scale = Vector3.One * 0.2f,
		};

		_world.AddChild(ball);

		var tween = ball.CreateTween().SetParallel();
		tween.TweenProperty(ball, "scale", Vector3.One * radius, 0.35f)
			.SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
		tween.TweenProperty(paint, "albedo_color:a", 0f, 0.45f);
		tween.Chain().TweenCallback(Callable.From(ball.QueueFree));
	}
}
