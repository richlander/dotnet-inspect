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
- exact subject- and lens-transition outcomes;
- owner-issued navigation intent and post-result effect authority; and
- reconciliation after coordinate, Library, or inventory changes.

The owner returns one internally consistent snapshot. Consumers render that
snapshot and submit opaque action IDs from it for subject activation. Lens
activation submits an owner-issued lens identity back to this owner. Consumers
do not choose a default, infer a parent, substitute another subject after
failure, mint navigation intent, or reconstruct identity from labels.

## Boundaries

### Inputs

The owner consumes:

- one realized coordinate identity and its owner-issued root kind, label, and
  root-lens capabilities;
- admitted library identities, declaration order, primary-library preference,
  and library-level capability outcomes from workspace realization;
- available bounded API Type and Member inventories, their producer-issued
  deterministic navigation order, and their scoped inspection failures;
- product-owned accessibility descriptors;
- exact type-definition identities and member anchors;
- view-facet registry descriptors plus subject applicability and availability;
- an optional committed owner-issued lens identity;
- an optional prior navigation snapshot; and
- an optional explicit subject or lens request or typed resolution outcome.

These inputs remain owned by their existing components. This owner sequences
and composes them; it does not redefine their construction, validation,
lifetime, correspondence, or failure semantics.

### Outputs

The owner returns:

- the active structured subject identity;
- the Library context governing the Type inventory, or a typed unavailable or
  failed outcome;
- ordered applicable hierarchy descriptors;
- ordered Library subject descriptors;
- Type and Member navigation rows that wrap producer-owned inventory rows with
  activation descriptors;
- owner-ordered lens descriptors with their owner-issued identity, labels, and
  per-descriptor availability state;
- an effective, unavailable, or failed typed lens outcome;
- scoped diagnostics and partial-result evidence; and
- a typed transition or reconciliation outcome; and
- opaque navigation-intent and post-result effect authority for retained hosts.

### Non-claims

This owner does not define:

- coordinate acquisition, authorization, lifetime, or workspace membership;
- package, platform, project, file, or other root construction;
- assembly, type, member, or API identity internals;
- type/member inventory extraction or filtering vocabulary;
- view-facet registry identity construction or compatibility;
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
[Workspace definitions](workspace-definitions.md) owns portable view-facet
registry binding. This owner consumes those registry identities and scopes
their navigation descriptors to a subject; issue #4787 owns portable projection
and restoration.

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

The navigation-scoped lens identity is:

```text
InspectionLensIdentity(subject kind, view-facet registry identity)
```

The view-facet registry owns its stable identity, labels, and order. Inspection
Subject Navigation binds that identity to one structural subject and issues
the resulting lens identity in its descriptors. Equality uses both components;
a same-labelled or same-registry facet bound to another subject is not the same
navigation lens.

### Action IDs

Each navigation snapshot issues opaque action IDs for its non-active available
Root, Library, Type, and Member subject descriptors. An active available
subject descriptor is marked `Current` rather than receiving a no-op action.
An action ID is scoped to the snapshot generation and coordinate. A UI retains
and submits it without parsing.

Action IDs are deliberately separate from structured identity:

- the action ID is the safe consumer command token;
- the structured identity is what product owners resolve and what canonical
  projection may consume; and
- display labels are presentation only.

A stale, foreign, unknown, or duplicated action ID produces a typed rejection
and does not mutate the current state.

### Retained navigation session

A retained host keeps one product-owned navigation session with the installed
snapshot. The session issues opaque monotonic intent tokens; consumers request
and retain those handles but never mint, order, or compare their values.
Session initialization establishes the first current token without implying a
user transition.

The conceptual authority currencies are:

```text
NavigationIntentToken
NavigationEffectAuthority(snapshot generation, intent token)
```

Beginning an explicit subject, lens, coordinate, or canonical-restoration
intent issues a new token and immediately supersedes every older explicit or
maintenance operation. A coordinate operation obtains its token before
acquisition and returns the realized coordinate and typed resolution outcomes
to this session under that same token.

Inventory refresh and reconciliation initiated independently of an explicit
operation are snapshot maintenance, not new user intent. Standalone maintenance
captures the current intent token without advancing it. It cannot install while
an explicit operation under that token is unresolved: it waits to rebuild from
the explicit result or is discarded. Reconciliation required to complete an
explicit transition instead inherits that transition's token and completes
atomically inside it. Maintenance started earlier cannot replace a newer
explicit intent, and maintenance completed against an older snapshot basis
cannot replace a newer generation.

Every result that may replace or retain the installed snapshot is admitted by
the session against its captured basis and token. A current result returns
effect authority bound to the returned snapshot generation and the same intent
token. A host accepts the returned snapshot for rendering only while that
authority remains current and revalidates it again when each deferred visible
effect executes. Installing the result alone is not lasting authority to move
focus or surface an outcome.

## Navigation snapshot

One result conceptually contains:

```text
InspectionSubjectNavigationSnapshot
  generation
  coordinate
  activeSubject
  typeInventoryLibraryContext
  hierarchyDescriptors
  libraryDescriptors
  typeNavigationRows
  memberNavigationRows
  lensDescriptors
  lensOutcome
  diagnostics
```

The active subject is exactly one available structured identity. The lens
outcome is:

```text
Effective(identity, Preserved | Recommended | Fallback, diagnostics)
Unavailable(reason)
Failed(diagnostic)
```

When at least one lens is available, the effective outcome retains any failed
alternatives as diagnostics. When none is available, completed valid-empty
availability produces `Unavailable`; any unresolved availability failure
produces `Failed`.

The snapshot returns the owner-issued lens descriptor collection verbatim in
owner order. Each descriptor retains its lens identity, labels, capabilities,
and one of:

```text
Available(diagnostics)
Unavailable(reason)
Failed(diagnostic)
```

An `Effective` lens identity must match exactly one returned `Available`
descriptor. Failed alternatives remain identifiable in the descriptor
collection as well as summarized in effective-outcome diagnostics. An empty
descriptor collection is distinct from a non-empty collection whose entries
are unavailable or failed.

The Type-inventory Library context is a separate axis from the active subject:

- at Library, it is the active Library identity;
- at Type or Member, it is the defining Library;
- at Root, it is available `All libraries`, then the defining Library of the
  highest-ranked trustworthy Type when aggregate inspection is unavailable,
  then the primary or first available one-Library subject; and
- when no Library context can be established, it is explicitly unavailable or
  failed.

This context scopes Type navigation without activating Library or promoting
Root. It changes only through an owner-issued replacement snapshot.

### Hierarchy descriptors

The hierarchy contains at most one descriptor for each applicable kind in
Root, Library, Type, Member order. A descriptor carries:

- kind and producer-owned order;
- display and accessible labels;
- whether it is active;
- an optional recommended lens; and
- one discriminated state arm:

  ```text
  Available(target, Current | Activate(actionId), diagnostics)
  SelectionRequired(reason)
  Unavailable(reason)
  Failed(diagnostic)
  ```

Only `Available` carries a target structured identity. Its active descriptor
carries `Current`; a non-active activatable descriptor carries an opaque action
ID. Unavailable and failed descriptors never fabricate placeholder identities
or action tokens. `SelectionRequired` is a hierarchy-only contextual arm: one
or more activation rows exist, but product policy supplies no implicit single
target.

The Library descriptor is the active Type or Member's defining Library when
one exists. At Root, its available arm targets the recommended Library subject;
that target is the Type-inventory Library context above. When no such target
exists, the descriptor is unavailable or failed. At Library, its available arm
targets the active Library identity.

The Type descriptor is the active Type, the containing Type of an active
Member, or the owner-recommended Type when no Type is active. When none can be
established, the descriptor is unavailable or failed and carries no target.

Member has no arbitrary default. Its hierarchy descriptor is available only
when an exact Member is active, retained, or explicitly requested. When no
Member is committed but at least one Member activation row is available, the
hierarchy uses `SelectionRequired` with an owner-issued reason such as
`Choose a member`. It uses `Unavailable` only when no activatable Member exists,
and `Failed` when that fact cannot be established.

### Library descriptors

The Library selector consumes a separate ordered descriptor list:

1. `All libraries`, when aggregate Library inspection is supported;
2. the coordinate's primary library, when one is supplied; and
3. remaining admitted libraries in workspace declaration order.

Each entry uses the `Available`, `Unavailable`, or `Failed` activation arms. An
available entry carries a complete Library subject identity plus `Current` or
an action ID; unavailable and failed entries carry no target. A consumer does
not treat assembly count, package kind, filename, or list position as selection
policy.

Library selection is single-select. Arbitrary subsets are result filters, not
Library subject identities.

### Type and Member activation descriptors

The owner unconditionally wraps every bounded Type or Member inventory row with
the same discriminated activation shape:

```text
Available(target, Current | Activate(actionId), diagnostics)
Unavailable(reason)
Failed(diagnostic)
```

The inventory producer continues to own row identity, content, capabilities,
ordering, truncation, and failure semantics. Subject navigation preserves that
row and order verbatim inside the navigation wrapper. An available activation
target must exactly equal the wrapped row's owner-issued identity. The owner
never omits a row based on consumer filters or current activatability, never
joins by label or ordinal, and does not create another Type or Member inventory.

The active Type or Member row carries `Current`; every other activatable row
carries a generation-scoped action ID. An inventory refresh returns a
replacement snapshot generation and invalidates actions from the prior
inventory. Truncation diagnostics remain visible, and rows outside the bounded
inventory have no implied action.

These lists are navigation choices, not additional structural levels. The
Type activation list covers the bounded inventory for the snapshot's
Type-inventory Library context; the Member activation list covers the bounded
inventory for the current Type context. The hierarchy still contains at most
one contextual Type and Member descriptor.

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

`SelectionRequired` is not an availability result. It says the hierarchy level
has available choices but no product-owned default target. Consumers must not
render it as valid-empty or failure.

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
2. Library, when at least one Library subject is available; or
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
use the inventory producer's deterministic navigation order. That order is an
owner-issued input, not a comparison invented from metadata identity, display
text, or arrival sequence. Current UI filters, current search text, current
namespace/kind selections, and backend arrival order never participate.

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
recommends `All libraries` only when its descriptor is available. Otherwise it
chooses the primary or first available one-Library descriptor and retains any
aggregate unavailability or failure evidence. The initial Library lens is
References when available.

When no Library is available, the owner recommends Root. A package coordinate
uses Package Overview. Another coordinate uses its root owner's recommended
lens.

If the preferred lens is unavailable or failed, the owner selects the first
available lens in the subject owner's order and retains the non-success
evidence. If no lens is available, the subject remains active and the lens
outcome is `Unavailable` when every result is valid-empty, otherwise `Failed`.
The consumer does not choose a lens fallback.

## Explicit transitions

Activating a hierarchy, Library, Type, or Member descriptor submits its opaque
action ID against the snapshot generation that issued it.

Interactive consumers receive action IDs only for non-active available
descriptors. Product peers such as canonical restoration may instead submit a
structured subject identity through a typed product seam; that identity never
passes through UI display text.

The transition outcome is one of:

```text
Applied(snapshot, effectAuthority)
Unavailable(snapshot, reason, effectAuthority)
Rejected(snapshot, diagnostic, effectAuthority)
Failed(snapshot, diagnostic, effectAuthority)
Superseded
```

- **Applied** activates exactly the requested subject and returns a complete
  replacement snapshot.
- **Unavailable** preserves the active subject while it remains available and
  returns a complete snapshot reflecting the resolved current availability.
  When availability changed while an otherwise valid action was being
  resolved, this is a fresh generation and the unavailable descriptor carries
  no obsolete action ID.
- **Rejected** retains the current snapshot because the request is stale,
  foreign, malformed, or otherwise invalid for this generation.
- **Failed** retains the current snapshot because subject resolution or
  navigation failed.
- **Superseded** means a newer explicit intent invalidated the operation. It
  carries no snapshot or visible effect authority.

An explicit transition never silently activates a sibling, ancestor,
recommended Type, or Root. It may return a recommendation as explanatory data,
but applying that recommendation requires another explicit request.

Before returning `Unavailable`, the owner confirms that the committed active
subject remains available. If it became unavailable independently of the
requested action, the owner runs automatic same-coordinate reconciliation and
returns that consistent replacement snapshot with the request's unavailable
reason. If the active subject's availability or reconciliation fails, the
transition returns `Failed` with the prior snapshot instead. Any subject change
in this path is caused by invalidation of the committed subject, not fallback
from the unavailable request.

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

For Type and Member, `Unavailable` follows the same row as a missing subject.
For every subject kind, `Failed` follows
[Reconciliation failure](#reconciliation-failure).

No arbitrary Member replaces a missing Member.
Inventory refresh does not auto-promote an explicitly selected Root or Library
to Type.

### Coordinate variation

A version, framework, RID, or equivalent coordinate variation may reconcile a
committed subject only through typed owner-issued resolution:

| Current subject and resolution | New-coordinate result |
| --- | --- |
| Root | The new coordinate's Root. |
| `All libraries` | The new coordinate's `All libraries` when available. When unavailable or failed, activate Root and retain any failure diagnostic. |
| One Library resolves exactly and is available | The resolved one-Library subject. |
| One Library is definitively missing | `All libraries` when available, otherwise Root. |
| Type resolves exactly and is available | The resolved Type. |
| Type is missing but its defining Library resolves and is available | The highest-ranked trustworthy Type in that Library, then that Library when none exists. |
| Type and its defining Library are definitively missing | `All libraries` when available, otherwise Root. |
| Member resolves exactly and is available | The resolved Member. |
| Member is missing but its containing Type resolves and is available | The resolved Type. |
| Member and Type are missing but the defining Library resolves and is available | The highest-ranked trustworthy Type in that Library, then that Library when none exists. |
| Member, Type, and defining Library are definitively missing | `All libraries` when available, otherwise Root. |

For a one-Library, Type, or Member subject, a resolved identity whose subject
availability is `Unavailable` follows the same row as a definitively missing
subject. When no correspondence is available, availability or correspondence
fails, or correspondence is ambiguous or refused, no old subject is carried.
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
outcome attached to its subject transition. It consumes view-facet registry
identity, labels, order, and availability facts without redefining them, then
issues the subject-scoped navigation lens identity defined above.

Explicit lens activation is not a subject action-ID transition. A consumer
requests a new explicit intent token from the navigation session, then submits
the returned owner-issued lens identity with the issuing snapshot generation,
coordinate, and active subject identity to this owner under that token.

This owner consumes current subject-scoped view-facet descriptors and typed
lens-resolution facts, validates the exact requested identity, and returns the
same transition outcome family as subject activation. It does not delegate
navigation-snapshot construction or lens reconciliation to a lens renderer or
content producer.

An applied result commits the requested lens identity and returns a complete
replacement navigation snapshot. An unavailable result also returns a fresh
generation: it preserves the active subject and prior effective lens while
they remain valid, updates the requested lens descriptor to unavailable, and
applies ordinary lens reconciliation if independent facts invalidated the
prior effective lens. Rejected and failed results retain the prior snapshot.
Superseded carries no state or visible effect. The consumer never mutates
effective lens state locally.

- Preserve the committed current lens when the active subject identity is
  retained and that exact owner-issued lens identity remains available.
- Whenever no valid committed lens remains -- because the subject changed, the
  lens became unavailable, no lens was previously committed, or the supplied
  identity is not valid for this subject -- select the subject's recommendation
  when available, then the first available lens in owner-issued order.
- When an effective lens is selected, retain failed alternatives as diagnostics.
- When no lens is available, return `Unavailable` only when every lens is
  validly unavailable; return `Failed` when availability could not be
  established. The subject remains active in either case.
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
- obtains opaque intent tokens from the retained navigation session;
- submits owner-issued lens identities through this owner's lens transition
  rather than treating them as subject actions;
- renders wrapped producer-owned Type and Member rows with their supplied
  activation descriptor;
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
and transition model without adopting browser layout. Retained hosts opt into
the explicit navigation-session contract; stateless CLI commands compute one
result without implicit cross-command session state.

## Acceptance scenarios

### Ordinary package

1. Realize a package with a primary Library and at least one default-accessible
   Type.
2. Confirm that Type is recommended with the API lens.
3. Confirm that the defining Library, Root, and contextual Member descriptor
   are present in hierarchy order; Member is `SelectionRequired` when choices
   exist and `Unavailable` only for valid empty inventory.
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

### Failed aggregate Library

1. Supply failed `All libraries` availability and one available primary
   Library.
2. Confirm that the primary Library is recommended instead of the failed
   aggregate subject.
3. Confirm that the aggregate failure remains visible.

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

1. Retain the action ID and generation for a non-active available Member.
2. Make that Member unavailable before the action resolves.
3. Submit the retained action and confirm that the current subject remains
   active and the outcome is `Unavailable`.
4. Confirm that the returned snapshot has a new generation, reflects current
   availability, and carries no action ID for the unavailable Member.
5. Confirm that no ancestor or recommended Type is activated automatically.
6. Repeat while independently invalidating the committed active subject and
   confirm automatic reconciliation produces a consistent active descriptor,
   or failed reconciliation returns `Failed` with the prior snapshot.

### Direct Type and Member activation

1. Supply an active Type and another bounded Type inventory row.
2. Activate the non-recommended Type using only its action ID and generation.
3. Supply bounded Member rows for the active Type while no Member is committed.
4. Activate one Member using only its action ID and generation.
5. Confirm that each replacement snapshot marks the selected row `Current` and
   invalidates the prior generation's actions.
6. Include unavailable and failed bounded rows and confirm that each remains in
   producer order with its non-activatable wrapper arm.

### Explicit Root Type context

1. Keep Root active in a multi-Library coordinate with available aggregate
   inspection and confirm that `All libraries` scopes Type navigation.
2. Remove aggregate availability while retaining a trustworthy Type only in a
   non-primary Library and confirm that its defining Library becomes the
   Type-inventory context.
3. Remove every trustworthy Type and confirm that the primary or first
   available one-Library subject becomes the context.
4. Confirm throughout that Root remains active and the Library hierarchy
   descriptor targets the same context without activating it.

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
   of its available recommendation, then the first owner-ordered available lens
   when the recommendation is unavailable.
4. Start from a retained subject with no committed lens after a prior
   `Unavailable` lens result and confirm the same recommendation-then-owner-order
   selection when lenses become available.
5. Make every lens validly unavailable and confirm an `Unavailable` outcome
   with the subject still active.
6. Supply no available lens and at least one failed availability result and
   confirm a `Failed` outcome.
7. Supply an available fallback plus another failed lens and confirm an
   effective outcome retaining the failure as diagnostics.

### Stale action

1. Retain an action ID from one snapshot.
2. Replace the coordinate or navigation generation.
3. Submit the old ID and confirm a typed rejection with no state change.

### Operation ordering

1. Start inventory refresh, then begin an explicit subject transition and
   confirm that the refresh cannot replace the explicit result.
2. Begin a subject transition, then request refresh and confirm that maintenance
   waits to rebuild from the transition result rather than invalidating it.
3. Install a transition result, begin a newer intent before deferred focus
   executes, and confirm that the older effect authority no longer validates.
4. Begin canonical restoration and confirm that it supersedes older subject,
   lens, coordinate, and maintenance work.

## Required gates

The target contract remains unverified until implementation adds named gates
covering at least:

- `InitialRecommendation_PrefersTypeThenLibraryThenRoot`
- `InitialRecommendation_NeverChoosesMember`
- `TypeRecommendation_UsesPrimaryLibraryAndAccessibilityTiers`
- `TypeRecommendation_IgnoresConsumerFiltersAndArrivalOrder`
- `TypeRecommendation_UsesProducerOrderAcrossArrivalPermutations`
- `LibraryDescriptors_PlaceAggregateThenPrimaryThenDeclarationOrder`
- `InitialLibraryRecommendation_SkipsUnavailableAggregate`
- `UnavailableDescriptor_HasNoTargetOrActionId`
- `MemberHierarchy_SelectionRequiredDiffersFromUnavailable`
- `ActiveDescriptor_IsCurrentWithoutActionId`
- `MemberSnapshot_LeavesTypeAndLibraryAncestorsNonActive`
- `AvailableNavigationRow_TargetMatchesWrappedIdentity`
- `EveryBoundedInventoryRow_IsWrappedInProducerOrder`
- `TypeActivation_UsesActionIdForNonRecommendedRow`
- `MemberActivation_UsesActionIdBeforeAnyMemberIsCommitted`
- `ExplicitRoot_UsesProductIssuedTypeInventoryLibraryContext`
- `ToolsV2WithoutLibraries_RecommendsPackageRoot`
- `ValidEmptyTypes_DiffersFromTypeInspectionFailure`
- `PartialTypeInventory_DeterministicallyRetainsCandidateAndFailure`
- `ExplicitUnavailableTransition_RefreshesAvailabilityWithoutFallback`
- `UnavailableTransition_ReconcilesIndependentlyInvalidatedActiveSubject`
- `UnavailableTransition_PreservesSubjectOnlyWhileAvailable`
- `ForeignOrStaleActionId_IsRejected`
- `NavigationSession_IssuesMonotonicOpaqueIntentTokens`
- `ExplicitIntent_SupersedesOlderExplicitAndMaintenanceWork`
- `Maintenance_CannotInvalidateInFlightExplicitIntent`
- `MaintenanceResult_CannotReplaceNewerSnapshot`
- `TransitionEffectAuthority_UsesReturnedSnapshotGeneration`
- `CanonicalRestoration_BeginsExplicitIntent`
- `MissingMember_FallsBackToContainingTypeNotAnotherMember`
- `SameCoordinateUnavailable_FollowsMissingSubjectRules`
- `MissingTypeWithMissingLibrary_FallsBackToAggregateThenRoot`
- `CoordinateVariation_RetainsExactTypeAndLibrary`
- `CoordinateVariation_FailedAggregateFallsBackToRootWithDiagnostic`
- `CoordinateVariation_NonSuccessUsesIndependentInitialRecommendation`
- `CoordinateVariation_UsesTypedResolutionNotDisplayText`
- `ExplicitRoot_RemainsRootAcrossInventoryRefresh`
- `LensReconciliation_PreservesExactCommittedIdentity`
- `LensReconciliation_SubjectChangeUsesRecommendationThenOwnerOrder`
- `LensReconciliation_RetainedSubjectWithoutValidLensUsesTotalFallback`
- `LensOutcome_DistinguishesUnavailableFailedAndPartialFailure`
- `EffectiveLens_MatchesExactlyOneReturnedAvailableDescriptor`
- `LensDescriptors_PreserveOwnerOrderAndPerDescriptorState`
- `LensIdentity_ComposesSubjectAndRegistryIdentity`
- `LensRecommendation_DoesNotCrossSubjectKindsByLabel`
- `ExplicitLensActivation_IsResolvedBySubjectNavigation`
- `InspectWeb_SubmitsActionIdAndGeneration`
- `InspectWeb_CoordinateVariationSuppliesPriorNavigationSnapshot`
- `InspectWeb_UsesProductTypeInventoryLibraryContextAtRoot`
- `InspectWeb_UsesAncestorContextWithoutActivatingAncestors`
- `InspectWeb_UnavailableLensOutcomeHasNoSelectedTabOrPanel`
- `InspectWeb_FailedLensOutcomeHasNoSelectedTabOrPanel`
- `InspectWeb_EffectiveLensRetainsPartialFailureDiagnostics`
- `InspectWeb_DerivesLensTabsOnlyFromSnapshotDescriptors`
- `InspectWeb_LibraryListboxCommitsOnlyAvailableAction`
- `InspectWeb_LensActivationUsesOwnerIssuedLensIdentity`
- `InspectWeb_ObtainsIntentTokensFromNavigationSession`
- `InspectWeb_AppliedLensActivationInstallsReplacementSnapshot`
- `InspectWeb_UnavailableLensActivationInstallsRefreshedSnapshot`
- `InspectWeb_RejectedOrFailedLensActivationRetainsPriorSnapshot`
- `InspectWeb_StaleLensActivationCannotReplaceNewerSubjectSnapshot`
- `InspectWeb_LatestLensIntentWinsAcrossCompletionOrders`
- `InspectWeb_NewerNavigationIntentInvalidatesOlderSubjectResult`
- `InspectWeb_DeferredFocusRevalidatesEffectAuthority`
- `InspectWeb_SupersededOutcomeHasNoVisibleEffect`
- `InspectWeb_SubjectNonAppliedOutcomeReturnsFocusToInvoker`
- `InspectWeb_ConsumesSubjectOutcomeWithoutHostFallback`

Product-side gates should live with the eventual subject-navigation query.
Inspect-web needs non-vacuity integration gates that fail when the host resumes
choosing its own initial subject or fallback, submits Type or Member identities
instead of actions, omits the prior navigation snapshot during coordinate
variation, or derives a Root Type-inventory Library context.

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
