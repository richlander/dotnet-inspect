# Ecosystem dependency recognition

## Status

Proposed focused owner for
[#7818](https://github.com/richlander/dotnet-inspect/issues/7818).

The owner defines one product-relative interpretation over already-issued
direct dependency observations. Package and Library extraction, CLI sections,
and Browser presentation remain separate adoption efforts.

## Approved composition scope

The operator approved a broad-design exception for this document to specify the
required composition between the recognition result and three existing CLI
owners: Package inspection, Library inspection, and dependency inspection. The
approved scope is limited to:

- complete route-owned envelope transport of recognition evidence;
- compact Package and Library Info rollups;
- pair-grain recognition evidence under existing section and column
  disclosure; and
- a dependency-inspection query and projection over recognized direct
  dependencies.

The exception does not transfer command grammar, section-schema construction,
row-query resolution, rendering, or dependency traversal into the recognition
owner. Those owners adopt the recognition result in separate implementation
slices.

## Owner and exact claim

The **Ecosystem Dependency Recognition** owner in
`DotnetInspector.Ecosystems` defines:

- one immutable product-authored recognition profile over shipped
  `EcosystemPackId` values;
- distinct Package ID and assembly simple-name association domains;
- exact and family association semantics in each domain;
- classification of one bounded direct-dependency observation batch;
- one owner-issued Package or Library semantic subject for every batch;
- typed availability for every required Package or Library input component;
- many-to-many recognition evidence, including every matching association;
- one resource-free classification value reusable as a named part of an
  owner-issued inspection Document;
- explicit complete, incomplete, and unavailable outcomes;
- deterministic product, observation, and recognition ordering;
- recognized, unrecognized, candidate, and association counts;
- one authoritative multi-part recognition Document; and
- the completed `InspectionEnvelope<EcosystemDependencyRecognitionOutcome>`
  handoff to production hosts.

Its exact claim is:

> Given one validated resource-free product recognition profile and one
> ordered owner-issued direct-dependency observation population, recognize
> every shipped ecosystem whose authored association matches each observation
> and preserve observation, declaring source, matching basis, overlap, and
> non-match in one reusable classification. For one Package or Library
> subject-bound batch, also preserve the semantic subject, applicable target
> selection, and input completion in one detached product-relative Document,
> without traversing dependencies or inferring Package-to-assembly provenance.

The answer is deliberately product-relative. An unrecognized dependency means
that no association in the supplied product profile matched it. It does not
mean that the dependency belongs to no external ecosystem.

## User value

Package inspection exposes a compact product-relative ecosystem rollup backed
by pair-grain Package-declaration and selected compile-Library reference
evidence. Library inspection remains the next adoption step:

```text
Package Info
...
Ecosystem Dependencies | .NET Runtime, Microsoft.Extensions
```

```text
Ecosystem Dependencies

Ecosystem           Kind      Dependency                                Declared By
.NET Runtime        Assembly  System.Net.Http                           Microsoft.Extensions.Http
Microsoft.Extensions Package  Microsoft.Extensions.DependencyInjection  package manifest
Microsoft.Extensions Assembly Microsoft.Extensions.Options              Microsoft.Extensions.Http
```

Dependency inspection can select the same pair-grain evidence:

```console
dotnet-inspect depends --package Microsoft.Extensions.Http@10.0.0 \
  -S "Ecosystem Dependencies" \
  --where "Ecosystem=ecosystem.microsoft-extensions"
```

An unprojected Package or Library envelope retains the complete recognition
Document. Dependency inspection composes its complete owner-issued Content and
the same classification evidence into one application-owned Document.

These are consumer mockups, not section schemas owned by this design. Package
Info, Library Info, dependency-section registration, exact query spelling,
column labels, and host rendering remain with their existing owners.

## Supporting owners

This owner consumes, but does not redefine:

- [Static Ecosystem Packs](ecosystem-packs.md) for `EcosystemPackId`, pack
  descriptor metadata, and product order;
- [Package input and dependency evidence](package-dependency-evidence.md) and
  `PackageDependencyGroupsQuery` for validated direct Package declarations and
  effective-target selection;
- [Assembly inspection query](assembly-inspection-query.md) for direct assembly
  reference identities and metadata failure semantics;
- [Inspection envelope](inspection-envelope.md) for completed cross-host
  content, Share, and diagnostic handoff;
- [Multi-part inspection documents](multi-part-inspection-documents.md) for one
  authoritative content value with independently useful summary, recognition,
  non-match, coverage, and failure parts;
- [Dependency inspection command](dependency-inspection-command.md) for the
  direct-declaration row set, traversal boundary, and sectioned dependency
  Document;
- [Row query and ordering](row-query-order.md) for typed ecosystem-key
  predicate resolution over recognition rows;
- [Progressive disclosure](progressive-disclosure.md), [Output
  Shapes](output-shapes.md), and [Style Guide](style-guide.md) for later CLI
  section adoption; and
- the managed inspect-web facade and Browser presentation owners for later
  Browser/Wasm adoption.

Product curation remains outside `DotnetInspector.Queries`. Reusable Package,
Metadata, Services, Queries, and browser Core projects do not reference the
application catalog.

## Boundary

Classification starts after dependency facts have been selected and decoded:

```text
Package or Library owner
  -> owner-issued semantic subject and typed input context
  -> ordered direct-dependency observation batch
  -> completion and owner-issued diagnostics

Dependency inspection owner
  -> ordered owner-issued direct observations
  -> root and phase completion

Product ecosystem catalog
  -> immutable recognition profile

Ecosystem Dependency Classifier
  -> reusable classification part

Package or Library composition
  -> complete, incomplete, or unavailable recognition outcome
  -> InspectionEnvelope

Dependency inspection composition
  -> DependencyInspectionContent
  + classification part and coverage
  -> DependencyEcosystemRecognitionDocument
  -> InspectionEnvelope<DependencyEcosystemRecognitionDocument>

Host
  -> CLI or managed inspect-web facade
```

The owner performs no network, filesystem, PackageHouse, PlatformHouse,
Workspace, metadata decoding, dependency traversal, package resolution, or
assembly loading. Its cost is bounded by the supplied observations and profile
associations.

The application composition consumes lower
`DotnetInspector.Sections` Content through the existing application-to-lower
dependency direction. It does not require changing the lower Content contract.

The operation is stateless and single-shot. It has no concurrency, scheduling,
or resource-lifetime transition that benefits from a TLA+ model.

## Recognition profile

The profile is a separately authored application-catalog manifest keyed by
`EcosystemPackId`. It is not another field on an ecosystem-pack registration
and does not change the Ecosystem Pack owner's contribution shape.

Each pack profile entry copies the pack's owner-issued identity, title, and
order and supplies zero or more associations in two independent domains:

```text
Package association
  = ExactPackageId
  | PackageIdFamily

Assembly association
  = ExactAssemblyName
  | AssemblyNameFamily
```

Package associations match only Package dependency observations. Assembly
associations match only assembly-reference observations. A Package association
is never applied to an assembly name, and an assembly association is never
applied to a Package ID.

The profile deliberately does not reinterpret:

- namespace roots as assembly associations;
- Package-prefix discovery populations as dependency associations;
- core Packages as membership;
- rendered labels as identity; or
- one domain's name as provenance for the other domain.

Similar authored strings may therefore appear in the Ecosystem Pack discovery
catalog and the recognition profile. That duplication is intentional: package
discovery, namespace retrieval, Package-set membership, and dependency
recognition are separate product claims and may evolve independently.

### Exact associations

An exact association matches one complete identity under the comparison rules
owned by that identity domain. It does not match descendants that merely begin
with the same text.

### Family associations

A family association matches either:

- the exact authored stem; or
- the stem followed by `.` and at least one additional segment.

For example, the `Microsoft.Extensions.AI` family matches both
`Microsoft.Extensions.AI` and
`Microsoft.Extensions.AI.Abstractions`. It does not match
`Microsoft.Extensions.AIBogus`.

Package ID comparison follows NuGet Package identity rules. Assembly-name
comparison follows Metadata's assembly simple-name identity rules. The
recognition owner does not replace either with culture-sensitive comparison.

### Validation

Profile construction rejects:

- a null or empty profile;
- an unknown `EcosystemPackId`;
- duplicate pack entries;
- missing or nonpositive product order;
- empty exact names or family stems;
- family stems with empty dot-separated segments;
- duplicate associations within one pack and identity domain under that
  domain's comparison rules; and
- associations whose declared domain and value kind disagree.

The same association may appear under multiple packs. Cross-pack overlap is a
supported product statement, not a validation error.

Profile construction snapshots its inputs. Later mutation of a source
collection cannot change an existing profile or recognition Document.

### Initial product profile

The first product profile authors the following associations independently in
each identity domain. Unqualified entries are family associations; `exact:`
entries are exact associations.

| Ecosystem | Package ID associations | Assembly-name associations |
| --- | --- | --- |
| .NET Runtime | `System` | `System`, `Microsoft.Win32`, `Microsoft.CSharp`, `Microsoft.VisualBasic`, exact: `mscorlib`, exact: `netstandard` |
| Microsoft.Extensions | `Microsoft.Extensions` | `Microsoft.Extensions` |
| ASP.NET Core | `Microsoft.AspNetCore` | `Microsoft.AspNetCore` |
| Aspire | `Aspire` | `Aspire` |
| AI | `Microsoft.Extensions.AI`, `Microsoft.Extensions.VectorData`, `Microsoft.Agents.AI`, `ModelContextProtocol` | `Microsoft.Extensions.AI`, `Microsoft.Extensions.VectorData`, `Microsoft.Agents.AI`, `ModelContextProtocol` |
| Azure | `Azure`, `Microsoft.Azure`, `Microsoft.Extensions.Azure`, `Aspire.Azure`, `Aspire.Hosting.Azure` | `Azure`, `Microsoft.Azure`, `Microsoft.Extensions.Azure`, `Aspire.Azure`, `Aspire.Hosting.Azure` |
| Blazor | `Microsoft.AspNetCore.Components`, `Microsoft.Authentication.WebAssembly` | `Microsoft.AspNetCore.Components`, `Microsoft.Authentication.WebAssembly` |
| .NET MAUI | `Microsoft.Maui`, `CommunityToolkit.Maui`, `Microsoft.AspNetCore.Components.WebView.Maui` | `Microsoft.Maui`, `CommunityToolkit.Maui`, `Microsoft.AspNetCore.Components.WebView.Maui` |

The repeated values across columns are two authored associations, not one
cross-domain rule. Overlap across rows is intentional. For example,
`Aspire.Hosting.Azure.SignalR` recognizes Aspire and Azure, while
`Microsoft.AspNetCore.Components.WebView.Maui` recognizes ASP.NET Core, Blazor,
and .NET MAUI.

## Subject and input context

Every observation batch carries one resource-free semantic subject. The
subject is part of Content and remains present when the observation population
is empty, incomplete, or unavailable. It contains only identity available
before dependency projection:

```text
Package subject
  - exact realized Package coordinate

Library subject
  - exact Library source coordinate
  - portable Library identity
```

The Package subject's `RealizedMemberCoordinate.Package` identifies the exact
acquisition even when no root manifest exists or manifest validation fails.
The Library subject consumes Source Selection's
`ExactLibrarySourceCoordinate` and the selected assembly's
`PortableLibraryIdentity`. The source coordinate preserves the Package,
Platform, Project, or Local domain and exact Metadata assembly identity without
reusing a Workspace realization coordinate or inferring Package provenance from
the assembly name.

Each batch separately carries a typed input context. Required input components
retain owner-issued values only when those values exist:

```text
Package input context
  - manifest projection:
      Available(PackageManifestFacts)
      | Unavailable(input-issue reference)
  - dependency-group selection:
      Available(PackageDependencyGroups, including SelectedGroup)
      | Unavailable(input-issue reference)
      | NotAttempted(input-issue reference)
  - compile-asset selection:
      Available(PackageCompileAssetSelectionReceipt,
                selected asset/portable Library correspondences)
      | Unavailable(input-issue reference)
      | NotAttempted(input-issue reference)

Library input context
  - direct-reference projection:
      Available
      | Unavailable(input-issue reference)
```

An input-issue reference is an outcome-local join to a contained input issue.
`NotAttempted` references the prior issue that prevented the component from
being produced. A context never fabricates an unavailable owner-issued value.
For example, `PackageDependencyGroupsResult.NoManifest` and
`PackageDependencyGroupsResult.Failed` retain the exact Package subject and
adapt their failure into an input issue; they do not manufacture
`PackageManifestFacts` or dependency-group selection evidence.

The available Package component values consume existing owner-issued values:

- `PackageManifestFacts` identifies the manifest whose declarations were
  projected;
- `PackageDependencyGroups` retains the complete owner-issued selection,
  including its exact logical `SelectedGroup`; and
- `PackageCompileAssetSelectionReceipt` plus explicit selected
  asset/`PortableLibraryIdentity` correspondences retain both the selected
  compile slice and the metadata identity of every Library realized from it.

The context does not rerun either selection. A complete Package batch requires
all three components to be available and corresponding, including one
effective target-framework request for the combined observation population.
Target-framework correspondence uses the lower owner's normalized NuGet
semantics rather than raw manifest/folder spelling, and a universal `any`
dependency group corresponds to the concrete compile request that selected it.
`NoDependencyGroups`, `NoCompileAssets`, and `EmptyCompileGroup` are successful
empty selections. `NoMatchingTargetFramework` and
`InvalidImplementationAssets` are failed selections and cannot appear as an
available component. An unavailable or not-attempted component, incomplete
selected-asset/Library correspondence, or a mismatch among available
components cannot produce a complete Document.

The dependency query may issue either one selected manifest group or a
compatible-policy logical group that coalesces several implicit manifest runs.
Recognition accepts both owner-issued forms and does not independently
coalesce or reselect them. Universal group target spelling may be `any` or
empty; the original spelling remains evidence while both forms correspond to
the concrete effective target request.

The semantic subject is not a display header. Two complete empty Documents for
different subjects remain distinct and attributable. Hosts must not use
`InspectionShare`, a rendered command, or a display label to repair missing
Content identity.

The Package and Library shapes are intentionally distinct:

```text
Package context
  - Package subject
  - Package input context

Library context
  - Library subject
  - Library input context
```

## Direct-dependency observations

One observation identifies one declaration occurrence, not one distinct
dependency name. Two selected Libraries that reference the same assembly
produce two observations because they have different declaring sources.

The closed observation kinds are:

```text
Package declaration
  - observation identity
  - complete normalized Package dependency identity
  - declaring source:
      Package inspection:
        DeclaredPackageDependency
        declaring Package coordinate or manifest subject
      Dependency inspection:
        DependencyRootOccurrenceIdentity
        owner-issued declaration identity
  - source order

Assembly reference
  - observation identity
  - AssemblyReferenceIdentity
  - declaring source:
      Library inspection:
        declaring Library identity
      Dependency inspection:
        DependencyRootOccurrenceIdentity
        owner-issued direct-relationship identity
  - source order
```

The observation identity is unique within one batch. It preserves occurrence
identity when dependency names repeat.

Package version ranges and assembly version, culture, and public-key-token
facts remain evidence even though recognition matches only Package ID or
assembly simple name. Matching must not discard or normalize those owner-issued
facts.

The caller supplies direct observations only. The recognition owner does not
walk Package dependencies, resolve assembly references, acquire candidate
Packages, or decide whether an observation is direct.

For Package inspection, a declaration observation retains the exact
`DeclaredPackageDependency` occurrence from
`PackageDependencyGroups.SelectedGroup`; structural equality with a
declaration from another group is insufficient. An assembly-reference
observation joins through the explicit selected-asset/Library correspondence,
not through `PackageCompileAsset.AssemblyName`: that field is the asset file
name and may include `.dll`, while `PortableLibraryIdentity.Name` is the
metadata simple name.

The classifier consumes one validated profile and one ordered direct-observation
population and returns `EcosystemDependencyClassification`. It does not decide
whether that population is complete. Package and Library recognition combine
the classification with their batch completion and issues. Dependency
inspection combines it with its root and phase completion in
an application-owned composition Document.

The inspected Package or Library is retained as the semantic subject but is not
classified from its own name. It may appear in a recognition or unrecognized
observation population only if an owner-issued dependency observation names
it.

## Observation batch and completion

The completed Package and Library operation accepts one input batch with one of
three states:

```text
Available
  - semantic subject
  - complete typed input context
  - all required direct observations

Incomplete
  - semantic subject
  - typed input context
  - every trustworthy observation obtained so far
  - one or more owner-issued input issues

Unavailable
  - semantic subject
  - typed input context
  - no trustworthy observation set
  - one or more owner-issued input issues
```

An input issue identifies the recognition input role that could not be
completed, its known declaring source when available, and an
`InspectionDiagnostic` supplied or adapted by the consuming owner. The roles
are limited to the recognition boundary:

- effective target-framework selection;
- Package manifest and declaration projection;
- selected compile-Library enumeration; and
- assembly-reference projection.

These roles locate missing recognition input. They do not replace the Package,
asset-selection, or Metadata owner's detailed failure result.

Ordinary absence is not a failure. A complete selected dependency group with
zero dependencies and a selected Library with zero assembly references each
produce an available empty observation contribution.

For Package adoption, unavailable effective-target selection, malformed
manifest data, incomplete selected compile-Library enumeration, or any failed
required assembly-reference projection prevents an `Available` batch. For
Library adoption, failed direct-reference projection prevents an `Available`
batch. Those rules are adoption obligations; the recognition operation
preserves the batch state it receives.

## Recognition Document and outcome

The outcome mirrors the observation batch state:

```text
EcosystemDependencyRecognitionOutcome
  = Complete(Document)
  | Incomplete(Document)
  | Unavailable(SemanticSubject, InputContext, InputIssues)
```

`Incomplete` is not a successful complete answer. It may retain classifications
for trustworthy observations so diagnostics can explain what was learned
before the gap. Its Document carries the input issues required to interpret
that partial evidence. `Unavailable` contains no recognition Document.
It still validates that every unavailable or not-attempted input component
refers to one of its contained input issues.

One `EcosystemDependencyClassification` contains:

- a summary with the ordered product ecosystem candidate count, Package and
  assembly association counts, total, recognized, and unrecognized observation
  counts, distinct recognized ecosystem count, and
  observation-to-ecosystem recognition count;
- distinct recognized ecosystem descriptors in product order;
- recognized observation entries;
- unrecognized observations in source order.

One `EcosystemDependencyRecognitionDocument` contains:

- the exact Package or Library semantic subject and typed input context;
- one `EcosystemDependencyClassification`;
- complete or incomplete coverage; and
- input issues when coverage is incomplete.

The classification is a named resource-free semantic part, not a second host
result or envelope. It lets an application composition retain the exact
profile-relative observations, pairs, non-matches, descriptors, and counts
beside another owner-issued Content value without copying the matching
algorithm or reconstructing recognition from display text.

The recognition Document's parts remain correlated. A compact rollup, detailed
recognition rows, non-match disclosure, and coverage/failure disclosure are
section projections over that Document, not independently constructed host
models.

The classification validates that every recognition refers to one contained
observation and one contained ecosystem descriptor, no observation appears in
both the recognized and unrecognized populations, and every summary count
equals its source populations. The Document additionally validates that the
classification's observations belong to its semantic subject and available
input components and that every unavailable or not-attempted component refers
to a contained input issue. A complete Document has no unavailable or
not-attempted required component. Observation and input-issue identities are
document-local joins, not portable subject identities.

One recognized observation entry retains:

- the complete source observation;
- one recognized ecosystem descriptor; and
- every matching association basis from that ecosystem's profile entry in
  authored association order.

The pair of observation identity and ecosystem identity is unique in a
Document. If two associations from the same ecosystem match one observation,
the Document contains one recognition entry with both bases. If two ecosystems
match one observation, the Document contains two entries.

### Ordering

Document populations use:

1. ecosystem product order;
2. observation source order; and
3. authored association order.

Unrecognized observations retain source order. Hosts must not recover
ecosystem order from titles or sort dependency identities to manufacture a
different semantic order.

### Empty and unrecognized Documents

An available empty batch produces a complete, subject-attributable Document
with zero observations and zero recognized ecosystems.

A nonempty batch in which no association matches produces a complete Document
whose unrecognized count equals its observation count. It does not produce an
unavailable or failed outcome.

Consumers may omit a compact Info field when the distinct recognized ecosystem
list is empty. That omission must not be described as proof that the subject
has no external ecosystem dependencies.

## Required consumer projections

Package and Library consumers project the same owner-issued recognition
outcome. Dependency inspection uses one
`DependencyEcosystemRecognitionDocument` that contains its exact owner-issued
`DependencyInspectionContent`, the same recognition-owner-issued
classification part, and recognition coverage. No host reruns matching,
infers ecosystem identity from dependency text, or rebuilds pair evidence from
the compact rollup.

### Complete envelope

Unprojected Package or Library envelope output contains the complete recognition
outcome. An available or incomplete Document retains every recognized
observation/ecosystem pair, every unrecognized observation, semantic subject,
typed input context, summary count, coverage value, and input issue.

Dependency inspection first settles its ordinary
`InspectionEnvelope<DependencyInspectionContent>`. The application composition
preserves that exact Content, Share, and diagnostics while constructing:

```text
DependencyEcosystemRecognitionDocument
  - DependencyInspectionContent
  - EcosystemDependencyClassification
  - ecosystem-recognition coverage
```

The completed ecosystem-aware route returns
`InspectionEnvelope<DependencyEcosystemRecognitionDocument>`. It does not
mutate the lower Dependency Content, replace it with several recognition
envelopes, or attach recognition as a second envelope payload.

The application route retains the registered `asset-dependencies`
`result_kind` and advances its complete wire contract to `schema_version` `2`.
Version 2 binds `content` to
`DependencyEcosystemRecognitionDocument`. Its enriched Debug form binds
`evidence` to the existing `DependencyInspectionEvidenceDocument`; every
evidence association continues to join to the exact
`DependencyInspectionContent` nested in the application Document. The
baseline and enriched forms use the same version-2 framing pair.

Version 1 remains the existing Debug-only contract whose Content is
`DependencyInspectionContent`. Adoption step 5 transitions the producing
route, paired `--envelope`, `--evidence-envelope`, and current version-1
consumers together. It must not serialize the application Document under
version 1, serialize lower Content under version 2, or emit different baseline
Content in the paired envelope and evidence attachment. Output Shapes does not
promise preservation of the obsolete serializer; the adoption must disclose
the machine-schema transition and update its registered contract.

After dependency-inspection adoption, the asset dependency route's baseline
semantic plan requests all applicable direct Package declarations and direct
assembly references needed by classification, independently from output
format. It does not authorize transitive traversal; hierarchy selection and
depth continue to own that work.

Section and column projection shape presentation. Row predicates select the
ordinary pair view through the dependency owner's typed row-query path. None
is silently applied after completion to narrow or reconstruct envelope Content.
A CLI route whose ordinary shaping or row-selection options cannot coexist
with complete envelope transport rejects the combination rather than
serializing a success-shaped partial Document.

Unprojected `--format json` serializes the complete
`DependencyEcosystemRecognitionDocument`. `--envelope.content` serializes the
same value under the same owner-issued serializer; envelope output adds only
Share and diagnostics. The ordinary JSON document does not embed transport
framing, but its step-5 schema transition is the same public Content migration
identified by `asset-dependencies` version 2. Projected JSON remains a named
presentation rather than complete envelope Content.

### Package and Library Info rollup

Package Info and Library Info project the ordered distinct recognized ecosystem
population as one compact list, analogous to the Package target-framework
list. The rollup preserves product order and emits each ecosystem once even
when several observations or matching bases recognize it.

A complete result with no recognized ecosystem may omit the row. An incomplete
result must not render an unqualified list that looks complete; the consuming
owner either discloses partial coverage with the rollup or omits the rollup and
surfaces the recognition failure.

### Pair-grain evidence

The detailed recognition population has one row per
observation/ecosystem pair. Its compact columns identify:

- the recognized ecosystem;
- whether the observation is a Package declaration or assembly reference;
- the complete dependency identity; and
- the declaring source.

The owning CLI section exposes matching bases, version/range evidence, source
occurrence, selected target/group context, and other retained fields through
its section schema and explicit column projection. It does not add a
`--details` flag: existing verbosity, section selection, discovery, and column
projection remain the disclosure axes.

An observation recognized by two ecosystems produces two pair rows. Several
matching associations from one ecosystem still produce one pair row whose
evidence retains every matching basis.

### Dependency inspection selection

Dependency inspection forms recognition observations only from owner-issued
direct evidence:

- normalized Package declarations retain their
  `DependencyRootOccurrenceIdentity` and owner-issued declaration identity; and
- a direct assembly relationship retains the exact root occurrence and
  owner-issued assembly-reference identity.

It does not classify transitive hierarchy nodes, resolved neighbors, or an edge
merely because traversal discovered it. A relationship is eligible only when
the dependency owner identifies it as a direct declaration/reference of the
explicit root occurrence.

One multi-root composition Document retains one ordered classification part
over all eligible direct observations. Observation identity includes the exact
root occurrence, so repeated equal dependency names from different roots
remain distinct. Its recognition coverage corresponds to the retained
Dependency Content's root and phase completion; a failed or unavailable root
cannot become an empty successful recognition population.

Construction validates that every observation joins to one retained direct
declaration/reference, every trustworthy eligible direct observation appears
in either the recognized or unrecognized population, and no traversal-only
relationship enters classification.

Its recognition-row vocabulary exposes a typed ecosystem key bound to
`EcosystemPackId`. Selecting one ecosystem retains each matching pair in
Document order and includes ecosystem identity in the projected row. Predicate
evaluation reads the typed recognition entry; it does not parse a rendered
ecosystem title or rematch the dependency name.

The existing Package Query `depends-ecosystem` predicate remains a distinct
catalog package-set/package-prefix question. Dependency inspection recognition
must not silently substitute either predicate's meaning for the other.

## Completed host handoff

The completed application operation returns:

```text
InspectionEnvelope<EcosystemDependencyRecognitionOutcome>
```

This completed operation is the Package and Library handoff. The reusable
classification value is not a standalone host response. The dependency
application composition instead returns:

```text
InspectionEnvelope<DependencyEcosystemRecognitionDocument>
```

The composition retains the exact lower Content as a Document part and
propagates the lower envelope's Share and diagnostics into the new envelope.
Classification and recognition coverage are the other parts of the one
authoritative Content value.

The Package or Library composition supplies a subject-bound
`EcosystemDependencyRecognitionShare` containing the `InspectionShare` outcome
for the exact semantic subject plan. Recognition requires its typed subject to
equal the batch subject before constructing the envelope. It does not derive
Share correspondence from a Package ID, assembly name, rendered command,
packet text, or dependency evidence.

Input issues remain in the owner-issued content outcome. Supplemental
cross-host diagnostics may also appear in the envelope, but diagnostics alone
must not turn an incomplete or unavailable content outcome into a complete
one.

Both completed Documents are detached and resource-free before they cross to a
host.

## Real-package evidence

The motivating assets are:

- [`Microsoft.Extensions.Http@10.0.0`](https://www.nuget.org/packages/Microsoft.Extensions.Http/10.0.0)
  directly declares `Microsoft.Extensions.*` Packages for `net10.0`; its
  selected compile Library also references `System.*` and
  `Microsoft.Extensions.*` assemblies. It motivates a compact
  `.NET Runtime, Microsoft.Extensions` answer assembled from both observation
  domains.
- [`Microsoft.Extensions.AI@10.10.0`](https://www.nuget.org/packages/Microsoft.Extensions.AI/10.10.0)
  directly declares `Microsoft.Extensions.AI.Abstractions`,
  `Microsoft.Extensions.*`, and `System.*` dependencies. It proves that
  `Microsoft.Extensions.AI.Abstractions` legitimately recognizes both AI and
  Microsoft.Extensions rather than requiring a preferred winner.
- [`Aspire.Hosting.Azure.SignalR@13.5.4`](https://www.nuget.org/packages/Aspire.Hosting.Azure.SignalR/13.5.4)
  directly declares Aspire, Azure, Microsoft.Extensions, AI
  (`ModelContextProtocol`), Runtime (`System.*`), and unrecognized third-party
  neighbors such as `Google.Protobuf` and `YamlDotNet`. It exercises several
  recognized ecosystems beside retained non-matches in one selected
  target-framework group.

The implementation plan preserves these observations through normal package
acquisition. Small deterministic fixtures will separately lock matching,
overlap, and incomplete-input boundaries.

## Analogous behavior

[`dotnet package list`](https://learn.microsoft.com/dotnet/core/tools/dotnet-package-list)
shows requested and resolved Package references, groups them by target
framework, and optionally distinguishes transitive dependencies.
[NuGet Package Explorer](https://github.com/NuGetPackageExplorer/NuGetPackageExplorer)
inspects package manifests and contents. These tools establish the conventional
baseline: preserve exact dependency identities, target selection, and
direct/transitive distinctions.

Their documented surfaces do not supply product ecosystem recognition. This
design deliberately diverges by adding an authored interpretation layer, while
retaining the exact dependency observation and recognition basis so the added
classification remains inspectable rather than replacing dependency evidence.

## Rendering strategy

The recognition Document is structured data, not formatted text.

- CLI adoption uses the normal Markout path for an ecosystem rollup and detail
  rows. Package and Library project the compact distinct-ecosystem population;
  dependency inspection projects and filters pair-grain direct-dependency
  evidence through its existing section and row-query paths.
- The managed inspect-web facade consumes the same Package/Library recognition
  envelope and projects a portable payload.
- TypeScript owns Browser gestures and DOM presentation but does not implement
  Package or assembly association matching.

Any host-specific Browser presentation is an explicit UI lowering over the
same typed recognition Document, not a second classifier.

## Production adoption plan

[#7818](https://github.com/richlander/dotnet-inspect/issues/7818) is the
end-to-end tracker. The current plan has eight steps:

1. **Complete:** Lock this focused recognition contract.
2. **Complete:** Implement the product profile, recognition operation, envelope, and
   reusable classification part with contract tests in
   `DotnetInspector.Ecosystems`.
3. **Complete:** Adopt Package direct-dependency observations and CLI Package
   Info/detail presentation from the exact PackageHouse compile realization.
4. Adopt Library direct-reference observations and CLI Library Info/detail
   presentation.
5. Adopt baseline pair-grain recognition, typed ecosystem selection, and
   complete JSON/envelope composition in CLI dependency inspection. Atomically
   register `asset-dependencies` version 2, transition the existing Debug
   evidence form and its current consumers, and disclose the ordinary
   unprojected-JSON schema migration.
6. Expose the Package/Library recognition envelope through the managed
   inspect-web facade.
7. Adopt the result in the Browser Package surface.
8. Adopt the result in the Browser Library surface.

Each adoption remains a focused owner change. This design does not define
Package target-framework selection, selected compile-Library construction,
Library query orchestration, section registration, or Browser interaction.

## Contract gates

The implementation must name Release gates for:

- profile validation and snapshot immutability;
- equal profile and observation inputs producing equal classification parts
  across Package, Library, and dependency-inspection adopters;
- exact versus dot-segment family matching in both identity domains;
- case behavior inherited from each identity domain;
- one observation matching zero, one, and several ecosystems;
- several associations from one ecosystem collapsing to one recognition entry
  while retaining every basis;
- repeated dependency names from different declaring sources remaining
  distinct observations;
- complete-empty Package and Library Documents remaining distinct and
  attributable through Content alone;
- no-manifest and malformed-manifest outcomes retaining exact Package
  attribution without fabricated manifest or selection facts;
- unavailable and not-attempted input components referring to contained input
  issues through valid document-local joins;
- Package requested/selected target and selected-group identity surviving
  Document and envelope transport;
- mismatched dependency-group and compile-slice context refusing a complete
  outcome;
- Document-local joins, disjoint recognized/unrecognized populations, and
  summary-count validation;
- product, source, and association ordering;
- recognized, unrecognized, candidate, and association counts;
- complete-empty, complete-unrecognized, incomplete, and unavailable outcomes;
- envelope content, Share, and diagnostic preservation;
- complete envelope transport retaining all recognition pairs, non-matches,
  context, coverage, counts, and failures independently from presentation
  shaping;
- Package and Library Info rollups deduplicating ecosystems in product order
  while refusing success-shaped incomplete disclosure;
- pair-grain rows preserving overlap without duplicating several bases from the
  same ecosystem;
- dependency inspection preserving multi-root occurrence and declaration or
  assembly-reference joins inside the application composition Document;
- dependency composition preserving the exact lower
  `DependencyInspectionContent`, Share, and diagnostics without a generic
  auxiliary payload;
- dependency baseline Content requesting complete direct evidence independently
  from output format without authorizing transitive traversal;
- `asset-dependencies` version 2 binding baseline and enriched Content to the
  application Document while version 1 remains bound only to lower
  `DependencyInspectionContent`;
- paired envelope and evidence-attachment output using equal version-2
  baseline Content, with evidence joining to the nested exact lower Content;
- version-1 producers and consumers refusing version-2 Content rather than
  silently reusing an incompatible registration;
- unprojected dependency JSON equaling `--envelope.content`, with the envelope
  adding only Share and diagnostics;
- dependency inspection filtering pair rows through typed
  `EcosystemPackId` identity without classifying traversal-only nodes;
- dependency recognition remaining distinct from Package Query's
  `depends-ecosystem` package-set/package-prefix predicate;
- the `Microsoft.Extensions.AI.Abstractions` overlap case;
- the `Microsoft.Extensions.AIBogus` family-boundary near miss;
- unrecognized neighbors beside recognized observations; and
- reachability from the managed Browser/Wasm consumer without TypeScript
  matching logic.

The real-package tests prove product motivation and end-to-end composition.
Focused synthetic fixtures prove boundary semantics without manufacturing the
dependency evidence checked by the product path.

## Non-claims

This owner does not claim:

- transitive ecosystem closure;
- exhaustive knowledge of external ecosystems;
- ecosystem membership of the inspected subject;
- Package-to-assembly or assembly-to-Package provenance;
- Package acquisition, version resolution, or dependency traversal;
- ecosystem classification of transitive dependency-inspection nodes or edges
  without owner-issued direct-dependency evidence;
- assembly binding, reference resolution, or platform compatibility;
- Integration detection or application-wiring capability;
- source ownership, publisher identity, support policy, security, or trust;
- namespace-to-assembly identity;
- exact section names, CLI query spelling, final column labels or order, or
  Browser visual layout; or
- that an unrecognized dependency has no ecosystem.
