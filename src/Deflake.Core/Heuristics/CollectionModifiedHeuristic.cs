using System;

namespace Deflake.Core.Heuristics;

/// <summary>Detects concurrent collection modification patterns.</summary>
internal sealed class CollectionModifiedHeuristic : IHeuristic
{
    public string PatternName => "CollectionModified";

    public HeuristicResult? Match(string? errorMessage, string? stackTrace)
    {
        if (string.IsNullOrEmpty(errorMessage) && string.IsNullOrEmpty(stackTrace))
            return null;

        var combined = (errorMessage ?? "") + " " + (stackTrace ?? "");
        var lower = combined.ToLowerInvariant();

        if (lower.Contains("collection was modified")
            || lower.Contains("collection has been modified")
            || (lower.Contains("invalidoperationexception") && lower.Contains("enumerat")))
        {
            return new HeuristicResult(
                "CollectionModified",
                "Collection modified during enumeration, indicating concurrent modification or order sensitivity.",
                0.9,
                "Check for shared mutable state, concurrent modifications, or test order dependencies."
            );
        }

        return null;
    }
}
