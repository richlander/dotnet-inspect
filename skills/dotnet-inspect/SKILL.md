---
name: dotnet-inspect
version: 0.16.0
description: Find evidence instead of guessing for .NET packages, platform libraries, local assemblies, APIs, dependencies, and version-to-version API changes.
---

# dotnet-inspect

Use dotnet-inspect for evidence about .NET packages, platform libraries, assemblies, APIs, dependencies, or API version diffs.

```bash
dnx dotnet-inspect -y -- <command>
```

`-y` skips interactive confirmation, including after package updates; `--` sends remaining options to dotnet-inspect, so `--help` does not show `dnx` help.

## Common starts

| Goal | Command |
| ---- | ------- |
| Find an API | `find Pattern` includes platform/BCL types; add `--project path/to/project` when project references should be in scope. |
| Inspect a type | `type Type --package Foo`; add `--all` for non-public/hidden members. |
| Inspect overloads | `member Type --platform Lib -m Name -S "Member Index"` |
| Select an overload | `member Type --platform Lib Name:1` or `Name~digest` |
| Find rendered body syntax | `body-shape ObjectCreationExpression --library path/to.dll`; load `skill decompiler` for stable kinds and coordinates. |
| Compare APIs | `diff --package Foo@old..new --breaking` (`--additive` new APIs); `--alloc-regressions` for perf regressions (allocations up, hot first). |
| Trace API evolution | `timeline --package Foo@old..new --type Type --members --at all`; omit `--at` to inspect the vector without acquiring packages. |
| Inspect packages | `package Foo -D` discovers effective package evidence; bare `-S` returns high-value fixed-length sections, while `-S @Package` requests the complete package-native lens. Load `skill private-feeds` for authenticated/custom sources. |
| Inspect libraries | `library Foo -D` is a cheap target-aware catalog; add `--effective` to run full probes. Bare `-S` returns high-value fixed-length sections; load `skill metadata` for raw ECMA-335 tables/heaps. |
| Relationships | `depends Type`, `extensions Type`, `implements Interface`. |

## Member lookup

Run `find Name` when scope is unknown, inspect the type, then `-S "Member Index"` to list overloads.
Select with `Name:N` (1-based) or `Name~digest` (stable). A selected overload
defaults to `Signature`. A fully-qualified `Namespace.Type.Member` needs no scope.

```bash
dnx dotnet-inspect -y -- find JsonSerializer
dnx dotnet-inspect -y -- member JsonSerializer --platform System.Text.Json -m Serialize -S "Member Index"
dnx dotnet-inspect -y -- member JsonSerializer --platform System.Text.Json Serialize:1 -S Signature
dnx dotnet-inspect -y -- member System.Text.Json.JsonSerializer.Serialize -S "Member Index"
```

## Tips

- Default output is Markdown. `package -D` is effective for a target; `library -D` is a cheap orientation gesture, while named/category discovery is structural unless `--effective` is added. Bare `-S` returns high-value, fixed-length, network-free base sections. For the full query model, load `dotnet-inspect skill query`.
- Add `--project <csproj|dir|project.assets.json>` when project-referenced packages should be in scope; it reads existing restored assets, so restore/build first if dependencies changed.
- Common BCL types resolve without scope: `type string`, `type 'List<T>'`. Quote generics and patterns: `member 'Dictionary<TKey,TValue>'`, `-S "Async*"`.
- Unpinned packages use latest stable; add `--preview` for prerelease APIs.
