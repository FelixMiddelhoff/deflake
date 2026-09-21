using Deflake.Core.Heuristics;
using Xunit;

namespace Deflake.Core.Tests.HeuristicsTests;

public class DateTimeAssertionHeuristicTests
{
    private readonly DateTimeAssertionHeuristic _heuristic = new();

    [Theory]
    [InlineData("DateTime value was 2024-01-01 12:00:00 but expected 2024-01-01 11:59:59", "Assert.Equal")]
    [InlineData("DateTime.UtcNow assertion failed", "at TestMethod()")]
    [InlineData(null, "at TestMethod() DateTime was not equal to")]
    public void Match_WithDateTimeAssertion_Returns_Match(string? message, string? stack)
    {
        var result = _heuristic.Match(message, stack);
        Assert.NotNull(result);
        Assert.Equal("DateTimeAssertion", result.PatternName);
    }

    [Theory]
    [InlineData("TimeSpan exceeded", "Expected 00:00:10 but was 00:00:11")]
    [InlineData("DateTimeOffset assertion", "should be 2024-01-01")]
    public void Match_WithRelatedDateTimeTypes_Returns_Match(string? message, string? stack)
    {
        var result = _heuristic.Match(message, stack);
        Assert.NotNull(result);
    }

    [Theory]
    [InlineData("Test passed", "")]
    [InlineData("Assertion failed on integer", null)]
    [InlineData(null, null)]
    public void Match_WithoutDateTimeAssertion_Returns_Null(string? message, string? stack)
    {
        var result = _heuristic.Match(message, stack);
        Assert.Null(result);
    }

    [Fact]
    public void Match_RequiresBothDateTimeAndAssertion()
    {
        // DateTime mentioned but no assertion context
        var dateTimeOnly = _heuristic.Match("DateTime.UtcNow returned something", "at Method()");
        Assert.Null(dateTimeOnly);

        // Assertion mentioned but no DateTime reference
        var assertionOnly = _heuristic.Match("Assertion failed: value was wrong", "");
        Assert.Null(assertionOnly);

        // Both present
        var both = _heuristic.Match("DateTime assertion failed", "Expected DateTime.UtcNow");
        Assert.NotNull(both);
    }

    [Fact]
    public void Match_IsCaseInsensitive()
    {
        var result1 = _heuristic.Match("DATETIME assertion failed", "");
        var result2 = _heuristic.Match("Expected DateTime", "ASSERT");

        Assert.NotNull(result1);
        Assert.NotNull(result2);
    }
}
