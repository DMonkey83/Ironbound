using Ironbound.Rules.Encounters;

namespace Ironbound.Simulation;

/// <summary>
/// Whatever decides what a creature does: a player's interface, or an AI.
/// </summary>
/// <remarks>
/// Returning null means "nothing more right now" and ends the turn. That is deliberately the same
/// answer an unattended player gives, so the loop never has to distinguish between "thinking",
/// "waiting for a click" and "done" — and never blocks on any of them.
/// </remarks>
public interface IActionSource
{
    GameAction? NextAction(Turn turn);
}
