using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Deflake.Core.Trx;

namespace Deflake.Core.Execution;

/// <summary>
/// Runs one <c>dotnet test</c> invocation and returns the parsed result. Every experiment goes
/// through this contract so it can run against a real process in production and against
/// <c>FakeTestRunner</c> in tests, without knowing which one it has.
/// </summary>
public interface ITestRunner
{
    Task<TestRunResult> RunAsync(TestRunRequest request, CancellationToken cancellationToken);
}

/// <summary>Everything one <c>dotnet test</c> run needs: what to run, how, and for how long.</summary>
public sealed class TestRunRequest
{
    public TestRunRequest(string targetPath)
    {
        if (string.IsNullOrWhiteSpace(targetPath))
        {
            throw new ArgumentException("A target path is required.", nameof(targetPath));
        }

        TargetPath = targetPath;
    }

    /// <summary>
    /// The test project (<c>.csproj</c>) or already-built test assembly (<c>.dll</c>) that
    /// <c>dotnet test</c> runs. Must already be built: the runner always passes <c>--no-build</c>.
    /// </summary>
    public string TargetPath { get; }

    /// <summary>
    /// A VSTest <c>--filter</c> expression, normally built with <see cref="TestFilter"/>. Null or
    /// empty runs every test in <see cref="TargetPath"/>.
    /// </summary>
    public string? Filter { get; init; }

    /// <summary>Path to a <c>.runsettings</c> file passed with <c>--settings</c>. Null uses VSTest defaults.</summary>
    public string? RunSettingsPath { get; init; }

    /// <summary>
    /// Extra environment variables for the <c>dotnet test</c> process, on top of the ones the
    /// current process already has. Used for experiments (culture, time zone, parallelism knobs)
    /// that a test reads from its environment.
    /// </summary>
    public IReadOnlyDictionary<string, string>? EnvironmentVariables { get; init; }

    /// <summary>
    /// Wall-clock budget for the whole run. If <c>dotnet test</c> and everything it spawned has
    /// not exited by then, the entire process tree is killed and <see cref="TestRunTimeoutException"/>
    /// is thrown.
    /// </summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>Working directory for the process. Null uses the directory that holds <see cref="TargetPath"/>.</summary>
    public string? WorkingDirectory { get; init; }
}
