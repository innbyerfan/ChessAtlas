# ADR-0001: Avalonia 11.3 over Avalonia 12

**Status:** accepted
**Date:** 2026-10-13

## Context

Chess Atlas needs a cross-platform desktop UI. Avalonia is the choice: free, open source (MIT),
self-drawn so it looks and behaves identically on Windows, Linux and macOS, and mature enough for
long-lived applications.

The question is which major version. The latest is **12.1.4**; the current long-term line is
**11.3.22**.

## Decision

Pin **Avalonia 11.3.22** across `Avalonia`, `Avalonia.Desktop`, `Avalonia.Themes.Fluent`,
`Avalonia.Themes.Simple`, `Avalonia.Fonts.Inter`, `Avalonia.Diagnostics`, `Avalonia.Headless` and
`Avalonia.Headless.XUnit`.

`Avalonia.Controls.DataGrid` is pinned separately at **11.3.13** because it lags the rest of the
11.3 line and 11.3.22 does not exist for it.

Avalonia 12 is deliberately not used.

## Reasoning

The decisive factor is not feature set, it is **verification**. This project can only be built and
tested on CI; there is no interactive desktop in the development sandbox to click through. Adopting
a new major version of a UI framework under those conditions means discovering breaking changes
through build errors alone, with no way to see whether a control still renders correctly.

A new major also means new control templates, new theme contracts and new styling APIs — exactly the
surface this product leans on hardest, given the 12-theme requirement and per-use-case theme
profiles.

11.3 is the line with the largest body of working examples and the most stable styling contract.

## Consequences

- Every Avalonia package must be pinned in `Directory.Packages.props`; none may float.
- `Avalonia.Controls.DataGrid` carries its own version. When the rest of the line advances, check
  whether DataGrid has caught up before bumping.
- The upgrade path to 12.x is a deliberate future task, not a drive-by version bump. It should be
  done when the desktop shell exists and can actually be exercised.
- No paid Avalonia controls or commercial tooling. Everything used here is free and MIT-licensed.
