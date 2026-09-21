using System;
using Deflake.Core.Execution;
using Deflake.Core.Statistics;

namespace Deflake.Core.Experiments;

/// <summary>
/// What one condition of an experiment measured: the request that was repeated, how often, how often
/// the test failed, and the rate with its interval. One of these is one row of the evidence table.
/// </summary>
public sealed class ExperimentRun
{
    /// <param name="factorValue">The value of the factor under test, for example the scope level <c>Alone</c>.</param>
    /// <param name="request">The request that was repeated; it is also the repro command for this row.</param>
    /// <param name="total">How many runs were actually started.</param>
    /// <param name="failures">How many of them ended with the test failed or aborted.</param>
    /// <param name="missing">How many of them produced no usable outcome for the test.</param>
    /// <param name="incompleteReason">Why fewer runs happened than planned, or null when the plan was carried out.</param>
    public ExperimentRun(
        string factorValue,
        TestRunRequest request,
        int total,
        int failures,
        int missing,
        string? incompleteReason = null)
    {
        if (string.IsNullOrWhiteSpace(factorValue))
        {
            throw new ArgumentException("A factor value is required.", nameof(factorValue));
        }

        if (total < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(total), "The number of runs cannot be negative.");
        }

        if (failures < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(failures), "The number of failures cannot be negative.");
        }

        if (missing < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(missing), "The number of runs without a result cannot be negative.");
        }

        if (failures + missing > total)
        {
            throw new ArgumentOutOfRangeException(
                nameof(failures),
                $"{failures} failures and {missing} runs without a result are more than the {total} runs that happened.");
        }

        FactorValue = factorValue;
        Request = request ?? throw new ArgumentNullException(nameof(request));
        Total = total;
        Failures = failures;
        Missing = missing;
        IncompleteReason = incompleteReason;
        Rate = new FailureRate(failures, total - missing);
    }

    /// <summary>The factor value this row measured, for example <c>Alone</c>, <c>Class</c> or <c>Assembly</c>.</summary>
    public string FactorValue { get; }

    /// <summary>The request every run of this row used.</summary>
    public TestRunRequest Request { get; }

    /// <summary>How many runs were started for this row.</summary>
    public int Total { get; }

    /// <summary>Runs in which the test failed or was aborted.</summary>
    public int Failures { get; }

    /// <summary>
    /// Runs that say nothing about the test: it was not in the results at all (a filter that matched
    /// nothing), it was skipped, or the run could not be scored. Never counted as a pass or a failure,
    /// because "we did not see it" is not "it was fine".
    /// </summary>
    public int Missing { get; }

    /// <summary>Runs in which the test passed.</summary>
    public int Passes => Total - Failures - Missing;

    /// <summary>The runs that carry evidence, and the denominator of <see cref="Rate"/>.</summary>
    public int Measured => Total - Missing;

    /// <summary>The failure rate over the measured runs, with its 95 % interval.</summary>
    public FailureRate Rate { get; }

    /// <summary>Why this row has fewer runs than planned (budget, timeout, a run that could not be executed), or null.</summary>
    public string? IncompleteReason { get; }

    public override string ToString()
    {
        return $"{FactorValue}: {Failures}/{Measured} failed ({Rate.Observed:P1}, 95 % {Rate.Lower:P1}–{Rate.Upper:P1})";
    }
}
