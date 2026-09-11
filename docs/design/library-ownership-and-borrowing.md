# Library Ownership and Borrowing

## Status

This document is the normative owner for the lifetime, reference, operation
authority, and synchronous content-borrowing contract of one realized managed
Library.

It expands step 13 of
[Resource Ownership and Borrowing](resource-ownership-and-borrowing.md) and is
tracked end to end by
[#6621](https://github.com/richlander/dotnet-inspect/issues/6621).

## Authority and exact claim

`DotnetInspector.Libraries` owns this claim:

> One realized managed Library has one immutable resource-free reference and
> one Library-resource owner. Async consumers receive fresh owner-issued
> operation authority by ownership transfer, use only synchronous scoped
> borrows of the referenced assembly and companion contents, and return only
> detached values or resource-free evidence. A reference, House contribution,
> result, or receipt never carries hidden lifetime authority.

The design is intentionally narrower than Library acquisition, package or
Platform asset selection, Workspace membership, Metadata inspection, source
settlement, documentation settlement, or host presentation. Those owners
consume this contract without transferring their policy here.

The initial production consumers are PackageHouse, PlatformHouse, the
Workspace-owned direct-library path used by the CLI, SourceHouse, and
DocumentationHouse. CLI and Browser/Wasm package and Platform paths adopt the
same host-neutral contract; this design does not add bare-Library loading to
Inspect Web.

## Basis

The normative basis is
[Resource Ownership and Borrowing](resource-ownership-and-borrowing.md):

- one owner holds each release obligation;
- ownership transfer is explicit;
- work that crosses `await` owns an operation-scoped lease;
- direct borrows and snapshot callbacks are synchronous and scoped;
- callback results are detached or independently owned;
- references and receipts carry identity and evidence, not hidden authority;
  and
- every terminal path releases or transfers every owned obligation.

Supporting owner-issued inputs are:

- Artifact identity, provenance, guarded content access, and the future
  artifact ownership contract from
  [Artifact Acquisition and Workspaces](artifact-acquisition-and-workspaces.md);
- exact Metadata assembly identity;
- the source-distinct package-or-Platform
  [exact Library source coordinate](exact-library-source-coordinate.md); and
- package-, Platform-, Workspace-, Metadata-, and PDB-owned correspondence
  evidence.

This design consumes those currencies. It does not redefine how an adjacent
owner derives them.

## Why Library owns a resource

A product Library is not only an assembly name. Once realized, it may associate
several independently acquired contents:

- an API-declaration assembly;
- an implementation assembly;
- a compiled XML documentation companion; and
- a Portable PDB companion.

One physical assembly may serve both assembly roles. A Library may omit an
implementation, XML, or PDB role. The association still matters: a same-named
XML file or PDB from another source, target, view, or Library is not an
eligible companion.

PackageHouse, PlatformHouse, and direct-library adapters know how their source
produced those artifacts. Metadata, SourceHouse, and DocumentationHouse need
to read them. Letting each consumer invent a "metadata-ready",
"source-ready", or "documentation-ready" lifetime wrapper would create
parallel owners, hidden lease capture, and incompatible release behavior.

The shared Library owner instead preserves the exact association once and
provides one resource-specific operation and borrowing contract.

## Contract vocabulary

### `LibraryReference`

`LibraryReference` is the resource-free public reference to one immutable
realized managed Library and its `LibraryContentOwner`.

It contains:

- the exact package-or-Platform source coordinate when one selected the
  Library, or process-local direct-artifact correspondence otherwise;
- exact API-declaration and optional implementation assembly references;
- zero or more exact companion-content references;
- the roles assigned to those contents;
- owner-issued assembly-role and companion correspondence evidence; and
- resource-free provenance references needed to explain the association.

It contains no stream, handle, memory owner, callback, service, client,
credential, path-opening capability, Artifact lease, Library lease, disposal
delegate, or other live authority.

A `LibraryReference` may outlive its Library owner. A later lease request then
fails visibly; the reference does not silently reopen content.

The package-or-Platform source coordinate remains the portable logical identity
for search, Workspace definition, and later realization. The
`LibraryReference` is the exact realized registration and is not a portable
replacement for that coordinate. Process-local Artifact correspondence cannot
be serialized and restored.

Two Workspaces may independently realize the same source coordinate. They
receive distinct Library references and owners. A consumer comparing logical
source intent compares the source coordinate; a consumer borrowing content
uses the exact Library reference.

### `LibraryContentReference`

`LibraryContentReference` identifies one content item within one exact
`LibraryReference`. Its closed initial roles are:

- `ApiAssembly`;
- `ImplementationAssembly`;
- `CompiledXmlDocumentation`; and
- `PortablePdb`.

A content item may hold both assembly roles. Companion contents each name the
assembly content reference to which the source owner associated them.

The content reference retains Artifact identity and provenance but no Artifact
or Library authority. It does not expose a path as identity.

### Correspondence

The Library reference preserves, but does not manufacture:

- source-to-Library correspondence;
- API-declaration-to-implementation correspondence;
- compiled-XML-to-assembly correspondence; and
- Portable-PDB-to-implementation correspondence.

The adjacent source or Metadata owner supplies the substantive evidence. The
Library owner validates that every record names content in the same
`LibraryReference` and preserves the evidence without reducing it to display
text.

Reference forwarding remains Metadata-owned. Platform view and facade
classification remain Platform-owned. A DocumentationHouse consumer must
follow the terminal Metadata supplier required by its owner design; the
Library contract does not select that supplier.

## `LibraryContentOwner`

`LibraryContentOwner` is the sole resource owner for one realized
`LibraryReference`. It is a resource under the shared `Inspector.Resources`
protocol.

Construction consumes ownership of the artifact-backed content obligations
accepted into the Library. A successful construction leaves the caller with
the resource-free `LibraryReference` and the live owner as separate values.

Construction is atomic:

1. validate the Library reference, roles, and correspondence against
   resource-free inputs;
2. validate that every transferred child obligation describes the matching
   content reference;
3. accept ownership of every child obligation; and
4. publish the owner and reference.

Before step 3, the caller retains every child obligation. After step 3, the
Library owner owns every accepted obligation. Rejection cannot leave ambiguous
or partially transferred ownership. Failure while accepting multiple children
settles already accepted children and returns or leaves unaccepted children
with the caller according to the concrete consuming API.

The concrete artifact child contract is blocked on step 12 of #6544. This design
does not bless the current `ArtifactContentReference` shape that captures an
`ArtifactQueryLease`. The Library implementation must consume the future
Artifact-owner-issued ownership and borrowing contract instead.

`LibraryContentOwner` uses asynchronous settlement because operation leases may
remain live across `await`. Beginning `DisposeAsync` is the linearized
retirement transition:

1. reject every new operation-lease request with `OwnerRetiring`;
2. keep every previously issued lease usable against its immutable Library;
3. asynchronously await settlement of all issued leases without blocking a
   thread or single-threaded Browser/Wasm event loop;
4. release every retained child obligation exactly once; and
5. complete only after child quiescence and release complete.

Beginning settlement consumes ordinary use of the owner. The returned awaitable
carries the obligation to observe settlement. Repeated settlement observes the
same in-progress or terminal outcome; it does not start a second drain or
release.

If settlement faults, the owner remains terminal and issues no new leases. The
failure reports which child obligation did not settle or release; it does not
convert retirement into successful disposal or silently reopen ownership.

`LibraryOperationLease` uses synchronous disposal because settling one
operation child only reports its completion to the still-live or retiring
owner. The owner, not the operation lease, performs aggregate child release
after quiescence. An adjacent asynchronously disposable authority remains a
separate resource and cannot be hidden inside either Library resource.

## `LibraryOperationLease`

`LibraryOperationLease` is the resource-named authority for one async
operation over one exact `LibraryReference`.

The Library owner issues a fresh lease for one exact `LibraryReference`.

Issuance validates owner liveness and exact reference identity. It never
substitutes another realization of the same source coordinate or a same-named
assembly or companion.

The lease owns its child release obligation and is transferred into the async
operation. The caller must not retain or dispose it after transfer. The
operation settles the lease on success, failure, cancellation, rejection, and
incomplete completion.

Stable service objects, deferred providers, requests, policies, operation
plans, House contributions, and receipts may retain the `LibraryReference`.
They never retain a `LibraryOperationLease`.

This creates the standard call shape:

```text
host or orchestrator
  -> asks LibraryContentOwner for operation authority
  -> transfers LibraryOperationLease into one async operation
  -> operation performs zero or more synchronous snapshots between awaits
  -> operation settles the lease on every terminal path
  -> result contains detached values and resource-free evidence only
```

An operation may transfer the lease onward to exactly one consuming operation.
The final consumer settles it. No result retains the lease, and an operation
may not copy it, hide it in an ordinary result, or return both a live lease and
a receipt claiming settlement.

## Synchronous content snapshots

`LibraryOperationLease` exposes owner-controlled synchronous snapshots over
exact content references in its Library.

The public contract follows the `Inspector.Resources` callback model:

1. the caller names one or more exact content references from the lease;
2. the lease validates operation liveness, exact Library reference, and content
   membership;
3. the owner begins read-only borrows for the complete set;
4. it invokes one synchronous callback with a scoped ref-like Library content
   view;
5. the callback reads the requested contents as `ReadOnlySpan<byte>` values;
6. the callback returns a detached or independently owned value; and
7. the owner ends all borrows before returning or propagating failure.

One callback may borrow multiple contents when an algorithm needs simultaneous
spans, such as assembly and PDB input. This is an access convenience, not a
freshness or atomic-snapshot requirement: Library content is immutable, and
separate synchronous snapshots through the same live lease remain associated
with the same `LibraryReference`.

The snapshot view exposes content only by exact `LibraryContentReference`. Role
convenience accessors may exist, but they must reject absent or ambiguous roles
rather than choosing a first match.

The callback is synchronous. It cannot:

- be `async`;
- retain the view or any span;
- return `Memory<byte>` over owner-backed storage;
- cross managed-to-JavaScript interop;
- start background work that uses the borrow; or
- access content after the callback returns.

An async consumer materializes the detached value needed for its next
asynchronous step, completes the callback, and then awaits.

## Results, contributions, and receipts

Library operation results contain only:

- detached or independently owned values;
- `LibraryReference`, `LibraryContentReference`, and correspondence evidence;
- completion state; and
- visible diagnostics or failure evidence.

House contributions and receipts are always resource-free. A House that needs
Library content receives a lease from the Library owner by transfer into its
operation. The House does not mint a House-named wrapper or issue a substitute
lease over another owner's resource.

Receipts may record the exact `LibraryReference`, content read, terminal
outcome, and lease-settlement evidence. They do not provide a route back to
live content.

## Failure and completion algebra

The Library owner keeps these outcomes distinct:

- `Issued`: exact operation authority was transferred;
- `OwnerRetiring`: owner settlement began and new authority is unavailable;
- `OwnerReleased`: the resource-free reference outlived its owner;
- `ReferenceMismatch`: the request and owner name different Library
  references;
- `ContentNotInLibrary`: a requested content reference is not a member;
- `RoleUnavailable`: the exact requested role is absent;
- `CorrespondenceRejected`: supplied association evidence is inconsistent;
- `BorrowRejected`: the lease is moved, settled, or otherwise not live;
- `CallbackFailed`: the callback threw and the borrow ended before propagation;
- `ReleaseFailed`: an owned release obligation failed.

None of these outcomes becomes an empty successful Library, missing companion,
or successful receipt.

Cancellation and finite-work completion belong to the consumer operation. The
Library owner settles its transferred lease on those paths but does not define
the House's cancellation or incomplete-result algebra.

## Immutability, Workspace replacement, and retirement

The initial contract permits concurrent read-only operation leases over one
immutable Library. The Library owner does not permit content mutation.

Retirement and lease issuance have one linearized owner-liveness decision. A
lease either owns a valid child obligation before retirement begins or issuance
fails with `OwnerRetiring`; it cannot be issued into a retiring or released
owner.

Retirement never synchronously waits for an active lease. Existing leases
remain usable while the owner drains them, including on single-threaded
Browser/Wasm. Aggregate child release starts only after the last lease settles.

There is no Library refresh, replacement, or generation operation. The current
product scenarios do not mutate a realized Library:

- nuget.org package contents and realized Platform contents are immutable;
- the CLI process is short-lived;
- Inspect Web has one active Workspace and no bare-Library loader;
- Spotlight is a one-shot Workspace editor; and
- local or private-source content may change between acquisitions, but one
  Artifact registration remains the immutable input to one Workspace.

Changed content is acquired into a new Workspace, which constructs a new
Library owner and reference. Existing operations finish against the old owner
before that owner settles. Workspace revision and Artifact generation remain
with those owners rather than being copied into a Library-owned generation.

The protocol does not defend against a trusted caller concurrently violating
its transfer obligation. Current C# declaration effects and Resource Lifecycle
Analysis provide the repository's staged enforcement.

## Security and platform compatibility

Library content may originate in untrusted internet packages. The Library
owner never executes inspected code and does not weaken the Artifact owner's
construction-time containment or bounded-reader requirements.

Identity and correspondence validation occur before a consumer receives a
borrow. Display paths and file names are never authority.

The contract is host-neutral, SRM-only, NativeAOT-compatible, Roslyn-free, and
compatible with single-threaded Browser/Wasm:

- no ambient filesystem access;
- no dynamic assembly loading;
- no thread or blocking-wait requirement;
- no host-specific serialization type;
- no network or credential ownership; and
- only synchronous scoped spans cross the content callback.

Browser interop receives a detached serialization or independently owned
projection after the callback returns. No borrow crosses the interop boundary.

## Pathological demonstration

The contract-defining scenario uses two `System.Text.Json` libraries:

```text
NuGet package System.Text.Json
  assembly identity System.Text.Json, Version=11.0.0.0

.NET 11 Platform reference pack
  assembly identity System.Text.Json, Version=11.0.0.0
```

The assembly identities may be equal. Their source coordinates and
Library references are not.

The Platform Library associates:

- its reference-pack assembly as `ApiAssembly`;
- its runtime-pack assembly as `ImplementationAssembly`;
- the runtime PDB only when exact implementation correspondence exists; and
- compiled XML only with the exact terminal API supplier required by Metadata
  forwarding.

An operation lease issued for the package Library cannot borrow Platform
content. A same-named XML file from the package cannot become the Platform
companion. Disposing one Library owner does not release or invalidate the
other.

The neighboring ordinary case uses one direct implementation assembly serving
both assembly roles with no XML or PDB. Metadata inspection can borrow that
single content; documentation and authored-source channels report their exact
missing companion outcomes without turning the Library into failure.

## Project and dependency boundary

The host-neutral contract and implementation belong in target
`src/DotnetInspector.Libraries/`.

The project may depend on:

- `Inspector.Resources`;
- the Artifact contract floor after #6544 step 12;
- `ILInspector.MetadataPrimitives` for exact assembly identity; and
- `DotnetInspector.SourceSelection` for the owner-issued exact source
  coordinate.

It does not depend on PackageHouse, PlatformHouse, Workspace, SourceHouse,
DocumentationHouse, Queries, CLI, Inspect Web, NuGet transports, filesystem
adapters, or rendering.

Integration projects owned by each adopter bind their source-specific
realization to this contract. The dependency direction is always:

```text
Artifact and identity owners
  -> DotnetInspector.Libraries
     -> adopter-specific integration
        -> PackageHouse / PlatformHouse / Workspace / consumer operation
```

No House is referenced from `DotnetInspector.Libraries`.

## Production adoption and retirement

[#6621](https://github.com/richlander/dotnet-inspect/issues/6621) owns eight
focused slices:

1. lock this Library ownership and borrowing design;
2. implement `DotnetInspector.Libraries` contracts and Release declaration
   gates after #6544 step 12;
3. implement owner construction, operation transfer, scoped content snapshots,
   release, and pathological `System.Text.Json` gates;
4. adopt the contract in PackageHouse;
5. adopt it in PlatformHouse;
6. adopt it in Workspace and the Workspace-owned direct-library adapter;
7. adopt it in SourceHouse; and
8. adopt it in DocumentationHouse through both CLI and Browser/Wasm consumers.

Each adoption changes one owner and retires that owner's consumer-specific
ready wrapper or hidden captured lease. The implementation slices update
the corresponding #6544 steps 16 through 18, 20, and 21 without combining
those owners into one PR.

The direct product result is DocumentationHouse reading the compiled XML
companion of the .NET 11 Platform `System.Text.Json` Library while SourceHouse
can independently read its implementation assembly and PDB. Both consume
separate Library operation leases over the same owner-issued reference; neither
defines a private lifetime wrapper.

## Evidence plan

This specification is design-only. Its behavioral properties remain
**unverified** until the named implementation and adoption slices add Release
gates.

The Library contract suite must gate:

- references and receipts are resource-free;
- portable source coordinates remain distinct from process-local realized
  Library and Artifact references;
- package and Platform Libraries with equal assembly identities remain
  source-distinct;
- construction transfers every accepted child exactly once;
- rejection and partial failure leave no ambiguous ownership;
- operation issuance rejects released owners and foreign Library references;
- content access rejects foreign content and absent roles;
- every operation terminal path settles its lease;
- single- and multi-content snapshots end every borrow on return or exception;
- callback results used by product paths are detached or independently owned;
- asynchronous owner settlement rejects new leases, drains active leases
  without blocking, releases aggregate children once, and surfaces settlement
  failures; and
- no successful result uses a same-named but uncorrelated XML or PDB companion.

Resource Lifecycle Analysis gates the supported transfer and borrow flows once
the machine-readable contract model recognizes the Library declarations. It
reports `Incomplete` rather than claiming safety for unsupported flow.

The adopter suites separately gate their own policy while reusing the Library
contract fixtures. CLI and Browser/Wasm product tests gate the final Platform
`System.Text.Json` documentation scenario without making either host the
Library owner.
