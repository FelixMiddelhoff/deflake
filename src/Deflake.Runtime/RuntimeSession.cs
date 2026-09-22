using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace Deflake.Runtime;

/// <summary>
/// The in-memory state one recording or replay run holds: the single shared TimeProvider stream, and
/// the named Random streams. Every event, in every stream, is stamped with a number from one
/// process-wide monotonic counter (<see cref="NextSequence"/>) assigned atomically with the event's
/// append to its own stream, so within any one stream, list order and <c>Seq</c> order always agree —
/// that ordering is what replay relies on to hand back the Nth recorded value to the Nth call, whichever
/// thread happens to make it, regardless of how differently threads race between the recording run and
/// the replay run.
/// </summary>
public sealed class RuntimeSession
{
    private readonly object _timeProviderLock = new();
    private readonly List<TimeProviderEvent> _timeProviderEvents = new();
    private readonly ConcurrentDictionary<string, RandomStreamState> _randomStreams = new(StringComparer.Ordinal);
    private int _sequence = -1;

    private RuntimeSession(
        string testName,
        DateTimeOffset recordedAtUtc,
        string machine,
        string dotnetVersion,
        long timestampFrequency)
    {
        TestName = testName;
        RecordedAtUtc = recordedAtUtc;
        Machine = machine;
        DotnetVersion = dotnetVersion;
        TimestampFrequency = timestampFrequency;
    }

    /// <summary>The test this session was recorded for, when known; empty otherwise.</summary>
    public string TestName { get; }

    /// <summary>When the recording run started.</summary>
    public DateTimeOffset RecordedAtUtc { get; }

    /// <summary>The machine the recording run happened on (<see cref="Environment.MachineName"/>).</summary>
    public string Machine { get; }

    /// <summary>The .NET runtime version the recording run used.</summary>
    public string DotnetVersion { get; }

    /// <summary>
    /// Recorded once, at session start, from the clock actually backing the recording
    /// (<see cref="Stopwatch.Frequency"/>). <see cref="ReplayTimeProvider"/> returns this value from its
    /// own <c>TimestampFrequency</c> rather than the replay machine's, so elapsed-time arithmetic in the
    /// system under test stays internally consistent even across machines whose real clocks differ.
    /// </summary>
    public long TimestampFrequency { get; }

    /// <summary>Starts a brand-new, empty session for a recording run.</summary>
    public static RuntimeSession StartRecording(string testName = "")
    {
        return new RuntimeSession(
            testName,
            recordedAtUtc: DateTimeOffset.UtcNow,
            machine: Environment.MachineName,
            dotnetVersion: Environment.Version.ToString(),
            timestampFrequency: Stopwatch.Frequency);
    }

    /// <summary>Rehydrates a session read back from a session file, for a replay run.</summary>
    public static RuntimeSession FromSerialized(
        string testName,
        DateTimeOffset recordedAtUtc,
        string machine,
        string dotnetVersion,
        long timestampFrequency,
        IEnumerable<TimeProviderEvent> timeProviderEvents,
        IEnumerable<KeyValuePair<string, IReadOnlyList<RandomEvent>>> randomStreams)
    {
        if (timeProviderEvents is null)
        {
            throw new ArgumentNullException(nameof(timeProviderEvents));
        }

        if (randomStreams is null)
        {
            throw new ArgumentNullException(nameof(randomStreams));
        }

        var session = new RuntimeSession(testName, recordedAtUtc, machine, dotnetVersion, timestampFrequency);

        session._timeProviderEvents.AddRange(timeProviderEvents);

        foreach (var stream in randomStreams)
        {
            var state = session._randomStreams.GetOrAdd(stream.Key, id => new RandomStreamState(id));
            foreach (var evt in stream.Value)
            {
                state.Seed(evt);
            }
        }

        return session;
    }

    /// <summary>The next value of the session-wide monotonic sequence counter.</summary>
    public int NextSequence() => Interlocked.Increment(ref _sequence);

    /// <summary>
    /// Builds and appends a TimeProvider event, assigning its sequence number atomically with the
    /// append so the event list stays in <c>Seq</c> order.
    /// </summary>
    internal TimeProviderEvent RecordTimeProviderEvent(Func<int, TimeProviderEvent> factory)
    {
        if (factory is null)
        {
            throw new ArgumentNullException(nameof(factory));
        }

        lock (_timeProviderLock)
        {
            var evt = factory(NextSequence());
            _timeProviderEvents.Add(evt);
            return evt;
        }
    }

    /// <summary>A snapshot of the TimeProvider events recorded so far, in recorded order.</summary>
    public IReadOnlyList<TimeProviderEvent> TimeProviderEvents
    {
        get
        {
            lock (_timeProviderLock)
            {
                return _timeProviderEvents.ToArray();
            }
        }
    }

    /// <summary>A snapshot of the named Random streams recorded so far.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<RandomEvent>> RandomStreams
    {
        get
        {
            var result = new Dictionary<string, IReadOnlyList<RandomEvent>>(StringComparer.Ordinal);
            foreach (var pair in _randomStreams)
            {
                result[pair.Key] = pair.Value.Snapshot();
            }

            return result;
        }
    }

    /// <summary>Gets or creates the named Random stream, for either recording an event into it or replaying one from it.</summary>
    internal RandomStreamState GetOrAddRandomStream(string streamId) =>
        _randomStreams.GetOrAdd(streamId, id => new RandomStreamState(id));
}

/// <summary>
/// One named Random call site's recorded events, in recorded order, plus (during replay) the cursor
/// that tracks how far that stream has been consumed.
/// </summary>
internal sealed class RandomStreamState
{
    private readonly object _lock = new();
    private readonly List<RandomEvent> _events = new();
    private int _replayCursor;

    public RandomStreamState(string id)
    {
        Id = id;
    }

    public string Id { get; }

    /// <summary>Appends an already-recorded event. Used only while rehydrating a session for replay.</summary>
    public void Seed(RandomEvent evt)
    {
        lock (_lock)
        {
            _events.Add(evt);
        }
    }

    /// <summary>
    /// Builds and appends a new event, assigning its sequence number (via <paramref name="nextSequence"/>,
    /// normally <see cref="RuntimeSession.NextSequence"/>) atomically with the append, so this stream's
    /// event list stays in <c>Seq</c> order regardless of which thread wins the race to record here.
    /// </summary>
    public RandomEvent Record(Func<int> nextSequence, Func<int, RandomEvent> factory)
    {
        lock (_lock)
        {
            var evt = factory(nextSequence());
            _events.Add(evt);
            return evt;
        }
    }

    /// <summary>Pops the next recorded event for this stream, in recorded order, or null once the stream is exhausted.</summary>
    public RandomEvent? PopNext()
    {
        lock (_lock)
        {
            if (_replayCursor >= _events.Count)
            {
                return null;
            }

            return _events[_replayCursor++];
        }
    }

    public IReadOnlyList<RandomEvent> Snapshot()
    {
        lock (_lock)
        {
            return _events.ToArray();
        }
    }
}
