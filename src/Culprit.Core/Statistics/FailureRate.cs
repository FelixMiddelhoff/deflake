using System;

namespace Culprit.Core.Statistics;

/// <summary>
/// How often a test failed in a number of runs, with the uncertainty that comes from having
/// only that many runs. Every claim Culprit makes about a factor rests on comparing these.
/// </summary>
public readonly struct FailureRate
{
    /// <summary>The z value for a two-sided 95 % interval.</summary>
    private const double Z95 = 1.959963984540054;

    public FailureRate(int failures, int runs)
    {
        if (runs < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(runs), "The number of runs cannot be negative.");
        }

        if (failures < 0 || failures > runs)
        {
            throw new ArgumentOutOfRangeException(nameof(failures), "The number of failures must be between 0 and the number of runs.");
        }

        Failures = failures;
        Runs = runs;
    }

    public int Failures { get; }

    public int Runs { get; }

    /// <summary>The observed share of failing runs, or 0 when there were no runs.</summary>
    public double Observed => Runs == 0 ? 0 : (double)Failures / Runs;

    /// <summary>Lower end of the 95 % Wilson interval. Without runs nothing is known: 0.</summary>
    public double Lower => Interval().Lower;

    /// <summary>Upper end of the 95 % Wilson interval. Without runs nothing is known: 1.</summary>
    public double Upper => Interval().Upper;

    /// <summary>
    /// True when the two rates are different beyond chance: their 95 % intervals do not overlap.
    /// Deliberately conservative, because a wrong "this factor is responsible" is worse than a missed one.
    /// </summary>
    public bool DiffersFrom(FailureRate other)
    {
        if (Runs == 0 || other.Runs == 0)
        {
            return false;
        }

        return Lower > other.Upper || other.Lower > Upper;
    }

    /// <summary>
    /// The highest failure rate that would still, with 95 % confidence, have produced no failure in
    /// <paramref name="runsWithoutFailure"/> runs (exact: <c>1 - 0.05^(1/n)</c>, about 3/n).
    /// Answers "it passed 20 times: how flaky can it still be?".
    /// </summary>
    public static double UpperBoundAfterNoFailures(int runsWithoutFailure)
    {
        if (runsWithoutFailure <= 0)
        {
            return 1;
        }

        return 1 - Math.Pow(0.05, 1.0 / runsWithoutFailure);
    }

    /// <summary>
    /// How many runs without a single failure are needed before a test with a failure rate of at least
    /// <paramref name="rate"/> would have been caught with 95 % confidence.
    /// </summary>
    public static int RunsNeededToSee(double rate)
    {
        if (rate <= 0 || rate > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(rate), "The rate must be greater than 0 and at most 1.");
        }

        if (rate == 1)
        {
            return 1;
        }

        return (int)Math.Ceiling(Math.Log(0.05) / Math.Log(1 - rate));
    }

    private (double Lower, double Upper) Interval()
    {
        if (Runs == 0)
        {
            return (0, 1);
        }

        var n = (double)Runs;
        var p = Observed;
        var zSquared = Z95 * Z95;
        var denominator = 1 + (zSquared / n);
        var center = (p + (zSquared / (2 * n))) / denominator;
        var margin = Z95 * Math.Sqrt(((p * (1 - p)) / n) + (zSquared / (4 * n * n))) / denominator;

        // Rounding can leave the ends a hair outside [0, 1] or off the observed value at the extremes.
        var lower = Failures == 0 ? 0 : Math.Max(0, center - margin);
        var upper = Failures == Runs ? 1 : Math.Min(1, center + margin);
        return (lower, upper);
    }
}
