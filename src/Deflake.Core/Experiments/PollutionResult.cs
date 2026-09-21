using System;
using System.Collections.Generic;
using System.Linq;

namespace Deflake.Core.Experiments;

/// <summary>
/// What the polluter search found: the smallest set of other tests that, run together with the
/// failing test, reproduce its failure, plus the evidence ddmin gathered while looking for it. A
/// verdict that names specific polluters rests on <see cref="Polluters"/>; everything else here is how
/// that answer was reached.
/// </summary>
public sealed class PollutionResult
{
    /// <param name="polluters">The minimal reproducing set, in scope order. Empty when none was found.</param>
    /// <param name="polluterFound">True when <paramref name="polluters"/> is a specific, non-empty answer.</param>
    /// <param name="runs">One row per subset ddmin tried, in the order it tried them.</param>
    /// <param name="completed">True when the search ran to a 1-minimal answer.</param>
    /// <param name="incompleteReason">Why it did not, when <paramref name="completed"/> is false.</param>
    /// <param name="ddminTestsRun">How many distinct subsets ddmin actually measured.</param>
    public PollutionResult(
        IReadOnlyList<string> polluters,
        bool polluterFound,
        IReadOnlyList<ExperimentRun> runs,
        bool completed,
        string? incompleteReason,
        int ddminTestsRun)
    {
        if (polluters is null)
        {
            throw new ArgumentNullException(nameof(polluters));
        }

        if (runs is null)
        {
            throw new ArgumentNullException(nameof(runs));
        }

        var rows = runs.ToArray();
        if (rows.Any(row => row is null))
        {
            throw new ArgumentException("A pollution result cannot contain a missing row.", nameof(runs));
        }

        if (completed && incompleteReason is not null)
        {
            throw new ArgumentException("A completed search has no reason to be incomplete.", nameof(incompleteReason));
        }

        if (!completed && string.IsNullOrWhiteSpace(incompleteReason))
        {
            throw new ArgumentException("An incomplete search has to say why.", nameof(incompleteReason));
        }

        if (ddminTestsRun < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ddminTestsRun), "The number of ddmin experiments cannot be negative.");
        }

        if (polluterFound && polluters.Count == 0)
        {
            throw new ArgumentException("A polluter set that was found cannot be empty.", nameof(polluterFound));
        }

        Polluters = polluters;
        PolluterFound = polluterFound;
        Runs = rows;
        Completed = completed;
        IncompleteReason = incompleteReason;
        DdminTestsRun = ddminTestsRun;
    }

    /// <summary>
    /// The minimal set of other tests that reproduce the failure together with the failing test, in
    /// scope order. Empty when <see cref="PolluterFound"/> is false: either nothing in the scope
    /// reproduced the failure, or the failing test needed no company to fail within this search.
    /// </summary>
    public IReadOnlyList<string> Polluters { get; }

    /// <summary>True when a specific, non-empty set of other tests reproduces the failure.</summary>
    public bool PolluterFound { get; }

    /// <summary>One row per subset ddmin tried, in the order it tried them.</summary>
    public IReadOnlyList<ExperimentRun> Runs { get; }

    /// <summary>
    /// True when the search ran to a 1-minimal answer instead of stopping on a ddmin iteration cap or
    /// a run that could not be measured.
    /// </summary>
    public bool Completed { get; }

    /// <summary>Why the search stopped short, or null.</summary>
    public string? IncompleteReason { get; }

    /// <summary>How many distinct subsets ddmin actually measured (repeats are cached, not rerun).</summary>
    public int DdminTestsRun { get; }
}
