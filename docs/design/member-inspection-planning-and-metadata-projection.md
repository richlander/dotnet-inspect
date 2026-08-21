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
| Markout structural schema operations | [Schema query](schema-query.md) |
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
  -> authorized typed producer query plan
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
| Parsed intent | The user's gesture, selectors, requested projection, and output mode | No |
| Structural selection | Names resolved against one stated catalog | No |
| Target-resolution plan | Minimal acquisition and Metadata inventory needed to resolve the address | Only after preflight |
| Target resolution | The exact type and optional member target, or a typed diagnostic | No new work |
| Producer plan | Typed producer queries, prerequisites, costs, capabilities, and probe/render mode | No |
| Authorized plan | A target or producer plan accepted by host capability policy | Yes |
| Presentation plan | Sections, shape, columns/fields, and format applied to completed results | No new work |

No state is represented by mutating the preceding state. In particular,
`IncludeSections` is not both a record of user intent and permission to acquire
or analyze content.

### Canonical axis mapping

The existing section and schema terms compose as follows:

| Question | Owner | Answer | Executes section producers? |
| --- | --- | --- | --- |
| What names and fields exist? | Markout schema query | Structural schema | No |
| Which sections did the gesture place in scope? | Section selection over one catalog | Candidate sections | No |
| Which target/member does the address denote? | Authorized target-resolution plan and typed resolver | Resolved target or diagnostic | No |
| Which candidates apply to this target? | Typed query probe plan | Effective sections or typed failures | Only declared probes |
| Which producer results are required? | Typed query planner | Producer prerequisite closure | Not during planning |
| Which results can this format show? | Presentation plan and Markout | Rendered sections and shape | No new work |

Schema query never establishes effectiveness. Section selection never grants
producer authority. A probe result never chooses an output format. This table
is the canonical mapping when older command-specific documentation uses the
terms less precisely.

### Parsed intent

The CLI owns parsing and creates an immutable intent. Exact type names, dotted
type-or-member spellings, explicit member filters, overload ordinals, digests,
generic arity, section/category selectors, projection selectors, discovery
mode, and format remain distinct fields.

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
selection may request PDB and source-content capabilities, while detailed
verbosity may request PDB work but cannot request source-content fetch merely
because code promoted the effective verbosity.

Conceptually:

```text
ParsedInspectionIntent
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

Structural selection resolves exact names, aliases, categories, and globs
against one identified catalog. The result retains:

- the catalog identity and version;
- selector provenance;
- resolved stable section identities;
- unresolved selectors and typed suggestions;
- whether the gesture selected the complete catalog;
- the required output shape constraints.

Structural selection does not inspect a target and does not decide whether a
section has data.

The inventory and selected-overload catalogs are different catalogs. A result
from their temporary union is explicitly provisional and cannot be used for:

- single-section validation;
- table, count, value, path, URL, print, or document-shape validation;
- required verbosity;
- producer demand;
- capability authorization.

### Target and member resolution

Except for static schema, the parsed source/address first lowers into a minimal
target-resolution query plan. The normal preflight authorizes that plan from
the explicit source intent and host policy. It may acquire the selected
package, platform, project, or local target and produce the base Metadata API
inventory needed for lookup. It cannot authorize PDB, source-content, or
section-analysis augmentation.

The resulting inventory is resolved through existing typed identity owners.
Address resolution follows this matrix:

| Input state | Outcome |
| --- | --- |
| The complete type spelling resolves exactly | Select that type; do not peel an implied member |
| Complete type lookup misses and one legal trailing segment can be peeled | Resolve the prefix as the type and retain the suffix as implied member intent |
| Both an exact complete type and a prefix type/member pair exist | Exact complete type wins |
| Positional type and a qualified explicit member name the same canonical type | Merge their member intent |
| Positional type and a qualified explicit member name different types | Return a typed address-conflict diagnostic |
| Implied and explicit member selectors are structurally identical | Coalesce them |
| Distinct implied/explicit members target a multi-member-capable section | Retain the explicit member set |
| Distinct implied/explicit members target a single-member detail shape | Return a typed cardinality diagnostic |
| Overload, digest, or generic-arity components disagree | Return a typed selector-conflict diagnostic |

Selector-bearing suffixes are parsed before comparison. Display spelling is not
used to decide canonical type agreement.

The resolution uses existing typed identity owners:

- type lookup resolves the exact type or returns a typed diagnostic;
- `MemberTargetSelector` remains the member question;
- `MemberTargetResolver` returns `ResolvedMemberTarget` or a typed diagnostic;
- an implied dotted member is merged with explicit member intent before final
  structural selection.

The resolved target chooses the active section catalog. Selection is then
resolved once against that catalog and shape validation runs once against the
final section set.

Static schema discovery is the intentional exception: it performs no target
lookup, so a dotted spelling that could denote either an exact type or an
implied member remains on the inventory catalog. Static schema must not pretend
it learned which interpretation exists.

There is no `MemberSelectionNeedsFinalization` Boolean in the target model.
Code that requires resolved selection accepts only the resolved plan type.

### Producer execution plan

The resolved selection lowers into the typed query plan already defined by the
[inspection space](../inspection-space.md#query-plan). Each demanded producer
declares:

- typed inputs;
- prerequisite producers;
- cost;
- allowed execution modes;
- required capabilities;
- retained-resource lifetime;
- typed result and failure.

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
  -> AuthorizedInspectionPlan | CapabilityDenied
```

Only this preflight may mint an authorized plan, and the executor accepts only
an authorized plan. L3 parses the user's capability-bearing gesture, producers
declare requirements, and the host policy grants or denies them. Section
names, `MemberOptions`, and presentation state are not authorization inputs
after lowering to the typed plan. L2 contributes request provenance through a
typed section/query binding; it does not grant the capability.

This gives source and PDB acquisition one auditable rule:

```text
authorized =
    progressive-disclosure policy requests the capability
    from the gesture provenance and resolved query binding
    AND the selected producer declares the capability
    AND the execution mode permits that producer
    AND (the mode is not a probe
         OR the probe policy permits that producer)
    AND the host capability policy grants the capability
```

Cached or adjacent content does not bypass that rule. Availability is not
authority. Artifact-owner admission and query leases revalidate the authorized
plan's capabilities and source policy at content access; they are downstream
enforcement of the same authority, not a second grant mechanism.

### Discovery modes

Discovery mode is part of parsed intent and execution planning, not inferred
from a non-null `Discover` array.

| Gesture | Target resolution | Section producer execution | Capability effect |
| --- | --- | --- | --- |
| `-D --schema` | None | None | None |
| Type/member `-D` during compatibility migration | Minimal target-resolution plan | Cheap network-free applicability probes | Target acquisition plus cheap probe policy |
| Type/member `-D <section>` during compatibility migration | Minimal target-resolution plan | Section-specific applicability/render-manifest probe | Target acquisition plus section-specific probe policy |
| Future explicit effective discovery | Minimal target-resolution plan | Declared full applicability probes only | Target acquisition plus explicit full-probe policy |
| `-S <section>` render | Minimal target-resolution plan | Selected producer closure | Target acquisition plus explicit gesture capabilities |
| Verbosity render | Minimal target-resolution plan | Automatic base-section closure | Target acquisition plus behavior-safe defaults |

An effective probe and a render are distinct execution modes. A producer may
support both, one, or neither. A render-only or opt-in producer remains
structurally discoverable without being executed as a probe.

In particular, named discovery of `Source Locations`, `Original Source`, or
another source-backed section may describe its schema without acquiring a PDB
or source document when its section-specific probe is structural. A named
section may instead declare a bounded data-level probe needed to distinguish
valid-but-empty from unknown; that probe still cannot acquire capabilities the
discovery gesture does not request. If a future full effective-discovery
gesture is allowed to probe one of those sections, the producer declaration,
probe policy, gesture provenance, and host must all authorize it.

Type/member commands currently treat every non-schema `-D` as effective
discovery. The compatibility rows above preserve that behavior during the
planning migration. Converging those commands on the library command's
structural/effective split is a separate user-visible transition: it must
update the section model, progressive-disclosure guidance, command help, and
valid-but-empty diagnostics together. The planning migration does not silently
perform that transition.

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
completed results and cannot open an index, acquire a PDB/source document, or
start any undeclared producer. Existing producer calls reached while building
the manifest must move into the declared probe plan before the adapter is
considered compliant.

### Planning invariants

1. Selector resolution is deterministic for one catalog version.
2. Shape validation runs only on final resolved selection.
3. Static schema performs no target or producer work.
4. Effective discovery executes only probe-authorized producers.
5. Producer demand and capabilities come from the same typed plan.
6. Presentation cannot widen producer demand.
7. Target, selection, producer, and presentation failures remain distinct.
8. A typed diagnostic is not converted into an empty selected set.

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
- focused queries may choose a whole-query exception boundary, but the
  underlying rejection kind and rule are the same;
- reducing output fields does not skip validation needed for identity,
  inclusion, or count correctness.

Summary is allowed to avoid materializing rich display fields. It is not
allowed to own a reduced validity model.

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
FallbackRequired(stable-reason, contained-declaration)
Degraded(signature-status, bounded-nonauthoritative-evidence)
Unavailable(metadata-declaration-failure)
```

Complete declarations may become `Representable` or `FallbackRequired`.
`FallbackRequired` requires complete contained identity and signature facts.
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

One operation-scoped projection budget reaches every nested name, signature,
custom-modifier, accessor, and `MethodImpl` decode required by the projection.

The owner charges before materialization. A cache may avoid repeated decode
work only when:

- its key includes every semantic decode context;
- the retained value was already charged to this operation or belongs to a
  separately budgeted immutable session result;
- cache reuse cannot turn a previously rejected operation into success;
- failure results are cached with the same context as successful results.

Full, summary, and focused projections may request different retained fields.
They do not receive different hostile-input ceilings for equivalent work.
These cache and equivalent-ceiling properties are unverified until `MDP009`
lands.

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

AuthorizedInspectionPlan
  resolved plan + query requirements + mode + host policy
  -> executable producer closure or typed denial

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
| Local source/PDB authorization checks derived from selected sections | Replace with producer-plan authorization |
| Render-manifest effective discovery | Retain for post-producer field/column/empty observation; move every producer call into a declared probe plan |
| Shared Metadata validators already used by every projection | Retain |
| Validators duplicated across full, summary, focused, or C# paths | Move to the Metadata declaration owner |
| C# fixes based on Metadata-owned typed semantics | Retain |
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
- Add parity fixtures that run the same declarations through full, summary,
  and focused projections.
- Make no behavior change.

Exit gate: the characterization fails when any currently observed execution
path is absent from the matrix.

### Slice 2: introduce parsed and resolved plan types

Depends on: slice 1.

- Parse type/member gestures into immutable intent.
- Resolve the active catalog only after target/member resolution.
- Move shape validation to the resolved plan.
- Preserve current address precedence and diagnostics through a compatibility
  adapter.
- Keep an adapter to current command execution while behavior remains
  byte-for-byte stable.

Exit gate: every type/member gesture produces one resolved plan or typed
diagnostic before command execution.

### Slice 3: enforce the address-resolution contract

Depends on: slice 2.

- Implement the exact-type, fallback-peel, dual-success, qualification, and
  selector-conflict matrix.
- Return typed conflict/cardinality diagnostics rather than silently combining
  incompatible address components.
- Update command help and compatibility tests for every intentional diagnostic
  change.

Exit gate: `MIP006` covers the full matrix, including exact-type/member
dual-success and conflicting positional/qualified types.

### Slice 4: lower resolved selection to typed producer plans

Depends on: slices 2 and 3.

- Bind member sections to typed query definitions.
- Preflight cost and capability authorization.
- Remove source/PDB/analysis authorization inferred from `IncludeSections`.
- Move every producer reached by render-manifest discovery into the declared
  section-specific probe plan; retain the manifest only as a post-producer
  observation adapter.
- Keep rendered output unchanged.

Exit gate: the executor accepts only the authorized plan and the producer trace
matches the resolved selection matrix. `MIP004` and `MIP005` must pass before
this slice lands.

### Slice 5: introduce Metadata declaration scaffolding

Depends on: slice 1. It may develop in parallel with slices 2 and 3 but cannot
be consumed by the type/member plan before slice 4 lands.

- Centralize operation budgets and reader-local caches.
- Add typed type, member, accessor, and `MethodImpl` validation results.
- Run characterization or shadow comparison without changing which validity
  implementation supplies product results.
- Do not activate the new admission semantics for only one projection path.

Exit gate: cache-context declarations drive `MDP009`, and shadow results expose
all full/summary/focused disagreements before cutover.

### Slice 6: activate shared declaration admission atomically

Depends on: slice 5.

- Route full, summary, and focused declaration admission through the same
  six-stage decision in one slice.
- Make summary counts consume accepted declaration identities.
- Make focused queries consume the same validators with their own inclusion
  policy and failure boundary.
- Delete path-local validity checks after parity gates pass.

Exit gate: accepted identity and rejection-rule sets agree across full,
summary, and focused projections for equivalent inclusion policies. `MDP001`
through `MDP004` must pass before this semantic cutover lands.

### Slice 7: migrate C# representability

Depends on: slice 6.

- Carry typed accessor and slot semantics into the API/C# boundary.
- Remove inference from raw flag combinations.
- Preserve valid metadata fallback, degraded-signature non-success, and strict
  failure containment.

Exit gate: CSharp consumes the typed representability outcome and contains no
raw-metadata relationship reconstruction. `MDP005`, `MDP007`, and `MDP010`
must pass.

### Slice 8: remove transitional state

Depends on: slices 4, 6, and 7.

- Remove provisional selection mutation and dual-use option fields.
- Remove duplicate Metadata validators and compatibility branches.
- Update architecture docs from proposed to implemented only after the
  corresponding gates pass.

Exit gate: targeted searches and architecture tests find no dual-use option
authority, duplicate declaration validity owner, or CSharp metadata
reconstruction.

## Verification obligations

The target contract in this document is **unverified** until the migration
gates below land. Existing tests and the paused stack's review reproductions are
evidence for the problem, not proof of the target architecture.

Gate IDs are stable design references. Implementations may use a more specific
test method name, but the PR must map each test to its gate ID.

| Gate | Property | Required evidence |
| --- | --- | --- |
| `MIP001` | Static schema runs no acquisition/producers and non-schema discovery runs only mode-declared probes | Separate target-resolution and section-producer trace equality for every actual type/member discovery gesture |
| `MIP002` | Named source discovery cannot acquire PDB/source | Existing `Member_SourceLocations_Discovery_DoesNotAcquirePdb` plus equivalent authored-source coverage |
| `MIP003` | Provisional catalogs cannot satisfy shape validation | Close-negative tests for exact type, implied member, mixed filters, globs, and categories |
| `MIP004` | Producer demand equals authorization | Exhaustive gesture-provenance/query-requirement/host-policy matrix, preflight-before-execution assertions, and artifact-owner lease revalidation |
| `MIP005` | Presentation cannot widen work | A non-vacuity test that fails when render-manifest or ordinary rendering starts an undeclared producer |
| `MIP006` | Address resolution is deterministic and conflict-bearing | Exact type, fallback peel, dual-success, qualified/positional conflict, implied/explicit member, overload, digest, and arity matrix |
| `MIP007` | L1 member execution remains content-shaped and owner-authorized | Architecture closure plus admission/query-lease tests proving no readable path or descriptor bypass |
| `MIP008` | The plan executes sequentially without filesystem assumptions | Browser/Wasm host test over in-memory content with the same producer trace and failures |
| `MIP009` | The path remains NativeAOT-friendly, SRM-only, Roslyn-free, and load-free | NativeAOT publish/run plus dependency and inspected-assembly-loading architecture gates |
| `MIP010` | Typed planning and resolution failures stay visible | Outcome tests proving diagnostics cannot become empty selection, empty results, or success exit |
| `MDP001` | Full/summary/focused validity agrees | Set equality over accepted identities and typed rejection rule IDs |
| `MDP002` | Declaration admission order is preserved | Direct-invalid, charged name/attribute exclusion, excluded-hostile, public/non-public accessor dependency, retained-rejected, aggregate-rejected, and valid-empty fixtures |
| `MDP003` | Cheap filtering precedes hostile MethodImpl projection | Large excluded-row fixture with bounded allocation/work evidence |
| `MDP004` | Accessor validity is shared | Property/event fixtures covering accessibility, staticness, abstraction, virtuality, slot, and body close negatives |
| `MDP005` | CSharp consumes typed slot semantics | Compiler-produced interface re-abstraction/default-implementation compile-back plus class base-slot rejection |
| `MDP006` | Decode accounting is transitive | Amplification fixtures for names, modifiers, signatures, accessors, and MethodImpl targets |
| `MDP007` | Failure text contains no artifact data | Hostile control-character names across Metadata, C#, and CLI failure paths |
| `MDP008` | Real artifacts remain stable | Pinned platform and package canaries with recorded rows and retained-text totals |
| `MDP009` | Declaration caches preserve context, budget, and failure semantics | Declaration-driven cache-key set equality, cached-work budget rejection, and same-context negative-result caching |
| `MDP010` | Degraded signatures remain nonauthoritative at the CSharp boundary | Existing degraded-signature fixtures mapped to typed `Degraded` outcomes with no authoritative C# or metadata fallback |

Contract tests should derive their cases from the declaration or section
catalog where practical, so a new mode or validator cannot silently avoid the
matrix.

### Known unverified claims

| Claim | Owner | Gate |
| --- | --- | --- |
| Type/member producer capability preflight is the only execution authority | Typed query executor | `MIP004` |
| Discovery catalog and producer traces agree for every mode | Section/query plan integration | `MIP001`, `MIP003` |
| Renderer code cannot trigger acquisition or analysis | Presentation boundary | `MIP005` |
| Target/member execution preserves content, lease, host, and platform boundaries | Query/acquisition integration | `MIP007`, `MIP008`, `MIP009` |
| Planning and address failures cannot become ordinary absence | Planning/resolution boundary | `MIP006`, `MIP010` |
| One declaration admission decision governs all API projections | Metadata | `MDP001`, `MDP002` |
| Excluded hostile rows cannot amplify expensive projection | Metadata | `MDP003`, `MDP006` |
| Valid metadata and C# fallback remain distinct from degraded or invalid input | Metadata/CSharp boundary | `MDP004`, `MDP005`, `MDP007`, `MDP010` |
| Cache reuse cannot bypass context or operation budgets | Metadata declaration session | `MDP009` |

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

Rejected. The inventory and selected-overload pipelines expose different
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
