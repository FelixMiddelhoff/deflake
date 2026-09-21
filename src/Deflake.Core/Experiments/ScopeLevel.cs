namespace Deflake.Core.Experiments;

/// <summary>
/// How much company the failing test keeps in a run, from none to the whole assembly. The scope
/// ladder climbs these in order to find the smallest one in which the test still fails.
/// </summary>
public enum ScopeLevel
{
    /// <summary>Only the failing test itself.</summary>
    Alone,

    /// <summary>Every test of the failing test's class.</summary>
    Class,

    /// <summary>The scope the failure was originally seen in, when that was narrower than the whole assembly.</summary>
    Full,

    /// <summary>Every test in the assembly.</summary>
    Assembly,
}
