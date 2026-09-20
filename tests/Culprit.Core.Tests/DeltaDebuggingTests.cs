using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Culprit.Core.Search;
using Xunit;

namespace Culprit.Core.Tests;

public class DeltaDebuggingTests
{
    private static readonly IReadOnlyList<string> Letters = "abcdefghij".Select(c => c.ToString()).ToList();

    [Fact]
    public void A_single_culprit_is_found()
    {
        var result = DeltaDebugging.Minimize(Letters, set => set.Contains("g"));

        Assert.True(result.InputFails);
        Assert.True(result.Completed);
        Assert.Equal(new[] { "g" }, result.Minimal);
    }

    [Fact]
    public void Two_items_that_only_fail_together_are_both_kept_in_their_original_order()
    {
        var result = DeltaDebugging.Minimize(Letters, set => set.Contains("b") && set.Contains("h"));

        Assert.Equal(new[] { "b", "h" }, result.Minimal);
        Assert.True(result.Completed);
    }

    [Fact]
    public void Items_at_both_ends_and_in_the_middle_are_found()
    {
        var result = DeltaDebugging.Minimize(Letters, set => set.Contains("a") && set.Contains("e") && set.Contains("j"));

        Assert.Equal(new[] { "a", "e", "j" }, result.Minimal);
    }

    [Fact]
    public void Nothing_is_needed_when_the_failure_appears_without_any_item()
    {
        var result = DeltaDebugging.Minimize(Letters, _ => true);

        Assert.Empty(result.Minimal);
        Assert.True(result.InputFails);
        Assert.True(result.Completed);
    }

    [Fact]
    public void An_empty_input_that_fails_is_its_own_minimum()
    {
        var result = DeltaDebugging.Minimize(new List<string>(), _ => true);

        Assert.Empty(result.Minimal);
        Assert.True(result.InputFails);
    }

    [Fact]
    public void An_input_that_does_not_fail_is_reported_and_returned_unchanged()
    {
        var result = DeltaDebugging.Minimize(Letters, _ => false);

        Assert.False(result.InputFails);
        Assert.True(result.Completed);
        Assert.Equal(Letters, result.Minimal);
        Assert.Equal(1, result.TestsRun);
    }

    [Fact]
    public void A_single_item_that_is_needed_stays()
    {
        var result = DeltaDebugging.Minimize(new[] { "only" }, set => set.Contains("only"));

        Assert.Equal(new[] { "only" }, result.Minimal);
        Assert.True(result.Completed);
    }

    [Fact]
    public void Order_matters_to_the_predicate_and_is_preserved()
    {
        // Fails only when "x" comes before "y": subsets must never reorder items.
        var items = new[] { "p", "x", "q", "y", "r" };

        var result = DeltaDebugging.Minimize(items, set =>
        {
            var x = set.ToList().IndexOf("x");
            var y = set.ToList().IndexOf("y");
            return x >= 0 && y >= 0 && x < y;
        });

        Assert.Equal(new[] { "x", "y" }, result.Minimal);
    }

    [Fact]
    public void Every_set_is_run_at_most_once()
    {
        var seen = new HashSet<string>();
        var repeats = 0;

        DeltaDebugging.Minimize(Letters, set =>
        {
            if (!seen.Add(string.Concat(set)))
            {
                repeats++;
            }

            return set.Contains("c") && set.Contains("d");
        });

        Assert.Equal(0, repeats);
    }

    [Fact]
    public void The_reported_number_of_tests_is_the_number_of_predicate_calls()
    {
        var calls = 0;

        var result = DeltaDebugging.Minimize(Letters, set =>
        {
            calls++;
            return set.Contains("f");
        });

        Assert.Equal(calls, result.TestsRun);
    }

    [Fact]
    public void A_single_culprit_among_many_needs_few_experiments()
    {
        var items = Enumerable.Range(0, 64).ToList();

        var result = DeltaDebugging.Minimize(items, set => set.Contains(41));

        Assert.Equal(new[] { 41 }, result.Minimal);
        Assert.True(result.TestsRun < 30, $"took {result.TestsRun} experiments");
    }

    [Fact]
    public void When_the_budget_runs_out_the_best_failing_set_so_far_is_returned()
    {
        var items = Enumerable.Range(0, 64).ToList();
        var required = new[] { 3, 20, 41, 60 };

        var result = DeltaDebugging.Minimize(items, set => required.All(set.Contains), maxTests: 6);

        Assert.False(result.Completed);
        Assert.True(result.InputFails);
        Assert.True(result.TestsRun <= 6);
        Assert.True(required.All(result.Minimal.Contains), "the returned set must still fail");
    }

    [Fact]
    public void A_budget_of_one_only_checks_that_the_input_fails()
    {
        var result = DeltaDebugging.Minimize(Letters, set => set.Contains("a"), maxTests: 1);

        Assert.False(result.Completed);
        Assert.Equal(Letters, result.Minimal);
        Assert.Equal(1, result.TestsRun);
    }

    [Fact]
    public void A_budget_smaller_than_one_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DeltaDebugging.Minimize(Letters, _ => true, maxTests: 0));
    }

    [Fact]
    public void Missing_arguments_are_rejected()
    {
        Assert.Throws<ArgumentNullException>(() => DeltaDebugging.Minimize<string>(null!, _ => true));
        Assert.Throws<ArgumentNullException>(() => DeltaDebugging.Minimize(Letters, null!));
    }

    [Fact]
    public void Cancellation_stops_the_search()
    {
        using var source = new CancellationTokenSource();

        Assert.Throws<OperationCanceledException>(() => DeltaDebugging.Minimize(
            Letters,
            set =>
            {
                source.Cancel();
                return set.Contains("a");
            },
            cancellation: source.Token));
    }

    [Fact]
    public void Searches_are_repeatable()
    {
        Func<IReadOnlyList<string>, bool> fails = set => set.Contains("d") && set.Contains("i");

        var first = DeltaDebugging.Minimize(Letters, fails);
        var second = DeltaDebugging.Minimize(Letters, fails);

        Assert.Equal(first.Minimal, second.Minimal);
        Assert.Equal(first.TestsRun, second.TestsRun);
    }

    [Fact]
    public void For_any_required_set_the_result_is_exactly_that_set()
    {
        var random = new Random(20260920);
        for (var round = 0; round < 300; round++)
        {
            var size = random.Next(1, 40);
            var items = Enumerable.Range(0, size).ToList();
            var required = items.Where(_ => random.Next(4) == 0).ToList();

            var result = DeltaDebugging.Minimize(items, set => required.All(set.Contains));

            Assert.True(result.Completed);
            Assert.Equal(required, result.Minimal);
        }
    }

    [Fact]
    public void For_arbitrary_deterministic_predicates_a_completed_result_fails_and_is_one_minimal()
    {
        var random = new Random(7);
        var checkedRounds = 0;
        for (var round = 0; round < 400; round++)
        {
            var size = random.Next(1, 24);
            var items = Enumerable.Range(0, size).ToList();
            var salt = random.Next();
            bool Fails(IReadOnlyList<int> set) => Math.Abs(HashCode.Combine(salt, set.Count, set.Sum(), set.Count > 0 ? set[0] : -1)) % 3 == 0;

            var result = DeltaDebugging.Minimize(items, Fails);

            if (!result.InputFails)
            {
                continue;
            }

            checkedRounds++;
            Assert.True(result.Completed);
            Assert.True(Fails(result.Minimal), "the result must still fail");
            for (var removed = 0; removed < result.Minimal.Count; removed++)
            {
                var smaller = result.Minimal.Where((_, index) => index != removed).ToList();
                Assert.False(Fails(smaller), $"removing item {removed} should make it pass (round {round})");
            }
        }

        Assert.True(checkedRounds > 50, "the property test must exercise enough failing inputs");
    }
}
