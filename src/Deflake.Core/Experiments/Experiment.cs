using System;
using System.Threading;
using System.Threading.Tasks;
using Deflake.Core.Execution;
using Deflake.Core.Statistics;
using Deflake.Core.Trx;

namespace Deflake.Core.Experiments;

/// <summary>
/// One question put to the failing test and answered by running it. An experiment changes exactly one
/// thing against the baseline scope and measures how the failure rate reacts; it never interprets the
/// numbers, because that is the verdict engine's job.
/// </summary>
/// <remarks>
/// Subclasses implement <see cref="RunCoreAsync"/>. The argument checks and the measuring loop live
/// here so that every experiment counts, stops and reports the same way, and so that a new experiment
/// cannot quietly invent its own statistics.
/// </remarks>
public abstract class Experiment
{
    /// <summary>The experiment's name, as reports and evidence tables print it.</summary>
    public abstract string Name { get; }

    /// <summary>One sentence on what the experiment does and what its answer means.</summary>
    public abstract string Description { get; }

    /// <summary>
    /// Runs the experiment. Runs happen one after another, never in parallel, because two runs on one
    /// machine influence each other and would falsify exactly what is being measured.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> is null.</exception>
    /// <exception cref="ArgumentException">The context asks for fewer maximum runs than initial runs.</exception>
    /// <exception cref="OperationCanceledException">The context's token or <paramref name="cancellationToken"/> was cancelled.</exception>
    public Task<ExperimentResult> RunAsync(TestRunContext context, CancellationToken cancellationToken = default)
    {
        ValidateContext(context);
        return RunLinkedAsync(context, cancellationToken);
    }

    /// <summary>
    /// The checks every public entry point makes before touching the context. Most experiments only
    /// need <see cref="RunAsync"/>, which calls this itself; the polluter search exposes a second
    /// entry point that returns its own richer result type and calls this too, so a bad context is
    /// refused the same way everywhere.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> is null.</exception>
    /// <exception cref="ArgumentException">The context asks for fewer maximum runs than initial runs.</exception>
    protected static void ValidateContext(TestRunContext context)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        if (context.MaxRuns < context.InitialRuns)
        {
            throw new ArgumentException(
                $"The run ceiling ({context.MaxRuns}) is below the {context.InitialRuns} runs every condition starts with.",
                nameof(context));
        }
    }

    /// <summary>The experiment itself. The token passed here already covers the context's token.</summary>
    protected abstract Task<ExperimentResult> RunCoreAsync(TestRunContext context, CancellationToken cancellationToken);

    /// <summary>
    /// Repeats <paramref name="request"/> until the evidence is as sharp as the context allows, and
    /// counts what happened to the test under investigation.
    /// </summary>
    /// <remarks>
    /// A run that cannot be scored (the process timed out, or left no results at all) is not turned
    /// into a failure of the test: it is counted as missing and ends this condition, because
    /// repeating a run that cannot be scored produces nothing but the same non-answer.
    /// </remarks>
    protected static async Task<ExperimentRun> MeasureAsync(
        TestRunContext context,
        string factorValue,
        TestRunRequest request,
        ExperimentBudget budget,
        CancellationToken cancellationToken)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        if (budget is null)
        {
            throw new ArgumentNullException(nameof(budget));
        }

        var testName = context.FailingTest.FullyQualifiedName;
        var started = 0;
        var failures = 0;
        var missing = 0;
        string? incompleteReason = null;

        while (NeedsMoreRuns(context, started, new FailureRate(failures, started - missing)))
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
            if (result.Failed(testName))
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

    private static bool NeedsMoreRuns(TestRunContext context, int runsStarted, FailureRate measured)
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

    private async Task<ExperimentResult> RunLinkedAsync(TestRunContext context, CancellationToken cancellationToken)
    {
        // The context carries the investigation's token; a caller may pass a second one for this
        // experiment alone. Linking them honours both without every experiment having to remember.
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken, cancellationToken);
        return await RunCoreAsync(context, linked.Token).ConfigureAwait(false);
    }
}
