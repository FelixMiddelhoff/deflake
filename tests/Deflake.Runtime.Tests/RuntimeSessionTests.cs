using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Deflake.Runtime.Tests;

public class RuntimeSessionTests
{
    [Fact]
    public void NextSequence_starts_at_zero_and_increments_by_one()
    {
        var session = RuntimeSession.StartRecording();

        Assert.Equal(0, session.NextSequence());
        Assert.Equal(1, session.NextSequence());
        Assert.Equal(2, session.NextSequence());
    }

    [Fact]
    public async Task NextSequence_is_unique_and_gapless_under_concurrent_callers()
    {
        var session = RuntimeSession.StartRecording();
        const int threadCount = 8;
        const int perThread = 500;
        var seen = new ConcurrentBag<int>();

        var tasks = Enumerable.Range(0, threadCount).Select(_ => Task.Run(() =>
        {
            for (var i = 0; i < perThread; i++)
            {
                seen.Add(session.NextSequence());
            }
        }));

        await Task.WhenAll(tasks);

        var expected = Enumerable.Range(0, threadCount * perThread).ToHashSet();
        Assert.Equal(expected.Count, seen.Count);
        Assert.True(expected.SetEquals(seen), "every sequence number 0..N-1 must appear exactly once, with no gaps or duplicates.");
    }

    [Fact]
    public void RecordTimeProviderEvent_appends_in_Seq_order()
    {
        var session = RuntimeSession.StartRecording();

        var first = session.RecordTimeProviderEvent(seq => new TimeProviderEvent(seq, EventKinds.GetUtcNow) { Utc = DateTimeOffset.UnixEpoch });
        var second = session.RecordTimeProviderEvent(seq => new TimeProviderEvent(seq, EventKinds.GetTimestamp) { Timestamp = 42 });

        Assert.True(first.Seq < second.Seq);
        Assert.Equal(new[] { first.Seq, second.Seq }, session.TimeProviderEvents.Select(e => e.Seq));
    }

    [Fact]
    public void GetOrAddRandomStream_returns_the_same_instance_for_the_same_id_and_a_different_instance_for_another_id()
    {
        var session = RuntimeSession.StartRecording();

        var first = session.GetOrAddRandomStream("payload");
        var second = session.GetOrAddRandomStream("payload");
        var other = session.GetOrAddRandomStream("ordering");

        Assert.Same(first, second);
        Assert.NotSame(first, other);
    }

    [Fact]
    public void StartRecording_captures_the_test_name_machine_dotnet_version_and_stopwatch_frequency()
    {
        var session = RuntimeSession.StartRecording("MyApp.Tests.Foo");

        Assert.Equal("MyApp.Tests.Foo", session.TestName);
        Assert.Equal(Environment.MachineName, session.Machine);
        Assert.Equal(Environment.Version.ToString(), session.DotnetVersion);
        Assert.Equal(Stopwatch.Frequency, session.TimestampFrequency);
    }

    [Fact]
    public void StartRecording_with_no_test_name_defaults_to_empty()
    {
        var session = RuntimeSession.StartRecording();

        Assert.Equal(string.Empty, session.TestName);
    }
}
