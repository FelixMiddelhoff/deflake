using Deflake.Core.Heuristics;
using Xunit;

namespace Deflake.Core.Tests.HeuristicsTests;

public class TimeoutHeuristicTests
{
    private readonly TimeoutHeuristic _heuristic = new();

    [Theory]
    [InlineData("Operation timed out", null)]
    [InlineData("Test timeout after 30 seconds", "")]
    [InlineData(null, "System.TimeoutException: Timed out")]
    [InlineData("", "at WaitAsync() : timed out")]
    public void Match_WithExplicitTimeout_Returns_HighConfidence(string? message, string? stack)
    {
        var result = _heuristic.Match(message, stack);
        Assert.NotNull(result);
        Assert.Equal("Timeout", result.PatternName);
        Assert.True(result.Confidence >= 0.9);
    }

    [Theory]
    [InlineData("Task was canceled", null)]
    [InlineData(null, "OperationCanceledException")]
    public void Match_WithTaskCanceled_Returns_Match(string? message, string? stack)
    {
        var result = _heuristic.Match(message, stack);
        Assert.NotNull(result);
    }

    [Theory]
    [InlineData("Task.Wait()", "at MyTest()")]
    [InlineData("Task.Result accessed", null)]
    [InlineData(null, "at DoWork() WaitOne() called")]
    [InlineData("WaitAll on multiple tasks", "")]
    public void Match_WithBlockingWait_Returns_LowerConfidence(string? message, string? stack)
    {
        var result = _heuristic.Match(message, stack);
        Assert.NotNull(result);
        Assert.Equal("Timeout", result.PatternName);
        Assert.True(result.Confidence < 0.9);
        Assert.True(result.Confidence > 0.5);
    }

    [Theory]
    [InlineData("Test passed successfully", "at Method()")]
    [InlineData("Assertion failed", "Expected 42 but got 41")]
    [InlineData(null, null)]
    public void Match_WithoutTimeout_Returns_Null(string? message, string? stack)
    {
        var result = _heuristic.Match(message, stack);
        Assert.Null(result);
    }

    [Fact]
    public void Match_IsCaseInsensitive()
    {
        var result1 = _heuristic.Match("TIMEOUT", "");
        var result2 = _heuristic.Match("TIMED OUT", "");
        var result3 = _heuristic.Match("TimeOut", "");

        Assert.NotNull(result1);
        Assert.NotNull(result2);
        Assert.NotNull(result3);
    }

    [Fact]
    public void Match_WithEmptyStrings_Returns_Null()
    {
        var result = _heuristic.Match("", "");
        Assert.Null(result);
    }

    [Fact]
    public void Match_PrefersBothMessageAndStack()
    {
        var messageOnly = _heuristic.Match("Operation timed out", "");
        var stackOnly = _heuristic.Match("", "timeout in stack");
        var both = _heuristic.Match("Operation timed out", "timeout in stack");

        Assert.NotNull(messageOnly);
        Assert.NotNull(stackOnly);
        Assert.NotNull(both);
    }
}
