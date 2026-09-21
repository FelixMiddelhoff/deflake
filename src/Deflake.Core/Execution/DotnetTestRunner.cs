using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Deflake.Core.Trx;

namespace Deflake.Core.Execution;

/// <summary>
/// Runs <c>dotnet test</c> as a real process and parses its TRX output. Every argument is passed
/// through <see cref="ProcessStartInfo.ArgumentList"/>, never built into a single command-line
/// string, so long names and special characters in a filter never need their own quoting rules.
/// The results directory is a fresh temporary folder per run and is always removed afterwards,
/// even when the run times out or throws.
/// </summary>
public sealed class DotnetTestRunner : ITestRunner
{
    private const string TrxFileName = "result.trx";

    private readonly string _dotnetExecutable;

    /// <param name="dotnetExecutable">
    /// The <c>dotnet</c> executable to launch. Defaults to <c>"dotnet"</c>, resolved through PATH;
    /// tests that need to observe or fake the process point this at something else.
    /// </param>
    public DotnetTestRunner(string dotnetExecutable = "dotnet")
    {
        if (string.IsNullOrWhiteSpace(dotnetExecutable))
        {
            throw new ArgumentException("A dotnet executable is required.", nameof(dotnetExecutable));
        }

        _dotnetExecutable = dotnetExecutable;
    }

    public async Task<TestRunResult> RunAsync(TestRunRequest request, CancellationToken cancellationToken)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        cancellationToken.ThrowIfCancellationRequested();

        var resultsDirectory = Directory.CreateTempSubdirectory("deflake-run-");
        try
        {
            return await RunInAsync(request, resultsDirectory.FullName, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            TryDeleteDirectory(resultsDirectory.FullName);
        }
    }

    private async Task<TestRunResult> RunInAsync(TestRunRequest request, string resultsDirectory, CancellationToken cancellationToken)
    {
        var trxPath = Path.Combine(resultsDirectory, TrxFileName);
        var startInfo = BuildStartInfo(request, resultsDirectory);

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        var standardOutput = new StringBuilder();
        var standardError = new StringBuilder();
        process.OutputDataReceived += (_, e) => AppendLine(standardOutput, e.Data);
        process.ErrorDataReceived += (_, e) => AppendLine(standardError, e.Data);

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeoutSource = new CancellationTokenSource(request.Timeout);
        using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);

        try
        {
            await process.WaitForExitAsync(linkedSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            await KillTreeAsync(process).ConfigureAwait(false);

            if (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(
                    "The test run was cancelled and its process tree was killed.", cancellationToken);
            }

            throw new TestRunTimeoutException(
                request.Timeout,
                $"'dotnet test' for '{request.TargetPath}' did not finish within {request.Timeout} and its process tree was killed.");
        }

        if (!File.Exists(trxPath))
        {
            throw new TestRunExecutionException(
                process.ExitCode,
                standardOutput.ToString(),
                standardError.ToString(),
                $"'dotnet test' for '{request.TargetPath}' exited with code {process.ExitCode} without writing a TRX result file. " +
                "This usually means the run never reached the test host (bad target path, missing build, or a bad --settings file).");
        }

        return TrxParser.ParseFile(trxPath);
    }

    private ProcessStartInfo BuildStartInfo(TestRunRequest request, string resultsDirectory)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = _dotnetExecutable,
            WorkingDirectory = ResolveWorkingDirectory(request),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        startInfo.ArgumentList.Add("test");
        startInfo.ArgumentList.Add(request.TargetPath);
        startInfo.ArgumentList.Add("--no-build");

        if (!string.IsNullOrEmpty(request.Filter))
        {
            startInfo.ArgumentList.Add("--filter");
            startInfo.ArgumentList.Add(request.Filter);
        }

        startInfo.ArgumentList.Add("--logger");
        startInfo.ArgumentList.Add($"trx;LogFileName={TrxFileName}");
        startInfo.ArgumentList.Add("--results-directory");
        startInfo.ArgumentList.Add(resultsDirectory);

        if (!string.IsNullOrEmpty(request.RunSettingsPath))
        {
            startInfo.ArgumentList.Add("--settings");
            startInfo.ArgumentList.Add(request.RunSettingsPath);
        }

        if (request.EnvironmentVariables is not null)
        {
            foreach (var variable in request.EnvironmentVariables)
            {
                startInfo.Environment[variable.Key] = variable.Value;
            }
        }

        return startInfo;
    }

    private static string ResolveWorkingDirectory(TestRunRequest request)
    {
        if (!string.IsNullOrEmpty(request.WorkingDirectory))
        {
            return request.WorkingDirectory;
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(request.TargetPath));
        return string.IsNullOrEmpty(directory) ? Environment.CurrentDirectory : directory;
    }

    /// <summary>
    /// Kills the whole process tree, not just the <c>dotnet</c> parent: <c>dotnet test</c> hands
    /// off to a <c>testhost</c> child (and that host can itself spawn more), and an orphaned
    /// testhost is exactly the kind of leaked process the quality gate forbids.
    /// </summary>
    private static async Task KillTreeAsync(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // The process exited between the HasExited check and Kill: nothing left to do.
        }

        try
        {
            using var graceSource = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await process.WaitForExitAsync(graceSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Best effort: the tree was asked to die; nothing more to do without leaking further.
        }
    }

    private static void AppendLine(StringBuilder builder, string? line)
    {
        if (line is null)
        {
            return;
        }

        lock (builder)
        {
            builder.AppendLine(line);
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best effort cleanup: a file still briefly locked by an exiting child is not worth failing the run for.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
