# ADR-0005: Opening lines carry provenance and a verification status

**Status:** accepted
**Date:** 2026-10-13

## Context

An opening explorer that invents names, variations, traps and statistics is worse than no opening
explorer, because the user cannot tell what it made up. Three failure modes have to be designed out:

1. Presenting a line derived from game data as if it were established theory.
2. Repeating an upstream label as though Chess Atlas had verified it.
3. Asserting a "trap" with no basis.

## Decision

Every `OpeningLine` carries three orthogonal facts, and the domain enforces how they may be set:

| Field | Meaning |
| --- | --- |
| `Kind` | The classification itself: MainLine, Gambit, Trap, … |
| `KindOrigin` | **Who asserted it**: `UpstreamName`, `ExtractedFromGames`, or editorial assertion |
| `VerificationStatus` | How much to trust it, including `ExtractedFromGames` |

Plus, on every line: `ProvenanceId` → a `DataProvenances` row recording provider, URI, licence,
version or commit, checksum, retrieval time and redistribution permission.

Enforced in the domain, not merely documented:

- `OpeningLine.Create` and `OpeningLine.Reclassify` **throw** if a `Trap` classification is
  editorially asserted without a `Rationale`.
- A line from game data is `VerificationStatus.ExtractedFromGames` and is never rendered as theory.
- An upstream `Gambit`/`Trap` label is `ClassificationOrigin.UpstreamName`, reported as what the
  source calls it.

## Reasoning

A label and its provenance are different facts, and conflating them is the specific way opening
databases become untrustworthy. "The lichess catalogue names this the Stafford Gambit" and "this is
a verified trap that wins a piece" are not the same claim, and the user is entitled to know which
one they are looking at.

Making the distinction a required part of the model — rather than a note in the documentation —
means the wrong presentation is not merely discouraged, it is not representable.

## Consequences

- The UI must render origin and verification status alongside the name. A bare name with no origin
  is a presentation bug.
- `DataProvenances` is unique on `(Provider, VersionOrCommit)`, so one snapshot is one row however
  many lines came from it, and every line can name its source.
- `ProvenanceId` deletes with `SetNull`: retiring a data snapshot leaves the line in place with a
  null origin rather than destroying the user's annotated data.
- Nothing is bundled. The catalogue is imported by the user, at the user's request, and its licence
  travels with it. See [`../development/data-sources.md`](../development/data-sources.md).
- A future feature that derives lines from the user's own game database must mark them
  `ExtractedFromGames` and must never merge them into the imported reference set.
