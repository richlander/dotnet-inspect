# Package dependency member call-graph inspection

This document owns the Sections boundary that hands one completed
dependency-aware package member call graph to production hosts.

Implementation is tracked by
[#8197](https://github.com/richlander/dotnet-inspect/issues/8197).
[Package dependency call-graph operation](package-dependency-call-graph-operation.md)
owns the lower PackageQueries lifecycle. This inspection composes that
operation without redefining traversal, PackageHouse, Workspace publication,
package-role contexts, or call-graph topology.

## Claim and owner

`DotnetInspector.Sections` owns one operation:

```text
PackageDependencyMemberCallGraphInspection
  -> InspectionEnvelope<PackageDependencyMemberCallGraphInspectionOutcome>
```

For one exact realized package root and one exact implementation MethodDef,
the inspection:

1. projects dependency evidence from the retained root generation;
2. traverses the root's authorized dependency declarations under an
   independent target-framework policy;
3. prepares every admitted resolved-candidate edge for PackageHouse;
4. creates a fresh operation-local Workspace and publishes the exact root;
5. invokes `PackageDependencyMemberCallGraphOperation` once;
6. projects its terminal result into resource-free host content; and
7. returns the `package-dependency-member-call-graph` Outcome envelope with one
   required portable projection and ordered diagnostics.

The inspection owns this production composition. The CLI and Browser/Wasm
hosts supply source capabilities and exact selection intent; they do not
assemble dependency participants or call the lower operation independently.

## Request

One `PackageDependencyMemberCallGraphInspectionRequest` carries:

- one exact live `PackageRootBinding`;
- one module version id and MethodDef token selected from that root;
- the independent traversal target-framework policy;
- finite dependency traversal depth and work bounds;
- finite external-focused graph depth and node bounds;
- one PackageHouse Realize operation carrying source deadlines;
- one finite Workspace deadline; and
- optional package assembly-context realization limits.

The selected root framework is frozen in the binding. The traversal target
does not replace it. Omitted host traversal input uses
`TraversalTargetFrameworkPolicy.ProductDefault`, currently `net12.0`.

The focus carries no display text. Hosts resolve type and member selectors
before invoking the inspection and supply exact implementation identity.

## Source capabilities

`PackageDependencyMemberCallGraphInspectionSource` supplies:

- the candidate resolver;
- the candidate-authorized exact manifest acquirer;
- one PackageHouse using the same source authorization;
- and a factory that issues the Package Source operation consumed by edge
  realization.

The candidate resolver, manifest acquirer, and issued edge operation must
belong to the same Package Source settlement generation. The lower operation
validates that association before payload acquisition.

The capability owns its Package Source settlement lifetime. The inspection
owns and consumes only the operation lease returned for the completed edge
set. Hosts retain ownership of clients, stores, root content, and settlement
roots.

## Operation-local Workspace

The inspection creates one fresh `InspectionWorkspace`, publishes only the
exact root binding, and passes its committed Scope to the lower operation.
Dependency routes append their exact Package contributions atomically.
The request carries the host's package-assembly realization policy. Browser/Wasm
uses the same participant, retained-image, entry, and declared-length bounds as
its ordinary package Workspaces, so automatic dependency composition cannot
bypass the Browser host's memory policy.

Traversal retains every resolved candidate and route. For graph assembly
composition only, the focused root wins any same-package-ID collision; other
collisions select the nearest routed occurrence, then the highest resolved
version at that distance. This prevents two versions of the same package from
contributing duplicate assembly identities without rewriting traversal or
route evidence.

The Workspace never contains the Browser's selected tabs or the CLI's former
manual `--package` participants. Unrelated open packages therefore cannot
change traversal, package-role composition, graph classification, or result
order.

The Workspace closes before the envelope crosses the host boundary. No
Workspace identity, revision, publication base, occurrence identity, root
binding, package-role resource, or source lease enters Content.

## Content

`PackageDependencyMemberCallGraphInspectionOutcome` is an owner-specific
Outcome.

Its available case contains one
`PackageDependencyMemberCallGraphDocument` with:

- the traversal target policy and completion summary;
- one detached route per admitted root-relative edge; and
- the portable `InspectionGraphDocument`.

A Package destination copies the resource-free Workspace package descriptor,
realization status, and route source but drops the process-local occurrence
identity. Platform and unavailable destinations retain their lower-owner typed
facts.

Its unavailable case carries one typed reason and contained detail for:

- root dependency-context absence or failure;
- root Workspace publication refusal;
- dependency Workspace publication refusal;
- exact focus unavailability; or
- package-context cleanup failure.

Cancellation and unexpected producer, acquisition, Workspace, analysis, or
cleanup exceptions produce no envelope.

## Resource, portable projection, and diagnostics

The inspection returns resource
`package-dependency-member-call-graph`, classifies the owner-specific terminal
content as Outcome, and returns
`InspectionPortableProjection.NonProjectable(NotSupported)`. No canonical
Workspace packet currently preserves exact root focus, traversal target, graph
bounds, and source capabilities. Hosts do not manufacture a portable
projection from argv, Browser navigation, display labels, or rendered graph
content.

Available Content retains graph limits, graph failures, and unavailable
routes as typed owner evidence. The envelope also emits deterministic
cross-host diagnostics for unavailable routes and non-request-bound graph
limitations or failures. Diagnostics do not replace or alter Content.

## Host adoption

The CLI:

- realizes and resolves one exact root member;
- supplies desktop source capabilities;
- consumes the shared envelope; and
- lowers the Document through its existing graph output adapter.

Browser/Wasm:

- resolves the active package member to exact implementation identity;
- supplies Browser source and bounded in-memory payload capabilities;
- consumes the same shared envelope; and
- lowers the Document through its existing Browser call-graph projection.

Host-specific Markdown, tables, JSON rows, Mermaid, navigation, and
interaction remain outside Sections.

Platform member call graphs retain their existing Platform-owned operation.

## Required gates

Release gates prove:

1. the inspection prepares traversal and every edge before invoking the lower
   operation;
2. root selection and traversal target remain distinct;
3. the operation-local Workspace contains no unrelated package participants;
4. available Content drops process-local Workspace occurrence identity while
   retaining route and graph evidence;
5. every expected non-available lower outcome remains typed Content;
6. resource identity and Outcome classification are stable and the portable
   projection is explicitly `NotSupported`;
7. equivalent CLI and Browser plans consume equal baseline envelopes;
8. the real `Microsoft.Extensions.Http.Polly` scenario reaches Polly without
   manual dependency participants;
9. dependency and graph bounds remain visible when exhausted; and
10. cancellation, source ownership, cleanup precedence, and detached-result
    behavior remain unchanged.

## Non-claims

This inspection does not:

- change exact root package, asset, type, or member selection;
- change dependency traversal, version choice, or Platform pruning;
- add active Platform assemblies to package graph analysis;
- change external-focused graph topology;
- define a canonical Workspace portable projection;
- serialize live execution capabilities;
- combine package and Platform call-graph paths; or
- prescribe one host rendering.
