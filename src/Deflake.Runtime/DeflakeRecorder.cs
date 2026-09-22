using System;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;

namespace Deflake.Runtime;

/// <summary>
/// The single entry point a test project touches. Reads <c>DEFLAKE_RECORD</c>/<c>DEFLAKE_REPLAY</c> from
/// the environment once, lazily, and hands back a <see cref="TimeProvider"/>/<see cref="Random"/> that is
/// either recording, replaying, or a transparent passthrough. Never throws for a project that isn't being
/// investigated: with neither environment variable set, <see cref="TimeProvider"/> behaves exactly like
/// <see cref="TimeProvider.System"/> and <see cref="Random(string?)"/> behaves exactly like
/// <see cref="Random.Shared"/> - zero overhead, zero files written, zero behaviour change for a project
/// that references this package for reasons unrelated to the current run.
/// </summary>
public static class DeflakeRecorder
{
    private const string RecordEnvironmentVariable = "DEFLAKE_RECORD";
    private const string ReplayEnvironmentVariable = "DEFLAKE_REPLAY";

    private static readonly object InitLock = new();
    private static readonly ConcurrentDictionary<string, Random> RandomStreamCache = new(StringComparer.Ordinal);

    private static volatile bool _initialized;
    private static string? _recordPath;
    private static string? _replayPath;
    private static RuntimeSession? _recordingSession;
    private static RuntimeSession? _replaySession;
    private static TimeProvider? _timeProvider;

    /// <summary>
    /// The process-wide clock: recording, replaying, or (when neither environment variable is set)
    /// <see cref="TimeProvider.System"/> itself.
    /// </summary>
    public static TimeProvider TimeProvider
    {
        get
        {
            EnsureInitialized();
            return _timeProvider!;
        }
    }

    /// <summary>True while this process is recording a session (<c>DEFLAKE_RECORD</c> is set).</summary>
    public static bool IsRecording
    {
        get
        {
            EnsureInitialized();
            return _recordingSession is not null;
        }
    }

    /// <summary>True while this process is replaying a session (<c>DEFLAKE_REPLAY</c> is set).</summary>
    public static bool IsReplaying
    {
        get
        {
            EnsureInitialized();
            return _replaySession is not null;
        }
    }

    /// <summary>The path being recorded to or replayed from, or null when neither is active.</summary>
    public static string? SessionPath
    {
        get
        {
            EnsureInitialized();
            return _recordPath ?? _replayPath;
        }
    }

    /// <summary>
    /// A source of randomness for one named call site: recording, replaying, or (passthrough)
    /// <see cref="Random.Shared"/>. One call site is one stream - repeated calls with the same
    /// <paramref name="streamId"/> return the same instance, backed by the same recorded/replayed
    /// sequence. <paramref name="streamId"/> defaults to the calling member's name; pass an explicit
    /// value to share one stream across several call sites, or to give two call sites in the same
    /// member independent streams.
    /// </summary>
    public static Random Random([CallerMemberName] string? streamId = null)
    {
        EnsureInitialized();
        var id = string.IsNullOrEmpty(streamId) ? "default" : streamId;
        return RandomStreamCache.GetOrAdd(id, CreateRandomForStream);
    }

    /// <summary>
    /// Flushes the in-memory session to <see cref="SessionPath"/>. Registered automatically via
    /// <see cref="AppDomain.ProcessExit"/>/<see cref="AssemblyLoadContext.Unloading"/> while recording,
    /// but exposed so a test fixture can call it explicitly before asserting on the file (xUnit/NUnit
    /// teardown, <c>WebApplicationFactory</c> disposal). A no-op while replaying or passthrough.
    /// </summary>
    public static void Flush()
    {
        EnsureInitialized();

        if (_recordingSession is null || _recordPath is null)
        {
            return;
        }

        SessionSerializer.Write(_recordingSession, _recordPath);
    }

    private static Random CreateRandomForStream(string streamId)
    {
        if (_recordingSession is { } recording)
        {
            return new RecordingRandom(recording, streamId);
        }

        if (_replaySession is { } replay)
        {
            return new ReplayRandom(replay, streamId);
        }

        // Qualified with the namespace: within this class, the unqualified name "Random" would resolve
        // to the DeflakeRecorder.Random(streamId) method above, not the BCL type.
        return System.Random.Shared;
    }

    private static void EnsureInitialized()
    {
        if (_initialized)
        {
            return;
        }

        lock (InitLock)
        {
            if (_initialized)
            {
                return;
            }

            var recordPath = Environment.GetEnvironmentVariable(RecordEnvironmentVariable);
            var replayPath = Environment.GetEnvironmentVariable(ReplayEnvironmentVariable);
            var hasRecordPath = !string.IsNullOrEmpty(recordPath);
            var hasReplayPath = !string.IsNullOrEmpty(replayPath);

            if (hasRecordPath && hasReplayPath)
            {
                throw new InvalidOperationException(
                    $"Both {RecordEnvironmentVariable} and {ReplayEnvironmentVariable} are set. "
                    + "Deflake.Runtime can only record or replay in one process, never both at once - "
                    + "this is a caller bug, since Deflake's own CLI never sets both.");
            }

            if (hasRecordPath)
            {
                _recordPath = recordPath;
                _recordingSession = RuntimeSession.StartRecording();
                _timeProvider = new RecordingTimeProvider(_recordingSession);
                RegisterFlushOnExit();
            }
            else if (hasReplayPath)
            {
                _replayPath = replayPath;
                _replaySession = SessionSerializer.Read(replayPath!);
                _timeProvider = new ReplayTimeProvider(_replaySession);
            }
            else
            {
                // Qualified with the namespace: within this class, the unqualified name "TimeProvider"
                // would resolve to the DeflakeRecorder.TimeProvider property below, not the BCL type.
                _timeProvider = System.TimeProvider.System;
            }

            _initialized = true;
        }
    }

    private static void RegisterFlushOnExit()
    {
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Flush();

        var context = AssemblyLoadContext.GetLoadContext(typeof(DeflakeRecorder).Assembly);
        if (context is not null)
        {
            context.Unloading += _ => Flush();
        }
    }
}
