using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Deflake.Core.Execution;
using Deflake.Core.Search;

namespace Deflake.Core.Experiments;

/// <summary>
/// Given a scope in which the failing test is known to fail (the class or assembly the scope ladder
/// pointed at), finds the smallest set of other tests in that scope that reproduce the failure when
/// run together with it. Isolation says the test is not broken alone; the scope ladder says how much
/// company it needs; this experiment names which company: the polluters.
/// </summary>
/// <remarks>
/// Delta debugging (<see cref="DeltaDebugging.Minimize{T}"/>) drives the search: each candidate subset
/// of the scope's other tests is run alongside the failing test, and ddmin shrinks a failing subset
/// until removing any single test from it makes the failure disappear. ddmin's predicate is
/// synchronous, and <see cref="Experiment.MeasureAsync"/> is not, so the whole search runs on a pool
/// thread (<see cref="Task.Run(Action)"/>) and each candidate is measured with a blocking wait on its
/// task there. That is safe: candidates are probed one at a time, in the order ddmin asks for them,
/// and no <see cref="ITestRunner"/> (real or fake) needs a captured synchronization context to
/// complete, so nothing can deadlock waiting for the thread that is doing the waiting.
/// </remarks>
public sealed class PollutionExperiment : Experiment
{
    /// <summary>The factor this experiment varies: which subset of the scope ran alongside the failing test.</summary>
    public const string PolluterFactor = "Polluters";

    /// <summary>ddmin candidates tried when no cap is given. Generous, because most searches are far smaller.</summary>
    public const int DefaultMaxDdminTests = 500;

    private readonly IReadOnlyList<string> _candidateTests;
    private readonly int _maxDdminTests;

    /// <param name="candidateTests">
    /// Every other test in the scope the failure needs (the scope ladder's answer), in the order they
    /// ran. Duplicates are removed and the failing test itself is dropped if present, so a caller does
    /// not have to filter its own list first.
    /// </param>
    /// <param name="maxDdminTests">Most ddmin candidates to try before returning the best set found so far.</param>
    public PollutionExperiment(IReadOnlyList<string> candidateTests, int maxDdminTests = DefaultMaxDdminTests)
    {
        if (candidateTests is null)
        {
            throw new ArgumentNullException(nameof(candidateTests));
        }

        if (maxDdminTests < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxDdminTests), "At least one ddmin candidate must be allowed.");
        }

        _candidateTests = candidateTests.Distinct(StringComparer.Ordinal).ToList();
        _maxDdminTests = maxDdminTests;
    }

    public override string Name => "Polluter search";

    public override string Description =>
        "Runs subsets of the failing scope's other tests together with the failing test, and delta-debugs "
        + "down to the smallest set that still reproduces the failure.";

    /// <summary>
    /// Runs the search and reports the answer it exists to find: the minimal polluter set, alongside
    /// the evidence. Callers that only want the generic experiment shape can use the inherited
    /// <see cref="Experiment.RunAsync"/> instead; both run the same search once and cost the same.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> is null.</exception>
    /// <exception cref="ArgumentException">The context asks for fewer maximum runs than initial runs.</exception>
    /// <exception cref="OperationCanceledException">The context's token or <paramref name="cancellationToken"/> was cancelled.</exception>
    public async Task<PollutionResult> FindPollutersAsync(TestRunContext context, CancellationToken cancellationToken = default)
    {
        ValidateContext(context);

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken, cancellationToken);
        var search = await Task.Run(() => Search(context, linked.Token), linked.Token).ConfigureAwait(false);

        return new PollutionResult(
            search.Polluters,
            search.PolluterFound,
            search.Runs,
            search.Completed,
            search.IncompleteReason,
            search.DdminTestsRun);
    }

    protected override async Task<ExperimentResult> RunCoreAsync(TestRunContext context, CancellationToken cancellationToken)
    {
        var search = await Task.Run(() => Search(context, cancellationToken), cancellationToken).ConfigureAwait(false);

        return new ExperimentResult(Name, PolluterFactor, search.Runs, search.Completed, search.IncompleteReason);
    }

    /// <summary>The ddmin-driven search itself. Synchronous end to end, and run on a pool thread by both callers.</summary>
    private SearchOutcome Search(TestRunContext context, CancellationToken cancellationToken)
    {
        var others = _candidateTests
            .Where(name => !string.Equals(name, context.FailingTest.FullyQualifiedName, StringComparison.Ordinal))
            .ToList();

        var budget = ExperimentBudget.Start(context);
        var runs = new List<ExperimentRun>();
        string? incompleteReason = null;
        var stopped = false;
        var candidateNumber = 0;

        bool Fails(IReadOnlyList<string> subset)
        {
            if (stopped)
            {
                // A run already came back unmeasurable, so there is nothing further to learn: another
                // run of the same kind would only fail to measure the same way. Answering "does not
                // reproduce" lets ddmin conclude quickly instead of spending the rest of its budget on
                // runs that cannot be scored.
                return false;
            }

            candidateNumber++;
            var label = $"Candidate {candidateNumber} ({subset.Count} test{(subset.Count == 1 ? string.Empty : "s")})";
            var testsInRun = subset.Append(context.FailingTest.FullyQualifiedName).ToList();
            var request = context.RequestWithFilter(TestFilter.ForTests(testsInRun));

            // ddmin's predicate is synchronous; see the class remarks for why blocking here is safe.
            var run = MeasureAsync(context, label, request, budget, cancellationToken).GetAwaiter().GetResult();
            runs.Add(run);

            if (run.IncompleteReason is not null)
            {
                stopped = true;
                incompleteReason = $"The polluter search stopped at {label}. {run.IncompleteReason}";
            }

            return run.Failures > 0;
        }

        var ddmin = DeltaDebugging.Minimize(others, Fails, _maxDdminTests, cancellationToken);

        var polluters = ddmin.InputFails ? ddmin.Minimal : Array.Empty<string>();
        var polluterFound = polluters.Count > 0;

        if (incompleteReason is null && !ddmin.Completed)
        {
            incompleteReason =
                $"The ddmin search stopped after {_maxDdminTests} candidate run(s) without confirming a 1-minimal set.";
        }

        return new SearchOutcome(polluters, polluterFound, runs, incompleteReason is null, incompleteReason, ddmin.TestsRun);
    }

    /// <summary>The search's raw findings, before they are shaped into either public result type.</summary>
    private sealed record SearchOutcome(
        IReadOnlyList<string> Polluters,
        bool PolluterFound,
        IReadOnlyList<ExperimentRun> Runs,
        bool Completed,
        string? IncompleteReason,
        int DdminTestsRun);
}
