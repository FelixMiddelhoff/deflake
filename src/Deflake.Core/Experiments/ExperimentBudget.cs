using System;

namespace Deflake.Core.Experiments;

/// <summary>
/// The wall-clock budget of one experiment, shared by all of its conditions: the scope ladder's four
/// steps draw on one budget, so a slow first step cannot be paid for twice. Started once per
/// experiment run, never reused.
/// </summary>
public sealed class ExperimentBudget
{
    private readonly TimeProvider _timeProvider;
    private readonly long _startedAt;

    private ExperimentBudget(TimeProvider timeProvider, TimeSpan? limit)
    {
        _timeProvider = timeProvider;
        _startedAt = timeProvider.GetTimestamp();
        Limit = limit;
    }

    /// <summary>Starts the clock now, with the context's budget and clock.</summary>
    public static ExperimentBudget Start(TestRunContext context)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        return new ExperimentBudget(context.TimeProvider, context.TotalTimeout);
    }

    /// <summary>The budget, or null when only the run count limits the experiment.</summary>
    public TimeSpan? Limit { get; }

    public TimeSpan Elapsed => _timeProvider.GetElapsedTime(_startedAt, _timeProvider.GetTimestamp());

    public bool IsExhausted => Limit is { } limit && Elapsed >= limit;

    public override string ToString()
    {
        return Limit is { } limit
            ? $"{Elapsed} of {limit}"
            : $"{Elapsed}, no limit";
    }
}
