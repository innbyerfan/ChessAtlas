# Data sources

Chess Atlas ships **no** opening, puzzle or game data. Everything is imported by the user, on
request, from a source that is named, versioned and licence-recorded in the application's own
database.

## Rules every source must satisfy

These are not aspirations; they are the contract a new `IOpeningCatalogSource` implementation has to
meet.

1. **One extensible layer.** Sources live in `src/ChessAtlas.Infrastructure/DataSources/` behind
   `IOpeningCatalogSource`. No download URL or API endpoint may be hardcoded in a use case, a view
   model or a view.
2. **Optional and user-initiated.** Nothing is downloaded automatically, and no large dataset is
   fetched without telling the user first.
3. **Transparent.** The UI shows what will be fetched, from where, and the estimated size and
   required free space, before anything starts.
4. **Cancellable**, with progress, in the background, never on the UI thread.
5. **Checksum-verified** where the source publishes one.
6. **Streaming.** `TsvOpeningCatalogSource` reads line by line; a whole archive is never buffered.
7. **Provenance recorded.** Every import writes a `DataProvenances` row: provider, URI, licence,
   version or commit, checksum, retrieval time, redistribution permission.
8. **Rate-limited, cached and offline-capable** for anything over the network, with an explicit
   User-Agent and timeouts.

## Registered sources

### lichess-org/chess-openings — the opening catalogue

| | |
| --- | --- |
| Provider | `lichess-org/chess-openings` |
| Repository | https://github.com/lichess-org/chess-openings |
| Licence | **CC0 1.0** (public domain) |
| Pinned snapshot | commit `a6189a30dc273ccb21fc2536a9a2fefd5592a67a` |
| Format | three TSV columns: `eco`, `name`, `pgn` |
| Size | 3,865 data rows |

`name` is a `Family: Variation` hierarchy; `pgn` is SAN. The hierarchy becomes the `Openings` tree
and the SAN becomes an `OpeningLines` row with `TerminalPositionKey` computed by replaying it.

Identity is held in `DataSources/LichessChessOpenings.cs` as constants, and
`data/openings/lichess-chess-openings/SOURCE.md` records the same facts for the user's own copy.
**Both must be updated together** when the snapshot is refreshed, or the provenance records stop
describing what is actually on disk.

Because it is CC0, this source may be cached and reused freely. It is still not bundled in the
repository or the distribution — it is optional data, and importing it is the user's action.

## Sources referenced by the roadmap, not yet implemented

Each of these has terms that must be respected when it is built. They are recorded here so the
constraint is not discovered late:

- **The Week in Chess (TWIC)** — free for personal use, **all rights reserved**. Its content must
  **not** be bundled in a distributed package. Redistribution and commercial terms need separate
  review before anything is shipped.
- **Chess.com PubAPI** — public and read-only, limited to publicly accessible data.
- **Lichess puzzle database** — large. It must never be downloaded automatically, never loaded
  whole into memory, and always streamed in batches with progress and cancellation.
- **Stockfish** — GPL-3.0. Distributing a binary obliges source availability. Neural-network and
  model acquisition must be explicit and user-controlled. Verify official checksums, validate the
  executable path, and retain licence and source information.
- **Leela Chess Zero** — as above for engine acquisition.
- **Lucide icons** — ISC licence. The licence file must ship with the icons.

## Online accounts

Not implemented. When it is: never ask a user to store a chess account password, use only official
authorised authentication, store tokens with OS-secure mechanisms where possible, never log
secrets, and allow disconnecting, purging tokens and deleting synced data.

## Attributing a puzzle to an opening

A planned feature, and a specific trap. "A puzzle from a game tagged with this opening" is not the
same claim as "a tactic that occurs in this opening line". The move and position must be validated
before a precise label is asserted, and the two must be presented differently. See
[ADR-0005](../decisions/ADR-0005-opening-provenance-and-verification.md).
