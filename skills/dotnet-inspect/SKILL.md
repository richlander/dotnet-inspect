---
name: dotnet-inspect
version: 0.27.0
description: Find and share evidence instead of guessing about .NET packages, libraries, APIs, dependencies, source, performance, and version changes.
---

# dotnet-inspect

Use dotnet-inspect to find evidence about compiled .NET code: discover APIs,
follow relationships into implementation and source, investigate risks, and
explain what changed. This guide maps the breadth; installed evidence supplies
the depth. Facts support judgment, not an automatic quality or safety verdict.

Run `dnx dotnet-inspect -y -- <command-or-target>`. `-y` skips confirmation;
`--` passes the remaining arguments to dotnet-inspect. If intent or result space
is unclear, use a bare target and let the router choose. Otherwise start directly.
Use `skill <name>` for a focused workflow only when the question needs it.

## Common starts

| Question | Evidence you can pursue | Start here |
| -------- | ----------------------- | ---------- |
| Where does an API live? | Public types and members across installed .NET Runtime, ASP.NET Core, and .NET Standard populations, including Microsoft.Extensions; explicit package, project and prefix scopes extend the search. | `find JsonSerializer -n 5 --table`, `find ControllerBase -n 5 --table`, or `find OptionsBuilder -n 5 --table`. For package APIs: `find IChatClient --package Microsoft.Extensions.AI.Abstractions -n 5 --table`; use `--project path/to/project` or `--package-prefix PREFIX` with a symbol pattern. Add `--members` for member names. |
| Which package or library should I inspect? | Package identity, versions, contents and documents; Library-grain discovery and public API shape. | `package query Foo` or `package query 'Foo.*'`, then `package <exact-id>`. Use `package Foo --versions -n 1` for the newest listed version or `package Foo@latest --versions` for a fresh check. Use `library query ./bin --where "references=System.Text.Json"` for Libraries, then `library path/to.dll`. |
| How do I call this API? | Signatures, overloads, nullable annotations, defaults, documentation and related Findings. | `type Type --package Foo`, then `member Type --package Foo -m Name -S "Member Index"`. Select `Name:N` (1-based) or stable `Name~digest`; add `--all` for hidden/non-public declarations. A selected overload defaults to Signature; `-S "Finding Census" --json` correlates its Facts and annotated source. |
| How is this code organized, and where should I investigate? | Library metrics, architectural families, namespace cycles, explaining Type edges and concentrated call paths. | `library Foo -S "Library Metrics"`, then discover related sections with `library -D`. Load `skill project-analysis` for a bounded orientation or evidence-backed deep dive, not an inspect-everything checklist. |
| Who depends on, extends, implements or calls this API? | Dependency graphs, receiver extensions, implementors, subclasses, callers and cross-package call-use clusters. | `depends Type`, `extensions Type`, `type Interface -S Implementers`, or `type Base -S "Derived Types"`, with an explicit scope where needed. Load `skill relationships` to follow calls and distinguish direct declarations from transitive or live applicability evidence. |
| Which dependencies need attention? | Direct/transitive declarations, platform-provided references, version lag, vulnerabilities and update impediments. | `depends --package Foo@version --tfm net10.0`; for a restored project use `depends --project path/to/project -S "Pruning,Latest Versions" --tfm net10.0`. Load `skill project-analysis` for the dependency-risk workflow; platform pruning and API compatibility are different questions. |
| What has changed across an ecosystem? | Network-backed recent package activity, current advisory context and evidenced security releases across the ecosystem's declared package population. | `package activity --ecosystem aspire`; add `--security-only` for security activity. Read the report's time window and coverage before interpreting an empty result. |
| Does a performance or correctness signal deserve a deeper look? | Allocation candidates, leverage and callers; unsafe operations, exceptions, resource lifetimes and exact Member/IL locations. | `library Foo -S "Performance:*"` or `library Foo -S "Unsafe Members"`. Load `skill performance` or `skill correctness` to trace a candidate. Static evidence is not measured runtime cost, and incomplete analysis is not proof of absence. |
| Can I read the implementation and establish its provenance? | Authored source, PDB/SourceLink evidence, decompiled C#, annotated IL, raw metadata and package signals. | `type Type --package Foo -S Source` selects authored-first source with fallback context. Load `skill sourcelink` for provenance, `skill decompiler` for implementation fidelity, `skill metadata` for ECMA-335 tables, or `skill signals` for package evidence. Decompiled text is not the original source. |
| What changed beyond the public API? | Assessed breaks, the complete API change population, implementation/body differences, structural similarity, analysis shifts, changed literals and version history. | `diff --package Foo@old..new --breaking`; load `skill compatibility` for the other evidence paths, including `match` for exact body comparisons or similar-member discovery. `diff --history --package Foo@old..new --type Type --members --major-versions` traces checkpoints, not every intermediate version. An assessed subset is not the complete change population. |
| Can I find packages by what their libraries contain? | Bounded package discovery using dependencies, references, metadata and decoded string literals. | Discover a facet with `explain literal`, copy its returned path into `explain <path> .hal --json`, then use its production binding: `package query Microsoft.Azure.SignalR --where "library-literal=https://" --tfm net8.0 --json`. Load `skill query` for composition and work limits; reaching a candidate limit is not family-wide completeness. |
| Can I retain and share the environment or investigation? | Portable Workspace declarations, immutable Package edits, live inventory and admitted saved inspections. | `workspace --register-ecosystem runtime` authors a packet without acquisition; add `--share url` for an activation URL. `workspace --packet "$w"` validates a packet; `--inventory` explicitly realizes inventory. Ordinary inspection commands consume admitted `--workspace "$w"` context. Apply `-w "$w"` or raw packet input to replay Workspace inventory, saved Package Libraries, Type API or Member overview; other saved facets/queries fail visibly. Saved Member overview does not admit Count. |
| What does this extracted Workspace declaration actually establish? | Declared Package/target fields and, only with the actual parent, qualified path, context and focus. | `workspace component list --packet "$w"` obtains descriptors and edit paths. `explain --component component.json` explains one extracted descriptor; add `--parent-packet "$w"` only for parent-dependent questions. `--packet`/`--parent-packet` take the packet value, not a file path; missing parent facts remain unavailable, not false. |
| Does this package provide its own usage guidance? | Package-authored skills selected from restored project dependencies. | `project path/to/project -S Skills`, then load `skill package-skills` to select and read relevant guidance. Use `skill private-feeds` for authenticated/custom feeds and offline acquisition. |

## Follow the evidence

- When you do not know the capability, bare `explain` gives routing hints and `explain <task-text>` searches installed capabilities. Follow a returned exact path or binding; a search miss does not prove the tool lacks the capability. Use command help or the relevant focused skill to recover. When the route is known, use `-D` for sections and `-Q` for facets/operators directly; neither requires a detour through the root.
- Use `explain vocabularies --json` for legal-value discovery. For supported vocabulary, query-space, facet and discovery readings, request `.hal --json`: `_embedded` is included data, `_links` supplies navigation. Cache the response, select locally with jq, preserve scope/completeness such as `data_scope` where emitted, and follow hrefs unchanged only for missing knowledge. Do not fetch a contract or another copy merely to confirm an answered question.
- Preserve package/version, selected Library, TFM/RID and exact Type/Member identities. Read diagnostics, completion and boundaries before interpreting empty output. Platform queries use installed packs when available, otherwise package-backed sources; for a non-conventional install verify `<root>/packs` and set process-scoped `DOTNET_ROOT`, not `PATH`. `--project` reads restored assets: restore/build first when dependencies changed.
- Start with ordinary domain output. Use `--table`, `--tsv`, `--jsonl` or `--json` when needed; load `skill query` for section/field selection, row limits, envelopes and Share. Stop when the requested answer has sufficient evidence, or name the missing evidence. Unpinned packages use latest stable; `--preview` opts into prereleases. Quote generics and patterns, such as `'List<T>'` and `-S "Async*"`.

## Interpret fixed text

- `[Text omitted: required containment]`: a complete value or document was not shared because it carried a text concern; this does not imply malicious intent.
- `REDACTED`: a URL query or credential-bearing path segment was removed.
- `<unparsable-url>`: no original locator was shown because an authority-like value could not be parsed into URL components.
- `<absent>` or `(absent)`: a requested package document was not present; this is not containment or redaction.
- `\u202E`, `\U0001F600`, `\^[`, and similar backslash forms preserve source text as reversible visual spellings. Do not decode them into live control or format characters before display or persistence.
