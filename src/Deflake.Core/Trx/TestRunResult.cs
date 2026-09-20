using System;
using System.Collections.Generic;
using System.Linq;

namespace Deflake.Core.Trx;

/// <summary>All test results of one run of <c>dotnet test</c>, in the order the tests started.</summary>
public sealed class TestRunResult
{
    public TestRunResult(IReadOnlyList<TestResult> tests)
    {
        Tests = tests;
    }

    /// <summary>Results ordered by start time; tests without a start time keep their file order.</summary>
    public IReadOnlyList<TestResult> Tests { get; }

    public IEnumerable<TestResult> Failures => Tests.Where(test => test.IsFailure);

    /// <summary>All results (theory cases) of the test with the given full name.</summary>
    public IEnumerable<TestResult> Named(string fullName)
    {
        return Tests.Where(test => string.Equals(test.FullName, fullName, StringComparison.Ordinal));
    }

    /// <summary>
    /// True when at least one case of the test passed and none failed or was aborted. A test that
    /// is absent or was not run is neither passed nor failed, so this is false for it.
    /// </summary>
    public bool Passed(string fullName)
    {
        var cases = Named(fullName).ToList();
        return cases.Any(test => test.Outcome == TestOutcome.Passed) && !Failed(fullName);
    }

    /// <summary>True when at least one case of the test failed or was aborted.</summary>
    public bool Failed(string fullName)
    {
        return Named(fullName).Any(test => test.Outcome == TestOutcome.Failed || test.Outcome == TestOutcome.Aborted);
    }
}
