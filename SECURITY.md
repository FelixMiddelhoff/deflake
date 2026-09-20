# Security

Deflake starts `dotnet test` on projects you point it at, so it runs the code
of those projects and your tests, with your permissions. Only investigate
projects you would run anyway. It does not open network connections itself and
does not modify your project files.

If you find a vulnerability (for example a crafted TRX file or test name that
makes the tool run unintended commands), please report it privately through
[GitHub security advisories](https://github.com/FelixMiddelhoff/deflake/security/advisories/new)
instead of a public issue. Expect a first answer within a week.

Supported versions: the latest release.
