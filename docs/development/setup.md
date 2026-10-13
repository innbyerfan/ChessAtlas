# Setting up a development environment

## Requirements

| Tool | Version | Notes |
| --- | --- | --- |
| .NET SDK | **10.0.100 or later** | `dotnet --list-sdks` to check. The SDKs available on `ubuntu-latest` include 10.0.112, 10.0.204, 10.0.303 and 10.0.401. |
| Git | any recent | |
| dotnet-ef | 10.0.12 | Only needed to regenerate migrations. Install with `dotnet tool install --global dotnet-ef --version 10.0.12`. |

No IDE is required. No paid tooling, no commercial Avalonia controls, no subscription service.

## First build

```bash
git clone https://github.com/innbyerfan/ChessAtlas.git
cd ChessAtlas
dotnet restore
dotnet build -c Release
```

`NuGet.config` pins the feed to nuget.org and `Directory.Packages.props` pins every package version,
so a restore here resolves to exactly what CI resolves. If a build ever produces a different
assembly version locally than in CI, that is a bug in one of those two files.

## Running the tests

`dotnet test` does **not** work on the .NET 10 SDK — see
[ADR-0003](../decisions/ADR-0003-testing-with-xunit-v3.md). The test projects are executables:

```bash
dotnet build -c Release
dotnet run --project tests/ChessAtlas.Domain.Tests         -c Release --no-build
dotnet run --project tests/ChessAtlas.Application.Tests    -c Release --no-build
dotnet run --project tests/ChessAtlas.Infrastructure.Tests -c Release --no-build
```

Each prints a summary line:

```
ChessAtlas.Domain.Tests  Total: 183, Errors: 0, Failed: 0, Skipped: 0, Not Run: 0, Time: 2.1s
```

The Infrastructure tests create throwaway SQLite files in the system temp directory and delete them
afterwards. Nothing writes into the repository.

## Regenerating migrations

Never edit a migration by hand. Put `[gen-migrations]` in a commit message and the
`generate-migrations` workflow regenerates the whole set from the current model, applies it to a
scratch database to prove it works, and commits the result back:

```bash
git commit --allow-empty -m "chore: refresh migrations [gen-migrations]"
git push
```

The transcript lands in `ci-artifacts/migrations-log.txt`.

## Project layout

```
src/ChessAtlas.Domain           chess rules, opening model, scheduler — no dependencies
src/ChessAtlas.Application      use cases and ports — Domain + logging abstraction
src/ChessAtlas.Infrastructure   EF Core, SQLite, importers, data sources
src/ChessAtlas.Desktop          (not yet) Avalonia UI
tests/…                         one test project per source project
docs/architecture               how the system is put together
docs/decisions                  architecture decision records
docs/development                this directory
```

## Conventions

- **Warnings are errors in CI** (`/p:TreatWarningsAsErrors=true`) but not locally, so a new SDK with
  a new analyser cannot block someone mid-edit while CI still refuses to merge noise.
- `.editorconfig` describes the existing style. Match it rather than reformatting.
- The Domain project must stay free of package references. Adding one is a design decision that
  needs an ADR, not a convenience.
- Never claim the project builds or that tests pass without having run them. CI's transcript is
  committed to the branch precisely so that claim is checkable.

## What is not here yet

There is no desktop project, so there is nothing to run. Building the solution produces libraries
and test executables only. See the status table in [`../../README.md`](../../README.md).
