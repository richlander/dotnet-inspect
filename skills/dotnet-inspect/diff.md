---
name: dotnet-inspect-diff
version: 0.1.0
description: Compare .NET API surfaces between package, platform, or local-library versions for migration and release-note work, plus how to resolve the versions to diff.
---

# dotnet-inspect: diff and version compatibility

Use this skill for upgrade, migration, and release-note work: see what changed
in an API surface between two versions, then drill into the affected members.

```bash
dnx dotnet-inspect -y -- <command>
```

## Choose a source

`diff` compares a version range from one of three sources:

```bash
dnx dotnet-inspect -y -- diff --package System.Text.Json@9.0.0..10.0.0
dnx dotnet-inspect -y -- diff --platform System.Runtime@9.0.0..10.0.0
dnx dotnet-inspect -y -- diff --library old/Foo.dll..new/Foo.dll
```

Use `--platform` for in-box .NET libraries (add `--framework aspnetcore` for the
ASP.NET Core shared framework), `--package` for NuGet, and `--library` for two
local builds. With no source flag, the first argument is the package range.

## Pick a lens

Default output lists every change. Narrow to the question you are answering:

- `--breaking` — only breaking changes (migration work).
- `--additive` — only new APIs (release-note work).
- `--changed` — only in-place changes to members present in both versions.
- `--name-only` — just the changed type names; `--legend` explains the symbols.

Narrow noisy diffs with `-t TypeName` (repeatable) and widen with `--all`
(non-public, hidden, obsolete). Then inspect the affected API at the new
version:

```bash
dnx dotnet-inspect -y -- diff --package System.Text.Json@9.0.0..10.0.0 --breaking -t JsonSerializer
dnx dotnet-inspect -y -- member JsonSerializer --package System.Text.Json@10.0.0 -m Serialize -S "Member Index"
```

## Find the versions to diff

Version resolution is cache-first (local cache answers in milliseconds; nuget.org
costs ~1–4s). Three behaviors:

- `Foo --version` — the version in the local cache; same version a bare `Foo`
  inspection will use. Fast default.
- `Foo --latest-version` — the absolute latest on nuget.org (always network).
- `Foo --versions [N]` — list published versions (newest N); add `--preview`
  for prerelease.

Pin explicitly with `@`: `Foo@9.0.0` (pinned, no network if cached),
`Foo@latest` (always checks nuget.org). Unpinned names prefer cache and refresh
on TTL expiry.
