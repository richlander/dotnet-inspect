# Inspection space architecture

The core of `dotnet-inspect` is not its decompiler, analysis engine, metadata
scanner, or any other individual fact producer. The core is the **inspection
space**: the shared environment in which subjects are resolved, inspection work
is requested and governed, results retain identity and provenance, and different
kinds of evidence can be composed.

Analysis, decompilation, metadata projection, source inspection, package
inspection, and future producers extend that space. They are add-ins in the
architectural sense: statically linked, NativeAOT-friendly producers behind
shared contracts, not dynamically loaded plugins.

## Status

This document describes the target core architecture and the principles that
govern its migration. The repository already contains important parts of it,
but not every assembly-backed command runs through one workspace and typed query
plan yet.

Mechanism-specific documents remain authoritative for the current behavior,
target design, and verification they own. In particular:

- [Inspection layers](design/inspection-layers.md) owns host and query-layer
  boundaries.
- [Assembly inspection query model](design/assembly-inspection-query.md) owns
  the resolution-to-inspection currency.
- [Progressive disclosure](design/progressive-disclosure.md) owns current
  command backpressure and defaults.
- [Cache concurrency and publication](design/cache-concurrency.md) owns package
  cache coordination and atomic publication.
- [InertText](design/inert-text.md) owns treated-text semantics and gates.
- [Untrusted data threat model](design/untrusted-data-threat-model.md) owns
  security boundaries and priorities.

The complete end-to-end claim that every presentation path accepts only inert
artifact text remains **unverified**; the InertText document records the
remaining boundary enumeration.

## Goals: Rich, Fast, Safe

The inspection space is organized around three goals:

```text
                         Rich
                    deep · broad · joined
                       /           \
                      /             \
                  Fast ------------- Safe
             demand · reuse     typed · bounded
```

None is subordinate to another. A design that is rich but eagerly computes
everything is not fast. A cache that is fast but can return bytes from the wrong
producer is not safe. A safety transform that destroys identity or evidence is
not rich.

### Rich

Richness has three dimensions.

#### Deep data for each inspection type

An inspection type should expose the evidence needed to answer its question,
not only the first summary the CLI happens to render. Typed identities,
provenance, failures, and producer-native detail remain available for focused
queries and later composition.

Depth does not require a large default document. The result can be rich while a
host initially requests or renders a compact projection.

#### Many inspection types

The space admits many typed query/result pairs: package facts, API surfaces,
source provenance, integrations, dependencies, metadata, implementation facts,
performance evidence, decompiled source, and others.

The core does not gain one branch per type. A producer declares what it needs,
what it costs, what scope it runs over, and what typed result it returns. Adding
an inspection type extends the catalog without changing workspace semantics.

#### Shared foundations enable joins

The most valuable answers often cross producer boundaries:

- integrations across companion assemblies;
- package and assembly provenance;
- API identity joined with source or implementation evidence;
- analysis observations projected onto decompiled source;
- comparisons across framework or package-version contexts.

Those joins must use shared typed identity, coordinates, provenance, and context
boundaries. Display text is not identity, and a renderer is not a composition
layer.

### Fast

Fast does not mean parallel. The first intended execution target for the
workspace is a single-threaded Wasm host, and sequential execution remains a
supported, deterministic policy everywhere.

The architecture gets speed from avoiding work:

- **Demand-driven planning.** Run only the queries and prerequisites requested
  by the host.
- **Progressive acquisition.** Permit cheap, high-value results before
  expensive or exhaustive layers.
- **Shared lifetimes.** Open or materialize one artifact generation once and
  reuse it across the queries that consume it.
- **Semantic caching.** Key reusable results by every input that can change
  their meaning, including content, source authorization, options, and producer
  version where applicable.
- **Single-flight and atomic publication.** Share equivalent in-process work
  and never expose partially published persistent content.
- **Budgets.** Bound graph traversal, retained content, output, network work,
  and other input-amplified costs.

Concurrency is an executor policy layered on this model. A native host may run
independent assembly work concurrently, but the same plan must also run
sequentially with identical result ordering and failure semantics.

### Safe

The inspection space accepts artifacts as untrusted data, never as code or
authority.

- Inspected assemblies are parsed, not loaded.
- Resolution carries typed identity and provenance rather than handing
  inspection a bare path.
- Network, source-content, and unbounded work require explicit capability and
  cost authorization.
- Malformed input, failed acquisition, and incomplete analysis remain visible
  as typed outcomes.
- Cache hits are valid only when the current request authorizes and identifies
  the stored result.
- Artifact-derived work is bounded so hostile input cannot silently turn a
  small request into unlimited CPU, memory, network, or output.
- Artifact text remains exact while it participates in identity and control
  flow, then crosses a structural presentation boundary as `InertString`.
  Format-specific escaping composes after that; it does not replace inertness.

Safety preserves evidence. `InertString` visually encodes rather than deleting
hostile text, and typed rejection refuses invalid input rather than repairing it
into a plausible answer.

## The inspection space

Conceptually, an inspection run combines three things:

```text
inspection space = workspace contexts × requested queries × execution policy
```

The product does not materialize that Cartesian product. The plan selects a
small demand-driven path through it.

```text
CLI · Wasm · agent · service host
                |
                | typed requests
                v
       +----------------------+
       |   Inspection plan    |
       | scope · cost · caps  |
       | dependencies · budget|
       +----------+-----------+
                  |
                  v
       +----------------------+
       |      Workspace       |
       | assembly groups      |
       | identity · provenance|
       | acquired generations |
       +----------+-----------+
                  |
          sequential baseline
          optional concurrency
                  |
        +---------+----------+
        |                    |
        v                    v
 metadata · packages   analysis · decompiler
 source · APIs         research · future producers
        |                    |
        +---------+----------+
                  |
                  v
       typed results · failures
       identity · provenance
                  |
                  v
       sections · shapes · formats
       inert text · structural escaping
```

### Workspace

Every invocation that inspects assemblies should use a workspace internally.
For the CLI it is normally ephemeral; a Wasm or service host may retain it and
run several query plans over the same acquired content.

A workspace contains one or more **assembly context groups**. A group is one
binding-consistent universe: root assemblies, dependency assemblies, target
framework and runtime identity when known, and the resolution policy that chose
them.

Queries may cross assembly boundaries within a group. They must not infer a
relationship across groups. Multiple groups support comparisons such as two
package versions or framework contexts without mixing their bindings.

The smallest case remains cheap: one workspace, one group, one root assembly,
and one requested query.

### Query plan

A host asks for typed inspections, not scanner names or output sections. Each
query declares:

| Property | Question answered |
| --- | --- |
| Scope | Does it run for one assembly, one context group, or several groups? |
| Inputs | Which typed content and prior results does it consume? |
| Cost | Is the work bounded, network-bound, source-content-bound, or exhaustive? |
| Capabilities | What must the caller authorize? |
| Dependencies | Which producer results must exist first? |
| Result | Which typed value or failure does it return? |

CLI sections and Wasm views lower their selections into this plan. They do not
own acquisition cost or producer dependencies.

The existing `ScannerRegistry` is an assembly-local predecessor: its explicit
prerequisites, once-per-run resources, deterministic ordering, and tracing are
useful foundations. String keys, mutable CLI models, path-shaped inputs, and
library-command ownership are migration boundaries rather than workspace
contracts.

### Executor

Sequential topological execution defines the baseline. It works in
single-threaded Wasm, is easy to audit, and provides the reference ordering for
every other policy.

A later executor may schedule independent nodes concurrently. Concurrency must
not alter:

- which work the plan authorizes;
- result and row ordering;
- acquisition or resource budgets;
- failure visibility;
- assembly, group, or producer provenance.

Producers receive the narrow context named by their scope, not a mutable
workspace object. This keeps the workspace from becoming a god object and makes
cross-group access explicit.

## Core currencies

The core is defined more by the values crossing its boundaries than by project
names.

### Identity and provenance

Resolution returns a descriptor such as `ResolvedAssemblyReference`: identity,
an opener for the selected content, and structured resolution provenance.
Inspection does not discard that information into a bare path and later
reconstruct it.

Identity, correspondence, provenance, and display remain separate. Joins use
the typed currencies; presentation chooses spelling afterward.

### Results and failures

Queries return typed results. Expected bad-input and acquisition failures are
typed outcomes with subject provenance, not empty collections shaped like
success. Unexpected producer defects remain fatal rather than being relabeled
as bad input.

Partial group results are valid only when their failures are carried beside
them and the result contract says partial inspection is meaningful.

### `CoreCache`

`CoreCache` is shared infrastructure for category roots, path-safe hashed keys,
maintenance, and cache telemetry. It is a mechanism, not a semantic authority.

The cache owner for each result must still define:

- the complete semantic key;
- producer and source provenance;
- freshness and versioning;
- validation on read;
- publication and concurrency behavior;
- whether a miss may authorize network work.

A cache may make a correct query faster. It must not change which query was
asked or which producer's bytes the caller is authorized to inspect.

### `InertString`

`InertString` is the presentation currency for untrusted artifact text. Its
construction applies a closed text policy and its type records that the value
crossed the containment boundary.

It belongs late in the pipeline. Metadata names, package ids, paths, and source
text stay exact while they participate in identity, matching, resolution, and
analysis. They become inert at the last shared structural boundary before
presentation, when the sink policy is known.

Structural escaping remains the renderer's responsibility. Inert text prevents
terminal control, visual reordering, and invisible agent-context payloads;
Markdown, JSON, TSV, and other writers separately escape their grammars.

## Add-ins

An add-in owns domain facts and algorithms. It may be large and sophisticated,
but it integrates through the same core contracts.

Examples include:

- Metadata producing assembly and API facts.
- Analysis producing IL-body evidence and graph facts.
- Decompiler producing source-shaped projections.
- Research composing evidence owned by other producers.
- Package and source producers contributing acquisition and provenance facts.

An add-in does not:

- parse CLI arguments or choose output formats;
- invent a second acquisition or cache policy;
- infer identity from display strings;
- hide its cost or capabilities behind a section;
- bypass workspace grouping for cross-assembly work;
- send untreated artifact text directly to a sink.

“Add-in” does not imply runtime discovery, reflection loading, or an external
compatibility surface. Static registration is compatible with the role and
with NativeAOT.

## Architectural tests

A proposed core change should answer all three goals.

| Goal | Questions |
| --- | --- |
| Rich | Does it preserve producer-native depth, admit more inspection types, or improve typed joins? |
| Fast | Can it avoid unrequested work, share acquisition, cache safely, and run sequentially? |
| Safe | Are identity, provenance, capability, budgets, failures, and presentation containment explicit? |

Common false tradeoffs are rejected:

- “Rich” is not permission to collect everything eagerly.
- “Fast” is not permission to require threads or weaken cache identity.
- “Safe” is not permission to delete evidence, collapse failures to empty
  output, or encode identity before matching.

The inspection space is successful when a host can ask a deep question across
many kinds of evidence, pay only for the requested answer, and safely retain
enough identity and provenance to trust what was joined.
