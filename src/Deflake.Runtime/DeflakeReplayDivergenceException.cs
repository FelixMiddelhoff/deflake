using System;

namespace Deflake.Runtime;

/// <summary>
/// A replay run asked its <see cref="ReplayTimeProvider"/> or <see cref="ReplayRandom"/> for a value the
/// recording does not have at that position: a different call, a different argument shape, or the
/// stream was already exhausted. Thrown immediately, never silently satisfied with a default or
/// substitute value. The caller (typically a future <c>RecordReplayExperiment</c> in Deflake.Core,
/// which never references this assembly and instead recognizes this exception's type name in the test
/// process's captured output) should treat a run that throws this as unmeasurable, not as a pass or a
/// fail of the test under investigation.
/// </summary>
public sealed class DeflakeReplayDivergenceException : Exception
{
    public DeflakeReplayDivergenceException(string streamId, string expected, string actual)
        : base(BuildMessage(streamId, expected, actual))
    {
        StreamId = streamId;
        Expected = expected;
        Actual = actual;
    }

    /// <summary>The named Random stream, or <c>"TimeProvider"</c>, the divergence happened on.</summary>
    public string StreamId { get; }

    /// <summary>What the recording had next for this stream.</summary>
    public string Expected { get; }

    /// <summary>What this run actually asked for.</summary>
    public string Actual { get; }

    private static string BuildMessage(string streamId, string expected, string actual)
    {
        return $"Deflake replay divergence on stream '{streamId}': the recording has {expected}, "
            + $"but this run asked for {actual}. The test's TimeProvider/Random usage no longer matches "
            + "the recorded call sequence (a different call, a different order, or a different argument "
            + "shape) - this run cannot be replayed and should be scored as unmeasurable, not as a pass "
            + "or a fail.";
    }
}
