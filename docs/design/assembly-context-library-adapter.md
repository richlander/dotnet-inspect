# Assembly-context Library adapter

## Authority and claim

The Workspace-owned adapter in `DotnetInspector.Queries` owns the conversion
of an existing exact assembly-context participant into a direct Library input.
It is tracked by [#7312](https://github.com/richlander/dotnet-inspect/issues/7312).

The assembly-only adapter and authored SourceHouse core are implemented.
Supplied companion admission landed under
[#7439](https://github.com/richlander/dotnet-inspect/issues/7439).
The first production-query adoption is the shared member-source-pair cutover
in [#7448](https://github.com/richlander/dotnet-inspect/issues/7448).

> Given a live group, its exact selected participant, an explicit assembly
> role, optional supplied Portable PDB, finite materialization bounds, and
> cancellation, construct an independently owned Library from that
> participant's retained image and matching companion, preserve input
> correspondence and provenance, and transfer its Library owner and adjacent
> Artifact session separately.

The adapter consumes group snapshot access, Artifact publication and content
children, Metadata-issued physical identity, and
[Library ownership and borrowing](library-ownership-and-borrowing.md).
It does not redefine their authority, lifetime, identity, or failure rules.

This is a compatibility path for existing assembly-context callers. It is not
PackageHouse or PlatformHouse realization, and does not claim that an adapted
Library is the canonical source-native Library for a package or Platform.

## Exact input and result association

The caller selects one participant already in the supplied group. Another
participant with an equal assembly identity is not a substitute. The adapter
uses the group's authoritative retained image, not the descriptor's path or a
new invocation of its original acquisition callback after that image exists.
Obtaining the initial group snapshot retains the group's existing admission
and image-budget behavior.

The caller explicitly chooses API-only content or an implementation image
that serves both API and implementation roles. The adapter does not infer the
choice from filenames, perform forwarding, or pair independent assembly images.
An optional already-acquired Portable PDB supplies immutable content and
resource-free Artifact provenance. It requires the implementation role, under
the existing Library companion contract; supplying it for an API-only image is
an argument error, not permission to promote that image.

Metadata must establish the Portable PDB's correspondence to the exact captured
assembly before the adapter issues a Library companion. A missing Portable
CodeView identity, mismatching PDB, or unreadable supplied content remains
`PortablePdbRejected` with native observations rather than a Library with
unvouched symbols. The adapter consumes Metadata's identity loader; it does not
implement another GUID/stamp comparison or interpret SourceLink.

XML companions remain outside this operation. Embedded symbols remain bytes
in the image; only their owning producer and consumer policy may interpret
them. Existing PDB acquisition stays upstream; this operation does not discover
symbols or infer source authorization from a path.

Materialization publishes a new bounded Artifact generation. Its provenance
preserves the original acquisition registration and binding-policy snapshot;
Metadata projects the exact published content and supplies its assembly
identity and MVID. The association records the new Artifact content as a new
registration, not as a restoration of the original one. Same-name inputs in
different groups or acquisitions remain distinguishable.
The companion has a distinct Artifact registration in the same generation,
retains its supplied provenance, and names the exact assembly Artifact through
Library companion correspondence. It does not inherit the assembly's source
provenance merely because the two are supplied together.

The resulting `LibraryReference` is a direct-artifact reference. Its
correspondence retains the exact published content and Metadata identity.
Neither source-coordinate reconstruction nor a new source-native Library
identity is inferred from display labels. This transitional direct reference
must not replace a later PackageHouse- or PlatformHouse-issued reference.

## Ownership and completion

The caller retains its group and participant. The adapter's capture finishes
inside a synchronous group snapshot callback; no borrowed view crosses an
`await`. The accepted immutable image is independent of subsequent group
disposal, following the group's existing retained-reference behavior.

The completed arm transfers a `LibraryContentOwner` and an
`ArtifactSetSession` as separate authorities beside the resource-free Library
reference and adapter correspondence. A companion contributes its own content
lease to the same Library owner. The result does not wrap them in a new
source-ready resource or return a live authority inside an inspection
envelope. The consumer obtains a `LibraryOperationLease` from that owner and
transfers it to the later Library-consuming operation.

Library and Artifact retirement retain their existing child-drain contract:
the caller retires the Library before awaiting terminal Artifact retirement.
The adapter releases every authority it created but did not transfer,
including when construction, publication, cancellation, or identity projection
prevents completion. Cleanup failure remains visible; it cannot accompany a
successful materialization.

Invalid group/participant access and rejected retained-image access preserve
the existing query boundary's visible failure. Artifact rejection and
Metadata non-projection remain typed non-success rather than an empty
Library. A declared materialization bound prevents work beyond that bound.
Caller cancellation remains cancellation after owned cleanup. Unexpected
programmer failures are not converted into absence.

The existing capture bound applies separately to the assembly and optional PDB
image, checked before either independent copy. Capture-limit incompleteness
names the content role, observed bytes and limit. The retained Artifact bound
applies to the combined assembly and PDB content, not a fresh allowance for
each. Existing assembly-only calls keep their bounds and behavior.
The adapter does not promise a process-RSS ceiling or zero-copy publication.
The caller's supplied immutable PDB and the group's existing image charge
remain with their respective owners.

## Adoption and retirement

The user selected adapter-first under
[#6512](https://github.com/richlander/dotnet-inspect/issues/6512).
The assembly-context portion of that tracker's step 9 moves ahead of authored
settlement in step 5; the twelve-step plan and both hosts remain in scope.
This adapter also advances
[#6621](https://github.com/richlander/dotnet-inspect/issues/6621) slice 6,
without claiming full Workspace ownership adoption.

The user approved companion admission as a separate prerequisite rather than
mixing an adapter contract expansion with a production-query cutover. The
counted adapter-to-first-production path now has four deliveries:

1. Implement this adapter and its exact-content and ownership gates.
2. Implement authored SourceHouse settlement over a transferred Library
   operation lease, replacing authored candidate composition.
3. Admit supplied Portable PDB companions through this adapter.
4. Have `MemberSourcePairInspection` supply these Library inputs to SourceHouse.
   Its existing `InspectionEnvelope<AssemblyMemberSourcePairResult>` handoff
   serves CLI `diff --pdb-source` and Browser/Wasm two-version Source.
   Retire the authored acquisition composition those endpoints replace.

The member-source-pair adoption now serves both hosts through SourceHouse;
this adapter alone did not make those callers House consumers. Ordinary type/member
source-query adoption, acquisition, decompiler fallback, other Library
producers, and remaining source policy retirement stay with the corresponding
twelve-step #6512 slices.

The bridge retires per adoption: when a caller receives a canonical Library
from PackageHouse, PlatformHouse, or Workspace realization, it passes that
Library onward rather than rematerializing it through this adapter. The
adapter never becomes a second permanent source-selection route.

No rendering is introduced. The adapter returns construction evidence and
explicit resource ownership, not finished host content. Later shared query
results preserve House evidence until existing CLI Markout output or browser
code-view lowering.

## Basis and evidence

The conventional basis is an explicit ownership adapter over an already
selected immutable input. Local analogues are
`RetainedAssemblyContextGroup`, which binds exact acquisition registrations to
retained images, and installed PlatformHouse materialization, which publishes
selected content and transfers Library and Artifact ownership separately.
Neither analogue transfers its selection policy into this adapter.

The real motivating input is the runtime `System.Text.Json` implementation
image selected for `JsonSerializerOptions.MaxDepth` source inspection.
Its pathless equivalent exercises the browser-compatible construction path;
API-only content is the neighboring role case.

The companion case uses the real repository's compiled
`CSharpText.MemberSlicing`, its external Portable PDB, and `MemberTextSlicer.cs`.
It transfers the adapted Library lease to `SourceHouse.ExecuteAuthoredAsync`
and retrieves the exact authored member after closing the input group.

`AssemblyContextLibraryAdapterTests` gates, in Release:

- exact selected-participant association and Metadata identity;
- retained bytes despite an unusable original opener after capture;
- explicit role assignment without reference/implementation inference;
- pathless input and preservation of original/new registration association;
- bounded rejection and cancellation without a partial ownership handoff;
- input group remaining caller-owned;
- separate Library and Artifact retirement, including a live operation lease
  delaying retirement; and
- visible rejection of non-projectable managed input.

The supplied-PDB gates additionally cover independent assembly/companion
retirement and byte preservation, unchanged API-only role restrictions,
foreign/malformed PDB and unavailable Portable CodeView identity, exact capture
and combined-retention thresholds, cancellation, and the real SourceHouse
member handoff. Native PDB grammar and matching remain Metadata's contract;
the adapter tests establish their use at this handoff.

The real-image retirement case verifies all returned image bytes while both
owners drain and the original group has already closed. The bounds case checks
one byte below and exactly at the image size. All adapter cases are PR-fast.
Existing Library and Artifact tests remain the authority for their own
lower-owner lifetime contracts. No production SourceHouse adoption or full
Workspace Library-owner registry is claimed by these adapter gates.
