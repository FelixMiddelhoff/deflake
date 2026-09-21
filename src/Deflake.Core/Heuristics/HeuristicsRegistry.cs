using System;
using System.Collections.Generic;
using System.Linq;

namespace Deflake.Core.Heuristics;

/// <summary>
/// Registry and analyzer for all heuristic patterns.
/// </summary>
public sealed class HeuristicsRegistry
{
    private readonly List<IHeuristic> _heuristics = new();

    public HeuristicsRegistry()
    {
        // Register all heuristics
        _heuristics.Add(new TimeoutHeuristic());
        _heuristics.Add(new ObjectDisposedExceptionHeuristic());
        _heuristics.Add(new CollectionModifiedHeuristic());
        _heuristics.Add(new AddressInUseHeuristic());
        _heuristics.Add(new FileInUseHeuristic());
        _heuristics.Add(new DatabaseLockedHeuristic());
        _heuristics.Add(new HttpRequestExceptionHeuristic());
        _heuristics.Add(new DateTimeAssertionHeuristic());
    }

    /// <summary>
    /// Analyze error message and stack trace against all registered heuristics.
    /// Returns findings in confidence order (highest first).
    /// </summary>
    public HeuristicFindings Analyze(string? errorMessage, string? stackTrace)
    {
        var findings = new HeuristicFindings();

        if (string.IsNullOrEmpty(errorMessage) && string.IsNullOrEmpty(stackTrace))
            return findings;

        var results = new List<HeuristicResult>();

        foreach (var heuristic in _heuristics)
        {
            var result = heuristic.Match(errorMessage, stackTrace);
            if (result != null)
                results.Add(result);
        }

        // Sort by confidence descending
        foreach (var result in results.OrderByDescending(r => r.Confidence))
            findings.Add(result);

        return findings;
    }
}
