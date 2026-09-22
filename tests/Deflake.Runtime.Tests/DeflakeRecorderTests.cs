using System;
using System.IO;
using Xunit;

namespace Deflake.Runtime.Tests;

/// <summary>
/// Exercises <see cref="DeflakeRecorder"/>'s env-var-driven branching (passthrough, recording, replay,
/// both-set) via <see cref="DeflakeRecorderHarness"/>, which resets the class's cached static state
/// between tests - the real class only ever initializes once per process by design (a real test run is
/// exactly one process), so these tests need that help to exercise more than one branch. All of them live
/// in this one class, which xUnit runs sequentially by default, so they never interleave with each other.
/// </summary>
public class DeflakeRecorderTests : IDisposable
{
    private readonly string _recordPath = Path.Combine(Path.GetTempPath(), $"deflake-record-{Guid.NewGuid():N}.json");
    private readonly string _replayPath = Path.Combine(Path.GetTempPath(), $"deflake-replay-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        DeflakeRecorderHarness.Reset();
        Environment.SetEnvironmentVariable("DEFLAKE_RECORD", null);
        Environment.SetEnvironmentVariable("DEFLAKE_REPLAY", null);

        foreach (var path in new[] { _recordPath, _replayPath })
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void With_neither_environment_variable_set_the_recorder_is_a_transparent_passthrough()
    {
        using (DeflakeRecorderHarness.WithEnvironment(recordPath: null, replayPath: null))
        {
            Assert.False(DeflakeRecorder.IsRecording);
            Assert.False(DeflakeRecorder.IsReplaying);
            Assert.Null(DeflakeRecorder.SessionPath);
            Assert.Same(TimeProvider.System, DeflakeRecorder.TimeProvider);
            Assert.Same(Random.Shared, DeflakeRecorder.Random());

            DeflakeRecorder.Flush();

            Assert.False(File.Exists(_recordPath));
            Assert.False(File.Exists(_replayPath));
        }
    }

    [Fact]
    public void When_recording_the_recorder_hands_out_a_RecordingTimeProvider_and_RecordingRandom()
    {
        using (DeflakeRecorderHarness.WithEnvironment(recordPath: _recordPath, replayPath: null))
        {
            Assert.True(DeflakeRecorder.IsRecording);
            Assert.False(DeflakeRecorder.IsReplaying);
            Assert.Equal(_recordPath, DeflakeRecorder.SessionPath);
            Assert.IsType<RecordingTimeProvider>(DeflakeRecorder.TimeProvider);
            Assert.IsType<RecordingRandom>(DeflakeRecorder.Random("stream"));
        }
    }

    [Fact]
    public void Flush_writes_the_recorded_session_to_the_record_path()
    {
        using (DeflakeRecorderHarness.WithEnvironment(recordPath: _recordPath, replayPath: null))
        {
            DeflakeRecorder.TimeProvider.GetUtcNow();
            DeflakeRecorder.Random("stream").Next();

            DeflakeRecorder.Flush();

            Assert.True(File.Exists(_recordPath));
            var session = SessionSerializer.Read(_recordPath);
            Assert.Single(session.TimeProviderEvents);
            Assert.Single(session.RandomStreams["stream"]);
        }
    }

    [Fact]
    public void Flush_is_a_no_op_while_replaying()
    {
        var seedSession = RuntimeSession.StartRecording();
        SessionSerializer.Write(seedSession, _replayPath);

        using (DeflakeRecorderHarness.WithEnvironment(recordPath: null, replayPath: _replayPath))
        {
            _ = DeflakeRecorder.TimeProvider;

            DeflakeRecorder.Flush();

            Assert.False(File.Exists(_recordPath));
        }
    }

    [Fact]
    public void When_replaying_the_recorder_reproduces_the_recorded_values()
    {
        var seedSession = RuntimeSession.StartRecording();
        var seedTimeProvider = new RecordingTimeProvider(seedSession, new ManualTimeProvider());
        var recordedUtc = seedTimeProvider.GetUtcNow();
        var seedRandom = new RecordingRandom(seedSession, "stream", new Random(3));
        var recordedValue = seedRandom.Next();
        SessionSerializer.Write(seedSession, _replayPath);

        using (DeflakeRecorderHarness.WithEnvironment(recordPath: null, replayPath: _replayPath))
        {
            Assert.False(DeflakeRecorder.IsRecording);
            Assert.True(DeflakeRecorder.IsReplaying);
            Assert.Equal(_replayPath, DeflakeRecorder.SessionPath);
            Assert.IsType<ReplayTimeProvider>(DeflakeRecorder.TimeProvider);
            Assert.IsType<ReplayRandom>(DeflakeRecorder.Random("stream"));

            Assert.Equal(recordedUtc, DeflakeRecorder.TimeProvider.GetUtcNow());
            Assert.Equal(recordedValue, DeflakeRecorder.Random("stream").Next());
        }
    }

    [Fact]
    public void When_replaying_a_divergent_call_throws_immediately()
    {
        var seedSession = RuntimeSession.StartRecording();
        new RecordingTimeProvider(seedSession, new ManualTimeProvider()).GetUtcNow();
        SessionSerializer.Write(seedSession, _replayPath);

        using (DeflakeRecorderHarness.WithEnvironment(recordPath: null, replayPath: _replayPath))
        {
            Assert.Throws<DeflakeReplayDivergenceException>(() => DeflakeRecorder.TimeProvider.GetTimestamp());
        }
    }

    [Fact]
    public void With_both_environment_variables_set_the_first_use_throws()
    {
        using (DeflakeRecorderHarness.WithEnvironment(recordPath: _recordPath, replayPath: _replayPath))
        {
            Assert.Throws<InvalidOperationException>(() => DeflakeRecorder.TimeProvider);
        }
    }

    [Fact]
    public void Random_with_the_same_stream_id_returns_the_same_instance()
    {
        using (DeflakeRecorderHarness.WithEnvironment(recordPath: _recordPath, replayPath: null))
        {
            var first = DeflakeRecorder.Random("payload");
            var second = DeflakeRecorder.Random("payload");
            var other = DeflakeRecorder.Random("ordering");

            Assert.Same(first, second);
            Assert.NotSame(first, other);
        }
    }

    [Fact]
    public void Random_defaults_the_stream_id_to_the_calling_members_name()
    {
        using (DeflakeRecorderHarness.WithEnvironment(recordPath: _recordPath, replayPath: null))
        {
            CallDefaultStream();

            DeflakeRecorder.Flush();
            var session = SessionSerializer.Read(_recordPath);
            Assert.True(session.RandomStreams.TryGetValue(nameof(CallDefaultStream), out var events));
            Assert.Single(events!);
        }
    }

    private static void CallDefaultStream() => DeflakeRecorder.Random().Next();
}
