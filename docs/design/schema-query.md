# Schema query

## Status

This document is the normative owner for dotnet-inspect's structural document
discovery and projection-diagnostic contract.

The Markout `DocumentSchema` model, generated-schema projection,
product-authored composition, CLI discovery, effective filtering, projection
validation, and rendered-manifest filtering are implemented. Some older
post-render projection paths still use Markout's rendered-string diagnostic;
that nonconforming path does not own section-scoped rendered truth and its
removal is tracked by
[#7138](https://github.com/richlander/dotnet-inspect/issues/7138).
Clone-candidate discovery, field validation, and summary rendering retain a
known divergence; the unsupported field-projection affordance is removed under
[#7141](https://github.com/richlander/dotnet-inspect/issues/7141). The earlier
[auto-generated schema note](auto-schema.md) records the first generated-schema
migration but is not a current owner.

The production consumer is the CLI `-D`/`--discover` surface and the
field/column projections that use the same schema. This design does not add a
parallel browser query language; browser adoption requires its own product
consumer and host contract.

## Authority and exact claim

**Schema query** owns:

> Given an owner-issued structural document schema and an explicit discovery or
> projection request, resolve names against that schema, preserve the caller's
> requested order and output shape, and diagnose structural misses separately
> from valid shapes that produced no rendered data. Generated Markout schema is
> the default structural source; product-owned composition or augmentation is
> required when one product document merges multiple generated views or
> contains dynamic structure that attributes cannot express. Schema query does
> not execute domain work, decide section applicability, or infer rendered
> content from structural declarations.

This is one product contract over an upstream owner-issued model. Markout owns
`DocumentSchema`, `SectionSchema`, `SchemaItem`, generated
`MarkoutSchemaInfo`, and `ToDocumentSchema()`. dotnet-inspect owns how its
commands compose those values, present discovery, validate projections, and
compare structural requests with actual rendering.

## Model

A structural schema describes the stable addressable vocabulary of one
document:

```text
DocumentSchema
  ordered Sections
    stable section name
    item kind
    ordered item names
```

The item kind identifies the projection vocabulary, currently `field` or
`column`. A section may have no item-level vocabulary and still be a valid
discoverable section.

The schema is descriptive. It says that a section or item can be addressed by
the document contract; it does not say that one request will render it, that
its producer is authorized, or that data exists for the current subject.

Names use the product's stable section, field, and column vocabulary. Schema
owners must not derive identity from a rendered heading after formatting or
invent item names that do not correspond to an addressable projection.
Composition preserves the owner-issued schema sequence. Final section
presentation follows Markout's default order or an explicit presentation-owner
override. Resolution uses ordinal, case-insensitive name matching.

## Structural schema construction

### Generated schema is the default

For a Markout view whose structure is fully declared by attributes, the
generated schema is the structural source:

```csharp
DocumentSchema schema = InspectionContext.Default
    .GetSchemaInfo<LibraryInspectionView>()!
    .ToDocumentSchema();
```

The same attribute walk supplies rendering metadata and structural query
metadata. Adding, removing, or renaming a statically declared section, field,
or table column therefore updates both surfaces from one declaration.

Generated schema is a default, not a requirement to force every product shape
into one view model. A generated schema describes the structure visible to that
one generated context; it does not discover neighboring views or
runtime-authored rows.

### Product composition is explicit

The command or section owner composes or augments generated schema when its
product document has structure outside one generated view:

| Product shape | Required construction |
| --- | --- |
| One statically attributed view | Use its generated schema directly. |
| One document rendered from several first-class views | Merge the owner-issued generated schemas in product order. |
| Generated structure plus product sections or columns | Augment the generated schema with the exact product vocabulary. |
| Runtime-shaped rows or a dynamic field table | Build the schema from the product owner's stable item vocabulary. Prefer one shared definition; otherwise enforce complete equivalence. |
| A narrower catalog over a larger structural document | Filter the complete product schema by the catalog's authored section identities. |

Current examples include the API document merging type, member-summary,
member-detail, operator, explicit-implementation, extension-method, event, and
code schemas; package discovery adding runtime-shaped items; and library
discovery augmenting generated sections with metadata and clone-candidate
vocabulary. These are examples of the composition forms, not a normative
call-site inventory.

The remaining known nonconforming path is:

| Surface | Current defect | Required removal |
| --- | --- | --- |
| `Clone Candidates` | `CandidateColumnNames` supplies structural discovery, while `SummaryFieldNames` accepts stale field selectors and `SummaryFields()` emits different names. | #7141 removes `--fields` from this row-oriented section, deletes the stale selectors and manual projected-summary path, and retains columns as its sole addressable item kind. |

An augmentation must have a reason the generated view cannot express. It must
reuse the renderer's owner-issued names and order rather than creating a
parallel approximation. A new dynamic shape with independently maintained
render and schema vocabularies requires a complete equivalence gate. Manual
composition is not an interim defect when the product document itself is
composed or dynamic.

## Three levels of truth

Structural, effective, and rendered evidence answer different questions:

| Level | Question | Owner |
| --- | --- | --- |
| Structural schema | What can this document address? | Markout-generated schema plus product-owned composition |
| Effective schema | Which structural sections are applicable and have evidence for this request? | Section and operation planning owners |
| Rendered manifest | Which fields and columns did this exact render emit? | Markout formatter events captured by the product host |

### Structural discovery

Given an owner-issued schema, structural discovery is resource-free. It lists
sections, or lists the addressable items of a resolved section, without
acquiring a package, opening an assembly, invoking a scanner, fetching source,
or probing producer-backed effectiveness merely to prove that a declared shape
exists.

Command owners decide whether a particular CLI request uses structural
discovery, a cheap target-aware catalog, or full effective discovery. That
binding is intentionally not uniform schema-query policy. Where a command
offers `--schema`, it explicitly requests the complete structural view.

### Effective filtering

`-D --effective` is a separate request. The relevant section or operation owner
may perform its bounded applicability or evidence work, then filter the
structural schema to the effective section and item set.

Schema query does not define that work, promote structural declarations to
evidence, or treat a missing effective row as proof that the structural schema
was invalid. Effective filtering consumes owner-issued outcomes.

### Rendered manifest

A valid structural field or column may still produce no row in one render.
Post-render diagnosis uses formatter events, not text search over the final
Markdown, table, or JSON artifact.

`RenderedSectionManifest` records section-scoped fields and table columns from
the actual Markout render. A heading event establishes section scope only at
the configured section level; the heading text is not recorded as a field.
Cell values, nested headings, and unrelated sections do not become evidence
that a requested item rendered.

The manifest is render evidence, not a replacement schema. It cannot advertise
an item that was not structurally addressable, and one empty render must not
erase that item from future structural discovery.

Some retained projection paths call Markout's
`DocumentSchema.DiagnoseRendered` over a rendered string. They preserve the
structural-miss versus valid-but-empty distinction, but they are not authority
for section-scoped field or column identity. New section-scoped work must use a
render manifest or typed projected identities. #7138 deletes the string path;
it is not an alternate or fallback contract.

## Discovery behavior

The CLI presents schema query results through `DiscoverOutput`.

| Request | Result |
| --- | --- |
| `-D` | Ordered section rows |
| `-D "Section"` | Ordered item rows for the resolved section |
| `-D` at eligible detailed presentation | A section/item tree |
| `-D --count` | The count of discovered rows, not the inspected subject document |
| `-D` with row selection | The selected discovery rows in their stable order |
| `-D` with a payload projection | The requested projection over discovery rows |

Discovery preserves explicit output intent. An explicit table, TSV, JSONL,
JSON, or plaintext request is not replaced by an automatic tree. `--no-header`
applies to formats with headers, and `--out` routes the complete discovery
artifact to its destination instead of also writing it to standard output.
Only eligible implicit table presentation or Markdown may promote to a tree.

Section patterns and category doors are resolved against the complete
owner-issued section vocabulary. Categories, costs, and visibility remain
section-catalog metadata; they do not become `DocumentSchema` item kinds.
Bare catalog presentation groups category doors before regular sections and
opt-in sections, with alphabetical order inside each group.

## Projection behavior

Projection has two checks.

### Structural validation

Before rendering, fields and columns resolve against the selected structural
schema:

- a name valid in any selected section is valid for the multi-section request;
- a mixed request warns for unresolved names but may continue when at least one
  requested name resolves;
- a request with no resolved names fails before rendering;
- diagnostics identify the requested kind and section and direct the user to
  `-D "Section"` for the available vocabulary; and
- pattern resolution preserves the user's requested order.

This validation catches structural mistakes such as misspellings. It does not
claim the resolved item has data.

### Render diagnosis

After rendering or typed projection, the product compares the resolved request
with rendered evidence or the projected item set. A structurally valid item
that produced no data is reported as a no-data note, not reclassified as an
unknown field or column. Section-scoped conclusions require the render manifest
or typed identities. The rendered-string path is a current violation scheduled
for removal under #7138, not a supported diagnostic alternative.

Pattern diagnosis uses the concrete names selected by the pattern. One rendered
concrete name satisfies that pattern; a cell value that merely contains the
same text does not.

## Ownership boundaries

| Concern | Owner and boundary |
| --- | --- |
| Structural schema types and generated projection | Markout; dotnet-inspect consumes the public owner-issued model. |
| Product schema composition | The command or section owner whose document merges views or adds dynamic structure. |
| Section order | Markout's default order plus explicit presentation-owner overrides; schema composition retains owner-issued sequence but does not own final presentation order. |
| Categories, verbosity, explicit-only policy, costs, applicability, and execution | [Progressive disclosure](progressive-disclosure.md), section-pipeline, and operation owners; schema query consumes their section identities and effective outcomes. |
| Discovery request binding and presentation | The CLI host; `DiscoveryOutputRequest` preserves the chosen format, tree eligibility, projection, row selection, and destination. |
| Rendering and format lowering | Markout and [output shapes](output-shapes.md). Schema query supplies structural vocabulary, not serialized output. |
| Row predicates and row ordering | [Row query and order](row-query-order.md) and row-selection owners; discovery may consume their selected row window without redefining their semantics. |
| Domain acquisition and analysis | Package, Workspace, Metadata, Source, Analysis, and other operation owners; structural discovery does not invoke them. |

The Markout dependency is existing shared product substrate. This reconciliation
does not change Markout, introduce a host-specific renderer, add a broad
rendering domain, or alter Browser/Wasm behavior.

## Pathological cases

### One product document spans several views

A type/member document is not required to collapse all first-class views into
one generated model. The product owner merges those generated schemas and
tests that sections absent from an individual view appear in the complete
document schema.

### Dynamic structure has no static attribute source

Runtime field tables, alternate row shapes, or product-defined columns use the
product owner's stable vocabulary. One shared typed definition is preferred.
An independently maintained duplicate is a defect, not a compatibility
contract: remove it or replace it with one owner-issued definition. Do not add
aliases, fallback inference, or forwarding members to preserve the duplicate
shape.

### A field exists in only one selected section

Multi-section projection validates across the selected set. A graph field can
remain valid when a companion table lacks it; only a name missing from every
selected section is a complete miss.

### Structural shape exists but renders empty

The request is structurally valid. The rendered manifest reports no data
without deleting the shape, claiming success-shaped content, or treating the
empty render as a misspelling.

### Display text resembles structural identity

A title, cell value, nested heading, or field in another section does not
satisfy a section-scoped request. Only section-scoped formatter events or typed
projected items count as section-scoped rendered evidence.

### Explicit format competes with tree promotion

Explicit output intent wins. Automatic tree presentation is permitted only for
the eligible implicit formats named above.

## Required gates

The current Release CLI suite owns the executable contract:

| Gate | Required observation |
| --- | --- |
| `OutputFormatterTests.TypeViewSchema_DoesNotOwnFirstClassMemberRows` and `TypeDocumentSchema_MergesFirstClassMemberViews` | One generated view is not mistaken for the complete composed product document. |
| `OutputFormatterTests.RenderManifestFormatter_CapturesStructuredSectionsColumnsAndFields` and `RenderManifestFormatter_DoesNotTreatTitleTextAsAField` | Render evidence is section-scoped and comes from formatter events rather than coincidental display text. |
| `ProjectionDiagnosticsTests.ValidateProjection_FieldResolvingInOneSection_SucceedsWithoutWarning` | Multi-section validation accepts a name owned by any selected section. |
| `ProjectionDiagnosticsTests.ValidateProjection_FieldResolvingInNoSection_FailsWithError` and `ValidateProjection_MixedValidAndUnknown_WarnsOnUnknownButSucceeds` | Complete structural misses fail; partial requests preserve valid work and report only unresolved names. |
| `ProjectionDiagnosticsTests.DiagnoseRendered_WildcardUsesResolvedNames` and `DiagnoseRendered_OverlappingPatternsUseResolvedNames` | Post-render pattern diagnosis follows resolved structural names. |
| `CommandExecutionTests.Project_Discover_ExplicitTableDoesNotPromoteToTree`, `Project_Discover_TsvNoHeaderOmitsHeader`, and `Project_Discover_JsonOutWritesOnlyToFile` | Discovery preserves explicit format, header, and destination intent. |
| `CommandExecutionTests.Member_DiscoverEffective_ListsCategoriesBeforeSections` | Bare effective discovery presents category doors before regular sections. |
| `PackageQueryCliTests.DataDiscovery_UsesPackageQuerySchemaWithoutAcquisition` and `LibraryIntegrationQueryTests.StructuralDiscoveryDoesNotRequireScannerOptInOrAcquireTarget` | Structural discovery uses owner-issued schema without triggering domain acquisition or scanner execution. |
| `InspectionResultTests.PackageInfo_OwnerVocabularyDrivesDiscoverySchema` | The complete package-info discovery vocabulary is derived from the same typed descriptor catalog that drives rendering, with stable order and no duplicate names. |
| `CloneCandidatesSectionTests.Type_JsonProjectionSupportsFieldsColumnsAndRows` and `Library_JsonProjectionSupportsSummaryFields` | These gates record the field-projection affordance that #7141 removes rather than preserves. |

New schema composition forms require a focused gate that proves their generated,
merged, augmented, or dynamic vocabulary matches the product document. The
known clone-candidate field affordance is a required removal; no safety,
completeness, or compatibility claim rests on its sampled gates. A
documentation-only change to this owner requires Markdown validation and
verification that every named gate still exists.

## Non-claims and required removals

This design does not:

- make structural schema an execution plan or evidence of nonempty data;
- require every document schema to be generated;
- move section policy, row query, acquisition, analysis, or rendering into
  `DocumentSchema`;
- define a Browser/Wasm discovery experience;
- guarantee item-level discovery for a dynamic shape whose owner exposes only
  a section boundary; or
- authorize parsing rendered output to recover structural identity.

Generated schema, product composition, structural discovery, effective
filtering, and rendered-manifest effective discovery are current behavior.
Replacing the remaining rendered-string projection diagnostics with manifest
or typed identity evidence is required by
[#7138](https://github.com/richlander/dotnet-inspect/issues/7138); no stronger
section-scoped claim rests on the string path. Removing clone-candidate field
projection and its stale vocabulary is required by
[#7141](https://github.com/richlander/dotnet-inspect/issues/7141). Any new
cross-host query language, generated accessor model, schema serialization
contract, or additional pattern semantics requires a focused issue and owner;
the retired proposal checklist is not standing authorization.
