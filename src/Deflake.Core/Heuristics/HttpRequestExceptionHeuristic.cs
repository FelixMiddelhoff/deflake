using System;

namespace Deflake.Core.Heuristics;

/// <summary>Detects HTTP and network failure patterns.</summary>
internal sealed class HttpRequestExceptionHeuristic : IHeuristic
{
    public string PatternName => "HttpRequestException";

    public HeuristicResult? Match(string? errorMessage, string? stackTrace)
    {
        if (string.IsNullOrEmpty(errorMessage) && string.IsNullOrEmpty(stackTrace))
            return null;

        var combined = (errorMessage ?? "") + " " + (stackTrace ?? "");
        var lower = combined.ToLowerInvariant();

        if (lower.Contains("httprequestexception") ||
            lower.Contains("connection refused") ||
            lower.Contains("connection reset") ||
            lower.Contains("connection aborted") ||
            lower.Contains("no connection could be made") ||
            lower.Contains("unable to connect") ||
            lower.Contains("network is unreachable"))
        {
            return new HeuristicResult(
                "HttpRequestException",
                "Network request failed, suggesting external service availability or timing issue.",
                0.85,
                "Check for flaky external dependencies. May need longer timeouts or retry logic."
            );
        }

        return null;
    }
}
