using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Deflake.Core.Execution;
using Deflake.Core.Statistics;
using Deflake.Core.Trx;

namespace Deflake.Core.Experiments;

/// <summary>
/// Compares the failing test's live failure rate against its failure rate under a recorded
/// <c>TimeProvider</c>/<c>Random</c> replay (<c>Deflake.Runtime</c>, an opt-in package referenced only
/// by the user's test project), to tell "this failure is driven by the values my clock/RNG returned"
/// apart from "this failure is a real race that no fixed sequence removes". See
/// <c>deflake-runtime-design.md</c> §3-4.
/// </summary>
/// <remarks>
/// The "live" condition costs nothing extra: it is the isolation experiment's own run, handed in
/// rather than repeated, because the recording env var already rode along on that run for free (see
/// <c>InvestigationPipeline</c>). Only the "replayed" condition spends new runs, and only when a
/// session file actually appeared — a project that never references <c>Deflake.Runtime</c> never
/// writes one, so this experiment reports itself <see cref="ExperimentResult.Completed"/> = false
/// immediately, before spending anything.
/// </remarks>
public sealed class RecordReplayExperiment : Experiment
{
    /// <summary>The factor this experiment varies.</summary>
    public const string RecordReplayFactor = "RecordReplay";

    /// <summary>The condition backed by the isolation experiment's own run, not repeated here.</summary>
    public const string Live = "live";

    /// <summary>The condition run with <c>DEFLAKE_REPLAY</c> pointed at the recorded session.</summary>
    public const string Replayed = "replayed";

    /// <summary>Environment variable the CLI sets on the replay runs, read by <c>DeflakeRecorder</c>.</summary>
    public const string ReplayEnvironmentVariable = "DEFLAKE_REPLAY";

    /// <summary>
    /// Name of the exception <c>Deflake.Runtime</c> throws when a replay run asks for a value the
    /// recording does not have. Matched against the TRX error message/stack trace, not caught
    /// directly: the run happened in a separate <c>dotnet test</c> process.
    /// </summary>
    public const string DivergenceExceptionName = "DeflakeReplayDivergenceException";

    private readonly string? _sessionPath;
    private readonly ExperimentRun? _liveRun;

    /// <param name="sessionPath">
    /// Absolute path to the session JSON <c>Deflake.Runtime</c> would have written while recording the
    /// isolation run, or null when <c>--record-replay</c> was not passed. Checked for existence when
    /// the experiment runs, not at construction, since the recording run may not have finished writing
    /// it yet when the pipeline builds this experiment.
    /// </param>
    /// <param name="liveRun">
    /// The isolation experiment's own "alone" run, reused as the live condition rather than repeated.
    /// Null when isolation never produced one, in which case this experiment has nothing to compare
    /// replay against and reports itself not tested.
    /// </param>
    public RecordReplayExperiment(string? sessionPath, ExperimentRun? liveRun)
    {
        _sessionPath = sessionPath;
        _liveRun = liveRun;
    }

    public override string Name => "Record/replay";

    public override string Description =>
        "Replays the failing test's recorded TimeProvider/Random values (Deflake.Runtime, opt-in) and "
        + "compares its failure rate against the live run, to see whether the values themselves, rather "
        + "than real thread interleaving, are sufficient to reproduce the failure.";

    protected override async Task<ExperimentResult> RunCoreAsync(TestRunContext context, CancellationToken cancellationToken)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        if (string.IsNullOrWhiteSpace(_sessionPath) || !File.Exists(_sessionPath))
        {
            return NotTested("no Deflake.Runtime session captured for this test");
        }

        if (_liveRun is null)
        {
            return NotTested("no isolation baseline was available to compare the replay against");
        }

        var live = new ExperimentRun(Live, _liveRun.Request, _liveRun.Total, _liveRun.Failures, _liveRun.Missing, _liveRun.IncompleteReason);

        var budget = ExperimentBudget.Start(context);
        var request = CreateReplayRequest(context, _sessionPath);
        var replayed = await MeasureReplayAsync(context, Replayed, request, budget, cancellationToken).ConfigureAwait(false);

        var incompleteReason = replayed.IncompleteReason is null
            ? null
            : $"The experiment stopped at '{Replayed}'. {replayed.IncompleteReason}";

        return new ExperimentResult(
            Name,
            RecordReplayFactor,
            new[] { live, replayed },
            completed: incompleteReason is null,
            incompleteReason);
    }

    private ExperimentResult NotTested(string reason)
    {
        return new ExperimentResult(Name, RecordReplayFactor, Array.Empty<ExperimentRun>(), completed: false, reason);
    }

    /// <summary>Overlays <see cref="ReplayEnvironmentVariable"/> on the isolated-test request, same shape every experiment uses.</summary>
    private static TestRunRequest CreateReplayRequest(TestRunContext context, string sessionPath)
    {
        var baseline = context.RequestWithFilter(TestFilter.ForTest(context.FailingTest.FullyQualifiedName));

        var env = baseline.EnvironmentVariables is not null
            ? new Dictionary<string, string>(baseline.EnvironmentVariables)
            : new Dictionary<string, string>();

        env[ReplayEnvironmentVariable] = sessionPath;

        return new TestRunRequest(baseline.TargetPath)
        {
            Filter = baseline.Filter,
            RunSettingsPath = baseline.RunSettingsPath,
            EnvironmentVariables = env,
            Timeout = baseline.Timeout,
            WorkingDirectory = baseline.WorkingDirectory,
        };
    }

    /// <summary>
    /// Repeats <paramref name="request"/> exactly the way <see cref="Experiment.MeasureAsync"/> does,
    /// with one difference: a run whose <see cref="TestResult"/> carries the
    /// <see cref="DivergenceExceptionName"/> marker is counted as missing, not as a failure of the
    /// test, because it measured a recording/replay mismatch, not the bug under investigation. This is
    /// the one place this experiment cannot reuse the base counting loop unmodified — everything else
    /// (adaptive run counts, budget, timeout/execution handling) mirrors it exactly.
    /// </summary>
    private static async Task<ExperimentRun> MeasureReplayAsync(
        TestRunContext context,
        string factorValue,
        TestRunRequest request,
        ExperimentBudget budget,
        CancellationToken cancellationToken)
    {
        var testName = context.FailingTest.FullyQualifiedName;
        var started = 0;
        var failures = 0;
        var missing = 0;
        string? incompleteReason = null;

        while (NeedsMoreReplayRuns(context, started, new FailureRate(failures, started - missing)))
        {
            if (budget.IsExhausted)
            {
                incompleteReason =
                    $"The time budget of {budget.Limit} ran out after {started} run(s), before the {context.InitialRuns} this condition needs.";
                break;
            }

            cancellationToken.ThrowIfCancellationRequested();

            TestRunResult result;
            try
            {
                result = await context.Runner.RunAsync(request, cancellationToken).ConfigureAwait(false);
            }
            catch (TestRunTimeoutException timeout)
            {
                started++;
                missing++;
                incompleteReason = $"Run {started} did not finish within {timeout.Timeout} and was killed: {timeout.Message}";
                break;
            }
            catch (TestRunExecutionException execution)
            {
                started++;
                missing++;
                incompleteReason = $"Run {started} produced no results: {execution.Message}";
                break;
            }

            started++;
            if (DivergedInReplay(result, testName))
            {
                missing++;
            }
            else if (result.Failed(testName))
            {
                failures++;
            }
            else if (!result.Passed(testName))
            {
                missing++;
            }
        }

        return new ExperimentRun(factorValue, request, started, failures, missing, incompleteReason);
    }

    /// <summary>
    /// True when any result for <paramref name="testName"/> in <paramref name="result"/> carries the
    /// <see cref="DivergenceExceptionName"/> marker in its error message or stack trace — the only way
    /// a separate <c>dotnet test</c> process can tell this experiment that replay diverged, since the
    /// exception was thrown and caught inside that process, not this one.
    /// </summary>
    private static bool DivergedInReplay(TestRunResult result, string testName)
    {
        foreach (var test in result.Named(testName))
        {
            if (Mentions(test.ErrorMessage) || Mentions(test.StackTrace))
            {
                return true;
            }
        }

        return false;
    }

    private static bool Mentions(string? text)
    {
        return text is not null && text.Contains(DivergenceExceptionName, StringComparison.Ordinal);
    }

    /// <summary>Same rule <see cref="Experiment"/> uses internally; duplicated because that one is private to the base class.</summary>
    private static bool NeedsMoreReplayRuns(TestRunContext context, int runsStarted, FailureRate measured)
    {
        if (runsStarted < context.InitialRuns)
        {
            return true;
        }

        if (runsStarted >= context.MaxRuns)
        {
            return false;
        }

        return AdaptiveRuns.WouldGainFromMoreRuns(measured, context.TargetIntervalWidth);
    }
}
