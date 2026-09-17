---
name: dotnet-inspect
version: 0.25.0
description: Find evidence instead of guessing for .NET packages, platform libraries, local assemblies, APIs, dependencies, and version-to-version API changes.
---

# dotnet-inspect

Run `dnx dotnet-inspect -y -- <command>`. `-y` skips interactive confirmation, and `--` sends remaining options to dotnet-inspect rather than `dnx`.

## Common starts

| Goal | Command |
| ---- | ------- |
| Find an API | `find Pattern` includes platform/BCL types; add `--project path/to/project` when project references should be in scope. Exact `--package Foo@version` or explicit `--platform Library` searches with `--tfm` use the Workspace locator internally while preserving Find's existing Markdown, tips, tables, and root-array JSON. Selected Package Type/Member handoff retains the exact implementation asset and compatible TFM. |
| Inspect a type | `type Type --package Foo`; add `--all` for non-public/hidden members. |
| Inspect overloads | `member Type --platform Lib -m Name -S "Member Index"` |
| Select an overload | `member Type --platform Lib Name:1` or `Name~digest` |
| Correlate one member's Findings | `member Type Method:1 --package Foo -S "Finding Census" --json` returns one receipt-scoped Facts and annotated-source envelope. Load `skill query` for selection and format constraints. |
| Discover legal query values or demos | `vocabulary -D`; select values with `vocabulary -S Accessibility`, `-S "C# Style Choices" --json`, or `-S "C# Body Kinds"`; use `demo list` for product-home scenarios. |
| Discover query facets and operators | `library -Q` lists query-capable sections; `type -Q "Body Shapes"` or `library -Q "Performance: Arrays" --json` describes accepted keys and operators without inspection. |
| Find rendered body syntax | `library path/to.dll --where "Kind=ObjectCreationExpression"`, `type Type --library path/to.dll --where "Kind=InvocationExpression"`, or `member Type Method:1 --library path/to.dll --where "Kind=InvocationExpression"`; load `skill decompiler` for stable kinds and coordinates. |
| Compare APIs or method bodies | `diff --package Foo@old..new --breaking` (`--additive` new APIs; `--alloc-regressions` for allocation regressions). Single-Library API Diff supports complete Content with unprojected `--json` or the complete service value with `--envelope`; load `skill compatibility` for scope and projection rules. `match Type.MethodA Type.MethodB --package Foo --body` adds C#/IL body differences to the structural result; `match Type.Method --similar --package Foo` ranks structural candidates for discovery. |
| Trace API evolution | `timeline --package Foo@old..new --type Type --members --at all`; omit `--at` to inspect the vector without acquiring packages. |
| Inspect packages and ecosystems | `package Foo`; use `-D` to discover sections and `-S "Signals,Audit: Findings"` to audit text-bearing files and SourceLink mappings. `package activity --ecosystem aspire` scans the named ecosystem's exact package set over the previous 42 days; add `--security-only`, paired exact `--from`/`--through` UTC timestamps, or `--json`. Load `skill private-feeds` for custom/authenticated sources. |
| Query packages | `package query Foo` selects the latest eligible listed version; use `'Foo.*'` for a literal package-ID prefix. Add `--library-literal "TEXT" --tfm net10.0` to qualify package rows by decoded `ldstr` uses in each selected primary implementation library; prefix mode defaults to five candidates and accepts `--take 1..5`. `-n` and `--rows` select package Results, not occurrences. |
| Inspect a Workspace | `workspace --package Foo@version --tfm net10.0`; repeat `--package` to compose ordered Package occurrences, then add inert top-level intent with `--register-library PACKAGE@VERSION/ASSEMBLY@ASSEMBLY_VERSION`, `--register-package-prefix PREFIX`, or `--register-ecosystem ID`. Filter the typed inventory with repeatable `--kind`, or restore a current-format canonical packet with `--packet PACKET`; Workspace Definitions realizes its complete context and retained Navigation state before inventory. Exact Package duplicates coalesce; packages without compile assemblies remain members. Use `--verbose` for Package producer/target details and `--share packet` or `--share url` only on top-level inventory. Add `--active-package N` on direct construction for structural hierarchy, Library asset IDs, Type/Member inventory, lenses, and diagnostics. Use `--root-request TOKEN` instead to reopen the exact Root a `package query --library-literal` result names; it is refused rather than approximated by package id and version. |
| Inspect libraries | `library Foo` or `library path/to.dll`; use `-D` to discover sections and `-S "Unsafe Members"` for standalone unsafe evidence. Load `skill metadata` for raw ECMA-335 tables/heaps. |
| Dependencies and relationships | `depends --package Foo@version --tfm net10.0` for a package graph plus declaration evidence; add `-S Dependencies` for evidence only or explicitly select `-S Pruning` to compare direct package candidates with an installed platform inventory. Use `depends Type`, `extensions Type`, or `implements Interface` for type relationships. Positional `depends Type` supports complete Content with `--json` or the complete service value with `--envelope`; asset mode does not support envelopes. Load `skill relationships` for scopes and semantics. |

## Member lookup

Run `find Name` when scope is unknown, inspect the type, then `-S "Member Index"` to list overloads. Select with `Name:N` (1-based) or `Name~digest` (stable). A selected overload defaults to `Signature`. A fully-qualified `Namespace.Type.Member` needs no scope.

## Tips

- `package` and `library` produce terse, token-efficient, high-value domain content by default. Output supports Markdown, tables, TSV, JSONL, and JSON; load `dotnet-inspect skill query` for discovery, selection, projection, and limits.
- Add `--project <csproj|dir|project.assets.json>` when project-referenced packages should be in scope; it reads existing restored assets, so restore/build first if dependencies changed.
- `workspace` reports committed Packages before inert Exact Library, Package Prefix, and Ecosystem registrations; JSON/JSONL retain typed entry arms. It never selects an occurrence implicitly. Copy a Library asset ID, Type full name, and optional Member stable selector from direct `workspace --active-package N`, then add `--lens type.*` or `--lens member.*` for one exact stateless descendant request. Selector failures remain structured; JSON/JSONL retain Library asset ancestry and Member containing-versus-declaring Type joins. Packet input currently supports inventory controls only.
- Common BCL types resolve without scope: `type string`, `type 'List<T>'`. Quote generics and patterns: `member 'Dictionary<TKey,TValue>'`, `-S "Async*"`.
- Unpinned packages use latest stable; add `--preview` for prerelease APIs.

## Interpret fixed text

- `[Text omitted: required containment]`: a complete value or document was not shared because it carried a text concern; this does not imply malicious intent.
- `REDACTED`: a URL query or credential-bearing path segment was removed.
- `<unparsable-url>`: no original locator was shown because an authority-like value could not be parsed into URL components.
- `<absent>` or `(absent)`: a requested package document was not present; this is not containment or redaction.
- `\u202E`, `\U0001F600`, `\^[`, and similar backslash forms preserve source text as reversible visual spellings. They are not replacements; do not decode them into live control or format characters before display or persistence.
