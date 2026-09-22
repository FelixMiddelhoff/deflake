# Deflake Tutorial

Learn how to use Deflake to investigate why your tests are flaky, identify the root cause, and get a reproducible command to verify the fix.

## Installation

Deflake is distributed as a dotnet global tool. Install it with:

```bash
dotnet tool install -g Deflake
```

Verify the installation:

```bash
deflake --version
```

You should see:

```
deflake 0.1.0
```

## Your First Investigation

Let's investigate a flaky test. This tutorial uses a sample project that comes with Deflake, but the same steps apply to any .NET project with xUnit, NUnit, or MSTest tests.

### Step 1: Prepare Your Test

Make sure your project builds and runs tests successfully at least once:

```bash
cd MyProject
dotnet test
```

The sample project in `tests/Deflake.EndToEnd/SampleProject` contains tests that can fail or pass depending on their environment. Let's use that.

### Step 2: Run Deflake

Start an investigation on a test you suspect is flaky. You can specify a test by its fully qualified name:

```bash
deflake investigate SampleProject.csproj --test "SampleProject.Tests.Environment_variable_is_expected_value"
```

Or investigate all tests in a project:

```bash
deflake investigate SampleProject.csproj
```

Deflake will:
1. Build your project
2. Run the test multiple times under different conditions
3. Analyze the results to identify the root cause
4. Report a verdict and confidence score

Here's what happens under the hood:

- **Isolation experiments:** Tests run alone vs. with other tests
- **Scope experiments:** Tests run at different levels (class scope, assembly scope)
- **Parallelism experiments:** Tests run serially vs. in parallel
- **Load experiments:** Tests run on an idle machine vs. under CPU/disk load
- **Culture experiments:** Tests run under different locales (en-US, de-DE, ja-JP, etc.)
- **Timezone experiments:** (Unix/Linux only) Tests run in different time zones

Each experiment adapts: if evidence points toward a factor, Deflake runs more tests under that condition to confirm.

### Step 3: Interpret the Report

Here's a sample investigation output:

```
Test: SampleProject.Tests.Environment_variable_is_expected_value
Assembly: SampleProject
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

Suspect Members:
  - SampleProject.Tests.Environment_variable_is_expected_value

Repro Command:
dotnet test SampleProject.csproj --no-build --filter FullyQualifiedName=SampleProject.Tests.Environment_variable_is_expected_value

Repro Verified: True

Notes:
  - The test is flaky — it failed 5 of 5 times at its worst condition (Alone) — but no factor tested changed the outcome beyond chance.
```

### Reading the Output

- **Verdict:** The diagnosis (e.g., `OrderDependent`, `TimingSensitive`, `Inconclusive`). See [Verdict Types](./verdicts.md) for details.

- **Confidence:** How certain the diagnosis is (0%–100%). Higher is better.

- **Evidence Table:** Each row is one experimental condition.
  - `Condition` — The factor tested and its value (e.g., `Isolation=Alone`).
  - `Passed` / `Failed` — Number of passing and failing runs.
  - `Rate` — Failure rate as a percentage with 95% confidence interval.
  
  Look for rows where the failure rate differs significantly. For example:
  
  ```
  Isolation=Alone      |     15 |      5 | 25.0%  (95% 8.7%–48.7%)
  Isolation=WithOthers |      0 |     20 | 100.0% (95% 83.2%–100.0%)
  ```
  
  The failure rate jumps from 25% to 100% when tests run together, suggesting an `OrderDependent` issue.

- **Not Tested:** Factors that were not tested or experiments that didn't complete. For example:
  - "fewer than 20 scoreable runs per condition" — Not enough data to compare. Increase `--runs`.
  - "not tested" — Deflake skipped this factor because earlier factors already identified the issue.

- **Suspect Tests:** Tests that appear to pollute the state or cause the failure (for `OrderDependent` or `ResourceConflict` verdicts).

- **Suspect Members:** Functions or methods from stack traces that might be involved in the bug.

- **Repro Command:** A copy-paste command that reproduces the failure. Run it to verify the issue yourself before fixing it.

- **Repro Verified:** True means Deflake re-ran the repro command and it failed again. Unverified repro commands are guesses.

- **Notes:** Additional context, warnings, or observations.

## Common Scenarios

### OrderDependent: A Test Fails Only When Other Tests Run First

Verdict indicates the test depends on state left by another test.

```
Isolation=Alone      |     18 |      2 | 10.0% (95% 1.2%–31.7%)
Isolation=WithOthers |      0 |     20 | 100.0% (95% 83.2%–100.0%)

Suspect Tests:
  - MyTests.SetupStateTest
```

**Fix:** Clean up shared state. Add a setup method that resets global state or mocks before each test.

```csharp
[Fact]
public void MyFlakyTest()
{
    // Reset shared state before this test
    GlobalState.Reset();
    
    // Now run the test
    Assert.True(MyCode.DoSomething());
}
```

### TimingSensitive: A Test Fails More Often Under Load

Verdict indicates the test has a timing-related bug.

```
Load=Idle      |     17 |      3 | 15.0% (95% 3.2%–37.9%)
Load=UnderLoad |      0 |     20 | 100.0% (95% 83.2%–100.0%)
```

**Fix:** Increase timeouts or remove timing assumptions.

```csharp
// BAD: Too-tight timeout
await Task.WaitAll(task1, task2);

// GOOD: Explicit timeout with enough margin
var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
await Task.WaitAll(task1, task2);
```

### ParallelismDependent: A Test Fails Only in Parallel

Verdict indicates a race condition or resource contention.

```
Parallelism=Serial     |     18 |      2 | 10.0% (95% 1.2%–31.7%)
Parallelism=Parallel   |      0 |     20 | 100.0% (95% 83.2%–100.0%)
```

**Fix:** Add thread-safety or unique resources per test.

```csharp
// BAD: Static field accessed without synchronization
private static int counter = 0;

[Fact]
public void TestCounter()
{
    counter++;  // Race condition!
    Assert.Equal(1, counter);
}

// GOOD: Thread-local or unique per test
[Fact]
public void TestCounter()
{
    var counter = 0;  // Local variable
    counter++;
    Assert.Equal(1, counter);
}
```

### CultureDependent: A Test Fails in Some Locales

Verdict indicates the test or code assumes a specific culture.

```
Culture=en-US   |     18 |      2 | 10.0% (95% 1.2%–31.7%)
Culture=de-DE   |      5 |     15 | 75.0% (95% 48.8%–93.2%)
Culture=ja-JP   |      4 |     16 | 80.0% (95% 56.3%–94.3%)
```

**Fix:** Use `CultureInfo.InvariantCulture`.

```csharp
// BAD: Uses current culture
var formatted = Math.PI.ToString();  // "3,14" in de-DE, "3.14" in en-US

// GOOD: Invariant culture
var formatted = Math.PI.ToString(CultureInfo.InvariantCulture);  // Always "3.14"
```

## Record and Replay (Advanced, Opt-In)

Some timing-sensitive failures can be difficult to diagnose with experiments alone. Record and replay is an **optional, advanced feature** that helps you determine whether a failure is caused by specific time and random values, or by thread interleaving and system load.

### What You Need

Record and replay requires code changes:

1. **Your test project must use dependency injection** for `TimeProvider` and `Random`. Code that calls `DateTime.UtcNow` or `new Random()` directly cannot be recorded — Deflake does not rewrite IL or hook the CLR.

2. **Install the optional NuGet package:**

```bash
dotnet add package Deflake.Runtime
```

3. **Wire DeflakeRecorder into your composition root:**

```csharp
services.AddSingleton<TimeProvider>(DeflakeRecorder.TimeProvider);
services.AddSingleton<Random>(_ => DeflakeRecorder.Random());
```

The test itself does not change. `DeflakeRecorder.TimeProvider` behaves like `TimeProvider.System` in normal runs and like `TimeProvider.System` under record/replay too — it is a transparent passthrough unless you run `deflake investigate --record-replay`.

### A Real Example

The sample project at `tests/Deflake.EndToEnd/RuntimeSampleProject` contains a timing-sensitive test that uses `DeflakeRecorder`:

```csharp
[Fact]
public async Task Operation_completes_before_timeout()
{
    var random = DeflakeRecorder.Random();
    var timeProvider = DeflakeRecorder.TimeProvider;
    
    var delayMs = random.Next(10, 100);  // Random delay
    var timeoutMs = 80;                  // Aggressive timeout
    
    var startTime = timeProvider.GetUtcNow();
    await Task.Delay(delayMs);  // Real async work
    
    var elapsed = timeProvider.GetUtcNow() - startTime;
    Assert.True(elapsed.TotalMilliseconds < timeoutMs);
}
```

This test passes most of the time but occasionally times out when system load increases the duration of `Task.Delay`.

### Run an Investigation with Record/Replay

Enable record/replay with the `--record-replay` flag:

```bash
deflake investigate RuntimeSampleProject.csproj --record-replay
```

Deflake will:

1. **Record a failing run:** If a failure is found, Deflake records every call to `TimeProvider` and `Random` that happened during that run to a session file.

2. **Replay the failure:** Deflake then re-runs the test multiple times with the recorded time and random values, without the real system load.

3. **Compare results:** If replay succeeds but the live run fails, then the failure was driven by thread interleaving and system load, not by the time/random values themselves. If replay also fails, then the specific time and random values are sufficient to reproduce the failure.

### Understanding the Report

In the verdict output, look for a new `RecordReplay` evidence row:

```
Condition                   | Passed | Failed |                      Rate
----------------------------+--------+--------+--------------------------
RecordReplay=Live           |     15 |      5 | 25.0% (95% 8.7%–48.7%)
RecordReplay=Replayed       |      0 |     20 | 100.0% (95% 83.2%–100.0%)
```

**Replay matched (or beat) live failure rate** → The recorded time and random values are sufficient to cause the failure. The bug is that specific values trigger it, not thread interleaving.

**Replay succeeded but live failed** → The failure requires thread interleaving. Recorded values are not the root cause.

**No RecordReplay row** → The test does not use `Deflake.Runtime`, or no failure was recorded.

### Limitations

- **Only records time/random:** Task scheduling (thread interleaving) is recorded for diagnostics but not replayed in v1. Record/replay can rule in "the values cause it" but cannot rule out "interleaving still contributes."

- **Requires code changes:** Unlike the rest of Deflake, record/replay needs you to use dependency injection for `TimeProvider` and `Random`.

- **Determinism within a machine:** The test must use the same `TimeProvider`/`Random` calls in the same order. If a test conditionally calls different code paths, replay may diverge.

### Next Steps

- For a complete walk-through, see the sample at `tests/Deflake.EndToEnd/RuntimeSampleProject`.
- Read the [design document](../deflake-planning/deflake-runtime-design.md) for technical details on how record/replay works.
- Record/replay is optional; most flaky tests can be diagnosed with the standard experiments alone.

## Running Deflake in CI/CD

### GitHub Actions

```yaml
name: Test
on: [push, pull_request]

jobs:
  test:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v3
      
      - name: Setup .NET
        uses: actions/setup-dotnet@v3
        with:
          dotnet-version: '8.0'
      
      - name: Install Deflake
        run: dotnet tool install -g Deflake
      
      - name: Run tests
        run: dotnet test
      
      - name: Investigate flaky tests (if any)
        if: failure()
        run: deflake investigate MyProject.csproj --report ./deflake-reports --format markdown --timeout 120
      
      - name: Upload Deflake reports
        if: always()
        uses: actions/upload-artifact@v3
        with:
          name: deflake-reports
          path: ./deflake-reports
```

### Azure Pipelines

```yaml
trigger:
  - main

pool:
  vmImage: 'ubuntu-latest'

variables:
  buildConfiguration: 'Release'

steps:
  - task: UseDotNet@2
    inputs:
      version: '8.0.x'
  
  - task: DotNetCoreCLI@2
    displayName: 'Install Deflake'
    inputs:
      command: 'custom'
      custom: 'tool'
      arguments: 'install -g Deflake'
  
  - task: DotNetCoreCLI@2
    displayName: 'Run tests'
    inputs:
      command: 'test'
      arguments: '--configuration $(buildConfiguration)'
  
  - task: DotNetCoreCLI@2
    displayName: 'Investigate flaky tests'
    condition: failed()
    inputs:
      command: 'custom'
      custom: 'deflake'
      arguments: 'investigate $(Build.SourcesDirectory)/MyProject.csproj --report $(Build.ArtifactStagingDirectory)/deflake-reports --format markdown'
  
  - task: PublishBuildArtifacts@1
    displayName: 'Publish Deflake reports'
    condition: always()
    inputs:
      PathtoPublish: '$(Build.ArtifactStagingDirectory)/deflake-reports'
      ArtifactName: 'deflake-reports'
```

## Advanced Options

### Customize Run Counts

By default, Deflake runs each test 20 times per condition. Increase this for low-flakiness tests:

```bash
deflake investigate MyProject.csproj --runs 50 --max-runs 200
```

This runs at least 50 times per condition, but up to 200 if Deflake needs more data to decide.

### Target a Specific Framework

If your project targets multiple frameworks, specify which one to test:

```bash
deflake investigate MyProject.csproj --framework net8.0
```

### Skip the Build

If your project is already built, skip the rebuild:

```bash
deflake investigate MyProject.csproj --no-build
```

### Save Reports to Files

Instead of printing to console, save reports to a directory:

```bash
deflake investigate MyProject.csproj --report ./deflake-reports --format markdown
```

This creates a file per test in the directory.

### Set a Time Budget

Limit the investigation to a specific number of minutes:

```bash
deflake investigate MyProject.csproj --timeout 30
```

## Understanding Confidence Intervals

Each condition shows a failure rate with a 95% confidence interval:

```
Culture=de-DE   |      5 |     15 | 75.0% (95% 48.8%–93.2%)
```

This means:
- 5 of 20 runs passed (15 failed).
- The observed failure rate is 75%.
- We are 95% confident the true failure rate is between 48.8% and 93.2%.

Deflake compares these intervals. If two conditions have non-overlapping intervals, the difference is statistically significant, and Deflake considers it "beyond chance."

For example:

```
Parallelism=Serial     | Rate: 10.0%  (95% 1.2%–31.7%)
Parallelism=Parallel   | Rate: 100.0% (95% 83.2%–100.0%)
```

The intervals don't overlap → the difference is significant → Verdict: `ParallelismDependent`.

## Next Steps

- Read [Verdict Types](./verdicts.md) to understand each verdict in detail and what to do about it.
- Consult the [CLI Reference](./cli-reference.md) for all command-line options.
- Run `deflake --help` for quick reference.
- Use `deflake repro <report.json>` (when implemented) to re-run the reproduction command programmatically.

## Troubleshooting

### Build Fails

```
deflake: build failed: 'dotnet test' exited with code 1
```

Ensure your project builds and tests run independently:

```bash
dotnet build
dotnet test
```

### No Tests Found

```
deflake: error: No tests were run or all tests were skipped.
```

Make sure:
1. You specified the correct `.csproj` or `.sln` path.
2. The project has test methods (xUnit `[Fact]`, NUnit `[Test]`, etc.).
3. No filters exclude all tests.

### Investigation Timeout

```
deflake: investigation timeout (budget exhausted).
```

The investigation took too long. Increase the timeout:

```bash
deflake investigate MyProject.csproj --timeout 180
```

Or reduce the run count:

```bash
deflake investigate MyProject.csproj --runs 10 --max-runs 50
```

### Help and Version

```bash
deflake --help       # Show all options
deflake -h           # Short form
deflake help         # Also works
deflake --version    # Show version
deflake -v           # Short form
```
