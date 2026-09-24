using System.Linq;
using Godot;
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
/// Only for models that have somewhere to put it. The human pack's skeleton carries a socket
/// bone in each hand, exported with everything else; the generated creatures have none, because
/// their kit is modelled into them. No socket, no attachment, and they are left exactly as they
/// were.
/// </para>
/// </remarks>
public partial class Main
{
	private const string RightSocket = "wep_pos_R";
	private const string LeftSocket = "wep_pos_L";
	private const string ShieldArm = "arm_lower_L";
	private const string ArmouryMark = "armoury";

	// How a weapon built to the generator's convention — grip at the origin, business end up its
	// own +Y, a shield's face down its +Z — sits in one of this pack's sockets. Found by putting
	// a sword in Valeria's hand at four rotations and looking, not by reading quaternions: the
	// socket's forward axis is where a blade goes, and a shield faces the other way about it.
	private static readonly Vector3 HeldGrip = new(90, 0, 0);

	// A shield is strapped to the forearm, not held in the fist. Hung on the hand socket it
	// looked right in the rest pose it was tuned against and lay flat like a tray the moment she
	// took up her guard, because a palm turns and a forearm does not. On the forearm bone it
	// needs no rotation at all — the bone's own forward is outward — only moving a third of the
	// way down the arm and out past its thickness, so the arm lies along the strap behind the
	// board instead of through the middle of it.
	private static readonly Vector3 ShieldGrip = Vector3.Zero;
	private static readonly Vector3 ShieldSeat = new(0f, 0.32f, 0.16f);

	/// <summary>Hangs everything <paramref name="creature"/> has in hand on its figure.</summary>
	private void Arm(Creature creature, Node model)
	{
		var skeleton = model.FindChildren("*", "Skeleton3D", true, false)
			.OfType<Skeleton3D>()
			.FirstOrDefault(candidate => candidate.FindBone(RightSocket) >= 0);

		if (skeleton is null)
		{
			return;
		}

		foreach (var worn in creature.Equipment.Worn)
		{
			var (socket, grip, seat) = worn.Slot switch
			{
				EquipmentSlot.MainHand => (RightSocket, HeldGrip, Vector3.Zero),
				EquipmentSlot.OffHand => (LeftSocket, HeldGrip, Vector3.Zero),
				EquipmentSlot.Shield => (ShieldArm, ShieldGrip, ShieldSeat),
				_ => (null, Vector3.Zero, Vector3.Zero),
			};

			if (socket is null || worn.Item.Model.Length == 0 || skeleton.FindBone(socket) < 0)
			{
				continue;
			}

			if (Scene(worn.Item.Model, $"item '{worn.Item.Id}'")?.Instantiate() is not Node3D held)
			{
				continue;
			}

			// A child of the skeleton, following one bone: whatever clip is playing carries the
			// weapon with it, because the pack's clips were authored against these sockets.
			var hand = new BoneAttachment3D { Name = $"Held_{worn.Item.Id}" };
			hand.SetMeta(ArmouryMark, true);
			skeleton.AddChild(hand);
			hand.BoneName = socket;

			hand.AddChild(held);
			held.RotationDegrees = grip;
			held.Position = seat;
		}
	}

	/// <summary>Takes everything out of their hands and puts back what they now hold.</summary>
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
	}
}
