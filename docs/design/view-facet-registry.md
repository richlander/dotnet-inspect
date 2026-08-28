# View Facet Registry

The View Facet Registry is the product authority for selectable inspection
facets. It gives navigation, portable definitions, and other hosts one stable
identity space without making a browser label, CLI section name, or command
flag into a contract.

This is a target contract. The registry is not implemented yet; the
[implementation status](#implementation-status) names the temporary surfaces
that remain.

## Why this is a separate owner

An inspection facet is the product answer to "which view of this subject?"
Today, several consumers answer that question independently:

- Inspect Web keeps Type, Package, and Member inventories in TypeScript.
- CLI section names are display names and selection tokens scoped to one
  pipeline.
- product demo definitions bind section display names through
  `ProductDemoSections`.

Those spaces collide. `overview`, `source`, and `metadata` each mean more than
one thing, while two different CLI pipelines may expose the same section name.
A consumer-owned array also makes labels and order look like presentation
choices even though adding or moving a facet changes product navigation.

The registry makes identity, presentation metadata, order, structural
applicability, and availability classification product facts. Hosts render
descriptors and submit exact IDs. They do not reproduce membership or recover
identity from display text.

## Decision

The View Facet Registry owns:

- one canonical, globally unique ID for every issued view facet;
- the facet's title, summary, structural subject kind, explicit order, and
  optional navigation recommendation role;
- the complete product registration set;
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
- the Root, Library, Type, and Member structural kinds defined by
  [Inspection Subject Navigation](inspection-subject-navigation.md);
- one exact structural subject for target-aware discovery;
- already-authorized, owner-issued capability or availability facts; and
- typed failures from the facet producer that owns those facts.

A registration may bind privately to one or several query, section, or
renderer implementations. That execution binding is not descriptor data and
does not become a public identity.

Each registration also provides a pure structural-applicability classifier.
The classifier consumes typed subject facts rather than display text. It may,
for example, distinguish a package-capable Root from another Root without
executing the facet or probing target content.

### Outputs

The registry returns:

- immutable descriptors in registry order;
- exact descriptor lookup by canonical ID;
- applicable descriptors for one structural subject kind;
- target-aware facet options carrying available, unavailable, or failed state;
- typed explicit-resolution outcomes; and
- the exact descriptor associated with every known non-success outcome.

### Adjacent owners

[Inspection Subject Navigation](inspection-subject-navigation.md) owns subject
hierarchy, subject binding, initial recommendation, activation commands,
reconciliation, and retained-session behavior. It consumes registry descriptors
and target-aware options or exact-resolution results. Navigation may orchestrate
a registry call by passing producer facts opaquely, but it does not inspect or
reclassify those facts. A navigation lens identity is the active structural
subject plus one registry ID; the registry ID identifies the facet definition,
not one exact subject instance.

[Workspace definitions](workspace-definitions.md) owns the persisted schema,
registry binding, and validation of complete query/view combinations. It stores
canonical view-facet IDs but does not mint them. Issue
[#4787](https://github.com/richlander/dotnet-inspect/issues/4787) owns portable
projection and complete restoration composition.

[Inspect Web UI](inspect-web-ui.md) owns rendering, accessibility, focus, and
interaction. It renders owner-issued descriptors and status without keeping a
parallel facet inventory.

[Section selection](section-model.md), query owners, and renderer owners define
facet content and execution. A facet may project one section, several sections,
or a payload that is not a section. Section display names and CLI `-S` tokens
therefore never double as view-facet IDs.

[Product vocabulary](vocabulary.md) may project this catalog for discovery.
That projection composes the registry; it does not become another authority.

## Identity

### Canonical spelling

A view-facet ID is an ASCII, ordinal, case-sensitive string:

```text
<subject>.<name>
```

`<subject>` is exactly `root`, `library`, `type`, or `member`. `<name>` is one
or more lower-case ASCII alphanumeric words separated by `-`, begins with a
letter, and ends with a letter or digit. The complete grammar is:

```text
^(root|library|type|member)\.[a-z][a-z0-9]*(?:-[a-z0-9]+)*$
```

Examples are `type.api`, `library.references`, and `member.call-graph`.
The complete ID is at most 80 ASCII characters.

The prefix makes every issued ID globally unambiguous and human-writable in a
workspace definition. It also agrees with the descriptor's structural subject
kind. The prefix is not permission for a consumer to parse, synthesize, or
rewrite IDs. Consumers compare and return the complete opaque string.

There is no trimming, case folding, Unicode normalization, label slugging, or
fallback to a CLI spelling. A non-exact value is unknown.

### Compatibility

An issued ID and its structural subject kind are permanent compatibility
surfaces:

- shipped IDs are additive;
- an ID is never renamed, removed, or reused;
- one spelling never becomes an alias for another ID; and
- replacing or splitting a facet mints new IDs while the prior ID remains
  known.

If an implementation is retired, its descriptor remains resolvable and reports
an owner-issued `Retired` unavailable reason through an explicit tombstone
registration. The tombstone replaces the active execution binding; it does not
leave a descriptor unregistered. A future retirement policy that removes
facets from ordinary discovery requires a separate design; absence must not
silently turn a formerly known persisted value into unknown.

Titles and summaries are presentation copy and may be reworded or localized.
Order and recommendation-role changes are intentional behavior changes rather
than identity changes: they require focused regression evidence because they
can change navigation fallback or presentation, but they do not migrate
persisted IDs.

One checked-in append-only compatibility manifest records each issued ID,
structural subject kind, and stable purpose statement. A registration carries
the same non-presentation purpose separately from its rewordable title and
summary. `ViewFacetRegistryCompatibilityTests.ShippedFacets_RetainIdentityKindAndPurpose`
must derive current entries from the registry and compare them with that
manifest so a missing ID, moved kind, or accidental repurposing fails. The gate
does not claim to prove semantic equivalence; changing an existing manifest row
is an explicit compatibility-contract edit requiring focused review.

## Descriptor contract

Conceptually, one static descriptor has this shape:

```text
ViewFacetDescriptor
  Id                  ViewFacetId
  SubjectKind         Root | Library | Type | Member
  Title               string
  Summary             string
  Order               int
  RecommendationRole  ViewFacetRecommendationRole?
```

`Id` is the stable identity. `Title` is concise visible text. `Summary` is one
complete user-facing sentence suitable for an accessible description or
discovery result. Both are product-owned text and cross a host boundary as
inert data.

`Order` is an explicit ascending integer. It is unique within one structural
subject kind. Registration order, enum ordinal, ID, and localized title are
not tie-breakers; a duplicate order makes the catalog invalid. Sparse values
allow an additive facet to be inserted without renumbering every later entry.

The complete-catalog order is structural kind in Navigation hierarchy order
(`Root`, `Library`, `Type`, `Member`), then ascending `Order` within that kind.
Kind-scoped discovery uses only ascending `Order`.

`RecommendationRole` is an optional typed semantic marker used by Inspection
Subject Navigation to find the exact facet that satisfies one of its preferred
roles. The registry owns which descriptor carries a role. Navigation owns
whether and when the role is preferred. A role is not persisted identity, and
a consumer does not infer one from `Title` or `Id`.

The initial role vocabulary is `PackageOverview`, `RootOverview`,
`LibraryReferences`, `TypeApi`, and `MemberOverview`. Each role is carried by
exactly one descriptor and agrees with that descriptor's subject contract.
Adding a role extends this typed handoff; changing a role assignment is a
coordinated Registry and Navigation contract change with focused evidence, not
a consumer-side edit.

The descriptor does not expose:

- implementation type names or delegates;
- CLI commands, flags, section names, or categories;
- browser route, DOM, or accessibility data;
- a default or selected bit;
- target-specific counts or availability; or
- acquisition, cost, network, cache, or rendering policy.

`ViewFacetRegistryTests.Catalog_IsCompleteUniqueAndDeterministicallyOrdered`
must gate valid IDs, ID uniqueness, prefix/kind agreement, nonempty
presentation, unique per-kind order, Root-to-Member kind order, ascending order
within each kind, and exact role-to-descriptor coverage.
`ViewFacetRegistryTests.RegistrationsAndBindingsAgree` must use set equality so
every descriptor has exactly one active or tombstone registration, every active
registration has an execution binding, no binding exists without a
registration, and every tombstone has no execution binding plus a fixed
`Retired` availability evaluator. Removing the registration input must fail
this test; that is the required non-vacuity gate for registry wiring.

## Discovery and availability

Static and target-aware discovery are separate operations.

### Static discovery

Static discovery returns every descriptor, or every descriptor for one
structural subject kind, without opening an artifact, executing a query,
probing a cache, reading the filesystem, or using the network. It answers
"what may the product offer?" rather than "can this target render it now?"

The returned collection is immutable and already ordered by kind then `Order`,
or by `Order` for kind-scoped discovery. A consumer must not sort it again.

`ViewFacetRegistryTests.StaticDiscovery_DoesNotExecuteOrAcquire` must provide
throwing execution, artifact-open/acquisition, cache, alias, dynamic-provider,
filesystem, and network sentinels and prove descriptor discovery succeeds
without invoking any of them.

### Target-aware discovery

Target-aware discovery accepts one exact structural subject and explicit
producer facts for one realized inspection snapshot. It first evaluates the
registrations' structural-applicability classifiers. Inapplicable descriptors
are omitted from ordinary discovery; explicit resolution still reports them as
known. Every applicable descriptor is returned in registry order paired with
exactly one status:

| Status | Meaning |
| ------ | ------- |
| Available | The facet can execute for this subject with the supplied capabilities. |
| Unavailable | The subject contract matches, but a required target capability or active implementation is absent; the typed reason distinguishes `CapabilityAbsent` from `Retired`. |
| Failed | The owner could not determine or prepare availability; diagnostic evidence is retained. |

Unavailable and failed facets remain discoverable. Each carries owner-issued
plain text explaining the non-success state; failed also preserves typed
diagnostic evidence. A validly empty result is still available. Row count,
presence of findings, display labels, and consumer support do not decide
availability.

The registry does not authorize acquisition or expensive work. A host or query
owner supplies only facts and capabilities already allowed by its request.
Expected inability to evaluate a target is returned as `Failed`; unexpected
program defects propagate rather than being caught and converted into an empty
or unavailable result.

`ViewFacetRegistryTests.TargetDiscovery_PreservesOrderAndFailureEvidence` must
cover available, validly empty, unavailable, and failed facets together and
prove one failed facet neither disappears nor suppresses successful peers. It
must also prove a structurally inapplicable facet is omitted without invoking
its availability evaluator. Throwing execution, acquisition, alias,
dynamic-provider, filesystem, and network sentinels must prove applicable
availability is classified only from the supplied facts and does not execute,
acquire, or consult a fallback for the payload.

## Exact resolution

Resolving a requested ID against one subject produces exactly one typed
outcome:

| Outcome | Descriptor | Meaning |
| ------- | ---------- | ------- |
| Available | Present | The exact known facet is available for the subject. |
| Unavailable | Present | The exact known facet has an owner-issued `CapabilityAbsent` or `Retired` reason. |
| Failed | Present | Availability or preparation failed with retained diagnostic evidence. |
| Inapplicable | Present | The ID is known, but its structural subject contract does not match the requested subject. |
| Unknown | Absent | No issued registry ID exactly matches the request. |

An inapplicable result never becomes unknown merely because the caller asked
under the wrong subject. Unknown retains the rejected input as inert data for a
diagnostic, but it does not invoke dynamic loading, reflection, filesystem
probing, network access, or alias resolution.

Resolution never selects a neighbor or default. Inspection Subject Navigation
owns any recommendation after a non-success result and retains the original
evidence.

`ViewFacetRegistryTests.Lookup_DistinguishesEveryOutcome` must derive known IDs
from the catalog and cover an exact available hit, unavailable hit, failed hit,
wrong-subject hit, and syntactically valid and invalid unknown values. Throwing
execution, acquisition, alias, dynamic-provider, filesystem, and network
sentinels must prove unknown lookup returns without consulting any fallback or
facet binding. The wrong-subject fixture must give the known registration a
throwing availability evaluator plus the same complete sentinel set and prove
`Inapplicable` is returned before any of them is invoked.

### Navigation handoff

Registry resolution and Navigation transitions are different typed layers; no
outcome is collapsed at their seam. After Navigation has accepted a current,
valid command:

| Registry result | Navigation use |
| --------------- | -------------- |
| Available | May become an available lens descriptor and exact activation target; successful Navigation preparation may return `Applied`, while a later Navigation failure remains `Failed`. |
| Unavailable | Remains an unavailable descriptor; an exact request returns Navigation's `Unavailable` outcome. |
| Failed | Remains a failed descriptor; an exact request returns Navigation's `Failed` outcome. |
| Inapplicable | Is absent from ordinary discovery; an exact request returns Navigation's `Rejected` outcome with the registry result retained as diagnostic evidence. |
| Unknown | Has no descriptor; an exact request returns Navigation's `Rejected` outcome with the rejected ID retained as diagnostic evidence. |

Navigation does not relabel `Failed` as unavailable, relabel `Inapplicable` as
unknown, or discard the original result after mapping it to a transition
outcome. `InspectionSubjectNavigationTests.RegistryResolutionOutcomesRetainExactEvidence`
must gate all five rows of this handoff.

## Initial registry

The first implementation issues the following inspection-lens descriptors.
Orders are sparse and local to one structural subject kind.

| ID | Title | Summary | Kind | Order | Recommendation role |
| -- | ----- | ------- | ---- | ----: | ------------------- |
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

The two `root.package-*` entries are structurally Root facets whose producers
require a package-capable root. They are omitted from ordinary target-aware
discovery for a non-package Root, and exact lookup there returns
`Inapplicable`.
`root.overview` applies to supported non-package Roots and supplies their
current root-owner recommendation; it is inapplicable to a package-capable
Root. The registry does not infer root capability from an ID prefix or
coordinate spelling.

Library Metadata and Type Metadata deliberately share a title but not an ID.
The same is true for Root and Member Overview and for Type and Member Source.

`ViewFacetRegistryTests.InitialInspectionLensInventory_MatchesContract` must pin
this first issued set, titles, summaries, kinds, orders, and recommendation
roles. After the first release, the additive compatibility gate becomes the
authority for identity retention; the inventory gate continues to guard
accidental semantic movement.

`ViewFacetRegistryTests.RootApplicability_PartitionsPackageAndNonPackageFacets`
is the non-vacuity gate for the initial Root contract. A package-capable Root
must discover exactly Package Overview and Package Dependencies in order,
resolve `root.overview` as `Inapplicable`, and expose `PackageOverview` on the
package descriptor. A supported non-package Root must discover exactly Root
Overview, resolve both `root.package-*` IDs as `Inapplicable`, and expose
`RootOverview`. Removing or inverting any applicability classifier must fail
the gate.

For the initial compatibility manifest, each row's stable purpose is the answer
stated by its Summary in the table above. The manifest copies that purpose at
first implementation; later presentation rewording changes the descriptor
Summary without changing the manifest.

## Facets and execution

A view facet is not required to be one section.

- a facet may compose several existing sections;
- two facets may reuse a lower-level query while presenting different
  subject-scoped answers;
- a facet may return a graph, source document, or other payload outside a
  section pipeline; and
- target-aware availability may use a bounded producer fact without executing
  the facet payload.

These bindings remain private registrations. Exposing CLI section display
names in a descriptor would recreate the unstable identity problem the
registry solves.

Facet execution receives an exact resolved registration from the owner; it
does not dispatch on user-controlled type names or reflection. The registry
does not define execution results, section composition, output shapes, or
rendering.

## Migration

Current browser tokens such as `api`, `metadata`, and `call-graph`, and current
CLI section names such as `Methods` and `Call Graph`, are presentation or
adapter vocabularies. They do not become aliases in registry lookup.

During migration, a host may keep an explicit, scope-aware mapping from one
legacy surface token to one canonical ID. That mapping belongs to the adapter
whose compatibility it preserves. It must not derive an ID by slugging a label
or section name, and it must reject an ambiguous mapping.

Existing Workspace Definitions schema version 1 and canonical packet format 1
use legacy presentation tokens. They do not acquire canonical-ID semantics in
place. The first schema and packet versions that opt into registry IDs must be
greater than 1. Their schema-owned version transforms are the single sources of
persisted-definition and packet legacy-to-canonical mapping: the version-1
values lower to canonical IDs before registry resolution, and each newer
canonical version rejects legacy tokens. `ProductDemoSections` remains the
temporary allow list until that work lands; the registry does not accept its
display names as identity.

The registry does not decide whether a portable packet carries one facet field
or separate lens and section fields, or which combinations are valid. That is
the portable projection and Workspace Definitions boundary owned by #4787.
Every field a newer owning schema version designates as a view-facet field
resolves through this one canonical ID space; an undecided `section` field does
not become registry-owned merely because it exists in version 1.

## Host and platform contract

The product catalog uses explicit static registrations. It does not discover
providers with reflection, load inspected assemblies, scan application
assemblies, or depend on registration completion order.

The descriptor and resolution contracts are SRM-only, NativeAOT-friendly, and
usable from single-threaded Browser/Wasm. Static discovery is synchronous and
side-effect-free. Any asynchronous work needed to produce target capability
facts occurs outside the catalog and returns one explicit snapshot to
target-aware discovery.

Registry IDs are bounded product constants. Requests from definitions, URLs,
or other untrusted inputs are compared as inert strings and never become
paths, type names, service keys, or network locations.

## Implementation status

The registry types, registrations, and gates in this document are not yet
implemented.

The current transitional surfaces are:

- `prototypes/inspect-web/src/data.ts`, which owns browser arrays and local
  tokens;
- `ProductDemoSections`, which admits product demo section display names; and
- CLI section descriptor names and `SelectResolver` aliases, which remain
  command presentation rather than registry identity.

Implementation must add the named gates above before replacing those surfaces.
Inspection Subject Navigation must additionally add
`InspectionSubjectNavigationTests.RegistryDescriptorsRemainOwnerOrdered`, a
non-vacuity gate that removes the registry input and proves navigation no
longer returns a locally authored membership or order. Workspace Definitions
retains its required view-facet registry gate for unknown persisted IDs and
additive shipped identities.

No TLA+ model accompanies this owner. The registry is an immutable catalog plus
pure lookup and classification over one explicit snapshot; it has no retained,
concurrent, distributed, or scheduling interaction. Navigation and complete
restoration own the stateful interactions and their models.

## Non-goals

This design does not:

- define subject identity, hierarchy, defaults, activation, or reconciliation;
- define inspect-web layout, keyboard behavior, focus, or accessibility;
- define facet payloads, section membership, query execution, or rendering;
- define acquisition, network, cache, or cost authorization;
- define CLI spellings or preserve them as registry aliases;
- define canonical packet fields, combination validation, or restoration
  atomicity;
- define target-aware accessibility or other query-filter facets; or
- allow third-party or inspected-artifact registrations.
