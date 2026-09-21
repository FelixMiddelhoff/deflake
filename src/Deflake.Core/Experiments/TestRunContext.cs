using System;
using System.Threading;
using Deflake.Core.Execution;

namespace Deflake.Core.Experiments;

/// <summary>
/// Everything an experiment needs to ask its question: who runs tests, the baseline scope to change
/// exactly one thing about, which test is being investigated, and how many runs and how much time it
/// may spend doing so. The same context is given to every experiment of one investigation, which is
/// what keeps them comparable.
/// </summary>
public sealed class TestRunContext
{
    /// <summary>Runs per condition before the adaptive part starts; the design's default.</summary>
    public const int DefaultInitialRuns = 20;

    private readonly int _initialRuns = DefaultInitialRuns;
    private readonly int _maxRuns = DefaultInitialRuns;
    private readonly double _targetIntervalWidth = AdaptiveRuns.DefaultTargetIntervalWidth;
    private readonly TimeProvider _timeProvider = TimeProvider.System;
    private readonly TimeSpan? _totalTimeout;

    public TestRunContext(ITestRunner runner, TestRunRequest baselineRequest, TestIdentity failingTest)
    {
        Runner = runner ?? throw new ArgumentNullException(nameof(runner));
        BaselineRequest = baselineRequest ?? throw new ArgumentNullException(nameof(baselineRequest));
        FailingTest = failingTest ?? throw new ArgumentNullException(nameof(failingTest));
    }

    public ITestRunner Runner { get; }

    /// <summary>
    /// The run the failure was seen in: the scope, run settings, environment and timeouts every
    /// experiment starts from. Experiments derive their own requests from it and change one thing.
    /// </summary>
    public TestRunRequest BaselineRequest { get; }

    /// <summary>The test being investigated.</summary>
    public TestIdentity FailingTest { get; }

    /// <summary>How many times each condition is run before the result is judged. At least 1.</summary>
    public int InitialRuns
    {
        get => _initialRuns;
        init
        {
            if (value < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "At least one run per condition is needed.");
            }

            _initialRuns = value;
        }
    }

    /// <summary>
    /// The ceiling on runs per condition when a borderline result earns extra ones. Equal to
    /// <see cref="InitialRuns"/> by default, which means "no extra runs".
    /// </summary>
    public int MaxRuns
    {
        get => _maxRuns;
        init
        {
            if (value < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "At least one run per condition is needed.");
            }

            _maxRuns = value;
        }
    }

    /// <summary>The interval width at which extra runs stop being worth their time.</summary>
    public double TargetIntervalWidth
    {
        get => _targetIntervalWidth;
        init
        {
            if (value <= 0 || value > 1)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "The target interval width must be greater than 0 and at most 1.");
            }

            _targetIntervalWidth = value;
        }
    }

    /// <summary>
    /// Wall-clock budget for one whole experiment, across all of its conditions. It is checked
    /// before each run, so it never interrupts a run in flight; null means only the run count limits.
    /// </summary>
    public TimeSpan? TotalTimeout
    {
        get => _totalTimeout;
        init
        {
            if (value is { } timeout && timeout <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "The total timeout must be greater than zero.");
            }

            _totalTimeout = value;
        }
    }

    /// <summary>The investigation's token: cancelling it stops every experiment that shares this context.</summary>
    public CancellationToken CancellationToken { get; init; }

    /// <summary>The clock the time budget is measured with. Tests replace it; production never does.</summary>
    public TimeProvider TimeProvider
    {
        get => _timeProvider;
        init => _timeProvider = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>
    /// The baseline request with a different scope and nothing else changed, which is how an
    /// experiment keeps to one factor at a time. A null filter runs the whole target.
    /// </summary>
    public TestRunRequest RequestWithFilter(string? filter)
    {
        return new TestRunRequest(BaselineRequest.TargetPath)
        {
            Filter = filter,
            RunSettingsPath = BaselineRequest.RunSettingsPath,
            EnvironmentVariables = BaselineRequest.EnvironmentVariables,
            Timeout = BaselineRequest.Timeout,
            WorkingDirectory = BaselineRequest.WorkingDirectory,
        };
    }
}
