using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Deflake.Core.Execution;
using Deflake.Core.Experiments;
using Deflake.Core.Heuristics;
using Deflake.Core.Statistics;

namespace Deflake.Core.Verdicts;

/// <summary>
/// Turns what the experiments measured into the one answer Deflake gives for a test. It is a pure
/// function of its input — no runs, no clock, no files — so the same evidence always produces the
/// same verdict and a verdict can be argued with by reading its rows.
/// </summary>
/// <remarks>
/// The rules are tried in order of how much a positive answer would tell a developer, and the first
/// one whose evidence holds wins. Every rule needs statistics: a factor is named only when the
/// intervals of two conditions do not overlap and both had enough runs. Message patterns never
/// decide, they only choose between two verdicts that the experiments already support
/// (<see cref="VerdictType.ResourceConflict"/> instead of the order or parallelism verdict) and nudge
/// the confidence.
/// </remarks>
public sealed class VerdictEngine
{
    /// <summary>
    /// Runs a condition needs before its rate may be compared with another. Below this, an interval
    /// is so wide that "they do not overlap" says more about the arithmetic than about the test.
    /// </summary>
    public const int MinimumRuns = 20;

    /// <summary>Confidence a statistically decisive factor starts from, before its own numbers improve it.</summary>
    private const double FactorBaseConfidence = 0.70;

    /// <summary>Confidence left to a claim that its own reproduction did not survive.</summary>
    private const double DowngradedConfidence = 0.25;

    /// <summary>Message patterns that name a machine-wide resource two tests can fight over.</summary>
    private static readonly string[] ResourcePatterns = { "AddressInUse", "FileInUse", "DatabaseLocked" };

    /// <summary>Message patterns that agree with "this is about timing".</summary>
    private static readonly string[] TimingPatterns =
    {
        "Timeout",
        "DateTimeAssertion",
        "ObjectDisposedException",
        "HttpRequestException",
    };

    /// <summary>The factors an investigation varies, in the order the evidence table lists them.</summary>
    private static readonly string[] Factors =
    {
        ScopeLadderExperiment.ScopeFactor,
        PollutionExperiment.PolluterFactor,
        ParallelismExperiment.ParallelismFactor,
        LoadExperiment.LoadFactor,
        CultureExperiment.CultureFactor,
        TimeZoneExperiment.TimeZoneFactor,
        RecordReplayExperiment.RecordReplayFactor,
    };

    /// <summary>The answer for one test, built from the experiments that completed.</summary>
    public Verdict Decide(VerdictInput input)
    {
        if (input is null)
        {
            throw new ArgumentNullException(nameof(input));
        }

        var ruledOut = RuledOutFactors(input).ToArray();
        var notTested = NotTestedFactors(input).ToArray();
        var candidate = Diagnose(input) ?? Unexplained(input, ruledOut.Length);

        var verdict = new Verdict(candidate.Type, input.Test)
        {
            ConfidenceScore = candidate.Confidence,
            Evidence = EvidenceTable(input),
            RuledOut = ruledOut.Where(factor => factor != candidate.DecisiveFactor).ToArray(),
            NotTested = notTested,
            SuspectTests = candidate.SuspectTests,
            SuspectMembers = StackTraceSuspects.From(input.StackTrace),
            Notes = candidate.Notes,
        };

        return candidate.ReproRequest is null
            ? verdict
            : verdict.WithRepro(
                ReproCommandBuilder.Build(candidate.ReproRequest, input.BaselineRequest?.EnvironmentVariables),
                candidate.ReproRequest,
                candidate.ReproNote);
    }

    /// <summary>
    /// The verdict after its repro command was run once more: confirmed when the test failed again,
    /// and otherwise downgraded to <see cref="VerdictType.Inconclusive"/>, because a diagnosis whose
    /// own reproduction does not reproduce is not a diagnosis. Verdicts without a command
    /// (<see cref="VerdictType.NotReproduced"/>, <see cref="VerdictType.HostCrash"/>) pass through
    /// untouched: there is nothing to check.
    /// </summary>
    public async Task<Verdict> VerifyReproAsync(
        Verdict verdict,
        IReproVerifier verifier,
        CancellationToken cancellationToken = default)
    {
        if (verdict is null)
        {
            throw new ArgumentNullException(nameof(verdict));
        }

        if (verifier is null)
        {
            throw new ArgumentNullException(nameof(verifier));
        }

        if (verdict.ReproCommand is null)
        {
            return verdict;
        }

        var reproduced = await verifier.VerifyAsync(verdict.Test, verdict, cancellationToken).ConfigureAwait(false);
        if (reproduced)
        {
            return verdict.Confirmed();
        }

        return verdict.DowngradedTo(
            VerdictType.Inconclusive,
            DowngradedConfidence,
            "repro verification failed: the command did not make the test fail again, "
            + $"so the {verdict.Type} claim is not supported.");
    }

    /// <summary>
    /// The rules in order of how much they would tell a developer. A crash invalidates every
    /// measurement around it, so it comes first; "broken" and "never failed" settle the whole
    /// question before any factor is worth discussing; the factor rules follow.
    /// </summary>
    private static Candidate? Diagnose(VerdictInput input)
    {
        return HostCrash(input)
            ?? ConsistentlyFails(input)
            ?? NotReproduced(input)
            ?? FirstDecisiveFactor(input);
    }

    private static Candidate? HostCrash(VerdictInput input)
    {
        if (!input.HostAborted)
        {
            return null;
        }

        var detail = input.HostCrashDetail ?? "The test host was aborted while this test was running.";
        return new Candidate(VerdictType.HostCrash, 0.90)
        {
            // No repro command: every run after a crash measured a process that was already dying,
            // so offering one would present unusable evidence as a reproduction.
            Notes = new[]
            {
                detail,
                "Nothing measured around a host crash is trustworthy; run with --blame to see which test was in flight.",
            },
        };
    }

    private static Candidate? ConsistentlyFails(VerdictInput input)
    {
        var alone = AloneRun(input);
        if (alone is null || alone.Measured < MinimumRuns || alone.Failures != alone.Measured)
        {
            return null;
        }

        // Every condition that was measured has to fail, everywhere: a single passing condition means
        // something changes the outcome, and that something is the verdict instead.
        var measured = MeasuredRuns(input).ToList();
        if (measured.Any(run => run.Failures != run.Measured))
        {
            return null;
        }

        return new Candidate(VerdictType.ConsistentlyFails, RunCountConfidence(0.90, alone.Measured))
        {
            ReproRequest = alone.Request,
            Notes = new[] { $"The test failed in all {alone.Measured} runs on its own, and in every other condition tested." },
        };
    }

    private static Candidate? NotReproduced(VerdictInput input)
    {
        var measured = MeasuredRuns(input).ToList();
        if (measured.Count == 0 || measured.Any(run => run.Failures > 0))
        {
            return null;
        }

        var runs = measured.Sum(run => run.Measured);
        if (runs < MinimumRuns)
        {
            return null;
        }

        var bound = FailureRate.UpperBoundAfterNoFailures(runs);
        return new Candidate(VerdictType.NotReproduced, Math.Clamp(1 - bound, 0.5, 0.95))
        {
            // No repro command: there is no failure to reproduce, and offering the original command
            // would suggest it does something it demonstrably did not do.
            Notes = new[]
            {
                $"The test did not fail once in {runs} runs across every condition tested. "
                + $"Its failure rate is below {bound:P1} (95 % confidence); it is not proven healthy, only not caught.",
            },
        };
    }

    /// <summary>
    /// The first factor whose conditions differ beyond chance. Order and parallelism are checked
    /// before the rest because they point at another test rather than at the machine, and a resource
    /// pattern in the message turns either of them into the sharper resource verdict.
    /// </summary>
    private static Candidate? FirstDecisiveFactor(VerdictInput input)
    {
        var order = OrderDependent(input);
        var parallelism = ParallelismDependent(input);
        var ranked = new[]
        {
            ResourceConflict(input, order ?? parallelism),
            order,
            parallelism,
            TimingSensitive(input),
            FlippingFactor(input, input.Culture, CultureExperiment.CultureFactor, VerdictType.CultureDependent),
            FlippingFactor(input, input.TimeZone, TimeZoneExperiment.TimeZoneFactor, VerdictType.TimeZoneDependent),
        }.Where(candidate => candidate is not null).Select(candidate => candidate!).ToList();

        if (ranked.Count == 0)
        {
            return null;
        }

        var winner = ranked[0];
        var others = ranked
            .Skip(1)
            .Select(candidate => candidate.DecisiveFactor)
            .Where(factor => factor is not null && factor != winner.DecisiveFactor)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (others.Count == 0)
        {
            return winner;
        }

        // More than one factor was decisive. The strongest signal is reported and the rest are named,
        // because a reader who knows a second factor also mattered reads the verdict differently.
        return winner.WithNote(
            $"{string.Join(", ", others)} also changed the outcome; {winner.DecisiveFactor} is reported as the stronger signal.");
    }

    private static Candidate? OrderDependent(VerdictInput input)
    {
        var alone = AloneRun(input);
        if (alone is null || alone.Measured < MinimumRuns || alone.Failures > 0)
        {
            return null;
        }

        var company = CompanyRuns(input)
            .Where(run => FailsMoreThan(run, alone))
            .OrderByDescending(run => run.Rate.Observed)
            .FirstOrDefault();

        if (company is null)
        {
            return null;
        }

        var polluters = input.PollutionSearch is { PolluterFound: true } search
            ? search.Polluters.ToArray()
            : Array.Empty<string>();

        // No heuristic weight here: which test pollutes is an experimental question, and a message
        // pattern that agrees would only be renaming this verdict ResourceConflict anyway.
        var candidate = new Candidate(VerdictType.OrderDependent, FactorConfidence(alone, company, 0))
        {
            DecisiveFactor = polluters.Length > 0
                ? PollutionExperiment.PolluterFactor
                : ScopeLadderExperiment.ScopeFactor,
            SuspectTests = polluters,
            ReproRequest = polluters.Length > 0 ? PolluterRequest(company.Request, input, polluters) : company.Request,
            Notes = new[]
            {
                $"The test passed in all {alone.Measured} runs on its own and failed "
                + $"{company.Failures} of {company.Measured} times with company ({company.FactorValue}).",
            },
        };

        if (polluters.Length > 0)
        {
            return candidate.WithNote($"Smallest set of other tests that brings the failure back: {string.Join(", ", polluters)}.");
        }

        // Which company is enough and which test is the culprit are two different questions, and only
        // the first one was answered here. Saying which was not asked keeps the two apart.
        return candidate.WithNote(input.PollutionSearch is null
            ? "No polluter search ran, so the culprit test is not named."
            : "The polluter search did not narrow the failure down to particular tests, so the culprit is not named.");
    }

    private static Candidate? ParallelismDependent(VerdictInput input)
    {
        var serial = input.Parallelism?.Run(ParallelismExperiment.Serial);
        var parallel = input.Parallelism?.Run(ParallelismExperiment.Parallel);
        if (serial is null || parallel is null || !FailsMoreThan(parallel, serial))
        {
            return null;
        }

        return new Candidate(VerdictType.ParallelismDependent, FactorConfidence(serial, parallel, 0))
        {
            DecisiveFactor = ParallelismExperiment.ParallelismFactor,
            ReproRequest = parallel.Request,
            ReproNote = "Parallel execution comes from a runsettings file Deflake generated; keep a copy of it to rerun this later.",
            Notes = new[]
            {
                $"Serial runs failed {serial.Failures} of {serial.Measured} times, parallel runs "
                + $"{parallel.Failures} of {parallel.Measured}.",
            },
        };
    }

    /// <summary>
    /// The same evidence as order or parallelism dependence, but the failure message names a port, a
    /// file or a database. The experiment still decides that something is shared; the pattern only
    /// says what, which is why this needs a decisive experiment underneath it.
    /// </summary>
    private static Candidate? ResourceConflict(VerdictInput input, Candidate? basis)
    {
        if (basis is null)
        {
            return null;
        }

        var pattern = MatchedPattern(input, ResourcePatterns);
        if (pattern is null)
        {
            return null;
        }

        return basis
            .As(VerdictType.ResourceConflict, Math.Min(1, basis.Confidence + 0.05))
            .WithNote($"The failure message matches the {pattern.PatternName} pattern: {pattern.Description}");
    }

    private static Candidate? TimingSensitive(VerdictInput input)
    {
        var idle = input.Load?.Run(LoadExperiment.Idle);
        var underLoad = input.Load?.Run(LoadExperiment.UnderLoad);
        if (idle is null || underLoad is null || !FailsMoreThan(underLoad, idle))
        {
            return null;
        }

        var pattern = MatchedPattern(input, TimingPatterns);
        var candidate = new Candidate(
            VerdictType.TimingSensitive,
            FactorConfidence(idle, underLoad, pattern?.Confidence ?? 0))
        {
            DecisiveFactor = LoadExperiment.LoadFactor,
            ReproRequest = underLoad.Request,
            ReproNote = "Reproduces only while the machine is busy: keep every core loaded for the duration of the run.",
            Notes = new[]
            {
                $"On an idle machine the test failed {idle.Failures} of {idle.Measured} times, under CPU load "
                + $"{underLoad.Failures} of {underLoad.Measured}.",
            },
        };

        return pattern is null
            ? candidate
            : candidate.WithNote($"The failure message matches the {pattern.PatternName} pattern: {pattern.Description}");
    }

    /// <summary>
    /// A factor whose conditions are a list of values rather than a pair — cultures, time zones. The
    /// claim is that the outcome flips, so the best and the worst condition are what get compared.
    /// </summary>
    private static Candidate? FlippingFactor(
        VerdictInput input,
        ExperimentResult? experiment,
        string factor,
        VerdictType type)
    {
        var comparable = experiment?.Runs.Where(run => run.Measured >= MinimumRuns).ToList();
        if (comparable is null || comparable.Count < 2)
        {
            return null;
        }

        var best = comparable.OrderBy(run => run.Rate.Observed).First();
        var worst = comparable.OrderByDescending(run => run.Rate.Observed).First();
        if (!FailsMoreThan(worst, best))
        {
            return null;
        }

        return new Candidate(type, FactorConfidence(best, worst, 0))
        {
            DecisiveFactor = factor,
            ReproRequest = worst.Request,
            Notes = new[]
            {
                $"With {factor} {worst.FactorValue} the test failed {worst.Failures} of {worst.Measured} times; "
                + $"with {best.FactorValue} it failed {best.Failures} of {best.Measured} times.",
            },
        };
    }

    /// <summary>
    /// What is left when the test did fail somewhere but no factor explained it. The more factors were
    /// properly tested and came back the same, the more this is a real answer rather than a shrug.
    /// </summary>
    private static Candidate Unexplained(VerdictInput input, int ruledOutCount)
    {
        var measured = MeasuredRuns(input).ToList();
        if (measured.Count == 0)
        {
            return new Candidate(VerdictType.Inconclusive, 0.1)
            {
                Notes = new[] { "No experiment produced a run that could be scored, so nothing was measured." },
            };
        }

        var worst = measured.OrderByDescending(run => run.Rate.Observed).First();
        var candidate = new Candidate(VerdictType.Inconclusive, Math.Min(0.60, 0.20 + (0.08 * ruledOutCount)))
        {
            ReproRequest = worst.Failures > 0 ? worst.Request : null,
            Notes = new[]
            {
                worst.Failures > 0
                    ? $"The test is flaky — it failed {worst.Failures} of {worst.Measured} times at its worst condition "
                      + $"({worst.FactorValue}) — but no factor tested changed the outcome beyond chance."
                    : $"The test failed in the original run but not in the {measured.Sum(run => run.Measured)} runs "
                      + "Deflake made, and no factor tested changed the outcome.",
            },
        };

        var pattern = input.Heuristics?.Results.FirstOrDefault();
        return pattern is null
            ? candidate

            // A pattern with no experiment behind it is a lead, never a verdict; it is printed as a
            // hint so the reader knows it was seen and knows it decided nothing.
            : candidate.WithNote(
                $"Hint only: the failure message matches the {pattern.PatternName} pattern ({pattern.Description}), "
                + "but no experiment confirmed it.");
    }

    /// <summary>
    /// True when the test failed more often in <paramref name="higher"/> than in
    /// <paramref name="lower"/> by more than chance: non-overlapping 95 % intervals, and enough runs
    /// on both sides for those intervals to mean anything.
    /// </summary>
    private static bool FailsMoreThan(ExperimentRun higher, ExperimentRun lower)
    {
        return higher.Measured >= MinimumRuns
            && lower.Measured >= MinimumRuns
            && higher.Rate.Observed > lower.Rate.Observed
            && higher.Rate.DiffersFrom(lower.Rate);
    }

    /// <summary>
    /// How sure a decisive factor makes us: the gap between the two intervals, how far past the
    /// minimum the run counts went, and — a little — whether a message pattern agreed.
    /// </summary>
    private static double FactorConfidence(ExperimentRun lower, ExperimentRun higher, double heuristicConfidence)
    {
        var gap = Math.Clamp((higher.Rate.Lower - lower.Rate.Upper) / 0.5, 0, 1);
        var runs = Math.Min(lower.Measured, higher.Measured);
        return Math.Clamp(
            FactorBaseConfidence
            + (0.15 * gap)
            + (0.10 * ExtraRunsWeight(runs))
            + (0.05 * Math.Clamp(heuristicConfidence, 0, 1)),
            0,
            1);
    }

    /// <summary>Confidence that grows with the runs behind an all-or-nothing observation.</summary>
    private static double RunCountConfidence(double baseConfidence, int runs)
    {
        return Math.Clamp(baseConfidence + ((1 - baseConfidence) * ExtraRunsWeight(runs)), 0, 1);
    }

    /// <summary>How much weight the runs past <see cref="MinimumRuns"/> are worth, from 0 to 1.</summary>
    private static double ExtraRunsWeight(int runs)
    {
        return Math.Clamp((runs - MinimumRuns) / 80.0, 0, 1);
    }

    /// <summary>The strongest matching pattern out of a family, or null when none of them matched.</summary>
    private static HeuristicResult? MatchedPattern(VerdictInput input, IReadOnlyCollection<string> patterns)
    {
        return input.Heuristics?.Results
            .Where(result => patterns.Contains(result.PatternName, StringComparer.Ordinal))
            .OrderByDescending(result => result.Confidence)
            .FirstOrDefault();
    }

    /// <summary>The failing test run on its own, from whichever experiment measured it.</summary>
    private static ExperimentRun? AloneRun(VerdictInput input)
    {
        return input.Isolation?.Run(IsolationExperiment.Alone)
            ?? input.ScopeLadder?.Run(ScopeLevel.Alone.ToString());
    }

    /// <summary>Every condition in which the test ran together with other tests.</summary>
    private static IEnumerable<ExperimentRun> CompanyRuns(VerdictInput input)
    {
        var ladder = input.ScopeLadder?.Runs
            .Where(run => run.FactorValue != ScopeLevel.Alone.ToString())
            ?? Enumerable.Empty<ExperimentRun>();

        return ladder.Concat(input.Pollution?.Runs ?? Enumerable.Empty<ExperimentRun>());
    }

    /// <summary>Every row of every experiment that produced at least one scoreable run.</summary>
    private static IEnumerable<ExperimentRun> MeasuredRuns(VerdictInput input)
    {
        return input.Experiments().SelectMany(experiment => experiment.Result.Runs).Where(run => run.Measured > 0);
    }

    /// <summary>
    /// One row per condition of every experiment. The pattern column is filled in on rows in which the
    /// test actually failed, because that is where the original failure message could apply.
    /// </summary>
    private static IReadOnlyList<VerdictEvidence> EvidenceTable(VerdictInput input)
    {
        var pattern = input.Heuristics?.Results.FirstOrDefault()?.PatternName;
        return input.Experiments()
            .SelectMany(experiment => experiment.Result.Runs.Select(
                run => VerdictEvidence.From(experiment.Factor, run, run.Failures > 0 ? pattern : null)))
            .ToArray();
    }

    /// <summary>
    /// The factors that were varied properly and changed nothing. "Properly" means at least two
    /// conditions with enough runs each: one condition proves nothing about a factor.
    /// </summary>
    private static IEnumerable<string> RuledOutFactors(VerdictInput input)
    {
        foreach (var (factor, result) in input.Experiments().Where(experiment => experiment.Factor != VerdictInput.IsolationFactor))
        {
            // The polluter search answers for itself below: its ddmin rows are probes on the way to
            // an answer, not conditions to compare.
            if (factor == PollutionExperiment.PolluterFactor && input.PollutionSearch is not null)
            {
                continue;
            }

            if (!result.Completed)
            {
                continue;
            }

            var comparable = result.Runs.Where(run => run.Measured >= MinimumRuns).ToList();
            if (comparable.Count < 2)
            {
                continue;
            }

            var best = comparable.OrderBy(run => run.Rate.Observed).First();
            var worst = comparable.OrderByDescending(run => run.Rate.Observed).First();
            if (!FailsMoreThan(worst, best))
            {
                yield return factor;
            }
        }

        // A search that ran to its end without finding a polluter has tested the factor: the failure
        // does not need any particular company.
        if (input.PollutionSearch is { Completed: true, PolluterFound: false })
        {
            yield return PollutionExperiment.PolluterFactor;
        }
    }

    /// <summary>
    /// What the verdict could not lean on: experiments that were never run, stopped short, or did not
    /// reach enough runs to compare. Kept apart from the ruled-out list so a gap in the evidence never
    /// reads as a finding.
    /// </summary>
    private static IEnumerable<string> NotTestedFactors(VerdictInput input)
    {
        var present = input.Experiments().ToDictionary(
            experiment => experiment.Factor,
            experiment => experiment.Result,
            StringComparer.Ordinal);

        foreach (var factor in new[] { VerdictInput.IsolationFactor }.Concat(Factors))
        {
            // A polluter search that ran is evidence even when it found nothing, and it reports its
            // own completion; its experiment rows would otherwise be judged as if they were conditions.
            if (factor == PollutionExperiment.PolluterFactor && input.PollutionSearch is { } search)
            {
                if (!search.Completed)
                {
                    yield return $"{factor}: {search.IncompleteReason}";
                }

                continue;
            }

            if (!present.TryGetValue(factor, out var result))
            {
                yield return $"{factor}: not tested";
                continue;
            }

            if (!result.Completed)
            {
                yield return $"{factor}: {result.IncompleteReason}";
                continue;
            }

            var needed = factor == VerdictInput.IsolationFactor ? 1 : 2;
            if (result.Runs.Count(run => run.Measured >= MinimumRuns) < needed)
            {
                yield return $"{factor}: fewer than {MinimumRuns} scoreable runs per condition, too few to compare";
            }
        }
    }

    /// <summary>
    /// The run that reproduces an order-dependent failure: the failing test plus exactly the polluters,
    /// nothing else. A polluter set too large for a command line falls back to the scope that was
    /// measured, which is longer to run but still real.
    /// </summary>
    private static TestRunRequest PolluterRequest(TestRunRequest scopeRequest, VerdictInput input, IEnumerable<string> polluters)
    {
        try
        {
            var filter = TestFilter.ForTests(polluters.Append(input.Test.FullyQualifiedName));
            return WithFilter(scopeRequest, filter);
        }
        catch (ArgumentException)
        {
            return scopeRequest;
        }
    }

    private static TestRunRequest WithFilter(TestRunRequest source, string filter)
    {
        return new TestRunRequest(source.TargetPath)
        {
            Filter = filter,
            RunSettingsPath = source.RunSettingsPath,
            EnvironmentVariables = source.EnvironmentVariables,
            Timeout = source.Timeout,
            WorkingDirectory = source.WorkingDirectory,
        };
    }

    /// <summary>A verdict the engine is considering, before it is dressed up with the evidence table.</summary>
    private sealed class Candidate
    {
        public Candidate(VerdictType type, double confidence)
        {
            Type = type;
            Confidence = confidence;
        }

        public VerdictType Type { get; }

        public double Confidence { get; }

        /// <summary>The factor that decided it, so it is not also listed as ruled out. Null when no factor did.</summary>
        public string? DecisiveFactor { get; init; }

        public IReadOnlyList<string> SuspectTests { get; init; } = Array.Empty<string>();

        public IReadOnlyList<string> Notes { get; init; } = Array.Empty<string>();

        public TestRunRequest? ReproRequest { get; init; }

        public string? ReproNote { get; init; }

        /// <summary>The same evidence, read as a different (sharper) verdict.</summary>
        public Candidate As(VerdictType type, double confidence)
        {
            return new Candidate(type, confidence)
            {
                DecisiveFactor = DecisiveFactor,
                SuspectTests = SuspectTests,
                Notes = Notes,
                ReproRequest = ReproRequest,
                ReproNote = ReproNote,
            };
        }

        public Candidate WithNote(string note)
        {
            return new Candidate(Type, Confidence)
            {
                DecisiveFactor = DecisiveFactor,
                SuspectTests = SuspectTests,
                Notes = Notes.Append(note).ToArray(),
                ReproRequest = ReproRequest,
                ReproNote = ReproNote,
            };
        }
    }
}
