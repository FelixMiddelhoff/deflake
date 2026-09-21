using System;

namespace Deflake.Core.Heuristics;

/// <summary>Detects file-in-use and lock patterns.</summary>
internal sealed class FileInUseHeuristic : IHeuristic
{
    public string PatternName => "FileInUse";

    public HeuristicResult? Match(string? errorMessage, string? stackTrace)
    {
        if (string.IsNullOrEmpty(errorMessage) && string.IsNullOrEmpty(stackTrace))
            return null;

        var combined = (errorMessage ?? "") + " " + (stackTrace ?? "");
        var lower = combined.ToLowerInvariant();

        if ((lower.Contains("file") && lower.Contains("is in use")) ||
            lower.Contains("file in use") ||
            lower.Contains("access to the file is denied") ||
            lower.Contains("the file is being used by another process") ||
            (lower.Contains("ioexception") && (lower.Contains("access denied") || lower.Contains("in use"))) ||
            lower.Contains("sharing violation"))
        {
            return new HeuristicResult(
                "FileInUse",
                "File locked or in use by another process, suggesting resource conflict.",
                0.9,
                "Check for tests that access shared files without proper locking or cleanup. May be order-dependent or parallelism-sensitive."
            );
        }

        return null;
    }
}
