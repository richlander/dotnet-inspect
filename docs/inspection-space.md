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
govern its migration. No command runs through a workspace or typed query plan
today. Existing foundations include shared image and inspection session
ownership, catalog generations, `CoreCache`, typed provenance and resolution
currencies, and `InertString`; the workspace and query-plan model describes how
those pieces will be composed.

Mechanism-specific documents remain authoritative for the current behavior,
target design, and verification they own. In particular:

- [Inspection layers](design/inspection-layers.md) owns host and query-layer
  boundaries.
- [Assembly inspection query model](design/assembly-inspection-query.md) owns
  the resolution-to-inspection currency.
- [Type, member, and API representation](design/type-member-api-representation.md)
  owns the map of lookup, shape, address, resolution, and correspondence
  currencies.
- [Finding coordinates](design/finding-coordinates.md) owns subject,
  correspondence, order, and provenance axes for Findings.
- [Member body substrate](design/member-body-substrate.md) owns body-local
  coordinates and bound versus portable source projections.
- [Type forwarding resolution](design/type-forwarding-resolution.md) owns
  catalog generations and definition correspondence.
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

The complete content-authorization claim is also target behavior. The current
package implementation may read source-blind global-folder content and derive
version candidates from content caches. The cache document records that
deviation under
[#3752](https://github.com/richlander/dotnet-inspect/issues/3752), with
provenance-matched payload work in
[#3767](https://github.com/richlander/dotnet-inspect/pull/3767).

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
layer. The shared foundation does not impose one universal identity. It gives
each domain enough context to issue the correspondence currency its joins
require.

### Fast

Fast does not mean parallel. The first intended execution target for the
workspace is a single-threaded Wasm host, and sequential execution remains a
supported, deterministic policy everywhere.

The architecture gets speed from avoiding work:

- **Demand-driven planning.** Run only the queries and prerequisites requested
  by the host.
- **Progressive acquisition.** Permit cheap, high-value results before
  expensive or exhaustive layers.
- **Shared lifetimes.** Open or materialize one artifact generation once, then
  reuse it through owner-controlled leases across the queries that consume it.
- **Frozen contexts.** Reuse binding and correspondence work inside an explicit
  catalog generation and the resolution and authorization policy snapshot that
  produced it; advance the generation rather than mutating its answers.
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
- Availability is not authorization. Cached or retained content is usable only
  when the current request authorizes its producer and coordinate.
- Network, source-content, and unbounded work require explicit capability and
  cost authorization.
- Malformed input, failed acquisition, and incomplete analysis remain visible
  as typed outcomes.
- Cache hits are valid only when the current request authorizes and identifies
  the stored result.
- Reader-local handles, catalog keys, and other bound currencies cannot outlive
  or escape the owner that gives them meaning.
- Equality, path spelling, display text, and durable addresses do not substitute
  for owner-issued correspondence.
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
inspection space = inspection contexts × requested queries × execution policy
```

The product does not materialize that Cartesian product. The plan selects a
small demand-driven path through it. **Inspection context** is a conceptual
role, not a shared base type. Assembly-backed contexts come from assembly
context groups. Feed discovery, package metadata, and other operations that do
not inspect assemblies may use narrower source or artifact contexts without
creating a fake assembly group.

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
       |  Inspection contexts |
       | assembly groups      |
       | source · artifact    |
       | identity · provenance|
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

An operation that does not inspect assemblies need not create an empty
workspace. It receives the narrow source, artifact, or request context declared
by its query. A discovery query may return typed inputs that an authorized later
stage uses to create a workspace; unresolved discovery terms do not masquerade
as a binding-consistent assembly group.

A workspace contains one or more **assembly context groups**. A group is one
binding-consistent universe: root assemblies, dependency assemblies, target
framework and runtime identity when known, and the resolution policy that chose
them. It also retains the acquisition provenance and policy inputs needed to
decide whether a query may use their content. Authorization remains a decision
for the current query plan, not a permanent property of the group.

Queries may cross assembly boundaries within a group. They must not infer a
relationship across groups. Multiple groups support comparisons such as two
package versions or framework contexts without mixing their bindings.

Domain catalogs operate inside a group. A catalog may advance through
progressive generations as new candidates or binding roots are discovered while
the group itself remains alive. Each generation is scoped to the resolution and
authorization policy snapshot that produced it. A later query plan may reuse
the generation only when the domain owner verifies that the plan has a
compatible policy; otherwise it requires a separate generation or catalog.
Reauthorizing an image lease alone is insufficient because binding and
correspondence answers can themselves reveal a candidate the later plan may not
use.

A query execution attempt that consumes catalog-bound values runs against one
frozen generation. A domain may return a typed plan-expansion request when the
manifest lacks required work. The inspection coordinator quiesces consumers of
the predecessor, unions the request into the plan, asks the domain owner to
freeze a successor, and restarts the affected work. The successor does not
mutate the predecessor, but the owner may invalidate predecessor contexts,
tokens, and leases when it publishes the successor.

The smallest case remains cheap: one workspace, one group, one root assembly,
and one requested query.

### Inspection bundles and demos

A host build may include zero or more immutable **inspection bundles**. For an
assembly-backed scenario, the bundle may carry a portable workspace definition
from which the host creates an ordinary runtime workspace. It never contains a
serialized live workspace.

A bundle may contain:

- a stable bundle id and descriptive metadata;
- zero or more workspace definitions, each with one or more context-group
  definitions;
- embedded artifact content, typed acquisition locations, or domain-typed
  runtime input slots, with the identity, digest, and provenance evidence
  appropriate to their source;
- required producer capabilities; and
- optional named query-plan and view presets.

The optional workspace definition, query preset, and view or navigation preset
remain separate. A **demo scenario** names one composition of them. A
workspace-free scenario omits the workspace definition but still names the
embedded input, typed acquisition location, or domain-typed runtime input slot
used by its source- or artifact-scoped query. A discovery-first scenario may use
a typed query result to instantiate a workspace in a later authorized stage.
Several scenarios may reuse one workspace definition, and a host may inspect
the definition without running a preset or acquiring its inputs.

Selecting a scenario lowers into the same acquisition and typed query paths
used by an interactive request; it does not create a second demo-only execution
path.

A bundle contains no live streams, `PEReader` instances, sessions, acquisition
registrations, candidate ids, catalog generations, join tokens, cached verdicts,
or authorization decisions. Loading a bundle materializes only immutable
definitions and presets. It performs no source discovery, artifact acquisition,
registration, image opening, or catalog construction.

For an assembly-backed input, the first authorized query plan that needs it asks
the normal acquisition owner to create its descriptor and registration lazily,
then asks the domain owner for a catalog under that plan's policy snapshot. A
workspace-free query asks its source or artifact owner only for the narrow
context its operation declares; no assembly registration, group, or catalog is
implied. A persistent host may retain a resulting workspace afterward under the
normal lifetime and budget rules.

Hosts statically register the bundles they choose to ship. Excluded bundles and
their definitions and embedded artifact bytes do not enter the build. Included
bundles require no runtime plugin discovery or reflection loading. A
self-contained bundle can run without filesystem or network access; a bundle
that names package, platform, project, or local acquisition locations uses the
normal owner and capability gates for those sources. This keeps the model
compatible with trimming, NativeAOT, both online and offline Wasm demos, and
non-browser hosts.

Bundle inclusion is a build-time publication decision for every field, not only
embedded bytes. The publisher must be authorized to disclose its scenario
metadata, presets, package ids, source endpoints, paths, and artifact content to
every build recipient. Runtime scenario authorization cannot conceal
information already shipped in a Wasm or native binary.

Sensitive coordinates and sensitive acquisition locations remain outside the
bundle. The bundle declares a domain-typed runtime input slot that a host
supplies at runtime instead. The supplied value follows the same acquisition
owner and input, cost, and capability gates as an interactive request; an
unfilled or denied slot is a typed outcome. A slot is a domain-owned typed hole,
not a universal input envelope or a stored secret.

Private or otherwise non-redistributable content likewise uses an appropriately
protected runtime acquisition location rather than embedded bytes. A digest
provides integrity evidence, not confidentiality, disclosure authority, or
redistribution authority.

Build inclusion makes bundled bytes available. Selecting a scenario forms a
request for its declared inputs and capabilities; the host
authorizes that request under the same input, cost, and capability policy as any
other request. Selection does not bypass a network, source-content, exhaustive,
or other expensive-work gate. The bytes remain untrusted inspection data, are
parsed rather than loaded, retain bundled acquisition provenance, and cross the
same budgets and presentation boundaries as user-supplied content.

An inspection bundle contains no precomputed query results or producer,
correspondence, or authorization verdicts. A future build-time result-cache
feature would require its own semantic-key, producer-version, validation, and
publication contract; bundle inclusion does not imply one.

### Query plan

A host asks for typed inspections, not scanner names or output sections. Each
query declares:

| Property | Question answered |
| --- | --- |
| Scope | Does it run without a workspace, for one source or artifact, one assembly, one context group, or several groups? |
| Inputs | Which typed content and prior results does it consume? |
| Cost | Is the work bounded, network-bound, source-content-bound, or exhaustive? |
| Capabilities | What must the caller authorize? |
| Dependencies | Which producer results must exist first? |
| Lifetimes | Which acquired images, catalogs, or other bound resources must remain alive? |
| Correspondence | Which owner establishes relationships between the inputs? |
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
- validity of bound currencies and frozen answers;
- assembly, group, or producer provenance.

It must also respect resource and generation barriers. Publishing a successor
must not silently invalidate a live consumer. The executor quiesces predecessor
consumers before an owner that invalidates on advance publishes the successor.
An owner may instead permit concurrent publication only when its leases retain
complete per-generation state until every consumer releases them. A query
cannot outlive a resource it borrowed. A producer may declare a resource safe
for concurrent consumers; otherwise the executor serializes access. The
sequential executor satisfies these rules without requiring threads.

Producers receive the narrow context named by their scope, not a mutable
workspace object. This keeps the workspace from becoming a god object and makes
cross-group access explicit.

## Core currencies

The core is defined more by the values crossing its boundaries than by project
names.

### Currency contracts

A **currency** is a value one owner accepts as authoritative for one operation.
It is not a repository-wide interchange type. Every currency has a contract:

| Property | Question |
| --- | --- |
| Authority | Which owner and operation may trust this value? |
| Scope | Is it valid for one reader, image, body, catalog generation, group, or comparison? |
| Lifetime | Which live owner or frozen generation must remain available? |
| Portability | May it cross a query, process, serialization, or persistence boundary? |
| Erasure | Which facts or capabilities were deliberately left behind? |
| Rebinding | Which owner can validate or bind it in another context? |
| Correspondence | Does equality have meaning, or must an owner compare or project it? |

These properties are independent. A durable address may be portable but unable
to prove that two artifacts correspond. An opaque catalog key may answer exact
correspondence but only while one generation remains alive. A portable source
line may survive serialization while its IL offset remains meaningful only
beside the physical body; its annotation extents use the coordinate plane of
the containing rendered stream.

Bound and portable forms are therefore a matrix, not a ladder. Projection from
a bound value into a portable value is explicit and names what authority it
loses. Rebinding is another owner operation with a typed failure, not an
implicit cast back to the original value.

Concrete types remain domain-owned. The core does not define a universal
`IJoinable`, generic anchor, bound-value wrapper, or portable-value envelope.
The architecture is the contract above and the ownership of each transition.

### Identity and provenance

Resolution returns a descriptor such as `ResolvedAssemblyReference`: identity,
an opener for the selected content, and structured resolution provenance.
Inspection does not discard that information into a bare path and later
reconstruct it.

Identity, correspondence, provenance, and display remain separate. Joins use
the typed currencies; presentation chooses spelling afterward.

### Acquired generations and leases

An acquired assembly has several related but distinct scopes:

| Scope | Meaning |
| --- | --- |
| Acquisition registration | Repeated policy selections name the same canonical candidate chosen by one acquisition owner. |
| Image lifetime | Consumers read one opened byte generation through a format owner's session or lease. |
| Catalog generation | Binding and correspondence answers share one frozen candidate universe and policy snapshot. |

None implies another. Matching descriptor fields do not prove one registered
candidate. Sharing one `PEReader` does not establish definition
correspondence. Advancing a catalog generation does not require reopening every
image whose bytes remain valid.

The workspace coordinates these lifetimes without making format-specific
handles core currency. Metadata may own a `PEReader`; another producer may own
an immutable byte image or a parsed index. Queries receive narrow sessions,
views, or leases and do not reopen or dispose the underlying resource.

This is a correctness rule as well as a performance rule. Opening the same path
twice can observe two different files after a build, restore, or symlink change
and silently combine facts from different assemblies. Sharing the acquired
generation removes that assumption.

Reader-local handles and pointers remain inside the owning lifetime. Results
that outlive it materialize producer facts or carry a durable address that its
owner revalidates before dereference. A durable address is location evidence,
not artifact identity or correspondence proof.

### Authorized content

Content availability never grants authority to inspect it. A package payload,
source document, retained byte image, or cache entry is visible only when the
current request authorizes the producer and coordinate that supplied it.

Acquisition retains enough provenance and authorization evidence for the owner
to make that decision. A persistent workspace may retain bytes or parsed
resources, but a later query plan revalidates access under its own capabilities
and source policy before receiving a lease. It also reuses derived binding or
correspondence results only when their authorization scope is compatible. The
cache answers only after that decision; it does not introduce candidates or
widen authorization.

This is the acquisition analogue of other owner-issued safety currencies. The
acquisition owner authorizes content, a catalog authorizes correspondence, and
the presentation boundary produces `InertString`. None can be reconstructed by
inspecting the visible fields of an untyped value.

### Results and failures

Queries return typed results. Expected bad-input and acquisition failures are
typed outcomes with subject provenance, not empty collections shaped like
success. Unexpected producer defects remain fatal rather than being relabeled
as bad input.

Partial group results are valid only when their failures are carried beside
them and the result contract says partial inspection is meaningful.

A plan-expansion request is a typed orchestration outcome, not absence or an
empty result. The coordinator advances the owning domain's generation and
restarts affected work before presentation.

### `CoreCache`

`CoreCache` is shared infrastructure for category roots, path-safe hashed keys,
maintenance, and cache telemetry. It is a mechanism, not a semantic authority.

The cache owner for each result must still define:

- the complete semantic key;
- producer and source provenance;
- freshness and versioning;
- validation on read;
- publication and concurrency behavior;
- whether already-authorized network work may run after a miss.

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

## Joins

A join is an owned operation over typed operands in an explicit context:

```text
join = operands × context × correspondence authority
    -> relation | typed non-relation
```

The architecture does not require one relation type. It requires the operation
to preserve the distinctions that make its answer trustworthy.

### Join operands

A join operand conceptually combines four parts:

| Part | Role |
| --- | --- |
| Subject | The entity being discussed across producers or contexts. |
| Local binding | The exact candidate, member, body, or resource in the current context. |
| Native coordinates | Producer-owned locations such as a metadata row, IL offset, source extent, or stream position. |
| Payload and provenance | The evidence being related and the producer that supplied it. |

Those parts need not be duplicated on every leaf. Identity belongs at the
highest container that knows the subject; native coordinates stay on the
lowest producer that owns their semantics. A body-local fact may carry only an
IL offset while its enclosing result carries the member subject and assembly
binding. A portable source line may depend on its containing stream for the
coordinate plane. Composition supplies the full operand without flattening it
into one key.

Member inspection is the worked pattern. A selector is a portable question, a
member anchor is a durable API-identity projection, a resolved target binds
that identity to one API surface and possible physical body, and a metadata
handle is exact only for one reader. Body evidence retains its native identity
and coordinates; Research owns the bridge when API and body vocabularies must
join. Projected members such as extension methods retain both the API target
and the physical body owner instead of collapsing them.

Source projection demonstrates the same pattern at another scale. An in-process
correlation may retain live annotation objects and IR relationships. Its
portable projection materializes annotation data and rebased extents so another
consumer can retain, filter, or render the relation without those live objects.
The line's IL offset remains scoped to its physical body, while annotation
extents use the containing rendered stream's coordinate plane. The projection
does not claim to recover the original graph.

These examples are precedents, not core types. Their owning documents define
the exact currencies and conversions.

### Correspondence precedes composition

Equality is not correspondence. A path, display string, MVID, metadata token,
durable address, record equality, or matching payload fields can be useful
evidence without proving that two operands denote the same subject.

The domain owner establishes correspondence. Depending on the domain, its
closed result may distinguish:

- exact sameness and definite difference;
- ambiguity or duplicate-artifact indeterminacy;
- incomparable contexts or stale generations;
- exact and named soft-match tiers with match provenance;
- inability to decide because required evidence was unavailable.

A boolean result is insufficient when the domain admits those states. In
particular, indeterminate is neither false nor permission to fabricate a
match. A safe negative comes only from the authority that has enough evidence
to rule the relation out.

When repeated joins need hashing or indexing, the authority may project a
generation-scoped join token. Consumers do not derive one by normalizing
display strings or unpacking an opaque key. A portable address may be rebound
and revalidated in another context, but it does not become a correspondence
token by surviving the trip.

### Join scope

The required correspondence changes with scope:

| Scope | Rule |
| --- | --- |
| One live reader | Reader-local handles are exact only inside that reader. |
| One body | IL offsets are interpreted beside the physical member and body binding. |
| One rendered stream | Annotation extents are interpreted in that stream's coordinate plane. |
| One context group | Cross-assembly correspondence uses one frozen binding catalog generation. |
| Several groups | Each portable subject is bound independently; bound handles, keys, and tokens never cross the group boundary. |
| Several versions | Producer-owned exact or soft correspondence retains its tier, ambiguity, and match provenance. |

Cross-group comparison is explicit work, not an exception to group isolation.
It consumes portable projections or independently resolved subjects from each
group and produces a new relation. A value that is incomparable across catalogs
does not become comparable because both catalogs happen to be in one
workspace.

### Join execution

Joins must remain demand-driven and bounded. The query plan declares their
operand producers, correspondence owner, scope, capabilities, prerequisites,
and result. The executor requests the required frozen contexts from their
owners and retains their leases until the relation is complete.

An owner may provide indexes, blocking keys, or conservative prefilters to
avoid a Cartesian comparison. Such a filter may admit extra candidates, but it
must not produce a negative outside the evidence its domain contract
authorizes. Candidate generation and final correspondence remain separate
operations.

The result retains producer-native evidence, subject identity, local
coordinates, correspondence provenance, and scoped failures. It is a projection
of the relation, not a replacement for either operand. Research normally owns
cross-producer composition; a domain producer continues to own its own binding,
matching, and coordinate semantics. The workspace orchestrates both without
learning type names, member grammars, IL offsets, or source-span rules.

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
