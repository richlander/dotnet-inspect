---
name: dotnet-inspect
version: 0.24.0
description: Find evidence instead of guessing for .NET packages, platform libraries, local assemblies, APIs, dependencies, and version-to-version API changes.
---

# dotnet-inspect

Use dotnet-inspect for evidence about .NET packages, platform libraries, assemblies, APIs, dependencies, or API version diffs.

Run `dnx dotnet-inspect -y -- <command>`. `-y` skips interactive confirmation,
and `--` sends remaining options to dotnet-inspect rather than `dnx`.

## Common starts

| Goal | Command |
| ---- | ------- |
| Find an API | `find Pattern` includes platform/BCL types; add `--project path/to/project` when project references should be in scope. |
| Inspect a type | `type Type --package Foo`; add `--all` for non-public/hidden members. |
| Inspect overloads | `member Type --platform Lib -m Name -S "Member Index"` |
| Select an overload | `member Type --platform Lib Name:1` or `Name~digest` |
| Discover legal query values or demos | `vocabulary -D`; select values with `vocabulary -S Accessibility`, `-S "C# Style Choices" --json`, or `-S "C# Body Kinds"`; use `demo list` for product-home scenarios. |
| Find rendered body syntax | `library path/to.dll --where "Kind=ObjectCreationExpression"`, `type Type --library path/to.dll --where "Kind=InvocationExpression"`, or `member Type Method:1 --library path/to.dll --where "Kind=InvocationExpression"`; load `skill decompiler` for stable kinds and coordinates. |
| Compare APIs or implementations | `diff --package Foo@old..new --breaking` (`--additive` new APIs; `--alloc-regressions` for allocation regressions); `match Type.MethodA Type.MethodB --package Foo --implementation` compares two methods with C#/IL; `match Type.Method --similar --package Foo` ranks structural candidates for discovery. |
| Trace API evolution | `timeline --package Foo@old..new --type Type --members --at all`; omit `--at` to inspect the vector without acquiring packages. |
| Inspect packages | `package Foo`; use `-D` to discover sections and `-S "Signals,Audit: Findings"` to audit text-bearing files and SourceLink mappings. Load `skill private-feeds` for custom/authenticated sources. |
| Inspect a Workspace | `workspace --package Foo@version --tfm net10.0`; repeat `--package` to preserve an ordered package occurrence set. |
| Inspect libraries | `library Foo` or `library path/to.dll`; use `-D` to discover sections and `-S "Unsafe Members"` for standalone unsafe evidence. Load `skill metadata` for raw ECMA-335 tables/heaps. |
| Relationships | `depends Type`, `extensions Type`, `implements Interface`. |

## Member lookup

Run `find Name` when scope is unknown, inspect the type, then `-S "Member Index"` to list overloads. Select with `Name:N` (1-based) or `Name~digest` (stable). A selected overload
defaults to `Signature`. A fully-qualified `Namespace.Type.Member` needs no scope.

## Tips

- `package` and `library` produce terse, token-efficient, high-value domain content by default. Output supports Markdown, tables, TSV, JSONL, and JSON; load `dotnet-inspect skill query` for discovery, selection, projection, and limits.
- Add `--project <csproj|dir|project.assets.json>` when project-referenced packages should be in scope; it reads existing restored assets, so restore/build first if dependencies changed.
- Common BCL types resolve without scope: `type string`, `type 'List<T>'`. Quote generics and patterns: `member 'Dictionary<TKey,TValue>'`, `-S "Async*"`.
- Unpinned packages use latest stable; add `--preview` for prerelease APIs.

## Interpret fixed text

- `[Text omitted: required containment]`: a complete value or document was not shared because it carried a text concern; this does not imply malicious intent.
- `REDACTED`: a URL query or credential-bearing path segment was removed.
- `<unparsable-url>`: no original locator was shown because an authority-like value could not be parsed into URL components.
- `<absent>` or `(absent)`: a requested package document was not present; this is not containment or redaction.
- `\u202E`, `\U0001F600`, `\^[`, and similar backslash forms preserve source text as reversible visual spellings. They are not replacements; do not decode them into live control or format characters before display or persistence.
