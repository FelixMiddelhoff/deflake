using Deflake.Core.Heuristics;
using Xunit;

namespace Deflake.Core.Tests.HeuristicsTests;

public class CollectionModifiedHeuristicTests
{
    private readonly CollectionModifiedHeuristic _heuristic = new();

    [Theory]
    [InlineData("Collection was modified", null)]
    [InlineData("The collection has been modified during enumeration", "")]
    [InlineData(null, "InvalidOperationException: Collection was modified")]
    public void Match_WithCollectionModified_Returns_Match(string? message, string? stack)
    {
        var result = _heuristic.Match(message, stack);
        Assert.NotNull(result);
        Assert.Equal("CollectionModified", result.PatternName);
        Assert.True(result.Confidence > 0.85);
    }

    [Fact]
    public void Match_WithInvalidOperationAndEnumerate_Returns_Match()
    {
        var result = _heuristic.Match("InvalidOperationException during enumeration", "");
        Assert.NotNull(result);
    }

    [Theory]
    [InlineData("Test passed", "")]
    [InlineData("Collection is empty", null)]
    [InlineData(null, null)]
    public void Match_WithoutModification_Returns_Null(string? message, string? stack)
    {
        var result = _heuristic.Match(message, stack);
        Assert.Null(result);
    }

    [Fact]
    public void Match_IsCaseInsensitive()
    {
        var result1 = _heuristic.Match("COLLECTION WAS MODIFIED", "");
        var result2 = _heuristic.Match("collection has been MODIFIED", "");

        Assert.NotNull(result1);
        Assert.NotNull(result2);
    }
}
