# Query Space producer capabilities

## Status

Focused design for
[#9161](https://github.com/richlander/dotnet-inspect/issues/9161).
It defines the reusable contract through which Query Space communicates a
complete structural requirement set to one producer and the producer chooses
one plan that satisfies those requirements.

The first production adoption is the replacement Package Tree stack following
[#9056](https://github.com/richlander/dotnet-inspect/pull/9056). That adoption
uses the same host-neutral requirement set for CLI and Browser/Wasm and retires
the eager Package-child path it replaces. The existing PR remains the exact
behavioral and NativeAOT evidence source until the replacement is published.

This contract is unimplemented. Every property in
[Required evidence](#required-evidence) remains **unverified** until its named
implementation or adoption gate lands in Release.

## Owner and exact claim

**Query Space Producer Capabilities** owns this exact claim:

> Given the complete known requirements for one owner-issued resource, Query
> Space carries one immutable structural demand to that resource's producer.
> The producer chooses one resource-free provision plan whose owner-certified
> provisions satisfy every requirement directly or from a covering
> realization. Query Space preserves each caller association, accepted
> completion requirement, satisfaction path, and typed outcome without
> prescribing the producer's algorithm.

This owner defines:

- the distinction among a consumer requirement, a producer provision, an
  owner-certified covering relationship, and a chosen provision plan;
- immutable grouping of requirements by owner-issued resource identity;
- structural scopes that distinguish a population, its selected rows, and
  capabilities requested for those rows;
- whole-set validation before producer work begins;
- the typed seam through which a producer receives the complete requirement
  set and returns its chosen plan;
- generic plan validation that every requirement is satisfied exactly once;
- preservation of direct and covering satisfaction paths in detached results;
  and
- the rule that provision planning reads no subject content and retains no live
  resource.

This owner does not define:

- Package, Library, Type, Member, Metadata, Analysis, Graph, or Research facts;
- which capabilities one producer offers or what any capability means;
- a universal ordering such as `Count < Name < Signature`;
- one cross-producer cost unit, optimizer, cache, or prepared index;
- owner-specific construction, projection, coverage, or completion evidence;
- row predicates, order, semantic selection, Count, Exists, or Rows semantics;
- source delegation, acquisition, continuation, retry, or resource lifetime;
- Producer Planning declarations, dependencies, visits, or work receipts;
- execution scheduling, concurrency, batching discovered after execution
  starts, or persistence across resource receipts; or
- CLI grammar, Browser controls, rendering, or output formats.

[Query Space Composition](query-space-composition.md) remains authoritative for
request associations, terminal meaning, source-plan grouping, and exact
per-request results. [Producer Planning](producer-planning.md) remains
authoritative for the closed set of evidence producers and their declared
dependencies. Domain owners remain authoritative for every fact and result
that a provision constructs.

## Product problem

Query Space can already preserve distinct Rows and Count requests and can avoid
materializing Rows when Count is independently answerable. It can also derive
Count from an already captured complete row snapshot when the row contract
proves that cardinality is exact.

That choice currently begins after the producer has fixed the row shape. A
producer with several materialization strengths cannot receive the complete
demand and choose where to stop decoding. It therefore tends to construct one
rich row for every candidate and lets later planning select, count, or discard
those rows.

Type declarations show the missing layer:

```text
Type population
├─ Count
└─ Rows
   ├─ Name
   │  └─ IList<T>
   └─ Signature
      └─ public interface IList<T> : ICollection<T>
         where T : ...
```

Count is a population capability. Name and Signature are capabilities of a
realized Type row. Signature can cover Name for the same Type, while complete
cardinality-preserving Name rows can cover Count for their population. A
windowed Name result cannot cover source Count, and one selected Signature
cannot cover Names for unselected Types. These are structural relationships,
not three rendering verbosity values.

The producer must see all requirements before choosing among:

- direct Count without row realization;
- Name rows without hierarchy or constraint decoding;
- selection over Names followed by Signature decoding only for selected Types;
- complete Signature rows that also cover requested Names; or
- deliberately separate paths when sharing would do more work.

## Production witness

The Package Tree candidate in #9056 provides the first measured witness.
Pinned `dotnet-inspect.any@0.26.0` contains 73 Package children. Its full
NativeAOT Package Tree paths added 192.4-197.4 ms over the effective base,
while scalar Count was 52.3% faster than full JSON and a one-row window was
28.4% faster. The stable receipt is recorded in that PR as
`15b54cf362569afe942bec5e33aad20d6f3e8accd5bc3ba576680ccc2a66d573`.

That evidence establishes only a population-sensitive eager-work problem. It
does not prove an asymptotic complexity or prescribe one implementation. The
replacement uses capability demand to let the Package producer distinguish:

- exact child Count;
- child identity and display rows;
- nested measurements requested for returned child rows; and
- exact-child work requested after navigation.

The same request and producer plan serve the CLI and Browser/Wasm. Hosts choose
gestures and presentation but do not assemble different capability sets for
equivalent product questions.

## Conventional basis

| Precedent | Adopted | Deliberate difference |
| --- | --- | --- |
| Query Space request sets | Complete known requests, owner-issued resource identity, terminal-specialized plans, and one result per caller association. | A producer capability plan works beneath terminal selection and can choose row materialization strength before values exist. |
| Relational logical and physical planning | Required properties remain separate from physical alternatives, and one stronger realization may satisfy a weaker requirement when containment is proven. | There is no relational algebra, global statistics model, or universal optimizer. Each producer owns its alternatives and choice. |
| GraphQL selection sets | A consumer communicates the fields it needs before execution. | Field presence alone does not prove population coverage, completion, cardinality preservation, or a cheaper physical plan. |
| Haxl and DataLoader | Known requests may share work while preserving one result association per request. | No event-loop batch, memoization, or cache authorizes sharing; one explicit immutable requirement set does. |
| Producer Planning | Work is declared before execution and a closed description remains inspectable. | Producer Planning chooses evidence producers and dependencies; this contract lets one selected producer choose among ways to satisfy several materialization requirements. |

The design keeps the useful separation from these systems without importing
their execution or cache models.

## Structural model

### Resource group

One planning input contains requirements for exactly one owner-issued immutable
resource identity. Query Space may partition a larger request set into several
resource groups, but it never joins groups from display text, path equality,
object identity, or coincidentally equal values.

The resource identity is the producer owner's join currency. Its owner defines
version, freshness, replacement, and portability. Planning may consume an
already available resource descriptor, but it does not open, decode, fetch, or
sample the resource.

### Scope

A requirement is attached to an owner-issued structural scope. Scopes express
where a capability applies, not how to compute it. A producer may define
scopes such as:

```text
resource
  population
    selected row
      nested population
```

Selection identity remains part of the scope. A capability on one selected row
cannot satisfy the same capability on another row or on the complete
population. A nested population's Count is not the parent population's Count.

The reusable contract preserves parent-child association and stable owner
identities. It does not standardize Type handles, Package selectors, graph
vertices, or another owner's subject representation.

### Requirement

One requirement contains:

- one caller-issued association identity;
- the owner-issued resource and structural scope;
- one owner-issued capability identity and parameters;
- the minimum accepted completion strength;
- the required result form; and
- the typed result and non-success contract.

Requirements state facts needed by consumers. They do not name a decoder,
cache, index, producer instance, callback, host component, or rendering field.
Equivalent requirements may share one provision, but every association remains
present in the result.

### Provision

A provision is one producer-owned realization that a strategy can construct.
Its declaration states:

- the exact capability and scope it directly provides;
- prerequisite provisions or already available resource facts;
- the owner-issued completion evidence it can publish;
- other requirements it may cover and the proof obligation for each;
- retained result and resource-lifetime classes; and
- typed rejection and execution-failure outcomes.

A provision declaration is structural data. Its executable binding remains
typed and local to the producer, following Query Space's existing separation
between inspectable meaning and executable machinery.

### Covering relationship

One provision covers another requirement only through an explicit
owner-certified relationship. That relationship binds:

- compatible resource and scope identities;
- the source provision and covered capability;
- the projection from the source result to the required result;
- the completion evidence that admits the projection; and
- cardinality preservation when Count is derived.

Coverage is a directed relation, not a numeric level. A producer may declare
`Signature -> Name` for one Type and `complete Names -> Count` for one
population without claiming that Signature rows always establish population
Count. Coverage need not be transitive unless the producer declares and the
planner validates the complete chain.

No generic rule treats non-empty Rows, a window, a page, a continuation, a
provider total, or an observed count as exact population completion.

### Strategy and provision plan

A producer declares one or more strategies over its own capabilities. Query
Space passes the complete validated requirement set to the producer's planner.
The producer returns one immutable plan that:

- selects provisions and their dependency order;
- assigns each requirement to exactly one direct or covering satisfaction
  path;
- identifies shared construction once;
- retains terminal-specialized or otherwise independent work when sharing
  would increase cost or change meaning; and
- contains no opened subject, borrowed resource, mutable cache, or deferred
  discovery.

"Best" is producer-defined and deterministic for the same requirement set and
planning inputs. A producer may consider request breadth, terminal, structural
scope, known resource descriptors, and owner-owned cost evidence. Query Space
does not compare costs from unrelated producers or require a prepared artifact
because it covers more capabilities.

When no specialized strategy applies, the producer supplies a deterministic
reference strategy or rejects the requirement set before execution. It does
not silently drop requirements or substitute weaker completion.

### Plan validation

The reusable substrate validates structure rather than producer semantics:

- every input association occurs exactly once in the plan;
- every satisfaction path begins at a selected provision;
- every covering edge is one declared by that producer;
- resource and scope identities agree along the path;
- the path's completion requirement is at least the requirement's accepted
  completion;
- provision dependencies are present and acyclic; and
- the detached result shape can route one typed outcome to every association.

The producer remains responsible for the truth of its capability and coverage
declarations. Adoption gates exercise those declarations against independent
owner-specific reference paths.

## Planning rules

### Complete demand precedes producer choice

Query Space combines compatible consumer requirements before asking the
producer to plan. A producer never discovers another consumer requirement
while executing. A later request forms a new plan and execution; it does not
mutate an active plan or retroactively retain discarded evidence.

### Selection precedes optional enrichment

The structural scopes let a producer place selection before an expensive
capability when the owner contract permits it:

```text
produce candidate identities or Names
  -> apply the resolved selection
  -> produce Signature only for returned Types
```

A predicate that itself needs Signature must request that capability before
the predicate can run. The producer cannot pretend the cheaper ordering applies
when query meaning requires the richer fact for every candidate.

### Cover work only when the plan chooses it

An already-required stronger provision may satisfy a weaker requirement when
the declared coverage and completion evidence admit it. A stronger provision
is not constructed solely because it could cover a weaker requirement unless
the producer's selected strategy chooses that construction as the cheaper valid
path.

This preserves singleton specialization:

- Count alone can remain Count-only;
- Exists alone can retain early settlement;
- Name rows do not imply Signature decoding; and
- one selected Signature does not expand to complete Signature rows.

### Results preserve independent meaning

Each result association records:

- its original requirement;
- direct or covering satisfaction;
- the provision and any covering path used;
- its typed value or non-success;
- accepted completion evidence; and
- shared work identity when applicable.

Shared work is recorded once. Per-requirement settlement remains visible. A
producer failure affects every unsettled requirement whose chosen path depends
on that provision. Independent settled requirements retain their results under
the request-set rules. Failure never becomes zero, empty Rows, false, omitted
association, or an unplanned fallback.

## Type example

The reusable contract supports these owner-specific Type strategies without
encoding Type semantics:

| Combined requirements | Valid producer choice |
| --- | --- |
| Population Count only | Read exact declaration cardinality without constructing Name or Signature rows. |
| Complete Name rows and Count | Construct Name rows and derive Count only when the owner certifies complete one-to-one cardinality. |
| Windowed Name rows and source Count | Produce exact Count separately and decode Names only as required by selection. |
| Name rows and one selected Signature | Resolve selection over the least sufficient facts, then decode Signature for the selected Type only. |
| Complete Signature rows and Count | Construct Signatures and derive Names and Count only when the complete coverage chain is certified. |

`IList<T>` Name decoding reads the generic parameter name needed for display.
Its Signature additionally requires owner-issued declaration facts such as
accessibility, modifiers, kind, base type, implemented interfaces, and generic
constraints. Kind-specific signatures may require an enum underlying type or a
delegate return and parameter list. Member populations remain separate nested
requirements.

## Failure and lifetime

Planning failures are typed and occur before producer work. They include an
unknown capability, incompatible scope, unsatisfied completion requirement,
invalid covering path, dependency cycle, or no admitted strategy.

Execution uses the producer's existing typed outcomes. A selected provision
that becomes unavailable or incomplete does not trigger an undeclared fallback.
A caller may form a new request after observing the failure, but the failed
execution remains failed.

Provisioned resources follow their owner's borrowing and release contract. The
generic plan is resource-free. Detached results retain immutable values and
receipts, never readers, streams, enumerators, callbacks, mutable indexes, or
leases. A prepared index or cache requires a separate owner contract for its
identity and lifetime; this design merely permits a producer strategy to
reference that owner-issued provision.

## Model boundary

Requirement normalization, provision selection, coverage validation, and
result association are deterministic construction over immutable data. They
introduce no concurrent or long-lived state machine, so ordinary Release gates
are the appropriate design evidence.

Execution settlement, early stopping, and shared failure routing remain owned
by [Open and closed queries](open-and-closed-queries.md) and its existing TLA+
model. Source acceptance and completion remain owned by
[Source Delegation](source-delegation.md). This design does not copy either
owner's transitions into another model.

## Adoption sequence

The approved stack has three slices:

1. **Focused design.** Lock this owner, its structural model, and its boundary
   with Query Space Composition, Producer Planning, and domain producers.
2. **Capability substrate.** Add the host-neutral requirement, provision,
   coverage, plan, and result-association contracts to QuerySpace. Adopt the
   model in the existing Graph Libraries section-row Rows/Count path so the
   substrate has a real caller while preserving that path's current terminal
   semantics.
3. **Package Tree replacement.** Branch from the capability slice, replace the
   eager Package-child work with Package-owned capabilities and strategies,
   exercise the same host-neutral plan through CLI and Browser/Wasm, reproduce
   the exact #9056 behavior and pathological cases, and publish exact
   NativeAOT before/after evidence. Publish the replacement before closing
   #9056 as superseded.

Type Count, Name, and Signature adoption follows as a focused Type/Library
owner stack rather than being folded into the Package adoption. This design
defines the reusable pattern and uses Type to prove its structural adequacy; it
does not transfer declaration semantics from Metadata, Library, or Type.

## Required evidence

| Gate | Required property |
| --- | --- |
| `ProducerCapabilityPlanPreservesEveryRequirement` | Whole-set planning returns one direct or covering satisfaction path for every association, in request order, with no missing or duplicate result. |
| `ProducerCapabilityCoverageRequiresOwnerProof` | Equal display text, result shape, or object identity cannot authorize coverage; resource, scope, capability, projection, and completion must use the producer's declared relationship. |
| `ProducerCapabilityPlanRejectsInvalidStructureBeforeWork` | Unknown capabilities, incompatible scopes, dependency cycles, insufficient completion, and unsatisfied requirements reject before subject access. |
| `ProducerCapabilityPlanPreservesSingletonSpecialization` | Count-only, Exists-only, Rows-only, or another singleton requirement retains its owner-issued direct strategy unless that producer explicitly selects a cheaper covering strategy. |
| `ProducerCapabilityPlanSelectsBeforeEnrichment` | The Package adopter proves a returned-row capability executes only for rows admitted by earlier selection unless the selection itself requires that capability. |
| `ProducerCapabilityResultsPreserveTypedFailure` | A failed or incomplete provision remains visible for every dependent requirement and never becomes an empty or zero result; independent settled requirements remain intact. |
| `ProducerCapabilityPlanIsResourceFree` | Planning opens no subject and the reusable plan and detached results retain no live resource. |
| `PackageTreeCapabilitiesPreserveHostsAndWorkReduction` | The replacement Package adoption produces equivalent CLI and Browser/Wasm children and exact navigation while Count, bounded Rows, and default Tree execute only their requested capability plan. |

The Package adoption additionally follows the repository's NativeAOT evidence
contract over pinned zero-, small-, and 73-child packages and every supported
terminal. The capability-substrate slice uses its focused Release suites and
does not claim a product performance improvement before the Package adopter
measures one.

## Non-claims

This design does not claim:

- that every richer result covers every simpler-looking result;
- that all capabilities form one lattice or hierarchy;
- that complete Rows are always cheaper or more expensive than direct Count;
- that producer choice finds a globally optimal plan;
- that planning may inspect a subject to estimate its cost;
- that one provision must be shared whenever several requirements can use it;
- that a prepared index, cache, or fused traversal is beneficial for singleton
  requests;
- that capability planning changes query, completion, or failure semantics;
- that provisions can be retained across unrelated resource receipts;
- that Type adoption is part of the Package Tree PR; or
- that #9056 is closed, merged, or superseded before its replacement is
  published.
