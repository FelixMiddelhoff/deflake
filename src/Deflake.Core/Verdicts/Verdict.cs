using System;
using System.Collections.Generic;
using System.Linq;
using Deflake.Core.Execution;
using Deflake.Core.Experiments;

namespace Deflake.Core.Verdicts;

/// <summary>
/// Deflake's answer for one test: what it concluded, how sure it is, every row of evidence the
/// conclusion rests on, what was tested and ruled out, and a command that makes the failure happen
/// again. Immutable: the engine decides it, the verification step replaces it with a confirmed or a
/// downgraded copy, and nothing else may edit it.
/// </summary>
public sealed class Verdict
{
    private readonly double _confidenceScore;
    private readonly IReadOnlyList<VerdictEvidence> _evidence = Array.Empty<VerdictEvidence>();
    private readonly IReadOnlyList<string> _ruledOut = Array.Empty<string>();
    private readonly IReadOnlyList<string> _notTested = Array.Empty<string>();
    private readonly IReadOnlyList<string> _suspectTests = Array.Empty<string>();
    private readonly IReadOnlyList<string> _suspectMembers = Array.Empty<string>();
    private readonly IReadOnlyList<string> _notes = Array.Empty<string>();

    public Verdict(VerdictType type, TestIdentity test)
    {
        Type = type;
        Test = test ?? throw new ArgumentNullException(nameof(test));
    }

    private Verdict(Verdict source)
    {
        Type = source.Type;
        Test = source.Test;
        _confidenceScore = source._confidenceScore;
        _evidence = source._evidence;
        _ruledOut = source._ruledOut;
        _notTested = source._notTested;
        _suspectTests = source._suspectTests;
        _suspectMembers = source._suspectMembers;
        _notes = source._notes;
        ReproCommand = source.ReproCommand;
        ReproRequest = source.ReproRequest;
        ReproNote = source.ReproNote;
        ReproVerified = source.ReproVerified;
    }

    /// <summary>Which answer this is.</summary>
    public VerdictType Type { get; private init; }

    /// <summary>The test the answer is about.</summary>
    public TestIdentity Test { get; }

    /// <summary>
    /// How certain the answer is, from 0 to 1: how far apart the intervals were, how many runs stood
    /// behind them, and whether a message pattern agreed. It never turns weak evidence into a claim —
    /// a factor that is not statistically decisive produces <see cref="VerdictType.Inconclusive"/>,
    /// not a low-confidence guess.
    /// </summary>
    public double ConfidenceScore
    {
        get => _confidenceScore;
        init
        {
            if (value < 0 || value > 1)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "A confidence is between 0 and 1.");
            }

            _confidenceScore = value;
        }
    }

    /// <summary>One row per condition of every experiment that produced runs, in experiment order.</summary>
    public IReadOnlyList<VerdictEvidence> Evidence
    {
        get => _evidence;
        init => _evidence = CopyRows(value, nameof(Evidence));
    }

    /// <summary>Factors that were tested properly and made no difference.</summary>
    public IReadOnlyList<string> RuledOut
    {
        get => _ruledOut;
        init => _ruledOut = CopyNames(value, nameof(RuledOut));
    }

    /// <summary>
    /// Factors that were not tested, or whose experiment stopped short, with the reason. They are
    /// listed apart from <see cref="RuledOut"/> because "not measured" is not "made no difference".
    /// </summary>
    public IReadOnlyList<string> NotTested
    {
        get => _notTested;
        init => _notTested = CopyNames(value, nameof(NotTested));
    }

    /// <summary>Tests to look at: the minimal polluter set, or the tests that share the failing scope.</summary>
    public IReadOnlyList<string> SuspectTests
    {
        get => _suspectTests;
        init => _suspectTests = CopyNames(value, nameof(SuspectTests));
    }

    /// <summary>Members to look at, read off the failing stack trace with framework frames dropped.</summary>
    public IReadOnlyList<string> SuspectMembers
    {
        get => _suspectMembers;
        init => _suspectMembers = CopyNames(value, nameof(SuspectMembers));
    }

    /// <summary>Everything the reader needs that is not a number: what was odd, what was assumed.</summary>
    public IReadOnlyList<string> Notes
    {
        get => _notes;
        init => _notes = CopyNames(value, nameof(Notes));
    }

    /// <summary>
    /// A copy-and-paste command that reproduces the failure under the conditions that trigger it.
    /// Null when there is nothing honest to offer: <see cref="VerdictType.NotReproduced"/> has no
    /// failure to reproduce and <see cref="VerdictType.HostCrash"/> has no trustworthy run to repeat.
    /// </summary>
    public string? ReproCommand { get; private init; }

    /// <summary>
    /// The same reproduction as data, for <c>deflake repro</c> and for the verification step, which
    /// needs a request rather than a string.
    /// </summary>
    public TestRunRequest? ReproRequest { get; private init; }

    /// <summary>
    /// What the command alone cannot express, for example that the machine has to be under load.
    /// Kept out of <see cref="ReproCommand"/> so that stays pasteable into any shell.
    /// </summary>
    public string? ReproNote { get; private init; }

    /// <summary>
    /// True once the repro command was run again and the test failed again. A verdict with a command
    /// must not be presented as a diagnosis until this is true: an unverified repro is a guess.
    /// </summary>
    public bool ReproVerified { get; private init; }

    /// <summary>The reproduction, set as one piece so a command can never be stored without its request.</summary>
    public Verdict WithRepro(string command, TestRunRequest request, string? note = null)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            throw new ArgumentException("A repro command cannot be blank.", nameof(command));
        }

        return new Verdict(this)
        {
            ReproCommand = command,
            ReproRequest = request ?? throw new ArgumentNullException(nameof(request)),
            ReproNote = note,
        };
    }

    /// <summary>The same verdict, with the repro command confirmed to fail again.</summary>
    public Verdict Confirmed()
    {
        return new Verdict(this) { ReproVerified = true };
    }

    /// <summary>
    /// The verdict this one becomes when its evidence did not survive a check: a weaker claim, a lower
    /// confidence and a note saying why. The evidence table is kept — it was measured and is still true.
    /// </summary>
    public Verdict DowngradedTo(VerdictType type, double confidence, string note)
    {
        if (string.IsNullOrWhiteSpace(note))
        {
            throw new ArgumentException("A downgrade has to say why.", nameof(note));
        }

        return new Verdict(this)
        {
            Type = type,
            ConfidenceScore = confidence,
            ReproVerified = false,
            Notes = _notes.Append(note).ToArray(),
        };
    }

    public override string ToString()
    {
        return $"{Test.FullyQualifiedName}: {Type} ({ConfidenceScore:P0} confidence)";
    }

    private static IReadOnlyList<VerdictEvidence> CopyRows(IReadOnlyList<VerdictEvidence> rows, string name)
    {
        if (rows is null)
        {
            throw new ArgumentNullException(name);
        }

        var copy = rows.ToArray();
        if (copy.Any(row => row is null))
        {
            throw new ArgumentException("A verdict cannot carry a missing evidence row.", name);
        }

        return copy;
    }

    private static IReadOnlyList<string> CopyNames(IReadOnlyList<string> names, string name)
    {
        if (names is null)
        {
            throw new ArgumentNullException(name);
        }

        var copy = names.ToArray();
        if (copy.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("A verdict cannot carry a blank entry.", name);
        }

        return copy;
    }
}
