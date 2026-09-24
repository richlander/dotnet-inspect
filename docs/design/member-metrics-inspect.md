# MemberMetricsInspect

## Status and scope

This document is the proposed normative design for
**MemberMetricsInspect**, tracked by
[#8445](https://github.com/richlander/dotnet-inspect/issues/8445).

The current product already has:

- the proposed resource-free `MemberInspect` subject and `Overloads`
  population contract;
- objective physical implementation profiles and exact sibling-overload call
  relationships;
- a bounded exact-family query used by CLI and Inspect Web;
- QuerySpace operation, row, selection, projection, Rows, and Count contracts;
  and
- explicit Analysis feature selection with prerequisite normalization.

The current exact-family operation acquires the complete implementation-profile
result. This design adds the narrower inspection boundary required when a
consumer needs only a small decoration over the already-rendered Member rows.
It does not redefine the existing metric evidence or make Analysis part of
ordinary `MemberInspect` completion.

## Owner and exact claim

**MemberMetricsInspect** owns this exact claim:

> Given one completed owner-issued Member subject and `Overloads` Rows
> population receipt, one non-empty effective metric-evidence requirement, and
> explicit work bounds, inspect only the required evidence and its owner-issued
> prerequisite closure; expose the corresponding logical Member metrics
> population through QuerySpace Rows and Count; and return one detached
> `InspectionEnvelope<MemberMetricsInspectionContent>` that CLI and Inspect Web
> can join to the unchanged Member population by typed identity.

This owner defines:

- the `MemberMetricsInspect` request and completed content boundary;
- correspondence with one `MemberInspect` subject and `Overloads` population;
- the distinction between authorized, requested, and effective evidence and
  its mapping to Analysis-issued producer participation;
- rejection of an executable request with no effective metric requirement;
- composition of QuerySpace metric projection, predicates, order, semantic
  selection, relationship demand, and work bounds into one evidence plan;
- one logical metrics row per exact Member row in the bound population;
- preservation of separately identified physical and generated evidence;
- metric-evidence outcome, coverage, completion, and failure states;
- the same-population requirement for metrics Rows and Count; and
- the completed cross-host envelope.

It does not define:

- Member subject resolution, logical grouping, exact Member identity, receiver
  classification, or base overload membership;
- QuerySpace facet, order, selection, terminal, projection-stage, or
  continuation mechanics;
- physical metric algorithms, call resolution, generated-body attribution, or
  Analysis producer prerequisites;
- a universal complexity or audit-risk scalar;
- section names, CLI syntax, Browser activation, caching, color, layout, or
  rendering; or
- Reflection or Unsafe Accessor evidence semantics.

The adjacent owners supply typed identities, query plans, evidence, and
execution receipts. MemberMetricsInspect composes those contracts without
reconstructing their facts or broadening their authority.

## Product question

The operation answers:

> What explicitly requested implementation evidence describes these exact
> Member rows, and which of those rows satisfy the requested metric query?

It is a peer inspection, not an optional field that delays completion of the
base Member document:

```text
MemberInspect
  -> MemberInspectionContent
       exact Member subject
       Overloads population receipt
       Overloads Rows or Count

MemberMetricsInspect
  + the Member subject and population receipt
  + explicit metric evidence and QuerySpace request
  -> MemberMetricsInspectionContent
       the same Member subject and base population binding
       requested and effective evidence receipt
       metrics Rows or Count
       coverage, completion, and diagnostics
```

There is no `MemberOverloadDocument`. `Overloads` remains the natural row space
of one Member subject. MemberMetricsInspect mirrors that population for an
independently completed evidence request.

## Production demonstration

`System.Text.StringBuilder.AppendFormat` in .NET 11 RC1 is the initial
production witness. The current exact-family evidence contains 15 logical
overloads and 16 physical profiles. It identifies
`AppendFormat(IFormatProvider, string, ReadOnlySpan<object>)` as an 814-byte IL
body with eight incoming sibling overloads, while thin family members have
10-56 IL bytes.

The target Inspect Web sequence is:

```text
1. MemberInspect(StringBuilder.AppendFormat)
   -> publish and render the 15 overload rows

2. MemberMetricsInspect(
     population = the published Overloads receipt,
     authorize = [body-size, overload-relationships],
     rows = all overloads in baseline order,
     projection = [
       member-identity,
       largest-physical-il-bytes,
       incoming-sibling-callers,
       outgoing-sibling-targets
     ])

3. Join returned rows by typed Member identity and population binding.
   Add family-relative size and forwarding cues without changing membership
   or order.
```

That request does not ask for control-flow, exception-region, allocation,
throw, unsafe, Reflection, or complete call-census evidence. None of those
evidence producers may participate merely because the complete compatibility
profile historically included their values.

The same route supports an audit query:

```text
MemberMetricsInspect(StringBuilder.AppendFormat)
  authorize = [body-size]
  order = largest-physical-il-bytes descending
  top = 5
  terminal = Rows
```

The output limit selects returned rows. It does not claim that only five
candidate rows were inspected when the requested order requires comparing the
complete bound population.

## Conventional basis

The design composes established repository contracts and familiar query-planner
distinctions:

| Precedent | Adopted rule |
| --- | --- |
| [Type and Member inspection documents](type-member-inspection-documents.md) | One Member subject owns one canonical `Overloads` population, exact row identities, a stable baseline order, and a population binding. |
| [Query Space Composition](query-space-composition.md) | Operation intent, row intent, semantic selection, projection, Rows, and Count remain explicit associated plans with fixed observable stages. |
| [Library Body Analysis Service](library-body-analysis-service.md) | One explicit Analysis request selects focused producers, normalizes prerequisites, scopes physical bodies, executes once, and publishes detached owner-typed results with one receipt. |
| [Inspect Web implementation profiles](inspect-web-implementation-profiles.md) | Family acquisition preserves typed logical, physical, public-Member, relationship, and subject identities and suppresses stale publication. |
| [CLI member implementation profiles](cli-member-implementation-profiles.md) | The CLI consumes the same completed exact-family envelope and retains compatibility only outside the eligible family route. |
| [GraphQL selection sets](https://spec.graphql.org/September2025/#sec-Selection-Sets) | A consumer names the result fields it needs; field selection does not by itself prove minimal producer work. |
| [OData query options](https://docs.oasis-open.org/odata/odata/v4.01/cs02/part2-url-conventions/odata-v4.01-cs02-part2-url-conventions.html) | Projection, filtering, ordering, and limiting are separate query concerns; a value used to filter or order may be required even when it is not returned. |
| [PostgreSQL query plans](https://www.postgresql.org/docs/current/using-explain.html) | Returned rows and scanned rows are different quantities; a restrictive result does not imply proportionally bounded acquisition work. |

MemberMetricsInspect adopts explicit field demand and prerequisite planning. It
does not adopt GraphQL resolver behavior, OData wire syntax, SQL cost
estimation, or a general relational optimizer.

## Owner map

| Concern | Owner | Input to MemberMetricsInspect |
| --- | --- | --- |
| Member subject and overload population | [Type and Member inspection documents](type-member-inspection-documents.md) | Exact subject, logical group, ordered exact rows, receiver facts, and population binding |
| Physical implementation evidence | `ILInspector.Analysis` focused result owners | Typed metric values, physical identity, logical attribution, limitations, and diagnostics |
| Analysis execution | [Library Body Analysis Service](library-body-analysis-service.md) | Explicit selected features, prerequisite closure, physical body scope, execution receipt, and focused detached results |
| Exact participant acquisition | AssemblyContext and Workspace owners | One immutable participant snapshot and generation-bound operation authority |
| Query composition | [Query Space Composition](query-space-composition.md) | Resolved operation intent, row intent, projection intent, terminal, and effects |
| Row filtering and order | [L2 row query and ordering](row-query-order.md) | Typed metric and inherited Member facets, comparisons, and named orders |
| Semantic row selection | [Semantic row selection](semantic-row-selection.md) | Head, Tail, Window, or Top over the resolved metrics rows |
| Completed handoff | [Inspection envelope](inspection-envelope.md) | Content, Share, and diagnostics |
| Presentation and scheduling | CLI, Inspect Web, Markout, and focused host owners | Explicit evidence gesture, asynchronous scheduling, projection, and rendering |

This design adds no generic metrics registry, Analysis House, subject algebra,
or host-independent presentation model.

## Subject and population correspondence

One request consumes one owner-issued Member subject and its complete base
`Overloads` Rows population receipt. The receipt retains:

- the exact containing Type and logical Member-group identity;
- the immutable base population binding;
- the stable baseline order;
- one exact Member identity per overload row;
- receiver and other inherited owner-issued row facts;
- the declaring identity needed for Metadata and body correspondence; and
- the Workspace or participant generation required by the producing route.

A base `MemberInspect` Count outcome alone is not a MemberMetricsInspect input
because it need not materialize the exact roster. A caller that needs metrics
first obtains the owner-issued Rows population receipt under the desired
binding; it need not render those rows.

The host passes that typed receipt or an owner-issued transport projection of
it. It does not reconstruct the roster from signature text, rendered rows, a
cached count, or a second public-API enumeration.

Before Analysis, MemberMetricsInspect proves that:

- the current participant and generation match the receipt;
- every requested row belongs to the bound population;
- no row identity is duplicated or unknown; and
- the request has not combined identities from another Member subject.

A mismatch is a typed rejection. The operation does not silently re-resolve the
Member address, widen to a whole Type or Library, or decorate a newer population
with evidence from an older one.

The completed result repeats the exact subject and base population binding. A
host publishes decoration only when those values still match the active
MemberInspection content.

## Request model

Hosts lower gestures into one semantic request. The request carries no section
name, output format, CSS class, color, Browser component name, or renderer
setting.

Conceptually:

```text
MemberMetricsInspectionRequest
  exact Member subject receipt
  exact Overloads Rows population receipt
  authorized metric evidence kinds
  optional relationship evidence request
  one Metrics QuerySpace request
  Analysis and aggregate work bounds
```

Evidence authorization is explicit. Omission means unauthorized, never "all".
A host-level `summary`, `decoration`, or `complete profile` convenience may
expand to an exact owner-issued authorization set before constructing this
request; the host-neutral operation receives and preserves the expanded set.

The authorization set is a cost and capability ceiling, not independent demand.
QuerySpace result selection, Rows projection, predicates, order, and semantic
selection derive the requested evidence. Every requested kind must be
authorized. An unauthorized query term is rejected before Analysis, while an
authorized kind that the resolved terminal does not need does not run.

The initial evidence vocabulary is based on the current objective
implementation profile and exact-family relationships:

- encoded body size;
- instruction shape;
- control flow;
- exception regions and locals;
- direct calls;
- exact sibling-overload relationships;
- allocation occurrences;
- throws;
- unsafe implementation evidence;
- direct Reflection calls; and
- async and generated-body attribution.

These are evidence selections, not a promise that each maps one-to-one to an
Analysis producer. Analysis owns their metric meanings and prerequisite graph.
Later evidence kinds are versioned additions. Reflection and Unsafe Accessor
declaration or occurrence models remain owned by their focused designs even
when MemberMetricsInspect later exposes their owner-issued results.

## Effective evidence requirement

Every executable Rows or Count request must have at least one effective metric
requirement after QuerySpace resolution.

An effective requirement comes from at least one of:

- a projected metric cell in a Rows terminal;
- a metric facet used by a row predicate;
- a metric order required to produce Rows or evaluate an order-dependent
  semantic selection;
- a selected relationship result; or
- another owner-declared result whose value depends on metric evidence.

Identity, population correspondence, baseline order, coverage, completion, and
diagnostics accompany requested evidence. They do not independently justify
Analysis.

Examples:

| Request | Result |
| --- | --- |
| Rows projecting IL bytes | Valid; body-size evidence is effective. |
| Rows ordered by loop count but not projecting loop count | Valid; control-flow evidence is effective. |
| Count where direct Reflection calls are greater than zero | Valid; Reflection-call evidence is effective. |
| Rows projecting only Member identity | Rejected; use `MemberInspect`. |
| Count with no metric predicate or metric-dependent selection | Rejected; use `MemberInspect` Count. |
| Count with only a metric cell projection | Rejected; Count validates but does not execute cell projection. |
| Count with metric order but no order-dependent selection | Rejected; ordering cannot affect the scalar result. |
| Rows selecting relationship evidence without scalar metric columns | Valid; the relationship result is effective. |
| Resource-free descriptor discovery | Valid; no inspection executes. |

A query-derived requirement that cannot affect the resolved terminal is
rejected according to its owner-issued compatibility rule. Unused
authorization remains authorization only. The operation never performs
expensive work solely so that the work receipt can report that it happened.

## Evidence prerequisite closure

MemberMetricsInspect derives one requested-evidence set from the resolved
request:

```text
projected metric cells
  union metric predicates
  union metric orders and selections
  union selected relationship results
  -> requested evidence kinds
  -> prove requested evidence is authorized
  -> owner-issued prerequisite normalization
  -> effective evidence kinds and physical scope
```

The operation asks Analysis for that normalized closure once. It does not
invoke one Analysis execution per metric or per overload.

Analysis may share prerequisites across requested evidence. One instruction
decode, body attribution, call scan, target-resolution pass, or control-flow
construction may support several selected results. Sharing does not authorize
an unrelated producer.

For example, the owner-issued prerequisite graph may establish that:

- encoded IL size needs body acquisition but not control-flow construction;
- a loop metric needs the applicable control-flow evidence;
- exact sibling relationships need call evidence and the required target
  resolution;
- direct Reflection evidence needs its owner-issued call classification; and
- logical metrics over async methods need recognized physical-body
  attribution.

Those examples describe required separation, not Analysis algorithms. The
exact prerequisite edges and producer implementations remain with Analysis.

The current monolithic `ImplementationProfiles` feature is a compatibility
input, not proof of this contract. Production adoption must add or narrow
focused Analysis requests until a compact evidence selection can execute
without producing every complete-profile metric.

## Minimal-work contract

The operation preserves three distinct sets:

```text
authorized evidence
requested evidence
effective evidence after prerequisite normalization
Analysis-issued producer participation outcomes
```

The completed content retains the authorization and request sets beside an
Analysis-issued execution receipt. MemberMetricsInspect maps the Analysis
receipt to its evidence requirements; it does not infer actual execution from
the planned feature set.

Together those receipts are sufficient to prove:

- every participating producer supports at least one effective requirement or
  a declared prerequisite;
- every effective requirement is covered by one completed, incomplete, or
  failed producer outcome;
- no omitted evidence kind is represented as requested or available; and
- body scope expansion is attributable to population correspondence,
  generated-body attribution, relationship scope, or another declared
  prerequisite.

Until Analysis publishes that owner-issued participation and scope evidence,
the current exact-family operation can supply compatibility data but cannot
satisfy this design's minimal-work claim.

An implementation may optimize more aggressively only when it preserves the
same evidence, population, completion, and failure semantics. Incidental data
already produced by a shared prerequisite may remain internal, but it does not
become an available unrequested metric cell.

The contract minimizes required work; it does not promise that every metric has
an independently avoidable machine instruction. Evidence kinds should align
with meaningful producer-cost boundaries. Measurements may justify combining
or splitting a proposed evidence kind before its public contract locks.

## QuerySpace boundary

MemberMetricsInspect declares one natural logical metrics row space over the
bound `Overloads` population. Its row unit is one exact Member declaration from
that population.

Conceptually:

```text
MemberMetricsPopulation
  exact Member subject
  source Overloads population binding
  canonical exact Member rows
  inherited Member row vocabulary
  metric row vocabulary
  stable baseline order
  requested and effective evidence
  Rows terminal
  Count terminal
```

The metrics row vocabulary consumes inherited Member facets such as `receiver`
without redefining them and adds only owner-issued metric facets and named
orders. A metric facet retains its evidence kind and value domain. Display
column text never becomes a query key.

QuerySpace keeps its fixed stages:

```text
subject and population binding
  -> operation qualification
  -> declared logical metrics rows
  -> membership projection
  -> row predicates
  -> baseline or requested order
  -> semantic selection
  -> Rows with cell projection, or exact Count
```

MemberMetricsInspect may plan acquisition from the complete resolved request
before executing Analysis. That is an owner-approved source optimization, not
a change to observable QuerySpace stage order. The result must equal the
reference semantics in which required evidence exists before metric predicates,
orders, and selection run.

Rows and Count are peer terminals over the same source population and row
intent. A Count request and a completely drained Rows request agree when the
Member subject, base population binding, evidence requirements, predicates,
order, and semantic selection are identical.

Count does not execute cell projection, but projection validity remains part of
request resolution. An unqualified Count has no effective metric requirement
and is rejected. A Count with a metric predicate executes only the evidence
needed to evaluate that predicate and its prerequisites.

## Candidate scope and result limits

The base Member population is the maximum logical candidate scope. The
operation never widens to other Member names, Types, or Libraries.

An owner-approved source optimization may narrow physical acquisition before
Analysis when a base-identity or inherited-Member predicate proves that the
excluded rows cannot affect the requested result. The work receipt retains the
effective scope.

Metric predicates, metric orders, and Top selection commonly require evidence
for every remaining candidate row. `Head(5)`, `Top(5)`, or a five-row result
therefore does not imply five analyzed bodies.

Cross-row evidence may require a wider scope than returned rows. Exact
sibling-overload incoming counts, for example, are relative to the complete
bound family unless the relationship owner defines and the request selects a
narrower relation scope. Returning one callee row may still require inspecting
other family callers.

Work bounds constrain acquisition independently from semantic row selection.
Exhaustion produces typed incomplete or failed evidence with coverage; it does
not silently truncate candidate analysis and report a complete top result.

## Logical and physical evidence

The metrics row mirrors one logical exact Member row. It does not collapse the
logical declaration and its physical evidence bodies.

Conceptually:

```text
MemberMetricRow
  exact Member row identity
  base ordinal and inherited facets
  requested evidence outcomes
  zero or more PhysicalMetricEvidence values
  exact contained overload relationships
  row coverage and completion
```

Each physical value retains:

- physical method identity;
- authoritative logical declared-source identity, when available;
- generated-framework or state-machine role;
- only the requested metric cells supported by that evidence kind;
- completeness and typed limitations; and
- exact evidence coordinates where the producing owner supplies them.

Logical scalar facets use an explicitly named owner-issued reduction over
physical evidence. A field is not simply called `il-bytes` when several
physical bodies can exist; a decoration may expose
`largest-physical-il-bytes`, while detailed output retains every physical
value. Boolean and occurrence evidence similarly names whether its reduction
is `any`, `all`, `sum`, `max`, or another focused contract.

This design does not define one generic reduction algebra. Each exposed
logical facet names and gates its exact reduction. Hosts do not invent
different reductions over the same field.

A bodyless declaration remains a metrics row with a `bodyless` outcome when
the base population proves no managed body. A failed or unavailable body is
not bodyless and does not acquire zero metric values.

## Evidence outcome and query semantics

Each requested evidence kind has one closed row-local outcome:

- `available`;
- `bodyless`;
- `unavailable`;
- `incomplete`;
- `rejected`; or
- `failed`.

An incomplete outcome may retain partial owner-issued values and limitations.
Unavailable, rejected, and failed outcomes do not become zero, `false`, an
empty relationship set, or absence of the logical row.

Metric value predicates operate only on comparable owner-issued values.
Unavailable evidence does not satisfy either `unsafe = false` or
`unsafe != true`; callers query the corresponding evidence-state facet when
they need unavailable or incomplete rows. This prevents missing evidence from
looking safe or structurally empty.

The row vocabulary exposes evidence state independently from value facets.
Named orders define deterministic placement for unavailable or incomplete
values. A host cannot choose another missing-value policy from presentation
code.

## Completion and failure

Completion is view-local and aggregate.

The operation distinguishes:

- request rejection before Analysis;
- participant or population-receipt rejection;
- complete evidence for every selected logical row;
- complete bodyless outcomes;
- partial physical evidence for a logical row;
- unavailable physical bodies with typed reasons;
- work-bound exhaustion;
- Analysis producer failure;
- QuerySpace planning or continuation failure; and
- completed empty selection after valid metric predicates.

A valid predicate may select no rows. That is successful empty Rows or zero
Count only when the requested evidence was sufficiently complete to evaluate
the predicate over the required candidate population.

One failed evidence kind does not erase independently completed kinds. Aggregate
completion reports whether the terminal result is authoritative for its exact
request. A top result is incomplete when missing candidate evidence could
change membership or order.

Diagnostics remain ordered and attributable to subject resolution, population
validation, Analysis, row planning, selection, projection, or transport. Hosts
do not replace those failures with generic "metrics unavailable" text in the
typed content.

## Completed host-neutral handoff

The completed operation returns:

```text
InspectionEnvelope<MemberMetricsInspectionContent>
```

Conceptually, Content retains:

```text
MemberMetricsInspectionContent
  exact Member subject receipt
  source Overloads population binding
  authorized evidence receipt
  requested evidence receipt
  effective evidence and work receipt
  MemberMetricsPopulation Rows or Count outcome
  aggregate coverage and completion
  contained diagnostics
```

The exact public names may follow repository naming conventions at
implementation time. The invariant is one resource-free Content result, one
Share outcome for the same semantic request, and complete diagnostics.

CLI and Inspect Web consume the same operation and envelope. Browser transport
records may normalize repeated identity, but they preserve:

- Member subject and population binding;
- exact logical and physical identities;
- base row ordinal;
- requested and effective evidence identities;
- evidence states, values, reductions, and limitations;
- relationship coordinates;
- coverage and completion; and
- Share and diagnostics.

Share projects the portable Member metrics request under its owner. It does not
serialize acquired images, Metadata readers, Analysis contexts, Workspace
leases, credentials, or live continuations.

## Host composition

The host schedules and renders; it does not reproduce evidence planning.

The target Inspect Web sequence is:

1. request and publish `MemberInspect`;
2. render its overload rows immediately;
3. construct one MemberMetricsInspect request for the exact active population
   and the configured decoration evidence;
4. keep base membership and order unchanged while the request is in flight;
5. publish decoration only when subject, population, generation, and current
   operation authority still match; and
6. expose typed incomplete or failed decoration without invalidating the base
   Member list.

The initial website decoration should request one compact evidence set in one
family operation, not one request per metric or overload. Family-relative
normalization and color are presentation derived from returned raw values; they
do not enter the host-neutral content.

The CLI may await the same operation before rendering an explicitly selected
Member Metrics section. CLI compatibility paths remain until their subject and
population gestures can be expressed through MemberInspect and
MemberMetricsInspect without losing behavior.

Full implementation-profile disclosure may continue as an explicit detailed
view over a broader evidence selection. A compact automatic or post-render
decoration does not authorize that complete profile.

## Cost and disclosure

MemberMetricsInspect is never part of base Member document completion.
Unrequested metrics do not run.

The host-neutral descriptor publishes:

- available evidence kinds and their result fields;
- the authorization required for each query facet, order, projection, or
  relationship result;
- query facets, named orders, reductions, and value domains;
- prerequisite and effect summaries;
- whether a query may require complete candidate inspection;
- supported work bounds; and
- Rows and Count result contracts.

QuerySpace discovery is resource-free. Effective discovery or measured cost
probing remains separately authorized by its existing owner.

Inspect Web may authorize a measured, bounded post-render decoration for one
visible family. Type-wide, Library-wide, source-content, network, or otherwise
unbounded metrics remain explicit until their own focused owners establish a
cost and disclosure contract.

## Platform and safety boundary

The request, population receipt, content, row, evidence-outcome, work-receipt,
and envelope contracts are resource-free, SRM-only, NativeAOT-compatible, and
suitable for single-threaded Browser/Wasm.

Analysis runs over an owner-protected immutable participant snapshot and never
loads or executes inspected code. Every reader, resolver, stream, buffer,
borrow, and Workspace authority settles before the completed content crosses
the host-neutral boundary.

Inspected names, signatures, diagnostics, and evidence strings remain inert
data under their producing owners. Display text cannot recover subject,
population, logical, physical, or relationship identity.

These implementation properties are **unverified** until the gates named by
[#8445](https://github.com/richlander/dotnet-inspect/issues/8445) exist and pass.

## Pathological cases

The implementation must preserve at least these cases:

- an executable request contains no projected, queried, ordered, or related
  metric evidence and is rejected before Analysis;
- Rows project Member identity while a non-projected loop metric supplies the
  requested order;
- Count uses a Reflection predicate without projecting Reflection values;
- body-size-only decoration runs without control-flow or unrelated signal
  producers;
- several requested metrics share one required body acquisition or instruction
  scan;
- one logical async Member has both a declared stub and an attributed generated
  body;
- a bodyless declaration is distinct from a body whose Analysis failed;
- one physical body succeeds while another attributed body is unavailable;
- unavailable unsafe evidence does not satisfy either safe or unsafe value
  predicates;
- a Top request cannot prove its order because one candidate metric is
  unavailable;
- a relationship request returns one selected callee but requires evidence from
  callers elsewhere in the same bound family;
- a base Member population is replaced while metrics are in flight;
- a typed population receipt names a different participant generation;
- Rows and Count use the same evidence and row intent but disagree;
- a work bound expires after partial evidence; and
- CLI and Browser attempt different logical reductions or missing-value policy
  over the same completed result.

Population mismatch, Rows/Count disagreement, and host-divergent reductions are
product defects, not presentation choices.

## Production adoption

[#8445](https://github.com/richlander/dotnet-inspect/issues/8445)
owns the counted path:

1. Lock this focused MemberMetricsInspect request, correspondence,
   evidence-planning, QuerySpace, and completed-envelope specification.
2. Add any separately owned focused Analysis capability required to execute
   selective evidence plans without producing unrelated complete-profile
   metrics.
3. Implement the host-neutral operation and QuerySpace descriptor over the
   owner-issued Member subject and `Overloads` population receipt.
4. Adopt MemberMetricsInspect in the CLI Member Metrics family path and retire
   the covered exact-family compatibility route.
5. Adopt it in Inspect Web as a post-render, stale-safe decoration request for
   one selected overload family, while retaining explicit complete-profile
   disclosure.
6. Measure per-evidence-set latency and prove that compact decoration performs
   less work than the complete implementation profile.

Each implementation or adoption remains a focused owner change. This design
and tracker connect them without approving one broad implementation PR.

A later `TypeMetricsInspect` may apply the same pattern to a Type's `Members`
population only through its own focused design and measured work contract.
This document does not define that operation.

## Required evidence

The implementation sequence must add Release gates proving:

- an executable no-evidence request is rejected before participant Analysis;
- the same Member subject and population receipt reaches both the base and
  metrics operations without display-text joins;
- every unfiltered logical metrics Rows result retains one row for each exact
  base overload, including bodyless and unavailable rows;
- body-size-only evidence does not select control-flow, call-classification,
  allocation, throw, unsafe, or Reflection producers;
- each metric predicate, order, relationship result, and Rows-projected metric
  cell contributes its evidence kind to the requested set;
- a Count whose only metric reference is cell projection is rejected before
  Analysis;
- a Count whose only metric reference is order without order-dependent
  selection is rejected before Analysis;
- requested evidence outside the authorized set is rejected, while unused
  authorized evidence does not run;
- prerequisite normalization adds only owner-declared dependencies and records
  the effective set;
- the Analysis-issued execution receipt, rather than the planned feature set,
  records producer participation and effective physical scope;
- several selected metrics share one Analysis execution and common
  prerequisites;
- a metric used only for order or predicate executes but need not be projected;
- an unqualified metrics Count is rejected, while a metric-filtered Count
  agrees with completely drained Rows for the same request;
- output limits do not claim to bound candidate Analysis when the selected
  metric order requires the complete population;
- exact sibling relationships retain their full-family scope when returned
  rows are narrower;
- logical rows retain every attributed physical profile and distinguish
  generated, bodyless, unavailable, incomplete, and failed evidence;
- metric predicates never coerce unavailable values to zero or false;
- work-bound exhaustion and candidate incompleteness prevent an authoritative
  Top result;
- a mismatched population or generation fails before evidence publication;
- Inspect Web renders Member rows before metrics complete and suppresses stale
  publication after navigation;
- the CLI and Browser consume the same envelope and preserve Content, Share,
  and diagnostics; and
- the `StringBuilder.AppendFormat` compact decoration executes fewer
  owner-issued evidence producers and has lower measured latency than the
  complete profile request.

The production platform Library is the behavioral witness. Focused fixtures
isolate no-evidence demand, prerequisite sharing, generated bodies, missing
values, partial coverage, work-bound exhaustion, stale binding, and
relationship scope. Harnesses invoke product-owned Member population,
QuerySpace, and Analysis planning rather than constructing a repaired metrics
aggregate.

All properties remain **unverified** in this design-only slice.

## Non-claims

This design does not:

- add metrics to `MemberInspectionContent` or delay MemberInspect completion;
- create `MemberOverloadDocument`, `SubjectMetrics<T>`, or a generic metrics
  registry;
- define Type- or Library-wide metrics inspection;
- define a canonical complexity, importance, risk, or review-candidate score;
- claim that QuerySpace result limits necessarily reduce Analysis work;
- require one Analysis producer per public metric cell;
- expose unrequested incidental evidence;
- define Reflection or Unsafe Accessor classification;
- define Browser color, badge, bar, animation, sorting, or loading-state UX;
- make complete implementation profiles an automatic navigation cost;
- replace the explicit complete-profile views before their consumers adopt the
  new route; or
- change the base Member `Overloads` population, receiver facet, Rows, or Count
  semantics.
