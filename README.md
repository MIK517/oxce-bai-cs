# OXCE .NET

This repository is the compatibility-focused .NET 10 port of OpenXcom Extended (OXCE).
The C++ reference engine is maintained as a separate checkout. A sibling directory
named `oxce-bai` (`../oxce-bai`) is the conventional local layout, but tools and tests
must not require a machine-specific absolute path.

The goal is to run existing OXCE mods and scripts, read and write compatible save
files, consume the same original X-COM assets, and reproduce the same game rules.
It is not a goal to reproduce the C++ engine's random sequence, pixel output, or UI
layout exactly.

The repository is an early compatibility implementation. Read these documents before
adding a subsystem:

- [Agent onboarding](AGENTS.md)
- [Compatibility contract](docs/compatibility-contract.md)
- [Architecture](docs/architecture.md)
- [Implementation plan](docs/implementation-plan.md)
- [Phase 0 status](docs/phase-0-status.md)
- [Testing strategy](docs/testing-strategy.md)

## Initial commands

Install a .NET 10 SDK, then run:

```bash
dotnet build Oxce.slnx
dotnet test Oxce.slnx
```

Tests use xUnit v3 on Microsoft Testing Platform. Compatibility artifacts are managed
with `tools/Oxce.FixtureTool`; see [the fixture guide](fixtures/README.md).

The optional SDL3 indexed-frame smoke test requires an
[SDL 3.4.10](https://github.com/libsdl-org/SDL/releases/tag/release-3.4.10) native runtime
on the library search path. On Windows, download the official VC archive and run:

```powershell
.\tools\run-sdl-smoke.ps1 -SdlDirectory <path-to-SDL3-x64-directory>
```

## Resource browser

The Phase 2 resource browser layers game data and optional overlays through the same
virtual-file catalog used by the content pipeline. It composes representative geoscape
and battlescape resources without requiring SDL:

```bash
dotnet run --project tools/Oxce.ResourceBrowser -- --root data/UFO --output artifacts/ufo-preview.ppm
dotnet run --project tools/Oxce.ResourceBrowser -- --root data/TFTD --output artifacts/tftd-preview.ppm
```

Pass additional `--root` arguments in ascending priority order to inspect overrides.
Add `--show` to present the preview through SDL3; `--scale` and `--duration` control the
temporary preview window. Original assets and private mods remain ignored and must not
be committed.

## Licensing

This project is free software licensed under the
[GNU General Public License, version 3 or later](LICENSE.txt). It is an independent
.NET port derived from the behavior and design of OpenXcom Extended and OpenXcom.
See [NOTICE.md](NOTICE.md) for attribution. Original X-COM game data is not included.
