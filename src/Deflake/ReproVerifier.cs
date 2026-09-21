using System;
using System.Threading;
using System.Threading.Tasks;
using Deflake.Core.Execution;
using Deflake.Core.Experiments;
using Deflake.Core.Verdicts;

namespace Deflake;

internal sealed class ReproVerifier : IReproVerifier
{
    private readonly DotnetTestRunner _runner;

    public ReproVerifier(DotnetTestRunner runner)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
    }

    public async Task<bool> VerifyAsync(TestIdentity test, Verdict verdict, CancellationToken cancellationToken)
    {
        if (test is null)
        {
            throw new ArgumentNullException(nameof(test));
        }

        if (verdict is null)
        {
            throw new ArgumentNullException(nameof(verdict));
        }

        if (verdict.ReproRequest is null)
        {
            return false;
        }

        try
        {
            var result = await _runner.RunAsync(verdict.ReproRequest, cancellationToken).ConfigureAwait(false);
            return result.Failed(test.FullyQualifiedName);
        }
        catch (TestRunTimeoutException)
        {
            return false;
        }
        catch (TestRunExecutionException)
        {
            return false;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
    }
}
