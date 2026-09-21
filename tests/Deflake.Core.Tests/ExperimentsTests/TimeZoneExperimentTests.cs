using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Deflake.Core.Execution;
using Deflake.Core.Experiments;
using Deflake.Core.Trx;
using Xunit;
using static Deflake.Core.Tests.ExperimentTestSupport;

namespace Deflake.Core.Tests;

public class TimeZoneExperimentTests
{
    [Fact]
    public async Task On_posix_systems_the_experiment_runs_all_provided_timezones()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return; // Skip on Windows; this test is for POSIX systems.
        }

        var timeZones = new[] { "UTC", "America/New_York", "Asia/Tokyo" };
        var runner = new FakeTestRunner()
            .Queue(20, TestPassed())
            .Queue(20, TestPassed())
            .Queue(20, TestPassed());

        var result = await new TimeZoneExperiment(timeZones).RunAsync(Context(runner));

        Assert.Equal(60, runner.Requests.Count);
        Assert.Equal(3, result.Runs.Count);
        Assert.Equal("UTC", result.Runs[0].FactorValue);
        Assert.Equal("America/New_York", result.Runs[1].FactorValue);
        Assert.Equal("Asia/Tokyo", result.Runs[2].FactorValue);
    }

    [Fact]
    public async Task On_posix_systems_the_experiment_has_the_timezone_factor()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        var runner = new FakeTestRunner().Queue(20, TestPassed()).Queue(20, TestPassed()).Queue(20, TestPassed());

        var result = await new TimeZoneExperiment(new[] { "UTC", "America/New_York", "Asia/Tokyo" }).RunAsync(Context(runner));

        Assert.Equal("TimeZone", result.Factor);
        Assert.Equal("TimeZone", result.Name);
    }

    [Fact]
    public async Task On_posix_systems_null_timezones_uses_the_default_set()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        var runner = new FakeTestRunner()
            .Queue(20, TestPassed())
            .Queue(20, TestPassed())
            .Queue(20, TestPassed());

        var result = await new TimeZoneExperiment(null).RunAsync(Context(runner));

        // Default is 3 timezones
        Assert.Equal(3, result.Runs.Count);
    }

    [Fact]
    public async Task On_posix_systems_each_timezone_is_set_via_environment_variable()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        var timeZones = new[] { "UTC", "America/New_York" };
        var runner = new FakeTestRunner().Queue(20, TestPassed()).Queue(20, TestPassed());

        await new TimeZoneExperiment(timeZones).RunAsync(Context(runner));

        var request1 = runner.Requests[0];
        Assert.NotNull(request1.EnvironmentVariables);
        Assert.Equal("UTC", request1.EnvironmentVariables["TZ"]);

        var request2 = runner.Requests[20];
        Assert.NotNull(request2.EnvironmentVariables);
        Assert.Equal("America/New_York", request2.EnvironmentVariables["TZ"]);
    }

    [Fact]
    public async Task On_posix_systems_baseline_environment_variables_are_preserved()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        var baseline = new TestRunRequest(TargetPath)
        {
            EnvironmentVariables = new Dictionary<string, string>
            {
                ["MY_VAR"] = "my_value",
            },
        };
        var runner = new FakeTestRunner().Queue(20, TestPassed()).Queue(20, TestPassed());
        var context = new TestRunContext(runner, baseline, Identity());

        await new TimeZoneExperiment(new[] { "UTC", "America/New_York" }).RunAsync(context);

        var request = runner.Requests[0];
        Assert.NotNull(request.EnvironmentVariables);
        Assert.True(request.EnvironmentVariables.ContainsKey("MY_VAR"));
        Assert.Equal("my_value", request.EnvironmentVariables["MY_VAR"]);
        Assert.True(request.EnvironmentVariables.ContainsKey("TZ"));
    }

    [Fact]
    public async Task On_posix_systems_everything_else_is_taken_from_baseline()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        var baseline = Baseline("Category=Slow");
        var runner = new FakeTestRunner().Queue(20, TestPassed()).Queue(20, TestPassed());
        var context = new TestRunContext(runner, baseline, Identity());

        await new TimeZoneExperiment(new[] { "UTC", "America/New_York" }).RunAsync(context);

        var request = runner.Requests[0];
        Assert.Equal(baseline.TargetPath, request.TargetPath);
        Assert.Equal(baseline.Filter, request.Filter);
        Assert.Equal(baseline.RunSettingsPath, request.RunSettingsPath);
        Assert.Equal(baseline.Timeout, request.Timeout);
        Assert.Equal(baseline.WorkingDirectory, request.WorkingDirectory);
    }

    [Fact]
    public async Task On_posix_systems_failures_in_one_timezone_are_counted()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        var run = 0;
        var runner = new FakeTestRunner()
            .Queue(20, TestPassed())
            .Queue(20, _ => run++ < 3 ? TestFailed() : TestPassed());

        var result = await new TimeZoneExperiment(new[] { "UTC", "America/New_York" }).RunAsync(Context(runner));

        Assert.Equal(0, result.Runs[0].Failures);
        Assert.Equal(3, result.Runs[1].Failures);
    }

    [Fact]
    public async Task On_posix_systems_a_timezone_failure_does_not_stop_the_experiment()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        var runner = new FakeTestRunner()
            .Queue(20, TestFailed())
            .Queue(20, TestPassed())
            .Queue(20, TestPassed());

        var result = await new TimeZoneExperiment(new[] { "UTC", "America/New_York", "Asia/Tokyo" }).RunAsync(Context(runner));

        Assert.Equal(3, result.Runs.Count);
        Assert.True(result.Completed);
    }

    [Fact]
    public async Task On_posix_systems_a_timeout_stops_the_experiment_there()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        var runner = new FakeTestRunner()
            .Queue(20, TestPassed())
            .Returns(_ => throw new TestRunTimeoutException(TimeSpan.FromMinutes(3), "timeout"));

        var result = await new TimeZoneExperiment(new[] { "UTC", "America/New_York", "Asia/Tokyo" }).RunAsync(Context(runner));

        Assert.Equal(2, result.Runs.Count);
        Assert.False(result.Completed);
        Assert.Contains("America/New_York", result.IncompleteReason!);
    }

    [Fact]
    public async Task The_description_explains_what_the_experiment_does()
    {
        var experiment = new TimeZoneExperiment();

        Assert.NotEmpty(experiment.Description);
        Assert.Contains("timezone", experiment.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_missing_context_is_refused()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => new TimeZoneExperiment(new[] { "UTC" }).RunAsync(null!));
    }

    [Fact]
    public async Task On_windows_the_experiment_reports_itself_unsupported()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return; // Skip on non-Windows systems
        }

        var runner = new FakeTestRunner();
        var result = await new TimeZoneExperiment(new[] { "UTC" }).RunAsync(Context(runner));

        Assert.False(result.Completed);
        Assert.Empty(result.Runs);
        Assert.Contains("not supported", result.IncompleteReason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Windows", result.IncompleteReason);
    }

    [Fact]
    public async Task On_posix_systems_single_timezone_is_supported()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        var runner = new FakeTestRunner().Queue(20, TestPassed());

        var result = await new TimeZoneExperiment(new[] { "UTC" }).RunAsync(Context(runner));

        Assert.Single(result.Runs);
        Assert.Equal("UTC", result.Runs[0].FactorValue);
    }

    [Fact]
    public async Task On_posix_systems_many_timezones_are_supported()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        var timeZones = new[] { "UTC", "America/New_York", "Europe/London", "Asia/Tokyo", "Australia/Sydney" };
        var runner = new FakeTestRunner();
        foreach (var _ in timeZones)
        {
            runner.Queue(20, TestPassed());
        }

        var result = await new TimeZoneExperiment(timeZones).RunAsync(Context(runner));

        Assert.Equal(5, result.Runs.Count);
        Assert.True(result.Completed);
    }
}
