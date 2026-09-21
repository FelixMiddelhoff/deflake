using System;
using System.Collections.Generic;
using System.Linq;

namespace Deflake.Core.Heuristics;

/// <summary>
/// Result of a heuristic pattern match against error message and stack trace.
/// Supporting evidence only, never the sole basis of a verdict.
/// </summary>
public sealed class HeuristicResult
{
    public HeuristicResult(string patternName, string description, double confidence, string? suggestion = null)
    {
        PatternName = patternName;
        Description = description;
        Confidence = confidence; // 0.0 to 1.0
        Suggestion = suggestion;
    }

    /// <summary>Name of the heuristic pattern that matched.</summary>
    public string PatternName { get; }

    /// <summary>Human-readable description of what was found.</summary>
    public string Description { get; }

    /// <summary>Confidence 0.0-1.0. Used to weight evidence; not binary.</summary>
    public double Confidence { get; }

    /// <summary>Optional suggestion for what to investigate next.</summary>
    public string? Suggestion { get; }
}

/// <summary>Registry of heuristic results found in an error.</summary>
public sealed class HeuristicFindings
{
    private readonly List<HeuristicResult> _results = new();

    public IReadOnlyList<HeuristicResult> Results => _results.AsReadOnly();

    public void Add(HeuristicResult result) => _results.Add(result);

    public bool Found => _results.Count > 0;

    public double MaxConfidence => _results.Count > 0 ? _results.Max(r => r.Confidence) : 0.0;
}
