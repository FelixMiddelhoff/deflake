namespace Deflake.Core.Heuristics;

/// <summary>
/// Pattern matcher for error messages and stack traces.
/// Analyzes text to extract evidence of what might cause flakiness.
/// </summary>
public interface IHeuristic
{
    /// <summary>Name of the heuristic pattern.</summary>
    string PatternName { get; }

    /// <summary>
    /// Analyze error message and stack trace.
    /// Returns null if pattern does not match; a result if it does.
    /// </summary>
    HeuristicResult? Match(string? errorMessage, string? stackTrace);
}
