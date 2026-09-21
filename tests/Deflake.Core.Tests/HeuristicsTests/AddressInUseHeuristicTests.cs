using Deflake.Core.Heuristics;
using Xunit;

namespace Deflake.Core.Tests.HeuristicsTests;

public class AddressInUseHeuristicTests
{
    private readonly AddressInUseHeuristic _heuristic = new();

    [Theory]
    [InlineData("Address already in use", null)]
    [InlineData("Address is already in use: 0.0.0.0:8080", "")]
    [InlineData(null, "Port already in use")]
    [InlineData("bind: address already in use", "")]
    public void Match_WithAddressInUse_Returns_HighConfidence(string? message, string? stack)
    {
        var result = _heuristic.Match(message, stack);
        Assert.NotNull(result);
        Assert.Equal("AddressInUse", result.PatternName);
        Assert.True(result.Confidence >= 0.95);
    }

    [Theory]
    [InlineData("WSAEADDRINUSE: Address in use", "")]
    [InlineData("EADDRINUSE", null)]
    public void Match_WithSocketErrorCode_Returns_Match(string? message, string? stack)
    {
        var result = _heuristic.Match(message, stack);
        Assert.NotNull(result);
    }

    [Theory]
    [InlineData("Test passed", "")]
    [InlineData("Connection timeout", null)]
    [InlineData(null, null)]
    public void Match_WithoutAddressInUse_Returns_Null(string? message, string? stack)
    {
        var result = _heuristic.Match(message, stack);
        Assert.Null(result);
    }

    [Fact]
    public void Match_IsCaseInsensitive()
    {
        var result1 = _heuristic.Match("ADDRESS ALREADY IN USE", "");
        var result2 = _heuristic.Match("Port ALREADY in USE", "");

        Assert.NotNull(result1);
        Assert.NotNull(result2);
    }
}
