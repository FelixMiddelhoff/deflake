namespace Culprit.Core.Trx;

/// <summary>What happened to one test, reduced to the cases the analysis cares about.</summary>
public enum TestOutcome
{
    /// <summary>The test ran and passed.</summary>
    Passed,

    /// <summary>The test ran and failed, errored or timed out.</summary>
    Failed,

    /// <summary>The test did not run: skipped, ignored, inconclusive or not runnable. Not a failure.</summary>
    NotRun,

    /// <summary>The test host was aborted or disconnected while the test ran, for example after a crash.</summary>
    Aborted,
}
