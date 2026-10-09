using System.Collections.Generic;
using System.Linq;
using Godot;
using Ironbound.Rules.Combat;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Items;

/// <summary>
/// The part of <see cref="Main"/> that puts in somebody's hands what the rules say they hold.
/// </summary>
/// <remarks>
/// The party used to swing at goblins with empty fists. Equipment is real in this game — the
/// sergeant's silvered longsword is looted, handed to Valeria and is <em>the</em> answer to the
/// werewolf — so what is drawn follows what is worn, item by item, and changes when it changes.
/// <para>
/// What is in the hands is the weapon the creature fights with, held the way the idle clip for
/// that weapon holds its fists: one-handed, through both hands, or a bow in the left. A shield
/// goes on the forearm when the left hand is free and on the back when it is not, and anything
/// else worn — Merrin's staff while she carries the crossbow — is slung across the back. Two
/// things in one fist is what this used to draw.
/// </para>
/// <para>
/// Only for models that have somewhere to put it. The human pack's skeleton carries a socket
/// bone in each hand, exported with everything else; the generated creatures have none, because
/// their kit is modelled into them. No socket, no attachment, and they are left exactly as they
/// were.
/// </para>
/// </remarks>
public partial class Main
{
	private const string RightSocket = "wep_pos_R";
	private const string ArmouryMark = "armoury";

	/// <summary>Which bone something rides on, and where it sits in that bone's own space.</summary>
	private readonly record struct Hold(string Bone, Vector3 Rotation, Vector3 Position);

	// Measured, not guessed. A scratch scene posed each character in the idle clip for each grip,
	// found each fist from its finger bones — the knuckle row is the grip's axis, the curled
	// fingertips close the hole — aimed the item through it (or through both fists for a two-handed
	// weapon), and read the result back in the socket's space. The male and female skeletons agreed
	// to a few degrees and a few hundredths, so one table serves both; these are their middles.
	//
	// The old single grip turned everything 90 degrees about the socket, which is right for none of
	// them: a one-handed blade wants almost no turn at all, a crossbow lying forward, a bow in the
	// other hand.
	//
	// The two-handed stance is the odd one. Its fists sit a hand apart and are not wrapped round a
	// common axis, so a haft put exactly through both ran sideways across the waist like a rifle,
	// blades up and down. It goes through the lower fist instead, tilted up past the other into a
	// guard, head high and blades fore and aft. Measured on Karn, whose axe it is.
	private static readonly Dictionary<string, Hold> Held = new()
	{
		["main_hand_melee"] = new(RightSocket, new(0.2f, -4.5f, 2.4f), new(-0.032f, -0.001f, -0.023f)),
		["two_handed_melee"] = new(RightSocket, new(-31.2f, -125.9f, 177.2f), new(0.001f, -0.011f, -0.006f)),
		["two_handed_crossbow"] = new(RightSocket, new(85.2f, -28.6f, -0.3f), new(-0.025f, -0.005f, 0.018f)),
		["two_handed_staff"] = new(RightSocket, new(-33.7f, -36.6f, 47.8f), new(-0.312f, 0.422f, -0.582f)),
		["two_handed_bow"] = new("wep_pos_L", new(-1.9f, -81.4f, -1.5f), new(0.031f, -0.002f, -0.017f)),
	};

	// On the outside of the forearm, boss outward, centred most of the way to the wrist and stood
	// off the arm by more than the fist is deep. The first measurement took the back of the hand
	// for the outside and hung the board on the inside of the arm, facing her; seen from her left
	// it showed planks and a strap with the arm in front of them. Checked from her left side, the
	// view that tells the two apart, before it went in.
	private static readonly Hold Strapped = new("arm_lower_L", new(0f, -43f, 0f), new(-0.150f, 0.305f, 0.160f));

	// A shield nobody has a hand free for lies flat on the upper back, boss outward. Slung like a
	// staff it hung off one hip edge-on.
	private static readonly Hold OnBack = new("torso", new(0f, -172f, 0f), new(-0.043f, 0.485f, -0.34f));

	// Anything else across the back on the torso bone, one diagonal each way so two things do not
	// share a place.
	private static readonly Hold[] Slung =
	[
		new("torso", new(0f, 9f, -37f), new(-0.58f, -0.31f, -0.22f)),
		new("torso", new(0f, -9f, 37f), new(0.58f, -0.31f, -0.30f)),
	];

	/// <summary>Hangs everything <paramref name="creature"/> carries on its figure.</summary>
	private void Arm(Creature creature, Node model)
	{
		var skeleton = model.FindChildren("*", "Skeleton3D", true, false)
			.OfType<Skeleton3D>()
			.FirstOrDefault(candidate => candidate.FindBone(RightSocket) >= 0);

		if (skeleton is null)
		{
			return;
		}

		// The weapon in hand is the one it fights with: the attack and the item are one object.
		var weapon = InHand(creature);
		var grip = Grip(weapon);
		var inHand = creature.Equipment.Worn.FirstOrDefault(worn => worn.Weapon is not null && ReferenceEquals(worn.Weapon, weapon));
		var leftFree = grip == "main_hand_melee";
		var slung = 0;

		foreach (var worn in creature.Equipment.Worn)
		{
			// Thrown, or dropped on a trip that went wrong: on the floor somewhere until the fight
			// is over, not on the figure.
			if (worn.Item.Model.Length == 0 || creature.Equipment.IsOutOfHand(worn.Item))
			{
				continue;
			}

			Hold? hold;
			if (ReferenceEquals(worn, inHand))
			{
				hold = Held.TryGetValue(grip, out var held) ? held : Held["main_hand_melee"];
			}
			else if (worn.Slot == EquipmentSlot.Shield)
			{
				hold = leftFree ? Strapped : OnBack;
			}
			else if (worn.Slot is EquipmentSlot.MainHand or EquipmentSlot.OffHand && slung < Slung.Length)
			{
				hold = Slung[slung++];
			}
			else
			{
				hold = null;
			}

			if (hold is not { } at || skeleton.FindBone(at.Bone) < 0)
			{
				continue;
			}

			if (Scene(worn.Item.Model, $"item '{worn.Item.Id}'")?.Instantiate() is not Node3D item)
			{
				continue;
			}

			// A child of the skeleton, following one bone: whatever clip is playing carries the
			// item with it, because the pack's clips were authored against these bones.
			var mount = new BoneAttachment3D { Name = $"Held_{worn.Item.Id}" };
			mount.SetMeta(ArmouryMark, true);
			skeleton.AddChild(mount);
			mount.BoneName = at.Bone;

			mount.AddChild(item);
			item.RotationDegrees = at.Rotation;
			item.Position = at.Position;
		}
	}

	private static readonly Dictionary<string, Texture2D> Icons = [];

	/// <summary>
	/// The inventory picture of an item, or null for one nobody has drawn yet.
	/// </summary>
	/// <remarks>
	/// Icons are rendered from the same models the figures hold (tools/render_icons.py), one per
	/// model and named after it, so the silvered longsword's icon is the silvered longsword and not
	/// a generic sword. An item with no model of its own falls back to its weapon's. Everything
	/// else — armour, rings, coin, valuables — is drawn by tools/render_item_icons.py and named
	/// after the item; anything not drawn yet shows as words.
	/// </remarks>
	private static Texture2D ItemIcon(ItemDefinition item)
	{
		if (item is null)
		{
			return null;
		}

		var name = item.Model.Length > 0 ? System.IO.Path.GetFileNameWithoutExtension(item.Model) : item.Weapon;
		var key = string.IsNullOrEmpty(name) ? $"items/{item.Id}" : $"weapons/{name}";

		if (!Icons.TryGetValue(key, out var icon))
		{
			var path = $"res://art/icons/{key}.png";
			var fallback = $"res://art/icons/items/{item.Id}.png";
			icon = ResourceLoader.Exists(path) ? GD.Load<Texture2D>(path)
				: ResourceLoader.Exists(fallback) ? GD.Load<Texture2D>(fallback)
				: null;
			Icons[key] = icon;
		}

		return icon;
	}

	/// <summary>
	/// What the creature has in its hand to fight with: the first of its weapons it still holds,
	/// and never the throwing half of one, which is the same dagger.
	/// </summary>
	private static WeaponAttack InHand(Creature creature) =>
		creature.Attacks.FirstOrDefault(attack => !attack.IsThrownUse && !IsOutOfHand(creature, attack));

	private static bool IsOutOfHand(Creature creature, WeaponAttack attack) =>
		creature.Equipment.ItemFor(attack) is { } item && creature.Equipment.IsOutOfHand(item);

	/// <summary>Takes one thing out of a figure's hand without stopping whatever it is doing: a
	/// dagger leaving on the throw, before the rest of the swing has played.</summary>
	private void LetGo(Creature creature, ItemDefinition item)
	{
		if (_figures.TryGetValue(creature, out var figure)
			&& figure.FindChild($"Held_{item.Id}", true, false) is { } mount)
		{
			mount.QueueFree();
		}
	}

	/// <summary>Takes everything off them, puts back what they now carry, and takes up the stance
	/// that suits it — a crossbow traded for a sword changes how the fists are held.</summary>
	private void Rearm(Creature creature)
	{
		if (!_figures.TryGetValue(creature, out var figure))
		{
			return;
		}

		foreach (var node in figure.FindChildren("*", "BoneAttachment3D", true, false))
		{
			if (node.HasMeta(ArmouryMark))
			{
				node.GetParent().RemoveChild(node);
				node.QueueFree();
			}
		}

		Arm(creature, figure);
		Idle(creature);
	}
}
