using System.Text;

namespace Ironbound.Rules.Dice;

/// <summary>One die as it landed. <paramref name="Counted"/> is false for a die dropped by kh/kl.</summary>
public readonly record struct DieResult(int TermIndex, int Sides, int Value, bool Counted);

/// <summary>
/// The outcome of a <see cref="DiceExpression"/>, with every die preserved. The dropped
/// dice are kept deliberately: "4d6kh3: [5, 3, (2), 6] = 14" is the log line a player trusts.
/// </summary>
public sealed class DiceRoll(string expression, int total, IReadOnlyList<DieResult> dice, int constant)
{
    /// <summary>Canonical text of the expression that produced this roll.</summary>
    public string Expression { get; } = expression;

    public int Total { get; } = total;

    public IReadOnlyList<DieResult> Dice { get; } = dice;

    /// <summary>Net contribution of the flat terms.</summary>
    public int Constant { get; } = constant;

    public override string ToString()
    {
        var text = new StringBuilder(Expression).Append(": ");

        if (Dice.Count > 0)
        {
            text.Append('[');
            for (var i = 0; i < Dice.Count; i++)
            {
                if (i > 0)
                {
                    text.Append(", ");
                }

                var die = Dice[i];
                text.Append(die.Counted ? die.Value.ToString() : $"({die.Value})");
            }

            text.Append(']');
        }

        if (Constant != 0)
        {
            if (Dice.Count > 0)
            {
                text.Append(' ');
            }

            text.Append(Constant.ToString("+0;-0"));
        }

        return text.Append(" = ").Append(Total).ToString();
    }
}
