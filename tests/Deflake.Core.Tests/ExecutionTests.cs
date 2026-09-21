using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Deflake.Core.Execution;
using Deflake.Core.Trx;
using Xunit;

namespace Deflake.Core.Tests;

// --- FakeTestRunner: the double every experiment and statistics test runs against. ---

public class FakeTestRunnerTests
{
    [Fact]
    public async Task Scripted_results_are_returned_in_the_order_they_were_queued()
    {
        var runner = new FakeTestRunner()
            .ReturnsAllPassed("A.B.One")
            .ReturnsAllFailed("A.B.Two");

        var first = await runner.RunAsync(new TestRunRequest("a.csproj"), CancellationToken.None);
        var second = await runner.RunAsync(new TestRunRequest("a.csproj"), CancellationToken.None);

        Assert.True(first.Passed("A.B.One"));
        Assert.True(second.Failed("A.B.Two"));
    }

    [Fact]
    public async Task Every_request_is_recorded_for_later_assertions()
    {
        var runner = new FakeTestRunner().ReturnsAllPassed("A.B.One");
        var request = new TestRunRequest("a.csproj") { Filter = TestFilter.ForTest("A.B.One") };

        await runner.RunAsync(request, CancellationToken.None);

        Assert.Same(request, Assert.Single(runner.Requests));
    }

    [Fact]
    public async Task A_call_beyond_the_scripted_results_fails_loudly_instead_of_returning_null()
    {
        var runner = new FakeTestRunner();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => runner.RunAsync(new TestRunRequest("a.csproj"), CancellationToken.None));
    }

    [Fact]
    public async Task A_result_that_depends_on_the_request_can_be_computed_lazily()
    {
        var runner = new FakeTestRunner().Returns(request =>
            new TestRunResult(new[]
            {
                new TestResult(request.TargetPath, request.TargetPath, TestOutcome.Passed, "Passed",
                    DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, TimeSpan.Zero, null, null, null),
            }));

        var result = await runner.RunAsync(new TestRunRequest("project.csproj"), CancellationToken.None);

        Assert.True(result.Passed("project.csproj"));
    }

    [Fact]
    public async Task A_cancelled_token_is_honored_before_a_scripted_result_is_consumed()
    {
        var runner = new FakeTestRunner().ReturnsAllPassed("A.B.One");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => runner.RunAsync(new TestRunRequest("a.csproj"), cts.Token));

        Assert.Empty(runner.Requests);
    }
}

// --- TestRunRequest ---

public class TestRunRequestTests
{
    [Fact]
    public void A_missing_target_path_is_refused()
    {
        Assert.Throws<ArgumentException>(() => new TestRunRequest(""));
    }

    [Fact]
    public void The_default_timeout_is_five_minutes()
    {
        Assert.Equal(TimeSpan.FromMinutes(5), new TestRunRequest("a.csproj").Timeout);
    }
}

// --- DotnetTestRunner: exercised against a real "dotnet test" run of SampleProject. ---

/// <summary>
/// Builds <c>tests/Deflake.EndToEnd/SampleProject</c> once with a real <c>dotnet build</c>, so
/// every test in <see cref="ExecutionTests"/> can point <see cref="DotnetTestRunner"/> at an
/// already-built project and pass <c>--no-build</c>, the way deflake always runs it.
/// </summary>
public sealed class SampleProjectFixture : IAsyncLifetime
{
    public string ProjectPath { get; } = FindSampleProject();

    public async Task InitializeAsync()
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add("build");
        startInfo.ArgumentList.Add(ProjectPath);
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add("Debug");
        startInfo.ArgumentList.Add("--nologo");

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start 'dotnet build' for the sample project.");

        var stdOutTask = process.StandardOutput.ReadToEndAsync();
        var stdErrTask = process.StandardError.ReadToEndAsync();
        await Task.WhenAll(stdOutTask, stdErrTask, process.WaitForExitAsync());

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Building the sample project failed (exit code {process.ExitCode}).\n" +
                $"stdout:\n{stdOutTask.Result}\nstderr:\n{stdErrTask.Result}");
        }
    }

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>Walks up from the test assembly's output directory to the repo root, identified by <c>Deflake.sln</c>.</summary>
    private static string FindSampleProject()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Deflake.sln")))
        {
            directory = directory.Parent;
        }

        if (directory is null)
        {
            throw new InvalidOperationException("Could not find the repo root (Deflake.sln) above " + AppContext.BaseDirectory);
        }

        return Path.Combine(directory.FullName, "tests", "Deflake.EndToEnd", "SampleProject", "SampleProject.csproj");
    }
}

public class ExecutionTests : IClassFixture<SampleProjectFixture>
{
    private const string FullyPassing = "SampleProject.Tests.Addition_is_commutative";
    private const string FullyFailing = "SampleProject.Tests.Always_fails";
    private const string EnvironmentTest = "SampleProject.Tests.Environment_variable_is_expected_value";
    private const string SleepTest = "SampleProject.Tests.Sleeps_for_configured_milliseconds";

    private readonly string _projectPath;

    public ExecutionTests(SampleProjectFixture fixture)
    {
        _projectPath = fixture.ProjectPath;
    }

    [Fact]
    public async Task A_full_run_is_parsed_into_the_designed_pass_and_fail_outcomes()
    {
        var runner = new DotnetTestRunner();
        var request = new TestRunRequest(_projectPath) { Timeout = TimeSpan.FromMinutes(2) };

        var result = await runner.RunAsync(request, CancellationToken.None);

        Assert.Equal(4, result.Tests.Count);
        Assert.True(result.Passed(FullyPassing));
        Assert.True(result.Failed(FullyFailing));
        // Environment_variable_is_expected_value fails here because no environment variable was set:
        // that is exactly what Environment_variable_is_passed_through below checks the other way round.
        Assert.True(result.Failed(EnvironmentTest));
        Assert.True(result.Passed(SleepTest));
    }

    [Fact]
    public async Task The_filter_runs_only_the_selected_test()
    {
        var runner = new DotnetTestRunner();
        var request = new TestRunRequest(_projectPath)
        {
            Filter = TestFilter.ForTest(FullyPassing),
            Timeout = TimeSpan.FromMinutes(2),
        };

        var result = await runner.RunAsync(request, CancellationToken.None);

        var test = Assert.Single(result.Tests);
        Assert.Equal(FullyPassing, test.FullName);
        Assert.Equal(TestOutcome.Passed, test.Outcome);
    }

    [Fact]
    public async Task Environment_variables_are_passed_through_to_the_test_process()
    {
        var runner = new DotnetTestRunner();
        var request = new TestRunRequest(_projectPath)
        {
            Filter = TestFilter.ForTest(EnvironmentTest),
            Timeout = TimeSpan.FromMinutes(2),
            EnvironmentVariables = new Dictionary<string, string>
            {
                ["DEFLAKE_SAMPLE_ENV"] = "expected-value",
            },
        };

        var result = await runner.RunAsync(request, CancellationToken.None);

        Assert.True(result.Passed(EnvironmentTest));
    }

    [Fact]
    public async Task A_run_that_exceeds_its_timeout_is_killed_and_reported_as_a_timeout()
    {
        var runner = new DotnetTestRunner();
        var request = new TestRunRequest(_projectPath)
        {
            Filter = TestFilter.ForTest(SleepTest),
            Timeout = TimeSpan.FromSeconds(2),
            EnvironmentVariables = new Dictionary<string, string>
            {
                ["DEFLAKE_SAMPLE_SLEEP_MS"] = "60000",
            },
        };

        var stopwatch = Stopwatch.StartNew();
        var exception = await Assert.ThrowsAsync<TestRunTimeoutException>(
            () => runner.RunAsync(request, CancellationToken.None));
        stopwatch.Stop();

        Assert.Equal(TimeSpan.FromSeconds(2), exception.Timeout);
        // The process tree was actually killed rather than left to finish its 60-second sleep:
        // this is the process-hygiene evidence, since the test would otherwise take a full minute.
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(30),
            $"The run took {stopwatch.Elapsed}, which suggests the sleeping process was not killed on timeout.");

        // The runner and the sample project's build output are still usable afterwards: nothing
        // from the killed run is left holding a file lock or a port.
        var followUp = await runner.RunAsync(
            new TestRunRequest(_projectPath) { Filter = TestFilter.ForTest(FullyPassing), Timeout = TimeSpan.FromMinutes(2) },
            CancellationToken.None);
        Assert.True(followUp.Passed(FullyPassing));
    }

    [Fact]
    public async Task Cancelling_the_token_kills_the_process_instead_of_waiting_for_it()
    {
        var runner = new DotnetTestRunner();
        var request = new TestRunRequest(_projectPath)
        {
            Filter = TestFilter.ForTest(SleepTest),
            Timeout = TimeSpan.FromMinutes(2),
            EnvironmentVariables = new Dictionary<string, string>
            {
                ["DEFLAKE_SAMPLE_SLEEP_MS"] = "60000",
            },
        };

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var stopwatch = Stopwatch.StartNew();
        await Assert.ThrowsAsync<OperationCanceledException>(() => runner.RunAsync(request, cts.Token));
        stopwatch.Stop();

        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(30),
            $"The run took {stopwatch.Elapsed}, which suggests cancellation did not kill the process.");
    }

    [Fact]
    public async Task A_target_that_cannot_produce_a_trx_file_reports_the_process_output_instead_of_an_empty_result()
    {
        var runner = new DotnetTestRunner();
        var missingProject = Path.Combine(Path.GetTempPath(), "deflake-does-not-exist-" + Guid.NewGuid() + ".csproj");
        var request = new TestRunRequest(missingProject) { Timeout = TimeSpan.FromSeconds(30) };

        var exception = await Assert.ThrowsAsync<TestRunExecutionException>(
            () => runner.RunAsync(request, CancellationToken.None));

        Assert.NotEqual(0, exception.ExitCode);
    }

    [Fact]
    public async Task A_missing_request_is_refused()
    {
        var runner = new DotnetTestRunner();
        await Assert.ThrowsAsync<ArgumentNullException>(() => runner.RunAsync(null!, CancellationToken.None));
    }
}
