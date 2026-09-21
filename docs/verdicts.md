# Verdict Types

Deflake answers with one of ten verdict types. Each one names a specific factor to investigate or change, so precision matters — a wrong diagnosis can send a developer on a wild goose chase. If the evidence is not conclusive, deflake says so with `Inconclusive` rather than guessing.

## Inconclusive

The test is flaky, but none of the factors Deflake varied (isolation, order, parallelism, CPU load, culture, or time zone) changed the outcome beyond statistical noise.

**What it means:** The flakiness has a cause outside Deflake's test scope, such as:
- Timing issues too subtle to trigger in controlled conditions
- Dependency on external services (databases, APIs)
- Nondeterministic behavior in libraries or frameworks
- Race conditions that require specific hardware timing

**What to do:** Look at the suspected members in the stack trace, check for sleep/wait conditions, verify database/file state before each run, and look for global state modifications.

### Example

```
Test: SampleProject.Tests.Environment_variable_is_expected_value
Verdict: Inconclusive
Confidence: 20%

Evidence:
Condition            | Passed | Failed |                      Rate
---------------------+--------+--------+--------------------------
Isolation=Alone      |      0 |      5 | 100,0% (95% 56,6%–100,0%)
Scope=Alone          |      0 |      5 | 100,0% (95% 56,6%–100,0%)
Parallelism=Serial   |      0 |      5 | 100,0% (95% 56,6%–100,0%)
Parallelism=Parallel |      0 |      5 | 100,0% (95% 56,6%–100,0%)
Load=Idle            |      0 |      5 | 100,0% (95% 56,6%–100,0%)
Load=UnderLoad       |      0 |      5 | 100,0% (95% 56,6%–100,0%)
Culture=en-US        |      0 |      5 | 100,0% (95% 56,6%–100,0%)
Culture=de-DE        |      0 |      5 | 100,0% (95% 56,6%–100,0%)
Culture=ja-JP        |      0 |      5 | 100,0% (95% 56,6%–100,0%)

Not Tested:
  - Isolation: fewer than 20 scoreable runs per condition, too few to compare
  - Scope: fewer than 20 scoreable runs per condition, too few to compare
  - Polluters: not tested
  - Parallelism: fewer than 20 scoreable runs per condition, too few to compare
  - Load: fewer than 20 scoreable runs per condition, too few to compare
  - Culture: fewer than 20 scoreable runs per condition, too few to compare
  - TimeZone: TimeZone testing via TZ environment variable is not supported on Windows.

Notes:
  - The test is flaky — it failed 5 of 5 times at its worst condition (Alone) — but no factor tested changed the outcome beyond chance.
```

The `Inconclusive` verdict carries a low confidence (often 20%) and a list of factors ruled out and factors not tested. This tells you where to look next.

---

## ConsistentlyFails

The test fails every time, in every condition. It is not flaky — it is broken.

**What it means:** The test or the code under test has a deterministic bug.

**What to do:** Fix the broken assertion or code. This is not a flakiness issue.

### Example

A test that always fails because its assertion is wrong:

```csharp
[Fact]
public void BrokenAssertion()
{
    var result = Add(2, 2);
    Assert.Equal(5, result);  // Will always fail
}
```

Deflake will report:

```
Verdict: ConsistentlyFails
Confidence: 99%

Evidence:
Condition            | Passed | Failed |                      Rate
---------------------+--------+--------+--------------------------
Isolation=Alone      |      0 |     20 | 100,0% (95% 83,2%–100,0%)

Notes:
  - The test is not flaky — it failed 20 of 20 times in every condition tested.
```

---

## NotReproduced

The test did not fail even once during the investigation. It may still be flaky, but Deflake could not reproduce the failure.

**Confidence claim:** The verdict includes an upper bound on how flaky the test can still be (using the "rule of three" — if something did not occur in n trials, there is a 95% chance it occurs fewer than 3/n times).

**What it means:** 
- The test may be flaky at a very low rate.
- The environment or conditions in which Deflake runs may differ from where the original failure occurred.
- The bug may have been fixed since the original failure.

**What to do:** 
- Collect more context about when the test failed originally.
- Ensure you are running with the same configuration and dependencies.
- If the failure was environment-specific, try to recreate that environment.

### Example

A test that passes when run locally or in CI:

```
Verdict: NotReproduced
Confidence: 20%

Evidence:
Condition            | Passed | Failed |                      Rate
---------------------+--------+--------+--------------------------
Isolation=Alone      |     20 |      0 | 0,0% (95% 0,0%–16,8%)

Notes:
  - The test did not fail during the investigation. Based on 20 runs, it can be at most 16.8% flaky.
```

---

## OrderDependent

The test passes when run in isolation, but fails significantly more often when other tests run before it (in the same process or scope).

**What it means:** The test depends on shared state left by another test, such as:
- Static fields or class-level state
- Global variables modified by other tests
- Shared database or file system state not cleaned up
- Singleton instances

**What to do:** 
1. Look at the `SuspectTests` list — these are the tests that appear to pollute the state.
2. Add setup/teardown code to isolate the test.
3. Clear or reset shared state before each test.
4. Run the test with the suspect tests to verify the failure.

### Example

```
Verdict: OrderDependent
Confidence: 92%

Evidence:
Condition            | Passed | Failed |                      Rate
---------------------+--------+--------+--------------------------
Isolation=Alone      |     15 |      5 | 25.0% (95% 8.7%–48.7%)
Isolation=WithOthers |      0 |     20 | 100.0% (95% 83.2%–100.0%)

Suspect Tests:
  - MyTests.SetupStateTest
  - MyTests.ModifyGlobalStateTest

Repro Command:
dotnet test MyProject.csproj --filter "FullyQualifiedName=MyTests.SetupStateTest OR FullyQualifiedName=MyTests.ModifyGlobalStateTest OR FullyQualifiedName=MyTests.FlakingTest"
```

---

## ParallelismDependent

The test passes when tests run serially, but fails significantly more often when tests run in parallel.

**What it means:** The test has a race condition or shares a resource that is not thread-safe, such as:
- Unsynchronized access to static fields
- Shared files or directories
- Ports or network resources
- Non-thread-safe singleton instances

**What to do:**
1. Add thread-safety mechanisms (locks, concurrent collections, thread-local storage).
2. Use unique resources per test (separate files, ports, database tables).
3. Use an ICollectionFixture or IAsyncLifetime in xUnit to coordinate resource sharing.
4. Check for unguarded static state.

### Example

```
Verdict: ParallelismDependent
Confidence: 88%

Evidence:
Condition              | Passed | Failed |                      Rate
-----------------------+--------+--------+--------------------------
Parallelism=Serial     |     18 |      2 | 10.0% (95% 1.2%–31.7%)
Parallelism=Parallel   |      0 |     20 | 100.0% (95% 83.2%–100.0%)

Notes:
  - The test fails significantly more often when run in parallel.
  - Likely cause: unsynchronized access to shared state or resource contention.
```

---

## TimingSensitive

The test fails significantly more often when the machine is under load (CPU, memory, or I/O).

**What it means:** The test has a timing-related bug, such as:
- A timeout that is too tight
- A busy-wait loop
- A sleep duration that is too short
- Assumptions about execution speed

**What to do:**
1. Increase timeout values.
2. Replace busy-wait or sleep loops with events or condition variables.
3. Remove assumptions about execution speed.
4. Use proper synchronization primitives (ManualResetEvent, Semaphore, etc.) instead of sleep.

### Example

```
Verdict: TimingSensitive
Confidence: 85%

Evidence:
Condition      | Passed | Failed |                      Rate
---------------+--------+--------+--------------------------
Load=Idle      |     17 |      3 | 15.0% (95% 3.2%–37.9%)
Load=UnderLoad |      0 |     20 | 100.0% (95% 83.2%–100.0%)

Notes:
  - The test fails significantly more often when the system is under load.
  - Likely cause: timing assumptions or insufficient wait/retry logic.
```

---

## CultureDependent

The test outcome changes when the culture (locale) changes. This affects string formatting, parsing, sorting, and comparison.

**What it means:** The test or code under test makes assumptions about culture-specific behavior, such as:
- Date/number formatting (e.g., "1.234" vs "1,234")
- String case conversion (e.g., Turkish "I" vs "İ")
- Sorting order (e.g., accented characters)
- Decimal separators

**What to do:**
1. Always specify the culture explicitly: `CultureInfo.InvariantCulture` for parsing/formatting.
2. Use culture-invariant comparisons.
3. Test with multiple cultures (e.g., en-US, de-DE, ja-JP).
4. Avoid assumptions about the current culture.

### Example

```csharp
// BAD: Assumes current culture
var formatted = value.ToString();
var parsed = int.Parse(formatted);

// GOOD: Invariant culture
var formatted = value.ToString(CultureInfo.InvariantCulture);
var parsed = int.Parse(formatted, CultureInfo.InvariantCulture);
```

Deflake verdict:

```
Verdict: CultureDependent
Confidence: 90%

Evidence:
Condition       | Passed | Failed |                      Rate
----------------+--------+--------+--------------------------
Culture=en-US   |     18 |      2 | 10.0% (95% 1.2%–31.7%)
Culture=de-DE   |      5 |     15 | 75.0% (95% 48.8%–93.2%)
Culture=ja-JP   |      4 |     16 | 80.0% (95% 56.3%–94.3%)

Suspect Members:
  - MyCode.FormatValue
  - MyCode.ParseValue

Notes:
  - The test outcome depends on the culture. Check for culture-specific formatting or parsing.
```

---

## TimeZoneDependent

The test outcome changes when the process time zone changes. This affects time calculations and parsing.

**What it means:** The test or code under test makes assumptions about time zones, such as:
- Hardcoded time zone offsets
- Parsing times without specifying a time zone
- Assumptions about the local time zone

**What to do:**
1. Use `TimeZoneInfo` explicitly or `DateTimeOffset` instead of `DateTime` when the time zone matters.
2. Always parse times with the correct time zone.
3. Use UTC internally and convert to local time only for display.

### Example

```csharp
// BAD: Assumes local time zone
var time = DateTime.Parse("2024-01-01 12:00:00");

// GOOD: Explicit time zone
var time = DateTime.Parse("2024-01-01 12:00:00", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
var offset = TimeZoneInfo.Local.GetUtcOffset(time);
```

Deflake verdict (on Unix-like systems or when TZ environment variable is supported):

```
Verdict: TimeZoneDependent
Confidence: 87%

Evidence:
Condition        | Passed | Failed |                      Rate
-----------------+--------+--------+--------------------------
TimeZone=UTC     |     18 |      2 | 10.0% (95% 1.2%–31.7%)
TimeZone=EST     |      2 |     18 | 90.0% (95% 68.3%–98.8%)
TimeZone=JST     |      1 |     19 | 95.0% (95% 75.1%–99.9%)

Notes:
  - The test depends on the process time zone. Check for hardcoded offsets or assumptions about local time.
```

**Note:** TimeZone testing requires `TZ` environment variable support, which is not available on Windows by default.

---

## HostCrash

The test host (test runner process) crashed or was terminated unexpectedly (e.g., stack overflow, native crash, `Environment.Exit`).

**What it means:** The test or code under test causes an uncontrolled termination, such as:
- Stack overflow from infinite recursion
- Native code crash or segmentation fault
- Calling `Environment.Exit` or `Environment.FailFast`
- Out-of-memory exception followed by shutdown

**What to do:**
1. Run the test under a debugger to catch the crash point.
2. Look for infinite recursion, buffer overflows, or native interop issues.
3. Check for environment.Exit() calls.
4. Add stack traces and memory profiling.

### Example

```
Verdict: HostCrash
Confidence: 99%

Evidence:
(No standard runs completed due to host crash)

Notes:
  - The test host crashed during execution.
  - Repro command cannot be provided for host crashes (no trustworthy run record).
  - Investigate stack overflow, native crashes, or Environment.Exit calls.

Repro Command:
(None — the host crashed and cannot be reproduced reliably from this report)
```

---

## ResourceConflict

Two or more tests want the same machine-wide or process-wide resource, and the outcome depends on which test gets it (determined by order or parallelism).

**What it means:** Tests share a resource that is not properly isolated, such as:
- A fixed port number
- A file path
- A database table or connection
- A named pipe or socket
- A shared memory region

**What to do:**
1. Run the conflicting tests in sequence (disable parallelism for them).
2. Allocate unique resources per test (e.g., use port 0 for OS-assigned ports).
3. Use temporary directories with unique names.
4. Use a resource pool or test fixture to coordinate access.
5. Clear resources in teardown.

### Example

```
Verdict: ResourceConflict
Confidence: 91%

Evidence:
Condition              | Passed | Failed |                      Rate
-----------------------+--------+--------+--------------------------
Isolation=Alone        |     19 |      1 | 5.0% (95% 0.1%–24.9%)
Isolation=WithOthers   |      1 |     19 | 95.0% (95% 75.1%–99.9%)

Suspect Tests:
  - MyTests.ListenerTest
  - MyTests.ServerTest

Repro Command:
dotnet test MyProject.csproj --filter "FullyQualifiedName=MyTests.ListenerTest OR FullyQualifiedName=MyTests.ServerTest"

Notes:
  - These tests likely compete for the same port or file resource.
  - Modify tests to use unique or ephemeral resources.
```

---

## Summary Table

| Verdict | Flaky? | Cause | Fix |
|---------|--------|-------|-----|
| Inconclusive | Yes | Unknown, outside tested factors | Inspect code, external state |
| ConsistentlyFails | No | Broken code/test | Fix the bug |
| NotReproduced | Unknown | Cannot trigger failure | Collect more context |
| OrderDependent | Yes | Shared state from prior tests | Clean up state, isolate tests |
| ParallelismDependent | Yes | Race condition or resource contention | Add thread safety, unique resources |
| TimingSensitive | Yes | Tight timeout or timing assumption | Increase timeouts, use events |
| CultureDependent | Yes | Culture-specific formatting/parsing | Use InvariantCulture |
| TimeZoneDependent | Yes | Timezone-specific assumptions | Use UTC or explicit timezones |
| HostCrash | Yes | Process crash or termination | Debug crash, check Environment.Exit |
| ResourceConflict | Yes | Tests sharing a fixed resource | Use unique or ephemeral resources |
