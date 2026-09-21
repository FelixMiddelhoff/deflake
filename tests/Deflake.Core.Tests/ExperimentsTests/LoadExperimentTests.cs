using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Deflake.Core.Execution;
using Deflake.Core.Experiments;
using Deflake.Core.Trx;
using Xunit;
using static Deflake.Core.Tests.ExperimentTestSupport;

namespace Deflake.Core.Tests;

public class LoadExperimentTests
{
    [Fact]
    public async Task The_experiment_runs_idle_then_under_load()
    {
        var runner = new FakeTestRunner().Queue(20, TestPassed()).Queue(20, TestPassed());

        var result = await new LoadExperiment().RunAsync(Context(runner));

        Assert.Equal(40, runner.Requests.Count);
        Assert.Equal(2, result.Runs.Count);
        Assert.Equal(LoadExperiment.Idle, result.Runs[0].FactorValue);
        Assert.Equal(LoadExperiment.UnderLoad, result.Runs[1].FactorValue);
    }

    [Fact]
    public async Task The_experiment_has_the_load_factor()
    {
        var runner = new FakeTestRunner().Queue(20, TestPassed()).Queue(20, TestPassed());

        var result = await new LoadExperiment().RunAsync(Context(runner));

        Assert.Equal(LoadExperiment.LoadFactor, result.Factor);
        Assert.Equal("Load", result.Name);
    }

    [Fact]
    public async Task Everything_else_is_taken_from_baseline()
    {
        var runner = new FakeTestRunner().Queue(20, TestPassed()).Queue(20, TestPassed());
        var baseline = Baseline("Category=Slow");
        var context = new TestRunContext(runner, baseline, Identity());

        await new LoadExperiment().RunAsync(context);

        var idleRequest = runner.Requests[0];
        Assert.Equal(baseline.TargetPath, idleRequest.TargetPath);
        Assert.Equal(baseline.Filter, idleRequest.Filter);
        Assert.Equal(baseline.RunSettingsPath, idleRequest.RunSettingsPath);
        Assert.Equal(baseline.Timeout, idleRequest.Timeout);
        Assert.Equal(baseline.WorkingDirectory, idleRequest.WorkingDirectory);
        Assert.Equal(baseline.EnvironmentVariables, idleRequest.EnvironmentVariables);

        var underLoadRequest = runner.Requests[20];
        Assert.Equal(baseline.TargetPath, underLoadRequest.TargetPath);
        Assert.Equal(baseline.Filter, underLoadRequest.Filter);
    }

    [Fact]
    public async Task Failures_in_idle_are_counted()
    {
        var run = 0;
        var runner = new FakeTestRunner()
            .Queue(20, _ => run++ < 5 ? TestFailed() : TestPassed())
            .Queue(20, TestPassed());

        var result = await new LoadExperiment().RunAsync(Context(runner));

        var idleRun = result.Runs[0];
        Assert.Equal(5, idleRun.Failures);
        Assert.Equal(15, idleRun.Passes);
    }

    [Fact]
    public async Task Idle_failure_does_not_stop_the_experiment()
    {
        var runner = new FakeTestRunner()
            .Queue(20, TestFailed())
            .Queue(20, TestPassed());

        var result = await new LoadExperiment().RunAsync(Context(runner));

        Assert.Equal(2, result.Runs.Count);
        Assert.True(result.Completed);
        Assert.Equal(20, result.Runs[0].Failures);
        Assert.Equal(0, result.Runs[1].Failures);
    }

    [Fact]
    public async Task Load_changes_failure_rate()
    {
        var run = 0;
        var runner = new FakeTestRunner()
            .Queue(20, TestPassed()) // Idle: always passes
            .Queue(20, _ => run++ < 10 ? TestFailed() : TestPassed()); // Under load: 50% failure

        var result = await new LoadExperiment().RunAsync(Context(runner));

        var idleRun = result.Runs[0];
        var loadRun = result.Runs[1];

        Assert.Equal(0, idleRun.Failures);
        Assert.Equal(10, loadRun.Failures);
        Assert.True(result.Completed);
    }

    [Fact]
    public async Task A_timeout_in_idle_stops_the_experiment()
    {
        var runner = new FakeTestRunner()
            .Returns(_ => throw new TestRunTimeoutException(TimeSpan.FromMinutes(3), "timeout"));

        var result = await new LoadExperiment().RunAsync(Context(runner));

        Assert.Single(result.Runs);
        Assert.False(result.Completed);
        Assert.Contains("Idle", result.IncompleteReason!);
    }

    [Fact]
    public async Task A_timeout_in_under_load_stops_the_experiment_after_idle()
    {
        var runner = new FakeTestRunner()
            .Queue(20, TestPassed())
            .Returns(_ => throw new TestRunTimeoutException(TimeSpan.FromMinutes(3), "timeout"));

        var result = await new LoadExperiment().RunAsync(Context(runner));

        Assert.Equal(2, result.Runs.Count);
        Assert.False(result.Completed);
        Assert.Contains("UnderLoad", result.IncompleteReason!);
    }

    [Fact]
    public async Task The_description_explains_what_the_experiment_does()
    {
        var experiment = new LoadExperiment();

        Assert.NotEmpty(experiment.Description);
        Assert.Contains("load", experiment.Description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("contention", experiment.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_missing_context_is_refused()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => new LoadExperiment().RunAsync(null!));
    }

    [Fact]
    public async Task Missing_baseline_environment_variables_start_with_empty_dict()
    {
        var baseline = new TestRunRequest(TargetPath);
        var runner = new FakeTestRunner().Queue(20, TestPassed()).Queue(20, TestPassed());
        var context = new TestRunContext(runner, baseline, Identity());

        var result = await new LoadExperiment().RunAsync(context);

        // Both requests should have been made successfully
        Assert.Equal(40, runner.Requests.Count);
        Assert.True(result.Completed);
    }
}
