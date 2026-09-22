using System;

namespace Deflake.Core.Heuristics;

/// <summary>Detects timeout-related error patterns.</summary>
internal sealed class TimeoutHeuristic : IHeuristic
{
    public string PatternName => "Timeout";

    public HeuristicResult? Match(string? errorMessage, string? stackTrace)
    {
        if (string.IsNullOrEmpty(errorMessage) && string.IsNullOrEmpty(stackTrace))
            return null;

        var combined = (errorMessage ?? "") + " " + (stackTrace ?? "");
        var lower = combined.ToLowerInvariant();

        // Check for explicit timeout indicators
        if (lower.Contains("timeout") ||
            lower.Contains("timed out") ||
            lower.Contains("task was canceled") ||
            lower.Contains("operation timed out") ||
            lower.Contains("operationcanceled"))
        {
            return new HeuristicResult(
                "Timeout",
                "Test or operation appears to have timed out, suggesting a performance issue or deadlock.",
                0.95,
                "Check for blocking operations, resource leaks, or deadlocks in the test or code under test."
            );
        }

        // Check for async timeout patterns
        if (lower.Contains("task.wait") ||
            lower.Contains("task.result") ||
            lower.Contains("waitone") ||
            lower.Contains("waitall"))
        {
            return new HeuristicResult(
                "Timeout",
                "Test uses blocking wait on async operation, may indicate timing sensitivity.",
                0.7,
                "Consider using async/await patterns instead of blocking waits."
            );
        }

        return null;
    }
}
