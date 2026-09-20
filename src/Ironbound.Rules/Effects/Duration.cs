namespace Ironbound.Rules.Effects;

/// <summary>
/// A span of game time, held in ticks.
/// </summary>
/// <remarks>
/// This type exists so that "3 rounds" can never be stored anywhere. A round is a scheduler
/// policy — a window in which one actor has control — not a unit the rules may count in. Keeping
/// every duration, cooldown and buff in ticks is what lets a turn-based scheduler and a
/// real-time-with-pause scheduler drive the same rules without either one knowing about the other.
/// </remarks>
public readonly record struct Duration : IComparable<Duration>
{
    /// <summary>A round is six seconds, and a tick is a tenth of a second.</summary>
    public const int TicksPerRound = 60;

    public const int RoundsPerMinute = 10;
    public const int TicksPerMinute = TicksPerRound * RoundsPerMinute;
    public const int TicksPerHour = TicksPerMinute * 60;
    public const int TicksPerDay = TicksPerHour * 24;

    private Duration(int ticks, bool permanent)
    {
        Ticks = ticks;
        IsPermanent = permanent;
    }

    /// <summary>Nothing at all. An effect with this duration ends at the next advance.</summary>
    public static Duration Zero { get; }

    /// <summary>Never runs out. Not a sentinel tick count — a flag, so no arithmetic can trip over it.</summary>
    public static Duration Permanent { get; } = new(0, permanent: true);

    public int Ticks { get; }

    public bool IsPermanent { get; }

    public bool IsZero => !IsPermanent && Ticks == 0;

    public static Duration FromTicks(int ticks) => new(Check(ticks), permanent: false);

    public static Duration Rounds(int rounds) => FromTicks(Check(rounds) * TicksPerRound);

    public static Duration Minutes(int minutes) => FromTicks(Check(minutes) * TicksPerMinute);

    public static Duration Hours(int hours) => FromTicks(Check(hours) * TicksPerHour);

    public static Duration Days(int days) => FromTicks(Check(days) * TicksPerDay);

    private static int Check(int value, [System.Runtime.CompilerServices.CallerArgumentExpression(nameof(value))] string? name = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value, name);
        return value;
    }

    public static Duration operator +(Duration left, Duration right) =>
        left.IsPermanent || right.IsPermanent ? Permanent : FromTicks(left.Ticks + right.Ticks);

    public static Duration operator -(Duration left, Duration right) =>
        left.IsPermanent ? Permanent : FromTicks(Math.Max(0, left.Ticks - right.Ticks));

    public static bool operator <(Duration left, Duration right) => left.CompareTo(right) < 0;

    public static bool operator >(Duration left, Duration right) => left.CompareTo(right) > 0;

    public static bool operator <=(Duration left, Duration right) => left.CompareTo(right) <= 0;

    public static bool operator >=(Duration left, Duration right) => left.CompareTo(right) >= 0;

    /// <summary>Permanence sorts above every finite span.</summary>
    public int CompareTo(Duration other) => (IsPermanent, other.IsPermanent) switch
    {
        (true, true) => 0,
        (true, false) => 1,
        (false, true) => -1,
        _ => Ticks.CompareTo(other.Ticks),
    };

    public override string ToString()
    {
        if (IsPermanent)
        {
            return "permanent";
        }

        if (Ticks == 0)
        {
            return "instant";
        }

        return Ticks switch
        {
            _ when Ticks % TicksPerDay == 0 => Plural(Ticks / TicksPerDay, "day"),
            _ when Ticks % TicksPerHour == 0 => Plural(Ticks / TicksPerHour, "hour"),
            _ when Ticks % TicksPerMinute == 0 => Plural(Ticks / TicksPerMinute, "minute"),
            _ when Ticks % TicksPerRound == 0 => Plural(Ticks / TicksPerRound, "round"),
            _ => Plural(Ticks, "tick"),
        };
    }

    private static string Plural(int count, string unit) => count == 1 ? $"1 {unit}" : $"{count} {unit}s";
}
