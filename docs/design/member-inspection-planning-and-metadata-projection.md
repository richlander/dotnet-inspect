# Member inspection planning and metadata projection

Status: **proposed**.

This design pauses implementation review of the type/member inspection stack
until two boundaries are explicit:

1. a resolved member inspection plan that separates user intent, section
   resolution, producer demand, capability authorization, and presentation;
2. one Metadata-owned declaration validation path consumed by full API
   extraction, summary extraction, focused queries, and model-bound C#
   projection.

The proposal applies the existing
[inspection space](../inspection-space.md),
[inspection layers](inspection-layers.md),
[progressive disclosure](progressive-disclosure.md),
[section model](section-model.md), and
[bounded metadata traversal](bounded-metadata-traversal.md), and
[shared metadata primitives](../metadata-primitives.md) decisions to the
type/member path. It does not introduce a second query architecture.

## Why the implementation review is paused

The #3864/#3865 implementation stack supplied repeated evidence in PRs #3877
and #3880 that review was discovering design rules rather than checking an
implementation against settled rules.

The findings clustered around two seams:

- type/member options represented parsed selectors, provisional catalog
  selection, resolved member selection, render demand, and acquisition
  authorization in one mutable value;
- full extraction, summary extraction, focused declaration queries, and C#
  projection independently interpreted metadata validity and representability.

Those seams produced recurring defect classes:

- a provisional section catalog was treated as a resolved member-detail
  catalog;
- schema resolution accidentally authorized PDB acquisition;
- effective discovery and rendering disagreed about the active pipeline;
- full, summary, and focused paths disagreed about visibility,
  accessibility, accessor shape, and `MethodImpl` semantics;
- bounded decode accounting reached some projections but not equivalent
  projections;
- C# inferred representability from raw Boolean combinations that did not
  distinguish interface slot reuse from base-class slot reuse.

These were reproducible product defects, not primarily review expansion beyond
the stated contract. Continuing the same review loop would likely find another
call-site manifestation without proving that the shared rule had one owner.

## Authority and verification status

This document is proposed integration policy. It does not replace the
mechanism-specific owners:

| Concern | Authoritative design |
| --- | --- |
| Typed query planning, execution, and capability model | [Inspection space](../inspection-space.md) |
| Layer and dependency ownership | [Inspection layers](inspection-layers.md) |
| Gesture disclosure and capability-request provenance | [Progressive disclosure](progressive-disclosure.md) |
| Candidate, effective, rendered, and discovery semantics | [Section model](section-model.md) |
| Markout structural schema and realized discovery/render-manifest mechanics | [Schema query](schema-query.md) |
| Metadata traversal and projection budgets | [Bounded metadata traversal](bounded-metadata-traversal.md) |
| Mechanical SRM primitives and registered lossless-row exceptions | [Shared metadata primitives](../metadata-primitives.md) |
| Type/member identities, selectors, and anchors | [Type, member, and API representation](type-member-api-representation.md) |
| Untrusted input and failure containment | [Untrusted data threat model](untrusted-data-threat-model.md) |

If this proposal conflicts with one of those mechanism-specific documents,
stop and reconcile the owner rather than silently treating this integration
document as an override.

After acceptance, this document is authoritative only for how the type/member
path composes those mechanisms. Its target behavior remains unverified until
the named migration gates in [Verification obligations](#verification-obligations)
land. Merging the document accepts the direction and boundaries; it does not
claim that current product code enforces them.

## Decision summary

The type/member path will have two explicit typed boundaries.

```text
CLI gesture
  -> ParsedInspectionIntent
  -> authorized target-resolution plan
  -> target and member resolution
  -> ResolvedMemberInspectionPlan
  -> preflighted typed producer query plan
  -> producer results and typed failures
  -> presentation projection

metadata image
  -> MetadataDeclarationSession
  -> validated declaration facts or typed rejection
  -> full / summary / focused API projections
  -> model-bound C# spelling
```

The boundaries meet at typed results. A resolved section name does not grant
permission to run a producer, and an `ApiMember` Boolean does not grant
permission to reinterpret raw metadata semantics.

## Goals

- Make provisional and resolved selection states different types.
- Preserve exact selector, overload, digest, category, and wildcard intent
  until the target and active member pipeline are known.
- Derive producer demand and authorization from one typed plan.
- Keep static schema target-free and keep schema/discovery resolution incapable
  of authorizing symbol, source, or analysis acquisition.
- Keep effective discovery within an explicit probe budget.
- Validate each shared metadata declaration fact once per image generation and
  semantic context within an operation, then reuse it across output
  projections.
- Preserve cheap filtering before expensive signature and `MethodImpl`
  projection without allowing malformed addressed declarations to appear
  valid.
- Keep Metadata responsible for metadata facts and CSharp responsible for C#
  representability.
- Preserve SRM-only, NativeAOT-friendly, Roslyn-free, Browser/Wasm-compatible
  product paths.
- Make malformed-input and acquisition failures visible rather than
  success-shaped absence.

## Non-goals

- Replacing Markout schema generation or rendering.
- Creating one universal metadata object model for Metadata, Analysis, and
  Decompiler.
- Moving C# spelling rules into Metadata.
- Making every discovery gesture execute every producer.
- Changing package, platform, or project acquisition policy.
- Supporting Windows Metadata (`.winmd`, `MetadataKind.WindowsMetadata`, or
  `MetadataKind.ManagedWindowsMetadata`). Those inputs are outside
  dotnet-inspect's current project scope.
- Making `ApiType` or `ApiMember` reader-backed.
- Reconstructing the design in a test harness.
- Preserving implementation types introduced only on the paused branches.

## Boundary one: resolved member inspection planning

### The states that must stay separate

| State | Meaning | May authorize work? |
| --- | --- | --- |
| Static structural route | For commandless static schema, one syntax-proven command/view/catalog or labeled cross-command alternatives | No |
| Parsed intent | The user's inspection surface, gesture, selectors, requested projection, and output mode | No |
| Demand classification | Selector matches reduced to canonical semantic-section target requirements only | No |
| Structural selection | Names resolved against one final catalog, or labeled static alternatives | No |
| Target-resolution plan | Minimal acquisition and Metadata inventory needed to resolve the address | Only after preflight |
| Target resolution | The exact type plus a resolved inventory set or exact member target, or a typed diagnostic | No new work |
| Producer plan | Typed producer queries, prerequisites, costs, capabilities, and probe/render mode | No |
| Preflighted plan | Authorized closures and typed denied alternatives fixed by host policy | Yes, for authorized closures only |
| Presentation plan | Sections, shape, columns/fields, and format applied to completed results | No new work |

No state is represented by mutating the preceding state. In particular,
`IncludeSections` is not both a record of user intent and permission to acquire
or analyze content.

### Canonical axis mapping

The existing section and schema terms compose as follows:

| Question | Owner | Answer | Executes section producers? |
| --- | --- | --- | --- |
| What names and fields exist? | Markout schema query | Structural schema | No |
| Which sections did the gesture place in scope? | Section selection over the final catalog | Candidate sections | No |
| Which target/member does the address denote? | Authorized target-resolution plan and typed resolver | Resolved target or diagnostic | No |
| Which candidates apply to this target? | Typed query probe plan | Effective sections or typed failures | Only declared probes |
| Which producer results are required? | Typed query planner | Producer prerequisite closure | Not during planning |
| Which results can this format show? | Presentation plan and Markout | Rendered sections and shape | No new work |

Schema query never establishes effectiveness. Section selection never grants
producer authority. A probe result never chooses an output format. This table
is the canonical mapping when older command-specific documentation uses the
terms less precisely.

### Parsed intent

The CLI owns parsing and creates an immutable intent. The explicit or inferred
inspection surface (`Type`, `Member`, or `Commandless`), exact type names,
dotted type-or-member spellings, explicit member filters, overload ordinals,
digests, generic arity, section/category selectors, projection selectors,
discovery mode, and format remain distinct fields. A `type` surface with
`--member`
remains a type inspection with its existing type-view filtering semantics; the
filter does not silently reroute it to the member surface.

The intent records **capability-request provenance**, not pre-resolved
capability bits. Existing capability-bearing gestures include:

- exact, category, or glob section selection;
- the user's original verbosity;
- discovery mode and probe policy;
- explicit network, source-content, offline, or other policy flags.

Internal verbosity promotion is retained separately and never becomes request
provenance. After selector resolution binds stable sections to typed queries,
the progressive-disclosure policy determines which declared query
capabilities the gesture requests. For example, exact `Original Source`
selection may request `LocalPdbRead`, `PdbAcquire`, and `SourceContent`, while
detailed verbosity may request bounded symbol work but cannot request
`SourceContent` merely because code promoted the effective verbosity.

Conceptually:

```text
ParsedInspectionIntent
  InspectionSurface
  AddressIntent
    SourceIntent
    TypeOrMemberIntent
  SectionIntent
    Selectors
    DiscoveryMode
  ProjectionIntent
  PresentationIntent
  CapabilityRequestProvenance
```

Exact public type names for these records are deferred to implementation. The
shape, immutability, and ownership are the decision.

### Structural selection

Final structural selection resolves exact names, aliases, categories, and
globs against one identified catalog. The result retains:

- the catalog identity and version;
- selector provenance;
- resolved stable section identities;
- unresolved selectors and typed suggestions;
- whether the gesture selected the complete catalog;
- the required output shape constraints.

Structural selection does not inspect a target and does not decide whether a
section has data.

The inspection routes use four different catalogs:

| Catalog | Realized owner and model | Route states |
| --- | --- | --- |
| Assembly type list | `ApiTypeSectionDescriptors` over `ApiSurface` | No exact type supplied, type glob, failed exact lookup promoted to prefix browse, or platform prefix browse |
| Type/member list | `ApiMemberSectionDescriptors` over one `ApiType` | Resolved exact type, including the type surface with `--member` filters |
| Name-scoped overload inventory | `ApiMemberOverloadSectionDescriptors` over a resolved member set | Member filters without exact-target demand |
| Exact-member detail | `ApiMemberDetailSectionDescriptors` over one resolved member target | Exact selector or valid exact-target promotion |

A result from their temporary union is explicitly provisional and cannot be
used for:

- single-section validation;
- table, count, value, path, URL, print, or document-shape validation;
- required verbosity;
- producer demand;
- capability authorization.

Before final catalog selection, a typed **section-demand index** may classify
the raw section selectors. The index is generated from canonical semantic
section declarations, not inferred from catalog membership, descriptor
`CanRender`, or the current number of matches. It is keyed by inspection
surface and stable section identity and contains only aliases, category
membership, selector visibility, and required target shape (`Type`,
`MemberSet`, or `ExactMember`).

One stable identity has one canonical target requirement on an inspection
surface even when view adapters register it in multiple catalogs. A section
that can represent a member group, such as `Source Locations`, declares
`MemberSet`; selecting it does not promote a singleton group. A section that
semantically requires one member, such as a selected signature or source body,
declares `ExactMember` even if an inventory adapter can expose it after a
single-body-member predicate. Catalog declarations reference that canonical
semantic declaration. Conflicting target requirements for the same stable
identity are a catalog-construction failure; genuinely different semantics
must use different stable identities.

The index can answer whether an explicit exact name, category, or glob requests
an exact member. Overlapping matches coalesce by stable identity before their
requirements are joined in the `Type < MemberSet < ExactMember` lattice. It
cannot validate output shape, choose columns or fields, establish producer
demand, or authorize work. Bare/default selection and whole-catalog selectors
such as `@All` do not request exact-member promotion.

This preliminary classification is not structural selection against a union
catalog. It produces only typed demand constraints. After the target and final
catalog are known, the original selectors are resolved once against that
catalog for final validation.

### Target and member resolution

Except for static schema, the parsed source/address first lowers into a minimal
target-resolution query plan. The normal preflight authorizes that plan from
the explicit source intent and host policy. It may acquire the selected
package, platform, project, or local target and produce the base Metadata API
inventory needed for lookup. It cannot authorize PDB, source-content, or
section-analysis augmentation.

The resulting inventory is resolved through existing typed identity owners.
Address and selection resolution has four ordered phases:

1. resolve the complete positional spelling, fallback peel, positional type,
   and every qualified selector to canonical type candidates, then require
   every candidate to agree;
2. classify raw section selectors through the section-demand index without
   performing final section or shape validation;
3. normalize each member gesture as either an inventory filter or an exact
   target selector, then resolve it without consulting a section catalog;
4. choose the catalog from the inspection surface, resolved route kind,
   selector kind, and classified exact-member demand, then resolve section
   selection and shape exactly once against that catalog.

The implied positional member gesture is a constraint on every explicit member
gesture with the same canonical name. Each same-name pair is normalized:
identical components coalesce, a component present on only one carrier is
copied, and two different present values conflict. If several explicit
gestures share that name, the implied constraint is applied independently to
each before duplicates coalesce. Separate explicit gestures remain separate,
and distinct inventory filters form a set. If no explicit gesture shares the
implied name, the implied gesture remains standalone.

An **inventory filter** is set-valued. Each logical bare-name or glob filter
resolves independently to a `ResolvedInventoryFilter` carrying its provenance,
matched identities, or typed no-match diagnostic and suggestions. Every
logical filter must match before matched identities are deduplicated into a
`ResolvedMemberSet`; one valid filter cannot hide a miss in another. Generic
arity or kind refinements that do not establish uniqueness remain inventory
filters. An **exact target selector** carries the overload, digest, accessor,
or other exact discriminator required by `MemberTargetSelector`;
`MemberTargetResolver` returns one `ResolvedMemberTarget` or a typed
diagnostic.

Explicit selection classified as `ExactMember` demand may promote a member
surface's name-scoped inventory only after every inventory filter matched and
the deduplicated set contains one eligible member. A body or accessor
requirement still applies its own typed selection diagnostic. Classification
resolves exact names, aliases, categories, and globs through the demand index;
overlapping matches coalesce by stable section identity. Promotion chooses the
detail catalog, then the original selectors undergo final structural and shape
validation exactly once against that catalog.

Address resolution follows this matrix:

| Input state | Outcome |
| --- | --- |
| The complete type spelling resolves exactly | Select that type; do not peel an implied member |
| Complete type lookup misses and one legal trailing segment can be peeled | Resolve the prefix as the type and retain the suffix as implied member intent |
| Both an exact complete type and a prefix type/member pair exist | Exact complete type wins |
| Positional type and a qualified explicit member name the same canonical type | Continue to member-set combination |
| Positional type and a qualified explicit member name different types | Return a typed address-conflict diagnostic |
| Two qualified explicit member selectors name the same canonical type | Continue to member-set combination |
| Two qualified explicit member selectors name different canonical types | Return a typed address-conflict diagnostic |
| Implied and explicit refinements have identical components | Coalesce them into one logical selector |
| Implied and explicit refinements have complementary components | Merge the present components into one logical selector |
| Implied and explicit refinements have different present overload, digest, or generic-arity components | Return a typed selector-conflict diagnostic |
| Explicit `type` surface with no exact type, a type glob, or a prefix-browse result | Select the assembly-type-list catalog |
| Explicit `type` surface with a resolved exact type, with or without `--member`/kind filters | Select the type/member-list catalog and preserve type-view filter semantics |
| Explicit `member` surface with no member gesture | Select the type/member-list catalog |
| Explicit `member` surface with bare-name or glob filters | Resolve every filter, then deduplicate their union as `ResolvedMemberSet` |
| Any logical inventory filter resolves to no members | Return one typed aggregate retaining every missed filter and suggestion before union materialization |
| Member inventory resolves to one or multiple members without exact-member demand | Select the name-scoped overload-inventory catalog |
| Member surface with one exact target selector resolved successfully | Select the exact-member-detail catalog |
| Exact targeting is combined with multiple logical selectors | Return a typed selector/cardinality diagnostic |
| Exact-member demand accompanies one eligible member-surface inventory member | Promote that member and select the exact-member-detail catalog |
| Exact-member demand accompanies zero, multiple, or ineligible member-surface inventory members | Return a typed no-match or cardinality diagnostic |
| Commandless target resolution proves a targetless, glob, or prefix-browse type route | Use the assembly-type-list catalog |
| Commandless target resolution proves an exact type | Use the type/member-list catalog |
| Commandless target resolution proves an inventory or exact member | Use the matching member catalog |
| A requested section does not exist in the chosen catalog | Return a typed unresolved-section diagnostic |

Selector-bearing suffixes are parsed before comparison. Display spelling is not
used to decide canonical type agreement. A missing component is not a
disagreement with a present component; disagreement requires two different
present values on carriers that refine the same logical selector.

The resolution uses existing typed identity owners:

- type lookup resolves the exact type or returns a typed diagnostic;
- typed inventory filters resolve independently through a set-valued resolver
  before their successful matches form a deduplicated `ResolvedMemberSet`;
- `MemberTargetSelector` remains the exact-member question;
- `MemberTargetResolver` returns one `ResolvedMemberTarget` or a typed
  diagnostic;
- the plan retains inspection surface, per-filter outcomes, selector kind,
  exact target or inventory set, and classified target-shape demand before
  final structural selection.

Inspection surface, resolved route kind, and selector kind choose the active
section catalog; raw target count does not. A type gesture with `--member` stays on
the type/member-list catalog. A member bare name that resolves to one overload
remains an overload inventory unless canonical exact-member demand promotes
it. A type glob or prefix browse stays on the assembly-type-list catalog even
when it produces one row. Final selection and shape validation run once
against the chosen catalog.

Static schema discovery is the intentional exception: it performs no target
lookup. An explicit `type` surface with no exact address or with list syntax
reports the assembly-type-list schema; one with syntactic exact-type intent
reports the type/member-list schema without attempting a lookup-based prefix
fallback. An explicit `member` surface with a separate inventory filter reports
the overload-inventory schema, and one with an exact selector reports detail.
An explicit `member` surface with no member gesture reports the member
type-view projection over `ApiMemberSectionDescriptors`, preserving the route's
member-specific parser capabilities.

If the explicit member positional spelling can syntactically denote either a
complete type with no member gesture or a legal type-plus-implied-member peel,
static schema returns labeled `StructuralCatalogAlternatives`. The complete
type interpretation carries the member type-view projection; the peeled
interpretation carries overload-inventory or detail according to its syntactic
selector and canonical section demand. A temporary union of detail section
names into the member type-view cannot validate a selector or stand in for
these alternatives.

Commandless static schema is a structural route query over every view the
hidden router can choose, not only over command names or the four type/member
catalogs. A destination command is not a catalog identity: the package command
can render its package view, one embedded library, or an all-libraries
aggregation. The CLI therefore owns a closed structural-view registry:

| Structural view | Destination command | Static schema source |
| --- | --- | --- |
| Package inspection | Package | Package-view projection over `PackageSectionDescriptors` |
| Package single-library | Package, then library adapter | Package-library projection over `LibrarySections` |
| Package all-libraries | Package aggregation | All-libraries aggregate projection over `LibrarySections` |
| Direct library | Library | Direct-library projection over `LibrarySections` |
| Type list or exact type | Type | View projection over `ApiTypeSectionDescriptors` or `ApiMemberSectionDescriptors` |
| Member type view | Member | Member-route projection over `ApiMemberSectionDescriptors` |
| Member inventory or detail | Member | View projection over `ApiMemberOverloadSectionDescriptors` or `ApiMemberDetailSectionDescriptors` |

Each entry declares its syntax marker, precedence, destination command, view
mode, catalog identity, parser capabilities, and static schema projection. The
projection intersects the command-owned catalog with sections, fields,
columns, coordinates, and output shapes the route's parser and renderer can
actually reach. It never executes a section predicate or producer.

Package single-library is not the direct-library schema merely because both
use `LibrarySections`. Reachability is declared per section and shape, not
inferred from the presence or absence of an option family. Sections that work
with default query inputs, including ordinary `Performance:*` sections, remain
advertised. A section is excluded only when its own declaration requires a
coordinate, filter, or shape that the package route cannot supply. Package
all-libraries has its own aggregate schema. Its Markdown section set and its
row-capable section allow list are explicit, and row shapes include the
package/version/library/TFM identity columns added by the aggregate renderer.
Static schema selects the shape for the parsed output mode; it cannot advertise
a direct-library field or shape that the package route later rejects.

The declarations are shared by
`ArgumentPreprocessor`, `RouterTokenRewriter`, and the destination command's
post-parse structural classifier; a command rewrite cannot silently change the
view. Declaration order preserves the realized precedence for file forms,
explicit member selectors, package-scoped `--library`, package-plus-type
forms, and package-version forms. Slice 2 intentionally inserts
`--all-libraries` after package-scoped `--library` and before
package-plus-type/version routing. Both package-scoped markers select package
library view modes even though their static schema derives from
`LibrarySections`.

Syntax-only precedence selects one structural view when a marker proves it.
This includes explicit package-library gestures and direct `.nupkg --library`
preprocessing paths as well as hidden-router paths. A bare dotted target that
still requires platform, package, type, member, facade, or prefix lookup
returns `StructuralCatalogAlternatives` containing every syntactically
possible `[destination, view, catalog]` tuple. That includes package and direct
library alongside the applicable assembly-type-list, type/member-list, and
member catalogs; lookup-dependent prefix fallback is not silently discarded.

The static structural route precedes this proposal's type/member parsed intent.
A deterministic type/member view lowers into `ParsedInspectionIntent`; a
deterministic package or library view returns its command-owned schema; labeled
cross-command alternatives remain an outer structural result. Package and
library execution plans never enter the type/member plan merely because their
catalogs appeared in that result.

The alternatives are not a union and cannot satisfy single-section, shape,
producer, or authorization decisions. Each labeled alternative may carry its
own selector resolution or diagnostic, but one alternative's success cannot
be treated as a resolved answer for another. A selector that exists in only
one alternative does not prove that the target routes there. Static schema
must not pretend it learned which destination or interpretation exists.

Commandless structural intent is classified before hidden-router target
resolution. `ArgumentPreprocessor` may preserve the syntactic rewrite to
`router`, but it must retain the structural gesture. `RouterCommandDefinition`
must dispatch that typed structural intent directly to the structural-view
registry before `RouterTokenRewriter` can perform platform resolution, facade
classification, package existence checks, all-framework searches, package
acquisition, or type/member lookup. The resulting deterministic command-owned
catalog or labeled alternatives follow the same no-producer rule as
explicit-command static schema. This registry composes existing package and
library schemas and makes package-library static schema return before package
resolution or extraction; it does not move their planning or execution
ownership into the type/member path.

There is no `MemberSelectionNeedsFinalization` Boolean in the target model.
Code that requires resolved selection accepts only the resolved plan type.

### Producer execution plan

The resolved selection lowers into the typed query plan already defined by the
[inspection space](../inspection-space.md#query-plan). Each demanded producer
declares:

- typed inputs;
- prerequisite producers;
- conditional successors keyed by typed results;
- cost;
- allowed execution modes and probe eligibility;
- required capabilities;
- retained-resource lifetime;
- typed result and failure.

The planner computes a closed producer graph before preflight. A producer may
declare:

- unconditional prerequisites required on every path; and
- conditional successors keyed by a typed predecessor outcome.

Conditional successors model fallback without execution-time authority
expansion. For example, a bounded local-PDB producer may return
`LocalPdbAvailable` or `LocalPdbMiss`; only the miss edge reaches the PDB
acquisition producer. The graph contains both edges before execution.

Required capabilities, costs, execution modes, and probe policies close over
each possible path. They are not flattened into one mandatory union across
mutually exclusive paths. Preflight grants or denies every conditional edge in
advance and records that immutable disposition in the plan. A denied optional
edge does not deny an otherwise executable predecessor; if execution later
selects that edge, the recorded denial becomes the typed result and no new
authority is requested.

The host intersects producer requirements with the gesture's explicit
capability-request provenance before any producer runs. L3 captures provenance;
L2's typed section-to-query binding applies progressive-disclosure request
policy; L1 queries declare requirements; the host grants or denies the request.
A selected section cannot widen authorization after this preflight.

The query execution owner exposes one conceptual operation and preflight:

```text
BeginInspectionOperation()
  -> InspectionOperationContext(opaque-operation-identity)

Preflight(
    inspection operation context,
    parsed gesture capability-request provenance,
    resolved typed query plan,
    execution mode,
    host capability policy)
  -> PreflightedInspectionPlan | PlanDenied(typed-reason)

PreflightedInspectionPlan
  operation identity -> exact owning InspectionOperationContext
  section demand ->
    Executable(AuthorizedProducerClosure)
    | Unavailable(CapabilityNotRequested
                  | CapabilityDenied
                  | CostDenied
                  | ExecutionModeDenied
                  | ProbePolicyDenied)
  conditional edge ->
    AuthorizedSuccessor
    | DeniedSuccessor(typed-reason)
```

Only this preflight may mint executable closures, and the executor accepts only
the preflighted plan together with its live owning operation context. A
preflighted plan cannot be rebound to or reused by another top-level operation;
an operation mismatch or disposed context is rejected before outcome-cache or
producer access. Repeated section demand inside one operation may reuse that
plan and its completed outcomes. A plan-level denial applies to mandatory
target or common producer work. Independent section demands and conditional
successors retain their own authorized or denied disposition. L3 parses the
user's capability-bearing gesture, producers declare requirements, and the
host policy grants or denies them. Section names, `MemberOptions`, and
presentation state are not authorization inputs after lowering to the typed
plan. L2 contributes request provenance through a typed section/query binding;
it does not grant the capability.

This gives local-symbol read, symbol acquisition, and source-content access one
auditable rule:

```text
executable closure or conditional successor =
    progressive-disclosure policy requests every capability
        required by that closed path
        from the gesture provenance and resolved query binding
    AND the execution mode permits every producer on that path
    AND (the mode is not a probe
         OR the probe policy permits every producer on that path)
    AND the host capability policy grants every capability on that path
```

Cached or adjacent content does not bypass that rule. Availability is not
authority. Artifact-owner admission and query leases revalidate the authorized
closure's capabilities and source policy at content access; they are downstream
enforcement of the same authority, not a second grant mechanism. A local PDB
hit completes under `LocalPdbRead`; a miss may follow only a pre-authorized
`PdbAcquire` edge. An unrequested or denied acquisition edge returns its
recorded denial without network access.

Completed effective-discovery outcomes are local to the fresh
`InspectionOperationContext` and exact `PreflightedInspectionPlan` that
authorized or denied each section. Lookup happens only after current-operation
preflight. No `Applicable`, typed `Unknown`, or producer-failure result crosses
into another operation or plan merely because target, options, catalog version,
probe policy, and host grants match. Persistent producer evidence may be reused
only after the later operation independently preflights that producer and the
artifact owner revalidates access; the later plan derives a fresh section
outcome. This avoids replaying an authorized answer into a denying host,
pinning an authorized host to another operation's denial or transient failure,
and inventing a hash of host policy as a second authorization currency.

The shipped library-only `effective-v*` catalog remains the compatibility path
described in
[the section model](section-model.md#existing-library-effective-catalog). It is
not consulted by the planned type/member executor and does not weaken this
operation boundary.

### Discovery modes

Discovery mode is part of parsed intent and execution planning, not inferred
from a non-null `Discover` array.

| Gesture | Target resolution | Section producer execution | Capability effect |
| --- | --- | --- | --- |
| `-D --schema` | None | None | None |
| Type/member `-D` during compatibility migration | Minimal target-resolution plan | Cheap network-free applicability probes | Target acquisition plus cheap probe policy |
| Type/member `-D <section-or-category>` during compatibility migration | Minimal target-resolution plan | Network-free section-specific applicability/render-manifest probes | Target acquisition plus network-free probe policy; no `LocalPdbRead`, `PdbAcquire`, or `SourceContent` request |
| Future explicit effective discovery | Minimal target-resolution plan | Declared full applicability probes only | Target acquisition plus explicit full-probe policy |
| `-S <section>` render | Minimal target-resolution plan | Selected producer closure | Target acquisition plus explicit gesture capabilities |
| Verbosity render | Minimal target-resolution plan | Automatic base-section closure | Target acquisition plus behavior-safe defaults |

An effective probe and a render are distinct execution modes. A producer may
support both, one, or neither. A render-only or opt-in producer remains
structurally discoverable without being executed as a probe.

Every declared discovery probe returns one typed disposition:

```text
Applicable(schema)
ValidEmpty(stable-reason)
Unknown(stable-reason, structural-schema)
Failed(typed-producer-failure)
```

Discovery preflights each selected section's producer closure independently.
A missing request or capability, cost, execution-mode, or probe-policy denial
makes that section `Unknown` with the typed stable reason; it does not deny
unrelated eligible sections in the gesture. A producer failure after an
authorized probe started makes that section `Failed`. For render, a denied
demanded closure is a visible typed failure and the requested operation returns
non-success. This keeps policy denial distinct from producer failure and from
proven absence.

`ValidEmpty` requires a completed authorized probe that proved absence.
`Unknown` means the mode could not determine effectiveness, including when a
required capability was intentionally not requested; it retains structural
schema and reports the reason rather than presenting ordinary emptiness.
`Failed` remains a named section failure and produces a non-success exit.
Render-manifest observation preserves these dispositions and cannot convert
`Unknown` or `Failed` into `ValidEmpty` or an empty successful result.

In particular, named or category discovery of `Source Locations`, `Original
Source`, `Source Diff`, or another source-backed section on the type/member
path requests none of `LocalPdbRead`, `PdbAcquire`, or `SourceContent`. It may
describe structural schema or run a network-free metadata-only probe needed to
distinguish valid-but-empty from unknown. When that distinction requires a PDB
or source document, the result is
`Unknown(CapabilityNotRequested, schema)`, not valid-empty. This does not alter
plain library discovery: its declared bounded SourceLink-door probe may request
`LocalPdbRead`. If a future full effective-discovery gesture is allowed to
probe more, the producer declaration, probe policy, gesture provenance, and
host must all authorize it.

Type/member commands currently treat every non-schema `-D` as effective
discovery. The compatibility rows above preserve that producer scope during
the planning migration, including section-specific valid-but-empty probes.
They do not preserve the current accidental union of `Discover` selections
into render authorization: overload-qualified named/category discovery can
currently acquire PDB/source content and can convert a failed probe into empty
successful output. Slice 4 removes that escalation and empty-success path as
an intentional correctness change.

Converging type/member commands on the library command's structural/effective
split remains a separate user-visible transition. It must update the section
model, [schema-query realized mechanics](schema-query.md#effective-filtering),
progressive-disclosure guidance, command help, and valid-but-empty diagnostics
together. The planning migration does not silently perform that broader
transition.

### Presentation plan

Presentation consumes completed typed results and the resolved section set. It
owns:

- document, table, vector, or scalar shape;
- columns and fields;
- Markdown, plaintext, table, TSV, JSONL, JSON, or Mermaid format;
- empty-section and valid-but-empty diagnostics;
- verbosity and section order.

Presentation cannot cause a producer to run. A missing result is either:

- a planning defect, if the producer was required;
- a typed producer failure;
- a legitimate inapplicable or empty outcome described by the section
  contract.

It is never repaired by consulting `IncludeSections` and acquiring more data
during rendering.

The existing `RenderedSectionManifest` remains useful for observing which
fields, columns, and empty sections an already-populated view would render. It
is retained as a post-producer observation adapter, not as a producer-demand
owner. A section-specific discovery probe may invoke that adapter only after
its typed prerequisite closure has executed. The adapter's serializer receives
completed results and cannot open an index, acquire a PDB/source document, read
local symbol/source content, or start any undeclared producer. Existing
producer calls reached while building the manifest must move into the declared
probe plan before the adapter is considered compliant.

### Planning invariants

1. Selector resolution is deterministic for one inspection surface and catalog
   version.
2. Shape validation runs only on final resolved selection.
3. Static schema performs no target or producer work.
4. Effective discovery executes only probe-authorized producers.
5. Producer demand, capabilities, execution modes, and probe policy come from
   the same closed typed plan.
6. Presentation cannot widen producer demand.
7. Target, selection, producer, and presentation failures remain distinct.
8. A typed diagnostic is not converted into an empty selected set.
9. Conditional execution chooses only among branches whose authorization or
   denial was fixed by preflight.
10. An effective-discovery outcome is reused only within the fresh top-level
    inspection operation and exact operation-bound preflighted plan that
    produced it.

## Boundary two: validated metadata declaration projection

### Owner and scope

`ILInspector.Metadata` owns declaration validity and metadata-derived facts.
The operation is bounded and its image state is scoped to acquisition-owned
immutable byte generations and operation-local entries. It does not create a
repository-wide normalized metadata graph.

The declaration path supports ordinary ECMA-335 assembly metadata only.
The `AssemblyImage`-owned metadata-session factory calls the
MetadataPrimitives-owned `MetadataImageFormatClassifier` before obtaining or
exposing a `MetadataReader`, before row admission, and before declaration work.
Direct public/reusable `PEReader` entry points perform the same classification
before their own reader construction. Once classification returns supported,
the factory constructs the reader and session as one owner-bound operation; a
caller cannot pair an independently supplied classification result and reader.
The session rechecks its owner or lender liveness and supported admission before
using that reader.

The classifier uses the registered bounded metadata-root admission guard: from
the acquisition-owned `PEReader` it reads only the fixed ECMA-335 metadata-root
prefix and at most the declared 256-byte padded version field, then performs an
ordinal byte match for `WindowsRuntime` before the first null byte. ECMA-335
permits at most 255 bytes including the terminator and rounds the stored field
length to four-byte alignment, hence the 256-byte read ceiling. This is the same
case-sensitive version discriminator SRM applies before optional WinRT
projection, without constructing a reader whose table initialization may scan
rows. It does not use the options-dependent `MetadataReader.MetadataKind`
property.

`WindowsMetadata` and `ManagedWindowsMetadata` are unsupported project inputs,
not malformed ECMA-335. The classifier needs no projected reader, mscorlib
lookup, handle correspondence, projected-accessor fallback, or WinMD
compatibility adapter. A malformed raw metadata root remains a distinct
malformed-input result. The guard does not parse stream headers, heaps, table
headers, row counts, or rows and is not a second metadata reader. Supported
images proceed to the ordinary SRM reader, which remains responsible for the
rest of metadata-root and stream validation.

The classifier returns `NoMetadata` without requesting a metadata block when
`PEReader.HasMetadata` is false. Acquisition and query owners preserve their
existing typed no-metadata boundary; they do not translate that result to
unsupported Windows Metadata, malformed metadata, or empty metadata
projection. Obtaining the metadata block for the other result arms may
materialize the complete metadata directory for a lazy `PEReader`. That
acquisition-owned byte cost is distinct from classifier work and remains
visible; after the block exists, the guard's work and allocation are bounded by
the fixed prefix and 256-byte field and cannot scale with stream, heap, table,
or row content.

A metadata directory that cannot be mapped into a block and a block shorter
than the fixed root prefix produce typed malformed-root results. An I/O failure
while a lazy owner materializes the block remains the acquisition owner's typed
failure and is not relabeled as malformed metadata. Direct APIs without a
failure union map the malformed-root arm to `BadImageFormatException` with
bounded, non-artifact text; query owners preserve that mechanism in their typed
malformed-input result.

A Metadata declaration session is created from:

- one owned or borrowed `AssemblyImage` lease, including its liveness check,
  acquisition-owned image generation, current `PEReader`, and that reader's
  `MetadataReader`;
- the operation's shared work, item, text, and decode context;
- access to entry-owned immutable declaration-fact caches keyed by handle, fact
  kind, and complete generic, resolution, and participating-generation context;
- the operation's stable validation policy;
- typed failure construction.

The session exposes declaration operations. It does not expose its PE reader,
metadata reader, borrowed memory, or mutable budget object to higher layers.
`MetadataOperationContext.AdmitImage` is the sole metadata-row charging
authority. Session construction calls it, and the operation context keys
admission by reference identity of an opaque, acquisition-minted
`MetadataImageGeneration` so the current
`ApiSurfaceExtractor.ExtractionBudget` charge can be relocated rather than
duplicated inside that operation. The generation establishes only that readers
expose one retained immutable byte generation; it grants no provenance,
validity, or content trust.

The generation is not artifact identity, `AssemblyAcquisitionRegistration`,
assembly identity, workspace participant identity, provenance, or
correspondence, and it is never serialized or used for semantic matching. A
registration identifies one canonical acquisition descriptor and can outlive a
content open; the generation is minted only for the exact retained snapshot or
live opened owner whose bytes may be reused.

Acquisition mints a fresh generation for each independent open and binds it to
the owner rather than accepting an independently supplied token/reader pair.
An `AssemblyImageSnapshot` retains the generation with its immutable bytes, so
each `AssemblyImage.Open(snapshot)` may create a new `PEReader` without turning
one workspace participant into many charged images. A borrowed
`AssemblyImage` copies the lender's generation. A `MetadataReader`, `PEReader`,
owned or borrowed wrapper, path, MVID, or content digest is not the generation;
two independent acquisitions receive different generations even when they
expose the same bytes.

The acquisition layer is the sole content-digest authority for its immutable
artifact-content snapshot. When a consumer requests a digest, the owner
computes and may memoize it from those retained bytes, charging the requesting
inspection operation that causes the one cold linear pass. Later reuse of that
memoized content fact performs fresh authorization but no second hash.
Projecting the content into an `AssemblyImageSnapshot` preserves the same bytes
and digest while minting the image generation; the digest is durable content
evidence, not the generation identity. Any persistent derived-result cache
using it must obtain the digest, format result, producer output, and publication
payload from that one retained content snapshot; APIs reject independently
supplied digest/snapshot or digest/result pairs. Hashing a mutable path before
and after a separately opened inspection is not equivalent because a
W-to-S-to-W replacement defeats the bracket. `MDP017` pins SHA-256 for the
library effective-catalog key; this declaration does not require every artifact
consumer to compute a digest eagerly.

The operation context shares the admission result, immutable semantics index,
and immutable declaration facts or typed failures through a context-owned
`MetadataImageEntry` mapped by generation reference identity, not through the
session object or a cache object retained by the snapshot. Each owned or
borrowed `AssemblyImage` wrapper has its own `MetadataDeclarationSession`,
current reader, and liveness check. Sessions over the same generation consult
one entry in that operation only after validating their own owner or lender.
Every fact key names the fact kind, subject handle, complete generic and
resolution context, and reference identity of every participating image
generation. A request whose meaning depends on a consumer inclusion decision
is not a shared fact and remains projection-local.

The entry retains only owned immutable values or typed failures, never a
`MetadataReader`, `PEReader`, borrowed block, pointer, lease, or mutable budget.
For a fact whose typed generic or resolution context names other generations,
the projection coordinator constructs an ephemeral
`MetadataFactAccessContext` from one specific current
`MetadataDeclarationSession` per participating generation and passes it to the
subject session's fact operation. The subject session validates that every
supplied session belongs to the same `MetadataOperationContext` and carries the
expected generation. Missing participation returns typed
`ResolutionContextUnavailable` before cache lookup; an operation or generation
mismatch is rejected as an invalid request, and a dead owner or lender
preserves the liveness failure. Multiple live wrappers for one generation are
unambiguous because the coordinator chooses the current wrapper for that
request.

The typed generic or resolution context enumerates the complete participating
generation set before lookup or decode. The ephemeral access context must match
that set exactly; a missing generation is unavailable, and an extra or
mismatched generation is invalid rather than silently changing the fact key.

The subject generation's `MetadataImageEntry` retains and charges the completed
fact or failure; other participating generations contribute key and liveness
context but do not retain duplicate values. Each supplied session checks its
owner or lender immediately before both a cache observation and a cold decode,
and its live lease covers materialization. The cold path charges the operation
before publishing the completed value or failure. Consequently separate full,
summary, and focused sessions over one generation can reuse one charged fact
without a cached result reviving a disposed owner or lender. Neither the
operation context nor an entry retains the ephemeral access context or its
sessions. Entry disposal releases all retained facts and indexes at operation
end.

Entry creation, image admission, association-index publication, and
declaration-fact publication are atomic within one `MetadataOperationContext`.
For each fact key, one caller owns cold decode and charges before publishing one
immutable value or typed failure; concurrent callers join that same completion
and perform their own liveness check before observing it. A same-chain recursive
request for the in-progress key returns typed `FactDependencyCycle` rather than
blocking itself or starting a second decode. The coordination mechanism exposes
no threading requirement to consumers: multithreaded hosts exercise contention,
while single-threaded Browser/Wasm takes the same uncontended contract.

Each distinct generation in a multi-image operation receives one cumulative
`MaxMetadataRows` charge. Admission is unconditional: a compatibility caller
without a configured ceiling uses an explicit `Unbounded` policy while still
recording the row sum, but every product entry point must supply a finite
central policy before the slice-6 cutover. The safety policy is
operation-scoped and immutable: sessions cannot supply a different policy
while sharing image state, and a transitional attempt to mix finite and
`Unbounded` policies in one operation is rejected before cache lookup.

One top-level CLI inspection, query, or invocation of a workspace query
coordinator creates one `MetadataOperationContext` and threads it through every
declaration session and snapshot-backed `UseAssemblySession` callback it
starts. Compatibility single-call APIs create an ephemeral context for that
call. The coordinator disposes the context when the top-level operation ends,
releasing all context-owned image entries, declaration facts, and retained
indexes. A later operation over the same persistent snapshot sees the same
byte-generation token but maps it into a fresh entry, repeats admission and
charging under its own policy, and cannot observe the earlier operation's
result or rejection.
The number and retained bytes of entries remain bounded by whole-image
admission and the declared fact/index retention budgets; a disposed session
leaves no `PEReader` in an entry, while a later session in the same operation
over the same immutable snapshot may reuse immutable facts and the neutral
semantics result through its generation.

### Validation stages

Every full, summary, or focused declaration projection uses the same stages in
the same order:

```text
1. Validate addressed row and declaring context.
2. Decode bounded admission facts needed for eligibility.
3. Validate the bounded dependency closure needed for admission.
4. Apply the consumer's explicit inclusion policy to the root declaration.
5. Lazily decode retained rich signatures, attributes, and relationships.
6. Validate retained aggregate semantics and materialize the requested projection.
```

Stage 1 includes validity that cannot be hidden by filtering, such as a
top-level TypeDef with nested visibility or an invalid declaring chain.

Stage 2 validates and charges every bounded admission probe needed to decide
whether a row can belong to the requested surface. Those probes include:

- accessibility and method/field flags;
- bounded names used by compiler-generated or reserved-name filters;
- bounded attribute type identities required by an inclusion policy;
- cheap accessor flags such as static, virtual, abstract, final, and new-slot.

Admission probes charge names and attribute type segments before SRM
materializes them. They do not decode rich attribute values or every signature
and `MethodImpl` relationship.

Stage 3 starts with the
[`ILInspector.MetadataPrimitives` lossless-row
boundary](../metadata-primitives.md#lossless-methodsemantics-row-boundary).
After whole-image row admission, `MethodSemanticsRowReader` reads one physical
association row at a time, records the visit, and charges the distinct retained-
association budget before retaining handles or materializing an `Other`
collection. The mechanical result preserves row identity, raw semantics bits,
a bounded MethodDef handle, the Property/Event association, and observed
association ordering; malformed layout, coded-index values, or row bounds
produce typed mechanical rejection.

Metadata then owns the semantic census for the requested property or event.
Each row must contain exactly one legal role for its association kind and
target a method on the aggregate's declaring type. Nonmonotonic association
ordering rejects the census as malformed Metadata, while the same physical
ordering with the sorted bit clear is rejected earlier by SRM reader
construction. Duplicate getter, setter, add, remove, or raise roles are rejected
rather than collapsed by last-write-wins projection. Only the validated bounded
rows may construct the typed accessor aggregate.

`PropertyDefinition.GetAccessors()` and `EventDefinition.GetAccessors()` are
not valid census inputs for untrusted metadata: those SRM convenience
projections allocate the complete `Other` array before returning, collapse
duplicate standard roles, and discard unrecognized combined semantics flags.
Consumers must not call them before the shared census or substitute their
lossy result for the typed aggregate.

The primitive remains SRM-backed and operates over a borrowed immutable view of
the already admitted metadata image. It uses only the narrow public SRM layout
surface registered by
[bounded metadata traversal](bounded-metadata-traversal.md#lossless-row-exception)
and retains no pointer or memory ownership beyond the caller's lease. A
Metadata owns a neutral `MethodSemanticsAssociationSession` that calls
`AssemblyImage.EnsureAlive()` immediately before every cold
`MethodSemanticsRowReader` pass and only then supplies that image's `PEReader`.
It is the sole product invocation owner; declaration projection, Metadata
scanners, CSharp, Decompiler, and CLI code consume its neutral immutable rows
rather than calling the primitive. Direct leaf calls remain only in
MetadataPrimitives boundary tests.

The association session is created from the same owned or borrowed
`AssemblyImage` lease and `MetadataOperationContext` as the consuming operation.
It owns no declaration inclusion or validity policy. Each association session
checks its own image and lender liveness before consulting or returning the
operation cache, so a completed index cannot turn a disposed owner or borrow
into success. The operation retains only the completed immutable neutral-row
index, which owns no borrowed memory. No cached value is observable without a
current association session's successful liveness check. Whole-image admission
charges declared metadata rows once; the pass records work without debiting
that same row budget again, and separately charges retained associations before
adding them to the index. That distinct budget protects retained bytes and may
reject every dependent property/event projection even when the image passed
its broader row ceiling; no unindexed streaming fallback is allowed. A
same-operation, same-generation entry reuses that already charged immutable
result or typed rejection and never crosses either boundary. The generation is
not a lease: retained handles can be interpreted only through a current live
reader carrying that generation, so cached success cannot bypass the session
liveness check. No aggregate result is released until the primitive reaches
the physical end of the table or returns rejection; an early association range
cannot prove completeness because a later out-of-order duplicate may exist.

Stage 3 then validates only dependencies required to decide admission of the
root declaration:

- a property composes the stage 1-2 results of its getter, setter, and every
  `MethodSemanticsAttributes.Other` method before its own inclusion decision;
- an event composes the stage 1-2 results of its add, remove, raise, and every
  `MethodSemanticsAttributes.Other` method before its own inclusion decision;
- an accessor excluded as a standalone method still participates in the
  dependency closure of a retained aggregate;
- a possible explicit implementation projects a bounded `MethodImpl` identity
  only when row-local admission facts cannot decide its inclusion;
- a row already excluded by bounded admission facts does not project unrelated
  signatures or relationship floods.

Stage 4 applies the consumer's explicit inclusion policy to the root using
those bounded admission facts. It does not independently filter dependency
rows that the root still needs.

Stages 5 and 6 run only for retained roots. They decode rich retained
signatures, attribute values, full `MethodImpl` targets, body facts, and
aggregate consistency/slot semantics. Every retained declaration uses the same
validators regardless of projection.

This ordering rejects malformed addressed declarations while preserving the
cheap-filter-before-expensive-projection safety property.

This six-stage declaration admission decision tree is normative per root
declaration and its bounded admission-dependency closure. A consumer may erase
fields after stage 6, but it may not reorder, skip, or independently
reimplement the admission stages. The validation matrix gate must cover:

- a malformed directly addressed row;
- a valid excluded row with hostile expensive relationships;
- a compiler-generated or reserved-name exclusion whose name/attribute probe
  is charged;
- a public property/event with a non-public accessor needed for aggregate
  admission;
- a property/event with zero, one, or multiple `Other` semantic methods,
  including malformed and over-budget dependencies;
- duplicate standard roles, invalid combined role flags, dangling method
  handles, and cross-declaring-type associations;
- a retained row whose signature is rejected;
- an aggregate with one rejected accessor;
- a valid retained declaration that produces an empty presentation section.

### Shared declaration facts

The exact implementation types are deferred, but the facts have one Metadata
owner:

| Fact | Required distinctions |
| --- | --- |
| Type visibility | Top-level versus nested context, declaring-chain validity, effective visibility |
| Member accessibility | Valid raw accessibility and projected API accessibility |
| Signature status | Complete, degraded with typed reason, or rejected |
| Accessor aggregate | Validated raw association-row identity; getter/setter/add/remove/raise and every `Other` identity; per-semantic accessibility, staticness, virtuality, abstraction, final/new-slot shape, and body presence |
| MethodImpl relationship | Interface declaration, reused interface slot, reused class slot, new slot, unresolved, or rejected |
| Explicit implementation | Proven target identity and aggregate membership, not member-name inference |
| Safety accounting | Consumed work and retained text charged through the operation context |

The Metadata owner exposes a closed shared-fact declaration catalog. Each
entry has a stable fact identity, typed result or rejection, prerequisite fact
identities, charged safety dimensions, and any permitted post-validation
erasure. Projection entry points submit typed fact-request sets; the catalog
derives which projections request each fact rather than trusting a manually
maintained consumer list. A fact requested by more than one projection is
computed by the same owner and compared by declaration identity. Set equality
between registered projection requests and actual fact consumption fails on
an undeclared request or stale registration; projection adapters cannot
substitute display text, raw flags, or a reduced fact model.

Raw Boolean combinations such as `Virtual && !NewSlot` remain metadata facts,
but consumers do not infer semantic slot ownership from them. Metadata issues
the relationship classification after validating the declaring type and
`MethodImpl` target.

### Projection parity

Full extraction, summary extraction, and focused queries are projections over
the same validated declarations.

For the same input and inclusion policy:

- a declaration accepted by one path cannot be rejected as malformed by
  another;
- a declaration rejected as malformed cannot appear as ordinary absence in
  another;
- summary counts derive from the same accepted declaration identities as the
  full surface;
- every shared fact requested by more than one projection has the same typed
  value or typed rejection for that declaration identity;
- focused queries may choose a whole-query exception boundary, but the
  underlying rejection kind and rule are the same;
- reducing output fields does not skip validation needed for identity,
  inclusion, or count correctness.

Summary is allowed to avoid requesting rich display-only facts and to erase a
declared shared fact after parity has been established. It is not allowed to
own a reduced validity model or compute an alternate value for a shared fact.

Focused queries are allowed to inspect non-public declarations when requested.
They are not allowed to accept context-invalid metadata because it is
non-public.

### C# representability

`ILInspector.CSharp` consumes Metadata-owned facts and decides whether they can
be spelled as C#. It does not reopen metadata or reconstruct relationship
semantics from display strings or generic Boolean flags.

Examples:

- interface-declared explicit implementations and re-abstractions consume an
  interface-slot classification;
- class or struct implementations that reuse a base-class slot consume a
  class-slot classification;
- property/event rendering consumes one validated accessor aggregate;
- init-only spelling consumes a validated modifier classification;
- metadata fallback consumes complete contained identity and signature facts.

Metadata validity and C# representability remain different outcomes. Valid
metadata may be unrepresentable in C# and require a typed fallback. Invalid
metadata is not converted into a C# fallback that looks authoritative.

The CSharp boundary returns a typed representability outcome:

```text
Representable(declaration)
FallbackRequired(stable-reason, ContainedTypeDeclaration | ContainedMemberDeclaration)
Degraded(signature-status, bounded-nonauthoritative-evidence)
Unavailable(metadata-declaration-failure)
```

Complete declarations may become `Representable` or `FallbackRequired`.
`FallbackRequired` requires complete contained identity and declaration facts.

A contained type declaration identifies its canonical namespace/nesting/name,
kind and generic arity, accessibility and modifiers, generic parameters and
constraints, base-type identity, interface identities, and kind-specific facts
such as enum underlying type or delegate signature. Members retain their own
representability outcomes; the type payload must not erase a valid base,
interface, modifier, or other header fact merely because C# cannot spell it.

A contained member declaration identifies its declaring type, member kind,
canonical member identity, generic arity, return or value type, parameter types
and modifiers, and accessor identities and accessibility where applicable. It
also retains the Metadata-owned declaration semantics consumed by ordinary C#
rendering: declaration accessibility; static, abstract, virtual, override,
sealed, readonly, const, unsafe, async, extension, and explicit-implementation
semantics where applicable; generic constraints; attributes; constant and
default values; and the complete accessor aggregate. It retains enough
signature information to distinguish overloads and indexers; dropping
parameters, collapsing accessor identity, or discarding declaration semantics
is not a fallback.

Standard C# property/event syntax can represent getter, setter, add, and remove
accessors. Metadata raise and `Other` semantic associations remain typed facts
but have no corresponding C# accessor syntax. A retained aggregate containing
either returns `FallbackRequired` with every associated semantic method
identity and fact; it cannot erase the association by leaving the methods as
unrelated standalone declarations. Projection policy may additionally expose a
semantic method as a method row, but that does not replace the aggregate fact.

For both type and member declarations, the fallback fact set is derived from
the normal representable renderer's typed Metadata fact requests. Set equality
is required after subtracting only explicit, named erasures from the shared
fact catalog. An erasure must be justified as presentation-only and occurs
after the representability outcome is fixed; adding a fact to ordinary C#
rendering without adding it to fallback or declaring the erasure fails the
gate.

Fallback rendering treats every artifact-authored name and signature fragment
as inert data through the existing
[InertText](inert-text.md) boundary. Identity and comparison use the original
typed facts; presentation applies the sink's closed `TextPolicy` and carries
the result as `InertString`. Because that type records that a policy was
applied, not which policy, every fallback sink calls
`EnsurePermitted(its-exact-TextPolicy)` immediately before unwrapping and
format-specific escaping. A `Prose` value cannot enter a `Field` sink without
that tightening.

`VisualEncoder` re-spells refused scalars in its canonical, total, injective,
lossless, and invertible form. Removal, replacement, or a fallback-local escape
vocabulary is forbidden because it can collapse distinct declaration
identities. Format-specific Markdown, JSON, TSV, or other structural escaping
runs after visual encoding and does not replace it. Strict diagnostics do not
echo the artifact payload. `MDP015` is the gate for contained fallback
identity, signature discrimination, and inert rendering.

A Metadata declaration retained with `SignatureDecodeStatus.Degraded` becomes
`Degraded`; CSharp may preserve its bounded diagnostic evidence but cannot
render the placeholder shape as authoritative C# or metadata fallback.
Rejected signatures and declaration failures become `Unavailable`.

`Unavailable` also covers compatibility or persisted inputs that carry a
Metadata failure; a live validated declaration path should normally stop
before calling CSharp with invalid input. The exact public type name is
deferred, but these arms and their non-success semantics are required. A
Boolean flag combination, empty string, or metadata-looking fallback is not the
outcome contract.

Strict failure messages identify the violated rule and caller-supplied
coordinate. They do not quote artifact-authored names or signature text.

### Budget and cache integrity

One central Metadata safety-policy declaration enumerates every budget
dimension and every declaration-fact request that charges it. One
operation-scoped projection budget reaches every nested name, signature,
custom-modifier, accessor, and `MethodImpl` decode required by the projection.
An undeclared local ceiling is a contract violation.

The owner charges before materialization. A cache may avoid repeated decode
work only when:

- its key includes every semantic decode context;
- the retained value or typed failure is owned by the current operation's
  generation-scoped `MetadataImageEntry` and was already charged there;
- every participating image owner or lender is live before lookup and cold
  decode;
- cache reuse cannot turn a previously rejected operation into success;
- failure results are cached with the same context as successful results.

Full, summary, and focused projections may request different retained fields.
They do not receive different hostile-input ceilings for equivalent work.
Equivalent work means the same metadata root and acquisition-minted
`MetadataImageGeneration` reference identity in one operation, generic decode
context, inclusion
policy, admission dependency closure, and requested shared fact. Its
per-dimension threshold, consumed work through that fact, and rejection rule
are equal across projections. Additional retained fields may charge separately
declared dimensions and are not required to produce equal total operation
counters.
These cache and equivalent-ceiling properties are unverified until `MDP009`
lands; cache-key tests alone do not prove ceiling parity.

### Failure mapping

Metadata returns typed declaration failures with:

- mechanism;
- stable rule identifier;
- subject handle or token when safe;
- bounded non-artifact detail;
- consumed-work counters when relevant.

Consumers map that result without changing its meaning:

| Consumer | Mapping |
| --- | --- |
| Full extraction | Record a row/type inspection failure and continue independent rows |
| Summary extraction | Record the same failure and exclude the rejected identity from counts |
| Focused query | Return a typed failure or throw at its existing whole-query boundary |
| C# projection | Preserve valid-but-unrepresentable facts; do not receive invalid declarations as ordinary members |
| CLI | Render the failure and return non-success when the requested operation cannot complete |

No path maps rejection to a normal empty collection, default accessibility,
ordinary `set`, or plausible metadata fallback.

### Metadata projection invariants

1. Context validity is checked before a declaration is admitted.
2. Expensive projection occurs only after bounded cheap eligibility checks.
3. Every retained declaration uses one shared validator.
4. Summary and full counts are derived from accepted identities.
5. Focused queries do not weaken validity.
6. CSharp consumes typed semantics instead of reconstructing them.
7. Decode accounting is operation-scoped and transitive.
8. Failure detail contains no artifact-authored text.
9. MethodSemantics rows are charged and validated before convenience
   projection, role collapse, or collection materialization.

## How the boundaries compose

The resolved inspection plan asks Metadata for typed declaration projections.
Metadata does not know section names, verbosity, or output format.

```text
ResolvedMemberInspectionPlan
  sections -> typed query definitions
  target   -> Metadata declaration address/selector
  policy   -> inclusion and capability-request provenance

PreflightedInspectionPlan
  resolved plan + query requirements + mode + host policy
  -> per-section executable/denied closures
     + pre-authorized/denied conditional successors

MetadataDeclarationSession
  address + inclusion policy + requested facts
  -> validated declarations or typed failures

Section adapters
  validated results + presentation plan
  -> Markout view models
```

The top-level coordinator owns the planning-layer
`InspectionOperationContext` and, when declaration work is required, one
Metadata-layer `MetadataOperationContext` for the same invocation lifetime.
They are sibling layer-owned state, not aliases: the former bounds
authorization-dependent section outcomes, while the latter bounds metadata
admission, facts, and retained indexes. Neither context, its identity, nor an
image generation grants authority in the other layer.

The plan may request a summary projection, full API projection, or one focused
declaration. Those are result shapes, not separate validity implementations.

## Disposition of the paused implementation stack

The current implementation branches are evidence and a source of tests, not
the design authority.

| Existing work | Disposition |
| --- | --- |
| Compiler-produced and hostile metadata fixtures | Retain and move to the owning boundary gates |
| Typed member selectors and resolved member targets | Retain where they match the currency contracts |
| Local selector finalization flags and provisional option mutation | Replace with parsed and resolved plan types |
| Local source/PDB authorization checks derived from `IncludeSections` or the union of `Discover` selections into requested sections | Replace with producer-plan authorization |
| Render-manifest effective discovery | Retain for post-producer field/column/empty observation; move every producer call into a declared probe plan |
| `LibraryCommand`'s cross-process `effective-v*` successful catalog | Retain as the bare `library -D --effective` compatibility cache for package and platform routes, but make direct local-file routes bypass persistent lookup and publication and recompute from a fresh retained image in each tool run. Replace the persistent routes' resolved-path/content-hash/`sl0`-or-`sl1` predecessor key. The slice-5 successor subject freezes the resolved path, acquisition-owned immutable assembly-content digest, typed `LibraryCatalogRouteEvidence` for every route fact consumed by discovery, and typed `LocalSymbolDiscoveryEvidence`: `None`, or an owner-minted identity containing the retained identity-validated PDB digest, discovery-relevant provider/provenance, and SourceLink effectiveness. Lookup, cold production, and publication use that one subject; no post-production evidence may re-key it. Do not expose the catalog to the planned type/member executor or treat it as authorization. Apply the repository-wide persistent-cache cutover rule: classify retained assembly bytes before lookup and select a successor category so no pre-classifier, bracket-hash-mislabeled, route-aliased, or Boolean-PDB-keyed predecessor entry remains eligible; supported package/platform inputs recompute and repopulate that category. Replace the pre/post mutable-path hashing tracked by #3478, make every transitive assembly/PDB consumer in each cold path consume the corresponding retained content, and apply one finite 64 MiB portable-PDB retention budget before copy, hash, or reader work across every provider. A future library typed-preflight migration must convert the catalog to authorization-independent producer evidence or remove it |
| `ArgumentPreprocessor`, `RouterCommandDefinition`, and `PackageCommand` structural routing | Retain syntactic routing, but replace command-only dispatch with the shared structural-view registry and move static classification before acquisition in slice 2 |
| `ApiCommand.RunPreamble` and `ApiMemberSectionPipelines` static member catalog selection | Replace the provisional selectable-section union with explicit member type-view, inventory, and detail registry entries plus labeled dotted-tail alternatives in slice 2 |
| `ApiSurfaceExtractor` and accessor-bearing `MetadataDeclarationQuery` calls to `GetAccessors()` | Replace every SRM convenience-accessor read in those files with the neutral `MethodSemanticsAssociationSession` and Metadata-owned semantic census, including non-admission compiler-generated-name heuristics; replace reader-only `GetProperty` and `GetTypeSurface` entry points with session-backed queries |
| Reader-only `ApiSurfaceExtractor.Extract`, `ExtractSummary`, and `ExtractBounded` entry points and their production callers | Make the extraction core consume `MetadataDeclarationSession` plus its finite `MetadataOperationContext`; delete the bare-`PEReader` entry points rather than fabricating liveness. `AssemblyInspectionSession` supplies its owned image, `PdbContext` uses its existing genuine borrow, and `AssemblyReader` plus path/stream callers such as `tsbindgen` open an owned session. Migrate Decompiler `MetadataSource` API-surface plumbing in slice 6 to an actual owner-backed image/session without yet moving its Decompiler-owned accessor policy, which remains slice 8. Test callers use an owned test image/session rather than preserving a production reader-only seam. |
| `ExtensionMethodScanner`, `OpenTelemetryScanner`, `MemberBodyProducer`, and `MethodDefinitionFacts` calls to `GetAccessors()` | Preserve each consumer's semantic policy but migrate its mechanical association lookup to the neutral Metadata-owned `MethodSemanticsAssociationSession`, which is backed by `MethodSemanticsRowReader`; these paths share operation admission and liveness mechanics but do not become consumers of Metadata's declaration model |
| `tools/DecompilerHarness` calls to SRM `GetAccessors()` | Treat the non-packable harness as test orchestration, not production, but grant no directory-wide exemption: `MDP013` owns an exact file/enclosing-member/occurrence-count allow list and fails stale or unlisted entries; retain only calls whose SRM result is compared with product output as an independent oracle or used solely to address a product test input whose assertion depends only on product output; remove or migrate calls that supply expected accessor structure or construct, normalize, repair, or substitute for an artifact later compiled or measured as product evidence |
| Test-project calls to SRM `GetAccessors()`, including `FidelityCheckGeneratedFilterTests` | Apply the same `MDP013` exact call-site classification as the harness: retain comparison-only independent SRM oracles and address-only test-input selectors, fail stale or unlisted entries, and remove or migrate calls that supply product evidence; reflection `PropertyInfo.GetAccessors` is outside this SRM closure |
| Shared Metadata validators already used by every projection | Retain |
| Validators duplicated across full, summary, focused, or C# paths | Move to the Metadata declaration owner |
| C# fixes based on Metadata-owned typed semantics | Retain |
| Type-shell `null` or omission used as a representability result | Replace with a typed contained type-declaration outcome in slice 7 |
| C# inference from raw flag combinations or display text | Replace |
| Exact range-diff and real-artifact canaries | Retain as migration evidence |

Neither stack PR should be declared architecturally ready merely because its
current head is green. After this design is accepted, each implementation
commit must be classified as retained, relocated, replaced, or dropped.

## Migration plan

Each slice must carry one behavior claim and be independently mergeable, but
the slices are strictly ordered. A compatibility adapter may preserve existing
behavior between slices; it must reject an unsupported mixed state rather than
silently running old and new semantics for equivalent requests.

### Slice 1: characterize the current boundaries

Depends on: none.

- Add a single matrix test that records discovery mode, active catalog,
  producer demand, and capability authorization.
- Enumerate every realized package, package single-library, package
  all-libraries, direct-library, assembly-type-list, type/member-list,
  member type-view, overload-inventory, exact-member-detail, and hidden-router
  route in that matrix.
- Add parity fixtures that run the same declarations through full, summary,
  and focused projections.
- Make no behavior change.

Exit gate: the characterization fails when any currently observed execution
path is absent from the matrix.

### Slice 2: introduce parsed and resolved plan types

Depends on: slice 1.

- Parse type/member gestures into immutable intent.
- Retain explicit/inferred inspection surface and classify section selectors
  through the canonical semantic-section demand index.
- Move commandless structural classification ahead of
  `RouterTokenRewriter`; static schema must complete before any platform,
  facade, package-existence, all-framework, type, or member resolution.
- Compose package, package-library, direct-library, and type/member
  command-owned static catalogs through the structural-view registry; return
  labeled alternatives when syntax alone cannot select one.
- Make explicit package `--library`/`--all-libraries`, commandless equivalents,
  and direct `.nupkg --library` preprocessing return their route-specific
  projections over `LibrarySections` before package resolution or extraction.
- Derive each projection's sections, fields, columns, coordinates, and output
  shapes from per-section input requirements and the route's parser and
  renderer declarations; do not infer reachability from option-family names.
- Register explicit `member` with no member gesture as a member type-view over
  `ApiMemberSectionDescriptors`.
- Replace explicit-member dotted-tail provisional unions with labeled complete-
  type and peeled-member alternatives.
- Apply query syntax to each surviving interpretation rather than using it to
  erase target ambiguity. For example, a Body Shapes predicate keeps the type
  alternative and promotes only the member alternative to exact-member detail.
  An interpretation rejected by its command's options contributes a typed
  diagnostic, not schema authority; another command's acceptance cannot make
  it valid.
- Resolve the active catalog only after target/member resolution.
- For non-static dotted ambiguity, reject selectors absent from every candidate
  catalog before acquisition, but defer selectors valid in at least one
  candidate until target/member resolution selects the active catalog. This
  intentionally replaces a provisional-catalog diagnostic when the same
  selector is valid for another interpretation.
- Move shape validation to the resolved plan.
  Partial provisional selection success does not settle the target catalog or
  authorize a catalog-dependent cardinality check.
- Preserve current address precedence and all other diagnostics for non-static
  execution through a compatibility adapter except for the declared ambiguity
  and `--all-libraries` corrections.
- Intentionally replace commandless static-schema resolution notes and
  target-chosen catalogs with deterministic syntax-only catalogs or labeled
  alternatives; update command help and compatibility tests in this slice.
- Intentionally add target-free static schema for package single-library and
  all-libraries views that currently defer or reject discovery; preserve their
  render behavior and document the new structural query.
- Resolve static selectors against each route's selectable sections. This makes
  invalid direct-library selectors visible instead of ignored and continues to
  reject contextual schema sections that are not legal direct selectors.
- Intentionally make commandless `<target> --all-libraries` route to package
  all-libraries before any lookup in static and non-static modes. The option
  exists only on the package command; this replaces the current lookup-driven
  library/type misroute and is covered as a compatibility change.
- Keep an adapter to current command execution while all other behavior remains
  byte-for-byte stable.

Exit gate: every type/member gesture produces one resolved plan or typed
diagnostic before command execution, commandless static schema reaches the
structural classifier without target work, and provisional catalog state
cannot satisfy final shape validation. `MIP001` and `MIP003` must pass before
this slice lands.

### Slice 3: enforce the address-resolution contract

Depends on: slice 2.

- Implement the exact-type, fallback-peel, dual-success, qualification, and
  selector-conflict matrix.
- Return typed address, selector, no-match, and unresolved-section diagnostics
  rather than silently combining incompatible address components.
- Update command help and compatibility tests for every intentional diagnostic
  change.

Exit gate: `MIP006` covers the full matrix, including exact-type/member
dual-success, complementary refinement, conflicting refinement, distinct
inventory-filter per-filter outcomes and deduplication, bare-name/glob
zero-one-many and partial-miss results, surface/route/selector-driven
four-catalog selection, cross-catalog canonical target requirements,
exact-section/alias/category/glob detail promotion, explicit `type --member`
versus
`member` compatibility, explicit-member no-selector type view and dotted-tail
static-schema alternatives, and conflicting positional/qualified and
qualified/qualified types.

### Slice 4: lower resolved selection to typed producer plans

Depends on: slices 2 and 3.

- Bind member sections to typed query definitions.
- Make L1 query definitions the sole owners of producer prerequisites,
  conditional successors, costs, capabilities, modes, and probe eligibility;
  section descriptors bind those definitions and apply disclosure/request
  policy without restating requirements.
- Preflight cost, capability, execution-mode, and probe-policy authorization
  for common work, independent section closures, and conditional successors.
- Mint one fresh `InspectionOperationContext` in each top-level type/member
  command, query, or workspace coordinator invocation; bind every preflighted
  plan and completed section outcome to its opaque identity, thread the live
  context through execution, reject cross-operation or post-disposal use, and
  dispose it when that invocation ends. Bare library discovery remains on the
  separately dispositioned compatibility path until its own typed-planning
  migration.
- Remove source/PDB/analysis authorization inferred from `IncludeSections` or
  from the union of `Discover` selections into requested render sections.
- Move every producer reached by render-manifest discovery into the declared
  section-specific probe plan; retain the manifest only as a post-producer
  observation adapter.
- Update `schema-query.md` realized type/member discovery mechanics to name the
  declared probe plan rather than render-manifest-owned producer execution.
- Keep ordinary rendered output unchanged.

Exit gate: the executor accepts only the preflighted plan together with its
live owning operation context and the producer trace matches the resolved
selection matrix; named/category source discovery neither acquires source
capabilities nor becomes empty success, denied discovery closures remain
per-section unknowns, denied render closures remain failures, and no executable
path retains another authorization mint. `MIP002`, `MIP004`, `MIP005`,
`MIP007` through `MIP011`, and `MIP013` must pass before this slice lands.

### Slice 5: introduce Metadata declaration scaffolding

Depends on: slice 1. It may develop in parallel with slices 2 and 3 but cannot
be consumed by the type/member plan before slice 4 lands.

- Land and pass
  `LayeringTests.MetadataPrimitives_RemainsLeaf` in
  `src/dotnet-inspect.Tests` before adding the lossless row reader in this
  slice; the composite `MDP016` gate expands after the reader exists.
- Introduce `MetadataOperationContext.AdmitImage` as the single metadata-row
  charging authority, move
  `ApiSurfaceExtractor.ExtractionBudget.AdmitMetadataRows` into it, and have
  every declaration session reuse the resulting per-generation operation
  entry.
- Centralize the remaining operation budgets, acquisition-minted image state,
  and immutable declaration-fact/failure caches in the operation's
  generation-scoped `MetadataImageEntry`; keep only leases and liveness checks
  session-local.
- Carry the owned or borrowed `AssemblyImage` lease into
  `MetadataDeclarationSession`; use its acquisition-minted image generation,
  current reader, and liveness check, and do not pair independently supplied
  generations, readers, and byte blocks or reopen the artifact.
- Thread one `MetadataOperationContext` through each top-level CLI/query
  operation and all of its workspace `UseAssemblySession` callbacks; preserve
  one image generation across `AssemblyImageSnapshot` reader recreation, map it
  to a fresh entry in each operation, and clear operation state at completion.
- Introduce the neutral Metadata-owned `MethodSemanticsAssociationSession` as
  the sole product `MethodSemanticsRowReader` invocation owner; it calls
  `AssemblyImage.EnsureAlive()` immediately before every cold pass and exposes
  only the completed immutable neutral rows or typed rejection.
- Add the MetadataPrimitives-owned `MetadataImageFormatClassifier` as the one
  registered bounded metadata-root admission guard. Read only the fixed root
  prefix and at most the ECMA-335 256-byte padded version field from the
  acquisition-owned metadata block, and apply SRM's ordinal
  `WindowsRuntime` marker rule. Make the `AssemblyImage`-owned session factory
  classify and bind a supported result before it constructs any
  `MetadataReader`; no caller may supply the result and reader independently.
  In this slice, route the new declaration-session path,
  `MetadataImageInspector`, every public `MetadataTableProjector`
  row/reference/heap entry point, and the defensive
  `MethodSemanticsRowReader` leaf check through it before `MetadataReader`
  construction, admission, or managed metadata work. Later caller migrations
  inherit the same gate; do not parse stream/table structure or add a projected
  WinMD reader, fallback, compatibility adapter, or correspondence gate.
- In the same cutover, bump `LibraryCommand`'s `effective-v*` category before
  any post-cutover package/platform cache lookup or write. Direct local-file
  routes bypass both operations and recompute from a fresh retained image in
  each tool run. Mint typed
  `LibraryCatalogRouteEvidence` from the owner-issued root route and every
  stable route fact consumed by discovery; do not infer it from the resolved
  path or use it as authorization. Acquire one bounded immutable
  artifact-content snapshot and its owner-computed SHA-256 digest, then open the
  acquisition-owned `PEReader` over those retained bytes and run the format
  classifier before the local-symbol probe or any catalog lookup. Charge
  retained assembly bytes and any requested digest pass to the
  operation's finite image/work budgets; over-limit input fails visibly before
  cache access. Unsupported or malformed input performs no PDB probe, cache
  read, or current-category write.
- After supported admission, create one operation-owned
  `PortablePdbRetentionBudget` with a finite 64 MiB compatibility maximum shared
  by adjacent, symbol-cache, acquired, and decompressed embedded providers.
  Reserve a selected seekable PDB's declared length before allocation, copying,
  hashing, or `MetadataReaderProvider` construction; bounded-copy non-seekable
  input to limit plus one, and reserve embedded declared decompressed length
  before expansion. An over-limit candidate returns typed
  `PortablePdbRetentionLimitExceeded`, performs no catalog read/write, and
  neither becomes `None` nor falls through to another provider. Product
  effective discovery cannot use `SourceLinkReadLimits.Unlimited`. Return typed
  `LocalSymbolDiscoveryEvidence`: `None`, or an owner-minted identity containing
  the retained identity-validated PDB digest, every provider/provenance
  dimension consumed by discovery, and typed SourceLink effectiveness. The
  probe constructs any PDB `MetadataReader` needed to mint that evidence before
  the catalog lookup. Bind the route evidence, retained assembly/digest,
  supported format result, and local-symbol evidence/snapshot into one immutable
  `LibraryEffectiveCatalogSubject`; on package and platform routes, cache
  lookup, every cold producer, and publication accept that subject rather than
  independently supplied key components. Direct local-file discovery consumes
  the retained evidence without persistent lookup or publication. A
  package/platform hit then returns without assembly identity decoding, an
  assembly `MetadataReader`, or full discovery.
- On a miss, make a new from-retained-content image/snapshot factory preserve
  the same bytes, digest, owner binding, and supported-format result while it
  performs `AssemblyImageSnapshot` identity/MVID decoding and opens the
  inspection session. Replace the current path-opening snapshot factory in this
  route rather than allowing it to call the mutable source opener again.
  Thread the retained reference through every transitive assembly consumer in
  all three bare-library branches -- package/platform cache-enabled and direct
  local-file cold-only -- including platform surface classification, metadata
  inspection, scanners, and SourceLink/PDB correlation; path remains
  provenance/presentation only. Carry `LibraryCatalogRouteEvidence` to every
  producer and, on persistent routes, the key rather than letting a
  platform/direct/package distinction disappear after path resolution. The
  cold inspection and any successor publication use the same retained assembly
  and PDB content and any digests frozen in the subject; do not reopen, rehash,
  or re-key from either mutable source inside the chain. Separately authorized
  source work remains outside the catalog subject. If the owner observes a
  local-symbol evidence-generation change before publication, decline the
  write rather than filing the existing result under new evidence; a later
  invocation recomputes. A catalog from the preceding category does not prove
  admission, and the successor may be populated only after inspection
  succeeds. Preserve subsequent cross-process hits for supported
  package/platform ECMA-335 inputs.
- Add typed type, member, accessor, and `MethodImpl` validation results.
- Replace the reader-only accessor-bearing
  `MetadataDeclarationQuery.GetProperty` and `GetTypeSurface` surfaces with
  session-backed queries; keep reader-only helpers that do not touch accessor
  semantics.
- Add the registered MetadataPrimitives `MethodSemanticsRowReader` and
  Metadata-owned semantic census. Shadow their results against every current
  property/event accessor projection, retaining duplicate roles and invalid
  flags as typed rejections rather than normalized output.
- Run characterization or shadow comparison without changing which validity
  implementation supplies product results.
- Do not activate the new admission semantics for only one projection path.

Exit gate: cache, context, and hostile-input limit declarations drive `MDP009`;
shadow results expose full/summary/focused limit, rejection, and projection
disagreements before cutover; `MDP016` proves the narrow lossless-row exception
before any consumer uses it to supply product results; the slice-5 portion of
`MDP017` proves the classifier and raw table/image/leaf paths. It does not
require later consumer migrations: slice-specific bypass closure belongs to
`MDP011`, and final repository closure belongs to `MDP013` and `MDP017`.

### Slice 6: activate shared declaration admission atomically

Depends on: slice 5.

- Route full, summary, and focused declaration admission through the same
  six-stage decision in one slice.
- Route every property/event projection through the validated raw
  `MethodSemantics` census before constructing an accessor aggregate; remove
  every direct SRM convenience-accessor read from `ApiSurfaceExtractor`,
  `MetadataDeclarationQuery`, `ExtensionMethodScanner`, and
  `OpenTelemetryScanner`, including non-admission heuristics.
- Replace `ApiSurfaceExtractor`'s bare-`PEReader` full, summary, and bounded
  entry points with a session-backed core. Migrate every production caller in
  the same slice: `AssemblyInspectionSession` supplies its owned image,
  `PdbContext` uses `AssemblyInspectionSession.Borrow`, `AssemblyReader` and
  path/stream utilities open owned sessions, and Decompiler `MetadataSource`
  moves its API-surface plumbing onto a genuine owner-backed image/session.
  This last change carries lifetime and operation context only; Decompiler's
  own accessor classification remains slice 8.
- Migrate Metadata-owned `ExtensionMethodScanner` and `OpenTelemetryScanner`
  association lookup through `MethodSemanticsAssociationSession` without
  changing their feature-specific inclusion policy; carry the image lease and
  operation context instead of a bare `PEReader`.
- Make summary counts consume accepted declaration identities.
- Make focused queries consume the same validators with their own inclusion
  policy and failure boundary.
- Delete path-local validity checks after parity gates pass.

Exit gate: accepted identity, rejection-rule, and shared-fact result sets agree
across full, summary, and focused projections for equivalent inclusion
policies, and none of those entry points retains a parallel admission owner.
`MDP001` through `MDP004`, `MDP006` through `MDP009`, `MDP011`, and `MDP016`
must pass before this semantic cutover lands.

### Slice 7: migrate C# representability

Depends on: slice 6.

- Carry typed accessor and slot semantics into the API/C# boundary.
- Migrate CSharp callers of accessor-bearing `MetadataDeclarationQuery`
  surfaces to the declaration session and its validated aggregate.
- Remove inference from raw flag combinations.
- Make `FallbackRequired` distinguish contained type and member declarations,
  preserve complete discriminating identity and signature/header facts, and
  render artifact-authored text inertly.
- Derive type/member fallback fact sets from the normal representable renderer;
  require equality except for explicit shared-fact erasures.
- Require every fallback sink to tighten `InertString` with its exact
  `TextPolicy` immediately before unwrapping and structural escaping.
- Replace type-shell representability omissions, including unsupported
  base/interface spellings, with typed type outcomes.
- Preserve degraded-signature non-success and strict failure containment.

Exit gate: CSharp consumes the typed representability outcome and contains no
raw-metadata or raw-flag relationship reconstruction. `MDP005`, `MDP010`,
`MDP012`, `MDP014`, and `MDP015` must pass.

### Slice 8: remove transitional state

Depends on: slices 4, 6, and 7.

- Remove provisional selection mutation and dual-use option fields.
- Remove duplicate Metadata validators and compatibility branches.
- Remove duplicate producer-requirement declarations and any remaining
  convenience-accessor path that bypasses the raw `MethodSemantics` census.
- Migrate Decompiler's `MemberBodyProducer` and `MethodDefinitionFacts`
  accessor lookup through the neutral Metadata-owned
  `MethodSemanticsAssociationSession` while retaining their Decompiler-owned
  heuristic and classification policy; the Decompiler operation must carry the
  image lease and finite operation context established by its slice-6
  API-surface plumbing rather than a bare reader.
- Inventory every test and Decompiler-harness SRM `GetAccessors()` call.
  Migrate harness paths that construct compile-back artifacts or otherwise
  supply expected accessor structure, including reader-only chains that must
  now carry an `AssemblyImage` lease; retain only exact allow-listed
  comparison-oracle and address-only test-input calls.
- Inventory every remaining product acquisition owner and public/reusable
  `PEReader` entry point. Route any path not already covered by
  `AssemblyImage`, the slice-6 API-surface migration, or the raw metadata
  projector through `MetadataImageFormatClassifier`; no reader-only
  compatibility path may bypass the unsupported-format gate.
- Update architecture docs from proposed to implemented only after the
  corresponding gates pass.
- Update `schema-query.md` and the other mechanism owners to remove superseded
  transitional type/member discovery descriptions.

Exit gate: targeted searches and architecture tests find no dual-use option
authority, duplicate declaration validity owner, or CSharp metadata
reconstruction, including inert compatibility state left after the semantic
cutovers. `MIP012`, `MDP013`, and the full `MDP017` closure must pass.

## Verification obligations

The target contract in this document is **unverified** until the migration
gates below land. Existing tests and the paused stack's review reproductions are
evidence for the problem, not proof of the target architecture.

Gate IDs are stable design references. Implementations may use a more specific
test method name, but the PR must map each test to its gate ID.

| Gate | Property | Required evidence |
| --- | --- | --- |
| `MIP001` | Static schema chooses only syntax-proven structural views and runs no target or producer work | Declaration-derived mapping equality between every preprocessor/rewrite/parsed view route and its structural-view registry entry, including precedence, destination command, view mode, catalog identity, parser capabilities, per-section input requirements, and schema projection; every advertised section/field/column/coordinate/shape has a corresponding accepted parser gesture and renderer mapping, and every reachable shape is advertised; package-library projections retain defaultable sections such as `Performance: Boxing` and omit only sections or shapes whose declared input is unavailable on that route; all-libraries row schemas expose only supported sections and include package/version/library/TFM identity columns; explicit and commandless package `--library`, package `--all-libraries`, and direct `.nupkg --library` cases return their view-specific projections before resolution/extraction; commandless `--all-libraries` routes to package before lookup in static and non-static cases; explicit package/library/type/member gestures prove their deterministic command-owned schemas, including explicit `member` with no member gesture; explicit-member dotted-tail ambiguity and other syntax-only ambiguous forms retain labeled per-alternative selector results; a close-negative fails if platform resolution, facade classification, package existence, all-framework search, acquisition, type/member lookup, or any section producer begins, and asserts that no resolution note is emitted |
| `MIP002` | Named/category type/member source discovery cannot read/acquire PDB/source content or confuse unknown with empty | Overload-qualified `-D "Source Locations"`, `-D "Original Source"`, `-D "Source Diff"`, and source-category cases proving no `LocalPdbRead`, `PdbAcquire`, or `SourceContent`; paired genuinely-empty and PDB-required fixtures produce distinct `ValidEmpty` and `Unknown(CapabilityNotRequested)`, while plain library discovery retains its bounded `LocalPdbRead` positive and close-negative gates |
| `MIP003` | Demand classification, provisional catalogs, and static alternatives cannot satisfy final shape validation | Close-negative tests for exact type, implied member, mixed filters, aliases, globs, categories, `@All`, commandless structural alternatives, and explicit-member dotted-tail alternatives; declaration-derived set equality requires one canonical target requirement for every stable identity registered in multiple catalogs and rejects conflicting declarations |
| `MIP004` | Closed producer paths equal preflighted authorization | Declaration-derived gesture-provenance/query-requirement/host-policy matrix; unconditional prerequisite closure; conditional local-PDB hit, unrequested/denied miss, and authorized acquisition paths; transitive cost, execution-mode, and probe-policy closure; a probe-capable producer with a render-only prerequisite mapping to per-section `Unknown`; explicit-render denial; preflight-before-execution assertions; artifact-owner lease revalidation; same-target effective discovery under granting and denying hosts in both execution orders; two freshly minted same-host, same-target, same-request operations in which one receives an injected producer failure and the other succeeds after recovery, repeated in both operation orders; explicit attempts to present the first operation's plan to the second operation and to execute it after disposing the first context, both rejected before cache or producer access; and architecture closure proving the planned type/member executor never reads or writes the library-only `effective-v*` catalog. Together these prove completed outcomes are scoped to one operation-bound preflighted plan while persistent producer evidence is independently reauthorized |
| `MIP005` | Presentation cannot widen work | A non-vacuity test that fails when render-manifest or ordinary rendering starts an undeclared producer |
| `MIP006` | Address and catalog resolution are deterministic, diagnostic, and surface-preserving | The slice-2 structural-view mapping remains closed; set equality between the type/member catalog/route registry and its four realized pipeline owners plus every entry route; exact type, fallback peel, dual-success, qualified/positional conflict, same-type and conflicting qualified/qualified selectors, identical/complementary/conflicting implied-explicit refinement, per-filter bare-name/glob outcomes, partial misses, overlapping-filter deduplication, zero/one/multiple inventory results, exact selector success/failure, explicit `type --member` versus `member` catalog/output compatibility, surface/route/selector-driven assembly-type-list/type-member-list/overload-inventory/detail catalogs, explicit-member no-selector type view, targetless/glob/failed-exact/platform-prefix list routes, cross-catalog `MemberSet` and `ExactMember` identities, exact-section/alias/category/glob detail promotion, commandless and explicit-member dotted-tail static alternatives, unavailable detail sections, and overload/digest/arity cases |
| `MIP007` | L1 member execution remains content-shaped and owner-authorized | Architecture closure plus admission/query-lease tests proving no readable path or descriptor bypass |
| `MIP008` | The plan executes sequentially without filesystem assumptions | Browser/Wasm host test over in-memory content with the same producer trace and failures |
| `MIP009` | The path remains NativeAOT-friendly, SRM-only, Roslyn-free, and load-free | NativeAOT publish/run plus dependency and inspected-assembly-loading architecture gates |
| `MIP010` | Typed planning, resolution, policy, producer, and observation failures stay visible | Outcome and injected-failure tests for every stage, including per-section request/capability/cost/mode/probe-policy `Unknown`, explicit-render policy denial, exact/category network-free discovery-probe failure, and authorized render/effective PDB/source producer failure, proving failures cannot become empty selection, `ValidEmpty`, empty results, or success exit |
| `MIP011` | L1 query definitions are the only producer-requirement owner and host preflight is the only executable authorization mint at the slice-4 cutover | Declaration-driven architecture closure over every type/member entry point, query definition, descriptor, prerequisite, and executor call site; fail on a prerequisite, conditional successor, cost, capability, mode, or probe-policy requirement declared by a section descriptor; fail on another mint or an option value read as execution authority |
| `MIP012` | No transitional dual-use planning/authorization state remains | Declaration-driven closure over option fields, compatibility adapters, query definitions, descriptors, and executors after slice 8; fail on duplicated producer requirements as well as alternate authority |
| `MIP013` | Non-schema discovery runs only mode-declared probes | Separate target-resolution and section-producer trace equality for every actual type/member discovery gesture; every started producer is declared for the effective probe mode, and every declared denial remains a typed per-section `Unknown` |
| `MDP001` | Full/summary/focused validity and shared semantic facts agree | Set equality over accepted identities and typed rejection rule IDs; set equality between typed projection fact requests and registered consumers; declaration-derived equality of typed values or typed rejections for every shared fact requested by multiple projections; declared summary erasure is checked only after parity |
| `MDP002` | Declaration admission order is preserved | Direct-invalid, charged name/attribute exclusion, excluded-hostile, public/non-public accessor dependency, retained-rejected, aggregate-rejected, and valid-empty fixtures; property/event cases prove raw association rows and getter/setter/add/remove/raise/every-`Other` dependencies are validated and charged before handle retention, `Other` materialization, or aggregate construction |
| `MDP003` | Cheap filtering precedes hostile MethodImpl projection | Large excluded-row fixture with bounded allocation/work evidence |
| `MDP004` | Accessor validity is shared | Property/event fixtures covering accessibility, staticness, abstraction, virtuality, slot, and body close negatives plus zero, one, and multiple valid `Other` associations, duplicate standard roles, invalid combined role flags, dangling methods, cross-declaring-type associations, malformed table ordering, and raise/`Other` representability outcomes |
| `MDP005` | CSharp consumes typed slot semantics | Compiler-produced interface re-abstraction/default-implementation compile-back plus class base-slot rejection |
| `MDP006` | Decode accounting is transitive | Amplification fixtures for names, modifiers, signatures, getter/setter/add/remove/raise/`Other` semantic dependencies, and MethodImpl targets; an oversized semantics table must pass whole-image row admission only when within `MaxMetadataRows`, then stop on the lower retained-association budget with bounded allocation and no double charge; dependent property/event projections receive typed rejection with no streaming fallback, while independent declaration kinds retain their normal failure policy |
| `MDP007` | Metadata and CLI failure text contains no artifact data | Hostile control-character names across Metadata declaration and CLI failure paths |
| `MDP008` | Real artifacts remain stable | Pinned platform and package canaries with recorded rows and retained-text totals; the complete MethodSemantics census accepts every pinned input and does not broaden a table-level malformed-ordering rejection into an unexplained omission |
| `MDP009` | Declaration caches and hostile-input ceilings preserve context, budget, lifetime, and failure semantics | Matrix derived from the central safety-policy dimensions and every charging fact request, including the operation's generation-scoped declaration facts, typed failures, and `MethodSemantics` association index; cache-key set equality covers fact kind, handle, generic context, resolution context, and every participating image generation; cached-work rejection, same-context negative caching, changed-context close negatives, missing participating-session `ResolutionContextUnavailable`, extra/cross-operation/generation-session mismatch rejection, and no undeclared local ceiling; multi-generation facts are retained and charged only in the subject generation's entry while every participating session supplies key and liveness context; after the image-format classifier accepts a supported image, `MetadataOperationContext.AdmitImage` runs unconditionally as the sole row-charge call site, records the row sum under explicit compatibility `Unbounded`, rejects construction of any product context lacking a finite policy after slice 6, and makes a full extraction's cumulative row charge equal the image's declared row sum exactly once; reference identity of the acquisition-minted `MetadataImageGeneration` maps to a context-owned entry and is not itself an operation-local cache object, artifact or assembly identity, `AssemblyAcquisitionRegistration`, a `MetadataReader`, `PEReader`, `AssemblyImage`, path, MVID, or digest; within one operation, repeated `GetMetadataReader()` results, owned/borrowed sessions, and snapshot-backed sessions whose callbacks recreate `PEReader` instances share admission, immutable fact/failure, and association-index state for one generation but retain separate session liveness checks, producing one row charge, one retained-association charge, and one charge per shared fact; full, summary, and focused projections run through separate sessions in every order under a budget permitting one decode of each shared fact, and every permutation returns the same value or typed failure and counters rather than becoming callback-order dependent; multithreaded concurrent cold requests for one image, association index, positive fact, and negative fact prove atomic entry creation, single publication, and exactly-once charging, while a same-chain reentrant fact request returns `FactDependencyCycle` without deadlock or a second decode; single-threaded Browser/Wasm exercises the same uncontended result contract; an independent open receives a different generation, while a later operation over the same persistent snapshot maps its retained generation to a fresh entry, repeats charges, and cannot observe the earlier result or rejection; the operation owns one immutable safety policy, and a mixed finite/`Unbounded` session attempt fails before cache lookup; a same-operation, same-generation cache hit reuses the entry-owned immutable value or typed rejection without recharging only after every participating session rechecks its owner/lender liveness; owned and borrowed fixtures dispose the owner or lender before uncached work and before positive and failure cache hits, asserting `ObjectDisposedException` with zero primitive invocations, row reads, or cached-value observation; entries retain no reader, block, pointer, fact-access context, session, lease, or mutable budget; solution-wide product-call closure requires `MethodSemanticsAssociationSession` to be the only product `MethodSemanticsRowReader` invocation owner, while direct calls are confined to leaf boundary tests; Metadata scanner fixtures prove their neutral-row paths use the association session with finite operation policy and no bare-reader bypass; constructor/API closure rejects independently supplied generation/reader pairs and independently supplied format-result/reader pairs; a workspace loop over one participant remains one charged image per operation, context disposal releases every fact and entry, and entry count/bytes remain within declared image/fact/index retention budgets; equivalent near/over-limit full/summary/focused fixtures assert equal per-fact thresholds, counters, and rejection rules while separately charging additional retained fields |
| `MDP010` | Degraded signatures remain nonauthoritative at the CSharp boundary | Existing degraded-signature fixtures mapped to typed `Degraded` outcomes with no authoritative C# or metadata fallback |
| `MDP011` | The Metadata slice-6 cutover has no parallel declaration owner, reader-only extraction seam, or convenience-accessor bypass | Declaration-driven architecture closure over full, summary, and focused entry points; fail on a duplicate validity implementation or admission bypass; require no bare-`PEReader` `ApiSurfaceExtractor` full, summary, or bounded entry point and prove every production caller carries a genuine owned or borrowed image lease plus finite operation context; require no SRM `PropertyDefinition.GetAccessors()` or `EventDefinition.GetAccessors()` call anywhere in `ILInspector.Metadata`, including `ExtensionMethodScanner` and `OpenTelemetryScanner`, after the cutover |
| `MDP012` | CSharp representability consumes only Metadata-owned semantic facts at the slice-7 cutover | Closure derived from the semantic fact types and every CSharp representability entry point; fail on direct `MetadataReader`/handle reconstruction or relationship decisions from raw accessibility, virtuality, new-slot, `MethodImpl`, or equivalent Boolean combinations |
| `MDP013` | No transitional declaration-validity or CSharp reconstruction state remains | Declaration-driven closure over compatibility adapters, validators, raw semantic fields, and consumers after slice 8; no shipped product or reusable product-library SRM `PropertyDefinition.GetAccessors()` or `EventDefinition.GetAccessors()` call remains; Decompiler fixtures prove `MemberBodyProducer` and `MethodDefinitionFacts` consume the association session under the slice-6 owner-backed image lease and a finite Decompiler operation policy, call the current owner/lender liveness check before both cold and cached results, and retain their own classification policy without a bare-reader bypass; a gate-owned exact file/enclosing-member/occurrence-count allow list records every remaining solution call exactly once with category and justification as either a comparison-only independent SRM oracle or an address-only test-input selector whose assertion depends solely on product output; the mechanical gate fails stale, unlisted, or occurrence-count drift, while category correctness is an explicit reviewer obligation whenever the list changes; no allowed category may supply expected accessor structure or construct, normalize, repair, or substitute for an artifact later compiled or measured as product evidence; reflection `PropertyInfo.GetAccessors` is outside this closure |
| `MDP014` | CSharp failure text contains no artifact data | Hostile control-character names through every CSharp representability failure path |
| `MDP015` | `FallbackRequired` preserves contained type/member semantics and renders artifact text through `InertString` | Set equality between the normal representable renderer's Metadata fact requests and each contained fallback payload after named erasures; type and member parity fixtures cover accessibility, modifiers, attributes, constraints, constants/defaults, explicit implementation, complete accessor aggregates including raise and every `Other` association, base/interfaces, and kind-specific facts; valid raise/`Other` aggregates force contained fallback and preserve each association instead of becoming unrelated standalone methods; unsupported type-header and paired member/indexer cases prove no fact becomes `null`, omission, or identity collapse; declaration-derived sink closure requires every fallback sink to call `EnsurePermitted` with its exact `TextPolicy` immediately before unwrapping and format escaping; cross-policy fixtures deliver Prose-produced CR/LF/TAB plus hostile type names, member names, and signature fragments to Field, Markdown, JSON, TSV, and diagnostic sinks; round-trip and pairwise injectivity prove canonical visual encoding preserves exact artifact text while no live disallowed scalar reaches a sink |
| `MDP016` | The lossless `MethodSemantics` row boundary is the only registered raw-table exception and remains mechanical, bounded, and SRM-backed | A pre-reader `LayeringTests.MetadataPrimitives_RemainsLeaf` gate in `src/dotnet-inspect.Tests` and post-reader symbol/API closure prove MetadataPrimitives remains an SRM-only leaf; the exact raw-layout allow list distinguishes the fixed metadata-root admission guard owned and gated by `MDP017` from table decoding, and only `MethodSemanticsRowReader` calls `GetTableMetadataOffset`, `GetTableRowSize`, or decodes raw ECMA table columns; the classifier and row reader may each call `PEReader.GetMetadata` and `PEMemoryBlock.GetReader` only for their separately bounded contracts, no arbitrary `TableIndex`, schema, or coded-index API escapes, and unrelated blob/heap `BlobReader` use is outside the detector; required-CI ordered-multiset equality with `ildasm` over association/role/method plus construction-known `ilasm` fixtures, with both external-tool groups required there but allowed to skip together locally; tool-independent `MetadataBuilder` and byte-patched raw-row fixtures remain the non-skipping construction-known floor, alongside conventional aggregate parity with SRM accessors; all four narrow/wide MethodDef and HasSemantics index combinations are generated once per test run and assert decoded values, while SRM row-size equality separately checks the total width; fixtures prove exact preservation of duplicate roles, zero/unknown/combined bits, physical row order, and nonmonotonic-order observation, while nil/out-of-range MethodDef or association rows produce typed mechanical rejection and the same out-of-order rows with the sorted bit clear fail at SRM reader construction; a supplied retained-association budget proves complete-scan bounded allocation before the leaf returns neutral rows, and the reader retains no block, reader, or pointer beyond the call; Browser/Wasm and NativeAOT gates exercise the same supported ECMA-335 result. Role legality, duplicates, declaring-type consistency, and ordering-policy rejection belong to `MDP004`; format classification belongs to `MDP017`; operation admission, generation/entry mapping, dependent-projection failure, and both cold/cache liveness wiring belong to `MDP006`/`MDP009`; consumer migration belongs to `MDP011`/`MDP013` |
| `MDP017` | Unsupported Windows Metadata cannot enter a product metadata path or become malformed ECMA-335 | `MetadataImageFormatClassifier` is the sole registered metadata-root admission guard. `PEReader.HasMetadata == false` returns typed `NoMetadata` without requesting a block; otherwise the classifier obtains the owner-bound metadata block from the supplied `PEReader`, reads only the ECMA-335 signature, fixed major/minor/reserved fields, signed padded-version length, and at most the declared 256-byte padded field, scans through the first null for the ordinal ASCII `WindowsRuntime` marker SRM uses before optional WinRT projection, and constructs no `MetadataReader`. Ordinary and marker-bearing `MetadataBuilder` images, including a marker-bearing image without an mscorlib `AssemblyRef`, prove `SupportedEcma335` versus `UnsupportedWindowsMetadata`; a native PE proves `NoMetadata` and preserves its established no-metadata boundary; wrong-case markers, markers after the first null, and markers outside the declared field remain supported close negatives; an unmappable metadata directory, truncated fixed prefix, invalid signature, negative/over-256 padded length, and length beyond the block remain distinct typed malformed-root results, while injected lazy-stream I/O failure remains an acquisition failure. The gate records the deliberate compatibility boundary that SRM may accept a longer field when enough bytes exist, while the guard rejects it because ECMA-335 bounds the null-terminated version to 255 bytes and the padded field to 256; no input may be admitted with an unexamined marker beyond the fixed window. A marker-bearing byte-patched image whose unsorted `MethodSemantics` table would scan or fail during SRM reader initialization must return unsupported without `MetadataReader`, row, table, heap, or stream-header work. Prefetched and lazy-stream fixtures record total block-materialization cost separately; a test-only pre-materialized measurement that first obtains the block from the same `PEReader` isolates the subsequent classifier delta and proves classifier-owned allocation/work is bounded only by the root prefix and 256-byte field rather than row count. Product paths still classify before `MetadataReader` construction or managed metadata work. Within MetadataPrimitives, architecture closure permits root-admission interpretation only in the classifier; `MDP016` separately owns the exact raw-layout allow list for the classifier and `MethodSemanticsRowReader`, while the existing `StructuralCloneAnalysis.ReadUserStringHeap` stream parser remains explicitly excluded migration debt rather than an admission path. Acquisition/public-`PEReader` closure requires `AssemblyImage`, `PdbContext`, Decompiler `MetadataSource`, `MetadataImageInspector`, every `MetadataTableProjector` table/row/reference/heap entry point, and direct `MethodSemanticsRowReader` calls to invoke the classifier before `MetadataReader` construction, admission, or managed metadata work; portable-PDB `MetadataReader` construction after the owning assembly has passed admission is outside this assembly-metadata closure and remains bounded by one finite operation-owned retention/expansion budget. Direct acquisition/projector APIs preserve `NoMetadata`, map malformed roots to `BadImageFormatException` with bounded non-artifact text when their return shape has no failure arm, and throw `UnsupportedMetadataFormatException` with the same text constraint only for unsupported Windows Metadata; owning queries preserve each mechanism in a typed no-metadata, malformed-input, or unsupported-input result; CLI Markdown and structured metadata-lens modes prove there is no empty or partial success; Browser/Wasm and NativeAOT gates exercise every classifier arm. The library cache cutover first mints `LibraryCatalogRouteEvidence` from the owner-issued platform/package/direct-file route and every route fact consumed by discovery, then acquires one bounded immutable assembly-content snapshot and its owner-computed SHA-256 digest, opens a `PEReader` over those retained bytes, and invokes the classifier before the local-symbol probe or every catalog lookup. Unsupported or malformed input proves zero PDB opens, cache reads, or current-category writes. After supported admission, one operation-owned `PortablePdbRetentionBudget` applies a finite 64 MiB compatibility ceiling across adjacent, symbol-cache, acquired, and decompressed embedded providers. It reserves seekable length before allocation/copy/hash/reader construction, bounded-copies non-seekable input to limit plus one, and reserves embedded declared decompressed length before expansion. Over-limit input returns typed `PortablePdbRetentionLimitExceeded` with zero catalog read/write, does not become `None`, and does not fall through to another provider; product effective-discovery call closure rejects `SourceLinkReadLimits.Unlimited`. Within that budget, the probe reads the retained image's PE debug directory, constructs any PDB `MetadataReader` needed before catalog lookup, and returns typed `LocalSymbolDiscoveryEvidence`: `None`, or an owner-minted identity containing one retained, assembly-identity-validated portable PDB's SHA-256 digest, every provider/provenance dimension consumed by effective discovery, and typed SourceLink effectiveness. Bind the route evidence, retained assembly/digest, supported result, and local-symbol evidence/snapshot into one immutable `LibraryEffectiveCatalogSubject` before lookup. A supported catalog hit then constructs no assembly `MetadataReader` and performs no full discovery. On a miss, from-retained-content factories and every producer consume that subject; API closure rejects independently supplied subject components, route evidence, digest/snapshot, digest/result, format-result/reader, or PDB-evidence/PDB-reader pairs. Call-graph closure starts at all three cache-enabled bare-library branches and requires every transitive assembly/PDB consumer, including platform `AssemblySurfaceClassifier`, metadata inspection, scanners, and SourceLink correlation, to use those retained references and route evidence; a path may remain provenance or presentation but cannot reopen the subject. The mutable assembly source and each selected local PDB source are each opened exactly once, and neither is rehashed or reopened for the cold producer or write; the bracketed-path-hash workaround tracked by #3478 is removed. Publication uses the frozen subject and never substitutes post-production evidence. Separately authorized source work stays outside the subject; if the owner observes a local-symbol evidence-generation change, it declines publication rather than re-keying, and a later invocation probes and recomputes. A declaration-derived set-equality gate covers every route- or PDB-dependent section/field predicate, including applicability that falls back to `CanRender`, and every route/provider/provenance fact consumed by discovery; each must be a function of `LibraryCatalogRouteEvidence` and `LocalSymbolDiscoveryEvidence`, so adding a dependent producer cannot leave the successor key under-scoped. The migration declaration owns distinct predecessor and successor `effective-v*` categories and their registered set independently of key reachability. Cutover evidence seeds successful predecessor-category sentinels for both legacy `sl0` and `sl1` keys of exact marker-bearing bytes and proves classification returns unsupported before any catalog read and writes no successor entry. A paired supported ECMA-335 fixture with predecessor sentinels proves the old catalog is ignored, one real retained-content inspection populates the successor category under the new key shape, and a separate process receives the supported hit. Route fixtures invoke one exact installed assembly through platform and direct-file routes in both population orders, prove route evidence and keys differ, preserve the platform-only `Facade` field, and receive correct separate-process hits; package/direct same-file and every other declared route pair receive the same closure. Stable PDB fixtures cover `None`, a PDB without SourceLink, two identity-valid SourceLink PDBs with different document-path/catalog effectiveness, and every discovery-relevant provider kind; different PDB bytes or relevant provenance produce different keys and catalogs. A `None` subject whose cache warms to PDB P2 and a PDB P1 subject replaced by P2 prove every cold producer remains on the frozen subject and no entry is written under P2 evidence; an observed generation change declines publication, while a later invocation probes P2, recomputes, and may publish. Clearing P1 back to `None` is likewise visible only to a later invocation. Near/over-limit fixtures cover seekable adjacent, symbol-cache, acquired, non-seekable, and embedded-decompressed PDBs; the over-limit identity-valid trailing-data PDB must fail before hash/reader/cache work rather than returning success. In-process product acquisition seams whose successive assembly or PDB source opens return W, S, and W count exactly one open per selected source and assert that each published digest names the exact retained bytes supplied to admission and every producer. That deterministic witness and API closure fail mutations that restore bracketing hashes, reopen any transitive producer/publication path, re-key publication from post-production evidence, or permit independently paired evidence and result; cross-process sentinel cases remain separate evidence rather than timing the mutation. The gate records assembly/PDB retained-byte peaks, one linear digest pass per newly retained subject, reservation release on every failure, and no `SourceLinkReadLimits.Unlimited` product caller; the same 64 MiB finite policy and typed failure run without threads on Browser/Wasm. Category registration/set-equality closes the versioned category inventory |

The [assembly image lifetime](assembly-image-lifetime.md) decision narrows
`MDP017`'s cache cutover: direct local-file routes still owe the retained
snapshot, classification, bounded PDB, and cold-path rules, but they must not
read or publish a persistent effective-catalog entry. Every contrary
direct-file persistence requirement in the `MDP017` row and the earlier
migration and disposition text -- including separate-process hits,
package/direct cache-sharing pairs, and direct-file successor keys -- is
superseded. Platform and package routes retain the persistent-cache
requirements. This target change is unverified pending
`LocalAssemblyFacts_DoNotEnterACrossRunCache`.

Contract tests should derive their cases from the declaration or section
catalog where practical, so a new mode or validator cannot silently avoid the
matrix.

### Known unverified claims

| Claim | Owner | Gate |
| --- | --- | --- |
| L1 query definitions are the only producer-requirement owner and type/member host preflight is the only execution authority | Typed query executor | `MIP004`, `MIP011`, `MIP012` |
| Static catalogs and non-static producer traces agree with their modes | Section/query plan integration | `MIP001`, `MIP003`, `MIP013` |
| Discovery applicability, valid-empty, unknown, and failure remain distinct | Section/query plan integration | `MIP002`, `MIP010` |
| Renderer code cannot trigger acquisition or analysis | Presentation boundary | `MIP005` |
| Target/member execution preserves content, lease, host, and platform boundaries | Query/acquisition integration | `MIP007`, `MIP008`, `MIP009` |
| Planning and address failures cannot become ordinary absence | Planning/resolution boundary | `MIP006`, `MIP010` |
| No dual-use option or alternate authorization mint survives migration | Planning/execution architecture | `MIP011`, `MIP012` |
| One declaration admission decision and one shared semantic-fact owner govern all API projections | Metadata | `MDP001`, `MDP002`, `MDP011`, `MDP013` |
| Excluded hostile rows cannot amplify expensive projection | Metadata | `MDP003`, `MDP006` |
| Valid metadata and complete, contained C# fallback remain distinct from degraded or invalid input | Metadata/CSharp boundary | `MDP004`, `MDP005`, `MDP010`, `MDP012`, `MDP014`, `MDP015` |
| Lossless `MethodSemantics` access remains the sole narrow raw-table exception below Metadata semantics | MetadataPrimitives/Metadata | `MDP002`, `MDP004`, `MDP006`, `MDP009`, `MDP016` |
| Cache reuse cannot bypass context or operation budgets | Metadata operation context and generation-scoped image entry | `MDP009` |
| No duplicate validity owner or CSharp raw-metadata/raw-flag reconstruction survives migration | Metadata/CSharp architecture | `MDP011`, `MDP012`, `MDP013` |
| Effective-discovery outcomes cannot cross top-level operations or authorization dispositions | Section/query plan integration | `MIP004` |
| Unsupported Windows Metadata cannot enter product metadata projection | MetadataPrimitives/acquisition/Metadata | `MDP017` |

## Review exit criteria

Implementation adversarial review resumes only after:

1. this design has completed its own docs-only review;
2. the existing stack has a written retain/relocate/replace/drop disposition
   against this design;
3. the next implementation candidate contains the effective base and passes
   the smallest gates for its migration slice;
4. reviewer prompts name these invariants and reject call-site patches that
   recreate a shared rule.

Review stops when one fixed head is review-clean. The six-round authorization
is a ceiling, not a target.

## Rejected alternatives

### Continue fixing each review finding locally

Rejected. The repeated defects are evidence that call-site fixes do not prove
one owner or cross-path parity.

### Make `MemberOptions` the plan

Rejected. CLI options are parsed user intent and presentation choices. Adding
more state flags preserves the ambiguity between provisional and resolved
state and continues to let rendering fields authorize work.

### Resolve every selector against a union catalog

Rejected. The assembly-type-list, type/member-list, name-scoped
overload-inventory, and exact-member-detail pipelines expose different
contracts. A union is useful only as provisional syntax recognition; it cannot
answer final shape, cost, capability, or effectiveness questions.

### Validate and project every metadata row before filtering

Rejected. Hostile excluded rows could amplify signature and `MethodImpl` work.
Context and cheap row validity run first, then explicit inclusion, then
expensive retained projection.

### Filter before checking addressed declaration context

Rejected. A focused query could otherwise turn context-invalid metadata into an
apparently valid private or internal declaration.

### Let CSharp reinterpret raw metadata

Rejected. That creates another metadata semantics owner. CSharp decides
spellability and fallback from Metadata-owned typed facts.

### Build one universal normalized metadata graph

Rejected. Different producers retain different semantic models and erasure
policies. The shared boundary is declaration validity and facts for API
projection, not a universal representation.

### Repair parity in the harness

Rejected. A harness may compare product outcomes but must not normalize,
repair, or reconstruct the declaration or C# artifact used as product evidence.

## Open implementation questions

The design does not require answers to these before docs review:

- whether parsed and resolved plan types live initially in
  `DotnetInspector.Sections` or a lower query-planning assembly;
- whether the Metadata declaration session is one concrete type or a small set
  of operation-scoped validators over one shared context;
- which typed facts are persisted on `ApiMember` versus consumed only during
  projection;
- whether generated section bindings are justified after the first handwritten
  member-plan migration.

The answers must preserve the ownership and state boundaries above. They may
not reintroduce dual-use option state, string-keyed producer authority, or
parallel metadata validity rules.
