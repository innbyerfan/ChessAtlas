# ADR-0002: A self-contained chess rules engine in Domain

**Status:** accepted
**Date:** 2026-10-13

## Context

Chess Atlas must never partially reimplement the rules of chess. Every board state change has to go
through a valid position model, and special cases — castling through check, en passant, promotion,
pins — have to be tested. The first question is whether to write that model or to adopt one.

The maintained .NET packages were surveyed before any code was written:

| Package | Finding |
| --- | --- |
| `Chessie` 0.6.0 | Not a chess library at all — it is an F# package manager. |
| `ChessDotNet` 1.0.0 | Unmaintained. |
| `Gera.Chess` 1.2.0 | Unmaintained. |
| `ChessKit.ChessLogic` 0.5.0.6 | Unmaintained, incomplete rule coverage. |
| `pax.chess` 0.7.0 | Unmaintained. |
| `LynxChess.Lynx` 1.11.0 | An engine, not a reusable rules library. |
| `pgn.NET` 1.2.1 | PGN only, no rules. |

None was credible for a product intended to last years.

## Decision

Write the rules in `src/ChessAtlas.Domain/Chess/`, with no external dependency, and validate them
with **perft** against published node counts.

## Reasoning

The rules of chess are a closed, fully specified problem. Unlike a database driver or a UI toolkit,
they will not change, so there is no maintenance burden that a third party would absorb on our
behalf — only a correctness burden, which we would carry either way.

Adopting an abandoned package would mean inheriting its bugs with no route to a fix. Writing them
means owning a problem that is small, well-understood and — crucially — **verifiable**.

Perft is what makes this safe rather than merely confident. It counts leaf nodes at depth and
compares against published figures, so it fails on any single defect in castling, en passant,
promotion or pin handling. It cannot pass by accident.

## Consequences

- `tests/ChessAtlas.Domain.Tests/Chess/PerftTests.cs` covers the standard start position, Kiwipete
  and positions 3–6, including `perft(5)` from the start position (4,865,609 nodes). These tests
  are the contract; they are not optional and must not be skipped to save CI time.
- `Position.PlayMove` is the only validating entry point. `MakeMove` is trusted and reserved for
  internal code that has already established legality.
- The engine has no dependency on UI language, locale or culture. Move ordering and PGN rules must
  never vary with the interface language.
- Performance is adequate for import, analysis display and training, but this is not a search
  engine. Playing strength comes from external UCI engines; this code only provides a correct board.
