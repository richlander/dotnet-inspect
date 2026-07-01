---
name: dotnet-inspect
version: 0.16.0
description: Find evidence instead of guessing for .NET packages, platform libraries, local assemblies, APIs, dependencies, and version-to-version API changes.
---

# dotnet-inspect

Use dotnet-inspect for evidence instead of guesses about .NET packages, platform
libraries, assemblies, APIs, dependencies, or API version diffs. Focused skills
are listed at the end.

```bash
dnx dotnet-inspect -y -- <command>
```

## Common starts

| Goal | Command |
| ---- | ------- |
| Find an API | `find Pattern` includes platform/BCL types; add `--project path/to/project` when project references should be in scope. |
| Inspect a type | `type Type --package Foo`; add `--all` for non-public/hidden members. |
| Inspect overloads | `member Type --platform Lib -m Name -S "Member Index"` |
| Select an overload | `member Type --platform Lib Name:1` or `Name~digest` |
| Compare APIs | `diff --package Foo@old..new --breaking` (`--additive` new APIs); `--alloc-regressions` for perf regressions (allocations up, hot first). |
| Inspect packages | `package Foo -S Signals`, `--library`, `--path @readme --content`. |
| Inspect libraries | `library Foo` or `library path/to.dll`; add `--platform`, `-S Signals`. |
| Relationships | `depends Type`, `extensions Type`, `implements Interface`. |

## Member lookup

Run `find Name` when scope is unknown, inspect the type, then `-S "Member Index"`
to list overloads. Select with `Name:N` (1-based) or `Name~digest` (stable). A
selected overload defaults to `Signature`. You can also pass a fully-qualified
`Namespace.Type.Member` and the tool finds the type/member split — no scope.

```bash
dnx dotnet-inspect -y -- find JsonSerializer
dnx dotnet-inspect -y -- member JsonSerializer --platform System.Text.Json -m Serialize -S "Member Index"
dnx dotnet-inspect -y -- member JsonSerializer --platform System.Text.Json Serialize:1 -S Signature
dnx dotnet-inspect -y -- member System.Text.Json.JsonSerializer.Serialize -S "Member Index"
```

## Tips

- Default output is Markdown; for formats, `-D`/`-S` discovery, projection, and
  limits, load `dotnet-inspect skill query`.
- Add `--project <csproj|dir|project.assets.json>` when project-referenced packages should be in scope; it reads existing restored assets, so restore/build first if dependencies changed.
- Common BCL types resolve without scope: `type string`, `type 'List<T>'`. Quote generics and patterns: `member 'Dictionary<TKey,TValue>'`, `-S "Async*"`.
- Unpinned packages use latest stable; add `--preview` for prerelease APIs.
