# Package call graph with Platform participants

This document owns one claim: under the default `Everything` focal length,
the dependency-aware package member call graph traverses into the exact
target Platform population instead of stopping at package-only boundaries.

The owner is the `DotnetInspector.Sections` composition
`PackageDependencyMemberCallGraphInspection`, which already hands one
completed dependency-aware member graph to the CLI and Inspect Web
([Package dependency member call-graph inspection](package-dependency-member-call-graph-inspection.md)).
This document extends that composition. It does not redefine package
traversal, pruning, Platform realization, Platform certification, binding,
Workspace replacement, or call-graph topology. Each of those has its own
owner, listed under [Owners consumed](#owners-consumed).

Implementation and every adoption slice are tracked end to end by
[#9072](https://github.com/richlander/dotnet-inspect/issues/9072).

## Motivating case

The production Inspect Web view of
`System.Text.Json@11.0.0-preview.7.26381.103`, `netstandard2.0`, with the
product traversal target `net11.0`, shows the
`JsonSerializer.Serialize~faeffed6d4` call graph:

```text
Partial call graph: 9 call targets could not be classified against the
loaded package definitions.
```

The CLI shows the same result:

```text
dotnet-inspect graph calls System.Text.Json.JsonSerializer \
  Serialize~faeffed6d4 \
  --root-package System.Text.Json@11.0.0-preview.7.26381.103 \
  --root-tfm netstandard2.0 --tfm net11.0 --verbose
```

Two independent gaps cause the result:

1. **No pruning input.** The inspection builds
   `PackageHouseTargetContext.Exact(tfm)` without a Platform family target and
   creates `PackageDependencyEdgeRealizationRequest` without a
   `PlatformPruneInventory`. On `net11.0`, `System.Buffers`, `System.Memory`,
   `System.Runtime.CompilerServices.Unsafe`, and other subsumed packages are
   therefore realized and graphed as ordinary package participants.
2. **No Platform participants.** `PackageDependencyMemberCallGraphOperation`
   builds its graph context only from the realized package roles, so the
   operation returns `IntrinsicCoreLibraryContextNonParticipation`. Targets
   in `corelib` (`Interlocked`, `Volatile`, `Span<T>` helpers, and others)
   stay unclassified, even though the Workspace's default ecosystem
   registrations declare the .NET Runtime Platform population. Registration
   only makes that population eligible; it must still be realized for the
   exact target and admitted before a graph can use it.

`System.Text.Json.JsonDocument.Dispose`, the motivating case in the
[ladder](assembly-reference-resolution-ladder.md#intrinsic-corelib-from-a-package-context)
and in
[PlatformHouse certification](platform-house-reference-processing.md),
fails for the same reason. Both cases are preserved as product tests.

## Existing coverage

Several existing designs already plan this composition. This document
adds only the consumer slice that joins them.

| Concern | Owning design | Status |
| --- | --- | --- |
| Focal lengths; `Everything` admits registered ecosystems | [Workspace registration and call-graph scope](workspace-registration-and-call-graph-scope.md#call-graph-focal-lengths) | `MemberCallGraphFocalScopeReceipt` reports `Everything` with Platform populations |
| Pruning is PackageHouse's typed delegation | [Platform package pruning](platform-package-pruning.md), [supply-chain focus](member-call-graph-supply-chain-focus.md) | Implemented; not supplied by this inspection |
| Platform routes don't fabricate package participants | [Package dependency call-graph operation](package-dependency-call-graph-operation.md) | Defers to "a later Platform composition" (this document) |
| Intrinsic CoreLib target and `Self` scope behavior | [Assembly-reference resolution ladder](assembly-reference-resolution-ladder.md#intrinsic-corelib-from-a-package-context) | Steps 1-4 landed in #8791 |
| Graph restart after Workspace replacement | [Ladder: immutable generation realization](assembly-reference-resolution-ladder.md) | Consumer obligation; assigned to Call Graph |
| Certified Platform Library and CoreLib binding capability | [PlatformHouse reference processing](platform-house-reference-processing.md) stages 4-6 | Stage 2 landed; stage 4 open |
| Package-origin `AssemblyRef` to Platform binding | [Platform assembly-reference binding](platform-assembly-reference-binding.md), [routing](package-origin-assemblyref-supply-routing.md) | #8466 open |
| Mixed package/Platform contexts | [Workspace definitions](workspace-definitions.md) | Unsupported "until a product demo needs that composition"; this is that demo |

## Claim

For one `PackageDependencyMemberCallGraphInspectionRequest` whose effective
focal length admits a registered Platform population, the inspection:

1. builds the package-only predecessor graph through
   `PackageDependencyMemberCallGraphOperation`, as today, passing the
   `DotNetRuntime` prune inputs to edge realization when the
   [pruning precondition](#pruning-precondition) holds;
2. when that graph reports intrinsic CoreLib non-participation and the focal
   scope admits the `DotNetRuntime` population, evaluates
   `IntrinsicCoreLibraryPlatformApplicabilityQuery` for the exact
   `DotNetRuntime` target and runs
   `IntrinsicCoreLibraryWorkspaceContinuationOperation` once. The
   continuation realizes that exact-target population and admits it into a
   successor Workspace generation;
3. when the continuation publishes, discards the predecessor graph and builds
   one mixed graph against the published successor. Its context contains the
   package participants plus the certified `DotNetRuntime` participants;
4. classifies every node owned by a certified Platform participant as a
   Platform node in the outcome, alongside Package nodes; and
5. under that precondition, never realizes or graphs pruned dependencies as
   package participants in either graph. They are the existing
   `PackageDependencyMemberCallGraphDestination.Platform` route; the mixed
   graph binds calls into them to Platform participants.

The inspection never infers Platform membership, CoreLib entitlement, or
pruning from an assembly name, namespace, package id, or display text.

### Target alignment

All Platform work shares one target framework: the traversal target. Within
it, each selected family keeps its own exact target.

- The focal scope receipt names families, not versions. The Platform owner
  selects the exact `DotNetRuntime` target at the traversal framework.
- This design admits only the `DotNetRuntime` family, the one that owns the
  intrinsic CoreLib target. The default curated Workspace also selects
  `AspNetCore`, which is an independent House request with its own exact
  target, as
  [PlatformHouse reference processing](platform-house-reference-processing.md)
  requires. Admitting `AspNetCore` participants into the successor is a
  non-claim; it follows the #8466 binding route, not this continuation.
- Pruning uses only the `DotNetRuntime` family's inventory and exact target.
  `PackageHouseTargetContext` carries one Platform target, and
  `PackageHousePruningReceipt` rejects a delegation whose supplying family
  differs from it. Pruning `AspNetCore`-supplied packages would need a
  separately owned PackageHouse contract change and is not claimed here.
- A population realized for another target, such as the CLI's versionless
  family default, is not reused.
- If the exact `DotNetRuntime` target is unavailable, the predecessor graph
  is the result, with that typed unavailability reported.

### Pruning precondition

Pruning removes a package participant, and it must also avoid that package's
acquisition. So the inspection decides once, before it realizes the
predecessor, using only request-time facts. It prunes only when all of these
hold:

- the exact Workspace revision contains the selected `ecosystem.runtime`
  registration, which is the only pruning switch defined by
  [Platform package pruning](platform-package-pruning.md#explicit-net-runtime-ecosystem-presence-is-the-switch);
- the focal scope admits the `DotNetRuntime` population, and the host
  supplies its prune inventory for the exact `DotNetRuntime` target at the
  traversal framework; and
- package-origin `AssemblyRef` to Platform binding (#8466) is available, so a
  call from a package into a pruned package's assembly can bind to a Platform
  participant.

Otherwise the inspection does not prune, and keeps today's behavior: subsumed
packages remain package participants in both graphs.

The decision does not wait for Platform admission, because admission happens
only after the predecessor is built. The predecessor and the mixed graph use
the same pruned realization set, and neither ever realizes a pruned package.
A pruned call never simply disappears and is never counted as generically
unclassified:

- In the mixed graph, it binds to the admitted Platform participant, or
  carries the typed binding outcome from #8466.
- When the predecessor is the result, it carries its typed `Platform` route
  plus the reason the Platform was not admitted. That reason is the
  continuation outcome, exact-target unavailability, or no intrinsic CoreLib
  non-participation to continue from.

### Generation restart

Graph construction is bound to one Workspace generation. The package-only
predecessor graph is what proves CoreLib non-participation, so it is always
built first and never against a context that already has Platform
participants. Continuation publication replaces the generation. The
inspection discards the predecessor graph session, builds once against the
successor, and retains the continuation receipt in the outcome. At most one
continuation and one rebuild occur for each request.

- A rejected or failed continuation returns the predecessor graph with that
  typed outcome visible.
- Cancellation propagates. As the
  [inspection owner](package-dependency-member-call-graph-inspection.md)
  specifies, a cancelled request produces no envelope, whether cancellation
  happens during the predecessor graph, the continuation, or the rebuild.

### Focal lengths

- `Everything` and `SelfAndRegisteredEcosystems` admit the registered
  Platform population and follow this design.
- `Self` keeps today's package-only behavior and reports intrinsic targets
  as `OutsideOperationScope`, as the ladder specifies.

Only `Everything` exists in code today. The other values join this
composition when Call Graph adds them.

### Completeness

The "could not be classified" diagnostic stays, with a narrower meaning:
after this composition it counts only targets with no Package, Platform, or
typed-unavailable classification. When the focal scope admits the Platform,
the motivating case must report zero unclassified CoreLib targets. Any
remaining unclassified targets must carry a typed reason, such as an
`AssemblyRef` binding pending under #8466, rather than a generic count.

### Presentation boundary

The inspection outcome gains one typed fact per node: a Package or Platform
classification, where a Platform node carries its exact `PlatformFamilyTarget`
and certified Library identity. Pruned dependencies keep the existing typed
`Platform` destination route. Neither fact carries display text.

Hosts lower these facts at their existing call-graph boundaries, following
[call-graph projection](call-graph-projection.md). For `graph calls`, the
lowering boundary is `ExternalCallGraphOutputAdapter`, which today receives
only the `InspectionGraphDocument`. The CLI adoption slice passes the typed
node classifications to that adapter beside the document, and the adapter
emits them in every format: through Markout for the formats it lowers
through Markout, and in the direct JSON and JSONL serialization. Inspect Web renders the same
host-neutral outcome. This document adds no new host-specific rendering path
and does not change projection or lowering rules.

## Non-claims

- Admitting `AspNetCore` or other non-`DotNetRuntime` Platform participants.
- Implementing package-origin `AssemblyRef` to Platform binding (#8466).
  Pruning waits for it, per the [pruning precondition](#pruning-precondition).
- Implementing the CoreLib binding capability. This document consumes
  PlatformHouse stage 4 and does not add a name-based substitute.
- Changing pruning policy, Platform realization policy, or focal-length
  semantics.
- Adding Platform participants to `Self` graphs or to non-call-graph package
  views.
- Retiring the browser's `PlatformCallGraphExports` expansion loop. The
  ladder's stage 8 owns that retirement after Web adoption.

## Owners consumed

| Input | Owner |
| --- | --- |
| Focal scope and Platform population scope | `MemberCallGraphFocalScopeReceipt` (Queries) |
| Prune inventory and family target | PackageHouse pruning; hosts supply `InstalledPlatformPruneSource` (CLI) or `PackagePruningExports` (Web) |
| Exact Platform population | PlatformHouse via Workspace Platform admission |
| Intrinsic CoreLib applicability and successor publication | `IntrinsicCoreLibraryPlatformApplicabilityQuery`, `IntrinsicCoreLibraryWorkspaceContinuationOperation` (ResearchQueries) |
| Call edges and topology | `PackageRoleMemberCallGraphQuery` (Queries) |

## Adoption plan

Each slice is one reviewed PR with a production consumer, checked off on
[#9072](https://github.com/richlander/dotnet-inspect/issues/9072). The order keeps the
product usable after every slice.

1. **Mixed graph context (CLI).** Depends on PlatformHouse stage 4.
   Build the predecessor graph, run applicability and continuation, rebuild
   against the successor, and classify Platform nodes. Pass node classifications to
   `ExternalCallGraphOutputAdapter`. No pruning yet. Adopt in `graph calls`
   with the `Serialize~faeffed6d4` and `JsonDocument.Dispose` tests.
2. **Mixed graph context (Web).** Adopt the same inspection outcome in
   Inspect Web, preserving the motivating URL, and hand off expansion-loop
   retirement to the ladder's stage 8.
3. **Pruning (CLI).** Depends on slice 1 and #8466. Supply the
   `DotNetRuntime` prune inventory and exact target to the predecessor's
   edge realization under the pruning precondition. Subsumed packages are no
   longer realized or graphed as package participants in either graph, and
   their calls bind to Platform participants in the mixed graph. Dependency traversal may still
   read their manifests, because `PackageDependencyTraversalRequest` takes no
   pruning input. Pruning during traversal is a separate traversal-owner
   change and is not claimed here.
4. **Pruning (Web).** Supply the same inputs from `PackagePruningExports`
   and verify the motivating URL realizes the same set as the CLI.

Shared CLI and Browser/Wasm slices show both the C# and TypeScript call
sites in their PR demos.

## Acceptance evidence

| Case | Expected |
| --- | --- |
| STJ `netstandard2.0`, traversal `net11.0`, `Everything` | Zero unclassified CoreLib targets (slice 1); subsumed packages pruned and their calls bound to Platform participants (slice 3) |
| `JsonDocument.Dispose`, same root | `Interlocked` and `Volatile` resolve to the exact certified CoreLib participant |
| Same root, `Self` | Package-only graph; intrinsic targets `OutsideOperationScope` |
| Exact `DotNetRuntime` target unavailable | Predecessor graph plus typed unavailability; no fallback target |
| Continuation rejected or failed | Predecessor graph plus the typed continuation outcome |
| Cancelled during any phase | Cancellation propagates; no envelope |
| Pruning precondition unmet, including no `ecosystem.runtime` registration | No pruning; subsumed packages stay package participants |
| Pruned, then continuation does not publish | Pruned packages never realized; their calls carry the typed `Platform` route and non-admission reason |
| `AspNetCore`-subsumed package dependency | Not pruned; remains a package participant |
| Equivalent CLI and Web inputs | Equal host-neutral outcomes |

Each slice classifies its tests as PR-fast or slow. Tests that need an exact
Platform target run where an installed or package-backed target is available.
