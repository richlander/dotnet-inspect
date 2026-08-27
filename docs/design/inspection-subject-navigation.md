# Inspection subject navigation

This document owns product navigation among the structural subjects of one
realized inspection coordinate: root, Library, Type, and Member. It defines the
typed descriptors and outcomes that let a host present those subjects without
inventing identity, availability, defaults, or fallback policy.

The intended implementation is host-neutral and belongs below presentation,
normally in `DotnetInspector.Queries`. The architectural owner is Inspection
Subject Navigation even if its implementation spans more than one project.

## Status

This is the target contract for issue #4794. It is not implemented yet.
Everything in this document is unverified until the gates under
[Required gates](#required-gates) land.

The current browser transport exposes assemblies, types, accessibility
descriptors, and one default assembly ID, but no product-owned subject
navigation result. `prototypes/inspect-web/src/dotnet-inspect.ts` therefore
chooses its own default type, widens accessibility to admit that choice,
represents Package/root, Library, Type, and Member in separate mutable fields,
and reconciles them after navigation. The engine also rejects a package
projection with no assembly API surface, so a tools-v2 pointer package cannot
yet reach its intended root-only Package view.

Those are current implementation facts, not authority for the target policy.

## Decision

Inspection Subject Navigation owns:

- the ordered structural subject kinds applicable to one realized coordinate;
- composition of subject identities from adjacent owner-issued currencies;
- one product-issued active subject;
- hierarchy and Library-selection descriptors;
- applicability, availability, valid-empty, partial, and failed outcomes;
- initial subject and lens recommendations;
- exact subject-transition outcomes; and
- reconciliation after coordinate, Library, or inventory changes.

The owner returns one internally consistent snapshot. Consumers render that
snapshot and submit opaque action IDs from it. They do not choose a default,
infer a parent, substitute another subject after failure, or reconstruct
identity from labels.

## Boundaries

### Inputs

The owner consumes:

- one realized coordinate identity and its owner-issued root kind, label, and
  root-lens capabilities;
- admitted library identities, declaration order, primary-library preference,
  and library-level capability outcomes from workspace realization;
- available API type inventories and their scoped inspection failures;
- product-owned accessibility descriptors;
- exact type-definition identities and member anchors;
- subject-scoped lens descriptors and availability;
- an optional committed owner-issued lens identity;
- an optional prior navigation snapshot; and
- an optional explicit subject request or typed resolution outcome.

These inputs remain owned by their existing components. This owner sequences
and composes them; it does not redefine their construction, validation,
lifetime, correspondence, or failure semantics.

### Outputs

The owner returns:

- the active structured subject identity;
- ordered applicable hierarchy descriptors;
- ordered Library subject descriptors;
- an effective owner-issued lens identity or a typed lens-unavailable outcome;
- scoped diagnostics and partial-result evidence; and
- a typed transition or reconciliation outcome.

### Non-claims

This owner does not define:

- coordinate acquisition, authorization, lifetime, or workspace membership;
- package, platform, project, file, or other root construction;
- assembly, type, member, or API identity internals;
- type/member inventory extraction or filtering vocabulary;
- lens contents, rendering, or browser accessibility behavior;
- browser history, URL shape, canonical packet encoding, or restoration
  atomicity;
- source, metadata, analysis, decompiler, or package-query behavior; or
- package-source selection, credentials, provenance, or caching.

[Artifact acquisition and workspace
composition](artifact-acquisition-and-workspaces.md) owns the realized
coordinate and admitted artifacts. Root-capable package realization when no
compile Library exists is adjacent acquisition work tracked by
[#4829](https://github.com/richlander/dotnet-inspect/issues/4829).
[Type, member, and API representation](type-member-api-representation.md) owns
the identity currencies this contract composes.
[Inspect Web UI](inspect-web-ui.md) owns rendering and interaction.
[Workspace definitions](workspace-definitions.md) and issue #4787 own portable
projection and restoration.

## Structural subjects

Source context and structural focus are different axes. A package, platform,
project, or file coordinate says where inspection input came from. The active
subject says what the user is inspecting inside that coordinate.

The subject kinds are ordered:

1. **Root** - the coordinate's product-owned root subject, such as Package.
2. **Library** - all admitted libraries when aggregate inspection is supported,
   or one admitted library.
3. **Type** - one exact type definition in one admitted library.
4. **Member** - one exact API member in one Type.

Root is the subject identity. Package Overview is a package root's recommended
lens, not another subject, and lens labels do not participate in subject
identity equality.

This is a navigation grammar, not a requirement to visit every intermediate
level. An explicit Type or Member identity may be activated directly.

Root is always applicable once the coordinate is realized. A lower level is
applicable when the coordinate kind admits that structural domain, even when
the current inventory is validly empty. Applicable-but-empty levels remain in
the hierarchy with an unavailable reason. A structurally unsupported level is
omitted rather than represented as unavailable.

For a managed package coordinate, Library, Type, and Member are applicable even
when the package contributes no inspectable compile library. This is what lets
a tools-v2 pointer package explain why its only available subject is Package.

## Identity

The conceptual identity family is:

```text
InspectionSubjectIdentity
  = Root(coordinate root identity)
  | Library(AllLibraries | OneLibrary(acquired library identity))
  | Type(acquired type-definition identity)
  | Member(acquired API-member identity)
```

The exact implementation type names are not fixed here. The semantic rules
are:

- every identity is bound to one realized coordinate;
- `AllLibraries` is an explicit aggregate identity, not a null library;
- one-Library identity uses the workspace's acquired assembly/library identity;
- Type identity composes that acquired library binding with the exact
  metadata-definition identity;
- Member identity composes the Type identity with the product-owned member
  anchor;
- equality never uses display text, list position, metadata token alone, or a
  filename alone; and
- an identity from another coordinate or generation is rejected rather than
  matched heuristically.

The structured identity is product currency. It is not itself a URL encoding.
Issue #4787 decides which structured identities are portable and how they are
projected.

### Action IDs

Each navigation snapshot issues opaque action IDs for its available
descriptors. An action ID is scoped to the snapshot generation and coordinate.
A UI retains and submits it without parsing.

Action IDs are deliberately separate from structured identity:

- the action ID is the safe consumer command token;
- the structured identity is what product owners resolve and what canonical
  projection may consume; and
- display labels are presentation only.

A stale, foreign, unknown, or duplicated action ID produces a typed rejection
and does not mutate the current state.

## Navigation snapshot

One result conceptually contains:

```text
InspectionSubjectNavigationSnapshot
  generation
  coordinate
  activeSubject
  hierarchyDescriptors
  libraryDescriptors
  lensOutcome
  diagnostics
```

The active subject is exactly one available structured identity. The lens
outcome is either an effective available identity, marked as preserved or
recommended, or a typed lens-unavailable result.

### Hierarchy descriptors

The hierarchy contains at most one descriptor for each applicable kind in
Root, Library, Type, Member order. A descriptor carries:

- kind and producer-owned order;
- display and accessible labels;
- whether it is active;
- an optional recommended lens; and
- one discriminated availability arm:

  ```text
  Available(target, actionId, diagnostics)
  Unavailable(reason)
  Failed(diagnostic)
  ```

Only `Available` carries a target structured identity and opaque action ID.
Unavailable and failed descriptors never fabricate placeholder identities or
action tokens.

The Library descriptor is the active Type or Member's defining Library when
one exists. At Root, its available arm targets the recommended Library subject;
when no such target exists, the descriptor is unavailable or failed. At
Library, its available arm targets the active Library identity.

The Type descriptor is the active Type, the containing Type of an active
Member, or the owner-recommended Type when no Type is active. When none can be
established, the descriptor is unavailable or failed and carries no target.

Member has no arbitrary default. It is available only when an exact Member is
active, retained, or explicitly requested. Otherwise it remains discoverable
with an owner-issued reason such as `Choose a member`.

### Library descriptors

The Library selector consumes a separate ordered descriptor list:

1. `All libraries`, when aggregate Library inspection is supported;
2. the coordinate's primary library, when one is supplied; and
3. remaining admitted libraries in workspace declaration order.

Each entry uses the same discriminated descriptor shape. An available entry
carries a complete Library subject identity and action ID; unavailable and
failed entries carry no target. A consumer does not treat assembly count,
package kind, filename, or list position as selection policy.

Library selection is single-select. Arbitrary subsets are result filters, not
Library subject identities.

## Applicability and availability

Applicability answers whether a subject kind belongs to the coordinate's
structural grammar. Availability answers whether the owner can provide a
trustworthy target now.

Availability has the three semantic arms defined by the descriptor shape.

- **Available** means an exact subject can be activated. Diagnostics may still
  disclose partial upstream evidence.
- **Unavailable** is a completed, valid result proving that no activatable
  target exists under the current coordinate or parent subject.
- **Failed** means the owner could not establish availability. It must not be
  rendered as a valid empty result.

Examples:

- a coordinate with no admitted Library still has an available Root; its
  Library, Type, and Member chain is unavailable, coordinate realization
  failure occurs before this snapshot exists, and root-lens failure remains a
  typed lens outcome rather than valid empty state;
- a package with no admitted compile libraries has an unavailable Library
  descriptor, not a failed one;
- a successfully inspected library with zero types has an unavailable Type
  descriptor;
- a failed API projection produces a failed Type descriptor unless another
  successful participant supplies a trustworthy Type target;
- a Type with zero members has an unavailable Member descriptor; and
- partial multi-library inspection may still make Type available while
  retaining every participant failure in diagnostics.

## Initial recommendation

An explicit caller-supplied subject is not an initial recommendation. It is an
exact transition request and follows [Explicit transitions](#explicit-transitions).

When a realized coordinate has no committed subject, the owner recommends:

1. Type, when at least one trustworthy Type candidate exists;
2. Library, when at least one Library subject exists; or
3. Root.

Member is never implicitly recommended.

### Type recommendation

The Type candidate ranking is deterministic and product-owned:

1. Types in the coordinate's primary library and a default accessibility
   bucket.
2. Types in another library and a default accessibility bucket.
3. Types in the primary library outside the default accessibility buckets.
4. Types in another library outside the default accessibility buckets.

Within a tier, libraries use primary-then-workspace declaration order and Types
use exact metadata-definition identity order. Current UI filters, current
search text, current namespace/kind selections, and backend arrival order never
participate.

A non-default-accessibility Type remains a valid recommendation when it is the
only trustworthy Type candidate. The active subject is independent of a
consumer's result filters; a consumer must not substitute a different Type
because its current filters would hide the returned identity.

If at least one participant supplied a trustworthy candidate and another
participant failed, the owner recommends the highest-ranked trustworthy
candidate among the successful participants and retains every failure as
partial-result evidence. A producer that cannot vouch for any candidate returns
failed Type availability instead of delegating the choice to a consumer.

The initial Type lens is API.

### Library and Root recommendation

When no Type candidate exists but a Library subject is available, the owner
recommends `All libraries` when aggregate inspection is supported, otherwise
the primary or first available Library. The initial Library lens is References
when available.

When no Library is available, the owner recommends Root. A package coordinate
uses Package Overview. Another coordinate uses its root owner's recommended
lens.

If the preferred lens is unavailable, the owner selects the first available
lens in the subject owner's order. If no lens is available, the subject remains
active and the result carries a typed lens-unavailable outcome. The consumer
does not choose a lens fallback.

## Explicit transitions

Activating a hierarchy or Library descriptor submits its opaque action ID
against the snapshot generation that issued it.

Interactive consumers receive action IDs only for available descriptors.
Product peers such as canonical restoration may instead submit a structured
subject identity through a typed product seam; that identity never passes
through UI display text.

The transition outcome is one of:

```text
Applied(snapshot)
Unavailable(snapshot, reason)
Rejected(snapshot, diagnostic)
Failed(snapshot, diagnostic)
```

- **Applied** activates exactly the requested subject and returns a complete
  replacement snapshot.
- **Unavailable** preserves the active subject and returns a complete snapshot
  reflecting the resolved current availability. When availability changed
  while an otherwise valid action was being resolved, this is a fresh
  generation and the unavailable descriptor carries no obsolete action ID.
- **Rejected** retains the current snapshot because the request is stale,
  foreign, malformed, or otherwise invalid for this generation.
- **Failed** retains the current snapshot because subject resolution or
  navigation failed.

An explicit transition never silently activates a sibling, ancestor,
recommended Type, or Root. It may return a recommendation as explanatory data,
but applying that recommendation requires another explicit request.

Selecting `All libraries` or one Library activates that Library subject. It
does not also select a Type. Selecting a Type or Member directly is allowed and
returns its complete ancestor descriptors in the replacement snapshot.

## Reconciliation

Reconciliation is automatic product work after the facts underlying an already
committed subject change. It is not an excuse to reinterpret an explicit
failed request.

The owner receives typed identity-resolution and correspondence outcomes from
the owners of those operations. It does not infer continuity from package ID,
assembly name, type display text, member signature text, token, or ordinal.

### Same coordinate

When the coordinate identity is unchanged and the required availability facts
resolve successfully, the active subject follows this ordered table:

| Current subject | Reconciled result |
| --- | --- |
| Root | Retain Root. |
| `All libraries` | Retain it when aggregate inspection remains available; otherwise Root. |
| One Library | Retain it when available; otherwise `All libraries` when available, then Root. |
| Type | Retain it when available. If it is missing and its defining Library remains available, activate the highest-ranked trustworthy Type in that Library, then that Library when none exists. If the defining Library is unavailable, use `All libraries` when available, then Root. |
| Member | Retain it when available. If it is missing and its containing Type remains available, activate that Type. Otherwise apply the Type row using its containing Type and defining Library. |

No arbitrary Member replaces a missing Member.
Inventory refresh does not auto-promote an explicitly selected Root or Library
to Type.

### Coordinate variation

A version, framework, RID, or equivalent coordinate variation may reconcile a
committed subject only through typed owner-issued resolution:

| Current subject and resolution | New-coordinate result |
| --- | --- |
| Root | The new coordinate's Root. |
| `All libraries` | The new coordinate's `All libraries` when available, otherwise Root. |
| One Library resolves exactly and is available | The resolved one-Library subject. |
| One Library is definitively missing | `All libraries` when available, otherwise Root. |
| Type resolves exactly and is available | The resolved Type. |
| Type is missing but its defining Library resolves and is available | The highest-ranked trustworthy Type in that Library, then that Library when none exists. |
| Type and its defining Library are definitively missing | `All libraries` when available, otherwise Root. |
| Member resolves exactly and is available | The resolved Member. |
| Member is missing but its containing Type resolves and is available | The resolved Type. |
| Member and Type are missing but the defining Library resolves and is available | The highest-ranked trustworthy Type in that Library, then that Library when none exists. |
| Member, Type, and defining Library are definitively missing | `All libraries` when available, otherwise Root. |

A resolved identity whose subject availability is `Unavailable` follows the
same row as a definitively missing subject. Failed availability follows the
non-success rule below.

For a one-Library, Type, or Member subject, when no correspondence is available,
or correspondence is ambiguous, refused, or failed, no old subject is carried.
The new coordinate uses the initial Type -> Library -> Root policy and retains
the non-success outcome as a diagnostic. An independently recommended subject
is not evidence that the ambiguous or failed correspondence matched it.

### Library inventory change

When an admitted Library is added, removed, or becomes unavailable, the same
rules apply. An active subject in an unaffected Library remains unchanged. A
removed defining Library falls back through `All libraries` or Root; it does
not borrow a same-named Library from another acquisition without a typed
resolution outcome.

### Reconciliation failure

For an unchanged coordinate, a reconciliation failure retains the prior
subject snapshot and surfaces the failure.

For a newly realized coordinate with no prior valid snapshot, Root is the
required fallback when no trustworthy lower recommendation can be produced,
and failed lower levels stay failed. Returning Root does not convert those
failures into valid empty inventories.

## Lens reconciliation

Subject and lens are separate axes, but subject navigation owns the lens
outcome attached to its subject transition. It consumes owner-issued lens
identities, order, and availability without defining them.

- Preserve the committed current lens when the active subject identity is
  retained and that exact owner-issued lens identity remains available.
- When the subject changes, select its recommended available lens.
- When a retained subject's committed lens becomes unavailable, select the
  first available lens in the subject owner's order.
- When no lens is available, return a typed lens-unavailable outcome while
  keeping the subject active.
- Never carry a same-labelled lens across subject kinds by display text.
- A failed or unavailable lens remains visible through its owner-issued
  outcome; the consumer does not choose another lens.

Library Metadata and Type Metadata are therefore distinct lens identities even
though they share a label.

## Consumer obligations

### Inspect Web

Inspect Web:

- renders descriptor labels, order, active state, availability, reasons, and
  diagnostics verbatim;
- submits opaque action IDs with their snapshot generation;
- renders the returned active subject consistently in the command, hierarchy,
  Library selector, lens strip, and content;
- does not use filters, package kind, assembly count, or display text to choose
  a subject; and
- does not perform fallback after `Unavailable`, `Rejected`, or `Failed`.

Focus, menus, listboxes, responsive composition, browser history, and visible
failure placement remain UI-owned.

### Canonical state

The canonical-state owner consumes structured subject identity and transition
outcomes. It decides portability, encoding, versioning, and atomic restoration.
Snapshot action IDs are never serialized.

### Other hosts

A CLI, service, or another retained host may consume the same recommendation
and transition model without adopting browser layout. This contract does not
add implicit session state to stateless CLI commands.

## Acceptance scenarios

### Ordinary package

1. Realize a package with a primary Library and at least one default-accessible
   Type.
2. Confirm that Type is recommended with the API lens.
3. Confirm that the defining Library, Root, and unavailable Member descriptor
   are present in hierarchy order.
4. Confirm that changing UI filters does not change the active Type.

### Multi-library package

1. Realize a package with a primary Library and several additional libraries.
2. Confirm that `All libraries` is first, the primary Library is next, and
   remaining libraries use workspace declaration order.
3. Confirm that the Type recommendation uses the primary Library before another
   Library within the same accessibility tier.
4. Activate another Library and confirm that the result is that Library subject,
   not an implicitly selected Type.

### Package with libraries but no Types

1. Realize a package whose Library inventory succeeds and whose Type inventory
   is validly empty.
2. Confirm that Library with References is recommended.
3. Confirm that Type is unavailable with a valid-empty reason rather than
   failed.

### Tools-v2 pointer package

This scenario consumes the root-capable package realization tracked by
[#4829](https://github.com/richlander/dotnet-inspect/issues/4829); this owner
does not construct that acquisition result.

1. Realize a tools-v2 package coordinate with no admitted inspectable Library.
2. Confirm that Root is active with Package Overview as its recommended lens.
3. Confirm that Library, Type, and Member remain applicable but unavailable
   with owner-issued reasons.
4. Confirm that no host fabricates a Type from tool metadata, package files, or
   display names.

### Non-default-accessibility Type

1. Supply no default-accessibility Type but one trustworthy non-default Type.
2. Confirm that Type remains the initial recommendation.
3. Confirm that its identity is active even when a consumer's current filters
   would otherwise hide it.

### Partial Type inventory

1. Supply one successful Library Type inventory and one failed participant.
2. Confirm that the highest-ranked trustworthy Type is recommended.
3. Confirm that the participant failure remains visible as partial-result
   evidence.

### Explicit unavailable transition

1. Submit an exact Member identity through the typed product seam after that
   Member becomes unavailable.
2. Confirm that the current subject remains active and the outcome is
   `Unavailable`.
3. Confirm that the returned snapshot reflects current availability and carries
   no action ID for the unavailable Member.
4. Confirm that no ancestor or recommended Type is activated automatically.

### Reconciliation scenarios

1. Retain a Member while the same exact Member resolves after a coordinate
   variation.
2. Remove the Member while retaining its Type and confirm fallback to Type.
3. Remove that Type while retaining its defining Library and confirm a
   recommended Type in that Library or fallback to the Library.
4. Remove the Library and confirm fallback to `All libraries` or Root.
5. Repeat with ambiguous correspondence and confirm that the new coordinate
   uses its independent initial recommendation while ambiguity remains visible
   rather than accepted as identity.
6. Keep Root active across an inventory refresh and confirm that new Types do
   not auto-promote it.

### Lens continuity scenarios

1. Retain a Type and its available committed Metadata lens and confirm that the
   exact Type Metadata lens remains effective.
2. Change the subject to Library and confirm that the same `Metadata` display
   label does not carry the Type lens identity across subject kinds.
3. Make the retained subject's committed lens unavailable and confirm selection
   of the first available owner-ordered lens.
4. Remove every lens and confirm a typed lens-unavailable outcome with the
   subject still active.

### Stale action

1. Retain an action ID from one snapshot.
2. Replace the coordinate or navigation generation.
3. Submit the old ID and confirm a typed rejection with no state change.

## Required gates

The target contract remains unverified until implementation adds named gates
covering at least:

- `InitialRecommendation_PrefersTypeThenLibraryThenRoot`
- `InitialRecommendation_NeverChoosesMember`
- `TypeRecommendation_UsesPrimaryLibraryAndAccessibilityTiers`
- `TypeRecommendation_IgnoresConsumerFiltersAndArrivalOrder`
- `LibraryDescriptors_PlaceAggregateThenPrimaryThenDeclarationOrder`
- `UnavailableDescriptor_HasNoTargetOrActionId`
- `ToolsV2WithoutLibraries_RecommendsPackageRoot`
- `ValidEmptyTypes_DiffersFromTypeInspectionFailure`
- `PartialTypeInventory_DeterministicallyRetainsCandidateAndFailure`
- `ExplicitUnavailableTransition_RefreshesAvailabilityWithoutFallback`
- `ForeignOrStaleActionId_IsRejected`
- `MissingMember_FallsBackToContainingTypeNotAnotherMember`
- `MissingTypeWithMissingLibrary_FallsBackToAggregateThenRoot`
- `CoordinateVariation_RetainsExactTypeAndLibrary`
- `CoordinateVariation_NonSuccessUsesIndependentInitialRecommendation`
- `CoordinateVariation_UsesTypedResolutionNotDisplayText`
- `ExplicitRoot_RemainsRootAcrossInventoryRefresh`
- `LensReconciliation_PreservesExactCommittedIdentity`
- `LensRecommendation_DoesNotCrossSubjectKindsByLabel`
- `InspectWeb_ConsumesSubjectOutcomeWithoutHostFallback`

Product-side gates should live with the eventual subject-navigation query.
Inspect-web needs one non-vacuity integration gate that fails when the host
resumes choosing its own initial subject or fallback.

## Non-goals

This design does not:

- define a universal identity for every producer or coordinate;
- make root, Library, Type, and Member mandatory waypoints;
- make arbitrary multi-Library subsets structural subjects;
- select a default Member;
- make UI result filters part of subject identity;
- define portable URL fields or browser-history push/replace behavior;
- define Library, Type, or Member lens contents;
- authorize acquisition or expensive inspection work;
- reinterpret expected failures as empty inventories; or
- require non-browser hosts to retain interactive navigation state.
