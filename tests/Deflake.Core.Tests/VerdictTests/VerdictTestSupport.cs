using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Deflake.Core.Execution;
using Deflake.Core.Experiments;
using Deflake.Core.Heuristics;
using Deflake.Core.Verdicts;

namespace Deflake.Core.Tests;

/// <summary>
/// Experiment results built by hand, because the verdict engine's input is exactly that: results.
/// Nothing here runs a test or an experiment, so every verdict test states its evidence in one line
/// and reads as the table a developer would be shown.
/// </summary>
internal static class VerdictTestSupport
{
    /// <summary>The default number of runs per condition: enough for the engine to compare rates.</summary>
    public const int Runs = VerdictEngine.MinimumRuns;

    /// <summary>One condition of an experiment: how often the test failed out of how many runs.</summary>
    public static ExperimentRun Row(
        string condition,
        int failures,
        int total = Runs,
        int missing = 0,
        string? filter = null,
        string? incompleteReason = null)
    {
        return new ExperimentRun(condition, ExperimentTestSupport.Baseline(filter), total, failures, missing, incompleteReason);
    }

    public static ExperimentResult Completed(string name, string? factor, params ExperimentRun[] rows)
    {
        return new ExperimentResult(name, factor, rows, completed: true);
    }

    public static ExperimentResult Incomplete(string name, string? factor, string reason, params ExperimentRun[] rows)
    {
        return new ExperimentResult(name, factor, rows, completed: false, reason);
    }

    /// <summary>The failing test run on its own.</summary>
    public static ExperimentResult Isolation(int failures, int total = Runs)
    {
        return Completed("Isolation", factor: null, Row(IsolationExperiment.Alone, failures, total));
    }

    /// <summary>The scope ladder, given as the steps it got to.</summary>
    public static ExperimentResult ScopeLadder(params (ScopeLevel Level, int Failures)[] steps)
    {
        var rows = steps.Select(step => Row(step.Level.ToString(), step.Failures)).ToArray();
        return Completed("Scope ladder", ScopeLadderExperiment.ScopeFactor, rows);
    }

    public static ExperimentResult Parallelism(int serialFailures, int parallelFailures, int total = Runs)
    {
        return Completed(
            "Parallelism",
            ParallelismExperiment.ParallelismFactor,
            Row(ParallelismExperiment.Serial, serialFailures, total),
            Row(ParallelismExperiment.Parallel, parallelFailures, total, filter: "parallel"));
    }

    public static ExperimentResult Load(int idleFailures, int loadedFailures, int total = Runs)
    {
        return Completed(
            "Load",
            LoadExperiment.LoadFactor,
            Row(LoadExperiment.Idle, idleFailures, total),
            Row(LoadExperiment.UnderLoad, loadedFailures, total));
    }

    public static ExperimentResult Cultures(params (string Culture, int Failures)[] rows)
    {
        return Completed(
            "Culture",
            CultureExperiment.CultureFactor,
            rows.Select(row => Row(row.Culture, row.Failures)).ToArray());
    }

    public static ExperimentResult TimeZones(params (string Zone, int Failures)[] rows)
    {
        return Completed(
            "TimeZone",
            TimeZoneExperiment.TimeZoneFactor,
            rows.Select(row => Row(row.Zone, row.Failures)).ToArray());
    }

    /// <summary>The polluter search, as the answer it reached.</summary>
    public static PollutionResult Polluters(params string[] polluters)
    {
        return new PollutionResult(
            polluters,
            polluterFound: polluters.Length > 0,
            new[] { Row("subset", failures: Runs) },
            completed: true,
            incompleteReason: null,
            ddminTestsRun: 3);
    }

    /// <summary>A polluter search that came back empty: the test kept company and still passed.</summary>
    public static PollutionResult NoPolluters()
    {
        return new PollutionResult(
            new List<string>(),
            polluterFound: false,
            new[] { Row("subset", failures: 0) },
            completed: true,
            incompleteReason: null,
            ddminTestsRun: 5);
    }

    /// <summary>Message patterns the heuristics would have reported, strongest first.</summary>
    public static HeuristicFindings Findings(params (string Pattern, double Confidence)[] patterns)
    {
        var findings = new HeuristicFindings();
        foreach (var pattern in patterns.OrderByDescending(pattern => pattern.Confidence))
        {
            findings.Add(new HeuristicResult(pattern.Pattern, $"{pattern.Pattern} was seen in the message.", pattern.Confidence));
        }

        return findings;
    }

    /// <summary>
    /// The engine's input, named part by part. Everything is optional because an investigation that
    /// ran out of budget hands in exactly that: some experiments and not others.
    /// </summary>
    public static VerdictInput Input(
        ExperimentResult? isolation = null,
        ExperimentResult? scope = null,
        ExperimentResult? pollution = null,
        PollutionResult? polluters = null,
        ExperimentResult? parallelism = null,
        ExperimentResult? load = null,
        ExperimentResult? culture = null,
        ExperimentResult? timeZone = null,
        HeuristicFindings? heuristics = null,
        bool hostAborted = false,
        string? hostCrashDetail = null,
        string? errorMessage = null,
        string? stackTrace = null,
        TestIdentity? test = null)
    {
        return new VerdictInput(test ?? ExperimentTestSupport.Identity())
        {
            BaselineRequest = ExperimentTestSupport.Baseline(),
            Isolation = isolation,
            ScopeLadder = scope,
            Pollution = pollution,
            PollutionSearch = polluters,
            Parallelism = parallelism,
            Load = load,
            Culture = culture,
            TimeZone = timeZone,
            Heuristics = heuristics,
            HostAborted = hostAborted,
            HostCrashDetail = hostCrashDetail,
            ErrorMessage = errorMessage,
            StackTrace = stackTrace,
        };
    }
}

/// <summary>A repro verification with a scripted answer, so the gate can be tested without a process.</summary>
internal sealed class FakeReproVerifier : IReproVerifier
{
    private readonly bool _reproduced;

    public FakeReproVerifier(bool reproduced)
    {
        _reproduced = reproduced;
    }

    /// <summary>How often the verification actually ran, to prove it is skipped where it should be.</summary>
    public int Calls { get; private set; }

    public Task<bool> VerifyAsync(TestIdentity test, Verdict verdict, CancellationToken cancellationToken)
    {
        Calls++;
        return Task.FromResult(_reproduced);
    }
}
