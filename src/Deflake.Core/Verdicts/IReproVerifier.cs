using System.Threading;
using System.Threading.Tasks;
using Deflake.Core.Experiments;

namespace Deflake.Core.Verdicts;

/// <summary>
/// Runs a verdict's repro command once more and says whether the test failed again. Kept as a
/// contract so the verdict engine itself stays a pure function of its inputs: the engine decides,
/// something that owns a process runs the check.
/// </summary>
public interface IReproVerifier
{
    /// <summary>
    /// True when the test failed again under the verdict's reproduction. False means the claim does
    /// not hold up and must be downgraded — including when the run could not be scored at all,
    /// because an unverifiable repro is not a verified one.
    /// </summary>
    Task<bool> VerifyAsync(TestIdentity test, Verdict verdict, CancellationToken cancellationToken);
}
