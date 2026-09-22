using System;
using System.Threading;
using System.Threading.Tasks;

namespace Deflake.Runtime;

/// <summary>
/// A <see cref="TimeProvider"/> that hands back exactly the values a <see cref="RecordingTimeProvider"/>
/// recorded, in the order they were recorded, instead of consulting the real clock. Any call whose kind
/// or position does not match what was recorded throws <see cref="DeflakeReplayDivergenceException"/>
/// immediately, rather than guessing or falling back to a real value.
/// </summary>
public sealed class ReplayTimeProvider : TimeProvider
{
    /// <summary>The stream id <see cref="DeflakeReplayDivergenceException.StreamId"/> carries for TimeProvider divergences.</summary>
    public const string StreamId = "TimeProvider";

    private readonly RuntimeSession _session;
    private readonly object _lock = new();
    private int _cursor;

    public ReplayTimeProvider(RuntimeSession session)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
    }

    /// <summary>
    /// The frequency recorded at session start, not this machine's <see cref="System.Diagnostics.Stopwatch.Frequency"/> -
    /// see <see cref="RuntimeSession.TimestampFrequency"/>.
    /// </summary>
    public override long TimestampFrequency => _session.TimestampFrequency;

    public override DateTimeOffset GetUtcNow()
    {
        var evt = PopNext(EventKinds.GetUtcNow, "GetUtcNow()");
        if (evt.Utc is not { } value)
        {
            throw MissingValue(evt, "GetUtcNow()");
        }

        return value;
    }

    public override long GetTimestamp()
    {
        var evt = PopNext(EventKinds.GetTimestamp, "GetTimestamp()");
        if (evt.Timestamp is not { } value)
        {
            throw MissingValue(evt, "GetTimestamp()");
        }

        return value;
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        if (callback is null)
        {
            throw new ArgumentNullException(nameof(callback));
        }

        var createEvent = PopNext(EventKinds.CreateTimer, $"CreateTimer(dueTime: {dueTime}, period: {period})");
        var fireCount = CountRecordedFirings(createEvent.Seq);

        // v1 simplification, the same scope decision the design makes for Task scheduling: replay never
        // sleeps for virtual time (per the design's explicit requirement), and this library does not
        // implement a full virtual-clock scheduler. What is reproduced exactly is *how many times* the
        // callback fires and in what relative order versus the rest of this stream (via the recorded
        // GetUtcNow/GetTimestamp calls the callback itself makes); *when* it fires in wall-clock terms is
        // not virtualized - the callback runs back-to-back, immediately. A caller that asserts on real
        // elapsed wall-clock time around a timer (rather than on the TimeProvider's own values) is not a
        // scenario this replay mechanism claims to reproduce.
        return new ReplayTimer(callback, state, fireCount);
    }

    private int CountRecordedFirings(int createSeq)
    {
        var count = 0;
        foreach (var evt in _session.TimeProviderEvents)
        {
            if (evt.Kind == EventKinds.TimerFired && evt.TimerSeq == createSeq)
            {
                count++;
            }
        }

        return count;
    }

    private TimeProviderEvent PopNext(string expectedKind, string requested)
    {
        lock (_lock)
        {
            var events = _session.TimeProviderEvents;
            if (_cursor >= events.Count)
            {
                throw new DeflakeReplayDivergenceException(StreamId, expected: "no more calls (stream exhausted)", actual: requested);
            }

            var evt = events[_cursor];
            _cursor++;

            if (evt.Kind != expectedKind)
            {
                throw new DeflakeReplayDivergenceException(StreamId, expected: $"{evt.Kind} (seq {evt.Seq})", actual: requested);
            }

            return evt;
        }
    }

    private static DeflakeReplayDivergenceException MissingValue(TimeProviderEvent evt, string requested)
    {
        return new DeflakeReplayDivergenceException(
            StreamId,
            expected: $"{evt.Kind} recorded without the expected payload (seq {evt.Seq})",
            actual: requested);
    }
}

/// <summary>
/// The <see cref="ITimer"/> <see cref="ReplayTimeProvider.CreateTimer"/> returns: fires the callback
/// exactly as many times as the recording did, back-to-back and without any real delay, then stops.
/// </summary>
internal sealed class ReplayTimer : ITimer
{
    private readonly TimerCallback _callback;
    private readonly object? _state;
    private readonly System.Threading.Timer _inner;
    private int _remaining;

    public ReplayTimer(TimerCallback callback, object? state, int fireCount)
    {
        _callback = callback;
        _state = state;
        _remaining = fireCount;

        var dueTime = fireCount > 0 ? TimeSpan.Zero : Timeout.InfiniteTimeSpan;
        var tickPeriod = fireCount > 1 ? TimeSpan.FromMilliseconds(1) : Timeout.InfiniteTimeSpan;
        _inner = new System.Threading.Timer(OnTick, null, dueTime, tickPeriod);
    }

    public bool Change(TimeSpan dueTime, TimeSpan period) => _inner.Change(dueTime, period);

    public void Dispose() => _inner.Dispose();

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    private void OnTick(object? ignored)
    {
        if (Interlocked.Decrement(ref _remaining) < 0)
        {
            _inner.Change(Timeout.Infinite, Timeout.Infinite);
            return;
        }

        _callback(_state);

        if (Volatile.Read(ref _remaining) <= 0)
        {
            _inner.Change(Timeout.Infinite, Timeout.Infinite);
        }
    }
}
