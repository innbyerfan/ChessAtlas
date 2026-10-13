# Storage strategy

Two very different kinds of data live in this application, and pretending they are the same is how
chess software ends up unable to open a 9 GB archive.

## What EF Core owns

Structured application data where correctness, relationships and schema evolution matter more than
raw throughput:

- the opening catalogue, aliases, lines and provenance records
- repertoires and training state
- engine registrations, settings, theme profiles
- metadata about imported collections

EF Core gives this versioned migrations, change tracking, transactions and LINQ, and at this scale
(Thousands to low millions of rows) it is simply the right tool.

## What EF Core does not own

Multi-million game archives. The reasons are structural, not performance tuning:

1. **EF's change tracker is built for edits, not bulk insert.** Tracking four million entities to
   write them once costs memory and time for a benefit that never arrives.
2. **A `DbSet<Game>` invites `ToList()`.** One careless query loads the archive.
3. **PGN is variable-length text.** A game's move text, comments and variations do not fit a
   relational row without either a size cap or an overflow table that turns every read into a join.

Game collections therefore get their own store with bulk-write and streaming-read paths, designed
when that phase is built. The DbContext documents this boundary in its own summary comment so the
decision is not rediscovered by accident.

## Rules that follow from this

**Nothing loads a whole dataset into memory.** `PgnReader` is `IEnumerable<PgnGame>` over a
`TextReader`, so peak memory is roughly one game regardless of file size, and the caller decides
when to stop enumerating.

**Every list-returning query is paged.** Use cases return `PagedResult<T>`, and `OpeningSearchQuery`
clamps its own page size rather than trusting the caller. "Return everything" is not an available
operation on a large collection.

**Long work is cancellable and reports progress.** Use cases are `async`, take a
`CancellationToken`, and surface `OperationProgress`. An import that cannot be cancelled is an
import the user will force-quit, and a force-quit import must not leave a half-written catalogue —
which is why the unique index on `(OpeningId, StartingFen, UciText)` exists: restarting an
interrupted import is idempotent rather than duplicating.

**Large optional data is imported, never seeded.** The opening catalogue arrives through an
importer as a user-initiated action, not through a migration. A migration carrying 3,865 rows of
reference data would slow every schema change forever, couple schema history to data history, and
make it impossible to update one without the other.

## SQLite specifics

The connection is opened with `Data Source=` only. `SqliteConnectionStringBuilder` exposes neither
a journal mode nor a cache size — those are PRAGMAs, not connection-string properties — so they are
applied to the open connection before migrating, in
`DependencyInjection.MigrateAsync`:

```csharp
await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");
await db.Database.ExecuteSqlRawAsync("PRAGMA cache_size=-8000;");
```

WAL lets a reader continue while an importer writes, which is the difference between a responsive UI
during a long import and a frozen one. `-8000` is an 8 MB page cache, expressed in kibibytes by the
negative-sign convention.
