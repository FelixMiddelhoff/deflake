using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Deflake.Core.Execution;
using Deflake.Core.Experiments;
using Deflake.Core.Trx;
using Xunit;
using static Deflake.Core.Tests.ExperimentTestSupport;

namespace Deflake.Core.Tests;

public class ParallelismExperimentTests
{
    [Fact]
    public async Task The_experiment_runs_serial_then_parallel()
    {
        var runner = new FakeTestRunner().Queue(20, TestPassed()).Queue(20, TestPassed());

        var result = await new ParallelismExperiment().RunAsync(Context(runner));

        Assert.Equal(40, runner.Requests.Count);
        Assert.Equal(2, result.Runs.Count);
        Assert.Equal(ParallelismExperiment.Serial, result.Runs[0].FactorValue);
        Assert.Equal(ParallelismExperiment.Parallel, result.Runs[1].FactorValue);
    }

    [Fact]
    public async Task The_experiment_has_the_parallelism_factor()
    {
        var runner = new FakeTestRunner().Queue(20, TestPassed()).Queue(20, TestPassed());

        var result = await new ParallelismExperiment().RunAsync(Context(runner));

        Assert.Equal(ParallelismExperiment.ParallelismFactor, result.Factor);
        Assert.Equal("Parallelism", result.Name);
    }

    [Fact]
    public async Task Serial_runs_have_runsettings_with_parallelism_disabled()
    {
        var runner = new FakeTestRunner().Queue(20, TestPassed()).Queue(20, TestPassed());

        await new ParallelismExperiment().RunAsync(Context(runner));

        var serialRequest = runner.Requests[0];
        Assert.NotNull(serialRequest.RunSettingsPath);
        Assert.True(File.Exists(serialRequest.RunSettingsPath));

        var doc = XDocument.Load(serialRequest.RunSettingsPath);
        var runConfig = doc.Root?.Element("RunConfiguration");
        var maxCpuCount = runConfig?.Element("MaxCpuCount");
        Assert.NotNull(maxCpuCount);
        Assert.Equal("1", maxCpuCount.Value);

        TryCleanupRunsettings(serialRequest.RunSettingsPath);
    }

    [Fact]
    public async Task Parallel_runs_have_runsettings_with_parallelism_enabled()
    {
        var runner = new FakeTestRunner().Queue(20, TestPassed()).Queue(20, TestPassed());

        await new ParallelismExperiment().RunAsync(Context(runner));

        var parallelRequest = runner.Requests[20]; // Parallel runs start at request 20
        Assert.NotNull(parallelRequest.RunSettingsPath);
        Assert.True(File.Exists(parallelRequest.RunSettingsPath));

        var doc = XDocument.Load(parallelRequest.RunSettingsPath);
        var runConfig = doc.Root?.Element("RunConfiguration");
        var maxCpuCount = runConfig?.Element("MaxCpuCount");
        Assert.NotNull(maxCpuCount);
        Assert.Equal("0", maxCpuCount.Value);

        TryCleanupRunsettings(parallelRequest.RunSettingsPath);
    }

    [Fact]
    public async Task The_runsettings_include_framework_specific_settings()
    {
        var runner = new FakeTestRunner().Queue(20, TestPassed()).Queue(20, TestPassed());

        await new ParallelismExperiment().RunAsync(Context(runner));

        var serialRequest = runner.Requests[0];
        var doc = XDocument.Load(serialRequest.RunSettingsPath!);
        var testRunParams = doc.Root?.Element("TestRunParameters");
        Assert.NotNull(testRunParams);

        // Check for xUnit settings
        var xUnitParallelize = testRunParams.Elements("Parameter")
            .FirstOrDefault(p => p.Attribute("name")?.Value == "xUnit.ParallelizeTestCollections");
        Assert.NotNull(xUnitParallelize);
        Assert.Equal("False", xUnitParallelize.Attribute("value")?.Value);

        // Check for NUnit settings
        var nunitWorkers = testRunParams.Elements("Parameter")
            .FirstOrDefault(p => p.Attribute("name")?.Value == "NUnit.NumberOfTestWorkers");
        Assert.NotNull(nunitWorkers);
        Assert.Equal("1", nunitWorkers.Attribute("value")?.Value);

        TryCleanupRunsettings(serialRequest.RunSettingsPath);
    }

    [Fact]
    public async Task Everything_except_runsettings_is_taken_from_baseline()
    {
        var runner = new FakeTestRunner().Queue(20, TestPassed()).Queue(20, TestPassed());
        var baseline = Baseline("Category=Slow");
        var context = new TestRunContext(runner, baseline, Identity());

        await new ParallelismExperiment().RunAsync(context);

        var serialRequest = runner.Requests[0];
        Assert.Equal(baseline.TargetPath, serialRequest.TargetPath);
        Assert.Equal(baseline.Filter, serialRequest.Filter);
        Assert.Equal(baseline.Timeout, serialRequest.Timeout);
        Assert.Equal(baseline.WorkingDirectory, serialRequest.WorkingDirectory);
        Assert.NotEqual(baseline.RunSettingsPath, serialRequest.RunSettingsPath);

        TryCleanupRunsettings(serialRequest.RunSettingsPath!);
    }

    [Fact]
    public async Task Baseline_environment_variables_are_preserved()
    {
        var runner = new FakeTestRunner().Queue(20, TestPassed()).Queue(20, TestPassed());
        var baseline = new TestRunRequest(TargetPath)
        {
            EnvironmentVariables = new Dictionary<string, string>
            {
                ["MY_VAR"] = "my_value",
            },
        };
        var context = new TestRunContext(runner, baseline, Identity());

        await new ParallelismExperiment().RunAsync(context);

        var serialRequest = runner.Requests[0];
        Assert.NotNull(serialRequest.EnvironmentVariables);
        Assert.True(serialRequest.EnvironmentVariables.ContainsKey("MY_VAR"));
        Assert.Equal("my_value", serialRequest.EnvironmentVariables["MY_VAR"]);

        TryCleanupRunsettings(serialRequest.RunSettingsPath!);
    }

    [Fact]
    public async Task Failures_in_serial_are_counted()
    {
        var run = 0;
        var runner = new FakeTestRunner()
            .Queue(20, _ => run++ < 5 ? TestFailed() : TestPassed())
            .Queue(20, TestPassed());

        var result = await new ParallelismExperiment().RunAsync(Context(runner));

        var serialRun = result.Runs[0];
        Assert.Equal(5, serialRun.Failures);
        Assert.Equal(15, serialRun.Passes);
        Assert.True(result.Completed);

        TryCleanupRunsettings(runner.Requests[0].RunSettingsPath!);
        TryCleanupRunsettings(runner.Requests[20].RunSettingsPath!);
    }

    [Fact]
    public async Task Serial_failure_does_not_stop_the_experiment()
    {
        var runner = new FakeTestRunner()
            .Queue(20, TestFailed())
            .Queue(20, TestPassed());

        var result = await new ParallelismExperiment().RunAsync(Context(runner));

        Assert.Equal(2, result.Runs.Count);
        Assert.True(result.Completed);
        Assert.Equal(20, result.Runs[0].Failures);
        Assert.Equal(0, result.Runs[1].Failures);

        TryCleanupRunsettings(runner.Requests[0].RunSettingsPath!);
        TryCleanupRunsettings(runner.Requests[20].RunSettingsPath!);
    }

    [Fact]
    public async Task A_timeout_in_serial_stops_the_experiment()
    {
        var runner = new FakeTestRunner()
            .Returns(_ => throw new TestRunTimeoutException(TimeSpan.FromMinutes(3), "timeout"));

        var result = await new ParallelismExperiment().RunAsync(Context(runner));

        Assert.Single(result.Runs);
        Assert.False(result.Completed);
        Assert.Contains("Serial", result.IncompleteReason!);
    }

    [Fact]
    public async Task A_timeout_in_parallel_stops_the_experiment_after_serial()
    {
        var runner = new FakeTestRunner()
            .Queue(20, TestPassed())
            .Returns(_ => throw new TestRunTimeoutException(TimeSpan.FromMinutes(3), "timeout"));

        var result = await new ParallelismExperiment().RunAsync(Context(runner));

        Assert.Equal(2, result.Runs.Count);
        Assert.False(result.Completed);
        Assert.Contains("Parallel", result.IncompleteReason!);

        TryCleanupRunsettings(runner.Requests[0].RunSettingsPath!);
    }

    [Fact]
    public async Task The_description_explains_what_the_experiment_does()
    {
        var experiment = new ParallelismExperiment();

        Assert.NotEmpty(experiment.Description);
        Assert.Contains("parallel", experiment.Description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("serial", experiment.Description, StringComparison.OrdinalIgnoreCase);
    }

    private static void TryCleanupRunsettings(string? path)
    {
        if (path is not null && File.Exists(path))
        {
            try
            {
                File.Delete(path);
            }
            catch
            {
                // Ignore cleanup errors in tests.
            }
        }
    }
}
