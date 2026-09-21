using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Deflake.Core.Execution;
using Deflake.Core.Experiments;
using Deflake.Core.Trx;
using Xunit;
using static Deflake.Core.Tests.ExperimentTestSupport;

namespace Deflake.Core.Tests;

public class IsolationExperimentTests
{
    [Fact]
    public async Task The_test_is_run_on_its_own_with_an_exact_filter()
    {
        var runner = new FakeTestRunner().Queue(20, TestPassed());

        var result = await new IsolationExperiment().RunAsync(Context(runner));

        Assert.Equal(20, runner.Requests.Count);
        Assert.All(runner.Requests, request => Assert.Equal("FullyQualifiedName=" + FailingTest, request.Filter));
        Assert.Single(result.Runs);
        Assert.Equal(IsolationExperiment.Alone, result.Runs[0].FactorValue);
    }

    [Fact]
    public async Task Everything_but_the_scope_is_taken_from_the_baseline_run()
    {
        var runner = new FakeTestRunner().Queue(20, TestPassed());
        var baseline = Baseline("Category=Slow");
        var context = new TestRunContext(runner, baseline, Identity());

        await new IsolationExperiment().RunAsync(context);

        var request = runner.Requests[0];
        Assert.Equal(baseline.TargetPath, request.TargetPath);
        Assert.Equal(baseline.RunSettingsPath, request.RunSettingsPath);
        Assert.Equal(baseline.EnvironmentVariables, request.EnvironmentVariables);
        Assert.Equal(baseline.Timeout, request.Timeout);
        Assert.Equal(baseline.WorkingDirectory, request.WorkingDirectory);
        Assert.NotEqual(baseline.Filter, request.Filter);
    }

    [Fact]
    public async Task Failures_alone_are_counted_and_turned_into_a_rate()
    {
        var run = 0;
        var runner = new FakeTestRunner().Queue(20, _ => run++ < 5 ? TestFailed() : TestPassed());

        var result = await new IsolationExperiment().RunAsync(Context(runner));

        var alone = result.Runs.Single();
        Assert.Equal(20, alone.Total);
        Assert.Equal(5, alone.Failures);
        Assert.Equal(15, alone.Passes);
        Assert.Equal(0, alone.Missing);
        Assert.Equal(0.25, alone.Rate.Observed, 10);
        Assert.InRange(alone.Rate.Lower, 0.10, 0.25);
        Assert.InRange(alone.Rate.Upper, 0.25, 0.50);
    }

    [Fact]
    public async Task A_test_that_always_passes_alone_gets_a_rate_of_zero_and_an_honest_upper_bound()
    {
        var runner = new FakeTestRunner().Queue(20, TestPassed());

        var result = await new IsolationExperiment().RunAsync(Context(runner));

        var alone = result.Runs.Single();
        Assert.Equal(0, alone.Failures);
        Assert.Equal(20, alone.Passes);
        Assert.Equal(0, alone.Rate.Observed);
        Assert.Equal(0, alone.Rate.Lower);
        Assert.InRange(alone.Rate.Upper, 0.01, 0.20);
        Assert.True(result.Completed);
        Assert.False(result.AnyFailure);
        Assert.Null(result.FirstFailingRun);
    }

    [Fact]
    public async Task A_test_that_always_fails_alone_is_reported_as_the_first_failing_run()
    {
        var runner = new FakeTestRunner().Queue(20, TestFailed());

        var result = await new IsolationExperiment().RunAsync(Context(runner));

        Assert.Equal(20, result.Runs[0].Failures);
        Assert.Equal(1, result.Runs[0].Rate.Observed);
        Assert.Same(result.Runs[0], result.FirstFailingRun);
    }

    [Fact]
    public async Task The_experiment_has_no_factor_and_says_what_it_is()
    {
        var runner = new FakeTestRunner().Queue(20, TestPassed());
        var experiment = new IsolationExperiment();

        var result = await experiment.RunAsync(Context(runner));

        Assert.Null(result.Factor);
        Assert.Equal(experiment.Name, result.Name);
        Assert.NotEmpty(experiment.Description);
    }

    [Fact]
    public async Task A_test_that_never_appears_in_the_results_counts_as_neither_passed_nor_failed()
    {
        var runner = new FakeTestRunner().Queue(20, TestAbsent());

        var result = await new IsolationExperiment().RunAsync(Context(runner));

        var alone = result.Runs.Single();
        Assert.Equal(20, alone.Total);
        Assert.Equal(20, alone.Missing);
        Assert.Equal(0, alone.Measured);
        Assert.Equal(0, alone.Passes);
        Assert.Equal(0, alone.Failures);
        Assert.Equal(0, alone.Rate.Runs);
        Assert.Equal(1, alone.Rate.Upper);
        Assert.True(result.Completed);
    }

    [Fact]
    public async Task A_skipped_test_is_not_a_pass()
    {
        var runner = new FakeTestRunner().Queue(20, Ran((FailingTest, TestOutcome.NotRun)));

        var result = await new IsolationExperiment().RunAsync(Context(runner));

        Assert.Equal(20, result.Runs[0].Missing);
        Assert.Equal(0, result.Runs[0].Passes);
    }

    [Fact]
    public async Task An_aborted_test_host_counts_as_a_failure()
    {
        var runner = new FakeTestRunner().Queue(20, Ran((FailingTest, TestOutcome.Aborted)));

        var result = await new IsolationExperiment().RunAsync(Context(runner));

        Assert.Equal(20, result.Runs[0].Failures);
    }

    [Fact]
    public async Task A_borderline_rate_earns_extra_runs_until_the_interval_is_sharp_enough()
    {
        var run = 0;
        var runner = new FakeTestRunner().Queue(200, _ => run++ % 2 == 0 ? TestFailed() : TestPassed());

        var result = await new IsolationExperiment().RunAsync(Context(runner, initialRuns: 20, maxRuns: 200));

        var alone = result.Runs.Single();
        Assert.True(alone.Total > 20, $"Expected more than the initial 20 runs, got {alone.Total}.");
        Assert.Equal(alone.Total, runner.Requests.Count);
        Assert.True(alone.Rate.Upper - alone.Rate.Lower <= AdaptiveRuns.DefaultTargetIntervalWidth);
    }

    [Fact]
    public async Task Extra_runs_stop_at_the_ceiling()
    {
        var run = 0;
        var runner = new FakeTestRunner().Queue(25, _ => run++ % 2 == 0 ? TestFailed() : TestPassed());

        var result = await new IsolationExperiment().RunAsync(Context(runner, initialRuns: 20, maxRuns: 25));

        Assert.Equal(25, result.Runs[0].Total);
    }

    [Fact]
    public async Task A_clear_answer_earns_no_extra_runs_even_when_more_are_allowed()
    {
        var runner = new FakeTestRunner().Queue(20, TestFailed());

        var result = await new IsolationExperiment().RunAsync(Context(runner, initialRuns: 20, maxRuns: 500));

        Assert.Equal(20, result.Runs[0].Total);
        Assert.Equal(20, runner.Requests.Count);
    }

    [Fact]
    public async Task The_time_budget_stops_the_experiment_and_the_result_says_so()
    {
        var clock = new ManualTimeProvider();
        var runner = new FakeTestRunner().Queue(20, _ =>
        {
            clock.Advance(TimeSpan.FromSeconds(20));
            return TestPassed();
        });
        var context = Context(runner, totalTimeout: TimeSpan.FromMinutes(1), timeProvider: clock);

        var result = await new IsolationExperiment().RunAsync(context);

        Assert.Equal(3, result.Runs[0].Total);
        Assert.False(result.Completed);
        Assert.Contains("time budget", result.IncompleteReason!);
        Assert.Equal(result.Runs[0].IncompleteReason, result.IncompleteReason!);
    }

    [Fact]
    public async Task A_run_that_times_out_is_not_scored_as_a_failure_of_the_test()
    {
        var runner = new FakeTestRunner()
            .Queue(2, TestPassed())
            .Returns(_ => throw new TestRunTimeoutException(TimeSpan.FromMinutes(3), "the test host was still running"));

        var result = await new IsolationExperiment().RunAsync(Context(runner));

        var alone = result.Runs.Single();
        Assert.Equal(3, alone.Total);
        Assert.Equal(0, alone.Failures);
        Assert.Equal(1, alone.Missing);
        Assert.Equal(2, alone.Passes);
        Assert.False(result.Completed);
        Assert.Contains("did not finish", result.IncompleteReason!);
    }

    [Fact]
    public async Task A_run_that_cannot_be_executed_stops_the_experiment()
    {
        var runner = new FakeTestRunner()
            .Returns(_ => throw new TestRunExecutionException(1, string.Empty, "MSB1009", "the project could not be found"));

        var result = await new IsolationExperiment().RunAsync(Context(runner));

        Assert.Equal(1, result.Runs[0].Total);
        Assert.Equal(1, result.Runs[0].Missing);
        Assert.False(result.Completed);
        Assert.Contains("no results", result.IncompleteReason!);
    }

    [Fact]
    public async Task Cancelling_the_experiment_stops_it_between_runs()
    {
        using var cancellation = new CancellationTokenSource();
        var calls = 0;
        var runner = new FakeTestRunner().Queue(20, _ =>
        {
            if (++calls == 3)
            {
                cancellation.Cancel();
            }

            return TestPassed();
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new IsolationExperiment().RunAsync(Context(runner), cancellation.Token));

        Assert.Equal(3, runner.Requests.Count);
    }

    [Fact]
    public async Task Cancelling_the_investigation_stops_the_experiment_too()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var runner = new FakeTestRunner().Queue(20, TestPassed());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new IsolationExperiment().RunAsync(Context(runner, cancellationToken: cancellation.Token)));

        Assert.Empty(runner.Requests);
    }

    [Fact]
    public async Task A_missing_context_is_refused()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => new IsolationExperiment().RunAsync(null!));
    }

    [Fact]
    public async Task A_run_ceiling_below_the_initial_runs_is_refused()
    {
        var context = Context(new FakeTestRunner(), initialRuns: 20, maxRuns: 5);

        var error = await Assert.ThrowsAsync<ArgumentException>(() => new IsolationExperiment().RunAsync(context));

        Assert.Contains("run ceiling", error.Message);
    }
}

