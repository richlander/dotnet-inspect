# Structured C# Type document

## Status and owner

This proposed design establishes the **Structured C# Type Document** owner for
[#8083](https://github.com/richlander/dotnet-inspect/issues/8083).

Its normative claim is:

> For one exact metadata Type, preserve complete accounting of its direct
> metadata artifacts, a complete owner-issued C# declaration population, exact
> Type, Member, and physical body identity, and validated C# declaration render
> plans in one immutable document. A shared projector turns that document into
> complete source text and exact structural ranges without any host parsing or
> C# reconstruction.

The implementation home is `ILInspector.Decompiler`, the
`CSharp.Decompiler` layer that already owns assembling CSharp Type shells with
decompiled bodies under
[Member body substrate](member-body-substrate.md). The owner defines the
document, its validation, its revision currency, and projection semantics. It
does not acquire assemblies, redefine Metadata facts or CSharp spelling, join
analysis, or render a host experience.

SourceHouse, Queries/Sections, CLI, and Browser adoptions follow as
independently reviewable slices.

## Purpose

Current whole-Type source is a scalar string. It answers "show the Type" but
does not retain which exact declaration, accessor, or physical body produced
each part of the final text. The one replaceable-body range in
`CSharpSourceArtifact` supports a selected compilation scenario, not a Type
viewer with many independently selectable declarations.

That missing structure creates two bad options for an interactive consumer:

- parse rendered C# and guess which text belongs to which metadata member; or
- add another Browser-specific Type renderer and let it drift from CLI source.

Neither is acceptable. Display text is not identity, and the Browser must not
become a C# parser. A second rendering stack would also violate the existing
division in which Metadata owns facts, CSharp owns declaration spelling, and
CSharp.Decompiler fills CSharp-owned body slots.

The structured document is the smallest shared substrate that lets the product
retain those boundaries while supporting:

- complete Bodies and Skeleton projections;
- a Selected body projection without selecting the first overload or accessor;
- static/instance, accessibility, documentation, attribute, and generated-code
  filters;
- exact declaration selection and member drill-down;
- later analysis results joined by exact identity and document revision; and
- unchanged CLI whole-Type text produced from the same document.

## Relationship to API Declarations

[Type API Declaration Inspection](type-api-declarations.md) is an adjacent
landed product owner, not an earlier version of this document. It owns an
API-review view with API-visible and all-declarations scopes, declaration-only
text, containing shells, and a selected nested declaration subtree. It is
already shared by CLI and Browser.

This document instead owns one exact implementation TypeDef and the bodies
that belong to it. Its Skeleton mode is the body-free projection of that same
implementation population, not an alias for the API Declarations source
choice. In particular:

- API Declarations may omit compiler-generated implementation artifacts by
  contract; this document retains positively identified generated C#
  declarations so Type Explorer can hide or reveal them and separately
  accounts for generated metadata artifacts that valid C# absorbs into a Type
  frame, logical declaration, or implementation.
- API Declarations includes a selected nested declaration subtree; this
  document keeps each nested TypeDef as an independent exact implementation
  subject with its own body budget and route.
- API Declarations returns one completed declaration string; this document
  retains exact declaration/body identity and render plans for repeated
  structural projection.

Both paths consume `ILInspector.CSharp` spelling and printing rather than
maintaining parallel C# formatters. Neither operation substitutes for or
changes the other's selection semantics, CLI section, Browser source choice,
or evidence.

## Motivating production assets

The primary package is
`System.Text.Json@11.0.0-preview.7.26381.103`, assembly
`lib/netstandard2.0/System.Text.Json.dll`.

`System.Collections.Generic.OrderedDictionary<TKey,TValue>.Enumerator`
motivates exact nested-Type identity and a useful body/skeleton view over one
selected Type. `System.Text.Json.JsonSerializerOptions` supplies the larger
case: fields, properties with accessors, constructors, static and instance
members, accessibility variation, attributes, generated declarations, and
enough bodies to expose an implementation that preserves only flat text or
positional member order.

Compiler-produced fixtures remain necessary for empty and bodyless Types,
delegates, overloaded indexers, explicit interface implementations, generated
members, field initializers, multiple accessor bodies, malformed metadata, and
bounded body-production failure.

## Ownership and boundaries

The Structured C# Type Document owner defines:

- the immutable complete-Type document shape;
- complete direct metadata-artifact accounting and primary representation
  associations;
- document-local declaration identity and ordering;
- exact Metadata-owned Type, Member, and physical body references carried by
  the document;
- declaration render plans with owner-issued full and skeletal alternatives;
- documentation, attribute, signature, and implementation regions;
- structural classifications needed to select declarations;
- a revision that binds later results to one exact document;
- validation of text, identities, ranges, render plans, and body associations;
- the pure host-neutral projection operation; and
- typed rejection of invalid projection requests.

It does not own:

- assembly, package, Library, Workspace, or source acquisition;
- Metadata Type, Member, accessor, or contract-relationship semantics;
- C# declaration spelling or body-raising semantics;
- authored-source parsing, partial-Type aggregation, or authored/decompiled
  correspondence;
- body fidelity grades, diagnostic causes, or analysis facts;
- asynchronous operation identity, cancellation, caching, or stale-result
  suppression;
- CLI sections, Markout rendering, Browser routing, HTML, syntax highlighting,
  folding, focus, or accessibility; or
- Annotated Source document construction or interaction.

The document consumes owner-issued facts. It never derives accessibility,
staticness, generated status, contract relationships, or identity from
rendered source.

## Selected Type extent

One document describes one exact `TypeDef` in one physical module. The selected
Type may itself be nested; its exact nested metadata name and enclosing generic
context remain part of its Metadata-owned identity.

Completeness has two correlated parts:

- A **complete physical artifact inventory** accounts exactly once for every
  `FieldDef`, `MethodDef`, `PropertyDef`, and `EventDef` directly owned by the
  selected Type.
- A **complete C# declaration population** contains every logical member that
  contributes an independent owner-issued C# declaration fragment.

The physical inventory includes non-public and compiler-generated artifacts.
Each artifact has an exact identity and one typed primary representation
association:

- an independent C# declaration;
- an accessor or other physical body of a logical declaration;
- Type-frame syntax or a Type fact represented by that frame; or
- implementation evidence already represented by an exact declaration or
  physical body.

This separation reflects valid C#. An auto-property backing field belongs to
the property implementation, an event backing field belongs to the event, an
enum's `value__` slot supplies the enum's underlying-Type fact, and delegate
methods are represented by the delegate Type declaration. None is fabricated
as an independent C# member merely to expose its metadata row.

Property and event accessors remain under their owning logical declaration
rather than becoming duplicate method declarations. Other MethodDefs remain
independent declarations unless the producing owners issue an exact
representation association proving that valid C# already represents them in a
Type frame, declaration, or implementation. A rendering failure is not such an
association: an unsupported independent logical declaration makes the document
non-available rather than disappearing behind an absorbed-artifact label.

Projection filters only the C# declaration population. They never remove
physical artifacts from the document, its validation, or its revision.

A nested `TypeDef` is another exact implementation subject, not a recursively
embedded document. This differs deliberately from API Declarations' selected
nested subtree: body production, body limits, revision, and Type Explorer
routing stay scoped to one exact TypeDef. A future nested-Type inventory may
expose exact navigation destinations, but it is not part of this document's
completeness claim.

Empty classes, bodyless interfaces, enums, and delegates are valid documents.
The absence of implementation bodies is not absence of the selected Type.

## Source provenance

The first supported source kind is **product-generated decompiled C#**. It is
constructed from one exact acquired assembly image and may consume a matching
Portable PDB for names and body recovery. The document identifies that
provenance and preserves the Decompiler owner's fidelity and diagnostics.

Ordinary authored Type Source is not an input to this contract. One authored
document may contain several Types or only one declaration of a partial Type,
so a successful Type Source selection does not prove the complete inventory
required here. Authored support requires a separate owner that can prove:

- complete logical-Type coverage across all relevant documents;
- exact correspondence for every declaration and body;
- one ordering and projection policy across partial declarations; and
- revision semantics equivalent to this document's physical-source boundary.

Until then, a host presents this document as product-generated C# and does not
silently substitute authored `BrowserSource` text.

## Document identity

### Type identity

The document carries both:

- the exact `MetadataTypeDefinitionName` used to resolve the selected Type; and
- its `MetadataTypeDefinitionAddress`, the physical MVID-plus-`TypeDef`
  address.

The name is the logical lookup identity. The address proves which physical
definition supplied the document. Neither is reconstructed from the rendered
Type declaration.

### Physical artifact and declaration identity

Every physical artifact records its complete Metadata-issued `MemberAnchor`,
validated metadata token, artifact kind, and typed primary representation
association.
Consumers compare the full canonical identity, not only the anchor's truncated
display fingerprint. The same-reader producer proves that the anchor, token,
selected Type, and association refer to the same physical row.

Every declaration receives a contiguous document-local integer ID for compact
joins inside one document. That ID is not portable identity and is never
stored in a route.

Every member declaration carries the complete `MemberAnchor` and token of its
primary logical declaration. One declaration may have several associated
physical artifacts, but each artifact has exactly one primary representation
association and each projected declaration retains one exact logical identity.

Property and event declarations remain one logical member even when they own
several physical accessors. They are never duplicated into independent logical
members merely because bodies are MethodDefs.

### Physical body identity

The document contains one physical body inventory. Every `MethodDef` artifact
identifies exactly one physical body row, including an explicit no-body row for
abstract, runtime, or otherwise bodyless methods. A row contains:

- the exact `MetadataMethodAddress`;
- the exact owning MethodDef artifact ID;
- its owner-issued role, such as method, getter, setter, init, adder, or
  remover;
- whether a managed body exists;
- the body-production outcome and fidelity when production was attempted; and
- a SHA-256 fingerprint over the exact method signature and complete raw method
  body, including headers and method-data sections, or the explicit no-body
  representation.

The fingerprint follows the existing Annotated Source physical-body precedent
and closes the MVID-collision boundary.

A declaration has zero or more **owned-body references** to rows whose body
syntax is presented by that declaration. Owned references may supply exact
drill-down destinations. A logical property or event may own several accessor
body rows; selection and drill-down never choose one by declaration order.

A declaration implementation slot may separately have zero or more
**body-contribution references**. Each reference names an exact physical body
row, contribution role, and relative range in the slot's full alternative whose
emitted text was reconstructed from that body. Contribution references are
many-to-many: one constructor body may contribute initializer text to several
field declarations, and one declaration may cite several contributing bodies.
They do not transfer body ownership, make the field a MethodDef, or create an
Annotated Source destination.

Field initializers and other declaration text reconstructed from body evidence
use contribution references. The constructor body remains owned by its
constructor declaration and can still supply its own presentation and
drill-down behavior.

### Document revision

The document carries a 64-character SHA-256 revision issued after validation.
The digest covers the canonical compact serialization of the document excluding
the revision field itself, including:

- exact Type and Member identities;
- physical artifact order and primary representation associations;
- declaration order and structural classifications;
- every render-plan fragment, alternative, and region;
- physical body addresses, fingerprints, outcomes, and fidelity;
- owned-body and body-contribution references and ranges;
- source provenance and symbol contribution; and
- the rendering-policy identity that affected the emitted C#.

The revision identifies one exact document value. It is not a cross-build
semantic correspondence claim. Async results must carry this revision plus
their exact Type or Member identity; equality of an MVID, token, display name,
or short member fingerprint alone is insufficient.

## Declaration model

The document contains one complete physical artifact inventory, one Type frame,
and one ordered C# declaration population. The frame owns the compilation-unit
prefix, namespace and Type declaration, opening and closing syntax, and
Type-level documentation and attributes. Each declaration owns its complete
source contribution between those frame parts.

A physical artifact records:

- complete `MemberAnchor`, validated token, table kind, and canonical physical
  order;
- its owner-issued generated-origin classification;
- its primary representation kind and exact target: Type frame, declaration
  ID, or physical body row; and
- a semantic contribution role such as enum storage, delegate signature,
  getter, setter, backing storage, or lowered implementation helper.

An artifact associated with a Type frame, declaration, or body has no
independent source fragment or projection range. The association says where
valid C# already represents its contribution; it does not erase the artifact's
identity or make it subject to declaration visibility filters. Secondary
body-contribution references do not change that one primary association.

Canonical physical order is ascending metadata token value and exists only for
deterministic validation and serialization. It is not C# source order.

A declaration records:

- document-local ID and source order;
- complete `MemberAnchor` and validated declaration token;
- declaration kind;
- declared accessibility;
- static, instance, or unclassified placement;
- positively generated, positively non-generated, or unknown origin;
- whether any implementation slot's full alternative differs from its
  skeleton alternative;
- owned-body references bound to implementation slots and any exact drill-down
  destinations;
- body-contribution references bound to implementation slots and their
  full-alternative ranges;
- optional owner-issued contract relationships; and
- one owner-issued C# declaration render plan with stable declaration-local
  fragment and slot IDs.

The render plan is an ordered sequence of fixed syntax fragments,
independently selectable documentation or attribute fragments, and
implementation slots. Each slot contains owner-issued full and skeleton
alternatives, including the indentation and separators needed for composition.
A method-body slot may choose a complete block or its body-free form; a field
initializer slot may choose the initializer or an empty alternative. CSharp
issues every fragment and alternative in declaration context so the supported
projection combinations remain valid C#.

An implementation slot is the smallest independently selectable contribution
that preserves valid C#. A slot that combines sources requiring different
Selected-body activation is invalid and must be split by the producing owner.

A host does not create a skeleton by deleting characters between braces, attach
a decompiled string to a signature, or splice a contribution into another
declaration.

Named regions use C#-appropriate roles:

- documentation;
- attributes;
- signature;
- implementation.

Regions describe source structure, not semantic facts. Their ranges are
zero-based UTF-16 offsets, end-exclusive within their fragment or slot
alternative. A body-contribution range exists only in the full alternative
that contains its text. A region may contain narrower body ranges, but sibling
regions do not partially overlap.

Documentation and attributes remain separate regions even when either is
absent. Absence, unavailable acquisition, and an available empty value are
distinct capability states; a consumer does not turn unavailable
documentation into an empty successful region.

## Projection

`CSharpTypeDocumentProjector` is a pure host-neutral operation over a validated
document and one projection request. It returns a
`CSharpTypeDocumentProjection` containing:

- the document revision;
- one complete well-formed UTF-16 C# text buffer;
- the visible declaration rows in unchanged source order;
- absolute UTF-16 declaration, region, owned-body, and body-contribution ranges
  into that buffer;
- the exact identity and structural classifications for each visible row; and
- typed projection diagnostics.

The projector, not a host, owns source composition. The CLI and Browser may
choose different projection requests and render the returned value
differently, but neither reimplements C# selection or range adjustment.

### Body projection

The body mode is one of:

- **Bodies** - use every implementation slot's full alternative when
  available;
- **Skeleton** - use every implementation slot's skeleton alternative; or
- **Selected body** - begin from Skeleton, use every implementation slot's
  full alternative in the selected declaration, then use a full contribution
  slot in another declaration when it references a physical body owned by the
  selected declaration.

The final rule is the selected declaration's **owned-body contribution
closure**. It preserves initializer or other lowered text that valid C# places
outside the declaration that owns the source body, without expanding unrelated
implementation slots.

Selected body is accepted only for an exact member whose resulting projection
has an observable implementation difference. For a property or event, every
local accessor implementation slot is full and its owned-body closure includes
all available accessor bodies represented by that logical declaration. For a
field with an initializer, its local initializer slot is full without
expanding the contributing constructor declaration. The projector never
selects the first accessor, overload, or contributing body.

A declaration with no body uses the same valid source in Bodies and Skeleton.
A body-production failure uses its valid skeleton source plus a typed body
failure and projection diagnostic; it does not emit an empty body or pretend
that decompilation succeeded. Hosts render that failure adjacent to the
declaration without rewriting the C# fragment.

### Structural selection

The same projection request may select:

- all, static, or instance declarations;
- any set of owner-issued accessibility classes;
- inclusion of positively generated C# declarations;
- inclusion of available documentation regions;
- inclusion of available attribute regions; and
- an exact contract relationship when that capability is present.

Selection removes only complete owner-issued declaration or region
contributions and preserves syntactically valid framing. It never reorders
declarations. A declaration with unknown static/instance or generated
classification remains visible in the unfiltered view and is not guessed into
a narrower category. Physical artifacts associated with a Type frame,
declaration, or body are not projection rows; generated-declaration filtering
does not fabricate or reveal standalone syntax for them.

Structural selection determines the visible declaration rows before body-plan
projection. A selected member must remain visible. When an explicit structural
filter removes another declaration that carries a selected owned-body
contribution, the projection omits that declaration as requested and reports
the exact hidden contribution rather than implying that the filtered view is a
complete presentation of the selected implementation.

An invalid member, unsupported contract selector, or impossible combination is
a typed rejected projection. It does not fall back to Bodies, All members, or a
display-name match.

### Projection identity

Document-local declaration IDs remain stable across every projection of one
document. Absolute text ranges belong only to the projection that issued them;
they are not reused after any projection control changes.

Routes and asynchronous analysis carry the document revision and complete
Member identity, not a projection range. A host may restore a projection
request, then ask the projector for fresh ranges in the resulting text.

## Contract relationship capability

The document does not derive override, interface implementation, or hiding
destinations. `ApiMember.IsOverride`, explicit-interface spelling, and matching
names are not enough to identify an exact related declaration.

The first document version therefore preserves ordinary structural
classifications but reports exact contract provenance as unavailable. A later
Metadata-owned relationship result may be adopted as an optional document
capability. That adoption must carry exact source and destination identities,
relationship kind, and completion; only then may the projector accept exact
contract filtering.

This boundary lets the initial document and Type Explorer ship without
manufacturing relationship targets or embedding inheritance analysis in
CSharp.

## Construction and validation

Construction is atomic over both populations. An available document must prove
that every direct physical artifact was inventoried exactly once, every
independently representable logical member has one declaration row, and every
artifact has one valid primary representation association. A filtered
`ApiType.Members` collection is not sufficient input.

The constructor validates at least:

- non-empty and internally consistent Type identity;
- unique physical artifact identities in canonical physical order;
- artifact tokens from the expected metadata tables and selected Type;
- exactly one valid primary representation association per physical artifact;
- association targets and roles consistent with the Type frame, declaration,
  or physical body they name;
- exactly one physical body row, including explicit no-body state, for every
  MethodDef artifact;
- unique body-row IDs and addresses owned by their exact MethodDef artifacts;
- contiguous declaration IDs and strictly increasing source order;
- unique complete Member identities, without trusting the short fingerprint
  as a unique key;
- a primary logical artifact for every declaration;
- owned-body references that identify the declaration's MethodDef or one of its
  accessors;
- body-contribution references that identify a same-document body and valid
  full-alternative range without granting ownership or drill-down;
- valid 64-character physical-body fingerprints;
- initialized render plans, alternatives, and region collections;
- contiguous declaration-local fragment and implementation-slot IDs;
- well-formed UTF-16 text;
- checked range arithmetic, bounds, ordering, and containment;
- valid render-plan ordering and full/skeleton alternatives for every
  implementation slot;
- valid C# for Bodies, Skeleton, and every Selected-body closure required by
  the declaration population;
- explicit capability state for documentation and contract relationships; and
- a revision equal to the canonical validated payload.

The projector validates requests and its own output. Every emitted range must
slice the intended text, declaration ranges must follow emitted source order,
and body ranges must remain inside their owning declaration and implementation
region.

Document construction and projection remain bounded by the repository's
existing metadata, Type-name, member-anchor, text, declaration-count, and body
work limits. A limit is a typed incomplete or unavailable result, never a
truncated document presented as complete.

## Outcome and failure semantics

The completed content uses an owner-specific outcome:

- **Available** carries a structurally complete document whose requested body
  work completed;
- **Incomplete** carries a structurally complete document plus exact
  declaration/body failures or exhausted body-work bounds;
- **Unavailable** states why no valid document can be constructed for the
  admitted input; and
- **Rejected** identifies an invalid or unsupported subject or request.

Unexpected exceptions and cancellation remain operation failures rather than
semantic outcomes. Cancellation publishes no replacement document.

Incomplete is useful content, not success-shaped fallback. Its physical
artifact inventory, C# declaration population, and skeleton projection are
complete, while every missing body is identified explicitly. If artifact or
body enumeration, primary association, contribution provenance, declaration
identity, or rendering cannot establish completeness, no document is
published.

## Layered production

The owner accepts typed inputs from existing layers:

```text
Metadata facts and identities
  -> CSharp declaration render plans and alternatives
  -> CSharp.Decompiler body alternatives and physical provenance
  -> Structured C# Type Document validation and projection
  -> shared inspection envelope
  -> CLI and Browser presentation
```

Metadata continues to own the facts and identities. CSharp continues to own
declaration spelling and body slots. CSharp.Decompiler continues to own body
production and complete-Type assembly. The document owner validates and
projects the resulting composition; it does not duplicate those producers.

Analysis remains outside the base document. Research or another owning
analysis facade joins later facts to the document revision and exact Member
identities. Analysis arrival cannot mutate the document or its source order.

## Production adoption

The end-to-end tracker remains
[#8083](https://github.com/richlander/dotnet-inspect/issues/8083). Adoption is
split by owner:

1. **Structured document owner** - add the document, validator, serializer,
   revision, and projector; refactor whole-Type composition through
   CSharp-owned declaration render plans; and retain exact physical artifact
   associations, logical declarations, document-owned body rows, and
   many-to-many body contribution provenance.
2. **SourceHouse and Queries/Sections** - preserve the document and native
   outcome through exact-Type decompiled settlement, then expose one completed
   `InspectionEnvelope<CSharpTypeDocumentOutcome>`.
3. **CLI** - consume the shared envelope and Bodies projection for existing
   whole-Type Decompiled Source, preserving current text and diagnostics before
   retiring the scalar attempt path.
4. **Browser** - consume the same envelope and projector for the static Type
   Explorer, then replace Type Source's Settings destination.
5. **Metadata contract relationships** - separately define and adopt exact
   override/interface/hiding provenance before enabling those controls.
6. **Analysis insights** - add one exact revision-and-member-correlated insight
   at a time under its own owner.

The CLI adoption is not an interactive Type Explorer. It proves that the
shared document remains the source of ordinary whole-Type text rather than a
Browser-only parallel model. The Browser uses its approved host-specific HTML
renderer over the same typed projection; it does not use Markout for the live
interactive surface.

## Required evidence

Planned Release gates:

| Gate | Claim |
| --- | --- |
| `CSharpTypeDocumentTests` | Constructor rejects broken Type/Member/body identity, missing or duplicate physical artifacts or body rows, invalid primary representation or body-contribution references, duplicate or non-contiguous declaration rows, malformed UTF-16, invalid fingerprints, inconsistent render-plan alternatives, and overflowing or out-of-bounds ranges. |
| `CSharpTypeDocumentProjectionTests` | Bodies, Skeleton, and Selected body use owner-issued render-plan alternatives; the selected declaration's owned-body contribution closure remains visible; structural filters preserve order, identities, valid C#, and projection-local absolute declaration, body, and contribution ranges without parsing source. |
| `CSharpTypeDocumentRevisionTests` | Canonical replay is stable; changing identity, physical-artifact association, classification, source, render policy, body address, physical fingerprint, ownership, or contribution provenance changes the revision; short-anchor collisions cannot merge artifacts or declarations. |
| `CSharpDecompilerTypeDocumentTests` | Complete same-reader physical artifact, body, and C# declaration populations; non-public and generated members; absorbed backing/enum/delegate artifacts; properties/events with multiple accessors; constructor-to-field initializer contributions; bodyless and empty Types; visible body failures; and one-load exact body association. |
| `TypeDocumentInspectionTests` | Exact-Type SourceHouse settlement preserves provenance, typed outcomes, bounds, diagnostics, detached serialization, and `InspectionEnvelope` content across supplied and absent PDB paths. |
| CLI whole-Type Decompiled Source tests | The existing command text, diagnostics, and failure behavior come from the shared Bodies projection for the real System.Text.Json witnesses and focused fixtures. |
| Browser Type Explorer production test | Type Source Explore opens the routed viewer; Bodies/Skeleton/Selected body and structural filters consume product projections and exact identities without Browser C# parsing. |

The Type document tests use independently compiled fixtures under the owning
fixture directory. The real `System.Text.Json` Types remain production-path
canaries. Documentation-only design changes require Markdown validation; these
gates become binding as their implementation slices land.

## Pathological cases

The implementation must demonstrate:

- the nested generic `OrderedDictionary<TKey,TValue>.Enumerator` resolves to
  one exact `TypeDef` and preserves its enclosing generic identity;
- `JsonSerializerOptions` remains usable with a large declaration population
  and all filters preserve source order;
- overloaded indexers retain distinct complete Member identities;
- a property with getter and setter keeps one logical declaration and two
  exact physical body rows;
- an explicit interface implementation is not matched by display name;
- generated auto-property and field-like-event backing fields remain exact
  physical artifacts associated with their logical declaration and never
  become fabricated standalone C# rows;
- an enum's `value__` slot remains associated with the Type frame while enum
  values remain independently projectable declarations;
- delegate runtime methods remain exact physical artifacts associated with the
  delegate Type frame rather than making a valid delegate unavailable;
- one constructor body remains owned by its constructor declaration while
  contributing initializer ranges to multiple field declarations, none of
  which gains a fabricated MethodDef destination;
- selecting that constructor expands its own body and those exact field
  initializer slots without expanding unrelated declaration implementation;
- an empty class, bodyless interface, enum, and delegate produce valid
  documents;
- a body budget exhaustion retains a complete skeleton and identifies every
  unavailable body without publishing a complete Bodies outcome;
- a malicious range whose `Start + Length` overflows is rejected before any
  slice; and
- two values with the same MVID and metadata tokens but different physical
  method bytes receive different document revisions.

## Analogous design evidence

Language-server semantic tokens and document symbols separate semantic ranges
from rendered text while binding every range to one document version. IDE
outline and CodeLens experiences similarly retain stable declaration identity
while text coordinates change after a new projection. This design adopts that
versioned structured-document convention.

It deliberately diverges by carrying product-issued declaration render plans
with full and skeleton alternatives instead of asking a host editor to parse
and rewrite C#. dotnet-inspect must remain Roslyn-free in product paths,
Inspect Web runs in Browser/Wasm, and the source is reconstructed from metadata
plus decompiled bodies rather than edited authored text. The extra render-plan
structure and projector are the cost of keeping identity and C# construction in
their owning layers.

## Non-claims

This design does not claim:

- authored-source completeness or authored/decompiled equivalence;
- recursive nested-Type expansion;
- whole-Type mixed IL/C# or Finding annotation;
- exact contract provenance before Metadata supplies it;
- analysis counts, ranking, runtime heat, or insight presentation;
- semantic correspondence between different document revisions;
- a new universal source-document, syntax-tree, or rendering abstraction;
- a replacement for member Annotated Source; or
- a change to current CLI or Browser behavior before their adoption slices
  land.
