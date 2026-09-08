# Platform package supply policy

## Status

Implemented by #6268 as the policy slice of #6228 effort 1. No production
consumer adopts the result in this slice.

## Authority and exact claim

This owner defines one transform:

> Given one package acquisition coordinate and the platform prune inventory
> for its target, report the inventory evidence for that package identity and
> delegate to the platform only when the requested version is subsumed.

The transform consumes the owner-issued
[platform/package prune inventory](platform-package-pruning.md) and the
Packages-owned `PackageCoordinate`. It does not redefine either input.

It owns:

- requiring the coordinate to satisfy its Packages-owned validation contract;
- requiring an explicitly supplied coordinate framework to match the
  inventory target;
- interpreting an unresolved floating coordinate as `NotComparable`;
- preserving family and supplied-version evidence when an inventory entry
  exists, including for non-delegating results; and
- deriving `DelegatesToPlatform` from exactly one result: `Subsumed`.

It does not own:

- construction, projection, freshness, or comparison semantics of the prune
  inventory;
- validation or resolution of a package acquisition coordinate;
- package-to-assembly or package-to-platform-library correspondence;
- package-input authorship, processing evidence, or graph-edge existence;
- direct-reference exemptions or `RestoreEnablePackagePruning`;
- traversal, acquisition, ranking, presentation, or rendering; or
- CLI or browser/Wasm host behavior.

## Inputs and result

`PlatformPrunePolicy.Decide` accepts:

- one `PlatformPruneInventory`, already composed for the platform target; and
- one `PackageCoordinate`.

It returns `PlatformSupply`:

| Field | Meaning |
| --- | --- |
| `Subsumption` | The inventory owner's comparison result |
| `Family` | The shared framework carrying the entry, or null when absent |
| `SuppliedVersion` | The literal supplied version, or null when absent |
| `DelegatesToPlatform` | True only when `Subsumption` is `Subsumed` |

The result deliberately contains no platform library. A package identity does
not establish an assembly identity, and this transform receives no
owner-issued package-to-library correspondence.

## Decision

1. Validate the coordinate through `PackageCoordinateResolver.Validate`.
   Invalid package ids, target monikers, runtime identifiers, or exact versions
   fail visibly rather than becoming a pruning result.
2. If the coordinate names a framework different from the inventory target,
   fail visibly. Answering a `net8.0` question from a `net11.0` inventory is a
   different question, not a conservative result.
3. If the inventory has no entry for the package identity, return
   `NotSubsumed` with no family or supplied version.
4. If the coordinate version is unresolved, return `NotComparable` while
   preserving the entry's family and supplied version.
5. Otherwise consume the inventory comparison.
6. Delegate to the platform only for `Subsumed`. Both `NotSubsumed` and
   `NotComparable` keep the package path available to later consumers.

The runtime identifier does not change the decision because prune membership
and supplied-version comparison are per target framework, not per runtime
asset.

## Boundary scenarios

| Request against exact `net11.0` | Result | Delegates | Evidence |
| --- | --- | --- | --- |
| `System.Text.Json@9.0.0` | `Subsumed` | yes | Family and supplied version |
| `System.Text.Json@12.0.0` | `NotSubsumed` | no | Same entry remains visible |
| `System.Text.Json` with unresolved floating version | `NotComparable` | no | Same entry remains visible |
| `Newtonsoft.Json@13.0.3` | `NotSubsumed` | no | No entry evidence |
| Any package with no platform | `NotSubsumed` | no | Empty inventory |
| `net8.0` coordinate with a `net11.0` inventory | visible failure | no result | Targets disagree |
| Wildcard or range version in a coordinate | visible failure | no result | Coordinate is invalid |

The leapfrogging and unresolved rows are the pathological cases. A false
delegation would hide a newer package behind an older platform implementation;
both therefore resolve away from delegation without erasing the evidence that
explains why.

## Composition and adoption

This is host-neutral shared policy in `DotnetInspector.Packages`. It performs
no I/O and holds no state.

[#6228](https://github.com/richlander/dotnet-inspect/issues/6228) is the
14-effort production-adoption tracker. This policy completes the third part of
effort 1 after design #6233 and inventory #6239. That tracker assigns later
adoption to:

- effort 5 for the CLI;
- efforts 2, 3, and 8 for browser/Wasm ecosystem, routing, and demo behavior;
  and
- effort 9 for package dependency traversal.

[#6266](https://github.com/richlander/dotnet-inspect/issues/6266) separately
tracks the eight-step package-input evolution. Its step 6 adopts normalized
package input in this policy, step 7 adopts the composed result in the CLI, and
step 8 adopts it in inspect-web Browser/Wasm. This slice consumes the existing
`PackageCoordinate`; it does not claim those later package-input steps are
complete.

No rendering strategy applies: the result is not a section or broad
information domain. Any future presentation owner must define its typed rows,
Markout lowering, and host registration.

## Convention and analogous evidence

The policy follows the NuGet pruning baseline documented by
[platform/package pruning](platform-package-pruning.md#correspondence-with-the-nuget-specification):
an inclusive supplied-version ceiling and target-framework-specific
membership. It deliberately differs from restore by returning evidence rather
than mutating a graph, and by leaving direct-reference exemptions and restore
switches to graph and restored-artifact owners.

The simplest sufficient result retains the tri-state comparison plus the entry
evidence. A boolean alone would collapse "the platform has an older copy,"
"the request is unresolved," and "the target has no entry," preventing later
consumers from explaining or safely refining the result.

## Gates

The gates live in
`DotnetInspector.Services.Tests.PlatformPrunePolicyTests` and run with:

```text
dotnet run --project src/DotnetInspector.Services.Tests -c Release -- \
  --filter-class '*PlatformPrunePolicyTests'
```

| Property | Gate |
| --- | --- |
| A subsumed version delegates and preserves evidence | `SubsumedIdentityReportsFamilyAndSuppliedVersion` |
| A leapfrogging version preserves evidence but does not delegate | `LeapfroggingVersionKeepsTheEntryButDoesNotDelegate` |
| An unresolved version preserves evidence but does not delegate | `UnansweredComparisonKeepsTheEntryButDoesNotDelegate` |
| An absent identity supplies no evidence and does not delegate | `UnknownIdentitySuppliesNothing` |
| An empty inventory delegates nothing | `WorkspaceWithNoPlatformSuppliesNothing` |
| Invalid coordinate syntax fails through the coordinate owner | `InvalidCoordinateFailsBeforePolicy` |
| An explicit framework must match the inventory target | `CoordinateFrameworkMustNameTheInventoryTarget` |
| A runtime identifier does not change the decision | `RuntimeIdentifierDoesNotChangeTheAnswer` |

## Non-claims

This owner does not claim that delegation has been adopted by a graph, CLI, or
browser/Wasm consumer. It does not identify a platform assembly or library,
acquire a package or reference pack, or interpret pre- versus post-restore
artifacts.
