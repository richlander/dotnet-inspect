# Library overview inspection

## Status

Status: **proposed**.

This design owns
[issue #8089](https://github.com/richlander/dotnet-inspect/issues/8089).
The command-family adoption and source-path composition remain tracked by
[#8088](https://github.com/richlander/dotnet-inspect/issues/8088) under the
repository command inventory in
[#6639](https://github.com/richlander/dotnet-inspect/issues/6639).

## Authority and exact claim

`DotnetInspector.Sections` owns this claim:

> Given one exact realized Library, a transferred operation lease, and one
> bounded overview request, inspect the owner-attested API assembly once and
> return one `InspectionEnvelope<LibraryOverviewOutcome>` containing a
> resource-free portable outcome, the required Share outcome for the same
> semantic request, and ordered typed diagnostics, after settling the
> transferred lease on every terminal path.

The available outcome contains one `LibraryOverviewDocument`. Expected cases
that cannot construct that Document remain typed non-available outcomes.

This owner composes existing contracts without redefining them:

- [Library ownership and borrowing](library-ownership-and-borrowing.md) owns
  the exact `LibraryReference`, transferred `LibraryOperationLease`,
  synchronous content snapshots, and retirement.
- [Library-Metadata correspondence](library-metadata-correspondence.md) owns
  bounded Metadata inspection of owner-attested Library content.
- [Inspection envelope](inspection-envelope.md) owns Content, Share, and
  diagnostics.
- [Inspection plan projections](inspection-plan-projections.md) owns the
  separation between content execution and required Share projection.
- [Host-observable content kinds](host-observable-content-kinds.md) owns
  Result, Document, and Outcome semantics.

## Product question

The operation answers:

> What bounded public-API overview describes this exact realized managed
> Library?

The exact subject is one `LibraryReference`. Package, Platform, direct-file,
and Workspace routes realize that subject through their own owners before this
operation begins. Source kind does not select another inspection algorithm.

The first real scenario is `System.Text.Json` 10.0.0. Its public API is large
enough to exercise meaningful type and member accounting, and the same managed
Library is available through package, Platform, and direct-file gestures.

The initial production consumers are the ordinary CLI `library` overview and
the Inspect Web Library overview. The CLI adopts a direct-file route first;
package and Platform routes follow after their existing House handoffs are
wired into the same operation. Inspect Web consumes the same envelope and may
compose navigation or interaction state outside it.

## Boundary

```text
source-specific realization
  -> exact LibraryReference + LibraryContentOwner
  -> transferred LibraryOperationLease
  -> LibraryOverviewRequest
  -> Library overview inspection
       |-- bounded LibraryMetadata API-surface inspection
       `-- required Share projection
  -> InspectionEnvelope<LibraryOverviewOutcome>
  -> CLI or Browser projection
```

The operation accepts no path, package archive, Platform label, CLI options,
Browser DTO, stream, reader, opener, or ambient resolver. Hosts lower their
gestures before this boundary.

The operation is the final host-neutral owner for this overview. It is not a
wrapper around a CLI model, and hosts do not add facts to its Content after the
envelope crosses the boundary.

## Request and execution plan

`LibraryOverviewRequest` names:

- the exact `LibraryReference`;
- explicit finite API-surface extraction bounds; and
- the fixed canonical Library-overview facet used by Share projection.

The initial request has one Execute purpose. It does not expose section names,
verbosity, fields, columns, row windows, output format, or Browser navigation.
Effective-section discovery remains outside this first operation.

The overview always inspects the public API assembly and reports the complete
bounded summary defined below. A host cannot request only the assembly name to
avoid the declared API-surface work, nor can it request the retained full API
surface through this overview contract.

The content plan and Share projection use the same normalized request. Share
projection performs no Metadata inspection and cannot change the content
outcome.

## Content

`LibraryOverviewOutcome` is a closed source-neutral sum:

```text
LibraryOverviewOutcome
  Available(LibraryOverviewDocument)
  Incomplete(LibraryOverviewBound)
  Rejected(LibraryOverviewRejection)
  Failed(LibraryOverviewFailure)
```

### Available Document

`LibraryOverviewDocument` is the portable composition needed to interpret one
complete overview:

- managed assembly identity;
- non-empty module-version identity;
- public type count;
- public method count;
- public property count;
- public event count;
- public field count;
- total retained public-member count derived from those typed counts;
- Metadata-row work consumed;
- retained-text work consumed; and
- the finite bounds that governed the operation.

Managed assembly identity crosses the host boundary through one portable
`LibraryOverviewAssemblyIdentity` record. It preserves the numeric version and
contained assembly name, culture, and public-key-token text without retaining
the Metadata or Library identity objects from which those values were
projected.

The Document is a settled serialization-ready value. It does not retain the
full `ApiSurface`, `LibraryReference`, `LibraryContentReference`, Artifact
identity, owner, lease, stream, reader, callback, or reopening authority.

The Metadata owner validates exact Library and content correspondence before
projection. The portable Document retains managed assembly identity and
module-version identity as inspected content facts; it does not claim that
those values reconstruct process-local Artifact identity.

The total public-member count is the checked 64-bit sum of method, property,
event, and field counts. It is presentation-independent summary data, not a
second scan or a count of rendered rows.

### Non-available outcomes

`Incomplete` identifies the exact owner-issued extraction bound that prevented
a complete overview. It publishes no shortened Document.

`Rejected` preserves a foreign Library lease or assembly-identity mismatch.
The operation does not retry against an equivalent Library or substitute a
same-named assembly.

`Failed` preserves unsupported or malformed managed content, a managed module,
Windows Metadata, or an empty module-version identity. These cases do not
become an available zero-count Document.

Cancellation and unexpected implementation failure produce no envelope.
They propagate only after the transferred operation lease has settled.

## Metadata composition

The operation invokes
`DotnetInspector.LibraryMetadata.LibraryApiSurfaceInspection` with the exact
Library, transferred lease, public extraction scope, and request bounds.

The Metadata result remains the evidence source:

- `Completed` supplies exact content correspondence, MVID, complete
  `ApiSurface`, and measured work;
- `Incomplete`, `Rejected`, and `Failed` retain their owner-issued meanings.

The overview owner projects only the portable summary needed by both hosts. It
does not reopen content, revalidate identity from display text, or expose the
full surface as a second exact-Library API operation.

The existing exact-Library API operation remains the owner of full API
inventory and navigation content. A host may request both operations and
compose their separately typed results, but one cannot be treated as an
alternate serialization of the other.

## Lease transfer and settlement

The caller transfers one `LibraryOperationLease` to the operation and must not
retain or dispose it afterward.

The operation:

1. validates the request before content access;
2. invokes the synchronous LibraryMetadata inspection while the lease is live;
3. projects detached content and diagnostics;
4. settles the lease before returning the envelope; and
5. settles the lease before propagating cancellation or unexpected failure.

No synchronous content borrow crosses an `await`. The first operation may be
synchronous, but the ownership contract allows a future asynchronous
orchestrator to perform detached work between separate synchronous snapshots
without retaining a borrow.

The returned envelope contains no live authority. Library-owner retirement
remains the caller's adjacent obligation after the operation has settled its
child lease.

## Share

Every envelope carries one `InspectionShare` for the same overview request.

Package and Platform source coordinates may project an available Workspace
scenario only through Workspace Definitions. The projection retains the exact
portable source coordinate, context, selected Library, and canonical Library
overview facet. It does not serialize result counts, diagnostics, content
bytes, credentials, or operation authority.

A direct Library has no portable source coordinate. Its initial Share outcome
is therefore `NonProjectable` with an owner-scoped path and contained reason.
The operation never serializes a local path or process-local Artifact identity
to manufacture a packet.

An unavailable Share does not alter independently valid overview Content.

## Diagnostics

The overview outcome retains semantic terminal meaning. Envelope diagnostics
supplement that content for consistent host disclosure:

- incomplete extraction maps to one warning with an owner-scoped code;
- rejected correspondence maps to one error;
- unsupported or malformed content maps to one error; and
- an available complete overview has no diagnostic unless a later owner
  demonstrates independent supplemental evidence.

Diagnostic summaries are fixed owner-authored text. Untrusted assembly names,
paths, Metadata strings, and exception messages do not enter them. The
diagnostic code, not its summary, is semantic identity.

## Security and compatibility

Assembly bytes may originate from untrusted internet content. The operation
inherits SRM-only admission and bounded extraction from LibraryMetadata.
Malformed and unsupported input remains typed non-success.

The operation is pathless, Roslyn-free, NativeAOT-compatible, and compatible
with single-threaded Browser/Wasm. It never loads or executes the inspected
assembly and introduces no blocking wait or worker-thread requirement.

## Pathological demonstration

Two independently realized Libraries contain equivalent
`System.Text.Json` 10.0.0 bytes.

Each operation must:

- require the lease issued for its exact `LibraryReference`;
- inspect only that reference's API content;
- receive exact Library/content correspondence from LibraryMetadata;
- project equal portable overview Documents for equal bytes;
- retain distinct in-process correspondence until projection;
- settle its own transferred lease; and
- leave owner retirement to the caller.

A lease from the other equivalent Library is rejected before content access
and settled by the terminal operation.

Neighboring cases cover one extraction bound below the real surface, malformed
Metadata, Windows Metadata, a managed module, identity mismatch, empty MVID,
and cancellation after authority transfer. No case returns an available
shortened or empty Document.

## Production adoption

The complete adoption has five owner-scoped steps:

1. Lock this focused operation design.
2. Implement the request, portable outcome and Document, envelope assembly,
   Share projection, diagnostics, and lease settlement in
   `DotnetInspector.Sections`.
3. Adopt the operation for one ordinary direct-file CLI Library overview
   through direct-Library realization and an ephemeral Workspace.
4. Adopt the same operation for PackageHouse and PlatformHouse CLI routes in
   separate source-owner slices.
5. Consume the same envelope in Inspect Web's Library overview, then retire
   the covered CLI-owned overview construction and direct serialization.

The remaining ordinary Library sections migrate under #8088 by their own
semantic owners. This design does not make their legacy implementation
conforming.

Markout is the intended ordinary CLI rendering substrate. The implementation
and host-adoption slices define typed views and generated serializers; this
operation owns no renderer.

## Evidence

The design remains **unverified** until Release gates prove:

- real `System.Text.Json` produces the expected managed identity, non-empty
  MVID, public API counts, measured work, and finite bounds;
- equivalent independently realized Libraries produce equal portable
  Documents without accepting one another's leases;
- each Library operation lease is settled on available, incomplete, rejected,
  failed, cancelled, and exceptional paths;
- bound exhaustion returns `Incomplete` without a shortened Document;
- malformed Metadata, Windows Metadata, managed modules, identity mismatch,
  and empty MVID remain typed;
- every returned shape is resource-free and source-generated JSON
  serialization succeeds under NativeAOT;
- direct source returns a truthful non-projectable Share outcome;
- package and Platform adoption preserve the same overview Content for the
  same Library bytes; and
- CLI and Inspect Web consume equal baseline envelopes for an equivalent
  semantic request.

No new TLA+ model is required. The operation introduces no new concurrent
state machine; it consumes the existing Library lease lifecycle and is gated by
terminal-path settlement tests.

## Non-claims

This owner does not define:

- package, Platform, direct-file, or Workspace realization;
- Library reference, content role, borrowing, or retirement semantics;
- Metadata grammar, API extraction, identity, MVID, or extraction bounds;
- full API inventory, Library Query, or exact Type/Member inspection;
- section catalogs, effective discovery, row selection, Analysis, Source,
  Documentation, ecosystem, or Finding semantics;
- CLI syntax, default verbosity, Browser navigation, rendering, or JSON
  transport;
- a portable representation for direct local files; or
- a generic inspection operation, outcome, diagnostic, or extension
  dictionary.
