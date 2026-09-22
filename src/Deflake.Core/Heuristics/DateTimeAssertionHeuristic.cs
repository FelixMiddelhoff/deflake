using System;

namespace Deflake.Core.Heuristics;

/// <summary>Detects DateTime and ordering assertion failures.</summary>
internal sealed class DateTimeAssertionHeuristic : IHeuristic
{
    public string PatternName => "DateTimeAssertion";

    public HeuristicResult? Match(string? errorMessage, string? stackTrace)
    {
        if (string.IsNullOrEmpty(errorMessage) && string.IsNullOrEmpty(stackTrace))
            return null;

        var combined = (errorMessage ?? "") + " " + (stackTrace ?? "");
        var lower = combined.ToLowerInvariant();

        // Check for DateTime-related assertions
        var hasDateTimeRef = lower.Contains("datetime")
                             || lower.Contains("utcnow")
                             || lower.Contains("datetimeoffset")
                             || lower.Contains("timespan")
                             || lower.Contains("date was");

        if (hasDateTimeRef
            && (lower.Contains("assert")
             || lower.Contains("was not equal to")
             || lower.Contains("expected")
             || lower.Contains("should be")))
        {
            return new HeuristicResult(
                "DateTimeAssertion",
                "DateTime or ordering assertion failed, suggesting timing sensitivity.",
                0.75,
                "Check for assertions on absolute times (DateTime.UtcNow). Use relative times or time providers in tests."
            );
        }

        return null;
    }
}
