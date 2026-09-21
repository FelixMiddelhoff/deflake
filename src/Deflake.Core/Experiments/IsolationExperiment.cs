using System;
using System.Threading;
using System.Threading.Tasks;
using Deflake.Core.Execution;

namespace Deflake.Core.Experiments;

/// <summary>
/// Runs the failing test on its own, with no other test in the process, as often as the budget
/// allows. Its rate, held against the rate of the original scope, is what separates a test that is
/// broken by itself from one that only fails in company.
/// </summary>
public sealed class IsolationExperiment : Experiment
{
    /// <summary>The single condition this experiment measures.</summary>
    public const string Alone = "Alone";

    public override string Name => "Isolation";

    public override string Description =>
        "Runs the failing test alone, with no other test in the same process, to see whether it still fails.";

    protected override async Task<ExperimentResult> RunCoreAsync(TestRunContext context, CancellationToken cancellationToken)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        var budget = ExperimentBudget.Start(context);
        var request = context.RequestWithFilter(TestFilter.ForTest(context.FailingTest.FullyQualifiedName));
        var run = await MeasureAsync(context, Alone, request, budget, cancellationToken).ConfigureAwait(false);

        return new ExperimentResult(
            Name,
            factor: null,
            new[] { run },
            completed: run.IncompleteReason is null,
            run.IncompleteReason);
    }
}
