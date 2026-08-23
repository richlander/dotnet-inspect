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
[bounded metadata traversal](bounded-metadata-traversal.md) decisions to the
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
- Validate metadata declaration facts once per operation and reuse them across
  output projections.
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
discovery mode, and format remain distinct fields. A `type` surface with `-m`
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
| Type/member list | `ApiMemberSectionDescriptors` over one `ApiType` | Resolved exact type, including the type surface with `-m` filters |
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
| Explicit `type` surface with a resolved exact type, with or without `-m`/kind filters | Select the type/member-list catalog and preserve type-view filter semantics |
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
section catalog; raw target count does not. A type gesture with `-m` stays on
the type/member-list catalog. A member bare name that resolves to one overload
remains an overload inventory unless canonical exact-member demand promotes
it. A type glob or prefix browse stays on the assembly-type-list catalog even
when it produces one row. Final selection and shape validation run once
against the chosen catalog.

Static schema discovery is the intentional exception: it performs no target
lookup. An explicit `type` surface with no exact address or with list syntax
reports the assembly-type-list schema; one with syntactic exact-type intent
reports the type/member-list schema without attempting a lookup-based prefix
fallback. An explicit `member` surface reports the overload-inventory or
detail schema from its syntactic selector kind.

Commandless static schema is a structural route query over every view the
hidden router can choose, not only over command names or the four type/member
catalogs. A destination command is not a catalog identity: the package command
can render its package view, one embedded library, or an all-libraries
aggregation. The CLI therefore owns a closed structural-view registry:

| Structural view | Destination command | Static catalog owner |
| --- | --- | --- |
| Package inspection | Package | `PackageSectionDescriptors` |
| Package single-library | Package, then library adapter | `LibrarySections` |
| Package all-libraries | Package aggregation | `LibrarySections` |
| Direct library | Library | `LibrarySections` |
| Type list or exact type | Type | `ApiTypeSectionDescriptors` or `ApiMemberSectionDescriptors` |
| Member inventory or detail | Member | `ApiMemberOverloadSectionDescriptors` or `ApiMemberDetailSectionDescriptors` |

Each entry declares its syntax marker, precedence, destination command, view
mode, and catalog identity. The declarations are shared by
`ArgumentPreprocessor`, `RouterTokenRewriter`, and the destination command's
post-parse structural classifier; a command rewrite cannot silently change the
view. Declaration order preserves the realized syntax precedence: file forms,
explicit member selectors, package-scoped `--library` or `--all-libraries`,
package-plus-type forms, and package-version forms are classified before any
lookup. In particular, `--library` and `--all-libraries` select library catalog
views even though execution enters `PackageCommand`.

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

The query execution owner exposes one conceptual preflight:

```text
Preflight(
    parsed gesture capability-request provenance,
    resolved typed query plan,
    execution mode,
    host capability policy)
  -> PreflightedInspectionPlan | PlanDenied(typed-reason)

PreflightedInspectionPlan
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
the preflighted plan. A plan-level denial applies to mandatory target or common
producer work. Independent section demands and conditional successors retain
their own authorized or denied disposition. L3 parses the user's
capability-bearing gesture, producers declare requirements, and the host policy
grants or denies them. Section names, `MemberOptions`, and presentation state
are not authorization inputs after lowering to the typed plan. L2 contributes
request provenance through a typed section/query binding; it does not grant
the capability.

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

## Boundary two: validated metadata declaration projection

### Owner and scope

`ILInspector.Metadata` owns declaration validity and metadata-derived facts.
The operation is reader-local and bounded. It does not create a repository-wide
normalized metadata graph.

A Metadata declaration session owns:

- one live `MetadataReader`;
- operation-scoped work, item, text, and decode budgets;
- reader-local caches keyed by handle plus required generic or resolution
  context;
- stable validation policy;
- typed failure construction.

The session exposes declaration operations. It does not expose its reader or
mutable budget object to higher layers.

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

Stage 3 validates only dependencies required to decide admission of the root
declaration:

- a property/event composes the stage 1-2 results of its getter, setter, add,
  remove, and raise methods before its own inclusion decision;
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
| Accessor aggregate | Per-accessor accessibility, staticness, virtuality, abstraction, final/new-slot shape, body presence |
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
retains enough signature information to distinguish overloads and indexers;
dropping parameters or collapsing accessor identity is not a fallback.

Fallback rendering treats every artifact-authored name and signature fragment
as inert data through the existing
[InertText](inert-text.md) boundary. Identity and comparison use the original
typed facts; presentation applies the sink's closed `TextPolicy` and carries
the result as `InertString`. `VisualEncoder` re-spells refused scalars in its
canonical, total, injective, lossless, and invertible form. Removal,
replacement, or a fallback-local escape vocabulary is forbidden because it
can collapse distinct declaration identities. Format-specific Markdown, JSON,
TSV, or other structural escaping runs after visual encoding and does not
replace it. Strict diagnostics do not echo the artifact payload. `MDP015` is
the gate for contained fallback identity, signature discrimination, and inert
rendering.

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
- the retained value was already charged to this operation or belongs to a
  separately budgeted immutable session result;
- cache reuse cannot turn a previously rejected operation into success;
- failure results are cached with the same context as successful results.

Full, summary, and focused projections may request different retained fields.
They do not receive different hostile-input ceilings for equivalent work.
Equivalent work means the same metadata root and reader, generic decode
context, inclusion policy, admission dependency closure, and requested shared
fact. Its per-dimension threshold, consumed work through that fact, and
rejection rule are equal across projections. Additional retained fields may
charge separately declared dimensions and are not required to produce equal
total operation counters.
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
| `ArgumentPreprocessor`, `RouterCommandDefinition`, and `PackageCommand` structural routing | Retain syntactic routing, but replace command-only dispatch with the shared structural-view registry and move static classification before acquisition in slice 2 |
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
  overload-inventory, exact-member-detail, and hidden-router route in that
  matrix.
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
  and direct `.nupkg --library` preprocessing return the `LibrarySections`
  static schema before package resolution or extraction.
- Resolve the active catalog only after target/member resolution.
- Move shape validation to the resolved plan.
- Preserve current address precedence and diagnostics for non-static execution
  through a compatibility adapter.
- Intentionally replace commandless static-schema resolution notes and
  target-chosen catalogs with deterministic syntax-only catalogs or labeled
  alternatives; update command help and compatibility tests in this slice.
- Intentionally add target-free static schema for package single-library and
  all-libraries views that currently defer or reject discovery; preserve their
  render behavior and document the new structural query.
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
exact-section/alias/category/glob detail promotion, explicit `type -m` versus
`member` compatibility, static-schema alternatives, and conflicting
positional/qualified and qualified/qualified types.

### Slice 4: lower resolved selection to typed producer plans

Depends on: slices 2 and 3.

- Bind member sections to typed query definitions.
- Preflight cost, capability, execution-mode, and probe-policy authorization
  for common work, independent section closures, and conditional successors.
- Remove source/PDB/analysis authorization inferred from `IncludeSections` or
  from the union of `Discover` selections into requested render sections.
- Move every producer reached by render-manifest discovery into the declared
  section-specific probe plan; retain the manifest only as a post-producer
  observation adapter.
- Update `schema-query.md` realized type/member discovery mechanics to name the
  declared probe plan rather than render-manifest-owned producer execution.
- Keep ordinary rendered output unchanged.

Exit gate: the executor accepts only the preflighted plan and the producer trace
matches the resolved selection matrix; named/category source discovery neither
acquires source capabilities nor becomes empty success, denied discovery
closures remain per-section unknowns, denied render closures remain failures,
and no executable path retains another authorization mint. `MIP002`, `MIP004`,
`MIP005`, `MIP007` through `MIP011`, and `MIP013` must pass before this slice
lands.

### Slice 5: introduce Metadata declaration scaffolding

Depends on: slice 1. It may develop in parallel with slices 2 and 3 but cannot
be consumed by the type/member plan before slice 4 lands.

- Centralize operation budgets and reader-local caches.
- Add typed type, member, accessor, and `MethodImpl` validation results.
- Run characterization or shadow comparison without changing which validity
  implementation supplies product results.
- Do not activate the new admission semantics for only one projection path.

Exit gate: cache, context, and hostile-input limit declarations drive `MDP009`;
shadow results expose full/summary/focused limit, rejection, and projection
disagreements before cutover.

### Slice 6: activate shared declaration admission atomically

Depends on: slice 5.

- Route full, summary, and focused declaration admission through the same
  six-stage decision in one slice.
- Make summary counts consume accepted declaration identities.
- Make focused queries consume the same validators with their own inclusion
  policy and failure boundary.
- Delete path-local validity checks after parity gates pass.

Exit gate: accepted identity, rejection-rule, and shared-fact result sets agree
across full, summary, and focused projections for equivalent inclusion
policies, and none of those entry points retains a parallel admission owner.
`MDP001` through `MDP004`, `MDP006` through `MDP009`, and `MDP011` must pass
before this semantic cutover lands.

### Slice 7: migrate C# representability

Depends on: slice 6.

- Carry typed accessor and slot semantics into the API/C# boundary.
- Remove inference from raw flag combinations.
- Make `FallbackRequired` distinguish contained type and member declarations,
  preserve complete discriminating identity and signature/header facts, and
  render artifact-authored text inertly.
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
- Update architecture docs from proposed to implemented only after the
  corresponding gates pass.
- Update `schema-query.md` and the other mechanism owners to remove superseded
  transitional type/member discovery descriptions.

Exit gate: targeted searches and architecture tests find no dual-use option
authority, duplicate declaration validity owner, or CSharp metadata
reconstruction, including inert compatibility state left after the semantic
cutovers. `MIP012` and `MDP013` must pass.

## Verification obligations

The target contract in this document is **unverified** until the migration
gates below land. Existing tests and the paused stack's review reproductions are
evidence for the problem, not proof of the target architecture.

Gate IDs are stable design references. Implementations may use a more specific
test method name, but the PR must map each test to its gate ID.

| Gate | Property | Required evidence |
| --- | --- | --- |
| `MIP001` | Static schema chooses only syntax-proven structural views and runs no target or producer work | Declaration-derived mapping equality between every preprocessor/rewrite/parsed view route and its structural-view registry entry, including precedence, destination command, view mode, and catalog identity; explicit and commandless package `--library`, package `--all-libraries`, and direct `.nupkg --library` cases return `LibrarySections` before resolution/extraction; explicit package/library/type/member gestures prove their deterministic command-owned catalogs; commandless package-version, library-file, member-selector, and package-plus-type forms prove syntax-only precedence; ambiguous `System.Text.Json` and `System.String.Substring` forms return labeled package/library/type/member catalog alternatives with per-alternative selector results; a close-negative fails if platform resolution, facade classification, package existence, all-framework search, acquisition, type/member lookup, or any section producer begins, and asserts that no resolution note is emitted |
| `MIP002` | Named/category type/member source discovery cannot read/acquire PDB/source content or confuse unknown with empty | Overload-qualified `-D "Source Locations"`, `-D "Original Source"`, `-D "Source Diff"`, and source-category cases proving no `LocalPdbRead`, `PdbAcquire`, or `SourceContent`; paired genuinely-empty and PDB-required fixtures produce distinct `ValidEmpty` and `Unknown(CapabilityNotRequested)`, while plain library discovery retains its bounded `LocalPdbRead` positive and close-negative gates |
| `MIP003` | Demand classification, provisional catalogs, and static alternatives cannot satisfy final shape validation | Close-negative tests for exact type, implied member, mixed filters, aliases, globs, categories, `@All`, and commandless structural alternatives; declaration-derived set equality requires one canonical target requirement for every stable identity registered in multiple catalogs and rejects conflicting declarations |
| `MIP004` | Closed producer paths equal preflighted authorization | Declaration-derived gesture-provenance/query-requirement/host-policy matrix; unconditional prerequisite closure; conditional local-PDB hit, unrequested/denied miss, and authorized acquisition paths; transitive cost, execution-mode, and probe-policy closure; a probe-capable producer with a render-only prerequisite mapping to per-section `Unknown`; explicit-render denial; preflight-before-execution assertions; and artifact-owner lease revalidation |
| `MIP005` | Presentation cannot widen work | A non-vacuity test that fails when render-manifest or ordinary rendering starts an undeclared producer |
| `MIP006` | Address and catalog resolution are deterministic, diagnostic, and surface-preserving | The slice-2 structural-view mapping remains closed; set equality between the type/member catalog/route registry and its four realized pipeline owners plus every entry route; exact type, fallback peel, dual-success, qualified/positional conflict, same-type and conflicting qualified/qualified selectors, identical/complementary/conflicting implied-explicit refinement, per-filter bare-name/glob outcomes, partial misses, overlapping-filter deduplication, zero/one/multiple inventory results, exact selector success/failure, explicit `type -m` versus `member` catalog/output compatibility, surface/route/selector-driven assembly-type-list/type-member-list/overload-inventory/detail catalogs, targetless/glob/failed-exact/platform-prefix list routes, cross-catalog `MemberSet` and `ExactMember` identities, exact-section/alias/category/glob detail promotion, commandless static alternatives, unavailable detail sections, and overload/digest/arity cases |
| `MIP007` | L1 member execution remains content-shaped and owner-authorized | Architecture closure plus admission/query-lease tests proving no readable path or descriptor bypass |
| `MIP008` | The plan executes sequentially without filesystem assumptions | Browser/Wasm host test over in-memory content with the same producer trace and failures |
| `MIP009` | The path remains NativeAOT-friendly, SRM-only, Roslyn-free, and load-free | NativeAOT publish/run plus dependency and inspected-assembly-loading architecture gates |
| `MIP010` | Typed planning, resolution, policy, producer, and observation failures stay visible | Outcome and injected-failure tests for every stage, including per-section request/capability/cost/mode/probe-policy `Unknown`, explicit-render policy denial, exact/category network-free discovery-probe failure, and authorized render/effective PDB/source producer failure, proving failures cannot become empty selection, `ValidEmpty`, empty results, or success exit |
| `MIP011` | Host preflight is the only executable authorization mint at the slice-4 cutover | Declaration-driven architecture closure over every type/member entry point, descriptor, prerequisite, and executor call site; fail on another mint or an option value read as execution authority |
| `MIP012` | No transitional dual-use planning/authorization state remains | Declaration-driven closure over option fields, compatibility adapters, descriptors, and executors after slice 8 |
| `MIP013` | Non-schema discovery runs only mode-declared probes | Separate target-resolution and section-producer trace equality for every actual type/member discovery gesture; every started producer is declared for the effective probe mode, and every declared denial remains a typed per-section `Unknown` |
| `MDP001` | Full/summary/focused validity and shared semantic facts agree | Set equality over accepted identities and typed rejection rule IDs; set equality between typed projection fact requests and registered consumers; declaration-derived equality of typed values or typed rejections for every shared fact requested by multiple projections; declared summary erasure is checked only after parity |
| `MDP002` | Declaration admission order is preserved | Direct-invalid, charged name/attribute exclusion, excluded-hostile, public/non-public accessor dependency, retained-rejected, aggregate-rejected, and valid-empty fixtures |
| `MDP003` | Cheap filtering precedes hostile MethodImpl projection | Large excluded-row fixture with bounded allocation/work evidence |
| `MDP004` | Accessor validity is shared | Property/event fixtures covering accessibility, staticness, abstraction, virtuality, slot, and body close negatives |
| `MDP005` | CSharp consumes typed slot semantics | Compiler-produced interface re-abstraction/default-implementation compile-back plus class base-slot rejection |
| `MDP006` | Decode accounting is transitive | Amplification fixtures for names, modifiers, signatures, accessors, and MethodImpl targets |
| `MDP007` | Metadata and CLI failure text contains no artifact data | Hostile control-character names across Metadata declaration and CLI failure paths |
| `MDP008` | Real artifacts remain stable | Pinned platform and package canaries with recorded rows and retained-text totals |
| `MDP009` | Declaration caches and hostile-input ceilings preserve context, budget, and failure semantics | Matrix derived from the central safety-policy dimensions and every charging fact request; cache-key set equality, cached-work rejection, same-context negative caching, no undeclared local ceiling, and equivalent near/over-limit full/summary/focused fixtures asserting equal per-fact thresholds, counters, and rejection rules while separately charging additional retained fields |
| `MDP010` | Degraded signatures remain nonauthoritative at the CSharp boundary | Existing degraded-signature fixtures mapped to typed `Degraded` outcomes with no authoritative C# or metadata fallback |
| `MDP011` | Shared declaration admission has no parallel owner at the slice-6 cutover | Declaration-driven architecture closure over full, summary, and focused entry points; fail on a bypass or duplicate validity implementation |
| `MDP012` | CSharp representability consumes only Metadata-owned semantic facts at the slice-7 cutover | Closure derived from the semantic fact types and every CSharp representability entry point; fail on direct `MetadataReader`/handle reconstruction or relationship decisions from raw accessibility, virtuality, new-slot, `MethodImpl`, or equivalent Boolean combinations |
| `MDP013` | No transitional declaration-validity or CSharp reconstruction state remains | Declaration-driven closure over compatibility adapters, validators, raw semantic fields, and consumers after slice 8 |
| `MDP014` | CSharp failure text contains no artifact data | Hostile control-character names through every CSharp representability failure path |
| `MDP015` | `FallbackRequired` preserves contained type/member identity and renders artifact text through `InertString` | Types retain namespace/nesting/name, kind/arity, accessibility/modifiers, constraints, base, interfaces, and kind-specific header facts; an unsupported-base/interface type-shell fixture proves a valid unrepresentable fact becomes typed fallback instead of `null` or omission; methods, properties, events, accessors, overloads, and indexers retain discriminating declaring-type/member/signature facts; paired indexers with different parameter types remain distinct; declaration-derived sink closure accepts only policy-produced `InertString`; round-trip and pairwise injectivity fixtures for hostile type names, member names, and signature fragments prove canonical visual encoding preserves the exact artifact text while emitting no live controls, with format-specific structural escaping applied afterward |

Contract tests should derive their cases from the declaration or section
catalog where practical, so a new mode or validator cannot silently avoid the
matrix.

### Known unverified claims

| Claim | Owner | Gate |
| --- | --- | --- |
| Type/member producer capability preflight is the only execution authority | Typed query executor | `MIP004`, `MIP011`, `MIP012` |
| Static catalogs and non-static producer traces agree with their modes | Section/query plan integration | `MIP001`, `MIP003`, `MIP013` |
| Discovery applicability, valid-empty, unknown, and failure remain distinct | Section/query plan integration | `MIP002`, `MIP010` |
| Renderer code cannot trigger acquisition or analysis | Presentation boundary | `MIP005` |
| Target/member execution preserves content, lease, host, and platform boundaries | Query/acquisition integration | `MIP007`, `MIP008`, `MIP009` |
| Planning and address failures cannot become ordinary absence | Planning/resolution boundary | `MIP006`, `MIP010` |
| No dual-use option or alternate authorization mint survives migration | Planning/execution architecture | `MIP011`, `MIP012` |
| One declaration admission decision and one shared semantic-fact owner govern all API projections | Metadata | `MDP001`, `MDP002`, `MDP011`, `MDP013` |
| Excluded hostile rows cannot amplify expensive projection | Metadata | `MDP003`, `MDP006` |
| Valid metadata and complete, contained C# fallback remain distinct from degraded or invalid input | Metadata/CSharp boundary | `MDP004`, `MDP005`, `MDP010`, `MDP012`, `MDP014`, `MDP015` |
| Cache reuse cannot bypass context or operation budgets | Metadata declaration session | `MDP009` |
| No duplicate validity owner or CSharp raw-metadata/raw-flag reconstruction survives migration | Metadata/CSharp architecture | `MDP011`, `MDP012`, `MDP013` |

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
