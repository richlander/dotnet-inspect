# Portable query intent

## Owner and consumers

This focused owner defines **query intent**: the single serializable
representation of a query anywhere in the product. It is introduced by
[#6971](https://github.com/richlander/dotnet-inspect/issues/6971) as a
cross-cutting pattern locked in one place, adopted by each owner separately.

Its claim is that a query has exactly three layers, and only the first one
crosses a persistence, host, or version boundary:

```text
canonical intent   vocabulary + conjoined terms + execution bounds
                   + ordered selection stages + order references
      |                          <- serializes; a compatibility surface
      v            resolve exactly once, atomically, against one vocabulary
typed bindings     owner-issued identities, accessors, comparers
      |                          <- runtime only
      v
executable plan    RowSelectionPlan, PackageQueryPlan, and their peers
```

Three consumers need this layer and none of them has it:

- **Inspect Web `/query`** stores no request in the URL, so a package query
  cannot be shared, saved, or demonstrated.
- **The share packet** reserves a delegation slot with nothing to fill it.
  [Workspace definitions](workspace-definitions.md) packet format 2 defines its
  `q` table as `[queryId, payload]`, where the payload is "the closed JSON
  object emitted by that query owner's version-2 packet codec" and must
  "round-trip its payload byte-for-byte through parse and canonical write." No
  query owner supplies such a codec.
- **The CLI** spells Package Query facets as `--where "facet=<opaque id>"`, one
  pseudo-field whose value is a product ID checked by string comparison.

Supporting owners retain their contracts:

- [L2 row query and ordering](row-query-order.md) owns row-schema resolution,
  order resolution, and `RowSelectionPlan` execution. This design generalizes
  its already-stated separation between the canonical query key — "the L2
  lookup namespace for unresolved intent" — and both resolved identity and
  display label. It does not restate or modify that resolution contract.
- [Workspace definitions](workspace-definitions.md) owns the packet family,
  its outer bounds, base64url and JSON hardening, canonical scalar escaping,
  and every coordinate-bearing arm.
- [CLI execution bounds](cli-execution-bounds.md) owns what an execution bound
  is, and [semantic row selection](semantic-row-selection.md) owns Head, Tail,
  Window, and Top. This design carries their values; it does not classify them.
- Each vocabulary owner — [Package Query](package-query-experience.md) first —
  owns its own keys, operators, value grammar, and resolution semantics.

## Why exactly one layer serializes

A resolved plan holds typed accessors, comparer factories, and opaque order
identities minted against a schema at one instant. Those bindings cannot
outlive the process that made them, and a share link that embedded them would
break on the next build that renamed one.

The canonical query key is the opposite: already declared stable, already
distinct from typed identity and from display text, and already the lookup
namespace for unresolved intent. Persisting that layer makes a link durable
across builds while leaving every binding beneath it free to change.

The rule is therefore symmetric in both directions. Encoding projects intent
and never a plan; decoding restores intent and resolves it again on the
receiving host under that host's current vocabulary and capabilities. A
restored query is a request to re-run, never a promise of the rows someone else
saw.

## The intent contract

One query intent is:

| Part | Meaning |
| --- | --- |
| `vocabulary` | Owner-issued identity of the vocabulary the terms resolve against. It is the packet's `queryId`. |
| `terms` | A canonical set of `(key, operator, value)` triples, conjoined. |
| `bounds` | Declared execution bounds, each carrying an owner-issued dimension identity. Unordered. |
| `stages` | The ordered selection-stage pipeline. Position-significant. |
| `order` | Optional unresolved order references: one baseline, plus one per ranking stage. |

A **key** is a canonical query key from the named vocabulary's declared key
namespace: a bounded ordinal token. It is not a display label, heading, column
name, or CLI option spelling.

An **operator** is one identity from the closed operator set already used by
row predicates — equality, inequality, and the ordered comparisons. A
vocabulary declares which operators each key admits; intent does not widen that
set, and this design introduces no nesting, disjunction, grouping, or solver.
Disjunction exists only where a vocabulary declares a family whose members
combine, as Package Query's tool-format selection group already does.

A **value** is an inert value token: bounded text preserved exactly as
supplied, constructed through the existing `InertText` containment shapes. This
layer does not parse, normalize, case-fold, or interpret it. Interpretation
belongs to the vocabulary's binder at resolution.

**Bounds** and **stages** are different kinds and never merge.

An **execution bound** is the `ExecutionBoundIntent(Dimension, RequestedMaximum)`
shape owned by [CLI execution bounds](cli-execution-bounds.md), limiting one
owner-named dimension of upstream work; reaching it produces a completion state
and proves nothing about exhaustion. Bounds in different dimensions are
independent, so the set is unordered and emits in a fixed declared slot order.

A **selection stage** is owned by
[semantic row selection](semantic-row-selection.md), which defines an ordered
pipeline in which each stage consumes the preceding stage's output. The sequence
is therefore part of the question, not a presentation detail: over `[4,1,3,2]`,
`Top(10, ascending)` then `Head(2)` yields `[1,2]`, while `Head(2)` then
`Top(10, ascending)` yields `[1,4]`. Stages serialize as a sequence and are
never sorted, deduplicated, or merged into the bound set.

Both parts are part of a query's identity. Changing an execution bound changes
the work a restored query performs and therefore what its completion state can
honestly claim; changing stage sequence changes the answer. Keeping them in
distinct typed slots prevents the error this separation exists to prevent:
reading an acquisition budget as a view window, or the reverse.

**Order** is an unresolved order reference: either one named-order identity
plus a direction, or an ordered list of key-and-direction terms composing
lexicographically in declaration order. Intent carries at most one baseline
order, and one ranking order bound to each selection stage that ranks. A
vocabulary with no order — Package Query has none, because source relevance
order is not its to own — simply omits the part.

Order references live in the same unresolved namespace as keys: named-order
identities and canonical keys, never a resolved comparer, comparer factory, or
opaque order identity. [L2 row query and ordering](row-query-order.md) resolves
them, owns the baseline-versus-ranking distinction, and decides what a missing
or defaulted order means; this design only carries the reference across the
boundary. Without this part, two supported row queries differing only in
baseline order would serialize identically and a restored link would answer a
different question than the one shared, which is the failure this whole
contract exists to prevent.

## Canonicalization

Canonicalization is syntactic. Two intents with identical canonical bytes are
the same query; equality is decided on those bytes and nowhere else.

- Terms sort by key, then operator, then value, each ordinal.
- Exactly duplicated terms collapse. A repeated key bearing a different
  operator or value is preserved: terms conjoin, and whether the conjunction is
  satisfiable is the vocabulary's question, not the codec's.
- Execution bounds emit in a fixed declared slot order. They are independent
  across dimensions, so their declaration sequence carries no meaning.
- Selection stages and order operands are **position-significant and never
  reordered**. Sequence is their meaning, so each emits exactly as declared,
  and a ranking order emits with the stage it binds to.

Three canonicalization classes therefore exist, and no rule may move a part
between them: conjoined terms sort, independent execution bounds occupy fixed
slots, and ordered stages and order operands retain their declared sequence.
- Scalar escaping follows the packet's pinned canonical rules rather than a
  second escaping convention.
- Values are never normalized here. Package identifiers, framework names, and
  assembly names each have owner-specific equivalence rules, and applying any
  of them at this layer would make canonical bytes depend on a vocabulary's
  current semantics.

That last rule has a consequence worth stating plainly: two spellings that a
vocabulary would resolve to the same query can canonicalize to different bytes
and therefore share as different links. Share identity is syntactic. A
vocabulary that wants spelling-independent identity must normalize **before**
constructing intent, where the normalization is visible to the user who typed
it, rather than inside a codec where it would silently rewrite what was shared.

## Declared limits

`workspace-definitions.md` delegates concrete payload limits to the query
owner's codec, so this design pins them rather than saying only "bounded". They
are a compatibility surface with the same standing as keys and operators: a link
one build emits must be admissible on another, so these maxima cannot vary by
build or by vocabulary.

| Limit | Maximum |
| --- | --- |
| Canonical payload | 3 KiB of UTF-8 |
| Nesting depth | 6 |
| Terms | 32 |
| Execution bounds | 8 |
| Selection stages | 8 |
| Order operands | 8, counting the baseline and every ranking reference |
| Key or dimension identity | 64 bytes of UTF-8 |
| Value token | 256 bytes of UTF-8 |

Every text maximum counts UTF-8 bytes, not scalars or grapheme clusters, so the
count is unambiguous for non-ASCII values. Each sits beneath the packet's
per-payload allowance of 4 KiB, depth 12, and 256 JSON values, so a payload
admitted here cannot breach the outer bound.

Limits are charged **as parsed, before duplicate collapse**. A payload declaring
forty terms that would collapse to three is rejected on the fortieth rather than
accepted on the third, so collapse can never be used to force unbounded parse
work. The canonical form must independently satisfy every limit.

A vocabulary may declare stricter limits for its own keys; it may not relax
these, because relaxation would make a link admissible on one build and not
another.

## Resolution boundary

Intent resolves exactly once, against exactly one vocabulary, producing either
the owner's executable plan or one structured failure.

- Resolution is **atomic**. The first failure, in deterministic order, returns
  no plan and no partial binding.
- Resolution **starts no work**. A rejected intent issues no acquisition, no
  source request, and no package payload fetch. This matters more here than for
  row predicates: a package-query term can authorize archive downloads, so a
  malformed restored link must cost nothing.
- A failure is **presentation-free**: term position, vocabulary identity, the
  offending key or operator identity, and a typed reason. No diagnostic
  sentence, rendered value, localized text, or exception text enters the
  contract.
- The distinguishable reasons are at least: unknown vocabulary, unknown key,
  operator not admitted for that key, value rejected by the key's binder,
  duplicate-after-binding, unknown bound dimension, a bound value outside its
  declared range, and an unknown or inadmissible order reference.

Duplicate-after-binding is a **vocabulary-stage** outcome, not a codec-stage
one. Exactly duplicated terms have already collapsed during canonicalization,
so resolution never receives one; what it can receive is two syntactically
distinct terms that the vocabulary's binder maps to the same predicate, because
canonicalization deliberately does not normalize values. Whether that collision
collapses idempotently or fails is the vocabulary's decision, declared by that
owner. It follows from syntactic canonicalization that two links differing only
in a spelling the vocabulary treats as equal may behave differently; that is the
stated cost of keeping share identity independent of vocabulary semantics.

## Replay and compatibility

Keys, operator identities, vocabulary identities, and bound dimension
identities are a **persisted compatibility surface**. Once a build emits one
into a share link or demo record, removing or renaming it breaks artifacts that
already exist.

A restored intent naming something the current build cannot resolve **fails
visibly**. It is never dropped, never defaulted, and never silently narrowed or
widened. Dropping an unresolvable term would answer a different question than
the one that was shared while looking like the one that was shared, which is the
same failure mode the bounded-completion rules exist to prevent.

## What intent never contains

- Resolved identities, typed accessors, comparers, or opaque order identities.
- Display labels, headings, column names, rendered values, or localized text.
- Outcomes: rows, counts, evidence, failures, or completion states. A shared
  query re-runs; nuget.org moves, and stale rows presented as current would be
  a lie the completion contract already forbids.
- Host state, credentials, local filesystem paths, or source authority.
- CLI argument spellings or option names. `--take`, `--where`, and their peers
  are L3 text that lowers into intent and never round-trips out of it.

## Packet projection

Canonical intent is the payload of one `[queryId, payload]` tuple in packet
format 2's query table: `queryId` is the vocabulary identity, and the payload
is the canonical intent object.

- Parse and canonical write round-trip byte-for-byte, satisfying the packet's
  owner-codec requirement.
- The pinned maxima in [Declared limits](#declared-limits) sit beneath the
  packet's per-payload allowance and are enforced before any vocabulary binder
  runs.
- Semantically identical query states deduplicate on canonical bytes, matching
  the packet's stated table ordering and dedup rule.

Packet format 2 retains `t`, `g`, `a`, and `x`, requires one view-state entry
per coordinate-table index, and rejects empty contexts. A package query has no
coordinate to carry a view state, so the packet family needs an arm that
carries query intent with no coordinate table. **That arm is owned by
[workspace definitions](workspace-definitions.md)**; this design supplies only
the payload it would carry.

## Required gates

| Gate | Contract |
| --- | --- |
| `IntentCanonicalFormRoundTripsByteForByte` | Parse then canonical write reproduces exact bytes for every supported term, operator, execution bound, selection stage, order operand, and escaping vector. |
| `IntentCanonicalFormIsIndependentOfTermOrder` | Term sequence, duplicate terms, and execution-bound declaration sequence do not change canonical bytes; semantically identical states deduplicate. |
| `SelectionStageSequenceSurvivesRoundTrip` | Stage sequence survives byte-for-byte and is never sorted, deduplicated, or merged into the bound set; two intents differing only in stage sequence have different canonical bytes, witnessed by the `Head`/`Top` commutation case. |
| `OrderOperandsRetainDeclaredSequence` | An order operand's sequence and direction survive round-trip exactly, are never sorted or deduplicated, and a ranking order stays bound to its selection stage; two intents differing only in baseline order have different canonical bytes. |
| `IntentResolutionIsAtomic` | An invalid vocabulary, key, operator, value, or bound returns the deterministic first structured failure with no plan and no partial binding. |
| `IntentResolutionStartsNoWork` | A rejected intent issues no acquisition, source request, or payload fetch; gated with a source capability that fails the test if invoked. |
| `UnresolvableTermFailsVisibly` | An intent naming a key, operator, or dimension absent from the current build fails; it is never dropped, defaulted, narrowed, or widened. |
| `IntentCarriesNoResolvedOrPresentationState` | Serialized intent contains no resolved identity, accessor, comparer, label, rendered value, or outcome. |
| `BoundKindsRemainDistinct` | An execution bound never resolves as a selection stage or the reverse, and each retains its owner-issued dimension identity. |
| `IntentFailureShapeIsPresentationFree` | Failures carry only position, vocabulary identity, offending identity, and typed reason. |
| `DuplicateAfterBindingIsReachableAndVocabularyOwned` | Two syntactically distinct terms that a vocabulary binds to one predicate reach the vocabulary stage and take that owner's declared collapse-or-fail outcome; no duplicate reaches resolution as canonical bytes. |
| `DeclaredLimitsPrecedeVocabularyBinding` | Every limit in the declared-limits table is enforced against the payload as parsed, before duplicate collapse and before any vocabulary binder runs, with cancellation observed. |
| `DeclaredLimitsAreBuildInvariant` | The pinned maxima are identical across vocabularies and builds; a payload at each exact maximum is admissible and one byte past each is refused. |
| `HostileIntentTextRemainsContained` | Adversarial value tokens round-trip through `InertText` construction and canonical escaping without escaping containment or reaching a diagnostic. |

## Decisions

**One syntax and intent type, separate resolvers.** Package terms and row
predicates share the `(key, operator, value)` shape, and
`RowPredicateSyntaxParser` already produces exactly that triple. They resolve
against different vocabularies with different consequences: a package term can
authorize acquisition, a row predicate never can. One shared syntax and intent
type with two resolution targets keeps `--where` coherent for anyone typing it,
while the acquisition distinction lives in the vocabulary, where it is visible,
rather than in the parser, where it would not be. That parser is currently
`internal` to `DotnetInspect.Cli`; the browser editor needs the same lowering,
so it must become host-neutral rather than acquire a TypeScript twin.

**Bounds travel in intent, typed by kind.** They are part of what a restored
query does and therefore of what it may claim. Merging the two kinds into one
integer would be the single most likely way to make a shared query dishonest.

## Non-claims

- No vocabulary. Package Query's keys, operands, tiers, and UI are
  [#6972](https://github.com/richlander/dotnet-inspect/issues/6972).
- No change to row-schema resolution, order resolution, or plan execution.
  Intent carries unresolved order references; it does not resolve them, rank
  anything, or define what a missing order means.
- No change to the packet's coordinate arms, outer bounds, hardening, or
  record family, and no new packet format defined here.
- No acquisition, pushdown, pagination, merge, or completion-evidence
  semantics. Intent describes a request, not its cost model or its honesty
  accounting.
- No free-text expression language, and no L3 spelling decisions.
