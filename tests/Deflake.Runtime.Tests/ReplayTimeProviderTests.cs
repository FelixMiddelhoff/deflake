using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Xunit;

namespace Deflake.Runtime.Tests;

public class ReplayTimeProviderTests : IDisposable
{
    private readonly string _tempFile = Path.Combine(Path.GetTempPath(), $"deflake-replay-timeprovider-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        if (File.Exists(_tempFile))
        {
            File.Delete(_tempFile);
        }
    }

    [Fact]
    public void GetUtcNow_and_GetTimestamp_replay_the_recorded_values_in_order_after_a_JSON_round_trip()
    {
        var recordingSession = RuntimeSession.StartRecording();
        var inner = new ManualTimeProvider();
        var recorder = new RecordingTimeProvider(recordingSession, inner);
        var expectedUtc = recorder.GetUtcNow();
        inner.Advance(TimeSpan.FromMinutes(1));
        var expectedTimestamp = recorder.GetTimestamp();
        SessionSerializer.Write(recordingSession, _tempFile);

        var replaySession = SessionSerializer.Read(_tempFile);
        var replay = new ReplayTimeProvider(replaySession);

        Assert.Equal(expectedUtc, replay.GetUtcNow());
        Assert.Equal(expectedTimestamp, replay.GetTimestamp());
    }

    [Fact]
    public void TimestampFrequency_returns_the_recorded_frequency_not_the_local_Stopwatch_frequency()
    {
        // A value that cannot coincidentally equal this machine's own Stopwatch.Frequency, simulating
        // replay on a different machine than the one that recorded the session.
        var replaySession = RuntimeSession.FromSerialized(
            "test",
            DateTimeOffset.UtcNow,
            "some-other-machine",
            "8.0.0",
            timestampFrequency: 123456789,
            timeProviderEvents: Array.Empty<TimeProviderEvent>(),
            randomStreams: Array.Empty<KeyValuePair<string, IReadOnlyList<RandomEvent>>>());

        var replay = new ReplayTimeProvider(replaySession);

        Assert.Equal(123456789, replay.TimestampFrequency);
    }

    [Fact]
    public void GetTimestamp_diverges_when_the_recording_has_GetUtcNow_next()
    {
        var recordingSession = RuntimeSession.StartRecording();
        var recorder = new RecordingTimeProvider(recordingSession, new ManualTimeProvider());
        recorder.GetUtcNow();

        var replay = new ReplayTimeProvider(recordingSession);

        var ex = Assert.Throws<DeflakeReplayDivergenceException>(() => replay.GetTimestamp());
        Assert.Equal(ReplayTimeProvider.StreamId, ex.StreamId);
        Assert.Contains("GetUtcNow", ex.Expected);
        Assert.Contains("GetTimestamp", ex.Actual);
    }

    [Fact]
    public void Calling_the_stream_after_it_is_exhausted_diverges()
    {
        var recordingSession = RuntimeSession.StartRecording();
        var recorder = new RecordingTimeProvider(recordingSession, new ManualTimeProvider());
        recorder.GetUtcNow();
        var replay = new ReplayTimeProvider(recordingSession);
        replay.GetUtcNow();

        var ex = Assert.Throws<DeflakeReplayDivergenceException>(() => replay.GetUtcNow());
        Assert.Contains("stream exhausted", ex.Expected);
    }

    [Fact]
    public void CreateTimer_fires_the_callback_exactly_as_many_times_as_the_recording_did()
    {
        var recordingSession = RuntimeSession.StartRecording();
        var recordingInner = new ManualTimeProvider();
        var recorder = new RecordingTimeProvider(recordingSession, recordingInner);
        var recordingTimer = (ManualTimer)recorder.CreateTimer(_ => { }, null, TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(1));
        recordingTimer.Fire();
        recordingTimer.Fire();
        recordingTimer.Fire();

        var replay = new ReplayTimeProvider(recordingSession);
        var countdown = new CountdownEvent(3);
        using var replayTimer = replay.CreateTimer(_ => countdown.Signal(), null, TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(1));

        var completed = countdown.Wait(TimeSpan.FromSeconds(10));

        Assert.True(completed, "the replay timer must fire exactly as many times as were recorded, without waiting for the real dueTime.");
        Assert.Equal(0, countdown.CurrentCount);
    }

    [Fact]
    public void CreateTimer_registered_but_never_fired_during_recording_never_fires_on_replay()
    {
        var recordingSession = RuntimeSession.StartRecording();
        var recorder = new RecordingTimeProvider(recordingSession, new ManualTimeProvider());
        recorder.CreateTimer(_ => { }, null, TimeSpan.FromSeconds(1), Timeout.InfiniteTimeSpan);

        var replay = new ReplayTimeProvider(recordingSession);
        var fired = false;
        using var replayTimer = replay.CreateTimer(_ => fired = true, null, TimeSpan.FromSeconds(1), Timeout.InfiniteTimeSpan);

        Thread.Sleep(50);

        Assert.False(fired);
    }

    [Fact]
    public void CreateTimer_throws_for_a_null_callback()
    {
        var session = RuntimeSession.StartRecording();
        var replay = new ReplayTimeProvider(session);

        Assert.Throws<ArgumentNullException>(() => replay.CreateTimer(null!, null, TimeSpan.Zero, Timeout.InfiniteTimeSpan));
    }

    [Fact]
    public void Constructor_rejects_a_null_session()
    {
        Assert.Throws<ArgumentNullException>(() => new ReplayTimeProvider(null!));
    }
}
