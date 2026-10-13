# Source: lichess-org/chess-openings

| Field | Value |
| --- | --- |
| Upstream repository | https://github.com/lichess-org/chess-openings |
| Snapshot commit | `a6189a30dc273ccb21fc2536a9a2fefd5592a67a` |
| Snapshot date | 2026-10-13 |
| Retrieved via | `https://codeload.github.com/lichess-org/chess-openings/tar.gz/refs/heads/master` |
| License | CC0 1.0 Universal (see `LICENSE-CC0.txt`) |
| Row count | 3865 (824 A + 782 B + 1251 C + 645 D + 368 E, minus 5 header rows) |

## Format

Tab-separated, three columns, one header row per file:

```
eco<TAB>name<TAB>pgn
A00	Amar Opening	1. Nh3
A00	Amar Opening: Paris Gambit	1. Nh3 d5 2. g3 e5 3. f4
```

- `eco` — Encyclopaedia of Chess Openings code, `A00`–`E99`.
- `name` — hierarchical name; `Family: Variation: Subvariation`.
- `pgn` — move text in SAN with move numbers, no tag pairs and no result.

## What this data is — and is not

This is a **naming and ECO catalogue**, not an opening encyclopedia. It gives an
authoritative name and a short reference line for each position. It carries **no**
traps, no gambit theory, no evaluation statistics, no model games and no
recommendations.

Chess Atlas therefore imports every row as an `OpeningLine` with:

- `VerificationStatus = ReferenceVerified` for the naming/ECO/line itself, because the
  name comes from a maintained upstream catalogue;
- no `Kind` upgrade — a row is only promoted to `Gambit`, `Trap` or `CriticalPosition`
  by an explicit editorial action with its own recorded source. Nothing in this dataset
  is silently presented to a user as a known trap.

## Re-importing

```bash
curl -L https://codeload.github.com/lichess-org/chess-openings/tar.gz/refs/heads/master \
  | tar xz --strip-components=1 -C data/openings/lichess-chess-openings \
      chess-openings-master/a.tsv chess-openings-master/b.tsv \
      chess-openings-master/c.tsv chess-openings-master/d.tsv \
      chess-openings-master/e.tsv chess-openings-master/COPYING.txt
```

Then record the new commit SHA above.
