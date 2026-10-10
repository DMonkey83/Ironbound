using System.Collections.Generic;
using System.Linq;
using Godot;
using Ironbound.Rules.Combat;
using Ironbound.Rules.Conditions;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Encounters;
using Ironbound.Rules.Encounters.Actions;
using Ironbound.Rules.Items;
using Ironbound.Rules.Maps;

/// <summary>
/// The part of <see cref="Main"/> that uses things off the belt in a fight: what a click means
/// with a potion or a flask picked, why a click would not do, the two ways of getting out of
/// trouble those things make, and how a throw looks.
/// </summary>
/// <remarks>
/// The owner chose belt quick slots: only what a character hung on the belt before the fight can
/// be reached in it. The rules are WP2's (<c>ItemActions.cs</c>); this is the hand on the mouse.
/// </remarks>
public partial class Main
{
	private static readonly Color AcidGlow = new(0.45f, 0.95f, 0.30f);
	private static readonly Color GooGlow = new(0.55f, 0.40f, 0.20f);
	private static readonly Color ThunderGlow = new(0.95f, 0.95f, 1.00f);

	private Button _breakFree;
	private Button _putOut;

	/// <summary>
	/// What clicking a square means with something from the belt picked: a potion on yourself or
	/// on a fallen friend beside you; a flask at somebody, or at the ground.
	/// </summary>
	private GameAction ItemActionFor(Creature actor, GridSquare square, Creature occupant)
	{
		if (SelectedUsable() is not { Consumable: { } use } item)
		{
			return null;
		}

		if (use.IsPotion)
		{
			return ReferenceEquals(occupant, actor) ? new DrinkPotionAction(item)
				: occupant is not null && !actor.IsEnemyOf(occupant) && !occupant.IsConscious ? new AdministerPotionAction(item, occupant)
				: null;
		}

		if (!use.IsThrown)
		{
			return null;
		}

		var somebody = occupant is not null && !ReferenceEquals(occupant, actor) ? occupant : null;
		return use.Aim switch
		{
			ThrowAim.Creature => somebody is not null ? ThrowItemAction.At(item, somebody) : null,
			ThrowAim.Square => ThrowItemAction.At(item, square),
			_ => somebody is not null ? ThrowItemAction.At(item, somebody) : ThrowItemAction.At(item, square),
		};
	}

	private string ItemWhyNot(Creature actor, GridSquare square, Creature occupant)
	{
		if (SelectedUsable() is not { Consumable: { } use } item)
		{
			return $"{actor.Name} has nothing picked from the belt.";
		}

		if (use.IsPotion)
		{
			return occupant is null || actor.IsEnemyOf(occupant)
				? $"Click {actor.Name} to drink the {item.Name}, or a fallen friend beside them to give it."
				: occupant.IsConscious
					? $"{occupant.Name} can drink for themselves; a potion is given only to somebody who is down."
					: $"{actor.Name} has to be beside {occupant.Name} to give it, and it takes the whole turn.";
		}

		var field = _battle.Battlefield;
		if (field?.SquareOf(actor) is not { } from)
		{
			return $"{actor.Name} cannot throw from here.";
		}

		if (actor.IsProne)
		{
			return $"{actor.Name} cannot throw from the floor.";
		}

		var feet = Distance.Between(from, square);
		if (feet > use.MaximumRange)
		{
			return $"That is {feet} ft away; a {item.Name} carries {use.MaximumRange} ft.";
		}

		if (!field.HasLineOfSight(from, square))
		{
			return $"{actor.Name} cannot see that square.";
		}

		return use.Aim == ThrowAim.Creature && occupant is null
			? $"A {item.Name} has to be thrown at somebody."
			: $"{actor.Name} cannot throw it there.";
	}

	/// <summary>The belt list again if what is on the belt has changed since it was filled.</summary>
	private void RefreshItems(Creature actor)
	{
		// Only once the board has caught up; RefreshControls runs again when it has.
		if (StageBusy)
		{
			return;
		}

		var now = Consumables.OnBelt(actor);
		if (now.Count != _usables.Count || now.Where((stack, i) => stack.Item.Id != _usables[i].Item.Id || stack.Count != _usables[i].Count).Any())
		{
			SelectItemsFor(actor);
		}
	}

	// ---- getting out of what the flasks do ----

	private void BuildItemButtons(HBoxContainer bar)
	{
		_breakFree = new Button
		{
			Text = "Break free",
			Visible = false,
			FocusMode = Control.FocusModeEnum.None,
			SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
			TooltipText = $"A full round: Strength or Escape Artist against DC {BreakFreeAction.TanglefootDifficulty} to tear free of what holds them to the floor.",
		};
		_breakFree.Pressed += () => ActOnSelf(new BreakFreeAction(), "Nothing is holding them down.");
		bar.AddChild(_breakFree);

		_putOut = new Button
		{
			Text = "Put out the flames",
			Visible = false,
			FocusMode = Control.FocusModeEnum.None,
			SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
			TooltipText = $"A full round rolling on the ground: Reflex against DC {PutOutFlamesAction.Difficulty}, +{PutOutFlamesAction.RollingBonus} for the rolling.",
		};
		_putOut.Pressed += () => ActOnSelf(new PutOutFlamesAction(), "Nothing on them is burning.");
		bar.AddChild(_putOut);
	}

	/// <summary>Shows the two only when they apply to whoever is acting.</summary>
	private void RefreshItemButtons(Turn turn)
	{
		if (_breakFree is null)
		{
			return;
		}

		var actor = turn?.Actor;
		_breakFree.Visible = actor is not null && actor.Conditions.Contains(Condition.Anchored);
		_breakFree.Disabled = turn is null || !turn.CanTake(new BreakFreeAction());
		_putOut.Visible = actor is not null && PutOutFlamesAction.IsBurning(actor);
		_putOut.Disabled = turn is null || !turn.CanTake(new PutOutFlamesAction());

		if (actor is not null)
		{
			RefreshItems(actor);
		}
	}

	private void ActOnSelf(GameAction action, string refusal)
	{
		if (StageBusy)
		{
			return;
		}

		var lines = _battle.Act(action);
		if (lines.Count == 0)
		{
			Refuse(refusal);
			return;
		}

		Stage(_battle.LastResult, lines);
		RefreshFigures();
		PromptTurn();
		RefreshControls();
		_hovered = null;
	}

	// ---- how it looks ----

	/// <summary>
	/// A flask in an arc to where it was aimed, a second hop if it went astray, and a burst of
	/// its colour where it broke: fire orange, acid green, goo brown, a thunderstone's white flash.
	/// </summary>
	private void StageThrow(ThrowResult thrown)
	{
		var use = thrown.Item.Consumable;
		var colour = use?.Kind switch
		{
			ConsumableKind.Tanglefoot => GooGlow,
			ConsumableKind.Thunderstone => ThunderGlow,
			_ => use?.DamageType == DamageType.Acid ? AcidGlow : FireGlow,
		};

		var aimed = Centre(thrown.Aimed, 0.4f);
		Enqueue(0.45, () =>
		{
			if (!_figures.TryGetValue(thrown.Actor, out var from))
			{
				return;
			}

			Turn(from, aimed.X - from.Position.X, aimed.Z - from.Position.Z);
			PlayClip(thrown.Actor, loop: false, SpellClips);
			LaunchAt(from, aimed, 0.4f, colour, 0.07f);
		});

		if (thrown.Landed is not { } landed)
		{
			return;
		}

		var broke = Centre(landed, 0.3f);
		if (landed != thrown.Aimed)
		{
			// Astray: off the aim point and on to where it really came down.
			Enqueue(0.3, () => Hop(aimed, broke, colour));
		}

		var radius = use?.Kind switch
		{
			ConsumableKind.Thunderstone => use.RadiusFeet / (float)Distance.FeetPerSquare + 0.5f,
			ConsumableKind.Tanglefoot => 0.5f,
			_ => 1.5f,
		};
		Enqueue(0.35, () => Burst(broke, radius, colour));
	}

	private static Vector3 Centre(GridSquare square, float height) => new(square.X + 0.5f, height, square.Y + 0.5f);

	/// <summary>A missile from one point to another, for the second leg of a throw gone astray.</summary>
	private void Hop(Vector3 from, Vector3 to, Color colour)
	{
		var missile = new MeshInstance3D
		{
			Mesh = new SphereMesh { Radius = 0.07f, Height = 0.14f, RadialSegments = 10, Rings = 6 },
			MaterialOverride = new StandardMaterial3D { AlbedoColor = colour, EmissionEnabled = true, Emission = colour, EmissionEnergyMultiplier = 2.5f },
			Position = from,
		};
		_world.AddChild(missile);
		var tween = missile.CreateTween();
		tween.TweenProperty(missile, "position", to, 0.25f);
		tween.TweenCallback(Callable.From(missile.QueueFree));
	}

	/// <summary>A potion drunk or given: a soft glow on whoever it went into.</summary>
	private void StagePotion(PotionResult potion)
	{
		Enqueue(0.5, () =>
		{
			if (_figures.TryGetValue(potion.Drinker, out var figure))
			{
				Burst(figure.Position + new Vector3(0, 0.8f, 0), 0.6f, SpellGlow);
			}
		});
	}
}
