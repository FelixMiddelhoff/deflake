using System;

namespace Deflake.Runtime;

/// <summary>The <c>kind</c> strings that appear in a session file and select an event's payload shape.</summary>
internal static class EventKinds
{
    public const string GetUtcNow = "GetUtcNow";
    public const string GetTimestamp = "GetTimestamp";
    public const string CreateTimer = "CreateTimer";
    public const string TimerFired = "TimerFired";

    public const string Next = "Next";
    public const string NextMax = "NextMax";
    public const string NextMinMax = "NextMinMax";
    public const string NextDouble = "NextDouble";
    public const string NextSingle = "NextSingle";
    public const string NextInt64 = "NextInt64";
    public const string NextInt64Max = "NextInt64Max";
    public const string NextInt64MinMax = "NextInt64MinMax";
    public const string NextBytes = "NextBytes";
}

/// <summary>
/// One recorded call into the shared <see cref="DeflakeRecorder.TimeProvider"/> stream: a
/// <see cref="EventKinds.GetUtcNow"/>/<see cref="EventKinds.GetTimestamp"/> read, a
/// <see cref="EventKinds.CreateTimer"/> registration, or a <see cref="EventKinds.TimerFired"/> firing of
/// one. Only the properties that apply to <see cref="Kind"/> are set; the rest are null.
/// </summary>
public sealed class TimeProviderEvent
{
    public TimeProviderEvent(int seq, string kind)
    {
        Seq = seq;
        Kind = kind ?? throw new ArgumentNullException(nameof(kind));
    }

    /// <summary>This event's position in the session-wide monotonic sequence, shared with every Random stream.</summary>
    public int Seq { get; }

    /// <summary>One of the <see cref="EventKinds"/> constants.</summary>
    public string Kind { get; }

    /// <summary>The value returned by <see cref="EventKinds.GetUtcNow"/>, or the time a timer fired at.</summary>
    public DateTimeOffset? Utc { get; init; }

    /// <summary>The value returned by <see cref="EventKinds.GetTimestamp"/>.</summary>
    public long? Timestamp { get; init; }

    /// <summary>The <c>dueTime</c> a <see cref="EventKinds.CreateTimer"/> call registered.</summary>
    public TimeSpan? DueTime { get; init; }

    /// <summary>The <c>period</c> a <see cref="EventKinds.CreateTimer"/> call registered.</summary>
    public TimeSpan? Period { get; init; }

    /// <summary>For <see cref="EventKinds.TimerFired"/>, the <see cref="Seq"/> of the <see cref="EventKinds.CreateTimer"/> event it belongs to.</summary>
    public int? TimerSeq { get; init; }
}

/// <summary>
/// One recorded call into a named <see cref="DeflakeRecorder.Random"/> stream. Only the properties that
/// apply to <see cref="Kind"/> are set; the rest are null. The argument fields (<see cref="MinValue"/>,
/// <see cref="MaxValue"/>, ...) exist so replay can detect a call whose argument shape does not match
/// what was recorded, not only a call of the wrong kind.
/// </summary>
public sealed class RandomEvent
{
    public RandomEvent(int seq, string kind)
    {
        Seq = seq;
        Kind = kind ?? throw new ArgumentNullException(nameof(kind));
    }

    /// <summary>This event's position in the session-wide monotonic sequence, shared with the TimeProvider stream.</summary>
    public int Seq { get; }

    /// <summary>One of the <see cref="EventKinds"/> constants.</summary>
    public string Kind { get; }

    public int? IntValue { get; init; }

    public long? Int64Value { get; init; }

    public double? DoubleValue { get; init; }

    public float? SingleValue { get; init; }

    public byte[]? BytesValue { get; init; }

    public int? MinValue { get; init; }

    public int? MaxValue { get; init; }

    public long? MinValueInt64 { get; init; }

    public long? MaxValueInt64 { get; init; }

    /// <summary>The buffer length requested of <see cref="EventKinds.NextBytes"/>.</summary>
    public int? Length { get; init; }
}
