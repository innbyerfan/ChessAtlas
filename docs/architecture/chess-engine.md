# The chess engine

Everything that changes a board position lives in `src/ChessAtlas.Domain/Chess/`. Nothing in this
directory references a database, a UI toolkit or a logging framework — the project has no package
references at all.

## Why it is written from scratch

Before writing any of it, the maintained .NET chess-rules packages were surveyed. None was
suitable: `Chessie` 0.6.0 turned out to be an F# package manager rather than a chess library, and
the rest (`ChessDotNet` 1.0.0, `Gera.Chess` 1.2.0, `ChessKit.ChessLogic` 0.5.0.6, `pax.chess`
0.7.0, `LynxChess.Lynx` 1.11.0, `pgn.NET` 1.2.1) were either unmaintained, engine-specific or
incomplete on the rules this product depends on.

The alternative to writing the rules was to build the product on a dependency nobody maintains.
See [ADR-0002](../decisions/ADR-0002-self-contained-chess-engine.md).

## `Position`

`Position` is the single source of truth for a board state. It is **mutable, with an explicit undo
stack**, because move generation and search need to make and unmake millions of moves and cannot
pay for an allocation each time.

The API separates two things that are easy to confuse:

| Method | Contract |
| --- | --- |
| `PlayMove(Move)` | The **only** entry point that validates. Throws `IllegalMoveException` on an illegal move. Use this everywhere a move arrives from outside — the UI, a PGN file, an engine. |
| `TryPlayMove(Move)` | Same, but returns `false` instead of throwing. |
| `MakeMove(Move)` | **Trusted.** No validation. Only for internal code that has already established legality — the perft walker, the SAN renderer comparing candidates. |
| `UndoMove()` | Restores the previous position, including castling rights, the en-passant square and both clocks. |

Other members: `FromFen` / `ToFen`, `At`, `FindKing`, `IsSquareAttackedBy`, `IsSideInCheck(Side)`,
`IsInCheck` (the side to move), `GeneratePseudoLegalMoves` / `GenerateLegalMoves`, `IsLegal`,
`EvaluateStatus`, `IsThreefoldRepetition`, `HasInsufficientMaterial`, `Clone`, `ZobristKey`,
`ToPositionKey`, `ToAsciiBoard`.

Note the naming: `IsInCheck` is a property about the side to move, and `IsSideInCheck(Side)` is the
method for an arbitrary side. They cannot share a name — C# rejects a property and a method with
the same signature on one type.

### Legality

Legality is decided by making the move and asking whether our own king is attacked, not by
pattern-matching pins. That is slower and completely reliable, which is the right trade for a
product whose correctness is the point. `GenerateLegalMoves` is the pseudo-legal list filtered this
way.

Castling is illegal if the king is in check, if a square it crosses is attacked, or if the
destination is attacked — but a square the king never stands on (b1 on the queenside) being attacked
is irrelevant, and that case is tested explicitly.

## Position identity and transpositions

`ToPositionKey()` is the canonical identity of a position:

```csharp
$"{placement} {sideToMove} {castling} {enPassantOrDash}"
```

Two deliberate details:

- **The en-passant square is included only when a legal en-passant capture actually exists.** A
  double push that nobody can capture creates no real difference in what can be played, and
  including it would split one position into two.
- **The halfmove and fullmove counters are excluded.** They are bookkeeping, not position.

This matters for the opening catalogue: the same position reached by two move orders must be one
position. See [`opening-catalogue.md`](opening-catalogue.md).

`ZobristKey` is the incremental 64-bit hash used for repetition detection. It is *not* the identity
— hashes collide — which is why the position key is a string.

## SAN

`San.ToSan(position, move)` and `San.TryParse(position, text, out move)`.

Disambiguation follows the PGN standard: file if the file disambiguates, otherwise rank, otherwise
both. A test covers the case a file cannot resolve — knights on a1, a5 and d4 all reach b3, so a1
and a5 must be written `N1b3` and `N5b3` while d4 is `Ndb3`.

Parsing works by **rendering every legal move and comparing normalised strings** (stripping
`x - = + # ! ?` and folding `0`-castling to `O`) rather than by writing a second parser. Parse and
render therefore round-trip by construction, and an unresolvable SAN is unresolvable for the same
reason in both directions. The cost is generating the legal move list per parse, which is fine
for import and irrelevant next to I/O.

## PGN

`PgnReader` is **streaming**: `IEnumerable<PgnGame>` over a `TextReader`, one game materialised at a
time. A 9 GB archive must never be loaded into memory, and the caller decides when to stop
enumerating.

It is deliberately forgiving. A game whose SAN cannot be resolved is flagged with
`HasErrors` and its `ParseWarnings`, and the rest of the line is skipped — an importer that dies on
game 4,000,000 is worse than one that reports the game and moves on.

`PgnWriter` writes the seven-tag roster, preserves comments and NAGs, nests variations, and wraps
at a fixed column so diffs stay readable.

> The output cursor is a small class, not a struct. It is passed into the recursive variation
> writer by value, and as a struct each call got its own copy of the column counter — so the caller
> still read column zero and glued the result token onto the last move, producing `3. Bb5 a61-0`.
> Two tests caught it. The class comment records why.

## Validation: perft

`tests/ChessAtlas.Domain.Tests/Chess/PerftTests.cs` counts leaf nodes at increasing depth and
compares them to the published figures for the standard start position, Kiwipete and positions 3–6,
including `perft(5)` from the start position (**4,865,609** nodes).

Perft is the only honest test of a move generator. It is sensitive to every edge case at once —
castling through check, en passant, promotions, pins — and it cannot pass by accident. Individual
rule tests explain *which* rule broke; perft proves none of them did.
