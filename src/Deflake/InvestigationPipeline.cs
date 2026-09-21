using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Deflake.Core.Execution;
using Deflake.Core.Experiments;
using Deflake.Core.Heuristics;
using Deflake.Core.Trx;
using Deflake.Core.Verdicts;

namespace Deflake;

internal sealed class InvestigationPipeline
{
    private readonly DotnetTestRunner _runner;
    private readonly IReproVerifier _verifier;
    private readonly InvestigationOptions _options;

    public InvestigationPipeline(DotnetTestRunner runner, IReproVerifier verifier, InvestigationOptions options)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _verifier = verifier ?? throw new ArgumentNullException(nameof(verifier));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task<List<Verdict>> InvestigateAsync(string targetPath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(targetPath))
        {
            throw new ArgumentException("Target path is required.", nameof(targetPath));
        }

        if (!_options.NoBuild)
        {
            await BuildAsync(targetPath, cancellationToken);
        }

        var failingTests = await GetFailingTestsAsync(targetPath, cancellationToken);

        var verdicts = new List<Verdict>();
        foreach (var test in failingTests)
        {
            var verdict = await InvestigateTestAsync(test, targetPath, cancellationToken);
            verdicts.Add(verdict);
        }

        return verdicts;
    }

    private async Task BuildAsync(string targetPath, CancellationToken cancellationToken)
    {
        var buildProcess = new ProcessStartInfo("dotnet")
        {
            ArgumentList = { "build" },
            WorkingDirectory = Path.GetDirectoryName(targetPath),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        using var process = Process.Start(buildProcess);
        if (process == null)
        {
            throw new TestRunExecutionException(1, "", "", "Failed to start build process");
        }

        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();
            throw new TestRunExecutionException(process.ExitCode, output, error, "Build failed");
        }
    }

    private async Task<List<TestResult>> GetFailingTestsAsync(string targetPath, CancellationToken cancellationToken)
    {
        var baselineRequest = new TestRunRequest(targetPath)
        {
            Timeout = TimeSpan.FromMinutes(_options.TimeoutMinutes),
        };

        var result = await _runner.RunAsync(baselineRequest, cancellationToken);
        return result.Failures.ToList();
    }

    private async Task<Verdict> InvestigateTestAsync(TestResult failingTest, string targetPath, CancellationToken cancellationToken)
    {
        var testIdentity = new TestIdentity(failingTest.FullName, Path.GetFileNameWithoutExtension(targetPath));
        var baselineRequest = new TestRunRequest(targetPath)
        {
            Timeout = TimeSpan.FromMinutes(_options.TimeoutMinutes),
        };

        var context = new TestRunContext(_runner, baselineRequest, testIdentity)
        {
            InitialRuns = _options.Runs,
            MaxRuns = _options.MaxRuns,
            TotalTimeout = TimeSpan.FromMinutes(_options.TimeoutMinutes),
            CancellationToken = cancellationToken,
        };

        var results = new Dictionary<string, ExperimentResult>();
        
        var isolation = new IsolationExperiment();
        results[VerdictInput.IsolationFactor] = await isolation.RunAsync(context, cancellationToken);

        var scopeLadder = new ScopeLadderExperiment();
        results[ScopeLadderExperiment.ScopeFactor] = await scopeLadder.RunAsync(context, cancellationToken);

        var pollutionResult = await RunPollutionExperimentAsync(context, scopeLadder, cancellationToken);
        if (pollutionResult != null)
        {
            results[PollutionExperiment.PolluterFactor] = pollutionResult;
        }

        var parallelism = new ParallelismExperiment();
        results[ParallelismExperiment.ParallelismFactor] = await parallelism.RunAsync(context, cancellationToken);

        var load = new LoadExperiment();
        results[LoadExperiment.LoadFactor] = await load.RunAsync(context, cancellationToken);

        var culture = new CultureExperiment();
        results[CultureExperiment.CultureFactor] = await culture.RunAsync(context, cancellationToken);

        var timeZone = new TimeZoneExperiment();
        results[TimeZoneExperiment.TimeZoneFactor] = await timeZone.RunAsync(context, cancellationToken);

        var registry = new HeuristicsRegistry();
        var heuristics = registry.Analyze(failingTest.ErrorMessage, failingTest.StackTrace);

        var input = new VerdictInput(testIdentity)
        {
            BaselineRequest = baselineRequest,
            Isolation = results.TryGetValue(VerdictInput.IsolationFactor, out var iso) ? iso : null,
            ScopeLadder = results.TryGetValue(ScopeLadderExperiment.ScopeFactor, out var scope) ? scope : null,
            Pollution = results.TryGetValue(PollutionExperiment.PolluterFactor, out var pollution) ? pollution : null,
            Parallelism = results.TryGetValue(ParallelismExperiment.ParallelismFactor, out var par) ? par : null,
            Load = results.TryGetValue(LoadExperiment.LoadFactor, out var load_) ? load_ : null,
            Culture = results.TryGetValue(CultureExperiment.CultureFactor, out var cult) ? cult : null,
            TimeZone = results.TryGetValue(TimeZoneExperiment.TimeZoneFactor, out var tz) ? tz : null,
            Heuristics = heuristics,
            HostAborted = failingTest.Outcome == TestOutcome.Aborted,
            ErrorMessage = failingTest.ErrorMessage,
            StackTrace = failingTest.StackTrace,
        };

        var engine = new VerdictEngine();
        var verdict = engine.Decide(input);

        verdict = await engine.VerifyReproAsync(verdict, _verifier, cancellationToken);

        return verdict;
    }

    private async Task<ExperimentResult?> RunPollutionExperimentAsync(TestRunContext context, ScopeLadderExperiment scopeLadder, CancellationToken cancellationToken)
    {
        var scopeResult = await scopeLadder.RunAsync(context, cancellationToken);
        if (scopeResult.Runs.Count == 0 || scopeResult.Runs[^1].Failures == 0)
        {
            return null;
        }

        var failsAlone = scopeResult.Runs.First().Failures > 0;
        if (failsAlone)
        {
            return null;
        }

        var allTestsRun = await _runner.RunAsync(context.BaselineRequest, cancellationToken);
        var otherTests = allTestsRun.Tests
            .Where(t => t.FullName != context.FailingTest.FullyQualifiedName)
            .Select(t => t.FullName)
            .ToList();

        if (otherTests.Count == 0)
        {
            return null;
        }

        var pollution = new PollutionExperiment(otherTests);
        return await pollution.RunAsync(context, cancellationToken);
    }
}
