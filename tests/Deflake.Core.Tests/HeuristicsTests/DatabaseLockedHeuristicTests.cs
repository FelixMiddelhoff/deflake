using Deflake.Core.Heuristics;
using Xunit;

namespace Deflake.Core.Tests.HeuristicsTests;

public class DatabaseLockedHeuristicTests
{
    private readonly DatabaseLockedHeuristic _heuristic = new();

    [Theory]
    [InlineData("Database is locked", null)]
    [InlineData("The database file is locked", "")]
    [InlineData(null, "SQLiteErrorCode.Locked")]
    [InlineData("Timeout waiting for lock", "")]
    public void Match_WithDatabaseLocked_Returns_HighConfidence(string? message, string? stack)
    {
        var result = _heuristic.Match(message, stack);
        Assert.NotNull(result);
        Assert.Equal("DatabaseLocked", result.PatternName);
        Assert.True(result.Confidence >= 0.9);
    }

    [Theory]
    [InlineData("SQLiteErrorCode.CantOpen", "")]
    [InlineData("Locked out of database", null)]
    public void Match_WithLockVariants_Returns_Match(string? message, string? stack)
    {
        var result = _heuristic.Match(message, stack);
        Assert.NotNull(result);
    }

    [Theory]
    [InlineData("Test passed", "")]
    [InlineData("Database connection timeout", null)]
    [InlineData(null, null)]
    public void Match_WithoutDatabaseLocked_Returns_Null(string? message, string? stack)
    {
        var result = _heuristic.Match(message, stack);
        Assert.Null(result);
    }

    [Fact]
    public void Match_IsCaseInsensitive()
    {
        var result1 = _heuristic.Match("DATABASE IS LOCKED", "");
        var result2 = _heuristic.Match("locked out", "");

        Assert.NotNull(result1);
        Assert.NotNull(result2);
    }
}
