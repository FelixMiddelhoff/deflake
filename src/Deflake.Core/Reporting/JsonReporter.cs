using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Deflake.Core.Verdicts;

namespace Deflake.Core.Reporting;

/// <summary>
/// Formats a verdict as JSON with a versioned schema for machine consumption.
/// </summary>
public sealed class JsonReporter : IReporter
{
    private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public Task<string> FormatAsync(Verdict verdict)
    {
        if (verdict is null)
        {
            throw new ArgumentNullException(nameof(verdict));
        }

        var json = BuildJson(verdict);
        return Task.FromResult(json);
    }

    private static string BuildJson(Verdict verdict)
    {
        var evidenceList = verdict.Evidence
            .Select(e => new
            {
                factor = e.Factor,
                condition = e.Condition,
                runs = e.Runs,
                failures = e.Failures,
                failureRatePercent = e.FailureRatePercent,
                intervalLowPercent = e.IntervalLow * 100,
                intervalHighPercent = e.IntervalHigh * 100,
                heuristicSupport = e.HeuristicSupport,
                note = e.Note,
                order = 1,
            })
            .ToList();

        var root = new
        {
            schemaVersion = "1.0",
            order = 0,
            verdict = new
            {
                test = verdict.Test.FullyQualifiedName,
                assembly = verdict.Test.AssemblyName,
                className = verdict.Test.ClassName,
                type = verdict.Type.ToString(),
                confidenceScore = verdict.ConfidenceScore,
                confidencePercent = ReportingHelpers.FormatConfidence(verdict.ConfidenceScore),
                order = 1,
            },
            evidence = evidenceList,
            ruledOut = verdict.RuledOut.OrderBy(x => x).ToList(),
            notTested = verdict.NotTested.OrderBy(x => x).ToList(),
            suspectTests = verdict.SuspectTests.OrderBy(x => x).ToList(),
            suspectMembers = verdict.SuspectMembers.OrderBy(x => x).ToList(),
            repro = new
            {
                command = verdict.ReproCommand,
                note = verdict.ReproNote,
                verified = verdict.ReproVerified,
                order = 2,
            },
            notes = verdict.Notes.ToList(),
        };

        var json = JsonSerializer.Serialize(root, Options);
        return json + Environment.NewLine;
    }
}
