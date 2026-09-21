using System;
using Deflake.Core.Statistics;

namespace Deflake.Core.Experiments;

/// <summary>
/// When repeating a run again is still worth its wall-clock time. Experiments do a fixed number of
/// runs first and then keep going only while more runs would actually sharpen the answer, up to the
/// budget the user allowed.
/// </summary>
public static class AdaptiveRuns
{
    /// <summary>
    /// How wide a 95 % interval may stay before extra runs are considered pointless. A quarter is
    /// narrow enough to tell a rare failure from a frequent one, and wide enough not to spend
    /// hundreds of runs on a rate nobody needs to three decimal places.
    /// </summary>
    public const double DefaultTargetIntervalWidth = 0.25;

    /// <summary>
    /// True when the measurement so far is still coarse enough that another run would improve it.
    /// </summary>
    /// <param name="rate">The rate measured so far at one factor value.</param>
    /// <param name="targetIntervalWidth">The interval width that counts as sharp enough.</param>
    public static bool WouldGainFromMoreRuns(FailureRate rate, double targetIntervalWidth = DefaultTargetIntervalWidth)
    {
        if (targetIntervalWidth <= 0 || targetIntervalWidth > 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(targetIntervalWidth),
                "The target interval width must be greater than 0 and at most 1.");
        }

        // Nothing was measured at all (every run was missing or timed out); repeating it changes nothing.
        if (rate.Runs == 0)
        {
            return false;
        }

        // Always or never is as clear as a single condition gets: the remaining uncertainty is the
        // rule of three, and shaving it needs many times more runs than any budget allows.
        if (rate.Failures == 0 || rate.Failures == rate.Runs)
        {
            return false;
        }

        return rate.Upper - rate.Lower > targetIntervalWidth;
    }
}
