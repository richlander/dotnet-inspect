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
jq '{scope, data_scope, data_states, facets: ._embedded.facets,
     bindings: ._embedded.bindings}' query.json
```

Vocabulary HAL nests values under `._embedded.vocabularies[]._embedded.values`.
When exact native value IDs are known, narrow the request with `-Q` before
selecting locally; keep their case, not the explanation path's spelling:

```bash
dnx dotnet-inspect -y -- explain vocabularies/api.accessibility \
  .hal --json -Q public > accessibility.json
jq '{scope, vocabularies: [._embedded.vocabularies[] |
     {id, values: ._embedded.values}]}' accessibility.json
```

Cache and select locally; follow emitted hrefs unchanged only for missing
knowledge. Preserve `scope` (profile, population and completeness), `data_scope`,
completion and applicable `data_states` where emitted. Unselected values remain
unknown, not false; a complete selection is not necessarily the full catalog.
An absent value establishes a negative only in an available, complete dataset
whose scope covers the question. `.data` supplies direct data; `.contract` is
for explanation-schema questions, not an obligatory extra request.

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

## Move between CLI and website

A Workspace packet is portable currency between the CLI and Inspect Web:
it carries the context and admitted inspection recipe, not a result snapshot.

**CLI to website:** append `--share url` to a supported inspection:

```bash
context=$(dnx dotnet-inspect -y -- workspace \
  --package System.Text.Json@10.0.0 --tfm net10.0 \
  --nuget-source-anonymous https://api.nuget.org/v3/index.json)
dnx dotnet-inspect -y -- package System.Text.Json \
  --workspace "$context" --share url > package.txt 2> share.txt
```

Check the command's exit status, then open the final URL in `share.txt`.
Ordinary content stays on stdout; the URL follows diagnostics on stderr.
By contrast, the `workspace` authoring command emits its packet or URL on stdout.

**Website to CLI:** copy the canonical Base64URL value of the website URL's
`w` parameter, not the whole URL, a file path or decoded JSON. For a Workspace
containing System.Text.Json:

```bash
packet='<value of w from the website URL>'
dnx dotnet-inspect -y -- -w "$packet"
dnx dotnet-inspect -y -- type System.Text.Json.JsonSerializer \
  --workspace "$packet"
```

`-w` replays the saved subject/view where supported; `--workspace` supplies
context for a new question. Neither sets a persistent current Workspace for
later CLI commands. Unsupported saved views or host populations fail visibly.

Each host reacquires content under its own source permissions and limits.
Packets carry no acquired binaries, computed results or credentials.
Website **Save Workspace** is browser-local persistence, not portable export
or cloud synchronization; use the packet or its URL for the handoff.

## Reuse a Workspace search space

```bash
base=$(dnx dotnet-inspect -y -- workspace \
  --register-ecosystem runtime --share packet)
dnx dotnet-inspect -y -- find JsonSerializer --type JsonSerializer \
  --workspace "$base" -n 5 --table
dnx dotnet-inspect -y -- package System.Text.Json@10.0.0 --workspace "$base"
```

Find admits registration-only Ecosystem packets, not arbitrary Workspace
content; do not combine this scope with competing source selectors or `--tfm`.
Package inspection can add a missing Package to a derived Workspace without
changing the input packet or replacing an existing Package version. Add
`--share packet` to preserve ordinary stdout and emit the derived packet on
stderr. Follow installed explanation for other admitted packet populations.

Packet arguments take the opaque packet value, not a file path.
`workspace --packet "$packet"` validates a declaration; `--inventory`
explicitly realizes it. Explain an extracted declaration with
`explain --component component.json`; supply `--parent-packet "$packet"` only
when parent-dependent context is needed. A declaration does not prove that its
content was acquired, and CLI packet support does not imply browser support.

Stop once the requested answer has sufficient evidence. If discovery misses,
try command help or the relevant focused skill; a search miss is not proof
that a capability does not exist.
