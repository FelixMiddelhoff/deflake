# Changelog

All notable changes to this project are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses
[Semantic Versioning](https://semver.org/).

## [Unreleased]

## [0.1.0] - 2026-09-21

### Added
- Initial release of Deflake as a dotnet tool
- Flaky test diagnosis by rerunning tests under controlled conditions
- Factor analysis: test ordering, parallelism, CPU load, culture, time zone
- Statistical significance testing to distinguish real factors from noise
- Support for xUnit, NUnit, and MSTest via standard TRX result files
- Works without code changes or test project modifications
- Detailed diagnostic reports with failure evidence and reproduction commands
