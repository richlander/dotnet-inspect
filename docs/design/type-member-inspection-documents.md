# Type and Member inspection documents

## Status and approved scope

This document is the normative design for **Type and Member Inspection
Documents**, tracked by
[#8430](https://github.com/richlander/dotnet-inspect/issues/8430).

The design is proposed. The current product already has exact-Type resolution,
Member inventories, DocumentationHouse queries, SourceHouse-backed source
operations, and host-neutral envelopes, but CLI and Inspect Web still compose
parts of those results independently.

The user explicitly approved specifying Type and Member together because they
are analogous inspection producers. This paired scope owns their shared
document contract and their distinct natural child populations. It does not
transfer Metadata, DocumentationHouse, SourceHouse, QuerySpace, Sections, or
host semantics into this owner.

## Owner and exact claim

**Type and Member Inspection Documents** owns this exact claim:

> Given one owner-resolved exact Type or one Type-scoped Member address, produce
> one resource-free subject document that composes only requested owner-issued
> Metadata, DocumentationHouse, and SourceHouse outcomes; exposes natural child
> populations only through QuerySpace Rows and Count; and crosses the completed
> host-neutral boundary in one `InspectionEnvelope<TContent>` shared by CLI and
> Inspect Web.

This owner defines:

- the paired Type and Member inspection request and document boundaries;
- the exact-subject correspondence required to adopt lower-owner outcomes;
- producer-owned exposure of Metadata, documentation, and source views;
- the Type document's `Members` row space;
- the Member document's `Overloads` row space;
- view-local and aggregate completion;
- the rule that every exposed Rows or Count terminal flows through QuerySpace;
- the same-population requirement for paired Rows and Count; and
- the completed cross-host document handoff.

It does not define:

- Metadata facts, API extraction, declaration spelling, or member identity;
- Library realization, ownership, borrowing, or retirement;
- DocumentationHouse channel settlement or field evidence;
- SourceHouse source selection, PDB policy, or producer settlement;
- QuerySpace predicates, ordering, semantic selection, terminals, or
  continuation mechanics;
- section names, disclosure, shape lowering, or rendering;
- Share packet construction or diagnostic transport;
- CLI syntax, Browser interaction, or presentation; or
- Inspection Capability Composition registration.

Those owners supply typed inputs and outcomes. Type and Member Inspection
Documents compose them without reconstructing or strengthening their claims.

## Product question

The paired operations answer:

> What requested Metadata, documentation, source, and child-population views
> describe this exact Type or Member subject?

The subject, not the command or host, determines the document:

```text
exact Type
  -> TypeDocument
       scalar Type views
       Members row space

Type-scoped Member address
  -> MemberDocument
       logical-member or exact-member views
       Overloads row space
```

Package, Platform, project, Workspace, and direct-Library routes first resolve
their source-specific gestures to one exact realized subject. They then invoke
the same Type or Member inspection producer. Source kind does not select
another document schema or permit a host to assemble one.

## Production demonstration

`Newtonsoft.Json@13.0.3` is the initial real package witness. Its
`Newtonsoft.Json.JsonConvert` Type has compiled XML documentation, SourceLink
evidence, many Members, and overloaded methods.

The current production CLI establishes the natural populations:

```console
$ dotnet-inspect type Newtonsoft.Json.JsonConvert \
    --package Newtonsoft.Json@13.0.3 \
    -S "Member Index" --count --tips q
67

$ dotnet-inspect member Newtonsoft.Json.JsonConvert SerializeObject \
    --package Newtonsoft.Json@13.0.3 \
    -S Methods --count --tips q
8
```

The target experience keeps those populations while changing their ownership:

```text
TypeInspection(JsonConvert)
  -> TypeDocument
       Metadata view
       Documentation view
       Source view, when authorized
       Members Count = 67
       Members Rows = the same 67-Member population

MemberInspection(JsonConvert.SerializeObject)
  -> MemberDocument
       logical Member Metadata view
       Overloads Count = 8
       Overloads Rows = the same 8-overload population

MemberInspection(one exact SerializeObject overload)
  -> MemberDocument
       exact Member Metadata view
       Documentation view
       Source view, when authorized
```

The numbers are evidence about this pinned production witness, not universal
schema constants. The contract is that Count and completely drained Rows agree
for the same producer population and QuerySpace request.

## Conventional basis

The design composes established repository contracts:

| Precedent | Adopted rule |
| --- | --- |
| [Library inspection documents and populations](library-inspection-document.md) | Documents follow resolved subjects; child rows are lightweight shapes; drill-down creates the child document. |
| [Section cardinality](section-cardinality.md) | An inventory exposes Rows and Count as peer terminals over one owner-declared population. |
| [Query Operation Infrastructure](query-operation-infrastructure.md) | Subject binding, operation planning, result rows, and QuerySpace row planning remain distinct stages. |
| [Inspection operation composition](inspection-operation-composition.md) | Hosts lower gestures to typed requests, Houses settle authorized content, and one resource-free envelope crosses the completed boundary. |
| [DocumentationHouse](documentation-house.md) | Compiled and authored documentation remain independent attempts with retained provenance, conflicts, and failures. |
| [SourceHouse](source-house.md) | Authored and decompiled source remain typed producer attempts under explicit source and PDB policy. |
| [Inspection Capability Composition](inspection-capability-composition.md) | Modern document routes and real host bindings may be registered without changing document or execution semantics. |

PR
[#8411](https://github.com/richlander/dotnet-inspect/pull/8411)
is the direct production precedent. One Library Type producer supplies both
Rows and Count, preventing presentation mode or a separate shortcut from
changing cardinality. Type and Member inspection apply the same rule to Member
and overload populations.

## Owner map

| Concern | Owner | Input to this composition |
| --- | --- | --- |
| Exact Library and content authority | [Library ownership and borrowing](library-ownership-and-borrowing.md) | Exact resource-free Library/content correspondence and operation authority |
| Type and Member facts and identities | Metadata and [Type, member, and API representation](type-member-api-representation.md) | Exact Type or Member identity and requested Metadata results |
| Type and Member resolution | [Member inspection planning](member-inspection-planning-and-metadata-projection.md) and exact-Type query owners | Resolved subject, ambiguity, forwarding, and typed failure |
| Documentation settlement | [DocumentationHouse](documentation-house.md) | Exact-subject channel attempts, field settlement, provenance, conflicts, and completion |
| Source settlement | [SourceHouse](source-house.md) | Exact-subject authored/decompiled attempts, selected source, provenance, and completion |
| Row planning and terminals | [QuerySpace composition](query-space-composition.md) and [Section-row shaping](section-row-shaping.md) | Resolved row intent, Rows or Count terminal, completion, and continuation |
| Completed handoff | [Inspection envelope](inspection-envelope.md) | Content, Share, and diagnostics |
| Capability graph | [Inspection Capability Composition](inspection-capability-composition.md) | Later registration of completed routes and host bindings |
| Presentation | CLI, Inspect Web, Markout, and focused output owners | Host gesture and rendering over unchanged typed content |

This composition adds no universal subject, House, row, or rendering owner.

## Subject document family

The document family is subject-shaped:

```text
PackageDocument
LibraryDocument
TypeDocument
MemberDocument
```

[Library inspection documents and populations](library-inspection-document.md)
owns the family rule and the Library document. This design owns the Type and
Member children.

A `TypeDocument` describes one exact resolved Type. Its Member rows are child
shapes, not embedded `MemberDocument` values.

A `MemberDocument` describes one Member inspection address within one exact
Type. The address may resolve to:

- one or more logical Member groups containing exact overload rows;
- one exact Member selected from the resolved set; or
- one typed non-success.

The document retains which state was resolved. It does not pretend that a
logical Member name is one exact method, property, event, field, or accessor.
An exact-Member-only view is unavailable until resolution proves one exact
definition.

An explicit Member-surface filter that retains several logical groups remains
one `MemberDocument` inventory. Each overload row retains its logical-group
identity, so the document does not flatten independent families into one
apparent Member. A Type-surface Member filter remains a `TypeDocument`
`Members` request under the existing planning contract.

A no-match result is a typed Member-address non-success, not a successful empty
`Overloads` population. A singleton resolved set remains an inventory unless
the request contains owner-issued exact-target intent. Exact targeting
combined with several logical groups is rejected by the Member-resolution
owner before exact-only views run.

## Exact subject correspondence

### Type subject

One Type inspection retains:

- the requested Library and Type identity;
- the exact defining `LibraryReference`;
- the defining API-declaration content reference;
- owner-issued Library-Metadata correspondence;
- one exact Metadata Type definition identity;
- forwarding correspondence from the requested Library when applicable; and
- the generation or population binding required by the producing owner.

A local Type uses the same Library as its requested and defining Library. A
forwarded Type retains the originating declaration and ordered forwarding
evidence, but its Metadata, documentation, source, and Member views use only
the exact defining Library and TypeDef issued by the Type-resolution owner.
The operation obtains independent authority for that defining Library before
producer work.

When the defining Library, TypeDef, content authority, or forwarding
correspondence cannot be established, Type resolution returns typed
non-success and neither DocumentationHouse nor SourceHouse runs. A Library
forwarder row remains a lightweight row; it is not itself a `TypeDocument`.
Display name, assembly name, forwarding target text, or a same-named Type in
another participant cannot perform the join.

### Member subject

One Member inspection begins with:

- one exact Type subject;
- the original typed Member selector or logical-group request; and
- the owner-issued resolved Member set in which inventory or exact resolution
  occurs.

The resolved set retains one or more logical Member groups. Each group retains
its exact containing Type, admitted Member kind or kinds, canonical name or
selector meaning, and population binding. Each overload row retains both its
group identity and one exact Member identity.

An attached extension Member additionally retains two distinct owner-issued
relationships:

- the inspected receiver Type and the evidence by which the Member participates
  in that Type's Member population; and
- the exact declaring Library, declaring Type, and Member definition.

Member drill-down preserves both. Metadata declaration facts,
DocumentationHouse subjects, and SourceHouse targets use the declaring
identity. The receiver Type remains navigation and attachment context and
never substitutes for the declaration owner.

Documentation or source requiring one exact declaration runs only after the
Member address resolves uniquely. An overload ordinal, digest, metadata token,
or signature is consumed only through its owning selector contract; rendered
text never becomes identity.

## Request model

Hosts lower gestures into typed semantic requests. Requests do not carry
section names, verbosity, output formats, Browser component names, or renderer
settings.

Conceptually:

```text
TypeInspectionRequest
  exact Type subject
  requested Type Metadata views
  optional Documentation view request
  optional Source view request
  optional Members QuerySpace request
  aggregate work bounds

MemberInspectionRequest
  exact Type subject
  Member address
  requested Member Metadata views
  optional Documentation view request
  optional Source view request
  optional Overloads QuerySpace request
  aggregate work bounds
```

Documentation demand remains the closed demand owned by DocumentationHouse.
Source demand and PDB policy remain the closed contracts owned by SourceHouse.
The inspection request selects those owner-issued policies; it does not copy
their fields or infer authorization from host availability.

Capabilities are operation inputs, not facts. A source-capable desktop host
does not authorize source content unless the request and host policy do so.
Inspect Web may expose fewer gestures while constructing the same semantic
request.

Unrequested views remain distinguishable from requested empty or unavailable
views. A producer does not execute Metadata, documentation, source, Rows, or
Count work merely because another host commonly displays it.

## Producer-owned views

Type and Member Inspection own the public arrangement of lower-owner results.
The lower owners retain their facts and failures.

### Metadata view

The Metadata view contains only requested owner-issued declaration and API
facts for the resolved subject. The inspection producer may project those
facts into a subject document, but it cannot reinterpret metadata validity,
visibility, accessibility, representability, or binding.

Metadata needed privately to resolve the subject or establish correspondence
does not automatically make every Metadata view requested.

### Documentation view

The Documentation view consumes one exact DocumentationHouse outcome. It
preserves:

- requested channels;
- selected or settled field values;
- all relevant compiled and authored contributions;
- provenance and corroboration;
- conflicts;
- channel-local non-success;
- aggregate completion; and
- diagnostics required by the completed inspection.

The producer may expose a concise selected document for ordinary consumption,
but that projection remains paired with the House evidence that explains it.
It must not select the first contribution and discard conflict, provenance, or
failure.

CLI and Inspect Web do not invoke DocumentationHouse separately, mutate a
Metadata model with documentation, or define different selected-value rules.

### Source view

The Source view consumes one exact SourceHouse outcome. It preserves the
requested source demand, PDB policy, authored and decompiled attempts, selected
provider, provenance, diagnostics, completion, and failures.

The Type or Member producer decides where the source view appears in its
document. It does not redefine authored-first policy, PDB acquisition,
decompilation, or SourceLink interpretation.

CLI and Inspect Web do not independently choose source fallback or pair a
source result with a subject by display name.

### View outcomes

Every requested view has a closed visible disposition equivalent to:

- not requested;
- available;
- unavailable;
- rejected;
- failed; or
- incomplete.

An owner-issued result may preserve a more specific union. The inspection
producer maps it exhaustively and retains the original evidence needed by
structured consumers.

## Type `Members` row space

The Type document declares one natural `Members` row space. Its logical row
unit is one exact admitted Member declaration in the Type's canonical Member
population.

The initial membership contract follows the canonical Member Index identity
and admission rules. Presentation grouping by method, property, event, field,
constructor, operator, or nested category does not change row identity or
Count. Compiler-generated exclusion, extension attachment, inherited-member
policy, and visibility remain owner-issued population decisions rather than
renderer conventions.

An attached extension row carries its receiver attachment separately from its
exact declaring Library, declaring Type, and Member identity. Selecting that
row for Member inspection uses the declaring identity for Metadata,
DocumentationHouse, and SourceHouse while preserving the receiver Type as the
navigation context.

Conceptually:

```text
TypeMembersPopulation
  exact Type subject
  canonical membership
  stable baseline order
  population binding
  QuerySpace row vocabulary
  Rows terminal
  Count terminal
```

Count and Rows share subject, membership, predicates, order, semantic
selection, completion, and population binding. Count never reports a rendered
group count, a retained prefix, the current segment length, or zero after
failure.

Documentation and source do not define Member membership or Count. A Count
request can complete without invoking DocumentationHouse or SourceHouse.

## Member `Overloads` row space

The Member document declares one natural `Overloads` row space for its resolved
Member set. Its logical row unit is one exact Member declaration admitted by
one retained logical group.

The name `Overloads` describes the inspection role rather than only C# method
overloading. Constructors, operators, indexers, or another owner-admitted
Member kind may have several exact declarations in one logical group.
A non-overloadable Member group contains one row after successful resolution.
When an explicit Member inventory selects several groups, Rows retain the group
identity of every exact declaration and Count reports the total selected exact
declarations. For the ordinary one-name request, that total is the overload
count for that one logical Member.

Conceptually:

```text
MemberOverloadsPopulation
  exact containing Type
  one or more logical Member-group identities
  canonical membership
  stable baseline order
  population binding
  QuerySpace row vocabulary
  Rows terminal
  Count terminal
```

An exact Member selection identifies one row from this owner-issued
population. Exact detail does not create a second overload population or
change the Count observed by the corresponding inventory request.

Documentation and source for an exact overload are child views of the selected
Member subject. They do not alter overload membership or Count.

## QuerySpace boundary

Whenever Type or Member Inspection exposes Rows or Count, it does so through a
declared QuerySpace row space.

The semantic sequence is:

```text
exact subject binding
  -> producer-owned population definition
  -> operation-owned plan
  -> declared QuerySpace row space
  -> predicates and order
  -> semantic selection
  -> Rows or Count
  -> typed completion and population binding
```

Rows and Count are peer terminals. Each QuerySpace execution chooses one
terminal. An outer document request that needs both coordinates two explicit
QuerySpace executions. A Count execution and a Rows execution retain the same
base producer-population binding and the same row intent. A later Rows request
following Count proves that binding or fails visibly.

Predicates, ordering, and semantic selection are part of row intent. Changing
them creates a different selected request even when the immutable base
producer population is unchanged. The invariant is that Count and completely
drained Rows agree when subject, base population binding, and row intent are
the same.

The producer may use source delegation or an optimized Count kernel only when
it preserves QuerySpace-observable membership, order, selection, completion,
and failure semantics. Counting an eager host model or rendered output is not
an optimization.

A scalar Metadata, documentation, or source view does not become a synthetic
one-row space. If a future producer exposes a real inventory such as
documentation contributions, source documents, or source locations, that
inventory receives its own declared row identity and QuerySpace row space.

## Completion and failure

Subject resolution is the prerequisite for every view. A missing, ambiguous,
rejected, failed, or incomplete exact subject remains visible and prevents
lower work that requires that subject.

After subject resolution, views settle independently:

- Metadata success does not turn documentation or source failure into absence;
- documentation success does not conceal source failure;
- source success does not repair incomplete Metadata correspondence;
- Count failure does not become zero;
- Rows failure does not become an empty complete population; and
- one view's non-success does not discard independently valid sibling views.

The document retains aggregate completion stating whether every requested view
and terminal reached its required terminal state. A partially useful document
may be returned with explicit incomplete or failed siblings and diagnostics.
The host does not need to discard valid content, but it cannot present the
operation as completely successful.

Failures preserve owner attribution. Type and Member Inspection do not replace
a DocumentationHouse conflict, SourceHouse acquisition failure, Metadata
rejection, or QuerySpace continuation failure with one generic missing-data
message in typed content.

## Completed host-neutral handoff

Each completed operation returns one shared envelope:

```text
InspectionEnvelope<TypeInspectionContent>
InspectionEnvelope<MemberInspectionContent>
```

The exact public names may follow repository naming conventions at
implementation time. The invariant is one owner-issued Content result, one
Share outcome for the same semantic request, and complete contained
diagnostics.

CLI and Inspect Web consume the same operation and envelope. Transport DTOs may
lower the content for Browser/Wasm, but cannot omit semantic states needed to
distinguish not-requested, unavailable, failed, incomplete, conflicting, or
complete outcomes.

Share projects the subject and portable semantic request under its owner. It
does not serialize acquired source, House receipts, live Workspace state,
credentials, or row continuations.

## Sections and presentation

Sections project the Type or Member document; they do not invoke another House
or reconstruct the document.

The Type section owner maps the existing canonical Member inventory, including
`Member Index`, to the document's `Members` row space and declares its
inventory cardinality. The Member section owner maps each overload-inventory
section to the document's `Overloads` row space and declares the same
cardinality contract. Structural discovery advertises Rows and Count together
only after those mappings exist.

Markout is the default shared lowering for CLI structured output. Inspect Web
may use host-native interaction and rendering over the same typed content.
Both hosts preserve:

- subject identity;
- view disposition;
- row-space identity;
- Count/Rows population correspondence;
- House provenance and conflicts at the applicable disclosure level;
- completion; and
- diagnostics.

Ordinary presentation may remain concise. Detailed disclosure can expose
documentation channels, source provider, conflicts, and failures without
changing the underlying document.

## Capability composition

After each route has a completed production implementation, it registers with
[Inspection Capability Composition](inspection-capability-composition.md):

- Type document definition;
- Type inspection route;
- `Members` QuerySpace surface;
- Type section-to-row-space bindings;
- Member document definition;
- Member inspection route;
- `Overloads` QuerySpace surface;
- Member section-to-row-space bindings;
- and real CLI and Browser bindings.

Registration describes implemented capability. It does not make an
unimplemented House view or host binding available. Legacy command-local paths
remain adoption gaps until their producer and consumer both use the shared
route.

## Platform and safety boundary

The reusable request, content, row, and envelope contracts are resource-free,
SRM-only, NativeAOT-compatible, and suitable for single-threaded Browser/Wasm.
They contain no metadata reader, stream, opener, callback, package payload,
Workspace lease, credential, or inspected-assembly type.

The operation may temporarily borrow owner-protected content while executing
lower producers. Every borrow and transferred authority settles before the
resource-free document crosses the completed boundary.

Inspected metadata, XML documentation, authored source, and decompiled text
remain inert data. The composition never loads or executes inspected code.
Containment and trust remain governed by the producing owner; the document
does not decode contained text back into trusted identity.

These target implementation properties are **unverified** until the
implementation and Browser/Wasm gates named by #8430 exist and pass.

## Pathological cases

The implementation must preserve these cases:

- a Type resolves exactly while DocumentationHouse reports conflicting
  compiled and authored summary values;
- Metadata and Member Count succeed while authorized SourceHouse acquisition
  fails;
- a Type Member Count succeeds, then a Rows request presents an incompatible
  population binding;
- a logical Member group has eight overloads, while one exact selector chooses
  only one overload for detail;
- a Member selector is ambiguous and neither DocumentationHouse nor
  SourceHouse runs;
- a forwarded Type is visible but its exact defining subject cannot be
  established;
- compiled documentation is available for a Type while exact authored-Type
  declaration correspondence remains unavailable;
- a property, event, field, accessor, or bodyless declaration lacks exact
  authored-source correspondence;
- one House channel fails while an independent channel succeeds;
- a scalar documentation or source view is requested with Count and is
  rejected before execution rather than treated as one row;
- completely drained Rows and exact Count disagree; and
- CLI and Browser attempt different selected-value or fallback policies over
  the same House evidence.

The last two are product defects, not permitted host divergence.

## Production adoption

[#8430](https://github.com/richlander/dotnet-inspect/issues/8430)
owns the counted path:

1. Lock this paired Type/Member inspection-document specification.
2. Implement the host-neutral Type request, document, exact Member population,
   and QuerySpace route.
3. Compose Type documentation and source views through DocumentationHouse and
   SourceHouse.
4. Bind the Type document's `Members` row space and inventory cardinality to
   the existing Type sections.
5. Adopt the Type document in CLI and Inspect Web; retire covered direct host
   composition and command-local Rows or Count execution.
6. Implement the host-neutral Member request, document, overload population,
   and QuerySpace route.
7. Compose exact-Member documentation and source views through
   DocumentationHouse and SourceHouse.
8. Bind the Member document's `Overloads` row space and inventory cardinality
   to the existing overload sections.
9. Adopt the Member document in CLI and Inspect Web; retire covered direct host
   composition and command-local Rows or Count execution.
10. Register the completed routes with Inspection Capability Composition and
   remove superseded Type/Member host-local capability inventories.

Each implementation or adoption is a focused owner change. This design and
tracker connect them without approving one broad implementation PR.

## Required evidence

The implementation sequence must add Release gates proving:

- `Newtonsoft.Json.JsonConvert` exposes the same 67-Member population through
  exact Count and completely drained Rows for one binding;
- `JsonConvert.SerializeObject` exposes the same eight-overload population
  through exact Count and completely drained Rows for one binding;
- Count and completely drained Rows agree when subject, immutable producer
  binding, and row intent are identical, while a changed predicate, order, or
  semantic selection produces a distinct selected request;
- Type and Member Count do not invoke DocumentationHouse or SourceHouse;
- Type `Member Index` and Member overload structural discovery advertise Rows
  and Count together only through their registered section-to-QuerySpace
  bindings;
- CLI and Browser exposed Rows and Count execute those shared QuerySpace routes
  rather than command-local enumeration or rendered-output counting;
- requested compiled and authored documentation preserve contributions,
  provenance, conflict, channel-local failure, and aggregate completion through
  both hosts;
- requested authored or decompiled source preserves SourceHouse policy,
  attempts, provenance, completion, and failure through both hosts;
- ambiguous or unavailable exact subjects prevent House execution;
- attached extension Member drill-down preserves receiver attachment while
  using the exact declaring Library, declaring Type, and Member for Metadata,
  documentation, and source;
- forwarded-Type inspection uses independently authorized defining-Library
  correspondence or returns typed non-success before House execution;
- CLI and Browser execute the same host-neutral Type and Member routes and
  preserve Content, Share, and diagnostics;
- Browser/Wasm serialization preserves every closed outcome and contains all
  artifact-authored text; and
- superseded host-local House joins and selected-value rules are unreachable
  after their adoption slice retires them.

The production package is the behavioral witness. Focused fixtures isolate
conflict, ambiguity, unsupported declaration kinds, continuation mismatch, and
partial completion. Harnesses must invoke product-owned subject, House, and
QuerySpace construction rather than manufacture a repaired aggregate.

All properties remain **unverified** in this design-only slice.

## Non-claims

This design does not:

- create a generic `SubjectInspection<T>` public API;
- require Type and Member documents to have identical fields;
- define Package or Library document contents;
- make every Type or Member section a row space;
- expose House attempts directly as host commands;
- make documentation or source affect Member or overload cardinality;
- require source acquisition for default Type or Member inspection;
- add exact authored-Type, property, event, field, accessor, or bodyless-source
  correspondence;
- define a universal Count optimization or continuation protocol;
- make QuerySpace the owner of Type or Member semantics;
- require CLI and Browser to use the same visual presentation; or
- preserve a legacy host path solely for compatibility after the shared route
  covers it.
