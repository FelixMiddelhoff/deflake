using System;

namespace Culprit.Core.Trx;

/// <summary>The result of one test (one theory case counts as one test) in one test run.</summary>
public sealed class TestResult
{
    public TestResult(
        string fullName,
        string displayName,
        TestOutcome outcome,
        string rawOutcome,
        DateTimeOffset startTime,
        DateTimeOffset endTime,
        TimeSpan duration,
        string? errorMessage,
        string? stackTrace,
        string? standardOutput)
    {
        FullName = fullName;
        DisplayName = displayName;
        Outcome = outcome;
        RawOutcome = rawOutcome;
        StartTime = startTime;
        EndTime = endTime;
        Duration = duration;
        ErrorMessage = errorMessage;
        StackTrace = stackTrace;
        StandardOutput = standardOutput;
    }

    /// <summary>
    /// Class name and method name, for example <c>Shop.OrderTests.Total_is_summed</c>. Every case
    /// of a theory shares one full name; this is the name a <c>dotnet test --filter</c> selects by.
    /// </summary>
    public string FullName { get; }

    /// <summary>The name the test framework shows, including theory arguments.</summary>
    public string DisplayName { get; }

    public TestOutcome Outcome { get; }

    /// <summary>The outcome exactly as written in the file, for example <c>NotExecuted</c>.</summary>
    public string RawOutcome { get; }

    /// <summary>When the test started, or <see cref="DateTimeOffset.MinValue"/> when the file has none.</summary>
    public DateTimeOffset StartTime { get; }

    /// <summary>When the test ended, or <see cref="DateTimeOffset.MinValue"/> when the file has none.</summary>
    public DateTimeOffset EndTime { get; }

    public TimeSpan Duration { get; }

    public string? ErrorMessage { get; }

    public string? StackTrace { get; }

    public string? StandardOutput { get; }

    /// <summary>True for failed tests. Skipped tests and aborted runs are not failures.</summary>
    public bool IsFailure => Outcome == TestOutcome.Failed;
}
