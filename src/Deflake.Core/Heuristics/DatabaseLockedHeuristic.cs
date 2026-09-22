using System;

namespace Deflake.Core.Heuristics;

/// <summary>Detects database lock and transaction patterns.</summary>
internal sealed class DatabaseLockedHeuristic : IHeuristic
{
    public string PatternName => "DatabaseLocked";

    public HeuristicResult? Match(string? errorMessage, string? stackTrace)
    {
        if (string.IsNullOrEmpty(errorMessage) && string.IsNullOrEmpty(stackTrace))
            return null;

        var combined = (errorMessage ?? "") + " " + (stackTrace ?? "");
        var lower = combined.ToLowerInvariant();

        if (lower.Contains("database is locked")
            || lower.Contains("the database file is locked")
            || lower.Contains("locked out")
            || lower.Contains("timeout waiting for lock")
            || lower.Contains("sqliteerrorcode.cantopen")
            || lower.Contains("sqliteerrorcode.locked"))
        {
            return new HeuristicResult(
                "DatabaseLocked",
                "Database is locked, suggesting concurrent access or transaction timeout.",
                0.92,
                "Check for tests that share a database without proper transaction handling or cleanup. May be parallelism-sensitive."
            );
        }

        return null;
    }
}
