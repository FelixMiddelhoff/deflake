using System;
using System.Collections.Generic;
using System.Text;
using Deflake.Core.Verdicts;

namespace Deflake.Core.Reporting;

/// <summary>
/// Shared formatting utilities for reporting. Ensures deterministic table layouts and formatting.
/// </summary>
internal static class ReportingHelpers
{
    /// <summary>
    /// Formats a failure rate with confidence interval as a human-readable string.
    /// </summary>
    public static string FormatRate(VerdictEvidence evidence)
    {
        var rate = evidence.FailureRatePercent;
        var intervalLow = evidence.IntervalLow * 100;
        var intervalHigh = evidence.IntervalHigh * 100;
        return $"{rate:F1}% (95% {intervalLow:F1}%–{intervalHigh:F1}%)";
    }

    /// <summary>
    /// Formats the confidence as a percentage.
    /// </summary>
    public static string FormatConfidence(double score)
    {
        return $"{Math.Round(score * 100)}%";
    }

    /// <summary>
    /// Builds a text table from evidence rows. Returns the table as lines.
    /// </summary>
    public static string[] BuildEvidenceTable(IReadOnlyList<VerdictEvidence> evidence)
    {
        if (evidence.Count == 0)
        {
            return Array.Empty<string>();
        }

        // Determine column widths
        var factorWidth = "Condition".Length;
        var passedWidth = "Passed".Length;
        var failedWidth = "Failed".Length;
        var rateWidth = "Rate".Length;

        foreach (var row in evidence)
        {
            var conditionLabel = $"{row.Factor}={row.Condition}";
            factorWidth = Math.Max(factorWidth, conditionLabel.Length);

            var passed = row.Runs - row.Failures;
            passedWidth = Math.Max(passedWidth, passed.ToString().Length);
            failedWidth = Math.Max(failedWidth, row.Failures.ToString().Length);

            var rateStr = FormatRate(row);
            rateWidth = Math.Max(rateWidth, rateStr.Length);
        }

        var lines = new List<string>();

        // Header
        var header = new StringBuilder();
        header.Append("Condition".PadRight(factorWidth));
        header.Append(" | ");
        header.Append("Passed".PadLeft(passedWidth));
        header.Append(" | ");
        header.Append("Failed".PadLeft(failedWidth));
        header.Append(" | ");
        header.Append("Rate".PadLeft(rateWidth));
        lines.Add(header.ToString());

        // Separator
        var separator = new StringBuilder();
        separator.Append(new string('-', factorWidth));
        separator.Append("-+-");
        separator.Append(new string('-', passedWidth));
        separator.Append("-+-");
        separator.Append(new string('-', failedWidth));
        separator.Append("-+-");
        separator.Append(new string('-', rateWidth));
        lines.Add(separator.ToString());

        // Rows
        foreach (var row in evidence)
        {
            var conditionLabel = $"{row.Factor}={row.Condition}";
            var passed = row.Runs - row.Failures;
            var rateStr = FormatRate(row);

            var line = new StringBuilder();
            line.Append(conditionLabel.PadRight(factorWidth));
            line.Append(" | ");
            line.Append(passed.ToString().PadLeft(passedWidth));
            line.Append(" | ");
            line.Append(row.Failures.ToString().PadLeft(failedWidth));
            line.Append(" | ");
            line.Append(rateStr.PadLeft(rateWidth));

            if (!string.IsNullOrEmpty(row.Note))
            {
                line.Append(" (").Append(row.Note).Append(")");
            }

            lines.Add(line.ToString());
        }

        return lines.ToArray();
    }

    /// <summary>
    /// Returns "N/A" if the string is null or empty, otherwise returns the string.
    /// </summary>
    public static string FormatOptional(string? value)
    {
        return string.IsNullOrEmpty(value) ? "N/A" : value;
    }
}
