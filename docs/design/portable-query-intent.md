# Portable query intent

## Status

**Unverified.** This is a design-only contract. No part of it is implemented,
and every gate in [Required gates](#required-gates) is a requirement on the
implementation rather than a property enforced today. Statements about what a
resolver does, admits, or refuses describe the contract an implementation must
satisfy, not observed behavior.

This is **slice 1 of 2** under
[#6971](https://github.com/richlander/dotnet-inspect/issues/6971). It owns the
intent model: what a query intent is, how it resolves, and what replaying one
must guarantee. Slice 2, Portable query payload, owns the single byte spelling
that model has — canonicalization, the closed JSON schema, pinned tokens,
declared limits, and the packet projection — and adds the cross-references
between the two documents when it lands. The model goes first because the
payload has nothing to encode without it, while a host can hold and resolve
intent in process without ever serializing one.

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

Three consumers need this layer and none of them has it:

- **Inspect Web `/query`** stores no request in the URL, so a package query
  cannot be shared, saved, or demonstrated.
- **The share packet** reserves a delegation slot with nothing to fill it.
  [Workspace definitions](workspace-definitions.md) packet format 2 defines its
  `q` table as `[queryId, payload]`, where the payload is "the closed JSON
  object emitted by that query owner's version-2 packet codec." No query owner
  supplies such a codec. Filling that slot is the successor slice's claim; this
  one defines what the slot would carry.
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
| `vocabulary` | Owner-issued identity of the vocabulary the terms resolve against. It is part of an intent's identity; where an encoding carries it is the codec's. |
| `terms` | A canonical set of `(key, operator, value)` triples. Composition follows the vocabulary's declared families. |
| `bounds` | Declared execution bounds, each carrying an owner-issued dimension identity. Unordered. |
| `stages` | The ordered selection-stage pipeline. Position-significant. |
| `order` | Optional unresolved order operations, keyed by role: at most one baseline, plus at most one per ranking stage. |

A **key** is a canonical query key from the named vocabulary's declared key
namespace: a bounded ordinal token. It is not a display label, heading, column
name, or CLI option spelling.

An **operator** is one identity from the closed operator set already used by
row predicates — equality, inequality, and the ordered comparisons. A
vocabulary declares which operators each key admits; intent does not widen that
set, and this design introduces no nesting, grouping, or solver.

**Composition.** Terms conjoin across families. Terms belonging to one
vocabulary-declared **combining family** form an OR-union within that family,
and the union conjoins with everything outside it. Family membership is declared
by the vocabulary, not marked on the term, so the serialized shape stays one
flat set and the codec needs no knowledge of composition. Package Query's
tool-format selection group is the existing instance: `v1` and `v2` are an
OR-union, and production evaluation already accepts the group when any selected
member matches. A universal conjunction rule would silently rewrite that
supported query into one requiring a package to be both formats at once.

Because composition is read from the vocabulary rather than the payload, family
membership is part of that vocabulary's compatibility surface: changing which
keys combine changes what an already-shared link means, and is governed by the
same replay rules as removing a key.
A **value** is an inert value token: bounded text preserved exactly as
supplied, constructed through the existing `InertText` containment shapes. This
layer does not parse, normalize, case-fold, or interpret it. Interpretation
belongs to the vocabulary's binder at resolution.

**Bounds** and **stages** are different kinds and never merge.

An **execution bound** is the `ExecutionBoundIntent(Dimension, RequestedMaximum)`
shape owned by [CLI execution bounds](cli-execution-bounds.md), limiting one
owner-named dimension of upstream work; reaching it produces a completion state
and proves nothing about exhaustion. A dimension carries **at most one** bound:
two maxima for one dimension are not a narrower request, they are a
contradiction. Bounds in different dimensions are independent, so their
declaration sequence carries no meaning.

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

## Resolution boundary

Intent resolves exactly once, against exactly one vocabulary, producing either
the owner's executable plan or one structured failure.

- Resolution is **atomic**. The first failure returns no plan and no partial
  binding, and which failure is first is fixed by the contract rather than by a
  host's enumeration order: parts resolve in the order they appear in the
  contract table — vocabulary, terms, bounds, stages, order — and within a part,
  elements resolve in that part's canonical order. Every part therefore requires
  a total order over its elements; the concrete orders belong to the codec. Two
  hosts resolving one intent report the same failure.
- Resolution **starts no work**. A rejected intent issues no acquisition, no
  source request, and no package payload fetch. This matters more here than for
  row predicates: a package-query term can authorize archive downloads, so a
  malformed restored link must cost nothing.
- A failure is **presentation-free**: the vocabulary identity, a typed location
  naming the part and the element within it, the offending owner-issued identity
  when the reason has one — a key, operator, dimension, or order reference — and
  a typed reason. A reason with no offending identity, such as an unknown
  vocabulary, carries none rather than an empty or invented one. No diagnostic
  sentence, rendered value, localized text, or exception text enters the
  contract.
- The distinguishable reasons are at least: unknown vocabulary, unknown key,
  operator not admitted for that key, value rejected by the key's binder,
  duplicate-after-binding, unknown bound dimension, a bound value outside its
  declared range, and an unknown or inadmissible order reference.

Duplicate-after-binding is a **vocabulary-stage** outcome, not a codec-stage
one. It rests on two rules the codec slice fixes and this one inherits:
canonicalization collapses exactly duplicated terms, and it never normalizes a
value, because package identifiers, framework names, and assembly names each
have owner-specific equivalence rules that would make canonical form depend on a
vocabulary's current semantics.

Resolution therefore never receives an exact duplicate. What it can receive is
two syntactically distinct terms that the vocabulary's binder maps to one
predicate. Whether that collision collapses idempotently or fails is the
vocabulary's decision, declared by that owner. It follows that two links
differing only in a spelling the vocabulary treats as equal may behave
differently; that is the stated cost of keeping share identity independent of
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
| `FailurePrecedenceIsContractFixed` | An intent carrying several independent defects reports the same failure regardless of host enumeration or construction order, following part order and then each part's canonical order. |
| `IntentResolutionStartsNoWork` | A rejected intent issues no acquisition, source request, or payload fetch; gated with a source capability that fails the test if invoked. |
| `UnresolvableTermFailsVisibly` | An intent naming a key, operator, or dimension absent from the current build fails; it is never dropped, defaulted, narrowed, or widened. |
| `IntentCarriesNoResolvedOrPresentationState` | Serialized intent contains no resolved identity, accessor, comparer, label, rendered value, or outcome. |
| `BoundKindsRemainDistinct` | An execution bound never resolves as a selection stage or the reverse, and each retains its owner-issued dimension identity. |
| `IntentFailureShapeIsPresentationFree` | Failures carry only the vocabulary identity, a typed part-and-element location, an optional owner-issued offending identity, and a typed reason; a reason without an offending identity carries none rather than an empty or invented one. |
| `DuplicateAfterBindingIsReachableAndVocabularyOwned` | Two syntactically distinct terms that a vocabulary binds to one predicate reach the vocabulary stage and take that owner's declared collapse-or-fail outcome; no duplicate reaches resolution as canonical bytes. |

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
