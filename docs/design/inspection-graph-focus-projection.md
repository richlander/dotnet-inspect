# Inspection Graph focus projection

## Status, owner, and claim

Status: **design contract** for
[#8403](https://github.com/richlander/dotnet-inspect/issues/8403).

The **Inspection Graph Focus Projection** in `DotnetInspector.Queries` owns:

> Given one finite, already-produced `InspectionGraphDocument`, its retained
> request and completeness state, owner-issued scope and affiliation
> decisions, optional resolved targets, and optional typed signal decisions,
> project the same evidence into an internal, exit-frontier, or target-corridor
> view whose focus, connector, exit, target, and unclassified roles remain
> explicit.

The projection preserves semantic subjects, relationship direction, physical
and derived occurrences, limits, failures, and producer evidence. It may omit
topology outside the requested question and add derived role characteristics;
it never relabels a relationship, manufactures an occurrence, or strengthens a
source completeness claim.

This is one new cross-cutting pattern owner. It does not redefine the
construction, identity, lifetime, or failure semantics of Inspection Graph,
Call Graph, dependency, Workspace, ownership, Integration, Finding, or
analysis producers. Each topology adopts the pattern separately.

## User questions

The projection gives one vocabulary to several current and planned questions:

| Question | Affiliation | Extent | Terminal | Signal |
| --- | --- | --- | --- | --- |
| Is my code internally tangled? | Self | Internal | None | All or structural concern |
| Where does my code leave its scope? | Self or Self + Friend | Exit frontier | Any exit | All |
| Which exits eventually reach an Integration? | Self | Target corridor | Integration target | All |
| Which internal paths contain costly or risky work? | Self | Internal | None | Selected signals |
| Which authored callbacks cross an external control boundary? | Self | Internal or exit | None | Callback overlay |

The axes compose only when the selected relationships and evidence support the
question. Unsupported combinations fail with guidance; they do not silently
drop an axis.

## Motivating demonstrations

### Package supply-chain frontier

The existing `Microsoft.Extensions.Http.Polly` product scenario classifies the
root and registered Microsoft.Extensions packages as context while preserving
Polly as incremental exposure. The
[package supply-chain baseline](package-supply-chain-baseline.md), extracted by
PR [#8383](https://github.com/richlander/dotnet-inspect/pull/8383), owns that
policy outside the member call graph. Its classification is one affiliation
input to this projection, not the shared projection itself.

The existing OpenTelemetry 1.18.0 external-call scenario demonstrates the
topology shape. A selected member reaches `OpenTelemetry.Api` through local
callers:

```text
AddOpenTelemetrySharedProviderBuilderServices
  -> Sdk.get_SuppressInstrumentation
  -> SuppressInstrumentationScope.get_IsSuppressed
  -> OpenTelemetry.Api: RuntimeContextSlot<T>.Get
```

The exit frontier retains the external boundary call and a deterministic
shortest internal connector. It does not retain unrelated internal work or the
external library's entire implementation.

### Multi-library Integration corridor

The pinned
[`IChatClient` dual-lens graph](../workflows/discovery/aspire-ai-package-graph.md)
supplies the target-corridor scenario. A call can leave the selected code,
cross several libraries or adapters, and eventually reach an exact
`IChatClient` member or a target carrying the `integration.ai` concept.

The result answers:

```text
Which first exits from this scope lie on a supported route to IChatClient?
```

It retains every such first exit and the union of the evidence-backed topology
connecting those exits to the target. It does not flatten intermediate
libraries into one fabricated source-to-target edge.

## Basis and deliberate boundary

[Inspection Graph document](inspection-graph-document.md) owns typed subjects,
relationships, occurrences, characteristics, limits, and failures.
[Inspection Graph modes](inspection-graph-modes.md) owns seeds, induced input
sets, relationship selection, traversal direction, and bounds. Focus
projection consumes those settled values after producer execution.

[External-focused call topology](external-focused-call-topology.md) is the
current call-specific analogue. It established boundary rows, deterministic
shortest connectors, explicit unknown membership, and physical-call receipt
preservation. This pattern generalizes those observable properties over
Inspection Graph without transferring Call Graph's topology or identity
contracts. A later focused adoption may retire the parallel call-only
projection after exact parity.

The conventional analogues recorded by that design remain applicable:

- Visual Studio Code Maps separates component boundaries from contributing
  member relationships;
- NDepend distinguishes coupling graphs, minimal paths, and all-paths graphs;
  and
- CodeQL path queries bind explicit source and sink sets to a selected edge
  relation.

This design is stricter about evidence. A retained route keeps its original
typed relationship and occurrences. Unknown classification never becomes
external merely because a conventional graph renderer would omit it.

## Existing graph axes remain independent

Focus projection consumes, rather than replaces, the existing graph axes:

| Existing axis | Supplying owner |
| --- | --- |
| Workspace participants | Inspection Workspace and producer plans |
| Seed or induced input mode | Inspection Graph modes |
| Traversal relationships and direction | Relationship descriptors and mode request |
| Subject lens | Inspection Graph document |
| Produced characteristics | Characteristic descriptors and producers |

It adds four projection choices:

| Focus choice | Question |
| --- | --- |
| Affiliation | Which classified subjects are focus candidates? |
| Extent | Internal topology, first exits, or routes to targets? |
| Terminal | Is any exit sufficient, or must the route reach a typed target? |
| Signal | Is every candidate relevant, or only candidates with selected evidence? |

Output format remains a presentation-only choice.

The source document is finite before focus projection begins. Projection does
not acquire another package, decode another assembly, widen a producer scope,
or rerun a signal analysis. An adopter that needs a larger source graph requests
that work through its owning query before invoking this projection.

## Scope and affiliation are different facts

**Scope** classifies a subject as inside, outside, or unknown relative to the
selected type, library, package, Workspace group, or other owner-issued
boundary.

**Affiliation** answers how an already-identified subject relates to the
selected product context:

- **Self** — selected roots or subjects admitted by exact self policy;
- **Friend** — subjects admitted by one or more explicit affiliated
  populations; and
- **Other** — classification is complete and neither Self nor Friend matched.

Self and Friend are independent, set-valued facts and may overlap. A view may
select Self, Friend, Self or Friend, or Other. Other is a derived complete
complement:

```text
Other = ClassificationComplete
        && !Self
        && !Friend
```

Missing, ambiguous, conflicting, or incomplete classification remains
**unknown**. It is not Other and cannot support an absence claim about
external relationships.

The classifier owns the evidence that establishes affiliation. For packages,
that may include canonical root package IDs, captured first-party prefix
policy, and a captured ecosystem-registration revision. For assemblies or
members it may use different owner-issued identities and policy. A prefix can
participate in policy only after the relevant owner has established the
subject's identity; a namespace, assembly label, package display name, or
formatted node text never proves ownership.

Affiliation is not source authority, publisher identity, trust, license,
vulnerability, or security classification.

## Extent

### Internal

Internal extent starts from the source document's existing origins:

- bound seeds for a seeded mode; or
- the explicit input closure for an induced mode.

It admits selected relationships whose semantic endpoints are inside the
owner-issued scope. Traversal follows the requested semantic direction without
reversing stored edges.

With the `All` signal selection, the result retains the admitted internal
topology. With a notable signal selection, it retains matched focus evidence
plus one deterministic shortest connector from an origin to each match.
Connectors shared by several matches remain singular. An isolated valid input
remains represented even when no relationship or signal matches.

### Exit frontier

An exit is a selected relationship whose traversal crosses from inside to
outside the owner-issued scope. Incoming traversal applies the corresponding
outside-to-inside question without reversing the relationship's stored
semantic direction.

For an induced input, exit-frontier projection can retain every classified
exit from the input closure. For seeded input, it retains every
directionally reachable exit and one deterministic shortest internal connector
from an origin to that exit. A boundary incident directly on an origin has an
empty connector.

An edge incident on an unknown scope or affiliation subject remains an
unclassified boundary candidate. It stays distinguishable from a positive exit
and prevents a complete all-exits claim. Relationships entirely outside scope
are not part of an exit-frontier result.

### Target corridor

Target-corridor extent requires a finite owner-resolved target set. Every
binding names an exact retained graph target and the terminal subject at which
route reachability is tested. Exact members, types, assemblies, and packages
can bind directly. Findings or Integration concepts bind only after their
owners resolve them to retained typed evidence and terminal subjects. The
projection never resolves a target from display text.

The corridor is the union of source nodes, relationships, and occurrences that
lie on at least one supported route from an origin to a resolved target through
the selected traversal relationships. It can cross several outside libraries
or packages. The first inside-to-outside relationship on each retained route is
an exit; every distinct first exit in the corridor remains visible.

The corridor is a subgraph, not a list of simple paths. Cycles terminate at
finite reachability closure and retain each semantic node, relationship, and
occurrence once. An implementation may use strongly connected component
condensation internally, but the result preserves the original cyclic
topology and never selects an arbitrary representative method for display.

If a resolved target is reachable entirely inside scope, the route contains no
external exit. That is not evidence of an external Integration. If no target is
reachable, the result distinguishes complete no-route evidence from incomplete
source topology, unresolved targets, exhausted bounds, and unavailable
participants.

## Signal and notability

Signal selection is either:

- **All** — no signal-based focus reduction; or
- **Notable** — focus candidates must match one or more selected typed signal
  decisions.

A signal owner defines its target kind, value, threshold or predicate,
population, completion, and failure semantics. Examples include:

- fan-in, fan-out, implementation size, or structural convergence;
- allocation, copy, unsafe, exception, or I/O evidence;
- structural-clone membership;
- async-sibling, callback, or Integration relationships; and
- owner-issued Findings.

Focus projection consumes settled match, non-match, and unknown decisions. It
does not interpret a missing value as zero or non-notable, choose a generic
importance threshold, or claim runtime heat from static evidence.

Topology discovery precedes notable reduction. A non-notable node may remain a
connector to a notable focus, exit, or target. Seeds, explicit inputs,
mandatory exit or target evidence, relevant unclassified candidates, limits,
and failures survive even when they do not match the selected signal.

Notability cannot make an unsupported extent/relationship combination valid.
For a target corridor, the corridor remains the explanation required by the
terminal question; signal roles may highlight evidence within it but cannot
remove the route and leave a disconnected target.

## Traversal relationships and overlays

Only relationships selected for traversal determine reachability. Their
descriptors continue to own admitted endpoint kinds, direction, prerequisites,
and roll-up.

Other typed relationships may contribute signal or overlay evidence without
changing reachability. For example:

- ordinary calls can drive a call corridor;
- package dependencies can drive a dependency corridor;
- an async-sibling opportunity can make a call node notable without pretending
  the alternative call occurred; and
- a structural clone can highlight related implementations without becoming a
  call edge.

A lambda or callback authored by Self remains self-authored when external code
invokes it. Callback or control-transfer evidence is a separate relationship or
characteristic. The projection may highlight that overlay; it never fuzzily
reclassifies the authored subject as Friend or Other.

## Derived roles and evidence

Projection roles are additive typed facts over retained graph targets. They do
not replace node roles or relationship descriptors.

The shared role vocabulary is:

| Role | Meaning |
| --- | --- |
| Focus | Satisfies the selected affiliation and signal question |
| Connector | Retained to explain reachability from an origin |
| Exit | First classified relationship crossing the selected scope |
| Target | Resolved terminal evidence for a target-corridor request |
| Unclassified boundary | Could affect an exit claim but lacks complete classification |

A target can also be focus, and an exit can also carry notable evidence.
Roles therefore form a set rather than one mutually exclusive enum.

Every derived role cites the retained source targets from which it was derived.
The projected document may assign new dense document-local IDs, but it retains
owner-issued subject identities, relationship descriptors, occurrence
identities, evidence, and semantic direction. Hosts never recover a role from
labels, groups, path position, or edge color.

The result also retains the focus request and detached classifier, target, and
signal evidence needed to explain the projection without a live Workspace or
producer session. Resource-bearing inputs remain behind their owning boundary.

## Completion and absence

Positive retained evidence remains valid when another part of the request is
incomplete. Focus projection never discards a known exit or target route merely
because another endpoint is unknown.

A complete empty or absence result requires all of the following for the
requested question:

1. the source relationship population and traversal are complete;
2. scope and affiliation classification are complete for every relevant
   endpoint;
3. target resolution and target population are complete when a terminal was
   selected;
4. selected signal evaluation is complete when Notable was selected;
5. focus projection reached finite closure without exhausting a bound; and
6. no retained producer failure can affect the requested population.

When any condition is not met, the result retains its positive evidence and
states the corresponding unknown, limit, or failure. It never reports
“no exits,” “no route,” “only my code,” or “no notable relationship” from a
strict subset.

Focus projection cannot improve source completeness. A known external endpoint
is an expected frontier, but a depth-limited internal connector, unresolved
dispatch, missing dependency participant, or incomplete occurrence population
remains a source limitation.

## Output and rendering

The shared result remains a host-neutral typed Inspection Graph document or an
owner-specific document that contains it. Structured subjects, relationships,
occurrences, projection roles, classifier evidence, targets, signal decisions,
limits, and failures reach the host boundary without presentation text.

CLI adoption lowers that document through Markout for graph, table, Markdown,
and structured sinks. Browser/Wasm consumes the same managed content through
its graph viewer. The hosts may choose different layouts and progressive
disclosure, but neither reclassifies affiliation, recomputes corridors, chooses
connectors, or infers roles.

This design adds no host-specific rendering path and does not change current
defaults.

## Pathological cases

Implementation adoptions preserve these contract cases:

- one subject matches both Self and Friend;
- complete classification derives Other while unknown classification does not;
- a direct exit needs no connector;
- equal shortest frontier connectors resolve deterministically;
- internal cycles terminate without duplicate evidence;
- several first exits converge through different libraries on one target;
- one exit reaches the target while a neighboring exit does not;
- a target lies inside scope and therefore proves no external exit;
- an unreachable target is distinguishable from unavailable target evidence;
- a notable descendant remains visible behind non-notable connectors;
- unknown signal evidence does not become non-notable; and
- a self-authored callback invoked by external code keeps Self affiliation and
  receives separate control-boundary evidence.

Each adopting owner supplies its own authentic fixture and Release gates. The
OpenTelemetry and `IChatClient` scenarios are the shared real-asset
demonstrations; they do not replace adopter-specific product gates.

## Adoption and retirement

The counted production path is five focused steps:

1. Lock this shared focus-projection contract.
2. Adopt it for the current external-focused call view, prove exact parity for
   boundary, shortest-connector, unknown, receipt, and completeness behavior,
   then retire the parallel call-specific projection.
3. Adopt owner-issued package and assembly affiliation for supply-chain and
   internal-code views without changing their classifiers or acquisition.
4. Adopt typed signal decisions for internal notable views without moving
   metric or Finding semantics into the projection.
5. Adopt Integration target corridors and carry the shared result through
   Sections to CLI/Markout and Browser/Wasm using the pinned `IChatClient`
   demonstration.

PR #8383 remains a focused package-policy transfer and does not absorb this
pattern. Every adoption after the shared contract is a separate owner-focused
issue or stack slice. An adopter retains its existing path until shared
projection reaches behavioral parity; no compatibility alias preserves a
superseded internal model after retirement.

## Required implementation gates

The shared projection and its focused adoptions collectively gate:

1. overlapping Self and Friend affiliation plus complete-complement Other;
2. unknown classification remaining visible and preventing an external
   absence claim;
3. internal induced topology for seeded and explicit-input modes;
4. all classified frontier exits plus deterministic shortest connectors;
5. target-corridor reachability across multiple libraries with every first
   exit retained;
6. cyclic corridors terminating without lost or duplicated evidence;
7. relationship direction and physical occurrence identity surviving every
   projection;
8. notable focus retaining required connectors and unknown signal evidence;
9. source limits and producer failures surviving projection;
10. complete empty results only under the full completion conjunction;
11. exact parity before retiring the current call-specific external-focus
    path; and
12. equal typed results for equivalent CLI and Browser/Wasm plans.

Until an adoption names its Release gate, its behavior in this section remains
**unverified**.

## Non-goals

This design does not:

- infer subject identity, ownership, provenance, or affiliation;
- make package prefixes or namespace text identity;
- define Workspace registration, package baseline, or friend policy;
- acquire or widen call, dependency, metadata, or Integration topology;
- define signal values, thresholds, Findings, or runtime importance;
- resolve Integration concepts to graph subjects;
- turn overlays into traversal relationships automatically;
- enumerate every simple path;
- replace Inspection Graph modes, lenses, relationships, or characteristics;
- define public command spelling, serialized schema, or browser interaction;
- classify affiliation as trust, security, publisher, license, or
  vulnerability; or
- claim unbounded whole-program completeness.
