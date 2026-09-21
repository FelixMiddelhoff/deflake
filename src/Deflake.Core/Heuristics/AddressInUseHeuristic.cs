using System;

namespace Deflake.Core.Heuristics;

/// <summary>Detects port/address already in use patterns.</summary>
internal sealed class AddressInUseHeuristic : IHeuristic
{
    public string PatternName => "AddressInUse";

    public HeuristicResult? Match(string? errorMessage, string? stackTrace)
    {
        if (string.IsNullOrEmpty(errorMessage) && string.IsNullOrEmpty(stackTrace))
            return null;

        var combined = (errorMessage ?? "") + " " + (stackTrace ?? "");
        var lower = combined.ToLowerInvariant();

        if (lower.Contains("address already in use") ||
            lower.Contains("address is already in use") ||
            lower.Contains("port already in use") ||
            lower.Contains("bind: address already in use") ||
            lower.Contains("wsaeaddrinuse") ||
            lower.Contains("eaddrinuse"))
        {
            return new HeuristicResult(
                "AddressInUse",
                "Network port or address in use by another process, suggesting resource conflict.",
                0.95,
                "Check for tests that bind ports without cleanup or with insufficient TIME_WAIT. May be order-dependent or parallelism-sensitive."
            );
        }

        return null;
    }
}
