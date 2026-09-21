using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Deflake.Core.Execution;
using Deflake.Core.Experiments;
using Deflake.Core.Trx;

namespace Deflake.Core.Tests;

/// <summary>
/// The ground truth the experiment tests are written against: one failing test in a class with a
/// sibling, a baseline request, and scripted run results. Nothing here touches a process or a clock,
/// so every experiment test is deterministic.
/// </summary>
internal static class ExperimentTestSupport
{
    public const string FailingTest = "Shop.OrderTests.Total_is_summed";
    public const string SiblingTest = "Shop.OrderTests.Total_is_rounded";
    public const string OtherClassTest = "Shop.CartTests.Items_are_counted";
    public const string ClassName = "Shop.OrderTests";
    public const string TargetPath = @"C:\repo\tests\Shop.Tests\Shop.Tests.csproj";

    public static TestIdentity Identity(string fullName = FailingTest, string? className = null)
    {
        return new TestIdentity(fullName, "Shop.Tests.dll", className);
    }

    /// <summary>The run the failure was seen in: the scope every experiment varies one thing about.</summary>
    public static TestRunRequest Baseline(string? filter = null)
    {
        return new TestRunRequest(TargetPath)
        {
            Filter = filter,
            RunSettingsPath = @"C:\repo\ci.runsettings",
            EnvironmentVariables = new Dictionary<string, string> { ["DOTNET_CLI_UI_LANGUAGE"] = "en" },
            Timeout = TimeSpan.FromMinutes(3),
            WorkingDirectory = @"C:\repo",
        };
    }

    public static TestRunContext Context(
        ITestRunner runner,
        string? baselineFilter = null,
        int initialRuns = 20,
        int? maxRuns = null,
        TestIdentity? test = null,
        TimeSpan? totalTimeout = null,
        TimeProvider? timeProvider = null,
        CancellationToken cancellationToken = default)
    {
        return new TestRunContext(runner, Baseline(baselineFilter), test ?? Identity())
        {
            InitialRuns = initialRuns,
            MaxRuns = maxRuns ?? initialRuns,
            TotalTimeout = totalTimeout,
            TimeProvider = timeProvider ?? TimeProvider.System,
            CancellationToken = cancellationToken,
        };
    }

    /// <summary>A run in which the given tests had the given outcomes.</summary>
    public static TestRunResult Ran(params (string Name, TestOutcome Outcome)[] tests)
    {
        var start = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var results = tests
            .Select(test => new TestResult(
                test.Name,
                test.Name,
                test.Outcome,
                test.Outcome.ToString(),
                start,
                start,
                TimeSpan.FromMilliseconds(7),
                errorMessage: test.Outcome == TestOutcome.Failed ? "scripted failure" : null,
                stackTrace: null,
                standardOutput: null))
            .ToList();

        return new TestRunResult(results);
    }

    /// <summary>A run in which the investigated test passed, next to a sibling that also passed.</summary>
    public static TestRunResult TestPassed()
    {
        return Ran((FailingTest, TestOutcome.Passed), (SiblingTest, TestOutcome.Passed));
    }

    /// <summary>A run in which the investigated test failed.</summary>
    public static TestRunResult TestFailed()
    {
        return Ran((FailingTest, TestOutcome.Failed), (SiblingTest, TestOutcome.Passed));
    }

    /// <summary>A run that says nothing about the investigated test, for example a filter that matched nothing.</summary>
    public static TestRunResult TestAbsent()
    {
        return new TestRunResult(Array.Empty<TestResult>());
    }

    /// <summary>Queues the same result for the next <paramref name="times"/> runs.</summary>
    public static FakeTestRunner Queue(this FakeTestRunner runner, int times, TestRunResult result)
    {
        for (var i = 0; i < times; i++)
        {
            runner.Returns(result);
        }

        return runner;
    }

    /// <summary>Queues a result computed per run for the next <paramref name="times"/> runs.</summary>
    public static FakeTestRunner Queue(this FakeTestRunner runner, int times, Func<TestRunRequest, TestRunResult> result)
    {
        for (var i = 0; i < times; i++)
        {
            runner.Returns(result);
        }

        return runner;
    }
}
