# ADR 0004: xUnit v3 on Microsoft Testing Platform

- Status: accepted
- Date: 2026-08-10

## Decision

Use xUnit v3 with Microsoft Testing Platform v2 for unit and compatibility tests. Use
Coverlet's native Microsoft Testing Platform extension to produce cross-platform
Cobertura coverage. Pin direct package versions centrally.

## Dependency review

- `xunit.v3.mtp-v2` is maintained by the xUnit project and licensed Apache-2.0.
- `coverlet.MTP` is maintained by the Coverlet project and licensed MIT.
- Microsoft Testing Platform is supplied transitively by the test runner and is
  maintained by Microsoft.
- All packages are test-only. They do not enter application deployments, affect mod or
  save compatibility, or constrain Native AOT evaluation of production assemblies.
- The executable test host works on the three required desktop operating systems and
  remains callable through `dotnet test` and directly when useful.

## Consequences

- `global.json` opts the repository into the Microsoft Testing Platform runner.
- CI can use the same commands on Windows, Linux, and macOS.
- Package upgrades require a test-infrastructure review and remain centrally visible in
  `Directory.Packages.props`.
