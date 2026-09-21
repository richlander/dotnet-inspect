# Portable query intent

## Status

**Implemented substrate and first vocabulary; sharing adoption pending.**
`QuerySpace` carries the intent type, identity texts, semantic orders,
canonical payload codec, vocabulary abstraction, and atomic resolver under the
root `QuerySpace` namespace. [QuerySpace Library
Boundary](query-space-library.md) owns that physical and namespace composition
without changing this owner's semantics. The Release
gates in [Required gates](#required-gates) enforce that substrate. Package Query
now supplies the first production vocabulary and both CLI and Browser lower
through it. Definitions record binding, packet projection, and share-link
restoration remain work under #6971.

This is **slice 1 of 2** under
[#6971](https://github.com/richlander/dotnet-inspect/issues/6971). It owns the
intent model: what a query intent is, how it resolves, and what replaying one
must guarantee, including the semantic order of every part. Slice 2,
[Portable query payload](portable-query-payload.md), now owns the single byte
spelling that model has — how those orders are emitted, the closed shape,
pinned tokens, declared limits, and packet projection. The model went first
because the payload has nothing to encode without it, while a host can hold and
resolve intent in process without ever serializing one.

## Owner and consumers

This focused owner defines **query intent**: the single serializable
representation of a query anywhere in the product. It is introduced by
[#6971](https://github.com/richlander/dotnet-inspect/issues/6971) as a
cross-cutting pattern locked in one place, adopted by each owner separately.

Its claim is that a query has exactly three layers, and only the first one
crosses a persistence, host, or version boundary:

```text
canonical intent   vocabulary + terms + execution bounds
                   + ordered selection stages + order operations
      |                          <- serializes; a compatibility surface
      v            resolve exactly once, atomically, against one vocabulary
typed bindings     owner-issued identities, accessors, comparers
      |                          <- runtime only
      v
executable plan    RowSelectionPlan, PackageQueryPlan, and their peers
```

Three consumers motivate this layer and are at different adoption stages:

- **Inspect Web `/query`** executes the shared Package Query intent but stores no
  request in the URL, so a package query cannot yet be shared, saved, or
  demonstrated from its canonical request.
- **The share packet** reserves a delegation slot with nothing to fill it.
  [Workspace definitions](workspace-definitions.md) packet format 2 defines its
  `q` table as `[queryId, payload]`. Portable Query Payload now supplies the one
  closed payload shape and canonical codec: `queryId` names the vocabulary and
  `payload` carries the intent's four serializable parts. Together they identify
  one canonical `PortableQueryIntent`. Workspace Definitions record binding and
  query-bearing packet adoption remain work under #6971.

  **The slot alone is not sufficient for a package query.** Format 2 rejects
  empty contexts, so it cannot represent a query-only package-discovery
  scenario. Workspace Definitions now specifies a format-3 query-only
  composition: the leading null-navigation row carries one coordinate-free
  primary query without pretending that the Workspace subject is query input.
  Its implementation is a counted step on the path to a working `/query` share
  link rather than an implied consequence of landing the codec.
- **The CLI** lowers Package Query `--where` terms through the same vocabulary.
  Packet emission and replay of that intent remain pending.

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
| `vocabulary` | Owner-issued identity of the vocabulary the terms resolve against. It is part of an intent's identity; where an encoding carries it is the codec's. |
| `terms` | A set of `(key, operator, value)` triples; membership is set-valued, so an identical triple present twice is one term. Composition follows the vocabulary's declared families. |
| `bounds` | Declared execution bounds, each carrying an owner-issued dimension identity. Unordered. |
| `stages` | The ordered selection-stage pipeline. Position-significant. |
| `order` | Optional unresolved order operations, keyed by role: at most one baseline, plus at most one per ranking stage. |

A **key** is a canonical query key from the named vocabulary's declared key
namespace: a bounded ordinal token. It is not a display label, heading, column
name, or CLI option spelling.

An **operator** is one of eight identities — equality, inequality, starts-with,
negated starts-with, contains, negated contains, at-least, and at-most — whose
canonical texts are `eq`, `ne`, `starts-with`, `not-starts-with`, `contains`,
`not-contains`, `gte`, and `lte`. As with a key, the text *is* the identity: it
is what a vocabulary admits, what orders a term, and what any encoding carries,
never an enum name or a CLI spelling. A vocabulary declares which operators
each key admits and owns the value domain's comparison behavior; intent does
not widen that set, normalize text, or introduce nesting, grouping, or a
solver.

The same holds for every other fixed identity in an intent. **Identity texts**
are the model's: directions `asc` and `desc`; stage kinds `head`, `tail`,
`window`, and `top`; order-operation kinds `named` and `fields`; and the
baseline role `base`. An encoding carries these texts and defines none of them;
what it owns is structure — how identities and values are arranged into bytes.

**Composition.** Terms conjoin across families. Terms belonging to one
vocabulary-declared **combining family** form an OR-union within that family,
and the union conjoins with everything outside it. Family membership is declared
by the vocabulary, not marked on the term, so the serialized shape stays one
flat set and the codec needs no knowledge of composition. Package Query's
tool-format selection group is the existing instance: `v1` and `v2` are an
OR-union, and production evaluation already accepts the group when any selected
member matches. A universal conjunction rule would silently rewrite that
supported query into one requiring a package to be both formats at once.

A family may instead declare its members **mutually exclusive**: two of them in
one intent are not a narrower question but a contradiction, and resolution
refuses the pair. Package Query's dependency group is the existing instance —
`has dependencies` and `no dependencies` replace each other rather than
combining. Exclusivity, like combination, is declared by the vocabulary and
read at resolution, not marked on the term.

A vocabulary may additionally declare two **bound terms incompatible** when
their relationship is not one family. The compatibility relation is symmetric
and is evaluated over owner-bound terms, so it may use keys, operators, values,
or the predicates the binders issued; it does not add a second composition
system. Terms the relation accepts still compose only by their families.
Package Query's broad `.NET Tool` fact beside a specific tool format is the
existing instance: `tool-format=v1` and `tool-format=v2` remain a valid
combining-family OR-union, while either one beside `tool=true` is refused rather
than silently broadening or redundantly restating the question.

Because composition and compatibility are read from the vocabulary rather than
the payload, family membership — combining or exclusive — and the compatibility
relation are part of that vocabulary's compatibility surface: changing which
terms combine or conflict changes what an already-shared link means, and is
governed by the same replay rules as removing a key.

A vocabulary may require that an intent contain at least one member of a named
family. This expresses a required owner choice without adding a privileged
intent slot or selecting one key as the model's default. Required families are
checked only after every present term has resolved; an invalid present term
therefore fails at its own semantic position before an absent family is
reported. If more than one required family is absent, the family whose identity
sorts first by the model's scalar order is reported. Its failure is located at
the next semantic term position and names the missing family.

A **value** is bounded text preserved exactly as supplied. This layer does not
parse, normalize, case-fold, or interpret it. Interpretation belongs to the
vocabulary's binder at resolution, and containment belongs to the sink: a value
reaching a display surface is spelled there through the existing `InertText`
shapes under that surface's policy. It cannot be contained here instead, because
those shapes encode what they contain and a value must reach the far side of a
share link as the same bytes it left with — the codec's round-trip is the whole
point, and every policy `InertText` offers refuses scalars the payload's vectors
require to survive.

**Bounds** and **stages** are different kinds and never merge.

An **execution bound** is the `ExecutionBoundIntent(Dimension, RequestedMaximum)`
shape owned by [CLI execution bounds](cli-execution-bounds.md), limiting one
owner-named dimension of upstream work; reaching it produces a completion state
and proves nothing about exhaustion. A dimension's admissible range is the
vocabulary's to declare, and it **may depend on the terms already bound** —
Package Query admits at most 20 candidates once a package-content term is
present, because each candidate then costs an archive — so the range is
evaluated with the terms resolved, which the precedence below guarantees, and a
breach is a bound failure located at the bound. A dimension carries **at most
one** bound:
two maxima for one dimension are not a narrower request, they are a
contradiction. Bounds in different dimensions are independent, so their
declaration sequence carries no meaning.

A vocabulary may require an execution-bound dimension. Required dimensions are
checked after every present bound has resolved, so an invalid present bound
fails before absence is considered. If more than one required dimension is
absent, scalar identity order selects the first failure. Its location is the
next semantic bound position and its offender is the missing dimension. A
required bound is never supplied by a vocabulary default: replay either carries
the bound that was shared or refuses.

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

**Order** is a role-keyed set of unresolved order operations. Each is either one
named-order identity plus a direction, or an ordered list of key-and-direction
terms composing lexicographically in declaration order — the two forms
`row-query-order.md` admits. Every operation carries its own role, so intent
holds at most one baseline operation and at most one ranking operation per
ranking stage, and an operation's internal boundary is never lost by flattening
it against a neighbour. Because the role identifies the operation, the set has
no meaningful outer sequence. A
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

### Semantic order

Every part has a total order over its elements, and that order is the model's,
not an encoding's. An intent held and resolved in process never touches a codec,
yet two hosts must still agree on which of several defects is reported first; so
the orders live here, and any encoding emits them rather than defining them.

- **Terms** order by key, then operator, then value — each compared as text:
  the key's identity text, the operator's identity text, and the exact value
  token, never a resolved or normalized form.
- **Bounds** order by dimension identity.
- **Required term families and required dimensions** each order by their
  owner-issued identity when absence must choose one failure.

Every text comparison in these orders is by **Unicode scalar value**, which is
also UTF-8 byte order. It is not UTF-16 code-unit order — the default in .NET's
`CompareOrdinal` and in JavaScript — because the two disagree above the Basic
Multilingual Plane, and an intent's order must not depend on which host computed
it.
- **Stages** keep declaration sequence, because the sequence is the question.
- **Order operations** place `base` first, then ranking operations by ascending
  stage index; inside one field-list operation, field terms keep declaration
  sequence, because they compose lexicographically.

## Resolution boundary

Intent resolves exactly once, against exactly one vocabulary, producing either
the owner's executable plan or one structured failure.

- Resolution is **atomic**. The first failure returns no plan and no partial
  binding, and which failure is first is fixed by this contract rather than by a
  host's enumeration order: the vocabulary; then terms in their
  [semantic order](#semantic-order), then missing required term families; then
  bounds in their semantic order, then missing required dimensions; then the
  baseline order operation; then the stages in declaration sequence, each
  ranking stage resolving its own ranking as it is reached — the operation
  bound to it, else the vocabulary's declared default, else *ranking missing*.
  Within one element the checks run existence, then admissibility, then binding,
  then collision — and within collision, vocabulary compatibility and family
  exclusivity before duplication, because a contradiction is never collapsible
  while a duplicate may be. When duplicate collapse is enabled, an equivalent
  normalized binding already present in the same exclusive family is the
  duplicate case rather than a second family member; distinct bindings in that
  family remain incompatible. Compatibility is checked against every earlier
  bound term occurrence, including one whose predicate later collapsed
  idempotently inside its composition context. A term with both an inadmissible
  operator and a value its binder would reject therefore reports the operator,
  and a term that is both incompatible with one earlier term and a
  binder-duplicate of another reports the incompatibility. For an order
  reference the sequence reads: exists, is orderable, has the purpose its role
  requires. Two hosts resolving one intent report the same failure.

  The part sequence and the stage-local ranking rule are
  [row query and ordering](row-query-order.md)'s own, and a row vocabulary
  resolved through this model agrees with the row owner's resolver on every
  bound, baseline, stage, and ranking failure. **Terms are the one deliberate
  divergence.** The row owner validates predicates in the order its caller
  declared them; a portable intent has no declaration order, because term
  membership is set-valued so that two spellings of one query share one identity.
  Terms therefore resolve in semantic order, and with two unknown keys the row
  owner's own resolver reports the one declared first while this model reports
  the one that sorts first. The row owner's rule is not changed — it still
  validates in the order it is given — but a host that lowers its own spelling
  into intent gives up first-typed failure ordering for terms, and each adopting
  host accepts that consequence as part of adoption.
- Resolution **starts no work**. A rejected intent issues no acquisition, no
  source request, and no package payload fetch. This matters more here than for
  row predicates: a package-query term can authorize archive downloads, so a
  malformed restored link must cost nothing.
- **Stages are structurally valid before they reach resolution.** Positive
  counts and ordered inclusive window bounds are
  [semantic row selection](semantic-row-selection.md)'s construction
  preconditions: enforced when a host constructs intent in process, and by the
  codec when a payload is decoded. Violating them is misuse there, not a
  resolution failure here, exactly as the row owner already states. Resolution
  asks a stage one thing only — whether the vocabulary admits **that stage
  kind**. Admission is declared per kind, not all or none: Package Query selects
  final package rows with head, tail, and window, and admits no top, because it
  has no order namespace to rank by; a row vocabulary admits all four. A stage
  of a kind the vocabulary does not declare is refused as not admitted.
- **A ranking stage needs a ranking, and resolves it in place.** A top stage
  takes the operation bound to it, else the vocabulary's declared default
  ranking as the row owner provides, else fails as ranking missing — at the
  stage, when the stage is reached, not after the other parts. Nothing in
  resolution spans parts out of sequence.
- A failure is **presentation-free**: the vocabulary identity, a typed location
  naming the part and the element within it, an owner-issued offending identity
  when the reason has one, and a typed reason. No diagnostic sentence, rendered
  value, localized text, or exception text enters the contract.

The reason union is **closed**. A vocabulary cannot add to it; a new reason is a
change to this contract. Each reason fixes its own location and offender, so two
hosts produce the same failure and not merely the same reason:

| Reason | Located at | Offending identity |
| --- | --- | --- |
| Unknown vocabulary | the vocabulary | none |
| Unknown key | the term | the key |
| Operator not admitted for the key | the term | the operator |
| Value rejected by the key's binder | the term | the key whose binder rejected it |
| Duplicate after binding | the later of the two terms in semantic order | the key |
| Terms incompatible — two bound terms the vocabulary declares incompatible | the later of the two terms in semantic order | its key |
| Required term family missing | the next semantic term position | the family |
| Unknown dimension | the bound | the dimension |
| Maximum outside the dimension's declared range, which may depend on the bound terms | the bound | the dimension |
| Required dimension missing | the next semantic bound position | the dimension |
| Stage not admitted | the stage | the stage kind |
| Unknown order reference | the operation, and the field-term index within a field list | the reference |
| Order reference not orderable | the operation, and the field-term index within a field list | the reference |
| Order not a ranking — a sequence-purpose named order supplied for a ranking role | the operation | the reference |
| Ranking missing — a top stage reached with no bound operation and no declared default | the stage | none |

A reason whose offender column reads *none* carries none rather than an empty or
invented one.

Duplicate-after-binding rests on two model invariants. Term membership is
set-valued, so an identical triple present twice is one term and resolution
never receives an exact duplicate. And a value is an exact token the model never
normalizes: package identifiers, framework names, and assembly names each have
owner-specific equivalence rules, and those rules apply only inside the
vocabulary's binder, never to the intent itself.

What resolution can receive, therefore, is two distinct terms that the binder
maps to one predicate. Whether that collision collapses idempotently or fails is
the vocabulary's decision, declared by that owner. It follows that two intents
differing only in a spelling the vocabulary treats as equal may behave
differently; that is the stated cost of keeping intent identity independent of
vocabulary semantics.

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

## Worked examples

Keys, dimension identities, and named orders below are illustrative. The
vocabulary owner defines the real ones; these show the model, not a vocabulary.
Byte-level examples live with the codec in slice 2
([#6981](https://github.com/richlander/dotnet-inspect/pull/6981)), which adds the
document link here when it lands.

### A package query, and everything it must carry

Someone narrows a package query to packages depending on Serilog, including
prereleases, over the first 200 candidates:

```text
rail      Microsoft.Extensions.*   [depends: Serilog]  [+ prerelease]
CLI       package query "Microsoft.Extensions.*" \
            --where "depends=Serilog" --where "prerelease=include" --take 200
intent    terms  (depends, eq, Serilog), (prefix, eq, Microsoft.Extensions.),
                 (prerelease, eq, include)
          bounds (candidates, 200)
```

Two rules are doing work here.

The source input travels as a term like everything else. Intent has **no
privileged scope slot**: a vocabulary that selects a population expresses that
selection in its own key namespace, and a typed distinction it needs to preserve
— an exact identifier against a literal prefix, say — is carried by using
distinct keys, not by a slot this layer defines. A restored query that lost its
scope would run against a different population while looking like the one that
was shared, so the scope is not optional context around the query; it is part of
the query.

Note also what the term's value is. The host spellings say
`Microsoft.Extensions.*`, but the terminal `*` is
[input-selection](package-query-input-selection.md) grammar marking the text as
a prefix, and the typed prefix it selects is `Microsoft.Extensions.` — which is
what production lowering constructs and what the term carries. An intent records
what the host *meant*, never how it was typed, so two hosts with different
spelling conventions for one typed prefix hold the same intent.

### A link that can no longer be answered

A link shared last year names a key this build does not offer. It is refused,
naming the term:

```text
Cannot restore query: vocabulary "package.query" does not offer key "depends".
```

The alternative — dropping the term and running the rest — would answer a
different question while looking like the shared one, and which direction it
moves depends on where the term sat. `depends` here is a plain conjunct,
belonging to no combining family, so removing it weakens the predicate: silent
dropping would admit matches the sender never asked for and authorize work their
execution bounds never covered. Drop a member of a multi-member OR-family
instead and the family narrows — under `(A OR B) AND C`, losing `A` rejects
items that satisfied only `A` and `C`.

Both directions are equally forbidden, which is why the rule is stated as
failure rather than as a bound on how far the answer may move. A restored query
either asks what was shared or refuses; it never asks something adjacent and
reports completion honestly about a request nobody made.

## Required gates

Named Release gates the implementation must add. Byte-level gates belong to the
successor slice.

| Gate | Contract |
| --- | --- |
| `IntentResolutionIsAtomic` | An invalid vocabulary, key, operator, value, bound, stage, or order reference returns one structured failure with no plan and no partial binding. |
| `FailurePrecedenceIsContractFixed` | An intent carrying several independent defects reports the same failure — reason, location, and offender — regardless of host enumeration or construction order: vocabulary, terms in semantic order, bounds in semantic order, the baseline order, then stages in sequence each with its ranking; existence, admissibility, binding, collision within one element; and vocabulary compatibility and family exclusivity before duplication within collision. A row vocabulary agrees with the row owner's resolver on every bound, baseline, stage, and ranking failure; for terms, the intent's semantic order stands in for the caller's declaration order, and the divergence is witnessed, not hidden. |
| `IntentResolutionStartsNoWork` | A rejected intent issues no acquisition, source request, or payload fetch; gated with a source capability that fails the test if invoked. |
| `UnresolvableTermFailsVisibly` | An intent naming a key, operator, or dimension absent from the current build fails; it is never dropped, defaulted, narrowed, or widened. |
| `IntentCarriesNoResolvedOrPresentationState` | Serialized intent contains no resolved identity, accessor, comparer, label, rendered value, or outcome. |
| `BoundKindsRemainDistinct` | An execution bound never resolves as a selection stage or the reverse, and each retains its owner-issued dimension identity. |
| `IntentFailureShapeIsPresentationFree` | Failures carry only the vocabulary identity, a typed part-and-element location, an optional owner-issued offending identity, and a typed reason; a reason without an offending identity carries none rather than an empty or invented one. |
| `DuplicateAfterBindingIsReachableAndVocabularyOwned` | Two distinct terms that a vocabulary binds to one predicate reach the vocabulary stage and take that owner's declared collapse-or-fail outcome; an exact duplicate never reaches resolution, because membership is set-valued. |
| `StagesCannotFailResolutionExceptByAdmission` | A structurally valid stage reaches resolution and is refused only when the vocabulary does not declare its kind; admission is per kind, so a vocabulary admitting head, tail, and window but not top refuses exactly the top; structural violations are refused at construction or decode and never reach resolution. |
| `RankingStagesResolveOrFail` | A top stage resolves its ranking when reached in stage sequence — the bound operation, else the declared default, else ranking missing at the stage with no offender — so with two top stages the earlier stage's missing ranking is reported before the later stage's unknown reference; a sequence-purpose named order in a ranking role fails as order not a ranking. |
| `ExclusiveFamilyMembersAreRefused` | Two distinct bound terms the vocabulary declares mutually exclusive fail as terms incompatible at the later term in semantic order. When duplicate collapse is enabled, equivalent normalized bindings already present in the same exclusive family collapse; distinct family members remain incompatible, including after a collapsed occurrence. |
| `BoundTermCompatibilityIsVocabularyOwned` | A vocabulary may refuse an owner-defined incompatible pair at the later term in semantic order without changing how accepted terms compose: Package Query's two specific tool formats remain one valid combining-family OR-union, while its broad tool fact beside either format fails as terms incompatible. Every previously bound occurrence participates even when duplicate collapse omits its predicate from the executable plan. |
| `BoundRangeSeesResolvedTerms` | A dimension whose admissible range depends on bound terms — Package Query's candidate cap with a package-content term — is checked with the terms resolved, at the bound, before any acquisition. |
| `RequiredQueryPartsFailVisibly` | A vocabulary-required term family or execution-bound dimension that is absent fails after present elements in that part validate, before later parts, plan creation, or acquisition; multiple missing requirements use scalar identity order, and no default silently changes replay. |
| `FailureReasonUnionIsClosed` | Every failure carries one reason from the table, at that reason's location, with that reason's offender or none; no implementation or vocabulary emits a reason outside it. |

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
- No byte spelling. Canonicalization, the closed payload schema, pinned tokens,
  declared limits, and the packet projection belong to slice 2.
- No change to row-schema resolution, order resolution, or plan execution.
  Intent carries unresolved order references; it does not resolve them, rank
  anything, or define what a missing order means.
- No change to the packet's coordinate arms, outer bounds, hardening, or
  record family, and no new packet format defined here.
- No acquisition, pushdown, pagination, merge, or completion-evidence
  semantics. Intent describes a request, not its cost model or its honesty
  accounting.
- No free-text expression language, and no L3 spelling decisions.
