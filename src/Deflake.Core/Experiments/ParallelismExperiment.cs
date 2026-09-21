using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Deflake.Core.Execution;

namespace Deflake.Core.Experiments;

/// <summary>
/// Runs the failing test with parallelism off and on, to see whether disabling parallel test
/// execution changes the failure rate. Parallelism is controlled via runsettings, which each
/// framework interprets in its own way: xUnit via ParallelizeTestCollections, NUnit via
/// NumberOfTestWorkers, and MSTest via MaxCpuCount.
/// </summary>
public sealed class ParallelismExperiment : Experiment
{
    /// <summary>The factor this experiment varies.</summary>
    public const string ParallelismFactor = "Parallelism";

    /// <summary>Condition: tests run serially (one at a time).</summary>
    public const string Serial = "Serial";

    /// <summary>Condition: tests run in parallel.</summary>
    public const string Parallel = "Parallel";

    public override string Name => "Parallelism";

    public override string Description =>
        "Runs the failing test with parallelism disabled (Serial) and enabled (Parallel) to see whether "
        + "parallel execution affects the failure rate.";

    protected override async Task<ExperimentResult> RunCoreAsync(TestRunContext context, CancellationToken cancellationToken)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        var budget = ExperimentBudget.Start(context);
        var runs = new List<ExperimentRun>();
        string? incompleteReason = null;

        // Run serial
        var serialRequest = CreateRequestWithParallelism(context, parallelism: false);
        var serialRun = await MeasureAsync(context, Serial, serialRequest, budget, cancellationToken)
            .ConfigureAwait(false);
        runs.Add(serialRun);

        if (serialRun.IncompleteReason is not null)
        {
            incompleteReason = $"The experiment stopped after {Serial}. {serialRun.IncompleteReason}";
            return new ExperimentResult(Name, ParallelismFactor, runs, completed: false, incompleteReason);
        }

        // Run parallel
        var parallelRequest = CreateRequestWithParallelism(context, parallelism: true);
        var parallelRun = await MeasureAsync(context, Parallel, parallelRequest, budget, cancellationToken)
            .ConfigureAwait(false);
        runs.Add(parallelRun);

        if (parallelRun.IncompleteReason is not null)
        {
            incompleteReason = $"The experiment stopped after {Parallel}. {parallelRun.IncompleteReason}";
        }

        return new ExperimentResult(
            Name,
            ParallelismFactor,
            runs,
            completed: incompleteReason is null,
            incompleteReason);
    }

    /// <summary>
    /// Creates a request with a modified runsettings file that controls parallelism. If the baseline
    /// has no runsettings, creates a new one. Otherwise, clones and modifies it.
    /// </summary>
    private static TestRunRequest CreateRequestWithParallelism(TestRunContext context, bool parallelism)
    {
        var runsettingsPath = CreateRunsettingsWithParallelism(context.BaselineRequest.RunSettingsPath, parallelism);

        var env = context.BaselineRequest.EnvironmentVariables is not null
            ? new Dictionary<string, string>(context.BaselineRequest.EnvironmentVariables)
            : new Dictionary<string, string>();

        return new TestRunRequest(context.BaselineRequest.TargetPath)
        {
            Filter = context.BaselineRequest.Filter,
            RunSettingsPath = runsettingsPath,
            EnvironmentVariables = env,
            Timeout = context.BaselineRequest.Timeout,
            WorkingDirectory = context.BaselineRequest.WorkingDirectory,
        };
    }

    /// <summary>
    /// Creates a runsettings file with parallelism enabled or disabled. If the baseline already has
    /// a runsettings file, loads and modifies it; otherwise, creates a new one. Returns the path
    /// to the temporary runsettings file.
    /// </summary>
    private static string CreateRunsettingsWithParallelism(string? baselineRunsettingsPath, bool parallelism)
    {
        XDocument doc;

        if (!string.IsNullOrEmpty(baselineRunsettingsPath) && File.Exists(baselineRunsettingsPath))
        {
            doc = XDocument.Load(baselineRunsettingsPath);
        }
        else
        {
            // Create a new minimal runsettings
            doc = new XDocument(
                new XDeclaration("1.0", "utf-8", null),
                new XElement("RunSettings"));
        }

        var root = doc.Root ?? throw new InvalidOperationException("RunSettings root is missing.");

        // Ensure RunConfiguration section exists
        var runConfig = root.Element("RunConfiguration");
        if (runConfig is null)
        {
            runConfig = new XElement("RunConfiguration");
            root.Add(runConfig);
        }

        // Add MSTest configuration (MaxCpuCount)
        var maxCpuCount = runConfig.Element("MaxCpuCount");
        if (maxCpuCount is null)
        {
            maxCpuCount = new XElement("MaxCpuCount");
            runConfig.Add(maxCpuCount);
        }
        maxCpuCount.Value = parallelism ? "0" : "1"; // 0 = use all, 1 = serial

        // Ensure TestRunParameters section exists for xUnit/NUnit settings
        var testRunParams = root.Element("TestRunParameters");
        if (testRunParams is null)
        {
            testRunParams = new XElement("TestRunParameters");
            root.Add(testRunParams);
        }

        // xUnit configuration
        SetParameter(testRunParams, "xUnit.ParallelizeTestCollections", parallelism.ToString());
        SetParameter(testRunParams, "xUnit.MaxParallelThreads", parallelism ? "0" : "1");

        // NUnit configuration
        SetParameter(testRunParams, "NUnit.NumberOfTestWorkers", parallelism ? "-1" : "1");

        // Write to a temporary file
        var tempPath = Path.Combine(Path.GetTempPath(), $"deflake-runsettings-{Guid.NewGuid():N}.xml");
        doc.Save(tempPath);

        return tempPath;
    }

    /// <summary>Sets or updates a TestRunParameters parameter.</summary>
    private static void SetParameter(XElement testRunParams, string name, string value)
    {
        var param = testRunParams.Element("Parameter");
        while (param is not null)
        {
            var nameAttr = param.Attribute("name");
            if (nameAttr?.Value == name)
            {
                param.SetAttributeValue("value", value);
                return;
            }

            param = param.NextNode as XElement;
            if (param?.Name.LocalName != "Parameter")
            {
                param = null;
            }
        }

        // Parameter not found, add it
        testRunParams.Add(
            new XElement("Parameter",
                new XAttribute("name", name),
                new XAttribute("value", value)));
    }
}
