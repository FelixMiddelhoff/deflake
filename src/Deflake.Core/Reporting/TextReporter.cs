using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Deflake.Core.Verdicts;

namespace Deflake.Core.Reporting;

/// <summary>
/// Formats a verdict as plain text, human-readable for console output or email.
/// </summary>
public sealed class TextReporter : IReporter
{
    public Task<string> FormatAsync(Verdict verdict)
    {
        if (verdict is null)
        {
            throw new ArgumentNullException(nameof(verdict));
        }

        var output = new StringBuilder();

        // Header
        output.AppendLine($"Test: {verdict.Test.FullyQualifiedName}");
        output.AppendLine($"Assembly: {verdict.Test.AssemblyName}");
        output.AppendLine($"Verdict: {verdict.Type}");
        output.AppendLine($"Confidence: {ReportingHelpers.FormatConfidence(verdict.ConfidenceScore)}");
        output.AppendLine();

        // Evidence table
        if (verdict.Evidence.Count > 0)
        {
            output.AppendLine("Evidence:");
            var table = ReportingHelpers.BuildEvidenceTable(verdict.Evidence);
            foreach (var line in table)
            {
                output.AppendLine(line);
            }
            output.AppendLine();
        }

        // Ruled out
        if (verdict.RuledOut.Count > 0)
        {
            output.AppendLine("Ruled Out:");
            foreach (var condition in verdict.RuledOut)
            {
                output.AppendLine($"  - {condition}");
            }
            output.AppendLine();
        }

        // Not tested
        if (verdict.NotTested.Count > 0)
        {
            output.AppendLine("Not Tested:");
            foreach (var condition in verdict.NotTested)
            {
                output.AppendLine($"  - {condition}");
            }
            output.AppendLine();
        }

        // Suspect tests
        if (verdict.SuspectTests.Count > 0)
        {
            output.AppendLine("Suspect Tests:");
            foreach (var test in verdict.SuspectTests)
            {
                output.AppendLine($"  - {test}");
            }
            output.AppendLine();
        }

        // Suspect members
        if (verdict.SuspectMembers.Count > 0)
        {
            output.AppendLine("Suspect Members:");
            foreach (var member in verdict.SuspectMembers)
            {
                output.AppendLine($"  - {member}");
            }
            output.AppendLine();
        }

        // Repro command
        output.AppendLine("Repro Command:");
        output.AppendLine(ReportingHelpers.FormatOptional(verdict.ReproCommand));
        output.AppendLine();

        // Repro note
        if (!string.IsNullOrEmpty(verdict.ReproNote))
        {
            output.AppendLine("Repro Note:");
            output.AppendLine(verdict.ReproNote);
            output.AppendLine();
        }

        // Repro verified
        output.AppendLine($"Repro Verified: {verdict.ReproVerified}");
        output.AppendLine();

        // Notes
        if (verdict.Notes.Count > 0)
        {
            output.AppendLine("Notes:");
            foreach (var note in verdict.Notes)
            {
                output.AppendLine($"  - {note}");
            }
            output.AppendLine();
        }

        return Task.FromResult(output.ToString().TrimEnd() + Environment.NewLine);
    }
}
