# Deflake.Runtime Guide: Deterministic Recording and Replay

**Deflake.Runtime** (opt-in) records and replays `TimeProvider` and `Random` calls to make timing-dependent flakes reproducible and debuggable. Use this guide to instrument your tests for deterministic execution.

---

## Overview

### Problem
Timing-sensitive tests fail intermittently because `DateTime.UtcNow`, `Random`, or task scheduling varies between runs. Reproducing the exact failure is hard.

### Solution
Deflake.Runtime records all calls to `TimeProvider` and `Random` during a failing run, then replays them exactly on demand. This lets you:
- **Deterministically reproduce** a failure every time
- **Debug in isolation** with the exact sequence of events
- **Verify the fix** by replaying the original failing scenario

### What Gets Recorded
- ✅ `TimeProvider`: all calls to `UtcNow`, `Today`, `LocalNow`, `GetTimestamp()`, etc.
- ✅ `Random`: every call to `Next()`, `NextDouble()`, `NextBytes()`, etc.
- ✅ Call ordering and timing via sequence counters (deterministic, not wall-clock)
- ❌ Task scheduling: recorded for diagnostics, not replayed (v1 scope)

---

## Getting Started

### 1. Add Deflake.Runtime to Your Test Project

```bash
dotnet add package Deflake.Runtime
```

**Supported frameworks:** `.NET 6.0+`

**Zero overhead:** When not recording/replaying, `DeflakeRecorder` transparently passes through to `TimeProvider.System` and `Random.Shared` — no performance cost.

### 2. Wire Up in Test Code

Replace your test's direct `DateTime.UtcNow` or `Random` with `DeflakeRecorder`:

#### Before (timing-sensitive test)
```csharp
[Fact]
public async Task OrderPlacedWithinDeadlineSucceeds()
{
    var deadline = DateTime.UtcNow.AddSeconds(5);
    var order = new Order { Id = 1, CreatedAt = DateTime.UtcNow };
    
    // Test waits for order to be processed
    await Task.Delay(100);
    
    var processed = DateTime.UtcNow < deadline;
    Assert.True(processed);
}
```

**Problem:** Fails randomly when system is slow.

#### After (deterministic test)
```csharp
[Fact]
public async Task OrderPlacedWithinDeadlineSucceeds()
{
    // Use DeflakeRecorder instead of DateTime.UtcNow
    var timeProvider = DeflakeRecorder.TimeProvider;
    var deadline = timeProvider.GetUtcNow().AddSeconds(5);
    var order = new Order { Id = 1, CreatedAt = timeProvider.GetUtcNow() };
    
    await Task.Delay(100);
    
    var processed = timeProvider.GetUtcNow() < deadline;
    Assert.True(processed);
}
```

---

## Recording and Replaying

### Recording: Capture a Failing Run

Use the `--record-replay` CLI flag when running tests:

```bash
deflake investigate MyProject.csproj --test OrderTests.OrderPlacedWithinDeadlineSucceeds --record-replay --runs 30
```

**What happens:**
1. Deflake runs the test 30 times
2. Each run: `DEFLAKE_RECORD=<report>/runtime-sessions/<test>.json` env var is set
3. `DeflakeRecorder` records all `TimeProvider` and `Random` calls to JSON
4. The failing run's session file is captured
5. Report includes path to session: `../runtime-sessions/OrderTests.OrderPlacedWithinDeadlineSucceeds.json`

**Session File Format (JSON):**
```json
{
  "testFqn": "OrderTests.OrderPlacedWithinDeadlineSucceeds",
  "recordedAt": "2026-09-22T14:30:00Z",
  "timestampFrequency": 10000000,
  "utcOffsetSeconds": 3600,
  "streams": {
    "TimeProvider": [
      { "callSite": "GetUtcNow", "arguments": {}, "returnValue": "2026-09-22T13:30:00Z", "sequenceNumber": 1 },
      { "callSite": "GetUtcNow", "arguments": {}, "returnValue": "2026-09-22T13:30:00.1Z", "sequenceNumber": 2 }
    ],
    "Random:main": [
      { "callSite": "Next", "arguments": { "maxValue": 100 }, "returnValue": 42, "sequenceNumber": 1 }
    ]
  }
}
```

### Replaying: Re-run with Exact State

Once a session is captured, replay it with:

```bash
deflake repro <report.json> --replay <path-to-session.json>
```

**What happens:**
1. Session file is loaded
2. `DEFLAKE_REPLAY=<path-to-session.json>` env var is set
3. Test runs with recorded `TimeProvider` and `Random` values
4. Every `GetUtcNow()` returns the recorded value from the session
5. Every `Random.Next()` returns the recorded value from the session
6. If test diverges (calls something that wasn't recorded), `DeflakeReplayDivergenceException` is thrown

**Output:**
```
Test: OrderTests.OrderPlacedWithinDeadlineSucceeds
Session: ../runtime-sessions/OrderTests.OrderPlacedWithinDeadlineSucceeds.json
Status: REPLAYED (matched recorded behavior)
Verdict: TimingSensitive
Confidence: 0.92
```

---

## Practical Examples

### Example 1: Timing-Sensitive Test with TimeProvider

```csharp
using Deflake.Runtime;
using Xunit;

public class PaymentProcessorTests
{
    [Fact]
    public async Task ProcessPaymentWithinSLA()
    {
        var timeProvider = DeflakeRecorder.TimeProvider;
        var processor = new PaymentProcessor(timeProvider);
        
        var startTime = timeProvider.GetUtcNow();
        var deadline = startTime.AddMilliseconds(500);
        
        // Simulate variable processing time
        await Task.Delay(100);
        
        var processedTime = timeProvider.GetUtcNow();
        
        // This assertion can fail if system is slow
        Assert.True(processedTime < deadline, 
            $"Payment took {(processedTime - startTime).TotalMilliseconds}ms, SLA is 500ms");
    }
}
```

**Recording Output:**
```
Running: PaymentProcessorTests.ProcessPaymentWithinSLA (30 runs)
- Run 1: PASSED
- Run 2: PASSED
- Run 15: FAILED (system slow, exceeded SLA)
- Runs 16-30: PASSED

Session captured: <report>/runtime-sessions/PaymentProcessorTests.ProcessPaymentWithinSLA.json
```

**Replay:**
```bash
deflake repro report.json --replay <report>/runtime-sessions/PaymentProcessorTests.ProcessPaymentWithinSLA.json
```

**Replay Output:**
```
Test: PaymentProcessorTests.ProcessPaymentWithinSLA
Session: <report>/runtime-sessions/PaymentProcessorTests.ProcessPaymentWithinSLA.json
Recorded behavior: MATCHES
Status: ✓ REPLAYED successfully

Verdict: TimingSensitive
  Evidence:
    - Condition: Idle vs Load
      Passed: 25/30 (83.3%)
      Failed: 5/30 (16.7%)
      Interval: 67.2%–95.1% (95% CI)
    - Heuristic: Timeout/SLA pattern matched in failure message
    
Confidence: 0.92

Repro command (for local debugging):
  DEFLAKE_REPLAY=<report>/runtime-sessions/PaymentProcessorTests.ProcessPaymentWithinSLA.json \
  dotnet test PaymentProcessorTests.cs -k ProcessPaymentWithinSLA
```

### Example 2: Random-Based Test

```csharp
using Deflake.Runtime;
using Xunit;

public class RandomShuffleTests
{
    [Fact]
    public void ShuffleProducesExpectedOrder()
    {
        var random = DeflakeRecorder.Random("shuffle");
        var items = new[] { 1, 2, 3, 4, 5 };
        
        // Fisher-Yates shuffle using recorded Random
        for (int i = items.Length - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            (items[i], items[j]) = (items[j], items[i]);
        }
        
        // Assertion might fail if random seed changes
        Assert.Equal(new[] { 3, 1, 5, 2, 4 }, items);
    }
}
```

**Recording Output (run that fails):**
```
Test: RandomShuffleTests.ShuffleProducesExpectedOrder
Session: <report>/runtime-sessions/RandomShuffleTests.ShuffleProducesExpectedOrder.json
Random calls recorded in stream "shuffle":
  1. Next(5) → 2
  2. Next(4) → 1
  3. Next(3) → 2
  4. Next(2) → 1
```

**Session File (excerpt):**
```json
{
  "streams": {
    "Random:shuffle": [
      { "callSite": "Next", "arguments": { "maxValue": 5 }, "returnValue": 2, "sequenceNumber": 1 },
      { "callSite": "Next", "arguments": { "maxValue": 4 }, "returnValue": 1, "sequenceNumber": 2 },
      { "callSite": "Next", "arguments": { "maxValue": 3 }, "returnValue": 2, "sequenceNumber": 3 },
      { "callSite": "Next", "arguments": { "maxValue": 2 }, "returnValue": 1, "sequenceNumber": 4 }
    ]
  }
}
```

**Replay Output:**
```
Test: RandomShuffleTests.ShuffleProducesExpectedOrder
Session: <report>/runtime-sessions/RandomShuffleTests.ShuffleProducesExpectedOrder.json
Recorded Random calls: 4 (stream "shuffle")
Playback: ✓ All calls matched (deterministic replay successful)
Result: PASSED
```

### Example 3: Multiple Random Streams

```csharp
[Fact]
public void DiceRollAndCardDraw()
{
    var diceRoll = DeflakeRecorder.Random("dice");
    var cardDraw = DeflakeRecorder.Random("cards");
    
    // Two independent random sources
    int roll = diceRoll.Next(1, 7);           // 1-6
    int card = cardDraw.Next(0, 52);          // 0-51
    
    Assert.True(roll >= 1 && roll <= 6);
    Assert.True(card >= 0 && card < 52);
}
```

**Session file records both streams separately:**
```json
{
  "streams": {
    "Random:dice": [
      { "callSite": "Next", "arguments": { "minValue": 1, "maxValue": 7 }, "returnValue": 4, "sequenceNumber": 1 }
    ],
    "Random:cards": [
      { "callSite": "Next", "arguments": { "minValue": 0, "maxValue": 52 }, "returnValue": 23, "sequenceNumber": 1 }
    ]
  }
}
```

**Replay preserves both streams** — each `DeflakeRecorder.Random(streamId)` gets its own deterministic sequence.

---

## CLI Integration

### Recording with `deflake investigate`

```bash
deflake investigate MyProject.csproj \
  --test MyNamespace.MyTest \
  --record-replay \
  --runs 50 \
  --max-runs 100 \
  --report ./deflake-report
```

**Output Flags:**
- `--record-replay`: Enable recording (default: off, opt-in)
- `--runs 50`: Initial run count before verdict
- `--max-runs 100`: Adaptive max (stop early if verdict is clear)
- `--report ./deflake-report`: Report and sessions saved here

### Replaying with `deflake repro`

```bash
deflake repro ./deflake-report/investigation.json
```

Deflake automatically detects session files in the report and offers replay:

```
Verdict: TimingSensitive
Repro command:
  deflake repro ./deflake-report/investigation.json --replay ./deflake-report/runtime-sessions/MyNamespace.MyTest.json
```

---

## Best Practices

### ✅ Do

- **Use `DeflakeRecorder.TimeProvider`** for all `DateTime.UtcNow` calls in timing-sensitive tests
- **Use unique stream IDs** for multiple `Random` instances: `DeflakeRecorder.Random("payments")`, `DeflakeRecorder.Random("retries")`
- **Record early**: Use `--record-replay` as soon as a test shows timing sensitivity
- **Share sessions** with teammates: commit session files to diagnose the same failure locally
- **Review session content**: JSON is human-readable; inspect to understand the exact sequence

### ❌ Don't

- **Mix `DateTime.UtcNow` and `DeflakeRecorder.TimeProvider`** in the same test (breaks determinism)
- **Hardcode expected values** based on one session: sessions are specific to one machine/run
- **Assume task order is replayed**: task scheduling is recorded but not replayed in v1 (only timing values)
- **Record on every run**: use `--record-replay` only for failing/flaky tests

---

## Troubleshooting

### "DeflakeReplayDivergenceException: Test behavior changed during replay"

**Cause:** Test made a call during record that doesn't match during replay (e.g., extra `Random.Next()` call).

**Solution:**
1. Compare the failing run's session with the test code
2. Check if test behavior changed (new Random call added?)
3. If test is correct, re-record with latest code

### "Session file not found"

**Cause:** Recording didn't create a session (test passed every run, no failure recorded).

**Solution:**
- Increase `--runs` or `--max-runs` to trigger more failures
- Verify `--record-replay` flag is set
- Check report directory: sessions are in `<report>/runtime-sessions/`

### "TimeProvider values are off by microseconds"

**Cause:** Machine or timezone changed between record and replay.

**Solution:** Session captures `timestampFrequency` and `utcOffsetSeconds` — Deflake normalizes them. This is expected; values are still deterministic on replay.

---

## Limitations (v0.1.0)

- **Task scheduling not replayed:** Task.Delay(), Task.Run(), and concurrent scheduling are recorded for diagnostics but not replayed. This is intentional (see design doc) to avoid false diagnoses.
- **Network calls not recorded:** HTTP requests, database calls, etc. are not in scope. Use mocking or fixture servers for those.
- **Attribute/reflection injection:** Only registry-based injection (`DeflakeRecorder.TimeProvider`) is supported. Custom `ITimeProvider` implementations won't be recorded.

---

## Next Steps

1. **Instrument a flaky test** with `DeflakeRecorder.TimeProvider` or `DeflakeRecorder.Random()`
2. **Run with `--record-replay`** to capture a failing session
3. **Review the session file** and verdict
4. **Replay locally** to confirm deterministic reproduction
5. **Share the session** with your team for collaborative debugging

For more details on verdicts and investigation results, see [verdicts.md](verdicts.md).
