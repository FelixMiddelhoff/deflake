using System;
using System.Threading.Tasks;
using Deflake.Runtime;
using Xunit;

namespace RuntimeSampleProject;

/// <summary>
/// A timing-sensitive test that deliberately uses DeflakeRecorder to allow record/replay
/// diagnosis of timing-dependent failures. The test passes ~80% of the time under normal
/// conditions (due to random delays) and more frequently under high load.
///
/// When record/replay is enabled, this test demonstrates whether the failure is driven by
/// the specific random/clock values (replay matches live) or by thread interleaving
/// (replay succeeds but live fails).
/// </summary>
public class RuntimeSampleTests
{
    /// <summary>
    /// A test that simulates an operation with a random delay and checks if it completes
    /// before a timeout. The outcome depends on both the random delay value and system load.
    /// </summary>
    [Fact]
    public async Task Operation_completes_before_timeout()
    {
        // Use DeflakeRecorder to get deterministic replay support for random values.
        // In normal runs, this returns Random.Shared; during record/replay, it returns
        // a recorded/replayed stream.
        var random = DeflakeRecorder.Random();

        // Use DeflakeRecorder to get the clock for deterministic timing measurements.
        // In normal runs, this returns TimeProvider.System; during record/replay,
        // it returns a recorded/replayed time sequence.
        var timeProvider = DeflakeRecorder.TimeProvider;

        // Simulate an operation that takes a random amount of time.
        // Most of the time (80%+), this will be quick enough.
        // Occasionally, combined with system load, it will timeout.
        var delayMs = random.Next(10, 100);  // Random delay between 10-100ms
        var timeoutMs = 80;  // Aggressive timeout at 80ms

        var startTime = timeProvider.GetUtcNow();

        // Simulate the async operation with a real Task.Delay.
        // This is affected by system load - under high load, delays take longer.
        await Task.Delay(delayMs);

        var elapsed = timeProvider.GetUtcNow() - startTime;

        // Assert that the operation completed before the timeout.
        Assert.True(
            elapsed.TotalMilliseconds < timeoutMs,
            $"Operation took {elapsed.TotalMilliseconds:F1}ms but timeout was {timeoutMs}ms. "
            + "This can happen under high system load. "
            + "Use 'deflake investigate --record-replay' to diagnose whether the delay values "
            + "or thread interleaving cause the failure."
        );
    }
}
