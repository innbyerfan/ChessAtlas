# The opening catalogue

The opening model is the part of Chess Atlas most likely to quietly become dishonest, so it is
designed around one rule: **every line knows where it came from and how much to trust it.**

## Four tables

Verified against `20261013113703_InitialSchema.cs`.

### `Openings`

A node in the name hierarchy: "Sicilian Defence", "Sicilian Defence: Najdorf Variation".

`Id`, `CanonicalName`, `TerminalName`, `FamilyKey`, `Depth`, `EcoCodeValue`, `Description`,
`Difficulty`, `ParentOpeningId`, `CreatedAtUtc`, `UpdatedAtUtc`.

- `CanonicalName` is the full path (`Sicilian Defence: Najdorf Variation`) and is **unique** — it is
  the node's identity, so re-importing the same catalogue finds these rows instead of duplicating
  them.
- `ParentOpeningId` is a self-referencing FK with `Restrict`, so a parent cannot be deleted out from
  under its children.
- Indexes on `FamilyKey`, `EcoCodeValue` and `Depth` serve the three ways the catalogue is browsed.

### `OpeningAliases`

`Id`, `OpeningId`, `Alias`, `Language`. One opening has many names ("Najdorf", "Najdorf Variation",
and the same again in other languages).

Unique on `(OpeningId, Alias, Language)`.

### `OpeningLines`

A concrete move sequence with its classification.

`Id`, `OpeningId`, `ParentLineId`, `Name`, `EcoCodeValue`, `StartingFen`, `SanText`, `UciText`,
`PlyCount`, `TerminalPositionKey`, `Kind`, `KindOrigin`, `VerificationStatus`, `Difficulty`,
`Victim`, `Rationale`, `Notes`, `ProvenanceId`, `CreatedAtUtc`, `UpdatedAtUtc`.

- `TerminalPositionKey` is indexed. "What is named at this position?" becomes an index seek rather
  than a scan, and lines that arrive by different move orders land on the same row set.
- Unique on `(OpeningId, StartingFen, UciText)`: one opening may not hold the same sequence twice.
  This is also what makes an interrupted import safe to restart.
- `ProvenanceId` → `DataProvenances` with `SetNull`, so retiring a data snapshot leaves the line
  standing with a null origin instead of destroying it.
- `ParentLineId` is a self-FK with `Restrict`.

### `DataProvenances`

`Id`, `Provider`, `Uri`, `License`, `VersionOrCommit`, `Checksum`, `RetrievedAt`,
`RedistributionAllowed`, `Notes`. Unique on `(Provider, VersionOrCommit)` — one row per snapshot,
no matter how many lines came from it.

### A SQLite trap worth recording

SQLite treats NULLs as **distinct** in unique indexes. A unique index over
`(OpeningId, Alias, Language)` would therefore happily accept the same alias with a NULL language
an unlimited number of times. Both nullable columns that appear in a unique index
(`OpeningAlias.Language`, `DataProvenance.VersionOrCommit`) are value-converted so that "no value"
is stored as the empty string and read back as `null`. The constraint stays real and the domain
model still sees `string?`.

## Classification carries its origin

Three enums work together, and the distinction between them is the whole point:

| Field | Question it answers |
| --- | --- |
| `Kind` | Main line / Gambit / Trap / … |
| `KindOrigin` | **Who said so** — upstream naming, extracted from games, or editorially asserted |
| `VerificationStatus` | How much to trust it — verified reference theory vs `ExtractedFromGames` |

Consequences that are enforced in the domain, not just documented:

- A line derived from game data is `VerificationStatus.ExtractedFromGames` and is **never**
  presented as established theory.
- A `Gambit` or `Trap` label that came from the upstream name is `ClassificationOrigin.UpstreamName`
  — reported as "this is what the source calls it", not as a claim by Chess Atlas.
- An **editorially asserted** `Trap` must carry a `Rationale`. `OpeningLine.Create` and
  `OpeningLine.Reclassify` throw without one. A trap with no stated reason is a rumour, and the
  model refuses to store it as fact.

See [ADR-0005](../decisions/ADR-0005-opening-provenance-and-verification.md).

## Transpositions are first-class

A position does not belong to one path or one name. `OpeningLine` stores `TerminalPositionKey`
alongside its own path, so the same position reached as a Najdorf and as a Scheveningen is two rows
that a single query on the position key finds together. There is no assumption anywhere that a
position has one canonical opening, and a test
(`The_same_position_reached_by_different_names_is_stored_twice_and_found_together`) asserts it.

## Where the data comes from

The initial catalogue is
[lichess-org/chess-openings](https://github.com/lichess-org/chess-openings), **CC0 1.0**, pinned at
commit `a6189a30dc273ccb21fc2536a9a2fefd5592a67a`. Three TSV columns — `eco`, `name`, `pgn` — where
`name` is a `Family: Variation` hierarchy and `pgn` is SAN. 3,865 data rows.

It arrives through `IOpeningCatalogSource` / `TsvOpeningCatalogSource` and is imported by
`OpeningCatalogImporter`. It is **not** EF seed data and **not** in a migration: 3,865 rows of
optional reference data in a migration would make every schema change drag the data along. See
[`storage-strategy.md`](storage-strategy.md) and
[`../development/data-sources.md`](../development/data-sources.md).
