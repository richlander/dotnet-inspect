---
name: dotnet-inspect-query
version: 0.1.0
description: Discover dotnet-inspect capabilities and query rules, select inspection sections, and shape answers with common formats, projections and limits; follow installed explanation for deeper contracts.
---

# dotnet-inspect: Discover CLI Capabilities and Shape Inspection Output

Use this skill to shape the result the user needs. Skills teach productive
starts and common arguments; installed explanation owns detailed bindings,
legal values, prerequisites, coverage and continuations.

Run `dnx dotnet-inspect -y -- <command>`. If intent is unclear, use a bare
target and let the router choose. Otherwise choose the result space directly:

| Result | Start |
| --- | --- |
| API types or members | `find JsonSerializer -n 5 --table`; add `--members` for member names. |
| Package IDs | `find --packages 'Microsoft.Extensions.*' -n 5`, then `package <exact-id>`. |
| Libraries | `find --libraries ./bin`, then `library path/to.dll`. |

Unscoped API Find searches installed .NET Runtime, ASP.NET Core, and
.NET Standard populations, including Microsoft.Extensions. Use `--package`,
`--project`, or `--package-prefix` for package API scope. Restored projects
need current assets. `--packages`, `--libraries`, and `--members` select
different grains; do not combine them.

## Discover only what is missing

`-D`/`-S` are the uppercase cross-command query namespace: discover sections
and fields with `-D`, then select with `-S`. `-Q` discovers supported query
facets, operators and values without inspecting the target.

```bash
dnx dotnet-inspect -y -- library -D
dnx dotnet-inspect -y -- library -D @Performance
dnx dotnet-inspect -y -- library -D "Performance: Arrays" --details
dnx dotnet-inspect -y -- library -Q "Performance: Arrays" --json
dnx dotnet-inspect -y -- library MyLib.dll -S "Performance: Arrays" --jsonl
```

Structural discovery does not run producers; `--effective`, where supported,
probes for populated evidence. Explicit sections and categories can authorize
expensive work. A category such as `@Performance` groups sections; a wildcard
such as `Performance:*` selects matching names. For row formats, prefer one
concrete section; a heterogeneous category is not one table.

If you do not know the capability, use `explain "<question>"`. Copy a returned
resource path rather than inventing one, and read its CLI binding before
constructing unfamiliar predicates.

```bash
dnx dotnet-inspect -y -- explain "compare string literals"
dnx dotnet-inspect -y -- explain diff-analysis/bindings/cli --json
dnx dotnet-inspect -y -- find --packages --explain
```

For supported vocabulary, query-space and facet resources, `.hal --json`
includes `_embedded` data and `_links` navigation:

```bash
dnx dotnet-inspect -y -- explain package-query/query .hal --json > query.json
jq '{data_scope, data_states, facets: ._embedded.facets,
     bindings: ._embedded.bindings}' query.json
```

Cache and select locally; follow emitted hrefs unchanged only for missing
knowledge. Preserve `data_scope`, completion and applicable `data_states`.
An absent value establishes a negative only in an available, complete
dataset. `.data` supplies direct data; `.contract` is for explanation-schema
questions, not an obligatory extra request.

## Common output arguments

| Need | Use |
| --- | --- |
| Compact human-readable rows | `--table` |
| Stable machine rows | `--tsv` or `--jsonl` |
| Structured content | `--json` |
| Selected columns or fields | `--columns "Name;Namespace"` or `--fields` after discovery |
| First or last N items | `-n N` (or `-N`); add `--tail` for the end |
| A row window or count | `--rows 2..10`, `--count` where supported |
| One document or locator | `--print --row 1`, `--urls`, `--paths`, or `--value` |
| Undecorated selected payload | `--raw`, without another format |
| Supported graph view | `--tree` or `--mermaid`; `--markdown --mermaid` embeds a diagram |

Default shape depends on the selected content: a hierarchy or source payload
need not be Markdown. Request `--markdown` explicitly when framing matters.
On Type/Member Find, plain `--json` retains the typed root result array;
`--columns`/`--fields` instead requests projected rows.

`-n` is like `head`, and `--tail` like `tail`, over the route's effective
items. Some routes select semantic rows; others clip rendered lines.
`--lines` explicitly requests line clipping. Strict semantic `--rows` windows
fail when required positions are missing. Legacy `--rows` composes with an
inferred or explicit rendered-line `-n`, with the row window applied first.
Discover the route's selection contract when ordering or completeness matters.

Keep output limits separate from work bounds: `--take` bounds candidates on
Package/Library Find, while `--top` needs an admitted ranking. A count is over
the selected result, not automatically the complete population. Preserve
warnings and exit status; zero rows with incomplete evaluation is not absence.
Displayed fields do not imply support for filtering or ranking: use `-Q`
before constructing `--where` or `--order-by`.

## Keep service and presentation boundaries distinct

Where admitted, `--envelope` returns complete service Content, Share and
diagnostics as JSON; it is not a wrapper around every output format.
Use command help and installed explanation for route-specific admission.
Unprojected content JSON and projected row JSON need not have the same schema.

`--share url` is useful only when the receiving browser can replay the
represented scenario. Inspect `share.kind` when using an envelope; do not turn
`nonProjectable` into an approximate link. A Member API Overview link is not a
replay of its call graph or performance analysis.

Workspace packet arguments take the opaque packet value, not a file path.
`workspace --packet "$packet"` validates a declaration; `--inventory`
explicitly realizes it. Explain an extracted declaration with
`explain --component component.json`; supply `--parent-packet "$packet"` only
when parent-dependent context is needed. A declaration does not prove that its
content was acquired, and CLI packet support does not imply browser support.

Stop once the requested answer has sufficient evidence. If discovery misses,
try command help or the relevant focused skill; a search miss is not proof
that a capability does not exist.
