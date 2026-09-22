using System;
using System.Reflection;

namespace Deflake.Runtime.Tests;

/// <summary>
/// <see cref="DeflakeRecorder"/> reads DEFLAKE_RECORD/DEFLAKE_REPLAY exactly once per process and caches
/// the result forever - correct for a real test process (which only ever runs once), but it means
/// exercising more than one of its passthrough/record/replay branches inside a single test *process*
/// needs a way to force it back to "never initialized" between tests, rather than spawning a child
/// process per scenario. This harness does that via reflection: <c>Deflake.Runtime</c> already grants
/// this test assembly <c>InternalsVisibleTo</c>, but these particular fields are private, not internal,
/// since no production code needs to reset them - only this test harness does.
/// </summary>
internal static class DeflakeRecorderHarness
{
    private const string RecordVariable = "DEFLAKE_RECORD";
    private const string ReplayVariable = "DEFLAKE_REPLAY";

    private static readonly Type RecorderType = typeof(DeflakeRecorder);

    /// <summary>
    /// Resets <see cref="DeflakeRecorder"/>'s cached state and sets DEFLAKE_RECORD/DEFLAKE_REPLAY for the
    /// scope of the returned <see cref="IDisposable"/>; disposing it clears both variables and resets
    /// the cached state again, so the next test starts from the same "never initialized" point.
    /// </summary>
    public static IDisposable WithEnvironment(string? recordPath, string? replayPath) => new Scope(recordPath, replayPath);

    /// <summary>Forces <see cref="DeflakeRecorder"/> back to its "never initialized" state.</summary>
    public static void Reset()
    {
        SetField("_initialized", false);
        SetField("_recordPath", null);
        SetField("_replayPath", null);
        SetField("_recordingSession", null);
        SetField("_replaySession", null);
        SetField("_timeProvider", null);

        var cache = GetField("RandomStreamCache");
        var clear = cache?.GetType().GetMethod("Clear");
        clear?.Invoke(cache, null);
    }

    private static void SetField(string name, object? value) => Field(name).SetValue(null, value);

    private static object? GetField(string name) => Field(name).GetValue(null);

    private static FieldInfo Field(string name) =>
        RecorderType.GetField(name, BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException(
                $"DeflakeRecorder.{name} was not found by reflection - has the field been renamed? "
                + "DeflakeRecorderHarness needs updating to match.");

    private sealed class Scope : IDisposable
    {
        public Scope(string? recordPath, string? replayPath)
        {
            Reset();
            Environment.SetEnvironmentVariable(RecordVariable, recordPath);
            Environment.SetEnvironmentVariable(ReplayVariable, replayPath);
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable(RecordVariable, null);
            Environment.SetEnvironmentVariable(ReplayVariable, null);
            Reset();
        }
    }
}
