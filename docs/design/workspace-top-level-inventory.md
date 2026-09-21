# Workspace top-level inventory

## Status and scope

This is the target design for
[#7219](https://github.com/richlander/dotnet-inspect/issues/7219), the focused
Workspace adoption of the
[location and result cardinality pattern](command-transition-model.md#location-and-result-cardinality).
The host-neutral content, snapshot query, selection receipt, Share basis,
admitted L2 operation, and CLI adoption are implemented. Inspect Web adoption
remains unimplemented, so the overall target is partially implemented and
verified through the CLI production host.
The current CLI placement is transitional: the definition-first purpose and
portable output of the `workspace` command are owned by
[Workspace Definitions](workspace-definitions.md#definition-first-workspace-interchange).

The operator approved one host-neutral inventory with direct CLI and Inspect
Web adoption, and required the command experience to work from both an
already-realized Workspace and a Workspace packet. This document owns that
inventory only. It consumes, without redefining:

- Package membership, occurrence identity, order, and realization state from
  [Workspace Scope](workspace-scope-and-expansion.md);
- Exact Library, Package Prefix, and Ecosystem registration identity, order,
  and inertness from
  [Workspace registration](workspace-registration-and-call-graph-scope.md);
- admission, definition/scope association, cutover, drainage, and disposal
  from
  [active Workspace realization cutover](artifact-acquisition-and-workspaces.md#active-workspace-realization-cutover);
- active-subject and descendant selection from
  [inspection-subject navigation](inspection-subject-navigation.md);
- portable Workspace representation from
  [Workspace Definitions](workspace-definitions.md); and
- host-native controls and rendering from the CLI and Inspect Web owners.

## Authority and exact claim

This document is the normative owner of one claim:

> One admitted Workspace operation reports one complete typed inventory of
> committed Package occurrences and inert Exact Library, Package Prefix, and
> Ecosystem registrations from the same definition/scope observation. The
> inventory preserves each owner's identity, order, state, and duplicate
> policy; explicit filtering selects reported entries without activating,
> resolving, acquiring, or mutating Workspace content.

The inventory is a projection over existing currencies, not another Workspace
lifecycle:

- `WorkspaceDefinitionSnapshot` already associates one
  `InspectionWorkspaceIdentity`, one exact `WorkspaceRegistrationRevision`,
  and one exact `WorkspaceScopeRevision`;
- `WorkspaceRealizationOperationLease` already associates that definition with
  the matching `WorkspaceScopeSnapshot`, including Package realization state;
- the inventory operation consumes that authority and emits detached typed
  content and a resource-free selection receipt; and
- the design introduces no inventory revision, mutable collection, registry,
  cache, background task, or second combined snapshot.

The product needs one shared semantic answer across hosts. It does not need
another state machine to compute it.

## Host-neutral operation

The L2 operation surface is
`WorkspaceTopLevelInventoryOperation`. Its completed handoff follows the
shared-operation pattern:

```csharp
WorkspaceTopLevelInventoryExecution(
    InspectionEnvelope<WorkspaceTopLevelInventoryOutcome> Inspection,
    WorkspaceTopLevelInventorySelectionReceipt Selection)
```

The operation accepts an admitted `WorkspaceRealizationOperationLease` and an
inventory request plus one `WorkspaceTopLevelInventoryShareBasis`. It holds
that lease through construction of the complete detached envelope and selection
receipt. The returned execution retains no Workspace, lease, stream, metadata
reader, artifact root, or other disposable resource.

The resource-free Share basis is:

```text
WorkspaceTopLevelInventoryShareBasis
  WorkspaceIdentity
  RegistrationRevision
  ScopeRevision
  RequestKind            PacketInput | DefinitionInput | RealizedWorkspace
  Projection             Projectable(CanonicalPacket) |
                         NonProjectable(reason)
```

For complete packet or definition restoration, the host derives this basis
from the Definitions-owned `CompleteRestorationResult.Activated`.
`Projection` retains that owner's exact canonical packet or typed
non-projectable reason. The basis
copies no construction authority, effect authority, restoration recipe,
Navigation disposition, or live Workspace.

A projectable basis is constructible only from a
`WorkspaceDefinitionShareProjectionReceipt`. Workspace Definitions issues that
resource-free receipt with the source kind, exact Workspace and revision
identities, and its own canonical packet. The inventory surface never accepts a
definition snapshot and packet string independently, so a host cannot
accidentally pair inventory content from one definition with another
definition's Share projection.

An already-realized Workspace whose host did not retain a Definitions
activation projection remains a supported inventory input. Its caller creates
a `RealizedWorkspace` basis associated with the lease's exact definition and a
specific `NonProjectable(NoRetainedDefinitionProjection)` reason. Lack of Share
projection therefore does not make the inventory content unavailable or create
a second query path.

Construction is deliberately outside the operation. `WorkspacePlan` carries
registration and context intent but is not an acquired Package-membership
snapshot or population recipe. A host must populate complete explicit Package
membership through ordinary acquisition and Scope operations under its
construction authority, complete and activate the candidate, enter an
operation lease, and only then call the shared inventory operation. It must not
infer top-level Package membership from `WorkspacePlan.Contexts`.

The CLI and Inspect Web therefore share one semantic operation without
pretending that their construction inputs are the same. The CLI directly owns one ephemeral `InspectionWorkspace` for explicit Package
inputs and registrations. For packet input it supplies a CLI realization host
to the Definitions-owned complete restoration transaction. Inspect Web uses
its existing Definitions-owned restoration recipe and realization host.
Construction, acquisition, Scope publication, candidate completion, cutover,
admission, cancellation, retirement, and settlement keep their existing
owner-issued outcomes and cleanup contracts outside the inventory envelope.

The query validates that the Scope snapshot carries the same
`WorkspaceScopeRevision` as the `WorkspaceDefinitionSnapshot`. Production
operation admission should make a mismatch unreachable, but the query boundary
still checks the association it depends on. The operation then validates that
the Share basis names the same Workspace, Registration revision, and Scope
revision as the admitted definition snapshot. The operation is a finite
in-memory projection over already detached snapshot facts and has no
asynchronous work or cancellation boundary of its own.

### Relationship to definition-first Workspace interchange

The current CLI inventory consumer has two mutually exclusive construction
routes:

1. **Explicit construction.** CLI Package and registration options form one
   `WorkspacePlan` plus the explicit Package-membership construction inputs
   needed by the current invocation.
2. **Packet restoration.** One current-format canonical Workspace packet is
   decoded, strictly version-dispatched, completely realized, projected, and
   handed to the CLI host by Workspace Definitions. Group subscriptions,
   non-Package members, Package duplicate policy, Scope publication, and
   retained Navigation state remain owned by that complete restoration
   transaction rather than an inventory-specific adapter.

Both routes populate and admit one ephemeral realized Workspace through the
same owner operations before calling
`WorkspaceTopLevelInventoryOperation`. The inventory query cannot distinguish
which construction route produced its admitted authority, and equal realized
definition/scope state produces equal inventory content.

The routes differ only in available Share evidence:

- packet restoration derives the exact `PacketInput` basis from the
  Definitions-issued complete activation and projection;
- direct explicit construction supplies `RealizedWorkspace` with
  `NonProjectable(NoRetainedDefinitionProjection)`; and
- a direct host that independently obtained and retained a valid
  Definitions-owned projection may supply that associated `DefinitionInput`
  basis instead.

The second case describes the implemented inventory route, not the target
definition-first CLI contract. Direct Workspace authoring must instead retain a
Definitions-owned portable basis before optional realization. The inventory
operation still does not manufacture a `DefinitionInput` from `WorkspacePlan`
or serialize inventory rows.

A packet is inert input, not acquisition authority. Packet decode, migration,
Registry resolution, source authorization, acquisition, Scope publication,
projection, cancellation, and cleanup retain the typed outcomes and ordering
defined by Workspace Definitions and their owning services. Failure before
operation admission does not become an inventory `Unavailable` result.

The target `workspace` command authors or transforms portable definition state
and may use this inventory as a summary, preview, or verification observation.
Library, Type, Member, and other noun selection belongs to commands that
consume the packet as aggregate context. Existing `--active-package`
Navigation is transitional and does not define the target command boundary.

Accepting a packet does not create an inventory-specific packet format or
restoration path. The command consumes the current canonical packet format
through the complete restoration transaction owned by Workspace Definitions.
That transaction installs the committed View and Navigation state before
operation admission; the current CLI surface still limits packet invocations
to inventory controls.

## Terminal outcomes

`WorkspaceTopLevelInventoryOutcome` is a closed family:

- `Available` carries one `WorkspaceTopLevelInventoryDocument`;
- `Rejected` carries `InvalidFilter`; and
- `Unavailable` carries `InvalidAuthority` when the definition and Scope
  observation do not share the required revision basis, or
  `InvalidShareBasis` when the projection basis names different Workspace
  state.

Validation is ordered:

| Condition after admission | Outcome |
| --- | --- |
| Mismatched definition/Scope basis | `Unavailable(InvalidAuthority)` |
| Matching authority and mismatched Share basis | `Unavailable(InvalidShareBasis)` |
| Matching basis and invalid or unsupported filter | `Rejected(InvalidFilter)` |
| Matching basis and valid request | `Available` |

Authority validation precedes request validation. A call with both mismatched
authority and an invalid filter returns `Unavailable(InvalidAuthority)`.
Share-basis validation follows authority validation and precedes filter
validation.

No-active-realization, construction, acquisition, Scope publication, cutover,
admission, cancellation, and settlement are not inventory outcomes because the
operation has not been admitted in those cases. Each host preserves the
existing typed outcome from the owner whose stage failed. Diagnostics disclose
additional neighboring limitations but never decide success, completeness,
retry, exit status, or navigation.

## Document and entry family

The available `WorkspaceTopLevelInventoryDocument` contains:

- the normalized filter;
- the total entry count before filtering;
- the selected entry count after filtering;
- the selected ordered vector of `WorkspaceTopLevelInventoryEntry`; and
- an optional detached Package preparation projection.

Every entry carries:

- a `WorkspaceTopLevelInventoryEntryKey`;
- its one-based source order in Package Scope or the registration revision.

`WorkspaceTopLevelInventoryEntry` is a closed typed family:

| Entry | Owner-issued content | Reported state |
| --- | --- | --- |
| Package | Closed Package projection defined below | Ready, Pending, or Failed |
| Exact Library | Closed Exact Library projection | Registered and inert |
| Package Prefix | Closed Package Prefix projection | Registered and inert |
| Ecosystem | Closed Ecosystem projection | Registered and inert |

Presence in a registration arm means **registered**, not loaded, matched,
expanded, or active. Presentation may spell the state as `Registered`, but the
semantic arm retains the owner-issued declaration.

Ecosystem is included even though the motivating examples named Packages,
exact Libraries, and package prefixes. It is already a member of the closed
Workspace registration vocabulary and participates in Registered scope. A
complete top-level inventory must not silently hide that existing Workspace
state. This inclusion reports the declaration only; it adds no Ecosystem
realization or navigation behavior.

### Registration projections

Each registration arm is a closed serialization-ready projection:

```text
WorkspaceTopLevelExactLibraryEntry
  Key
  SourceOrder
  Coordinate

WorkspaceTopLevelPackagePrefixEntry
  Key
  SourceOrder
  Prefix

WorkspaceTopLevelEcosystemEntry
  Key
  SourceOrder
  Id
  NamespaceRoots
  CorePackages
  Populations
  HasIntegrationScanner
```

The Exact Library and Package Prefix arms retain their owner-issued portable
value. The Ecosystem arm projects its canonical ID, ordered namespace roots,
ordered unversioned core Package coordinates, and ordered closed population
declarations. `HasIntegrationScanner` reports only whether the declaration has
that contribution; the inventory never retains, serializes, or invokes
`EcosystemIntegrationScannerBinding`.

### Package projection

`WorkspaceTopLevelPackageEntry` is a closed serialization-ready projection:

```text
WorkspaceTopLevelPackageEntry
  Key
  SourceOrder
  PackageId
  PackageVersion
  Producer
  RequestedTargetFramework
  SelectedTargetFramework
  EffectiveTargetFramework
  RuntimeIdentifier
  AssetSelectionStatus
  State
```

The fields project the matching `WorkspacePackageDescriptor`:

- `PackageId` and `PackageVersion` retain the resolved canonical identity;
- `Producer` retains the credential-free content-cache producer key from
  `RealizedMemberCoordinate.Package`;
- requested, selected, and effective target frameworks remain distinct;
- `RuntimeIdentifier` retains the requested runtime identifier when present;
  and
- `AssetSelectionStatus` retains the Package asset-selection result.

The projection does not serialize `WorkspacePackageOccurrenceIdentity`,
`ArtifactRootCorrespondence`, `ArtifactRootGenerationReference`, publication
identity, physical composition identity, an artifact root, or a binding.

Package state is another closed projection:

```text
WorkspaceTopLevelPackageState
  = Ready
  | Pending
  | Failed(ArtifactRootFailure)
```

`Ready` excludes the physical generation reference. `Pending` carries no
owner-private evidence. `Failed` retains the stable `ArtifactRootFailure` value
needed to explain the row.

The optional document-level preparation projection is:

```text
WorkspaceTopLevelPreparation
  Kind
  RequestedPackageCount
  Deadline
```

It projects those three fields from `WorkspaceScopePreparationDescriptor` and
excludes Workspace identity, publication operation identity, publication base,
physical composition identity, exact cancellation action, and disposable
authority.

### Document-local entry key

`WorkspaceTopLevelInventoryEntryKey` is a serialized deterministic scalar
unique within one complete document. Its canonical form identifies the source
sequence and zero-based source offset, for example `package:0` or
`registration:2`.

The key:

- is assigned before filtering and survives filtered views unchanged;
- supports equality and event correlation within the transported document;
- may be returned by Browser interaction to the managed host;
- has no meaning without the matching selection receipt; and
- is not a Package, registration, Navigation, cross-operation, or
  cross-process identity.

A later inventory operation may issue the same scalar for a different entry.
The receipt and exact definition association, not the scalar alone, prevent
retargeting.

## Ordering and overlap

No current owner issues an authored interleaving between Package Scope order
and registration order. The inventory defines one canonical presentation order
without claiming such authorship:

1. Package entries in `WorkspaceScopeRevision` occurrence order.
2. Registration entries in `WorkspaceRegistrationRevision` order.

Filtering preserves entry keys, source order, and the selected entries'
relative canonical vector order; it does not renumber entries into a new
semantic identity.

Existing duplicate rules remain authoritative:

- Package Scope owns Package duplicate and coalescing behavior;
- Workspace Plan owns duplicate rejection within each registration arm; and
- Package membership and registration are independent.

Consequently, an admitted Package and an Exact Library registration naming a
Library within that Package both appear. A Package Prefix that matches an
admitted Package also appears. Those entries report different owner-issued
facts; deduplicating them would erase the distinction between content present
in Scope and inert intent registered for future use.

## Filtering

The first shared filter contract is entry-kind selection over `Package`,
`ExactLibrary`, `PackagePrefix`, and `Ecosystem`.

- An absent filter selects all entry kinds.
- A present filter must select at least one recognized kind.
- Repeated kinds normalize to one selected kind.
- Filtering is applied after the complete inventory has been constructed.
- A valid filter that matches no entries returns an available empty selection
  with the unfiltered total count and normalized filter.
- An unknown kind or explicitly empty kind set is
  `Rejected(InvalidFilter)`, not an empty inventory.

Filtering never acquires an artifact, activates a registration, expands a
prefix, changes Package preparation, mutates the Workspace, or changes
duplicate policy. Text search and field predicates require a later focused
extension justified by a concrete experience; hosts must not implement them by
silently changing the shared semantic baseline.

## Selection receipt and drill-down

The inventory identifies what can be selected; it does not perform selection.
Navigation remains the owner of active-subject and descendant drill-down.
Registration realization remains the owner of converting inert intent into
usable content.

`WorkspaceTopLevelInventorySelectionReceipt` provides the exact in-process
association:

- it carries the Workspace identity plus the registration and Scope revision
  identities from the definition snapshot used by the query;
- it maps each document-local entry key to the corresponding owner-issued
  Package occurrence identity or exact registration arm and source index;
- it does not retain `WorkspacePlan`, `WorkspaceRegistration`, a registration
  declaration, or an integration scanner binding;
- it contains no live resource and is never serialized as part of the
  envelope; and
- lookup rejects a key absent from that exact receipt.

The execution carries a non-empty receipt only with `Available`. `Rejected`
and `Unavailable` carry an empty receipt. An available receipt maps exactly the
entries in the returned selected vector; filtered-out keys cannot resolve
through that receipt.

The CLI may immediately resolve its existing one-based Package occurrence
selector through the receipt for the same invocation. Inspect Web retains the
receipt in managed state and exposes the serialized document key through a
host-issued action token. Before invoking Navigation or registration
realization, a later action enters current operation authority and requires the
receipt's exact definition snapshot to remain active. A changed definition
produces a typed stale-selection outcome instead of retargeting the action by
row number, coordinate, or display text.

The inventory does not define direct activation syntax for Exact Library,
Package Prefix, or Ecosystem registrations. Those controls belong to their
focused host and realization adoptions.

## Rendering and production consumers

CLI and Inspect Web consume the same
`WorkspaceTopLevelInventoryExecution`. Neither host may read Scope and
registration collections separately to reconstruct the inventory.

The CLI lowers the typed document through Markout. Its default inventory table
uses a compact common row shape such as `Kind`, `Location`, and `State`;
kind-specific detail remains available at higher verbosity. Vector position
already communicates presentation order, so the default table does not spend a
column on a universal index.

The existing one-based `--active-package` selector resolves Package source
order through the receipt and remains a valid test of exact selection
association. It is transitional CLI behavior rather than the target route to
Library, Type, or Member inspection. Registrations do not acquire a shared
numeric selector, and Inspect Web does not show an index column.

Inspect Web renders the same typed document in the existing Workspace surface.
It may use host-native interaction and styling, but it does not add a second
Workspace model, persistent global Workspace chrome, or simultaneous active
Workspace switching. Its managed host enters the existing active realization
operation and lowers the detached envelope for the Browser.

JSON and other structured formats retain the typed entry discriminator and
kind-specific content rather than flattening entries into the CLI's visual row
shape.

## Demo

The production-adoption PRs must demonstrate a real mixed Workspace containing
at least:

- `System.Text.Json@10.0.0`;
- `Markout@0.35.2`;
- one Exact Library registration;
- the `Microsoft.Extensions.` Package Prefix registration; and
- one Ecosystem registration.

The default CLI rendering is expected to resemble this mockup; final option
spelling belongs to the CLI adoption:

```text
Workspace

Kind            Location                                      State
Package         System.Text.Json@10.0.0                       Ready
Package         Markout@0.35.2                                Ready
Exact Library   System.Text.Json@10.0.0/System.Text.Json.dll  Registered
Package Prefix  Microsoft.Extensions.                         Registered
Ecosystem       dotnet                                        Registered
```

A kind-filtered view retains entry keys and order while explaining the
selection:

```text
Workspace
Filter: Exact Library, Package Prefix
Selected: 2 of 5

Kind            Location
Exact Library   System.Text.Json@10.0.0/System.Text.Json.dll
Package Prefix  Microsoft.Extensions.
```

The Exact Library entry remains present even though its Package is also
present: the first is inert registration intent, while the Package entry is
committed Scope content.

## Adoption and evidence

Implementation proceeds as independently reviewable slices:

1. **Host-neutral content and operation (implemented).** The outcome, document,
   typed entry family, entry key, selection receipt, kind filter, pure snapshot
   query, Definitions-owned Share projection receipt, Share basis, and admitted
   L2 operation are implemented in `DotnetInspector.Queries` and
   `DotnetInspector.Sections`.
2. **CLI inventory adoption (implemented).** Directly own one ephemeral
   `InspectionWorkspace` from explicit construction inputs, or supply that
   Workspace lifetime to Definitions-owned complete packet restoration,
   keeping explicit Package membership separate from
   `WorkspacePlan`; preserve every owner-issued packet, acquisition, Scope
   publication, Navigation restoration, projection, cancellation, and cleanup
   outcome. Replace Package-only row
   reconstruction with the shared operation, add focused
   registration/filter/packet controls, render through Markout, and preserve
   exact Package occurrence drill-down through the receipt. The CLI spells the
   controls as `--packet`, `--register-library`,
   `--register-package-prefix`, `--register-ecosystem`, and repeatable
   `--kind`; verbose human rows disclose Package-specific detail, while Share,
   kind filtering, and packet restoration remain top-level-inventory controls
   that reject Package Navigation under the focused deferral above. This proves
   the CLI can consume the shared inventory operation, but it does not settle
   the `workspace` command's primary definition/interchange role.
3. **Inspect Web adoption.** Consume the admitted operation from
   `BrowserWorkspaceRealizationHost`, render the same semantic document in the
   existing Workspace surface, and bind actions through retained managed
   authority and the selection receipt.
4. **CLI placement correction.** Under
   [#7379](https://github.com/richlander/dotnet-inspect/issues/7379), make
   direct Workspace input projectable, emit the portable definition as packet
   or URL, retain realization only for portable transformation, move noun
   inspection to packet-context noun commands, and retire
   `--active-package` only after those paths exist.

Packet input and result Share are separate concerns. A valid packet can restore
and inventory a Workspace even when the inventory request, such as a kind
filter, cannot yet be projected into a packet.

The operation composes Share from its exact basis and inventory request:

- an unfiltered request with `Projectable(CanonicalPacket)` returns
  `InspectionShare.Available` with that exact packet;
- an unfiltered request with a non-projectable basis returns
  `InspectionShare.NonProjectable` with the retained reason;
- a filtered request returns `InspectionShare.Available` only when Workspace
  Definitions can project that exact filter state over the retained basis; and
- otherwise a filtered request returns the specific
  `InspectionShare.NonProjectable` reason for unsupported inventory request
  projection.

The first implementation slice must state whether the existing Workspace
Definition packet can project the filter state. Until the portable projection
owner adopts any missing state, the operation returns the non-projectable
result rather than inventing a parallel share format. A packet-sourced
unfiltered result therefore retains its exact canonical input packet, while a
filtered result remains fully usable even when it is not yet shareable.

The current Workspace Definition packet does not project inventory kind-filter
state. The implemented operation therefore returns
`InspectionShare.NonProjectable` for every present inventory kind filter while
preserving available filtered content.

Required gates use Release configuration and include:

- query tests for exact revision association, ordering, every typed arm,
  overlap preservation, filter normalization, empty selection,
  combined-invalidity precedence, every Package state, and preparation
  projection;
- operation tests proving detached content and receipt lifetime, exact key
  lookup, invalid-authority and invalid-Share-basis rejection, retained packet
  projection, realized-Workspace non-projection, filtered Share composition,
  and release of the admitted lease after construction;
- serializer shape and round-trip tests for every outcome, entry, Package
  state, preparation, and diagnostic arm, with exact expected fields;
- CLI output tests for the real mixed Workspace, structured formats, filtering,
  overlaps, failed Package entries, Package occurrence drill-down, direct and
  packet-restored content equivalence, their expected non-projectable versus
  exact-packet Share difference, invalid packet and incompatible-option
  failures, and every coordinator failure or cleanup stage the CLI newly
  orchestrates; and
- Browser managed-host and browser-runtime tests showing the same mixed
  inventory, filter semantics, receipt-bound actions, and stale-action
  rejection.

The serializer tests are positive exact-shape gates for the named content
contract, not a repository-wide absence scan. The implementation adds no new
concurrent state transition or lifecycle currency, so no new TLA+ model is
required. Existing realization and revision models remain the gates for
admission and replacement behavior; the inventory's new properties are finite
snapshot-query contracts gated by the tests above.

## Non-claims

The top-level inventory does not:

- make a noun inspection command multi-coordinate;
- replace Package Query, Find, or descendant API inspection;
- resolve, activate, or expand a registration;
- define call-graph participation;
- create a portable identity for a live Package occurrence or registration;
- define cross-Workspace aggregation or simultaneous active Workspaces;
- promise text search or arbitrary field predicates;
- add WinMD support, inspected-assembly loading, Roslyn product dependencies,
  or execution of inspected code.
