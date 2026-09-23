# Library overview inspection

## Status

Status: **implemented** by
[#8112](https://github.com/richlander/dotnet-inspect/issues/8112).
Production-host adoption remains tracked by #8088, with scalar declaration,
admission, and discovery staged under #8228.

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
> return one resource-free scalar
> `InspectionEnvelope<LibraryOverviewOutcome>` after settling the transferred
> lease on every terminal path.

The available outcome contains one `LibraryOverviewDocument`. Expected cases
that cannot construct that Document remain typed non-available outcomes.
The overview is a scalar value under
[Section cardinality](section-cardinality.md): it exposes neither semantic
Rows nor semantic Count. Its fields describe one Library; they are not an
inventory population.

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
- [View Facet Registry](view-facet-registry.md) owns canonical facet identity
  and the closed registration set. It is adjacent future Share work, not an
  input to this first operation.

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
the Inspect Web Library overview. Both ultimately consume this same operation
and envelope; neither host independently reconstructs the overview or its
cardinality. Inspect Web may compose navigation or interaction state outside
the envelope.

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
- explicit finite API-surface extraction bounds.

The operation accepts one complete request and returns one scalar envelope. It
does not declare a QuerySpace row set or admit Count, predicates, ordering, or
semantic row selection. It does not expose section names, verbosity, fields,
columns, line windows, output format, or Browser navigation.
Effective-section discovery remains outside this operation.

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
  Incomplete(LibraryOverviewIncompleteReason)
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

The complete Document is one scalar value. Its properties do not form logical
Rows, and the value does not have semantic Count `1`. A host may render its
properties as field/value pairs or lines and may clip those rendered lines,
but neither presentation creates an inventory terminal.

### Non-available outcomes

`Incomplete` identifies the exact owner-issued extraction bound that prevented
a complete overview, or reports that the completed Metadata surface retained
one or more declaration-inspection failures. It publishes no shortened
Document in either case. Its closed reason is:

```text
LibraryOverviewIncompleteReason
  ExtractionBound(ApiSurfaceExtractionBound)
  MetadataInspectionFailures(count)
```

The failure count is evidence about omitted declarations, not a substitute for
their owner-issued details. The broad exact-API operation remains responsible
for content that preserves those detailed failures.

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

- `Completed` supplies exact content correspondence, MVID, the bounded
  `ApiSurface` with any retained inspection failures, and measured work;
- `Incomplete`, `Rejected`, and `Failed` retain their owner-issued meanings.

Before publishing an available Document, the overview checks
`ApiSurface.InspectionFailures`. A non-empty sequence maps to
`Incomplete(MetadataInspectionFailures(count))`, because the retained healthy
types and member counts do not describe the complete requested overview. The
operation neither publishes those reduced counts as complete nor reclassifies
the producer's individual failure kinds.

The overview owner projects only the portable summary needed by both hosts. It
does not reopen content, revalidate identity from display text, or expose the
full surface as a second exact-Library API operation.

The existing exact-Library API operation remains the owner of full API
inventory and navigation content. A host may request both operations and
compose their separately typed results, but one cannot be treated as an
alternate serialization of the other.

## Host compatibility and composition

The shared overview is the baseline scalar value, not the union of every fact
that either initial host currently places on an overview screen. Production
adoption therefore composes separately owned results at the presentation
boundary without adding those results to `LibraryOverviewDocument`.

The ordinary CLI projects these existing `Library Info` fields from the shared
Document when the overview is available:

- `Name` from managed assembly identity;
- `AssemblyVersion` from managed assembly identity; and
- `PublicKeyToken` from managed assembly identity.

The legacy CLI `Types` and `Methods` fields remain supplemental during
adoption. They count all Metadata definitions, while the shared Document
reports public API counts; substituting one for the other would silently
change their semantics. Architecture, target framework, file provenance,
product attributes, build characteristics, Source, resources, Findings,
Integration, ecosystem, and filesystem facts also remain results of their
current adjacent owners until their focused #8088 migrations. The CLI may
continue rendering those typed supplemental facts in the same section, but
they do not become overview Content and cannot turn the scalar Document into
an inventory.

Inspect Web projects managed assembly identity and public API counts from the
same shared Document. Its exact-Library API result remains a separate
supplement that owns package and asset selection, source coordinate, type-kind
facets, namespaces, full API inventory, projection truncation, and the
interaction state derived from that inventory. Navigation and filtering may
compose those facts around the scalar baseline; they do not authorize the
Browser to recover baseline counts or identity from the exact-API result.

Composition preserves these invariants:

- both hosts receive the complete
  `InspectionEnvelope<LibraryOverviewOutcome>`, including its Share and
  diagnostics;
- an unavailable overview remains visible even when a supplemental operation
  succeeds and never falls back to host reconstruction;
- a supplemental failure remains independently visible and does not rewrite a
  successful overview outcome;
- public counts and total Metadata-definition counts retain their distinct
  names and meanings;
- equivalent requests over the same exact Library bytes produce equal
  baseline Content regardless of source route or host; and
- host view models may reference the baseline and supplemental results, but
  no host-authored combined model becomes a replacement semantic operation or
  transport envelope.

Running the baseline and a richer supplemental operation independently may
repeat bounded Metadata work. Correct shared ownership and typed failure
behavior take priority in this cutover. Any later optimization must preserve
both operation contracts and prove that it shares owner-issued evidence rather
than deriving one result from the other's display or wire projection.

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
This first operation always returns `NonProjectable` with the owner-scoped path
`library-overview/share` and a contained reason that no complete portable
Workspace scenario was supplied.

The operation does not derive available Share from
`LibraryReference.SourceCoordinate`. That coordinate intentionally omits
framework, RID, Platform version/view, and Workspace occurrence, while direct
Libraries have no portable source coordinate. None of those cases supplies
enough information to restore the same exact Library.

The operation never serializes a local path, process-local Artifact identity,
partial source coordinate, result counts, diagnostics, content bytes,
credentials, or operation authority to manufacture a packet.

An unavailable Share does not alter independently valid overview Content.

## Diagnostics

The overview outcome retains semantic terminal meaning. Envelope diagnostics
supplement that content for consistent host disclosure:

- extraction-bound or retained-inspection-failure incompleteness maps to one
  warning with an owner-scoped code;
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
and settled by the terminal operation. A structurally valid assembly with one
declaration that the Metadata extractor skips produces
`Incomplete(MetadataInspectionFailures)` and no shortened Document.

Neighboring cases cover one extraction bound below the real surface, malformed
Metadata, Windows Metadata, a managed module, identity mismatch, empty MVID,
and cancellation after authority transfer. No case returns an available
shortened or empty Document.

## Production adoption

The complete initial operation adoption is staged through focused slices:

1. Lock this focused operation design.
2. Implement the request, portable outcome and Document, envelope assembly,
   required non-projectable Share, diagnostics, and lease settlement in
   `DotnetInspector.Sections`.
3. Under #8228, associate the operation-owned scalar declaration with exact
   Library routes, associate the independently inventory-shaped declaration
   with aggregate all-libraries routes, publish both through structural
   Discovery, and reject exact Count or Rows before direct or package
   acquisition. The legacy multi-TFM package gesture remains undeclared until
   it receives its own aggregate row-unit contract. Aggregate detailed
   Discovery initially publishes only the adopted `Library Info` capability;
   mixed aggregate JSON plus semantic Rows fails visibly until a structured
   per-section JSON lowering can preserve independent row windows.
4. Apply the compatibility boundary above: shared identity and public counts
   come only from the baseline envelope, total-definition counts retain their
   distinct CLI meaning, and Browser API inventory remains a separately typed
   navigation supplement.
5. Adopt one complete host-neutral overview operation and envelope for direct,
   PackageHouse, and PlatformHouse CLI routes. Preserve supported supplemental
   fields, projections, Share, diagnostics, and rendered output without
   appending those fields to overview Content.
6. Consume that same envelope in Inspect Web's Library overview. Preserve its
   separately typed exact-API navigation inventory and failure behavior, then
   retire Browser reconstruction of the baseline identity and public counts.

The remaining ordinary Library sections migrate under #8088 by their own
semantic owners. This design does not make their legacy implementation
conforming.

Available Library-overview Share is a separate four-step sequence under #8088:

1. #7746 supplies the landed selected-context Package Library scenario.
2. #8093 issues the canonical Library-overview facet through the View Facet
   Registry owner.
3. #8095 extends portable active-Library descendants to exact Platform rows
   through Workspace Definitions.
4. A later focused L2 adoption consumes one complete owner-issued Share plan
   associated with the exact overview request and replaces `NonProjectable`
   only for faithfully projectable scenarios.

That later adoption must define the typed association between the complete
Workspace scenario and exact Library request. This design does not accept
loose framework, RID, Platform, navigation, or packet fields and does not let a
host inject an arbitrary `InspectionShare`.

Markout is the intended ordinary CLI rendering substrate. The implementation
and host-adoption slices define typed views and generated serializers; this
operation owns no renderer.

## Evidence

`DotnetInspector.Sections.Tests.LibraryOverviewInspectionOperationTests`
provides Release gates for:

- real `System.Text.Json` produces the expected managed identity, non-empty
  MVID, public API counts, measured work, and finite bounds;
- the available Document remains one detached scalar value rather than a
  declared row population;
- equivalent independently realized Libraries produce equal portable
  Documents without accepting one another's leases;
- each Library operation lease is settled on available, incomplete, rejected,
  failed, cancelled, and exceptional paths;
- bound exhaustion returns `Incomplete` without a shortened Document;
- a completed Metadata surface with retained declaration-inspection failures
  returns `Incomplete` without publishing its reduced counts as a complete
  Document;
- malformed Metadata, Windows Metadata, managed modules, identity mismatch,
  and empty MVID remain typed;
- every returned shape is resource-free and source-generated JSON
  serialization succeeds;
- the initial operation returns its stable truthful non-projectable Share
  outcome; and
- cancellation and validation failure settle transferred authority before
  propagating.

`DotnetInspect.Cli.Tests.CommandExecutionTests` provides production-host gates
for the direct-file envelope, publication after cleanup, and parser admission.
The #8228 declaration/admission slice additionally gates exact scalar
Discovery, pre-acquisition Count/Rows rejection for direct and package routes,
and preservation of aggregate library-row Count and Rows.

The production-adoption claims remain **unverified** until their later Release
gates prove:

- the consuming NativeAOT and Browser/Wasm hosts preserve the serialized
  contract;
- direct, package, and Platform routes preserve the same non-projectable Share
  outcome;
- package and Platform adoption preserve the same overview Content for the
  same Library bytes; and
- CLI and Inspect Web consume equal baseline envelopes for an equivalent
  semantic request while retaining separately typed supplemental results.

No new TLA+ model is required. The operation introduces no new concurrent
state machine; it consumes the existing Library lease lifecycle and is gated by
terminal-path settlement tests.

## Non-claims

This owner does not define:

- package, Platform, direct-file, or Workspace realization;
- Library reference, content role, borrowing, or retirement semantics;
- Metadata grammar, API extraction, identity, MVID, or extraction bounds;
- full API inventory, Library Query, or exact Type/Member inspection;
- section catalogs, effective discovery, inventory row selection, Analysis,
  Source, Documentation, ecosystem, or Finding semantics;
- CLI syntax, default verbosity, Browser navigation, rendering, or JSON
  transport;
- available Share projection or its Workspace/request association;
- a portable representation for direct local files; or
- a generic inspection operation, outcome, diagnostic, or extension
  dictionary.
