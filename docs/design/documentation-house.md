# DocumentationHouse composition

## Status and approved scope

This document is the normative owner for the host-neutral
`DocumentationHouse` composition boundary. It is tracked by
[#6579](https://github.com/richlander/dotnet-inspect/issues/6579).

The user approved DocumentationHouse as the product-facing documentation
settlement concept and approved retirement of `DocCommentParser`. The House
settles compiled XML documentation and documentation extracted from authored
source without acquiring packages, platforms, PDBs, or source bytes itself.

This is one focused new-owner effort under
[Design Scope](../design-scope.md). It transfers one cohesive responsibility:
documentation settlement moves from
[PlatformHouse](platform-house-reference-processing.md), whose remaining
target, realization, view-correspondence, forwarding, and library-handoff
authority is unchanged. The existing PlatformHouse documentation contracts and
host-local documentation composition are migration evidence, not the target
architecture.

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
- [PDB acquisition](../pdb-acquisition.md) for SourceLink interpretation,
  checksum semantics, and authored-source evidence; and
- [Resource ownership and borrowing](resource-ownership-and-borrowing.md) for
  operation-scoped content access and resource-free receipts.

## Authority and exact claim

**DocumentationHouse Composition** owns:

> Given one exact library-scoped documentation subject, one owner-issued
> documentation-ready library representation, one explicit documentation
> demand, one host-authorized operation plan, and finite work, settle compiled
> XML and authored-source documentation into independent typed attempts and
> deterministic field evidence that retains artifact, source, subject,
> generation, conflict, completion, failure, and lifetime correspondence
> without reconstructing authority from paths, names, or display text.

The owner defines:

- the product-facing `DocumentationHouse` facade;
- the exact library-scoped documentation subject consumed by the House;
- closed compiled-XML and authored-source documentation demand;
- the host-authorized documentation operation plan;
- House-facing compiled-XML and authored-source contribution contracts;
- contribution validation, execution, ordering, and short-circuiting;
- independent per-channel attempts and terminal outcomes;
- field-level single-source, corroboration, conflict, and provenance states;
- the common result envelope and resource-free settlement receipt;
- visible absent, unavailable, ambiguous, rejected, failed, and incomplete
  evidence; and
- the rule that hosts, Queries, PackageHouse, PlatformHouse, SourceHouse, and
  presentation do not recreate documentation settlement.

It does not define:

- package, platform, project, direct-library, or Workspace realization;
- library, assembly, artifact, XML companion, PDB, source-document, or
  declaration identity;
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
- Workspace admission, replacement, revision, lease, or binding policy;
- CLI flags, browser interaction, section selection, rendering, or serialized
  transport; or
- ambient filesystem, network, credential, cache, or cancellation policy.

Those owners issue typed identities, content, capabilities, and outcomes.
DocumentationHouse composes them and preserves their evidence.

## Why documentation needs a House

Documentation currently crosses independent producers and host-local policy:

```text
selected package or platform library
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

The current CLI `SourceEnricher` combines package-adjacent and platform
reference-pack XML selection, source acquisition, comment search, parsing,
field merging, and warnings. Inspect Web separately derives an XML path from a
package assembly and parses it with a Browser-local reader. PlatformHouse
contracts express a third settlement path. DocumentationHouse replaces these
with one source-neutral product boundary.

## Relationship to adjacent owners

The target dependency direction is:

```text
Metadata ------------------------------> exact documentation subject
CSharpText ----------------------------> owner-issued XML and comment results
SourceHouse ---------------------------> deferred AuthoredOnly provider

Package documentation adapter --------> PackageHouse + DocumentationHouse
Platform documentation adapter -------> PlatformHouse + DocumentationHouse
direct-library documentation adapter -> artifact owner + DocumentationHouse
source documentation adapter ---------> SourceHouse + DocumentationHouse

Queries -------------------------------> DocumentationHouse
CLI / Browser -------------------------> Queries
```

DocumentationHouse invokes only a source-neutral deferred authored-
documentation provider after the operation reaches the source stage. The
SourceHouse integration assembly implements that provider over one
pre-authorized exact `AuthoredOnly` request and plan. DocumentationHouse never
references SourceHouse types, chooses PDB policy, or widens source
authorization. SourceHouse does not reference DocumentationHouse. PackageHouse
and PlatformHouse likewise do not call DocumentationHouse; integration
assemblies above both owners bind their owner-issued evidence to the
source-neutral House contribution contract. This keeps every dependency
acyclic and avoids
`PackageHouse -> DocumentationHouse -> PackageHouse` and equivalent platform
and source cycles.

An adapter is not a second settlement owner. It proves that live content and
resource-free evidence describe the same owner-issued generation, then exposes
only the narrow contribution DocumentationHouse consumes. The application
orchestrator retains the source owner's original receipt separately; the
source-neutral contribution carries an opaque owner-issued evidence reference,
not a concrete PackageHouse, PlatformHouse, SourceHouse, or Workspace type.
The deferred provider is a source-neutral operation capability, not a
SourceHouse result or evidence that authored documentation is available.
It is cold: construction performs no SourceHouse, filesystem, repository,
content-store, or network work and does not start a background task.

## Exact documentation subject

Every request names one type or member in one exact library context. The
subject retains:

- the exact Metadata type or member target;
- the exact compiler XML-documentation identity when compiled XML is
  requested;
- the logical library and physical API-declaration supplier;
- the owner-issued library, assembly, and generation correspondence;
- reference forwarding or view correspondence when the public API subject and
  implementation source target differ; and
- the exact implementation target used for authored source when that channel
  is requested.

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

## Documentation-ready library representation

The House consumes an owner-issued, content-backed representation rather than
a path bundle. It supplies:

- exact logical-library and physical-assembly correspondence;
- the API-declaration supplier used to issue the documentation subject;
- guarded repeatable access to already-realized content;
- content and artifact generation identity;
- an opaque owner-issued source reference and source kind;
- zero or more compiled-XML contributions;
- optional exact reference-to-implementation correspondence that a deferred
  source provider may consume; and
- the issuer-provided lifetime under which each contribution can be borrowed.

The representation does not prove that documentation exists. It only binds
the exact request to content and capabilities the operation is permitted to
consider.

PackageHouse and PlatformHouse own construction and validation of their
representations. Separately compiled adapters above both owners construct the
source-neutral contribution; neither source owner references DocumentationHouse.
DocumentationHouse does not reopen a package, resolve a platform target, derive
a sibling path, enumerate an ambient directory, directly invoke SourceHouse, or
reacquire content already supplied.

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

Compiled XML is always the cheap first stage. Its contribution is already
realized, local or in-memory content, and the attempt performs no acquisition
or network work. For combined demand, DocumentationHouse completes the
compiled-XML attempt before invoking the deferred authored provider. It does
not start both effects concurrently.

The sequence does not short-circuit the explicitly requested authored attempt
when XML is available. Consumers that need only the inexpensive result request
`CompiledXml`; a future cheap-first fallback demand would be a distinct policy,
not a reinterpretation of the combined demand.

## Host-authorized operation plan

The immutable plan contains only capabilities authorized for this operation:

- compiled-XML contribution access;
- an optional source-neutral deferred authored-documentation provider bound by
  its adapter to one exact pre-authorized SourceHouse `AuthoredOnly` request and
  operation plan;
- XML, source-document, character, candidate, field, and deadline limits;
- operation identity and policy generation; and
- caller cancellation.

The plan is capability, not evidence. A content lease does not prove an XML
entry exists. A deferred provider does not prove SourceHouse will produce
authored source or that documentation is attached to its mapped declaration.
DocumentationHouse never turns availability of a desktop filesystem, HTTP
client, SourceHouse service, PDB, or source path into authorization. The
source integration adapter captures the caller-authorized SourceHouse request,
PDB policy, acquisition capabilities, bounds, and policy generation before
constructing the provider.

The provider is single-invocation and receives the current DocumentationHouse
operation identity, remaining source/document/character/deadline ledger, and
caller cancellation when the House reaches the authored stage. It cannot spend
work before invocation, exceed the remaining House limits, or publish a result
for a different operation.

Operation ordering is closed:

1. validate the exact request, representation, demand, and plan;
2. for `CompiledXml` or combined demand, settle compiled XML first;
3. for `AuthoredSourceDocumentation` alone, invoke the deferred provider
   immediately after validation;
4. for combined demand, invoke the deferred provider only after the compiled
   attempt reaches its terminal state; and
5. compose the retained channel attempts and field evidence.

A terminal compiled attempt of **Available**, **Absent**, **Unavailable**,
**Ambiguous**, **Failed**, or **Incomplete** does not suppress the requested
authored attempt. A request-level rejection or caller cancellation prevents
subsequent effects because the operation itself cannot continue.

## Compiled XML contribution

One compiled-XML contribution binds:

- the exact documentation subject and API-declaration supplier;
- the opaque source reference that supplied the assembly;
- artifact and content generation identity;
- one owner-issued XML companion candidate identity;
- complete, partial, or unavailable companion-selection evidence;
- operation-scoped guarded content access; and
- source-specific provenance and failure evidence.

The candidate coordinate may be a package entry such as
`lib/net10.0/System.Text.Json.xml` or a reference-pack companion such as
`ref/net11.0/System.Text.Json.xml`. The coordinate is not proof. The adapter
must establish its association with the selected assembly and generation
before DocumentationHouse receives it.

The House invokes the bounded CSharpText reader with the exact compiler ID.
The reader owns XML grammar and resource limits; the House classifies the
effect on settlement:

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

## Authored-source documentation contribution

Authored-source documentation starts with one exact implementation target and
one deferred source-neutral provider bound to a matching SourceHouse
`AuthoredOnly` request and operation plan. When invoked, the provider requests
that exact source operation and returns a detached authored-documentation
contribution or its typed non-success. Decompiled C# is never a documentation
producer.

SourceHouse owns:

- whether supplied, embedded, or acquired PDB content is usable;
- PDB and SourceLink interpretation;
- local, repository, content-store, and remote source candidate ordering;
- checksum verification and source decoding;
- exact or inferred mapping strength;
- source-unit scope and partiality; and
- the authored-source attempt and receipt.

Before invocation, the provider binding is eligible only when its exact
implementation target, source-ready representation generation, SourceHouse
policy generation, PDB-access policy, request identity, and operation-plan
identity match the DocumentationHouse request and plan.

After invocation, the returned contribution is eligible only when its
SourceHouse receipt confirms that binding and supplies the matching result
identity, source-document identity, checksum evidence, mapping evidence, and
trusted physical-declaration correspondence identity and generation from
issue #6584. A result from a different request, generation, policy, target,
document, or declaration is rejected rather than reused.

The source integration adapter passes the owner-issued authored source and
correlation evidence to the CSharpText operation owned by #6583. That focused
CSharpText design owns its model-free input and result contract, lexical and
declaration mechanics, attached-comment grammar, limits, and uncertainty. This
design neither requires a particular CSharpText implementation nor redefines
its outcomes.

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

The authored-source adapter maps CSharpText evidence into the channel attempt
without weakening it:

- documentation attached to one uniquely vouched declaration is
  **Available**;
- a uniquely vouched declaration with no attached documentation is **Absent**;
- no trusted physical-declaration correspondence is **Unavailable**;
- ambiguous or inferred correspondence is **Ambiguous**;
- invalid target or generation correspondence is **Rejected**;
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
- **Rejected** means the request, subject, representation, policy, or required
  owner-issued correspondence is invalid;
- **Failed** means a House-level operational failure prevented the requested
  attempts from reaching terminal evidence; and
- **Incomplete** means a finite operation boundary or invalidated generation
  prevented settlement.

Caller cancellation remains cancellation.

`Completed` does not mean documentation was available. Authoritative absence,
channel unavailability, and retained producer failure are completed attempts
when the House had enough evidence to classify them. Top-level non-success is
reserved for a condition that prevents the requested attempts themselves from
settling.

The resource-free receipt binds:

- the exact request, demand, subject, and policy generation;
- documentation-ready representation identity and generation;
- every selected and outcome-relevant contribution;
- each opaque source reference and source kind;
- API-declaration and implementation-target correspondence;
- exact XML identity and companion evidence;
- SourceHouse request, result, mapping, checksum, and source-document evidence
  when authored source was requested;
- every channel attempt and field provenance or conflict;
- completion and charged work; and
- the identities and generations of every operation-scoped lifetime used while
  materializing the result.

The receipt carries no credentials, paths as identity, mutable buffers, live
streams, readers, leases, lifetime authority, borrowed views, or capability
that can repeat acquisition. Every documentation value in a completed result
is fully materialized and detached before settlement.

## Content and lifetime

DocumentationHouse follows
[Resource Ownership and Borrowing](resource-ownership-and-borrowing.md):

- artifact owners issue content leases and remain responsible for cleanup;
- adapters prove evidence-to-content generation correspondence;
- the House and parsers borrow content only within the operation;
- parsed documentation is fully materialized and detached from streams,
  readers, buffers, and leases;
- a completed resource-free receipt cannot reopen content;
- invalidated or replaced input generations cannot publish success; and
- failure and cancellation release every acquired or borrowed resource.

The House does not mutate a package, platform, SourceHouse, or Workspace
generation. Publication of a reusable parsed catalog or Workspace artifact is
a separate owner operation.

Cache keys retain the exact subject, demand, policy generation, representation
generation, companion generation, SourceHouse result identity when applicable,
and parser contract version. Equal paths or display labels cannot reuse a
result across generations.

## Project and dependency boundaries

The logical owner may span a contract seam, composition project, and
source-specific adapters. A project name alone does not define the owner.

The target shape is:

```text
Inspector.Artifacts / Inspector.Resources
Metadata subject contracts / CSharpText result contracts
               |
               v
DotnetInspector.DocumentationHouse.Contracts
  - source-neutral request, deferred-provider, contribution, result, and
    receipt shapes
  - opaque source references; no PackageHouse, PlatformHouse, SourceHouse,
    direct-library, or Workspace types
               |
               v
DotnetInspector.DocumentationHouse
  - XML reading and channel/field settlement

DotnetInspector.DocumentationHouse.Packages
  -> PackageHouse contracts + DocumentationHouse contracts

DotnetInspector.DocumentationHouse.Platform
  -> PlatformHouse contracts + DocumentationHouse contracts

DotnetInspector.DocumentationHouse.Source
  -> SourceHouse contracts + CSharpText operation + DocumentationHouse contracts
  - implements the deferred provider without exposing SourceHouse types

DotnetInspector.DocumentationHouse.Direct
  -> direct artifact contracts + DocumentationHouse contracts
```

PackageHouse, PlatformHouse, SourceHouse, direct-artifact owners, and Workspace
do not depend on DocumentationHouse contracts or implementation. Integration
assemblies depend toward both the source owner and the source-neutral
DocumentationHouse floor and cannot change either owner's evidence. The
DocumentationHouse core does not reference an integration assembly; it invokes
only the source-neutral deferred-provider contract. Queries and hosts compose
the applicable adapter above both owners and capture explicit authorization
before the House operation starts.

The product implementation belongs in a host-neutral `DotnetInspector`
boundary above Metadata, CSharpText, and source-neutral artifact content. It
remains SRM-only, Roslyn-free, NativeAOT-compatible, and portable to
single-threaded Browser/Wasm.

## Pathological cases

### Equal XML IDs in two assemblies

Two libraries contain `M:Example.Widget.Parse(System.String)`. Only the XML
contribution bound to the selected library and API-declaration supplier may
satisfy the request. A same-named companion from the other library is
ineligible.

### Content and handoff generations disagree

A package library handoff describes one retained generation while the live
content capability exposes its replacement. The adapter or House rejects the
correspondence. It does not open a same-named XML entry from the newer content.

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
authorized an `AuthoredOnly` SourceHouse plan through the deferred provider.
DocumentationHouse records XML absence, then invokes the provider and retains
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

An in-memory package contribution exposes guarded XML content and SourceHouse
uses in-memory assembly and PDB content. Documentation settlement is identical
to desktop behavior without requiring a filesystem path.

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
2. define the source-neutral documentation-ready contribution and lifetime;
3. implement compiled-XML attempt and receipt settlement over CSharpText;
4. add the PackageHouse adapter;
5. add the direct-library adapter;
6. add the shared Queries compiled-documentation result;
7. adopt package compiled documentation in Inspect Web;
8. adopt package and direct-library compiled documentation in the CLI;
9. add the PlatformHouse adapter;
10. remove PlatformHouse's superseded documentation contracts;
11. adopt platform reference-pack compiled documentation in Inspect Web;
12. adopt platform reference-pack compiled documentation in the CLI;
13. lock the focused SourceHouse physical-declaration correspondence contract
    under #6584;
14. implement one production SourceHouse path that issues that trusted
    correspondence;
15. lock the focused CSharpText authored-documentation contract under #6583;
16. implement the owner-issued CSharpText authored-documentation operation;
17. add the SourceHouse-to-DocumentationHouse integration adapter;
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
| Generation correspondence | A handoff and live content from different generations reject before parsing. |
| Explicit authorization | No SourceHouse, source/PDB discovery or acquisition, repository, content-store, or network work occurs without authored demand and a pre-authorized deferred provider. Guarded reads of already-realized file-backed XML remain authorized by compiled demand. |
| Cheap-first ordering | Provider construction starts no source work; combined demand reaches a terminal compiled-XML attempt before the deferred provider is invoked once with the remaining ledger, and XML availability does not suppress the requested source attempt. |
| Exact XML lookup | Compiled XML uses the Metadata-issued compiler ID and associated contribution. |
| Authoritative absence | XML absence requires complete readable companion evidence for the exact subject. |
| Independent channels | Success, absence, failure, or incompleteness in one channel does not rewrite the other. |
| Authored-source boundary | Source documentation consumes SourceHouse-authored evidence plus #6584 trusted physical-declaration correspondence and never decompiled or PDB-only output. |
| Declaration correspondence | The owner-issued CSharpText gate from #6583 returns attached documentation or visible uncertainty without name-based fallback; DocumentationHouse preserves that result. |
| Field provenance | Filled, corroborated, and conflicting fields retain every contributing value and origin. |
| Visible failure | Malformed or over-budget XML/comment content never becomes an empty or plain-text success. |
| Resource lifetime | Success, failure, incompleteness, and cancellation release content and retain only valid resource-free evidence. |
| Browser portability | In-memory package and platform content requires no filesystem path. |
| Host parity | Representative CLI and Browser requests produce equivalent House demand and settlement. |
| Retirement | #6497 deletes its owned legacy XML readers; DocumentationHouse adoption deletes host-local companion selection and merge policy; final CSharpText adoption deletes `DocCommentParser`. |

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
  acquisition; a pre-authorized deferred provider may perform SourceHouse work
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
