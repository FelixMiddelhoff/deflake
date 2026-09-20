# Contributing

Thanks for helping. Bug reports, wrong verdicts and ideas for new causes of
flakiness are all welcome as issues; pull requests are welcome for anything
you have already discussed in an issue.

## Build and test

You need the .NET 8 SDK or newer.

```
dotnet build
dotnet test
```

## How the code is organised

- `src/Deflake.Core`: the logic, with no dependencies. Result parsing,
  statistics, delta debugging, experiments, verdicts and reports. Everything
  that needs to run tests goes through `ITestRunner`, so the logic is tested
  against scripted fakes instead of real test runs.
- `src/Deflake`: the console front end. Argument parsing and printing only.
- `tests/`: unit tests. Every experiment and verdict has tests for the case it
  reports, the negative case and edge cases.
- `docs/`: tutorial and reference. Update them whenever behaviour, options or
  verdicts change.

## Principles

- A wrong diagnosis is worse than "inconclusive". A verdict needs statistical
  evidence, not a hunch.
- Code should read well for a person who has not seen it before: short
  methods, names that say what a thing is, comments that explain why.
  `dotnet build` treats warnings as errors and enforces `.editorconfig`.

## Releasing

Maintainers only. Set `<Version>` in `Directory.Build.props`, move the
`[Unreleased]` entries in `CHANGELOG.md` under the new version, and push a tag
`vX.Y.Z`. The `release` workflow checks that everything agrees, runs the tests,
creates the GitHub release and publishes the package to nuget.org.
