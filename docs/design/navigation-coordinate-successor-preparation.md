# Navigation coordinate successor preparation

## Status and exact claim

[Inspection Subject Navigation](inspection-subject-navigation.md) owns
Navigation subject retention and fresh-state preparation. This focused contract
addresses step 3 of
[#6751](https://github.com/richlander/dotnet-inspect/issues/6751):

> Given one current source Navigation state and one separately realized,
> already-populated destination Workspace, Navigation may prepare a fresh
> destination state by retaining the source's exact coordinate path through
> owner-issued correspondence. The operation consumes only explicitly supplied
> endpoints and does not require a Scope Replace association.

The public producer is `NavigationCoordinateSuccessorQuery.PrepareAsync`.
It accepts the source Workspace and Navigation state, exact retained source
binding, destination Workspace and complete Scope snapshot, exact destination
binding, Registry, and invocation-local availability provider.

The host must pass its current source Navigation slot. Navigation does not own
or inspect that host slot at this preparation boundary, so Workspace identity
validation is not a freshness check and a saved historical state is not an
eligible source.

The source and destination Workspaces must have distinct identities. The source
Navigation state and active occurrence must belong to the source Workspace.
The destination Scope and requested occurrence must belong to the destination
Workspace. Each binding must match its exact occurrence and retained immutable
package-content snapshot. Before Root access, the producer compares the
binding's opaque content-generation and selection identities with the
owner-issued association retained by that Scope occurrence; an equal
resource-free Root request alone is insufficient.

This is a pure preparation boundary over two already-realized endpoints. It
does not acquire a Package, populate or publish a Workspace, select a
successor, cut over a host slot, retire a predecessor, derive portable
definitions, or mutate either Scope.

## Demo

The motivating production path remains
`Avalonia@11.3.14 -> 12.1.2/net8.0`.
The source Navigation state selects `Avalonia.Data.MultiBinding` from
`Avalonia.Markup`; the destination package forwards it to its defining
`Avalonia.Base` Library.

```csharp
NavigationCoordinateSuccessorPreparationResult result =
    await NavigationCoordinateSuccessorQuery.PrepareAsync(
        sourceWorkspace,
        sourceNavigation,
        sourceBinding,
        destinationWorkspace,
        destinationScope,
        destinationBinding,
        registry,
        availability,
        cancellationToken);

if (result is NavigationCoordinateSuccessorPreparationResult.Prepared prepared)
{
    NavigationState destinationNavigation = prepared.Initialization.State;
    NavigationCoordinateRetentionResult evidence = prepared.Retention;
}
```

The destination state and every retained structural subject belong to
`destinationWorkspace`. The detached retention evidence identifies the exact
source and destination Package descriptors and correspondence outcomes. Neither
Workspace contains both versions.

The neighboring one-Workspace protected replacement remains implemented by
`NavigationScopeOperations.EvaluateCoordinateReplacementAsync` until the
portable consumer moves to this producer. It continues to use the same
retention policy but retains its Scope association, settlement, protection, and
completion behavior.

## Boundary and composition

The caller supplies two exact endpoint arguments; there is no ambient set of
active Workspaces. Per
[cross-Workspace composition and sharing](artifact-acquisition-and-workspaces.md#cross-workspace-composition-and-sharing),
the query enters the source Root operation under source authority. While that
access remains valid, it enters the destination Root operation under
destination authority.

Inside those operation scopes:

1. [Coordinate Library pairing](coordinate-library-pairing.md) validates the
   exact source and destination observations.
2. [Forwarded API coordinate correspondence](forwarded-api-coordinate-correspondence.md)
   binds a retained source declaration and resolves the destination only in the
   destination realization.
3. Navigation applies its existing level-local retention and fallback policy.
4. `NavigationTransitions.PrepareRestoration` creates a fresh state lineage
   bound to the destination Workspace.

The producer returns one closed result:

- `Prepared` carries the fresh `NavigationOperationInitialization` and detached
  `NavigationCoordinateRetentionResult`.
- `NotPrepared` preserves the existing typed
  `NavigationRestorationPreparationResult` together with the retention evidence
  that led to its requested initialization.
- `Failed` reports a typed endpoint, occurrence, binding, Root-access, or
  observation failure. It never returns a success-shaped empty state.

Cancellation uses the ordinary `OperationCanceledException` contract before or
during Root access. A failed endpoint projection preserves its owner-issued
`ArtifactRootFailure`; a pending projection carries no invented Root failure.
Root-access and observation failures likewise remain visible through their
owner-issued evidence.

## Identity and lifetime

No source subject is installed in the destination state. The retention policy
constructs destination subjects only from the destination Package evaluation
and owner-issued correspondence:

- Workspace and Package active subjects become their destination counterparts;
- exact Library, Type, and Member paths use destination registrations and
  declaration identities;
- fallback truncates to a destination ancestor; and
- an exact lens request is retained only when the active subject is retained
  exactly.

The fresh `NavigationState` has a new session, revision, generation, actions,
receipts, and effect authority. Source Navigation state, Root, registration,
binding context, operation lease, and publication authority do not transfer.
`NavigationCoordinateRetentionResult` contains detached descriptors and
evidence, not image access or an opener, and remains readable after both
Workspaces close.

## Adoption and retirement

This is step 3 of the five focused owner slices:

1. Cross-Workspace Library pairing — implemented by #8015.
2. Cross-Workspace API correspondence — implemented by #8037.
3. Navigation successor preparation — this contract and #8084.
4. Portable coordinate replacement constructs and composes a successor
   realization, moves its Avalonia production route to this producer, and
   retires the old portable `ReplaceScope` call path.
5. Workspace Scope removes Replace after its final production consumer is gone.

The test harness is this substrate slice's production host. Step 4 is the
production-consumer adoption slice. Browser installation remains tracked by
issues #5510 and #5511.

This contract adds no host state machine, Browser control, rendering path,
serialization format, scheduler, cache policy, or Workspace lifecycle policy.
Completed host-facing boundaries continue to use
`InspectionEnvelope<TContent>` where applicable.

## Required evidence

Release gates in `NavigationCoordinateSuccessorQueryTests` prove:

- a real forwarded Type and Member produce a fresh destination Navigation state
  with destination-owned subjects and the exact retained inspector request;
- source and destination correspondence evidence names the exact endpoint
  Package descriptors and remains readable after both Workspaces close;
- the source and destination Workspaces never contain the other's Package
  version;
- same-Workspace input, a foreign source state, a foreign destination Scope,
  and mismatched source or destination bindings — including independently
  acquired same-coordinate bindings with empty compile selection — fail with
  their exact typed reason;
- source and destination Root-access or observation failure remains typed;
- source and destination failed Root projections preserve their exact
  owner-issued `ArtifactRootFailure`; and
- the existing protected same-Workspace replacement suite remains green.

The focused implementation gate is:

```bash
dotnet run --project tests/DotnetInspector.Queries.Tests -c Release -- \
  --filter-class '*NavigationCoordinateSuccessorQueryTests'
```

The existing
`NavigationCoordinateReplacementTests` class remains the neighboring
regression gate.

NativeAOT and Browser/Wasm remain inherited platform requirements. The producer
uses existing SRM-only services and adds no dependency or platform exception.
