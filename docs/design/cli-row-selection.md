# CLI row-selection grammar

## Status and owner

Focused L3 design proposal for
[#5414](https://github.com/richlander/dotnet-inspect/issues/5414), part of
[#4677](https://github.com/richlander/dotnet-inspect/issues/4677).

This document owns the `dotnet-inspect` command-line grammar and lowering
boundary for semantic row selection and rendered-line selection. The current
product has not adopted this contract. All asserted behavior is unverified
until the gates in [Required gates](#required-gates) land.

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
| `-n N` | `Head(N)`, or `Tail(N)` when `--tail` is present |
| bare `-N` | exact shorthand for `-n N` |
| `-n N --head` | explicit `Head(N)` |
| `-n N --tail` | `Tail(N)` |
| `--rows A..B` | closed `Window(A, B)` |
| `--rows A+K` | closed `Window(A, A + K - 1)` |
| `--rows A..` | suffix `Window(A, null)` |
| `--rows ..B` | prefix `Window(null, B)` |
| `--top N --order-by ORDER` | `Top(N)` with explicit L2-resolved ranking order |
| `--top N` | `Top(N)` with the schema-declared default Top ranking |
| `-n N --lines` | first *N* rendered lines, not a semantic stage |
| `-n N --lines --tail` | last *N* rendered lines |
| `-n N --tail-lines` | exact sugar for `-n N --lines --tail` |

`N`, `A`, `B`, and `K` are unsigned decimal tokens whose values must be
positive and fit the integer representation used by typed L3 intent. Zero,
signs, and overflow fail value validation. `A + K - 1` must also fit that
representation. `A..B` requires `B >= A`.

`--rows` accepts only a range form. A bare integer is not a range and fails
with guidance to use `-n N`. The boundless semantic identity Window is not
exposed as `--rows ..`; a no-op option is more likely an incomplete request
than useful intent.

`--head` and `--tail` are direction modifiers for the one `-n` gesture. They
carry no count, conflict with each other, and fail when no `-n` or bare `-N`
is present. Count-bearing `--head N`, `--tail N`, and `--take N` are not part
of the grammar.

Before positional binding on an adopted command, a recognized `--head` or
`--tail` immediately followed by a separate, otherwise-unowned unsigned
decimal token is the retired count-bearing form. It fails at the modifier
token with the canonical form `-n <positive-count> --head` or
`-n <positive-count> --tail`. This is a syntax template, not a reconstructed
or paste-ready full argv. Retirement guidance is available only when the
active command or active common route envelope supports the replacement
Head/Tail gesture; otherwise the recognized retired form produces ordinary
capability rejection. The rule applies only before `--`;
`-n 5 --tail -- 20` preserves `20` as a positional literal.

`--tail-lines` is a boolean modifier, not a count-bearing option. It requires
`-n`, conflicts with `--head`, and supplies both line unit and tail direction.
Combining it with the equivalent `--lines` or `--tail` modifier is tolerated
redundancy and does not add another operation.

`--top` takes its own positive count. It does not consume `--head` or `--tail`;
ranking direction belongs to its order operand. Because the CLI exposes at most
one `Top`, one explicit `--order-by` in the same invocation attaches to that
`Top` stage and is not also promoted to baseline order. Its position in argv
does not create a stage or change the `Top` stage's position.

Without `--top`, `--order-by` retains its L2-owned baseline-order role. With
`--top` and no explicit `--order-by`, L2 may use only the schema's declared
default Top ranking. A default baseline order is not a ranking. This grammar
does not provide two simultaneous explicit order operands; a command that
needs both an explicit baseline order and a different explicit Top ranking
must wait for a separately designed spelling. The same limitation prevents an
explicit baseline order from composing with the default Top ranking: in an
invocation containing `--top`, the one explicit order always belongs to Top.

Each of `-n`, `--rows`, `--top`, and an exposed `--order-by` may occur at most
once in one adopted invocation. This keeps modifier and Top-order binding
unambiguous while still allowing the three different stage kinds to compose.
Repeated stages remain supported by the semantic component for non-CLI
consumers and future grammar evolution.

## Ordered semantic intent

L3 preserves the argv order of `-n`, `--rows`, and `--top` after shorthand
rewriting. Direction and unit modifiers do not occupy a stage position:

- `--tail` changes the `-n` stage from `Head` to `Tail`;
- `--lines` removes `-n` from semantic intent and creates line-selection
  intent; and
- `--tail-lines` performs both changes.

Parent-bound options retain their original token position when the active leaf
command is determined. Router resolution lowers only the authoritative child
parse; a speculative router parse cannot commit a different operation order.

Examples over the logical sequence `[1,2,3,4,5,6,7,8]`:

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
semantic intent: Window(3,6)
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

1. establish required-value, attached-value, and `--` ownership;
2. normalize eligible bare `-N` shorthand; and
3. classify recognized retired forms before positional binding.

Ordinary value, repetition, modifier, and capability lowering follows those
steps. Bare shorthand therefore already has `-n` identity when later
conflicts are evaluated, while retired guidance never reinterprets an owned
value.

L3 rewrites `-N` to `-n N` only when:

- it occurs before the `--` end-of-options marker;
- the token is `-` followed by one or more decimal digits;
- the active command exposes `-n`; and
- the token is not owned as the value of a preceding required-value option.

Recognition is lexical; zero and overflow still rewrite and then fail the
common numeric-value validation. This keeps shorthand-shaped invalid input
from becoming an unrelated unknown option while preserving the positive,
representable surface grammar.

An optional-valued or boolean option does not claim a following `-N` merely
because its parser could accept a value. A required-value option does claim it,
including a negative integer that is semantically valid for that option.

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
route-independent envelope:

- the required-value arity union protects a following negative token whenever
  any candidate route must consume it as that option's value;
- while every candidate route is unadopted, the released router grammar and
  behavior remain unchanged;
- when an owned current or retired row-selection gesture is requested and
  candidate routes have mixed adoption or assign different meanings to that
  gesture, the invocation fails without routing and directs the caller to name
  an explicit command;
- the new route-independent grammar is active only when every candidate route
  has adopted the same meaning and required adjacent capability; and
- once active, malformed row-selection spellings, common retired
  count-bearing modifier forms, common row-option arity or repetition failures,
  and route-independent modifier conflicts fail without routing.

When candidate routes disagree about whether an option consumes a following
token, the arity union marks that token indeterminate as well as protected.
Every envelope decision that depends on whether the token is an option value,
bare shorthand, positional, or a modifier's companion is deferred to the
authoritative child. Protection prevents premature reinterpretation; it does
not manufacture a route-independent absence or conflict.

After routing, the authoritative child parse performs ordinary lowering. This
design does not claim that L2 order or schema resolution happens before target
acquisition: L2 owns that work and its failure timing. The router guarantee is
narrower and enforceable — L3 never performs target acquisition merely to
decide whether a CLI spelling is malformed, ambiguous across routes, or
unsupported by the common route envelope.

Envelope activation is evaluated per requested gesture. A route whose
released and adopted spellings happen to overlap is not common-capability
evidence when their meanings differ. For example, legacy rendered-line `-n`
and adopted semantic-item `-n` require an explicit command across a mixed
candidate set. An invocation with no owned current or retired row-selection
gesture follows released routing unchanged even when candidate adoption is
mixed.

## L3 conflicts and failure

The active command validates CLI-decidable conflicts before command execution:

- nonpositive counts or coordinates;
- malformed or overflowing counts or coordinates;
- reversed, integer-only, or boundless `--rows`;
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
- gives one supported current-form template when a spelling retired, without
  claiming to reconstruct the caller's full corrected argv;
- occurs before the active command executes;
- emits no success-shaped empty result.

An explicit command reports these failures before command-owned acquisition.
An implicit route follows the narrower
[route-independent envelope](#implicit-routing). L2 resolution and adjacent
payload failures retain their owners and timing.

### Token ownership for retired spellings

A token is eligible for retired-spelling guidance only after L3 determines
its ownership for the active command or active common route envelope:

- a token consumed as a required option value is never reinterpreted;
- a token after `--` is a positional literal;
- an attached value remains owned by its option;
- an otherwise-unrecognized option spelling may receive the focused retirement
  diagnostic; and
- before positional binding, a recognized `--head` or `--tail` plus its
  immediately following unowned unsigned decimal token is the retired
  count-bearing form rather than a boolean plus a positional.

Recognized retired syntax is not an unknown option. When its replacement
gesture is unsupported, L3 retains the gesture identity for capability
rejection instead of offering unavailable replacement guidance.

This is the same ownership discipline as bare `-N`. For example,
`--out --take` preserves `--take` as the output path when `--out` requires a
value, while an unowned `--take 5` can report the `-n <positive-count>`
replacement form. Likewise, `--tail 20` reports the
`-n <positive-count> --tail` form, while
`-n 5 --tail -- 20` preserves the positional `20`. The `--` marker changes
token ownership; it does not waive the modifier's requirement for `-n`.

### Failure precedence

One explicit-command invocation can contain several bad tokens. L3 reports one
selection diagnostic using this order:

1. supported replacement guidance for a recognized retired spelling or
   count-bearing modifier form, in argv order;
2. the first System.CommandLine token, option-arity, or unknown-option failure
   in argv order;
3. the first malformed, nonpositive, or overflowing row-selection value in
   gesture order;
4. the first repeated gesture or modifier conflict at the token that completes
   the conflict;
5. active-command capability rejection, including recognized retired syntax
   whose replacement gesture is unsupported, for the first unsupported
   requested gesture or modifier in argv; and
6. the one L2 resolution failure supplied in L2's owned resolution order,
   including an unresolved ranking order.

Token-completed conflicts use the position of the token that makes the
combination invalid. Absence conflicts, such as `--lines` without `-n`,
complete at end of argv and therefore follow every token-completed conflict in
the same category. When several absence conflicts complete together, the
modifier that appeared first in argv selects the diagnostic.

An implicit invocation has two ordered phases:

1. the route envelope applies required-value and `--` ownership, then reports
   the first active-envelope supported retired-form guidance, common row-option
   token or arity failure, malformed value, repeated gesture or token-completed
   modifier conflict, end-of-argv absence conflict, or mixed/non-uniform
   capability rejection, using that listed category order and the corresponding
   explicit-command tie-breaker within each category; and
2. after successful routing, the authoritative child applies the explicit
   command order above, including child-specific retired and unknown options.

The envelope phase precedes child-specific categories because those categories
do not exist until the child is known. While every possible child is unadopted,
the released router path runs instead and this new precedence is inactive.

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

During migration, an unadopted command retains its released behavior and help.
It does not claim this grammar, and shared/root guidance must not call `-n`
universal until all commands named by #4677 have adopted it. An adopted command
uses `-n` only for semantic rows; its rendered-line operation is available only
through `--lines`.

This temporary difference is an adoption state, not a fallback: one invocation
is governed entirely by the active command's declared state and never changes
meaning based on whether a later subsystem happens to handle the result.

## Compatibility and guidance

Compatibility is deliberately low. As each command adopts:

- numeric count overloads on command-specific `-t`, `-m`, or equivalent
  options retire;
- `--take` and count-bearing `--head`/`--tail` do not remain as compatibility
  aliases;
- a retired spelling fails with its supported `-n`, `--rows`, or `--top`
  replacement form rather than a reconstructed full argv;
- when `--top` is present, the one explicit `--order-by` becomes Top's operand
  rather than baseline order, and adopting help calls out that rebinding;
- retired-spelling recognition follows required-value and `--` token ownership;
- nonnumeric selector aliases are outside this design and remain only when
  their command still owns them; and
- the command's help, README examples, workflows, and shipped skills change in
  the same PR.

Guidance for unadopted commands remains accurate to released behavior. The
migration does not preserve stale examples merely to make one binary appear
uniform before its commands are ready.

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

All gates run in Release. Until implemented, each property is **unverified**.

| Gate | Property |
| --- | --- |
| `CliRowSelectionGrammarTests` | Positive representable counts, lexical shorthand recognition, zero and overflow across `-n`, bare `-N`, `--rows`, and `--top`, sign-bearing values for value-taking gestures, range-only Window forms, modifiers including tolerated line/tail redundancy, repetition, and replacement diagnostics lower according to this grammar. |
| `CliRowSelectionOrderTests` | `-n`, `--rows`, and `--top` preserve argv order; modifiers change unit or direction without becoming stages. |
| `CliRowSelectionBareShorthandTests` | Required, optional, boolean, attached, positional, router, parent-option, and `--` cases classify bare `-N` by parsed arity and ownership; normalization precedes retired-form and duplicate-gesture lowering. |
| `CliRowSelectionCapabilityTests` | Only the active adopted leaf command accepts its declared gestures; shared helpers and parent commands do not imply adoption. |
| `CliRowSelectionRouterPreflightTests` | All-unadopted routes preserve released behavior; gesture-free mixed routes preserve released routing; a requested gesture across mixed routes requires an explicit command; all-adopted routes reject common row-option arity, repetition, and malformed grammar before target resolution; arity-union-indeterminate cases defer dependent decisions while preserving required negative option values; every envelope failure returns nonzero and emits no success-shaped result. |
| `CliRowSelectionTopOrderBindingTests` | One explicit `--order-by` attaches only to the one Top stage; no explicit order uses only a schema default Top ranking; baseline order is not inferred as ranking. |
| `CliRowSelectionPreExecutionFailureTests` | L3-decidable explicit-command failures occur before command execution or command-owned acquisition, return nonzero, and emit no success-shaped result; L2 ranking failures follow L2-owned timing. |
| `CliRowSelectionFailurePrecedenceTests` | Explicit and implicit multi-fault invocations, token-completed conflicts, tied end-of-argv absence conflicts, multiple capability rejections, retired count-bearing modifier forms, and owned retired-looking values produce the one diagnostic selected by their applicable precedence. |
| `CliRowSelectionCountHandoffTests` | Semantic intent remains ordered and intact when terminal `--count` is handed to L2; line/Count behavior is not invented by L3. |
| `CliRowSelectionMigrationTests` | Adopted commands expose only current count spellings; retired `--take`, `--head N`, and `--tail N` before positional binding name a supported current-form template without claiming a reconstructed runnable argv; unsupported replacement gestures receive capability rejection; unadopted commands retain accurate released help. |
| `CliRowSelectionGuidanceTests` | Help, README examples, workflows, and shipped skills teach only behavior available on their named command. |

Each command adoption adds its own outcome-level gate proving selected row
identity across supported formats. These grammar gates do not substitute for
command, L2, source, payload, or presentation evidence.
