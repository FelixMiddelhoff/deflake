using System.Collections.Generic;

namespace Deflake.Core.Search;

/// <summary>The outcome of a delta debugging search.</summary>
public sealed class DeltaDebuggingResult<T>
{
    public DeltaDebuggingResult(IReadOnlyList<T> minimal, bool inputFails, bool completed, int testsRun)
    {
        Minimal = minimal;
        InputFails = inputFails;
        Completed = completed;
        TestsRun = testsRun;
    }

    /// <summary>
    /// The smallest failing subset found, in the original order. When <see cref="InputFails"/> is
    /// false there is nothing to minimize and this is the unchanged input.
    /// </summary>
    public IReadOnlyList<T> Minimal { get; }

    /// <summary>False when the full input did not fail, so there is nothing to explain.</summary>
    public bool InputFails { get; }

    /// <summary>
    /// True when the search ran to the end: <see cref="Minimal"/> is then 1-minimal. False when the
    /// budget ran out first, in which case <see cref="Minimal"/> still fails but may be reducible.
    /// </summary>
    public bool Completed { get; }

    /// <summary>How many experiments were actually run (repeated sets are remembered, not rerun).</summary>
    public int TestsRun { get; }
}
