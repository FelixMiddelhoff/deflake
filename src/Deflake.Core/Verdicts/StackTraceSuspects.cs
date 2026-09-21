using System;
using System.Collections.Generic;
using System.Linq;

namespace Deflake.Core.Verdicts;

/// <summary>
/// Reads the members worth looking at off a stack trace. A stack is mostly framework: only the frames
/// belonging to the code under test tell a developer where to go, so everything else is dropped.
/// </summary>
public static class StackTraceSuspects
{
    /// <summary>How many members are worth printing; further down a stack is rarely the cause.</summary>
    public const int MaxSuspects = 5;

    /// <summary>Frames from these namespaces are the test runner and the BCL, never the suspect.</summary>
    private static readonly string[] FrameworkPrefixes =
    {
        "System.",
        "Microsoft.",
        "Xunit.",
        "NUnit.",
        "MSTest.",
        "Castle.",
        "Moq.",
        "FluentAssertions.",
        "Shouldly.",
        "JetBrains.",
    };

    /// <summary>
    /// The first few non-framework members named in the stack, outermost first, without duplicates.
    /// An empty stack, or one that is all framework, yields nothing rather than a guess.
    /// </summary>
    public static IReadOnlyList<string> From(string? stackTrace)
    {
        if (string.IsNullOrWhiteSpace(stackTrace))
        {
            return Array.Empty<string>();
        }

        return stackTrace!
            .Split('\n')
            .Select(MemberOf)
            .Where(member => member is not null)
            .Select(member => member!)
            .Where(member => !IsFramework(member))
            .Distinct(StringComparer.Ordinal)
            .Take(MaxSuspects)
            .ToArray();
    }

    /// <summary>
    /// The member in one frame, or null when the line is not a frame. A frame reads
    /// <c>   at Shop.OrderService.Total(Order order) in C:\…:line 42</c>; everything between "at " and
    /// the argument list is the member.
    /// </summary>
    private static string? MemberOf(string line)
    {
        var trimmed = line.Trim();
        if (!trimmed.StartsWith("at ", StringComparison.Ordinal))
        {
            return null;
        }

        var member = trimmed.Substring(3);

        var arguments = member.IndexOf('(');
        if (arguments > 0)
        {
            member = member.Substring(0, arguments);
        }

        // A lambda or local function frame reads "Shop.OrderService.Total>b__0"; the member that owns
        // it is what a developer opens, so the generated part is cut away.
        var generated = member.IndexOf('>');
        if (generated > 0)
        {
            member = member.Substring(0, generated);
        }

        member = member.Replace("<", string.Empty).Trim();
        return string.IsNullOrWhiteSpace(member) ? null : member;
    }

    private static bool IsFramework(string member)
    {
        return FrameworkPrefixes.Any(prefix => member.StartsWith(prefix, StringComparison.Ordinal));
    }
}
