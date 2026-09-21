namespace Deflake.Core.Verdicts;

/// <summary>
/// The answers Deflake is allowed to give. Each one names a different thing to go and change, so a
/// wrong one costs a developer a day; <see cref="Inconclusive"/> and <see cref="NotReproduced"/> are
/// therefore real answers, not failures of the tool.
/// </summary>
public enum VerdictType
{
    /// <summary>
    /// Flaky, but every factor Deflake can vary was varied and none of them changed the outcome.
    /// The default: nothing else is claimed until the evidence forces it.
    /// </summary>
    Inconclusive,

    /// <summary>Not flaky at all: the test fails at every scope, in every run. It is broken.</summary>
    ConsistentlyFails,

    /// <summary>
    /// It never failed once while Deflake watched. The verdict carries the upper bound on how flaky
    /// it can still be (rule of three), because "did not see it" is not "it is fine".
    /// </summary>
    NotReproduced,

    /// <summary>Passes alone, fails once a minimal set of other tests runs before it.</summary>
    OrderDependent,

    /// <summary>Passes run serially, fails at a significantly higher rate when tests run in parallel.</summary>
    ParallelismDependent,

    /// <summary>Fails significantly more often while the machine is busy: a race or a too-tight timeout.</summary>
    TimingSensitive,

    /// <summary>The outcome flips with the culture the test runs under: formatting, parsing or sorting.</summary>
    CultureDependent,

    /// <summary>The outcome flips with the process time zone.</summary>
    TimeZoneDependent,

    /// <summary>The test host died (stack overflow, native crash, <c>Environment.Exit</c>), so no run is trustworthy.</summary>
    HostCrash,

    /// <summary>
    /// Two tests want the same machine-wide thing — a port, a file, a database. Order or parallelism
    /// changes the outcome and the failure message names the resource.
    /// </summary>
    ResourceConflict,
}
