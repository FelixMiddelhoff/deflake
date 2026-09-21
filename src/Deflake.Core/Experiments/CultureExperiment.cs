using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Deflake.Core.Execution;

namespace Deflake.Core.Experiments;

/// <summary>
/// Runs the failing test under different culture settings, to see whether culture (locale, number
/// formatting, date formatting, sorting order, etc.) affects the failure rate. Each culture is set
/// via the DOTNET_CULTURE environment variable.
/// </summary>
public sealed class CultureExperiment : Experiment
{
    /// <summary>The factor this experiment varies.</summary>
    public const string CultureFactor = "Culture";

    /// <summary>Default set of cultures to test, if none is provided.</summary>
    private static readonly IReadOnlyList<string> DefaultCultures = new[]
    {
        "en-US",
        "de-DE",
        "ja-JP",
    };

    private readonly IReadOnlyList<string> _cultures;

    /// <param name="cultures">
    /// The culture names to test (e.g., "en-US", "de-DE", "ja-JP"). If null or empty, uses the default set.
    /// Culture names are passed as-is to the DOTNET_CULTURE environment variable.
    /// </param>
    public CultureExperiment(IReadOnlyList<string>? cultures = null)
    {
        _cultures = (cultures is null || cultures.Count == 0) ? DefaultCultures : cultures;
    }

    public override string Name => "Culture";

    public override string Description =>
        "Runs the failing test under different culture settings (locale, number/date formatting, etc.) "
        + "to see whether culture affects the failure rate.";

    protected override async Task<ExperimentResult> RunCoreAsync(TestRunContext context, CancellationToken cancellationToken)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        var budget = ExperimentBudget.Start(context);
        var runs = new List<ExperimentRun>();
        string? incompleteReason = null;

        foreach (var culture in _cultures)
        {
            var request = CreateRequestWithCulture(context, culture);
            var run = await MeasureAsync(context, culture, request, budget, cancellationToken)
                .ConfigureAwait(false);
            runs.Add(run);

            if (run.IncompleteReason is not null)
            {
                incompleteReason = $"The experiment stopped at culture '{culture}'. {run.IncompleteReason}";
                break;
            }
        }

        return new ExperimentResult(
            Name,
            CultureFactor,
            runs,
            completed: incompleteReason is null,
            incompleteReason);
    }

    /// <summary>Creates a request with a specific culture set via environment variable.</summary>
    private static TestRunRequest CreateRequestWithCulture(TestRunContext context, string culture)
    {
        var env = context.BaselineRequest.EnvironmentVariables is not null
            ? new Dictionary<string, string>(context.BaselineRequest.EnvironmentVariables)
            : new Dictionary<string, string>();

        env["DOTNET_CULTURE"] = culture;

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
