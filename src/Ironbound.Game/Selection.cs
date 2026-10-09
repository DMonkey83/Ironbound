using System.Collections.Generic;
using System.Linq;
using Godot;
using Ironbound.Rules.Creatures;
using Ironbound.Simulation;

/// <summary>
/// The part of <see cref="Main"/> that is who the player has picked between fights: one of the
/// party, several, or all of them, as in Wrath.
/// </summary>
/// <remarks>
/// The project owner asked to be able to pick a character between fights and use them. Before
/// this the party walked as one with whoever was first in line out in front, so it was that
/// character, not the rogue, who tried the storeroom door, and that character, not the one
/// with the best Acrobatics, who jumped the crevice.
/// <para>
/// A click on a portrait or a figure picks that one; Shift adds or removes; Ctrl+A or the All
/// plate picks everybody again. Whoever is picked walks when the ground is clicked — the first
/// of them in front, the rest round them — and the first of them is who opens a door, crosses
/// the bridge, searches a chest, takes the spoils, levels up, and whose sheet and bar are shown.
/// In a fight none of it applies: whoever's turn it is acts.
/// </para>
/// </remarks>
public partial class Main
{
	private static readonly Color SelectedRing = new(0.45f, 0.95f, 0.55f, 0.9f);

	/// <summary>Who is picked, in the order they were picked; the first leads.</summary>
	private readonly List<Creature> _selection = [];

	private readonly Dictionary<Creature, MeshInstance3D> _rings = new();
	private Button _selectAll;

	private bool Choosing => _campaign is not null && _campaign.State != CampaignState.Fighting;

	/// <summary>Who is picked and still on their feet; everybody standing if that is nobody.</summary>
	private List<Creature> Selected()
	{
		_selection.RemoveAll(one => !one.IsConscious || !_campaign.Party.Contains(one));
		return _selection.Count > 0
			? [.. _selection]
			: [.. _campaign.Party.Where(one => one.IsConscious)];
	}

	private void SelectAll()
	{
		_selection.Clear();
		_selection.AddRange(_campaign.Party.Where(one => one.IsConscious));
		SelectionChanged();
	}

	/// <summary>Picks <paramref name="who"/> alone, or with Shift adds or removes them.</summary>
	private void Select(Creature who, bool adding)
	{
		if (!Choosing || who is null || !_campaign.Party.Contains(who))
		{
			return;
		}

		if (!adding)
		{
			_selection.Clear();
			_selection.Add(who);
		}
		else if (_selection.Count == 0)
		{
			// Nobody picked means everybody: Shift on one of them takes them out of the whole.
			_selection.AddRange(Selected().Where(one => !ReferenceEquals(one, who)));
		}
		else if (!_selection.Remove(who))
		{
			_selection.Add(who);
		}
		else if (_selection.Count == 0)
		{
			_selection.Add(who);
		}

		SelectionChanged();
	}

	private void SelectionChanged()
	{
		// The spoils list names the same person, so taking an item gives it to whoever is picked.
		if (Selected().FirstOrDefault() is { } first && _bearer is not null)
		{
			var index = _campaign.Party.ToList().IndexOf(first);
			if (index >= 0 && index < _bearer.ItemCount)
			{
				_bearer.Selected = index;
			}
		}

		RefreshSheet();
		UpdateStatus();
		RefreshControls();
	}

	/// <summary>The party member whose figure is under the pointer, if any: within a hand's
	/// breadth of their middle on screen, or standing on the square pointed at.</summary>
	private Creature PartyMemberUnderCursor(Vector2 mouse)
	{
		if (_camera is null || _battle?.Battlefield is not { } field)
		{
			return null;
		}

		Creature best = null;
		var nearest = 48f;
		foreach (var member in _battle.Party)
		{
			if (!_figures.TryGetValue(member, out var figure) || !IsInstanceValid(figure))
			{
				continue;
			}

			var middle = figure.GlobalPosition + new Vector3(0, CapsuleHeight(member) * 0.5f, 0);
			if (_camera.IsPositionBehind(middle))
			{
				continue;
			}

			var gap = _camera.UnprojectPosition(middle).DistanceTo(mouse);
			if (gap < nearest)
			{
				nearest = gap;
				best = member;
			}
		}

		if (best is null && SquareUnderCursor() is { } square)
		{
			best = _battle.Party.FirstOrDefault(member => field.SquareOf(member) == square);
		}

		return best;
	}

	/// <summary>Between fights, a click on one of the party picks them rather than walking there.
	/// True if the click was taken.</summary>
	private bool SelectionClick(InputEventMouseButton click)
	{
		if (!Choosing || PartyMemberUnderCursor(click.Position) is not { } who)
		{
			return false;
		}

		Select(who, click.ShiftPressed);
		return true;
	}

	private bool SelectionKey(InputEvent @event)
	{
		if (Choosing && @event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.A, CtrlPressed: true })
		{
			SelectAll();
			return true;
		}

		return false;
	}

	/// <summary>A green ring at the feet of everybody picked, between fights. Every frame, so a
	/// figure rebuilt by a fight or a load gets its ring back.</summary>
	private void TendSelection()
	{
		if (_campaign is null)
		{
			return;
		}

		var picked = Choosing ? Selected() : [];
		foreach (var (who, ring) in _rings.ToList())
		{
			if (!IsInstanceValid(ring) || !picked.Contains(who) || !_figures.TryGetValue(who, out var figure) || ring.GetParent() != figure)
			{
				if (IsInstanceValid(ring))
				{
					ring.QueueFree();
				}

				_rings.Remove(who);
			}
		}

		foreach (var who in picked)
		{
			if (_rings.ContainsKey(who) || !_figures.TryGetValue(who, out var figure) || !IsInstanceValid(figure))
			{
				continue;
			}

			var ring = new MeshInstance3D
			{
				Mesh = new TorusMesh { InnerRadius = 0.36f, OuterRadius = 0.43f, Rings = 32, RingSegments = 6 },
				MaterialOverride = new StandardMaterial3D
				{
					AlbedoColor = SelectedRing,
					ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
					Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
				},
				Scale = new Vector3(1f, 0.2f, 1f),
				Position = new Vector3(0, 0.03f, 0),
				CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			};
			figure.AddChild(ring);
			_rings[who] = ring;
		}

		if (_selectAll is not null && IsInstanceValid(_selectAll))
		{
			_selectAll.Visible = Choosing && _battle.Party.Count > 1;
		}
	}

	/// <summary>The All plate at the head of the portrait row.</summary>
	private Button SelectAllButton()
	{
		_selectAll = Ironclad(new Button
		{
			Text = "ALL",
			TooltipText = "Pick the whole party  [Ctrl+A]",
			FocusMode = Control.FocusModeEnum.None,
			CustomMinimumSize = new Vector2(48, 0),
		});
		_selectAll.AddThemeFontOverride("font", Display);
		_selectAll.AddThemeFontSizeOverride("font_size", 15);
		_selectAll.AddThemeColorOverride("font_color", Parchment);
		_selectAll.AddThemeColorOverride("font_hover_color", BronzeBright);
		_selectAll.Pressed += SelectAll;
		return _selectAll;
	}
}
