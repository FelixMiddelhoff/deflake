using Deflake.Core.Heuristics;
using Xunit;

namespace Deflake.Core.Tests.HeuristicsTests;

public class HttpRequestExceptionHeuristicTests
{
    private readonly HttpRequestExceptionHeuristic _heuristic = new();

    [Theory]
    [InlineData("HttpRequestException", null)]
    [InlineData("Connection refused", "")]
    [InlineData(null, "Connection reset by peer")]
    [InlineData("Connection aborted", "")]
    public void Match_WithNetworkError_Returns_Match(string? message, string? stack)
    {
        var result = _heuristic.Match(message, stack);
        Assert.NotNull(result);
        Assert.Equal("HttpRequestException", result.PatternName);
    }

    [Theory]
    [InlineData("No connection could be made", "")]
    [InlineData("Unable to connect to remote host", null)]
    [InlineData("Network is unreachable", "")]
    public void Match_WithConnectivityError_Returns_Match(string? message, string? stack)
    {
        var result = _heuristic.Match(message, stack);
        Assert.NotNull(result);
    }

    [Theory]
    [InlineData("Test passed", "")]
    [InlineData("Assertion failed", null)]
    [InlineData(null, null)]
    public void Match_WithoutNetworkError_Returns_Null(string? message, string? stack)
    {
        var result = _heuristic.Match(message, stack);
        Assert.Null(result);
    }

    [Fact]
    public void Match_IsCaseInsensitive()
    {
        var result1 = _heuristic.Match("HTTPREQUESTEXCEPTION", "");
        var result2 = _heuristic.Match("Connection REFUSED", "");

        Assert.NotNull(result1);
        Assert.NotNull(result2);
    }
}
