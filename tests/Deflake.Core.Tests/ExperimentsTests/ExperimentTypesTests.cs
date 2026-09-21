using System;
using System.Collections.Generic;
using System.Linq;
using Deflake.Core.Execution;
using Deflake.Core.Experiments;
using Deflake.Core.Statistics;
using Xunit;
using static Deflake.Core.Tests.ExperimentTestSupport;

namespace Deflake.Core.Tests;

/// <summary>The types the experiments are built from: what they accept, what they refuse, what they compute.</summary>
public class ExperimentTypesTests
{
    [Fact]
    public void A_test_identity_reads_its_class_off_the_full_name()
    {
        var identity = new TestIdentity("Shop.OrderTests.Total_is_summed", "Shop.Tests.dll");

        Assert.Equal("Shop.OrderTests", identity.ClassName);
        Assert.Equal("Shop.Tests.dll", identity.AssemblyName);
        Assert.Contains("Shop.Tests.dll", identity.ToString());
    }

    [Fact]
    public void A_nested_class_keeps_its_plus_sign()
    {
        var identity = new TestIdentity("Shop.OrderTests+Sums.Total_is_summed", "Shop.Tests.dll");

        Assert.Equal("Shop.OrderTests+Sums", identity.ClassName);
    }

    [Fact]
    public void A_given_class_name_wins_over_the_derived_one()
    {
        var identity = new TestIdentity("Total_is_summed", "Shop.Tests.dll", "Shop.OrderTests");

        Assert.Equal("Shop.OrderTests", identity.ClassName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_test_identity_needs_a_name(string fullName)
    {
        Assert.Throws<ArgumentException>(() => new TestIdentity(fullName, "Shop.Tests.dll"));
    }

    [Fact]
    public void A_test_identity_needs_an_assembly()
    {
        Assert.Throws<ArgumentException>(() => new TestIdentity(FailingTest, " "));
    }

    [Theory]
    [InlineData("Total_is_summed")]
    [InlineData(".Total_is_summed")]
    [InlineData("Shop.OrderTests.")]
    public void A_name_no_class_can_be_read_from_is_refused_rather_than_guessed(string fullName)
    {
        Assert.Throws<ArgumentException>(() => new TestIdentity(fullName, "Shop.Tests.dll"));
    }

    [Fact]
    public void A_context_starts_at_the_default_run_counts()
    {
        var context = new TestRunContext(new FakeTestRunner(), Baseline(), Identity());

        Assert.Equal(20, context.InitialRuns);
        Assert.Equal(20, context.MaxRuns);
        Assert.Equal(TestRunContext.DefaultInitialRuns, context.InitialRuns);
        Assert.Null(context.TotalTimeout);
        Assert.Same(TimeProvider.System, context.TimeProvider);
        Assert.Equal(AdaptiveRuns.DefaultTargetIntervalWidth, context.TargetIntervalWidth);
    }

    [Fact]
    public void A_context_needs_a_runner_a_baseline_and_a_test()
    {
        Assert.Throws<ArgumentNullException>(() => new TestRunContext(null!, Baseline(), Identity()));
        Assert.Throws<ArgumentNullException>(() => new TestRunContext(new FakeTestRunner(), null!, Identity()));
        Assert.Throws<ArgumentNullException>(() => new TestRunContext(new FakeTestRunner(), Baseline(), null!));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void A_context_refuses_impossible_run_counts(int runs)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new TestRunContext(new FakeTestRunner(), Baseline(), Identity()) { InitialRuns = runs });
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new TestRunContext(new FakeTestRunner(), Baseline(), Identity()) { MaxRuns = runs });
    }

    [Fact]
    public void A_context_refuses_a_budget_that_has_already_run_out()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new TestRunContext(new FakeTestRunner(), Baseline(), Identity()) { TotalTimeout = TimeSpan.Zero });
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new TestRunContext(new FakeTestRunner(), Baseline(), Identity()) { TargetIntervalWidth = 0 });
        Assert.Throws<ArgumentNullException>(() =>
            new TestRunContext(new FakeTestRunner(), Baseline(), Identity()) { TimeProvider = null! });
    }

    [Fact]
    public void A_derived_request_changes_the_scope_and_nothing_else()
    {
        var context = new TestRunContext(new FakeTestRunner(), Baseline("Category=Slow"), Identity());

        var derived = context.RequestWithFilter("FullyQualifiedName=X");

        Assert.Equal("FullyQualifiedName=X", derived.Filter);
        Assert.Equal(context.BaselineRequest.TargetPath, derived.TargetPath);
        Assert.Equal(context.BaselineRequest.RunSettingsPath, derived.RunSettingsPath);
        Assert.Same(context.BaselineRequest.EnvironmentVariables, derived.EnvironmentVariables);
        Assert.Equal(context.BaselineRequest.Timeout, derived.Timeout);
        Assert.Equal(context.BaselineRequest.WorkingDirectory, derived.WorkingDirectory);
        Assert.Null(context.RequestWithFilter(null).Filter);
    }

    [Fact]
    public void A_run_counts_passes_and_measured_runs_around_the_missing_ones()
    {
        var run = new ExperimentRun("Alone", Baseline(), total: 20, failures: 3, missing: 5);

        Assert.Equal(12, run.Passes);
        Assert.Equal(15, run.Measured);
        Assert.Equal(15, run.Rate.Runs);
        Assert.Equal(0.2, run.Rate.Observed, 10);
        Assert.Null(run.IncompleteReason);
        Assert.Contains("Alone", run.ToString());
    }

    [Fact]
    public void A_run_refuses_counts_that_do_not_add_up()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ExperimentRun("Alone", Baseline(), 10, failures: 8, missing: 3));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ExperimentRun("Alone", Baseline(), -1, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ExperimentRun("Alone", Baseline(), 10, -1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ExperimentRun("Alone", Baseline(), 10, 0, -1));
        Assert.Throws<ArgumentException>(() => new ExperimentRun(" ", Baseline(), 10, 0, 0));
        Assert.Throws<ArgumentNullException>(() => new ExperimentRun("Alone", null!, 10, 0, 0));
    }

    [Fact]
    public void A_run_without_a_single_scored_attempt_knows_nothing_rather_than_claiming_zero()
    {
        var run = new ExperimentRun("Class", Baseline(), total: 4, failures: 0, missing: 4);

        Assert.Equal(0, run.Rate.Runs);
        Assert.Equal(0, run.Rate.Lower);
        Assert.Equal(1, run.Rate.Upper);
    }

    [Fact]
    public void A_result_finds_its_rows_by_factor_value()
    {
        var alone = new ExperimentRun("Alone", Baseline(), 20, 0, 0);
        var klass = new ExperimentRun("Class", Baseline(), 20, 4, 0);
        var result = new ExperimentResult("Scope ladder", "Scope", new[] { alone, klass }, completed: true);

        Assert.Same(alone, result.Run("Alone"));
        Assert.Same(klass, result.Run("Class"));
        Assert.Null(result.Run("Assembly"));
        Assert.Same(klass, result.FirstFailingRun);
        Assert.True(result.AnyFailure);
    }

    [Fact]
    public void A_result_keeps_its_rows_from_being_changed_afterwards()
    {
        var rows = new List<ExperimentRun> { new("Alone", Baseline(), 20, 0, 0) };
        var result = new ExperimentResult("Isolation", null, rows, completed: true);

        rows.Add(new ExperimentRun("Class", Baseline(), 20, 1, 0));

        Assert.Single(result.Runs);
    }

    [Fact]
    public void A_result_refuses_to_be_both_complete_and_cut_short()
    {
        Assert.Throws<ArgumentException>(() =>
            new ExperimentResult("Isolation", null, Array.Empty<ExperimentRun>(), completed: true, "the budget ran out"));
        Assert.Throws<ArgumentException>(() =>
            new ExperimentResult("Isolation", null, Array.Empty<ExperimentRun>(), completed: false));
        Assert.Throws<ArgumentException>(() =>
            new ExperimentResult(" ", null, Array.Empty<ExperimentRun>(), completed: true));
        Assert.Throws<ArgumentException>(() =>
            new ExperimentResult("Isolation", " ", Array.Empty<ExperimentRun>(), completed: true));
        Assert.Throws<ArgumentNullException>(() =>
            new ExperimentResult("Isolation", null, null!, completed: true));
        Assert.Throws<ArgumentException>(() =>
            new ExperimentResult("Isolation", null, new ExperimentRun[] { null! }, completed: true));
    }

    [Fact]
    public void A_budget_without_a_limit_never_runs_out()
    {
        var clock = new ManualTimeProvider();
        var budget = ExperimentBudget.Start(Context(new FakeTestRunner(), timeProvider: clock));

        clock.Advance(TimeSpan.FromDays(1));

        Assert.False(budget.IsExhausted);
        Assert.Equal(TimeSpan.FromDays(1), budget.Elapsed);
        Assert.Null(budget.Limit);
        Assert.Contains("no limit", budget.ToString());
    }

    [Fact]
    public void A_budget_runs_out_exactly_at_its_limit()
    {
        var clock = new ManualTimeProvider();
        var budget = ExperimentBudget.Start(
            Context(new FakeTestRunner(), totalTimeout: TimeSpan.FromMinutes(2), timeProvider: clock));

        clock.Advance(TimeSpan.FromSeconds(119));
        Assert.False(budget.IsExhausted);

        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.True(budget.IsExhausted);
        Assert.Throws<ArgumentNullException>(() => ExperimentBudget.Start(null!));
    }

    [Theory]
    [InlineData(0, 20, false)]
    [InlineData(20, 20, false)]
    [InlineData(10, 20, true)]
    [InlineData(50, 100, false)]
    [InlineData(0, 0, false)]
    public void More_runs_are_only_worth_it_while_the_answer_is_still_blurred(int failures, int runs, bool expected)
    {
        Assert.Equal(expected, AdaptiveRuns.WouldGainFromMoreRuns(new FailureRate(failures, runs)));
    }

    [Fact]
    public void A_wider_target_stops_the_extra_runs_sooner()
    {
        var borderline = new FailureRate(10, 20);

        Assert.True(AdaptiveRuns.WouldGainFromMoreRuns(borderline, 0.25));
        Assert.False(AdaptiveRuns.WouldGainFromMoreRuns(borderline, 0.5));
        Assert.Throws<ArgumentOutOfRangeException>(() => AdaptiveRuns.WouldGainFromMoreRuns(borderline, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => AdaptiveRuns.WouldGainFromMoreRuns(borderline, 1.5));
    }

    [Fact]
    public void Every_scope_level_has_a_name_the_evidence_table_can_print()
    {
        var levels = Enum.GetValues<ScopeLevel>().Select(level => level.ToString()).ToArray();

        Assert.Equal(new[] { "Alone", "Class", "Full", "Assembly" }, levels);
    }
}
