using Ironbound.Rules.Creatures;
using Ironbound.Rules.Maps;

namespace Ironbound.Rules.Encounters.Actions;

/// <summary>
/// Backing carefully out of a melee: a full round's effort, up to twice your speed, and the
/// square you start in does not count as threatened.
/// </summary>
/// <remarks>
/// Only the <em>starting</em> square is safe, which is the part people misremember. Withdraw gets
/// you out of the melee you are standing in; it does not make you immune to everyone else's reach
/// on the way past.
/// </remarks>
public sealed class WithdrawAction(IReadOnlyList<GridSquare> path) : MoveAction(path)
{
    public override string Name => "withdraw";

    public override ActionCost Cost => ActionCost.FullRound;

    protected override int Allowance(Creature mover) => mover.CurrentSpeed * 2;

    protected override bool ProvokesLeaving(int stepIndex) => stepIndex > 0;
}
