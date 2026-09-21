using System;
using System.Collections.Generic;
using Deflake.Core.Execution;
using Deflake.Core.Experiments;
using Deflake.Core.Heuristics;

namespace Deflake.Core.Verdicts;

/// <summary>
/// Everything the verdict engine is allowed to reason from: what the original failure looked like and
/// what each experiment measured. Every experiment is optional, because an investigation can run out
/// of budget, and a factor the OS cannot vary (time zone on Windows) is simply never measured — a
/// verdict is then built from what exists and says what it could not test.
/// </summary>
public sealed class VerdictInput
{
    /// <summary>The name the evidence table gives the isolation experiment, which varies no factor.</summary>
    public const string IsolationFactor = "Isolation";

    public VerdictInput(TestIdentity test)
    {
        Test = test ?? throw new ArgumentNullException(nameof(test));
    }

    /// <summary>The test under investigation.</summary>
    public TestIdentity Test { get; }

    /// <summary>
    /// The run the failure was first seen in. Repro commands are built from it when no experiment
    /// produced a better one, and its environment is the baseline the repro command's extra
    /// environment variables are measured against.
    /// </summary>
    public TestRunRequest? BaselineRequest { get; init; }

    /// <summary>The failing test run on its own.</summary>
    public ExperimentResult? Isolation { get; init; }

    /// <summary>Alone, class, original scope, assembly: the smallest scope the failure needs.</summary>
    public ExperimentResult? ScopeLadder { get; init; }

    /// <summary>The subsets ddmin measured while looking for the polluters.</summary>
    public ExperimentResult? Pollution { get; init; }

    /// <summary>The minimal polluter set itself, which is what names suspect tests.</summary>
    public PollutionResult? PollutionSearch { get; init; }

    /// <summary>Serial against parallel.</summary>
    public ExperimentResult? Parallelism { get; init; }

    /// <summary>Idle against a busy machine.</summary>
    public ExperimentResult? Load { get; init; }

    /// <summary>One row per culture.</summary>
    public ExperimentResult? Culture { get; init; }

    /// <summary>One row per time zone.</summary>
    public ExperimentResult? TimeZone { get; init; }

    /// <summary>What the message and stack patterns matched. Supporting evidence only.</summary>
    public HeuristicFindings? Heuristics { get; init; }

    /// <summary>
    /// True when the test host died during the original run — an <c>Aborted</c> outcome in the TRX, or
    /// blame data naming the test as the one in flight. Nothing measured around a crash is trustworthy,
    /// which is why this outranks every experiment.
    /// </summary>
    public bool HostAborted { get; init; }

    /// <summary>What the crash looked like, for the report.</summary>
    public string? HostCrashDetail { get; init; }

    /// <summary>The failure message from the original run, the heuristics' input.</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>The failure's stack trace, which names the members to look at.</summary>
    public string? StackTrace { get; init; }

    /// <summary>
    /// Every experiment that was handed in, paired with the factor name the evidence table uses, in
    /// the order an investigation runs them. Isolation varies nothing, so it names itself.
    /// </summary>
    public IEnumerable<(string Factor, ExperimentResult Result)> Experiments()
    {
        if (Isolation is not null)
        {
            yield return (IsolationFactor, Isolation);
        }

        if (ScopeLadder is not null)
        {
            yield return (ScopeLadderExperiment.ScopeFactor, ScopeLadder);
        }

        if (Pollution is not null)
        {
            yield return (PollutionExperiment.PolluterFactor, Pollution);
        }

        if (Parallelism is not null)
        {
            yield return (ParallelismExperiment.ParallelismFactor, Parallelism);
        }

        if (Load is not null)
        {
            yield return (LoadExperiment.LoadFactor, Load);
        }

        if (Culture is not null)
        {
            yield return (CultureExperiment.CultureFactor, Culture);
        }

        if (TimeZone is not null)
        {
            yield return (TimeZoneExperiment.TimeZoneFactor, TimeZone);
        }
    }
}
