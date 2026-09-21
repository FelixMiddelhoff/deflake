using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Deflake.Core.Execution;
using Deflake.Core.Experiments;
using Deflake.Core.Trx;
using Xunit;
using static Deflake.Core.Tests.ExperimentTestSupport;

namespace Deflake.Core.Tests;

public class CultureExperimentTests
{
    [Fact]
    public async Task The_experiment_runs_all_provided_cultures()
    {
        var cultures = new[] { "en-US", "de-DE", "ja-JP" };
        var runner = new FakeTestRunner()
            .Queue(20, TestPassed())
            .Queue(20, TestPassed())
            .Queue(20, TestPassed());

        var result = await new CultureExperiment(cultures).RunAsync(Context(runner));

        Assert.Equal(60, runner.Requests.Count);
        Assert.Equal(3, result.Runs.Count);
        Assert.Equal("en-US", result.Runs[0].FactorValue);
        Assert.Equal("de-DE", result.Runs[1].FactorValue);
        Assert.Equal("ja-JP", result.Runs[2].FactorValue);
    }

    [Fact]
    public async Task The_experiment_has_the_culture_factor()
    {
        var runner = new FakeTestRunner().Queue(20, TestPassed()).Queue(20, TestPassed()).Queue(20, TestPassed());

        var result = await new CultureExperiment(new[] { "en-US", "de-DE", "ja-JP" }).RunAsync(Context(runner));

        Assert.Equal("Culture", result.Factor);
        Assert.Equal("Culture", result.Name);
    }

    [Fact]
    public async Task Null_cultures_uses_the_default_set()
    {
        var runner = new FakeTestRunner()
            .Queue(20, TestPassed())
            .Queue(20, TestPassed())
            .Queue(20, TestPassed());

        var result = await new CultureExperiment(null).RunAsync(Context(runner));

        // Default is 3 cultures
        Assert.Equal(3, result.Runs.Count);
    }

    [Fact]
    public async Task Empty_cultures_uses_the_default_set()
    {
        var runner = new FakeTestRunner()
            .Queue(20, TestPassed())
            .Queue(20, TestPassed())
            .Queue(20, TestPassed());

        var result = await new CultureExperiment(Array.Empty<string>()).RunAsync(Context(runner));

        Assert.Equal(3, result.Runs.Count);
    }

    [Fact]
    public async Task Each_culture_is_set_via_environment_variable()
    {
        var cultures = new[] { "en-US", "de-DE" };
        var runner = new FakeTestRunner().Queue(20, TestPassed()).Queue(20, TestPassed());

        await new CultureExperiment(cultures).RunAsync(Context(runner));

        var request1 = runner.Requests[0];
        Assert.NotNull(request1.EnvironmentVariables);
        Assert.Equal("en-US", request1.EnvironmentVariables["DOTNET_CULTURE"]);

        var request2 = runner.Requests[20];
        Assert.NotNull(request2.EnvironmentVariables);
        Assert.Equal("de-DE", request2.EnvironmentVariables["DOTNET_CULTURE"]);
    }

    [Fact]
    public async Task Baseline_environment_variables_are_preserved()
    {
        var baseline = new TestRunRequest(TargetPath)
        {
            EnvironmentVariables = new Dictionary<string, string>
            {
                ["MY_VAR"] = "my_value",
            },
        };
        var runner = new FakeTestRunner().Queue(20, TestPassed()).Queue(20, TestPassed());
        var context = new TestRunContext(runner, baseline, Identity());

        await new CultureExperiment(new[] { "en-US", "de-DE" }).RunAsync(context);

        var request = runner.Requests[0];
        Assert.NotNull(request.EnvironmentVariables);
        Assert.True(request.EnvironmentVariables.ContainsKey("MY_VAR"));
        Assert.Equal("my_value", request.EnvironmentVariables["MY_VAR"]);
        Assert.True(request.EnvironmentVariables.ContainsKey("DOTNET_CULTURE"));
    }

    [Fact]
    public async Task Everything_else_is_taken_from_baseline()
    {
        var baseline = Baseline("Category=Slow");
        var runner = new FakeTestRunner().Queue(20, TestPassed()).Queue(20, TestPassed());
        var context = new TestRunContext(runner, baseline, Identity());

        await new CultureExperiment(new[] { "en-US", "de-DE" }).RunAsync(context);

        var request = runner.Requests[0];
        Assert.Equal(baseline.TargetPath, request.TargetPath);
        Assert.Equal(baseline.Filter, request.Filter);
        Assert.Equal(baseline.RunSettingsPath, request.RunSettingsPath);
        Assert.Equal(baseline.Timeout, request.Timeout);
        Assert.Equal(baseline.WorkingDirectory, request.WorkingDirectory);
    }

    [Fact]
    public async Task Failures_in_one_culture_are_counted()
    {
        var run = 0;
        var runner = new FakeTestRunner()
            .Queue(20, TestPassed())
            .Queue(20, _ => run++ < 7 ? TestFailed() : TestPassed());

        var result = await new CultureExperiment(new[] { "en-US", "de-DE" }).RunAsync(Context(runner));

        Assert.Equal(0, result.Runs[0].Failures);
        Assert.Equal(7, result.Runs[1].Failures);
    }

    [Fact]
    public async Task A_culture_failure_does_not_stop_the_experiment()
    {
        var runner = new FakeTestRunner()
            .Queue(20, TestFailed())
            .Queue(20, TestPassed())
            .Queue(20, TestPassed());

        var result = await new CultureExperiment(new[] { "en-US", "de-DE", "ja-JP" }).RunAsync(Context(runner));

        Assert.Equal(3, result.Runs.Count);
        Assert.True(result.Completed);
    }

    [Fact]
    public async Task A_timeout_in_one_culture_stops_the_experiment_there()
    {
        var runner = new FakeTestRunner()
            .Queue(20, TestPassed())
            .Returns(_ => throw new TestRunTimeoutException(TimeSpan.FromMinutes(3), "timeout"));

        var result = await new CultureExperiment(new[] { "en-US", "de-DE", "ja-JP" }).RunAsync(Context(runner));

        Assert.Equal(2, result.Runs.Count);
        Assert.False(result.Completed);
        Assert.Contains("de-DE", result.IncompleteReason!);
    }

    [Fact]
    public async Task The_description_explains_what_the_experiment_does()
    {
        var experiment = new CultureExperiment();

        Assert.NotEmpty(experiment.Description);
        Assert.Contains("culture", experiment.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_missing_context_is_refused()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => new CultureExperiment(new[] { "en-US" }).RunAsync(null!));
    }

    [Fact]
    public async Task Single_culture_is_supported()
    {
        var runner = new FakeTestRunner().Queue(20, TestPassed());

        var result = await new CultureExperiment(new[] { "en-US" }).RunAsync(Context(runner));

        Assert.Single(result.Runs);
        Assert.Equal("en-US", result.Runs[0].FactorValue);
    }

    [Fact]
    public async Task Many_cultures_are_supported()
    {
        var cultures = new[] { "en-US", "de-DE", "ja-JP", "fr-FR", "zh-CN" };
        var runner = new FakeTestRunner();
        foreach (var _ in cultures)
        {
            runner.Queue(20, TestPassed());
        }

        var result = await new CultureExperiment(cultures).RunAsync(Context(runner));

        Assert.Equal(5, result.Runs.Count);
        Assert.True(result.Completed);
    }
}
