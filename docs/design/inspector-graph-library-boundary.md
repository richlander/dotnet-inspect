# Inspector.Graph library boundary

## Status, owner, and claim

Status: **design contract** for
[#8721](https://github.com/richlander/dotnet-inspect/issues/8721), the first
focused adoption under
[#8669](https://github.com/richlander/dotnet-inspect/issues/8669).

The **Inspector.Graph Structural Document** owns this claim:

> `Inspector.Graph` owns an immutable graph document whose nodes, groups,
> directed logical edges, physical or derived occurrences, targets, seeds,
> characteristics, limits, and failures use dense document-local identities
> and caller-owned typed payloads. It snapshots and validates graph-local
> structure without defining or interpreting .NET subjects, producer
> ownership, evidence meaning, queries, Workspace lifetime, or presentation.

This transfers one cohesive responsibility from the current
Queries-owned `InspectionGraphDocument`: graph-local structure and structural
validity. The current runtime remains authoritative until a focused
implementation moves the carrier and retires the old path. The target
properties are **unverified** until the gates under
[Required evidence](#required-evidence) land and run in Release.

[Inspection operation kernels](inspection-operation-kernels.md) owns the
cross-cutting typed handoff. [Inspection graph
document](inspection-graph-document.md) continues to own product Inspection
Graph semantics, including .NET subject lenses, producer relationships,
evidence, completeness, and presentation-independent meaning. This document
owns only the new library and structural carrier boundary.

## User outcome

The production-shaped demonstration remains the OpenTelemetry external-focused
call view:

```text
AddOpenTelemetrySharedProviderBuilderServices
  -> Sdk.get_SuppressInstrumentation
  -> SuppressInstrumentationScope.get_IsSuppressed
  -> OpenTelemetry.Api: RuntimeContextSlot<T>.Get
```

Analysis and CallGraph continue to issue exact member and call-site identities.
Queries continues to classify product subjects, bind the retained Workspace
generation, declare relationship and diagnostic meaning, and construct the
product operation. `Inspector.Graph` retains the resulting topology,
occurrence receipts, characteristics, limits, and failures without learning
what a member, call, package, or external scope means.

The neighboring demonstration is deliberately outside dotnet-inspect. An
application defines ordinary records such as:

```csharp
record Service(string Name);
record DependsOn(string Kind);
record DependencyReceipt(string ConfigurationSource);
```

It uses those records directly as graph payloads. They implement no
`Inspector.Graph` interface and carry no dotnet-inspect identity. The same
carrier invariants apply. This direct consumer is what makes the extraction a
real subject-neutral boundary rather than Queries types moved under a shorter
namespace.

## Responsibility boundary

### `Inspector.Graph` owns

- immutable snapshots of graph collections;
- dense document-local node, group, edge, and occurrence ids;
- directed logical edge topology;
- physical and derived occurrence linkage;
- document-local targets and derivation sources;
- node grouping and acyclic group parentage;
- seed-to-node or seed-to-group binding;
- target-scoped characteristics, limits, and failures;
- deterministic preservation of supplied order; and
- structural construction failure when those invariants are invalid.

Those concepts are intrinsic to the graph document. None requires knowledge of
IL, Metadata, packages, Workspaces, QuerySpace, or a product host.

### Product and producer owners retain

- what each subject identity means and how it compares;
- member, type, assembly, package, Integration, and acquisition identities;
- which producer owns a relationship, characteristic, limit, or failure;
- which subject kinds a relationship admits at each endpoint;
- how a physical occurrence supports a rolled-up logical endpoint;
- occurrence identity and evidence compatibility;
- which Query prerequisites produce an optional characteristic;
- producer completion and the semantic meaning of empty or partial evidence;
- whether all payloads are portable beyond a retained session;
- Workspace generation, acquisition, capability, cost, and work bounds;
- operation registration, defaults, and product errors; and
- Markout, JSON, CLI, and Browser lowering.

The library therefore validates structure, not domain truth. A structurally
valid document can still be semantically invalid for a product vocabulary.
The product composer must reject that input before or while binding it into the
carrier under its own Release gates.

## Typed carrier

The conceptual carrier has six independent caller-owned payload planes:

```text
GraphDocument<
  TSubject,
  TRelationship,
  TOccurrenceEvidence,
  TCharacteristic,
  TLimit,
  TFailure>
```

The final CLR API may factor repeated type parameters into another statically
typed form, but it must preserve all six distinctions. It may not replace them
with `object`, `dynamic`, reflection discovery, a type-keyed bag, string type
names, or a marker interface that every consumer payload must implement.

The planes have these roles:

| Plane | Structural use | Semantic owner |
| --- | --- | --- |
| `TSubject` | Node/group value, occurrence endpoints, and seed value | Caller |
| `TRelationship` | Edge and occurrence relation identity | Caller |
| `TOccurrenceEvidence` | Receipt carried by one occurrence | Producer/caller |
| `TCharacteristic` | Typed observation attached to a graph target | Producer/caller |
| `TLimit` | Typed incompleteness or work-bound statement | Operation/producer |
| `TFailure` | Typed visible failure statement | Producer/composer |

`TSubject` and `TRelationship` are ordinary generic values. The carrier uses
their typed equality for document-local uniqueness and relationship
consistency. A caller that needs specialized equality supplies an
owner-defined value wrapper or an explicitly typed comparer admitted by the
concrete API; it does not ask Graph to infer identity from labels.

Occurrence evidence, characteristics, limits, and failures may be rich
caller-owned discriminated unions. Graph retains them but does not branch on
their cases. The product can therefore keep today’s Analysis, Metadata,
CallGraph, package, Integration, and Research evidence as precise types rather
than normalizing them into one universal graph evidence model.

## Structural document

The target document contains these graph-owned structures:

```text
GraphDocument
  Scope
  Nodes[]            Id, Subject, Role, GroupIds[]
  Groups[]           Id, Subject, ParentId?
  Edges[]            Id, FromNodeId, ToNodeId, Relationship, OccurrenceIds[]
  Occurrences[]      Id, Relationship, SourceSubject, TargetSubject,
                     Evidence, DerivedFromOccurrenceIds[]
  Characteristics[] Target, Payload, Derivation
  Seeds[]            Subject, Target, Role
  Limits[]           Target?, Payload
  Failures[]         Target?, Payload
```

This is a contract shape, not frozen CLR spelling.

### Document-local identity

Node, group, edge, and occurrence ids are contiguous zero-based indices into
their respective immutable collections. They are valid only within one
document instance. A projection that removes structure remaps every retained
reference into a new dense identity space.

Document-local ids are never:

- portable subject identity;
- producer evidence identity;
- a replacement for an owner-issued receipt;
- stable across independently constructed documents; or
- display order recovered from rendered output.

The payload remains the durable owner-issued currency. A call member keeps its
`GraphNodeIdentity` and `MemberRef`; acquired Metadata values keep their exact
registration and row identity; an independent consumer keeps its own record.

### Structural validity

Construction is atomic. It snapshots every collection and either produces one
valid immutable document or fails visibly. It validates:

1. every collection is initialized;
2. every local id is dense and matches its collection position;
3. node subjects are unique under the admitted typed equality;
4. every node group, group parent, edge endpoint, edge occurrence, target,
   seed target, derivation source, and derived-occurrence reference exists;
5. group parent links are acyclic and no group is its own parent;
6. every seed subject equals the subject at its node or group target;
7. every edge occurrence carries the same typed relationship as its edge;
8. every derived-occurrence relation is acyclic; and
9. supplied collection order is retained exactly.

Graph cannot validate whether a rolled-up edge endpoint is semantically
supported by a finer occurrence endpoint. That requires the caller's subject
containment and relationship projection contracts. Queries retains that gate.

An empty graph is structurally valid. Whether it is a complete answer depends
on caller-issued limits, failures, producer completion, and operation
semantics.

### Graph-owned local vocabulary

These values remain graph-owned because their meaning is document-local:

- node role needed to distinguish ordinary, external, truncated, or
  unclassified structural positions;
- target kind: node, group, edge, or occurrence;
- seed role: primary or peer;
- characteristic derivation kind and graph-target sources; and
- session-bound versus portable document scope.

The caller decides which role or scope applies. The carrier retains and
validates the value but does not derive it. A future focused Graph design may
extend this local vocabulary; product-specific owner, subject-kind, exposure,
affiliation, and producer descriptors do not enter it.

## Semantic binding

The current `InspectionGraphDocument` combines graph structure with product
vocabulary in one file. Extraction separates two construction stages:

```text
producer evidence
  -> Queries-owned semantic binding and validation
  -> Inspector.Graph structural construction and validation
  -> typed product result
```

Queries-owned semantic binding:

- forms `InspectionGraphSubject` values containing the unchanged owner-issued
  identities;
- applies `InspectionGraphSubjectKind`, `InspectionGraphOwner`, endpoint
  projection, seed admission, and occurrence-identity rules;
- validates descriptor ids, evidence contracts, prerequisites, and
  portability;
- declares product characteristic, limit, and failure payloads; and
- passes those typed values into the generic carrier.

The binding is ordinary typed code, not a registry discovered by reflection.
It may use a product-owned discriminated union or existing abstract record
family as a generic payload. `Inspector.Graph` does not require the payload to
implement an interface.

This two-stage boundary does not weaken validation. Every current invariant
must have exactly one target owner and an equivalent Release gate before the
old carrier retires. Structural invariants move; semantic invariants remain.

### Product result binding

The generic carrier is not the complete product result. Queries retains a
typed product binding that composes:

```text
Inspection Graph product result
  Mode / neighborhood / induced-set / focus request
  Product vocabulary and semantic completion
  Inspector.Graph structural document
```

The exact CLR name is not frozen. The binding contains one carrier; it does not
copy its collections or maintain a second set of local ids. Product requests
continue to be available to CLI and Browser consumers without becoming
`Inspector.Graph` dependencies.

This product result is durable composition, not a compatibility wrapper. A
temporary old-to-new conversion may exist only inside an independently
coherent migration slice and retires when that slice closes.

### Validation disposition

Every current constructor check has this target:

| Current validation | Target owner |
| --- | --- |
| Initialized collections, dense ids, collection snapshots, and local reference ranges | `Inspector.Graph` |
| Unique node subjects under typed equality | `Inspector.Graph` |
| Group parent validity and acyclicity | `Inspector.Graph` |
| Edge/occurrence relationship equality and derived-occurrence reference acyclicity | `Inspector.Graph` |
| Target and derivation-source validity | `Inspector.Graph` |
| Seed target validity and target-subject equality | `Inspector.Graph` |
| Portable payload eligibility | Queries/product binding |
| Descriptor identity and owner uniqueness | Queries/product binding |
| Subject-kind endpoint and owned-subject admission | Queries/product binding |
| Occurrence evidence compatibility and semantic identity | Producer catalog and Queries binding |
| Characteristic target/value/derivation admission and Query prerequisites | Producer catalog and Queries binding |
| Limit/failure evidence compatibility and completion meaning | Producer catalog and Queries binding |
| Mode cardinality, selected relationships, direction, depth, induced closure, and focus request | Current request owners, then focused execution successors |

## Lifetime and portability

`Inspector.Graph` owns the graph-local distinction between a session-bound and
portable document because consumers must know whether the carrier can outlive
its construction scope. It does not decide whether a caller payload is
portable.

Product composition may construct a portable document only after its
owner-issued payload contracts establish portability. Acquired registrations,
Workspace handles, readers, streams, leases, and provider callbacks remain
session-bound. The generic carrier does not retain a live Workspace or acquire
another subject.

Serialization remains outside this library. A serializer consumes a portable
product binding and lowers typed values under the product schema. Structural
portability does not create a universal graph wire format.

## Rendering boundary

`Inspector.Graph` defines no renderer, row vocabulary, label, or format
lowering. The generic document keeps subject, relationship, occurrence,
characteristic, limit, and failure payloads typed until product composition.

The current product Inspection Graph owner continues to define the structured
information presented by both hosts. CLI uses its existing Markout lowering
and structured formats; Inspect Web uses its typed browser projection and
TypeScript boundary. Carrier migration changes neither result meaning nor
format. Any future broad Graph rendering design remains separately subject to
the repository's Markout-default rule.

## Dependency boundary

The initial `Inspector.Graph` production project is BCL-only and has no
production project reference. Its public contract is coherent with ordinary
application types, independent of every repository inspection domain. This
introduces no platform exception: the carrier uses supported BCL collections
and language features and inherits the repository's cross-platform and
Browser/Wasm contract.

The permanent forbidden dependency set is:

- `ILInspector.*`;
- `DotnetInspector.*`;
- `DotnetInspect.*`;
- `QuerySpace`;
- Markout; and
- Roslyn (`Microsoft.CodeAnalysis*`).

The first three preserve the selected full family boundary from
[Inspection operation kernels](inspection-operation-kernels.md#required-evidence).
QuerySpace remains a sibling operation substrate; a later bridge may reference
both owners. Markout and Roslyn would couple the carrier to presentation or
compiler infrastructure it does not use.

One Release architecture test checks both dependency representations:

1. evaluate the target project's transitive `ProjectReference` graph and
   require that it contains only the `Inspector.Graph` project; and
2. read the built `Inspector.Graph.dll` assembly-reference table and require
   that every runtime reference is a BCL assembly.

The test reads evaluated project and compiled artifact evidence. A source-text
scan is not sufficient. Repository-wide build-only analyzer and linker
packages are not runtime dependencies and do not violate this boundary.

## Current type disposition

The transfer is semantic rather than a file move:

| Current surface | Target disposition |
| --- | --- |
| `InspectionGraphDocument` collection carrier | Re-express as the generic `Inspector.Graph` document |
| `InspectionGraphNode`, `InspectionGraphGroup`, `InspectionGraphEdge`, and `InspectionGraphOccurrence` structure | Re-express as graph-owned generic structures |
| `InspectionGraphTarget`, seed role, derivation links, local ids, and document scope | Transfer as graph-local vocabulary |
| Immutable snapshots, dense ids, reference integrity, group-cycle checks, and deterministic preservation | Transfer with equivalent gates |
| `InspectionGraphSubject` and member/type/assembly/package identity families | Retain in `DotnetInspector.Queries` as `TSubject` |
| `InspectionGraphSubjectKind` and owner-issued containment | Retain in product composition |
| `InspectionGraphOwner`, producer descriptors, evidence interfaces, and catalogs | Retain in their domain composition |
| Subject-kind endpoint projection, seed admission, evidence compatibility, and occurrence identity | Retain until a focused typed-provider/execution design transfers a generic mechanism |
| Characteristic descriptors' `InspectionQueryDefinition` prerequisites | Retain in Queries |
| Call, Metadata, package, Integration, and Research adapters | Retain |
| `InspectionGraphRelationshipComposer` | Retain until the provider design names a typed replacement |
| Mode, neighborhood, induced-set, and focus requests and algorithms | Focused reference-execution successors |
| Product request plus semantic-completion result | Retain as a Queries-owned binding around one neutral carrier |
| CLI, Markout, JSON, and Browser adapters | Retain with current hosts and presentation owners |

The implementation may rename graph-local types when that makes the public
boundary clearer. It must not keep a permanent Queries-owned structural
carrier, copied local-id space, or bidirectional conversion layer after all
production consumers migrate.

## Conventional basis

The design uses analogous libraries as evidence, not authority:

| Design | Transferable evidence | Deliberate boundary |
| --- | --- | --- |
| QuikGraph generic graph types | A .NET graph carrier can remain generic over application vertex and edge values and provide immutable representations | QuikGraph edges implement graph interfaces; `Inspector.Graph` owns local endpoints so consumer payload records need no marker interface |
| Boost Graph Library concepts and property maps | Algorithms and graph-associated values can remain separate from one concrete inspected domain | C++ compile-time concepts do not supply typed evidence, completion, failure, or runtime-authored inspection plans |
| `AnnotatedSourceDocument` | A document can own local structure and references while producers own semantic observations and presentation remains derived | Graph has no canonical text coordinate and must retain topology, physical occurrences, limits, and failures |
| Current `InspectionGraphDocument` | Dense local ids, typed occurrence receipts, target-scoped observations, and visible diagnostics are proven product shapes | .NET identities, Query prerequisites, producer catalogs, and host lowering do not transfer into the neutral carrier |

References:

- [QuikGraph API](https://kernelith.github.io/QuikGraph/api/QuikGraph.html)
- [Boost Graph Library graph concepts](https://www.boost.org/doc/libs/release/libs/graph/doc/graph_concepts.html)

## Production adoption and retirement

The focused boundary reaches production in four slices:

1. **Boundary implementation:** add the BCL-only project, structural carrier,
   direct consumer fixture, invariant tests, and dual dependency gate.
2. **Product binding:** bind the current Queries-owned subject, relationship,
   evidence, characteristic, limit, and failure families into the new carrier;
   preserve the OpenTelemetry call document and neighboring Integration shape.
3. **Consumer migration:** migrate Queries, Sections, CLI, and Inspect Web to
   the bound carrier, including C# and TypeScript call sites and existing
   structured/Markout lowerings.
4. **Retirement:** remove the Queries-owned structural carrier and temporary
   conversion code after parity; record exact NativeAOT base/head evidence for
   every production terminal affected by the modernization.

Slices 1 and 2 may land together because they prove the new owner with one
existing donor binding. Slices 2 through 4 may form one coherent PR only when
every current consumer moves and the old carrier retires in that same PR.
Traversal execution, QuerySpace-backed providers, Count specialization, and new
host behavior do not join that implementation.

After retirement:

```text
Analysis / Metadata / CallGraph / package / Research evidence
  -> DotnetInspector.Queries semantic binding
  -> Inspector.Graph structural document
  -> shared product result
  -> CLI Markout and JSON | Browser typed lowering
```

The overall product Graph operation remains tracked by
[#7624](https://github.com/richlander/dotnet-inspect/issues/7624). Reference
execution, provider expansion, QuerySpace composition, specialization, and
terminal execution remain successors under #8669.

## Required evidence

The implementation requires:

1. **Structural invariant gates.** Release tests cover empty, disconnected,
   duplicate-subject, invalid-reference, cyclic-group, cyclic-derived-
   occurrence, default-collection, and deterministic-order cases.
2. **Semantic parity gates.** Every current `InspectionGraphDocument`
   constructor validation has one explicit structural or Queries-owned target
   gate before retirement.
3. **Direct consumer.** A separately compiled fixture references only
   `Inspector.Graph` and constructs nodes, relationships, occurrence evidence,
   characteristics, limits, and failures from ordinary application records.
4. **Full dependency coverage.** One Release architecture test checks the
   evaluated transitive project closure and built assembly references.
5. **Production binding.** Existing Queries tests preserve owner-issued member,
   Metadata, acquisition, package, and Integration identity and evidence.
6. **Both hosts.** Existing CLI and Browser/Wasm tests consume the same bound
   document and preserve structured diagnostics and output lowering.
7. **Pathological product case.** The OpenTelemetry document retains a
   cross-package exit, exact physical call receipt, incomplete-evidence
   diagnostics, and deterministic connector topology through migration.
8. **NativeAOT modernization evidence.** Exact base/head production binaries,
   real assets, result identities, and every affected terminal follow
   [the repository evidence contract](../evidence-and-validation.md#nativeaot-beforeafter-for-modernization).

No implementation property is green merely because this design names its
gate. Until a gate lands and runs in Release, the property is **unverified**.

## Non-claims

This boundary does not:

- define a universal subject, relationship, owner, evidence, characteristic,
  limit, failure, graph request, or operation result;
- transfer Analysis, Metadata, CallGraph, package, Integration, Research,
  QuerySpace, Workspace, or host semantics;
- define traversal, frontier expansion, focus/path algorithms, Count,
  reachability, or specialized execution;
- decide semantic endpoint support, seed admission, occurrence deduplication,
  producer completion, or absence;
- require consumer types to implement an `Inspector.Graph` interface;
- add runtime plugins, reflection discovery, type-keyed payload lookup, or
  arbitrary executable plan content;
- define a serialized graph schema or portable packet;
- change CLI grammar, defaults, output, rendering, or Browser interaction;
- introduce a permanent compatibility wrapper or parallel carrier;
- create a Graph-to-QuerySpace project reference; or
- include Diff, Source Delegation, QueryOverflow, or naming-audit work.

## Immediate successors

After this boundary locks:

1. implement the structural carrier and complete its product migration;
2. define the Graph reference interpreter and typed frontier-provider contract;
3. adopt QuerySpace-backed frontier expansion with one real relationship
   provider;
4. specialize one measured terminal only after reference behavior is
   production-backed; and
5. continue general product Graph adoption under #7624.

Each successor has one owner. This document is not reopened to absorb its
execution or host-specific contracts.
