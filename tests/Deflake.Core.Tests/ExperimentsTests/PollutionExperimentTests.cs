using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Deflake.Core.Execution;
using Deflake.Core.Experiments;
using Xunit;
using static Deflake.Core.Tests.ExperimentTestSupport;

namespace Deflake.Core.Tests;

public class PollutionExperimentTests
{
    // Two extra tests beyond ExperimentTestSupport.SiblingTest and OtherClassTest, for scenarios that
    // need more than two candidates (e.g. "every test in scope is a polluter").
    private const string ThirdTest = "Shop.CartTests.Discount_is_applied";

    [Fact]
    public async Task A_specific_sibling_that_reproduces_the_failure_is_named_the_polluter()
    {
        // Only SiblingTest makes the failure reproduce; OtherClassTest is innocent company.
        var runner = new FakeTestRunner().Queue(500, request =>
            request.Filter!.Contains(SiblingTest) ? TestFailed() : TestPassed());
        var candidates = new[] { SiblingTest, OtherClassTest };

        var result = await new PollutionExperiment(candidates).FindPollutersAsync(Context(runner));

        Assert.True(result.PolluterFound);
        Assert.Equal(new[] { SiblingTest }, result.Polluters);
        Assert.True(result.Completed);
        Assert.Null(result.IncompleteReason);
    }

    [Fact]
    public async Task The_failing_test_alone_does_not_reproduce_the_failure_the_search_finds()
    {
        // The empty subset (the failing test alone) is one of the candidates ddmin always tries; its
        // own row must show the test passing, which is exactly what isolation already established.
        var runner = new FakeTestRunner().Queue(500, request =>
            request.Filter!.Contains(SiblingTest) ? TestFailed() : TestPassed());
        var candidates = new[] { SiblingTest, OtherClassTest };

        var result = await new PollutionExperiment(candidates).FindPollutersAsync(Context(runner));

        var aloneRow = result.Runs.Single(run => run.FactorValue.Contains("(0 tests)"));
        Assert.Equal(0, aloneRow.Failures);
        Assert.Equal(20, aloneRow.Passes);
    }

    [Fact]
    public async Task No_polluter_is_found_when_the_failure_never_reproduces_with_company()
    {
        var runner = new FakeTestRunner().Queue(500, TestPassed());
        var candidates = new[] { SiblingTest, OtherClassTest };

        var result = await new PollutionExperiment(candidates).FindPollutersAsync(Context(runner));

        Assert.False(result.PolluterFound);
        Assert.Empty(result.Polluters);
        Assert.True(result.Completed);
    }

    [Fact]
    public async Task Every_test_in_scope_can_be_the_polluter_set()
    {
        // The failure needs all three other tests present at once; any two of them is not enough.
        var candidates = new[] { SiblingTest, OtherClassTest, ThirdTest };
        var runner = new FakeTestRunner().Queue(500, request =>
        {
            var reproduces = candidates.All(test => request.Filter!.Contains(test));
            return reproduces ? TestFailed() : TestPassed();
        });

        var result = await new PollutionExperiment(candidates).FindPollutersAsync(Context(runner));

        Assert.True(result.PolluterFound);
        Assert.Equal(candidates, result.Polluters);
        Assert.True(result.Completed);
    }

    [Fact]
    public async Task A_ddmin_iteration_cap_stops_the_search_and_reports_why()
    {
        // Two required tests among four candidates need more than two ddmin experiments to minimize
        // down to; capping at two forces the search to stop before it can prove a 1-minimal answer.
        var candidates = new[] { SiblingTest, OtherClassTest, ThirdTest, "Shop.CartTests.Total_is_zero_when_empty" };
        var required = new[] { SiblingTest, ThirdTest };
        var runner = new FakeTestRunner().Queue(500, request =>
        {
            var reproduces = required.All(test => request.Filter!.Contains(test));
            return reproduces ? TestFailed() : TestPassed();
        });

        var result = await new PollutionExperiment(candidates, maxDdminTests: 2).FindPollutersAsync(Context(runner));

        Assert.False(result.Completed);
        Assert.Contains("ddmin", result.IncompleteReason, StringComparison.OrdinalIgnoreCase);
        Assert.True(result.DdminTestsRun <= 2);
    }

    [Fact]
    public async Task A_run_that_cannot_be_measured_during_ddmin_stops_the_search_gracefully()
    {
        var candidates = new[] { SiblingTest, OtherClassTest, ThirdTest };
        var runner = new FakeTestRunner()
            .Returns(_ => throw new TestRunTimeoutException(TimeSpan.FromMinutes(3), "the test host was still running"));

        var result = await new PollutionExperiment(candidates).FindPollutersAsync(Context(runner));

        Assert.False(result.Completed);
        Assert.Contains("did not finish", result.IncompleteReason);
        Assert.Single(runner.Requests);
        Assert.False(result.PolluterFound);
    }

    [Fact]
    public async Task The_failing_test_is_dropped_from_the_candidates_even_if_the_caller_included_it()
    {
        var runner = new FakeTestRunner().Queue(500, request =>
            request.Filter!.Contains(SiblingTest) ? TestFailed() : TestPassed());
        var candidates = new[] { FailingTest, SiblingTest };

        var result = await new PollutionExperiment(candidates).FindPollutersAsync(Context(runner));

        // Only one real candidate (SiblingTest) remains once the failing test is dropped, so the "all
        // candidates" row asks for exactly one test.
        var allRow = result.Runs[0];
        Assert.Equal("Candidate 1 (1 test)", allRow.FactorValue);
        Assert.Equal(new[] { SiblingTest }, result.Polluters);
    }

    [Fact]
    public async Task Duplicate_candidates_are_not_counted_twice()
    {
        var runner = new FakeTestRunner().Queue(500, TestFailed());
        var candidates = new[] { SiblingTest, SiblingTest, OtherClassTest };

        var result = await new PollutionExperiment(candidates).FindPollutersAsync(Context(runner));

        var allRow = result.Runs[0];
        Assert.Equal("Candidate 1 (2 tests)", allRow.FactorValue);
    }

    [Fact]
    public async Task The_experiment_names_its_factor_and_itself()
    {
        // Always fails regardless of company: enough queued runs for both the "all candidates" and
        // "candidate alone" ddmin checks the search always makes.
        var runner = new FakeTestRunner().Queue(40, TestFailed());
        var experiment = new PollutionExperiment(new[] { SiblingTest });

        var result = await experiment.RunAsync(Context(runner));

        Assert.Equal(PollutionExperiment.PolluterFactor, result.Factor);
        Assert.Equal(experiment.Name, result.Name);
        Assert.NotEmpty(experiment.Description);
    }

    [Fact]
    public async Task The_inherited_run_method_reports_the_same_evidence_as_the_dedicated_one()
    {
        var runner = new FakeTestRunner().Queue(40, TestFailed());
        var experiment = new PollutionExperiment(new[] { SiblingTest });

        var result = await experiment.RunAsync(Context(runner));

        Assert.True(result.AnyFailure);
        Assert.NotEmpty(result.Runs);
        Assert.True(result.Completed);
    }

    [Fact]
    public void A_missing_candidate_list_is_refused()
    {
        Assert.Throws<ArgumentNullException>(() => new PollutionExperiment(null!));
    }

    [Fact]
    public void A_ddmin_cap_below_one_is_refused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PollutionExperiment(new[] { SiblingTest }, maxDdminTests: 0));
    }

    [Fact]
    public async Task A_missing_context_is_refused()
    {
        var experiment = new PollutionExperiment(new[] { SiblingTest });

        await Assert.ThrowsAsync<ArgumentNullException>(() => experiment.FindPollutersAsync(null!));
    }

    [Fact]
    public async Task Cancelling_between_candidates_stops_the_search()
    {
        using var cancellation = new CancellationTokenSource();
        var calls = 0;
        var runner = new FakeTestRunner().Queue(200, _ =>
        {
            if (++calls == 3)
            {
                cancellation.Cancel();
            }

            return TestPassed();
        });
        var candidates = new[] { SiblingTest, OtherClassTest, ThirdTest };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new PollutionExperiment(candidates).FindPollutersAsync(Context(runner), cancellation.Token));
    }
}
