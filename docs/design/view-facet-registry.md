# View Facet Registry

The View Facet Registry is the product authority for selectable inspection
facets. It gives navigation, portable-definition owners, and other hosts one
stable identity and descriptor space without turning a browser label, CLI
section name, or command flag into a contract.

The initial inspection-lens catalog is implemented in
`DotnetInspector.Queries`. The [Workspace and Package adoption](#workspace-and-package-adoption)
contract is planned, not implemented or issued by this document change.
Adjacent Navigation, workspace-definition, and host consumers remain separate
work.

## Why this is a separate owner

Current consumer vocabularies cannot serve as durable facet identity:

- Inspect Web keeps Type, Package, and Member inventories in TypeScript.
- CLI section names are display names and selection tokens scoped to one
  pipeline.
- product demo definitions bind section display names through
  `ProductDemoSections`.

Those spaces collide. `overview`, `source`, and `metadata` each mean more than
one thing, while two CLI pipelines may expose the same section name. Section
display names also change as presentation: #3229 renamed twelve in one change.
`SelectResolver.LegacySectionAliases` preserves CLI resolution for prior
spellings, not one stable identity across pipelines and releases.

A consumer-owned array makes membership, labels, and order look like local
presentation choices even though changing them changes the product facets a
host can offer. The registry makes those facts product-owned.

## Decision

The View Facet Registry owns:

- one canonical, globally unique ID for every issued view facet;
- each facet's title, summary, structural subject kind, explicit order, stable
  purpose, and optional semantic role;
- the complete explicit product registration set;
- pure structural-applicability classification;
- static descriptor discovery;
- target-aware availability classification from explicit producer facts;
- exact ID and subject resolution; and
- typed unknown, inapplicable, unavailable, and failed outcomes.

The expected implementation is host-neutral and belongs at or below
`DotnetInspector.Queries`. The architecture owner is this contract, not a
project boundary.

The registry is a closed product catalog. Inspected artifacts, workspace
definitions, extensions, and hosts cannot add registrations.

## Ownership and boundaries

### Inputs

The registry consumes:

- explicit product facet registrations;
- the Workspace, Package, Root, Library, Type, and Member structural kinds owned by
  [Inspection Subject Navigation](inspection-subject-navigation.md);
- one exact structural subject for applicability and target-aware discovery;
- already-authorized, owner-issued capability or availability facts; and
- typed failures from the facet producer that owns those facts.

A registration may bind privately to one or several query, section, or
renderer implementations. That execution binding is not descriptor data and
does not become public identity.

### Outputs

The registry returns:

- immutable static descriptors in registry order;
- exact descriptor lookup by canonical ID;
- applicable target-aware options carrying available, unavailable, or failed
  state;
- exact typed resolution outcomes; and
- the descriptor associated with every known non-success outcome.

### Adjacent owners

[Inspection Subject Navigation](inspection-subject-navigation.md) consumes
target-aware options and exact-resolution results. It owns exact subject
binding, recommendation and fallback policy, activation and transition
outcomes, reconciliation, and retained-session behavior. Issue
[#5013](https://github.com/richlander/dotnet-inspect/issues/5013) owns the
focused recommendation residual exposed while designing this registry.

[Workspace definitions](workspace-definitions.md) consumes canonical IDs and
owns persisted registry binding. It also owns which portable fields carry
them, version migration, combination validation, projection, and complete
restoration, tracked by
[#4787](https://github.com/richlander/dotnet-inspect/issues/4787).

[Inspect Web Navigation Presentation](inspect-web-navigation-presentation.md)
consumes the descriptors returned through Navigation. It owns rendering,
accessibility, and the removal of current browser-local catalogs. [Inspect
Web Navigation Consumer](inspect-web-navigation-consumer.md) owns post-result
effect-authority validation, snapshot/history commitment, and
result-authorized focus/announcement ordering, with the migration historically
tracked by [#4917](https://github.com/richlander/dotnet-inspect/issues/4917).

[Section selection](section-model.md), query owners, and renderer owners define
facet content and execution. A facet may project one section, several sections,
or a payload that is not a section. Section display names and CLI `-S` tokens
never double as view-facet IDs.

[Product vocabulary](vocabulary.md) may project this catalog for discovery.
That projection composes the registry; it does not become another authority.

## Identity

### Canonical spelling

A view-facet ID is an ordinal, case-sensitive ASCII string:

```text
<subject>.<name>
```

`<subject>` is exactly `workspace`, `package`, `root`, `library`, `type`, or
`member`. `<name>` is one or more lower-case ASCII alphanumeric words separated
by `-`, begins with a letter, and ends with a letter or digit. The complete
grammar, including the planned Workspace/Package adoption, is:

```text
\A(workspace|package|root|library|type|member)\.[a-z][a-z0-9]*(?:-[a-z0-9]+)*\z
```

The `\A` and `\z` anchors require an absolute full-string match under .NET
regular-expression semantics; in particular, a terminal line feed is not
accepted. The complete ID is at most 80 ASCII characters. Examples are
`type.api`, `library.references`, and `member.call-graph`.

The prefix makes issued IDs globally unambiguous and human-writable. It agrees
with the descriptor's structural subject kind, but consumers do not parse,
synthesize, or rewrite IDs. They compare and return the complete opaque
string.

There is no trimming, case folding, Unicode normalization, label slugging, or
fallback to a CLI spelling. A non-exact value is unknown.

### Compatibility

An issued ID, structural subject kind, and purpose are permanent compatibility
surfaces:

- shipped IDs are additive;
- an ID is never renamed, removed, or reused;
- one spelling never becomes an alias for another ID; and
- replacing or splitting a facet mints new IDs while the prior ID remains
  known.

One checked-in append-only compatibility manifest records each issued ID,
structural kind, and stable purpose statement. Purpose is separate from
rewordable presentation copy. The manifest detects removal, movement, or
accidental repurposing; it does not claim to prove semantic equivalence.

If an implementation is retired, its descriptor remains resolvable through an
explicit tombstone registration. The tombstone has no execution binding and
returns a typed `Retired` unavailable reason when structurally applicable.
Applicability still runs first: a wrong-subject request is `Inapplicable`,
including for a tombstone. Retirement does not turn a formerly known persisted
value into unknown.

Titles and summaries may be reworded or localized. Order and semantic-role
changes are intentional product behavior changes requiring focused evidence,
but they do not migrate persisted IDs.

## Descriptor and registration contracts

Conceptually, one static descriptor has this shape:

```text
ViewFacetDescriptor
  Id       ViewFacetId
  Kind     Workspace | Package | Root | Library | Type | Member
  Title    string
  Summary  string
  Order    int
  Role     ViewFacetRole?
```

`Title` is concise visible text. `Summary` is one complete user-facing sentence
suitable for discovery or an accessible description. Both cross host
boundaries as inert product-owned data.

`Order` is an explicit ascending integer unique within one structural kind.
Sparse values allow additive insertion. Registration order, enum ordinal, ID,
and localized title are not tie-breakers.

Complete-catalog order after Workspace/Package adoption is Workspace, Package,
Root, Library, Type, Member, then descriptor `Order`. The initial catalog has
only the latter four kinds. Kind-scoped and target-aware discovery use
descriptor `Order`.

`Role` is optional semantic metadata for an adjacent product policy. The
registry owns which descriptor carries a role; it does not define how
Navigation chooses or falls back from that role. Within one structural kind,
at most one descriptor carries a given role.

The descriptor does not expose implementation types, delegates, CLI spellings,
browser data, target counts, target availability, selection, acquisition,
network, cost, output-shape, or rendering policy.

A registration has a shared descriptor, stable purpose, and pure applicability
classifier, plus an explicit active-or-tombstone arm. The active arm contains
the availability evaluator, and its ID has exactly one private execution
binding. The tombstone arm contains the fixed `Retired` result but no evaluator
or execution binding. The type shape makes one registration being both active
and tombstoned unrepresentable. Applicability consumes typed
structural-subject facts, not display text, and runs before either arm.

## Discovery and exact resolution

### Static discovery

Static discovery returns every descriptor, or every descriptor for one
structural kind, in registry order. It does not open an artifact, execute a
query or facet, probe a cache, consult an alias or dynamic provider, read the
filesystem, or use the network.

### Target-aware discovery

Target-aware discovery accepts one exact structural subject and explicit
producer facts from one realized inspection snapshot. It omits structurally
inapplicable descriptors. Every applicable descriptor is returned in registry
order with exactly one status:

| Status | Meaning |
| ------ | ------- |
| Available | The facet can execute for this subject with the supplied capabilities. |
| Unavailable | A required capability or active implementation is absent; the typed reason distinguishes `CapabilityAbsent` from `Retired`. |
| Failed | The owner could not determine or prepare availability; diagnostic evidence is retained. |

Unavailable and failed facets remain discoverable with owner-issued reason
text. Failed also preserves typed diagnostic evidence. A validly empty payload
is still available; row count, presence of findings, display labels, and host
support do not decide availability.

The registry does not authorize acquisition or expensive work. Expected
inability to evaluate target facts is `Failed`; unexpected program defects
propagate rather than becoming an empty or unavailable result.

### Exact resolution

Resolving one requested ID against one subject produces exactly one outcome:

| Outcome | Descriptor | Meaning |
| ------- | ---------- | ------- |
| Available | Present | The exact known and applicable facet is available. |
| Unavailable | Present | The exact known facet has a typed `CapabilityAbsent` or `Retired` reason. |
| Failed | Present | Availability preparation failed with diagnostic evidence. |
| Inapplicable | Present | The ID is known, but its structural contract does not match the subject. |
| Unknown | Absent | No issued ID exactly matches the request. |

Applicability is decided before the availability evaluator or execution
binding is consulted. Unknown and inapplicable lookup do not use alias,
dynamic-provider, acquisition, filesystem, network, cache, or execution
fallbacks.

Resolution never selects a neighbor, default, or replacement. Adjacent owners
consume the exact result.

## Initial registry

The first implementation issues these inspection-lens descriptors. This is
the shipped baseline before Workspace/Package adoption:

| ID | Title | Summary | Kind | Order | Role |
| -- | ----- | ------- | ---- | ----: | ---- |
| `root.package-overview` | Overview | Package identity, selected target, assets, and summary facts. | Root | 100 | Package overview |
| `root.package-dependencies` | Dependencies | Declared package dependencies for the selected target framework. | Root | 200 | — |
| `root.overview` | Overview | Coordinate identity, selected target, and available structural subjects. | Root | 300 | Root overview |
| `library.references` | References | Direct assembly references for the active Library. | Library | 100 | Library references |
| `library.integrations` | Integrations | Framework and ecosystem integrations found in the active Library. | Library | 200 | — |
| `library.opportunities` | Opportunities | Framework and ecosystem integrations the active Library could adopt. | Library | 300 | — |
| `library.analysis` | Analysis | Static analysis findings and code characteristics for the active Library. | Library | 400 | — |
| `library.metadata` | Metadata | Physical ECMA-335 metadata and PE structure for the active Library. | Library | 500 | — |
| `type.api` | API | API shape and member inventory for the active Type. | Type | 100 | Type API |
| `type.metadata` | Metadata | Metadata records and attributes for the active Type. | Type | 200 | — |
| `type.source` | Source | Source or decompiled code for the active Type. | Type | 300 | — |
| `member.overview` | Overview | Signature, documentation, and overload context for the active Member. | Member | 100 | Member overview |
| `member.call-graph` | Call graph | Incoming and outgoing calls for the active Member. | Member | 200 | — |
| `member.facts` | Facts | Metadata, IL, safety, and analysis facts for the active Member. | Member | 300 | — |
| `member.source` | Source | Source or decompiled code for the active Member. | Member | 400 | — |
| `member.annotated-source` | Annotated source | Source for the active Member with product analysis annotations. | Member | 500 | — |

In that baseline, the two `root.package-*` facets apply only to a package-capable Root.
`root.overview` applies to supported non-package Roots and is inapplicable to a
package-capable Root. Applicability comes from typed root facts, never ID
parsing or coordinate spelling.

Distinct IDs may share presentation: Library Metadata and Type Metadata, Type
and Member Source, and Root and Member Overview remain separate facets.

The stable purpose of each initial entry is the answer stated by its Summary.
The compatibility manifest copies those purposes at first implementation;
later presentation rewording does not change them.

## Workspace and Package adoption

Issue [#5509](https://github.com/richlander/dotnet-inspect/issues/5509) adopts
Navigation's Workspace-rooted subject grammar without changing issued facet
identity. A Package is no longer a package-capable Root; changing an existing
descriptor's kind would violate this owner's compatibility contract even when
its visible title or content remained similar.

The baseline is additive schema evolution, not reinterpretation of an issued
identity. [Protocol Buffers' field deletion guidance](https://protobuf.dev/programming-guides/proto3/#deleting)
similarly preserves retired identifiers against reuse. That is comparative
evidence, not authority for this Registry: the existing contract additionally
retains queryable descriptors and typed lookup outcomes. No schema or
implementation is imported.

### Additive catalog and exact outcomes

The adoption issues these additional descriptors:

| ID | Title | Summary and stable purpose | Kind | Order | Role |
| --- | --- | --- | --- | ---: | --- |
| `workspace.overview` | Overview | Current Workspace scope, ordered roots, and realization status. | Workspace | 100 | Workspace overview |
| `package.overview` | Overview | Package identity, selected target, assets, and summary facts. | Package | 100 | Package overview |
| `package.dependencies` | Dependencies | Declared package dependencies for the selected target framework. | Package | 200 | — |

Each applies only to its declared owner-issued subject kind. The roles supply
the names already consumed by Navigation's preferred-role contract; this owner
does not choose a subject or a default lens. Availability still consumes
explicit producer facts: an empty Workspace or empty dependency result is not
unavailable merely because it has no rows.

`root.overview` keeps its issued Root kind, purpose, order, and applicability
to supported non-package Roots. Library, Type, and Member facets keep their
issued identities. No new Root producer or subject-construction authority is
introduced by this catalog adoption.

When the package-as-Root execution path is replaced, retain
`root.package-overview` and `root.package-dependencies` as tombstones with their
original IDs, Root kind, and purposes. Retire their execution bindings, not
their descriptors or compatibility-manifest entries. Append the three new IDs
on implementation; do not rewrite the two old entries.

The old tombstones do not acquire Package applicability. Their former
package-capable-Root domain has no subject in Navigation's adopted grammar:
Package and non-package Root are mutually exclusive. Consequently, neither a
current Package nor a non-package Root matches either old facet. A known
descriptor need not have an applicable subject in the current grammar.
Registry does not retain or manufacture a legacy Navigation subject to make
that domain reachable.

The following are post-adoption outcomes, not current runtime results:

| Request and target | Exact result |
| --- | --- |
| `package.overview` on Package, with available producer facts | `Available`, new Package descriptor |
| `package.dependencies` on Package, with absent capability | `Unavailable(CapabilityAbsent)`, new Package descriptor |
| `workspace.overview` on Workspace, with failed producer facts | `Failed`, Workspace descriptor and producer evidence |
| Either old `root.package-*` ID on Package | `Inapplicable`, original Root descriptor |
| Either old `root.package-*` ID on a supported non-package Root | `Inapplicable`, original Root descriptor |
| `root.overview` on Package | `Inapplicable`, unchanged Root descriptor |
| A never-issued ID | `Unknown`, no descriptor |

Target-aware discovery omits the old package-as-Root tombstones because they
are inapplicable. Static discovery retains them in Root order. The generic
tombstone rule is unchanged: an applicable retired facet returns
`Unavailable(Retired)`; an inapplicable one returns `Inapplicable`. The new
Package IDs are not aliases, automatic replacements, or implicit fallback
targets for the old IDs.

Workspace Definitions owns any portable-schema migration that consumes these
outcomes under [#5525](https://github.com/richlander/dotnet-inspect/issues/5525).
This contract supplies exact descriptor identity and lookup results, not a
packet rewrite, a schema-version decision, or restoration policy.

### Consumer-led delivery and evidence

The immediate producer is the stateless Navigation projection
[#6111](https://github.com/richlander/dotnet-inspect/issues/6111), adopted by
the CLI structural consumer
[#5513](https://github.com/richlander/dotnet-inspect/issues/5513).
Inspect Web adopts the same descriptors through #5510, with retained result
consumption under #5511/#6113. CLI lowering remains Markout and structured
output; interactive Browser rendering remains host-owned.

[#5512](https://github.com/richlander/dotnet-inspect/issues/5512), route C of
[#5865](https://github.com/richlander/dotnet-inspect/issues/5865), owns the
counted path: 10 direct delivery milestones and six identified shared
dependencies in its 2026-09-06 reconciliation, a lower bound rather than a
promise of 16 PRs. This is its C2 compatibility checkpoint, not completion of
C2 runtime adoption. The first consumer group is C1 identity replacement,
C2 Registry adoption, C3 stateless projection, and C4 CLI adoption after the
Scope floor. Browser prerequisites and their owners remain separate.

Keep runtime Registry adoption with its producer/host group or at most one
unmerged PR ahead; this design change issues no runtime registrations. Retire
the two old execution bindings at that cutover, not before their existing
consumers have migrated. Do not make a CLI command or Browser adapter translate
an old ID merely to make the Registry cutover appear complete.

Adoption is **unverified** until the existing Release Registry suite covers
the new catalog and the outcome table above. Extend the existing catalog,
binding, compatibility, discovery, and exact-resolution gates; preserve the
generic applicable-tombstone positive and wrong-subject negative using a
subject kind that remains constructible after the identity cutover. The
original package-capable-Root witness belongs to the pre-adoption baseline,
not a reason to preserve that subject family.

`ViewFacetRegistryTests.WorkspacePackageAdoption_PreservesExactLookupOutcomes`
is the required outcome-level adoption gate: it exercises the new available,
unavailable and failed results, the known-but-inapplicable legacy IDs, and an
unknown neighbor. The catalog/compatibility gates additionally retain all 16
issued IDs with their original kinds and purposes, append exactly the three
new entries, and verify that the two old package bindings are retired. Host
adoption supplies its own real consumer evidence; catalog tests alone do not
complete the two-host path.

## Migration boundary

Current browser tokens such as `api`, `metadata`, and `call-graph`, CLI section
names such as `Methods` and `Call Graph`, and `ProductDemoSections` values are
presentation or adapter vocabularies. They do not become registry aliases.

An adjacent owner may keep an explicit scope-aware mapping while migrating its
own surface. It must not slug a label or accept an ambiguous mapping.

This owner does not decide the Workspace Definitions or canonical-packet
version boundary, whether `lens` and `section` survive as separate fields, or
which combinations are valid. [Workspace Definitions](workspace-definitions.md)
owns those decisions, tracked by
[#4787](https://github.com/richlander/dotnet-inspect/issues/4787), and
consumes this canonical ID space.

## Host and platform contract

The catalog uses explicit static registrations. It does not discover providers
with reflection, load inspected assemblies, scan application assemblies, or
depend on registration completion order.

The contracts are SRM-only, NativeAOT-friendly, and usable from
single-threaded Browser/Wasm. Static discovery is synchronous and
side-effect-free. Asynchronous work needed to produce target facts occurs
outside the catalog and returns one explicit snapshot.

Registry requests from definitions, URLs, or other untrusted inputs are inert
bounded strings. They never become paths, type names, service keys, or network
locations.

## Required implementation gates

Before implementation claims this contract, it must add:

Here, the complete no-work sentinel set is execution,
artifact-open/acquisition, cache, alias, dynamic-provider, filesystem, and
network access.

- `ViewFacetRegistryTests.Catalog_IsCompleteUniqueAndDeterministicallyOrdered`:
  valid bounded absolute-match IDs, including rejection of a terminal line
  feed; ID uniqueness; prefix/kind agreement; nonempty presentation; unique
  per-kind order; declared complete-kind order; and role-to-descriptor
  coverage and per-kind uniqueness;
- `ViewFacetRegistryCompatibilityTests.ShippedFacets_RetainIdentityKindAndPurpose`:
  current registrations compared with the append-only compatibility manifest;
- `ViewFacetRegistryTests.RegistrationsAndBindingsAgree`: descriptor IDs equal
  the disjoint union of active- and tombstone-registration IDs;
  active-registration IDs equal the independently declared active-binding IDs;
  every tombstone has no binding and the fixed `Retired` shape; and a synthetic
  tombstone exercises those assertions before the product has a retired facet;
- `ViewFacetRegistryTests.Tombstone_PreservesApplicabilityAndReturnsRetired`:
  a synthetic package-capable-Root-only tombstone returns `Retired` for a
  package-capable Root and remains omitted or exact `Inapplicable` for a
  non-package Root;
- `ViewFacetRegistryTests.StaticDiscovery_DoesNotExecuteOrAcquire`: throwing
  execution, artifact-open/acquisition, cache, alias, dynamic-provider,
  filesystem, and network sentinels all remain untouched;
- `ViewFacetRegistryTests.TargetDiscovery_PreservesOrderAndFailureEvidence`:
  available, validly empty, unavailable, retired, and failed peers retain exact
  order and evidence; inapplicable facets are omitted before evaluation; the
  complete no-work sentinel set remains untouched;
- `ViewFacetRegistryTests.Lookup_DistinguishesEveryOutcome`: all five outcomes,
  syntactically valid and invalid unknown values, and wrong-subject lookup;
  both unknown fixtures and the wrong-subject fixture return before
  availability or any complete no-work sentinel;
- `ViewFacetRegistryTests.RootApplicability_PartitionsPackageAndNonPackageFacets`:
  exact package and non-package Root descriptor sets and opposite
  `Inapplicable` lookups; and
- `ViewFacetRegistryTests.InitialInspectionLensInventory_MatchesContract`: the
  initial IDs, kinds, titles, summaries, order, roles, and applicability.

No single gate claims every catalog wiring property. Before the first release,
the independent expected set in
`InitialInspectionLensInventory_MatchesContract` makes registration omission
observable; after issuance, the compatibility manifest makes removal or
repurposing observable. `RegistrationsAndBindingsAgree` is the execution-wiring
non-vacuity gate: removing an active binding or attaching one to its synthetic
tombstone must fail it.
`Tombstone_PreservesApplicabilityAndReturnsRetired` makes retained
applicability observable. The registration sum type makes active/tombstone
overlap unrepresentable, so this contract does not claim a runtime overlap
mutation.

No TLA+ model accompanies this owner. It is an immutable catalog plus pure
lookup and classification over one explicit snapshot, with no retained,
concurrent, distributed, or scheduling interaction. Navigation and complete
restoration own stateful interactions.

## Implementation status

The immutable registry, initial 16-facet catalog, private execution bindings,
typed applicability and availability inputs, exact resolution outcomes, and
append-only compatibility manifest are implemented by
`ViewFacetRegistry.cs`, `InspectionViewFacetCatalog.cs`, and
`eng/view-facet-compatibility.json`.

Workspace/Package grammar, the three new descriptors, and retirement of the
two package-as-Root bindings are not implemented here. The initial four-kind
runtime and 16 issued manifest entries remain unchanged until the
consumer-paired adoption above.

The initial contract is enforced by
`ViewFacetRegistryTests.Catalog_IsCompleteUniqueAndDeterministicallyOrdered`,
`ViewFacetRegistryCompatibilityTests.ShippedFacets_RetainIdentityKindAndPurpose`,
`ViewFacetRegistryTests.RegistrationsAndBindingsAgree`,
`ViewFacetRegistryTests.Tombstone_PreservesApplicabilityAndReturnsRetired`,
`ViewFacetRegistryTests.StaticDiscovery_DoesNotExecuteOrAcquire`,
`ViewFacetRegistryTests.TargetDiscovery_PreservesOrderAndFailureEvidence`,
`ViewFacetRegistryTests.Lookup_DistinguishesEveryOutcome`,
`ViewFacetRegistryTests.RootApplicability_PartitionsPackageAndNonPackageFacets`,
and
`ViewFacetRegistryTests.InitialInspectionLensInventory_MatchesContract`.

Current transitional surfaces are:

- `prototypes/inspect-web/src/data.ts`, which owns browser arrays and local
  tokens;
- `ProductDemoSections`, which admits product demo section display names; and
- CLI section descriptor names and `SelectResolver` aliases, which remain
  command presentation rather than registry identity.

## Non-goals

This design does not:

- define exact subject-bound lens identity, subject defaults, recommendation,
  fallback, activation, transitions, or reconciliation;
- define inspect-web rendering, keyboard behavior, focus, accessibility, or
  local-catalog migration;
- define portable schema or packet fields, versioning, combination validation,
  or restoration atomicity;
- define facet payloads, section membership, query execution, or rendering;
- define acquisition, network, cache, or cost authorization;
- define CLI spellings or preserve them as registry aliases;
- define target-aware accessibility or other query-filter facets; or
- allow third-party or inspected-artifact registrations.
