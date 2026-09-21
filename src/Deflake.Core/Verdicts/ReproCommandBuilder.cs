using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Deflake.Core.Execution;

namespace Deflake.Core.Verdicts;

/// <summary>
/// Turns the run that reproduced a failure into the line a developer can paste into a shell. The run
/// is the truth; this only renders it, so a printed command can never describe something Deflake did
/// not actually do.
/// </summary>
public static class ReproCommandBuilder
{
    /// <summary>
    /// The command for one run. Environment variables an experiment added on top of
    /// <paramref name="baselineEnvironment"/> are written as a POSIX-style prefix
    /// (<c>TZ=Asia/Tokyo dotnet test …</c>); the ones the baseline already had are the developer's
    /// own environment and are left out, so the command stays about the one thing that matters.
    /// </summary>
    public static string Build(TestRunRequest request, IReadOnlyDictionary<string, string>? baselineEnvironment = null)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        var command = new StringBuilder();
        foreach (var variable in AddedVariables(request.EnvironmentVariables, baselineEnvironment))
        {
            command.Append(variable.Key).Append('=').Append(Quote(variable.Value)).Append(' ');
        }

        command.Append("dotnet test ").Append(Quote(request.TargetPath));

        // --no-build matches how every experiment ran: the project was built once up front, and a
        // rebuild here could change the very timing the failure depends on.
        command.Append(" --no-build");

        if (!string.IsNullOrWhiteSpace(request.Filter))
        {
            command.Append(" --filter ").Append(Quote(request.Filter!));
        }

        if (!string.IsNullOrWhiteSpace(request.RunSettingsPath))
        {
            command.Append(" --settings ").Append(Quote(request.RunSettingsPath!));
        }

        return command.ToString();
    }

    /// <summary>
    /// The variables this run has that the baseline did not, or gives a different value, ordered by
    /// name so the same run always renders the same command.
    /// </summary>
    private static IEnumerable<KeyValuePair<string, string>> AddedVariables(
        IReadOnlyDictionary<string, string>? environment,
        IReadOnlyDictionary<string, string>? baseline)
    {
        if (environment is null)
        {
            return Array.Empty<KeyValuePair<string, string>>();
        }

        return environment
            .Where(variable => !HasSameValue(baseline, variable))
            .OrderBy(variable => variable.Key, StringComparer.Ordinal);
    }

    private static bool HasSameValue(IReadOnlyDictionary<string, string>? baseline, KeyValuePair<string, string> variable)
    {
        return baseline is not null
            && baseline.TryGetValue(variable.Key, out var existing)
            && string.Equals(existing, variable.Value, StringComparison.Ordinal);
    }

    /// <summary>
    /// Quotes anything that would otherwise break apart at a space. Test names can hold quotes of
    /// their own, so those are escaped rather than assumed away.
    /// </summary>
    private static string Quote(string value)
    {
        var needsQuotes = value.Length == 0 || value.Any(char.IsWhiteSpace) || value.Contains('"');
        if (!needsQuotes)
        {
            return value;
        }

        return "\"" + value.Replace("\"", "\\\"") + "\"";
    }
}
