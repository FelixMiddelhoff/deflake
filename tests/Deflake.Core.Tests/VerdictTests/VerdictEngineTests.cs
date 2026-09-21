using System.Linq;
using Deflake.Core.Experiments;
using Deflake.Core.Verdicts;
using Xunit;
using static Deflake.Core.Tests.VerdictTestSupport;

namespace Deflake.Core.Tests;

/// <summary>
/// One test per verdict the engine may give: the evidence that earns it, the confidence range it
/// belongs in, and the reproduction it hands the developer. These are the cases the precision gate
/// is about — a wrong verdict here is a wrong verdict in the field.
/// </summary>
public sealed class VerdictEngineTests
{
    private readonly VerdictEngine _engine = new();

    [Fact]
    public void A_host_crash_outranks_every_experiment()
    {
        var verdict = _engine.Decide(Input(
            isolation: Isolation(failures: 0),
            parallelism: Parallelism(serialFailures: 0, parallelFailures: 18),
            hostAborted: true,
            hostCrashDetail: "The active test run was aborted. Reason: Process is terminated due to StackOverflowException."));

        Assert.Equal(VerdictType.HostCrash, verdict.Type);
        Assert.InRange(verdict.ConfidenceScore, 0.85, 1.0);
        Assert.Contains(verdict.Notes, note => note.Contains("StackOverflowException"));
    }

    [Fact]
    public void A_host_crash_offers_no_repro_command_because_no_run_around_it_is_trustworthy()
    {
        var verdict = _engine.Decide(Input(isolation: Isolation(failures: 0), hostAborted: true));

        Assert.Null(verdict.ReproCommand);
        Assert.Null(verdict.ReproRequest);
    }

    [Fact]
    public void A_test_that_fails_in_every_run_at_every_scope_is_broken_not_flaky()
    {
        var verdict = _engine.Decide(Input(
            isolation: Isolation(failures: Runs),
            scope: ScopeLadder((ScopeLevel.Alone, Runs), (ScopeLevel.Class, Runs))));

        Assert.Equal(VerdictType.ConsistentlyFails, verdict.Type);
        Assert.InRange(verdict.ConfidenceScore, 0.9, 1.0);
        Assert.NotNull(verdict.ReproCommand);
    }

    [Fact]
    public void One_passing_condition_is_enough_to_stop_calling_a_test_consistently_broken()
    {
        var verdict = _engine.Decide(Input(
            isolation: Isolation(failures: Runs),
            culture: Cultures(("en-US", 0), ("de-DE", Runs))));

        Assert.NotEqual(VerdictType.ConsistentlyFails, verdict.Type);
    }

    [Fact]
    public void A_test_that_never_failed_is_not_reproduced_and_the_bound_is_reported()
    {
        var verdict = _engine.Decide(Input(
            isolation: Isolation(failures: 0),
            scope: ScopeLadder((ScopeLevel.Alone, 0), (ScopeLevel.Class, 0), (ScopeLevel.Assembly, 0))));

        Assert.Equal(VerdictType.NotReproduced, verdict.Type);
        Assert.InRange(verdict.ConfidenceScore, 0.5, 0.95);
        Assert.Contains(verdict.Notes, note => note.Contains("failure rate is below"));

        // Nothing failed, so there is nothing to reproduce; a command here would be a false promise.
        Assert.Null(verdict.ReproCommand);
    }

    [Fact]
    public void A_test_that_passes_alone_and_fails_in_company_is_order_dependent()
    {
        var verdict = _engine.Decide(Input(
            isolation: Isolation(failures: 0),
            scope: ScopeLadder((ScopeLevel.Alone, 0), (ScopeLevel.Class, 18)),
            polluters: Polluters(ExperimentTestSupport.SiblingTest)));

        Assert.Equal(VerdictType.OrderDependent, verdict.Type);
        Assert.InRange(verdict.ConfidenceScore, 0.7, 1.0);
        Assert.Equal(new[] { ExperimentTestSupport.SiblingTest }, verdict.SuspectTests);
    }

    [Fact]
    public void An_order_dependent_repro_runs_the_polluters_and_the_failing_test_and_nothing_else()
    {
        var verdict = _engine.Decide(Input(
            isolation: Isolation(failures: 0),
            scope: ScopeLadder((ScopeLevel.Alone, 0), (ScopeLevel.Class, 18)),
            polluters: Polluters(ExperimentTestSupport.SiblingTest)));

        Assert.NotNull(verdict.ReproCommand);
        Assert.Contains(ExperimentTestSupport.SiblingTest, verdict.ReproCommand);
        Assert.Contains(ExperimentTestSupport.FailingTest, verdict.ReproCommand);
        Assert.Contains("--filter", verdict.ReproCommand);
    }

    [Fact]
    public void A_test_that_only_fails_in_parallel_is_parallelism_dependent()
    {
        var verdict = _engine.Decide(Input(
            isolation: Isolation(failures: 0),
            parallelism: Parallelism(serialFailures: 0, parallelFailures: 17)));

        Assert.Equal(VerdictType.ParallelismDependent, verdict.Type);
        Assert.InRange(verdict.ConfidenceScore, 0.7, 1.0);
        Assert.NotNull(verdict.ReproCommand);
        Assert.NotNull(verdict.ReproNote);
    }

    [Fact]
    public void Parallel_runs_that_fail_less_than_serial_ones_do_not_blame_parallelism()
    {
        var verdict = _engine.Decide(Input(
            isolation: Isolation(failures: 0),
            parallelism: Parallelism(serialFailures: 18, parallelFailures: 0)));

        Assert.NotEqual(VerdictType.ParallelismDependent, verdict.Type);
    }

    [Fact]
    public void A_test_that_fails_far_more_under_cpu_load_is_timing_sensitive()
    {
        var verdict = _engine.Decide(Input(
            isolation: Isolation(failures: 1),
            load: Load(idleFailures: 1, loadedFailures: 18),
            heuristics: Findings(("Timeout", 0.9)),
            errorMessage: "The operation has timed out."));

        Assert.Equal(VerdictType.TimingSensitive, verdict.Type);
        Assert.InRange(verdict.ConfidenceScore, 0.7, 1.0);
        Assert.Contains(verdict.Notes, note => note.Contains("Timeout"));
        Assert.NotNull(verdict.ReproNote);
        Assert.Contains("loaded", verdict.ReproNote);
    }

    [Fact]
    public void A_test_whose_outcome_flips_with_the_culture_is_culture_dependent()
    {
        var verdict = _engine.Decide(Input(
            isolation: Isolation(failures: 0),
            culture: Cultures(("en-US", 0), ("de-DE", 19), ("ja-JP", 0))));

        Assert.Equal(VerdictType.CultureDependent, verdict.Type);
        Assert.Contains(verdict.Notes, note => note.Contains("de-DE"));
        Assert.NotNull(verdict.ReproCommand);
    }

    [Fact]
    public void A_test_whose_outcome_flips_with_the_time_zone_is_time_zone_dependent()
    {
        var verdict = _engine.Decide(Input(
            isolation: Isolation(failures: 0),
            timeZone: TimeZones(("UTC", 0), ("Asia/Tokyo", 19))));

        Assert.Equal(VerdictType.TimeZoneDependent, verdict.Type);
        Assert.Contains(verdict.Notes, note => note.Contains("Asia/Tokyo"));
    }

    [Fact]
    public void A_parallel_failure_whose_message_names_a_port_is_a_resource_conflict()
    {
        var verdict = _engine.Decide(Input(
            isolation: Isolation(failures: 0),
            parallelism: Parallelism(serialFailures: 0, parallelFailures: 16),
            heuristics: Findings(("AddressInUse", 0.95)),
            errorMessage: "Address already in use"));

        Assert.Equal(VerdictType.ResourceConflict, verdict.Type);
        Assert.Contains(verdict.Notes, note => note.Contains("AddressInUse"));
        Assert.NotNull(verdict.ReproCommand);
    }

    [Fact]
    public void An_order_dependent_failure_whose_message_names_a_file_is_a_resource_conflict()
    {
        var verdict = _engine.Decide(Input(
            isolation: Isolation(failures: 0),
            scope: ScopeLadder((ScopeLevel.Alone, 0), (ScopeLevel.Class, 18)),
            polluters: Polluters(ExperimentTestSupport.SiblingTest),
            heuristics: Findings(("FileInUse", 0.9))));

        Assert.Equal(VerdictType.ResourceConflict, verdict.Type);
        Assert.Equal(new[] { ExperimentTestSupport.SiblingTest }, verdict.SuspectTests);
    }

    [Fact]
    public void A_flaky_test_no_factor_explains_stays_inconclusive()
    {
        var verdict = _engine.Decide(Input(
            isolation: Isolation(failures: 3),
            parallelism: Parallelism(serialFailures: 4, parallelFailures: 6),
            load: Load(idleFailures: 3, loadedFailures: 5),
            culture: Cultures(("en-US", 3), ("de-DE", 4), ("ja-JP", 5))));

        Assert.Equal(VerdictType.Inconclusive, verdict.Type);
        Assert.InRange(verdict.ConfidenceScore, 0.0, 0.6);
        Assert.Contains(ParallelismExperiment.ParallelismFactor, verdict.RuledOut);
        Assert.Contains(LoadExperiment.LoadFactor, verdict.RuledOut);
        Assert.Contains(CultureExperiment.CultureFactor, verdict.RuledOut);
    }

    [Fact]
    public void Every_verdict_carries_one_evidence_row_per_condition_measured()
    {
        var verdict = _engine.Decide(Input(
            isolation: Isolation(failures: 0),
            scope: ScopeLadder((ScopeLevel.Alone, 0), (ScopeLevel.Class, 18)),
            parallelism: Parallelism(serialFailures: 2, parallelFailures: 3)));

        Assert.Equal(5, verdict.Evidence.Count);
        Assert.All(verdict.Evidence, row => Assert.Equal(Runs, row.Runs));
        Assert.Contains(verdict.Evidence, row => row.Factor == VerdictInput.IsolationFactor);
        Assert.Contains(verdict.Evidence, row => row.Factor == ScopeLadderExperiment.ScopeFactor);
        Assert.Contains(verdict.Evidence, row => row.Factor == ParallelismExperiment.ParallelismFactor);
    }

    [Fact]
    public void Evidence_rows_carry_the_interval_the_comparison_was_made_with()
    {
        var verdict = _engine.Decide(Input(isolation: Isolation(failures: 5)));

        var row = Assert.Single(verdict.Evidence);
        Assert.Equal(25, row.FailureRatePercent, 3);
        Assert.InRange(row.IntervalLow, 0.10, 0.12);
        Assert.InRange(row.IntervalHigh, 0.45, 0.50);
    }

    [Fact]
    public void The_stack_trace_names_the_members_to_look_at()
    {
        var verdict = _engine.Decide(Input(
            isolation: Isolation(failures: 0),
            load: Load(idleFailures: 0, loadedFailures: 18),
            stackTrace: "   at Shop.OrderService.Total(Order order) in C:\\repo\\OrderService.cs:line 42\n"
                + "   at Shop.OrderTests.Total_is_summed() in C:\\repo\\OrderTests.cs:line 11\n"
                + "   at System.RuntimeMethodHandle.InvokeMethod(Object target)"));

        Assert.Equal(new[] { "Shop.OrderService.Total", "Shop.OrderTests.Total_is_summed" }, verdict.SuspectMembers);
    }

    [Fact]
    public void The_decisive_factor_is_never_also_listed_as_ruled_out()
    {
        var verdict = _engine.Decide(Input(
            isolation: Isolation(failures: 0),
            parallelism: Parallelism(serialFailures: 0, parallelFailures: 18),
            load: Load(idleFailures: 2, loadedFailures: 3)));

        Assert.Equal(VerdictType.ParallelismDependent, verdict.Type);
        Assert.DoesNotContain(ParallelismExperiment.ParallelismFactor, verdict.RuledOut);
        Assert.Contains(LoadExperiment.LoadFactor, verdict.RuledOut);
    }

    [Fact]
    public void The_same_evidence_always_gives_the_same_verdict()
    {
        var first = _engine.Decide(Input(
            isolation: Isolation(failures: 0),
            culture: Cultures(("en-US", 0), ("de-DE", 19))));
        var second = _engine.Decide(Input(
            isolation: Isolation(failures: 0),
            culture: Cultures(("en-US", 0), ("de-DE", 19))));

        Assert.Equal(first.Type, second.Type);
        Assert.Equal(first.ConfidenceScore, second.ConfidenceScore);
        Assert.Equal(first.ReproCommand, second.ReproCommand);
        Assert.Equal(first.Evidence.Select(row => row.ToString()), second.Evidence.Select(row => row.ToString()));
    }
}
