# L2 row query and ordering

## Status

Focused L2 design proposal for
[#5162](https://github.com/richlander/dotnet-inspect/issues/5162), adopting the
row-selection composition pattern locked by
[Item and line selection composition](item-and-line-limits.md). The
host-neutral L2 contract is implemented under
[#6841](https://github.com/richlander/dotnet-inspect/issues/6841); production
adoption remains tracked by
[#6840](https://github.com/richlander/dotnet-inspect/issues/6840).

The Release gates in [Required gates](#required-gates) verify the implemented
L2 behavior.

Related designs:

- [Inspection layers](inspection-layers.md) assigns typed operation resolution,
  executable-plan construction, and declared-row-set binding to L2.
- [Semantic row selection](semantic-row-selection.md) owns `Head`, `Tail`,
  `Window`, and `Top` execution after L2 resolves every order identity.
- [Output shapes](output-shapes.md) owns declared row units and the
  Document-to-Scalar shape ladder.
- [Section-row shaping](section-row-shaping.md) owns declared-row-set binding,
  projection roles, terminal Count, and typed result binding.
- [Source delegation](source-delegation.md) owns delegation planning and
  completion-evidence binding without changing this design's query
  semantics.
- [Schema query](schema-query.md) owns section and projection discovery. Its
  current field spellings are not row-query key identities.
- [The package query CLI](package-query-cli.md) contains provisional CLI
  examples. They do not define this L2 contract or approve a CLI grammar.

## Authority and scope

This L2 design is the authority for resolving typed row-query intent against
one declared section-row vocabulary. The generic implementation is carried by
the dependency-free `QuerySpace` assembly under `QuerySpace.Rows`;
[QuerySpace Library Boundary](query-space-library.md) owns that physical and
namespace composition without changing this contract. Concrete vocabularies
and declared-row-set binding remain with their subject owners.

This design owns:

- stable key and order identities used by row queries;
- the predicate capabilities and comparison domains declared by a row
  vocabulary;
- predicate binding and evaluation over typed row values;
- baseline-order and ranking-order resolution;
- the distinction between sequence order and ranking order;
- the effective baseline order supplied to semantic row selection; and
- the opaque order identities and total resolver supplied to semantic row
  selection.

This design does not own:

- CLI option names, expression grammar, aliases, token order, diagnostics, or
  compatibility;
- declared-row-set binding across sections, field or column projection,
  logical reductions such as count, or common result binding;
- source requests, pushdown, acquisition, pagination, merge, deduplication, or
  completion evidence;
- `Head`, `Tail`, `Window`, or `Top` execution semantics;
- payload acquisition, rendering, output formats, or line selection; or
- Markout schema construction or presentation behavior.

Those adjacent owners consume this contract or provide its inputs without
redefining row-query meaning.

## Typed boundary

L3 first validates its own syntax and lowers it into typed operation intent.
For row-query operations, that intent distinguishes:

- predicate operations, each containing a key reference, operator identity,
  and inert value token;
- an optional baseline-order operation; and
- an optional ranking-order operation attached to each intended `Top` stage.

An order operation is either one named-order reference plus direction or an
ordered list of key-and-direction terms. Key terms compose
lexicographically in declaration order. The intent carries no executable
expression string.

Unresolved references use the row vocabulary's canonical query keys. They are not
display headings. L3-owned compatibility aliases and focused flags lower to
those keys before this boundary; L2 does not know which CLI spelling produced
them. L2 resolves the references and values exactly once against the selected
row vocabulary.

Selection-stage intent is complete and satisfies the semantic component's
construction preconditions before it reaches L2: counts and present window
coordinates are positive, closed windows are ordered, and every stage has all
required operands. L2 constructs the executable `RowSelectionPlan`; a caller
violating this precondition is misuse rather than a row-query resolution
failure.

Successful resolution produces:

1. typed predicate bindings;
2. an effective baseline-order binding;
3. one opaque resolved ranking-order identity for each `Top` stage; and
4. the executable semantic `RowSelectionPlan` containing those identities; and
5. an immutable resolved-order catalog from which L2 supplies the semantic
   executor's comparer resolver.

The catalog is total for every order identity emitted into the plan. `Top`
contains only the opaque identity. The semantic component retains authority
over lazy, stage-ordered resolver invocation and comparer caching.
Resolution stores typed comparer-producing bindings without invoking owner
factories. Baseline execution invokes its factory after predicates and before
semantic selection; each `Top` factory remains behind the semantic resolver and
is invoked only when that stage is reached.

Resolution failure produces one structured failure and no executable request.

This boundary is per vocabulary. The later L2 declared-row-set design owns how one
resolved request is associated with one or more concrete row sets and how
results are rebound to their declared identities.

## Row vocabulary contract

A row vocabulary exposes queryable structure independently from presentation:

- A **key identity** is a stable owner-issued token. Its display name,
  heading, aliases, canonical query key, and rendered value are not its
  identity.
- A queryable key declares its typed value domain and the predicate and order
  capabilities valid for that domain.
- One typed accessor supplies that key value for both predicate and order
  evaluation. A key descriptor erases the value type only after construction
  has paired that accessor with its typed operand binder and typed order
  comparer factory.
- A **named order identity** is a stable owner-issued token resolving to one
  deterministic order over the vocabulary's rows. A named order may compose
  multiple typed keys without exposing that composition as a downstream
  comma-separated string.
- A vocabulary may independently declare one default baseline order and one
  default `Top` ranking. Named orders are classified as either `Sequence` or
  `Ranking`; only a ranking may be the default for `Top`.

The canonical query key is the L2 lookup namespace for unresolved intent. It
is stable within the row vocabulary contract but remains distinct from both
the typed identity used after resolution and the label shown to a user.

The same displayed key may participate in predicates, ordering, projection,
all three, or none. Those capabilities are independent. In particular, a
key need not be projected to remain available to a predicate or order.

Query evaluation reads typed key values from the row contract. It must not
parse table cells, formatted numbers, localized text, headings, labels, or
other rendered output back into data.

## Predicate resolution and evaluation

A predicate operation identifies one key, one vocabulary-supported operator, and
one value token. L2 resolves the key identity, verifies that the operator is
valid for its typed domain, and asks the key's typed binder to normalize the
value before any row is evaluated. An ordinary binder rejection is the
structured invalid-value resolution failure. An unexpected binder exception
propagates unchanged; it is not converted into a user-input failure.

Multiple predicates form one conjunction. A row survives only when every
predicate succeeds. Predicate evaluation preserves the incoming relative order
of surviving rows.

Comparison meaning belongs to the key's typed domain. Numeric comparison is
numeric, enum or ranked-value comparison follows the vocabulary-issued order, and
text matching follows the resolved text predicate. Display formatting never
changes that meaning.

The CLI design may later choose spellings for exact, negated, ordered, or text
predicates. Those spellings cannot introduce an operator the selected vocabulary
does not support.

## Order resolution

An order binding is deterministic over the selected vocabulary's rows and has one
of two purposes:

- **Sequence** establishes a stable traversal order without claiming that
  earlier rows are better.
- **Ranking** establishes priority, so earlier rows are better under the
  resolved criteria.

This distinction is semantic, not presentational. Alphabetical, declaration,
token, source, or insertion order may provide useful sequence stability without
being a ranking. A relevance, confidence, severity, or explicitly requested
priority order may be a ranking.

Every order operation has an explicit direction. Direction applies to the
complete named order or to its individual key term. The vocabulary supplies the
direction-aware comparer, including any owner-defined placement for missing
typed values; L2 does not infer missing-value order by reversing rendered
values.

An ordered list of key terms compares the first term, then each later term
only when every earlier term compares equal. Rows equal under the complete
order retain their incoming relative order.

### Effective baseline order

After predicates run, L2 establishes the baseline sequence consumed by semantic
row-selection stages:

1. an explicit baseline-order binding, when typed intent supplies one;
2. otherwise the vocabulary's declared default baseline order, when present; or
3. otherwise the incoming owner-defined row order.

Equal comparisons preserve incoming relative order. The resulting order is
therefore deterministic whenever the incoming row order and resolved comparer
are deterministic.

### Ranking order for `Top`

Each `Top` stage receives its own resolved ranking-order identity. The order is
attached to that stage, not promoted into the baseline order.

This separation preserves ordered-stage meaning:

```text
Head(100) -> Top(10, ScoreDescending)
```

`Head` first keeps the first 100 rows in the effective baseline order. `Top`
then ranks only those survivors by `ScoreDescending`. Sorting the complete
input before `Head` would produce a different plan and is not equivalent.

Typed intent may supply an explicit ranking order for a `Top` stage. Without
one, L2 may use only the vocabulary's default `Top` ranking. The default baseline
order never becomes a `Top` ranking implicitly, and the default `Top` ranking
never becomes the baseline implicitly. A vocabulary may intentionally assign one
ranking identity to both roles, but that is an explicit vocabulary declaration.

Repeated `Top` stages resolve independently and may carry different opaque
ranking identities. Every emitted identity resolves through the accompanying
catalog. The semantic component owns when each resolver entry is invoked, how
comparers are cached, how stages reindex their inputs, and how equal ranks
preserve current order.

## Logical composition

For one complete logical row sequence, the L2 reference order is:

```text
owner-defined rows
-> conjunctive typed predicates
-> effective baseline order
-> semantic RowSelection stages
```

A `Top` stage performs its own ranking at its position in the semantic plan.
Other semantic stages consume the current sequence order without changing the
meaning of the baseline-order binding.

This reference order defines observable row-query meaning. A source owner may
execute predicates, ordering, or semantic selection elsewhere only through the
[source delegation](source-delegation.md) contract's exact-equivalence and
honest-completion rules. This design does not
define that optimization or its evidence. Delegation follows that owner's
[source-closed boundary](source-delegation.md#source-closed-operations);
operations this design does not declare source-closed remain on the reference
or row-handoff residual path.

If this owner declares an operation source-closed, it proves that declaration
with `SourceClosedDeclarationsMatchOwnerContracts` against this design's
resolution, callback, exception, ordering, and failure-precedence contract.

Membership projection is outside this owner and supplies the rows entering this
sequence. Cell projection, Count, and payload operations are also outside this
owner.
[Section-row shaping](section-row-shaping.md#reference-composition) defines
those two projection positions and where Count observes the result without
changing predicate or order semantics; the focused payload design remains
separate.

## Failure model

Resolution returns one structured failure when:

- a key or named order does not resolve in the selected vocabulary;
- a key does not support the requested predicate or ordering capability;
- a value token cannot normalize into the key's typed domain;
- a `Top` stage has neither an explicit ranking order nor a default `Top`
  ranking; or
- an owner-issued binding is otherwise invalid for that vocabulary.

The failure identifies the selected vocabulary, operation kind, its one-based
position within that kind, reason, and the resolved key or named-order
identity when lookup succeeded. An unknown reference carries no invented
target identity; L3 retains its original typed intent at the reported position.
A `Top` ranking failure also carries its semantic stage number. The failure
contains no CLI diagnostic sentence, rendered row value, exception text, or
presentation suggestion.

Validation examines predicates in declaration order, then the baseline-order
operation, then `Top` ranking operations in semantic stage order. Terms within
one order operation follow their declaration order. The first failure wins.
Resolution is atomic: a failure returns no partial predicate set, effective
order, resolved-order catalog, semantic plan, or source request. A binder
returning ordinary rejection participates in that precedence as an
invalid-value failure.

An unexpected exception thrown by a binder during resolution, or by an
already-resolved key accessor, predicate, order factory, comparer, or order
resolver during execution, is not a row-query failure. The implementation must
surface it unchanged rather than converting it into an invalid-value result,
empty output, or successful partial query.

## Implementation and adoption sequence

The first implementation is a two-PR stack under
[#6840](https://github.com/richlander/dotnet-inspect/issues/6840):

1. implement this complete host-neutral L2 vocabulary, resolver, plan, failure,
   and executor contract with the required gates below; then
2. migrate the complete Performance Triage key set for `library`, `type`, and
   `member` without dividing keys between old and new evaluators.

Performance Triage declares no vocabulary default baseline order. L3 supplies its
Triage or Allocation Fanout baseline only for requests whose current contract
requires that baseline. The vocabulary independently declares Triage descending as
the default `Top` ranking, so a Top-only request ranks only at the Top stage.
Explicit equal-order rows retain incoming order; legacy hidden Member, IL, or
Shape tie-breakers and reversing equal groups are not part of this contract.
Focused `--loop` and `--min-confidence` gestures lower to canonical typed
predicates. `--triage-shape` remains a separate membership or producer control
because its repeated values are a set and Allocation Fanout may change the
admitted population.

The browser/Wasm adoption path extends the existing Type Dependency row plan
that already flows through `DotnetInspect.Web.Interop.Metadata`. Its row vocabulary
will consume this same L2 resolver and executor through the production browser
facade rather than adding a host-specific query evaluator.

## Worked examples

### Predicate plus positional selection

Given rows in owner order:

```text
A(score 3), B(score 1), C(score 3), D(score 2)
```

and a predicate `score >= 2`, with no declared or explicit baseline order, the
semantic input is:

```text
A(score 3), C(score 3), D(score 2)
```

`Head(2)` therefore returns `A, C`. The displayed spelling of `score` and its
formatted values are irrelevant to evaluation.

### Baseline order versus `Top` ranking

With an explicit baseline sequence of `NameAscending`:

```text
A(score 1), B(score 1), C(score 1), D(score 5)
```

the plan:

```text
Head(3) -> Top(2, ScoreDescending)
```

first keeps `A, B, C`, then returns `A, B` because equal scores retain that
current order. Promoting `ScoreDescending` into the baseline would instead
produce `D, A`; the two plans are observably different.

### Baseline default cannot rank implicitly

A default baseline order of `MetadataTokenAscending` may make traversal stable.
It does not answer which rows are most important. A `Top` stage without an
explicit ranking order therefore fails resolution unless the vocabulary separately
declares a default `Top` ranking.

## Required gates

The implementation must add these named Release gates:

| Gate | Contract |
| --- | --- |
| `RowQueryResolvesVocabularyIdentitiesOnce` | Key and order spellings are resolved once at the L2 boundary into owner-issued identities; execution receives no unresolved expression or rendered key name. |
| `RowPredicatesUseTypedValues` | Numeric, ranked-value, and text predicates evaluate typed row values and are unchanged by presentation formatting, headings, or projected columns. |
| `RowPredicatesConjoinAndPreserveOrder` | Multiple predicates use AND semantics and retain the incoming relative order of every surviving row. |
| `EffectiveBaselineOrderFollowsPrecedence` | Explicit baseline order wins over a declared default; a declared default wins over incoming owner order; equal comparisons retain incoming order. |
| `TopRankingDoesNotBecomeBaselineOrder` | `Head(100) -> Top(10, order)` ranks only the first-stage survivors, while applying the same order as the baseline before `Head` produces the independently expected different result. |
| `EachTopCarriesItsResolvedRankingIdentity` | Repeated `Top` stages may resolve distinct order identities, the semantic plan contains no unresolved key or named-order spelling, and the supplied resolver is total for every emitted identity. |
| `BaselineAndTopDefaultsAreIndependent` | A baseline default never satisfies `Top` implicitly, a default `Top` ranking never changes baseline order implicitly, and one identity occupies both roles only through two explicit vocabulary declarations. |
| `RowQueryResolutionIsAtomic` | Any invalid key, operator, value, order, or ranking requirement returns the deterministic first structured failure and no executable plan or source request. |
| `RowQueryFailureShapeIsPresentationFree` | Resolution failures contain only typed operation position, selected vocabulary identity, available resolved target identity, and reason; no diagnostic sentence, rendered value, or exception text enters the contract. |
| `RowQueryExecutionFailuresStayVisible` | A sentinel exception from an already-resolved accessor, predicate, order factory, comparer, or resolver is not converted into successful empty or partial output, and an earlier strict-stage failure prevents later resolver invocation. |

## Non-claims

This design does not select CLI spellings, define source optimization, place
count or projection relative to the selected rows, or change current product
behavior. Those decisions remain with their focused owners.
