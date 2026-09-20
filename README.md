# Deflake

Don't retry the flaky test. Name the culprit.

Deflake is a `dotnet` tool that finds out **why** a test is flaky. It reruns
the failing test under controlled conditions, changes one factor at a time
(other tests running before it, parallelism, CPU load, culture, time zone)
and reports which factor flips the result, with the evidence and a command
that reproduces the failure.

It works from the outside through `dotnet test`, so it needs no changes to
your code or your test project, and has no dependencies. It reads results
from the standard TRX files, so it works with xUnit, NUnit and MSTest.

## How it decides

- A test that never fails again is reported as not reproduced, with how
  often it could still fail, not as "fine".
- A factor is called responsible only when the failure rates with and without
  it differ beyond statistical noise.
- When the evidence is not enough, the answer is "inconclusive" and the report
  lists everything that was ruled out. A wrong diagnosis is worse than none.

## License

MIT
