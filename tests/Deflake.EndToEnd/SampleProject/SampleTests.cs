using System;
using System.Threading;
using Xunit;

namespace SampleProject;

/// <summary>
/// Four tests with a fixed, known outcome, used as ground truth by
/// <c>Deflake.Core.Tests.ExecutionTests</c>. Do not add flakiness or change an outcome without
/// updating the assertions there: the exact pass/fail counts are asserted by name.
/// </summary>
public class Tests
{
    /// <summary>Name of the environment variable <see cref="Environment_variable_is_expected_value"/> reads.</summary>
    public const string EnvironmentVariableName = "DEFLAKE_SAMPLE_ENV";

    /// <summary>The value that makes <see cref="Environment_variable_is_expected_value"/> pass.</summary>
    public const string ExpectedEnvironmentValue = "expected-value";

    /// <summary>Name of the environment variable that controls how long <see cref="Sleeps_for_configured_milliseconds"/> sleeps.</summary>
    public const string SleepMillisecondsVariableName = "DEFLAKE_SAMPLE_SLEEP_MS";

    [Fact]
    public void Addition_is_commutative()
    {
        Assert.Equal(2 + 3, 3 + 2);
    }

    /// <summary>Always fails, by design: ground truth for "a run with a known failure".</summary>
    [Fact]
    public void Always_fails()
    {
        Assert.Fail("designed to fail: this is deflake's own ground truth, not a real bug");
    }

    /// <summary>Passes only when <see cref="EnvironmentVariableName"/> was passed through to the process.</summary>
    [Fact]
    public void Environment_variable_is_expected_value()
    {
        var value = Environment.GetEnvironmentVariable(EnvironmentVariableName);
        Assert.Equal(ExpectedEnvironmentValue, value);
    }

    /// <summary>
    /// Sleeps for <see cref="SleepMillisecondsVariableName"/> milliseconds (0 when unset), then
    /// passes. Used to make a run take exactly as long as a timeout test needs.
    /// </summary>
    [Fact]
    public void Sleeps_for_configured_milliseconds()
    {
        var raw = Environment.GetEnvironmentVariable(SleepMillisecondsVariableName);
        var milliseconds = int.TryParse(raw, out var parsed) ? parsed : 0;
        Thread.Sleep(milliseconds);
        Assert.True(true);
    }
}
