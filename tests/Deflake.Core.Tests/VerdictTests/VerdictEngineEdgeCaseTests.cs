using System;
using Deflake.Core.Experiments;
using Deflake.Core.Verdicts;
using Xunit;
using static Deflake.Core.Tests.VerdictTestSupport;

namespace Deflake.Core.Tests;

/// <summary>
/// The cases where a careless engine would claim something. Each one asserts that it does not: too
/// few runs, overlapping intervals, a pattern without an experiment, an experiment that never
/// finished. The quality policy's "never claim more than the evidence shows" is this file.
/// </summary>
public sealed class VerdictEngineEdgeCaseTests
{
    private readonly VerdictEngine _engine = new();

    [Fact]
    public void A_polluter_search_that_found_nothing_is_inconclusive_not_order_dependent()
    {
        var verdict = _engine.Decide(Input(
            isolation: Isolation(failures: 0),
            scope: ScopeLadder((ScopeLevel.Alone, 0), (ScopeLevel.Class, 0), (ScopeLevel.Assembly, 0)),
            polluters: NoPolluters(),
            parallelism: Parallelism(serialFailures: 0, parallelFailures: 2)));

        Assert.NotEqual(VerdictType.OrderDependent, verdict.Type);
        Assert.Empty(verdict.SuspectTests);
    }

    [Fact]
    public void A_polluter_search_that_ran_is_ruled_out_rather_than_called_untested()
    {
        var verdict = _engine.Decide(Input(
            isolation: Isolation(failures: 4),
            scope: ScopeLadder((ScopeLevel.Alone, 4), (ScopeLevel.Class, 5)),
            polluters: NoPolluters()));

        Assert.Contains(PollutionExperiment.PolluterFactor, verdict.RuledOut);
        Assert.DoesNotContain(verdict.NotTested, entry => entry.Contains(PollutionExperiment.PolluterFactor));
    }

    [Fact]
    public void A_polluter_search_that_stopped_short_is_flagged_with_its_reason()
    {
        var search = new PollutionResult(
            Array.Empty<string>(),
            polluterFound: false,
            new[] { Row("subset", failures: 0) },
            completed: false,
            incompleteReason: "The ddmin iteration cap was reached.",
            ddminTestsRun: 500);

        var verdict = _engine.Decide(Input(isolation: Isolation(failures: 4), polluters: search));

        Assert.Contains(verdict.NotTested, entry => entry.Contains("ddmin iteration cap"));
        Assert.DoesNotContain(PollutionExperiment.PolluterFactor, verdict.RuledOut);
    }

    [Fact]
    public void Overlapping_intervals_decide_nothing()
    {
        var verdict = _engine.Decide(Input(
            isolation: Isolation(failures: 4),
            parallelism: Parallelism(serialFailures: 4, parallelFailures: 9)));

        Assert.Equal(VerdictType.Inconclusive, verdict.Type);
        Assert.Contains(ParallelismExperiment.ParallelismFactor, verdict.RuledOut);
    }

    [Fact]
    public void A_difference_measured_in_too_few_runs_is_not_a_verdict()
    {
        // 0 of 10 against 10 of 10 looks decisive, but ten runs are not enough to say so: the
        // minimum is a gate in front of the interval comparison, not an afterthought.
        var verdict = _engine.Decide(Input(
            isolation: Isolation(failures: 0, total: 10),
            parallelism: Parallelism(serialFailures: 0, parallelFailures: 10, total: 10)));

        Assert.Equal(VerdictType.Inconclusive, verdict.Type);
        Assert.Contains(verdict.NotTested, entry => entry.Contains(ParallelismExperiment.ParallelismFactor));
        Assert.DoesNotContain(ParallelismExperiment.ParallelismFactor, verdict.RuledOut);
    }

    [Fact]
    public void A_message_pattern_on_its_own_never_becomes_a_resource_conflict()
    {
        var verdict = _engine.Decide(Input(
            isolation: Isolation(failures: 5),
            parallelism: Parallelism(serialFailures: 5, parallelFailures: 6),
            heuristics: Findings(("AddressInUse", 0.95)),
            errorMessage: "Address already in use"));

        Assert.Equal(VerdictType.Inconclusive, verdict.Type);
        Assert.Contains(verdict.Notes, note => note.StartsWith("Hint only", StringComparison.Ordinal));
    }

    [Fact]
    public void A_message_pattern_on_its_own_never_becomes_timing_sensitivity()
    {
        var verdict = _engine.Decide(Input(
            isolation: Isolation(failures: 5),
            load: Load(idleFailures: 5, loadedFailures: 7),
            heuristics: Findings(("Timeout", 0.95))));

        Assert.Equal(VerdictType.Inconclusive, verdict.Type);
    }

    [Fact]
    public void When_two_factors_are_decisive_the_stronger_signal_is_reported_and_the_other_is_named()
    {
        var verdict = _engine.Decide(Input(
            isolation: Isolation(failures: 0),
            parallelism: Parallelism(serialFailures: 0, parallelFailures: 18),
            culture: Cultures(("en-US", 0), ("de-DE", 19))));

        Assert.Equal(VerdictType.ParallelismDependent, verdict.Type);
        Assert.Contains(verdict.Notes, note => note.Contains(CultureExperiment.CultureFactor));
    }

    [Fact]
    public void Order_dependence_outranks_parallelism_when_both_are_decisive()
    {
        var verdict = _engine.Decide(Input(
            isolation: Isolation(failures: 0),
            scope: ScopeLadder((ScopeLevel.Alone, 0), (ScopeLevel.Class, 18)),
            polluters: Polluters(ExperimentTestSupport.SiblingTest),
            parallelism: Parallelism(serialFailures: 0, parallelFailures: 17)));

        Assert.Equal(VerdictType.OrderDependent, verdict.Type);
        Assert.Contains(verdict.Notes, note => note.Contains(ParallelismExperiment.ParallelismFactor));
    }

    [Fact]
    public void Experiments_that_never_ran_are_listed_as_not_tested()
    {
        var verdict = _engine.Decide(Input(isolation: Isolation(failures: 4)));

        Assert.Contains(verdict.NotTested, entry => entry.Contains(TimeZoneExperiment.TimeZoneFactor));
        Assert.Contains(verdict.NotTested, entry => entry.Contains(CultureExperiment.CultureFactor));
        Assert.Contains(verdict.NotTested, entry => entry.Contains("not tested"));
    }

    [Fact]
    public void An_experiment_that_stopped_short_is_flagged_with_its_reason_not_ruled_out()
    {
        var timeZone = Incomplete(
            "TimeZone",
            TimeZoneExperiment.TimeZoneFactor,
            "TZ is not supported on Windows.",
            Row("UTC", failures: 0));

        var verdict = _engine.Decide(Input(isolation: Isolation(failures: 4), timeZone: timeZone));

        Assert.Contains(verdict.NotTested, entry => entry.Contains("TZ is not supported on Windows."));
        Assert.DoesNotContain(TimeZoneExperiment.TimeZoneFactor, verdict.RuledOut);
    }

    [Fact]
    public void A_verdict_is_still_built_when_only_one_experiment_completed()
    {
        var verdict = _engine.Decide(Input(
            isolation: Isolation(failures: 0),
            culture: Cultures(("en-US", 0), ("de-DE", 19)),
            timeZone: Incomplete("TimeZone", TimeZoneExperiment.TimeZoneFactor, "Budget exhausted.", Row("UTC", 0))));

        Assert.Equal(VerdictType.CultureDependent, verdict.Type);
        Assert.Contains(verdict.NotTested, entry => entry.Contains("Budget exhausted."));
    }

    [Fact]
    public void Runs_that_could_not_be_scored_are_not_counted_as_passes()
    {
        // Every run of the parallel condition went missing, so it says nothing: it must not become
        // "parallel never failed" and it must not be compared with anything.
        var parallelism = Completed(
            "Parallelism",
            ParallelismExperiment.ParallelismFactor,
            Row(ParallelismExperiment.Serial, failures: 0),
            Row(ParallelismExperiment.Parallel, failures: 0, total: Runs, missing: Runs));

        var verdict = _engine.Decide(Input(isolation: Isolation(failures: 0), parallelism: parallelism));

        Assert.Equal(VerdictType.NotReproduced, verdict.Type);
        Assert.Contains(verdict.NotTested, entry => entry.Contains(ParallelismExperiment.ParallelismFactor));
    }

    [Fact]
    public void Nothing_scoreable_at_all_is_inconclusive_with_the_lowest_confidence()
    {
        var isolation = Completed("Isolation", factor: null, Row(IsolationExperiment.Alone, 0, total: Runs, missing: Runs));

        var verdict = _engine.Decide(Input(isolation: isolation));

        Assert.Equal(VerdictType.Inconclusive, verdict.Type);
        Assert.InRange(verdict.ConfidenceScore, 0.0, 0.2);
        Assert.Null(verdict.ReproCommand);
    }

    [Fact]
    public void Too_few_runs_in_total_are_not_enough_to_say_a_failure_was_not_reproduced()
    {
        var verdict = _engine.Decide(Input(isolation: Isolation(failures: 0, total: 5)));

        Assert.Equal(VerdictType.Inconclusive, verdict.Type);
    }

    [Fact]
    public void A_test_that_also_fails_alone_is_never_called_order_dependent()
    {
        var verdict = _engine.Decide(Input(
            isolation: Isolation(failures: 6),
            scope: ScopeLadder((ScopeLevel.Alone, 6), (ScopeLevel.Class, 19)),
            polluters: Polluters(ExperimentTestSupport.SiblingTest)));

        Assert.NotEqual(VerdictType.OrderDependent, verdict.Type);
        Assert.NotEqual(VerdictType.ResourceConflict, verdict.Type);
    }

    [Fact]
    public void Order_dependence_without_a_named_polluter_says_so()
    {
        var verdict = _engine.Decide(Input(
            isolation: Isolation(failures: 0),
            scope: ScopeLadder((ScopeLevel.Alone, 0), (ScopeLevel.Class, 18))));

        Assert.Equal(VerdictType.OrderDependent, verdict.Type);
        Assert.Empty(verdict.SuspectTests);
        Assert.Contains(verdict.Notes, note => note.Contains("No polluter search ran"));
    }

    [Fact]
    public void Order_dependence_whose_polluter_search_came_back_empty_says_which_question_went_unanswered()
    {
        var verdict = _engine.Decide(Input(
            isolation: Isolation(failures: 0),
            scope: ScopeLadder((ScopeLevel.Alone, 0), (ScopeLevel.Class, 18)),
            polluters: NoPolluters()));

        Assert.Equal(VerdictType.OrderDependent, verdict.Type);
        Assert.Empty(verdict.SuspectTests);
        Assert.Contains(verdict.Notes, note => note.Contains("did not narrow the failure down"));
    }

    [Fact]
    public void A_single_culture_that_was_measured_proves_nothing_about_culture()
    {
        var verdict = _engine.Decide(Input(
            isolation: Isolation(failures: 0),
            culture: Cultures(("en-US", 19))));

        Assert.NotEqual(VerdictType.CultureDependent, verdict.Type);
    }

    [Fact]
    public void The_engine_refuses_an_input_that_is_not_there()
    {
        Assert.Throws<ArgumentNullException>(() => _engine.Decide(null!));
    }
}
