using System;
using System.Collections.Generic;
using System.Linq;

namespace Deflake.Core.Experiments;

/// <summary>
/// What one experiment measured: one row per value of the factor it varied. The verdict engine reads
/// these rows and nothing else, so everything a claim could rest on has to be in here.
/// </summary>
public sealed class ExperimentResult
{
    /// <param name="name">The experiment's name, as the report prints it.</param>
    /// <param name="factor">What was varied (<c>Scope</c>), or null when the experiment has a single condition.</param>
    /// <param name="runs">One row per condition, in the order they were run.</param>
    /// <param name="completed">True when the experiment carried out its plan.</param>
    /// <param name="incompleteReason">Why it did not, when <paramref name="completed"/> is false.</param>
    public ExperimentResult(
        string name,
        string? factor,
        IEnumerable<ExperimentRun> runs,
        bool completed,
        string? incompleteReason = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("An experiment name is required.", nameof(name));
        }

        if (runs is null)
        {
            throw new ArgumentNullException(nameof(runs));
        }

        if (factor is not null && string.IsNullOrWhiteSpace(factor))
        {
            throw new ArgumentException("A factor is either named or null, never blank.", nameof(factor));
        }

        var rows = runs.ToArray();
        if (rows.Any(row => row is null))
        {
            throw new ArgumentException("An experiment result cannot contain a missing row.", nameof(runs));
        }

        if (completed && incompleteReason is not null)
        {
            throw new ArgumentException("A completed experiment has no reason to be incomplete.", nameof(incompleteReason));
        }

        if (!completed && string.IsNullOrWhiteSpace(incompleteReason))
        {
            throw new ArgumentException("An incomplete experiment has to say why.", nameof(incompleteReason));
        }

        Name = name;
        Factor = factor;
        Runs = rows;
        Completed = completed;
        IncompleteReason = incompleteReason;
    }

    public string Name { get; }

    /// <summary>The factor that was varied, or null for a single-condition experiment such as isolation.</summary>
    public string? Factor { get; }

    /// <summary>One row per condition, in the order they were run.</summary>
    public IReadOnlyList<ExperimentRun> Runs { get; }

    /// <summary>
    /// True when the experiment did what it set out to do. Stopping early because the answer was
    /// found (the ladder's first failing step) counts as completed; running out of time does not.
    /// </summary>
    public bool Completed { get; }

    /// <summary>Why the experiment stopped short, or null.</summary>
    public string? IncompleteReason { get; }

    /// <summary>True when the test failed at least once somewhere in this experiment.</summary>
    public bool AnyFailure => Runs.Any(run => run.Failures > 0);

    /// <summary>
    /// The first condition in which the test failed, or null when it never did. For the scope ladder
    /// that is the smallest scope the failure needs.
    /// </summary>
    public ExperimentRun? FirstFailingRun => Runs.FirstOrDefault(run => run.Failures > 0);

    /// <summary>The row for one factor value, or null when the experiment never got that far.</summary>
    public ExperimentRun? Run(string factorValue)
    {
        return Runs.FirstOrDefault(run => string.Equals(run.FactorValue, factorValue, StringComparison.Ordinal));
    }
}
