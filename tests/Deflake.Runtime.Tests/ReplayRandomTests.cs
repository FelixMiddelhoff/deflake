using System;
using Xunit;

namespace Deflake.Runtime.Tests;

public class ReplayRandomTests
{
    private static RuntimeSession Record(Action<RecordingRandom> record)
    {
        var session = RuntimeSession.StartRecording();
        var recorder = new RecordingRandom(session, "stream", new Random(7));
        record(recorder);
        return session;
    }

    [Fact]
    public void Next_replays_the_exact_recorded_value()
    {
        var expected = 0;
        var session = Record(r => expected = r.Next());

        var replay = new ReplayRandom(session, "stream");

        Assert.Equal(expected, replay.Next());
    }

    [Fact]
    public void Next_with_max_replays_the_value_when_the_bound_matches()
    {
        var expected = 0;
        var session = Record(r => expected = r.Next(100));

        var replay = new ReplayRandom(session, "stream");

        Assert.Equal(expected, replay.Next(100));
    }

    [Fact]
    public void Next_with_max_diverges_when_the_bound_does_not_match()
    {
        var session = Record(r => r.Next(100));
        var replay = new ReplayRandom(session, "stream");

        var ex = Assert.Throws<DeflakeReplayDivergenceException>(() => replay.Next(50));
        Assert.Equal("stream", ex.StreamId);
    }

    [Fact]
    public void Next_with_min_and_max_diverges_when_only_the_minimum_differs()
    {
        var session = Record(r => r.Next(10, 20));
        var replay = new ReplayRandom(session, "stream");

        Assert.Throws<DeflakeReplayDivergenceException>(() => replay.Next(5, 20));
    }

    [Fact]
    public void Requesting_a_different_overload_than_was_recorded_diverges()
    {
        var session = Record(r => r.Next());
        var replay = new ReplayRandom(session, "stream");

        var ex = Assert.Throws<DeflakeReplayDivergenceException>(() => replay.NextDouble());
        Assert.Contains("Next", ex.Expected);
        Assert.Contains("NextDouble", ex.Actual);
    }

    [Fact]
    public void NextInt64_overloads_replay_faithfully()
    {
        long a = 0, b = 0, c = 0;
        var session = Record(r =>
        {
            a = r.NextInt64();
            b = r.NextInt64(1000);
            c = r.NextInt64(-50, 50);
        });
        var replay = new ReplayRandom(session, "stream");

        Assert.Equal(a, replay.NextInt64());
        Assert.Equal(b, replay.NextInt64(1000));
        Assert.Equal(c, replay.NextInt64(-50, 50));
    }

    [Fact]
    public void NextInt64_with_max_diverges_when_the_bound_does_not_match()
    {
        var session = Record(r => r.NextInt64(1000));
        var replay = new ReplayRandom(session, "stream");

        Assert.Throws<DeflakeReplayDivergenceException>(() => replay.NextInt64(999));
    }

    [Fact]
    public void NextDouble_and_NextSingle_replay_faithfully()
    {
        double d = 0;
        float f = 0;
        var session = Record(r =>
        {
            d = r.NextDouble();
            f = r.NextSingle();
        });
        var replay = new ReplayRandom(session, "stream");

        Assert.Equal(d, replay.NextDouble());
        Assert.Equal(f, replay.NextSingle());
    }

    [Fact]
    public void NextBytes_byte_array_overload_replays_the_exact_recorded_bytes()
    {
        var expected = Array.Empty<byte>();
        var session = Record(r =>
        {
            var buffer = new byte[16];
            r.NextBytes(buffer);
            expected = buffer;
        });
        var replay = new ReplayRandom(session, "stream");
        var target = new byte[16];

        replay.NextBytes(target);

        Assert.Equal(expected, target);
    }

    [Fact]
    public void NextBytes_span_overload_replays_the_exact_recorded_bytes()
    {
        var expected = Array.Empty<byte>();
        var session = Record(r =>
        {
            Span<byte> buffer = stackalloc byte[8];
            r.NextBytes(buffer);
            expected = buffer.ToArray();
        });
        var replay = new ReplayRandom(session, "stream");
        Span<byte> target = stackalloc byte[8];

        replay.NextBytes(target);

        Assert.Equal(expected, target.ToArray());
    }

    [Fact]
    public void NextBytes_diverges_when_the_requested_length_does_not_match_the_recording()
    {
        var session = Record(r => r.NextBytes(new byte[16]));
        var replay = new ReplayRandom(session, "stream");

        Assert.Throws<DeflakeReplayDivergenceException>(() => replay.NextBytes(new byte[8]));
    }

    [Fact]
    public void NextBytes_null_array_throws_ArgumentNullException()
    {
        var session = Record(r => r.NextBytes(new byte[4]));
        var replay = new ReplayRandom(session, "stream");

        Assert.Throws<ArgumentNullException>(() => replay.NextBytes((byte[])null!));
    }

    [Fact]
    public void Calling_a_stream_that_was_never_recorded_diverges_immediately()
    {
        var session = RuntimeSession.StartRecording();
        var replay = new ReplayRandom(session, "never-recorded");

        var ex = Assert.Throws<DeflakeReplayDivergenceException>(() => replay.Next());
        Assert.Contains("stream exhausted", ex.Expected);
    }

    [Fact]
    public void Calling_the_stream_after_it_is_exhausted_diverges()
    {
        var session = Record(r => r.Next());
        var replay = new ReplayRandom(session, "stream");
        replay.Next();

        Assert.Throws<DeflakeReplayDivergenceException>(() => replay.Next());
    }

    [Fact]
    public void Two_named_streams_replay_independently_even_when_read_in_the_opposite_order_from_how_they_were_recorded()
    {
        var session = RuntimeSession.StartRecording();
        var orderingRecorder = new RecordingRandom(session, "ordering", new Random(1));
        var payloadRecorder = new RecordingRandom(session, "payload", new Random(2));
        var orderingExpected = orderingRecorder.Next();
        var payloadExpected = payloadRecorder.NextDouble();

        var orderingReplay = new ReplayRandom(session, "ordering");
        var payloadReplay = new ReplayRandom(session, "payload");

        Assert.Equal(payloadExpected, payloadReplay.NextDouble());
        Assert.Equal(orderingExpected, orderingReplay.Next());
    }

    [Fact]
    public void A_null_or_empty_stream_id_is_rejected()
    {
        var session = RuntimeSession.StartRecording();

        Assert.Throws<ArgumentException>(() => new ReplayRandom(session, ""));
        Assert.Throws<ArgumentException>(() => new ReplayRandom(session, null!));
    }

    [Fact]
    public void A_null_session_is_rejected()
    {
        Assert.Throws<ArgumentNullException>(() => new ReplayRandom(null!, "stream"));
    }
}
