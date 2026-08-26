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

`-n N` and bare `-N` take a positive decimal integer and mean the first N items
in each declared row set after filtering and effective ordering. Zero,
negative, and non-integer counts reject. They make no ranking claim. `--head`
names the default first-N direction explicitly; `--tail` changes it to last N.

Bare `-N` is an option token, not a textual rewrite of every dash-digit token.
It is recognized only before the `--` option terminator and when the token is
not the required value of a preceding option. Thus target value-less
`--versions -5` selects five version rows, while `--type -5` binds `-5` as the
required selector value and rejects it under the retired numeric-selector
rule. Value-less and optional-value options do not consume a following
dash-digit token; a valid negative optional value must use that option's inline
form. One invocation may carry only one count spelling; `-n=5` is the inline
form of `-n 5`, while combining either with `-5` rejects.

The adjacent concepts keep distinct names:

| Gesture | Meaning |
| --- | --- |
| `-n N` / bare `-N` | First N items in each declared row set. |
| `-n N --head` | First N items, with the default direction explicit. |
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

A producer that cannot establish a truthful suffix rejects item-mode `--tail`
rather than treating the end of an upstream prefix as the end of the source.
Bounded `package search` is the explicit case below.

## Declared row-set scope

An item is the producer-declared row unit, never a rendered line or an inferred
piece of presentation:

- packages in a package query;
- types or members in an API listing;
- rows in a table or list;
- directed logical edges in a graph;
- files, versions, or `(version, feed)` observations in a lens that declares
  those rows.

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

An empty intersection is an honest empty result. `--head` and `--tail` cannot
modify an absolute row range. Either can modify an accompanying item count or
an independent line window:

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
-> apply item-mode -n/--head|--tail or --top
-> intersect --rows range
-> project columns, fields, values, paths, URLs, or printable payloads
-> apply per-payload --lines window when requested
-> render
```

`--count` branches after filtering and before ordering or windows. It reports
the full matched-row count within the declared input extent, so it is mutually
exclusive with `-n`, `--top`, `--rows`, `--row`, `--head`, `--tail`, and
`--lines`; no accepted selector may be silently ignored. For an exhaustive
input that is the source cardinality. For an explicitly upstream-bounded input
it is the complete count of the bounded candidate set, never an extrapolated
corpus total.

`--row` remains an exactly-one address and is mutually exclusive with item-mode
`-n`/`--head`/`--tail`, `--top`, and `--rows`. It may combine with `--print`,
`--value`, `--urls`, or `--paths`, and with line-mode
`-n`/`--head`/`--tail` under `--lines`.

`--lines` and the direction flags each require an active `-n` window and reject
when bare. `--lines` changes that window's unit; `--head` and `--tail` direct
the active item or line window and are mutually exclusive. An absolute range
alone rejects either direction, while a range may intersect an independently
directed item or line window as shown above.

### Cost and completion

A semantic result limit is not automatically a work budget:

- Natural streaming order may stop after N matching rows.
- A request must exhaust the applicable input when provider order cannot
  determine the requested declared rows. Global `--order-by` and `--top`
  normally require exhaustion; last-N does unless provider order delivers the
  declared suffix first.
- An absolute range may stop after its closed upper bound when the producer's
  order is already final.
- `--lines` limits projected text, not acquisition. It does not authorize
  partial artifact reads or change whether a selected payload must be fetched.
- A printable section declares when each row may require separate network
  acquisition. Multi-row `--print` over such a section requires an explicit
  finite item bound: `--row`, item-mode `-n`, `--top`, or a closed `--rows`
  range. Line-mode `-n`, `--where`, and an open-ended range do not bound that
  fan-out.
- `package search` uses a provider extent of 20 rows per configured source when
  no item count is supplied. Item-mode `-n N` changes both the per-source
  provider extent and global retained-row cap to N; this is the ordinary
  naturally ordered streaming case, not a general work-budget interpretation
  of `-n`. Resolved source order is semantic priority. Within each source,
  provider order is retained; the merged sequence keeps the first occurrence
  of each `(ordinal-ignore-case package id, normalized version)` identity, then
  applies the global cap. Reversing configured source order may therefore
  change retained rows deliberately.
- Package-search `--tail` rejects. A bounded relevance-ordered page cannot
  establish the suffix of the remote result set, and treating the last N rows
  of an N-row provider request as a suffix would be success-shaped nonsense.
- Search completion has separate source and selection components. Each source
  reports exhausted when its successful page is shorter than its provider
  extent, upstream-bounded when the page fills that extent, failed, or
  cancelled. The merged selection reports cap-reached when eligible unique
  rows are discarded by the global cap. Human completion text summarizes every
  non-exhausted component, including source count and discarded-row count;
  partial source failure remains visible. Because NuGet `totalHits` is not
  reliable across supported feeds, no full page claims source exhaustion. A
  short successful page from every source with no failures may report
  exhausted.
- Bare search `--count` counts the merged, deduplicated bounded candidate set
  before the global retained-row cap. It remains a scalar on stdout, and the
  selection's cap-reached state does not apply because `--count` branches before
  selection. Any non-exhausted source state is a non-suppressible stderr note.
- Expensive promoted-tier queries retain their own explicit candidate/work
  budget, such as the proposed package-query `--deepen` bound.

Completion text must distinguish exhausted, bounded, failed, and cancelled
inputs. A result cap never upgrades a bounded search into an exhaustive claim.

## Ranking and `--top`

`--top` is a validated composition, not a second general count flag. Its N is
a positive decimal integer; zero, negative, overflowed, and duplicate values
reject:

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
- `--top` is mutually exclusive with item-mode `-n`, `--head`, and `--tail`. It
  may combine with `-n N --lines` and a line-mode direction.
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
no longer unary, but it requires exactly one declared row set after subject and
section selection. A selection spanning multiple sections, inspections, or
per-subject row sets rejects before capability checks or acquisition; narrow it
to one row set first.

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
| <content line 1>
| <content line 2>
```

The frame uses contained typed row identity, not text parsed back from a
rendered cell. Every successful frame includes the selected line range, total
line count, and whether content was truncated. Full acquisition makes that
metadata available even when the content projection is clipped.

Line boundaries are CR, LF, and CRLF, with CRLF treated as one boundary. An
empty payload has zero lines. A terminator ends its line but does not create a
phantom line after it, so `"a\n"` has one line and `"\n"` has one empty line.
Consecutive terminators create consecutive empty lines, and a non-empty final
segment without a terminator is a line. Other Unicode separators remain content
and pass through the terminal-safety encoding. An empty successful payload has
no selected range, total zero, and `truncated: false`; framed text renders that
state as `lines none of 0`, while structured range endpoints are null.

Framed text prefixes every rendered payload line with the tool-owned `|`
followed by one space, including empty lines, and inserts a tool-owned line ending after the
last payload line when another frame follows. A payload line that looks exactly
like a frame therefore remains visibly and structurally payload text. Frame and
gutter text do not consume the line budget. Identity-dependent machine
consumers should prefer `--jsonl` or `--json-array`, which carry explicit
objects rather than presentation framing.

`--bare` remains valid only when exactly one row is selected. It removes both
the frame and payload gutter. Multiple unframed payloads are rejected because
their boundary and row identity would be lost.
Unstructured `--out` payload export likewise remains unary; a multi-item file
export requires a structured format or a separately designed directory export.
Line windows reject unscoped exact `--out`; scoped-text and structured output
written to a file may carry clipped content because neither claims original
payload bytes.

Print projection formats are exclusive and determine what `--out` means:

| Mode | Cardinality | Destination and envelope |
| --- | --- | --- |
| Normal text | Batch | Framed stdout. |
| `--bare` | Unary | Terminal-safe payload-only stdout; rejects structured formats and `--out`. |
| Unscoped `--out` with no structured format | Unary | Full provider payload file; rejects `--bare`. |
| `--frontmatter` / `--body` plus unstructured `--out` | Unary | Projected UTF-8 text file, not an original-byte claim; rejects `--bare`. |
| Plain `--json` | Unary | One result object on stdout or at `--out`. |
| `--jsonl` / `--json-array` | Batch | Result objects on stdout or at `--out`. |

`--json`, `--jsonl`, and `--json-array` reject one another. `--out` is only an
exact-payload mode when no structured format or payload scope is present. With
`--frontmatter`/`--body`, it writes the transformed text. With a structured
format it selects that format's destination.

General document/section formats do not acquire a meaning under `--print`.
Explicit `--table`, `--tsv`, `--markdown`, `--plaintext`, `--mermaid`,
verbosity, and their format-only modifiers reject before acquisition or
destination mutation. `DOTNET_INSPECT_FORMAT` does not apply to payload
projections; a caller chooses JSON/JSONL/JSON-array explicitly. A
registry-derived gate keeps this deny set complete as formats are added.

These unary modes do not invent a per-row envelope. After successful
capability preflight, an acquisition failure under `--bare` emits no stdout,
writes a diagnostic to stderr, and exits nonzero. Exact or scoped-text
unstructured `--out` completes acquisition and any requested Markdown
transformation before opening its destination; a failure therefore leaves an
absent destination absent and an existing destination unchanged, emits a
diagnostic, and exits nonzero. Successful `--bare` emits only the terminal-safe
payload. Successful unscoped `--out` emits the provider payload bytes;
when a provider declares only text, it emits that full text in the
repository-standard UTF-8 encoding without a frame or gutter. Exact byte
fidelity is claimed only when the provider declares bytes. Frontmatter/body
output emits only the requested text projection using repository-standard
UTF-8 text output.

All file modes preserve the destination through validation and preflight.
Unstructured unary `--out` additionally completes acquisition/transformation
before publication. Structured output treats acquisition failure as result
data: plain JSON writes its one typed failure object, while JSONL and JSON-array
retain one complete result per selected row and continue later rows. A
structured destination may therefore replace an existing file even when the
command exits nonzero. No mode adds crash-safe or disk-failure transaction
semantics once an otherwise valid write begins; that broader filesystem
contract is outside #4677.

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

If the selected row set declares no printable capability, `--print` rejects the
request once before payload acquisition or stdout. It does not emit one failure
per row for a shape that cannot print any row.

After that row-set preflight, every selected row in framed or structured batch
output produces a visible success or failure result. A heterogeneous row that
does not declare a printable payload, or whose payload acquisition fails, is
not skipped. Text output emits a framed failure; structured output emits a
typed failure row. Other selected rows continue, and the command exits nonzero
when any row failed. Unary `--bare` and unstructured `--out` use the
diagnostic-only failure contract above instead of an output envelope.

This is batch-result behavior, not a success-shaped fallback. A zero-row
selection remains an error for `--print` and rejects before acquisition,
stdout, or destination mutation.

## Line windows

`--lines` changes the unit carried by `-n`; it does not carry another number
and rejects without an active `-n`. Therefore one invocation cannot use `-n`
for both item and line counts.

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

## Compatibility and migration

The implementation removes command-specific result counts and short selector
aliases without a deprecation period:

- numeric and nonnumeric `-t`;
- numeric and nonnumeric `-m`;
- numeric values on the surviving long `--type` and `--member` selectors;
- count values on `--versions` and `--versions-with-feed`;
- package-search `--take`; search instead requests 20 rows per configured
  source and retains 20 merged rows by default, while item-mode `-n N` uses N
  for both extents;
- count-form `--rows N` and `--rows N --tail`;
- implicit line mode on `-n`.

The target also changes the default text presentation of `--print`: normal
stdout is framed and guttered even when one row is selected. Maintained guidance
that consumes one payload body adds `--bare`; guidance that consumes multiple
results uses the framed form or a structured batch format. This audit is part of
the same atomic implementation change as the retired syntax migration. Every
command that exposes `--print` must also register `--bare`; `library` is the
known released gap that the implementation closes. Every print-capable command
must also register `--out`; `library`, `type`, and `member` are the known
released gaps. Registration is not enough: each route must propagate both
options to its print writer.

Surviving-spelling compositions also change. Current `--count --rows N..M`
returns the size of the window, while current `--count --top N` succeeds after
silently dropping `--top`. The target rejects `--count` with every item, range,
or line window. This preserves the existing requirement that a count describe
the payload it accompanies and prevents a successful ignored limiter. Existing
count/window tests migrate to `CountRejectsItemAndLineWindows`, with explicit
negative fixtures for both `--rows` and `--top`.

Current `--top N` is also accepted and silently ignored outside Performance
Triage even though the option is registered on `library`, `type`, and `member`.
The target rejects it on a sequence-default section unless explicit
`--order-by` supplies a ranking. This is a surviving-spelling behavior change,
not merely new validation for Performance Triage.

Current `--print --bare --out` succeeds with `--bare` silently ignored; the
target rejects that ambiguous pair. Existing positive output-alias/value-token
fixtures `SkillDocuments_OutputAliasesWritePackageAndProjectPayloads` and
`Package_ReadmeInAValuePosition_IsNotMistakenForTheRemovedFlag` migrate to
negative combination fixtures plus separate bare-stdout and
unstructured-output coverage.

Current row-window validation rejects every `--rows` composition with `-n`,
every range with `--head`/`--tail`, and every `--print --rows` request. The
target instead intersects a range with an independently directed item or line
window and permits multi-print over that selected range. Existing negative
fixtures for those blanket validators become the positive fixtures of
`WindowModifiersBindOnlyToActiveCount` and
`MultiPrintLineWindowsArePerPayload`.

Retiring `--take` does not make remote search exhaustive. Bare
`package search` requests 20 rows per configured source, deduplicates in
configured-source/provider order, retains at most 20 globally, and discloses
the source and selection completion components. `package search --count` uses
the same per-source extent and deduplication but reports the complete merged
candidate count before the global cap; it has no selection completion component
and discloses every non-exhausted source component.
`package search ... -n N` replaces the user-directed result-count use of
`--take`; `--count` remains incompatible with `-n`, and package-search
`--tail` rejects because the bounded pages do not establish a remote suffix.

The replacement for count-valued `--versions-with-feed N` also changes the
counted noun. For a bare package, the retired spelling keeps the newest N
distinct versions and all feed rows for each; target
`--versions-with-feed -n N` keeps the first N `(version, feed)` rows. A package
range uses its caller-directed Vector for both forms. This is the universal
declared-row rule: cross-feed duplication is a visible result, not a hidden
expansion outside the item limit.

Long `--type` and `--member` selectors remain where the command needs them;
their numeric count interpretation does not. A bare integer value on either
long selector rejects instead of becoming a literal type/member-name filter.
Positional member syntax remains. `--versions` and `--versions-with-feed` become
value-less lens selectors and compose with `-n`.

Numbers that are not result counts remain distinct: `--row` addresses one row,
`--index` addresses one overload, `--depth` controls traversal, and timeout or
budget options retain their own units.

The implementation PR updates all maintained command guidance in the same
change, including `README.md`, `docs/**`, `prompts/**`, runnable workflows, and
every shipped `skills/**/SKILL.md` reference. Historical prose may still name a
retired spelling to explain the migration, but no maintained invocation may
recommend or execute it. Internally generated argv, router rewrites,
diagnostics, tips, and help examples change in the same commit. No compatibility
alias is required; an old spelling may receive direct replacement guidance,
but must not execute with its old meaning.

## Required gates

The implementation must provide named Release gates for these target properties:

| Gate | Contract |
| --- | --- |
| `ItemLimitsUseDeclaredRowsAcrossFormats` | After filtering and effective ordering, every renderer selects the same first/last logical rows per declared row set and preserves non-row sections, including fixtures where limiting the unfiltered or naturally ordered prefix would select different rows. |
| `AbsoluteRangesIntersectWithoutRenumbering` | `--rows` range intersections retain stable row addresses across item limits and rankings. |
| `SingleRowAddressRejectsWindows` | `--row` rejects item-mode `-n`/`--head`/`--tail`, `--top`, and `--rows`, while remaining compatible with a line window. |
| `First_And_Last_ResolveToDisplayedEndpoints` | Gap-producing `--value`, `--urls`, and `--paths` projections resolve `first` and `last` to the first and last projected rows without renumbering their stable addresses; normal framed and structured `--print` retains every selected row as a success or failure. |
| `CountReportsFullPostFilterCardinality` | `--count` reports the full cardinality within the declared input extent after filters and before ordering or windows, including zero matches, aggregate inspections, an exhaustive source, and a multi-source upstream-bounded candidate set with enough unique rows to exceed the global display cap, without extrapolating a corpus total. |
| `CountRejectsItemAndLineWindows` | `--count` rejects every row address, result/line window, or direction, with explicit negative fixtures for `--row`, `--head`, current windowed `--rows` counting, and silently ignored `--top`. |
| `WindowModifiersBindOnlyToActiveCount` | `--lines`, `--head`, and `--tail` each require one active `-n` window and reject when bare. `--lines` changes its unit; `--head` and `--tail` direct the item or line window, reject together, and never modify an absolute range or ranking. Positive fixtures cover `-n N --tail --rows A..B` and `--rows A..B --print -n N --lines --tail`; negative fixtures cover bare `--lines` and a range with bare `--head` or `--tail`. |
| `UniversalLimitShorthandIsArityAware` | Separate and inline `-n`, bare `-N`, zero, duplicate counts, the `--` terminator, required-value, optional-value, and value-less options prove that only one positive count option is recognized; numeric long `--type`/`--member` values remain selector values and reject. |
| `TopRequiresRankingOrder` | `--top` takes one positive decimal value, requires explicit order or a schema-declared ranking default, rejects item-mode `-n`/`--head`/`--tail`, renders "top N by ..." only for `--top`, renders "first N"/"last N" for plain `-n`, and suppresses those human notes in structured and quiet output. Zero, negative, overflow, duplicate, and sequence-default `library -S References --top N` fixtures prevent a nonpositive or ignored value from becoming unbounded. |
| `AddressProjectionDoesNotAcquirePayloads` | `--paths` and `--urls` project selected row addresses without fetching printable content. |
| `MultiPrintRequiresOneRowSet` | `--print` rejects selections spanning multiple declared row sets before capability checks, acquisition, stdout, or destination mutation. |
| `NonPrintableRowSetsRejectOnce` | A selected row set with no printable capability emits one preflight diagnostic without payload acquisition or stdout and leaves an absent destination absent and an existing destination byte-for-byte unchanged. |
| `MultiPrintPreservesIdentityAndFailures` | After print-capability preflight, every selected row in framed or structured output emits one success/failure result, and any failure makes the exit nonzero. |
| `MultiPrintFrameFieldsAreContained` | Adversarial row identity, path, URL, and failure values cannot forge a frame or emit live terminal controls. |
| `MultiPrintPayloadCannotForgeFrames` | Payload lines matching the frame grammar, mixed line terminators, empty lines, and missing final newlines remain guttered payload and cannot create a sibling frame. |
| `MultiPrintLineWindowsArePerPayload` | Line budgets exclude frames, apply independently per payload, and preserve complete structured values. |
| `MultiPrintLineMetadataIsExact` | Full, head, and tail projections report the exact selected range, total line count, and truncation state in framed text, JSONL, and JSON-array output; fixtures cover CR, LF, CRLF, mixed and consecutive terminators, empty payloads, terminal newlines, and missing final newlines. |
| `OrdinaryLineWindowsApplyAfterRendering` | Ordinary head/tail line windows and `--top` plus line-mode `-n` preserve the selected item set and clip the final text only. |
| `NonPrintJsonRejectsLineWindows` | Typed and lowered document JSON reject `--lines` with empty stdout; printable JSON clips content before complete-value encoding. |
| `MarkdownScopeRejectsMixedSelectionAtomically` | `--frontmatter` and `--body` inspect only selected rows, but one selected non-Markdown document rejects the whole `--print` or `--content` request before acquisition, per-row output, stdout, or destination mutation. |
| `RemoteMultiPrintRequiresBoundedSelection` | A per-row network payload source rejects multi-row `--print` without an explicit finite item bound, performs no fetch or stdout, and leaves an absent destination absent and an existing destination byte-for-byte unchanged. |
| `SelectedRowsBoundPayloadAcquisition` | Instrumented payload providers are called exactly once for each selected row with an acquired payload, and never for non-printable, filtered, unselected, or windowed-out rows. |
| `RejectedExportsPreserveDestination` | Every preflight rejection path produces no stdout, leaves an absent destination absent, and leaves an existing destination byte-for-byte unchanged. |
| `ExactPayloadOutRejectsLineWindows` | Unscoped exact `--out` rejects line windows before acquisition or destination mutation; scoped-text and structured file output may carry clipped content. |
| `UnaryPrintModesRejectMultipleRows` | `--bare`, plain `--json`, and unstructured `--out` reject multiple rows before stdout or acquisition, leaving an absent destination absent and an existing destination byte-for-byte unchanged. |
| `UnaryPrintFailuresAreVisible` | A unary acquisition/transformation failure under `--bare` or unstructured `--out` emits no payload/stdout, reports a diagnostic, exits nonzero, and for `--out` preserves an absent or existing destination because work precedes publication. |
| `ScopedPayloadOutIsProjectedText` | Unscoped `--out` preserves declared provider bytes or emits the provider's full declared text in the repository-standard UTF-8 encoding, while `--frontmatter`/`--body --out` emits only the selected Markdown text; line windows apply only to the projected-text case, and both acquire/transform before publication. |
| `PrintModeCombinationsAreUnambiguous` | `--bare`, unstructured `--out`, plain JSON, JSONL, and JSON-array accept only the cardinalities and combinations in the format matrix; structured and scoped-text `--out` retain their modes, while `--bare --out`, ambiguous format pairs, every registered document/section format, explicit verbosity, and format-only modifiers reject before acquisition or destination mutation. The expected deny set derives from the format registry, and `DOTNET_INSPECT_FORMAT` is ignored for payload projection. |
| `StructuredOutRetainsFailures` | After successful preflight, plain JSON `--out` writes its typed unary success/failure object; JSONL and JSON-array write one complete result per selected row, retain typed acquisition failures, continue later rows, and exit nonzero. Structured modes do not claim unstructured acquisition-before-publication semantics. |
| `ZeroRowPrintRejectsAtomically` | An empty selection exits nonzero without acquisition, stdout, file creation, truncation, overwrite, or replacement. |
| `ResultLimitCompletionStatesAreHonest` | Source-exhausted, cap-reached, upstream-bounded, failed, and cancelled components remain distinct and composable. Package-search fixtures cover one and multiple sources, full and short pages, overlap, configured-source priority reversal, global-row discard, partial failure, a scalar bounded count before the global cap with no selection component, `-n N` provider/global extents, and rejected tail. Only a successful short page from every source with no failure may claim exhaustion. |
| `VersionSelectionRespectsProviderOrder` | An instrumented ascending lazy source proves bare newest-first and both caller-directed range Vectors preserve their declared addresses; both literal endpoints are validated before any limited range result; missing far endpoints reject in both directions; first-N, last-N, and absolute ranges exhaust or stop only when provider order can determine the requested rows; and report line windows do not shorten metadata enumeration. |
| `VersionFeedLimitsCountRows` | `--versions-with-feed -n N` selects N `(version, feed)` rows in the containing Vector's direction rather than N distinct versions with unbounded feed-row expansion; both range directions retain that order, and equal-version fixtures use labels ordered opposite their canonical producer keys to prove the exact key-ordered cutoff under reversed source declarations. |
| `LegacyResultLimitSpellingsAreAbsent` | CLI aliases, generated argv, router paths, runtime diagnostics/tips, help, and maintained invocations in README, docs, prompts, workflows, and embedded skills contain no retired spelling; negative execution tests reject every retired grammar, including numeric long `--type`/`--member`, value-bearing `--versions`/`--versions-with-feed`, and count-form `--rows`, while affected replacement routes execute successfully. |
| `LegacyLineLimitInvocationsDeclareLines` | A generated inventory classifies every maintained/generated `-n` or bare-numeric invocation by item or line intent; every former renderer cap carries `--lines`, including close fixtures where an item limit intentionally does not. |
| `PrintCommandsWireAllModes` | A registry-derived fixture enumerates every command exposing `--print`, asserts that it also parses `--bare` and `--out`, and executes unary and multi-row printable fixtures through each route. It verifies payload-only bare output, full unstructured destination output (exact when the provider declares bytes), multi-row bare rejection, and framed, JSONL, and JSON-array multi-row success with exactly one identity-preserving result per selected row. It includes `library`, `type`, and `member` so removing option propagation fails the gate. |
| `PrintGuidanceMatchesFramingContract` | Maintained `--print` guidance uses `--bare` for a unary payload body, unstructured `--out` for a full unary payload export, and framed text or a structured batch format when row identity and boundaries matter; representative maintained invocations for each print-capable command execute through the real command tree. |
