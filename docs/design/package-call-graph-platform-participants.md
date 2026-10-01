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
   stay unclassified, even when the Workspace has already admitted the
   .NET Runtime Platform population through its default ecosystem
   registrations.

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

1. derives the exact `PlatformFamilyTarget` for the traversal target from the
   focal scope receipt's Platform population and passes it with the matching
   `PlatformPruneInventory` to edge realization;
2. graphs pruned dependencies as the existing
   `PackageDependencyMemberCallGraphDestination.Platform` route, not as
   package participants;
3. builds one graph context that contains the package participants plus the
   certified Platform participants admitted for that exact target;
4. when the package-only context reports intrinsic CoreLib non-participation,
   runs `IntrinsicCoreLibraryWorkspaceContinuationOperation` once and, if it
   publishes, restarts the graph in the successor Workspace generation; and
5. classifies every node owned by a certified Platform participant as a
   Platform node in the outcome, alongside Package nodes.

The inspection never infers Platform membership, CoreLib entitlement, or
pruning from an assembly name, namespace, package id, or display text.

### Target alignment

The traversal target, the pruning target, and the Platform population target
are one exact target. If the Workspace's Platform population was realized for
a different target, such as the CLI's versionless family default, the
inspection requests the exact traversal target from the Platform population
owner. It does not reuse a population for another target. If the exact target
is unavailable, the graph stays package-only and reports that typed
unavailability.

### Generation restart

Graph construction is bound to one Workspace generation. Continuation
publication replaces that generation. The inspection discards the earlier
graph session, rebuilds once against the successor, and retains the
continuation receipt in the outcome. A rejected, cancelled, or failed
continuation returns the package-only graph with that typed outcome visible.
At most one continuation and one restart occur for each request.

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
lowers them to Markout for every format. Inspect Web renders the same
host-neutral outcome. This document adds no new host-specific rendering path
and does not change projection or lowering rules.

## Non-claims

- Implementing package-origin `AssemblyRef` to Platform binding (#8466).
  Pruning alone may leave a package-to-pruned-package call edge unresolved
  until #8466 lands. That reason must be typed and visible.
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

1. **Pruning inputs (CLI).** Supply the exact family target and prune
   inventory to edge realization through the inspection request; adopt in
   `graph calls`. This change is visible on its own: subsumed packages are
   no longer realized or graphed as package participants and appear as
   Platform routes. Dependency traversal may still read their manifests,
   because `PackageDependencyTraversalRequest` takes no pruning input.
   Pruning during traversal is a separate traversal-owner change and is not
   claimed here.
2. **Pruning inputs (Web).** Supply the same inputs from
   `PackagePruningExports`; verify the motivating URL realizes the same
   set.
3. **Mixed graph context.** Depends on PlatformHouse stage 4. Admit the
   certified exact-target Platform participants into the graph context, run
   the continuation and restart, and classify Platform nodes. Adopt in the
   CLI, including passing node classifications to
   `ExternalCallGraphOutputAdapter`, with the `Serialize~faeffed6d4` and `JsonDocument.Dispose` tests.
4. **Web adoption.** Adopt the same inspection outcome in Inspect Web,
   preserving the motivating URL, and hand off expansion-loop retirement to
   the ladder's stage 8.

Shared CLI and Browser/Wasm slices show both the C# and TypeScript call
sites in their PR demos.

## Acceptance evidence

| Case | Expected |
| --- | --- |
| STJ `netstandard2.0`, traversal `net11.0`, `Everything` | Subsumed packages pruned to Platform routes; zero unclassified CoreLib targets |
| `JsonDocument.Dispose`, same root | `Interlocked` and `Volatile` resolve to the exact certified CoreLib participant |
| Same root, `Self` | Package-only graph; intrinsic targets `OutsideOperationScope` |
| Exact Platform target unavailable | Package-only graph plus a typed unavailability; no fallback target |
| Continuation rejected or failed | Package-only graph plus the typed continuation outcome |
| Equivalent CLI and Web inputs | Equal host-neutral outcomes |

Each slice classifies its tests as PR-fast or slow. Tests that need an exact
Platform target run where an installed or package-backed target is available.
