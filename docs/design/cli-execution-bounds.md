# CLI execution bounds

## Status and owner

Focused L3 design proposal for
[#6489](https://github.com/richlander/dotnet-inspect/issues/6489), a
prerequisite for continuing command adoption under
[#4677](https://github.com/richlander/dotnet-inspect/issues/4677).

This document owns how `dotnet-inspect` classifies, spells, validates, and
lowers explicit CLI execution bounds. An execution bound limits one
owner-named dimension of upstream work. It is not semantic row selection, and
reaching it does not by itself prove exhaustion or an exact result.

The design is intentionally host-specific. During #4677 design review, the
user selected a typed CLI option family rather than one universal scalar
spelling. Browser and Wasm hosts retain their own controls while consuming the
same query-, source-, or graph-owner request and completion contracts. This
document creates no host-neutral substrate and does not redefine those
contracts.

The end-to-end tracker has three steps:

1. lock this L3 grammar and composition boundary;
2. adopt it for `find`, with `--take` bounding package-prefix candidates while
   `-n` selects final semantic rows; and
3. correct `package search` so `--take` and `-n` remain separate intents.

Other command-owned bounds are evidence for the family, not implicit
participants in that adoption path.

Related owners:

- [CLI row-selection grammar](cli-row-selection.md) owns semantic `-n`,
  Window, Top, direction, and rendered-line selection.
- [Inspection layers](inspection-layers.md) owns the L3-to-L2 typed-intent
  boundary.
- [Progressive disclosure](progressive-disclosure.md) owns user-visible
  disclosure of non-semantic operational bounds.
- [Section-row shaping](section-row-shaping.md) owns declared row sets, Rows,
  Count, and result binding.
- [Source delegation](source-delegation.md) owns semantics-preserving source
  execution and exact completion evidence.
- Each adopting query, source, analysis, or graph design owns its execution
  dimension, stopping rule, hard ceilings, result evidence, and failures.
- [Item and line selection composition](item-and-line-limits.md) sequences
  these owners without redefining them.

## Claim

L3 preserves two independent user intentions:

- **semantic selection** chooses final logical rows after owner-defined
  filtering and effective ordering; and
- **execution bounding** authorizes at most a stated amount of work in one
  owner-defined dimension before semantic result construction completes.

The same integer must not stand for both intentions. L3 lowers an execution
bound with its owner-issued dimension identity and requested maximum. The
adopting operation returns its own bound and completion evidence. L3 preserves
and discloses that evidence; it never turns "the bound was reached" into "the
source was exhausted."

## Authority and scope

The L3 CLI execution-bound grammar is the authority for:

- deciding whether a count-like option is semantic selection or an execution
  bound;
- selecting `--take` or a dimension-specific long option;
- CLI arity, numeric validation, repetition, aliases, and capability
  rejection;
- lowering an explicit option to a typed dimension plus maximum;
- keeping execution-bound intent independent from row-selection intent;
- passing owner-issued bound and completion state to presentation without
  inferring stronger claims; and
- command-by-command adoption requirements and CLI gates.

This design does not own:

- what a package, candidate, match, method, node, edge, hop, byte, page, or
  other domain value means;
- source paging, filtering, ordering, deduplication, retry, timeout, caching,
  traversal, or early-exit algorithms;
- default operational ceilings or provider hard limits;
- whether a source result is complete, equivalent, Count-sufficient, or
  Rows-usable;
- semantic row-set identity, predicates, ordering, Rows, Count, or rendering;
- Browser or Wasm controls; or
- adoption by a command not named in [Adoption](#adoption).

An adopting owner must define those behaviors before L3 exposes its bound.
This grammar cannot make an untyped legacy integer coherent merely by giving
it a new option name.

## Terms

### Semantic selection

Semantic selection is an operation over an owner-defined logical row sequence.
The reference path obtains the applicable rows, applies filtering and effective
ordering, then applies Head, Tail, Window, or Top according to the focused row
contracts.

`-n N` is semantic Head by default on an adopted command. It answers "which
final rows survive?" It does not authorize fewer sources, candidates, pages,
methods, or graph hops unless an adopting source separately proves an exact
semantics-preserving delegation under the source-delegation contract.

### Execution bound

An execution bound is an explicit maximum over one owner-defined work
dimension. It answers "how much of this work may the operation attempt?" The
operation may stop when the maximum is reached even though more applicable
results could exist.

An execution bound therefore has three necessary pieces:

1. an **owner-issued dimension identity** that gives the integer a unit,
   execution stage, and scope;
2. a positive **requested maximum**; and
3. an adopting operation that can report whether its execution and completion
   evidence were constrained by that dimension.

The L3 lowering is conceptually:

```text
ExecutionBoundIntent(Dimension, RequestedMaximum)
```

`Dimension` is not an option string. It is the adopting owner's typed identity.
Downstream code does not recover meaning from `--take`, `--candidates`, or
other raw CLI text.

This design does not require one universal .NET type. The first adoption may
reuse an owner-issued request type when that type preserves the identity and
maximum without string recovery. Shared implementation is justified only when
the two named CLI consumers would otherwise duplicate the same lowering,
validation, or result-preservation logic.

### Operational ceiling

An operational ceiling is a source-, provider-, or command-owned safety limit
that applies whether or not the user supplies a CLI bound. It is not an
implicit `ExecutionBoundIntent`.

An explicit execution bound may replace a configurable default in the same
dimension according to the adopting owner's contract. It does not erase a
provider hard limit, timeout, page ceiling, or another independently owned
constraint. When another constraint stops the operation first, the result
names that actual reason rather than attributing truncation to the user's
bound.

L3 must not silently clamp an explicit value to a known smaller supported
maximum. A statically known maximum is a validation boundary. A limit learned
only during execution remains owner-issued outcome evidence.

## Option family

### `--take`

`--take N` is eligible only when all of these statements are true:

- the command or selected mode has one primary upstream item dimension;
- that dimension has an effective owner-defined order;
- "take N" naturally means at most N items from that dimension;
- the bound is not defined as semantic selection over the command's declared
  final row set; and
- no second plausible work dimension makes the bare noun ambiguous.

The dimension identity distinguishes scope such as per source from aggregate
across selected sources; an integer and unit alone are insufficient. Command
help names the unit, scope when it is not already obvious, and effect. For
example:

```text
--take <PACKAGES>  Maximum package candidates to inspect
```

The metavar or description must not say only "results" or "limit." Those words
hide whether the option controls source work or final rows.

`--take` is not a universal alias for every integer maximum. A command does not
gain it merely because its implementation calls LINQ `Take`, accepts a
provider `take` parameter, or stops a loop after N iterations.

### Dimension-specific names

Use a dimension-specific option when the unit or role carries information the
user needs to reason about cost or completeness. Existing shapes include:

- a structural unit such as `--depth`;
- a role noun such as `--candidates` or `--matches`; and
- an explicit scan ceiling such as `--max-methods` or `--max-packages`.

These spellings are not interchangeable style variants. They preserve
different dimensions. A command with candidate and match budgets may expose
both; collapsing them into one `--take` would discard which stage may stop.

An existing option is not automatically classified as an execution bound by
appearing in this list. Its owning design must show that it constrains upstream
work rather than selects final semantic rows.

### Selection spelling

Use `-n`, `--rows`, `--top`, and their row-selection modifiers only for the
semantic operations owned by
[CLI row-selection grammar](cli-row-selection.md).

Do not retain a command-specific count option as an alias for `-n` merely for
compatibility. When one legacy option currently mixes a filter, work bound,
and result limit, its adoption separates those roles and retires ambiguous
spellings. Shipped `SKILL.md` files, help, examples, and completions are the
primary compatibility inventory.

## Validation and lowering

### Value contract

Every execution-bound option:

- has arity exactly one;
- accepts a base-10 positive integer;
- rejects zero, negative, missing, malformed, and overflowing values before
  acquisition or other owner effects;
- rejects a value above a statically declared supported maximum rather than
  silently clamping it; and
- produces the common option-value diagnostic shape.

The adopting owner declares any tighter minimum or maximum required by its
dimension. L3 validates those static constraints while lowering the same
requested value that the user authored.

### Occurrence contract

One argv may contain at most one occurrence for each execution-bound
dimension. Repeating the same option or using two aliases for the same
dimension is a deterministic L3 error.

Different dimensions may coexist when the command owns their composition.
Their option order does not imply execution order or precedence. The adopting
owner's typed request names both dimensions and owns their interaction.

An adopted command does not expose an old spelling and a replacement spelling
as long-lived synonyms. A short migration may parse an obsolete form only to
produce a retirement diagnostic; it does not lower two names to one hidden
integer.

### Cross-family failure precedence

An adopted invocation may contain invalid row-selection and execution-bound
gestures together. L3 extends the row grammar's
[failure precedence](cli-row-selection.md#failure-precedence) without changing
row-only behavior:

1. the first System.CommandLine token, option-arity, or unknown-option failure
   in argv order;
2. the first malformed, nonpositive, overflowing, or statically out-of-range
   row-selection or execution-bound value in argv order;
3. the first repeated row gesture, repeated execution-bound dimension, or
   token-completed conflict in argv order;
4. active-command capability rejection for the first unsupported requested row
   gesture, modifier, or execution-bound dimension in argv; and
5. the first owner-issued resolution failure in that owner's defined order.

Within a category, argv position compares occurrences from both grammars.
An execution-bound implementation must not validate in a separate pre-pass
whose diagnostic always wins or loses regardless of token position. Absence
conflicts retain the row grammar's end-of-argv ordering.

The explicit-command adapter owns the combined diagnostic selection. An
implicit route may classify only execution-bound spellings whose ownership and
dimension are uniform across every surviving route; otherwise authoritative
child routing precedes bound capability resolution. Each implicit adoption
defines and gates that envelope rather than inheriting row-option routing by
analogy.

### Capability contract

Execution-bound capability belongs to the active command mode or lens, not to
the root command by accidental option inheritance. L3 rejects a recognized
bound before acquisition when the selected mode has not adopted its dimension.

The capability binds:

- the option identity;
- the owner-issued dimension identity;
- static value constraints; and
- the owner path that consumes and reports the bound.

Raw aliases and help text stop at L3. The operation receives typed intent.

## Composition

CLI row selection and execution bounds lower independently:

```text
CLI tokens
-> L3 row grammar -> typed row-selection intent
-> L3 bound grammar -> typed execution-bound intent
-> adopting owner resolves both against its command mode
-> source/query/graph owner executes within its effective constraints
-> owner-issued rows, failures, bound state, and completion evidence
-> filtering and effective ordering not already proven upstream
-> semantic row selection
-> Rows, Count, projection, and presentation
```

The diagram gives logical ownership, not a required eager execution algorithm.
A source may perform filtering, ordering, or semantic selection early only
through its independently owned source-delegation proof.

### Independent intent

L3 must preserve all explicitly authored intents. It must not:

- copy `-n` into an upstream `take`, candidate, match, or traversal request;
- rewrite an execution bound into semantic Head;
- choose the smaller of `-n` and an execution bound as one combined limit;
- treat equality between returned row count and a bound as completion; or
- remove owner-issued incompleteness because later row selection returned
  fewer rows.

The following request is valid:

```console
dotnet-inspect package search json --take 100 -n 10
```

It authorizes at most 100 items in the package-search work dimension, then
selects 10 final semantic rows. If the operation reaches 100 without exhaustion
evidence, the ten displayed rows remain part of an incomplete search outcome.

The inverse numeric relationship is also valid:

```console
dotnet-inspect package search json --take 10 -n 20
```

The operation may produce fewer than 20 final rows. L3 does not reject the
request or increase the work bound because the two values answer different
questions.

### Count

An execution bound neither selects Count's input nor proves its exactness.
`--count` may compose syntactically with an execution bound, but
[Section-row shaping](section-row-shaping.md#count-semantics) and the adopting
source owner decide whether the returned completion evidence is
Count-sufficient.

In particular, reaching `--take 10` does not make `10` an exact corpus count.
If the owner cannot produce the exact Count contract, it preserves the bounded
or unavailable outcome instead of publishing the ceiling as a total.

### Failures and diagnostics

An execution bound controls work; it does not authorize hiding failures.
Failures observed before the stop remain part of the owner-issued result.
Sources or candidates not attempted because a bound stopped execution remain
represented according to the adopting owner's completion and truncation
contract.

L3 renders that structured outcome. It does not replace it with an empty
success, infer missing failures, or reconstruct a truncation reason from row
count.

## Disclosure

The adopting operation must provide enough typed outcome information for L3 to
distinguish:

- the requested execution-bound dimension and maximum;
- whether that bound actually constrained execution;
- a different operational constraint that stopped execution first;
- exact completion when the owner can prove it; and
- failure or unavailable evidence.

This design does not prescribe one result type or one sentence. The
[progressive-disclosure](progressive-disclosure.md) owner determines placement
and verbosity. The invariant is that bounded incompleteness remains
user-observable in every format that represents the affected result.

A default operational ceiling follows the same disclosure rule when it
constrains the result, even though no explicit CLI intent exists. Ordinary
successful execution need not narrate every ceiling that was not reached.

## Convention and deliberate divergence

The ecosystem uses `take` for more than one concept:

- .NET LINQ
  [`Enumerable.Take`](https://learn.microsoft.com/dotnet/api/system.linq.enumerable.take)
  returns a prefix of a sequence.
- Kusto
  [`take`](https://learn.microsoft.com/kusto/query/take-operator)
  returns up to a requested number of result rows and is equivalent to its
  `limit` operator.
- The NuGet
  [Search Query Service](https://learn.microsoft.com/nuget/api/search-query-service-resource)
  uses `skip` and `take` as pagination request parameters, may impose a maximum
  `take`, and separately returns `totalHits`.

The first two are result-selection operators. `dotnet-inspect` assigns that
role to its richer semantic row grammar, principally `-n` and `--rows`.
Duplicating that meaning in `--take` would recreate the overlapping vocabulary
this design is meant to remove.

The NuGet API demonstrates the narrower operational use: a request may return
up to `take` items while corpus size and provider ceilings remain separate
facts. `dotnet-inspect package search --take` may preserve that familiar
upstream-item meaning, but the provider convention does not prove source
exhaustion or define final row selection.

The deliberate divergence is therefore bounded and visible: `--take` is an
execution-bound spelling only on commands with one unambiguous ordered
upstream item dimension. Everywhere else, the CLI names the dimension.

## Adoption

### Step 1: lock the grammar

This document, its architecture entry, and the thin composition-map update
lock only L3 ownership and sequencing. They do not change product behavior.

### Step 2: adopt `find`

The `find` owner defines each mode's final row set and work dimensions.
Command-wide row-selection adoption then:

- retires numeric and short-form `-t`;
- retains long-form `--type` only where it is a genuine type filter;
- exposes `--take` only for the patternless package-prefix profile mode's one
  package-candidate dimension;
- rejects `--take` in semantic and literal Package Query modes, which retain
  their independently owned `--candidates` and `--matches` dimensions; and
- keeps `-n` after owner-defined result construction.

That focused adoption decides whether ordinary type/member early exit can be
proven equivalent through source delegation or must be removed. This design
does not decide it.

### Step 3: correct `package search`

The package-search owner separates:

- `--take`, which bounds the upstream package-search stream; and
- `-n`, which selects final package rows.

The adoption preserves source failures and truncation evidence, updates help
and shipped skills, and replaces tests that treat the two values as one limit.

### Later evaluations

Owners of `--depth`, `--candidates`, `--matches`, `--max-methods`,
`--max-results`, or `--max-packages` may evaluate this pattern when their own
design changes. This issue neither renames those options nor declares that
their current implementations satisfy the contract.

## Required evidence

This docs-only slice is gated by Markdown validation and adversarial design
review. Product claims remain **unverified** until each adoption supplies
Release gates for its observable contract.

Every adopting command must gate at least:

- invalid, missing, repeated, and unsupported bound rejection before owner
  effects;
- deterministic cross-family failure selection for invalid bound and
  row-selection combinations;
- independent lowering of the execution bound and semantic row selection;
- unchanged execution-bound intent when `-n` changes;
- unchanged row-selection intent when the execution bound changes;
- visible owner-issued incompleteness when the bound constrains execution;
- refusal to publish the bound as an exact Count without completion evidence;
- preservation of failures observed before the bound stops work;
- parity across every supported output format; and
- help, shipped skill, completion, and neighboring no-bound behavior.

An adoption that delegates semantic work upstream also runs the source owner's
equivalence gates. Bound tests do not substitute for those proofs.

## Pathological cases

| Request or condition | Required result |
| --- | --- |
| `--take 10 --count` reaches ten without exhaustion evidence | Do not publish ten as an exact corpus total. |
| `--take 10 -n 20` | Valid independent intents; return at most the available final rows and preserve incompleteness. |
| `--take 100 -n 10` reaches the work bound | Select ten final rows while retaining the owner-issued bounded outcome. |
| Candidate and match budgets both exist | Keep both dimension identities; do not collapse them into one bare integer. |
| Provider hard ceiling is lower than the explicit request | Reject when statically known; otherwise report the actual provider constraint without rewriting the user's request. |
| Failure occurs before the bound is reached | Preserve the failure; the bound is not a success fallback. |
| Source returns exactly N rows for `--take N` | Row count alone proves neither that the user bound constrained execution nor that the source is exhausted. |
| Command has no effective order for its primary stream | Do not expose generic `--take`; define the order or use an owner-specific bound whose semantics do not imply a prefix. |

## Non-claims

This design does not:

- make every finite operational limit user-configurable;
- require every existing count option to use one shared implementation type;
- classify timeouts, cancellation, memory ceilings, or byte budgets;
- define default values or maximums for any adopting command;
- promise that requesting more work produces more semantic rows;
- authorize unbounded network, source-content, exhaustive, or graph work;
- make a bounded result complete, Count-sufficient, or failure-free;
- define package-search ordering, Package Query budgets, `find` row sets, or
  graph traversal; or
- preserve obsolete flags solely for compatibility.
