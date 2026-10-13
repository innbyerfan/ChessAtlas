# Architecture overview

Chess Atlas is a Clean Architecture solution. The rule that matters is short: **dependencies point
inwards.** The chess rules do not know a database exists; the database does not know a window
exists.

```
ChessAtlas.sln
├── src/
│   ├── ChessAtlas.Domain          no dependencies
│   ├── ChessAtlas.Application     → Domain
│   ├── ChessAtlas.Infrastructure  → Application, Domain
│   └── ChessAtlas.Desktop         → Application        (planned)
└── tests/
    ├── ChessAtlas.Domain.Tests
    ├── ChessAtlas.Application.Tests
    ├── ChessAtlas.Infrastructure.Tests
    └── ChessAtlas.IntegrationTests                     (planned)
```

## The layers

### Domain — `src/ChessAtlas.Domain`

The chess rules, the opening model and the training scheduler. It references **nothing** — no EF
Core, no Avalonia, no `Microsoft.Extensions.*`, not even a logging abstraction. Its `.csproj` has
no `PackageReference` at all.

This is not stylistic. It means the move generator can be tested by running it, the opening model
can be reasoned about without a database, and neither can be broken by an ORM upgrade.

Contents:

- `Chess/` — `Position` (the board), `Move`, `Square`, `Piece`, `Side`, `CastlingRights`,
  `GameStatus`, `Zobrist`, `San`, `MoveSequence`, `PgnReader`, `PgnWriter`.
- `Openings/` — `Opening`, `OpeningLine`, `OpeningAlias`, `EcoCode`, classification enums.
- `Learning/` — `SpacedRepetition`, `SpacedRepetitionScheduler`.
- `Provenance/` — `DataProvenance`.

### Application — `src/ChessAtlas.Application`

Use cases and the ports they need. References Domain, plus one package: the logging abstraction.
That is the single exception to "Domain only" and it is an abstraction, not an implementation —
no Serilog, no EF Core, no Avalonia.

A use case declares what it needs as an interface — `OpeningCatalogPorts.cs` declares
`IOpeningCatalogSource`, `IOpeningCatalogImporter` and `IOpeningCatalogRepository` — and
Infrastructure provides the implementation. That is the seam that keeps EF Core out of the use case and lets the use case be
tested with a fake.

Also here: `Common/PagedResult.cs`, the shape every list-returning use case returns. Paging is part
of the contract, not an afterthought — see
[`storage-strategy.md`](storage-strategy.md).

### Infrastructure — `src/ChessAtlas.Infrastructure`

Implements the ports. This is the only project that touches EF Core, SQLite, the file system and
the network.

- `Persistence/` — `ChessAtlasDbContext`, the entity configurations, the versioned migration.
- `Openings/` — `OpeningCatalogRepository` (queries and writes), `OpeningCatalogImporter`
  (batched, deduplicating import).
- `DataSources/` — `TsvOpeningCatalogSource` (parses the upstream TSV), `LichessChessOpenings`
  (the identity, URI, licence and pinned commit of that one source).
- `DependencyInjection.cs` — service registration and the migration/tuning entry point.

### Desktop — planned

Avalonia UI + XAML, MVVM with CommunityToolkit.Mvvm. It will reference **Application only**.

Two rules that are non-negotiable and are the reason the layering exists:

- **No `DbContext`, no EF Core, no SQL in a view or in code-behind.** A view talks to a view model,
  a view model talks to a use case.
- **No heavy logic in a view model.** A view model adapts use-case results for display and forwards
  user intent. If a piece of logic needs a unit test that mentions chess, it belongs in Domain.

## Dependency rules, enforced how

| Rule | Mechanism |
| --- | --- |
| Domain references nothing | `ChessAtlas.Domain.csproj` has no `PackageReference` and no `ProjectReference` |
| Application sees only Domain | `ChessAtlas.Application.csproj` references Domain only |
| Infrastructure implements ports | Interfaces live in Application; implementations in Infrastructure |
| No EF in the UI | The Desktop project will not reference Infrastructure's persistence types |
| No unversioned schema changes | EF migrations are the only way the schema changes; see [ADR-0004](../decisions/ADR-0004-ef-core-schema-and-migrations.md) |

## Cross-cutting decisions

- **Long operations never block the caller's thread.** Use cases are `async`, take a
  `CancellationToken`, and report progress through `OperationProgress`. Imports stream; they do not
  buffer a file into memory.
- **Paging everywhere a list can be long.** `PagedResult<T>` is the return type, and page sizes are
  clamped by the query object rather than trusted from the caller.
- **Errors are values where they are expected, exceptions where they are not.** An unresolvable SAN
  in a 4-million-game archive is recorded on the game and skipped; a corrupt position in our own
  code is a bug and throws.
- **Nothing reaches the network without the user asking.** Data sources are an explicit, extensible
  layer with licence and provenance recorded alongside the data.
