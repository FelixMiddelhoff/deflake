using Deflake.Core.Heuristics;
using Xunit;

namespace Deflake.Core.Tests.HeuristicsTests;

public class ObjectDisposedExceptionHeuristicTests
{
    private readonly ObjectDisposedExceptionHeuristic _heuristic = new();

    [Theory]
    [InlineData("System.ObjectDisposedException", null)]
    [InlineData("ObjectDisposedException: Cannot access disposed object", "")]
    [InlineData(null, "object has been disposed")]
    public void Match_WithDisposedException_Returns_Match(string? message, string? stack)
    {
        var result = _heuristic.Match(message, stack);
        Assert.NotNull(result);
        Assert.Equal("ObjectDisposedException", result.PatternName);
    }

    [Fact]
    public void Match_WithDisposeInStack_HigherConfidence()
    {
        var withoutDispose = _heuristic.Match("ObjectDisposedException", "at Method()");
        var withDispose = _heuristic.Match("ObjectDisposedException", "at Handler.Dispose() at Cleanup()");

        Assert.NotNull(withoutDispose);
        Assert.NotNull(withDispose);
        Assert.True(withDispose.Confidence > withoutDispose.Confidence);
    }

    [Theory]
    [InlineData("Test passed", "")]
    [InlineData("Assertion failed", null)]
    [InlineData(null, null)]
    public void Match_WithoutDisposedException_Returns_Null(string? message, string? stack)
    {
        var result = _heuristic.Match(message, stack);
        Assert.Null(result);
    }

    [Fact]
    public void Match_IsCaseInsensitive()
    {
        var result1 = _heuristic.Match("OBJECTDISPOSEDEXCEPTION", "");
        var result2 = _heuristic.Match("object has been DISPOSED", "");

        Assert.NotNull(result1);
        Assert.NotNull(result2);
    }

    [Fact]
    public void Match_WithPartialMatch_Returns_Null()
    {
        var result = _heuristic.Match("Disposed of resources", "");
        Assert.Null(result);
    }
}
