using System;
using Deflake.Core.Statistics;
using Xunit;

namespace Deflake.Core.Tests;

public class FailureRateTests
{
    [Fact]
    public void The_observed_rate_is_failures_over_runs()
    {
        Assert.Equal(0.25, new FailureRate(5, 20).Observed);
    }

    [Fact]
    public void Without_runs_the_rate_is_zero_and_nothing_is_known()
    {
        var rate = new FailureRate(0, 0);

        Assert.Equal(0, rate.Observed);
        Assert.Equal(0, rate.Lower);
        Assert.Equal(1, rate.Upper);
    }

    [Theory]
    [InlineData(-1, 10)]
    [InlineData(11, 10)]
    [InlineData(1, -1)]
    public void Impossible_counts_are_rejected(int failures, int runs)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new FailureRate(failures, runs));
    }

    // Reference values for the 95 % Wilson score interval, computed independently.
    [Theory]
    [InlineData(1, 10, 0.0179, 0.4041)]
    [InlineData(5, 20, 0.1119, 0.4687)]
    [InlineData(50, 100, 0.4038, 0.5962)]
    [InlineData(0, 20, 0.0, 0.1611)]
    [InlineData(20, 20, 0.8389, 1.0)]
    public void The_interval_matches_reference_values(int failures, int runs, double lower, double upper)
    {
        var rate = new FailureRate(failures, runs);

        Assert.Equal(lower, rate.Lower, 3);
        Assert.Equal(upper, rate.Upper, 3);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(0, 1000)]
    [InlineData(1000, 1000)]
    [InlineData(1, 1000000)]
    public void The_interval_always_stays_inside_zero_and_one_and_contains_the_observed_rate(int failures, int runs)
    {
        var rate = new FailureRate(failures, runs);

        Assert.InRange(rate.Lower, 0, 1);
        Assert.InRange(rate.Upper, 0, 1);
        Assert.True(rate.Lower <= rate.Observed && rate.Observed <= rate.Upper);
    }

    [Fact]
    public void More_runs_make_the_interval_narrower()
    {
        var few = new FailureRate(5, 20);
        var many = new FailureRate(50, 200);

        Assert.True(many.Upper - many.Lower < few.Upper - few.Lower);
    }

    [Fact]
    public void Clearly_different_rates_differ()
    {
        Assert.True(new FailureRate(0, 40).DiffersFrom(new FailureRate(30, 40)));
        Assert.True(new FailureRate(30, 40).DiffersFrom(new FailureRate(0, 40)));
    }

    [Fact]
    public void Similar_rates_do_not_differ()
    {
        Assert.False(new FailureRate(5, 20).DiffersFrom(new FailureRate(7, 20)));
    }

    [Fact]
    public void Too_few_runs_cannot_show_a_difference()
    {
        Assert.False(new FailureRate(0, 3).DiffersFrom(new FailureRate(3, 3)));
    }

    [Fact]
    public void A_condition_without_runs_never_differs_from_anything()
    {
        Assert.False(new FailureRate(0, 0).DiffersFrom(new FailureRate(20, 20)));
        Assert.False(new FailureRate(20, 20).DiffersFrom(new FailureRate(0, 0)));
    }

    [Fact]
    public void Twenty_runs_without_failure_still_allow_a_rate_of_about_fourteen_percent()
    {
        Assert.Equal(0.1391, FailureRate.UpperBoundAfterNoFailures(20), 3);
    }

    [Fact]
    public void The_upper_bound_after_no_failures_is_close_to_three_over_n_for_many_runs()
    {
        Assert.Equal(3.0 / 300, FailureRate.UpperBoundAfterNoFailures(300), 3);
    }

    [Fact]
    public void Without_any_runs_the_upper_bound_is_one()
    {
        Assert.Equal(1, FailureRate.UpperBoundAfterNoFailures(0));
    }

    [Theory]
    [InlineData(0.5, 5)]
    [InlineData(0.1, 29)]
    [InlineData(0.01, 299)]
    [InlineData(1.0, 1)]
    public void The_runs_needed_to_see_a_failure_grow_as_the_rate_shrinks(double rate, int expected)
    {
        Assert.Equal(expected, FailureRate.RunsNeededToSee(rate));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    public void The_runs_needed_are_undefined_for_impossible_rates(double rate)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => FailureRate.RunsNeededToSee(rate));
    }

    [Fact]
    public void The_runs_needed_and_the_upper_bound_agree()
    {
        var runs = FailureRate.RunsNeededToSee(0.1);

        Assert.True(FailureRate.UpperBoundAfterNoFailures(runs) <= 0.1);
        Assert.True(FailureRate.UpperBoundAfterNoFailures(runs - 1) > 0.1);
    }
}
