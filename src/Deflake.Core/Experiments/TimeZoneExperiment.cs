using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Deflake.Core.Execution;

namespace Deflake.Core.Experiments;

/// <summary>
/// Runs the failing test under different timezone settings, to see whether timezone affects the
/// failure rate. Timezone is set via the TZ environment variable, which works on POSIX systems
/// (Linux and macOS) but not on Windows. On Windows, the experiment reports itself incomplete
/// with a note that timezone testing is not supported.
/// </summary>
public sealed class TimeZoneExperiment : Experiment
{
    /// <summary>The factor this experiment varies.</summary>
    public const string TimeZoneFactor = "TimeZone";

    /// <summary>Default set of timezones to test, if none is provided.</summary>
    private static readonly IReadOnlyList<string> DefaultTimeZones = new[]
    {
        "UTC",
        "America/New_York",
        "Asia/Tokyo",
    };

    private readonly IReadOnlyList<string> _timeZones;

    /// <param name="timeZones">
    /// The IANA timezone names to test (e.g., "UTC", "America/New_York", "Asia/Tokyo").
    /// If null or empty, uses the default set. Timezone names are passed as-is to the TZ environment variable.
    /// </param>
    public TimeZoneExperiment(IReadOnlyList<string>? timeZones = null)
    {
        _timeZones = (timeZones is null || timeZones.Count == 0) ? DefaultTimeZones : timeZones;
    }

    public override string Name => "TimeZone";

    public override string Description =>
        "Runs the failing test under different timezone settings to see whether timezone affects the failure rate. "
        + "Note: timezone testing via TZ environment variable only works on POSIX systems (Linux/macOS); on Windows it is unsupported.";

    protected override async Task<ExperimentResult> RunCoreAsync(TestRunContext context, CancellationToken cancellationToken)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        // On Windows, TZ environment variable is not respected by .NET. Report the experiment as unsupported.
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return new ExperimentResult(
                Name,
                TimeZoneFactor,
                Array.Empty<ExperimentRun>(),
                completed: false,
                "TimeZone testing via TZ environment variable is not supported on Windows.");
        }

        var budget = ExperimentBudget.Start(context);
        var runs = new List<ExperimentRun>();
        string? incompleteReason = null;

        foreach (var timeZone in _timeZones)
        {
            var request = CreateRequestWithTimeZone(context, timeZone);
            var run = await MeasureAsync(context, timeZone, request, budget, cancellationToken)
                .ConfigureAwait(false);
            runs.Add(run);

            if (run.IncompleteReason is not null)
            {
                incompleteReason = $"The experiment stopped at timezone '{timeZone}'. {run.IncompleteReason}";
                break;
            }
        }

        return new ExperimentResult(
            Name,
            TimeZoneFactor,
            runs,
            completed: incompleteReason is null,
            incompleteReason);
    }

    /// <summary>Creates a request with a specific timezone set via the TZ environment variable.</summary>
    private static TestRunRequest CreateRequestWithTimeZone(TestRunContext context, string timeZone)
    {
        var env = context.BaselineRequest.EnvironmentVariables is not null
            ? new Dictionary<string, string>(context.BaselineRequest.EnvironmentVariables)
            : new Dictionary<string, string>();

        env["TZ"] = timeZone;

        return new TestRunRequest(context.BaselineRequest.TargetPath)
        {
            Filter = context.BaselineRequest.Filter,
            RunSettingsPath = context.BaselineRequest.RunSettingsPath,
            EnvironmentVariables = env,
            Timeout = context.BaselineRequest.Timeout,
            WorkingDirectory = context.BaselineRequest.WorkingDirectory,
        };
    }
}
