using System;

namespace Deflake.Core.Heuristics;

/// <summary>Detects ObjectDisposedException patterns.</summary>
internal sealed class ObjectDisposedExceptionHeuristic : IHeuristic
{
    public string PatternName => "ObjectDisposedException";

    public HeuristicResult? Match(string? errorMessage, string? stackTrace)
    {
        if (string.IsNullOrEmpty(errorMessage) && string.IsNullOrEmpty(stackTrace))
        {
            return null;
        }

        var combined = (errorMessage ?? "") + " " + (stackTrace ?? "");
        var lower = combined.ToLowerInvariant();

        if (lower.Contains("objectdisposedexception") || lower.Contains("object has been disposed"))
        {
            var hasDispose = (stackTrace ?? "").ToLowerInvariant().Contains(".dispose()");
            var confidence = hasDispose ? 0.95 : 0.8;

            return new HeuristicResult(
                "ObjectDisposedException",
                "Object accessed after being disposed, suggesting timing-dependent resource lifecycle.",
                confidence,
                "Check for race conditions between object disposal and usage. May be order-dependent or parallelism-sensitive."
            );
        }

        return null;
    }
}
