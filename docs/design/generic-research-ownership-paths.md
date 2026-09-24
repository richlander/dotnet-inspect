# Generic Research ownership paths

## Status and claim

This document is the normative owner for the detached interprocedural
ownership summary and its Research path composition. It is tracked by
[#6732](https://github.com/richlander/dotnet-inspect/issues/6732).

Analysis publishes the smallest method-local summary that lets Research carry
one Resource Occurrence acquisition obligation across existing physical
call-graph edges to a proven release, storage, or caller-return sink. The
summary preserves the acquisition's resource-kind identity, exact physical
forwarding coordinates, and local completeness without retaining IL,
control-flow, reaching-definitions, resolver, or acquisition state.

[Resource Ownership and Borrowing](resource-ownership-and-borrowing.md) owns
the protocol vocabulary. [Resource Occurrence
Analysis](resource-occurrence-analysis.md) owns resolved resource kinds,
acquisition obligations, and terminal operations.
[Call-graph projection](call-graph-projection.md) owns progressive graph
acquisition, physical edge rows, and correspondence. This document composes
those owner-issued facts; it does not redefine them.

The motivating production evidence remains the pinned ArrayPool-heavy
community corpus, including MessagePack 2.5.192, Npgsql 8.0.4, and
Pipelines.Sockets.Unofficial 2.2.8. Their existing ArrayPool lifecycle and
Research paths remain independent fidelity oracles during this migration.

## Analysis summary contract

A Resource Occurrence request also produces one
`ResourceOwnershipMethodSummary` for every selected managed body that Analysis
decoded successfully. No summary is produced for a body that was unavailable
or failed before its method-body context existed.

Each method summary contains:

- the exact physical method identity and member;
- acquisition flows rooted by the unchanged
  `ResourceOccurrenceRoot.Acquisition`;
- resource-neutral incoming-parameter flows keyed by the physical parameter
  index; and
- method-level completeness for the shared body and value-flow substrate.

An acquisition flow keeps its owner-issued Resource Occurrence root as the
obligation identity. An incoming-parameter flow does not guess a resource kind:
Research carries the originating acquisition obligation into that parameter
after joining a physical call to a graph edge.

Each flow contains ordered uses with one of four outcomes:

| Use | Meaning |
| --- | --- |
| `Released` | The admitted resource effects prove that this obligation is released at the physical call. |
| `Stored` | The same value reaches a physical field store. |
| `ReturnedToCaller` | The same value reaches a physical method return. |
| `Forwarded` | The same value reaches one physical direct-call parameter. |

A forwarded use retains the complete `DirectCall` and the zero-based callee
parameter index. Repeated physical calls therefore remain distinct even when
the call-graph projection collapses them onto one logical edge row. It also
retains detached declaring-type and method generic arguments. An argument
carries a detached `ResourceOccurrenceType` template only when method-local
metadata proves every non-generic identity without resolution. Generic leaves
remain scope-owned placeholders that Research can bind from exact prior-call
evidence; if any fixed leaf is unavailable, the template is absent rather than
inferred from display or simple assembly names.

Release uses carry the exact Resource Occurrence resource-kind domain to which
the admitted effect applies. Resource-neutral forwarding remains the fallback
at the same call coordinate. During composition, a release for the carried
obligation's domain supersedes only the matching forwarding coordinate; it
does not reclassify another resource kind or another parameter at that call.
When Analysis reuses the acquisition's owner-issued immutable domain for a
method-local release, that shared identity is already exact evidence even when
the containing method remains open generic.

Analysis builds the summary in the same execution that produced Resource
Occurrence. It reuses the retained `MethodBodyAnalysisContext`, resolved
effects, and existing direct-call evidence. Research never decodes a body,
runs reaching definitions, resolves effects, or runs Resource Lifecycle.
Resource-effect resolution routes in-group selections through lazy
snapshot-backed participant descriptors, including every active and shadow
candidate in ambiguous or designated selections returned by a participant
policy, while preserving each participant's source-relative policy for
external dependencies. Summary production therefore does not reacquire a
participant source.

## Research composition contract

Research starts at acquisition flows in the selected focus method. A query
may select one `ResourceKindIdentity`; the ArrayPool consumer selects
`ArrayPoolResourceEffectModel.BufferKind` through this query policy. Analysis
contains no ArrayPool-name or ArrayPool-result type in the generic summary.

For each selected resource kind, Research carries:

- the unchanged acquisition obligation;
- the selected resource kind;
- the physical forwarding steps already joined to stable graph edge rows; and
- path-local summary completeness.

At each forwarding step, Research resolves exactly one callee definition body
using the graph evidence's exact `DefinitionStorage`, or primary storage when
that storage is itself a definition, and then selects the incoming-parameter
flow named by the forwarding use. It never borrows a same-shaped body from
another image. Missing or ambiguous definition evidence is incomplete.

Research carries the physical call site's method generic arguments across each
forwarding step. Before selecting a typed terminal use, it substitutes the
complete detached occurrence type into the definition-local resource domain.
The substitution recurses through compound element and argument domains and
composes across multiple generic forwarding steps, including an intermediate
call that constructs a compound argument such as `T[]` from exact incoming
`T` evidence. A compound wrapper's resolved assembly, definition, and
forwarding identity is inherited evidence, not a second identity claim:
Research compares it at the recursively substituted element that owns that
identity. Missing exact argument evidence is incomplete, and a non-matching
domain remains resource-neutral; neither becomes a typed terminal.

A terminal `ResourceOwnershipPathWitness` contains the obligation, selected
resource kind, ordered physical forwarding coordinates, typed sink outcome,
sink method and offset, and path-local completeness. Finding identity uses the
acquisition coordinate, resource-kind identity and bound resource arguments,
forwarding coordinates, and typed sink identity. It does not use labels or
projection-local edge row numbers, so unrelated graph rows do not rename a
Finding. Bound type identity uses injective metadata definition names and exact
detached assembly evidence rather than display spelling.

Path-local completeness says whether every Analysis summary traversed by that
positive witness was complete. Operation completeness remains separate:
`NotRequested`, `TraversalBoundary`, `IncompleteCorrespondence`,
`BodyUnavailable`, `AnalysisFailure`, `WitnessBudget`, and `PathBudget` are
independent limits. A positive witness remains valid when another path or
unrelated body is incomplete.

## Bounds and progression

The query retains the existing witness and path budgets. A witness budget
preserves already-created positive Findings and reports `WitnessBudget`.
A path budget stops further queued traversal, preserves positive Findings, and
reports `PathBudget`.

The first body-scoped graph tier may publish the focus acquisition and its
forwarding call before a callee body is available. A later full tier reuses the
graph session, supersedes the scoped Analysis result, and composes the path
from newly available summaries. Summary production does not add graph,
source, or package acquisition.

## Fidelity gates

The focused gates establish:

- existing ArrayPool fixtures produce the same physical forwarding
  coordinates and corresponding release, storage, and caller-return outcomes
  as the unchanged legacy projection;
- two independently identified resource kinds traverse the same graph without
  obligation or Finding-key collision;
- repeated physical calls mapped to one logical edge retain distinct
  coordinates;
- call-site method-generic substitution selects a matching typed release both
  directly and across multiple forwarding steps, including compound domains
  whose element is defined in the inspected assembly and compound arguments
  constructed at an intermediate call;
- same-simple-name assembly versions do not become one bound generic domain,
  and unavailable exact generic evidence remains incomplete;
- one resource-kind identity bound to different resource arguments at one
  acquisition coordinate retains distinct, repeat-stable Finding identity,
  including nested versus namespace-qualified metadata names with the same
  display spelling;
- a resource used as a field receiver is incomplete rather than a proven field
  store, and an unsupported rootless acquisition or release keeps its method
  summary incomplete;
- missing and ambiguous body correspondence remain incomplete;
- a positive terminal witness survives unrelated incompleteness; and
- witness and path budgets preserve positive evidence while reporting their
  limits.

The legacy `ArrayPoolOwnershipFlow` and
`ArrayPoolOwnershipPathFindings` remain independently executable. They are not
adapters, inputs, or fallback results for the generic path.

## Non-claims

This design does not:

- retire or rewrite the legacy ArrayPool Analysis or Research path;
- redefine resource declarations, effect resolution, Resource Occurrence, or
  Resource Lifecycle policy;
- infer a resource kind for an incoming parameter before a caller supplies an
  acquisition obligation;
- add receiver, field, callback, reflection, function-pointer, or arbitrary
  alias traversal where Analysis cannot prove the value flow;
- change call-graph acquisition, node identity, or edge-row construction;
- claim complete ownership paths when a body, correspondence, flow, or budget
  is incomplete; or
- add CLI or Browser/Wasm presentation.

Legacy retirement, broader value-flow support, and product-host adoption remain
separately reviewed focused work.
