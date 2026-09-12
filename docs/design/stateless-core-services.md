# Stateless core services

Status: **target composition contract** for
[#6749](https://github.com/richlander/dotnet-inspect/issues/6749).

This user-approved cross-cutting design owns one pattern: inspection services
take every behavior-bearing observation as explicit input rather than retaining
caller history between unrelated operations. The host supplies retained
semantic state through a Workspace and an optional persistent-cache port.

The Workspace, Artifact, Library, Package Source, House, query, cache, and host
designs remain normative for their own behavior and adoption algorithms. This
document defines only the identities and invariants their owner-issued values
must preserve when they compose through this pattern.

## Claim

One host selects zero or one active Workspace realization. A realization
materializes one resource-free Workspace definition and issues the exact
realization identity and operation authority used by services. A conforming
service retains no behavior-bearing observation after the operation that
received it.

The composition preserves four owner-issued associations:

1. a realization identifies the exact definition it materialized;
2. operation authority identifies the exact realization that issued it;
3. detached content identifies the evidence and outcome produced by that
   operation without retaining live authority; and
4. persistent-cache evidence identifies the cache owner's exact key, result
   class, and validity contract, never Workspace authority.

Replacing a Workspace definition creates a fresh realization association.
Equal coordinates do not transfer realization identity or live authority across that
boundary. The Workspace and retained-host owners define construction, cutover,
admission closure, and drainage while preserving this invariant.

The optional persistent-cache port is the only general observed-content state
this pattern admits beyond a Workspace lifetime. Each cache-category owner
remains responsible for its keys, validation, authorization, freshness,
negative-result lifetime, and availability semantics. Cache policy cannot
supply Workspace realization identity or live authority.

This is a target conformance contract, not an assertion that the current
repository already contains no hidden process state. Current implementation
conformance is **unverified** until focused owners adopt the pattern and name
their Release gates.

## Demo

Consider a Browser Workspace containing:

```text
System.Text.Json@10.0.0
Humanizer.Core@2.14.1
```

The user chooses a definition containing only:

```text
System.Text.Json@10.0.0
```

The host does not remove `Humanizer.Core` from the live realization or transfer
the existing `System.Text.Json` package, Artifact, Library, assembly group,
index, or lease into a successor. It constructs a candidate realization from
the one-package definition.

The candidate may receive the exact authorized package payload through the
persistent-cache port. It still receives fresh Workspace identity,
registrations, generations, owners, and operation authority. The focused
Workspace and retained-host contracts must ensure that an operation is admitted
by exactly one realization and that already-admitted predecessor work can settle
without authorizing new work.

```text
old active realization
  System.Text.Json generation A
  Humanizer.Core generation B
  query Q admitted

candidate realization
  definition: System.Text.Json@10.0.0
  PersistentCache may supply validated bytes
  fresh generation C

cut over
  candidate becomes the selected active realization
  no new operations are admitted to the predecessor
  query Q finishes against A/B
  old realization drains and settles A/B
```

If candidate construction fails, the active-realization association does not
change. The focused owners make the failure and candidate settlement visible;
this pattern does not prescribe their concrete transaction algorithm.

The neighboring extension case is conditional. Adding
`Microsoft.Extensions.Logging@10.0.0` may preserve the active realization only
when the Workspace owner issues evidence that the addition does not reinterpret
any prior identity, binding, policy, correspondence, or derived result. Without
that evidence, the addition uses the same fresh-realization boundary as removal
or replacement.

## Why this is service orientation

A service is stateless when one invocation's observed content, identity
history, mutable options, failures, or derived decisions do not become hidden
inputs to another invocation.

A reusable service instance may retain immutable product configuration,
transport infrastructure, or behavior-transparent memoization. Those values
do not become a third semantic state port:

- immutable product tables describe the program rather than observations;
- transport pools own connections, not inspection answers;
- request-local indexes disappear with the request;
- weak exact-object memoizers disappear with the object and cannot substitute
  another generation; and
- in-flight single-flight coordination joins equivalent work but removes its
  entry after settlement.

State that changes an answer, authorization, completeness, failure
classification, identity, or currentness is not an implementation detail. It
belongs to the Workspace definition, active realization, an explicit request,
or the persistent-cache port. Another retained core state owner requires a
separate architecture decision.

No universal service interface is required. PackageHouse, PlatformHouse,
SourceHouse, DocumentationHouse, Workspace queries, and other owners keep
their focused request and result types.

## Pattern ports and join currencies

### Workspace

The Workspace port has two distinct forms.

The **Workspace definition** is resource-free. It contains the coordinates,
registrations, and policy needed to describe the intended inspection
population. A definition may be copied, serialized, retained by a host, or
selected again without keeping any process resource alive.

The **active Workspace realization** is the physical owner for one
materialization of a definition. Its focused owner may retain:

- Artifact sessions and owner-issued content children;
- realized Library owners;
- binding-consistent assembly groups and immutable image snapshots;
- catalog generations and correctness-bearing indexes;
- operation admissions and leases;
- retained-byte and work budgets; and
- retirement, drainage, and cleanup state.

These values are not caches merely because they avoid repeated work. They
preserve one exact admitted observation and its ownership or correspondence.
They end with the realization.

The terms above are architectural roles, not final CLR type names. The
Workspace owner chooses the concrete names and APIs in its focused adoption.
That adoption must expose enough typed evidence to associate each realization
with its definition and each operation authority with its issuing realization.

### Persistent cache

The **persistent-cache port** is an optional host-supplied service outside
Workspace ownership. `DotnetInspector.Cache.PersistentCache` is the current
filesystem implementation for hosts that can use it; Browser/Wasm may omit the
port or adopt a separately owned persistent implementation. Absence of the port
uses the ordinary cold path.

The port may retain validated immutable content, a validated derivation, or an
owner-defined bounded observation such as a negative feed result after a
Workspace settles.

A category qualifies for transparent reuse only when:

1. its key contains the complete semantic identity, producer identity, and
   contract version required by the owning result;
2. a hit is validated before use;
3. current request authorization and capability policy are re-evaluated;
4. a missing, invalid, or corrupt entry follows the ordinary cold path;
5. the cache does not supply Workspace identity, realization generation,
   correspondence, or live authority; and
6. the cold producer and cache publisher consume the same retained immutable
   evidence named by the key.

These criteria classify transparent immutable reuse only. They do not prohibit
an owning cache or source contract from retaining an observable result. For
example, that owner may cache for a time-to-live that a package or package
version was unavailable on a feed. Failure caching, retry timing, offline
availability, and user disclosure are outside this design.

Such a cache result cannot update or refresh an already realized Workspace
component, or supply realization identity or authority. The cache and source
owners define what happens when the entry expires or later acquisition
succeeds.

## Workspace composition requirements

### Immutable components and extension

A Workspace component is a coordinate-bearing member of the definition. Once
realized, its observed content is fixed for that realization's lifetime. It has
no update or refresh operation.

If a host learns that the content available at an existing coordinate changed,
it creates a new Workspace realization, even when the definition still contains
the same coordinate. This principally applies to mutable feeds and local
directories used as feeds; nuget.org package content is immutable under its
source contract. How the host detects the change, whether replacement is
automatic, and whether the user is notified are outside this design.

The Workspace owner may preserve a realization across the addition of a new
component only when it issues non-interference evidence for that exact change.
The evidence must establish that every prior coordinate, identity, binding,
policy decision, correspondence, derived result, and admitted operation keeps
its meaning.

The Workspace owner defines the evidence shape, publication, failure,
occurrence identity, ordering, deduplication, capacity, and expansion policy.
An addition lacking that evidence is replacement; the fact that all prior
coordinates remain textually present is insufficient. Re-observing changed
content at an existing coordinate is replacement, not addition.

### Replacement

Any operation that removes a coordinate, replaces a coordinate, clears scope,
or lacks the required non-interference evidence selects a complete definition
and creates a fresh realization. The selected definition may compare equal to
the prior definition when content at a mutable coordinate changed.

Replacement is a semantic reboot:

- only the replacement definition participates in construction;
- prior realization identities, failures, generations, registrations, leases,
  and indexes are absent;
- equal coordinates receive fresh realization authority;
- omitted content cannot remain reachable through a retained index; and
- the old realization's state cannot affect the candidate's success or result.

This does not require bypassing `PersistentCache`, the operating-system page
cache, immutable product tables, or host transport pools. It prohibits using a
live old-realization object or hidden process observation as semantic input.

### Active-realization cutover

The Workspace and retained-host owners define the cutover protocol. Their
focused contracts must jointly establish:

- candidate failure or supersession does not change the selected active
  realization;
- each accepted operation authority comes from exactly one realization;
- no operation admitted after cutover receives predecessor authority;
- predecessor operations already admitted retain only their exact predecessor
  authority;
- candidate and predecessor resources never transfer live authority; and
- candidate and predecessor settlement outcomes remain visible.

The host may retain definitions or restoration snapshots for history and
selection. Selecting one constructs another realization. A retained definition is
not a dormant live Workspace and cannot authorize access to resources from its
former materialization.

Only one realization is selected for new operation admission before and after
cutover. Candidate construction and predecessor drainage may temporarily keep
resources from more than one realization alive, but that transition is not a
user-selectable collection of live Workspaces.

The Workspace owner defines admission, cutover, and settlement types. Artifact,
Library, and other resource owners define child settlement. The retained host
composes those owner-issued outcomes without flattening them into
success-shaped disposal. The focused cutover design must provide its own
concurrency model and single-threaded Browser/Wasm progress evidence before
claiming this requirement is implemented.

## Stateless operation boundary

An inspection operation has this composition:

```text
host intent
  -> resource-free semantic request
  -> active-realization admission
  -> owner-issued operation authority
  -> stateless service execution
  -> owner-issued detached content and outcome
  -> settle operation authority
  -> optional terminal InspectionEnvelope<TContent>
  -> host projection
```

The exact operation authority remains owner-specific. A synchronous operation
may use a scoped borrow. Work crossing `await` owns an operation resource or
independently owned data; no borrow crosses suspension or Browser interop.

Internal services, prerequisite queries, and Workspace operations return their
owner-issued results; this design does not make the envelope universal. A
completed host-neutral terminal operation already adopted under
[Inspection Envelope](inspection-envelope.md) may wrap detached content and its
Share outcome. Such an envelope contains no Workspace, session, group, lease,
stream, callback, opener, metadata reader, or cache handle. Diagnostics cannot
substitute for a typed failure or incomplete outcome.

Services may compose owner-issued operations, but they do not retain their
inputs or results for a later unrelated request. A service that needs
cross-operation observed state must place it behind one of the two explicit
ports. A proposal for a third retained core state owner must revise this design
rather than adding a private cache.

## Relationship to resource ownership

[Resource ownership and borrowing](resource-ownership-and-borrowing.md) owns
the lifecycle vocabulary. This design applies only its composition
consequences:

- live resources have one explicit owner;
- ownership transfer invalidates the prior owner's use;
- scoped borrows cannot escape;
- work crossing `await` carries ownership rather than a borrow;
- invoking asynchronous release creates a settlement-observation obligation;
  and
- references, receipts, and envelopes retain no live authority.

The non-normative owner index tracked by
[#6654](https://github.com/richlander/dotnet-inspect/issues/6654) records how
focused owners realize those roles. It does not define this composition or any
owner's behavior.

Future compiler ownership can strengthen synchronous moves, borrows, and
destruction. Workspace replacement, owner-issued correspondence, operation
outcomes, abandonment, drainage, and observed asynchronous settlement remain
explicit protocol.

## Current-state migration map

This table records migration evidence, not new normative contracts for the
listed owners.

| Current state | Target classification | Focused owner action |
| --- | --- | --- |
| `InspectionWorkspace` combines logical scope and physical ownership | Resource-free definition plus one active realization owner | Workspace Definitions [#6750](https://github.com/richlander/dotnet-inspect/issues/6750) and realization [#6752](https://github.com/richlander/dotnet-inspect/issues/6752) |
| Scope supports Replace, Clear, Add, and Remove | Evidence-bearing non-interfering extension or fresh-realization replacement | Workspace Scope [#6751](https://github.com/richlander/dotnet-inspect/issues/6751) |
| Browser retains several live Workspace scopes | Retained definitions with one materialized realization | Inspect Web retained host [#6757](https://github.com/richlander/dotnet-inspect/issues/6757) |
| Artifact sessions, groups, snapshots, and query admissions | Correctness-bearing active-realization ownership | Artifact and Workspace owners |
| `AnalysisIndexCache` survives Workspaces | Realization- or operation-owned derived evidence | Analysis [#6754](https://github.com/richlander/dotnet-inspect/issues/6754) |
| `ResearchAssemblyContextCache` strongly retains exact indexes | Exact-index memoization without lifetime extension | Research [#6755](https://github.com/richlander/dotnet-inspect/issues/6755) |
| `PlatformTypeCatalog` retains path-keyed filesystem inventory | Exact-generation realization state or recomputation | Platform [#6756](https://github.com/richlander/dotnet-inspect/issues/6756) |
| Package acquisition single-flight removes settled entries | Behavior-transparent in-flight coordination | Package owner; retain if its equivalence contract remains satisfied |
| Weak memoization keyed by an exact immutable reader | Behavior-transparent implementation detail | Focused format owner |
| Mutable request options or accumulated telemetry in statics | Explicit request or host state | CLI or host owner |
| Exact validated immutable package entries | Persistent-cache candidate | Cache-category owner |
| TTL package or version unavailability entries | Owner-defined cache/source behavior outside this pattern | No #6749 migration requirement |
| Other mutable listings, metadata, or currentness observations | Owner classification before use as Workspace input | Focused source or cache owner |

The first implementation task for each row is to confirm its focused owner and
current consumer before changing code. A cache census does not authorize one
repository-wide cache rewrite.

## Failure and pathological cases

Focused adopters must demonstrate these composition outcomes:

- candidate construction fails after acquiring some children: the candidate
  settles them all, the prior realization remains active, and the failure is
  visible;
- cutover races an old operation: every accepted operation is associated with
  exactly one realization generation;
- equal package coordinates occur in both definitions: the successor receives
  fresh authority and may reuse only validated external cache material;
- old-realization cleanup fails after successor publication: the successor
  remains active, while the old settlement failure remains visible to the
  host;
- a cache entry is missing or invalid: the ordinary cold path runs or returns
  its typed failure without inventing empty success;
- a negative cache entry remains effective until its owner-defined expiry: the
  source/cache owner controls retry and disclosure, and the entry does not
  refresh an already realized component; and
- a terminal result attempts to retain live authority: the focused result owner
  rejects it rather than exporting the authority through the host boundary.

The Workspace and retained-host owners must provide model and implementation
evidence for cutover, admission closure, drainage, settlement, and
single-threaded Browser/Wasm progress. A cache owner claiming transparent reuse
must provide category-specific cold/warm correspondence evidence. Until those
focused gates land, the target composition is **unverified**.

## Adoption plan

This pattern lands first. Adoption then proceeds through filed, focused owner
efforts:

| Owner effort | Required outcome |
| --- | --- |
| Workspace Definitions [#6750](https://github.com/richlander/dotnet-inspect/issues/6750) | Distinguish a resource-free definition from each materialized realization identity. |
| Workspace Scope [#6751](https://github.com/richlander/dotnet-inspect/issues/6751) | Make realized components immutable, require owner-issued non-interference evidence for extension, and route every other change through replacement. |
| Workspace realization [#6752](https://github.com/richlander/dotnet-inspect/issues/6752) | Define and model candidate construction, cutover, admission closure, drainage, and visible settlement. |
| Analysis [#6754](https://github.com/richlander/dotnet-inspect/issues/6754) | End `AnalysisIndexCache` history at an operation or exact Workspace realization boundary. |
| Research [#6755](https://github.com/richlander/dotnet-inspect/issues/6755) | Stop exact-index memoization from extending index ownership across unrelated operations. |
| Platform [#6756](https://github.com/richlander/dotnet-inspect/issues/6756) | Associate `PlatformTypeCatalog` observations with exact generation evidence or recompute them. |
| Type dependencies [#6758](https://github.com/richlander/dotnet-inspect/issues/6758) | Pass explicit realization authority through one terminal operation already shared by the CLI and Browser/Wasm. |
| Inspect Web retained host [#6757](https://github.com/richlander/dotnet-inspect/issues/6757) | Retain definitions or restoration data, materialize one active Workspace realization, and retire multiple-open-realization infrastructure. |

Each row is one focused issue or a sequence of issues for that same owner. The
tracker in #6749 records their links and retirement dependencies; this document
does not specify their internal migrations.

Artifact adoption in #6647, Library implementation in #6621, and the
non-normative owner map in #6654 proceed as dependencies or parallel focused
work. They are not folded into this design's normative scope.

Every implementation slice names one focused owner and includes or directly
links its production consumer. A Workspace or host compatibility path retires
in the adopting owner's slice after its consumers move; there is no final
cross-owner cleanup sweep. The migration may reorder independent slices, but
it does not claim completion while either production host still depends on
hidden cross-operation state.

## Non-goals

- A universal service interface, dependency-injection framework, owner
  wrapper, lease type, or result algebra.
- Owner-specific Artifact, Library, Package Source, House, query, cache, or
  host algorithms.
- Simultaneous queries spanning several Workspaces.
- Several materialized Workspaces retained for instant switching.
- Transfer of live resources between replacement Workspaces.
- Async borrows or an assumption of future asynchronous compiler `Drop`.
- Elimination of Workspace-owned correctness indexes or exact retained
  content.
- Treating every current `PersistentCache` category as already compliant.
- A repository-wide implementation sweep in one PR.
