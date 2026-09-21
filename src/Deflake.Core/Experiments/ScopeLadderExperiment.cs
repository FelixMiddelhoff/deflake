using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Deflake.Core.Execution;

namespace Deflake.Core.Experiments;

/// <summary>
/// Gives the failing test more and more company — alone, then its class, then the original scope,
/// then the whole assembly — and stops at the first step in which it fails. That step is the smallest
/// amount of company the failure needs, and therefore where to look for what it shares with.
/// </summary>
public sealed class ScopeLadderExperiment : Experiment
{
    /// <summary>The factor this experiment varies.</summary>
    public const string ScopeFactor = "Scope";

    public override string Name => "Scope ladder";

    public override string Description =>
        "Runs the failing test alone, then with its class, then in the original scope, then with the whole assembly, "
        + "and reports the smallest scope in which it still fails.";

    /// <summary>
    /// The filter that runs every test of one class. VSTest has no class property that all of xUnit,
    /// NUnit and MSTest report the same way, so this matches on the full name instead; the trailing
    /// dot keeps <c>Shop.OrderTests.</c> from also selecting <c>Shop.OrderTestsExtra.Total</c>.
    /// </summary>
    public static string ClassFilter(string className)
    {
        if (string.IsNullOrWhiteSpace(className))
        {
            throw new ArgumentException("A class name is required.", nameof(className));
        }

        return "FullyQualifiedName~" + TestFilter.Escape(className + ".");
    }

    protected override async Task<ExperimentResult> RunCoreAsync(TestRunContext context, CancellationToken cancellationToken)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        var budget = ExperimentBudget.Start(context);
        var runs = new List<ExperimentRun>();
        string? incompleteReason = null;

        foreach (var step in Ladder(context))
        {
            var run = await MeasureAsync(context, step.Level.ToString(), step.Request, budget, cancellationToken)
                .ConfigureAwait(false);
            runs.Add(run);

            if (run.IncompleteReason is not null)
            {
                incompleteReason = $"The ladder stopped at {step.Level}. {run.IncompleteReason}";
                break;
            }

            // The first step that fails is the answer: a wider scope cannot make the failure need less
            // company, so running the rest would cost time and add nothing.
            if (run.Failures > 0)
            {
                break;
            }
        }

        return new ExperimentResult(
            Name,
            ScopeFactor,
            runs,
            completed: incompleteReason is null,
            incompleteReason);
    }

    /// <summary>
    /// The steps in order of increasing company. The original scope is a step of its own only when it
    /// was narrower than the assembly; otherwise it is the assembly step and the ladder would pay
    /// twice for one answer.
    /// </summary>
    private static IEnumerable<(ScopeLevel Level, TestRunRequest Request)> Ladder(TestRunContext context)
    {
        var test = context.FailingTest;

        yield return (ScopeLevel.Alone, context.RequestWithFilter(TestFilter.ForTest(test.FullyQualifiedName)));
        yield return (ScopeLevel.Class, context.RequestWithFilter(ClassFilter(test.ClassName)));

        if (!string.IsNullOrWhiteSpace(context.BaselineRequest.Filter))
        {
            yield return (ScopeLevel.Full, context.BaselineRequest);
        }

        yield return (ScopeLevel.Assembly, context.RequestWithFilter(null));
    }
}
