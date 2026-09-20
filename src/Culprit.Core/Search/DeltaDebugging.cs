using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Culprit.Core.Search;

/// <summary>
/// Delta debugging (Zeller's <c>ddmin</c>): finds a small subset of <c>items</c> that still makes a
/// test fail. Culprit uses it to find which tests that ran before a test make it fail.
/// </summary>
/// <remarks>
/// Subsets keep the original order of the items, which matters because test order matters.
/// The search is deterministic for a deterministic predicate. With a flaky predicate the answer is
/// only as good as the predicate; callers make the predicate robust by repeating runs.
/// </remarks>
public static class DeltaDebugging
{
    /// <summary>
    /// Shrinks <paramref name="items"/> to a subset for which <paramref name="fails"/> is still true.
    /// When the search completes the result is 1-minimal: removing any single item makes it pass.
    /// </summary>
    /// <param name="items">The full set, in order.</param>
    /// <param name="fails">Runs the experiment on a subset and says whether the failure appeared.</param>
    /// <param name="maxTests">Most calls to <paramref name="fails"/> allowed; the best set found so far is returned when it runs out.</param>
    /// <param name="cancellation">Checked before every experiment.</param>
    public static DeltaDebuggingResult<T> Minimize<T>(
        IReadOnlyList<T> items,
        Func<IReadOnlyList<T>, bool> fails,
        int maxTests = int.MaxValue,
        CancellationToken cancellation = default)
    {
        if (items is null)
        {
            throw new ArgumentNullException(nameof(items));
        }

        if (fails is null)
        {
            throw new ArgumentNullException(nameof(fails));
        }

        if (maxTests < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxTests), "At least one experiment must be allowed.");
        }

        var search = new Search<T>(items, fails, maxTests, cancellation);
        return search.Run();
    }

    private sealed class Search<T>
    {
        private readonly IReadOnlyList<T> _items;
        private readonly Func<IReadOnlyList<T>, bool> _fails;
        private readonly int _maxTests;
        private readonly CancellationToken _cancellation;
        private readonly Dictionary<string, bool> _known = new Dictionary<string, bool>();
        private int _testsRun;

        public Search(IReadOnlyList<T> items, Func<IReadOnlyList<T>, bool> fails, int maxTests, CancellationToken cancellation)
        {
            _items = items;
            _fails = fails;
            _maxTests = maxTests;
            _cancellation = cancellation;
        }

        public DeltaDebuggingResult<T> Run()
        {
            var all = Enumerable.Range(0, _items.Count).ToList();

            // The first experiment always fits in the budget: the budget is at least one.
            TryFails(all, out var inputFails);
            if (!inputFails)
            {
                return Result(all, inputFails: false, completed: true);
            }

            // The failure may not need any of the items at all.
            if (TryFails(new List<int>(), out var emptyFails) && emptyFails)
            {
                return Result(new List<int>(), inputFails: true, completed: true);
            }

            var current = all;
            var granularity = 2;
            while (current.Count >= 2)
            {
                var chunks = Split(current, granularity);

                if (TryReduce(chunks, chunk => chunk, out var reduced, out var outOfBudget))
                {
                    current = reduced;
                    granularity = 2;
                    continue;
                }

                if (outOfBudget)
                {
                    return Result(current, inputFails: true, completed: false);
                }

                if (TryReduce(chunks, chunk => current.Except(chunk).ToList(), out reduced, out outOfBudget))
                {
                    current = reduced;
                    granularity = Math.Max(granularity - 1, 2);
                    continue;
                }

                if (outOfBudget)
                {
                    return Result(current, inputFails: true, completed: false);
                }

                if (granularity >= current.Count)
                {
                    break;
                }

                granularity = Math.Min(current.Count, granularity * 2);
            }

            // A single remaining item was proven necessary by the empty-set experiment above.
            return Result(current, inputFails: true, completed: true);
        }

        /// <summary>Tries each candidate the chooser derives from a chunk and takes the first that still fails.</summary>
        private bool TryReduce(
            List<List<int>> chunks,
            Func<List<int>, List<int>> chooseCandidate,
            out List<int> reduced,
            out bool outOfBudget)
        {
            foreach (var chunk in chunks)
            {
                var candidate = chooseCandidate(chunk);
                if (!TryFails(candidate, out var fails))
                {
                    reduced = new List<int>();
                    outOfBudget = true;
                    return false;
                }

                if (fails)
                {
                    reduced = candidate;
                    outOfBudget = false;
                    return true;
                }
            }

            reduced = new List<int>();
            outOfBudget = false;
            return false;
        }

        /// <summary>Runs (or remembers) one experiment. False means the budget is spent and no answer exists.</summary>
        private bool TryFails(List<int> indices, out bool fails)
        {
            var key = string.Join(",", indices);
            if (_known.TryGetValue(key, out fails))
            {
                return true;
            }

            _cancellation.ThrowIfCancellationRequested();
            if (_testsRun >= _maxTests)
            {
                fails = false;
                return false;
            }

            _testsRun++;
            fails = _fails(indices.Select(index => _items[index]).ToList());
            _known[key] = fails;
            return true;
        }

        /// <summary>Cuts a list into the given number of consecutive, nearly equal chunks.</summary>
        private static List<List<int>> Split(List<int> indices, int chunkCount)
        {
            var chunks = new List<List<int>>();
            var start = 0;
            for (var chunk = 0; chunk < chunkCount; chunk++)
            {
                var end = start + ((indices.Count - start) / (chunkCount - chunk));
                chunks.Add(indices.GetRange(start, end - start));
                start = end;
            }

            return chunks.Where(chunk => chunk.Count > 0).ToList();
        }

        private DeltaDebuggingResult<T> Result(List<int> indices, bool inputFails, bool completed)
        {
            return new DeltaDebuggingResult<T>(indices.Select(index => _items[index]).ToList(), inputFails, completed, _testsRun);
        }
    }
}
