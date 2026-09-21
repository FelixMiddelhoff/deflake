using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Deflake.Core.Execution;
using Deflake.Core.Trx;

namespace Deflake.Core.Tests;

/// <summary>
/// An <see cref="ITestRunner"/> with scripted ground truth: every call returns the next
/// pre-configured result instead of spawning a process. Experiments and statistics tests use this
/// so they never depend on a real <c>dotnet test</c> run; only <see cref="DotnetTestRunner"/>
/// itself is exercised against a real test project, in <c>ExecutionTests</c>.
/// </summary>
public sealed class FakeTestRunner : ITestRunner
{
    private readonly Queue<Func<TestRunRequest, TestRunResult>> _scripted = new();

    /// <summary>Every request the fake received, in call order, for assertions on what was asked for.</summary>
    public List<TestRunRequest> Requests { get; } = new();

    /// <summary>Queues a fixed result for the next call.</summary>
    public FakeTestRunner Returns(TestRunResult result)
    {
        _scripted.Enqueue(_ => result);
        return this;
    }

    /// <summary>Queues a result computed from the request, for a fake whose answer depends on the filter.</summary>
    public FakeTestRunner Returns(Func<TestRunRequest, TestRunResult> result)
    {
        _scripted.Enqueue(result);
        return this;
    }

    /// <summary>Convenience: queues a run where every named test passed.</summary>
    public FakeTestRunner ReturnsAllPassed(params string[] fullNames)
    {
        return Returns(new TestRunResult(BuildResults(fullNames, TestOutcome.Passed)));
    }

    /// <summary>Convenience: queues a run where every named test failed.</summary>
    public FakeTestRunner ReturnsAllFailed(params string[] fullNames)
    {
        return Returns(new TestRunResult(BuildResults(fullNames, TestOutcome.Failed)));
    }

    public Task<TestRunResult> RunAsync(TestRunRequest request, CancellationToken cancellationToken)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        cancellationToken.ThrowIfCancellationRequested();
        Requests.Add(request);

        if (_scripted.Count == 0)
        {
            throw new InvalidOperationException(
                $"FakeTestRunner received a call for '{request.TargetPath}' but has no scripted result left. " +
                "Queue one with Returns(...) before running the code under test.");
        }

        return Task.FromResult(_scripted.Dequeue()(request));
    }

    private static List<TestResult> BuildResults(string[] fullNames, TestOutcome outcome)
    {
        var results = new List<TestResult>();
        var start = DateTimeOffset.UtcNow;
        foreach (var fullName in fullNames)
        {
            results.Add(new TestResult(
                fullName,
                fullName,
                outcome,
                outcome.ToString(),
                start,
                start,
                TimeSpan.Zero,
                errorMessage: outcome == TestOutcome.Failed ? "scripted failure" : null,
                stackTrace: null,
                standardOutput: null));
        }

        return results;
    }
}
