---
name: dotnet-inspect
version: 0.26.0
description: Find and share evidence instead of guessing about .NET packages, libraries, APIs, dependencies, source, performance, and version changes.
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
| Compare APIs or method bodies | `diff --package Foo@old..new --breaking` (`--additive` new APIs; `--alloc-regressions` for allocation regressions). Single-Library API Diff supports complete Content with unprojected `--json` or the complete service value with `--envelope`; its Share is currently non-projectable. Use `type` or `member ... --match --envelope` when one API coordinate's complete correspondence outcome and diagnostics are the question; ordered match endpoints are also non-projectable. `match Type.MethodA Type.MethodB --package Foo --body` adds C#/IL body differences to the structural result; `match Type.Method --similar --package Foo` ranks structural candidates for discovery. |
| Trace API evolution | `timeline --package Foo@old..new --type Type --members --at all`; omit `--at` to inspect the vector without acquiring packages. |
| Inspect packages and ecosystems | `package Foo`; use `-D` to discover sections, `-S "Package license files"` to list shipped license documents, and `-S "Signals,Audit: Findings"` to audit text-bearing files and SourceLink mappings. `package activity --ecosystem aspire` scans the named ecosystem's exact package set over the previous 42 days; add `--security-only`, paired exact `--from`/`--through` UTC timestamps, or complete Content `--json`; use `--envelope` for the full service value. Load `skill private-feeds` for custom/authenticated sources. |
| Query packages | `package query Foo` selects the latest eligible listed version; use `'Foo.*'` for a literal package-ID prefix. The closed nuspec license values are `any`, `MIT`, and `OSMF`; use them with `--where` and `--nuspec-only` to avoid opening archives. Add `--library-literal "TEXT" --tfm net10.0` to qualify package rows by decoded `ldstr` uses in each selected primary implementation library; prefix mode defaults to five candidates and accepts `--take 1..5`. `-n` and `--rows` select package Results, not occurrences. `--envelope` retains complete Content and diagnostics, but Package Query Share is currently non-projectable. |
| Inspect a Workspace | `workspace --package Foo@version --tfm net10.0`; repeat `--package` to compose ordered Package occurrences, then add inert top-level intent with `--register-library PACKAGE@VERSION/ASSEMBLY@ASSEMBLY_VERSION`, `--register-package-prefix PREFIX`, or `--register-ecosystem ID`. Filter the typed inventory with repeatable `--kind`; `-n` and `--rows` select complete inventory entries after that filter, while `--lines` explicitly selects rendered lines. Restore a current-format canonical packet with `--packet PACKET`; Workspace Definitions realizes its complete context and retained Navigation state before inventory. Exact Package duplicates coalesce; packages without compile assemblies remain members. Use `--verbose` for Package producer/target details and `--share packet` or `--share url` only on top-level inventory. Add `--active-package N` on direct construction for structural hierarchy, Library asset IDs, Type/Member inventory, lenses, and diagnostics. Use `--root-request TOKEN` instead to reopen the exact Root a `package query --library-literal` result names; it is refused rather than approximated by package id and version. |
| Replace a coordinate in a portable Workspace | `workspace --packet "$w" --replace-package 1 --to-version 12.1.2 --share packet`; select a direct Package by one-based navigation-row order. Use `--to-tfm` or both destination options; TFM changes require an unsubscribed single-member context. This explicitly acquires Packages and retains the packet's supported API/inspector intent while preserving unrelated state. Choose scalar `--share packet`/`--share url` or `--json --envelope` for the complete derived Share and typed outcome; do not restate noun selectors. Format 4 supports active Library/Type/Member views. Source and destination Versions must be exact. |
| Inspect libraries | `library Foo` or `library path/to.dll`; use `-D` to discover sections and `-S "Unsafe Members"` for standalone unsafe evidence. Load `skill metadata` for raw ECMA-335 tables/heaps. |
| Dependencies and relationships | `depends --package Foo@version --tfm net10.0` for a package graph plus declaration evidence; add `-S Dependencies` for evidence only or explicitly select `-S Pruning` to compare direct package candidates with an installed platform inventory. Use `depends Type`, `extensions Type`, or `implements Interface` for type relationships. Positional `depends Type` supports complete Content with `--json` or the complete service value with `--envelope`; asset mode does not support envelopes. Load `skill relationships` for scopes and semantics. |

## Shape item lists before reading them

Every command has one effective item sequence. When the active command or lens
declares semantic rows, the items are packages, types, dependencies, graph
edges, or other complete domain rows. Otherwise the items are rendered lines.

- `-n N` and bare `-N` keep the first N items; add `--tail` for the last N.
- `--rows A..B`, `--rows A..`, and `--rows ..B` select a strict, one-based,
  inclusive semantic range where the route supports windows.
- Selection stages compose in argument order. `-n 2 --rows 2..` keeps one item,
  while `--rows 2.. -n 2` keeps two when the input has at least three.
- Where supported, `--count` observes the selected semantic rows. On sectioned
  output, select one concrete table when a scalar count is required.
- `--lines` switches an adopted semantic route to rendered-line selection.
  Use it only when clipping presentation text is the actual goal.

Do not confuse selection with work or ranking. `--take` bounds candidate work;
`--top` requests a ranked prefix; neither is another spelling of `-n`.
Load `skill query` for adopted routes, strict-window behavior, and projection
constraints.

## Keep the service result when context matters

Use unprojected `--json` when Content alone answers the question. Use
`--envelope` when the answer also needs the operation's Share outcome or
ordered diagnostics. It normally implies JSON and is not a more verbose
presentation format. Workspace coordinate replacement is the exception and
requires `--json --envelope` together. Incompatible section, field, row, count,
and rendering projections fail rather than shaping the service value.

High-value envelope cases:

- `type` or `member ... --match --envelope` retains one API-coordinate
  correspondence outcome and diagnostics. Its ordered endpoints currently make
  Share non-projectable.
- `depends Type --package Foo@version --tfm TFM --envelope` retains complete
  dependency evidence, the selected relationship rows, diagnostics, and a
  restorable Dependencies URL when the request is projectable.
- Online package version populations retain completion and source evidence.
  `--count --envelope` makes the selected Count the envelope Content.
- Workspace coordinate replacement with `--json --envelope` retains the
  derived Share, actual Scope outcome, fallback decision, and diagnostics.
- API Diff, Package Activity, and Package Query retain complete typed outcomes
  and diagnostics, but a Share may be `nonProjectable`; inspect `share.kind`
  before offering a URL.

Member `Call Graph` has not adopted `--envelope`. Use its Markdown table,
`--tree`, `--mermaid`, `--tsv`, `--jsonl`, or structured `--json` content.
A separate member `--share url` opens the public API Overview; it does not
preserve the selected Call Graph.

## Hand a question to Inspect Web

Inspect Web at `https://dotnet-inspect.net/` uses the same inspection codebase
and provides an analogous interactive experience. Prefer a product-issued URL
over describing how the user could reconstruct a view:

- Use `--share url` on supported Workspace, public member, and dependency
  routes.
- Given a schema-4 packet, add `--workspace "$packet" --share url` to one exact
  Type query to use its selected aggregate context and issue the derived
  Inspect Web URL.
- When an envelope has `share.kind: "available"`, use its `full_url` directly
  and treat its `packet` as opaque replay state.
- Use `workspace --packet URL` to validate and restore an exact Inspect Web
  URL; do not edit its `w=` payload.

```bash
dnx dotnet-inspect -y -- member JsonSerializer \
  --package System.Text.Json@10.0.0 Serialize:1 \
  --tfm net10.0 --share url
dnx dotnet-inspect -y -- depends \
  --package Newtonsoft.Json@13.0.4 --tfm net6.0 --share url
dnx dotnet-inspect -y -- workspace \
  --package System.Text.Json@10.0.0 --tfm net10.0 --share url
dnx dotnet-inspect -y -- type System.Text.Json.JsonSerializer \
  --workspace "$schema4_packet" --share url
```

These URLs carry canonical datapackets rather than rendered output. The member
URL opens the selected public API Overview. The package-dependency URL lets
Inspect Web acquire the exact package and compute its dependency graph. The
Workspace URL restores the complete projectable definition. The Type command
uses only a packet string, not a URL; ordinary Type output remains on stdout
and the derived URL is the final stderr line. A schema-3 packet remains valid
Type context but cannot encode derived Type Share. Package Query envelopes are
currently `nonProjectable`; do not hand-author a query-bearing packet or
promise that Inspect Web can restore it.

## Member lookup

Run `find Name` when scope is unknown, inspect the type, then `-S "Member Index"` to list overloads. Select with `Name:N` (1-based) or `Name~digest` (stable). A selected overload defaults to `Signature`. A fully-qualified `Namespace.Type.Member` needs no scope.

## Tips

- `package` and `library` produce terse, token-efficient, high-value domain content by default. Output supports Markdown, tables, TSV, JSONL, and JSON; load `dotnet-inspect skill query` for discovery, selection, projection, and limits.
- Add `--project <csproj|dir|project.assets.json>` when project-referenced packages should be in scope; it reads existing restored assets, so restore/build first if dependencies changed.
- `workspace` reports committed Packages before inert Exact Library, Package Prefix, and Ecosystem registrations; JSON/JSONL retain typed entry arms. It never selects an occurrence implicitly. Copy a Library asset ID, Type full name, and optional Member stable selector from direct `workspace --active-package N`, then add `--lens type.*` or `--lens member.*` for one exact stateless descendant request. Selector failures remain structured; JSON/JSONL retain Library asset ancestry and Member containing-versus-declaring Type joins. Packet input supports inventory, resource-free `--share` re-emission, or explicit `--replace-package` transformation, not noun-selector refinement.
- Common BCL types resolve without scope: `type string`, `type 'List<T>'`. Quote generics and patterns: `member 'Dictionary<TKey,TValue>'`, `-S "Async*"`.
- Unpinned packages use latest stable; add `--preview` for prerelease APIs.

## Interpret fixed text

- `[Text omitted: required containment]`: a complete value or document was not shared because it carried a text concern; this does not imply malicious intent.
- `REDACTED`: a URL query or credential-bearing path segment was removed.
- `<unparsable-url>`: no original locator was shown because an authority-like value could not be parsed into URL components.
- `<absent>` or `(absent)`: a requested package document was not present; this is not containment or redaction.
- `\u202E`, `\U0001F600`, `\^[`, and similar backslash forms preserve source text as reversible visual spellings. They are not replacements; do not decode them into live control or format characters before display or persistence.
