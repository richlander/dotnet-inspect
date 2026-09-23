# Package dependency closure diff

## Status, owner, and claim

Status: **focused query-contract proposal** for
[#8365](https://github.com/richlander/dotnet-inspect/issues/8365).

The **Package Dependency Closure Diff Query** in
`DotnetInspector.Queries` owns:

> Given two complete source-authorized package-manifest dependency traversals
> for different versions of the same Package under one traversal target,
> compare their complete reachable exact-coordinate populations by canonical
> Package ID, retain deterministic path evidence, and classify every changed
> Package through one owner-issued supply-chain baseline.

This owner defines:

- endpoint comparability;
- exact reachable dependency populations;
- canonical Package-ID grouping and exact-version-set equality;
- introduced, removed, and changed version-set transitions;
- deterministic representative path evidence;
- transition ordering;
- the complete-zero-result meaning;
- typed non-comparability when either closure is incomplete; and
- baseline classification association for each transition.

It does not own package acquisition, dependency declarations, candidate
resolution, traversal, target-framework policy, Workspace registration,
ecosystem membership, supply-chain baseline policy, command syntax, sections,
rendering, or Browser state.

## User question and real package

The pilot answers:

> Across the complete manifest dependency closures of these two Package
> versions, which Package IDs or exact version sets changed, and which changes
> fall outside my selected supply-chain baseline?

Direct declarations are not a sufficient supply-chain population. A direct
dependency can retain its identity while introducing or removing packages
several edges away, and removing one direct dependency can contract an entire
transitive branch.

The motivating nuget.org pair is:

```text
OpenTelemetry.Exporter.OpenTelemetryProtocol@1.9.0..1.13.1
Traversal target: net8.0
```

The `1.9.0` manifest closure contains this branch:

```text
Grpc.Net.Client
  -> Grpc.Net.Common
     -> Grpc.Core.Api
Google.Protobuf
```

The `1.13.1` closure removes it. `Grpc.Net.Common` and `Grpc.Core.Api` are
transitive evidence that a direct-declaration comparison cannot report.
Comparing the versions in the reverse direction demonstrates newly introduced
transitive exposure.

The product gate will exercise this pair through normal nuget.org acquisition.
Focused synthetic fixtures will preserve multiple-version, cycle, shared-node,
incomplete-endpoint, and path-tie boundaries.

## Design basis

This query composes existing owner-issued evidence:

| Concern | Owner | Consumed contract |
| --- | --- | --- |
| Exact dependency graph | [Package dependency traversal](package-dependency-traversal.md) | Exact coordinates, source-relative projections, declaration edges, root-relative reachability, failures, and completion |
| Governing framework | [Traversal target-framework policy](traversal-target-framework-policy.md) | One retained product-default or configured target for every newly reached participant |
| Dependency declarations | [Package input and dependency evidence](package-dependency-evidence.md) | Normalized declaration identity, canonical constraints, selected-group state, and contained source evidence |
| Candidate identity | [Package dependency candidate resolution](package-dependency-candidate-resolution.md) | Source-authorized exact candidates and canonical Package coordinate identity |
| Supply-chain policy | [#8368](https://github.com/richlander/dotnet-inspect/issues/8368) | One captured baseline evaluation basis and detached classification evidence for already-known Package IDs |
| Completed host boundary | [Inspection envelope](inspection-envelope.md) | A later Sections inspection hands one detached owner-issued Document to CLI and Browser/Wasm |

The traversal owner's deliberate divergence from NuGet restore remains intact.
Each manifest declaration is resolved independently. Multiple exact versions
of one Package ID may therefore coexist in one closure. This query compares
that evidence; it does not reconcile it into a hypothetical project graph.

## Analogous behavior and deliberate boundary

[`dotnet package list --include-transitive`](https://learn.microsoft.com/dotnet/core/tools/dotnet-package-list)
reports the direct and transitive packages selected by an authoritative
project restore, including one resolved version for each listed package. That
supports a flat transitive inventory as a useful review unit. It does not
transfer because this query compares package manifests without a project,
lock file, central version policy, or restore reconciliation.

The
[`cyclonedx diff`](https://github.com/CycloneDX/cyclonedx-cli#readme)
command can report component versions added, removed, or modified between two
BOMs. That supports typed component-population transitions rather than a
rendered file diff. It does not transfer BOM identity, generation,
completeness, or matching rules into this query.

The deliberate product boundary is stricter than either analogy:

- the compared population comes only from complete owner-issued traversal
  outcomes;
- one Package ID retains its full exact-version set on each endpoint;
- correspondence is never guessed between individual versions; and
- each exact coordinate retains a package-declaration path from the compared
  root.

## Request and comparability

One request carries:

- one Before `PackageDependencyTraversalOutcome`;
- one After `PackageDependencyTraversalOutcome`; and
- one owner-issued package supply-chain baseline evaluation basis from #8368,
  captured for both endpoint roots.

Each endpoint must contain exactly one root occurrence. The two root
coordinates must have the same canonical Package ID and may have different
exact versions. Both roots must authorize recursive sources.

The traversal target policies must be equal as owner-issued structural
currency. Display framework strings, selected compatible groups, or inferred
asset frameworks cannot establish that equality.

The baseline basis must name both endpoint root Package IDs as Self context
and must belong to one captured Workspace registration revision. The query
does not accept a live Workspace, read ambient latest registrations, or
reconstruct baseline membership from detached labels.

Malformed structural combinations are invalid requests. Valid endpoint
outcomes that are not complete produce a typed non-comparable outcome rather
than an exception or partial transition population.

## Complete closure population

For each endpoint, the query selects every semantic package node reachable
from that endpoint's root occurrence according to the traversal's
owner-issued root-relative reachability.

The root Package ID is comparison context and is excluded from dependency
transitions, including a cycle or another exact version that reaches the root
Package ID again.

Every other reachable node contributes its owner-issued canonical exact
Package coordinate. Source-relative manifest projections do not create
duplicate coordinates. Distinct exact versions remain distinct even when
they share a Package ID, path, declaration constraint, or source.

The endpoint population groups exact coordinates by canonical Package ID:

```text
Package ID
  Before exact-version set
  After exact-version set
```

Package ID and version equality use owner-issued canonical NuGet identity.
Display spelling, source labels, authors, descriptions, constraints, and
rendered paths never participate in grouping or set equality.

## Transition semantics

One canonical Package ID produces at most one transition:

| Transition | Before versions | After versions |
| --- | --- | --- |
| `Introduced` | Empty | Non-empty |
| `Removed` | Non-empty | Empty |
| `Changed` | Non-empty | Non-empty and unequal |

No row is produced when the exact-version sets are equal.

Every transition retains:

- canonical Package ID;
- complete ordered Before and After exact-version sets;
- versions introduced and removed by the set difference;
- baseline classification and detached baseline evidence association; and
- endpoint evidence for every retained exact coordinate.

`Changed` does not assert one-to-one version correspondence. For example:

```text
Before: [1.0.0, 2.0.0]
After:  [2.0.0, 3.0.0]
```

is one Package-ID version-set transition with `1.0.0` removed and `3.0.0`
introduced. It is not two guessed pairwise version changes.

Equal coordinate sets reached through different declarations or paths are not
a dependency-population transition. Declaration-edge and path-topology
comparison are separate questions.

## Representative path evidence

Every exact coordinate retained on an endpoint carries one deterministic
shortest path from that endpoint root.

Path distance comes from the traversal's root-relative reachability. When
several shortest paths exist, the earliest complete predecessor chain in the
traversal's stable edge order wins. This keeps evidence bounded without
claiming that the representative is the only path.

One path retains:

- the root and target exact Package coordinates;
- every intermediate exact Package coordinate;
- each traversed owner-issued declaration identity and canonical constraint;
  and
- the owner-issued edge emission authority.

The path contains at most one edge for each increasing root-relative distance,
so cycles cannot enter the representative path. All graph edges remain owned
by the traversal outcome; this query does not manufacture, delete, or
reinterpret them.

An endpoint with several exact versions of one Package ID retains one path per
exact coordinate. Presentation may summarize those paths, but the typed query
result does not discard them.

## Supply-chain baseline application

The baseline names Packages excluded from incremental-exposure highlighting.
It never changes either closure population or transition.

Every transition is classified as:

- `Baseline`, when #8368 classifies its canonical Package ID inside the
  selected baseline; or
- `Exposure`, when the exact Package identity is known and lies outside that
  baseline.

The query retains both classes. A later Supply Chain section uses `Exposure`
as its primary row population while detailed or machine projections may retain
the complete classified transition set.

The default product policy remains `SelfAndRegisteredEcosystems`. `Nothing`
therefore exposes changes for every dependency Package, while
`SelfAndRegisteredEcosystems` leaves registered platform and first-party
Packages available as path connectors and evidence without highlighting their
transitions.

Baseline membership is not publisher identity, trust, vulnerability, license,
or safety evidence.

## Completion and non-comparability

A dependency closure comparison is available only when both traversal
summaries are `Complete`.

`DepthBounded`, `SourceBounded`, and `Partial` endpoints are non-comparable.
The outcome retains:

- Before and After root coordinates and traversal target policy;
- each endpoint's completion;
- typed depth, source, resolution, manifest, projection, and work-budget
  boundaries or failures; and
- which endpoint or endpoints prevented comparison.

It emits no transition population. In particular, evidence reachable before a
failure cannot establish that a Package was removed or absent from the failed
endpoint.

Traversal cancellation produces no traversal outcome and therefore no
comparison invocation. The comparison query does not translate cancellation
into a non-comparable result.

A complete empty transition population means:

> Under the retained traversal target and package-manifest traversal contract,
> the two complete reachable dependency populations contain equal canonical
> Package-ID/exact-version sets.

It does not mean that declarations, paths, source provenance, package content,
vulnerabilities, licenses, owners, or safety are unchanged.

This is an algorithmic completeness property, not a repository-composition
absence claim. Focused Release gates cover complete and incomplete endpoint
cases directly.

## Typed result and ordering

The closed query outcome has:

- `Available`, carrying one resource-free
  `PackageDependencyClosureDiffResult`; or
- `NotComparable`, carrying both endpoint summaries and their owner-issued
  incompleteness evidence.

The available result retains:

- Before and After root coordinates;
- the exact traversal target policy;
- detached baseline evidence;
- endpoint package populations;
- ordered transitions; and
- complete-zero-result meaning.

Transitions order by canonical Package ID using ordinal case-insensitive
ordering with canonical spelling as a stable tie-breaker. Exact versions order
by NuGet precedence with canonical version spelling as a stable tie-breaker.
Representative path selection uses traversal order and does not influence row
ordering.

The result retains only detached identities, values, and evidence. It contains
no source client, store, Workspace, live registration revision, stream,
reader, lease, callback, filesystem path, or cancellation token.

## Host and rendering path

This query is an L1 producer and does not return an
`InspectionEnvelope<TContent>`.

The #8365 production plan adds a focused Sections inspection that consumes
this outcome and issues one host-observable Document with:

- comparison and policy context;
- one declared dependency-transition row population;
- exact-version and path evidence;
- endpoint coverage and failures; and
- explicit available or non-comparable state.

That completed inspection returns
`InspectionEnvelope<PackageDependencyClosureDiffDocument>`. CLI package Diff
uses shared Markout lowering for Markdown, table, TSV, JSONL, and projected
JSON. Complete Content JSON and envelope transport use the same typed
Document. Inspect Web Compare consumes that same envelope through the managed
Browser/Wasm boundary and owns only interaction and DOM presentation; it does
not reimplement closure comparison in TypeScript.

The initial CLI section is explicit because it performs exhaustive network
traversal. It does not enter the default Diff view unless later product
evidence establishes it as the command's single high-value section.
Command spelling, baseline controls, section naming, row shaping, exit status,
Share projection, and Browser placement remain owned by their focused
adoption slices.

## Pathological cases

### Multiple versions on both endpoints

One closure contains `Example@1.0.0` and `Example@2.0.0`; the other contains
`Example@2.0.0` and `Example@3.0.0`. The result is one `Changed` transition
with complete endpoint sets and explicit added/removed versions.

### Shared node with several parents

Two branches reach one exact coordinate at equal distance. The coordinate
appears once. Its representative path uses stable traversal edge order, while
the traversal outcome remains authoritative for every parent edge.

### Same population, different route

Both closures reach equal exact coordinates, but a dependency moves from one
parent to another. The result is empty because this query compares dependency
population, not path topology.

### Cycle to the root Package ID

A dependency path reaches the same Package ID as the compared root. Every
version of that root Package ID remains Self context and is excluded from
dependency transitions. The traversal retains the closing edge.

### One incomplete endpoint

Before is complete and After has a candidate-resolution or work-budget
failure. The outcome is `NotComparable`; no package is reported as removed
from After.

### Complete no-matching-framework endpoint

One root has the owner-issued complete
`NoMatchingTargetFramework` selection state, while the other has a selected
dependency group. The empty endpoint closure remains complete evidence under
the retained target, and ordinary transitions describe the population
difference. The endpoint selection states remain available to the later
Document.

## Required gates

Focused Release tests must establish:

- the real
  `OpenTelemetry.Exporter.OpenTelemetryProtocol@1.9.0..1.13.1/net8.0`
  comparison reports removed `Grpc.Net.Common` and `Grpc.Core.Api`;
- reversing that pair reports those transitive Packages as introduced;
- direct, transitive, shared, cyclic, and repeated-coordinate paths contribute
  one correct coordinate population;
- several versions of one Package ID produce one version-set transition;
- equal version sets reached through different paths produce no transition;
- each exact coordinate receives the deterministic shortest representative
  path;
- both endpoint roots and the traversal target must correspond exactly;
- every non-`Complete` endpoint produces `NotComparable` with no transitions;
- a complete empty result carries the narrow equality meaning above;
- `Nothing` and `SelfAndRegisteredEcosystems` change classification without
  changing transition or path evidence;
- artifact-origin text remains contained through the result; and
- result construction and serialization remain NativeAOT- and
  single-threaded Browser/Wasm-compatible.

The later shared-inspection gates must establish equal Document Content,
baseline evidence, and diagnostics for equivalent CLI and Browser plans.

## Non-goals

This design does not:

- compare direct declarations alone;
- reproduce NuGet restore reconciliation;
- compare a package-manifest graph with a restored-project graph;
- compare declaration-edge or path topology;
- enumerate every root-to-package path;
- infer one-to-one correspondence between exact versions;
- use baseline policy to limit acquisition or traversal;
- infer first-party ownership or ecosystem membership;
- classify vulnerabilities, licenses, publishers, maintainers, or malicious
  behavior;
- add URL-literal or text-containment transitions;
- define a general SBOM format;
- change package dependency traversal; or
- define host syntax, rendering, or Browser interaction.
