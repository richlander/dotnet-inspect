# Inspect Web Type leverage

## Status

This document is the normative owner for the Inspect Web Type-leverage
experience. It owns automatic Browser acquisition, exact-generation
composition, transport and cache identity, visible state transitions, and the
Type-inventory presentation of structural poles.

The host-neutral meanings of namespace leverage, Type degree, roles,
designation eligibility, cohorts, poles, evidence qualification, and
signature/body comparison are owned by
[Library structural report](library-structural-report.md#namespace-and-type-structural-leverage).
This design consumes those owner-issued results without recomputing or merging
their evidence.

## Claim

The Type inventory can show two independent structural perspectives for one
Type:

- **surface leverage** is the Research signature-evidence pole; and
- **implementation leverage** is the Research body-use-evidence pole.

A Type shows zero, one, or two pole glyphs. When both evidence modes issue a
pole, Inspect Web shows both. This includes two poles in the same direction and
opposing poles. Agreement is not collapsed into one stronger cue, and
disagreement is not suppressed or demoted. The two results answer different
questions and their co-occurrence is itself useful evidence.

The Browser never derives a pole, degree, role, cohort, or eligibility decision.
It joins owner-issued results to visible Types by exact Type-definition
identity. A missing, rejected, failed, or qualified evidence mode remains
visible and never becomes a successful empty result.

## Adjacent owners

This owner composes existing contracts by their issued currencies:

- `docs/inspection-space.md` owns selected participants, immutable image
  snapshots, and assembly-context outcomes.
- `docs/design/library-structural-report.md` owns both leverage evidence modes
  and their MVID-plus-assembly-identity join.
- `docs/design/analysis-library-body-use.md` owns typed body-operand decoding,
  logical-owner attribution, body-use coverage, and limits.
- `docs/design/inspect-web-jsexport-partitioning.md` assigns this result to the
  Analysis facade.
- `docs/design/ts-jsexport.md` owns source-generated C#-to-TypeScript wire
  contracts.
- `docs/design/inspect-web-operation-authority.md` owns current-view operation
  identity, replacement, stale-publication suppression, and quiescence.
- `docs/design/progressive-disclosure.md` owns disclosure of unbounded work.

This design transfers one claim to Queries: a **Library Type-leverage query**
issues the exhaustive surface document and, when an implementation participant
exists, every body-use Type-leverage shard from one immutable implementation
image snapshot. It acquires each exact-namespace Metadata inventory once and
uses that same inventory for both Research producers. The query does not add a
decoder, relationship index, Graph algorithm, or call-graph acquisition.

The existing `AssemblyContextLibrarySurfaceLeverageQuery` remains the cheap
signature-only query. The production Browser query composes it only for a
reference-only Library; an implementation Library uses the composite query so
surface and implementation evidence share one exact generation without
duplicating Metadata work.

## Supported scope

The production experience is the Type inventory for a package or platform
Library represented in the retained Inspect Web workspace. Uploaded standalone
Libraries are outside this slice because they do not participate in the same
retained package/platform workspace path.

Every non-forwarded Type in the selected Library remains a possible join
target. Accessibility filtering affects which rows are painted, not the
Research topology or acquisition population. Forwarded Types have no local
definition identity and receive no leverage cue.

Surface leverage retains the exhaustive namespace index and signature Type
shards. Implementation leverage has no namespace index; it contributes only
body-use Type shards. Namespace top-leverage presentation therefore remains a
surface-evidence cue.

## Activation and cost

The Browser first paints the Type inventory from the package or platform
surface. After that paint, it automatically requests one Library Type-leverage
document for every Library represented in the active Type inventory. Changing
Type, namespace, accessibility, kind, trait, or text filters issues no new
request. Re-entering the same exact workspace reads the retained result.

The query declares `InspectionCost.Unbounded`. Signature acquisition scans
Library metadata; implementation acquisition additionally decodes the whole
implementation Library's method bodies under the existing body-use limits.
For this consumer only, entering the Type inventory for concrete Libraries is
the explicit depth request, by operator decision. There is no salience button,
toggle, or filter.

The managed work runs synchronously in the ordinary single-threaded Browser
Worker. It does not block the page thread, but another ordinary-Worker request
waits for a running leverage query to finish and cancellation cannot interrupt
the managed scan. The Browser admits at most one request per exact Library key
and drops stale publication through operation authority. A future cooperative
body traversal may improve responsiveness but does not change this contract.

The top-ten package census is diagnostic feasibility evidence only. This design
makes no runtime-performance claim from CoreCLR timing. The production
System.Text.Json experience and the Browser regression gates below establish
correct behavior, not a universal latency bound.

## Query composition and availability

One composite result contains two independently settled channels:

1. The **surface channel** carries the exhaustive signature document.
2. The **implementation channel** carries every body-use Type shard or one
   explicit unavailability.

For a Library with an implementation participant, Queries opens one immutable
implementation snapshot. It acquires the whole-Library signature result,
acquires each exact-namespace signature result once, and runs one whole-Library
body-use analysis over that same snapshot. Research constructs surface and body
shards from the shared exact-namespace inventories. MVID and assembly identity
must correspond before body shards can be issued.

The complete body-use occurrence population is partitioned by exact endpoint
namespace in one pass before per-namespace Graph execution. Namespace count
must not multiply traversal of that whole-Library population.

For a reference-only package Library, the surface channel uses the selected
surface participant and the implementation channel settles as
`NoImplementationAssembly`. Surface poles remain available. A body-use
rejection, failure, qualification, or empty pole set likewise does not suppress
or qualify the surface channel. Surface acquisition failure prevents Type
leverage publication because the canonical Type inventories needed by both
channels are unavailable.

An available channel carries its own methodology version, evidence mode,
qualification, coverage, diagnostics, shards, complete owner-issued orders, and
pole designations. An unavailable channel carries a typed failure kind and
detail. The wire does not use absent arrays to represent failure.

## Browser wire identity

The Analysis facade lowers the composite query result to one source-generated,
assembly-local document. The document has a schema version and two named
channels. Each channel carries:

- `signature` or `body-use` evidence mode;
- available, unavailable, or failed outcome;
- methodology version when available;
- the surface namespace index when applicable;
- exact-namespace shards in owner-issued order;
- exact Type-definition IDs and display strings;
- incoming and outgoing degree, role, eligibility, pole, and complete
  owner-issued pole orders; and
- mode-specific coverage, diagnostics, and failure.

The facade may normalize the signature and body row shapes into common incoming
and outgoing degree fields only because the evidence mode remains attached to
the enclosing channel. It does not compare or combine those values.

The Browser validates the schema, methodology, evidence mode, namespace
coverage, exact shard order, pole-order identities, and duplicate Type
identities before publication. Malformed transport fails visibly rather than
publishing a partial map.

## Cache and publication authority

One cache entry contains the complete two-channel document. Its exact Library
key includes:

- methodology and wire-schema version;
- retained workspace generation;
- package ID, package version, target framework, and Library asset identity; or
- platform framework, platform version, pack, and assembly file name.

The key does not include Type, namespace, accessibility, or inventory filters.
Available, qualified, reference-only, rejected, and failed settled results are
terminal for that key. In-flight work is single-flight.

An explicit retry replaces the terminal entry for the same key and reruns both
channels against the workspace's current immutable generation. This deliberate
small duplication keeps one atomic exact-generation result; navigation never
retries automatically. Operation authority prevents an older generation or
superseded Library from publishing into the current inventory while allowing
its settled cache entry to remain associated with its own key.

## Presentation

Leverage is an automatic icon-only Type-row affordance:

- an outline wave or mountain glyph means a surface pole;
- a filled wave or mountain glyph means an implementation pole; and
- color continues to distinguish sea level from mountain peak.

Shape/fill distinguishes evidence mode without relying on color. The accessible
label and tooltip name both mode and pole, for example `surface sea level` or
`implementation mountain peak`.

When both channels issue a pole, the surface glyph precedes the implementation
glyph. An API-difference glyph, when present, follows them. This fills the
existing three-slot Type achievement rail without overflow. Same-direction
poles use distinct achievement kinds, so the rail retains both rather than
rejecting them as duplicates.

Pole cues add no row tint, badge, section, summary count, control, or filter.
The existing namespace top-leverage glyph remains a surface namespace cue.
Qualified cues stay visible. Qualification, failure, and Retry use the transient
feedback presentation owned by
[Data bar and Diagnostics](inspect-web-surface-composition.md#data-bar-and-diagnostics),
so diagnostics never reduce the Type inventory's space. An unavailable
implementation channel leaves surface cues visible and names the body
unavailability. Retry remains a failure-status action, not a salience control.

Qualification messages identify the affected evidence mode. Implementation
qualification does not change the surface presentation's disposition or
diagnostics. Repeated implementation diagnostics are disclosed once.

Markout 0.38.0 (`net10.0`), opened at `Markout.BlockWriter`, motivates this
boundary: compiler-generated physical-only bodies produce an implementation
qualification while signature-based surface cues remain usable. A focused
TypeScript regression retains the surface disposition and cues, labels the
implementation qualification, and deduplicates repeated body diagnostics.
Running `AnalysisLibraryBodyUseService.ExecutePath` on the package's
`lib/net10.0/Markout.dll` records 1,540 considered bodies, 820 logical-owner
bodies, 720 physical-only bodies, and 9,139 examined typed operands, with zero
unavailable or limited bodies or operands. This is a fidelity qualification,
not a body-decoding failure.

## Validation

The contract is gated at four boundaries:

- Research-query tests prove one exact implementation snapshot and shared
  exact-namespace inventories issue both channels, while body rejection retains
  the surface document.
- Analysis-facade boundary tests prove package and platform wire projection,
  reference-only surface success, implementation unavailability, qualification,
  and exact generic/nested Type identity.
- TypeScript unit tests prove independent channel validation, same-direction
  and opposing dual poles, cache replacement, retry, and stale-publication
  suppression.
- Browser tests prove automatic post-paint acquisition, outline/filled icon
  presentation, accessible mode-plus-pole labels, two leverage glyphs plus an
  API-difference glyph, qualified/failure status, and the absence of salience
  controls or filters.

System.Text.Json is the production demonstration. Owner-issued body evidence
must make `ThrowHelper` observable as an implementation sea-level Type and
`JsonSerializer` or `JsonDocument` observable as an implementation
mountain-peak Type when those Types meet the current Research cohort policy.
Synthetic Browser cases preserve both aligned and opposing two-pole
presentations because a single real package need not exercise both states
stably across versions.
