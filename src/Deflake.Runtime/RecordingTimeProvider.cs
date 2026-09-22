using System;
using System.Threading;

namespace Deflake.Runtime;

/// <summary>
/// A <see cref="TimeProvider"/> that behaves exactly like an inner clock (<see cref="TimeProvider.System"/>
/// unless a different one is supplied) while recording every value it hands out - and every timer
/// registration and firing - into a <see cref="RuntimeSession"/>, so a later run can replay the exact
/// same sequence of values.
/// </summary>
public sealed class RecordingTimeProvider : TimeProvider
{
    private readonly TimeProvider _inner;
    private readonly RuntimeSession _session;

    public RecordingTimeProvider(RuntimeSession session)
        : this(session, TimeProvider.System)
    {
    }

    public RecordingTimeProvider(RuntimeSession session, TimeProvider inner)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    public override DateTimeOffset GetUtcNow()
    {
        var now = _inner.GetUtcNow();
        _session.RecordTimeProviderEvent(seq => new TimeProviderEvent(seq, EventKinds.GetUtcNow) { Utc = now });
        return now;
    }

    public override long GetTimestamp()
    {
        var timestamp = _inner.GetTimestamp();
        _session.RecordTimeProviderEvent(seq => new TimeProviderEvent(seq, EventKinds.GetTimestamp) { Timestamp = timestamp });
        return timestamp;
    }

    public override long TimestampFrequency => _inner.TimestampFrequency;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        if (callback is null)
        {
            throw new ArgumentNullException(nameof(callback));
        }

        var createEvent = _session.RecordTimeProviderEvent(
            seq => new TimeProviderEvent(seq, EventKinds.CreateTimer) { DueTime = dueTime, Period = period });

        // Recorded for diagnostics only in v1 (the design's Task-scheduling scope decision extends to
        // timer firing order too): the real inner provider still drives the actual timing here, so a
        // recording run behaves exactly like an unrecorded one. Only ReplayTimeProvider changes what a
        // timer actually does.
        TimerCallback recordingCallback = callbackState =>
        {
            var firedAt = _inner.GetUtcNow();
            _session.RecordTimeProviderEvent(
                seq => new TimeProviderEvent(seq, EventKinds.TimerFired) { TimerSeq = createEvent.Seq, Utc = firedAt });
            callback(callbackState);
        };

        return _inner.CreateTimer(recordingCallback, state, dueTime, period);
    }
}
