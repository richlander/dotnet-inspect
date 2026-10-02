# Inspect Web method-leverage achievements

Status: proposed for [issue #9134](https://github.com/richlander/dotnet-inspect/issues/9134).

## Owned claim

This document owns how Inspect Web acquires and presents **Top Leverage**
achievements for the methods declared by one selected Type.

The achievement answers one question:

> Which methods declared by this Type have the strongest inbound call leverage
> in the defining Library?

`ILInspector.Analysis.MethodLeverageRanking` owns the method evidence and rank
order. This design owns the Type-scoped designation, exact correspondence to
browsable member rows, explicit Browser acquisition, filtering, and composition
with other member achievements. It does not redefine call-graph evidence,
Implementation Hub, or namespace structural-report semantics.

The motivating asset is `System.Text.Json@10.0.0`. For
`System.Text.Json.JsonSerializerOptions`, internal `VerifyMutable()` has the
strongest method leverage. The strongest public member is only a runner-up.
Inspect Web must therefore analyze every accessibility and must never promote a
visible public member when the real winner is hidden by the current
presentation.

## Design basis

- `src/ILInspector.Analysis/MethodLeverage.cs` owns the complete method evidence,
  score fields, and deterministic rank order.
- [Progressive disclosure](progressive-disclosure.md) requires this unbounded
  implementation analysis to remain explicit. Accessibility filters describe
  presentation; they do not define an implementation-analysis population.
- [Inspect Web implementation profiles](inspect-web-implementation-profiles.md)
  owns the adjacent overload-family heat and Implementation Hub fact. Its
  all-accessibility analysis and exact visible-row projection are precedent,
  not a shared designation.
- [Library structural report](library-structural-report.md) owns the shared
  zero-to-two item-achievement rail and the distinction between producer-issued
  designations and Browser presentation.
- [Output shapes](output-shapes.md) requires a typed result with visible failure
  instead of a success-shaped empty payload.

## Terms

- **Method population**: every analyzable MethodDef declared directly by the
  selected Type, independent of accessibility or whether it has a browsable
  member row.
- **Semantic rank**: the ordered tuple of direct caller count, root reach,
  fanout, and loop call count defined below. The metadata token is not part of
  semantic equality.
- **Winner**: a method whose semantic rank equals the qualified maximum for the
  selected Type.
- **Anchored winner**: a winner that corresponds to an exact product-issued
  Browser member anchor.
- **Visible winner**: an anchored winner whose member row survives the current
  accessibility, kind, trait, and text filters.

These terms remain distinct. An unanchored or filtered winner is still a
winner, and its existence must not cause another method to receive the
achievement.

## Designation

### Population

The producer analyzes the defining implementation Library with whole-assembly
call evidence, then selects every method whose declaring Type is the exact
selected Type. The selection includes:

- public, protected, internal, and private methods;
- constructors and accessors;
- methods hidden from ordinary API presentation; and
- compiler-created methods declared directly by the Type when Analysis retains
  them.

Inherited methods and methods declared by nested Types are not part of the
selected Type's population.

The Browser's current accessibility, kind, trait, text, and spelling choices do
not change this population. The `--all` CLI option is likewise not part of the
designation contract. Explicit CLI Top Leverage already analyzes non-public
methods without `--all`; that option only widens API-surface enrichment.

### Semantic maximum and ties

For each method, the existing Analysis owner supplies:

1. `DirectCallerCount`, descending;
2. `RootReach`, descending;
3. `Fanout`, descending; and
4. `LoopCallCount`, descending.

`MaxDepth` remains reported evidence but is not a rank component.

Analysis uses ascending metadata token only to make otherwise equal rows
deterministic. The token does not break a semantic tie. Every method equal to
the first row on the four semantic rank fields is a winner.

A maximum qualifies only when `DirectCallerCount` is greater than zero. A Type
whose methods all have zero inbound callers has no Top Leverage achievement.
This prevents a disconnected or call-free method population from receiving an
arbitrary badge.

The designation query consumes Analysis rows and compares their owner-issued
rank fields. It does not independently compute callers, reach, fanout, loops,
or depth.

### Exact member attribution

The query extracts the selected Type's API surface at
`ApiSurfaceExtractionScope.IncludeAll` and uses product-issued identities:

- the Type definition identity selects the exact declaring Type;
- MethodDef tokens join Analysis methods to member body selectors; and
- the member stable selector identifies the Browser row.

Rendered names and signatures are never parsed to infer correspondence.

A method, constructor, or operator maps to its exact overload row. An accessor
maps to its owning property or event row through the member's body selectors.
If multiple winning accessors map to one member, the member receives one
achievement. If any accessor wins, the property or event is an anchored winner;
non-winning sibling accessors do not remove it.

An overload-family parent is not a MethodDef and never receives Top Leverage by
inference. A family with one overload uses its single member row. A family with
multiple overloads reserves the rail on the parent for alignment but places the
achievement only on exact winning overload rows.

Compiler-created methods or malformed rows may have no Browser anchor. They
remain in the population and may be winners. The result reports the number of
semantic winners and anchored winners separately. An unanchored winner yields
no glyph and no visible runner-up substitution.

If API extraction or token attribution is incomplete, the outcome is not
available. The query must not publish a partial anchored set as authoritative.

## Acquisition

### Explicit activation

Ordinary Type navigation does not start method-leverage analysis. The member
filter disclosure offers an explicit **Show Top Leverage** action. Activation
authorizes this capability for the current workspace.

After activation:

- the selected Type is requested immediately;
- later Type navigation requests each newly selected Type while the capability
  remains active; and
- revisiting an already inspected coordinate reuses the cached result.

This makes the initial unbounded work deliberate without requiring repeated
button presses during one exploration.

### Coordinate and cache

One request names:

- the current workspace generation;
- the exact defining Library identity;
- the selected Type definition identity; and
- the package or platform acquisition route.

The cache and in-flight coordinator use the same coordinate. A result from an
old workspace generation, Library selection, or Type intent cannot publish into
the current view. Concurrent consumers of one coordinate share one request.
Rejected or failed requests remain retryable and do not poison a successful
cache entry.

Package and platform routes expose equivalent operations. Each route resolves
the exact implementation participant before entering the shared Type-scoped
query. Uploaded Libraries do not yet have a product-owned Analysis lifecycle;
their control remains explicitly unavailable instead of opening or analyzing
the image in the Browser host.

### Typed result

The host-neutral operation returns an `InspectionEnvelope` whose content has
one of these outcomes:

- **available**: complete method population, qualified maximum, and attribution
  were obtained;
- **no metadata**: the selected implementation has no managed metadata;
- **Type not found** or **Type ambiguous**: the exact request could not select
  one declaring Type;
- **incomplete**: required Analysis or API-attribution evidence was truncated
  or unavailable; or
- **failed**: acquisition or analysis failed with a visible diagnostic.

An available result carries:

- selected Type definition identity;
- method-population count;
- winner count;
- anchored-winner count;
- the winning semantic rank when one qualifies;
- anchored winner entries with stable selector, winning MethodDef tokens, and
  owner-issued rank evidence; and
- diagnostics.

An available result with zero winners is a valid negative answer. It is
different from an incomplete or failed result.

The Browser facade lowers this content to a source-generated wire contract.
TypeScript validates the required shape and identity fields, but it does not
rank rows or manufacture a designation.

## Browser presentation

### Member achievement rail

Every member and overload row reserves the shared two-slot achievement rail
after activation, including rows with no achievement. Kind icons remain to the
right of the rail; implementation heat remains right-anchored.

Member achievement order is:

1. Top Leverage;
2. Implementation Hub.

The order expresses scope, not a numeric comparison: Top Leverage summarizes
whole-Library callers for the selected Type, while Implementation Hub describes
one same-name overload family. Both fit in the two available slots. Neither
fact changes or suppresses the other.

Top Leverage uses the approved 16-by-16 upward-leverage glyph as an inert CSS
mask whose color comes from the current theme. It has no independent network
request, executable SVG content, or semantic text inside the icon.

The accessible name and tooltip summarize the fact, for example:

> Top Leverage in JsonSerializerOptions: 31 direct callers, 47 roots, fanout 8.

The text uses only producer-issued evidence. It does not describe a member as
"highest visible".

### Filtering

Once an available result exists, the member filter disclosure offers:

- **all members**; and
- **top leverage**.

The Top Leverage choice keeps only exact anchored winner rows after the ordinary
accessibility, kind, trait, and text filters are applied. It never recomputes
the winner over the remaining rows.

If anchored winners exist but ordinary filters hide them, the empty state says
that no Top Leverage member matches the current filters and suggests changing
those filters. If semantic winners exist but none has a browsable anchor, the
empty state says the true winner has no browsable member row. If no nonzero
winner exists, the state says the Type has no inbound-call Top Leverage
designation.

The filter and capability activation participate in navigation history. A
shared view requesting the Top Leverage filter is itself an explicit request
for the capability and may start the analysis after the normal workspace
construction succeeds.

### Pending and failure states

Pending state keeps the member list usable and labels the control as analyzing.
It never shows stale cues from another Type.

Rejected, incomplete, and failed outcomes show their diagnostic beside a Retry
action. They do not display an empty winner set. Retry preserves the requested
filter but does not publish until the replacement result is available for the
current coordinate.

## Boundaries

This slice does not:

- change the Analysis rank fields or call-graph algorithm;
- define namespace-level top leverage;
- change sea-level or mountain-peak structural salience;
- change Implementation Hub designation or family-relative heat;
- infer source-level declarations for compiler-created methods;
- add a host-owned Analysis path for uploaded Libraries;
- make method-leverage analysis part of ordinary Type navigation before
  explicit activation;
- require `--all` for CLI Top Leverage; or
- tint member rows with a scalar or categorical wash.

The shipped performance skill currently overstates the role of `--all`. Its
release-managed wording should be reconciled separately; this feature does not
preserve that stale description.

## Validation

The implementation must gate:

- complete all-accessibility population and nonzero qualification;
- semantic ties independent of metadata-token order;
- a private winner suppressing a public runner-up;
- exact method, constructor, accessor, and overload attribution;
- an unanchored compiler-created winner with no fallback;
- typed incomplete and failure outcomes;
- package and platform route parity plus explicit uploaded-Library
  unavailability;
- generation-safe single-flight caching and retry;
- Top Leverage plus Implementation Hub ordering in the two-slot rail;
- top-only filtering without recomputation;
- keyboard and accessible-name behavior; and
- real Wasm operation and a production-host screenshot using
  `System.Text.Json`.

The production-host demonstration must include
`JsonSerializerOptions.VerifyMutable()` under an internal or all-accessibility
view and show that a public-only view does not relabel
`AllowDuplicateProperties` as Top Leverage.
