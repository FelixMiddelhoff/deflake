using System;

namespace Deflake.Core.Tests;

/// <summary>
/// A clock that only moves when a test moves it. Experiment budgets are wall-clock budgets, and a
/// test that waited for real time would be both slow and flaky — the two things this tool exists to
/// remove.
/// </summary>
internal sealed class ManualTimeProvider : TimeProvider
{
    private DateTimeOffset _now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => _now;

    /// <summary>One tick per <see cref="TimeSpan.TicksPerSecond"/>, so a timestamp is simply a tick count.</summary>
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override long GetTimestamp() => _now.UtcTicks;

    public void Advance(TimeSpan by) => _now = _now.Add(by);
}
