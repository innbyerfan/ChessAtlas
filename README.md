# Chess Atlas

A cross-platform desktop chess application for studying openings, analysing games and training —
built to be used for years, not demoed once.

Chess Atlas is written in C# / .NET 10 with Avalonia UI, backed by SQLite through EF Core, and
driven by external UCI engines. It follows Clean Architecture: the chess rules and the opening
model live in a Domain layer that has no idea a database or a window exists.

> **Status: early. Phase 0 (design) and the Phase 1 core are in place, plus a complete chess
> engine and the opening-catalogue vertical slice. There is no user interface yet.**
> See [Current state](#current-state) for exactly what works and what does not.

---

## Current state

### What is implemented and tested

| Area | What exists | Where |
| --- | --- | --- |
| Chess rules | Full legal move generation, castling, en passant, promotion, check / mate / stalemate, insufficient material, repetition, FEN read + write, Zobrist keys, undo | `src/ChessAtlas.Domain/Chess/` |
| Move notation | SAN generation and parsing with full disambiguation; PGN reading and writing with comments, NAGs and nested variations | `San.cs`, `Pgn*.cs` |
| Opening model | Openings, aliases, lines, ECO codes, classification with provenance and verification status | `src/ChessAtlas.Domain/Openings/` |
| Training scheduler | SM-2 variant spaced repetition with a relearn step and interval ceiling | `src/ChessAtlas.Domain/Learning/` |
| Persistence | EF Core + SQLite, one versioned migration, four tables, search-appropriate indexes | `src/ChessAtlas.Infrastructure/Persistence/` |
| Opening import | Streaming TSV importer for the CC0 lichess catalogue, with deduplication and provenance records | `src/ChessAtlas.Infrastructure/Openings/` |
| Use cases | Catalogue import, search, detail, prefix lookup for a picker | `src/ChessAtlas.Application/Openings/` |

The engine is validated by perft against the published node counts for the standard start
position, Kiwipete and positions 3–6 (`tests/ChessAtlas.Domain.Tests/Chess/PerftTests.cs`).

### What is not implemented yet

These are planned phases, listed here so nothing is mistaken for finished:

- **No desktop UI.** `src/ChessAtlas.Desktop` does not exist yet. There is no board, no sidebar,
  no navigation, no themes.
- **No UCI engine integration.** Stockfish cannot be added or run yet.
- **No game databases.** PGN import to storage, game search and browsing are not built.
- **No repertoire, trainer or puzzle lab.** The scheduler exists; nothing drives it.
- **No online accounts.**
- **No localization or themes.**

### Verification

Every push runs on GitHub Actions: restore, build, and all three test projects executed as real
processes. The last run reported **229 tests across 3 assemblies**. The full transcript is
committed back to the branch at [`ci-artifacts/ci-log.txt`](ci-artifacts/ci-log.txt) so a failure
can be read without the Actions log viewer. See [`docs/development/ci.md`](docs/development/ci.md).

---

## Building

Requirements: the **.NET 10 SDK** (10.0.100 or later).

```bash
dotnet restore
dotnet build -c Release
```

`Directory.Packages.props` pins every package version centrally, and `NuGet.config` pins the feed,
so a build here and a build in CI resolve to identical assemblies.

## Testing

The test projects target **xunit.v3**, which builds each test project as an executable. On the
.NET 10 SDK, `dotnet test` still routes through the VSTest target and is refused, so run the
projects directly:

```bash
dotnet run --project tests/ChessAtlas.Domain.Tests        -c Release
dotnet run --project tests/ChessAtlas.Application.Tests   -c Release
dotnet run --project tests/ChessAtlas.Infrastructure.Tests -c Release
```

See [ADR-0003](docs/decisions/ADR-0003-testing-with-xunit-v3.md) for why.

## Documentation

| Document | Contents |
| --- | --- |
| [`docs/architecture/overview.md`](docs/architecture/overview.md) | Layers, dependency rules, what belongs where |
| [`docs/architecture/chess-engine.md`](docs/architecture/chess-engine.md) | The position model, legality, SAN strategy, perft |
| [`docs/architecture/opening-catalogue.md`](docs/architecture/opening-catalogue.md) | Schema, transpositions, provenance, verification |
| [`docs/architecture/storage-strategy.md`](docs/architecture/storage-strategy.md) | Why EF Core does not own game archives |
| [`docs/development/setup.md`](docs/development/setup.md) | Getting a working development environment |
| [`docs/development/data-sources.md`](docs/development/data-sources.md) | Where opening data comes from, and its licence |
| [`docs/development/ci.md`](docs/development/ci.md) | The CI loop and its artefacts |
| [`docs/decisions/`](docs/decisions/) | Architecture decision records |

## Licence

Chess Atlas itself: see [`LICENSE`](LICENSE).

The opening catalogue data comes from
[lichess-org/chess-openings](https://github.com/lichess-org/chess-openings) under **CC0 1.0** and
is imported at runtime by the user, not bundled into this repository. See
[`docs/development/data-sources.md`](docs/development/data-sources.md).
