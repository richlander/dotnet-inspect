# DocumentationHouse composition

## Status and approved scope

This document is the normative owner for the host-neutral
`DocumentationHouse` composition boundary. It is tracked by
[#6579](https://github.com/richlander/dotnet-inspect/issues/6579).

The user approved DocumentationHouse as the product-facing documentation
settlement concept and approved retirement of `DocCommentParser`. The House
settles compiled XML documentation and documentation extracted from authored
source over one exact shared Library reference and transferred operation lease,
without acquiring packages, platforms, PDBs, or source bytes itself.

The source-neutral request, contribution, attempt, outcome, and receipt floor
and the compiled-XML operation are implemented in
`DotnetInspector.DocumentationHouse.Contracts` and
`DotnetInspector.DocumentationHouse`. The subject consumes owner-issued
Library-Metadata correspondence rather than pairing independently acquired
Metadata with a Library by assembly identity. The operation consumes one transferred
`LibraryOperationLease`, snapshots one exact associated XML content reference
into bounded detached bytes, ends the borrow, invokes the bounded CSharpText
reader, and settles the lease before publishing resource-free evidence.
Cancellation and the absolute deadline are observed at each synchronous stage
boundary; CSharpText's document, member, child, depth, and retained-text limits
bound the non-interruptible scan itself. Its current `XmlException` contract
maps malformed and parser-limit-exhausted input to a visible **Failed**
compiled attempt, while House contribution, byte, and deadline exhaustion
remain typed **Incomplete** evidence.
The PackageHouse, direct-Library, and PlatformHouse adapters plus the shared
Queries compiled-documentation result are implemented. Queries preserves the
exact detached House outcome for in-process composition and publishes a
separately owned portable terminal outcome as the source-generated JSON
contract. Inspect Web package and platform member documentation now consume
those House-backed query paths through generated TypeScript declarations.
PlatformHouse's superseded subject-level documentation contracts are removed.
The CLI now composes package, direct-Library, and platform reference-pack
compiled documentation through the same House-backed Queries paths. The
SourceHouse authored-documentation operation is implemented as a cold,
single-use adapter over one pre-authorized exact request. The authored channel,
field settlement, and remaining legacy retirement remain staged.

This is one focused new-owner effort under
[Design Scope](../design-scope.md). It transfers one cohesive responsibility:
documentation settlement moves from
[PlatformHouse](platform-house-reference-processing.md), whose remaining
target, realization, view-correspondence, forwarding, and library-handoff
authority is unchanged. Host-local documentation composition remains migration
evidence, not the target architecture.

The first production consumer is exact package-member documentation in Inspect
Web. CLI and Browser/Wasm then converge on the same House contract. The tracker
contains 22 ordered slices from this specification through package, platform,
and authored-source adoption and retirement of the previous composition.

The design depends on:

- [Type, member, and API representation](type-member-api-representation.md)
  for Metadata-issued exact type and member identity;
- [#6497](https://github.com/richlander/dotnet-inspect/issues/6497) for exact
  compiler XML-documentation identity and bounded CSharpText reading;
- [#6583](https://github.com/richlander/dotnet-inspect/issues/6583) for the
  separately owned model-free CSharpText operation that extracts documentation
  attached to a caller-correlated declaration;
- [#6584](https://github.com/richlander/dotnet-inspect/issues/6584) for
  SourceHouse-owned trusted correspondence between one exact Metadata target
  and the physical declaration that produced it;
- [PackageHouse](package-house.md) for package realization, selected assets,
  content generation, and library handoff;
- [PlatformHouse](platform-house-reference-processing.md) for exact platform
  target, reference and implementation realization, forwarding, and view
  correspondence;
- [SourceHouse](source-house.md) for authored-source settlement over one exact
  implementation target;
- [Library ownership and borrowing](library-ownership-and-borrowing.md) for the
  exact realized Library reference, selected content references, transferred
  operation authority, synchronous snapshots, and resource-free evidence;
- [Library-Metadata correspondence](library-metadata-correspondence.md) for
  bounded owner-issued correspondence between one exact Metadata surface and
  the realized Library API content that supplied it;
- [PDB acquisition](../pdb-acquisition.md) for SourceLink interpretation,
  checksum semantics, and authored-source evidence; and
- [Resource ownership and borrowing](resource-ownership-and-borrowing.md) for
  operation-scoped content access and resource-free receipts.

## Authority and exact claim

**DocumentationHouse Composition** owns:

> Given one exact library-scoped documentation subject, one owner-issued
> `LibraryReference`, one request-selected API-declaration
> `LibraryContentReference`, ownership of one matching
> `LibraryOperationLease`, one explicit documentation demand, one
> host-authorized operation plan, and finite work, settle compiled XML and
> authored-source documentation into independent typed attempts and
> deterministic field evidence that retains artifact, source, subject, Library,
> conflict, completion, failure, and resource-free lease-settlement
> correspondence without reconstructing authority from paths, names, or display
> text.

The owner defines:

- the product-facing `DocumentationHouse` facade;
- the exact library-scoped documentation subject consumed by the House;
- closed compiled-XML and authored-source documentation demand;
- the host-authorized documentation operation plan;
- House-facing compiled-XML and authored-source contribution contracts;
- contribution validation, execution, ordering, and short-circuiting;
- consumption, optional onward transfer, and terminal settlement of the
  transferred Library operation lease;
- independent per-channel attempts and terminal outcomes;
- field-level single-source, corroboration, conflict, and provenance states;
- the common result envelope and resource-free settlement receipt;
- visible absent, unavailable, ambiguous, rejected, failed, and incomplete
  evidence; and
- the rule that hosts, Queries, PackageHouse, PlatformHouse, SourceHouse, and
  presentation do not recreate documentation settlement.

It does not define:

- package, platform, project, direct-library, or Workspace realization;
- Library construction, content roles, correspondence, ownership, operation
  lease issuance, retirement, or scoped-borrowing mechanics;
- assembly, artifact, XML companion, PDB, source-document, or declaration
  identity;
- package or platform companion discovery, construction, correspondence
  validation, generation, or lifetime;
- XML-documentation identity grammar, XML parsing, or textual normalization;
- C# lexing, declaration recognition, documentation-comment grammar, or
  comment parsing;
- PDB policy, PDB acquisition, SourceLink mapping, authored-source candidate
  ordering, source transport, checksum verification, source decoding, or
  SourceHouse settlement;
- decompilation or decompiled text as documentation provenance;
- Metadata forwarding, binding, declaration, or resolution semantics;
- `<inheritdoc>` or `<include>` expansion;
- Workspace admission, replacement, revision, or binding policy;
- CLI flags, browser interaction, section selection, rendering, or serialized
  transport; or
- ambient filesystem, network, credential, cache, or cancellation policy.

Those owners issue typed identities, resource-free references, operation
authority, capabilities, and outcomes. DocumentationHouse composes them and
preserves their evidence.

## Why documentation needs a House

Documentation currently crosses independent producers and host-local policy:

```text
LibraryReference + selected API assembly + transferred operation lease
  + exact Metadata documentation identity
  + associated compiled XML companion
  + optional SourceHouse-authored source
        |
        v
channel attempts
  -> exact compiled XML lookup
  -> PDB-anchored authored documentation comment
        |
        v
field-level settlement and provenance
```

No adjacent owner should decide the product result:

- Metadata can issue the exact compiler XML ID but does not select an artifact;
- CSharpText can parse XML and recover declaration-attached comments but does
  not establish package, platform, PDB, or library correspondence;
- PackageHouse and PlatformHouse can realize assemblies and companion content
  but do not own documentation semantics;
- SourceHouse can produce checksum-verified authored source but explicitly
  does not extract or settle documentation;
- Queries can compose a request but should not duplicate field policy; and
- hosts choose authorization and presentation but should not reproduce
  companion selection, channel interpretation, or conflict handling.

The CLI package, direct-Library, and platform paths and the Inspect Web package
and platform-member paths compose compiled documentation through
DocumentationHouse. The CLI runtime and ASP.NET Core paths realize the exact
reference Library through PlatformHouse before issuing the shared platform
query. Canonical SDK-pack paths use the installed source; application
`packs-v2` paths use the package-backed source so the realized provenance
matches the selected reference pack. The CLI queries the IDs represented by
that exact reference surface and leaves implementation-only members
undocumented rather than failing otherwise valid reference subjects. Deferred
type/member discovery preserves an explicitly selected framework family and
version by resolving the type and owning assembly from that exact catalog, so
reopening cannot combine a requested target with the current catalog's Library.
Mixed reference and implementation search results may acquire the requested
pack, but they do not issue the selected Library: versioned deferred routing
projects the exact `PlatformTypeCatalog` definition or forwarder candidate
after acquisition.
Direct type and member source selection uses that same requested catalog
before applying current-runtime core-library heuristics; when the requested
catalog is not local, source selection defers to acquisition rather than
substituting a current-runtime Library. Best-effort type-prefix browsing is
likewise constrained to an explicit target, so an exact miss cannot become a
success-shaped browse result from the current runtime. A target-scoped browse
derives its reported version and TFM from the realized reference assets rather
than reconstructing either identity from search display text.
The resolved platform source retains the actual reference-pack TFM, including
legacy `netcoreapp*` directory identities, rather than reconstructing it from
the display version. The netstandard path, for which PlatformHouse defines no
family, constructs the monolithic `netstandard.dll` direct Library with its
`netstandard.xml` companion, queries only IDs represented by that contract
surface, and applies a bounded 32-MiB allowance for the XML document. The
remaining CLI `SourceEnricher` path retains PDB and authored-source acquisition,
comment search, parsing, field merging, and warnings until the authored channel
transfers those responsibilities in slice 21.

## Relationship to adjacent owners

The target dependency direction is:

```text
Library owner -------------------------> exact content and operation authority
Metadata ------------------------------> exact documentation subject
CSharpText ----------------------------> owner-issued XML and comment results
SourceHouse ---------------------------> deferred AuthoredOnly operation

Package documentation adapter --------> PackageHouse + DocumentationHouse
Platform documentation adapter -------> PlatformHouse + DocumentationHouse
direct-library documentation adapter -> Library owner + DocumentationHouse
source documentation adapter ---------> SourceHouse + DocumentationHouse

Queries -------------------------------> DocumentationHouse
CLI / Browser -------------------------> Queries
```

DocumentationHouse invokes only a source-neutral deferred authored-
documentation operation after the operation reaches the source stage. The
SourceHouse integration assembly implements the adapter that creates that
operation over one pre-authorized exact `AuthoredOnly` request and plan.
DocumentationHouse never references SourceHouse types, chooses PDB policy, or
widens source authorization. SourceHouse does not reference DocumentationHouse.
PackageHouse and PlatformHouse likewise do not call DocumentationHouse;
integration assemblies above both owners bind their resource-free evidence to
exact shared Library content references in the source-neutral House
contribution contract. Live content access comes only from the transferred
Library operation lease. This keeps every dependency acyclic and avoids
`PackageHouse -> DocumentationHouse -> PackageHouse` and equivalent platform
and source cycles.

An adapter is not a second settlement or resource owner. It associates the
source owner's evidence with the exact `LibraryReference` and content
references but carries no stream, opener, snapshot callback, content lease, or
Library operation lease. The application orchestrator retains the source
owner's original receipt separately; the source-neutral contribution carries
an opaque owner-issued evidence reference, not a concrete PackageHouse,
PlatformHouse, SourceHouse, or Workspace type.

The deferred operation is a source-neutral consuming operation, not a
SourceHouse result or evidence that authored documentation is available. It is
cold: construction performs no SourceHouse, Library, filesystem, repository,
content-store, or network work, starts no background task, and retains no
Library operation lease. Invocation receives the lease by ownership transfer
only when DocumentationHouse reaches the authored stage. The operation becomes
the sole lease owner on acceptance. It settles every operation-local rejection,
failure, cancellation, or incomplete path before SourceHouse accepts the
lease, or transfers the lease onward once to SourceHouse as the final Library
consumer.

## Exact documentation subject

Every request names one type or member in one exact library context. The
subject retains:

- owner-issued correspondence between one exact Metadata surface and the
  realized Library API content that supplied it;
- the exact type or member selected from that correspondence's surface;
- the exact compiler XML-documentation identity when compiled XML is
  requested;
- the exact realized `LibraryReference`;
- the request-selected API-declaration `LibraryContentReference`;
- reference forwarding or view correspondence when the public API subject and
  implementation source target differ; and
- the exact implementation target and `LibraryContentReference` used for
  authored source when that channel is requested.

The initial authored-source channel supports only exact type definitions and
MethodDefs for which SourceHouse issues both target correspondence and the
trusted physical-declaration correspondence owned by #6584.
Properties, events, fields, accessors, bodyless declarations, and other source
constructs remain unavailable until their owning layer supplies separately
reviewed exact Metadata-to-source correspondence. DocumentationHouse does not
invent that correspondence from a member name, containing declaration, or
nearby sequence point. Portable PDB destination coordinates, SourceLink
provenance, and a checksum-valid document do not prove this physical
correspondence.

An `XmlDocMemberIdentity` is an exact lookup key inside an associated compiler
artifact. It is not a global subject: two assemblies may legitimately carry
the same ID. A request cannot pair an ID issued from one API surface with a
same-named XML file or SourceHouse target from another library.

Display names, package IDs, assembly file names, source paths, URLs, overload
ordinals, and rendered signatures are not substitute identity.

Multi-subject execution is a bounded execution optimization, not a broader
subject contract. It accepts several ordinary exact requests under one
matching Library operation lease, preserves request order and one independent
terminal outcome and settlement per subject, and groups only the selected
companion read. Each distinct selected content reference and read-limit policy
is snapshotted and scanned once; the scan retains only the requested exact IDs.
Work evidence charges the read and parse once rather than repeating those
costs on every outcome.

## Library input and operation ownership

The primary House inputs are one owner-issued resource-free Library-Metadata
correspondence, the exact `LibraryReference` and API-declaration
`LibraryContentReference` it retains, and ownership of one matching
`LibraryOperationLease`.

The reference supplies:

- exact source-to-Library, API-declaration, and optional implementation
  correspondence;
- exact assembly and companion content references with closed roles;
- compiled-XML-to-assembly and Portable-PDB-to-implementation association;
- Artifact registration, generation, and provenance evidence; and
- the exact resource-free content identities that contributions may name.

The subject selects its type or member only from the correspondence's exact
`ApiSurface`; it does not accept an independently acquired surface or recreate
association through equivalent assembly identity. An eligible compiled-XML
content reference must belong to that Library, carry the
`CompiledXmlDocumentation` role, and name the correspondence's API content as
its associated assembly. Authored demand additionally names the exact
implementation content and target preserved by the applicable source-owner
and Metadata correspondence.

The resource-free references carry no stream, callback, opener, or other live
authority. The transferred lease authorizes synchronous snapshots of exact
content references in that Library. DocumentationHouse materializes a detached
or independently owned value before a callback returns; no borrow, view, or
owner-backed span crosses `await`.

PackageHouse, PlatformHouse, and direct-library or Workspace composition
construct the shared Library owner and reference. Separately compiled adapters
bind source-specific evidence into resource-free DocumentationHouse
contributions; they do not construct a documentation-ready wrapper or
substitute lifetime. DocumentationHouse does not reopen a package, resolve a
platform target, derive a sibling path, enumerate an ambient directory,
directly invoke SourceHouse, or reacquire content already represented by the
Library.

The PlatformHouse integration follows the same boundary. A one-Library
realization may explicitly request `CompiledXmlDocumentation` content together
with a reference view. Installed and package-backed Platform sources then
snapshot the exact same-basename reference-pack companion, when present, under
the existing byte, XML-document, duration, and source-operation bounds.
PlatformHouse publishes that content in the assembly's Artifact generation and
constructs its `CompiledXmlDocumentation` Library correspondence, but performs
no XML parsing or documentation settlement. A completed requested realization
without that companion proves authoritative absence; a realization that did
not request it proves only unavailability. The separately compiled
`DotnetInspector.DocumentationHouse.Platform` adapter maps those states to one
source-neutral contribution and retains no Library or Artifact authority.

## Documentation demand

The initial demand is closed:

- **CompiledXml** requests only exact compiler XML documentation;
- **AuthoredSourceDocumentation** requests only documentation attached to the
  exact declaration in SourceHouse-authored source; and
- **CompiledXmlAndAuthoredSourceDocumentation** requests both independent
  attempts, executed in that order.

The House requires an explicit demand. Product hosts may use `CompiledXml` as
their inexpensive default, but that is a host gesture lowered to typed demand,
not an omitted House policy.

One channel never widens into the other. Missing XML does not authorize PDB or
source acquisition. SourceHouse failure does not suppress available XML.
Requesting both channels means both attempts are retained; it is not an
authored-source fallback hidden behind an XML miss.

Compiled XML is always the cheap first stage. Its content reference names
already-realized local or in-memory content, and the attempt performs no
acquisition or network work. For combined demand, DocumentationHouse completes
the compiled-XML attempt and ends every Library borrow before invoking the
deferred authored operation. It does not start both effects concurrently.

The sequence does not short-circuit the explicitly requested authored attempt
when XML is available. Consumers that need only the inexpensive result request
`CompiledXml`; a future cheap-first fallback demand would be a distinct policy,
not a reinterpretation of the combined demand.

## Host-authorized operation plan

The immutable plan contains only capabilities authorized for this operation:

- resource-free compiled-XML selection and source evidence;
- an optional source-neutral deferred authored-documentation operation bound by
  its adapter to one exact pre-authorized SourceHouse `AuthoredOnly` request and
  operation plan;
- XML, source-document, character, candidate, field, and deadline limits;
- operation identity and policy generation; and
- caller cancellation.

The transferred Library lease is a separate consuming input, not a hidden plan
capability. It does not prove an XML entry exists. A deferred operation does not
prove SourceHouse will produce authored source or that documentation is
attached to its mapped declaration. DocumentationHouse never turns
availability of a desktop filesystem, HTTP client, SourceHouse service, PDB, or
source path into authorization. The source integration adapter captures the
caller-authorized SourceHouse request, PDB policy, acquisition capabilities,
bounds, and policy generation before constructing the operation.

The operation is single-invocation. When the House reaches the authored stage,
invocation passes the exact `LibraryReference`, selected implementation content
reference, current DocumentationHouse operation identity, remaining
source/document/character/deadline ledger, and caller cancellation, and
transfers ownership of the matching Library operation lease. Acceptance is the
linearized ownership boundary: DocumentationHouse no longer owns the lease, and
the operation must settle it on every path unless SourceHouse accepts a single
onward transfer. The operation cannot spend work before invocation, exceed the
remaining House limits, or publish a result for a different operation.
SourceHouse settles the lease when it accepts ownership and returns
resource-free settlement evidence before the adapter extracts documentation
from detached authored source.

Operation ordering is closed:

1. accept and validate the exact request, `LibraryReference`, selected API
   content, transferred matching `LibraryOperationLease`, demand, and plan;
2. for `CompiledXml` or combined demand, settle compiled XML through
   synchronous Library snapshots;
3. for `AuthoredSourceDocumentation` alone, transfer the lease into the
   deferred operation immediately after validation;
4. for combined demand, transfer the lease only after the compiled attempt
   reaches its terminal state and every Library borrow has ended;
5. require the operation to settle every pre-SourceHouse terminal path or
   transfer the lease onward once to SourceHouse;
6. perform no Library access in DocumentationHouse after operation acceptance;
   and
7. compose the retained channel attempts and field evidence from detached
   results.

A terminal compiled attempt of **Available**, **Absent**, **Unavailable**,
**Ambiguous**, **Rejected**, **Failed**, or **Incomplete** does not suppress the
requested authored attempt. A channel-local **Rejected** attempt means one
optional contribution or its correspondence was invalid after the request and
Library inputs were accepted; the independent authored attempt still runs.

A request-level rejection means the subject, exact Library, selected API
content, operation lease, or required owner-issued correspondence is invalid.
It prevents all channel work and settles the lease in DocumentationHouse.
Operation unavailability before invocation becomes an authored
**Unavailable** attempt and also leaves DocumentationHouse responsible for
settlement. After operation acceptance, the operation owns every pre-SourceHouse
terminal path and SourceHouse owns every path after its acceptance;
DocumentationHouse cannot reuse or release the moved lease.

## Compiled XML evidence

Each resource-free compiled-XML candidate contribution binds:

- the exact documentation subject and API-declaration supplier;
- the exact `LibraryReference` and request-selected API content reference;
- the opaque source reference that supplied the assembly;
- one exact compiled-XML `LibraryContentReference` associated with that API
  content;
- complete, partial, or unavailable companion-selection evidence;
- source-specific provenance and failure evidence.

The input set may instead carry complete absence, partial selection, or
unavailable evidence without a candidate content reference. A candidate does
not erase evidence about the completeness of the owner-authorized selection.

The candidate coordinate may be a package entry such as
`lib/net10.0/System.Text.Json.xml` or a reference-pack companion such as
`ref/net11.0/System.Text.Json.xml`. The coordinate is not proof. The adapter
must bind the owner-issued selection evidence to the exact Library content
reference before DocumentationHouse receives it.

The House snapshots the exact XML content reference through its transferred
Library lease and invokes the bounded CSharpText reader with the exact compiler
ID. The snapshot callback returns only detached parser input or a detached
parsed result. The reader owns XML grammar and resource limits; the House
classifies the effect on settlement:

- a matching entry is available;
- a complete readable companion without the entry is absent;
- no authorized companion is unavailable;
- conflicting eligible companions without explicit precedence are ambiguous;
- invalid correspondence or identity is rejected;
- malformed or failed reading is failed; and
- partial candidate evidence or exhausted finite work is incomplete.

An adapter may provide explicit precedence among several associated
companions. DocumentationHouse does not infer precedence from path order,
framework spelling, package layout, or file timestamps.

The PackageHouse adapter is implemented in the separately compiled
`DotnetInspector.DocumentationHouse.Packages` project. It accepts one exact
`PackageHouseLibraryMaterializationReceipt` and the caller's documentation
subject, then binds the receipt's Library and API content to the exact
API-associated compiled-XML content materialized by PackageHouse. A present
companion becomes one candidate contribution; a completed materialization
without that companion becomes authoritative absence.

The implemented package adapter consumes PackageHouse's compile
materialization. A configured in-package assembly outside the exact selected
compile-asset set is not relabeled as a direct Library: the host preserves its
ordinary inspection result without compiled-documentation enrichment until an
owner-issued adapter exists for that asset kind.

The adapter creates only resource-free contribution evidence. It does not
retain the PackageHouse payload, `LibraryContentOwner`, `ArtifactSetSession`,
or a `LibraryOperationLease`, and it does not invoke LibraryMetadata or
DocumentationHouse. Orchestration retains the two owners, issues and settles a
first operation lease while obtaining
`LibraryApiSurfaceCorrespondence`, then issues a distinct second operation
lease and transfers it to `DocumentationHouse.ExecuteAsync`.

The direct-Library adapter is implemented in the separately compiled
`DotnetInspector.DocumentationHouse.Direct` project. It accepts one exact
direct Artifact-backed `LibraryReference` and the caller's documentation
subject, then emits one candidate for every compiled-XML content reference
associated with that Library's exact API assembly. It does not invent
precedence when the direct Library contains several companions, so
DocumentationHouse retains its ordinary ambiguity behavior.

A direct Library with no admitted XML companion produces unavailable evidence,
not authoritative absence. Unlike PackageHouse's completed materialization
receipt, a bare direct Library does not prove that its producer exhaustively
searched an external source for companions. The adapter rejects a
source-coordinated Library rather than relabeling package, platform, project,
or local-source evidence as direct-Library evidence. It retains no
`LibraryContentOwner`, `ArtifactSetSession`, or `LibraryOperationLease` and
does not invoke LibraryMetadata or DocumentationHouse.

## Authored-source documentation contribution

Authored-source documentation starts with one exact implementation target and
content reference and one deferred source-neutral operation bound to the exact
`LibraryReference`, a matching SourceHouse `AuthoredOnly` request, and its
operation plan. When invoked, DocumentationHouse transfers the Library
operation lease into the operation. The operation invokes that exact
SourceHouse request and returns a detached authored-documentation contribution,
resource-free lease-settlement evidence, or its typed non-success. Decompiled
C# is never a documentation producer.

Operation acceptance transfers ownership even when the later SourceHouse call
cannot start. The operation settles the lease itself on binding rejection,
failure, cancellation, or incomplete work before SourceHouse accepts it. Once
SourceHouse accepts the lease, SourceHouse is the sole owner and final Library
consumer; the operation performs only detached CSharpText work afterward.

SourceHouse owns:

- whether supplied, embedded, or acquired PDB content is usable;
- PDB and SourceLink interpretation;
- local, repository, content-store, and remote source candidate ordering;
- checksum verification and source decoding;
- exact or inferred mapping strength;
- source-unit scope and partiality; and
- the authored-source attempt and receipt.

Before invocation, the operation binding is eligible only when its exact
`LibraryReference`, implementation content reference, implementation target,
SourceHouse policy generation, PDB-access policy, request identity, and
operation-plan identity match the DocumentationHouse request and plan.

After invocation, the returned contribution is eligible only when its
SourceHouse receipt confirms that binding and supplies the matching result
identity, source-document identity, checksum evidence, mapping evidence, and
trusted physical-declaration correspondence identity and generation from
issue #6584. It must also confirm settlement of the transferred Library lease.
A result from a different Library, content reference, request, policy, target,
document, or declaration is rejected rather than reused.

The source integration adapter passes the owner-issued authored source and
correlation evidence to the CSharpText operation owned by #6583. That focused
CSharpText design owns its model-free input and result contract, lexical and
declaration mechanics, attached-comment grammar, limits, and uncertainty. This
design neither requires a particular CSharpText implementation nor redefines
its outcomes.

`SourceHouseDocumentationHouseAdapter` implements this boundary in
`DotnetInspector.DocumentationHouse.Source`. It creates one deferred
source-neutral operation whose construction validates and
retains only the stable DocumentationHouse binding and one pre-authorized
SourceHouse request; it performs no source or Library work. Its single
invocation validates the exact binding, Library operation lease, remaining
source/document limits, and deadline before transferring the lease once to
SourceHouse. Operation-local exits settle the lease locally. After transfer,
the adapter accepts only the exact SourceHouse request and receipt evidence,
uses the complete decoded physical document plus the #6584 exact declaration
span, and invokes `CSharpAuthoredDocumentation`. Its terminal outcomes retain
the CSharpText result, bounded work, opaque source/declaration evidence
references when available, and the final lease consumer without retaining
SourceHouse types or live authority. DocumentationHouse core does not invoke
this operation until slice 18 adds authored demand and channel settlement.

DocumentationHouse consumes the returned owner-issued evidence and preserves
it with the SourceHouse receipt. It does not upgrade filename inference,
member-name equality, proximity, inferred mapping, or an unvouched declaration
span into exact declaration correspondence.

PDB-only evidence cannot produce authored-source **Available** or **Absent**.
Without the #6584 correspondence, the channel is **Unavailable** even when a
checksum-valid destination document contains a declaration at the mapped
lines.

Bodyless members commonly have no sequence point and therefore no
PDB-anchored declaration. They remain unavailable unless another focused owner
issues exact declaration correspondence. The House does not fall back to name
search.

## Channel and field settlement

Every requested channel reaches one terminal attempt:

- **Available** carries parsed documentation and provenance;
- **Absent** means an authoritative contribution completed and found no
  documentation for the exact subject;
- **Unavailable** means no authorized applicable contribution could run;
- **Ambiguous** retains several eligible results without authorized selection;
- **Rejected** retains invalid request or correspondence evidence;
- **Failed** retains operational or parse failure; and
- **Incomplete** retains the finite boundary that prevented an authoritative
  answer.

Channel-level **Rejected** applies only after the request, Library reference,
selected content, and operation lease have been accepted. It retains invalid
optional contribution or producer correspondence without rewriting an
independent requested channel. Invalid core inputs instead produce the
top-level **Rejected** outcome before channel execution.

The authored-source adapter maps CSharpText evidence into the channel attempt
without weakening it:

- documentation attached to one uniquely vouched declaration is
  **Available**;
- a uniquely vouched declaration with no attached documentation is **Absent**;
- no trusted physical-declaration correspondence is **Unavailable**;
- ambiguous or inferred correspondence is **Ambiguous**;
- invalid target, Library, or content correspondence is **Rejected**;
- malformed attached documentation is **Failed**; and
- lexical uncertainty, an unvouched span, unresolved conditional or `#line`
  behavior, or exhausted finite work is **Incomplete**.

When both channels are requested, the result preserves both original attempts.
Each documentation field has one closed evidence state:

- **Selected** carries one non-empty value and its single contributing channel;
- **Corroborated** carries one equal value and every contributing channel;
- **Conflict** carries every differing non-empty value and provenance; or
- **Absent** means no available channel contributed that field and retains the
  channel attempts explaining why.

The House does not choose a winner inside **Conflict**. A concise host may
prefer compiled XML for display, but that presentation policy sits above the
settlement result and cannot erase the authored value, the conflict, or either
channel attempt. Unavailable, failed, ambiguous, rejected, or incomplete
evidence is never rewritten as an empty successful field.

Unresolved `<inheritdoc>` or `<include>` content remains owner-issued parsed
content or an explicit limitation. The House does not fetch, expand, or
silently discard it.

## Result and receipt

The common House outcome is closed:

- **Completed** carries every requested terminal channel attempt, the optional
  field evidence, and a settlement receipt;
- **Rejected** means the request, subject, Library, selected content, lease,
  policy, or required owner-issued correspondence is invalid;
- **Failed** means a House-level operational failure prevented the requested
  attempts from reaching terminal evidence; and
- **Incomplete** means a finite operation boundary prevented settlement.

Caller cancellation remains cancellation.

`Completed` does not mean documentation was available. Authoritative absence,
channel unavailability, and retained producer failure are completed attempts
when the House had enough evidence to classify them. Top-level non-success is
reserved for a condition that prevents the requested attempts themselves from
settling.

The resource-free receipt binds:

- the exact request, demand, subject, and policy generation;
- the exact `LibraryReference`, selected API content reference, and applicable
  implementation content reference;
- every selected and outcome-relevant contribution;
- each opaque source reference and source kind;
- API-declaration and implementation-target correspondence;
- exact XML identity and companion evidence;
- SourceHouse request, result, mapping, checksum, and source-document evidence
  when authored source was requested;
- every channel attempt and field provenance or conflict;
- completion and charged work; and
- the final Library-lease consumer and resource-free settlement evidence,
  distinguishing DocumentationHouse, operation-local, and SourceHouse
  settlement.

The receipt carries no credentials, paths as identity, mutable buffers, live
streams, readers, leases, lifetime authority, borrowed views, or capability
that can repeat acquisition. Every documentation value in a completed result
is fully materialized and detached before settlement.

## Library operation ownership and content borrowing

DocumentationHouse follows
[Library Ownership and Borrowing](library-ownership-and-borrowing.md):

- the host or orchestrator transfers one matching `LibraryOperationLease` into
  the complete async DocumentationHouse operation;
- compiled XML is read only through synchronous snapshots of exact Library
  content references;
- no borrow, callback view, owner-backed span, stream, or reader crosses
  `await`;
- parsed documentation is fully materialized and detached before a snapshot
  callback returns;
- compiled-only settlement and every pre-transfer terminal path settle the
  lease in DocumentationHouse;
- authored settlement transfers the lease to the operation, which settles every
  pre-SourceHouse terminal path or transfers it onward once to SourceHouse as
  the final consumer;
- DocumentationHouse performs no Library access after operation acceptance, and
  the operation performs no Library access after SourceHouse acceptance;
- owner retirement after issuance does not invalidate the lease, while
  retirement before issuance prevents the operation from starting;
- a completed resource-free receipt cannot reopen content; and
- success, rejection, failure, cancellation, and incomplete completion settle
  the lease and every independently acquired resource exactly once.

DocumentationHouse does not mutate a Library, package, platform, SourceHouse,
or Workspace generation. There is no Library refresh or generation operation.
Changed content belongs to a newly realized Library and cannot substitute for
the exact request reference or lease. Publication of a reusable parsed catalog
or Workspace artifact is a separate owner operation.

Cache keys retain the exact `LibraryReference`, selected content references,
subject, demand, policy generation, SourceHouse result identity when
applicable, and parser contract version. Equal source coordinates, paths,
assembly identities, or display labels cannot reuse a result across realized
Libraries.

## Project and dependency boundaries

The logical owner may span a contract seam, composition project, and
source-specific adapters. A project name alone does not define the owner.

The target shape is:

```text
Inspector.Artifacts / Inspector.Resources / DotnetInspector.Libraries
Metadata subject contracts / CSharpText result contracts
               |
               v
DotnetInspector.DocumentationHouse.Contracts
  - source-neutral request, deferred-operation, resource-free contribution,
    result, and receipt shapes
  - opaque source references; no PackageHouse, PlatformHouse, SourceHouse,
    direct-library, or Workspace types
               |
               v
DotnetInspector.DocumentationHouse
  - Library lease consumption, XML snapshots, and channel/field settlement

DotnetInspector.DocumentationHouse.Packages
  -> PackageHouse contracts + Library contracts + DocumentationHouse contracts

DotnetInspector.DocumentationHouse.Platform
  -> PlatformHouse contracts + Library contracts + DocumentationHouse contracts

DotnetInspector.DocumentationHouse.Source
  -> SourceHouse contracts + Library contracts + CSharpText operation
     + DocumentationHouse contracts
  - implements the deferred operation without exposing SourceHouse types

DotnetInspector.DocumentationHouse.Direct
  -> direct Library composition + DocumentationHouse contracts

DotnetInspector.Queries
  -> DocumentationHouse
  - executes an already-authorized source-neutral request
  - retains the exact detached outcome for in-process composition
  - publishes a copied portable terminal outcome as the sole JSON contract

DotnetInspector.PlatformQueries
  -> Queries + PlatformHouse + DocumentationHouse.Platform
  - accepts one completed exact Platform Library realization
  - resolves exact documentation subjects and executes DocumentationHouse
  - owns no package, installed-pack, or host acquisition
```

PackageHouse, PlatformHouse, SourceHouse, direct-Library composition, and
Workspace do not depend on DocumentationHouse contracts or implementation.
Integration assemblies depend toward both the source owner and the
source-neutral DocumentationHouse floor and cannot change either owner's
evidence. The DocumentationHouse core does not reference an integration
assembly; it invokes only the source-neutral deferred-operation contract and
passes the Library lease to it solely by consuming invocation. Queries and
hosts compose the applicable adapter above both owners, capture explicit
authorization, obtain one exact Library operation lease, and transfer it before
the House operation starts. `CompiledDocumentationQuery` owns no source adapter
selection or acquisition. It invokes the House with that prepared request and
transferred lease, then copies the settled result into
`CompiledDocumentationOutcome` before returning.

The exact `DocumentationHouseOutcome` remains available on the in-process
Queries result and is excluded from JSON. It retains reference-scoped Library,
content, subject, and receipt correspondence that must not be mistaken for a
portable interchange identity. The JSON context registers only the
Queries-owned outcome. A completed host adapter serializes that outcome to one
JSON string; the string, not a UTF-8 byte array, is the C#-to-TypeScript
exchange. Its common subject contains only the exact assembly identity and
compiler documentation ID needed for correlation. A required `kind`
discriminator selects one case-specific shape: `available`, `absent`,
`unavailable`, `ambiguous`, `contributionsRejected`,
`malformedOrUnreadableDocument`, `incomplete`, `requestRejected`, or
`contentAccessFailed`.
Available content carries the selected source and documentation. Each
non-available case carries only its applicable reason and bounded source
evidence; repeated source arrays retain at most eight distinct values and state
when more values were omitted. Within every bounded source-evidence list,
evidence that establishes the terminal case precedes contextual evidence, so
the bound cannot retain only evidence for a weaker outcome. An `absent` result
caused by a selected compiled XML document that lacks the requested member
carries that selected candidate as its sole decisive evidence. An `absent`
result with no selected candidate prioritizes contributions that
authoritatively report absence. An `incomplete` result with a selected
candidate prioritizes that candidate before the other observed contributions;
a companion-selection-partial result similarly prioritizes partial
contributions.
Malformed or unreadable contributed XML and top-level content-access failure
are separate singleton-cause wire cases. Their concrete discriminators encode
the failure reason without a redundant reason property or a shared enum that
would admit cross-case combinations the producer cannot emit. Both retain the
selected source that established the terminal case.

Request, operation-plan, policy-generation, demand, work-charge, lease-consumer,
duplicate type/member anchors, full contribution history, and nullable
alternatives for other terminal cases remain only in the exact in-process
outcome. The portable outcome contains no Library or Artifact reference, lease,
reader, stream, callback, or reopening capability. This is the producer-owned
content boundary required by
[host-observable content kinds](host-observable-content-kinds.md#serialization-ready-schema);
serialization does not walk a lower-owner correspondence graph after the
operation or Library lifetime ends. The discriminator and case-specific
properties are also the semantic C#-to-TypeScript contract. Inspect Web exposes
separate package and platform operations so each five-string input contract is
semantic and ordinary package calls do not acquire a platform-only coordinate.
Both return the same generated nine-case union as one JSON string; no UTF-8
byte-array transport or host-local duplicate shape is introduced.

This L1 operation returns the bare Queries result. A completed L2 or host
handoff wraps the portable outcome in `InspectionEnvelope<TContent>`; it does
not envelope the lower House outcome or nest envelopes around House and Query
stages.

The product implementation belongs in a host-neutral `DotnetInspector`
boundary above Metadata, CSharpText, and `DotnetInspector.Libraries`. It remains
SRM-only, Roslyn-free, NativeAOT-compatible, and portable to single-threaded
Browser/Wasm.

## Pathological cases

### Equal XML IDs in two assemblies

Two libraries contain `M:Example.Widget.Parse(System.String)`. Only the XML
contribution bound to the selected library and API-declaration supplier may
satisfy the request. A same-named companion from the other library is
ineligible.

### Library reference and operation authority disagree

A package request names one exact `LibraryReference` and API assembly while the
transferred lease belongs to another realization of the same package
coordinate. DocumentationHouse rejects the mismatch before parsing. It does
not open a same-named XML entry or reacquire the requested package.

### Library retirement begins during settlement

Retirement before lease issuance prevents DocumentationHouse from starting.
Retirement after issuance preserves the in-flight lease. The House completes
its compiled XML snapshot and either settles the lease or transfers it to the
operation. The operation then settles it or transfers it once to SourceHouse
while the Library owner drains.

### Reference XML and implementation source

A platform reference contribution supplies exact XML while the corresponding
implementation target supplies SourceHouse-authored source. Both are eligible
only through PlatformHouse forwarding and view-correspondence evidence.
Compiled XML must come from the reference artifact associated with Metadata's
terminal `ResolvedTypeDefinition` supplier after forwarding; an XML companion
associated only with the starting facade is ineligible. The House retains the
distinct terminal reference and implementation suppliers.

### XML is absent and authored source is available

The exact package companion is authoritatively absent. The caller separately
authorized an `AuthoredOnly` SourceHouse plan through the deferred operation.
DocumentationHouse records XML absence, then invokes the operation and retains
the available authored documentation.

### Documentation fields disagree

Compiled XML and an attached checksum-verified source comment contain
different summaries. The field is a conflict carrying both values and
provenances. A concise host may display XML first, but the House does not
select it as the settled value.

### The mapped declaration is not vouched

The PDB maps a method into source containing an unresolved conditional or
`#line` remapping that prevents safe physical-line correlation. CSharpText
reports uncertainty. The House does not search for the method name elsewhere.

### The mapped destination is valid but belongs to another declaration

One compilation input uses `#line` to map a MethodDef into another real source
document whose checksum and declaration text are valid. SourceHouse may retain
that destination evidence, but without the stronger #6584 physical-declaration
correspondence DocumentationHouse reports the authored channel as unavailable.
It does not attach the destination declaration's comment to the requested
MethodDef.

### A bodyless member has no source mapping

An interface member has exact compiled XML but no sequence point. The XML
channel may be available while authored-source documentation is unavailable.
Name equality does not manufacture a source declaration.

### Malformed documentation

An XML companion or source comment exceeds its bound or contains malformed
XML. The attempt is failed or incomplete. It never becomes a plain-text
success.

### Browser/Wasm has no paths

An in-memory package Library exposes exact assembly, XML, and PDB content
references. The host transfers a matching operation lease to
DocumentationHouse, which uses the same snapshots, onward transfer, and
settlement as desktop behavior without requiring a filesystem path.

## Analogous implementation evidence

The analogues inform the boundary; they are not architectural authority.

| Implementation | Observed behavior | DocumentationHouse lesson |
| --- | --- | --- |
| [C# XML documentation](https://learn.microsoft.com/dotnet/csharp/language-reference/xmldoc/) | The compiler emits member-addressed documentation into a companion XML artifact. | Preserve exact compiler identity and artifact association rather than matching display signatures. |
| Roslyn documentation-comment IDs, adopted by #6497 | Metadata symbols have exact compiler IDs with generic and overload structure. | Issue identity from structural facts and never reconstruct it in a host. |
| Roslyn Metadata-as-Source provider ordering, also used as SourceHouse evidence | Authored source and reconstructed source are distinct producers with explicit policy. | Consume authored SourceHouse evidence only; decompiled C# is not documentation provenance. |
| Current dotnet-inspect CLI and Browser implementations | Both locate and parse documentation independently. | Centralize settlement while retaining host authorization and presentation. |

The real motivating asset is `System.Text.Json` 10.0.0, whose generic
`JsonSerializer.Deserialize<TValue>` overload demonstrates why exact compiler
identity matters. The platform counterpart is the `System.Text.Json` reference
assembly and XML companion in the .NET 11 reference pack.

## Production adoption and retirement

[#6579](https://github.com/richlander/dotnet-inspect/issues/6579) owns the
22-slice end-to-end plan:

1. lock this focused contract and transfer documentation settlement out of
   PlatformHouse;
2. reconcile DocumentationHouse inputs, contributions, and lifetime with the
   shared Library ownership contract under #6950;
3. **Completed.** Implement compiled-XML attempt and receipt settlement over
   CSharpText;
4. **Completed.** Add the PackageHouse adapter;
5. **Completed.** Add the direct-library adapter;
6. **Completed.** Add the shared Queries compiled-documentation result;
7. **Completed.** Adopt package compiled documentation in Inspect Web;
8. **Completed.** Adopt package and direct-library compiled documentation in
   the CLI;
9. **Completed.** Add the PlatformHouse adapter;
10. **Completed.** Remove PlatformHouse's superseded documentation contracts;
11. **Completed.** Adopt platform reference-pack compiled documentation in
    Inspect Web;
12. **Completed.** Adopt platform reference-pack compiled documentation in the
    CLI;
13. **Completed.** Lock the focused SourceHouse physical-declaration
    correspondence contract under #6584;
14. **Completed.** Implement one production SourceHouse path that issues that
    trusted correspondence;
15. **Completed.** Lock the focused CSharpText authored-documentation contract
    under #6583;
16. **Completed.** Implement the owner-issued CSharpText
    authored-documentation operation;
17. **Completed.** Add the SourceHouse-to-DocumentationHouse integration
    adapter;
18. add authored-source channel and field settlement to DocumentationHouse;
19. extend Queries with authored-source documentation evidence;
20. adopt authored-source documentation in Inspect Web;
21. adopt authored-source documentation in the CLI and remove the remaining
    `SourceEnricher` composition; and
22. delete `DocCommentParser` from CSharpText after all consumers are gone and
    close the documentation portion of #6335.

Each slice changes one owner or one production consumer. The count changes only
through an explicit tracker update that preserves both hosts and retirement of
the previous architecture.

Issue #6497 owns compiler XML identity, bounded XML reading, and retirement of
`XmlDocFileParser` and `BrowserXmlDocumentation`. Those are prerequisites, not
DocumentationHouse slices, and this plan does not duplicate their retirement.
Workspace publication and consumption of reusable documentation artifacts are
not initial scope; adding them requires a separately counted production-
consumer slice.

## Evidence and required gates

This design introduces no independent distributed state machine. Artifact and
resource lifetime, SourceHouse settlement, Metadata resolution, and parser
behavior remain under their focused owners. A new TLA+ model would duplicate
those contracts rather than establish this request/settlement boundary.

Implementation and adoption slices own these Release gates:

| Property | Required gate |
| --- | --- |
| Library-scoped subject | Equal XML IDs in two assemblies cannot cross-satisfy one request. |
| Library correspondence | A foreign Library reference, selected content reference, or operation lease rejects before parsing. |
| Explicit authorization | No SourceHouse, source/PDB discovery or acquisition, repository, content-store, or network work occurs without authored demand and a pre-authorized deferred operation. Snapshots of already-realized XML require compiled demand and the transferred Library lease. |
| Cheap-first ordering | Operation construction starts no source work; combined demand reaches a terminal detached compiled-XML attempt and ends every borrow before the operation receives the lease once, and XML availability does not suppress the requested source attempt. |
| Exact XML lookup | Compiled XML uses the Metadata-issued compiler ID and associated contribution. |
| Bounded repeated lookup | A multi-subject request scans each selected compiled-XML companion once per matching read policy, retains only that policy's requested exact IDs under independent per-request retained-text budgets, rechecks the latest matching request deadline between snapshot and parse, and reports actual parsing work once. |
| Authoritative absence | XML absence requires complete readable companion evidence for the exact subject. |
| Independent channels | Success, absence, failure, or incompleteness in one channel does not rewrite the other. |
| Authored-source boundary | Source documentation consumes SourceHouse-authored evidence plus #6584 trusted physical-declaration correspondence and never decompiled or PDB-only output. |
| Declaration correspondence | The owner-issued CSharpText gate from #6583 returns attached documentation or visible uncertainty without name-based fallback; DocumentationHouse preserves that result. |
| Field provenance | Filled, corroborated, and conflicting fields retain every contributing value and origin. |
| Visible failure | Malformed or over-budget XML/comment content never becomes an empty or plain-text success. |
| Resource lifetime | Success, rejection, failure, incompleteness, and cancellation name exactly one current lease owner; the operation settles pre-SourceHouse exits or transfers once to SourceHouse, and no prior owner accesses the Library after transfer. |
| Owner retirement | Issuance after retirement fails visibly; retirement after issuance drains without invalidating DocumentationHouse or SourceHouse use. |
| Browser portability | In-memory package and platform content requires no filesystem path. |
| Host parity | Representative CLI and Browser requests produce equivalent House demand and settlement. |
| Retirement | #6497 deletes its owned legacy XML readers; DocumentationHouse adoption deletes host-local companion selection and merge policy; final CSharpText adoption deletes `DocCommentParser`. |

`CompiledXmlDocumentationHouseTests` is the Release gate for the implemented
slice. It exercises the real `System.Text.Json` 10.0.0 assembly and XML
companion, equal-ID cross-Library substitution, readable absence, unavailable
and partial selection, distinct-content precedence with duplicate
observations, malformed and bounded XML, the deadline boundary between content
snapshot and parsing, cancellation, in-flight owner retirement, and the
resource-free result closure.
`DeadlineReachedDuringSnapshot_PreventsCompiledXmlParsing` proves that an
expired latest matching deadline stops before parsing while retaining the
snapshot byte charge.
`SharedParseCompletedAfterFirstDeadline_IsChargedExactlyOnce` proves that a
later live matching request may authorize the shared parse and that the
request performing it retains the sole parse and byte charge even when its own
terminal attempt is deadline-incomplete.

`AuthoredSourceDocumentationAdapterTests` gates the SourceHouse integration
over a real direct C# build of `CSharpText.MemberSlicing`. It demonstrates that
the exact physical declaration for `MemberTextSlicer.ExtractMemberText`
produces parsed authored documentation, operation construction starts no source
work, and SourceHouse is the sole final lease consumer after transfer.
Neighboring gates prove that a foreign DocumentationHouse binding and an
API-only content from a Library with distinct API and implementation
assemblies reject before source work; an insufficient remaining source-byte
budget rejects before source work and settles the lease locally; pre-transfer
cancellation settles without source work; a second invocation performs no
additional work and settles its newly supplied lease; and checksum-valid PDB
source without the #6584 physical-input identity remains unavailable without
reaching CSharpText. The existing public-outcome closure gate includes the
authored operation contracts and proves that completed outcomes retain no lease,
content owner, stream, delegate, or disposable authority. Per the operator's
issue #8017 evidence choice, this slice adds no repository-wide
source-dependency absence rule; project references establish the intended
direction, while Release behavior and adversarial design review provide the
slice evidence.

`PackageHouseExecutionTests` gates the PackageHouse adapter over real
`System.Text.Json` 10.0.0 package assembly and XML content. It demonstrates the
separate LibraryMetadata and DocumentationHouse operation leases, exact
candidate settlement, authoritative missing-companion absence, detached
documentation, owner retirement, and rejection when byte-identical content
from another PackageHouse materialization is offered for the selected subject.

`CompiledXmlDocumentationHouseTests` also gates the direct-Library adapter over
a real direct Artifact-backed `System.Text.Json` 10.0.0 Library. It demonstrates
exact candidate settlement, unavailable evidence when no XML companion was
admitted, preservation of multiple companions without invented precedence,
rejection of source-coordinated Libraries, and rejection when byte-identical
content from another direct Library is offered for the selected subject.

`CompiledDocumentationQueryTests` gates the shared Queries result over the same
real `System.Text.Json` 10.0.0 assembly and documentation. It demonstrates that
multi-subject type/member execution scans the selected companion once while
publishing independent typed outcomes, that same-policy requests retain
independent text budgets, and that heterogeneous read policies retain only
their own subjects. Both batch shapes match their corresponding single-request
outcomes. It also demonstrates that the exact House outcome remains available
in process, the transferred operation is settled, the Library owner can retire
before serialization, and the
source-generated JSON contract round trips the separately copied portable
outcome. Neighboring absent, unavailable, contribution-rejected,
malformed-document, content-access-failed, and top-level lease-rejected results
each retain a discriminator-specific shape rather than becoming empty
documentation. Both failure shapes retain the selected source and round trip
without a redundant reason property.
The absent gates cover both a selected document without the requested member
and a package-shaped authoritative missing-companion contribution preceded by
eight distinct unavailable sources; each preserves its applicable source
provenance after Library retirement.
The selected-incomplete gate similarly places eight unavailable sources before
a candidate whose compiled XML exceeds the byte limit and proves that the
selected candidate remains first in the bounded portable evidence.
The neighboring companion-selection-partial gate proves the same ordering when
no candidate was selected and the partial contribution itself establishes
incompleteness.
A four-million-contribution input constrained by a nine-entry House limit
retains eight distinct source, kind, and precedence values plus explicit
truncation without presenting unfinished work as available documentation. That
JSON string is 999 UTF-16 code units and is gated at no more than 1,024 code
units. The available real-package JSON string is 1,028 code units and is gated
at no more than 1,100 code units.
`PortableContract_IsDiscriminatedAndQueriesOwned` provides full public-type
closure plus exact discriminator and case-property coverage for the claim that
the portable outcome contains only primitive, string, enum, nullable,
immutable-array, and Queries-owned values.

The CLI documentation command gates require a projected extension method on
its receiver type to receive the exact declaration-owned compiled
documentation, and require a direct Library without a companion to omit both
documentation and `XmlDoc` source-resolution provenance. The neighboring
declaring extension type, explicit `--all` non-public member, malformed
companion, and configured-package compile-asset paths retain their established
outcomes. A configured runtime-only package asset gates the non-compile
boundary: detailed inspection remains successful without relabeling its
package evidence as direct-Library evidence.

`BrowserEngineBoundaryTests.QueryMemberDocumentation_UsesSharedPackageDocumentationContract`
executes the production package export over the real `System.Text.Json` 10.0.0
package and requires the Queries-owned `available` case and expected member
summary.
`BrowserEngineBoundaryTests.QueryMemberDocumentation_MissingCompanionIsAuthoritativeAbsence`
gates the neighboring package-without-companion case as typed authoritative
`absent` evidence rather than empty browser documentation.
`BrowserEngineBoundaryTests.QueryMemberDocumentation_SelectableDeclarationShapesReturnAvailable`
uses a compiled package fixture to gate both documentation on a non-public type
exposed by the browser accessibility surface and declaration selection when an
extension-method projection shares its compiler XML identity. The generated
`QueryMemberDocumentation_BrowserAdmittedLargeSurfaceReturnsAvailable` test
uses `Microsoft.FluentUI.AspNetCore.Components.Icons` 4.1.0 to require that
documentation lookup admits the same large API surface as the browser member
selector. The generated Inspect Web facade and frontend member-detail tests
gate exhaustive consumption of the same discriminated contract across the
C#-to-TypeScript JSON-string boundary.

`BrowserEngineBoundaryTests.QueryPlatformMemberDocumentation_UsesSharedPlatformContract`
and its missing-companion neighbor gate platform reference-pack settlement,
`Platform` source evidence, the exact available-case property set, and payload
bounds of 4,096 UTF-16 code units for available content and 1,024 for absence.
The package-adoption Firefox gate assembles deterministic standard runtime and
reference packages from the cataloged documentation fixture, calls the
five-string platform operation through the published production Worker, and
requires the same typed summary and available payload bound in Browser/Wasm.

The design-only PR is Markdown-only and requires `markdownlint`. The
implementation slices add only the gates for the property they adopt.

No repository-wide bypass-absence gate is claimed by this design. Representative
adoption gates and deletion of the named legacy implementations provide the
bounded retirement evidence.

## Non-claims

This design does not:

- create a global XML-documentation-ID namespace;
- infer package, platform, assembly, or source identity from a file name;
- implement package, platform, assembly, PDB, source-document, or network
  acquisition; a pre-authorized deferred operation may perform SourceHouse work
  under its owning policy;
- redefine PackageHouse, PlatformHouse, SourceHouse, Metadata, SourceLink,
  CSharpText, artifact, or resource-owner internals;
- treat decompiled C# as authored documentation;
- promise source documentation for bodyless or ambiguously mapped members;
- expand `<inheritdoc>` or `<include>`;
- define documentation rendering, section defaults, or host interaction;
- require a desktop filesystem or multithreaded runtime;
- permit inspected-assembly loading or a Roslyn product dependency;
- add WinMD support; or
- create a generic Houses assembly.
