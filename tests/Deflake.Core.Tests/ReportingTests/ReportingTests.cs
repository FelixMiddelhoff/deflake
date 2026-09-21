using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Deflake.Core.Experiments;
using Deflake.Core.Reporting;
using Deflake.Core.Verdicts;
using Xunit;
using static Deflake.Core.Tests.VerdictTestSupport;

namespace Deflake.Core.Tests;

/// <summary>
/// Tests for all three reporter implementations: TextReporter, MarkdownReporter, and JsonReporter.
/// Ensures deterministic output and covers common verdict scenarios.
/// </summary>
public sealed class ReportingTests
{
    private static readonly TestIdentity Test = ExperimentTestSupport.Identity();
    private static readonly TestIdentity OtherTest = new("Shop.OrderTests.Total_calculation", "Shop.Tests.dll");

    [Fact]
    public async Task TextReporter_formats_basic_verdict()
    {
        var verdict = new Verdict(VerdictType.Inconclusive, Test);

        var reporter = new TextReporter();
        var output = await reporter.FormatAsync(verdict);

        Assert.Contains("Test:", output);
        Assert.Contains("Verdict: Inconclusive", output);
        Assert.Contains("Assembly:", output);
    }

    [Fact]
    public async Task TextReporter_formats_verdict_with_evidence()
    {
        var verdict = new Verdict(VerdictType.ParallelismDependent, Test)
        {
            ConfidenceScore = 0.85,
            Evidence = new[]
            {
                new VerdictEvidence("Parallelism", "Serial", 20, 1, 0.01, 0.15),
                new VerdictEvidence("Parallelism", "Parallel", 20, 8, 0.20, 0.50),
            },
        };

        var reporter = new TextReporter();
        var output = await reporter.FormatAsync(verdict);

        Assert.Contains("Evidence:", output);
        Assert.Contains("Parallelism=Serial", output);
        Assert.Contains("Parallelism=Parallel", output);
        Assert.Contains("Confidence: 85%", output);
    }

    [Fact]
    public async Task TextReporter_formats_ruled_out_conditions()
    {
        var verdict = new Verdict(VerdictType.Inconclusive, Test)
        {
            RuledOut = new[] { "Culture", "TimeZone" },
        };

        var reporter = new TextReporter();
        var output = await reporter.FormatAsync(verdict);

        Assert.Contains("Ruled Out:", output);
        Assert.Contains("- Culture", output);
        Assert.Contains("- TimeZone", output);
    }

    [Fact]
    public async Task TextReporter_formats_not_tested_conditions()
    {
        var verdict = new Verdict(VerdictType.Inconclusive, Test)
        {
            NotTested = new[] { "Load", "Scope" },
        };

        var reporter = new TextReporter();
        var output = await reporter.FormatAsync(verdict);

        Assert.Contains("Not Tested:", output);
        Assert.Contains("- Load", output);
        Assert.Contains("- Scope", output);
    }

    [Fact]
    public async Task TextReporter_formats_suspect_tests_and_members()
    {
        var verdict = new Verdict(VerdictType.OrderDependent, Test)
        {
            SuspectTests = new[] { OtherTest.FullyQualifiedName },
            SuspectMembers = new[] { "Shop.OrderTests.Setup" },
        };

        var reporter = new TextReporter();
        var output = await reporter.FormatAsync(verdict);

        Assert.Contains("Suspect Tests:", output);
        Assert.Contains(OtherTest.FullyQualifiedName, output);
        Assert.Contains("Suspect Members:", output);
        Assert.Contains("Shop.OrderTests.Setup", output);
    }

    [Fact]
    public async Task TextReporter_formats_repro_command()
    {
        var request = ExperimentTestSupport.Baseline();
        var command = "dotnet test Shop.Tests.dll --no-build";
        var verdict = new Verdict(VerdictType.TimingSensitive, Test)
            .WithRepro(command, request, "Run under load");

        var reporter = new TextReporter();
        var output = await reporter.FormatAsync(verdict);

        Assert.Contains("Repro Command:", output);
        Assert.Contains(command, output);
        Assert.Contains("Repro Note:", output);
        Assert.Contains("Run under load", output);
    }

    [Fact]
    public async Task TextReporter_shows_N_A_when_no_repro_command()
    {
        var verdict = new Verdict(VerdictType.NotReproduced, Test);

        var reporter = new TextReporter();
        var output = await reporter.FormatAsync(verdict);

        Assert.Contains("Repro Command:", output);
        Assert.Contains("N/A", output);
    }

    [Fact]
    public async Task TextReporter_formats_notes()
    {
        var verdict = new Verdict(VerdictType.Inconclusive, Test)
        {
            Notes = new[] { "Low failure rate", "Needs more data" },
        };

        var reporter = new TextReporter();
        var output = await reporter.FormatAsync(verdict);

        Assert.Contains("Notes:", output);
        Assert.Contains("- Low failure rate", output);
        Assert.Contains("- Needs more data", output);
    }

    [Fact]
    public async Task TextReporter_produces_deterministic_output()
    {
        var verdict = BuildComplexVerdict();

        var reporter = new TextReporter();
        var output1 = await reporter.FormatAsync(verdict);
        var output2 = await reporter.FormatAsync(verdict);

        Assert.Equal(output1, output2);
    }

    [Fact]
    public async Task MarkdownReporter_formats_basic_verdict()
    {
        var verdict = new Verdict(VerdictType.Inconclusive, Test);

        var reporter = new MarkdownReporter();
        var output = await reporter.FormatAsync(verdict);

        Assert.Contains("## Shop.OrderTests.Total_is_summed", output);
        Assert.Contains("| Verdict | `Inconclusive` |", output);
    }

    [Fact]
    public async Task MarkdownReporter_formats_evidence_as_table()
    {
        var verdict = new Verdict(VerdictType.ParallelismDependent, Test)
        {
            Evidence = new[]
            {
                new VerdictEvidence("Parallelism", "Serial", 20, 1, 0.01, 0.15),
                new VerdictEvidence("Parallelism", "Parallel", 20, 8, 0.20, 0.50),
            },
        };

        var reporter = new MarkdownReporter();
        var output = await reporter.FormatAsync(verdict);

        Assert.Contains("### Evidence", output);
        Assert.Contains("| Condition | Passed | Failed | Rate |", output);
        Assert.Contains("`Parallelism=Serial`", output);
        Assert.Contains("`Parallelism=Parallel`", output);
    }

    [Fact]
    public async Task MarkdownReporter_formats_repro_as_code_block()
    {
        var request = ExperimentTestSupport.Baseline();
        var command = "dotnet test Shop.Tests.dll --no-build";
        var verdict = new Verdict(VerdictType.TimingSensitive, Test)
            .WithRepro(command, request);

        var reporter = new MarkdownReporter();
        var output = await reporter.FormatAsync(verdict);

        Assert.Contains("### Repro Command", output);
        Assert.Contains("```bash", output);
        Assert.Contains(command, output);
        Assert.Contains("```", output);
    }

    [Fact]
    public async Task MarkdownReporter_produces_deterministic_output()
    {
        var verdict = BuildComplexVerdict();

        var reporter = new MarkdownReporter();
        var output1 = await reporter.FormatAsync(verdict);
        var output2 = await reporter.FormatAsync(verdict);

        Assert.Equal(output1, output2);
    }

    [Fact]
    public async Task JsonReporter_produces_valid_json()
    {
        var verdict = new Verdict(VerdictType.Inconclusive, Test);

        var reporter = new JsonReporter();
        var output = await reporter.FormatAsync(verdict);

        Assert.Contains("\"schemaVersion\"", output);
        Assert.Contains("\"verdict\"", output);
        System.Text.Json.JsonDocument.Parse(output);
    }

    [Fact]
    public async Task JsonReporter_includes_all_verdict_data()
    {
        var request = ExperimentTestSupport.Baseline();
        var verdict = new Verdict(VerdictType.CultureDependent, Test)
        {
            ConfidenceScore = 0.92,
            Evidence = new[]
            {
                new VerdictEvidence("Culture", "en-US", 20, 2, 0.05, 0.25),
                new VerdictEvidence("Culture", "de-DE", 20, 15, 0.60, 0.90),
            },
            RuledOut = new[] { "Load" },
            NotTested = new[] { "Scope" },
            SuspectTests = new[] { OtherTest.FullyQualifiedName },
            SuspectMembers = new[] { "System.Globalization.Culture" },
            Notes = new[] { "Parsing difference in decimal format" },
        }.WithRepro("CULTURE=de-DE dotnet test", request, "Must run with German culture");

        var reporter = new JsonReporter();
        var output = await reporter.FormatAsync(verdict);

        var doc = System.Text.Json.JsonDocument.Parse(output);
        var root = doc.RootElement;

        Assert.Equal("1.0", root.GetProperty("schemaVersion").GetString());
        Assert.Equal("CultureDependent", root.GetProperty("verdict").GetProperty("type").GetString());
        Assert.Equal(0.92, root.GetProperty("verdict").GetProperty("confidenceScore").GetDouble(), 0.001);
        Assert.Equal("92%", root.GetProperty("verdict").GetProperty("confidencePercent").GetString());
        Assert.Equal(2, root.GetProperty("evidence").GetArrayLength());
        Assert.Equal(1, root.GetProperty("ruledOut").GetArrayLength());
        Assert.Equal(1, root.GetProperty("notTested").GetArrayLength());
    }

    [Fact]
    public async Task JsonReporter_produces_deterministic_output()
    {
        var verdict = BuildComplexVerdict();

        var reporter = new JsonReporter();
        var output1 = await reporter.FormatAsync(verdict);
        var output2 = await reporter.FormatAsync(verdict);

        Assert.Equal(output1, output2);
    }

    [Fact]
    public async Task JsonReporter_sorts_string_lists()
    {
        var verdict = new Verdict(VerdictType.Inconclusive, Test)
        {
            RuledOut = new[] { "Z", "A", "M" },
            NotTested = new[] { "Charlie", "Alpha", "Bravo" },
            SuspectTests = new[] { "ZTest", "ATest", "MTest" },
            SuspectMembers = new[] { "Zulu", "Alpha", "Mike" },
        };

        var reporter = new JsonReporter();
        var output = await reporter.FormatAsync(verdict);

        var doc = System.Text.Json.JsonDocument.Parse(output);
        var root = doc.RootElement;

        var ruledOut = new List<string>();
        foreach (var item in root.GetProperty("ruledOut").EnumerateArray())
        {
            ruledOut.Add(item.GetString()!);
        }
        Assert.Equal(new[] { "A", "M", "Z" }, ruledOut);

        var notTested = new List<string>();
        foreach (var item in root.GetProperty("notTested").EnumerateArray())
        {
            notTested.Add(item.GetString()!);
        }
        Assert.Equal(new[] { "Alpha", "Bravo", "Charlie" }, notTested);
    }

    [Fact]
    public async Task Reporters_handle_empty_evidence()
    {
        var verdict = new Verdict(VerdictType.NotReproduced, Test);

        var textReporter = new TextReporter();
        var mdReporter = new MarkdownReporter();
        var jsonReporter = new JsonReporter();

        var text = await textReporter.FormatAsync(verdict);
        var md = await mdReporter.FormatAsync(verdict);
        var json = await jsonReporter.FormatAsync(verdict);

        Assert.NotEmpty(text);
        Assert.NotEmpty(md);
        Assert.NotEmpty(json);

        // JSON should have empty evidence array
        var doc = System.Text.Json.JsonDocument.Parse(json);
        Assert.Equal(0, doc.RootElement.GetProperty("evidence").GetArrayLength());
    }

    [Fact]
    public async Task Reporters_handle_long_condition_names()
    {
        var longCondition = "VeryLongConditionNameThatExceedsNormalLength";
        var verdict = new Verdict(VerdictType.Inconclusive, Test)
        {
            Evidence = new[]
            {
                new VerdictEvidence("Factor", longCondition, 20, 5, 0.1, 0.4),
            },
        };

        var textReporter = new TextReporter();
        var mdReporter = new MarkdownReporter();

        var text = await textReporter.FormatAsync(verdict);
        var md = await mdReporter.FormatAsync(verdict);

        Assert.Contains(longCondition, text);
        Assert.Contains(longCondition, md);
    }

    [Fact]
    public async Task Reporters_handle_special_characters()
    {
        var specialNote = "Test failed with message: \"Connection refused\" at 192.168.1.1:8080";
        var verdict = new Verdict(VerdictType.Inconclusive, Test)
        {
            Notes = new[] { specialNote },
        };

        var textReporter = new TextReporter();
        var mdReporter = new MarkdownReporter();
        var jsonReporter = new JsonReporter();

        var text = await textReporter.FormatAsync(verdict);
        var md = await mdReporter.FormatAsync(verdict);
        var json = await jsonReporter.FormatAsync(verdict);

        Assert.Contains(specialNote, text);
        Assert.Contains(specialNote, md);

        // JSON should parse without issues
        System.Text.Json.JsonDocument.Parse(json);
    }

    [Fact]
    public async Task TextReporter_throws_on_null_verdict()
    {
        var reporter = new TextReporter();

        await Assert.ThrowsAsync<ArgumentNullException>(() => reporter.FormatAsync(null!));
    }

    [Fact]
    public async Task MarkdownReporter_throws_on_null_verdict()
    {
        var reporter = new MarkdownReporter();

        await Assert.ThrowsAsync<ArgumentNullException>(() => reporter.FormatAsync(null!));
    }

    [Fact]
    public async Task JsonReporter_throws_on_null_verdict()
    {
        var reporter = new JsonReporter();

        await Assert.ThrowsAsync<ArgumentNullException>(() => reporter.FormatAsync(null!));
    }

    [Fact]
    public async Task Reporters_handle_confirmed_repro()
    {
        var request = ExperimentTestSupport.Baseline();
        var verdict = new Verdict(VerdictType.TimingSensitive, Test)
            .WithRepro("dotnet test", request)
            .Confirmed();

        var textReporter = new TextReporter();
        var mdReporter = new MarkdownReporter();
        var jsonReporter = new JsonReporter();

        var text = await textReporter.FormatAsync(verdict);
        var md = await mdReporter.FormatAsync(verdict);
        var json = await jsonReporter.FormatAsync(verdict);

        Assert.Contains("Repro Verified: True", text);
        Assert.Contains("True", md);

        var doc = System.Text.Json.JsonDocument.Parse(json);
        Assert.True(doc.RootElement.GetProperty("repro").GetProperty("verified").GetBoolean());
    }

    [Fact]
    public async Task Reporters_handle_evidence_with_notes()
    {
        var verdict = new Verdict(VerdictType.Inconclusive, Test)
        {
            Evidence = new[]
            {
                new VerdictEvidence("Load", "Idle", 10, 0, 0.0, 0.3, note: "Stopped early after 10 runs"),
            },
        };

        var textReporter = new TextReporter();
        var mdReporter = new MarkdownReporter();

        var text = await textReporter.FormatAsync(verdict);
        var md = await mdReporter.FormatAsync(verdict);

        Assert.Contains("Stopped early", text);
        Assert.Contains("Stopped early", md);
    }

    private static Verdict BuildComplexVerdict()
    {
        var request = ExperimentTestSupport.Baseline();
        return new Verdict(VerdictType.OrderDependent, Test)
        {
            ConfidenceScore = 0.78,
            Evidence = new[]
            {
                new VerdictEvidence("Scope", "Alone", 20, 0, 0.0, 0.17),
                new VerdictEvidence("Scope", "Class", 20, 3, 0.05, 0.35),
                new VerdictEvidence("Scope", "Assembly", 20, 12, 0.40, 0.75),
            },
            RuledOut = new[] { "Culture", "TimeZone", "Load" },
            NotTested = new[] { "Parallelism" },
            SuspectTests = new[] { "Shop.OrderTests.CreatesOrder", "Shop.OrderTests.CalculatesTotal" },
            SuspectMembers = new[] { "Shop.OrderService.Create", "Shop.Database.Connection" },
            Notes = new[] { "Failed when running with other tests", "Order of execution matters" },
        }.WithRepro("dotnet test Shop.Tests.dll --no-build --filter FullyQualifiedName=Shop.OrderTests.Concurrent", request, "Must run test suite in order");
    }
}
