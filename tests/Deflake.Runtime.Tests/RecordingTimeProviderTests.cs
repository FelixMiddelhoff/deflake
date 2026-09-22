using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Deflake.Runtime.Tests;

public class RecordingTimeProviderTests
{
    [Fact]
    public void GetUtcNow_returns_the_inner_value_and_records_a_GetUtcNow_event()
    {
        var inner = new ManualTimeProvider();
        var session = RuntimeSession.StartRecording();
        var provider = new RecordingTimeProvider(session, inner);

        var now = provider.GetUtcNow();

        Assert.Equal(inner.GetUtcNow(), now);
        var evt = Assert.Single(session.TimeProviderEvents);
        Assert.Equal(EventKinds.GetUtcNow, evt.Kind);
        Assert.Equal(now, evt.Utc);
    }

    [Fact]
    public void GetTimestamp_returns_the_inner_value_and_records_a_GetTimestamp_event()
    {
        var inner = new ManualTimeProvider();
        inner.Advance(TimeSpan.FromSeconds(5));
        var session = RuntimeSession.StartRecording();
        var provider = new RecordingTimeProvider(session, inner);

        var timestamp = provider.GetTimestamp();

        Assert.Equal(inner.GetTimestamp(), timestamp);
        var evt = Assert.Single(session.TimeProviderEvents);
        Assert.Equal(EventKinds.GetTimestamp, evt.Kind);
        Assert.Equal(timestamp, evt.Timestamp);
    }

    [Fact]
    public void TimestampFrequency_passes_through_without_recording_an_event()
    {
        var inner = new ManualTimeProvider();
        var session = RuntimeSession.StartRecording();
        var provider = new RecordingTimeProvider(session, inner);

        Assert.Equal(inner.TimestampFrequency, provider.TimestampFrequency);
        Assert.Empty(session.TimeProviderEvents);
    }

    [Fact]
    public void Default_constructor_wraps_TimeProvider_System()
    {
        var session = RuntimeSession.StartRecording();
        var provider = new RecordingTimeProvider(session);

        var before = DateTimeOffset.UtcNow;
        var now = provider.GetUtcNow();
        var after = DateTimeOffset.UtcNow;

        Assert.InRange(now, before.AddSeconds(-1), after.AddSeconds(1));
    }

    [Fact]
    public void CreateTimer_records_the_registration_then_one_event_per_firing()
    {
        var inner = new ManualTimeProvider();
        var session = RuntimeSession.StartRecording();
        var provider = new RecordingTimeProvider(session, inner);
        var fireCount = 0;

        var timer = (ManualTimer)provider.CreateTimer(_ => Interlocked.Increment(ref fireCount), null, TimeSpan.FromMilliseconds(500), Timeout.InfiniteTimeSpan);
        timer.Fire();
        timer.Fire();

        Assert.Equal(2, fireCount);
        var events = session.TimeProviderEvents;
        Assert.Equal(3, events.Count);
        Assert.Equal(EventKinds.CreateTimer, events[0].Kind);
        Assert.Equal(TimeSpan.FromMilliseconds(500), events[0].DueTime);
        Assert.Equal(Timeout.InfiniteTimeSpan, events[0].Period);
        Assert.All(events.Skip(1), e => Assert.Equal(EventKinds.TimerFired, e.Kind));
        Assert.All(events.Skip(1), e => Assert.Equal(events[0].Seq, e.TimerSeq));
    }

    [Fact]
    public void CreateTimer_with_zero_due_time_and_infinite_period_records_a_one_shot_registration()
    {
        var inner = new ManualTimeProvider();
        var session = RuntimeSession.StartRecording();
        var provider = new RecordingTimeProvider(session, inner);

        var timer = (ManualTimer)provider.CreateTimer(_ => { }, null, TimeSpan.Zero, Timeout.InfiniteTimeSpan);
        timer.Fire();

        var createEvent = session.TimeProviderEvents[0];
        Assert.Equal(TimeSpan.Zero, createEvent.DueTime);
        Assert.Equal(Timeout.InfiniteTimeSpan, createEvent.Period);
        Assert.Equal(2, session.TimeProviderEvents.Count);
    }

    [Fact]
    public void CreateTimer_throws_for_a_null_callback()
    {
        var session = RuntimeSession.StartRecording();
        var provider = new RecordingTimeProvider(session, new ManualTimeProvider());

        Assert.Throws<ArgumentNullException>(() => provider.CreateTimer(null!, null, TimeSpan.Zero, Timeout.InfiniteTimeSpan));
    }

    [Fact]
    public void Constructor_rejects_a_null_session_or_a_null_inner_provider()
    {
        var session = RuntimeSession.StartRecording();

        Assert.Throws<ArgumentNullException>(() => new RecordingTimeProvider(null!, new ManualTimeProvider()));
        Assert.Throws<ArgumentNullException>(() => new RecordingTimeProvider(session, null!));
    }

    [Fact]
    public async Task Concurrent_GetUtcNow_calls_from_many_threads_produce_a_unique_monotonic_sequence()
    {
        var inner = new ManualTimeProvider();
        var session = RuntimeSession.StartRecording();
        var provider = new RecordingTimeProvider(session, inner);
        const int threadCount = 8;
        const int perThread = 200;

        var tasks = Enumerable.Range(0, threadCount).Select(_ => Task.Run(() =>
        {
            for (var i = 0; i < perThread; i++)
            {
                provider.GetUtcNow();
            }
        }));

        await Task.WhenAll(tasks);

        var events = session.TimeProviderEvents;
        Assert.Equal(threadCount * perThread, events.Count);
        Assert.Equal(events.Select(e => e.Seq).OrderBy(s => s), events.Select(e => e.Seq));
        Assert.Equal(events.Count, events.Select(e => e.Seq).Distinct().Count());
    }
}
