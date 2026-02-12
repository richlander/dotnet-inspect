# Scope Simplification (Implemented)

This document describes the scoping system for search commands (`find`, `extensions`, `implements`) and the removal of the `platform` command.

## Motivation

The tool originally required a local .NET SDK installation and exposed platform assemblies from the local SDK packs directory. The `platform` command was the entry point for browsing these assemblies. Recent changes download all runtime and targeting packs from NuGet, eliminating the dependency on a local SDK install. This means:

- The tool runs on any machine. No global .NET install needed.
- The "what's on your machine" story is no longer relevant — content comes from NuGet.
- The version ambiguity problem is gone. Previously, bare names resolved to whatever was installed locally, which could be 6 months old. Now the tool always fetches a specific version.
- The runtime/aspnetcore framework split is an implementation detail that doesn't need to be exposed in the CLI surface.

The `library` command already exposes everything useful about platform assemblies. The `platform` command is redundant.

## Changes

### Remove the `platform` command

Everything useful about `platform` is already available through `library` and the search commands. Remove the command entirely.

### Remove `--framework` from search commands

The `--framework` flag requires users to know the framework shorthand names (`runtime`, `aspnetcore`, `netstandard`). These are implementation details. No user has requested fine-grained framework selection. Remove the flag.

If a user makes a case for it, it can be re-added.

### Replace `--framework` with `--platform` on search commands

A single `--platform` flag means "search all recognized platform libraries" (runtime, aspnetcore, netstandard). Users don't need to know or care which ref pack contains a given type.

### Add `--extensions` scope flag

Searches a curated set of `Microsoft.Extensions.*` NuGet packages. These are the same libraries that ship in the ASP.NET Core ref pack, but as standalone NuGet packages they may be at a newer version than the platform. This matters when a project references a newer package version than what's in the target framework (e.g., Microsoft.Extensions.DependencyInjection 11.0 on .NET 10) — the package reference is real and doesn't collapse via [package pruning](https://learn.microsoft.com/dotnet/core/compatibility/sdk/10.0/nu1510-pruned-references).

Note: This flag name overlaps with the `extensions` subcommand (find extension methods). Consider whether this causes confusion in practice. Alternatives: `--ms-extensions`.

### Add `--aspnetcore` scope flag

Searches a curated set of `Microsoft.AspNetCore.*` NuGet packages. Same rationale as `--extensions` — NuGet versions may be ahead of what's in the platform ref pack.

### Default scope

When no scope flags are provided, the default scope is platform (all frameworks) plus Microsoft.Extensions.AI. The ASP.NET Core ref pack already includes most `Microsoft.Extensions.*` and `Microsoft.AspNetCore.*` libraries, so the platform alone provides broad coverage. Microsoft.Extensions.AI is the only high-value package not yet in any ref pack.

The `--curated` flag is hidden but accepted, so maintainers can test the default scope explicitly.

### No `--dotnet` flag

Earlier iterations had a `--dotnet` flag meaning "search everything Microsoft ships." This is unnecessary because the default scope already provides broad coverage (platform + Microsoft.Extensions.AI). For the rare case where you want platform plus the NuGet-specific versions of extensions and aspnetcore packages, combine flags explicitly: `--platform --extensions --aspnetcore`.

### Tips on no-arg invocations

When `find`, `extensions`, or `implements` are invoked with no arguments, show tips that teach the scoping system:

```text
Tips:
find Chat*                                # search default scope
find Chat* --platform                     # platform libraries only
find Chat* --extensions                   # Microsoft.Extensions.* packages
find Chat* --aspnetcore                   # ASP.NET Core packages
find Chat* --package Newtonsoft.Json       # specific package
find Chat* --platform --extensions         # combine scopes
```

Same pattern for `extensions` and `implements`, with example types appropriate to each command (e.g., `HttpClient` for extensions, `Stream` for implements).

## Scope summary

| Flag | What it searches |
| ---- | --------------- |
| *(no flags)* | platform + Microsoft.Extensions.AI |
| `--platform` | runtime + aspnetcore + netstandard ref packs |
| `--extensions` | curated Microsoft.Extensions.* NuGet packages |
| `--aspnetcore` | curated Microsoft.AspNetCore.* NuGet packages |
| `--package X` | specific NuGet package (unchanged) |
| `--library X` | specific local file (unchanged) |

Flags are combinable — use multiple flags to widen the search. `--platform --package Newtonsoft.Json` searches both. When `--platform` is combined with `--extensions` or `--aspnetcore`, platform wins for overlapping types (consistent with package pruning behavior).

## Package overlap and pruning

Most `Microsoft.Extensions.*` and `Microsoft.AspNetCore.*` libraries ship in the ASP.NET Core ref pack. When a project targets .NET 10 and references Microsoft.Extensions.DependencyInjection 10.0, the package reference collapses into the platform via [package pruning](https://learn.microsoft.com/dotnet/core/compatibility/sdk/10.0/nu1510-pruned-references) — the ref pack version wins.

However, if the project references Microsoft.Extensions.DependencyInjection **11.0** on .NET 10, the package reference is real. The NuGet version is ahead of the platform. This is when `--extensions` and `--aspnetcore` are most useful — they search the NuGet package versions, which may have APIs not yet in the platform ref pack.

When both `--platform` and `--extensions`/`--aspnetcore` are active, platform resolution wins for types that exist in the ref packs. This mirrors how pruning works at build time.

## Curated package lists

**Extensions** (`--extensions`):
- Microsoft.Extensions.DependencyInjection
- Microsoft.Extensions.Logging
- Microsoft.Extensions.Configuration
- Microsoft.Extensions.Options
- Microsoft.Extensions.Hosting
- Microsoft.Extensions.Http
- Microsoft.Extensions.Caching.Memory
- Microsoft.Extensions.AI

**ASP.NET Core** (`--aspnetcore`):
- Microsoft.AspNetCore.Authentication
- Microsoft.AspNetCore.Authorization
- Microsoft.AspNetCore.Components
- Microsoft.AspNetCore.Mvc.Core
- Microsoft.AspNetCore.SignalR

**Default** (no flags / `--curated` hidden):
- Everything in `--platform`
- Microsoft.Extensions.AI

The default is intentionally close to just `--platform`. The ASP.NET Core ref pack already includes most Extensions and AspNetCore libraries. Only Microsoft.Extensions.AI is added because it's high-value and not yet in any ref pack. As new packages emerge outside the platform, they can be added to the default without changing flag semantics.

## Migration

- `--framework runtime` → `--platform` (or just use the default)
- `--framework aspnetcore` → `--platform` (aspnetcore is included)
- `--framework runtime --framework aspnetcore` → `--platform`
- `--dotnet` → default (no flags), or `--platform --extensions --aspnetcore` for explicit union
- `platform` command → `library --platform <name>` or search commands with `--platform`

The `--framework` flag and `platform` command should produce a clear error message pointing to the replacement for one release before full removal.
