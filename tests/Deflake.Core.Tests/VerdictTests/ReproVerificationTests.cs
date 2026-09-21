using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Deflake.Core.Experiments;
using Deflake.Core.Verdicts;
using Xunit;
using static Deflake.Core.Tests.VerdictTestSupport;

namespace Deflake.Core.Tests;

/// <summary>
/// The gate in front of every printed diagnosis: the repro command is run once more, and a claim
/// whose own reproduction does not reproduce is withdrawn rather than printed with a caveat.
/// </summary>
public sealed class ReproVerificationTests
{
    private readonly VerdictEngine _engine = new();

    [Fact]
    public async Task A_repro_that_fails_again_confirms_the_verdict()
    {
        var verdict = OrderDependentVerdict();
        var verifier = new FakeReproVerifier(reproduced: true);

        var verified = await _engine.VerifyReproAsync(verdict, verifier);

        Assert.True(verified.ReproVerified);
        Assert.Equal(VerdictType.OrderDependent, verified.Type);
        Assert.Equal(verdict.ConfidenceScore, verified.ConfidenceScore);
        Assert.Equal(1, verifier.Calls);
    }

    [Fact]
    public async Task A_repro_that_passes_downgrades_the_verdict_to_inconclusive()
    {
        var verdict = OrderDependentVerdict();

        var verified = await _engine.VerifyReproAsync(verdict, new FakeReproVerifier(reproduced: false));

        Assert.Equal(VerdictType.Inconclusive, verified.Type);
        Assert.False(verified.ReproVerified);
        Assert.Contains(verified.Notes, note => note.Contains("repro verification failed"));
        Assert.True(verified.ConfidenceScore < verdict.ConfidenceScore);
    }

    [Fact]
    public async Task A_downgraded_verdict_says_which_claim_did_not_hold_up()
    {
        var verdict = OrderDependentVerdict();

        var verified = await _engine.VerifyReproAsync(verdict, new FakeReproVerifier(reproduced: false));

        Assert.Contains(verified.Notes, note => note.Contains(nameof(VerdictType.OrderDependent)));
    }

    [Fact]
    public async Task A_downgraded_verdict_keeps_the_evidence_that_was_measured()
    {
        var verdict = OrderDependentVerdict();

        var verified = await _engine.VerifyReproAsync(verdict, new FakeReproVerifier(reproduced: false));

        Assert.Equal(verdict.Evidence.Count, verified.Evidence.Count);
        Assert.Equal(verdict.Evidence.Select(row => row.ToString()), verified.Evidence.Select(row => row.ToString()));
    }

    [Fact]
    public async Task A_verdict_without_a_repro_command_is_left_alone()
    {
        var notReproduced = _engine.Decide(Input(
            isolation: Isolation(failures: 0),
            scope: ScopeLadder((ScopeLevel.Alone, 0), (ScopeLevel.Class, 0))));
        var verifier = new FakeReproVerifier(reproduced: false);

        var verified = await _engine.VerifyReproAsync(notReproduced, verifier);

        Assert.Equal(VerdictType.NotReproduced, verified.Type);
        Assert.False(verified.ReproVerified);
        Assert.Equal(0, verifier.Calls);
    }

    [Fact]
    public async Task A_host_crash_is_not_sent_through_the_repro_gate()
    {
        var crash = _engine.Decide(Input(isolation: Isolation(failures: 3), hostAborted: true));
        var verifier = new FakeReproVerifier(reproduced: false);

        var verified = await _engine.VerifyReproAsync(crash, verifier);

        Assert.Equal(VerdictType.HostCrash, verified.Type);
        Assert.Equal(0, verifier.Calls);
    }

    [Fact]
    public void A_freshly_decided_verdict_is_never_already_verified()
    {
        var verdict = OrderDependentVerdict();

        Assert.False(verdict.ReproVerified);
        Assert.NotNull(verdict.ReproCommand);
    }

    [Fact]
    public async Task The_verification_passes_the_cancellation_token_on()
    {
        using var cancellation = new CancellationTokenSource();
        var verifier = new CancellationWatchingVerifier();

        await _engine.VerifyReproAsync(OrderDependentVerdict(), verifier, cancellation.Token);

        Assert.Equal(cancellation.Token, verifier.Seen);
    }

    [Fact]
    public async Task Verification_refuses_arguments_that_are_not_there()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _engine.VerifyReproAsync(null!, new FakeReproVerifier(true)));
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _engine.VerifyReproAsync(OrderDependentVerdict(), null!));
    }

    private Verdict OrderDependentVerdict()
    {
        return _engine.Decide(Input(
            isolation: Isolation(failures: 0),
            scope: ScopeLadder((ScopeLevel.Alone, 0), (ScopeLevel.Class, 18)),
            polluters: Polluters(ExperimentTestSupport.SiblingTest)));
    }

    private sealed class CancellationWatchingVerifier : IReproVerifier
    {
        public CancellationToken Seen { get; private set; }

        public Task<bool> VerifyAsync(TestIdentity test, Verdict verdict, CancellationToken cancellationToken)
        {
            Seen = cancellationToken;
            return Task.FromResult(true);
        }
    }
}
