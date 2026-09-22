using System;
using System.Threading;
using System.Threading.Tasks;

namespace Deflake.Runtime.Tests;

/// <summary>
/// A fully controllable "inner" clock used only to drive <see cref="RecordingTimeProvider"/> in tests:
/// <see cref="GetUtcNow"/>/<see cref="GetTimestamp"/> return a fixed, test-advanced value, and
/// <see cref="CreateTimer"/> hands back a <see cref="ManualTimer"/> the test fires explicitly - so
/// recording tests need no real waiting and stay deterministic.
/// </summary>
internal sealed class ManualTimeProvider : TimeProvider
{
    private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => _now;

    /// <summary>One tick per <see cref="TimeSpan.TicksPerSecond"/>, so a timestamp is simply a tick count.</summary>
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override long GetTimestamp() => _now.UtcTicks;

    public void Advance(TimeSpan by) => _now = _now.Add(by);

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
        new ManualTimer(callback, state);
}

/// <summary>An <see cref="ITimer"/> that only fires when the test calls <see cref="Fire"/> - never on a real clock.</summary>
internal sealed class ManualTimer : ITimer
{
    private readonly TimerCallback _callback;
    private readonly object? _state;

    public ManualTimer(TimerCallback callback, object? state)
    {
        _callback = callback;
        _state = state;
    }

    /// <summary>Invokes the registered callback synchronously, as if the timer had just ticked.</summary>
    public void Fire() => _callback(_state);

    public bool Change(TimeSpan dueTime, TimeSpan period) => true;

    public void Dispose()
    {
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
