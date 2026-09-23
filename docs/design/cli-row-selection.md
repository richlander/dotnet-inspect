# CLI row-selection grammar

## Status and owner

Focused L3 design proposal for
[#5414](https://github.com/richlander/dotnet-inspect/issues/5414), part of
[#4677](https://github.com/richlander/dotnet-inspect/issues/4677).

This document owns the `dotnet-inspect` command-line grammar and lowering
boundary for semantic row selection and rendered-line selection. Every output
command that exposes `-n` has one effective item sequence. An active semantic
row adoption supplies that sequence. Otherwise, the command's rendered lines
are the sequence. `-n N` takes the first *N* items from that effective sequence;
`--tail` takes the last *N*. `--lines` and `--tail-lines` explicitly select the
rendered-line sequence even when semantic rows are available. Legacy `--rows`
contracts remain command-owned until their semantic adoption.

The package `--versions` and `--versions-with-feed` lenses, finite `demo list`
catalog, `find`, `implements`, `extensions`, `depends`, `ecosystem`,
`vocabulary` value rendering, `diff --history`, `package query`, package activity,
projected member Facts JSON, Workspace top-level inventory, and Integration
graph edges, a single package's layout lens, `Package files`, or
`SourceLink: Files` section,
one selected Project document section, explicit-source Type catalog listings,
`match --similar` ranked candidates, and the exact `Clone Candidates` section
for Library, Type, or Member, plus exact Member `Calls` and `Callers`, and the
Graph Libraries default or exact `Direct Use Clusters`, the Graph Cluster
default or exact `Call Sites`, and exact `Consumer Use Sites` or `Provider API
Types` cohorts have semantic `-n` adoption. Their supported Window and
direction capabilities remain command-specific. These adopters also accept
explicit rendered-line selection where their output format permits it.
Unselected modes of a partially adopted command use the rendered-line
fallback.
Commands without an active semantic row adoption, including text documents and
structured commands whose item rows have not yet been adopted, lower bare `-n`
to rendered-line selection. Explicit `--lines` remains accepted as redundant
unit selection.

Implementation is partial. #5644 implements value parsing, ordered lowering,
modifier composition, Top-order attachment, typed capability rejection, and
structured failure selection over already-owned explicit-command option
occurrences. #5678 implements the explicit-command adapter that establishes
required-value ownership from System.CommandLine, normalizes eligible bare
shorthand, preserves raw positions, extracts those occurrences, and invokes
the lowerer. #5786 installs that adapter for plural package-version listings,
renders its L3 failures before package acquisition, and carries typed intent
through L2 row-cohort selection after source aggregation. The general implicit
route envelope is implemented by #5784 as a pure pre-acquisition classifier;
issue #6327 constructs candidates from the real package, library, type, and
member commands and each selected lens declaration, then runs the envelope
before the commandless router enters target acquisition. #6379 adopts the
finite product-demo catalog for explicit `demo list` and equivalent bare
`demo` listing. #6489 adopts `find` across API search, package profile,
Package Query, and literal Package Query modes, including shared semantic
selection and Count evidence. #6643 adopts product-vocabulary value rows across
selected sections. #6650 adopts timeline Evaluation and Transition rows while
preserving its explicit package-cell acquisition plan. The broad #4677 line
unit rollout defines rendered lines as the fallback item sequence, adds shared
`--lines`/`--tail-lines`, and retires numeric `-t` as a row-count spelling on
`implements` and `extensions`. Numeric `-m` is likewise ordinary member-filter
input rather than a count spelling on Type and Member routes. Nonnumeric `-t`
and `-m` remain selector aliases. The Workspace top-level inventory adoption
selects complete owner-issued entries after kind filtering without reducing
Workspace construction or acquisition. The Integration graph adoption selects
logical edges after complete induced-set construction without reducing package
acquisition or graph production. The Package `SourceLink: Files` adoption
selects complete package-library/type/URL rows after SourceLink collection and
type filtering without reducing package, library, or PDB acquisition. The
Package layout adoption selects complete normalized file paths after scoped
enumeration, plumbing exclusion, and sorting without reducing package
acquisition or archive extraction. The
Package `Package files` adoption selects complete ordered package-file rows
after archive extraction, full file enumeration, and optional path filtering.
The Project document adoption selects complete restored-package Skill or root
README rows after inventory construction and validation when exactly one
section is selected. Multi-section Project output remains outside that
declaration. The Type catalog adoption selects complete `ApiType` entries after
type, kind, and unsafe filtering when package, library, platform, or project
source selection makes the catalog interpretation unambiguous. The
`match --similar` adoption selects Analysis-ranked structural candidates after
retrieval while preserving complete non-row retrieval evidence. The Clone
Candidates adoption selects the Query-issued global candidate-pair ranking
while preserving complete coverage and work evidence. Semantic adoption for
the remaining command row sets is still staged.

Only the implemented subsets are verified by their named Release gates in
[Required gates](#required-gates). Every other asserted behavior remains
unverified until its named gate lands.

Related owners:

- [Semantic row selection](semantic-row-selection.md) owns ordered `Head`,
  `Tail`, `Window`, and `Top` execution after L2 constructs an executable plan.
- [Inspection layers](inspection-layers.md) owns the L3-to-L2 boundary: L3
  produces typed operation intent, and L2 resolves that intent against declared
  row sets and effective order.
- [Row query and ordering](row-query-order.md) owns predicate and effective-order
  resolution, including whether an order is a ranking.
- [Section-row shaping](section-row-shaping.md) owns declared-row-set binding,
  projection, `Rows`, and `Count` meaning.
- [Source delegation](source-delegation.md) owns any semantics-preserving source
  optimization.
- [Item and line selection composition](item-and-line-limits.md) sequences these
  owners without redefining them.

## Authority and scope

The L3 CLI row-selection grammar is the authority for:

- item, absolute-window, ranked-selection, direction, and line-selection
  spellings;
- token arity, aliases, shorthand rewriting, and end-of-options behavior;
- preserving the relative order of semantic selection gestures;
- constructing one typed, presentation-free operation-intent sequence;
- command-level adoption declarations and capability rejection;
- conflicts that are decidable from CLI intent alone; and
- one deterministic L3 diagnostic when CLI lowering fails.

This design does not own:

- declared row sets, row identity, predicates, projection, or `Count` meaning;
- resolution of an effective order or whether that order is a ranking;
- executable `RowSelectionPlan` construction or semantic stage execution;
- source acquisition, pagination, stopping, deduplication, or completion
  evidence;
- payload projection, printing, export, or destination publication;
- where or how rendered-line selection is applied to a report or payload; or
- Markout rendering.

## Effective item sequence

`-n` is an item limiter, conceptually operating over an `IEnumerable<T>`:

1. If the active command or selected lens declares semantic item rows, those
   rows are the effective sequence.
2. Otherwise, each rendered output line is one item in the fallback sequence.
3. `--lines` selects the rendered-line sequence explicitly, overriding an
   available semantic sequence.

The fallback is determined from the typed command or lens declaration before
execution. L3 does not inspect rendered text, infer rows from table-shaped
output, or change the unit according to which downstream renderer happens to
handle the result. A table-like command without semantic adoption therefore
uses rendered lines until it declares and supplies semantic rows.

`-n N --tail` selects the last *N* items from the same effective sequence.
`-n N --lines`, `-n N --tail-lines`, and their equivalent supported modifier
compositions continue to select rendered lines explicitly. Bare `-N` is only a
compact spelling of `-n N`; it follows the same effective-unit rule.

Inferred and explicit line selection have the same downstream contract. Both
must pass line-output validation before command work, reject complete JSON
documents that cannot remain valid after clipping, and preserve exact-output
and destination-publication protections.

L3 may reject a combination because its command has not adopted the required
adjacent capability. It may not invent that capability or define the adjacent
owner's behavior to make the combination succeed.

## Explicit-command lowering boundary

The first implemented slice begins after an explicit command has assigned each
row-selection occurrence its option identity, value where applicable, and argv
position. It does not split attached tokens, classify option arity, rewrite bare
`-N`, or participate in command routing.

The lowerer parses count, Window, and Top values; applies direction and line
modifiers; preserves the argv order of semantic gestures; attaches the one
typed opaque `--order-by` operand to Top when Top is present; otherwise
preserves it as baseline-order input; and checks the active command's typed
capability declaration. Success contains L2-owned `RowSelectionIntent` plus
optional L3 rendered-line intent. Failure is structured and content-free; this
slice does not render a diagnostic or echo argv text.

Capabilities follow the lowered unit. A count that survives as semantic Head
or Tail requires the semantic Head/Tail capability. A count redirected by
`--lines` or `--tail-lines` requires only the rendered-line capability, because
it contributes no semantic operation.

Value failures are selected before repetition and modifier conflicts, which are
selected before capability failures. Within each category, argv position
selects the first failure except that absence conflicts complete at end of argv
and use the first modifier's position. Token, arity, routing, L2-resolution, and
diagnostic precedence remain unimplemented.

The CLI project and reusable L2 project temporarily contain types in the same
`DotnetInspector.Sections` namespace while existing section pipelines remain in
the CLI assembly. This slice introduces no colliding type and uses only the
L2-owned intent contract; the broader namespace migration remains owned by
[Inspection layers](inspection-layers.md).

## Explicit-command argv adapter boundary

The explicit-command adapter takes one command tree, the raw argument array,
the active command's typed row-option identities, and its capability
declaration. It performs an ownership parse, normalizes eligible bare shorthand,
reparses only when normalization changes the token sequence, extracts typed
occurrences with original raw argument positions, and invokes the
explicit-occurrence lowerer.

Required-value protection comes from option results in the ownership parse,
including parent-bound options and attached values. It does not use a static
option-name list. An optional-valued or zero-arity option does not protect a
separate shorthand-shaped token even when System.CommandLine initially assigns
that token as its value. Occurrence extraction and row-specific arity checks
also require the authoritative parse token to be an option token for the bound
alias; row-option-shaped text owned as another required option's value does not
create a row-selection occurrence.

Bare shorthand recognition is lexical and ASCII-only. Zero and overflowing
decimal text normalize to `-n` and reach the common value failure rather than
becoming unrelated unknown options. Both normalized tokens retain the original
raw token's position. The adapter normalizes only when the bound limit option
actually exposes the `-n` alias and the earliest active command declaring that
option owns option syntax at the raw token's position. It does not hoist
shorthand across a command boundary: earlier tokens remain ancestor positional
input or retain their ancestor parse diagnostic. Tokens after `--` are not
normalized or extracted.

The adapter disables System.CommandLine's POSIX multi-option bundling and
response-file token replacement for both parse passes. The adapter itself
normalizes the one documented compact `-nN` form; a broader bundle such as a
separate short option joined with `-nN` is not an additional spelling in this
grammar. `@`-prefixed arguments remain literal command input rather than
introducing a second source-position domain. This deliberate narrowing applies
to the parse result returned by the adapter and must remain visible in each
command adoption.

System.CommandLine accepts a boolean attached value such as `--head=true` even
when an option is declared zero-arity. The adapter records an attached-value
failure for the four row-selection modifiers from the raw token so the grammar
does not inherit that boolean convention. A following separate token remains
independently parsed; the host's
[common option-value validation](cli-option-value-validation.md) diagnoses
surplus input after a zero-arity flag without rejecting valid positionals.
The adapter similarly records a missing-value failure when an exact
row value option is followed by end of argv, `--`, or a known option token;
signed numeric text remains a value for common validation.

The four value-bearing row options use repeatable raw-string option identities
with one value allowed per token. This parser shape preserves each occurrence
without producing System.CommandLine's scalar-option aggregate error; the
explicit-occurrence lowerer therefore owns repeated-gesture failure. The
adapter preserves parser errors and structured row-arity failures but does not
yet select or render the one diagnostic when both exist.

This adapter is installed only for explicitly registered command or lens
adoptions: the plural package-version lenses, demo listing, ecosystem catalog,
and vocabulary value rendering. Existing behavior for unregistered command
surfaces and the general implicit-routing envelope remains unchanged.

Existing options-first implicit package routing preserves direction-modifier
presence arity when a prospective package parse owns the selected plural lens.
Explicit commands and selector-shaped required values retain their existing
binding. This does not implement the general route-independent envelope below.

## Convention and deliberate divergence

GNU `head` and `tail` establish the familiar short count gesture and
first-versus-last direction:

- [`head -n N`](https://www.gnu.org/software/coreutils/manual/html_node/head-invocation.html)
  keeps the first *N* lines;
- [`tail -n N`](https://www.gnu.org/software/coreutils/manual/html_node/tail-invocation.html)
  keeps the last *N* lines.

`dotnet-inspect` binds the default unit to the active command's declared items.
Rendering is not normally the semantic source of truth, because commands
return useful items such as packages, types, dependencies, and graph edges.
When no semantic item sequence is declared, rendered lines are the only
available item sequence and therefore the default. Explicit `--lines` retains
the Unix text operation as a unit override on semantic line-capable surfaces
and as redundant clarity on fallback surfaces.

Kusto's
[`top N by Expression`](https://learn.microsoft.com/en-us/kusto/query/top-operator)
requires a ranking expression and is equivalent to sorting before taking
*N*. `dotnet-inspect --top N` follows that distinction: it is not a second
spelling of `-n N`; it is valid only when L2 resolves a ranking order.

`System.CommandLine` treats tokens according to the active command and option
arity. Bare `-N` rewriting follows that parsed ownership rather than assuming
that every hyphenated integer is a limiter. This is stricter than traditional
obsolete `head -N` recognition because `dotnet-inspect` has required-value
options for which a negative integer can be the value.

Window coordinates use the established CLI convention of positive, one-based,
inclusive positions. Unlike C# `Range`, they are not zero-based or end-exclusive
and do not accept from-end `^N` operands. Unlike lenient text utilities, a
semantic Window has the strictness defined by
[Semantic row selection](semantic-row-selection.md#normalized-plan).

## Grammar

An adopted command may expose these gestures:

| Gesture | Typed L3 intent |
| --- | --- |
| `-n N` | `HeadIntent(N)`, or `TailIntent(N)` when `--tail` is present |
| bare `-N` | exact shorthand for `-n N` |
| `-n N --head` | explicit `HeadIntent(N)` |
| `-n N --tail` | `TailIntent(N)` |
| `--rows A..B` | closed `WindowIntent(A, B)` |
| `--rows A..` | suffix `WindowIntent(A, null)` |
| `--rows ..B` | prefix `WindowIntent(null, B)` |
| `--top N --order-by ORDER` | `TopIntent(N)` with an explicit unresolved ranking-order operation |
| `--top N` | `TopIntent(N)` with no explicit ranking-order operation |
| `-n N --lines` | first *N* rendered lines, not a semantic stage |
| `-n N --lines --tail` | last *N* rendered lines |
| `-n N --tail-lines` | exact sugar for `-n N --lines --tail` |

`N`, `A`, and `B` are strings of ASCII decimal digits whose parsed values must
be positive integers that fit the representation used by typed L3 intent.
Zero, signs, non-ASCII digits, and overflow fail value validation. `A..B`
requires `B >= A`.

Value-bearing options accept a separated value or the System.CommandLine
`=`/`:` attached forms. This includes `-n=N`, `-n:N`, compact `-nN`,
`--rows=RANGE`, `--rows:RANGE`, `--top=N`, and `--order-by=ORDER`. An attached
value retains the option token's argv position and does not become a separate
operation-intent position.

`--rows` accepts exactly the closed, prefix, and suffix Window forms in the
table. An integer, start-plus-count expression, or boundless `..` is invalid.

`--head` and `--tail` are zero-arity presence modifiers for the one `-n`
gesture. They consume no following token, conflict with each other, and fail
when no `-n` or bare `-N` is present. An attached value is an option-arity
failure; a following separate token is parsed independently according to the
active command.

`--lines` is a zero-arity unit modifier. `--tail-lines` is a zero-arity
modifier that requires `-n`, conflicts with `--head`, and supplies both line
unit and tail direction. Combining it with the equivalent `--lines` or
`--tail` modifier is tolerated redundancy and does not add another operation.

Exact repeats of the zero-arity `--head`, `--tail`, `--lines`, or
`--tail-lines` modifier are also tolerated as idempotent redundancy. Repeated
different directions still conflict.

`--top` takes its own positive count. It does not consume `--head` or `--tail`;
ranking direction belongs to its order operand. Because the CLI exposes at
most one intended `Top`, one explicit `--order-by` in the same invocation
attaches as that intent's unresolved ranking-order operation and is not also
promoted to baseline-order intent. L3 preserves the operand but does not
resolve it. Its position in argv does not create a stage or change the
`TopIntent` position.

Without `--top`, `--order-by` retains its L2-owned baseline-order role. Per
[row query order](row-query-order.md#ranking-order-for-top), `--top` with no
explicit `--order-by` reaches L2 with no explicit ranking-order operation; L2
alone decides whether the schema supplies a declared default Top ranking. A
default baseline order is not a ranking. This grammar does not provide two
simultaneous explicit order operands; a command that needs both an explicit
baseline order and a different explicit Top ranking must wait for a separately
designed spelling. The same limitation prevents an explicit baseline order
from composing with the default Top ranking: in an invocation containing
`--top`, the one explicit order always belongs to `TopIntent`.

Each of `-n`, `--rows`, `--top`, and an exposed `--order-by` may occur at most
once in one adopted invocation. This keeps modifier and Top-order binding
unambiguous while still allowing the three different stage kinds to compose.
Repeated stages remain supported by the semantic component for non-CLI
consumers and future grammar evolution.

## Ordered semantic intent

L3 preserves the argv order of `-n`, `--rows`, and `--top` after shorthand
rewriting. Direction and unit modifiers do not occupy an intent position:

- `--tail` changes `HeadIntent` to `TailIntent`;
- `--lines` removes `-n` from semantic intent and creates line-selection
  intent; and
- `--tail-lines` performs both changes.

Parent-bound options retain their original token position when the active leaf
command is determined. Router resolution lowers only the authoritative child
parse; a speculative router parse cannot commit a different operation order.

After L2 resolves intent into executable stages, examples over the logical
sequence `[1,2,3,4,5,6,7,8]` are:

```text
--rows 3..6 -n 2
Window(3,6) -> Head(2) -> [3,4]

-n 4 --rows 2..3
Head(4) -> Window(2,3) -> [2,3]

-n 2 --rows 2..3
Head(2) -> strict Window(2,3) failure
```

The final example is the pathological ordering case. Treating the gestures as
an intersection against original ordinals would return row 2 and hide the
missing third position. Preserving typed stage order makes the strict failure
observable before output.

When `-n` carries `--lines`, it does not participate in the semantic sequence:

```text
--rows 3..6 -n 2 --lines
semantic intent: WindowIntent(3,6)
line intent: first 2 rendered lines
```

The future line-selection owner decides where that line intent applies. This
grammar only keeps it distinct from semantic selection and acquisition.

`--count` is a separate terminal reduction request, not a selection stage.
L3 preserves semantic operation intent when `--count` is present so L2 can
apply its owned selection-before-Count contract. This design does not decide
whether a rendered-line request can compose with Count; the pending
payload/line owner and each adopting command must either define that composition
or reject it as an unsupported adjacent capability.

## Bare `-N` rewriting

L3 preprocessing has one order:

1. establish required-value, attached-value, and `--` ownership; and
2. normalize eligible bare `-N` shorthand.

Ordinary value, repetition, modifier, and capability lowering follows those
steps. Bare shorthand therefore already has `-n` identity when later conflicts
are evaluated.

L3 rewrites `-N` to `-n N` only when:

- it occurs before the `--` end-of-options marker;
- the token is `-` followed by one or more decimal digits;
- the active command exposes `-n`; and
- the token is not owned as the value of a preceding required-value option.

Recognition is lexical; zero and overflow still rewrite and then fail the
common numeric-value validation. This keeps shorthand-shaped invalid input
from becoming an unrelated unknown option while preserving the positive,
representable surface grammar.

An optional-valued or zero-arity option does not claim a following `-N` merely
because its parser could accept a value. A required-value option does claim its
value, including a negative integer that is semantically valid for that option.

The pathological matrix includes:

- `--required-number -5` — keep `-5` as the option value;
- `--optional-value -5` — leave `-5` available as limiter shorthand unless the
  parser establishes that it was explicitly attached to the option;
- `--flag -5` — rewrite `-5`; and
- `-- -5` — preserve the positional literal.

Attached option values such as `--required-number=-5` are never rewritten.

## Implicit routing

An explicit command gives L3 the active option arity and adoption declaration
before any command-owned work. An implicit bare-target invocation does not:
the target can route to more than one command, and deciding the route may
require platform or package resolution.

Before an implicit router performs observable resolution, it uses a pure
route-independent envelope over candidate command declarations:

- `-n N` selects the first *N* items from each candidate's effective item
  sequence: declared semantic rows when active, otherwise rendered lines;
- the required-value arity union protects a following negative token whenever
  any candidate route must consume it as that option's value;
- an invocation with no row-selection request follows ordinary routing;
- when candidate declarations assign different effective units, meanings,
  support, or required adjacent capabilities to a requested gesture or
  modifier, the invocation fails without routing and requires an explicit
  command;
- when every candidate uniformly lacks the requested gesture or required
  adjacent capability, the invocation fails with common capability rejection
  without routing; and
- when every candidate declares the same meaning and capability, the common
  grammar is active and its token, arity, value, repetition, and modifier
  failures are reported without routing.

When candidate routes disagree about whether an option consumes a following
token, the arity union marks that token indeterminate as well as protected.
Every envelope decision that depends on whether the token is an option value,
bare shorthand, or positional is deferred to the authoritative child.
Protection prevents premature reinterpretation; it does not manufacture a
route-independent gesture, non-uniform candidate rejection, absence, or
conflict. Only a determinate owned request activates those envelope decisions.

After routing, the authoritative child parse performs ordinary lowering. This
design does not claim that L2 order or schema resolution happens before target
acquisition: L2 owns that work and its failure timing. The router guarantee is
narrower and enforceable — for a determinate route-independent request, L3
never performs target acquisition merely to decide whether a CLI spelling is
malformed, ambiguous across routes, or unsupported by the common route
envelope. Arity-indeterminate decisions retain the deferral above.

Envelope activation is evaluated per owned row-selection request: a semantic
gesture or one of its direction/unit modifiers. A candidate that does not
declare the request makes the candidate set non-uniform; L3 does not infer
support from shared option objects or display behavior.

Bare `-N` is the one declaration-sensitive exception because it has no explicit
option identity until normalization. It is route-independent only when every
candidate binds `-n`; mixed binding defers the shorthand to the authoritative
child rather than manufacturing an explicit-command requirement. When no
candidate binds `-n`, bare `-N` is not a route-envelope request at all.
Explicit `-n N` already carries option identity, so mixed declaration remains
a non-uniform request and uniform non-declaration remains a common unsupported
gesture.

## L3 conflicts and failure

The active command validates CLI-decidable conflicts before command execution:

- nonpositive counts or coordinates;
- malformed or overflowing counts or coordinates;
- integer, start-plus-count, reversed, or boundless `--rows`;
- attached values on zero-arity modifiers;
- repeated `-n`, `--rows`, `--top`, or `--order-by`;
- both `--head` and `--tail`;
- a direction or line modifier without `-n`;
- `--tail-lines` with `--head`;
- a gesture the active command has not adopted.

`--top` additionally requires L2 to resolve its attached explicit order or the
schema's default Top ranking. The absence of a ranking is reported through the
CLI diagnostic boundary at L2-owned resolution timing, but L3 does not infer
ranking from a field name, display label, or row order.

Payload projections may impose additional combination rules. Those rules
belong to their focused owner and command adoption, not this grammar.

An adoption's format-compatibility rules apply before a line window is installed
or any handler emits output, including acquisition-free disclosure handlers.

Every L3 lowering failure:

- returns nonzero;
- identifies the incompatible or malformed gesture;
- occurs before the active command executes;
- emits no success-shaped empty result.

Any argv-derived text included in a lowering diagnostic remains untrusted
presentation data and passes through the existing CLI diagnostic-containment
discipline. The presentation boundary and risk are defined by the
[untrusted-data threat model](untrusted-data-threat-model.md#trust-boundaries);
this design changes diagnostic selection, not containment.

An explicit command reports these failures before command-owned acquisition.
An implicit route follows the narrower
[route-independent envelope](#implicit-routing). L2 resolution and adjacent
payload failures retain their owners and timing.

### Failure precedence

One explicit-command invocation can contain several bad tokens. L3 reports one
selection diagnostic using this order:

1. the first System.CommandLine token, option-arity, or unknown-option failure
   in argv order;
2. the first malformed, nonpositive, or overflowing row-selection value in
   gesture order;
3. the first repeated gesture or modifier conflict at the token that completes
   the conflict;
4. active-command capability rejection for the first unsupported requested
   gesture or modifier in argv; and
5. the one L2 resolution failure supplied in L2's owned resolution order,
   including an unresolved ranking order.

Token-completed conflicts use the position of the token that makes the
combination invalid. Absence conflicts, such as `--lines` without `-n`,
complete at end of argv and therefore follow every token-completed conflict in
the same category. When several absence conflicts complete together, the
modifier that appeared first in argv selects the diagnostic.

An implicit invocation has two ordered phases:

1. the route envelope applies required-value and `--` ownership, then reports
   the first common row-option token or arity failure, malformed value,
   repeated gesture or token-completed modifier conflict, end-of-argv absence
   conflict, or non-uniform or uniformly unsupported capability rejection,
   using that listed category order and the corresponding explicit-command
   tie-breaker within each category; and
2. after successful routing, the authoritative child applies the explicit
   command order above, including child-specific unknown options.

The envelope phase precedes child-specific categories because those categories
do not exist until the child is known.

Earlier categories prevent later lowering work. A diagnostic supplied by an
adjacent owner is rendered through the CLI boundary without replacing its
structured cause. Unrelated command validation remains outside this precedence;
an adoption gate must show that it cannot run before an earlier row-selection
failure and hide that failure.

## Command-by-command adoption

Item adoption is explicit on the active leaf command or on an explicit
zero-arity lens selector whose row set is determined at L3. A command or lens
does not gain a semantic row set because it happens to use a shared option
object, renders a table, or shares an execution helper with an adopted surface.
Unselected modes of a partially adopted command use the shared rendered-line
fallback.

One adoption PR defines:

- the command and subcommand boundary;
- the declared row set or sets supplied by L2;
- which semantic and line gestures are supported;
- any adjacent capability required for `Top`, payload projection, or source
  delegation;
- the same selected logical rows across every supported format; and
- outcome-level gates for the command's pathological and neighboring cases.

A command without an active semantic-row declaration lowers `-n` to the
host-owned rendered-line operation. A semantic adopter uses `--lines` or
`--tail-lines` to switch explicitly from semantic items to rendered lines.
The shared fallback is a typed declaration default, not an inference from
rendered output.

One invocation is governed entirely by the active command or selected lens
declaration and never changes meaning based on whether a later subsystem
happens to handle the result.

The package adoption consumes the online metadata-query evidence policy from
[Package Source Model](package-source-model.md#metadata-only-version-queries).
Semantic limits do not cap that discovery or relax its completeness rules.
Rows and Count follow query resolution: raw and observed pinned results retain
partial-source disclosure, while latest and range queries with insufficient
evidence fail before row selection or projection.

The adoption preserves feed document JSON's existing `version`, `feed`, and
Boolean `listed` fields. The table/JSONL `listing` text remains a separate
presentation, not a reason to change the typed document schema.

## Supported spellings and guidance

Only spellings in [Grammar](#grammar) are part of this contract. L3 does not
define behavior for any other row-selection spelling.

Each adoption removes other overlapping row-selection spellings from that
command. Its help, README examples, workflows, and shipped skills change in
the same PR. Help states that `-n` selects semantic rows when the active command
declares them and otherwise selects rendered lines; `--lines` explicitly
selects rendered lines; and an explicit `--order-by` belongs to `--top` when
both are present.

Guidance names only behavior available on its declared command. Shared guidance
does not anticipate adoption.

The plural package-version selectors use ordinary zero-arity parsing, without
recognizing former count spellings or providing migration diagnostics.
A following numeric token is ordinary positional package input when the
package slot is available, not a count. Surplus input directly following the
flag receives the host's common zero-arity error.

## Workspace top-level inventory adoption

Ordinary `workspace` inventory declares one semantic row per
`WorkspaceTopLevelInventoryEntry` in the owner-issued `Entries` order. This
includes direct construction, packet restoration, and `--root-request`
reopening. The inventory operation first completes Workspace construction,
restoration, acquisition, admission, and `--kind` filtering; Head/Tail and
strict Window stages then select from that completed typed vector before Count
or format lowering.

```console
$ dotnet-inspect workspace \
    --register-package-prefix Microsoft.Extensions. \
    --register-ecosystem aspire \
    -n 1 --tail --table
Kind       Location          State
Ecosystem  ecosystem.aspire  Registered
```

What to notice: `-n 1 --tail` selects the last complete inventory entry. It
does not clip the rendered table, skip Workspace construction, or reduce
Package acquisition. The same typed selection reaches complete JSON entry
objects. `--count` observes the selected entry vector, while the document's
inventory counts retain the operation's complete pre-row-selection evidence.

The adoption supports Head/Tail, Window, and explicit Lines. Complete JSON
rejects explicit line selection before Workspace work. One strict Window
failure withholds the complete inventory document:

```console
$ dotnet-inspect workspace \
    --register-package-prefix Microsoft.Extensions. \
    --register-ecosystem aspire \
    --rows 2..3 --json
Error: Workspace inventory row selection stage 1 requires entry 3, but only 2 entries are available.
```

Portable definition output selected by `--share` and Package Navigation
selected by `--active-package` or its descendant selectors remain outside this
semantic declaration. They use the rendered-line fallback rather than
reinterpreting their distinct result shapes as inventory entries.

## Integration graph adoption

`graph integrations` declares one semantic row per logical
`InspectionGraphEdge` in the owner-issued document order. The CLI's
`InspectionGraphEdgeRow` projection preserves each edge's typed `EdgeId`, so
Head/Tail and strict Window stages select identities rather than matching
display labels. Selection runs only after the complete explicit package set is
acquired, the induced-set request executes, relationship admission finishes,
and every graph failure is retained. Count and every graph or row renderer then
consume the selected edge vector.

```console
$ dotnet-inspect graph integrations \
    --package Microsoft.Extensions.DependencyInjection.Abstractions@10.0.0 \
    --package Microsoft.Extensions.Logging.Abstractions@10.0.0 \
    --package Microsoft.Extensions.Logging@10.0.0 \
    --package Microsoft.Extensions.Http@10.0.0 \
    --tfm net10.0 \
    --relationship integration.observed \
    -n 1 --tail --table
```

What to notice: `-n 1 --tail` selects the final admitted logical edge. It does
not clip rendered output, reduce package acquisition, change relationship
producer demand, or hide retained graph failures. JSON carries the selected
edge plus its required node and group context; JSONL, Markdown, table, TSV,
Mermaid, plaintext graph, and Count consume the same selected edge identities.

The adoption supports Head/Tail, Window, and explicit Lines. Complete JSON
rejects explicit line selection before package acquisition. One strict Window
failure withholds the complete graph document:

```console
$ dotnet-inspect graph integrations \
    --package Microsoft.Extensions.Logging@10.0.0 \
    --tfm net10.0 \
    --rows 2..3 --json
Error: Integration graph row selection stage 1 requires edge 3, but only 2 edges are available.
```

The default `graph libraries` view and exact `-S "Direct Use Clusters"`
selection declare one semantic row per complete
`AssemblyPairDirectUseCluster` in Query-issued deterministic order. Selection
runs after both local libraries resolve, bidirectional pair inspection
completes, and complete cluster derivation finishes. Count, Markdown,
plaintext, table, TSV, JSONL, and JSON then consume the same selected cluster
identities and retained call-site references.

```console
$ dotnet-inspect graph libraries \
    --library ./Consumer.dll \
    --library ./Provider.dll \
    -n 1 --tail --jsonl
```

What to notice: the final pair-wide cluster is selected before JSONL lowering.
Strict Window failure withholds the complete command output, while explicit
`--lines` continues to select rendered text.

The default `graph cluster N` call-site view and exact `-S "Call Sites"`
selection declare one semantic row per complete
`AssemblyPairCallUseOccurrence` in the owner-issued query order. Selection
runs after the positive ordinal scopes the pair to one observed Direct-Use
Cluster. Count and every row renderer consume the same selected physical
call-site identities. Retained incomplete-evidence diagnostics remain visible
and preserve their nonzero exit.

```console
$ dotnet-inspect graph cluster 3 \
    --library ./Consumer.dll \
    --library ./Provider.dll \
    -n 1 --tail --jsonl
```

Exact `-S "Consumer Use Sites"` and `-S "Provider API Types"` each declare one
semantic row per owner-issued summary group. Consumer rows are attributed
source methods plus the directed target participant and MVIDs. Provider rows
are structured target declaring types plus the directed source participant and
MVIDs. Each vector inherits deterministic first-occurrence order from the
complete pair result. On `graph cluster N`, selection runs after focused
cluster scoping and summary grouping, so a selected row keeps its complete
group counts and occurrence-index receipts. Its rendered `Call Site Rows`
continue to reference the unchanged call-site vector for the effective pair
scope. Count and every row renderer consume the same selected summary
identities. Incomplete pair evidence remains visible and nonzero after
positive selected output.

Exact `-S "Direct Use Clusters"` selection, including repeated identical exact
selectors after case-insensitive deduplication, declares a separate semantic row
per complete `AssemblyPairDirectUseCluster` in the Query-issued deterministic
cluster order.
On `graph libraries`, selection runs after pair inspection and complete
cluster derivation. On `graph cluster N`, the same declaration contains the
one focused cluster. The selected cluster identities feed Count, Markdown,
plaintext, table, TSV, JSONL, and JSON without changing their retained
call-site references. Repeated identical exact selectors resolve to the same
declaration after case-insensitive deduplication. Incomplete pair evidence
remains visible and nonzero after selected cluster output.

Graph Libraries lowers these four exact semantic lenses through the
Query-owned Graph Libraries QuerySpace and the Sections row executor. One
section declaration supplies the stable row-set identity, typed QuerySpace
scope, Sections schema, producer demand, and result binding. The same
association reaches the Rows terminal for rendered output or the Count terminal
for cardinality, so adding a section cannot silently fall through to Call
Sites.

Bare `-S`, Public Root Paths, wildcard or category selection, and every
multi-section view remain outside these declarations because they expose
independent row sets with different schemas. They retain the legacy `--rows`
contract and use rendered-line fallback for bare `-n`.

## Type catalog adoption

An unambiguous explicit-source `type` catalog declares one semantic row per
`ApiType`. This includes a catalog with no positional Type target, a positional
Type glob, or a distinct `-t`/`--type` filter. Package, library, platform, or
project resolution and complete API extraction finish first. Type glob, kind,
and unsafe filtering then establish the ordered catalog vector before
Head/Tail or strict Window stages select from it.

```console
$ dotnet-inspect type --platform System.Text.Json \
    -n 1 --tail --json
{
  ...
  "public_type_count": 1,
  "types": [
    {
      "name": "Utf8JsonWriter",
      ...
    }
  ]
}
```

What to notice: `-n 1 --tail` selects the final complete Type catalog entry.
Markdown, table, TSV, JSONL, and complete JSON consume the same selected type
identity, and complete JSON recomputes its public type and member counts.
Assembly-level companion evidence such as Type forwarders remains visible; it
is not another selectable Type row sequence. Selection does not reduce source
resolution or API extraction.

The adoption supports Head/Tail, Window, and explicit Lines. Complete JSON
rejects explicit line selection before source resolution. One strict Window
failure withholds every output shape:

```console
$ dotnet-inspect type --platform System.Text.Json \
    --rows 92..93 --json
Error: Type row selection stage 1 requires row 93, but only 91 rows are available.
```

Exact-type inspection, explicit section selection, structural or effective
discovery, query help, shape, `--tfm all`, performance and row-query filters,
pairwise `--match`, and commandless requests without an explicit source remain
outside this declaration. Those surfaces use rendered-line fallback for bare
`-n`. Numeric `-t` is an ordinary Type filter literal, not a hidden count;
numeric `--rows N` is rejected on the adopted catalog because Window requires
range syntax.

## Package Files adoption

Ordinary single-package `package` inspection declares one semantic row per
`PackageFile` when the effective section selection is exactly `Package files`.
The `Files` alias and `--path` sugar reach the same declaration. Package
resolution, extraction, complete ordered file enumeration, and optional path
filtering finish before Head/Tail or strict Window stages select from the typed
row vector.

```console
$ dotnet-inspect package Markout@0.35.2 \
    --path "skills/*/SKILL.md" -n 1 --tail --paths
skills/markout/SKILL.md
```

What to notice: `-n 1 --tail` selects the final complete path/size row.
Markdown, table, TSV, JSONL, complete JSON, Count, and path projection consume
that same selected model, including field-value projection. Selection does not
reduce package acquisition, archive extraction, or file enumeration.

The adoption supports Head/Tail, Window, and explicit Lines. Complete JSON
rejects explicit line selection before package resolution. One strict Window
failure withholds every output shape:

```console
$ dotnet-inspect package Markout@0.35.2 \
    --path "skills/*/SKILL.md" --rows 5..6 --json
Error: Package file row selection stage 1 requires row 6, but only 5 rows are available.
```

The document-family sections (`Package nuspec file`, `Package README file`, and
`Package skill files`), `@Files`, mixed sections, effective or static discovery,
content output, embedded `--library`/`--all-libraries` inspection, range or
version listing, and multiple-package inspection remain outside this
declaration. Those surfaces retain their existing row contracts and use
rendered-line fallback for bare `-n`. Direct callers that provide only the
legacy `RowWindow` also retain their existing behavior.

## Package layout adoption

The ordinary single-package `package --layout` lens declares one semantic row
per normalized package-relative file path in its scoped layout. Package
resolution, extraction, scoped recursive enumeration, packaging-plumbing
exclusion, and path sorting finish before Head/Tail or strict Window stages
select from the completed vector.

The scope remains layout-specific. `--lib` and `--tools` scope to those package
roots. `--tfm <TFM>` scopes to `lib/<TFM>` when present and otherwise
`tools/<TFM>`, rendering paths relative to the TFM directory's parent so the
framework remains the tree root. It does not adopt the cross-root Package-file
TFM predicate.

Markdown renders a tree derived only from the selected file identities. JSON
emits a document array of `{ "path": ... }` rows, JSONL emits one such row per
line, and Count observes the same selected vector. The adoption supports
Head/Tail, Window, and explicit Lines. Explicit `--lines` clips the rendered
tree and does not select file identities; JSON rejects line selection before
package resolution.

One strict Window failure withholds every output shape:

```console
$ dotnet-inspect package Newtonsoft.Json@13.0.4 \
    --layout --rows 20..21 --json
Error: Package layout file row selection stage 1 requires row 21, but only 20 layout file rows are available.
```

Dependencies, TFM and version listings, file and content sections, embedded
`--library`/`--all-libraries` inspection, multiple-package inspection,
discovery/schema, envelope output, and unsupported print or shape projections
remain outside this declaration. Unselected Package modes continue to use the
rendered-line fallback for bare `-n`.

## Package TFM adoption

The ordinary single-package `package --tfms` lens declares one semantic row
per target-framework string. Package resolution, extraction, complete assembly
enumeration, TFM de-duplication, and TFM-priority ordering finish before
Head/Tail or strict Window stages select from that completed vector.

```console
$ dotnet-inspect package Newtonsoft.Json@13.0.4 \
    --tfms -n 1 --tail --json
[
  {
    "tfm": "net20"
  }
]
```

What to notice: `-n 1 --tail` selects the final complete TFM row. Markdown,
table, TSV, JSONL, JSON, and Count consume the same selected TFM identity.
Selection does not reduce package acquisition, archive extraction, or assembly
enumeration.

The adoption supports Head/Tail, Window, and explicit Lines. JSON rejects
explicit line selection before package resolution. One strict Window failure
withholds every output shape:

```console
$ dotnet-inspect package Newtonsoft.Json@13.0.4 \
    --tfms --rows 8..9 --json
Error: Package TFM row selection stage 1 requires row 9, but only 8 TFM rows are available.
```

The declaration activates only for the ordinary one-package TFM lens.
Version listing and its population-only modifiers, valid or malformed
package-range coordinates, dependencies, bare `--tree`, layout, file, content,
and SourceLink selectors or modifiers, embedded
`--library`/`--all-libraries` inspection, multiple-package inspection, explicit
section selection, discovery/schema, envelope output, and unsupported print or
shape projections remain outside it. Fields and columns remain inside only
with Count, where they project the TFM lens's count result. These competing
intents retain their existing diagnostics or legacy row contracts and use
rendered-line fallback for bare `-n`. Numeric `--rows N` is rejected on the
ordinary adopted lens because Window requires range syntax.

## Match candidate adoption

`match --similar` declares one semantic row per
`StructuralCloneRetrievalCandidate` in the Analysis-issued structural-
similarity ranking. Seed and population resolution, candidate-method scanning,
feature extraction, ranking, and `--max-results` truncation finish before
Head/Tail, strict Window, or Top stages select from the completed returned
candidate vector. Top uses that intrinsic ranking as its default ranking; it
does not introduce another score or retrieval limit.

```console
$ dotnet-inspect match Sample.Encode --similar \
    --library ./app.dll --top 2 --json
{
  ...
  "row_selection": {
    "available_candidates": 14,
    "selected_candidates": 2
  },
  "candidates": [
    ...
  ],
  "method_outcomes": [
    ...
  ]
}
```

What to notice: Markdown, table, TSV, JSONL, JSON, and Count consume the same
selected candidate identities. JSON selection changes only `candidates`;
`method_outcomes`, blockers, and the query-issued receipt remain complete
retrieval evidence. `row_selection` makes that boundary explicit, so
`receipt.returned_candidates` may exceed the selected candidate count without
claiming that retrieval returned less evidence than it did. Count observes the
selected candidate vector.

The adoption supports Head/Tail, strict Window, Top, and explicit Lines. JSON
rejects rendered-line selection before source resolution. One unavailable
Window after completed retrieval withholds every output shape:

```console
$ dotnet-inspect match Sample.Encode --similar \
    --library ./app.dll --rows 999..999 --json
Error: Match candidate row selection stage 1 requires row 999, but only 14 ranked candidates are available.
```

`--max-results` and `--max-methods` remain product retrieval limits and execute
before semantic candidate selection. Pairwise `match` has no ranked candidate
row vector and remains outside this declaration; bare `-n` there uses the
rendered-line fallback. The seed outcome, per-method outcomes, blockers,
receipt, and disclosure are companion evidence, not additional selectable row
sets. A rejected, unsupported, limit-reached, or failed retrieval remains
visible and nonzero rather than being replaced by a row-selection failure.

## Library References adoption

An exact `library -S References` request declares one semantic row per complete
direct `AssemblyReference`. The legacy `--references` alias reaches the same
declaration. Local-file, package-backed, and platform-library resolution,
metadata acquisition, and the complete assembly-reference Finding census
finish before the references are ordered by ordinal assembly name and
Head/Tail or strict Window stages select from that vector.

```console
$ dotnet-inspect library System.Text.Json \
    -S References -n 1 --tail --json
{
  ...
  "assembly_info": {
    ...
    "references": [
      {
        "name": "System.Threading",
        ...
      }
    ]
  }
}
```

What to notice: Markdown, table, TSV, JSONL, complete JSON, and Count consume
the same selected direct-reference identities. Complete JSON selects
`assembly_info.references`; the complete Finding census and typed reference
identities remain acquisition evidence, so selection does not claim that
metadata inspection observed fewer references. A failed reference inspection
remains visible and nonzero rather than becoming an empty selected vector.

The adoption supports Head/Tail, strict Window, and explicit Lines. Complete
JSON rejects rendered-line selection before library resolution. One
unavailable strict Window withholds every output shape:

```console
$ dotnet-inspect library System.Text.Json \
    -S References --rows 999..1000 --json
Error: Library reference row selection stage 1 requires row 1000, but only 6 direct reference rows are available.
```

`Reference Hierarchy`, mixed sections, discovery/schema, `--tfm all`,
print and shape projections, and embedded or aggregate Package Library modes
remain outside this declaration. Those surfaces retain their existing row
contracts and use rendered-line fallback for bare `-n`.

## Clone Candidates adoption

An exact `Clone Candidates` section on `library`, including the delegated
`package --library` route, `type`, or `member` declares one semantic row per
Query-issued `CloneCandidateRow`. Workspace realization, seed expansion,
candidate admission, retrieval, suppression, and global ranking finish before
Head/Tail or strict Window stages select from that ranked vector. The same
declaration applies when a `Breadth` or `Discovery` predicate infers the section.

```console
$ dotnet-inspect type Cases.Widget --library ./app.dll \
    -S "Clone Candidates" -n 1 --tail --json
{
  ...
  "rows": [
    {
      "rank": 14,
      ...
    }
  ],
  "seeds": [
    ...
  ],
  "libraries": [
    ...
  ],
  "receipt": {
    "returned_pairs": 14,
    ...
  }
}
```

What to notice: Markdown, table, TSV, JSONL, projected JSON, complete JSON, and
Count consume the same selected candidate identities. Complete JSON selection
changes only `rows`; seed coverage, participant coverage, limits, and the
query-issued receipt remain complete companion evidence. Consequently
`receipt.returned_pairs` may exceed `rows.length` without claiming that the
Query returned less evidence.

The adoption supports Head/Tail, strict Window, and explicit Lines. Since the
Query has already placed rows in global rank order, `-n N` selects the highest
ranked *N* candidates. The shared `--top` option remains Performance Triage
syntax and is not reinterpreted for Clone Candidates. Complete JSON rejects
rendered-line selection before source resolution. An unavailable strict Window
withholds every output shape:

```console
$ dotnet-inspect type Cases.Widget --library ./app.dll \
    -S "Clone Candidates" --rows 999..1000 --json
Error: Clone Candidates row selection stage 1 requires row 1000, but only 14 ranked candidates are available.
```

Other Library, Type, and Member sections remain outside this declaration.
Type-catalog and projected Member Facts adoptions retain their existing
activation rules; all other neighboring surfaces use their existing row
contracts or rendered-line fallback.

## Member Calls adoption

An exact `member -S Calls` request declares one semantic row per direct
call-site occurrence in the completed `CallSiteRow` vector. The request retains
Member's existing requirement for one selected target overload. Direct-call
analysis completes for that method and its generated evidence methods before
selection. Repeated calls to the same target remain distinct occurrences, and
the completed vector retains its existing IL-offset order.

```console
$ dotnet-inspect member JsonSerializer --package System.Text.Json \
    Serialize:1 -S Calls -n 1 --tail --json
{
  "calls": [
    {
      "il_offset": "IL_000A",
      "opcode": "call",
      "call_kind": "direct",
      "callee": "System.Text.Json.JsonSerializer.WriteString<TValue>(ref TValue, System.Text.Json.Serialization.Metadata.JsonTypeInfo<TValue>)",
      "operand_token": "0x2B00005E",
      "return_address": "IL_000F"
    }
  ]
}
```

What to notice: Markdown, table, TSV, JSONL, structured JSON, and Count consume
the same selected call-site occurrences. Exact Calls JSON lowers the section
row model rather than returning the surrounding Member document. Evidence
Method remains companion evidence for rows contributed by generated bodies;
semantic selection does not reduce analysis work.

The adoption supports Head/Tail, strict Window, and explicit Lines. Structured
JSON rejects rendered-line selection before source resolution. One unavailable
strict Window withholds every output shape:

```console
$ dotnet-inspect member Widget --library ./app.dll \
    -m Run -S Calls --rows 4..4 --json
Error: Member Calls row selection stage 1 requires call row 4, but only 3 call rows are available.
```

`Callers`, `Call Graph`, `@Calls`, mixed section selections, discovery, query
help, and Calls included only by verbosity remain outside this declaration.
They retain their current row contracts or rendered-line fallback.

## Member Callers adoption

An exact `member -S Callers` request declares one semantic row per deduplicated
caller-site occurrence. The request retains Member's existing requirement for
one selected target overload. The caller scan completes across that overload
and every explicitly authorized caller scope before selection. Occurrences are
deduplicated by source assembly, evidence method identity, IL offset, and
operand token, then retain the existing deterministic order by Source, Caller,
Evidence Method, and IL Offset.

```console
$ dotnet-inspect member System.ThrowHelper \
    --platform System.Private.CoreLib --all \
    -m ThrowArgumentNullException:1 -S Callers \
    -n 1 --tail --json
{
  "callers": [
    {
      "caller": "ushort.Parse(string, System.Globalization.NumberStyles, System.IFormatProvider)",
      "il_offset": "IL_0005",
      ...
    }
  ]
}
```

What to notice: Markdown, table, TSV, JSONL, structured JSON, and Count consume
the same selected caller-site identities. Exact Callers JSON lowers the section
row model rather than returning the surrounding Member document. Caller-scan
diagnostics and the optional Source and Evidence Method fields remain companion
evidence on the selected rows; semantic selection does not reduce the caller
scope or analysis work. When the completed caller vector contains rows from
multiple source assemblies, Source remains visible even if selection narrows
the result to rows from one assembly.

The adoption supports Head/Tail, strict Window, and explicit Lines. Structured
JSON rejects rendered-line selection before source resolution. One unavailable
strict Window withholds every output shape:

```console
$ dotnet-inspect member Widget --library ./app.dll \
    -m Run -S Callers --rows 4..4 --json
Error: Member Callers row selection stage 1 requires caller row 4, but only 3 caller rows are available.
```

`Calls`, `Call Graph`, `@Calls`, mixed section selections, discovery, and
scope-implied Callers without an exact selector remain outside this
declaration. They retain their current row contracts or rendered-line fallback.
`--bin`, `--project`, and `--caller-package` compose with the declaration when
the explicit section selection remains exactly `Callers`.

## Package SourceLink file adoption

Ordinary single-package `package` inspection declares one semantic row per
`PackageSourceFileInfo` when the effective section selection is exactly
`SourceLink: Files`. The legacy `Source Files` alias and `-t`/`--type` sugar
reach the same declaration. Package resolution, extraction, compatible-library
selection, PDB acquisition, SourceLink collection, and type filtering all
complete before Head/Tail or strict Window stages select from the typed row
vector.

```console
$ dotnet-inspect package Newtonsoft.Json@13.0.3 \
    -S "SourceLink: Files" -t JsonReader \
    -n 1 --tail --urls
https://raw.githubusercontent.com/JamesNK/Newtonsoft.Json/.../JsonReader.Async.cs
```

What to notice: `-n 1 --tail` selects the final complete
library/type/URL row. Markdown, table, TSV, JSONL, complete JSON, Count,
`--value`/`--urls`, and `--raw` consume that same selected model. Selection
does not reduce Package or PDB acquisition, compatible-library enumeration, or
SourceLink collection.

The adoption supports Head/Tail, Window, and explicit Lines. Complete JSON
rejects explicit line selection before package resolution. One strict Window
failure withholds every output shape:

```console
$ dotnet-inspect package Newtonsoft.Json@13.0.3 \
    -S "SourceLink: Files" -t JsonReader \
    --rows 2..3 --json
Error: Package SourceLink file row selection stage 1 requires row 3, but only 2 rows are available.
```

`@SourceLink`, multi-section selections, effective or static discovery,
embedded `--library`/`--all-libraries` inspection, `--path`, `--dependencies`,
and multiple-package inspection remain outside this declaration. Those
surfaces retain their existing row contracts and use rendered-line fallback
for bare `-n`. Direct callers that provide only the legacy `RowWindow` also
retain their existing behavior.

## Project document row adoption

The `project` command declares one semantic row per `ProjectDocumentRow` when
the effective selection is exactly one of `Skills` or `Package README file`.
This includes bare `-S`, whose focused default resolves to `Skills`. Project
assets discovery, direct-package enumeration, document inventory construction,
and row validation complete before Head/Tail or strict Window stages select
from the ordered typed vector.

```console
$ dotnet-inspect project ./src/DotnetInspect.Cli \
    -S Skills -n 1 --tail --jsonl
{"package":"Markout","version":"0.37.0","path":"skills/markout/SKILL.md",...}
```

What to notice: `-n 1 --tail` selects the final complete package-document row.
Markdown, table, TSV, JSONL, complete JSON, Count, value/path projection, and
print/bare output consume that same selected vector. Printable document content
is read only after row selection, while an invalid later Skill row still fails
inventory construction instead of disappearing behind a selected prefix.
Projection and print row numbers address the selected sequence and therefore
start again at one.

The adoption supports Head/Tail, Window, and explicit Lines. Complete JSON
rejects explicit line selection before project resolution. One strict Window
failure withholds every output shape:

```console
$ dotnet-inspect project ./src/DotnetInspect.Cli \
    -S Skills --rows 2..3 --json
Error: Project document row selection stage 1 requires row 3, but only 2 rows are available.
```

Multi-section `-S @Project`, effective or structural discovery, and direct
callers that provide only the legacy `RowWindow` remain outside this semantic
declaration. They retain their existing row contracts and use rendered-line
fallback for bare `-n`.

## Demo-list adoption

The finite `demo list` catalog and equivalent bare `demo` listing declare one
semantic row per `EcosystemDemoDescriptor` in existing product order. They
adopt Head/Tail, Window, and Lines; scenario execution adopts only explicit
rendered-line selection.

`--count` is the terminal reduction of the selected descriptor vector. It
emits one scalar after semantic Head/Tail and Window stages; explicit rendered-
line selection remains a presentation operation over that scalar. Scenario
execution does not support Count.

```console
$ dotnet-inspect demo list -n 1 --json
[
  {
    "id": "stj-serializer",
    "title": "System.Text.Json",
    "summary": "Browse a real package API"
  }
]
```

What to notice: `-n 1` selects one declared demo row before JSON encoding. It
does not clip the JSON text.

Neighboring ordered case:

```console
$ dotnet-inspect demo list -n 2 --rows 2..3 --json
Error: Demo row selection stage 2 requires row 3, but only 2 demo rows are available.
```

## Vocabulary adoption

`vocabulary` declares one row per stable product-owned value in each selected
vocabulary section. Every participating section is a separate named sequence
in owner catalog order. Head/Tail and Window stages apply independently to all
of them before count or format lowering; one strict Window failure withholds
every selected section.

```console
$ dotnet-inspect vocabulary -S Accessibility -n 2 --tail --columns ID --tsv
id
internal
private
```

The command exposes explicit rendered-line selection but does not expose Top
or `--order-by`. Complete JSON rejects line selection before command work.
Predicate and ranking adoption waits for the shared row-query owner rather than
adding a vocabulary-local implementation. Structural `-D` output remains
outside this adoption and keeps the existing discovery projection behavior.

## Diff History adoption

`diff --history` declares five independent row sets. Outcome is the terminal
knowledge row, Probe Trace follows chronological evaluation order, Evaluations
and Changed Versions follow version-vector order, and Transitions follow
ordered evaluated endpoint pairs with producer-native Finding identity.
Head/Tail and Window stages apply independently to every selected rendered row
set after the command completes its policy-authorized package-cell evaluation.
One strict Window failure withholds the complete projected document.

```console
$ dotnet-inspect diff --history \
    --package Markout@0.33.0..0.35.2 \
    --type Markout.MarkoutWriterOptions \
    --finding api.member --at all \
    -S Transitions -n 10 --tail
```

History policy remains traversal authorization: no policy option selects full
population evaluation, repeated `--at` selectors authorize sparse checkpoints,
`--at all` explicitly spells full traversal, and `--max-probes N` bounds
adaptive evaluation. Semantic row selection never reduces those acquired
cells. Markdown, table, TSV, JSONL, and projected JSON consume the same selected
rows. Count admits only Changed Versions and is bound by the shared service
before rendering.

The command exposes explicit rendered-line selection but does not expose Top,
`--order-by`, or predicates. Complete JSON rejects line selection before
package acquisition. The remaining capabilities require their owning
row-query adoption rather than a History-local implementation.

## Required gates

All gates run in Release. New gates are **unverified** until implemented.
`UntrustedArgumentDiagnosticContainmentTests` already exists and remains the
enforcing gate for argv-derived diagnostic containment; each implemented
spelling adds its new diagnostic channels to that gate.

The implemented explicit-command occurrence lowerer is enforced by:

| Gate | Property |
| --- | --- |
| `CliRowSelectionExplicitValueTests` | Positive ASCII-decimal Count and Top values plus closed, prefix, and suffix Window values lower to typed intent; empty, signed, non-ASCII, zero, overflowing, integer, start-plus-count, boundless, repeated-operator, reversed, and internally spaced forms return their structured value failure. |
| `CliRowSelectionExplicitOrderTests` | Count, Window, and Top preserve argv order; one order operand attaches only to Top when Top is present and otherwise remains typed baseline-order input. |
| `CliRowSelectionExplicitModifierTests` | Direction modifiers lower the limit count to Head or Tail intent, line modifiers remove that count while preserving neighboring semantic operations, exact modifier repeats and equivalent tail/line redundancy are tolerated, conflicting directions reject at the completing token, and absence conflicts name the first modifier. |
| `CliRowSelectionExplicitCapabilityTests` | An explicit request succeeds with exactly its lowered semantic, order, and line capabilities; line-unit counts do not require the Head/Tail capability, missing capabilities reject in argv order, and an empty request requires none. |
| `CliRowSelectionExplicitFailurePrecedenceTests` | Value failure precedes repetition/conflict, repetition/conflict precedes capability rejection, each category uses the specified position rule, and structured failure publishes no success value. |

The implemented explicit-command argv adapter is enforced by:

| Gate | Property |
| --- | --- |
| `CliRowSelectionExplicitTokenOwnershipTests` | Required separated, attached, compact, and parent-bound values remain owned; row-option-shaped required values do not create phantom occurrences; unrelated option prefixes do not steal row occurrences; optional and zero-arity options leave a separate bare shorthand available; response-file-shaped and `--`-following text remain literal positional input. |
| `CliRowSelectionExplicitBareShorthandTests` | Positive, zero, and overflowing ASCII-decimal shorthand normalize to the common `-n` path with original positions only where the earliest active command declaring `-n` owns option syntax; ancestor positional text and non-ASCII text are not shorthand; repeats remain distinct occurrences. |
| `CliRowSelectionExplicitOccurrencePositionTests` | Separated, `=`/`:` attached, and compact values extract typed occurrences at their raw option positions, including one opaque order operand, then preserve semantic order through lowering. |
| `CliRowSelectionExplicitParseFailureTests` | Authoritative System.CommandLine failures, including unsupported POSIX bundles, suppress lowering; missing row values and attached modifier values produce structured row-arity failures; following separate tokens remain independently parsed; repeatable one-value-per-token option identities preserve complete repeats for the lowerer's repeated-gesture failure. |
| `CliRowSelectionExplicitAdapterCompositionTests` | Raw Window plus bare count and line-unit input composes through the adapter into surviving semantic Window and rendered-line intent with exactly the lowered capabilities. |
| `CliRowSelectionExplicitScalarOptionBindingsLower` | An adopting command can bind existing scalar System.CommandLine options while preserving compact-limit normalization and ordered Window/Head intent. |
| `CliRowSelectionExplicitPresenceArityPreservesLegacyBinding` | Adopted modifiers leave Boolean-shaped input positional while ordinary parsing retains the shared legacy binding's value behavior before and after adoption. |
| `CliRowSelectionRawOwnershipPreservesCompactOptionExpansion` | The shared ownership map preserves later option identities and required values when a preliminary parse expands a compact scalar option into separate option/value tokens. |

The plural package-version adoption is enforced by:

| Gate | Property |
| --- | --- |
| `Versions_WithLimit_RespectsLimit`, `Versions_BareShorthandAndTailSelectRows`, and `Versions_ModifierBeforeBareShorthandSelectsRows` | Explicit `-n`, implicit-route `-N`, and either modifier order for Head/Tail select complete version rows. |
| `Versions_WithLimit_ProducesCompleteJsonRows` and `VersionsWithFeed_WithLimit_ProducesCompleteJsonRows` | JSON contains the selected complete row objects for merged and feed-attributed listings. |
| `VersionFeed_JsonPreservesBooleanListedProperty` | Feed document JSON retains its established fields and Boolean listing values for both listed and unlisted rows. |
| `VersionsWithFeed_LinesMakesRenderedClippingExplicit`, `Versions_LinesRejectsDocumentJsonBeforeAcquisition`, and `Versions_LinesRejectsEnvironmentDocumentJsonBeforeAcquisition` | Line intent opts into rendered-line selection where the format remains valid and rejects explicit or environment-selected document JSON before acquisition, including when Boolean-shaped input follows a line modifier. |
| `Versions_QueryDiscoveryPreservesJsonFormatContract` and `Versions_QueryDiscoveryReportsConflictingFormats` | Query discovery cannot bypass either plural lens's line/JSON rejection, including environment-selected JSON; complete discovery JSON and explicit non-JSON overrides retain their behavior, and conflicting renderer flags still report a visible error. |
| `Versions_ZeroArityFlagsPreserveFollowingPackageInput` | New plural selectors and line modifiers are zero-arity: following Boolean-shaped package input remains positional rather than disabling the flag. |
| `Versions_ModifierRequiresCountReportsUsableRemedy` | A range does not satisfy a modifier's missing count; its diagnostic requests `-n`, and adding that count succeeds. |
| `Versions_ValuedDirectionWithRangeUsesZeroArityDiagnostic` | Surplus input after an adopted direction uses the common zero-arity diagnostic for both explicit and implicit plural lenses; the corrected `-n` command succeeds. |
| `PackageVersionListing_DirectionPreservesBooleanPackageInput`, `PreprocessArgs_RequiredSelectorValuePreservesLegacyDirection`, and `PreprocessArgs_ExplicitSearchRetainsBooleanDirectionValue` | Explicit and options-first implicit plural lenses preserve Boolean-shaped package names after either direction modifier and select the correct row; required selector-shaped values and explicit search retain legacy direction binding. |
| `PreprocessArgs_ZeroArityVersionFlagsPreserveBooleanTarget` and `PreprocessArgs_AdoptedDirectionRetainsOtherBooleanOptionValues` | Implicit routing leaves Boolean-shaped package input after presence-only flags while preserving separated Boolean values on ordinary options. |
| `Versions_SelectorLeavesNumericInputAsPackageArgument` and `Versions_AdditionalPackageUsesMultiPackageValidation` | Both zero-arity selectors preserve a numeric package ID in the available slot; surplus input directly after the selector uses the common zero-arity diagnostic, not a former-count recognizer. |
| `Versions_ConflictingSelectorsRejectBeforeAcquisition` and `Versions_ValuedSingularSelectorConflictsBeforeAcquisition` | A selected plural package-version lens conflicts visibly with another plural or any bare, separated-valued, or attached-valued singular version selector before acquisition. |
| `ExplicitCoordinateSemanticSingleVersion_PreservesRequestedRow` | Semantic single-row selection preserves an explicitly pinned package coordinate or `@latest` request without restoring plural source-side limits. |
| `FeedCoordinateSemanticSingleVersion_PreservesFeedRowIdentity` | Pinned, `@latest`, and range coordinates keep the feed-attributed row identity through semantic single-row selection. |
| `LatestVersionListing_RefreshesOnlineEvidence` and `FeedLatest_RefreshFailureDoesNotFallBackToCachedRows` | Ordinary and `@latest` online listings acquire fresh source evidence, preserve feed row identity, and report refresh failures rather than returning earlier rows. |
| `PackageVersionListing_LimitOneStillReportsPartialEvidence`, `PackageVersionListing_IncludeUnlistedLimitOneStillReportsPartialEvidence`, and `PackageVersionFeedListing_LimitOneStillReportsPartialEvidence` | Semantic Head(1) does not suppress multi-source failure evidence in merged, listing-aware, or feed-attributed listings, including count projection. |
| `CoordinateVersionListing_PreservesPartialEvidence` and `SingularCoordinateVersionListing_PreservesSourceFailureDisclosure` | Adopted coordinates preserve source-failure evidence before selection and count projection, with or without listing status: observed pins disclose partial results, latest/range queries fail without rows, and singular pins retain the source owner's disclosure. |
| `PackageVersionListing_LocalFolderReadsVersionsWithoutHttpTransport` | Local-directory and file-URI sources enumerate versions without HTTP/plugin authentication under semantic Head(1). |

The implemented implicit-route envelope is enforced by:

| Gate | Property |
| --- | --- |
| `CliRowSelectionRouterPreflightTests` | Request-free invocations preserve ordinary routing; common row grammar failures and uniformly unsupported requests survive unrelated route deferral; mixed declaration or capability requires an explicit command; required-value disagreement defers dependent decisions; bare `-N` is common only when every candidate binds `-n`; original raw-argv positions are preserved. |
| `CliRowSelectionRouterIntegrationTests` | The production commandless router constructs candidates from real commands and selected lenses; common malformed and uniformly unsupported requests stop before router rewrite and acquisition; mixed real declarations require an explicit command; required-value disagreement defers; and `NoRequest` and `Success` continue to authoritative routing. |

The demo-list adoption is enforced by:

| Gate | Property |
| --- | --- |
| `DemoCommandTests` | Explicit `demo list` and equivalent bare `demo` apply semantic Head/Tail and ordered Window stages to complete catalog descriptors before JSON, Markout, or Count projection; every format observes the same selected demo identities; Count emits the selected descriptor cardinality; strict Window failure emits no partial payload; JSON rejects rendered-line clipping; scenario execution accepts only explicit rendered-line selection. |

The vocabulary adoption is enforced by:

| Gate | Property |
| --- | --- |
| `VocabularyCommandTests` | Explicit `vocabulary` value rendering applies semantic Head/Tail and ordered Window stages to stable catalog rows before count or format lowering; bare `-N` and explicit `-n` select the same identities, multiple selected sections remain independent named sequences, and one strict Window failure emits no partial document. Explicit Lines clips rendered TSV, while complete JSON rejects line selection. Structural discovery retains its existing projection path. |

The timeline adoption is enforced by:

| Gate | Property |
| --- | --- |
| `TimelineCommandTests` | Evaluations and Transitions apply semantic Head/Tail and ordered Window stages independently before Markdown, table, TSV, JSONL, typed JSON, or Count lowering; one strict Window failure emits no partial document. |
| `ConfiguredPayloadAcquisitionTests.TimelineRange_SemanticRowsComposeWithoutReducingExplicitAcquisition` | Bare `-N`, Tail, and Window compose over final Evaluation rows while explicit dense `--at all` still acquires every selected package cell. |
| `ConfiguredPayloadAcquisitionTests.TimelineRange_ExplicitLinesClipRenderedOutput` and `TimelineRange_LinesRejectDocumentJsonBeforeAcquisition` | Explicit Lines clips rendered TSV without reducing authorized package-cell acquisition, while complete JSON rejects line selection before package acquisition. |

The Workspace top-level inventory adoption is enforced by:

| Gate | Property |
| --- | --- |
| `WorkspaceCommandTests` | Direct, packet, and Root-reopening top-level inventory modes declare complete typed entries after kind filtering; explicit and bare Head, Tail, and closed, prefix, or suffix strict Window select the same owner-ordered identities before JSON, JSONL, Markout, or Count lowering. One unavailable Window emits no partial document. |
| `WorkspaceCommandTests.SemanticHead_DoesNotHideFailedWorkspaceConstruction` | Semantic selection does not reduce Package acquisition or hide a failed Workspace construction behind a selected successful prefix. |
| `WorkspaceCommandTests.CommandLineInventory_LinesRejectCompleteJsonBeforeWorkspaceWork`, `CommandLineNavigation_InferredLinesRejectCompleteJsonBeforeWorkspaceWork`, and `CommandLineShare_SpellingsRemainRenderedLineFallback` | Explicit Lines rejects complete inventory JSON before Workspace work, while active-package Navigation and both bare and explicit-URL Share remain outside the inventory declaration and infer rendered-line selection. |

The Integration graph adoption is enforced by:

| Gate | Property |
| --- | --- |
| `InspectionGraphCommandTests.OutputModes_UseTheSameWindowedLogicalEdges` and `SemanticTail_SelectsTheSameLogicalEdgeAcrossFormats` | Legacy direct callers retain row-window behavior, while semantic Tail selects one edge identity before Markdown, table, JSON, JSONL, or Count lowering. |
| `InspectionGraphCommandTests.SemanticUnavailableWindow_WithholdsGraph` and `VisibleGraphFailure_PreservesOutputAndNonzeroExit` | One strict unavailable Window emits no partial graph; successful semantic selection preserves retained graph failures and their nonzero exit. |
| `InspectionGraphCommandTests.IntegrationsCommand_AcceptsSemanticOpenWindows`, `IntegrationsCommand_RejectsLegacyCountRows`, `IntegrationsCommand_HeadAllowsCompleteJsonBeforeRequiredInputs`, and `IntegrationsCommand_LinesRejectJsonBeforeRequiredInputs` | Integration graph accepts shared prefix/suffix Window, explicit Head, and bare Head as semantic requests, rejects the retired legacy count form of `--rows`, and rejects explicit complete-JSON line clipping before package validation. |
| `InspectionGraphCommandTests.ClusterCommand_SemanticTailSelectsTheSameCallSiteAcrossFormats`, `LibrariesCommand_SemanticTailSelectsTheSameSummaryAcrossFormats`, `LibrariesCommand_SemanticTailSelectsTheSameClusterAcrossFormats`, `LibrariesCommand_StrictUnavailableWindowWithholdsOutput`, `LibrariesCommand_StrictUnavailableSummaryWindowWithholdsOutput`, `LibrariesCommand_StrictUnavailableClusterWindowWithholdsOutput`, `ClusterCommand_ClusterScopePrecedesSemanticWindow`, `ClusterCommand_SummaryGroupingAndClusterScopePrecedeSemanticWindow`, `LibrariesCommand_SemanticSummarySelectionPreservesIncompleteEvidence`, `LibrariesCommand_SemanticClusterSelectionPreservesIncompleteEvidence`, `LibrariesCommand_RejectsLegacyCountRows`, `LibrariesCommand_HeadAllowsCompleteJsonBeforeRequiredInputs`, and `LibrariesCommand_BareSummaryViewRetainsRenderedLineFallback` | The Graph Cluster default and exact Call Sites select the same physical occurrence before every row lowering. Exact Consumer Use Sites and Provider API Types select owner-issued summary identities after focused cluster scoping and grouping without changing complete group counts or call-site receipts. The Graph Libraries default and exact Direct Use Clusters select the same Query-issued cluster identities; the focused route retains one selected cluster. All exact declarations reject legacy numeric `--rows`, withhold output for unavailable strict Window, and permit semantic Head with complete JSON. Selected summary and cluster output preserve incomplete pair evidence and its nonzero exit; bare, path, wildcard, category, and multi-section views remain on rendered-line fallback. |

The Type catalog adoption is enforced by:

| Gate | Property |
| --- | --- |
| `CommandExecutionTests.TypeListing_SemanticTailSelectsTheSameTypeAcrossFormats`, `TypeListing_FiltersBeforeSemanticSelection`, and `TypeListing_PositionalGlobAcceptsSemanticSelection` | An explicit-source Type catalog, including positional and `-t` Type glob forms, applies semantic Head/Tail or Window after type filtering; Markdown, table, TSV, JSONL, and complete JSON consume the same selected `ApiType`, complete JSON recomputes selected counts, and assembly-level Type forwarders remain companion evidence. |
| `CommandExecutionTests.TypeListing_UnavailableWindowWithholdsOutput` and `TypeListing_RejectsInvalidRowsBeforeSourceResolution` | One unavailable strict Window emits no partial output, numeric legacy `--rows` is rejected, and complete-JSON line clipping fails before source resolution. |
| `CommandExecutionTests.TypeListing_ExcludedModesInferRenderedLines`, `TypeListing_NumericTypeFilterIsOrdinaryFilterInput`, `CommandExecutionTests.Member_NumericMemberFilter_MatchesLongSelector`, `TypeOptionsParserTests.NumericMemberAndTypeFilters_AreOrdinaryFilterInput`, `MemberOptionsParserTests.NumericPositionalMember_IsOrdinaryFilterInput`, `SharedParsersTests`, and the commandless numeric-filter cases in `InspectionPlanningTests` | Exact-type and selected-section modes remain outside the declaration and infer rendered Lines; numeric `-t` and `-m` are ordinary Type and Member filter input rather than hidden row counts, including commandless structural routing. |

The Package SourceLink file adoption is enforced by:

| Gate | Property |
| --- | --- |
| `CommandExecutionTests.Package_SourceFilesSection_SemanticTailSelectsTheSameRowAcrossFormats`, `Package_SourceFilesSection_AliasAndTypeSugarAcceptSemanticOpenWindows`, and `Package_SourceFilesSection_Bare_ComposesSemanticAndLineWindows` | The canonical selector, legacy alias, and type-filter sugar select complete package SourceLink file rows after collection; Tail and open or closed Window stages feed Markdown, table, TSV, JSONL, complete JSON, Count, value/URL projection, and bare output, while explicit Lines remains a rendered-text operation. |
| `CommandExecutionTests.Package_SourceFilesSection_UnavailableSemanticWindowWithholdsOutput`, `Package_SourceFilesSection_RejectsLegacyCountRows`, and `Package_SourceLinkFileLinesRejectJsonBeforePackageResolution` | One unavailable strict Window emits no partial payload, the retired numeric `--rows` count form is rejected, and explicit Lines rejects complete JSON before package resolution. |
| `CommandExecutionTests.Package_NonSourceLinkFileSurfacesRetainLegacyWindowValidation` | The `@SourceLink` category, mixed section selection, embedded-library modes, and multiple-package inspection remain outside the semantic declaration and retain legacy Window validation. |

The Package Files adoption is enforced by:

| Gate | Property |
| --- | --- |
| `CommandExecutionTests.Package_FileRows_SemanticTailSelectsTheSameRowAcrossFormats` and `Package_FileRows_AliasAndPathAcceptSemanticWindows` | One selected whole-package Files section applies semantic Head/Tail and strict Window after complete ordered file enumeration and optional path filtering; Markdown, table, TSV, JSONL, complete JSON, Count, value projection, and paths consume the same selected rows. |
| `CommandExecutionTests.Package_FileRows_UnavailableWindowWithholdsOutput`, `Package_FileRows_RejectInvalidRequestsBeforePackageResolution`, and `Package_FileRows_ExplicitLinesClipsRenderedText` | One unavailable strict Window emits no partial payload, numeric legacy `--rows` and complete-JSON line clipping fail before package resolution, document-family bare `-n` remains rendered-line selection, and explicit Lines clips rendered table text. |
| `CommandExecutionTests.Package_FileRows_MultiSectionRetainsLegacyWindowValidation`, `Package_MultiplePackages_FilesJsonlWindowsCombinedRows`, and `PackageContentOutput_RowWindowHydratesTheUnarySelection` | Document-family sections, `@Files`, mixed sections, multiple-package file rows, and package-content payloads remain outside the semantic declaration and retain their existing row-window behavior. |

The Package TFM adoption is enforced by:

| Gate | Property |
| --- | --- |
| `CommandExecutionTests.Tfms_SemanticTailSelectsTheSameFrameworkAcrossFormats` and `Tfms_Count_CountsTheListedFrameworks` | One ordinary `--tfms` lens selects complete TFM rows after package extraction and TFM ordering; Markdown, table, TSV, JSONL, JSON, and Count consume the same selected identity. |
| `CommandExecutionTests.Tfms_UnavailableWindowWithholdsOutput`, `Tfms_RejectInvalidSelectionBeforePackageResolution`, and `Tfms_LinesMakesRenderedClippingExplicit` | One unavailable strict Window emits no partial payload, numeric legacy `--rows` and JSON line clipping fail before package resolution, and explicit Lines clips rendered text. |
| `CommandExecutionTests.Tfms_CompetingLayoutRetainsRenderedLineFallback`, `Tfms_CompetingTreeAndRangesRetainOwnedDiagnostics`, `Tfms_CompetingProjectionsRetainOwnedDiagnostics`, `Tfms_CompetingModifiersRetainLegacyWindow`, and `LensCounts_ApplyRowsAndValidateProjectedColumns` | Competing Package modes, selectors, modifiers, unsupported projections, and valid or malformed range coordinates remain outside the declaration and retain their owned diagnostics or legacy Window validation, while the ordinary TFM lens applies semantic Window before Count, declared-column validation, and JSONL lowering. |

The Match candidate adoption is enforced by:

| Gate | Property |
| --- | --- |
| `MatchDiscoveryTests.Similar_TopSelectsCandidateRowsAcrossJsonAndMarkdown`, `Similar_SemanticTailSelectsTheSameCandidateAcrossFormats`, and `Similar_CliTopUsesSharedSemanticSelection` | The completed Analysis-ranked candidate vector receives semantic Top or Tail once before Markdown, table, TSV, JSONL, or JSON lowering, and the real CLI routes `--top` through the shared row-selection grammar. |
| `MatchDiscoveryTests.Similar_CountObservesTheSelectedCandidateSequence`, `Similar_CliCountObservesSemanticTail`, `Similar_UnavailableSemanticWindowWithholdsOutput`, `Similar_CliUnavailableSemanticWindowWithholdsOutput`, `Similar_SemanticSelectionDoesNotHideRetrievalFailure`, and `Similar_JsonLineSelectionRejectsBeforeSourceResolution` | Count observes selected candidates through both the typed handoff and real CLI; one strict unavailable Window after completed retrieval emits no partial payload; a retrieval failure remains visible instead of becoming a selection failure; complete-JSON line clipping fails before source resolution. |
| `MatchDiscoveryTests.Similar_MaximumResultsBoundsTheProductRetrievalAndIsReported`, `Similar_MethodOutcomes_AreNotBoundedByTop`, `Similar_Json_IdentifiesEveryMethodOutcomeBehindTheReceiptCounts`, `PairwiseMatch_IsUnchangedWhenSimilarIsNotRequested`, and `Pairwise_InferredLimitRetainsRenderedLineFallback` | Product retrieval limits remain distinct from candidate selection; complete method outcomes and the query receipt remain truthful companion evidence; pairwise Match stays outside the semantic candidate declaration and retains inferred rendered-line selection. |

The Library References adoption is enforced by:

| Gate | Property |
| --- | --- |
| `CommandExecutionTests.LibraryCommand_ReferenceRows_SemanticTailSelectsTheSameReferenceAcrossFormats` and `LibraryCommand_ReferenceRows_PackageBackedSelectionUsesTheCompleteReferenceVector` | One exact References section applies semantic Head/Tail after complete direct-reference acquisition and deterministic ordering; Markdown, table, TSV, JSONL, complete JSON, and Count consume the same selected identity for platform and package-backed libraries. |
| `CommandExecutionTests.LibraryCommand_ReferenceRows_UnavailableWindowWithholdsOutput`, `LibraryCommand_ReferenceRows_ExplicitLinesClipsRenderedText`, `LibraryCommand_ReferenceRows_ExplicitLinesRejectJsonBeforeAcquisition`, and `LibraryCommand_DirectReferenceFailure_RemainsVisible` | One unavailable strict Window emits no partial payload, explicit Lines clips rendered table text, complete-JSON line selection fails before library resolution, and semantic selection does not hide a failed Finding inspection. |
| `CommandExecutionTests.LibraryCommand_ReferenceRows_SynthesizedMixedSelectionRetainsRenderedLineFallback`, exact activation checks in `LibraryReferenceRowSelectionAdoption`, and existing Reference Hierarchy and all-TFM Library command tests | Explicit or legacy-alias References combined with a synthesized Source Files section, Reference Hierarchy, and all-TFM package inspection remain outside the declaration and retain their existing row contracts. |

The Clone Candidates adoption is enforced by:

| Gate | Property |
| --- | --- |
| `CloneCandidatesSectionTests.SemanticTailSelectsTheSameCandidateAcrossFormats`, `CountObservesSemanticHeadAcrossSubjectHosts`, `PackageLibraryRouteObservesSemanticSelection`, and `QueryPredicateImplicitSelectionAdoptsSemanticRows` | The Query-issued global ranking receives semantic Head or Tail once before Markdown, table, TSV, JSONL, projected JSON, or complete JSON lowering; Count observes the selected vector across Library, delegated package-backed Library, Type, and Member hosts; Clone predicates reach the same declaration. |
| `CloneCandidatesSectionTests.UnavailableSemanticWindowWithholdsOutput`, `SemanticSelectionFailureKeepsIncompleteCoverageVisible`, `JsonLineSelectionRejectsBeforeSourceResolution`, and `NumericLegacyRowsAreRejectedBeforeSourceResolution` | One strict unavailable Window emits no partial payload, incomplete coverage remains visible beside a selection failure, numeric legacy `--rows` is rejected, and complete-JSON line clipping fails before library resolution. |
| `CommandExecutionTests.TypeListing_SemanticTailSelectsTheSameTypeAcrossFormats`, `Member_FactsProjectedJson_AppliesItemWindowBeforeSerialization`, and `Member_FactsDiscovery_DoesNotActivateProjectedJsonAdoption` | The adjacent Type-catalog and projected Member Facts semantic declarations retain their own activation and row identities. |

The Member Callers adoption is enforced by:

| Gate | Property |
| --- | --- |
| `MemberCallersSectionTests.CallersSection_SemanticTailSelectsTheSameCallSiteAcrossFormats` and `CallersSection_ScansAuthorizedScopesBeforeSemanticSelection` | The completed, deduplicated, deterministically ordered caller-site vector receives semantic Head or Tail once before Markdown, table, TSV, JSONL, structured JSON, or Count lowering; authorized external caller scopes finish before selection, structured JSON exposes the selected Callers rows instead of the surrounding Member document, and a selected subset preserves Source when the completed vector contained rows from multiple source assemblies. |
| `MemberCallersSectionTests.CallersSection_UnavailableWindowWithholdsOutput` and `CallersSection_ExplicitLinesRejectJsonBeforeAcquisition` | One unavailable strict Window emits no partial payload, while explicit rendered-line selection under structured JSON fails before source resolution. |
| `MemberCallersSectionTests.CallersSection_MultiSectionSelectionRetainsRenderedLineFallback` | Mixed Callers/Calls and `@Calls` selection remain outside the declaration and infer rendered-line selection for bare `-n`. |

The Member Calls adoption is enforced by:

| Gate | Property |
| --- | --- |
| `MemberCallsSectionTests.CallsSection_SemanticTailSelectsTheSameCallSiteAcrossFormats`, `CallsSection_GeneratedEvidenceCompletesBeforeSemanticSelection`, and `CallsSection_EmptyStructuredJsonPreservesRowArray` | The completed IL-offset-ordered direct-call vector receives semantic Head or Tail once before Markdown, table, TSV, JSONL, structured JSON, or Count lowering; generated evidence methods finish before selection, exact Calls JSON exposes the selected Calls rows instead of the surrounding Member document, an empty selected vector remains an array, and repeated direct calls remain distinct occurrences. |
| `MemberCallsSectionTests.CallsSection_UnavailableWindowWithholdsOutput` and `CallsSection_ExplicitLinesRejectJsonBeforeAcquisition` | One unavailable strict Window emits no partial payload, while explicit rendered-line selection under structured JSON fails before source resolution. |
| `MemberCallsSectionTests.CallsSection_NeighboringSelectionsRetainRenderedLineFallback` and `CallsSection_SemanticSelectionRetainsSingleOverloadRequirement` | Mixed Calls/Callers and `@Calls` selection remain outside the declaration, while semantic selection retains Member's existing one-selected-overload boundary. |

The Project document row adoption is enforced by:

| Gate | Property |
| --- | --- |
| `CommandExecutionTests.Project_SkillsSection_SemanticTailSelectsSameRowAcrossOutputs`, `Project_SkillsCount_ObservesSemanticHead`, `Project_ReadmeSection_SemanticWindowReindexesPrintRows`, and `Project_ReadmePaths_SemanticWindowReindexesBeforeRowSelection` | One selected Skills or Package README section applies semantic Head/Tail and strict Window to complete document rows before Markdown, table, TSV, JSONL, complete JSON, Count, value/path projection, or print/bare lowering; bare `-S` reaches the Skills declaration, and projection or print row numbers address the selected sequence. |
| `CommandExecutionTests.Project_SingleSection_UnavailableSemanticWindowWithholdsOutput`, `Project_SingleSection_ExplicitLinesClipsRenderedText`, `Project_SingleSection_RejectsInvalidRowRequestBeforeProjectResolution`, and `Project_SingleSection_SemanticHeadCannotHideInvalidLaterRow` | One unavailable strict Window emits no partial payload, explicit Lines clips rendered table text, numeric legacy `--rows` and complete-JSON line clipping fail before project resolution, and semantic Head cannot hide invalid later Skill metadata from complete inventory validation. |
| `CommandExecutionTests.Project_MultiSection_RetainsLegacyWindowValidation` and `Project_MultiSection_InferredLinesRejectJsonBeforeProjectResolution` | Multi-section Project output remains outside the semantic declaration, retains its command-owned legacy row-window behavior, and infers rendered-line selection for bare `-n`. |

The broad explicit-line rollout is enforced by:

| Gate | Property |
| --- | --- |
| `CacheCommandTests` | A command without semantic rows infers rendered lines for bare `-n`, supports inferred Head/Tail and explicit `--lines`/`--tail-lines`, and rejects inferred or explicit line selection with complete JSON before command work. |
| `SkillCommandTests` | `skill`, `skill list`, and focused skill-document commands infer rendered lines for bare `-n`, preserve explicit `--lines`, apply `--tail` to lines, retain fixed Markdown output independent of the environment-selected format, and accurately teach legacy `--rows` composition. |
| `ImplementsCommandTests` and `ExtensionsCommandTests` | Existing semantic Head/Tail and Window selection remains intact, explicit line clipping is available, and numeric `-t` is ordinary type-filter input rather than a hidden row count or a compatibility diagnostic. |
| `PackageChangesCommandTests` | Package activity retains semantic `-n`, does not reuse that count when line selection is explicit, and rejects JSON line clipping before acquisition. |
| `CommandExecutionTests.Member_FactsProjectedJson_AppliesItemWindowBeforeSerialization`, `Member_FactsProjectedJson_RejectsUnavailableWindow`, `Member_FactsProjectedJson_DeduplicatesEquivalentSelectors`, `Member_FactsCount_DoesNotActivateProjectedJsonAdoption`, `Member_FactsCount_LegacyRowsComposeWithInferredLines`, and `Member_FactsDiscovery_DoesNotActivateProjectedJsonAdoption` | Projected member Facts JSON applies semantic Head, Tail, and strict Window selection before serialization, equivalent selector spellings retain the same active adoption, terminal Count preserves legacy row/line composition, and effective or structural discovery remains outside that projection-specific declaration. |
| `CommandExecutionTests.LibraryCoordinateCommand_InferredLinesComposeWithRows` | Inferred rendered-line selection composes with a command-owned legacy row window exactly as explicit `--lines` does, including Head and Tail direction. |
| `CliRowSelectionRouterIntegrationTests` | Uniform fallback candidates lower commandless `-n` as rendered-line selection, while candidates with different effective units require an explicit command before target acquisition. |
| `PayloadLensContainmentTests` | Explicit line clipping preserves end-of-options ownership; row-shaped payload text after `--` is not interpreted as row selection. |

The remaining implementation must satisfy:

| Gate | Property |
| --- | --- |
| `CliRowSelectionGrammarTests` | Positive representable ASCII-decimal counts and coordinates, separated and `=`/`:` attached values, compact `-nN`, lexical shorthand recognition, closed/prefix/suffix Window forms, idempotent exact modifier repeats, tolerated equivalent line/tail redundancy, and gesture repetition lower according to this grammar; zero, signs, non-ASCII digits, overflow, integer/start-plus-count/boundless `--rows`, attached values on zero-arity modifiers, and conflicting directions fail. |
| `CliRowSelectionOrderTests` | `-n`, `--rows`, and `--top` preserve argv order; modifiers change unit or direction without becoming operation-intent positions. |
| `CliRowSelectionBareShorthandTests` | Required, optional, boolean, attached, positional, router, parent-option, and `--` cases classify bare `-N` by parsed arity and ownership; normalization precedes duplicate-gesture lowering. |
| `CliRowSelectionCapabilityTests` | Only the active adopted leaf command accepts its declared gestures; shared helpers and parent commands do not imply adoption. |
| `CliRowSelectionTopOrderBindingTests` | One explicit `--order-by` attaches only as the one `TopIntent`'s unresolved ranking-order operation; no explicit order leaves that operation absent for L2 default resolution; L3 never emits a resolved ranking identity or infers baseline order as ranking. |
| `CliRowSelectionPreExecutionFailureTests` | L3-decidable explicit-command failures occur before command execution or command-owned acquisition, return nonzero, and emit no success-shaped result; L2 ranking failures follow L2-owned timing. |
| `CliRowSelectionFailurePrecedenceTests` | Explicit and implicit multi-fault invocations, token/arity failures, malformed values, token-completed conflicts, tied end-of-argv absence conflicts, multiple capability rejections, and L2 failures produce the one diagnostic selected by their applicable precedence. |
| `CliRowSelectionCountHandoffTests` | Semantic intent remains ordered and intact when terminal `--count` is handed to L2; line/Count behavior is not invented by L3. |
| `CliRowSelectionAdoptionTests` | A command exposes only the grammar gestures and adjacent capabilities it declares; other row-selection spellings do not participate; shared option objects do not imply adoption. |
| `UntrustedArgumentDiagnosticContainmentTests` | Every argv-derived token echoed by parse-time, pre-routing envelope, or lowering diagnostics is contained at the CLI presentation boundary, including `-n`, `--rows`, and `--top` failures. |
| `CliRowSelectionGuidanceTests` | Help, README examples, workflows, and shipped skills teach only behavior available on their named command. |

Each command adoption adds its own outcome-level gate proving selected row
identity across supported formats. These grammar gates do not substitute for
command, L2, source, payload, or presentation evidence.
