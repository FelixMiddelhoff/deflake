# Deflake

[![CI](https://github.com/FelixMiddelhoff/deflake/actions/workflows/ci.yml/badge.svg)](https://github.com/FelixMiddelhoff/deflake/actions/workflows/ci.yml)
[![NuGet version](https://img.shields.io/nuget/v/Deflake.svg)](https://www.nuget.org/packages/Deflake/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![Tests passing](https://img.shields.io/badge/tests-479%20passing-brightgreen)](https://github.com/FelixMiddelhoff/deflake)

> **Early release (v0.1.0).** Core investigation pipeline complete and tested. More features coming: runtime record/replay helpers, test-smell analyzer, CI history mining.

Don't retry the flaky test. Name the culprit.

**Deflake** is a `dotnet` tool that finds out **why** a test is flaky. It reruns the failing test under controlled conditions, changes one factor at a time (other tests running before it, parallelism, CPU load, culture, time zone) and reports which factor flips the result, with the evidence and a command that reproduces the failure.

It works from the outside through `dotnet test`, so it needs **no changes to your code or test project**, and has **no runtime dependencies**. It reads results from the standard TRX files, so it works with **xUnit**, **NUnit** and **MSTest**.

## Quick Start

### Install

```bash
dotnet tool install -g Deflake
```

### Investigate a failing test

```bash
deflake --investigate SomeNamespace.SomeClass.SomeTest --runs 100
```

Output shows:
- What factor flips the result (if any)
- Confidence score
- Evidence table with failure rates and statistical intervals
- Command to reproduce the failure
- Next steps

For a complete walkthrough, see the [tutorial](docs/tutorial.md).

## Features

✅ **Pinpoints root causes** — Order dependency? Parallelism issue? Timing-sensitive? Culture or timezone? Deflake tells you.

✅ **Statistical rigor** — Uses Wilson score intervals; a difference counts only when it's not statistical noise (minimum 20 measured runs, non-overlapping 95% confidence intervals).

✅ **Precise verdicts** — 10 distinct verdicts (HostCrash, ConsistentlyFails, NotReproduced, OrderDependent, ParallelismDependent, TimingSensitive, CultureDependent, TimeZoneDependent, ResourceConflict, Inconclusive) with confidence scores. "Cannot tell" when evidence is insufficient, not a guess.

✅ **Reproducible output** — Every verdict includes a `dotnet test` command that reproduces the failure, verified before reporting.

✅ **Zero changes needed** — Works through `dotnet test` and TRX files. No instrumentation, no code changes, no test project edits.

✅ **No dependencies** — BCL only. No NuGet dependencies to install or update.

✅ **CI-friendly** — Designed for CI environments. Exit codes, machine-readable JSON output, timeout handling.

## Supported Frameworks

- **xUnit** 2.x
- **NUnit** 3.x
- **MSTest** (via VSTest)

## Key Commands

```bash
# Investigate a failing test
deflake --investigate <test-fqn> --runs 50 --max-runs 200

# Reproduce a known flaky failure
deflake --repro "TZ=Asia/Tokyo dotnet test ..."

# Explain a verdict
deflake --explain <test-fqn>

# Help
deflake --help
```

See [CLI Reference](docs/cli-reference.md) for all options and exit codes.

## How It Decides

- **No false positives:** A test that never fails again is reported as *not reproduced* (with the probability it could still fail), not as "fine".
- **Statistical gates:** A factor is called responsible only when failure rates with and without it differ beyond statistical noise (Wilson intervals, minimum 20 runs).
- **Precision over recall:** When evidence is not enough, the answer is *inconclusive*, and the report lists everything that was ruled out. A wrong diagnosis is worse than none.

See [Verdicts Guide](docs/verdicts.md) for detailed explanations of all 10 verdict types.

## Documentation

- **[Tutorial](docs/tutorial.md)** — Install, step-by-step investigation workflow, real examples, common scenarios, CI/CD integration
- **[Verdicts Guide](docs/verdicts.md)** — All 10 verdict types, what they mean, what to do, evidence examples
- **[CLI Reference](docs/cli-reference.md)** — Commands, options, exit codes, output formats

## Requirements

- **.NET SDK** 6.0+ (to run `dotnet test`)
- **Windows**, **Linux**, or **macOS**
- A test project using xUnit, NUnit, or MSTest

## Example Output

```
Test: Acme.Tests.OrderServiceTests.CancelOrder_WithConcurrentCancel_DoesNotCrash

Verdict: OrderDependent (80% confidence)

Evidence:
┌────────────────────┬────────┬────────┬──────────┐
│ Condition          │ Passed │ Failed │ Rate     │
├────────────────────┼────────┼────────┼──────────┤
│ Alone              │ 50     │ 0      │ 0.0%     │
│ With Setup_Data    │ 25     │ 25     │ 100.0%   │
│ With other tests   │ 10     │ 40     │ 80.0%    │
└────────────────────┴────────┴────────┴──────────┘

Repro command:
dotnet test --filter "FullyQualifiedName=Acme.Tests.OrderServiceTests.Setup_Data|Acme.Tests.OrderServiceTests.CancelOrder_WithConcurrentCancel_DoesNotCrash"
```

## Contributing

Found a bug or have an idea? Open an [issue](https://github.com/FelixMiddelhoff/deflake/issues) or a [pull request](https://github.com/FelixMiddelhoff/deflake/pulls).

## License

MIT. See [LICENSE](LICENSE) file.

---

**Built for teams that care about test reliability.** Deflake doesn't hide flakes—it names them.
