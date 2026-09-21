using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Deflake.Core.Execution;
using Deflake.Core.Experiments;
using Xunit;
using static Deflake.Core.Tests.ExperimentTestSupport;

namespace Deflake.Core.Tests;

public class ScopeLadderExperimentTests
{
    private const string AloneFilter = "FullyQualifiedName=" + FailingTest;
    private const string ClassFilter = "FullyQualifiedName~" + ClassName + ".";

    [Fact]
    public async Task A_test_that_already_fails_alone_stops_the_ladder_at_the_first_step()
    {
        var runner = new FakeTestRunner().Queue(20, TestFailed());

        var result = await new ScopeLadderExperiment().RunAsync(Context(runner));

        var alone = Assert.Single(result.Runs);
        Assert.Equal(nameof(ScopeLevel.Alone), alone.FactorValue);
        Assert.Equal(20, alone.Failures);
        Assert.Same(alone, result.FirstFailingRun);
        Assert.True(result.Completed);
        Assert.Equal(20, runner.Requests.Count);
    }

    [Fact]
    public async Task A_test_that_only_fails_with_its_class_names_the_class_as_the_smallest_scope()
    {
        var runner = new FakeTestRunner()
            .Queue(20, TestPassed())
            .Queue(20, TestFailed());

        var result = await new ScopeLadderExperiment().RunAsync(Context(runner));

        Assert.Equal(new[] { nameof(ScopeLevel.Alone), nameof(ScopeLevel.Class) }, result.Runs.Select(run => run.FactorValue));
        Assert.Equal(AloneFilter, result.Runs[0].Request.Filter);
        Assert.Equal(ClassFilter, result.Runs[1].Request.Filter);
        Assert.Equal(0, result.Runs[0].Failures);
        Assert.Equal(20, result.Runs[1].Failures);
        Assert.Equal(nameof(ScopeLevel.Class), result.FirstFailingRun!.FactorValue);
    }

    [Fact]
    public async Task The_ladder_climbs_to_the_whole_assembly_when_the_class_is_not_enough()
    {
        var runner = new FakeTestRunner()
            .Queue(40, TestPassed())
            .Queue(20, TestFailed());

        var result = await new ScopeLadderExperiment().RunAsync(Context(runner));

        Assert.Equal(
            new[] { nameof(ScopeLevel.Alone), nameof(ScopeLevel.Class), nameof(ScopeLevel.Assembly) },
            result.Runs.Select(run => run.FactorValue));
        Assert.Null(result.Runs[2].Request.Filter);
        Assert.Equal(nameof(ScopeLevel.Assembly), result.FirstFailingRun!.FactorValue);
    }

    [Fact]
    public async Task The_original_scope_is_a_step_of_its_own_when_it_was_narrower_than_the_assembly()
    {
        var runner = new FakeTestRunner()
            .Queue(60, TestPassed())
            .Queue(20, TestFailed());

        var result = await new ScopeLadderExperiment().RunAsync(Context(runner, baselineFilter: "Category=Slow"));

        Assert.Equal(
            new[] { nameof(ScopeLevel.Alone), nameof(ScopeLevel.Class), nameof(ScopeLevel.Full), nameof(ScopeLevel.Assembly) },
            result.Runs.Select(run => run.FactorValue));
        Assert.Equal("Category=Slow", result.Runs[2].Request.Filter);
        Assert.Null(result.Runs[3].Request.Filter);
    }

    [Fact]
    public async Task The_original_scope_is_not_run_twice_when_it_was_the_whole_assembly()
    {
        var runner = new FakeTestRunner().Queue(60, TestPassed());

        var result = await new ScopeLadderExperiment().RunAsync(Context(runner));

        Assert.Equal(3, result.Runs.Count);
        Assert.DoesNotContain(nameof(ScopeLevel.Full), result.Runs.Select(run => run.FactorValue));
        Assert.Equal(60, runner.Requests.Count);
    }

    [Fact]
    public async Task A_failure_that_appears_at_no_scope_is_reported_as_no_failure_at_all()
    {
        var runner = new FakeTestRunner().Queue(60, TestPassed());

        var result = await new ScopeLadderExperiment().RunAsync(Context(runner));

        Assert.False(result.AnyFailure);
        Assert.Null(result.FirstFailingRun);
        Assert.True(result.Completed);
        Assert.All(result.Runs, run => Assert.Equal(20, run.Passes));
    }

    [Fact]
    public async Task The_ladder_names_its_factor_and_itself()
    {
        var runner = new FakeTestRunner().Queue(20, TestFailed());
        var experiment = new ScopeLadderExperiment();

        var result = await experiment.RunAsync(Context(runner));

        Assert.Equal(ScopeLadderExperiment.ScopeFactor, result.Factor);
        Assert.Equal(experiment.Name, result.Name);
        Assert.NotEmpty(experiment.Description);
    }

    [Fact]
    public async Task A_class_step_whose_filter_matches_nothing_is_not_mistaken_for_a_passing_step()
    {
        var runner = new FakeTestRunner()
            .Queue(20, TestPassed())
            .Queue(20, TestAbsent())
            .Queue(20, TestFailed());

        var result = await new ScopeLadderExperiment().RunAsync(Context(runner));

        var classStep = result.Runs[1];
        Assert.Equal(20, classStep.Missing);
        Assert.Equal(0, classStep.Measured);
        Assert.Equal(0, classStep.Passes);
        Assert.Equal(nameof(ScopeLevel.Assembly), result.FirstFailingRun!.FactorValue);
    }

    [Fact]
    public async Task The_class_of_the_failing_test_can_be_given_instead_of_derived()
    {
        var runner = new FakeTestRunner().Queue(40, TestPassed()).Queue(20, TestFailed());
        var context = Context(runner, test: Identity(FailingTest, className: "Shop.OrderTests+Sums"));

        var result = await new ScopeLadderExperiment().RunAsync(context);

        Assert.Equal("FullyQualifiedName~Shop.OrderTests+Sums.", result.Runs[1].Request.Filter);
    }

    [Fact]
    public void The_class_filter_escapes_what_the_filter_syntax_would_read_as_syntax()
    {
        Assert.Equal(@"FullyQualifiedName~Shop.Order\(Tests\).", ScopeLadderExperiment.ClassFilter("Shop.Order(Tests)"));
        Assert.Throws<ArgumentException>(() => ScopeLadderExperiment.ClassFilter("  "));
    }

    [Fact]
    public async Task The_budget_is_shared_by_every_step_of_the_ladder()
    {
        var clock = new ManualTimeProvider();
        var runner = new FakeTestRunner().Queue(20, _ =>
        {
            clock.Advance(TimeSpan.FromSeconds(3));
            return TestPassed();
        });
        var context = Context(runner, totalTimeout: TimeSpan.FromMinutes(1), timeProvider: clock);

        var result = await new ScopeLadderExperiment().RunAsync(context);

        Assert.Equal(2, result.Runs.Count);
        Assert.Equal(20, result.Runs[0].Total);
        Assert.Equal(0, result.Runs[1].Total);
        Assert.False(result.Completed);
        Assert.Contains(nameof(ScopeLevel.Class), result.IncompleteReason!);
        Assert.Contains("time budget", result.IncompleteReason!);
    }

    [Fact]
    public async Task A_step_that_cannot_be_run_ends_the_ladder_without_a_verdict_about_wider_scopes()
    {
        var runner = new FakeTestRunner()
            .Queue(20, TestPassed())
            .Returns(_ => throw new TestRunExecutionException(1, string.Empty, string.Empty, "no results file was written"));

        var result = await new ScopeLadderExperiment().RunAsync(Context(runner));

        Assert.Equal(2, result.Runs.Count);
        Assert.Equal(1, result.Runs[1].Missing);
        Assert.False(result.Completed);
        Assert.Contains("no results", result.IncompleteReason!);
    }

    [Fact]
    public async Task Cancelling_between_steps_stops_the_ladder()
    {
        using var cancellation = new CancellationTokenSource();
        var calls = 0;
        var runner = new FakeTestRunner().Queue(40, _ =>
        {
            if (++calls == 20)
            {
                cancellation.Cancel();
            }

            return TestPassed();
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new ScopeLadderExperiment().RunAsync(Context(runner), cancellation.Token));

        Assert.Equal(20, runner.Requests.Count);
    }

    [Fact]
    public async Task Extra_runs_are_earned_per_step_and_not_shared()
    {
        var classRuns = 0;
        var runner = new FakeTestRunner().Queue(120, request =>
        {
            if (request.Filter == AloneFilter)
            {
                return TestPassed();
            }

            return classRuns++ % 2 == 0 ? TestFailed() : TestPassed();
        });

        var result = await new ScopeLadderExperiment().RunAsync(Context(runner, initialRuns: 20, maxRuns: 100));

        Assert.Equal(20, result.Runs[0].Total);
        Assert.True(result.Runs[1].Total > 20, $"Expected the borderline class step to earn extra runs, got {result.Runs[1].Total}.");
        Assert.True(result.Runs[1].Rate.Upper - result.Runs[1].Rate.Lower <= AdaptiveRuns.DefaultTargetIntervalWidth);
    }

    [Fact]
    public async Task A_missing_context_is_refused()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => new ScopeLadderExperiment().RunAsync(null!));
    }
}
