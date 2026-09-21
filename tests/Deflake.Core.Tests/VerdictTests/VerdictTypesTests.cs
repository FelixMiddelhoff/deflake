using System;
using System.Collections.Generic;
using Deflake.Core.Execution;
using Deflake.Core.Experiments;
using Deflake.Core.Verdicts;
using Xunit;
using static Deflake.Core.Tests.VerdictTestSupport;

namespace Deflake.Core.Tests;

/// <summary>
/// The verdict and its evidence rows as data: what they refuse to hold, and what they keep when the
/// verdict is confirmed or withdrawn. A report and a JSON schema are built straight off these, so a
/// row that could hold nonsense would print nonsense.
/// </summary>
public sealed class VerdictTypesTests
{
    private static readonly TestIdentity Test = ExperimentTestSupport.Identity();

    [Fact]
    public void An_evidence_row_reports_the_rate_it_measured()
    {
        var row = new VerdictEvidence("Load", "UnderLoad", runs: 40, failures: 10, intervalLow: 0.14, intervalHigh: 0.40);

        Assert.Equal(25, row.FailureRatePercent, 3);
        Assert.Equal(10, row.Rate.Failures);
        Assert.Equal(40, row.Rate.Runs);
    }

    [Fact]
    public void An_evidence_row_without_runs_reports_no_rate_rather_than_dividing_by_zero()
    {
        var row = new VerdictEvidence("Scope", "Class", runs: 0, failures: 0, intervalLow: 0, intervalHigh: 1);

        Assert.Equal(0, row.FailureRatePercent);
    }

    [Fact]
    public void An_evidence_row_is_built_from_the_run_that_produced_it()
    {
        var run = Row(LoadExperiment.UnderLoad, failures: 7, total: 25, missing: 5);

        var row = VerdictEvidence.From(LoadExperiment.LoadFactor, run, "Timeout");

        Assert.Equal(LoadExperiment.LoadFactor, row.Factor);
        Assert.Equal(LoadExperiment.UnderLoad, row.Condition);
        Assert.Equal(20, row.Runs);
        Assert.Equal(7, row.Failures);
        Assert.Equal("Timeout", row.HeuristicSupport);
    }

    [Theory]
    [InlineData("", "Alone", 10, 1, 0.0, 1.0)]
    [InlineData("Scope", " ", 10, 1, 0.0, 1.0)]
    [InlineData("Scope", "Alone", -1, 0, 0.0, 1.0)]
    [InlineData("Scope", "Alone", 10, 11, 0.0, 1.0)]
    [InlineData("Scope", "Alone", 10, -1, 0.0, 1.0)]
    [InlineData("Scope", "Alone", 10, 1, -0.1, 1.0)]
    [InlineData("Scope", "Alone", 10, 1, 0.0, 1.4)]
    [InlineData("Scope", "Alone", 10, 1, 0.8, 0.2)]
    public void An_evidence_row_refuses_numbers_that_cannot_be_true(
        string factor,
        string condition,
        int runs,
        int failures,
        double low,
        double high)
    {
        Assert.ThrowsAny<ArgumentException>(() => new VerdictEvidence(factor, condition, runs, failures, low, high));
    }

    [Fact]
    public void A_verdict_needs_a_test_to_be_about()
    {
        Assert.Throws<ArgumentNullException>(() => new Verdict(VerdictType.Inconclusive, null!));
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    public void A_confidence_is_a_number_between_zero_and_one(double confidence)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new Verdict(VerdictType.Inconclusive, Test) { ConfidenceScore = confidence });
    }

    [Fact]
    public void A_verdict_refuses_a_blank_entry_in_any_of_its_lists()
    {
        Assert.Throws<ArgumentException>(
            () => new Verdict(VerdictType.Inconclusive, Test) { SuspectTests = new[] { "Shop.Tests.A", " " } });
        Assert.Throws<ArgumentException>(
            () => new Verdict(VerdictType.Inconclusive, Test) { RuledOut = new[] { string.Empty } });
    }

    [Fact]
    public void A_verdict_copies_the_lists_it_is_given_so_they_cannot_change_underneath_it()
    {
        var suspects = new List<string> { ExperimentTestSupport.SiblingTest };
        var verdict = new Verdict(VerdictType.OrderDependent, Test) { SuspectTests = suspects };

        suspects.Add("Shop.OrderTests.Added_later");

        Assert.Single(verdict.SuspectTests);
    }

    [Fact]
    public void Confirming_a_verdict_leaves_everything_else_as_it_was()
    {
        var verdict = new Verdict(VerdictType.ParallelismDependent, Test)
        {
            ConfidenceScore = 0.8,
            RuledOut = new[] { "Culture" },
        }.WithRepro("dotnet test", ExperimentTestSupport.Baseline(), "note");

        var confirmed = verdict.Confirmed();

        Assert.True(confirmed.ReproVerified);
        Assert.False(verdict.ReproVerified);
        Assert.Equal(0.8, confirmed.ConfidenceScore);
        Assert.Equal(new[] { "Culture" }, confirmed.RuledOut);
        Assert.Equal("note", confirmed.ReproNote);
    }

    [Fact]
    public void A_downgrade_has_to_say_why()
    {
        var verdict = new Verdict(VerdictType.TimingSensitive, Test);

        Assert.Throws<ArgumentException>(() => verdict.DowngradedTo(VerdictType.Inconclusive, 0.2, "  "));
    }

    [Fact]
    public void A_downgrade_adds_its_reason_to_the_notes_that_were_already_there()
    {
        var verdict = new Verdict(VerdictType.TimingSensitive, Test) { Notes = new[] { "measured under load" } };

        var downgraded = verdict.DowngradedTo(VerdictType.Inconclusive, 0.2, "did not hold up");

        Assert.Equal(new[] { "measured under load", "did not hold up" }, downgraded.Notes);
        Assert.Single(verdict.Notes);
    }

    [Fact]
    public void A_verdict_reads_as_its_answer_and_its_confidence()
    {
        var verdict = new Verdict(VerdictType.CultureDependent, Test) { ConfidenceScore = 0.82 };

        Assert.Contains("CultureDependent", verdict.ToString());
        Assert.Contains(ExperimentTestSupport.FailingTest, verdict.ToString());
    }

    [Fact]
    public void The_stack_trace_reader_keeps_the_code_under_test_and_drops_the_framework()
    {
        var members = StackTraceSuspects.From(
            "   at Shop.Clock.Now() in C:\\repo\\Clock.cs:line 9\r\n"
            + "   at Xunit.Sdk.TestInvoker`1.AggregateAsync()\r\n"
            + "   at System.Threading.Tasks.Task.Wait()");

        Assert.Equal(new[] { "Shop.Clock.Now" }, members);
    }

    [Fact]
    public void The_stack_trace_reader_names_the_method_that_owns_a_lambda_frame()
    {
        var members = StackTraceSuspects.From("   at Shop.OrderService.<Total>b__3_0(Order order)");

        Assert.Equal(new[] { "Shop.OrderService.Total" }, members);
    }

    [Fact]
    public void The_stack_trace_reader_lists_a_member_once_however_often_it_recurses()
    {
        var members = StackTraceSuspects.From(
            "   at Shop.Recursive.Step()\n   at Shop.Recursive.Step()\n   at Shop.Recursive.Step()");

        Assert.Equal(new[] { "Shop.Recursive.Step" }, members);
    }

    [Fact]
    public void The_stack_trace_reader_stops_before_the_list_becomes_noise()
    {
        var frames = string.Empty;
        for (var index = 0; index < 20; index++)
        {
            frames += $"   at Shop.Deep.Frame{index}()\n";
        }

        Assert.Equal(StackTraceSuspects.MaxSuspects, StackTraceSuspects.From(frames).Count);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("no frames here, just a message")]
    [InlineData("   at System.IO.File.Delete(String path)")]
    public void The_stack_trace_reader_names_nobody_rather_than_guessing(string? stackTrace)
    {
        Assert.Empty(StackTraceSuspects.From(stackTrace));
    }

    [Fact]
    public void An_input_needs_the_test_it_is_about()
    {
        Assert.Throws<ArgumentNullException>(() => new VerdictInput(null!));
    }

    [Fact]
    public void An_input_lists_only_the_experiments_it_was_given()
    {
        var input = Input(isolation: Isolation(failures: 1), load: Load(idleFailures: 1, loadedFailures: 2));

        Assert.Collection(
            input.Experiments(),
            experiment => Assert.Equal(VerdictInput.IsolationFactor, experiment.Factor),
            experiment => Assert.Equal(LoadExperiment.LoadFactor, experiment.Factor));
    }

    [Fact]
    public void A_request_is_all_a_repro_needs_to_be_rendered_again_later()
    {
        var verdict = new VerdictEngine().Decide(Input(
            isolation: Isolation(failures: 0),
            culture: Cultures(("en-US", 0), ("de-DE", 19))));

        Assert.NotNull(verdict.ReproRequest);
        Assert.Equal(
            verdict.ReproCommand,
            ReproCommandBuilder.Build(verdict.ReproRequest!, ExperimentTestSupport.Baseline().EnvironmentVariables));
    }

    [Fact]
    public void Every_verdict_type_is_reachable_from_the_enum_the_reports_switch_on()
    {
        Assert.Equal(10, Enum.GetValues<VerdictType>().Length);
    }
}
