using System;
using Deflake.Core.Experiments;
using Deflake.Core.Statistics;

namespace Deflake.Core.Verdicts;

/// <summary>
/// One row of the table a verdict rests on: one condition of one experiment, with the runs behind it.
/// A reader who disagrees with the verdict can recompute it from these rows alone, which is the point.
/// </summary>
public sealed class VerdictEvidence
{
    /// <param name="factor">The factor that was varied, for example <c>Parallelism</c>.</param>
    /// <param name="condition">The value of that factor in this row, for example <c>Serial</c>.</param>
    /// <param name="runs">The runs that carry evidence; runs without a usable outcome are not in here.</param>
    /// <param name="failures">How many of those runs the test failed or was aborted in.</param>
    /// <param name="intervalLow">Lower end of the 95 % Wilson interval.</param>
    /// <param name="intervalHigh">Upper end of the 95 % Wilson interval.</param>
    /// <param name="heuristicSupport">The message pattern that matched, or null. Supporting evidence, never the reason.</param>
    /// <param name="note">Why this row is short of runs, or anything else the reader needs to judge it.</param>
    public VerdictEvidence(
        string factor,
        string condition,
        int runs,
        int failures,
        double intervalLow,
        double intervalHigh,
        string? heuristicSupport = null,
        string? note = null)
    {
        if (string.IsNullOrWhiteSpace(factor))
        {
            throw new ArgumentException("An evidence row names the factor it measured.", nameof(factor));
        }

        if (string.IsNullOrWhiteSpace(condition))
        {
            throw new ArgumentException("An evidence row names the condition it measured.", nameof(condition));
        }

        if (runs < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(runs), "The number of runs cannot be negative.");
        }

        if (failures < 0 || failures > runs)
        {
            throw new ArgumentOutOfRangeException(nameof(failures), "The failures must be between 0 and the number of runs.");
        }

        if (intervalLow < 0 || intervalLow > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(intervalLow), "An interval end is a probability.");
        }

        if (intervalHigh < 0 || intervalHigh > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(intervalHigh), "An interval end is a probability.");
        }

        if (intervalHigh < intervalLow)
        {
            throw new ArgumentOutOfRangeException(nameof(intervalHigh), "An interval cannot end before it starts.");
        }

        Factor = factor;
        Condition = condition;
        Runs = runs;
        Failures = failures;
        IntervalLow = intervalLow;
        IntervalHigh = intervalHigh;
        HeuristicSupport = heuristicSupport;
        Note = note;
    }

    /// <summary>What was varied, for example <c>Scope</c>, <c>Parallelism</c> or <c>Culture</c>.</summary>
    public string Factor { get; }

    /// <summary>The value of the factor in this row, for example <c>Alone</c> or <c>UnderLoad</c>.</summary>
    public string Condition { get; }

    /// <summary>The runs that carry evidence: the denominator of <see cref="FailureRatePercent"/>.</summary>
    public int Runs { get; }

    /// <summary>How many of those runs the test failed or was aborted in.</summary>
    public int Failures { get; }

    /// <summary>The observed failure rate as a percentage, for the table.</summary>
    public double FailureRatePercent => Runs == 0 ? 0 : 100.0 * Failures / Runs;

    /// <summary>Lower end of the 95 % Wilson interval.</summary>
    public double IntervalLow { get; }

    /// <summary>Upper end of the 95 % Wilson interval.</summary>
    public double IntervalHigh { get; }

    /// <summary>The message pattern that matched this failure, or null when none did.</summary>
    public string? HeuristicSupport { get; }

    /// <summary>Why this row is short of runs, or null.</summary>
    public string? Note { get; }

    /// <summary>The row for one condition of an experiment.</summary>
    public static VerdictEvidence From(string factor, ExperimentRun run, string? heuristicSupport = null)
    {
        if (run is null)
        {
            throw new ArgumentNullException(nameof(run));
        }

        return new VerdictEvidence(
            factor,
            run.FactorValue,
            run.Measured,
            run.Failures,
            run.Rate.Lower,
            run.Rate.Upper,
            heuristicSupport,
            run.IncompleteReason);
    }

    /// <summary>The rate this row measured, for comparisons that need the statistics rather than the text.</summary>
    public FailureRate Rate => new(Failures, Runs);

    public override string ToString()
    {
        return $"{Factor}={Condition}: {Failures}/{Runs} failed ({FailureRatePercent:F1} %, 95 % {IntervalLow:P1}–{IntervalHigh:P1})";
    }
}
