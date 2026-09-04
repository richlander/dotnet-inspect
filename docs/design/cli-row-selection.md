# CLI row-selection grammar

## Status and owner

Focused L3 design proposal for
[#5414](https://github.com/richlander/dotnet-inspect/issues/5414), part of
[#4677](https://github.com/richlander/dotnet-inspect/issues/4677).

This document owns the `dotnet-inspect` command-line grammar and lowering
boundary for semantic row selection and rendered-line selection. The current
product has not adopted this contract.

Implementation is partial. #5644 implements value parsing, ordered lowering,
modifier composition, Top-order attachment, typed capability rejection, and
structured failure selection over already-owned explicit-command option
occurrences. #5678 implements the explicit-command adapter that establishes
required-value ownership from System.CommandLine, normalizes eligible bare
shorthand, preserves raw positions, extracts those occurrences, and invokes
the lowerer. Implicit routing, diagnostic selection and rendering, command
adoption, and guidance remain unimplemented.

Only those two explicit-command subsets are verified by their named Release
gates in [Required gates](#required-gates). Every other asserted behavior
remains unverified until its named gate lands.

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
independent input. It similarly records a missing-value failure when an exact
row value option is followed by end of argv, `--`, or a known option token;
signed numeric text remains a value for common validation.

The four value-bearing row options use repeatable raw-string option identities
with one value allowed per token. This parser shape preserves each occurrence
without producing System.CommandLine's scalar-option aggregate error; the
explicit-occurrence lowerer therefore owns repeated-gesture failure. The
adapter preserves parser errors and structured row-arity failures but does not
yet select or render the one diagnostic when both exist.

This adapter is not installed in the current command path. Existing
rendered-line compatibility behavior, explicit commands, and implicit routing
remain unchanged.

## Convention and deliberate divergence

GNU `head` and `tail` establish the familiar short count gesture and
first-versus-last direction:

- [`head -n N`](https://www.gnu.org/software/coreutils/manual/html_node/head-invocation.html)
  keeps the first *N* lines;
- [`tail -n N`](https://www.gnu.org/software/coreutils/manual/html_node/tail-invocation.html)
  keeps the last *N* lines.

`dotnet-inspect` deliberately changes the default unit from rendered lines to
the active command's declared semantic rows. Rendering is not the semantic
source of truth, and commands already return useful items such as packages,
types, dependencies, and graph edges. Explicit `--lines` retains the Unix text
operation when presentation lines are the intended unit.

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

- the required-value arity union protects a following negative token whenever
  any candidate route must consume it as that option's value;
- an invocation with no row-selection request follows ordinary routing;
- when candidate declarations assign different meanings, support, or required
  adjacent capabilities to a requested gesture or modifier, the invocation
  fails without routing and requires an explicit command;
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

Adoption is explicit on the active leaf command. A command does not become
adopted because it happens to use a shared option object, renders a table, or
shares an execution helper with an adopted command.

One adoption PR defines:

- the command and subcommand boundary;
- the declared row set or sets supplied by L2;
- which semantic and line gestures are supported;
- any adjacent capability required for `Top`, payload projection, or source
  delegation;
- the same selected logical rows across every supported format; and
- outcome-level gates for the command's pathological and neighboring cases.

A command that does not declare this contract makes no claim about this
grammar. Shared/root guidance must not call `-n` universal until all commands
named by #4677 have adopted it. An adopted command uses `-n` only for semantic
rows; its rendered-line operation is available only through `--lines`.

One invocation is governed entirely by the active command's declaration and
never changes meaning based on whether a later subsystem happens to handle the
result.

## Supported spellings and guidance

Only spellings in [Grammar](#grammar) are part of this contract. L3 does not
define behavior for any other row-selection spelling.

Each adoption removes other overlapping row-selection spellings from that
command. Its help, README examples, workflows, and shipped skills change in
the same PR. Help states that `-n` selects semantic rows, `--lines` selects
rendered lines, and an explicit `--order-by` belongs to `--top` when both are
present.

Guidance names only behavior available on its declared command. Shared guidance
does not anticipate adoption.

## Mock demo

The first proposed command adoption is the finite `demo list` catalog:

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
Error: stage 2 (--rows 2..3) requires row 3 from stage 1's output, but only 2 rows are available.
```

The exact diagnostic shape will consume the L2 structured failure contract;
this mockup establishes the visible nonzero outcome, not presentation text
owned by a later implementation.

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

The remaining implementation must satisfy:

| Gate | Property |
| --- | --- |
| `CliRowSelectionGrammarTests` | Positive representable ASCII-decimal counts and coordinates, separated and `=`/`:` attached values, compact `-nN`, lexical shorthand recognition, closed/prefix/suffix Window forms, idempotent exact modifier repeats, tolerated equivalent line/tail redundancy, and gesture repetition lower according to this grammar; zero, signs, non-ASCII digits, overflow, integer/start-plus-count/boundless `--rows`, attached values on zero-arity modifiers, and conflicting directions fail. |
| `CliRowSelectionOrderTests` | `-n`, `--rows`, and `--top` preserve argv order; modifiers change unit or direction without becoming operation-intent positions. |
| `CliRowSelectionBareShorthandTests` | Required, optional, boolean, attached, positional, router, parent-option, and `--` cases classify bare `-N` by parsed arity and ownership; normalization precedes duplicate-gesture lowering. |
| `CliRowSelectionCapabilityTests` | Only the active adopted leaf command accepts its declared gestures; shared helpers and parent commands do not imply adoption. |
| `CliRowSelectionRouterPreflightTests` | Request-free invocations route ordinarily; a determinate request across non-uniform candidate declarations requires an explicit command; uniform non-support rejects before routing; uniform support handles common token, arity, value, repetition, and modifier failures before target resolution; arity-union-indeterminate cases defer dependent decisions while preserving required negative option values; every envelope failure returns nonzero and emits no success-shaped result. |
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
