# ADR-0003: Testing with xunit.v3, executed as project executables

**Status:** accepted
**Date:** 2026-10-13

## Context

The project needs a real test suite, not "it compiles". The obvious choice on .NET is xUnit with
`dotnet test`. On the .NET 10 SDK that combination no longer works the way it used to.

## Decision

Use **xunit.v3 4.0.2**, `xunit.runner.visualstudio` 4.0.1 and `Microsoft.NET.Test.Sdk` 18.10.1,
and run each test project **as an executable** rather than through `dotnet test`:

```bash
dotnet run --project tests/ChessAtlas.Domain.Tests -c Release --no-build
```

## Reasoning

`Microsoft.Testing.Platform` (MTP) has replaced VSTest. On the .NET 10 SDK, `dotnet test` still
routes to the VSTest target and fails at test time with:

> Testing with VSTest target is no longer supported by Microsoft.Testing.Platform on .NET 10 SDK
> and later.

This cost four CI rounds to pin down, because `dotnet test --help` on SDK 10.0.401 prints the
**VSTest** option set — `--logger`, `--filter`, `--collect`, `--blame` — so the command looks
supported right up until it is not. Setting
`<TestingPlatformDotnetTestSupport>true</TestingPlatformDotnetTestSupport>` did not clear it when
`dotnet test` targeted the solution.

xunit.v3 projects compile to executables with their own entry point, so bypassing `dotnet test`
entirely is not a workaround — it is the supported way to run them.

## Consequences

Test projects need three properties, and each one exists for a specific reason:

```xml
<OutputType>Exe</OutputType>                                  <!-- xunit.v3 runs as an executable -->
<GenerateProgramFile>false</GenerateProgramFile>              <!-- Microsoft.NET.Test.Sdk emits an entry point too; two is a build error -->
<TestingPlatformDotnetTestSupport>true</TestingPlatformDotnetTestSupport>
<NoWarn>$(NoWarn);xUnit1051</NoWarn>
```

`xUnit1051` wants `TestContext.Current.CancellationToken` at every cancellation point. Under
`TreatWarningsAsErrors` it becomes an error, and 73 instances failed the build. The tests pass a
token explicitly wherever cancellation is the thing under test and use the default token elsewhere
on purpose, so it is suppressed **per test project with the reason recorded in the project file** —
not globally, and not by turning off `TreatWarningsAsErrors`.

**CI fails if zero tests ran.** The assertion step parses the xunit.v3 summary line
(`Total: n, Errors: n, Failed: n, Skipped: n`) and derives the passing count, because that summary
prints no `Passed:` figure. A green build with no tests discovered is a failure, not a pass.

## Related traps, recorded so they are not paid for twice

- `ArgumentException.ThrowIfNull` **does not exist**. It is `ArgumentNullException.ThrowIfNull`.
- `SqliteConnectionStringBuilder` has no `JournalMode` and no `CacheSize` property, and there is no
  `SqliteJournalMode` type. Those are PRAGMAs; apply them with `ExecuteSqlRawAsync`.
- EF Core's private parameterless constructor must assign every non-nullable property or `CS8618`
  fires.
- A `readonly struct` cannot have a mutable instance field (`CS8340`).
- `IAsyncEnumerable<T>` has no `ToListAsync` in the BCL. Use an `await foreach` helper;
  `System.Linq.Async` was deliberately not added.
- `Assert.ThrowsAsync<T>(() => ...)` inside an expression-bodied `void` test never awaits and passes
  trivially. Write `async Task ... => await`.
