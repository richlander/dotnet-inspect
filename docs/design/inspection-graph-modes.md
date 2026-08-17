# Inspection graph modes

How explicit seed subjects and input sets shape an inspection graph. Mode is
independent of relationship family, subject lens, characteristic selection,
workspace scope, and output format.

Tracking: [#4133](https://github.com/richlander/dotnet-inspect/issues/4133).

The current L1 product has an explicit `InspectionGraphModeRequest` for
single-seed, peer-seed, and induced-set documents. The member call adapter
declares its existing focus member as one primary seed. Package-boundary and
Integration projections accept type, assembly, and realized-package seeds over
the owner-issued subjects already present in their graph; a package binds to a
node or group according to the selected package lens. The Integration query
also binds mixed peer seeds without selecting a primary and declares its
request-free workspace projection as an induced set over workspace
participants.

The current executable mode slices bind request intent to graph subjects and
make seed admission a descriptor-owned relationship contract without changing
producer demand or topology. Request-driven relationship selection and
neighborhood construction, traversal bounds, induced explicit-subject sets, and
command/presentation surfaces remain design targets.

`CallAdapter_PreservesTypedTopologyAndDisclosesEvidenceGap`,
`PackageSeed_BindsToNodeOrGroupSelectedByLens`,
`Execute_BindsTypeSeedToExactNode`,
`Execute_BindsAssemblySeedToExactNode`,
`Execute_BindsPackageSeedToDetailedLensGroup`,
`Execute_BindsPeerSeedsWithoutChoosingPrimary`, and
`Execute_DefaultsToWorkspaceInducedSetWithoutSeeds` gate mode binding.
`RelationshipDescriptor_ValidatesAndSnapshotsSeedAdmissions`,
`RelationshipCatalogsDeclareCurrentSeedAdmissions`, and
`SeedAdmissionValidator_RejectsUnsupportedRelationshipSet` gate descriptor
admission and request validation.

| Mode | Focus | Input | Primary question |
| --- | --- | --- | --- |
| **Single seed** | One member, type, assembly, or realized package subject | Seed plus optional workspace scope | What surrounds this subject? |
| **Peer seeds** | Several declared subjects with equal seed roles | Typed member/type/assembly/package subjects | How do these anchors connect? |
| **Induced set** | None | Workspace participants or typed subjects | What admitted graph does this set contain? |

Single-seed and peer-seed requests form the **seeded** mode family. Issue #4133
used **ad hoc** for both peer seeds and induced sets; the distinction above
preserves whether the user named graph anchors or only bounded the input.

Related:

- [Inspection graph document](inspection-graph-document.md) owns the typed
  envelope, relationships, occurrences, lenses, and execution contract.
- [Call-graph projection](call-graph-projection.md) owns the current
  member-seeded call topology.
- [Call-graph characteristics](call-graph-characteristics.md) maps
  call-specific evidence into the shared descriptor model.
- [Inspection space](../inspection-space.md) owns workspace groups, retained
  contexts, query planning, and execution.
- [IChatClient dual-lens demo](../workflows/discovery/aspire-ai-package-graph.md)
  exercises both a type seed and package seeds over mixed relationship
  families.

## Seed is not scope

A seed is an owner-issued subject with an explicit focus role in the result.
Workspace scope names participants that may contribute evidence. Adding a
package to acquisition scope does not make it a seed; selecting a package seed
does not make every acquired package a peer focus.

The document binds each seed subject to a node or group carrying that same
subject. A package may therefore be a focus node in a package lens or a focus
group around member/type nodes in a detailed lens without losing its seed role.

The seed also does not choose:

- which relationship families are admitted;
- which direction the query traverses;
- which subject kinds remain visible or roll up;
- which optional characteristics execute; or
- which output format renders the result.

Those are separate request axes. A relationship descriptor decides whether a
seed subject is a direct endpoint, expands through owned subjects, or is
unsupported for that relation.

## Single-seed mode

**Required:** one typed focus subject.

**Optional:** workspace scope, relationship set, traversal direction, subject
lens, characteristic selection, and bounds.

**Output:** one focus subject plus the bounded, evidence-backed neighborhood
admitted by the request. Walking an incoming edge never reverses its semantic
direction.

### Member seed

The current `member -S "Call Graph"` path is the worked example. The member is
the focus, while caller scopes widen evidence coverage without becoming seeds.
The `call` relationship remains directed caller to callee.

### Type seed

A type seed may remain a type focus while admitted producers expand through:

- its members for call or body evidence;
- extension and implementation relationships;
- metadata references;
- integrations and opportunities; and
- typed ownership roll-ups to assemblies or packages.

The expansion retains the original member and metadata occurrences behind any
type or package edge.

### Assembly seed

An assembly seed may remain an assembly endpoint for relationships that admit
assemblies, or expand through its types and members for finer evidence. Its
acquisition identity and effective target remain part of the subject; display
name equality cannot join assemblies.

### Package seed

A realized package is a first-class seed, not merely a group label or scope
widener. In a package graph it may remain the focus package node and traverse
relationship families that admit package subjects, including dependency,
ownership, rolled-up reference, integration, and opportunity evidence.

In a detailed member or type lens the same package seed may instead be the
focus group containing package-owned subjects. Group membership does not
replace the typed seed subject.

The same seed may expand through package-owned assemblies, types, and members
when the selected relationship needs finer evidence. A call-only contribution
still emits member-to-member call edges; it does not fabricate a package call.
The surrounding inspection graph may roll those occurrences back to package
endpoints when the call relationship descriptor admits that lens.

A package-inward request may traverse incoming relationships to discover the
types and APIs that explain the package. The stored edges retain their original
semantic direction.

## Peer-seed mode

**Required:** two or more typed seed subjects.

Every seed retains an equal focus role. The graph may include bounded paths or
neighborhoods connecting and surrounding them, but it must not silently choose
one hero subject because its topology is convenient.

Peer seeds may share a subject kind or be mixed. For example, the locked demo
can name `IChatClient` plus OpenAI, Bedrock, and Azure package subjects. Each
producer still contributes only relationships it owns.

## Induced-set mode

**Required:** a bounded input set of workspace participants or typed subjects.

There is no focus subject. The request admits relationships according to an
explicit rule, such as all cross-participant edges, all edges among admitted
subjects, or the union of bounded neighborhoods around resolved entries. The
result must disclose which rule it used.

An induced package set is not automatically a package-only lens. It may retain
member/type evidence, package groups, package endpoints, or a mixed view as the
request declares.

The command entry point remains deferred to #3292. It may be a distinct command
or an explicit lens if adding it to a subject command would obscure the
single-seed default.

## Relationship-specific admission

Mode does not impose one expansion algorithm on every producer:

| Current relationship | Declared seed admission |
| --- | --- |
| `call` | Member at either logical edge endpoint |
| `api.extension`, `integration.observed` | Member at the logical source, type at the logical target, assembly/package through source-owned subjects |
| `metadata.reference` | Assembly at either logical endpoint, package through source- or target-owned subjects |
| `integration.opportunity` | Assembly at the logical source, type at the original occurrence source or logical target, package through source-owned subjects |

`EdgeEndpoint` and `OccurrenceEndpoint` admissions must name a subject kind in
the descriptor's corresponding semantic source or target domain.
`OwnedSubjects` declares that the seed may expand through subjects it owns; it
does not fabricate an edge at the owner's subject kind. Unsupported selected
relationship sets fail before producer execution with guidance. They do not
drop the seed, relabel it, or substitute a different relationship. Induced-set
requests have no seeds and therefore do not require seed admission.

The current slice declares and validates these capabilities. It does not yet
use them to select producers or construct a bounded neighborhood.

## Shared contract

- Every seed is an owner-issued typed subject.
- Seed roles, relation direction, identity, limits, and failures are
  correctness-bearing state.
- Physical occurrences and provenance survive subject roll-up.
- Expensive widening and network acquisition remain capability-gated.
- Sequential execution is the baseline, including single-threaded browser/Wasm.
- Any future concurrent executor produces the same deterministic document.

The inspection workspace owns participant lifetime and orchestration. Mode is
query intent, not a second workspace or permission to introduce shared mutable
state.

## Locked-demo interpretation

The `IChatClient` experience supports several valid requests over the same
evidence:

- one type seed projected outward to provider packages;
- one package seed projected inward to the hub type;
- several package peer seeds, optionally with the type as another peer; or
- an induced graph over the pinned package set.

The request mode changes focus and admission, not relationship semantics. The
combined diagram still needs call, extension, integration, ownership,
metadata-reference, and opportunity adapters; no mode turns those into calls.

## Delivery

1. **Implemented:** preserve and name the current member-seeded call behavior.
2. **Implemented:** add generic typed seed roles to the inspection-graph mode
   request and document.
3. **Implemented:** bind type, assembly, and realized-package seeds to existing
   Integration/package graph subjects, declare direct endpoint and
   owned-subject admission on each relationship descriptor, and reject a
   selected relationship set that admits none of a request's seeds.
   Admission-driven producer selection and neighborhood construction remain.
4. **Partially implemented:** bind peer-seed requests without choosing a hero
   node. Connecting-neighborhood construction remains.
5. **Partially implemented:** declare workspace-participant and
   document-subject induced-set rules. Explicit-subject admission and bounds
   remain.

## Required gates

- a scope widener does not become a seed;
- a member, type, assembly, and realized package can each retain focus identity;
- a package seed can remain a package endpoint in a package lens;
- a package seed can become a focus group in a detailed lens without losing its
  typed seed role;
- a package-seeded call contribution retains member call evidence and never
  invents a package-to-package call;
- peer seeds remain equally focused;
- an induced set has no fabricated focus;
- incoming traversal does not reverse semantic edge direction;
- unsupported seed/relation combinations fail with guidance;
- failures and traversal limits remain visible in every mode; and
- the same evidence has identical identity and direction across modes.

## Non-goals

- Treating package, type, or member as the universally preferred seed kind.
- Replacing relationship-specific admission with one generic containment walk.
- Conflating acquisition scope, graph focus, subject lens, and grouping.
- Requiring peer-seed or induced modes before further single-seed improvements.
- Unbounded whole-program closure.
