using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Deflake.Runtime.Tests;

public class RecordingRandomTests
{
    [Fact]
    public void Next_records_the_value_it_returns()
    {
        var session = RuntimeSession.StartRecording();
        var random = new RecordingRandom(session, "stream", new Random(42));

        var value = random.Next();

        var evt = Assert.Single(session.RandomStreams["stream"]);
        Assert.Equal(EventKinds.Next, evt.Kind);
        Assert.Equal(value, evt.IntValue);
    }

    [Fact]
    public void Next_with_max_records_the_bound_and_the_value()
    {
        var session = RuntimeSession.StartRecording();
        var random = new RecordingRandom(session, "stream", new Random(1));

        var value = random.Next(50);

        var evt = Assert.Single(session.RandomStreams["stream"]);
        Assert.Equal(EventKinds.NextMax, evt.Kind);
        Assert.Equal(value, evt.IntValue);
        Assert.Equal(50, evt.MaxValue);
        Assert.InRange(value, 0, 49);
    }

    [Fact]
    public void Next_with_min_and_max_records_both_bounds_and_the_value()
    {
        var session = RuntimeSession.StartRecording();
        var random = new RecordingRandom(session, "stream", new Random(1));

        var value = random.Next(10, 20);

        var evt = Assert.Single(session.RandomStreams["stream"]);
        Assert.Equal(EventKinds.NextMinMax, evt.Kind);
        Assert.Equal(10, evt.MinValue);
        Assert.Equal(20, evt.MaxValue);
        Assert.InRange(value, 10, 19);
    }

    [Fact]
    public void NextInt64_overloads_record_their_bounds_and_values()
    {
        var session = RuntimeSession.StartRecording();
        var random = new RecordingRandom(session, "stream", new Random(1));

        random.NextInt64();
        random.NextInt64(1000L);
        random.NextInt64(-100L, 100L);

        var events = session.RandomStreams["stream"];
        Assert.Equal(new[] { EventKinds.NextInt64, EventKinds.NextInt64Max, EventKinds.NextInt64MinMax }, events.Select(e => e.Kind));
        Assert.Equal(1000L, events[1].MaxValueInt64);
        Assert.Equal(-100L, events[2].MinValueInt64);
        Assert.Equal(100L, events[2].MaxValueInt64);
    }

    [Fact]
    public void NextDouble_and_NextSingle_record_their_values()
    {
        var session = RuntimeSession.StartRecording();
        var random = new RecordingRandom(session, "stream", new Random(1));

        var d = random.NextDouble();
        var f = random.NextSingle();

        var events = session.RandomStreams["stream"];
        Assert.Equal(EventKinds.NextDouble, events[0].Kind);
        Assert.Equal(d, events[0].DoubleValue);
        Assert.Equal(EventKinds.NextSingle, events[1].Kind);
        Assert.Equal(f, events[1].SingleValue);
    }

    [Fact]
    public void NextBytes_byte_array_overload_records_the_exact_bytes_and_length()
    {
        var session = RuntimeSession.StartRecording();
        var random = new RecordingRandom(session, "stream", new Random(1));
        var buffer = new byte[16];

        random.NextBytes(buffer);

        var evt = Assert.Single(session.RandomStreams["stream"]);
        Assert.Equal(EventKinds.NextBytes, evt.Kind);
        Assert.Equal(buffer, evt.BytesValue);
        Assert.Equal(16, evt.Length);
    }

    [Fact]
    public void NextBytes_span_overload_records_the_exact_bytes_and_length()
    {
        var session = RuntimeSession.StartRecording();
        var random = new RecordingRandom(session, "stream", new Random(1));
        Span<byte> buffer = stackalloc byte[8];

        random.NextBytes(buffer);

        var evt = Assert.Single(session.RandomStreams["stream"]);
        Assert.Equal(8, evt.Length);
        Assert.Equal(buffer.ToArray(), evt.BytesValue);
    }

    [Fact]
    public void NextBytes_with_an_empty_buffer_records_a_zero_length_event()
    {
        var session = RuntimeSession.StartRecording();
        var random = new RecordingRandom(session, "stream", new Random(1));

        random.NextBytes(Array.Empty<byte>());

        var evt = Assert.Single(session.RandomStreams["stream"]);
        Assert.Equal(0, evt.Length);
        Assert.Empty(evt.BytesValue!);
    }

    [Fact]
    public void NextBytes_null_array_throws_ArgumentNullException()
    {
        var session = RuntimeSession.StartRecording();
        var random = new RecordingRandom(session, "stream", new Random(1));

        Assert.Throws<ArgumentNullException>(() => random.NextBytes((byte[])null!));
    }

    [Fact]
    public void Two_named_streams_record_independently()
    {
        var session = RuntimeSession.StartRecording();
        var ordering = new RecordingRandom(session, "ordering", new Random(1));
        var payload = new RecordingRandom(session, "payload", new Random(2));

        ordering.Next();
        payload.NextDouble();
        ordering.Next();

        Assert.Equal(2, session.RandomStreams["ordering"].Count);
        Assert.Single(session.RandomStreams["payload"]);
    }

    [Fact]
    public void A_null_or_empty_stream_id_is_rejected()
    {
        var session = RuntimeSession.StartRecording();

        Assert.Throws<ArgumentException>(() => new RecordingRandom(session, ""));
        Assert.Throws<ArgumentException>(() => new RecordingRandom(session, null!));
    }

    [Fact]
    public void Constructor_rejects_a_null_session_or_a_null_inner_random()
    {
        var session = RuntimeSession.StartRecording();

        Assert.Throws<ArgumentNullException>(() => new RecordingRandom(null!, "stream"));
        Assert.Throws<ArgumentNullException>(() => new RecordingRandom(session, "stream", null!));
    }

    [Theory]
    [InlineData(int.MaxValue)]
    [InlineData(1)]
    public void Next_with_extreme_bounds_is_recorded_faithfully(int maxValue)
    {
        var session = RuntimeSession.StartRecording();
        var random = new RecordingRandom(session, "stream", new Random(1));

        var value = random.Next(maxValue);

        var evt = Assert.Single(session.RandomStreams["stream"]);
        Assert.Equal(maxValue, evt.MaxValue);
        Assert.Equal(value, evt.IntValue);
    }

    [Fact]
    public void NextInt64_with_extreme_bounds_is_recorded_faithfully()
    {
        var session = RuntimeSession.StartRecording();
        var random = new RecordingRandom(session, "stream", new Random(1));

        var value = random.NextInt64(long.MinValue / 2, long.MaxValue / 2);

        var evt = Assert.Single(session.RandomStreams["stream"]);
        Assert.Equal(long.MinValue / 2, evt.MinValueInt64);
        Assert.Equal(long.MaxValue / 2, evt.MaxValueInt64);
        Assert.Equal(value, evt.Int64Value);
    }

    [Fact]
    public async Task Concurrent_calls_on_the_same_stream_from_many_threads_produce_a_unique_monotonic_sequence()
    {
        var session = RuntimeSession.StartRecording();
        var random = new RecordingRandom(session, "stream", new Random(1));
        const int threadCount = 8;
        const int perThread = 200;

        var tasks = Enumerable.Range(0, threadCount).Select(_ => Task.Run(() =>
        {
            for (var i = 0; i < perThread; i++)
            {
                random.Next();
            }
        }));

        await Task.WhenAll(tasks);

        var events = session.RandomStreams["stream"];
        Assert.Equal(threadCount * perThread, events.Count);
        Assert.Equal(events.Select(e => e.Seq).OrderBy(s => s), events.Select(e => e.Seq));
        Assert.Equal(events.Count, events.Select(e => e.Seq).Distinct().Count());
    }

    [Fact]
    public async Task Concurrent_calls_on_two_different_streams_never_cross()
    {
        var session = RuntimeSession.StartRecording();
        var ordering = new RecordingRandom(session, "ordering", new Random(1));
        var payload = new RecordingRandom(session, "payload", new Random(2));
        const int perStream = 300;

        await Task.WhenAll(
            Task.Run(() => { for (var i = 0; i < perStream; i++) { ordering.Next(); } }),
            Task.Run(() => { for (var i = 0; i < perStream; i++) { payload.NextDouble(); } }));

        Assert.Equal(perStream, session.RandomStreams["ordering"].Count);
        Assert.Equal(perStream, session.RandomStreams["payload"].Count);
        Assert.All(session.RandomStreams["ordering"], e => Assert.Equal(EventKinds.Next, e.Kind));
        Assert.All(session.RandomStreams["payload"], e => Assert.Equal(EventKinds.NextDouble, e.Kind));
    }
}
