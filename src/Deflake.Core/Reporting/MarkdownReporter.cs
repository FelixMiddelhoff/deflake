using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Deflake.Core.Verdicts;

namespace Deflake.Core.Reporting;

/// <summary>
/// Formats a verdict as Markdown, suitable for inclusion in GitHub issues or documentation.
/// </summary>
public sealed class MarkdownReporter : IReporter
{
    public Task<string> FormatAsync(Verdict verdict)
    {
        if (verdict is null)
        {
            throw new ArgumentNullException(nameof(verdict));
        }

        var output = new StringBuilder();

        // Header
        output.AppendLine($"## {verdict.Test.FullyQualifiedName}");
        output.AppendLine();

        // Basic info
        output.AppendLine("| Property | Value |");
        output.AppendLine("|----------|-------|");
        output.AppendLine($"| Verdict | `{verdict.Type}` |");
        output.AppendLine($"| Confidence | {ReportingHelpers.FormatConfidence(verdict.ConfidenceScore)} |");
        output.AppendLine($"| Assembly | `{verdict.Test.AssemblyName}` |");
        output.AppendLine($"| Repro Verified | {verdict.ReproVerified} |");
        output.AppendLine();

        // Evidence table
        if (verdict.Evidence.Count > 0)
        {
            output.AppendLine("### Evidence");
            output.AppendLine();
            output.AppendLine("| Condition | Passed | Failed | Rate |");
            output.AppendLine("|-----------|--------|--------|------|");

            foreach (var row in verdict.Evidence)
            {
                var conditionLabel = $"`{row.Factor}={row.Condition}`";
                var passed = row.Runs - row.Failures;
                var rateStr = ReportingHelpers.FormatRate(row);
                var rateDisplay = string.IsNullOrEmpty(row.Note) ? rateStr : $"{rateStr} _{row.Note}_";

                output.AppendLine($"| {conditionLabel} | {passed} | {row.Failures} | {rateDisplay} |");
            }

            output.AppendLine();
        }

        // Ruled out
        if (verdict.RuledOut.Count > 0)
        {
            output.AppendLine("### Ruled Out");
            output.AppendLine();
            foreach (var condition in verdict.RuledOut)
            {
                output.AppendLine($"- `{condition}`");
            }
            output.AppendLine();
        }

        // Not tested
        if (verdict.NotTested.Count > 0)
        {
            output.AppendLine("### Not Tested");
            output.AppendLine();
            foreach (var condition in verdict.NotTested)
            {
                output.AppendLine($"- `{condition}`");
            }
            output.AppendLine();
        }

        // Suspect tests
        if (verdict.SuspectTests.Count > 0)
        {
            output.AppendLine("### Suspect Tests");
            output.AppendLine();
            foreach (var test in verdict.SuspectTests)
            {
                output.AppendLine($"- `{test}`");
            }
            output.AppendLine();
        }

        // Suspect members
        if (verdict.SuspectMembers.Count > 0)
        {
            output.AppendLine("### Suspect Members");
            output.AppendLine();
            foreach (var member in verdict.SuspectMembers)
            {
                output.AppendLine($"- `{member}`");
            }
            output.AppendLine();
        }

        // Repro command
        if (!string.IsNullOrEmpty(verdict.ReproCommand))
        {
            output.AppendLine("### Repro Command");
            output.AppendLine();
            output.AppendLine("```bash");
            output.AppendLine(verdict.ReproCommand);
            output.AppendLine("```");
            output.AppendLine();
        }

        // Repro note
        if (!string.IsNullOrEmpty(verdict.ReproNote))
        {
            output.AppendLine("### Repro Note");
            output.AppendLine();
            output.AppendLine(verdict.ReproNote);
            output.AppendLine();
        }

        // Notes
        if (verdict.Notes.Count > 0)
        {
            output.AppendLine("### Notes");
            output.AppendLine();
            foreach (var note in verdict.Notes)
            {
                output.AppendLine($"- {note}");
            }
            output.AppendLine();
        }

        return Task.FromResult(output.ToString().TrimEnd() + Environment.NewLine);
    }
}
