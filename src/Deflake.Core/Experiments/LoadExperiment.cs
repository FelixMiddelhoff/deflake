using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Deflake.Core.Execution;

namespace Deflake.Core.Experiments;

/// <summary>
/// Runs the failing test under normal conditions (Idle) and again with CPU load running in the
/// background (UnderLoad). Load is generated via busy-wait loops on all available CPU cores,
/// simulating system contention that may expose timing-sensitive flakes.
/// </summary>
public sealed class LoadExperiment : Experiment
{
    /// <summary>The factor this experiment varies.</summary>
    public const string LoadFactor = "Load";

    /// <summary>Condition: no background load.</summary>
    public const string Idle = "Idle";

    /// <summary>Condition: background CPU load active on all cores.</summary>
    public const string UnderLoad = "UnderLoad";

    public override string Name => "Load";

    public override string Description =>
        "Runs the failing test under normal conditions and again with background CPU load active "
        + "to see whether system contention affects the failure rate.";

    protected override async Task<ExperimentResult> RunCoreAsync(TestRunContext context, CancellationToken cancellationToken)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        var budget = ExperimentBudget.Start(context);
        var runs = new List<ExperimentRun>();
        string? incompleteReason = null;

        // Run idle
        var idleRun = await MeasureAsync(context, Idle, context.BaselineRequest, budget, cancellationToken)
            .ConfigureAwait(false);
        runs.Add(idleRun);

        if (idleRun.IncompleteReason is not null)
        {
            incompleteReason = $"The experiment stopped after {Idle}. {idleRun.IncompleteReason}";
            return new ExperimentResult(Name, LoadFactor, runs, completed: false, incompleteReason);
        }

        // Run under load
        var loadTokenSource = new CancellationTokenSource();
        var loadTask = Task.Run(() => GenerateLoad(loadTokenSource.Token), CancellationToken.None);

        try
        {
            var underLoadRun = await MeasureAsync(context, UnderLoad, context.BaselineRequest, budget, cancellationToken)
                .ConfigureAwait(false);
            runs.Add(underLoadRun);

            if (underLoadRun.IncompleteReason is not null)
            {
                incompleteReason = $"The experiment stopped after {UnderLoad}. {underLoadRun.IncompleteReason}";
            }
        }
        finally
        {
            loadTokenSource.Cancel();
            try
            {
                await loadTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected when load generation is cancelled.
            }
        }

        return new ExperimentResult(
            Name,
            LoadFactor,
            runs,
            completed: incompleteReason is null,
            incompleteReason);
    }

    /// <summary>Generates CPU load by spinning on all available cores until cancellation.</summary>
    private static void GenerateLoad(CancellationToken cancellationToken)
    {
        var coreCount = Environment.ProcessorCount;
        var tasks = new Task[coreCount];

        for (int i = 0; i < coreCount; i++)
        {
            tasks[i] = Task.Run(() =>
            {
                // Busy-wait to consume CPU. Check cancellation periodically.
                long counter = 0;
                while (!cancellationToken.IsCancellationRequested)
                {
                    counter++;
                    if (counter % 100000 == 0)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                    }
                }
            }, CancellationToken.None);
        }

        try
        {
            Task.WaitAll(tasks);
        }
        catch (AggregateException)
        {
            // Expected when cancellation is requested; tasks are cleaning up.
        }
    }
}
