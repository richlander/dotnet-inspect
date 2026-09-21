# Query space composition

## Status

Focused cross-cutting design for
[#7712](https://github.com/richlander/dotnet-inspect/issues/7712). This design
establishes **Query Space Composition** as one architectural owner. It records
the target contract agreed before the held Depends and Library Query changes in
[#7944](https://github.com/richlander/dotnet-inspect/pull/7944),
[#7945](https://github.com/richlander/dotnet-inspect/pull/7945), and
[#7872](https://github.com/richlander/dotnet-inspect/pull/7872) continue.

The current implementation has several prerequisites:

- `QuerySpace` carries portable intent, row-query resolution, semantic row
  selection, Query Operation registration, and the immutable
  `QuerySpaceDescriptor`, `QuerySpaceRequest`, and explicit row-intent
  association contracts;
- [Query Operation Infrastructure](query-operation-infrastructure.md) derives
  effective route capabilities from executable operation registration;
- [L2 row query and ordering](row-query-order.md) resolves typed row predicates
  and orders;
- [Section-row shaping](section-row-shaping.md) binds declared row sets,
  projection, and terminal Count; and
- [Source delegation](source-delegation.md) defines exact substitution of
  source work through completion evidence.

Those owners remain authoritative for their own semantics. This design owns
only their reusable composition into one discoverable query space.

The Query Operation and direct-consumer Release gates verify that descriptors
project operation terms and work-bound dimensions from executable routes,
project order and semantic-stage capabilities only through explicit row
scopes, reject duplicate canonical keys, and that requests preserve explicit
compatible row-set associations while separating operation bounds from row
order and semantic selection. Transitional Query Operation route order and
stage capabilities are not operation-scope capabilities. Section projection,
typed row-plan resolution, terminal execution, continuation binding, and the
complete gates in [Required gates](#required-gates) remain **unverified** until
their named implementation slices land and run in Release.

## Owner and exact claim

**Query Space Composition** owns this exact claim:

> One query-capable route exposes one closed, host-neutral query space that
> composes one operation query scope, one or more row-query scopes over
> declared row sets, and one terminal requirement without merging their
> semantic ownership. The same executable registrations produce its capability
> descriptor, structural request lowering, owner-issued plans, host lowering,
> source-delegation input, and optional generated consumer API.

This owner defines:

- the parts that form one query space;
- the host-neutral request shape that associates scoped portable intents with
  operation and row bindings;
- the distinction between reusable facet definitions and their operation or
  row bindings;
- the closed query-language boundary shared by hosts;
- the requirement that resolved plans preserve inspectable structural meaning
  beside executable machinery;
- preservation of the owner-issued Rows and exact Count terminal branch;
- the distinction among semantic selection, work bounds, source continuation,
  delivery demand, and rendering windows;
- preservation of an adjacent source owner's continuation without interpreting
  or manufacturing it;
- the host-neutral capability descriptor; and
- the declaration boundary from which optional source generation may produce
  mechanical wiring.

This owner does not define:

- any Package, Library, Type, Member, Dependency, Graph, or Find semantics;
- subject or source authority, acquisition, pagination, retry, caching, or
  completion-evidence construction;
- row predicate, order, Head, Tail, Window, Top, projection, or Count
  semantics;
- portable payload bytes or compatibility policy;
- CLI grammar, Browser interaction, Markout rendering, LINQ use, or `jq`
  programs;
- `explain` resource paths, structural-discovery documents, value-vocabulary
  contents, or envelope wire-contract registration;
- a universal executable plan shared by operation owners; or
- the physical package, namespace, dependency, lifetime, or extension boundary
  owned by [QuerySpace Library Boundary](query-space-library.md).

The `QuerySpace` library now carries the portable, row, Query Operation, and
initial structural-composition contracts without becoming their semantic
owner.

## Product goal

The query language is one part of a larger composition:

```text
dotnet-inspect query language
  + source delegation
  + agent intelligence
  + jq, LINQ, and similar downstream tools
  + section shaping and Markout presentation
```

The query language therefore does not attempt to become T-SQL, a general
expression language, or a replacement for `jq`. It obtains high capability by
composing accurate typed documents with specialized sources and capable
consumers.

The product favors an accurate, sufficiently structured document over the
smallest document an arbitrary expression could produce. Operations belong in
the shared query space when they affect acquisition, source delegation,
completion, evidence, stable ordering, or an important cross-host experience.
Ad hoc grouping, calculated fields, arbitrary Boolean reshaping, and
application-specific projections remain natural downstream work.

## Production experience

The target interaction remains one consistent tuple of facet, operator, and
operand:

```console
dotnet-inspect package query 'Microsoft.Extensions.*' \
  --where "depends starts-with Microsoft.Extensions."

dotnet-inspect library query ./artifacts \
  --where "references=System.Runtime"

dotnet-inspect library ./artifacts/example.dll \
  -S References \
  --where "Name contains Http"
```

The spellings are illustrative until their CLI owner adopts them. They show the
shared semantics:

- `depends` and `references` are facets, not operator-specific keys;
- `starts-with` and `contains` are explicit operators rather than wildcard
  conventions embedded in equality;
- operation qualification and row filtering remain distinct even when a host
  presents both through one `--where` gesture; and
- command and section experiences consume the same effective query-space
  descriptor rather than maintaining parallel capability inventories.

That compact host gesture is not one universal portable intent. Host lowering
uses the descriptor to route each canonical term to its operation or row query
scope and constructs the explicit row-intent associations required by the
selected row sets. Ambiguous stage or order targeting fails before execution
rather than applying one global pipeline by convention.

Another .NET application may instead obtain the descriptor, construct the same
typed intent directly, execute it through the same plans, and apply LINQ to the
returned typed rows. A Browser application may construct controls from the
descriptor and use a source continuation to request more data. Neither host
needs to parse CLI text.

## Conventional basis

This design deliberately derives individual mechanisms from established
systems:

| Precedent | Adopted idea | Deliberate difference |
| --- | --- | --- |
| LINQ query providers | Query composition is separate from provider execution. | The portable and resolved structural plans are inspectable; source capability and completion are not hidden behind provider type tests. |
| GraphQL schema and introspection | One executable schema drives validation, discovery, and generated clients. | Query Space composes operation and row stages instead of exposing one graph field language. |
| OData query options | Filtering, ordering, selection, and Count are distinct operations, with explicit text operations such as starts-with and contains. | HTTP syntax and provider pushdown do not define product semantics. |
| Substrait and relational planners | Stable logical operations remain separate from physical execution. | Query Space is a small product algebra rather than a general relational plan. |
| NLinq | Source-specific static execution and fold lowering avoid opaque runtime type tests. | Runtime-authored queries remain structural data rather than nested generic pipeline types. |

The design is intentionally derivative by layer. No precedent is authoritative
for dotnet-inspect's work authorization, evidence, completion, or source
continuation contracts.

## Query-space definition

One query space is an immutable effective binding containing:

| Part | Meaning |
| --- | --- |
| Identity | Stable owner-issued identity for discovery and portable intent resolution. |
| Operation scope | One Query Operation definition, operation-only query-vocabulary identity, subject role, result grain, and operation profile. |
| Row-query scopes | One or more stable scope identities, each pairing one row query vocabulary with the compatible declared row-set identities and shaping capabilities to which an instance of that intent may apply. |
| Request shape | One operation intent plus optional section-owned projection intent, zero or more ordered row-intent associations, a non-empty participating row-set selection, and one terminal requirement. |
| Terminal space | The supported terminal requirements, initially Rows and exact Count, preserving each participating row-set identity. |
| Effects | The capability, acquisition, work, and completion consequences reachable through the effective bindings. |
| Continuation acceptance | Whether this result composition can preserve an adjacent source contract's continuation; the selected source offer supplies any effective continuation capability. |
| Result-contract references | Optional owner-issued mapping from a terminal/result shape to the output contract it produces; schema, Content Kind, and serialization remain with the output owner. |
| Descriptor | The complete resource-free, serializable capability projection consumed by hosts and generators without execution or reflection. |

The query space composes these parts without replacing their plans. A successful
resolution retains:

```text
query-space request
  -> operation PortableQueryIntent -> operation-owned executable plan
  -> section-owned projection intent and participating row-set selection
  -> ordered row-intent associations:
     -> row-query-scope identity
     -> one PortableQueryIntent
     -> non-empty ordered compatible row-set identities
  -> Section-row shaping plans and cohorts
  -> terminal requirement and optional result-contract reference
```

There is no universal executable query-plan type. The query-space binding
retains the association among the owner-issued plans and the stage at which
each one executes.

A terminal resolution selects a non-empty list of participating declared row
sets. An operation route with no declared row set may still use Query Operation
Infrastructure, but it does not form a Rows-or-Count query space under this
owner and cannot advertise those terminal capabilities. Composition never
invents an implicit operation-result row set or an undefined zero-entry Count.

Each participating row set is assigned to exactly one row-intent association.
One association may target multiple sets only when they use the named row query
scope and all admit the same row intent; independently shaped or heterogeneous
sets use separate associations. Unknown, repeated, unassigned, or incompatible
row-set associations fail atomically before execution.

When a request supplies no row-intent associations, Query Space preserves
Section-row shaping's default association across all participating sets. A
request with explicit row terms, order, or selection supplies explicit
associations. One `PortableQueryIntent` always resolves once against its named
operation or row query vocabulary; it never spans several vocabularies.

Host lowering is deterministic:

1. operation terms and bounds enter the operation intent;
2. each row term enters the row intent for the unique row query scope named by
   its canonical key;
3. each row intent is associated with its explicit non-empty ordered target
   set; and
4. row order and selection enter that association rather than a global slot.

A host may offer one shorthand that applies the same row intent to several
compatible selected sets, but it constructs one explicit association naming
those sets. If the host cannot determine one target scope for an unqualified
order or selection gesture, lowering fails before plan resolution. Query Space
does not guess from schema, position, section name, or display text.
The operation intent's order and semantic-stage collections are required to be
empty and are rejected before either operation or row-plan resolution. Order
and Head, Tail, Window, or Top execute exactly once through a row-intent
association after its row predicates. A row intent's execution-bound collection
is likewise required to be empty and rejected before resolution; source or
candidate work authorization exists only in the operation intent.

## Facet definitions and bindings

A reusable **facet definition** declares:

- one stable identity and one canonical query key;
- one typed value domain;
- an optional owner-issued value-vocabulary identity;
- the admitted operator identities;
- cardinality and repeated-term composition;
- display label, summary, query-local constraints, and examples; and
- compatibility identity for portable replay.

A facet definition does not make the facet executable. It becomes available
only through one of two bindings. Within one effective query space, the
canonical query-key namespace is unique across all executable bindings: one key
routes to exactly one operation or row query scope.
Handwritten and generated registration fail before capability discovery or
host construction when two active bindings claim the same canonical key.
Portable intent therefore remains a `(key, operator, value)` term and does not
acquire a stage or row-space discriminator.

An **operation facet binding** declares:

- subject-role and result-grain applicability;
- one owner-issued operation-plan binder;
- capability, acquisition, work, and completion effects; and
- either subject qualification or operation selection as its semantic role.

A **row facet binding** declares:

- one row-query-scope identity;
- one typed accessor and operand binder;
- predicate capabilities;
- optional sequence or ranking order capabilities; and
- no authority to initiate source or acquisition work.

The same facet definition may support different bindings in different
effective query spaces when its value domain and operator meanings remain
equivalent. The bindings may still differ in quantification, evidence,
completion, and failure behavior. For example,
`references starts-with System.` may
mean that a Package candidate has at least one matching reference across its
admitted Libraries, while another route's References row space evaluates each
declared reference row.

Display labels may coincide across operation and row facets, but bindings
exposed together use distinct canonical query keys. A displayed field or
section name never creates a facet or supplies a portable lookup key.

An external value-vocabulary reference points to the stable legal-value domain
owned by the `vocabulary` subsystem. The query-space descriptor does not copy
that domain's complete value catalog. Open-ended facets expose their typed
domain, constraints, and examples without claiming an enumerated value
vocabulary.

### Query-vocabulary and identity composition

In this design, **query vocabulary** means the keys, operators, families,
bounds, stages, and orders accepted by one `PortableQueryIntent`.
**Value vocabulary** means an independently owned stable legal-value domain
such as the existing `VocabularyDocument`. Unqualified *vocabulary* is avoided
when the distinction matters.

Query Space does not merge owner vocabularies into one universal
`PortableQueryVocabulary`. The operation scope and each row query scope retain
their own stable query-vocabulary identity, family namespace, predicate
identity namespace, and admitted intent components. The effective operation
query vocabulary admits operation terms and execution bounds but no order or
semantic selection stages. A row query vocabulary may admit row terms, order,
and semantic stages but declares no execution-bound dimensions. Each portable
intent resolves exactly once inside that scope.

Canonical term keys are unique across the complete query space because the
compact host term has no scope discriminator. That uniqueness lets a host route
a term to one scope without changing portable term syntax. Bounds, named
orders, family identities, and bound predicate identities remain local to the
specific operation or row intent that carries or resolves them.

Distinct keys in one query-vocabulary scope therefore retain an owner-declared
combining, exclusive, or required family and may retain an owner-declared
common bound predicate identity. The same owner-local family or predicate text
in an operation scope and a row scope is unrelated because the two intents
resolve against different query vocabularies. A family never spans scopes
implicitly. A future cross-scope family would require an explicit
composition-owned identity and semantics. The initial algebra contains no such
family.

For global inspection, the descriptor identifies any scope-local declaration
structurally, such as `(query-space identity, query-scope identity, local
identity)`. That composite identity is not user query syntax and is not
substituted for the owner-local text inside portable intent.

The current Query Operation implementation's `ResultPredicate` term role is
transitional. Result predicates belong to explicit row-space bindings composed
beside the operation route. Removing that role is a focused Query Operation
Infrastructure adoption of this pattern, not an incidental rename.

## Closed query algebra

The shared algebra is deliberately smaller than SQL, LINQ expressions, or
`jq`.

The initial canonical predicate operators are:

- Equals;
- NotEquals;
- StartsWith;
- NotStartsWith;
- Contains;
- NotContains;
- AtLeast; and
- AtMost.

Portable identity texts and host spellings remain owned by their respective
contracts. `=` and `!=` are appropriate CLI spellings for equality and
inequality. StartsWith and Contains remain named because no terse shell-safe
symbol has sufficiently consistent meaning.

Comparison behavior belongs to the facet's typed value domain. The generic
substrate does not impose one text comparison, normalization, culture, or case
policy.

Terms retain the portable-intent owner's flat composition:

- terms conjoin across families;
- one owner-declared combining family may form an OR-union;
- one owner-declared exclusive family rejects conflicting members; and
- compatibility rules may reject combinations.

The language adds no nested groups, arbitrary Boolean expression tree,
subquery, join, lambda, user-defined function, regex, glob convention, custom
comparer, or general aggregate. A future operator is a versioned addition to
the closed algebra, not arbitrary executable content carried in an intent.

## Fixed stages

Query Space preserves the stage order issued by the operation, row, selection,
and section-row owners. For each participating section-shaped row set, the
observable order is:

```text
subject or population binding
  -> operation qualification or selection
  -> declared result rows
  -> membership projection
  -> row predicates
  -> effective baseline order
  -> semantic Head, Tail, Window, or Top
  -> one terminal branch:
     -> selected rows -> cell projection -> Rows
     -> selected rows -> exact Count
```

This is the [Section-row shaping](section-row-shaping.md#reference-composition)
branch, not a projection contract defined by Query Space. A row space without
projection support treats the two projection positions as absent. Count still
validates applicable cell-projection intent through the section-row owner but
does not execute cell projection. The completed per-set results retain their
declared row-set identities and are assembled in declaration order.

An owner may perform equivalent work earlier only through source delegation or
another owner-approved optimization contract. The structural plan continues
to record the operation in its semantic position.

The fixed order intentionally rules out a general optimizer that arbitrarily
reorders stages. It makes plan meaning, failure precedence, source
substitution, and generated execution predictable.

## Structural transparency

Every resolved operation or row term retains inspectable structural meaning:

- facet and binding identity;
- operator identity;
- normalized typed operand;
- semantic stage and declared row set;
- order and cardinality properties needed by composition;
- effects and completion consequences; and
- the owner-issued executable binding used for local execution.

An executable delegate, accessor, comparer, or callback may be cached beside
that structure, but it is never the sole representation. Source delegation,
discovery, diagnostics, and generated consumers must not recover meaning by
inspecting a delegate target or testing a source's concrete CLR type.

A source advertises an explicit offer over structural operations. The offer
states the operations and compositions it can execute, the order it preserves,
the result shapes it can return, and the completion evidence it can construct.
The delegation planner intersects that offer with the resolved structural plan.
It does not infer capability because a source happens to implement a
similarly-named method.

This is the query-space equivalent of NLinq's self-typed source specialization:
the source-specific path is selected through an explicit contract rather than
an `if (source is SomeConcreteType)` branch. Unlike NLinq, the complete
runtime-authored query shape is not encoded as nested generic types.

## Discovery and explanation seam

The capability descriptor and a resolved structural plan are distinct
artifacts.

The descriptor can be enumerated and serialized without execution,
acquisition, reflection, or live resources. It exposes stable identities and
typed relationships for the query space, operation and row query scopes,
eligible row sets, facets, bindings, operators, terminals, effects, and
continuation acceptance. A facet may reference an external value-vocabulary
identity, and a terminal/result shape may reference an external result-contract
identity. Those references remain opaque owner-issued identities; Query Space
does not copy value catalogs or define output schemas. The descriptor
representation remains dependency-light, NativeAOT-compatible, and usable in
single-threaded Browser/Wasm.

A resolved plan adds the normalized operands, selected row-intent
associations, semantic stages, effects, terminal requirement, and
source-delegation boundary for one request. It remains inspectable without
executing that request.

`-Q` may be a compact projection of the descriptor, and
[Resource Explanation](resource-explanation.md) may compose it with structural
discovery, value vocabulary, and output-contract catalogs. Those consumers
must follow typed links rather than copying query metadata into
`DiscoveryDocument`, parsing labels, or inferring semantics from rendered
companion sections. Query Space does not own the `explain` command,
resource-path grammar, discovery document, envelope registration, Content
Kind, or schema generation.

## Terminal requirements

Rows and exact Count are peer terminal requirements over each participating
row set's selected sequence after membership projection, predicates, effective
order, and semantic selection.

**Rows** then applies any validated cell projection and returns the selected
typed rows plus their row-set identity, source, and completion outcomes. The
assembled result preserves participating declaration order. A source-bound or
otherwise incomplete result may remain usable when the owning row contract
permits it, but it stays visibly incomplete.

Rows preserves independent source outcomes. A usable or incomplete set may
contribute shaped rows while a failed, `Absent`, or Rows-unavailable companion
remains a disposition-and-evidence-only outcome; the unavailable set neither
disappears nor suppresses healthy companion rows. Once residual row-query or
semantic execution begins, publication is atomic: a later cohort failure
publishes no earlier Row-outcomes.

**Count** validates cell-projection intent but does not execute it, because the
terminal result has no row cells. A successful result contains one exact
cardinality, including zero, for every participating selected row set in
declaration order. It does not invent an aggregate across independently
declared sets; an aggregate exists only when the producer declared one
aggregate row set before shaping. A typed non-count outcome remains visible,
and an observed row count is never returned as though it were exact. Count may
be satisfied for a participating set:

- by logical exhaustion after local or delegated execution;
- by an owner-accepted exact source Count witness; or
- after `Head(N)`, by witnessing N applicable ordered rows or exhausting the
  population with fewer than N.

Count publication is all-or-failure across the participating sets. A failed,
`Absent`, or Count-insufficient source outcome prevents every Count entry,
preserves every participating set's disposition and completion evidence in one
typed source failure, carries no row values or Count payload, and invokes no
residual row-query or semantic execution. If residual execution begins, a
later row-query or semantic failure likewise publishes no partial Count.
Already-reached owner-defined observations remain governed by the section-row
failure-precedence contract.

Count remains first class because it tests whether every layer preserves
semantic scope and completion. A provider's candidate count, total-hit field,
page size, work bound, or observed match count is not automatically the final
Count.

A future combined preview-and-count shape would carry two independent
requirements: bounded row delivery and exact Count over each participating
set's complete semantic population. Exact Count would not imply random row
access, and row delivery credit would not limit Count work explicitly requested
by the consumer.

## Work bounds and semantic selection

An execution bound and a semantic selection remain separate:

| Concept | Meaning |
| --- | --- |
| Candidate `take` | The maximum owner-dimensioned source or candidate work authorized for one execution. |
| Semantic `Head(N)` | The final result contains at most the first N applicable ordered rows. |
| Source continuation | The source can identify an unconsumed remainder of the ordered source population. |

A lone `Head(N)` may supply an execution optimization when the operation proves
that stopping after N applicable rows is equivalent. An explicit candidate
bound may intentionally authorize a larger population. Neither rule makes
candidate work and result cardinality interchangeable.

## Continuation, not paging

The query language defines no Page, PageSize, page number, or provider offset
operation. Paging is a higher-level policy that composes ordinary query
executions.

One execution begins either at its source population's start or from one
owner-issued source continuation. Its result reports independently:

- whether the requested semantic result is satisfied;
- whether the underlying source population is exhausted;
- whether execution stopped at a work or provider bound; and
- whether another compatible execution can resume from a continuation.

Semantic completion and population exhaustion are orthogonal. A
`Head(10)` result may be semantically complete while carrying a continuation
for later source rows. An unbounded Count result carrying only a continuation
is not exact.

The query-space layer preserves a continuation as an opaque typed receipt. It
does not interpret the payload, construct a provider token, advance the token,
or treat its presence as completion evidence. The source owner defines:

- source and ordered-population correspondence;
- binding to the source-side plan;
- snapshot, operation, or live consistency;
- portability and expiration;
- advancement and replay behavior;
- containment of untrusted provider data; and
- which authorization must be supplied again when resuming.

A continuation carries no credential or reusable source authority. A consumer
resuming it supplies current authority through the normal source-selection
path.

A continuation is not a portable query term, execution bound, selection stage,
or order operation. It is a source input beside the same canonical query-space
request, including its operation intent and row-intent associations. A
higher-level consumer resumes by composing that unchanged compatible request,
the continuation, and a new execution bound or terminal requirement. Portable
sharing starts from the source population's beginning unless a separate
source-owned interchange contract explicitly admits the continuation.

The useful consistency classes are:

| Class | Meaning |
| --- | --- |
| Snapshot-stable | Every continuation resumes one immutable ordered population. |
| Operation-stable | Exact while the originating live operation remains active. |
| Live | Resumes against a population that may have changed between requests. |
| Non-resumable | The source may continue internally but exposes no usable continuation. |

NuGet Search `skip` and `take` can support bounded sequential acquisition, but
they do not establish a snapshot-stable result population. NuGet Catalog
cursors and horizons have different semantics and describe a change feed, not
the same ranked Search population.

[Source delegation](source-delegation.md) currently excludes cursors from its
result branches. A separate focused adoption must replace that prohibition
with the owner-issued continuation receipt before a delegated result exposes
one. This design does not make that source-owner change implicitly.

## Delivery demand and source batches

Delivery demand is an execution-control protocol, not portable query meaning.
A higher-level consumer may grant cumulative room for final rows:

```text
initial row credit: 20
additional credit:  10
additional credit:  10
```

The [engine-to-Browser async event stream](engine-browser-async-event-stream.md)
owner defines credit accounting, pull-ahead, pausing, cancellation, and event
publication for a live stream. Query Space owns only that delivery demand is
not query semantics and therefore cannot become a portable term, completion
witness, or Count bound. A higher-level system may alternatively settle one
segment, retain its continuation, and start another execution later.

Source acquisition owners define physical batch size and advancement. Ten
additional final rows may require many source pages when residual predicates
reject candidates. A physical page size never becomes semantic Head, Count
evidence, or delivery demand.

Inspect Web's current Package Query behavior is the first production evidence
for this separation: it grants 20 initial matches and 10 more near the end,
retains all delivered rows, and mounts at most 30 cards. Those numbers and
scroll policy remain Browser-owned. The shared query space exposes the
structural plan and continuation capability that let such a policy reach the
source without becoming query syntax.

## Downstream shaping

The completed `InspectionEnvelope<TContent>` remains the authoritative typed
result. A source segment that is not population-exhaustive must retain its
completion and continuation state; it must not masquerade as a complete
document.

Downstream consumers may reshape accurate content:

- .NET applications may use LINQ over acquired typed rows;
- agents may issue additional focused queries;
- `jq` and similar tools may transform structured output;
- the section system may select declared rows, projections, summaries, and
  terminal Count; and
- Markout may lower those typed shapes into compact presentation.

A section-owned semantic reduction known before execution should lower into
the shared row and terminal intent so source delegation can observe it.
Markout-only elision performed after the envelope is complete remains
presentation and cannot reduce source work.

The substrate must not return a lazy `IEnumerable<T>` whose enumeration hides
live source resources, deferred failures, or continuation advancement.
Convenience enumeration may repeatedly execute explicit continuation-bearing
segments, but each source operation and result remains visible.

## Source-generation seam

Source generation is optional mechanical lowering from explicit declarations.
The handwritten declarations remain authoritative. The
[QuerySpace library boundary](query-space-library.md) owns the separation
between the Roslyn-free runtime package and any generated execution witnesses.

A generator may produce:

- stable identity constants;
- immutable registration and descriptor tables;
- typed intent builders;
- closed query-vocabulary-specific predicate and comparer dispatch;
- source-offer matching tables;
- portable serialization metadata; and
- diagnostics for duplicate identities, incompatible operator domains,
  ambiguous stage bindings, or row facets carrying acquisition effects.

A generator must not infer:

- facets from row properties, section columns, or display labels;
- operator meaning from CLR types or method names;
- source capability from the presence of a compatible-looking method;
- work, evidence, completion, or continuation semantics;
- arbitrary expression execution; or
- presentation layout.

Generated and handwritten registrations must produce equivalent descriptors,
structural plans, failures, and execution behavior.

The first implementation should remain explicit through at least Package
Query, Library Query, and one declared References row-space adoption. The
generator follows only after those consumers expose stable repeated
boilerplate. It specializes by query space, query-vocabulary scope, row schema,
and source adapter, not by every runtime sequence of terms; full per-query
generic specialization would create unacceptable NativeAOT code-size growth
and cannot represent runtime-authored query shapes.

## Pathological cases

The eventual implementation and adopter gates must preserve these cases:

- A NuGet source page contains 100 candidates but only two survive the query.
  A ten-row delivery grant continues acquisition rather than treating the page
  size or short result as completion.
- `Head(10)` finds ten rows and returns a continuation. The semantic query is
  complete, while the underlying population is explicitly not exhausted.
- Unbounded Count reaches a candidate limit with 327 observed matches and a
  continuation. No exact Count is returned.
- A source provides an exact filtered Count and only the first 20 rows. Count
  is exact, row delivery is partial, and seekability is not inferred.
- NuGet Search reports `totalHits` for its broader ranked query. Exact-prefix
  filtering and package-ID deduplication prevent that field from satisfying
  final Count.
- An operation facet and a row facet use the same displayed word. They expose
  distinct canonical query keys, and portable round-trip resolution preserves
  each binding's declared stage without guessing from display spelling.
- An operation binding and a row binding both declare an owner-local family
  named `population`. Their scope-local query vocabularies neither combine,
  conflict, nor satisfy one another's required-family rule.
- One operation scope declares distinct `package` and `prefix` keys in its
  required exclusive `population` family. Qualification preserves their shared
  family, so either key satisfies the requirement and using both still fails.
- Two compatible row sets share one row query scope and one explicit row-intent
  association, so the same predicates, order, and selection apply independently
  to both. A heterogeneous companion set uses a separate association and cannot
  consume the first scope's stages or orders.
- A host supplies an unqualified row order while two incompatible row query
  scopes are active. Lowering fails before execution rather than assigning the
  order by declaration position or applying it globally.
- A direct .NET caller supplies `Head(1)` in the operation intent and a row
  predicate in a row association. Construction rejects the operation stage
  rather than selecting before the predicate or executing selection twice.
- A direct .NET caller supplies a candidate `take` bound in a row intent.
  Construction rejects the row bound rather than ignoring it or authorizing
  source work from residual shaping.
- Two participating row sets contain three and five selected rows. Count
  returns ordered entries `(first, 3)` and `(second, 5)` rather than an invented
  total of eight.
- Two participating row sets are requested for Count, but one is
  Count-insufficient. The result preserves both source dispositions and
  completion evidence, executes no residual shaping, and publishes no Count
  entries for either set.
- A route declares no row sets. It may expose its operation capabilities but
  cannot form a terminal query space or advertise Rows or Count.
- A Rows request receives one incomplete-but-usable set and one failed set. It
  preserves the first set's shaped rows and incompleteness plus the second
  set's disposition and evidence. If a later residual cohort instead fails,
  neither cohort publishes Row-outcomes.
- A generated consumer omits a control. The runtime descriptor remains
  complete and another host can expose the capability.
- A source advertises starts-with filtering but cannot preserve the required
  comparison or order. Its adoption fails equivalence rather than silently
  returning plausible rows.
- A consumer changes the query while retaining an old continuation. The
  source-owner binding rejects incompatible resumption rather than skipping an
  unknown portion of the new population.

## Adoption sequence

Implementation proceeds as focused owner adoptions:

1. Lock this composition contract and its owner map.
2. Extend Portable Query Intent with the explicit StartsWith, NotStartsWith,
   Contains, and NotContains identities.
3. Lock the QuerySpace library boundary, establish the product-neutral project,
   and migrate the reusable contracts without changing their semantic owners.
4. Have Query Operation Infrastructure introduce the host-neutral query-space
   request, compose the operation intent with ordered row-intent associations,
   project an operation-only query vocabulary, move existing operation-level
   result predicates, order, and selection into result row-query scopes, and
   retire its transitional `ResultPredicate` role.
5. Preserve structural predicate and order nodes through row-query resolution
   beside local executable bindings.
6. Extend Source Delegation through a separate focused continuation-receipt
   design and implementation.
7. Rework #7944 onto the shared `depends starts-with VALUE` operator model.
8. Use #7945 as the focused Library Query operation adoption, then restack the
   Browser work from #7872 onto its settled query space.
9. Add the References row space and Assembly Reference Prefixes summary over
   owner-issued assembly-reference evidence.
10. Rebase Inspect Web result demand on the shared execution/continuation
   boundary without moving scroll or virtualization policy into the substrate;
   migrate `-Q` to the descriptor while leaving future `explain` adoption with
   its owning effort.
11. Add one small non-CLI .NET consumer, including descriptor enumeration and
    owner-issued value-vocabulary and result-contract links, before enabling
    supported package publication.
12. Evaluate source generation after the first three explicit query-space
    adopters establish repeated boilerplate.

Each step names one adopting owner and retains every other owner's contract.

## Required gates

The implementation plan assigns these Release gates to their eventual owning
slices:

| Gate | Required property |
| --- | --- |
| `QuerySpaceDescriptorMatchesExecutableBindings` | Discovery, host construction, and executable resolution derive from the same effective operation and row bindings. |
| `QuerySpaceDescriptorRoundTripsWithoutResources` | The descriptor is enumerable and serializable without execution, acquisition, reflection, or live resources; round-trip preserves scope, facet, value-vocabulary, row-set, terminal, effect, continuation-capability, and result-contract relationships. |
| `QuerySpaceRequestLowersToExplicitRowAssociations` | One operation intent and zero or more ordered row-intent associations lower deterministically; the operation intent rejects order and semantic stages, row intents reject execution bounds, every participating set is assigned exactly once, one shared association targets only compatible sets, heterogeneous or independently shaped sets remain separate, ambiguous unqualified order or selection fails before execution, and selection executes exactly once after row predicates. |
| `EffectiveQuerySpaceIdentitiesRemainScoped` | Handwritten and generated registration reject duplicate canonical term keys across the query space; each portable intent resolves inside one operation or row query vocabulary; same-named owner-local families and predicates remain isolated across scopes, while distinct keys within one scope preserve their shared combining, exclusive, required-family, and duplicate-binding behavior. |
| `OperationAndRowFacetStagesRemainDistinct` | An operation facet may authorize work; a row facet cannot, and identical display spelling never changes the bound stage. |
| `QuerySpacePreservesSectionRowBranch` | The composed plan reuses `SelectedRowSetListIsNonEmpty`, `MembershipProjectionPrecedesRowQuery`, `CellProjectionFollowsSelectionAndPreservesCardinality`, `RowsPreserveIndependentSourceOutcomes`, `IncompleteRowsRemainVisibleWithoutBecomingCount`, `CrossCohortRowsAreAtomicOnExecutionFailure`, `CountObservesPrecedingSemanticStages`, `CountPreservesDeclaredRowSetScope`, `CountFailurePrecedenceIsDeterministic`, and `CountSourceFailureBindingPreservesOutcomes`; terminal resolution requires a participating row set, Rows preserves independent source evidence but publishes no partial execution result, and Count preserves its owner-issued success and all-or-failure branches. |
| `ResolvedRowPlanRetainsStructuralMeaning` | Every executable predicate and order remains associated with its facet, operator, normalized operand, row set, and semantic stage. |
| `ClosedOperatorAlgebraRejectsExecutableContent` | Portable resolution rejects unknown operators and carries no delegate, expression tree, regex program, or host callback. |
| `SemanticHeadAndCandidateTakeRemainDistinct` | Candidate work and final-row cardinality coincide only through an explicitly proven optimization. |
| `ContinuationDoesNotImplyCompletion` | A continuation may accompany semantic completion, while its presence alone never establishes exhaustion or exact Count. |
| `ExactCountRequiresAcceptedEvidence` | Source Count, exhaustion, and `Head(N)` witnesses are accepted only under the terminal owner's exact requirement. |
| `SourcePageSizeDoesNotDefineResultMeaning` | Different physical source batch sizes produce the same rows, Count, completion, and continuation semantics. |
| `ManualAndGeneratedQuerySpacesAreEquivalent` | When generation is introduced, generated and handwritten paths produce the same descriptors, plans, failures, and results. |
| `ExternalConsumerBuildsFromQuerySpaceDescriptor` | A non-CLI consumer enumerates the descriptor, follows its optional value-vocabulary and result-contract links, constructs scoped intents and row associations, and interprets the terminal and continuation without CLI types or reflection. |

## Non-claims

This design does not claim:

- that every query is source-delegable;
- that every source exposes a continuation;
- that continuation implies stable random access;
- that exact Count implies row seekability;
- that downstream LINQ or `jq` transformations can be pushed upstream;
- that a dynamic runtime query can receive NLinq-style full generic
  specialization;
- that the current Browser match-credit protocol already implements this
  general contract;
- that the current project layout is the final package layout; or
- that the held Depends and Library Query PRs are correct without focused
  adoption and renewed review.
