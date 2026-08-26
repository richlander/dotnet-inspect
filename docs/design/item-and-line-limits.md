# Item and line limits

## Status

Approved design target for
[#4677](https://github.com/richlander/dotnet-inspect/issues/4677). The current
CLI still uses `-n` for rendered lines, accepts count-form `--rows`, and has
command-specific result limits. README examples, workflows, and shipped skills
continue to describe that released behavior until the implementation lands.

All target behavior in this document is unverified until the implementation
adds the gates listed in [Required gates](#required-gates).

Related docs:

- [Output shapes](output-shapes.md) owns the Document -> Table -> Vector ->
  Scalar ladder and printable-row contract.
- [Row query and ordering](row-query-order.md) owns `--where`, `--order-by`,
  ranking metadata, and `--top`.
- [Projected JSON](projected-json.md) owns complete structured values and the
  typed-versus-lowered JSON boundary.
- [Package query CLI](package-query-cli.md) applies the result-limit contract to
  a streaming package corpus.

## Decision

One short numeric gesture answers the ordinary question "how many results?":

```bash
dotnet-inspect find "Json*" -n 20
dotnet-inspect type --package System.Text.Json -20
dotnet-inspect package System.Text.Json --versions -n 5
```

`-n N` and bare `-N` mean the first N items in each declared row set after
filtering and effective ordering. They make no ranking claim. `--tail` changes
the direction to the last N items.

The adjacent concepts keep distinct names:

| Gesture | Meaning |
| --- | --- |
| `-n N` / bare `-N` | First N items in each declared row set. |
| `-n N --tail` | Last N items in each declared row set. |
| `--rows N..M` / `N+K` / `N..` | Absolute 1-based row range. |
| `--top N --order-by "Field desc"` | Ranked first N; `--order-by` is mandatory unless the section declares a ranking default. |
| `-n N --lines` | First N rendered lines; for multi-item `--print`, first N lines of each payload. |
| `-n N --lines --tail` | Last N rendered lines; for multi-item `--print`, last N lines of each payload. |
| `-n N --tail-lines` | Sugar for `-n N --lines --tail`. |
| `--count` | Full post-filter row count, with no item window. |
| `--row N\|first\|last` | One stable row address, not a window. |

`--rows N` and `--rows N --tail` retire. A count belongs on `-n`; `--rows`
exists only for ranges.

## Declared row-set scope

An item is the producer-declared row unit, never a rendered line or an inferred
piece of presentation:

- packages in a package query;
- types or members in an API listing;
- rows in a table or list;
- directed logical edges in a graph;
- files or versions in a lens that declares those rows.

`-n` applies independently to every declared row set in a multi-section
document. Non-row field sets, text, code, and scalar sections remain unchanged.
This preserves ordinary multi-section reports without inventing one global
sequence across heterogeneous sections.

Grouping remains part of the row declaration. A singular section repeated once
per library keeps one row set per library; an aggregate section has one rolled-up
row set. Markdown, table, TSV, JSONL, typed JSON, lowered JSON, tree, and Mermaid
must select the same logical items even when their presentation differs.

`--rows` addresses the section's stable rendered row ordinals. Combining a
range with `-n` or `--top` intersects the two selections without renumbering:

```bash
# Rows 50 through 60 within the first 200 filtered, ordered results.
dotnet-inspect ... -n 200 --rows 50..60

# Rows 21 through 40 within a ranked top 100.
dotnet-inspect ... --top 100 --order-by "Score desc" --rows 21..40
```

Filtering and effective ordering establish those 1-based addresses before any
item window. Consequently, rows 21 through 40 in the second example are ranked
positions 21 through 40, and retain those addresses after the intersection.

An empty intersection is an honest empty result. `--tail` cannot modify an
absolute row range. It can modify an accompanying item count or an independent
line window:

```bash
# Stable rows 90 through 95, if they occur among the last 20 items.
dotnet-inspect ... -n 20 --tail --rows 90..95

# Rows 2 through 5, printing the last 20 lines of each selected payload.
dotnet-inspect ... --rows 2..5 --print -n 20 --lines --tail
```

## Pipeline

Each declared row set follows one pipeline:

```text
select subject and section/lens
-> produce rows
-> apply --where and command-owned filters
-> apply effective order and assign 1-based row addresses
-> apply item-mode -n/--tail or --top
-> intersect --rows range
-> project columns, fields, values, paths, URLs, or printable payloads
-> apply per-payload --lines window when requested
-> render
```

`--count` branches after filtering and before ordering or windows. It reports
the full matched-row count, so it is mutually exclusive with `-n`, `--top`,
`--rows`, `--tail`, and `--lines`; no accepted limiter may be silently ignored.

`--row` remains an exactly-one address and is mutually exclusive with item
windows. It may combine with `--print`, `--value`, `--urls`, or `--paths`.

### Cost and completion

A semantic result limit is not automatically a work budget:

- Natural streaming order may stop after N matching rows.
- A global `--order-by`, `--top`, or last-N request must exhaust the applicable
  input before it can choose rows.
- An absolute range may stop after its closed upper bound when the producer's
  order is already final.
- `--lines` limits projected text, not acquisition. It does not authorize
  partial artifact reads or change whether a selected payload must be fetched.
- Expensive promoted-tier queries retain their own explicit candidate/work
  budget, such as the proposed package-query `--deepen` bound.

Completion text must distinguish exhausted, bounded, failed, and cancelled
inputs. A result cap never upgrades a bounded search into an exhaustive claim.

## Ranking and `--top`

`--top` is a validated composition, not a second general count flag:

```bash
dotnet-inspect library My.dll -S "Performance Triage" \
  --top 20 --order-by "RootReach desc"
```

It lowers to the same ranked row plan as `-n 20` plus the effective order, but
retains the user's ranking intent for validation and human-readable notes.

Rules:

- `--top N` requires an explicit `--order-by`, unless the selected section
  declares that its default order is a ranking order.
- A stable but non-ranking default, such as alphabetical or producer order,
  cannot satisfy bare `--top`.
- `--top` is mutually exclusive with item-mode `-n` and `--tail`. It may
  combine with `-n N --lines` and line-mode `--tail`.
- The order's `asc`/`desc` direction chooses lowest/highest ranking; `--tail`
  does not reverse it.
- `--top` may combine with an absolute `--rows` range to intersect ranked
  positions after the effective ranking order assigns their addresses.

Human output says "top N by ..." only for `--top`. Plain `-n` says "first N" or
"last N", even when an explicit order is present. Structured output carries
facts rather than those notes.

Section schema exposes whether the default order is `ranking` or `sequence`.
Sortable field names and composite-order expansion remain discoverable through
`--schema`; presentation code must not infer ranking from a field name.

## Multi-item paths and printing

Path/URL projection and content projection are separate operations over the same
selected rows.

### Paths and URLs

`--paths` and `--urls` return the selected row addresses without acquiring
their content:

```bash
dotnet-inspect package My.Package -S "Package skill files" --paths
dotnet-inspect package My.Package -S "Package skill files" --paths --rows 2..5
```

An agent can use that list directly or issue a later content request. Item
limits and ranges apply before path/URL projection.

### Printable payloads

`--print` projects every selected row to its declared printable payload. It is
no longer unary:

```bash
# Print all selected skills.
dotnet-inspect package My.Package -S "Package skill files" --print

# Print the first five selected skills.
dotnet-inspect package My.Package -S "Package skill files" -n 5 --print

# Print the first 20 lines of rows 1 through 5.
dotnet-inspect package My.Package -S "Package skill files" \
  --rows 1..5 --print -n 20 --lines
```

Normal text output frames every selected row so payload boundaries and identity
survive concatenation:

```text
------ [skills/query/SKILL.md]; lines 1-20 of 86 ------
<content>
```

The frame uses contained typed row identity, not text parsed back from a
rendered cell. Every successful frame includes the selected line range, total
line count, and whether content was truncated. Full acquisition makes that
metadata available even when the content projection is clipped. Frame lines do
not consume the line budget.

`--bare` remains valid only when exactly one row is selected. Multiple unframed
payloads are rejected because their boundary and row identity would be lost.
Exact `--out` payload export likewise remains unary; a multi-item file export
requires a structured format or a separately designed directory export.

Plain `--json` also remains unary and preserves its one-object contract.
Multi-item structured print requires `--jsonl` or `--json-array`; accepting
multiple rows with plain `--json` would leave its envelope ambiguous.

For `--jsonl`, each selected row produces one complete object that retains its
row identity and adds content, selected line range, total line count,
truncation state, and a typed failure when applicable. Successful rows always
carry the line metadata; failed acquisitions carry failure data instead.
`--json-array` wraps the same objects in one array. Line windows modify each
content value before JSON encoding; they never truncate serialized JSON.

### Partial failures

Every selected row produces a visible success or failure result. A row that
does not declare a printable payload, or whose payload acquisition fails, is
not skipped. Text output emits a framed failure; structured output emits a
typed failure row. Other selected rows continue, and the command exits nonzero
when any row failed.

This is batch-result behavior, not a success-shaped fallback. A zero-row
selection remains an error for `--print`.

## Line windows

`--lines` changes the unit carried by `-n`; it does not carry another number.
Therefore one invocation cannot use `-n` for both item and line counts.

Use a range when both dimensions are needed:

```bash
# Five items and 20 lines per item.
dotnet-inspect ... --rows 1..5 --print -n 20 --lines
```

For an ordinary rendered report, the report is one textual payload and the line
window applies to that payload. For multi-item `--print`, each selected payload
gets its own independent line window. Separators and structured framing never
consume that budget.

Line windows are rejected for structured shapes that cannot preserve a complete
value. JSON/JSONL print projections support them by clipping each content string
before serialization, not by clipping the encoded output.

## Retired result-limit spellings

The implementation removes command-specific result counts and short selector
aliases without a deprecation period:

- numeric and nonnumeric `-t`;
- numeric and nonnumeric `-m`;
- count values on `--versions` and `--versions-with-feed`;
- package-search `--take`;
- count-form `--rows N` and `--rows N --tail`;
- implicit line mode on `-n`.

Long `--type` and `--member` selectors remain where the command needs them;
positional member syntax remains. `--versions` and `--versions-with-feed`
become value-less lens selectors and compose with `-n`.

Numbers that are not result counts remain distinct: `--row` addresses one row,
`--index` addresses one overload, `--depth` controls traversal, and timeout or
budget options retain their own units.

The implementation PR updates README examples, runnable workflows, and every
shipped `skills/**/SKILL.md` reference in the same change. No compatibility
alias is required; an old spelling may receive direct replacement guidance, but
must not execute with its old meaning.

## Required gates

The implementation must add named Release gates for these target properties:

| Gate | Contract |
| --- | --- |
| `ItemLimitsUseDeclaredRowsAcrossFormats` | Every renderer selects the same first/last logical rows per declared row set and preserves non-row sections. |
| `AbsoluteRangesIntersectWithoutRenumbering` | `--rows` range intersections retain stable row addresses across item limits and rankings. |
| `CountRejectsItemAndLineWindows` | `--count` never silently ignores or applies a result/line window. |
| `TopRequiresRankingOrder` | `--top` requires explicit order or a schema-declared ranking default and rejects item-mode `-n`/`--tail`. |
| `AddressProjectionDoesNotAcquirePayloads` | `--paths` and `--urls` project selected row addresses without fetching printable content. |
| `MultiPrintPreservesIdentityAndFailures` | Every selected row emits one framed or structured success/failure result, and any failure makes the exit nonzero. |
| `MultiPrintFrameFieldsAreContained` | Adversarial row identity, path, URL, and failure values cannot forge a frame or emit live terminal controls. |
| `MultiPrintLineWindowsArePerPayload` | Line budgets exclude frames, apply independently per payload, and preserve complete structured values. |
| `LegacyResultLimitSpellingsAreAbsent` | CLI aliases, help, README, workflows, and embedded skills contain no retired result-limit spelling. |
