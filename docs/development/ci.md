# Continuous integration

Two workflows run on every push to `arena/**` and `main`.

## `ci.yml` — build and test

1. **Prepare** — truncates `ci-artifacts/ci-log.txt`, records the run id, SHA and `dotnet --info`.
2. **Restore / build** — `dotnet build -c Release /p:TreatWarningsAsErrors=true`, tee'd into the log.
3. **Test** — runs each test project as an executable:
   ```bash
   dotnet run --project tests/ChessAtlas.Domain.Tests -c Release --no-build
   ```
   Failures accumulate into one exit status so all three projects report, not just the first.
4. **Assert tests actually ran** — parses the xunit.v3 summary lines and fails on any failure, any
   error, or **zero tests discovered**. A green build with no tests proves nothing.
5. **Check EF Core model snapshot drift** — `dotnet ef migrations has-pending-model-changes`, with
   `if: always()` so it still reports when the tests fail.
6. **Publish the log** — commits `ci-artifacts/ci-log.txt` back to the branch with `[skip ci]`.

## `generate-migrations.yml` — regenerate the EF migration

Gated on `[gen-migrations]` appearing in the commit message (or a manual dispatch). It installs
`dotnet-ef` 10.0.12, builds the startup project, regenerates into `Persistence/Migrations` with
`--verbose`, applies the result to a scratch SQLite database to prove it works, and commits it back.
Its transcript is published to `ci-artifacts/migrations-log.txt` on success *and* failure.

## Why the logs are committed back

The development sandbox has no .NET SDK, so nothing can be built or run locally. Every claim about
whether the project compiles or its tests pass has to be verified on CI — and the Actions log viewer
is not reliably reachable from the sandbox.

Committing the transcript into the branch makes it readable with `git pull`. The cost is one extra
commit per run, which is cheap against the alternative of guessing.

**The log is truncated at the start of every run.** It is a tracked file, so an earlier version of
this workflow appended to it with `tee -a` and accumulated every previous run into one file — which
meant a diagnosis could be reading errors from three pushes ago. Truncation is load-bearing.

## Reading a failure

```bash
git pull
grep -oE "[A-Za-z0-9_./]+\([0-9]+,[0-9]+\): (error|warning) [A-Z]+[0-9]+: [^\[]*" \
  ci-artifacts/ci-log.txt | sort -u          # compiler diagnostics
grep -B1 -A6 '\[FAIL\]' ci-artifacts/ci-log.txt   # test failures
tail -45 ci-artifacts/ci-log.txt                  # the run's tail
```

## Current status

The last completed run reported **229 tests across 3 assemblies**. `ci.yml` runs on
`ubuntu-latest`; there is no Windows or macOS matrix yet, which is a real gap for a product whose
point is being cross-platform. Adding it is tracked as part of the desktop phase, where there will
be something platform-specific to test.
