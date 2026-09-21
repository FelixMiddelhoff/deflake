using System;

namespace Deflake.Core.Experiments;

/// <summary>
/// The test under investigation, named at the three levels a run can be scoped to: the test method,
/// the class that holds it, and the assembly that holds the class. Every case of a theory shares one
/// <see cref="FullyQualifiedName"/>, which is also the level a <c>--filter</c> selects at, so that is
/// the level Deflake investigates.
/// </summary>
public sealed class TestIdentity
{
    /// <param name="fullyQualifiedName">The name a filter selects by, for example <c>Shop.OrderTests.Total_is_summed</c>.</param>
    /// <param name="assemblyName">The test assembly the test lives in; it distinguishes tests of the same name in different projects.</param>
    /// <param name="className">The class, when it cannot be read off the name (a name without a dot, or an unusual naming scheme). Null derives it.</param>
    public TestIdentity(string fullyQualifiedName, string assemblyName, string? className = null)
    {
        FullyQualifiedName = Required(fullyQualifiedName, nameof(fullyQualifiedName));
        AssemblyName = Required(assemblyName, nameof(assemblyName));
        ClassName = className is null
            ? ClassOf(FullyQualifiedName)
            : Required(className, nameof(className));
    }

    /// <summary>Class and method, for example <c>Shop.OrderTests.Total_is_summed</c>.</summary>
    public string FullyQualifiedName { get; }

    /// <summary>The declaring class, for example <c>Shop.OrderTests</c>; the scope ladder's class step runs it.</summary>
    public string ClassName { get; }

    /// <summary>The test assembly, for example <c>Shop.Tests.dll</c>.</summary>
    public string AssemblyName { get; }

    public override string ToString()
    {
        return $"{FullyQualifiedName} ({AssemblyName})";
    }

    /// <summary>
    /// Everything before the last dot. A nested class arrives as <c>Shop.OrderTests+Sums.Total</c>,
    /// where the last dot still separates the method, so the same rule gives <c>Shop.OrderTests+Sums</c>.
    /// </summary>
    private static string ClassOf(string fullyQualifiedName)
    {
        var lastDot = fullyQualifiedName.LastIndexOf('.');
        if (lastDot <= 0 || lastDot == fullyQualifiedName.Length - 1)
        {
            throw new ArgumentException(
                $"'{fullyQualifiedName}' does not look like a fully qualified test name ('Class.Method' at least). Pass the class name explicitly.",
                nameof(fullyQualifiedName));
        }

        return fullyQualifiedName.Substring(0, lastDot);
    }

    private static string Required(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A name is required.", parameterName);
        }

        return value;
    }
}
