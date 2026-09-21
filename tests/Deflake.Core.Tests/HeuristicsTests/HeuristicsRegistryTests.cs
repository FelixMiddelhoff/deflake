using System;
using System.Collections.Generic;
using System.Linq;
using Deflake.Core.Heuristics;
using Xunit;

namespace Deflake.Core.Tests.HeuristicsTests;

public class HeuristicsRegistryTests
{
    [Fact]
    public void Analyze_WithNullInputs_ReturnsEmptyFindings()
    {
        var registry = new HeuristicsRegistry();
        var findings = registry.Analyze(null, null);
        Assert.False(findings.Found);
        Assert.Empty(findings.Results);
    }

    [Fact]
    public void Analyze_WithEmptyStrings_ReturnsEmptyFindings()
    {
        var registry = new HeuristicsRegistry();
        var findings = registry.Analyze("", "");
        Assert.False(findings.Found);
    }

    [Fact]
    public void Analyze_WithTimeoutError_FindsTimeout()
    {
        var registry = new HeuristicsRegistry();
        var findings = registry.Analyze("Operation timed out", "at SomeMethod()");
        Assert.True(findings.Found);
        Assert.Contains(findings.Results, r => r.PatternName == "Timeout");
    }

    [Fact]
    public void Analyze_ReturnsSortedByConfidence()
    {
        var registry = new HeuristicsRegistry();
        // Message that triggers both TimeoutHeuristic (high confidence) and DateTimeAssertionHeuristic (lower confidence)
        var findings = registry.Analyze(
            "Test expected DateTime value but timed out",
            "System.DateTime.UtcNow assertion failed"
        );

        if (findings.Results.Count > 1)
        {
            for (int i = 0; i < findings.Results.Count - 1; i++)
            {
                Assert.True(findings.Results[i].Confidence >= findings.Results[i + 1].Confidence);
            }
        }
    }

    [Fact]
    public void Analyze_WithNoMatches_ReturnsEmpty()
    {
        var registry = new HeuristicsRegistry();
        var findings = registry.Analyze(
            "Test failed for unknown reason",
            "at UserCode.TestMethod() at line 42"
        );
        Assert.False(findings.Found);
    }

    [Fact]
    public void Analyze_WithObjectDisposedException_Finds()
    {
        var registry = new HeuristicsRegistry();
        var findings = registry.Analyze(
            "System.ObjectDisposedException: object has been disposed",
            null
        );
        Assert.True(findings.Found);
        Assert.Contains(findings.Results, r => r.PatternName == "ObjectDisposedException");
    }

    [Fact]
    public void Analyze_WithCollectionModified_Finds()
    {
        var registry = new HeuristicsRegistry();
        var findings = registry.Analyze(
            "System.InvalidOperationException: Collection was modified",
            null
        );
        Assert.True(findings.Found);
        Assert.Contains(findings.Results, r => r.PatternName == "CollectionModified");
    }

    [Fact]
    public void Analyze_WithAddressInUse_Finds()
    {
        var registry = new HeuristicsRegistry();
        var findings = registry.Analyze(
            "Address already in use: 0.0.0.0:8080",
            "System.Net.Sockets.SocketException: Address already in use"
        );
        Assert.True(findings.Found);
        Assert.Contains(findings.Results, r => r.PatternName == "AddressInUse");
    }

    [Fact]
    public void Analyze_WithFileInUse_Finds()
    {
        var registry = new HeuristicsRegistry();
        var findings = registry.Analyze(
            "The file 'output.txt' is in use by another process",
            "System.IO.IOException"
        );
        Assert.True(findings.Found);
        Assert.Contains(findings.Results, r => r.PatternName == "FileInUse");
    }

    [Fact]
    public void Analyze_WithDatabaseLocked_Finds()
    {
        var registry = new HeuristicsRegistry();
        var findings = registry.Analyze(
            "SQLite error (5): The database file is locked",
            null
        );
        Assert.True(findings.Found);
        Assert.Contains(findings.Results, r => r.PatternName == "DatabaseLocked");
    }

    [Fact]
    public void Analyze_WithHttpRequestException_Finds()
    {
        var registry = new HeuristicsRegistry();
        var findings = registry.Analyze(
            "An error occurred while sending the request: Connection refused",
            "System.Net.Http.HttpRequestException"
        );
        Assert.True(findings.Found);
        Assert.Contains(findings.Results, r => r.PatternName == "HttpRequestException");
    }

    [Fact]
    public void Analyze_WithDateTimeAssertion_Finds()
    {
        var registry = new HeuristicsRegistry();
        var findings = registry.Analyze(
            "Expected DateTime to be 2024-01-01 but was different",
            "at TestMethod() Assert.Equal(expected, DateTime.UtcNow)"
        );
        Assert.True(findings.Found);
        Assert.Contains(findings.Results, r => r.PatternName == "DateTimeAssertion");
    }

    [Fact]
    public void Analyze_IsNotCaseSensitive()
    {
        var registry = new HeuristicsRegistry();
        var findings1 = registry.Analyze("TIMEOUT", "");
        var findings2 = registry.Analyze("timeout", "");
        var findings3 = registry.Analyze("TimeOut", "");

        Assert.True(findings1.Found);
        Assert.True(findings2.Found);
        Assert.True(findings3.Found);
    }

    [Fact]
    public void Analyze_WithVeryLongStackTrace_Completes()
    {
        var registry = new HeuristicsRegistry();
        var longStack = string.Concat(Enumerable.Repeat("   at Method" + Environment.NewLine, 1000));
        var findings = registry.Analyze("Test failed", longStack);
        // Should complete without performance issues
        Assert.NotNull(findings);
    }

    [Fact]
    public void Analyze_WithUnicodeCharacters_Completes()
    {
        var registry = new HeuristicsRegistry();
        var findings = registry.Analyze(
            "Test failed: 文字列が無効です (Invalid string) - タイムアウト",
            "Stack trace with unicode: Ошибка (Error)"
        );
        // Should complete without throwing
        Assert.NotNull(findings);
    }

    [Fact]
    public void HeuristicResult_StoresAllProperties()
    {
        var result = new HeuristicResult(
            "TestPattern",
            "This is a test description",
            0.85,
            "Check your code"
        );

        Assert.Equal("TestPattern", result.PatternName);
        Assert.Equal("This is a test description", result.Description);
        Assert.Equal(0.85, result.Confidence);
        Assert.Equal("Check your code", result.Suggestion);
    }

    [Fact]
    public void HeuristicResult_AllowsNullSuggestion()
    {
        var result = new HeuristicResult("Pattern", "Description", 0.8);
        Assert.Null(result.Suggestion);
    }

    [Fact]
    public void HeuristicFindings_MaxConfidence_WithNoResults()
    {
        var findings = new HeuristicFindings();
        Assert.Equal(0.0, findings.MaxConfidence);
    }

    [Fact]
    public void HeuristicFindings_MaxConfidence_WithMultipleResults()
    {
        var findings = new HeuristicFindings();
        findings.Add(new HeuristicResult("P1", "D1", 0.5));
        findings.Add(new HeuristicResult("P2", "D2", 0.9));
        findings.Add(new HeuristicResult("P3", "D3", 0.7));

        Assert.Equal(0.9, findings.MaxConfidence);
    }
}
