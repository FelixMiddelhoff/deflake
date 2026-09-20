using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Deflake.Core.Execution;

/// <summary>
/// Builds the <c>--filter</c> expression that makes <c>dotnet test</c> run exactly the given tests.
/// A test is selected by its full name (<c>Namespace.Class.Method</c>), which for xUnit and NUnit
/// selects every case of a theory too. The exact match (<c>=</c>) is used, never "contains",
/// so <c>Cases</c> never also selects <c>CasesMore</c>.
/// </summary>
public static class TestFilter
{
    /// <summary>
    /// Longest filter <see cref="ForTests"/> builds by default. Windows limits a whole command line to
    /// 32,767 characters and the rest of the command needs room.
    /// </summary>
    public const int DefaultMaxLength = 24000;

    // The characters that mean something in the filter syntax, and so must be escaped in a name.
    private static readonly char[] Special = { '\\', '(', ')', '&', '|', '=', '!', '~' };

    /// <summary>The filter that selects one test.</summary>
    public static string ForTest(string fullName)
    {
        return "FullyQualifiedName=" + Escape(fullName);
    }

    /// <summary>
    /// The filter that selects all the given tests. Duplicates are removed. An empty list is refused,
    /// because an empty filter would silently run the whole suite.
    /// </summary>
    /// <exception cref="ArgumentException">A name is empty or contains control characters, the list is empty, or the filter would be longer than <paramref name="maxLength"/>.</exception>
    public static string ForTests(IEnumerable<string> fullNames, int maxLength = DefaultMaxLength)
    {
        if (fullNames is null)
        {
            throw new ArgumentNullException(nameof(fullNames));
        }

        var names = fullNames.Distinct(StringComparer.Ordinal).ToList();
        if (names.Count == 0)
        {
            throw new ArgumentException("At least one test is needed: an empty filter would run every test.", nameof(fullNames));
        }

        var filter = string.Join("|", names.Select(ForTest));
        if (filter.Length > maxLength)
        {
            throw new ArgumentException(
                $"The filter for {names.Count} tests is {filter.Length} characters long, more than the {maxLength} allowed on a command line.",
                nameof(fullNames));
        }

        return filter;
    }

    /// <summary>Puts a backslash before every character that is special in the filter syntax.</summary>
    /// <exception cref="ArgumentException">The name is empty or contains a control character.</exception>
    public static string Escape(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("A test name cannot be empty.", nameof(name));
        }

        if (name.Any(char.IsControl))
        {
            throw new ArgumentException("A test name cannot contain control characters.", nameof(name));
        }

        var escaped = new StringBuilder(name.Length + 4);
        foreach (var character in name)
        {
            if (Array.IndexOf(Special, character) >= 0)
            {
                escaped.Append('\\');
            }

            escaped.Append(character);
        }

        return escaped.ToString();
    }
}
