using Deflake.Core.Heuristics;
using Xunit;

namespace Deflake.Core.Tests.HeuristicsTests;

public class FileInUseHeuristicTests
{
    private readonly FileInUseHeuristic _heuristic = new();

    [Theory]
    [InlineData("File is in use", null)]
    [InlineData("The file is being used by another process", "")]
    [InlineData(null, "Access to the file is denied")]
    [InlineData("Sharing violation", "")]
    public void Match_WithFileInUse_Returns_Match(string? message, string? stack)
    {
        var result = _heuristic.Match(message, stack);
        Assert.NotNull(result);
        Assert.Equal("FileInUse", result.PatternName);
    }

    [Fact]
    public void Match_WithIOExceptionAndAccessDenied_Returns_Match()
    {
        var result = _heuristic.Match("IOException: access denied to output.log", "");
        Assert.NotNull(result);
    }

    [Theory]
    [InlineData("Test passed", "")]
    [InlineData("File not found", null)]
    [InlineData(null, null)]
    public void Match_WithoutFileInUse_Returns_Null(string? message, string? stack)
    {
        var result = _heuristic.Match(message, stack);
        Assert.Null(result);
    }

    [Fact]
    public void Match_IsCaseInsensitive()
    {
        var result1 = _heuristic.Match("FILE IS IN USE", "");
        var result2 = _heuristic.Match("The FILE is being used by another process", "");

        Assert.NotNull(result1);
        Assert.NotNull(result2);
    }
}
