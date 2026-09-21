using System;

namespace Deflake.Core.Execution;

/// <summary>
/// <c>dotnet test</c> exited on its own (no timeout) but left no TRX result file to parse, for
/// example because the target could not be found or a pre-run check failed. A nonzero exit code
/// from failing tests is normal and does not throw this; only a missing result file does.
/// </summary>
public sealed class TestRunExecutionException : Exception
{
    public TestRunExecutionException(int exitCode, string standardOutput, string standardError, string message)
        : base(message)
    {
        ExitCode = exitCode;
        StandardOutput = standardOutput;
        StandardError = standardError;
    }

    public int ExitCode { get; }

    public string StandardOutput { get; }

    public string StandardError { get; }
}
