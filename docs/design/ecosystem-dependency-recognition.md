# Ecosystem dependency recognition

## Status

Proposed focused owner for
[#7818](https://github.com/richlander/dotnet-inspect/issues/7818).

The owner defines one product-relative interpretation over already-issued
direct dependency observations. Package and Library extraction, CLI sections,
and Browser presentation remain separate adoption efforts.

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
- explicit complete, incomplete, and unavailable outcomes;
- deterministic product, observation, and recognition ordering;
- recognized, unrecognized, candidate, and association counts;
- one authoritative multi-part recognition Document; and
- the completed `InspectionEnvelope<EcosystemDependencyRecognitionOutcome>`
  handoff to production hosts.

Its exact claim is:

> Given one validated resource-free product recognition profile and one
> owner-issued subject-bound direct-dependency observation batch, recognize
> every shipped ecosystem whose authored association matches each observation,
> preserve the subject and applicable target selection, observation,
> declaration source, matching basis, overlap, non-match, and input completion
> in one detached product-relative Document without traversing dependencies or
> inferring Package-to-assembly provenance.

The answer is deliberately product-relative. An unrecognized dependency means
that no association in the supplied product profile matched it. It does not
mean that the dependency belongs to no external ecosystem.

## User value

Package and Library inspection currently expose exact dependency declarations
but leave users to recognize product families from Package and assembly names.
The target experience adds a compact answer backed by inspectable evidence:

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

These are consumer mockups, not section schemas owned by this design. Package
Info, Library Info, section placement, labels, and host rendering remain with
their existing owners.

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
- [Progressive disclosure](progressive-disclosure.md), [Output
  Shapes](output-shapes.md), and [Style Guide](style-guide.md) for later CLI
  section adoption; and
- the managed inspect-web facade and Browser presentation owners for later
  Browser/Wasm adoption.

Product curation remains outside `DotnetInspector.Queries`. Reusable Package,
Metadata, Services, Queries, and browser Core projects do not reference the
application catalog.

## Boundary

The operation starts after dependency facts have been selected and decoded:

```text
Package or Library owner
  -> owner-issued semantic subject and typed input context
  -> ordered direct-dependency observation batch
  -> completion and owner-issued diagnostics

Product ecosystem catalog
  -> immutable recognition profile

Ecosystem Dependency Recognition
  -> complete, incomplete, or unavailable recognition outcome
  -> InspectionEnvelope
  -> CLI or managed inspect-web facade
```

The owner performs no network, filesystem, PackageHouse, PlatformHouse,
Workspace, metadata decoding, dependency traversal, package resolution, or
assembly loading. Its cost is bounded by the supplied observations and profile
associations.

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
  - exact realized source coordinate
  - portable Library identity
```

The Package subject's `RealizedMemberCoordinate.Package` identifies the exact
acquisition even when no root manifest exists or manifest validation fails.
The Library subject consumes the source owner's exact
`RealizedMemberCoordinate` and the selected assembly's
`PortableLibraryIdentity`. It does not infer Package provenance from the
assembly name.

Each batch separately carries a typed input context. Required input components
retain owner-issued values only when those values exist:

```text
Package input context
  - manifest projection:
      Available(PackageManifestFacts)
      | Unavailable(input-issue reference)
  - dependency-group selection:
      Available(requested target, selection status,
                selected target, selected group index)
      | Unavailable(input-issue reference)
      | NotAttempted(input-issue reference)
  - compile-asset selection:
      Available(PackageCompileAssetSelectionReceipt)
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
- the dependency-group query's requested target, selection status, selected
  target, and selected group index retain which manifest group supplied
  Package observations; and
- `PackageCompileAssetSelectionReceipt` retains the requested and selected
  compile slice from which selected-Library observations were produced.

The context does not rerun either selection. A complete Package batch requires
all three components to be available and corresponding, including one
effective target-framework slice for the combined observation population. An
unavailable or not-attempted component, or a mismatch among available
components, is an input issue and cannot produce a complete Document.

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
  - DeclaredPackageDependency
  - declaring Package coordinate or manifest subject
  - source order

Assembly reference
  - observation identity
  - AssemblyReferenceIdentity
  - declaring Library identity
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

The inspected Package or Library is retained as the semantic subject but is not
classified from its own name. It may appear in a recognition or unrecognized
observation population only if an owner-issued dependency observation names
it.

## Observation batch and completion

One input batch has one of three states:

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

One `EcosystemDependencyRecognitionDocument` contains:

- the exact Package or Library semantic subject and typed input context;
- a summary with the ordered product ecosystem candidate count, Package and
  assembly association counts, total, recognized, and unrecognized observation
  counts, distinct recognized ecosystem count, and
  observation-to-ecosystem recognition count;
- distinct recognized ecosystem descriptors in product order;
- recognized observation entries;
- unrecognized observations in source order;
- complete or incomplete coverage; and
- input issues when coverage is incomplete.

These are correlated semantic parts of one Document. A compact rollup, detailed
recognition rows, non-match disclosure, and coverage/failure disclosure are
section projections over that Document, not independently constructed host
models.

The Document validates that its observations belong to its semantic subject
and available input components, every unavailable or not-attempted component
refers to a contained input issue, every recognition refers to one contained
observation and one contained ecosystem descriptor, no observation appears in
both the recognized and unrecognized populations, and every summary count
equals its source populations. A complete Document has no unavailable or
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

## Completed host handoff

The completed application operation returns:

```text
InspectionEnvelope<EcosystemDependencyRecognitionOutcome>
```

The Package or Library composition supplies the `InspectionShare` outcome for
the exact semantic subject plan. Recognition validates that this plan
corresponds to the Document's semantic subject. It does not derive Share from
a Package ID, assembly name, rendered command, or dependency evidence.

Input issues remain in the owner-issued content outcome. Supplemental
cross-host diagnostics may also appear in the envelope, but diagnostics alone
must not turn an incomplete or unavailable content outcome into a complete
one.

The Document is detached and resource-free before it crosses to either host.

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
  rows.
- The managed inspect-web facade consumes the same envelope and projects a
  portable payload.
- TypeScript owns Browser gestures and DOM presentation but does not implement
  Package or assembly association matching.

Any host-specific Browser presentation is an explicit UI lowering over the
same typed recognition Document, not a second classifier.

## Production adoption plan

[#7818](https://github.com/richlander/dotnet-inspect/issues/7818) is the
end-to-end tracker. The current plan has seven steps:

1. Lock this focused recognition contract.
2. Implement the product profile, recognition operation, envelope, and
   contract tests in `DotnetInspector.Ecosystems`.
3. Adopt Package direct-dependency observations and CLI Package Info/detail
   presentation.
4. Adopt Library direct-reference observations and CLI Library Info/detail
   presentation.
5. Expose the same envelope through the managed inspect-web facade.
6. Adopt the result in the Browser Package surface.
7. Adopt the result in the Browser Library surface.

Each adoption remains a focused owner change. This design does not define
Package target-framework selection, selected compile-Library construction,
Library query orchestration, section registration, or Browser interaction.

## Contract gates

The implementation must name Release gates for:

- profile validation and snapshot immutability;
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
- assembly binding, reference resolution, or platform compatibility;
- Integration detection or application-wiring capability;
- source ownership, publisher identity, support policy, security, or trust;
- namespace-to-assembly identity;
- section names, default disclosure, Info-field placement, or rendering; or
- that an unrecognized dependency has no ecosystem.
