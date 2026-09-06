# Artifact acquisition and workspace composition

How storage, packages, artifacts, assemblies, workspaces, sessions, and
inspection producers remain separate concepts while still composing into one
inspection experience.

This is a design proposal with an incremental implementation. Target boundaries
remain **unverified** until their named implementation gates exist. The
source-neutral contract floor, artifact-session publication, explicit local-file
snapshot adapter, shared local-path admission, and package-free local host now
have the gates named under [Required gates](#required-gates). Current types and
remaining target behavior are identified explicitly under
[Current mismatches](#current-mismatches).

The resource-free Root projection tracked by
[#5713](https://github.com/richlander/dotnet-inspect/issues/5713) is a focused
addition to this existing owner. Its initial implementation supplies the
resource-free Package correspondence currency; physical-generation identity,
current status, and stale-access validation remain with the publication
handoff tracked by
[#5727](https://github.com/richlander/dotnet-inspect/issues/5727), the middle
slice. The logical Workspace Scope contract is the upper slice in
[#5701](https://github.com/richlander/dotnet-inspect/pull/5701).

See [inspection-space.md](../inspection-space.md) for workspace and query
planning, [inspection-layers.md](inspection-layers.md) for consumer layers, and
[assembly-inspection-query.md](assembly-inspection-query.md) for the
`ResolvedAssemblyReference` and `AssemblyInspectionSession` seam, and
[assembly-image-lifetime.md](assembly-image-lifetime.md) for the focused
single-image and MVID correctness contract.
[workspace-definitions.md](workspace-definitions.md) owns static context
coordinates, while
[workspace-scope-and-expansion.md](workspace-scope-and-expansion.md) owns
committed logical Root membership, occurrence order, selective dependency
expansion, scope revisions, and scope-operation results, and
[inspection-graph-document.md](inspection-graph-document.md) owns graph
subjects and relationships.

## Decision

A workspace may contain artifacts acquired from any number of heterogeneous
sources. Source adapters contribute source-neutral artifact content and typed
provenance. The workspace owns the resulting lifetimes and composes artifacts
into binding-consistent assembly context groups.

Packages, local files, restored projects, platform packs, embedded bundle
content, and remote CI build artifacts are peer **artifact sources**. None is
the universal model.

The target layering is:

```text
host composition
  |
  +-- local adapter -----------+
  +-- package adapter ---------+
  +-- project adapter ---------+--> artifact acquisitions
  +-- platform adapter --------+          |
  +-- CI artifact adapter -----+          v
  |                              ArtifactSetSession(s)
  |                                 owned by workspace
  v                                          |
InspectionWorkspace                         |
  +-- AssemblyContextGroup <----------------+
        |
        v
      query
        |
        v
AssemblyInspectionSession
        |
        v
metadata / analysis / source inspection producer
```

The query boundary is intentional. A consumer does not take an artifact handle
and invoke producers ad hoc. A query selects the participant and owns the
`AssemblyInspectionSession` lifetime through which producers run.

Two different roles must not both be called "source producer":

- an **artifact source adapter** runs before the workspace session and acquires
  content;
- an **inspection producer** runs inside a query after an assembly session is
  open and produces metadata, Analysis, decompiler, or authored-source
  evidence.

## Why this boundary

The immediate pressure came from package acquisition. Several consumers need a
package root or package entry before they can select an assembly. Routing that
need through `AssemblySetResolver` appears to centralize acquisition, but it
makes the assembly-set abstraction own package identity, package extraction,
NuGet source options, and package-specific TFM policy.

[PR #4391](https://github.com/richlander/dotnet-inspect/pull/4391) validated
that such routing could preserve current behavior. Its implementation and two
exact-head review rounds were clean. The PR was closed because the shape
required an "acquisition-only assembly set": a successful assembly set
containing no assemblies whose purpose was to return a package root. That is
evidence that the dependency direction was wrong, not evidence that an empty
assembly set is a useful concept.

The stronger requirement is a compile-time one:

> It must be possible to build a dotnet-inspect variant that inspects only local
> libraries without referencing package resolution, NuGet transport, package
> storage, or package-specific workspace implementations.

Turning package support off at runtime does not satisfy this requirement. The
package implementation must be absent from the local-only product's project and
package dependency closure.

## Concepts and owners

| Concept | Meaning | Owns | Must not own |
| --- | --- | --- | --- |
| Storage | Retention and retrieval of opaque bytes | cache keys, publication, eviction, content leases | package selection, PE identity, workspace binding |
| Artifact | One immutable inspectable content item | logical identity, media/kind hint, content digest when requested, owner-mediated content access | package or assembly policy |
| Artifact acquisition | One adapter's typed attempt to contribute artifacts | outcomes, diagnostics, provenance, content leases | workspace binding |
| Artifact source adapter | Resolves one source-specific coordinate | source protocol, authorization, listing, archive rules | inspection queries |
| `ArtifactSetSession` | One sealed artifact generation admitted to a workspace | child acquisition leases and artifact handles | source-specific resolution or assembly binding |
| Root scope projection | Resource-free facts about one admitted or replacing Root | logical correspondence, current-generation freshness, typed realization status | logical membership, Root order, scope policy, or physical access authority |
| Root preparation receipt | One complete provisional physical Root batch | prepared resources, candidate correspondence, budget reservation, one-shot publication or release | logical membership, order, expansion policy, Navigation, or portable state |
| Inspection Workspace runtime | Physical inspection composition | runtime identity and lifetime, artifact sessions, contexts, roles, query plans, aggregate admission budgets | logical Root membership, dependency-expansion eligibility, or scope-operation policy |
| Workspace scope | One committed logical inspection scope | [Root membership, occurrence order, selective expansion, revisions, and scope-operation results](workspace-scope-and-expansion.md) | acquisition, assembly binding, query execution, or runtime lifetime |
| Assembly context group | One binding-consistent universe | participants, binding policy, retained assembly snapshots | package acquisition |
| Resolved assembly reference | Neutral handle for one selected managed assembly | assembly identity and guarded repeatable content access | package coordinate parsing or storage implementation |
| Assembly inspection session | One opened PE inspection lifetime | [reader/image lifetime and session-scoped operations](assembly-image-lifetime.md) | artifact acquisition |
| Inspection producer | Computes one family of facts | metadata, IL, source, or comparison evidence | source discovery |

An artifact is broader than an assembly. An artifact set may contain assemblies,
portable PDBs, XML documentation, manifests, source archives, or other content.
Only an assembly projection attempts to decode an artifact as managed PE
content and mint a `ResolvedAssemblyReference`.

Artifact identity and assembly identity are separate:

- **artifact identity** answers which immutable content item a source
  contributed;
- **assembly identity** is decoded from managed metadata;
- **workspace participant identity** binds that assembly identity to one
  artifact and one policy snapshot;
- **presentation identity** is projected later.

No layer infers one identity from another's display text.

### Provenance and correspondence

Source-specific provenance remains typed without becoming a source-specific
dependency of the artifact or assembly projects.

The artifact contract supplies only an owner-issued artifact identity and a
source-neutral marker implemented by typed provenance records. Each adapter
defines its own record: package coordinates and producer key in the package
companion, CI run and artifact ids in a CI companion, and local
path/fingerprint evidence in the local adapter. The workspace retains the
record and correlates it with the artifact identity.

Assembly projection carries the artifact identity, not a Metadata-owned union
of every possible source kind. Adapter-aware queries or hosts can ask the
workspace for the source-specific provenance record through the adapter's typed
contract. Assembly-only consumers can preserve and compare the correlation
identity without referencing or interpreting package or CI types.

This is not an untyped property bag. Adding a new source requires a typed
provenance record and its owning projection/serialization code, but does not
require changing Metadata.

Correspondence is likewise owner-issued. After validating its coordinate
against acquired content, an adapter mints an acquisition registration scoped
to one artifact generation. Assembly projection records which artifact
registration produced each assembly registration. A package-aware query can
then consume the package adapter's typed realization and correspondence proof;
it does not ask Metadata to reinterpret package fields.

Artifact identity and acquisition registration answer exact correspondence
only inside their owning artifact generation. A digest and immutable source
coordinate can provide durable content evidence, but neither recreates the
owner-issued registration after that generation ends.

Caller designation is a policy input, not source provenance or assembly
identity. The current `AssemblyResolutionProvenance.DesignatedAsset` carries the
fact that a caller explicitly enumerated a corpus/build-layout assembly so the
decompiler can grant core-library identity trust. In the target architecture,
the local/project adapter records how the artifact was acquired, while the
authorized admission records an explicit designation role on the workspace
registration. A producer may consume that role under the current plan's trust
policy; it cannot infer designation from a path or metadata name.

## `ArtifactSetSession`

`ArtifactSetSession` is one acquisition lifetime and consistency boundary owned
by a workspace. A workspace may own several sessions, normally one for each
context realization or other atomic admission. Each session may aggregate
several source adapters.

The session:

1. accepts typed artifact-acquisition outcomes from multiple adapters;
2. retains every admitted source-specific content lease;
3. exposes a stable, source-neutral artifact catalog without an unguarded
   content opener;
4. preserves provenance on each artifact;
5. rejects identity collisions or represents them explicitly;
6. makes acquisition failures visible to workspace construction;
7. releases all child leases after every dependent group is quiescent.

The session has a construction phase and a sealed phase. Adapters contribute
only during construction. Queries observe only the sealed catalog; adding,
removing, or replacing an acquisition creates a new session rather than
mutating one that queries use. Loading another context into a retained workspace
adds another sealed session and group; it does not discard or mutate sessions
and groups already in use.

Sealing does not claim that GitHub, Azure DevOps, a local directory, and a
package feed participated in one transaction. Each acquisition records its own
immutable coordinate, producer, content identity, and observation. The session
guarantees that those recorded acquisitions do not silently drift after
admission.

Before the first adapter call, the workspace atomically reserves the authorized
plan's declared maximum artifact count, peak acquisition/expansion bytes, and
retained logical bytes from one aggregate admission budget. The reservation
covers the whole multi-source plan, every other in-flight admission, and every
sealed session the workspace still retains. An adapter that cannot declare
finite bounds is not admissible. Failure to reserve produces a typed budget
failure before acquisition begins; it does not silently omit a member or
shorten the context.

Adapters still enforce source-specific download, enumeration, archive, and
expansion limits inside that reservation. Before sealing, every admitted
artifact's exact logical bytes are materialized into an immutable retained
snapshot or content-addressed store. Archive entries are expanded into their
selected logical artifacts at this boundary, not on a later query open.

The owner independently validates artifact identity, count, any source-declared
digest, and every byte dimension before publication. An identity mismatch or
reservation overrun is a typed admission failure, never an over-commit. The
admission lease then opens those retained bytes, decodes managed metadata, and
creates every assembly participant required by the context. A missing,
colliding, non-projectable, or binding-incompatible required participant fails
admission; the workspace publishes neither a shortened group nor a partial
session.

The owner is the sole authority that may produce a content digest for an
artifact. When a consumer requires one, the owner computes and may memoize it
from the retained immutable bytes and charges the requesting operation that
causes the one cold linear pass; later authorized reuse of the memoized digest
does not recharge. It never rehashes the mutable source. A persistent
derived-result cache that keys on that digest must run its cold gate and
producer over the same snapshot and publish under the snapshot's digest; it may
not hash a mutable source path, reopen it for production, and hash it again.
Equal bracketing hashes do not exclude a W-to-S-to-W replacement. This is gated
for the library effective catalog by `MDP017` in
[member inspection planning and Metadata
projection](member-inspection-planning-and-metadata-projection.md).

Publication atomically commits the sealed catalog, all projected participants,
the artifact-count charge, and actual retained-byte charges. It releases the
peak-acquisition reservation and unused retained remainder only after that
commit. Query leases may later open the retained logical bytes, but those opens
perform no source acquisition, archive traversal or expansion, participant
minting, or catalog mutation.

A rejected or cancelled admission releases its reservation only after every
partial download, expansion, snapshot, and returned lease is cleaned up.
Cancellation remains cancellation rather than becoming a failure-shaped
result. Workspace disposal beginning is not itself a release boundary;
published count and retained-byte charges remain until dependent groups
quiesce. Storage caches and assembly groups may apply additional
physical-retention and image budgets; they do not replace the workspace
admission budget.

Reservation is a logical workspace state transition, not a requirement for
threads or blocking locks. Concurrent hosts serialize the transition;
single-threaded Browser/Wasm hosts preserve it across awaited reentrancy, so a
second admission cannot spend capacity already reserved by the first.

An implementation ensures stable bytes by retaining a snapshot or a lease on
content-addressed storage before sealing. A later open may reopen only that
retained content, not the source adapter's mutable path or expiring download
URL. If retained content can no longer be opened, the operation fails visibly
rather than reacquiring or reading replacement bytes.

The workspace registers an admission operation before its first asynchronous
adapter call and owns that operation through atomic group publication. The
operation is single-flight for one normalized context generation and admission
policy snapshot. The first authorized demand enters the in-flight state before
reserving budget or calling an adapter. A compatible concurrent demand
reauthorizes that exact admission generation, joins the operation, observes
its typed outcome, and consumes no second reservation. It receives no catalog
or participant detail until its own query lease later authorizes selection.

Caller cancellation is first recorded by the owner, then resolves that demand
as cancellation before any later admission action can use it. A demand still
pending behind an incompatible generation is removed and cannot later replan,
reserve, or acquire. An attached demand detaches even if workspace disposal has
already moved its admission from in-flight to draining. Adapter completion
cannot resolve a demand whose cancellation request is recorded.

When no authorized waiter remains, the owner requests adapter cancellation and
enters a draining state. A new demand does not join a draining operation; after
cleanup it may start a fresh admission if the workspace remains open. An
incompatible policy generation likewise cannot join or start duplicate work for
the same context while the prior admission is active; absent cancellation, it
waits for the terminal transition and replans.

Workspace disposal first closes admission, rejects new demands, requests
cancellation of in-flight operations, and prevents a late result from
publishing a session or group. A late acquisition outcome transfers directly
to cleanup: every returned lease is disposed even when the adapter did not
observe cancellation. These transitions also handle single-threaded
Browser/Wasm awaited reentrancy; they do not depend on a parallel thread
reaching a lock.

Disposal then disposes published groups. A group may already have an active
callback that has not performed its first lazy content open. Artifact leases
therefore outlive `Dispose()` and are released only after every exact dependent
group reports quiescence. The asynchronous `InspectionWorkspace` records this
association from the published `ArtifactSetSession` and its query lease to the
workspace-owned group objects whose participants carry registrations minted by
that session. Ownership transfer requires the complete set of current dependent
groups; later admission remains available for unrelated work but rejects a new
group projected from that transferred session. It observes only those groups'
exact admission-held physical-release settlements and typed close results; an
unrelated, foreign, or incomplete group set cannot authorize release. Cleanup
failures compose with, and never replace, group cleanup results in the workspace
close report. A coordinated close-result fault cannot authorize artifact
cleanup before physical group-release settlement, while a fault after
settlement does not skip cleanup. A synchronous coordinated release-request
fault before terminal release remains the close failure and keeps the artifact
session live until the adjacent owner later establishes terminal settlement;
artifact cleanup never becomes a second physical-release authority.

### Interaction model

[`docs/models/artifact-session-admission/ArtifactSessionAdmission.tla`](../models/artifact-session-admission/ArtifactSessionAdmission.tla)
model-checks the admission lifecycle described above: single-flight admission
across concurrent demands, an incompatible-generation demand's inability to
join or start duplicate work while a prior admission is active, cancellation
before attachment, attached cancellation before or after disposal enters
draining, voluntary cancellation draining, disposal-forced draining, the rule
that a late adapter result must never publish a session or group, and that a
published group's artifact leases release only as part of the disposal cleanup
path once the group is quiescent. Its
[model guide](../models/artifact-session-admission/README.md) records the
checked properties, focused negative controls, reachability probes, commands,
and results.

The shipped artifact-session lifetime handoff is checked separately by
[`docs/models/artifact-session-group-release/ArtifactSessionGroupRelease.tla`](../models/artifact-session-group-release/ArtifactSessionGroupRelease.tla).
Its [model guide](../models/artifact-session-group-release/README.md) records
the bounded two-dependent-group topology, three named group-release owner
instances, exact transfer-set and receipt joins, the coordinated-close fault
path that retains artifact resources until an adjacent owner requests and
settles the missing physical release, reachability for the retained-pending
state, and focused controls for incomplete, duplicate, foreign, partial,
unauthorized-release, and unreported outcomes. It preserves the product's join
currency as the exact artifact-session registration, complete dependent-group
set, release-request origin, and each group's owner-issued terminal
receipt/result. Artifact cleanup observes those receipts but never becomes a
second physical-release authority. The model establishes those bounded
interaction properties, not implementation conformance.

The admission model abstracts away budget arithmetic, adapter identity,
content digests, and query-lease authorization, and it bounds the state space
to one outstanding published group's lease lifecycle at a time (a fresh
admission cannot publish while the previous group awaits lease release); this
is a scope-bounding simplification of the model, not a claim about real
concurrent groups. A demand's requested generation is also fixed once it
arrives; the model does not represent a caller re-deriving a different
generation when it replans after an incompatible admission terminates.

The admission model checks the design intent stated in the prose above. The
asynchronous `InspectionWorkspace` now owns the exact
published-session-to-dependent-group association and disposes the session only
after all recorded group release receipts complete; the focused group-release
model checks that shipped interaction. `ArtifactSetSession` still serves one
caller per generation with no workspace-wide reservation, multi-demand join,
or incompatible-generation wait. Closing those admission gaps remains future
implementation work, not a defect the admission model found.

TLC 2026.08.21.155922 (rev `9787e65`, from the pinned `tla2tools.jar` v1.8.0 —
see [`docs/runbooks/tla-plus-setup.md`](../runbooks/tla-plus-setup.md))
checked the target model with 3 demands and 2 admission generations: 65,395
states generated, 24,305 distinct states, maximum depth 16, no invariant
violations, and no counterexamples for the checked liveness properties. The
invariants include the headline `DisposalPreventsPublication` (`disposed =>
admission # "InFlight"`, since only `"InFlight"` can transition to a published
outcome), cancellation authorization and detachment, exact-race request and
completion witnesses, and independent guard-witness invariants that re-derive,
at the point of action, the exact condition each of disposal publication
safety, lease-release ordering, outcome authorization, pending cancellation,
and attached cancellation depends on.

Disabling pending cancellation explored 49,489 generated and 21,311 distinct
states before violating
`IncompatiblePendingCancellationEventuallyCompletes`; disabling draining
cancellation explored 51,071 generated and 20,378 distinct states before
violating `PostDisposalDrainingCancellationEventuallyCompletes`. Removing the
request guard from pending or attached cancellation violated its independent
guard witness after 15 or 90 generated states, respectively.

Dedicated reachability configurations now require the exact races. The pending
trace starts one generation, leaves another demand pending on the incompatible
generation, records its request, and cancels it. The draining trace starts
admission, begins disposal, records the attached waiter's request only after
admission is draining, and cancels it. They violate only their intentional
`PendingCancellationNotReached` and `DrainingCancellationNotReached`
invariants. These results establish the bounded model properties, not
implementation conformance.

The companion model
[`docs/design/models/artifact-generation-access/ArtifactGenerationAccess.tla`](models/artifact-generation-access/ArtifactGenerationAccess.tla)
covers the layer the admission model treats as an abstract given: what "the
dependent group reports quiescent" must mean for content access. It models
admission-phase materialization reads through acquisition leases, query-phase
opens of retained content, and the `EndGeneration`/lease-disposal sequence,
in both the target design and the current mechanics (flag rechecks outside
the gate; immediate release). Generation end and backing-resource release
are distinct events: the access contract deliberately keeps an
already-returned stream valid after `EndGeneration` and rejects only later
opens, so the target design registers each admitted open atomically with
generation end, runs its potentially blocking opener after registration, ends
access immediately at termination, cancels registered openers and an in-flight
materialization read it owns, and releases acquisition leases only at content
quiescence. Source and retained-content openers therefore accept the
generation owner's cancellation token; a potentially blocking opener must
observe it promptly without depending on a worker thread. The token is scoped
to opener execution and detached under the authority gate before a successful
stream escapes; it is not a returned-stream lifetime signal. Admission stream
wrappers separately combine generation cancellation with the caller token for
asynchronous materialization reads. Returned query streams are the
intentional exception: they remain readable after generation end and pin
backing leases until disposal. `DisposeAsync` has no timeout and remains
incomplete for an abandoned query stream; query consumers are required to
dispose every returned stream. This follows the repository's
`AssemblyContextGroup` gate-and-active-count release pattern and its
`NuGetOperationDeadline.DeadlineStream` linked-cancellation pattern, while
preserving the artifact contract's longer-lived returned stream.
The target configurations pass safety and liveness; three committed
current-mechanics configurations produce
counterexamples showing an open can complete after `EndGeneration`, a
disposal racing `SealAsync` disposes acquisition leases under an active
materialization read, and leases can be released while a published
generation's query stream is open. Its `README.md` records the checked
bounds, results, assumptions, and the three termination-policy obligations
enforced by this contract. These results establish evidence about the model,
not the implementation.

### Supplemental acquisition bridge

`ArtifactSetSession.AddSupplementalAcquisitionAsync` is the focused bridge for
a source that the host may omit from its plan, but whose outcome is no longer
optional once the host invokes it. `Unavailable`, `Rejected`, and `Failed`
remain visible admission failures; supplemental does not mean that a declared
source may fail as success-shaped absence. An `Acquired` outcome with no
artifacts is instead a successful no-op.

The API has the same owner and outcome vocabulary as required acquisition, but
adds an owner-issued capacity value to the callback:

```csharp
ValueTask AddSupplementalAcquisitionAsync(
    Func<
        ArtifactContributionScope,
        SupplementalAcquisitionCapacity,
        CancellationToken,
        ValueTask<ArtifactAcquisitionOutcome>> acquire,
    IReadOnlyCollection<ArtifactWorkspaceRole>? roles = null,
    CancellationToken cancellationToken = default);
```

`SupplementalAcquisitionCapacity` is an immutable positive
`MaxArtifacts`/`MaxArtifactBytes`/`MaxRetainedBytes` grant for that one call.
The workspace computes `MaxArtifactBytes` as the smaller of the session's
per-artifact ceiling and the remaining retained-byte capacity. A host may map
the grant into stricter adapter limits, but may not enlarge it. Source-specific
bounds such as directory `MaxObservedEntries` remain adapter-owned and are not
invented by the workspace.

The current session cannot calculate an honest remaining-byte grant while
required contributions are still deferred until `SealAsync`. The first
supplemental call therefore performs a one-way phase transition:

1. Under the session gate, it permanently closes required acquisition before
   its first await. A later `AddRequiredAcquisitionAsync` rejects even when the
   checkpoint or supplemental call fails. A new session is the escape hatch
   for a composition that needs more required members.
2. Existing required-acquisition failures prevent the callback from running
   before the checkpoint observes cancellation. They retain their original
   outcome kind and diagnostic.
3. The checkpoint applies the current seal order to accepted required batches:
   aggregate count first; then, for each contribution in acquisition order,
   cancellation, materialization and the per-artifact bound, cumulative
   retained bytes, and finally identity collision. Required scope ownership was
   already checked when the batch was added.
4. Count, per-artifact, aggregate-byte, identity, and recognized
   materialization failures retain the current `artifact.session.*` kind,
   diagnostic, and precedence. A zero required count is the sole exception:
   the checkpoint defers `artifact.session.empty` because a later nonempty
   supplemental batch may make the session publishable.
5. A diagnostic checkpoint failure returns normally without invoking the
   callback; a later seal returns `NotPublished`. Cancellation at the same
   per-contribution points aborts and throws `OperationCanceledException`.
6. A successful checkpoint retains the exact required count, byte charge,
   identities, snapshots, roles, and leases. `SealAsync` consumes those
   snapshots and never reopens or double-counts the original contributions.

The checkpoint is a one-way simulation of current seal behavior: except for
deferring the empty-session decision, a checkpoint diagnostic is the same
first diagnostic that sealing the same required state under the same
cancellation observations would produce. Sessions that never call
supplemental acquisition retain the existing required-add and seal-time
materialization behavior for ordinary sources. A duplicate identity now
reports the documented identity-collision diagnostic instead of an incidental
materialization failure. The supplemental model treats this correctly derived
checkpoint result as an owner input; the implementation gate, not that model,
proves the simulation.

Invocation behavior is closed:

| State at method entry | Result |
| --- | --- |
| Session is sealing, published, or disposed | Preserve the existing non-constructing `InvalidOperationException`. |
| Another required or supplemental acquisition is active | Throw the existing acquisition-in-progress `InvalidOperationException`; do not close the required phase. |
| First eligible supplemental call | Close the required phase and run the checkpoint. |
| Checkpoint or an earlier acquisition already recorded a diagnostic failure | Do not invoke this callback; return normally so seal reports the retained failures. |
| Checkpoint succeeded, no failure exists, and positive capacity remains | Record this call as active, issue the grant, and invoke the callback. |
| Checkpoint succeeded but count or retained-byte capacity is exhausted | Record the supplemental capacity rejection without invoking the callback. |

Concurrent supplemental calls are rejected rather than queued. A successful
empty or nonempty call leaves the phase open for the next sequential
supplemental call; any diagnostic failure prevents later callbacks because
publication is already impossible.

For each eligible call, recording the request and resolving it to either a
positive grant or capacity rejection occur under one owner-gate hold before
the first await. Seal, disposal, and a competing call cannot interleave inside
that transition; whichever reaches the gate first determines the result. The
model represents request and resolution as two logical steps but deliberately
permits no external transition between them.

After a successful checkpoint, supplemental calls are sequential. Each call
receives all positive capacity remaining after the checkpoint and every
previous accepted supplemental batch. If either the artifact-count or
retained-byte remainder is zero, the workspace records
`Rejected`/`artifact.supplemental.capacity-exhausted` before invoking the
adapter. That is an attributed failure for a supplemental source the host
chose to invoke, not an empty adapter result.

Owner-produced `artifact.supplemental.*` diagnostics attribute the failure to
the supplemental admission operation rather than the generation-wide seal.
This API does not add a source identifier. Adapter-specific coordinate and
producer attribution remains in the adapter's own preserved diagnostic.

For a returned nonempty batch, the workspace:

1. requires every contribution to belong to the callback's scope;
2. rejects a count overrun before opening content;
3. rejects an identity collision against checkpointed required content,
   previously accepted supplemental content, or the current batch before
   opening content;
4. materializes the complete batch into owner-private snapshots while
   enforcing the granted per-artifact and retained-byte bounds; and
5. atomically commits the snapshots, actual count and bytes, explicit roles,
   and returned lease only after every contribution succeeds.

No partial batch survives. Count, per-artifact, retained-byte, foreign-scope,
identity-collision, and materialization failures use
`artifact.supplemental.count-limit`,
`artifact.supplemental.artifact-byte-limit`,
`artifact.supplemental.byte-limit`, `artifact.supplemental.foreign`,
`artifact.supplemental.identity-collision`, and
`artifact.supplemental.materialization-failed`, respectively. Limit failures
are `Rejected`; foreign scope, identity collision, and materialization failure
are `Failed`. When one byte would exceed both byte bounds, the per-artifact
failure takes precedence. Adapter-produced diagnostic outcomes retain their
adapter-defined kind and diagnostic rather than being wrapped.

`ArtifactMaterializationLimitException` maps to the supplemental
per-artifact rejection. `IOException`, `UnauthorizedAccessException`,
`NotSupportedException`, `InvalidOperationException`, and `OverflowException`
map to supplemental materialization failure, matching the current seal
classification. An unexpected materialization exception aborts the session and
propagates after cleanup rather than becoming a generic diagnostic.

An empty `Acquired` batch contributes no artifact, identity, role, or retained
byte. The workspace still owns its returned lease and attempts disposal before
the call ceases to be in progress or another acquisition may start. A cleanup
failure is recorded in `CleanupFailures`; consistent with other lease cleanup,
it does not turn otherwise valid artifact content into an admission failure or
replace a primary failure. The capacity grant ends only after that cleanup
attempt.

An acquired batch rejected during scope, identity, count, or byte validation
also has its returned lease cleaned up before the call ends. Callback
exception, materialization cancellation, or other exceptional failure aborts
the session through the existing termination path. Cancellation uses the
supplemental call's token for required checkpointing, adapter work, and
supplemental materialization, remains `OperationCanceledException`, and
attaches cleanup failures without replacing cancellation.

If caller cancellation is observed before owner termination, cancellation is
the primary result. If termination closes admission before the owner can commit
a returned or materialized batch, `ObjectDisposedException` is primary. In
either order, cleanup failures are attached without replacing that primary
result.

Seal rejects while checkpoint, adapter work, materialization, or returned-lease
cleanup is active. Disposal closes admission first and prevents a late
nonempty batch from committing. Any late `Acquired` outcome, including empty,
transfers directly to cleanup, after which the call throws
`ObjectDisposedException`; cleanup failure remains attached and visible.
A late `Unavailable`, `Rejected`, or `Failed` outcome has no lease to clean and
cannot be returned through seal after disposal. The call therefore attaches
the exact `ArtifactSetAdmissionFailure` to the `ObjectDisposedException` under
`DotnetInspector.Artifacts.Workspaces.AdmissionFailures`; it must not discard
the adapter's kind or diagnostic. Previously accepted supplemental leases
remain governed by the session's ordinary retained-lease lifetime.

Preserving current termination behavior means `DisposeAsync` cleans leases
already known to the session but does not wait for an in-flight adapter to
return. The supplemental call owns any late returned lease and completes its
cleanup before that call finishes. `CleanupFailures` may therefore gain a late
entry after `DisposeAsync` returns; awaiting the supplemental call is the
completion boundary for that late cleanup.

A session may enter the supplemental phase with no required artifacts. A
nonempty supplemental batch can then make the session publishable; if every
supplemental result is empty, seal retains the existing
`Rejected`/`artifact.session.empty` result. Roles are copied before the first
await and apply only to an accepted nonempty batch. Supplemental acquisition
never infers caller designation, platform trust, or another role from source
kind or location.

This bridge is a bounded implementation stage, not the workspace-wide
whole-plan reservation described earlier in this section. Its grant is
exclusive because the current session serializes acquisition and excludes
seal; it does not reserve against other sessions, join concurrent demands, or
retain charges until dependent groups quiesce. It therefore does not satisfy
the `WorkspaceAdmissionBudget_*` or single-flight target gates. Directory
enumeration and selection remain owned by the
[bounded directory coordinate](#bounded-directory-coordinate); context
identity, assembly projection, and binding remain outside this bridge.

[`SupplementalAcquisitionAdmission.tla`](../models/supplemental-acquisition-admission/SupplementalAcquisitionAdmission.tla)
model-checks the new interaction from phase close through checkpoint, one
explicit requested call, one active exact remaining-capacity grant, empty or
nonempty adapter completion, operation-level acceptance after an abstract
materialization result, cleanup, seal, and close. Its
[model guide](../models/supplemental-acquisition-admission/README.md) records
the 26 configurations, checked properties, 12 reachability probes, nine guard
mutations, commands, and non-claims. It does not derive the checkpoint result,
model per-stream progress or temporary snapshots, prove scope, identity, or
byte-failure precedence, or project a late diagnostic into an exception
payload.

TLC 2026.08.21.155922 (rev `9787e65`, from the pinned `tla2tools.jar` v1.8.0)
checked five complete two-operation bounds with no invariant violations or
temporal counterexamples. The primary, zero-required, count-dimension,
retained-byte-dimension, and per-artifact-dimension runs generated
1,097/1,264/1,115/1,115/1,047 states, found 581/647/581/581/557 distinct
states, and reached depth 15/16/15/15/15, respectively. Each reachability
sentinel produced its intended counterexample. Mutations that accept a late
required add, start before the checkpoint, independently omit count,
retained-byte, or per-artifact admission, accept after close, release before
lease cleanup, convert failure to empty success, or commit an empty result
violated their paired properties. These results establish the bounded model
claims, not implementation conformance.

Implementation conformance is enforced by these focused gates:

- `SupplementalAcquisition_RequiredCheckpointPreservesSealOutcome`
- `SupplementalAcquisition_SealUsesCheckpointedSnapshots`
- `SupplementalAcquisition_EmptyBatchPublishesNoArtifactsAndOwnsItsLease`
- `SupplementalAcquisition_ReservesBeforeAdapterAndCannotOverrunAtSeal`
- `SupplementalAcquisition_PreservesAdapterOutcomeKindAndDiagnostic`
- `SupplementalAcquisition_NonEmptyBatchPreservesScopeAndRoleChecks`
- `SupplementalAcquisition_IdentityAndMaterializationAreAtomic`
- `SupplementalAcquisition_RejectedAcquiredBatchCleansLeaseWithoutMaskingFailure`
- `SupplementalAcquisition_ConcurrentTerminationDisposesLateOutcomeAndReservation`
- `SupplementalAcquisition_LateDiagnosticRemainsVisibleOnTermination`
- `SupplementalAcquisition_CancellationRemainsCancellation`

Retaining content does not retain authority. The artifact owner issues two
different source-neutral access leases:

- an **admission lease** authorizes the context loader to project sealed
  artifacts into assembly identities and participants while constructing the
  group. It is issued under the first authorized query plan that demands that
  context; loading an inert definition alone cannot obtain one. The lease
  expires when group publication succeeds or the admission attempt aborts.
  Neither the group nor its participants retain it;
- a **query lease** revalidates the current query plan's capabilities and
  source policy before it can select participants, observe binding or
  correspondence answers, receive content, or use a retained snapshot.

Changed, narrowed, or revoked authorization rejects the query before catalog or
participant selection even when the selected image remains authorized and the
bytes and prior binding answers remain in memory. Reuse of a group also
requires that its binding-policy and correspondence generation be compatible
with the current plan's complete authorization scope. Reauthorizing only the
image is insufficient because catalog membership and binding answers can
themselves reveal unauthorized candidates.

During construction, only the current admission lease exposes content. After
group publication, guarded content access rejects that expired lease and
accepts only a current query lease. An artifact catalog descriptor or
`ResolvedAssemblyReference` cannot bypass the owner with a bare `Func<Stream>`
or readable path. A path on a target descriptor is inert location evidence, not
read authority; when a producer genuinely requires a path, the current lease
may provide a lease-scoped path to the exact retained snapshot. Receiving or
opening that path grants no designation or core-library trust; those remain
separate workspace admission roles. This is a target change from the current
parameterless
`ResolvedAssemblyReference.OpenRead` and public readable `Path`.

`ArtifactContentReference` is the compatibility query-time input to a downstream
content consumer. The artifact owner issues it for one identity in a sealed generation
and binds that artifact's descriptor and acquisition registration. Role
and registration observations and retained-content opens revalidate the query
lease supplied when the reference was issued. The type makes no claim that the
content is a managed assembly; Metadata owns that decode and identity.

Assembly projection passes the exact acquisition registration and the
reference's guarded content callback to
`ResolvedAssemblyReference.CreateFromArtifactIfManaged`. Metadata retains the
registration, decodes assembly identity, and binds a non-empty MVID. It does
not receive the workspace role set or interpret a lease-scoped path as content
authority, designation, or trust.

#### Phase-scoped retained byte access

Issue [#5884](https://github.com/richlander/dotnet-inspect/issues/5884) supplies
the Artifact Acquisition boundary consumed by Metadata admission and query
validation in
[#4857](https://github.com/richlander/dotnet-inspect/issues/4857). That consumer's
[assembly projection contract](assembly-inspection-query.md#admission-scoped-artifact-projection)
owns classification and assembly facts; this section owns only the retained
image, authorization, and callback lifetime.

One immutable owner-retained image backs admission, subsequent query callbacks,
and compatibility streams for an artifact. Scoped access does not reopen the
source or allocate another full-image copy. The owner transfers its private
materialized array into immutable storage; a low-level owner supplying an
`ImmutableArray<byte>` must already have relinquished mutable aliases. This
follows the existing `AssemblyImageSnapshot` ownership-transfer pattern rather
than treating trusted in-process owners as hostile. A stream-only retained
content registration remains a compatibility facility and explicitly rejects
scoped byte access; arbitrary openers cannot attest an immutable image.

The synchronous callback convention follows .NET span callbacks: two distinct
`readonly ref struct` views carry the exact opaque `ArtifactIdentity`, its
generation, and `ReadOnlySpan<byte>`. Only the artifact owner constructs these
views. `ArtifactAdmissionContentCallback<TResult>` and
`ArtifactQueryContentCallback<TResult>` take a scoped view and caller
cancellation token. Their result type cannot be byref-like. The consumer
finishes image-local work before returning; retaining a view or borrowed span
across an asynchronous continuation is not an available operation.

`RetainedArtifactContent.WithAdmissionContent` accepts only admission leases;
`WithQueryContent` accepts only query leases. Each registers access atomically
with authorization validation, before invoking consumer code. Missing,
foreign, disposed, revoked, or ended authority produces
`ArtifactContentAccessOutcome<TResult>.Unauthorized` without invocation.
`Accessed.Value` is the consumer's result, including any consumer-owned typed
rejection. Consumer exceptions retain their instance and type, including
`UnauthorizedAccessException` and `ObjectDisposedException`; they cannot be
mistaken for owner rejection. Caller cancellation is observed before access
and after a normally returning callback, and remains cancellation.

Authorization expiry rejects subsequent callbacks, not work already admitted.
An active callback keeps acquisition leases alive through generation end until
it unwinds, just as an already-returned compatibility stream does until
disposal. Callbacks must return; they must not synchronously wait for the
session's own disposal, which waits for them. No worker thread or background
execution is required by this contract.

`ArtifactSetSession.SealWithProjectionAsync` is the pre-publication integration
point. After bounded materialization succeeds, it supplies each artifact in
catalog order through an admission callback while the catalog remains
unpublished. The callback returns either no failure or an
`ArtifactSetAdmissionFailure`. A failure rejects the whole generation and stops
further projection. An exception aborts and cleans the generation, then
propagates unchanged with the session's existing ancillary cleanup evidence.
Projected values collected by the caller remain provisional until `Published`;
they cannot authorize content and must be discarded on any other completion.
Publication occurs only after every callback succeeds and caller cancellation
and session termination are checked. Ordinary `SealAsync` remains the
compatibility path without projection.

The session's query callback validates current authorization before selecting
an artifact. A valid lease with an identity outside that session is an explicit
lookup error, not a request to reacquire or substitute content. After selection,
the retained-content owner atomically admits the callback, so revocation or
termination during that handoff can still reject it.

The existing
[generation-access model](models/artifact-generation-access/README.md) supplies
the access-registration/quiescent-release design basis. Scoped callbacks reuse
that protocol; they do not add a second release mechanism or alter the session's
construction, sealing, publication, and disposal states. The model's recorded
stream results are not evidence of these new APIs. Release conformance is
established by the focused product gates:

- `ScopedContent_AdmissionAndQueryKeepExactIdentityAndBytes`
- `ScopedContent_RejectsAuthorityBeforeInvocation`
- `ScopedContent_ConsumerExceptionsAreNotAuthorizationFailures`
- `ScopedContent_CancellationRemainsCancellation`
- `ScopedContent_RequiresImmutableSnapshot`
- `ScopedContent_RepeatedQueriesDoNotAllocateFullImage`
- `ScopedContent_ActiveCallbackPinsRelease`
- `ArtifactProjection_PrecedesPublicationAndReusesSnapshot`
- `ArtifactProjection_RejectsAtomicallyAndRetainsDiagnostic`
- `ArtifactProjection_MaterializationFailureDoesNotInvokeConsumer`
- `ArtifactProjection_ExceptionAbortsWithoutReclassification`
- `ArtifactProjection_CancellationOrTerminationPreventsPublication`
- `ArtifactProjection_QueryRejectsBeforeSelectionAndPinsRelease`

Production-host adoption is tracked by
[#5766](https://github.com/richlander/dotnet-inspect/issues/5766). Its revised
path has **13 steps**, inserting this prerequisite before Metadata
implementation; CLI and Browser/Wasm both adopt the shared query/result
contract in step 13. Sparse composition (#5843) and explicit-context composition
(#5053) replace the relevant compatibility reference consumers after #4857.
General retirement of unrelated stream/descriptor consumers remains outside
this slice; neither host claims assembly-pattern search from this API alone.

It does not:

- resolve package versions;
- parse project assets;
- choose a target framework;
- inspect PE metadata;
- construct assembly binding groups;
- render diagnostics.

#### On-demand retained-content digests

`ArtifactSetSession.GetContentDigest` and the corresponding
`ArtifactContentReference` operation implement [#4916](https://github.com/richlander/dotnet-inspect/issues/4916).
The artifact-session owner issues an immutable SHA-256 digest bound to the exact
`ArtifactIdentity` and generation. Its lowercase hexadecimal value is content
evidence, not artifact correspondence: equal bytes do not coalesce distinct
registrations, artifacts, or generations.

Every request uses the existing query-content authorization and access lifetime,
including requests for a cached value. `Accessed.Value` carries the owner-issued
digest. The existing typed `Unauthorized` arm covers missing, foreign, disposed,
revoked, replaced, or ended query authority; it does not invent a finer
authorization classification. A current lease selecting an identity outside
the session remains a lookup error. The value may escape the query; it contains
no borrowed content view, stream, or lease.

The operation is explicit and lazy. Ordinary acquisition and publication do not
hash. The first authorized request invokes its required `chargeWork` callback
with the retained byte length immediately before the hash pass. The callback
applies the requesting operation's budget; it may refuse by throwing, and that
exception propagates without a published digest. A successful value is reused
by later authorized requests without another charge or hash pass. Concurrent
requests for one artifact share the successful cold computation. Neither
computation nor reuse opens the original source or changes the catalog.

Cancellation follows scoped-content semantics: it is observed before admission
and after the synchronous operation, and remains cancellation. Once charged,
the bounded hash pass completes and memoizes its value even if cancellation is
requested during it; a cancelled caller does not receive that value, but the
completed work is not charged again. An admitted operation may finish after
authorization expires, and pins retained resources until it returns. Charge
callbacks have the same synchronous lifetime restriction as other content
callbacks: they must not wait for disposal of their own session.

The existing generation-access model supplies the authorization and quiescence
basis. This operation reuses that protocol rather than adding publication or
release states. SHA-256 and per-artifact lazy computation follow ordinary .NET
hashing and memoization; no worker thread is required. Release gates are:

- `Digest_ChargesColdPassAndReusesOwnerValue`
- `Digest_UsesRetainedSnapshotWithoutReopeningSource`
- `Digest_EqualBytesDoNotCoalesceArtifactIdentity`
- `Digest_RejectsAuthorityEvenWhenCached`
- `Digest_ReferenceRevalidatesAndAuthorizesBeforeLookup`
- `Digest_ChargeFailurePropagatesWithoutPublishingValue`
- `Digest_CancellationRemainsCancellationAndCompletedWorkIsMemoized`
- `Digest_ConcurrentRequestsShareOneColdPass`
- `Digest_ActiveComputationPinsReleaseAndValueCanEscape`

The immediate consumer is the artifact contract harness; the intended
production consumer is tools compile-back reference selection, tracked by
[#5890](https://github.com/richlander/dotnet-inspect/issues/5890). Digest adoption
has two steps: this owner API with its harness consumer, then consumption by
the tools frozen-reference stage within that tracker's second decoder-adoption
step. The user selected #4916 as the prerequisite in the tools-first sequence;
CLI/Wasm production adoption remains deferred, not implied by the portable API.
No existing eager-hash path is replaced, and this slice does not construct a
compiler reference set, artifact manifest, or admission receipt. The result is
structured data; rendering remains with its eventual consumer.

### Multiple sources are ordinary

A workspace is not associated with one source. For example:

```text
workspace
  artifact set
    local acquisition
      ./bin/MyApp.dll
    package acquisition
      Newtonsoft.Json@13.0.4/lib/net6.0/Newtonsoft.Json.dll
    platform acquisition
      System.Runtime.dll
    CI acquisition
      base-build/api/Contracts.dll
      pr-build/api/Contracts.dll
```

Provenance belongs to each artifact, not to the workspace or the artifact set.
An assembly context group may contain participants projected from several
acquisitions when its binding policy permits that composition. Conversely,
artifacts from one acquisition may be partitioned into several groups when they
represent incompatible framework or runtime contexts.

The artifact set therefore owns content lifetime but not assembly grouping.

### Explicit local/designated/platform assembly context

One focused context shape composes:

- one exact local assembly as the inspection root;
- zero or more exact local files designated by the caller; and
- one exact installed-platform realization.

This section owns acquisition, role assignment, binding-policy realization,
lifetime, and atomic workspace publication for that shape. It consumes shared
local-path admission from #5096, admission-scoped assembly projection from
[#5143](https://github.com/richlander/dotnet-inspect/issues/5143), and the
platform closure and overlay policy from
[platform composition and overlays](platform-composition-and-overlays.md);
it does not redefine local path normalization or entry classification,
assembly identity/MVID construction, assembly-identity matching,
designated-over-platform precedence, or request-level compatibility.

#### Typed request and outcome

The host-neutral request consists of one `ExactLocalFileCoordinate` root, a
sequence of `ExactLocalFileCoordinate` designations, and one
`InstalledPlatformCoordinate`. The coordinates are acquisition requests, not
paths that later consumers may reinterpret as provenance or authority.
Directory, package, project, dependency-manifest, sibling-discovery, Browser
upload, and remote-platform coordinates are not accepted by this request
shape.

The workspace derives an `AuthorizedExplicitAssemblyContextDemand` from one
current query-plan demand, the exact request, the complete source-policy
generation, and finite local-file, platform-artifact, acquisition-byte,
retained-byte, and group-snapshot limits. It snapshots those inputs and owns
the resulting admission authorization. The realizer accepts only that
owner-issued demand; an inert workspace definition, absent query demand,
stale/narrowed plan, or direct request cannot reserve budget, call an adapter,
or mint a role.

Before reservation, the workspace frames one syntactic context-generation key
from every request field, the policy generation, all admission-affecting
limits, and the selected adapter capability identities, including the path
spellings exactly as supplied. Length framing prevents coordinate-boundary
ambiguity without interpreting a path. Only demands with equal complete keys
are compatible and single-flight under the existing admission lifecycle.
Different limits wait for the active generation to terminate and then replan;
they cannot join it. Different spellings are different generations even when
issue #5096 later gives them the same canonical local-coordinate identity;
cross-spelling single-flight convergence is not a claim of this context.

The installed platform adapter registration must declare finite maximum member
and byte bounds within the authorized demand. Raw local-coordinate cardinality
and the declared adapter bounds compute a conservative combined reservation
with checked arithmetic; the workspace reserves it from the same aggregate
account as every other in-flight or retained context before local-path
admission or either adapter begins. Duplicate coordinates may leave part of
that reservation unused; publication or cleanup releases the remainder under
the ordinary session rules.

`ExactLocalFileCoordinate` frames one path spelling with the expected
regular-file kind. Shared local-path admission from #5096 owns normalization,
entry classification, link policy, supported-host behavior, and the canonical
coordinate identity this context consumes. A coordinate admitted as any other
entry kind is rejected visibly and is never expanded into directory members.

The realization outcome is one of:

- `Realized`, a non-owning opaque context handle identifying one
  workspace-owned sealed `AssemblyContextGroup`, its exact root
  `AssemblyContextParticipant`, its `ArtifactGenerationIdentity`, and the
  artifact-owner and Metadata-owner registrations that relate every
  participant to its retained bytes and acquisition evidence;
- `NotRealized`, carrying typed invalid-input, unavailable, rejected, failed,
  unsupported-capability, budget, projection, or role-conflict evidence.

Caller cancellation first records an owner-visible request, then throws
`OperationCanceledException` after that demand is detached; it does not occupy
`NotRealized` or become an acquisition failure. A demand cancelled while
pending behind an incompatible generation is removed from pending state and
cannot later replan, reserve, or acquire. Other compatible waiters keep the
shared admission alive. When the final waiter detaches, the workspace requests
adapter cancellation and enters draining, but the detached caller does not wait
for that owner-managed cleanup to finish. If workspace disposal has already
moved an attached admission to draining, the caller still detaches and returns
cancellation without waiting for adapter cleanup. Reservation release still
waits for cleanup, and a new demand cannot join the draining operation.

The root participant is reference-identical to exactly one member of the
group. A successful outcome never asks a consumer to rediscover the root by
path or assembly name. `Realized` does not expose that group, root, catalog, or
participant correspondence directly. `Realized.OpenContext` accepts a current
authorized query plan. The workspace validates that plan against the original
demand's complete request, source-policy generation, and authorization scope,
issues the corresponding query authorization and lease internally, and returns
a disposable lease-bound context view that identifies the group and exact root
and mediates their query operations. The caller never mints or supplies a raw
`ArtifactQueryLease`. A changed, revoked, incompatible, or ended plan rejects
view acquisition.

Every operation on an existing view revalidates its current authorization
before participant lookup, callback invocation, or returning a cached image or
cached failure. Replaced or revoked authorization and view disposal therefore
reject cached and cold paths alike without exposing participant or image facts.

Before publication, the artifact owner issues one admission-scoped guarded
content projection for each required artifact. The realizer passes that
projection to #5143 and receives Metadata-owned assembly registration,
identity, and bound-MVID facts with no retained opener or lease. Each fact
preserves the exact `ArtifactAcquisitionRegistration`; the admission
capability expires on group publication or abort and is never retained by a
participant.

After publication, a query-authorized `ArtifactContentReference` is an internal
handoff from the artifact owner to group-owned image acquisition. Neither
`Realized` nor its context view returns that reference, a
`ResolvedAssemblyReference`, a readable path, or a `Stream`.

The lease-bound context view mediates bounded image operations using
context-specific `ExplicitAssemblyImageView` and
`ExplicitAssemblyImageAccessResult<TResult>` types. The view is stack-only and
contains only an opaque `AssemblyContextParticipantIdentity` and immutable
content span. A rejected result contains that same opaque identity and a typed
open failure. Owner-supplied members in the complete public property and result
closure contain only that identity, span, and failure; the caller's generic
result remains caller-owned. No public type exposes an assembly descriptor,
artifact authorization or lease, opener delegate, or other content capability.

The group opens retained content under the view's query lease, validates and
snapshots the image, closes the internal stream, and only then invokes the
consumer callback. It may adapt its existing `AssemblyImageView` internally,
but that descriptor-bearing view and the existing descriptor-bearing rejection
result do not cross this context boundary. A foreign participant is rejected
before content access.

The workspace remains the sole owner of the group and artifact session.
Disposing a context view revokes that query access but does not release the
session, and dropping a `Realized` handle has no lifetime effect. This focused
shape adds no independently disposable context. A current generation's
session, group, count, and retained-byte charges remain until workspace
disposal or workspace-owned retirement. Retirement blocks new admission
immediately but releases those charges only after existing generation work
quiesces. Workspace disposal closes admission, ends every generation, and
follows the content-quiescence contract in the
[generation-access interaction model](models/artifact-generation-access/README.md).
Both adapters must honor owner cancellation during acquisition and
materialization and provide bounded snapshot openers that cannot wait on their
original source. This focused view creates no consumer-owned stream, so it does
not adopt or resolve the general artifact API's abandoned-stream termination
policy.

Downstream consumers receive participant identity and correspondence under
their current query lease, then use the group's bounded immutable-image
callback. They do not reopen the local source or installed platform path.

#### Admission and roles

The workspace registers the authorized demand under its syntactic generation
key and reserves one aggregate admission. The realizer then obtains #5096
admission for each local coordinate, coalesces equal admitted identities, and
invokes the exact-file local adapter and installed-platform adapter within one
`ArtifactSetSession`. Every supplied local coordinate and every selected
platform member is required. It projects managed assembly participants only
after all acquisitions succeed, using #5143's admission-scoped assembly facts,
then seals the session privately. It completes the binding-policy preparation
below before constructing or publishing the group. An invalid managed image,
missing required platform member, failed acquisition, projection failure, role
conflict, or budget exhaustion publishes neither a shortened group nor a
partial session. Cancellation remains cancellation and follows the session
cleanup contract.

Workspace admission, rather than an adapter or downstream assembly consumer,
assigns these roles:

| Input | Context role | Workspace role |
| --- | --- | --- |
| Exact root | inspection root | `CallerDesignated` |
| Exact designated file | participant | `CallerDesignated` |
| Root also named as designated | inspection root | `CallerDesignated` |
| Installed-platform member | participant | `PlatformAuthorized` |

The platform role is granted only after validating the platform owner's
generation-scoped realization evidence against the authorized demand, selected
hive coordinate, contributing artifact registrations, and current workspace
generation. One closure proof may authorize all of its member registrations
inside that admission; foreign, stale, ended-generation, mismatched, or
replayed evidence cannot authorize another admission and produces typed
failure without publication. Local provenance does not grant designation, and
platform provenance does not grant authority by itself. The exact-local
request grants designation to each file the caller names, including the root;
it grants nothing to a sibling. Adapters return acquisition facts; admission
grants policy roles.

Before group construction, the realizer derives one immutable projection from
each participant's assembly registration to its workspace roles and preserves
that projection in the context generation. This owner does not translate a
role into source provenance or define how a binding policy selects among
registrations. Platform-policy consumption of this output belongs to
[platform composition and overlays](platform-composition-and-overlays.md) and
is tracked by #5133. Until that contract lands, correspondence from these roles
to the current provenance-based binding policy is unverified.

The installed-platform coordinate selects one exact coherent closure through
the installed-platform adapter. For this context it contains one owner-issued
installed-hive identity, one platform family, one exact version, and the
implementation-layout kind. The adapter returns the immutable
`InstalledPlatformRealization` owned by
[platform composition and overlays](platform-composition-and-overlays.md) and
tracked by #5139.

This realizer validates that proof against the authorized demand, selected
hive, family, version, layout kind, member registrations, and current
generation. It does not construct or reinterpret closure membership. Missing
or mismatched proof, an unavailable requested closure, or a realization that
violates the owner-issued uniqueness contract fails visibly without
publication. Exact closure membership and fallback exclusions are defined by
[#5139](https://github.com/richlander/dotnet-inspect/issues/5139) but remain
product-unverified.

#### Binding-policy preparation and generation replacement

After assembly-registration projection and role assignment are final, but
before constructing any `AssemblyContextParticipant`, the workspace issues one
immutable `AssemblyBindingPolicyPreparation` for that private context
generation. The preparation carries owner-issued identities for:

- the context generation and preparation occurrence;
- the exact ordered participant plan;
- the exact role projection over those participants;
- the exact delegated-policy map consumed by composition; and
- the non-reusable `AssemblyBindingPolicyVersion` captured from the composed
  binding policy before composition begins.

The participant plan, role projection, and delegate map are already complete.
For this sealed context, every planned participant registration has one
delegated-policy route before preparation, and discovery may use only those
planned registrations as binding origins. If the workspace cannot supply a
configured route map for which discovery-time route addition is impossible, it
rejects realization. This requirement constrains the prepared policy state,
not whether its policy type can learn routes in other, open-ended contexts.
The workspace cannot append a late participant, learn a late origin route,
change a role, or replace one delegate after issuing the preparation. Binding
composition consumes the complete candidate-domain and finalization contracts
from
[complete identity-eligible binding composition](type-forwarding-resolution.md#complete-identity-eligible-binding-composition).
The delegated-policy map names the delegates and routes used to build that
composite. Their individual versions and refresh remain internal to the
composite owner. The workspace captures and later compares only the
composite's distinct outer token; delegate drift reaches the workspace when the
composite publishes a refreshed state and outer token.
This is the complete-route-map option anticipated by the adjacent
[composite policy contract](type-forwarding-resolution.md#atomic-selectionversion-snapshots).
This section does not reconstruct selections from evidence order or define
selection, ambiguity, miss, or precedence semantics.

The binding-policy owner may instead return a typed completion failure. The
workspace preserves that failure as `NotRealized`, constructs no group, and
publishes no current generation. `PolicyVersionChanged(expected, observed)` is
one such control result. Binding-selection results such as selected, ambiguous,
unavailable, rejected, and miss arms retain their adjacent meanings; none is
reclassified merely because it is carried by a completed policy.

Successful composition returns one immutable
`CompletedAssemblyBindingPolicy` receipt. In addition to the owner-issued
policy capability, the receipt repeats the exact preparation,
participant-plan, role-projection, delegate-map, and captured outer-version
identities. These fields are correspondence evidence, not values that the
workspace may normalize or infer. Before adopting the receipt, the workspace
validates them in that order and finally compares the composite policy's
current outer version with the captured version. The first failed check returns
one of these typed reasons:

| Failed check | Reason |
| --- | --- |
| Preparation identity | `ForeignPreparation` |
| Participant-plan identity | `ParticipantPlanMismatch` |
| Role-projection identity | `RoleProjectionMismatch` |
| Delegate-map identity | `DelegateMapMismatch` |
| Receipt's captured version | `CompletionVersionMismatch` |
| Composite policy's current outer token | `PolicyVersionMismatch` |

A rejected receipt remains failed with that exact reason. It cannot become an
empty policy, trigger group construction, or promise an automatic successful
retry. Cleanup follows the private generation's ordinary failure path.
Multiple simultaneous correspondence mismatches are reported by the first
table row in validation order. The TLA+ model isolates each cause; the named
implementation gate below owns this precedence.

Successful adoption records the immutable receipt and captured outer token in
the private generation. Only then may the workspace construct each
`AssemblyContextParticipant` in the exact plan with that adopted composed
policy, followed by the `AssemblyContextGroup`. Neither a participant nor the
group has a policy setter or rebinding path. The composite may still refresh
its own immutable internal state and outer token under the adjacent
binding-version contract; that owner-local refresh does not mutate any
participant, the group, or adopted receipt. If the outer token changes before
publication, `PolicyVersionChanged(expected, observed)` projects to workspace
`PolicyVersionMismatch`, the private generation fails, and nothing becomes
current.

Workspace publication is one atomic transition to a
`CurrentExplicitAssemblyContextGeneration` containing the sealed artifact
session, exact group and root, and adopted policy from the same generation.
The artifact-session model continues to own its internal seal and cleanup;
the workspace model projects only the group/policy visibility needed to prove
that no observer can obtain a group without its policy, a policy without its
group, or an old group paired with a replacement policy.

The adopted receipt and captured token remain immutable after publication.
Before returning a current context view, and during the existing per-operation
authorization revalidation before participant or cached-result access, the
workspace compares the captured token with the composite's current outer token.
An access request may wait before that gate without entering the generation.
At the gate, a mismatch is the observation and retirement linearization point:
that operation rejects with typed `PolicyVersionMismatch`, and the workspace
atomically removes both the group and policy from current-generation admission.

Retirement means that no new view or operation can enter the old generation.
It does not revoke work that already passed its access linearization point or
mean that the old session and group have physically quiesced. Existing leases,
budget retention, callback completion, and eventual cleanup remain governed by
the generation-access and group-lifecycle contracts.

Only after that atomic removal may a subsequent authorized realization or
current-access demand start a replacement preparation. The replacement
receives a new context-generation identity, a new preparation identity, and
the then-current non-reusable outer token. It does not mutate or rebind the
retired group. Admission uses the workspace's ordinary aggregate budget and
may wait for the old generation to quiesce and release its charges. A budget
rejection or any other replacement failure remains `NotRealized` with no
current generation and no automatic retry.

For a started, admitted replacement whose composite token remains stable, fair
preparation, adoption, construction, and publication eventually settle. Each
demand makes at most one attempt. A token change returns typed
`PolicyVersionMismatch`, and only a later authorized demand may try again with
a new generation identity, preparation identity, and then-current token. The
workspace never automatically retries a failed private generation. The
complete participant and route plan removes discovery-time route growth from
this context, so an observed change is external policy drift rather than
expected realization progress. The workspace makes no convergence or
elapsed-time guarantee under continuing churn.

The workspace may observe drift at realization and current-access boundaries;
this contract does not require a background watcher, prescribe notification or
polling cadence, or make elapsed-time claims. Acquisition, binding arbitration,
query semantics, and post-retirement resource cleanup remain with their
adjacent owners.

#### Duplicate and collision policy

Local-coordinate equality is the canonical identity issued by shared
local-path admission #5096. This context neither compares raw path strings nor
adds a second physical-file, casing, link, or host rule. Distinct admitted
coordinate identities remain distinct even when later assembly projection
finds equal metadata identity.

Duplicates and cross-role collisions then have these outcomes:

- repeated spellings of one designated coordinate acquire and register that
  local artifact once when #5096 assigns them the same canonical coordinate
  identity, preserving first-occurrence order;
- when the root is also designated, one local acquisition and participant
  carries both the root context role and `CallerDesignated`;
- a local root or designation whose admitted source path also appears in the
  platform realization remains distinct from the platform acquisition. Each
  keeps its own artifact and assembly registration, provenance, and role.
  Shared retained storage may deduplicate equal immutable bytes, but it must
  not merge those identities or grants;
- a platform realization that repeats one platform asset coordinate, reuses a
  registration, or projects two distinct members with one canonical assembly
  identity is rejected as a typed incoherent-realization conflict under
  #5139. An admission plan that attaches a local-only role to a platform
  realization is rejected as a typed role conflict;
- different files that decode to the same assembly identity remain distinct
  participants. The binding-policy owner, not path normalization, decides
  whether that identity set is selected, shadowed, ambiguous, or invalid.

Consequently, a designated/platform same-path case can preserve both the
designated candidate and the platform-backed candidate. The workspace does not
erase the evidence that the binding policy needs, and it does not promote an
ordinary sibling merely because that sibling is nearby.

The public request cannot directly assign roles. The role-conflict arm protects
the workspace boundary against an internally inconsistent adapter realization
or admission plan; its gate uses a controlled adapter seam rather than claiming
that ordinary request normalization can produce that state.

#### Host capability and interaction scope

The request and outcome surfaces contain no platform-specific implementation
types and remain portable. A host without exact-local-file or
installed-platform adapters returns a typed unsupported-capability result
before admission. Browser/Wasm does not reinterpret an upload as a local file
or silently switch to remote platform acquisition; those are separate context
shapes.

Each generation's realization is one-shot and immutable. All adapters and
binding-policy composition finish before publication, and adding, removing, or
replacing any input requires a new session and generation in the owning
workspace. Observed binding-version drift also retires rather than mutates the
current generation. Concurrent realization and retained contexts draw from
that workspace's one aggregate budget. Artifact-count and retained-byte
charges remain while a generation is current and, after retirement or
workspace disposal, until quiescent cleanup releases the session.

This design introduces no incremental admission, independently disposable
context, cross-spelling convergence, mutable candidate set, background polling,
or elapsed-time replacement guarantee. The existing
[artifact-session admission model](../models/artifact-session-admission/ArtifactSessionAdmission.tla)
covers one demand generation's single-flight lifecycle, pending cancellation,
attached cancellation before and after disposal enters draining, and aggregate
publication ordering, while the
[generation-access model](models/artifact-generation-access/README.md) covers
one generation's content-access and workspace-disposal handoff. The
[workspace binding-policy realization model](models/workspace-binding-policy-realization/README.md)
covers exact policy completion adoption, policy-before-group construction,
atomic group/policy publication, observed-drift retirement, and replacement
ordering across two generations.
`ExplicitAssemblyContext_FailurePublishesNoPartialGroup`, not the admission
model, owns evidence that several source acquisitions publish all-or-nothing.
Designated/platform selection remains covered by the
[platform-overlay model](models/platform-overlay-resolution/README.md).
Introducing incremental inputs, cross-spelling convergence, per-context
release, concurrent realization outside the existing admission lifecycle,
mutable role grants, or eager replacement preparation requires stopping and
modeling those interactions first.

#### Mock realization

The documentation-only design can be read against this typed mock:

```text
request
  root        = /work/App.dll
  designated  = [/work/System.Collections.dll,
                 /work/./System.Collections.dll]
  platform    = installed hive h1
                / Microsoft.NETCore.App @ 10.0.4
                / implementation

realized generation g1
  root        = participant p1 / artifact a1 / local
                / root + caller-designated
  participant = p2 / artifact a2 / local / caller-designated
  participant = p3 / artifact a3 / platform / System.Collections
  participant = p4 / artifact a4 / platform / System.Private.CoreLib
```

The two designation spellings produce only `a2`. The local and platform
`System.Collections` participants remain distinct even if their source paths
or bytes coincide. The mock demonstrates realization, not which binding
candidate later wins. As a neighboring negative case,
`designated = [/work/]` produces typed rejected local-path-admission evidence
for an unexpected entry kind and no published session; it does not designate
`App.dll`, `System.Collections.dll`, or any sibling.

The same realization's policy lifecycle is:

```text
g1 prepares participants [p1, p2, p3, p4], their roles, and delegate map d1
the composed binding policy contributes outer token v1
g1 adopts the exact completed policy, constructs its group, and publishes
delegate drift makes the composite publish refreshed outer token v2
a current-access gate observes v2, rejects entry, and atomically retires g1
a later authorized demand prepares the same requested context at v2
g2 adopts its exact completed policy, constructs its group, and publishes
```

No state exposes the `g1` group with the `g2` policy, exposes either policy
without its matching group, or admits new work to `g1` after observed drift.
The trace does not claim that an unchanged request must produce identical
participants or policy answers after the composite's outer token advances.

As a correspondence near miss, suppose the `g1` receipt names delegate map
`d2` while its preparation names `d1`. Adoption returns
`DelegateMapMismatch`; no group is constructed and no current generation is
published. It does not guess which map was intended or continue with an empty
policy.

#### Required implementation gates

These properties remain unverified until the named Release gates land:

- `ExplicitAssemblyContext_ExactInputsFormOneSealedGroup` proves a compiled
  root fixture, one exact designation, and an installed platform closure enter
  one group with distinct owner-issued roles, `CallerDesignated` on every exact
  local input, and one unambiguous root;
- `ExplicitAssemblyContext_QueryDemandAuthorizesAdmission` proves the exact
  query demand issues the admission authority covering both adapters and that
  absent, stale, narrowed, definition-only, or direct-request access cannot
  reserve, acquire, or publish;
- `ExplicitAssemblyContext_QueryPlanIssuesLeaseBoundView` proves an authorized
  plan obtains the group/root view and absent, incompatible, revoked, or ended
  authorization cannot obtain one;
- `ExplicitAssemblyContext_ContextViewRevalidatesEveryOperation` warms image
  and failure caches, then replaces/revokes authorization or disposes the view
  and proves later access rejects before participant lookup, cached-result
  return, or callback invocation;
- `ExplicitAssemblyContext_CancellationDetachesOneWaiter` proves a cancelling
  joined waiter throws after detachment without stopping or waiting for the
  shared admission, while final-waiter cancellation enters draining and blocks
  a new join until owner cleanup releases the reservation;
- `ExplicitAssemblyContext_PendingCancellationCannotReplan` holds one demand
  pending behind an incompatible active generation, records cancellation, lets
  the active generation terminate, and proves the cancelled demand cannot
  later replan, reserve, invoke either adapter, or publish;
- `ExplicitAssemblyContext_DrainingCancellationDetachesWaiter` begins
  workspace disposal with an attached waiter, records that waiter's
  cancellation after admission enters draining, and proves the caller detaches
  and throws without waiting for adapter cleanup or receiving a late adapter
  outcome;
- `ExplicitAssemblyContext_SyntacticGenerationKeyIsSingleFlight` proves
  identical framed requests join one admission while different path spellings
  remain different generations even when #5096 later assigns equal canonical
  local-coordinate identity within each realization;
- `ExplicitAssemblyContext_GenerationCompatibilityIncludesEveryLimit` proves
  concurrent demands with different limits or adapter capability identities
  cannot join and replan after the active generation terminates;
- `ExplicitAssemblyContext_RoleProjectionPreservesEveryGrant` proves the
  context generation preserves each participant registration and exact
  workspace-role set without provenance translation;
- `ExplicitAssemblyContext_PolicyCompletionPrecedesGroupConstruction` proves
  no absent, pending, or rejected policy receipt can reach group construction;
- `ExplicitAssemblyContext_EveryParticipantUsesAdoptedPolicy` proves each
  participant in the exact plan is constructed only after adoption and receives
  that receipt's exact composed-policy capability rather than a placeholder or
  foreign policy;
- `ExplicitAssemblyContext_PolicyAdoptionRequiresExactPreparation` proves a
  completion from another preparation is rejected without group construction
  or publication;
- `ExplicitAssemblyContext_PolicyAdoptionRequiresExactParticipants` proves a
  completion over a shortened, extended, reordered, or foreign participant
  plan is rejected;
- `ExplicitAssemblyContext_PolicyAdoptionRequiresExactRoles` proves changed,
  omitted, or foreign role-projection evidence is rejected;
- `ExplicitAssemblyContext_PolicyAdoptionRequiresExactDelegateMap` proves a
  changed, omitted, or foreign delegated-policy map is rejected;
- `ExplicitAssemblyContext_DiscoveryUsesCompleteRouteMap` proves every
  discovery binding origin belongs to the exact participant plan and already
  has its delegated-policy route before preparation, including a multi-hop
  forwarding fixture that completes without an observed composite-token
  advance;
- `ExplicitAssemblyContext_PolicyAdoptionRequiresCapturedVersion` proves both a
  receipt carrying another captured version and a composite outer token that
  advanced before publication fail without publishing;
- `ExplicitAssemblyContext_PolicyAdoptionFailuresAreTypedAndExact` proves each
  correspondence mismatch retains the first exact typed reason above rather
  than collapsing into empty policy or generic failure;
- `ExplicitAssemblyContext_PublishesGroupAndPolicyAtomically` proves observers
  can see neither half alone nor a group/policy pair from different
  generations;
- `ExplicitAssemblyContext_ObservedPolicyDriftRetiresCurrentGeneration` proves
  current-view acquisition and warm and cold operation gates reject after
  observing drift and atomically remove both old current handles;
- `ExplicitAssemblyContext_ReplacementStartsOnlyAfterRetirement` proves a new
  generation cannot enter preparation while the prior group and policy remain
  current, even though already admitted old-generation work may continue
  toward quiescence;
- `ExplicitAssemblyContext_ReplacementPublishesOnlyAfterRetirement`
  independently proves a new generation cannot become current before the prior
  group and policy are retired;
- `ExplicitAssemblyContext_StableAdmittedReplacementEventuallyPublishes`
  proves a started replacement with admitted budget and a stable composite
  token reaches publication under fair execution;
- `ExplicitAssemblyContext_ReplacementFailureRemainsUnavailable` proves a
  budget, composition, correspondence, or version failure in the replacement
  leaves no current generation and schedules no automatic retry;
- `ExplicitAssemblyContext_SealedGroupPolicyCannotRebind` proves no post-build
  path can replace a group's adopted policy or version in place;
- `ExplicitAssemblyContext_AdmissionProjectionRetainsNoContentAuthority`
  proves #5143 projection runs before publication under the exact admission
  authority and returns matching artifact/assembly registration, identity, and
  MVID facts without retaining a path, opener, content reference, or lease;
- `ExplicitAssemblyContext_PlatformRoleRequiresCurrentOwnerEvidence` proves
  only matching current-generation platform realization evidence can mint
  `PlatformAuthorized`; foreign, stale, ended, mismatched, or replayed evidence
  fails without publication;
- `ExplicitAssemblyContext_RequiresOwnerIssuedPlatformClosure` proves only the
  exact current #5139 realization for the requested hive, family, version, and
  implementation layout can contribute members; mismatched or absent proof
  fails without publication;
- `ExplicitAssemblyContext_UsesAdmittedLocalCoordinateIdentity` proves
  #5096-issued canonical identities drive duplicate handling without a second
  path classifier on Windows, Linux, and macOS;
- `ExplicitAssemblyContext_RepeatedAdmittedDesignationHasOneRegistration`
  proves repeated spellings admitted as one canonical coordinate acquire one
  designated registration;
- `ExplicitAssemblyContext_PathCollisionsPreserveRoleSemantics` covers
  root/designated coalescing and distinct designated/platform and root/platform
  registrations;
- `ExplicitAssemblyContext_EqualIdentityDesignationsRemainDistinct` proves two
  distinct designated coordinates with equal assembly identity retain separate
  registrations and ambiguity evidence;
- `ExplicitAssemblyContext_IncoherentPlatformRealizationDoesNotPublish` covers
  a repeated platform coordinate, reused registration, and two distinct
  platform members with one canonical assembly identity through a controlled
  platform-adapter seam;
- `ExplicitAssemblyContext_NonExactInputsCannotAcquireDesignation` covers
  directories, siblings, packages, projects, and dependency manifests;
- `ExplicitAssemblyContext_SelectedImageUsesRetainedArtifactBytes` mutates the
  source after realization and proves selected image access uses the admitted
  immutable bytes;
- `ExplicitAssemblyContext_ViewExposesOnlyBoundedImageAccess` proves the public
  context surface and the transitive public closure of its callback and result
  types return no `ArtifactContentReference`, `ResolvedAssemblyReference`,
  descriptor-bearing `AssemblyImageView`, `ArtifactAuthorization`,
  `ArtifactAdmissionLease`, `ArtifactQueryLease`, opener delegate, readable
  path, or `Stream`, and closes its internal stream before invoking the image
  callback;
- `ExplicitAssemblyContext_RetainedHandoffRejectsForeignAuthority` proves the
  participant-to-image operation rejects a foreign participant, query lease,
  or ended generation;
- `ExplicitAssemblyContext_WorkspaceOwnsContextLifetime` proves disposing a
  context view or dropping the opaque handle does not release its group,
  session, or budget charge, while workspace disposal ends later access and
  joins the ordinary quiescent cleanup;
- `ExplicitAssemblyContext_FailurePublishesNoPartialGroup` covers invalid
  managed images, absent platform versions, role conflicts, artifact and group
  snapshot-budget exhaustion, local success followed by platform failure, and
  platform success followed by local failure;
- `ExplicitAssemblyContext_UnsupportedHostIsTyped` proves a host without the
  two required adapters fails before admission rather than changing source
  kinds.

This realization does not resolve members or call targets and does not define
CLI options, sections, rows, verbosity, or exit status. Those downstream
components consume this owner-issued outcome without extending its authority.

### Acquisition outcomes

An adapter returns a typed outcome, conceptually:

```text
ArtifactAcquisitionOutcome
  = Acquired(artifacts, provenance, lease)
  | Unavailable(diagnostic)
  | Rejected(diagnostic)
  | Failed(diagnostic)
```

The exact type names are an implementation decision. The semantic requirements
are not:

- a required acquisition failure cannot become an empty successful set;
- each declared context member must realize at least one artifact eligible for
  that member's projection; an empty or wholly non-projectable `Acquired`
  result is a typed member failure;
- a context cannot silently omit a failed required member;
- every member in a static workspace context remains required;
- host composition may make an entire acquisition optional before constructing
  a context, but failure never makes a declared context member optional;
- disposal failure cannot replace the primary acquisition or inspection
  failure;
- cancellation propagates as cancellation rather than occupying a failure arm
  or being relabeled as an acquisition diagnostic.

## Source adapters

An artifact source adapter owns the semantics needed to resolve its coordinate.
It returns artifacts and leases; it does not create an assembly session.

### Local adapter

The local adapter accepts explicit file and bounded directory coordinates under
host policy. It opens local content without acquiring package or remote-storage
dependencies. The explicit-file implementation exists; the directory contract
below is target design tracked by
[#4999](https://github.com/richlander/dotnet-inspect/issues/4999).

Before registration, `DotnetInspector.Artifacts.Local` opens an explicit file
once, copies it under a loop-enforced byte limit, and records path, exact copied
length, and last-write observation from that handle as typed local provenance.
The artifact session then copies the adapter-private snapshot into
owner-private retained bytes before publication. The deliberate second copy
keeps adapter memory from becoming owner memory. Both openers are read-only and
do not expose their backing arrays. Rebuild, replacement, symlink retargeting,
or deletion after acquisition cannot substitute new bytes into the published
snapshot.

The ordinary retained snapshot does not compute a digest eagerly. Its
[owner-mediated on-demand digest](#on-demand-retained-content-digests) is a
separate authorized query over those retained bytes.

#### Shared local-path admission

`DotnetInspector.Artifacts.Local` owns one package-free admission contract for
every path coordinate it consumes. The contract is internal to the local
adapter; it does not add filesystem policy to source-neutral artifact
contracts. It has two stages over one classifier:

1. Classification accepts a non-empty requested path and returns a canonical
   path plus the observed final kind, `RegularFile` or `Directory`, or a typed
   path outcome.
2. Admission adds one required kind and rejects a classified target that does
   not match it. Regular-file admission then opens and verifies the content
   handle; directory admission returns the canonical path.

Direct coordinates always admit one required kind:

- `RegularFile` for an explicit-file coordinate or a selected directory entry;
  or
- `Directory` for a bounded-directory root.

There is no "any filesystem entry" admission. Classification exposes only the
two admissible observed kinds; non-regular or unclassifiable entries remain
typed outcomes. Bounded-directory candidate filtering is the only consumer that
uses an observed kind before choosing whether the candidate becomes an admitted
coordinate.

The classifier retains the original non-empty requested path and converts it
once with `Path.GetFullPath`. Successful normalization adds the canonical path
used by provenance and ordinary diagnostics. For ordinary paths, this lexical
normalization makes the coordinate absolute and removes relative path segments,
but it does not resolve links, normalize case, or mint physical file identity.
Windows treats extended `\\?\` paths as already normalized and leaves their
segments unchanged. An extended disk or UNC coordinate is therefore admitted
only when it is fully qualified and contains no `.` or `..` segment; otherwise
it is rejected as invalid. Alternate or mixed-separator device-prefix spellings
such as `//?/` are classified from their raw caller spelling rather than passed
through `Path.GetFullPath`, which would erase their namespace and dot-segment
evidence. Every noncanonical separator spelling of the `\\?\`, `\\.\`, and
`\??\` namespace signatures is invalid rather than being reinterpreted as an
ordinary UNC coordinate. That coordinate rule does not reject a valid extended
link merely because its stored target is relative: the target is resolved
against the link's parent and normalized before final-target classification,
with parent traversal bounded by the drive, share, or volume root and without
rewriting the canonical requested coordinate. Absolute extended targets retain
their raw substitute namespace and remain subject to the final-target syntax
rules without this relative-target normalization. Managed link resolution must
not erase that syntax evidence before classification. Any other non-empty path
that cannot be normalized is also rejected rather than escaping as a platform
exception. Invalid-path results carry the requested path and no canonical path.

All local coordinates follow symbolic links and supported link-like reparse
points to their final target. The same policy preserves existing explicit-file
behavior and supports symlinked build outputs without making directory
acquisition a second policy domain. A dangling link is unavailable. A link
whose final target has the wrong kind is rejected. A reparse entry whose tag
denotes an unsupported link, a non-regular entry, or an unknown meaning is
rejected rather than opened. Recognized non-link data-bearing reparse entries
retain their own expected kind; the `ReparsePoint` attribute alone does not
reject them.

The canonical requested path remains the coordinate after link resolution. The
adapter does not publish a physical target path or require the target to remain
beneath the lexical parent. Consequently, a top-level directory entry that is a
link may resolve outside the requested root and still contribute the regular
file named by that entry. This is coordinate behavior, not a containment
boundary: the caller selected the local location, and local actors are outside
the hostile-input model. Hard links are ordinary regular files and are not
collapsed.

Classification and admission have these semantic results. Exact implementation
type names are deferred, but consumers cannot replace them with
exception-message matching:

| Condition | Result |
| --- | --- |
| The final target is a regular file or directory | Internal classified result carrying the observed kind |
| The path is missing, a link is dangling, or the entry disappears before the file open | `Unavailable` |
| The path cannot be normalized | `Rejected` with an invalid-path reason and no canonical path |
| The final target has the wrong expected kind | `Rejected` with a kind-mismatch reason |
| The final target is a FIFO, socket, block device, character device, Windows device or pipe coordinate, or an unsupported, special, ambiguous, or unknown reparse entry | `Rejected` with a non-regular or unsupported-entry reason |
| Metadata inspection, native classification, or the file open fails for a reason other than absence | `Failed` with an admission-failed reason |
| The current host has no supported classifier | `Failed` with a classification-unsupported reason |
| Cancellation is observed | `OperationCanceledException` |

The shared result carries a typed reason, requested path, and optional canonical
path, not a source-neutral artifact outcome. The explicit-file and directory
operations project it into their existing `LocalArtifactDiagnostic` surface.
The target diagnostic adds a requested path and nullable canonical path;
existing `FullPath` remains a compatibility display projection of
`CanonicalPath ?? RequestedPath`. Provenance is produced only after successful
normalization and always uses the canonical path. Equivalent admission reasons
must retain the same result arm and meaning across direct coordinates;
coordinate-specific context may refine the diagnostic code and summary.
Explicit-file projection preserves `local.file.missing`,
`local.file.read-failed`, and `local.file.size-limit`: path-specific metadata,
open, and read failures continue to use `Failed` with
`local.file.read-failed`. Invalid paths use `Rejected` with
`local.file.invalid-path`; an existing target that is a directory, non-regular
entry, or unsupported reparse entry uses `Rejected` with
`local.file.unsupported-entry`. Unexpected absence of a required platform
classifier uses `Failed` with `local.file.classification-unsupported`, not the
generic read-failure code. The unsupported-entry projection deliberately
changes the current directory-target behavior from
`Failed`/`local.file.read-failed` because the present path is now proven not to
satisfy the explicit regular-file coordinate. Failures after a regular file is
admitted, including bounded snapshot-copy failures, remain read failures owned
by the consuming acquisition rather than being relabeled as path admission.

Admission occurs before any operation known to block on a stable non-regular
entry. Managed attributes are insufficient on Unix: a FIFO, a character device,
and an empty regular file can all appear as normal zero-length files. Supported
non-Windows hosts therefore classify the final target with the .NET runtime's
normalized `SystemNative_Stat` ABI. `stat` follows links and exposes the stable
mode mask needed to distinguish directories, regular files, FIFOs, sockets,
and block or character devices without opening the entry.

Windows admission first rejects device and pipe namespaces and reserved DOS
device coordinates. Extended-length disk and UNC paths are not rejected merely
for using the `\\?\` prefix after their segments satisfy the normalization rule
above. An ordinary drive root such as `C:\` is a supported `Directory`
coordinate; bounded-directory limits still govern its top-level enumeration.
An ordinary regular file directly beneath that root, such as `C:\foo.dll`, is a
supported `RegularFile` coordinate. Neither form is a device coordinate.
Ordinary filesystem paths are inspected component by component through managed
attributes so an ancestor link is classified rather than followed implicitly
by the metadata lookup. A metadata handle opened without following each
reparse point queries its tag. Supported links also read the raw substitute
name and relative flag from that handle. Relative targets are resolved and
normalized under the rule above; absolute targets preserve their namespace for
final-target syntax classification. A stable ancestor or final-component link
cycle consumes the same bounded traversal budget and is rejected as an
unsupported entry. A direct-cycle shortcut compares path spellings ordinally;
it does not case-fold distinct coordinates on a case-sensitive Windows
directory. Raw reparse parsing requires the returned byte count to equal the
common header plus `ReparseDataLength`, an even payload with a zero reserved
field, and aligned substitute-name and print-name ranges contained by the
declared path buffer. A symbolic-link reparse buffer is supported only when its
flags are exactly `0` for an absolute target or `SYMLINK_FLAG_RELATIVE` for a
relative target; reserved flag values are malformed unsupported entries.
Classification is tag-semantic rather than based only on the name-surrogate
bit:

- symbolic-link and mount-point tags are supported links, so their final target
  is resolved and classified;
- tags that can denote a special file or an unsupported link are rejected,
  including AF_UNIX, WSL FIFO/character/block entries, and the entire NFS tag
  unless a later design adds bounded subtype parsing;
- audited non-link data-bearing tags, including cloud-placeholder,
  deduplication, and projection forms, retain their own file or directory
  attributes and proceed to expected-kind and post-open checks; and
- unknown tags are rejected rather than assumed to be data-bearing.

The tag policy is one enumerated classifier whose coverage gate fails when an
implemented known-tag constant lacks a disposition. This step must prove a file
or directory target before a file-content open.

Regular-file admission then opens the canonical requested path exactly once and
returns that owned read-only stream or handle to the consuming acquisition.
Before returning it, the adapter classifies the handle again:

- non-Windows hosts use `SystemNative_FStat` and require regular-file mode; and
- Windows requires `GetFileType` to report `FILE_TYPE_DISK` and handle-based
  attributes not to report a directory.

A `FILE_TYPE_UNKNOWN` result with a nonzero captured last error is an admission
failure, not a kind mismatch. A successful non-disk result remains a rejected
kind mismatch.

A post-open kind mismatch is rejected and the handle is disposed. The explicit
file and future directory-entry copy therefore consume the same verified open
generation; neither reopens the path after admission. Size limits, last-write
observation, copying, provenance construction, and contribution registration
remain with the consuming acquisition.

Directory-root admission performs the same normalization, link policy, and
pre-open expected-kind classification, then returns the canonical requested
path for enumeration. It does not promise handle-relative or transactional
enumeration. If the root changes after classification, ordinary enumeration
failure remains visible through the directory contract below.

This contract prevents a stable non-regular entry from predictably reaching a
blocking content open. It does not make path classification and open atomic. A
local process can replace a regular file with a FIFO after classification and
before open, or replace a directory before enumeration. Defending against that
same-machine mutation would require a native nonblocking or handle-relative
filesystem protocol and is outside the local-actor threat model. Once file
admission returns an open regular-file handle, later link retargeting or path
replacement cannot change the generation that is copied; immutable retained
snapshot lifetime remains owned by
[#4816](https://github.com/richlander/dotnet-inspect/issues/4816).

The implementation remains package-free, source-generated-interop compatible,
and NativeAOT-friendly. `LibraryImport` of the runtime-normalized `Stat` and
`FStat` entry points works on ordinary Unix hosts and Browser/Wasm; Windows uses
managed file APIs plus source-generated `kernel32` interop. No currently
supported target is intentionally excluded. A missing native library or entry
point is a visible classification-unsupported failure, never permission to
open an unclassified path.

This does not inherit the CLI-only physical-identity exception in
[`schema-query.md`](schema-query.md#effective-filtering). That provider's
contract needs stable device/inode identity and intentionally restricts the
hosts on which it deduplicates physical files. Local-path admission consumes
only the normalized mode field and does not mint physical identity. The
portable gate must run the actual `Stat` and `FStat` imports under 32-bit
Browser/Wasm as well as NativeAOT. It must also preserve unavailable outcomes
for missing and not-directory errors and rejected outcomes for symbolic-link
loops under each platform's normalized error values. Browser/Wasm selects only
its WASI-derived values rather than also accepting colliding Linux errno
numbers. If that gate fails on a supported target, the platform design reopens;
returning
classification-unsupported is an operational failure mode, not approval to ship
an unsupported-platform degradation.

The implementation is complete when focused gates prove:

- canonicalization, including extended-path segment rejection, expected-kind
  mismatches, requested-path retention when canonicalization fails,
  final-target link following, relative targets from valid extended links,
  dangling links, name-surrogate handling, special and unknown reparse
  rejection, audited data-bearing reparse treatment, and hard-link
  non-deduplication have the same semantics for every consuming local
  coordinate;
- stable FIFOs, sockets, devices, and their link aliases are rejected before a
  blocking content open, while an empty regular file remains admissible;
- a regular-file consumer receives the once-opened, post-classified handle and
  cannot reopen the coordinate;
- unavailable, rejected, failed, and cancellation results remain distinct; and
- the normalized `Stat` and `FStat` classifier compiles and runs under both
  NativeAOT and Browser/Wasm, preserving missing, not-directory, and link-loop
  outcomes, while Windows gates cover disk files,
  drive-root directories, regular files directly beneath a drive root,
  traversable links, reserved device names, named-pipe coordinates, allowed
  data-bearing reparse tags, every supported special-tag family, and an unknown
  tag.

These properties are represented by
`LocalPathAdmission_ExpectedKindsAndLinksAreShared`,
`LocalPathAdmission_StableNonRegularEntriesRejectBeforeOpen`,
`LocalPathAdmission_ConsumerReceivesTheVerifiedOpenGeneration`,
`LocalPathAdmission_OutcomesAndCancellationRemainDistinct`, and
`LocalPathAdmission_PlatformClassifiersRemainPortable`. The Windows-specific
`LocalPathAdmission_WindowsExtendedRelativeLinkTargetIsNormalized`,
`LocalPathAdmission_WindowsAbsoluteExtendedLinkTargetRetainsSyntaxPolicy`, and
`LocalPathAdmission_WindowsAncestorLinkLoopIsRejected` gates run in Deep
Inspect's `platform-test` lane, together with
`LocalPathAdmission_WindowsCaseDistinctLinkTargetIsNotCycle` and
`LocalPathAdmission_WindowsGetFileTypeFailureIsFailed` and
`LocalPathAdmission_WindowsAlternateDevicePrefixIsInvalid`;
`LocalPathAdmission_WindowsPoliciesAreEnumerated` enforces the closed
symbolic-link flag and namespace-separator matrices, while
`LocalPathAdmission_WindowsReparsePayloadBoundsAreClosed` enforces exact raw
payload sizing and both name ranges. The Browser/Wasm probe also rejects
colliding Linux errno values. The change-detection suite requires a local-path
product change to select the Browser/Wasm lane that runs its executable probe.
Together these gates enforce the shared classifier and explicit-file admission
contract. The bounded-directory
implementation must exercise the same contract through its public root and
selected-entry outcomes rather than adding another classifier.

Admission is sequential and adds no publication, interleaving, or concurrent
ownership state. A new TLA+ model would duplicate the existing artifact-session
model without checking a new state machine. Native classification and
cross-platform executable canaries are the evidence this contract needs.

#### Bounded directory coordinate

A directory coordinate contributes opaque artifacts from one explicit local
root. It does not decide which artifact is an inspection target, decode managed
metadata, resolve dependencies, assign workspace roles, or establish binding
precedence. A directory is an artifact source, not an assembly context.

The target peer API is
`LocalArtifactSource.AcquireDirectoryAsync(scope, path, options,
cancellationToken)`. `LocalDirectoryArtifactAcquisitionOptions` contains:

- `ExcludedFileNames`, an `IReadOnlyCollection<string>` copied before the first
  await;
- `IncludedFileExtensions`, an `IReadOnlyCollection<string>` copied before the
  first await and defaulting to `.dll`;
- `MaxObservedEntries`, defaulting to 1,024 observed top-level entries;
- `MaxSelectedFiles`, defaulting to 1,024 selected files;
- `MaxFileBytes`, defaulting to 512 MiB; and
- `MaxTotalBytes`, defaulting to 512 MiB across selected files.

The limits are standalone adapter ceilings, not workspace reservations, and
the package-free local project does not depend on `ArtifactSetSessionLimits`.
Each limit is positive and each byte limit fits the array-backed snapshot
implementation. A workspace host passes stricter selected-file and byte limits
when required by the supplemental capacity obtained through
[#5010](https://github.com/richlander/dotnet-inspect/issues/5010), then passes
them to this adapter. #5010 owns reservation and session behavior.

Selection is top-level and source-neutral. Each included extension is a
non-empty extension such as `.dll`, not a glob or path; extension matching is
ordinal-ignore-case on every platform. Each exclusion is one non-rooted file
name with no separator or parent traversal and is also matched
ordinal-ignore-case, preventing a lexical case difference from reacquiring an
explicit file on a case-insensitive volume. On a case-sensitive volume that
conservatively excludes case-only aliases; callers needing both acquire them
explicitly. Selection never establishes semantic kind or artifact identity,
and physical aliases are not collapsed. Recursion or richer selection requires
a later contract.

Acquisition follows this order:

1. Validate and copy options before filesystem work.
2. Admit the requested root as a directory through the
   [shared contract](#shared-local-path-admission). Preserve its visible
   unavailable, rejected, failed, and cancellation outcomes.
3. Enumerate top-level entries incrementally. Count every observed entry before
   classifying it and stop with a typed rejection as soon as
   `MaxObservedEntries` is exceeded.
4. Derive one direct-child relative name per observed entry and sort by that
   name with `StringComparer.Ordinal`. Filesystem enumeration order is never
   publication order.
5. Use enumeration attributes only to discard an entry already proven to be a
   non-reparse directory. Apply the extension allow list and exclusions to the
   remaining observed names, producing lexical candidates without deciding
   their final target kind.
6. In sorted order, classify each candidate through the shared contract. Ignore
   a classified directory. For each classified regular file, increment the
   selected-file count, reject the batch if `MaxSelectedFiles` is exceeded,
   then continue shared admission through its verified open handle and consume
   that handle into an adapter-private snapshot while enforcing
   `MaxFileBytes` and the remaining `MaxTotalBytes`. Any `Unavailable`,
   `Rejected`, or `Failed` classification or admission result aborts the atomic
   batch without publishing and preserves that exact outcome arm.
7. Register the complete batch in one contribution scope only after every
   selected snapshot succeeds. Any enumeration, candidate admission,
   selected-entry, limit, registration, or outcome-construction failure
   publishes no contribution from the batch.

Copying stops before retaining a byte that would exceed either bound. The bound
that would be exceeded first determines the rejection; when the same byte would
exceed both, the per-file limit takes precedence. A read failure after admission
is a failed directory acquisition, not a path-admission outcome.

An existing directory with no selected files returns `Acquired` with an empty
artifact list and `ArtifactAcquisitionLeases.None`. That is a successful answer
to an optional source coordinate, not a shortened successful batch. Required
workspace acquisition retains its existing empty-batch failure; #5010 owns the
supplemental path that can compose this result.

Root and selected-entry admission outcomes remain those of the shared contract.
A lexical candidate classified as a directory is used for filtering, not
admitted as a selected coordinate, and emits no path outcome. Every other
candidate classification or admission outcome remains visible. Directory path
diagnostics retain the requested root, its canonical path when available, and
the observed relative candidate name when applicable:

| Condition | Outcome | Diagnostic code |
| --- | --- | --- |
| Root is missing or a root link is dangling | `Unavailable` | `local.directory.root-missing` |
| Root path is invalid | `Rejected` | `local.directory.root-invalid-path` |
| Root has the wrong kind or unsupported entry kind | `Rejected` | `local.directory.root-unsupported` |
| Root classification fails | `Failed` | `local.directory.root-admission-failed` |
| Candidate is missing, dangling, or disappears | `Unavailable` | `local.directory.entry-missing` |
| Candidate is non-regular or unsupported | `Rejected` | `local.directory.entry-unsupported` |
| Candidate classification or open fails | `Failed` | `local.directory.entry-admission-failed` |

Directory-specific enumeration and snapshot-copy diagnostics use the canonical
root and, when applicable, the observed relative name:

| Condition | Outcome | Diagnostic code |
| --- | --- | --- |
| Observed entry count exceeds the limit | `Rejected` | `local.directory.entry-limit` |
| Selected file count exceeds the limit | `Rejected` | `local.directory.selected-file-limit` |
| Selected file exceeds the per-file limit | `Rejected` | `local.directory.file-size-limit` |
| Selected files exceed the aggregate limit | `Rejected` | `local.directory.total-size-limit` |
| Selected file cannot be read after admission | `Failed` | `local.directory.read-failed` |
| Directory enumeration fails | `Failed` | `local.directory.enumeration-failed` |

Cancellation remains `OperationCanceledException` and is never translated into
a diagnostic arm.

Each contribution records a `LocalDirectoryArtifactProvenance`:

- the canonical requested root;
- the direct-child relative name as observed;
- the full observed entry path;
- the exact copied length; and
- the last-write observation from the same open file used to copy the snapshot.

The artifact kind is `local-directory-entry`; the entry name, matched extension,
and bytes do not establish media or semantic kind.

The adapter establishes an immutable batch of the bytes it actually copied,
not a transactional filesystem snapshot. A file created after enumeration is
outside that acquisition. Any candidate classification, admission, or selected
entry failure aborts the whole batch with its defined outcome rather than
shortening it. Shared admission owns the residual local-path race and
classification guarantees; mutation after copying cannot change the retained
snapshot.

The implementation is complete when focused gates prove:

- bounded top-level selection is deterministic and source-neutral;
- plain and linked directory targets are ignored through the shared
  final-target classification, while links to regular files remain selectable;
- empty selection registers nothing, while enumeration failure remains failed
  rather than becoming empty success, and neither it nor entry admission,
  per-file and aggregate overflow with their defined precedence, read failure,
  or registration failure can publish a partial batch; and
- directory provenance, immutable batch snapshots, and cancellation are
  preserved.

These properties are represented by
`LocalDirectoryAcquisition_BoundedDeterministicSelection`,
`LocalDirectoryAcquisition_EmptyOrFailedBatchPublishesNothing`, and
`LocalDirectoryAcquisition_ProvenanceSnapshotAndCancellationArePreserved`.

#### Directory composition scenarios

The adapter contract has the same meaning in each scenario; only host
composition differs:

| Scenario | Local artifact acquisitions | Boundary left to the workspace |
| --- | --- | --- |
| One DLL requested; its directory supplies candidates | Acquire the exact DLL once, then optionally acquire its directory with that top-level name excluded. Zero remaining DLLs is a successful empty candidate batch. | Supplemental workspace acquisition from [#5010](https://github.com/richlander/dotnet-inspect/issues/5010) contributes discovered candidates without making the batch a required context member; the exact-file registration alone may receive caller designation. |
| Several DLLs requested from one directory | Acquire each exact requested file, then optionally acquire the directory once with all of their names excluded. | Optional workspace acquisition contributes remaining candidates; context planning decides whether the requested roots share a group. |
| Several DLLs requested from different directories | Acquire each exact file and optionally acquire at most one bounded candidate batch per explicitly authorized directory. | Optional workspace acquisition contributes candidates; binding policy, not directory order, handles collisions and precedence. |
| Directory configured as a NuGet source | Do not use this adapter unless loose-file acquisition was separately requested. | The package adapter and #3759 own folder-feed identity, package enumeration, and asset selection. |

The same physical directory can be authorized separately as a loose-artifact
source and as a NuGet folder feed. Those coordinates share no provenance,
roles, selection policy, or cache identity.

This sequential adapter design adds no concurrency or publication state
machine. `ArtifactSetSession` retains ownership of admission, cancellation,
materialization, and publication interleavings, so a new TLA+ model would
duplicate that owner without checking a new interaction. Parallel directory
copying, directory-level single-flight, or independent partial publication
would reopen that decision and require a focused interaction model.

After shared local-path admission is implemented, one local-adapter PR adds
this API, options, provenance, directory-specific diagnostics, and
outcome-level gates without changing `AssemblySetResolver`, CLI defaults,
assembly projection, workspace admission, or binding policy. Adoption follows
[#5010](https://github.com/richlander/dotnet-inspect/issues/5010) and may choose
which directories a scenario authorizes; it may not weaken adapter bounds or
reinterpret directory provenance as caller designation.

This adapter is the proof that the abstraction is independently useful. The
package-free fixture composes:

```text
artifact contracts + artifact workspace + local adapter + Metadata
```

Its dependency closure excludes `DotnetInspector.Packages`, `NuGetFetch`,
NuGet protocol libraries, package stores, and package-specific query
implementations. Core Queries still reference package implementation today, so
adding Queries to this local-only closure remains part of workspace-realization
migration.

### Package adapter

The package adapter owns:

- package coordinates and version selection;
- source authorization and source mapping;
- package archive acquisition and admission;
- nuspec and package asset-group semantics;
- TFM/RID asset selection;
- package-specific provenance.

It may internally use a package content lease or package session. That is a
package-layer implementation detail, not the shared workspace currency.

The adapter projects selected package entries into neutral artifacts. Package
identity stays in artifact/workspace provenance and optional package query
results. It does not become a case in a Metadata-owned provenance union that
assembly inspection must understand.

#### Package Root realization

An exact acquired package is a `PackageRootRealization` regardless of whether
compile asset selection succeeds. That host-neutral package-level result
retains:

- exact package id and version;
- the requested target framework and the selector's selected framework, when
  either exists;
- the requested runtime identifier, when one participates in selection;
- the package content producer key and cache origin;
- the complete typed `PackageCompileAssetSelection`, including
  `NoCompileAssets`, `NoMatchingTargetFramework`, `EmptyCompileGroup`, and
  `InvalidImplementationAssets`.

Here, a compile asset is a package assembly selected as a compile-time
reference; [NuGet package structure and asset roles](../nuget-package-structure.md)
describes the relevant package layouts and implementation counterparts.
When no real reference group supplies the selected framework, compile fallback
uses that framework's neutral `lib` assets. RID-specific
`runtimes/<rid>/lib/<tfm>` replacement applies only to the independently
selected implementation role, so one neutral library compile asset may
correspond to a different RID-specific implementation asset.

The related identity concepts have distinct jobs:

| Concept | Meaning |
| --- | --- |
| `PackageRootRealization` | The in-process package-level selection outcome over already-acquired content. It remains valid for Root-only and unsuccessful selection outcomes and is not by itself a cache or admission identity. |
| `RealizedMemberCoordinate.Package` | The canonical, portable request that repeats the same package, version, producer, and acquisition target. Unlike a possibly floating `WorkspaceMemberCoordinate`, every identity field has already been resolved. It does not promise the same bytes forever. |
| `ProducerKey` | The opaque, credential-free identity of the content producer. The acquired content and payload carry this value, and the realized coordinate records the same value as `Producer`. It distinguishes sources but not successive byte generations from one source. |
| `PackageContentGenerationIdentity` | The process-local identity of one retained immutable package-content snapshot. Cache handles over that retained snapshot may share the identity; a replacement snapshot receives a new identity. |
| `PackageRootSelectionIdentity` | The process-local identity of one frozen package-selection occurrence. |
| `PackageRootBinding` | The acquisition-issued value that joins one Root, realized coordinate, content-snapshot identity, and frozen selection and proves their exact physical correspondence. |

Acquisition issues a `PackageRootBinding` from one
`AcquiredPackagePayload` or `AcquiredPackageSourcePayload`. The immutable
binding carries the exact `PackageRootRealization`, its authoritative
`RealizedMemberCoordinate.Package`, a
`PackageContentGenerationIdentity`, and a
`PackageRootSelectionIdentity`. The factory validates that the retained
content and acquisition result name the same producer before selection, then
creates the Root, snapshots every selection sequence into read-only storage,
and mints the coordinate and both identities without repeating coordinate
resolution, content acquisition, or compile asset selection.
The acquired payload result has an internal constructor and get-only
properties, so ordinary consumers cannot forge a coordinate/content pairing
or replace either half after acquisition issues it.

The content-generation identity is an opaque, credential-free reference token
for one retained immutable package-content snapshot, owned by
`IPackageContent`. Every binding over the same content handle shares it. A
store may preserve that token across handles to one retained cache snapshot;
the Browser and in-memory stores do so. A replacement snapshot must receive a
new token, even under the same package/version/producer slot.
Implementations without a retained-generation registry conservatively receive
one token per content handle. Equal identities therefore guarantee the same
retained immutable snapshot for the binding lifetime, while unequal identities
make no claim about byte inequality. The token is deliberately process-local
and is never serialized or reconstructed from coordinate display fields.

The selection identity is also an opaque reference token, minted once for one
binding after the typed selection has been frozen. Equal identities guarantee
the same selection status, selected target framework, default asset, and
ordered available-framework, surface, candidate, and implementation
sequences. Independently repeated equal selections may receive different
identities. This conservative equality is sufficient for exact-request
admission: sharing requires the same retained binding, never a display-field
reconstruction.

`RealizedMemberCoordinate.Package` is the portable producer-bound acquisition
request, not immutable-byte identity. Reacquiring it may observe a later
payload generation published under the same package/version/producer slot.
The generation token is the authoritative immutable-content proof inside one
adopting process. For the typed source path, the binding derives package id and
version from `PackageSourceCoordinate`, producer from the acquired payload,
and the effective acquisition framework from the requested target only when
the shared target grammar can represent it; otherwise the framework is absent.
Absence denotes framework-neutral source acquisition and is distinct from the
real NuGet target `any`. The binding never derives the coordinate from a
package-supplied asset folder. The original selection target and typed outcome
remain on the Root and selection identity. The optional runtime identifier is
carried exactly and must already be canonical. The resolved multi-source path
uses its already normalized acquisition framework and runtime identifier even
when a caller requests a different framework for compile asset selection. A
coordinate that cannot pass the existing canonical
`RealizedMemberCoordinate.Package` grammar fails construction visibly.

The descriptive `PackageRootRealization` constructor remains a compatibility
surface for callers that already own retained content, but it does not issue a
binding and is not admissible as exact-request cache identity. The Browser
adapter is the first adopting path: it retains the acquisition result, asks it
for a binding, and carries the issued coordinate and identities. Its legacy
test-only package constructor remains unbound. This adoption is gated by
`BrowserPackageRealization_ReceivesAcquisitionIssuedCoordinate`; generation
replacement, selection difference, coordinate coherence, Root-only binding,
and producer mismatch are gated by
`PackageContentGenerationIdentity_ExternalBuffersCannotMutateGeneration`,
`PackageRootGenerationIdentity_ReplacementChangesIdentity`,
`PackageRootSelectionIdentity_DifferentAssetsChangeIdentity`,
`PackageRootSelectionIdentity_SelectionSequencesAreImmutable`,
`RealizedPackageCoordinate_ReacquisitionContractIsCoherent`,
`PackageRootBinding_RootOnlyOutcomeRemainsValid`, and
`PackageRootBinding_RejectsProducerMismatch`. Construction control,
framework-neutral source binding, exclusion of package-controlled framework
folders, and resolved framework/RID correspondence are gated by
`PackageRootBinding_AcquiredPayloadsAreConstructionControlled`,
`PackageRootBinding_UnrequestedFrameworkDoesNotUsePackageFolderAsCoordinate`,
`PackageRootBinding_UnrepresentableSelectionTargetUsesFrameworkNeutralCoordinate`,
`PackageRootBinding_ResolvedCoordinatePreservesAcquisitionTargetAndRuntime`,
and `PackageRootBinding_SourceRuntimeRequiresFramework`.
Neutral-library compile fallback and RID-specific implementation
correspondence are gated by
`RidSpecificImplementation_DoesNotReplaceLibraryCompileFallback` and
`RidSpecificImplementation_UsesSeparateNeutralCompileRole`; Browser adoption
is gated by `RidSpecificPackage_SeparatesCompileAndImplementationAssets`.

#### Sparse selected-assembly projection

[#5798](https://github.com/richlander/dotnet-inspect/issues/5798) owns the
package-adapter projection used by bounded Package Query assembly evaluation.
Given one acquisition-issued `PackageRootBinding`, one exact canonical
`PackageCompileAsset` occurrence from that binding's frozen selection, an
asynchronous candidate workspace, and explicit entry and aggregate
retained-image bounds, the adapter projects only that asset into one
artifact-backed participant.

The caller owns why it selected the asset. The adapter does not choose a
primary assembly, interpret compile-surface versus implementation-body intent,
count siblings, or map package selection states into query-level
`NotApplicable` or item-failure outcomes.

`PackageCompileAsset` is publicly constructible, so value equality is not
selection authority. The sparse projection accepts only the exact canonical
object retained in the binding's frozen `Assets` or `ImplementationAssets`
sequence. A newly constructed equal value, an asset from another binding, a
same-ID value with different fields, or a candidate sequence member that is not
in either selected sequence is rejected before content access. After
admission, the adapter uses only fields from the canonical retained object.
Canonical implementation assets remain admissible when the Root has an
explicit empty compile group: this projection validates occurrence, not the
caller's reason for selecting it.

The canonical selected-asset occurrence authorizes opening its recorded
package path. The path string alone carries no authority and is never accepted
with a package ID as a substitute for the binding and canonical occurrence.

For an admitted occurrence, the adapter:

1. retains the binding's coordinate, content-generation identity, selection
   identity, and canonical asset as package provenance;
2. registers only that selected entry in one `ArtifactSetSession`;
3. materializes it under both the declared and observed entry-byte limits;
4. seals the generation all-or-nothing;
5. invokes the assembly-inspection owner's existing artifact-backed
   compatibility projection with a deterministic package-adapter rejection
   carrier identity to create exactly one participant; and
6. transfers the artifact session and query lease to that participant's
   candidate workspace.

The rejection carrier gives the Metadata bridge the nonblank identity required
to preserve a participant when the selected image has no decoded assembly
identity. It is deterministic for this one-asset projection and is not
presented as artifact-derived identity. The adapter does not classify PE or
metadata kinds or reinterpret Metadata failures. Native images, managed
modules, malformed managed images, empty-MVID assemblies, and unsupported
Windows Metadata retain the assembly-inspection owner's participant and
rejection semantics unchanged.

The aggregate retained-image bound covers the artifact-owned snapshot and the
independent Metadata workspace snapshot. The sparse adapter uses the current
artifact-backed partition: half of the aggregate bound is reserved for the
artifact generation and the remainder for the one-participant group. The
selected entry is therefore limited to the smaller of the explicit per-entry
bound and the artifact share. Declared entry length is only a preflight;
observed copying remains bounded and rejects an entry whose actual expanded
bytes exceed the limit. For an image of `N` bytes with no stricter per-entry
limit, an aggregate retained-image bound of `2N` admits projection while
`2N - 1` rejects before participant publication.

After caller input validation, the package-owned projection outcome is closed:

- **Available** carries one operation-scoped sparse realization containing the
  canonical selected asset, exact one-participant group and participant, and
  the Metadata bridge's `IdentityDecoded` signal. The group is required query
  authority; the signal prevents a consumer from treating a rejection-carrier
  identity as decoded assembly evidence.
- **InvalidBinding** means the Root no longer corresponds to the binding's
  content-generation identity.
- **InvalidSelectedAsset** means the supplied object is not an exact canonical
  member of the binding's frozen selected sequences.
- **SelectedEntryUnavailable** combines a missing entry with a package-content
  implementation that returns `false` from bounded open because the current
  `IPackageContent` boundary cannot distinguish those cases.
- **EntryByteLimitExceeded** means an owner-recognized declared-length
  preflight or observed artifact copy crossed the admitted entry or
  artifact-share byte limit.
- **ArtifactPublicationFailed** preserves the artifact owner's typed
  publication failures.

Null inputs, an invalid bound, or a workspace that is not asynchronous are
caller contract violations and retain their existing argument or invalid-
operation exceptions outside this outcome algebra.

The adapter recognizes its own internal selected-entry-unavailable sentinel
when `TryOpenEntry` returns `false` inside the one materialization callback.
That preserves one package-entry open attempt while distinguishing
`SelectedEntryUnavailable` from unrelated artifact publication failure.
Manifest preflight and the artifact failure code
`artifact.session.artifact-byte-limit` map to `EntryByteLimitExceeded`; other
owner-issued publication failures remain `ArtifactPublicationFailed`.
Product filesystem package content must implement
`IPackageContentEntryManifest` so its known file length reaches the typed
preflight instead of throwing an indistinguishable `InvalidDataException`
inside materialization. For a third-party content implementation without a
manifest, bounded-open `false` remains `SelectedEntryUnavailable` and other
open exceptions remain publication failures.

Cancellation is not an outcome arm. The operation propagates the caller's
`OperationCanceledException` and token after cleanup. Cleanup failures remain
secondary diagnostics and do not replace the primary failure or cancellation.
Unexpected implementation exceptions remain exceptional rather than becoming
success-shaped or generic typed outcomes.

The current artifact materializer's `using`-declaration path can let a throwing
stream disposal replace a cancellation raised by `ReadAsync`. #5798 must first
close that owner-local gap by capturing the materialization failure, disposing
separately, attaching disposal failure as cleanup evidence, and rethrowing the
original condition. The cancellation-preservation claim remains unverified
until the named sparse cleanup gate exercises a stream whose read is cancelled
and whose disposal throws.

An available projection is operation-scoped and resource-bearing. It supplies
the canonical selected asset, group, participant, and `IdentityDecoded` signal
only. Query roles, selection rationale, sibling accounting, and durable
evidence remain consumer-owned.

Artifact registration and participant remain execution authority, not durable
query evidence. A consumer may copy the package coordinate, content-generation
identity, selection identity, and selected asset into a resource-free receipt,
but it cannot retain the workspace, artifact identity or registration,
group, participant, content opener, stream, session, lease, or callback.

Workspace close is the candidate release boundary. Disposing a realization
alone does not release its transferred artifact session, so a streaming caller
uses one candidate-scoped asynchronous workspace and closes it after all query
callbacks are quiescent. Reusing one workspace across a corpus would retain
prior candidate artifact sessions and is outside this sparse contract.

Cancellation before publication produces no participant. Cancellation or
failure after registration but before ownership transfer cleans up the
artifact session and query lease without replacing the primary condition.
Every non-available outcome publishes no participant.

This linear projection adds no concurrency or scheduling state machine.
Candidate parallelism and aggregate cross-candidate memory belong to the
consuming stream owner. CLI and Browser/Wasm consumers use the same pathless,
SRM-only projection under the repository's existing platform and dependency
constraints. This focused design introduces no new platform exception or
independent composition-absence claim.

NuGet Insights demonstrates the useful portion of this shape: copy one package
entry into a seekable candidate buffer, construct an SRM reader, and dispose
the candidate in `finally`. Its full-package download, all-DLL scan,
accumulated output, and server temp-file policy do not transfer.
([driver](https://github.com/NuGet/Insights/blob/c449aa472b10aea098bf46e94767f9952fd16a60/src/Worker.Logic/Drivers/PackageAssemblyToCsv/PackageAssemblyToCsvDriver.cs#L73-L245))
SRM independently requires readable seekable input and makes reader ownership
explicit.
([`PEReader`](https://github.com/dotnet/runtime/blob/bdec678032fd579854e525c5c309eac1c1dd22c8/src/libraries/System.Reflection.Metadata/src/System/Reflection/PortableExecutable/PEReader.cs#L91-L128))
NuGet Insights' tests also show that declared stream length may be missing or
wrong, supporting the separate observed-byte gate rather than trusting ZIP
metadata as the limit.
([tests](https://github.com/NuGet/Insights/blob/c449aa472b10aea098bf46e94767f9952fd16a60/test/Logic.Test/TempStream/TempStreamWriterTest.cs#L33-L120))

The target Release gates are:

- `SparsePackageAssemblyProjection_RejectsReconstructedOrForeignAsset`
- `SparsePackageAssemblyProjection_UsesOnlyCanonicalAssetFields`
- `SparsePackageAssemblyProjection_OpensSelectedPackageEntryExactlyOnce`
- `SparsePackageAssemblyProjection_DoesNotEnumerateOrOpenSiblingEntriesAfterBinding`
- `SparsePackageAssemblyProjection_ExactAggregatePartitionBoundary`
- `SparsePackageAssemblyProjection_FileSystemLengthUsesManifestPreflight`
- `SparsePackageAssemblyProjection_DeclaredOrObservedBytesMapToEntryLimit`
- `SparsePackageAssemblyProjection_CompatibilityCasesUseMetadataOutcome`
- `SparsePackageAssemblyProjection_RejectionCarrierIsDeterministicAndNotDecoded`
- `SparsePackageAssemblyProjection_EmptyCompileGroupImplementationCanProject`
- `SparsePackageAssemblyProjection_PublishesOneExactParticipantOrNone`
- `SparsePackageAssemblyProjection_PreservesBindingCorrespondence`
- `SparsePackageAssemblyProjection_CancellationDuringMaterializationPublishesNone`
- `SparsePackageAssemblyProjection_CloseWaitsForActiveQueryCallback`
- `SparsePackageAssemblyProjection_RealizationDisposeRetainsArtifactUntilWorkspaceClose`
- `SparsePackageAssemblyProjection_TerminalPathsReleaseLeaseAndSession`
- `SparsePackageAssemblyProjection_CleanupFailurePreservesPrimaryCondition`
- `SparsePackageAssemblyProjection_BrowserConsumerExecutesQueryThroughGroup`

The Package Query evaluator tracked by
[#5785](https://github.com/richlander/dotnet-inspect/issues/5785) is the first
named consumer. It owns pattern semantics, semantic work bounds, candidate
role selection, selection-state mapping, sibling accounting, outcomes, and
resource-free evidence; this adapter does not inspect metadata or IL and does
not publish host events.

Compile-library availability is a capability of that Root, not a precondition
for the Root to exist. The host workspace retains every requested Root.
`PackageAssemblyContextRealization` separately creates surface or
implementation assembly-context groups only for Roots whose selection status is
`Selected` and whose selected asset set is non-empty. It does not become a
package-root container. A workspace containing only Root-capable coordinates
has no assembly groups. A mixed workspace retains all Roots at the host
boundary while creating groups for selected coordinates only.

`InspectionWorkspace.RealizePackageAssemblyContextRolesAsync` is the
artifact-backed realization for one acquisition-issued `PackageRootBinding`.
It requires an asynchronous workspace and uses the binding's package
coordinate, content-generation identity, and selection identity as the exact
join currency. The complete distinct union of selected surface and
implementation assets enters one `ArtifactSetSession`; an asset selected into
both roles contributes only once. The existing
`MaxAggregateRetainedImageBytes` option is the one caller-supplied retained-byte
limit for the whole realization. The artifact generation receives half; the
resulting role groups receive the remainder. A distinct surface and
implementation group divide the role-group share again. This partition bounds
the source snapshots retained by the artifact session plus the independent
snapshots retained by Metadata groups rather than applying the same limit to
both copies.

Publication is all-or-nothing. Every selected asset must materialize within the
per-entry and aggregate limits before a role group is created. Each distinct
artifact receives Metadata's
[admission-scoped assembly projection](assembly-inspection-query.md#admission-scoped-artifact-projection)
before publication; those facts remain provisional until publication succeeds.
A published valid assembly retains its artifact registration and consumes the
projected identity and non-empty MVID without reopening content to decode them.
An artifact shared by distinct roles reuses those materialized facts.
A selected malformed, native, module, or empty-MVID asset
remains a participant through the compatibility rejection carrier defined by
the assembly-inspection-query owner; identities unsuitable for a compatibility
descriptor also retain that route. This includes preservation of partially
decoded identity rather than substituting a fallback name for every
non-projectable image. The artifact session and its query lease
transfer to the exact distinct role groups, and workspace close releases them
only after those groups report quiescence. Failure before transfer attempts
group, query-lease, and artifact-session cleanup without replacing the primary
failure. Disposing the returned role realization releases its groups but not
the artifact session; the asynchronous workspace remains the session owner
until close. Callers serialize this realization with other workspace group
admissions because exact ownership transfer cannot be evaluated while a group
admission is incomplete.

`ArtifactBackedPackageRealization_PreservesMixedParticipantsAndExactLifetime`
gates one valid and one malformed selected asset, one source entry open per
distinct asset, exact package binding identities in artifact provenance,
visible available/rejected query outcomes, and artifact release after an
active group operation completes.
`ArtifactBackedPackageRealization_ReusesAdmissionFactsAcrossRoles` gates reuse
of the admitted identity and MVID when one artifact participates in distinct
surface and implementation groups.
`ArtifactBackedPackageRealization_RejectsAggregateBudgetWithoutPartialGroup`
gates aggregate retained-byte rejection and absence of a partial group.
The synchronous stream-backed realization remains available for current
callers. The CLI and browser/Wasm adopters described below consume this shared
admission path; their host adoption is tracked in
[#5577](https://github.com/richlander/dotnet-inspect/issues/5577).
Admission adoption adds no host retention, cache, eviction, or presentation
behavior and leaves group snapshot acquisition and query revalidation on the
existing compatibility path.

The CLI adoption is the remote `package --all-libraries` grouped
Integrations path when the command resolves one default or explicit target framework and
the binding's frozen surface role exactly covers the command's visible library
selection.
After the existing desktop extraction resolves the exact package and version,
the CLI consumes its retained acquisition-issued payload when available.
Authority-backed pinned extraction carries that admitted payload through
caller-owned extraction cleanup; it must not be reacquired by treating producer
identity as source authority. Older extraction results without a configured
authority reacquire the same immutable payload through the authorized
`FileSystemPackageStore`. The CLI creates the `PackageRootBinding` from the
legacy acquisition result for compile-role realization. Configured-authority
payloads retain their actual producer and use explicit inspection selection
below, rather than being relabeled as legacy content-cache coordinates.
Both paths realize their input in an asynchronous `InspectionWorkspace`.
`PackageCommand_GroupedIntegrationsUseRetainedAuthorizedPayload` gates the
configured HTTP and local-source handoff through the real command, including
one HTTP payload acquisition and no local-source HTTP transport.
The host maps the existing
surface-library selection to its exact body-bearing implementation participant
when correspondence exists. The selected surface descriptor remains the input
to ordinary library inspection while only the Integration query runs against
the implementation participant; this prevents implementation-only metadata
from being presented as part of the compile surface. The host consumes those
typed Integration results through the existing library section pipeline and
preserves the selected extraction file's timestamp for ordinary presentation;
that timestamp remains a host presentation fact rather than artifact identity.
The host awaits workspace close so artifact cleanup follows exact group
settlement. It does not mint an artifact registration or infer correspondence
from assembly display text.

`ArtifactBackedCreate_RetainsArtifactUntilActiveQueryCompletes` gates
distinct surface and implementation descriptors at the CLI adapter,
implementation-query lifetime across a racing close, and rejection of access
after terminal settlement.
`ArtifactBackedImplementationRejection_PreservesSurfaceWithoutPathFallback`
gates a valid selected surface beside a malformed implementation carrier:
ordinary inspection still receives the surface while the typed implementation
failure remains visible. Existing package command gates continue to own Markout
output compatibility.

#### Explicit package inspection selection

The #6035 prerequisite and #5840 retirement use a second, distinct Package
adapter selection: the exact entries selected for inspection from one retained
authorized package input. Inspection selection is not compile-role selection.
It can include tools, nested assets, multiple frameworks, legacy framework
spellings, or implementation entries beside an `EmptyCompileGroup`. None of
these inputs changes the compile selector's outcome or issues a
`PackageCompileAsset` occurrence.

`PackageInspectionInput` retains the actual `IPackageContent`, producer, and
generation identity. Remote construction consumes an acquisition-issued
source payload or `PackageRootBinding`, preserving exact producer/content
correspondence. A source payload retains its owner-issued source coordinate;
it does not need or invent a portable Root coordinate. In particular, a
configured-authority producer is not relabeled to fit the older content-cache
producer grammar. Artifact provenance carries the issued source coordinate and
actual producer alongside the generation and frozen inspection selection.
Local construction consumes explicitly supplied package content and optional
nuspec identity. A valid nuspec ID and normalized version provide descriptive
package provenance; missing or invalid identity provides local-archive
provenance. Neither archive filenames nor display fallbacks create a canonical
NuGet coordinate. Local inspection input does not issue a `PackageRootBinding`.

The input freezes the ordered inspection selections, including each exact
package-relative entry path, original framework spelling, and binding-context
key. Entry names must be present in that content's manifest; a missing selected
entry remains a visible unavailable outcome, not permission to reopen another
source. Framework and binding-context strings are selection facts, never
acquisition coordinates. Binding universes group the context key, falling back
to the framework, case-insensitively, without merging different asset
directories. The host supplies its existing binding policy over the projected
descriptors; filesystem resolution remains CLI-owned. A pathless host can use
the same realizer with in-group reference binding.

Compile-role and explicit-selection realization share artifact registration,
bounded materialization, admission-scoped Metadata projection, and compatibility
descriptor construction. Explicit inspection uses one publication attempt per
selected entry so an unreadable or over-limit entry cannot erase successful
neighbors. Each successful publication transfers to its exact dependent group.
No group or source-path substitute is created for a failed publication.
For a non-projectable image, the adapter consumes Metadata's existing
descriptor-selection compatibility result over the published snapshot.
Descriptor-less images remain a distinct no-assembly result, carrying the
typed projection outcome; descriptor-selection exceptions remain unavailable
preflight outcomes. An image
that supplies a descriptor but fails group admission retains the existing
typed participant rejection. The ordered result therefore contains the exact
participant, no-assembly result, or unavailable reason for every selection.

A no-assembly result is outside grouped Integration realization, not a rejected
Integration participant. The CLI preserves its existing ordinary file
inspection for these entries, including native-image metadata and managed
metadata without a usable assembly name. It supplies no grouped Integration
evidence and does not invent an assembly identity. This ordinary reader remains
an extraction consumer outside #5840's retirement; this slice does not claim
artifact-backed ordinary inspection for descriptor-less images.
`PackageCommand_AllLibraries_BlankAssemblyNameSuppressesOpportunities` and the
native-image case of
`PackageCommand_LocalInspectionSelectionPreservesSupportedShapes` gate that
boundary. Unavailable entries and rejected Integration participants never take
this ordinary-inspection route.

The aggregate retained-byte limit is divided between artifact snapshots and
group images. Successful artifacts consume the artifact share in selection
order; a rejected entry consumes no retained capacity. Each group's image
budget covers its successful entries' exact retained lengths, and a caller may
impose a smaller per-group limit. Entry reads also consume the existing
bounded-entry contract; a package-content `InvalidDataException` is an
entry-level unavailable result, not a change to Artifact failure classification.
These are Package adapter
admission choices, not new Artifact publication or group-budget contracts.
Disposing the realization settles every group independently; workspace close
retains each artifact session until its dependent group is quiescent.
Cancellation and unexpected failures propagate after cleanup, preserving
secondary release failures.

The CLI keeps compatible compile-role behavior for existing Root bindings:
ordinary inspection uses
the selected surface, and Integration queries use its exact implementation
participant. A non-exact surface selection or identity-correspondence mismatch
instead realizes the original inspection selection through the same artifact
machinery, not the legacy path-opening workspace. Acquisition failure and a
rejected implementation never trigger source reopening. All-enabled Integration
production, typed evidence/opportunities, Markout rendering, and extraction
consumers outside this workspace remain unchanged.

The shared implementation is pathless and sequential for CLI and Browser/Wasm.
The CLI is this slice's explicit-selection consumer; Browser compile-role
production consumes the extracted common machinery without UI changes. Both
adoptions belong to #5577, with #6035 supplying the prerequisite for #5840.
Release gates are `PackageInspectionAssemblyContextTests`,
`PackageIntegrationsWorkspaceTests`, the artifact-backed cases in
`PackageAssemblyContextRealizationTests`, and the existing Browser package
artifact-scope and compile-role cases in `BrowserEngineBoundaryTests`.

`PackageCommand_ExplicitTfmPreservesSelectionAndUsesCompatibleArtifactRoles`
gates the real CLI command over a source-scoped cached package, including
default, explicit, legacy framework spellings, all-framework, and mixed
surface/implementation selections.
The verbose artifact-backed route message is emitted only after successful
shared realization, so the gate covers actual adoption rather than eligibility
alone. #5917 supplied the bounded compile-role expansion; #5840 preserves that
command selection and the descriptor-less ordinary inspection boundary above.

A host may project Root-owned facts such as exact identity, package documents,
or manifest dependencies from a Root-only coordinate. Compile-role
operations must report the retained compile-library outcome as unavailable or
failed. They must not invent an assembly participant, reinterpret an absent
group as an empty API surface, or route package-root access through an
acquisition-only assembly set. A selected assembly that fails metadata decoding
remains a distinct visible participant failure.

Browser workspace registry identity frames every package, version, and
framework component with its length before composing a multi-package key.
Caller-controlled framework text therefore remains data inside one coordinate
and cannot create or remove coordinate boundaries. Manifest dependency groups
with a missing or blank framework project as NuGet's framework-neutral `any`;
nonblank framework text that the Browser cannot represent still fails visibly
rather than being emitted or silently dropped.

This contract does not choose the initial UI subject or define package-view
presentation. Inspection Subject Navigation owns subject availability and
initial subject recommendation; host presentation consumes those decisions.

The adapter also validates package coordinate, version, selected asset path,
producer, and content identity before minting a package realization and
acquisition registration. Package-aware graph and dependency queries move to an
optional companion and consume that proof. The shared graph document may retain
its serialized `package` subject kind as a full-host contract; core assembly
queries do not construct package subjects or parse package provenance.

`DotnetInspector.PackageQueries` is that optional package-aware query companion.
Its `PackageWorkspaceIntegrationsQuery` consumes the current package-role
realization proof and the package-neutral `AssemblyContextIntegrationsQuery`.
It scans implementation assets in their product role order, then scans only
surface assets without an implementation correspondence. Results retain
immutable package and asset identity beside each typed participant outcome
without exposing package content or merging the role groups.
`PackageAssemblyContextRealizationTests.PackageWorkspaceIntegrationsQuery_UsesImplementationRoleAndReferenceFallback`
gates role selection, package/asset provenance, ordering, and reference-only
fallback.
`PackageAssemblyContextRealizationTests.PackageWorkspaceIntegrationsQuery_SharedRoleDoesNotDuplicateLibraries`
gates the shared-role case. Moving the existing package realization itself out
of core Queries remains part of the broader workspace-realization migration,
not this query-adapter slice.

### Project adapter

The project adapter interprets restore outputs and project build products. A
restored package asset may retain package provenance, but the adapter—not the
assembly layer—understands `project.assets.json`, package roots, and restore
layout.

### Platform adapter

The platform adapter resolves installed or remotely acquired platform content.
Platform packs may happen to be transported as NuGet packages, but transport
does not make "package" the workspace model. An installed-platform adapter is
package-free. A NuGet-backed remote-platform implementation may instead live
with the optional package acquisition implementation so that it reuses package
source mapping, producer authorization, version selection, and payload-cache
rules rather than duplicating them. It returns a neutral validated platform
realization; the platform graph projection and core assembly path do not
reference its package transport.

It validates platform family, version, selected assembly, producer, and content
identity before minting a platform realization and generation-scoped
correspondence proof. Platform-aware graph projection consumes that proof
without parsing NuGet versions or Metadata-owned platform provenance in core
assembly Queries. The realization records evidence; it does not grant
core-library trust. Workspace admission assigns any platform-trust role under
explicit host policy after validating that evidence.

### Embedded adapter

The embedded adapter resolves bundle-relative content from an explicitly
authorized inspection bundle. It must preserve the bundle content digest and
declared logical identity without turning a bundle into a pseudo-package.

## Remote CI build artifacts

Remote build artifacts are a required architecture scenario because they
exercise the boundary without any package semantics.

An Azure DevOps or GitHub Actions adapter could resolve a coordinate such as:

```text
provider
repository or project
immutable run or build id
artifact name
optional entry selection
```

The adapter would:

1. use explicit host-supplied network and credential capabilities;
2. query the provider for the exact immutable run or build;
3. retain repository, commit, PR, workflow or pipeline, job, artifact name,
   provider artifact id, and digest as provenance;
4. acquire the archive during the authorized admission operation;
5. apply archive traversal, entry-count, expanded-size, content-identity, and
   workspace-reservation limits;
6. materialize every selected entry as immutable retained logical content;
7. contribute the validated neutral-artifact descriptors and content leases
   before sealing;
8. dispose the download/archive lease after materialization and before
   publication, while retaining materialized-entry leases until the owning
   artifact session's dependent groups are quiescent.

Later queries reauthorize that provider, repository/project, run/build, and
artifact coordinate before receiving a query access lease. Retaining
materialized content does not preserve a credential grant after the host
removes it.

The workspace could then compare:

```text
Baseline context
  GitHub Actions run for base commit
  platform reference artifacts

Candidate context
  Azure DevOps build for PR commit
  selected local dependency override
  the same platform reference artifacts
```

Queries and assembly sessions see artifact handles, assembly identities, and
workspace provenance. They do not know which provider supplied the bytes, how
its API authenticates, or whether the content arrived in a zip archive.

This scenario establishes several design tests:

- one workspace can own acquisitions from different providers;
- contexts can compose artifacts from different sources;
- provenance survives comparison without becoming assembly policy;
- archive storage is not package storage;
- source authorization stays with acquisition;
- stable coordinates use immutable run/build ids and digests, not moving branch
  names.

No CI adapter is required in the first implementation. The scenario is an
acceptance test for the abstractions that precede it.

## Storage boundary

Storage owns opaque content retention. It may provide filesystem, memory,
browser, remote-cache, or content-addressed implementations.

An owner-authorized access lease may expose a repeatable stream opener, a
bounded buffer, or a lease-scoped path to retained content when the storage
implementation has one. A catalog or target descriptor cannot expose those
routes, and consumers cannot require the path form. A leased path is content
transport, not evidence of caller designation or another workspace role.
Storage does not:

- decide whether content is a package or assembly;
- parse a nuspec or PE header;
- select package assets;
- assign workspace roles;
- authorize a producer for a package coordinate.

Authorization and storage eligibility remain separate. A cache hit is usable
only when the current source adapter proves that the request authorizes the
content and its producer.

Package stores may implement package-specific admission and entry access above
the generic storage boundary. Their interfaces must not leak into the
source-neutral artifact or assembly layers.

Portable-PDB and authored-source storage also need neutral ownership.
`IPdbStore`, source authorization, and package symbol-source options currently
live in the package project and appear in a core assembly query. The target
extracts a neutral symbol-content store and source-access capability below core
Queries. NuGet symbol lookup and package-source authorization remain in an
optional package/source companion that adapts to those contracts.

## Assembly boundary

The assembly layer begins when a consumer asks whether an artifact is a managed
assembly. It decodes managed metadata, mints assembly identity, and opens
`AssemblyInspectionSession`.

It accepts neutral content:

```text
artifact identity
acquisition registration
guarded OpenRead(admission or query access lease)
optional lease-scoped path to retained snapshot
```

It does not accept:

- package ids or versions;
- NuGet source options;
- package roots or entry paths;
- storage cache implementations;
- project restore models;
- CI provider clients.

Package, project, platform, and CI provenance may remain available beside the
assembly participant in the workspace. Metadata does not define or pattern
match those source-specific provenance variants.

## Workspace and query boundary

### Resource-free Root scope projection

Artifact Acquisition issues immutable point-in-time projections for logical
Roots that have entered its admitted Workspace composition:

```text
ArtifactRootScopeProjection
  Correspondence          ArtifactRootCorrespondence
  Status                  ArtifactRootRealizationStatus

ArtifactRootRealizationStatus
  = Ready(ArtifactRootGenerationReference)
  | Pending(resource-free evidence)
  | Failed(resource-free evidence)
```

`ArtifactRootCorrespondence` is opaque, process-local, and credential-free.
Equality proves that this owner classifies two admitted or replacement
realizations as the same logical Root request. For the package arm,
construction consumes the binding's exact `RealizedMemberCoordinate.Package`
and typed selection-target facts. For a non-package arm, construction consumes
that adapter's exact owner-issued Root coordinate. Display text, paths,
filenames, assembly names, row indexes, and cache keys cannot construct or
compare correspondence.

Correspondence deliberately excludes physical generation. Reacquiring the same
logical package Root from replacement content retains correspondence when the
resolved coordinate and selection target still correspond. A different
package version, producer, target, runtime, or non-package owner coordinate
receives different correspondence. This owner also answers exact
request-to-correspondence matching for a fully resolved request without opening
content or entering an artifact generation.

`ArtifactRootGenerationReference` is a second opaque, process-local,
credential-free value. Equality proves only the same exact generation issuance,
including the content, selection, and binding-context generation facts that can
change dependency evidence. References are never reused during the issuing
Workspace process lifetime. Any replacement of those facts receives a
different reference, even when logical correspondence remains equal. The
reference is a freshness precondition only; it is not a binding, context
handle, lease, receipt, cache key, or access grant.

Both values are erasing projections. They strongly own no
`PackageRootRealization`, package content, byte buffer, `PackageRootBinding`,
artifact, assembly context, artifact session, lease, provisional receipt,
stream opener, delegate, or access authority. Holding either value after
retirement cannot delay generation quiescence or resource release.

`ArtifactRootScopeProjection` is an immutable snapshot, not a live view.
Consumers may retain `ArtifactRootCorrespondence` as logical identity and may
retain an older projection as historical evidence, but must call
`GetCurrentRootScopeProjection(Workspace, Correspondence)` for current status.
That owner operation returns the current point-in-time projection or a typed
foreign-Workspace, absent, closing, or closed outcome.

`Ready` carries the exact current generation reference at projection time.
Retirement removes the old generation from current admission before
replacement starts, so a refreshed `Pending` or `Failed` projection carries no
current reference. Its evidence is likewise resource-free and may name typed
diagnostics and last-known identity facts without retaining a physical
resource.

The projection does not replace ordinary artifact authorization. A later
physical operation still enters through the existing Workspace query or
content-access gate, supplies a retained generation reference as a freshness
precondition, and acquires the owner's normal lease. Currentness is established
only by atomic comparison with the owner's current `Ready` projection at that
gate, never by reference equality alone. A stale, foreign-Workspace, or unknown
reference, or a Root whose current projection is `Pending` or `Failed`, returns
typed `ArtifactGenerationMismatch` before physical access. This generation
check precedes binding-policy revalidation, so a simultaneously stale
generation reference and policy token reports `ArtifactGenerationMismatch`.
Work that already passed the existing access linearization point retains its
ordinary lease semantics.

This projection may be retained by logical Workspace state, Navigation,
history preparation, diagnostics, or serialized-output preparation. It remains
process-local and is never serialized. A host lowers only portable coordinate,
status, and diagnostic facts; it never lowers either opaque identity.

The named consumers are:

- the Workspace Scope and Expansion design in #5701, which consumes
  correspondence and generation freshness without redefining them; and
- Inspection Subject Navigation adoption in
  [#5584](https://github.com/richlander/dotnet-inspect/issues/5584), which
  consumes typed status without owning artifact lifetime.

The shared projection serves both Browser/Wasm and CLI hosts through those
host-neutral consumers. It adds no host-specific storage, rendering, or
interaction contract.

The required pathological cases are:

| Case | Required result |
| --- | --- |
| History retains many removed package Roots after repeated Open and Clear | Retained projections keep no package bytes, bindings, contexts, sessions, or leases alive |
| The same logical package Root is reacquired from replacement content | Correspondence remains equal and the generation reference changes |
| Package version, producer, target, runtime, or non-package coordinate changes | Correspondence changes rather than aliasing the prior logical Root |
| Current content retires before replacement settles | A refreshed projection is `Pending` or `Failed` with no current generation reference |
| A retained old generation reference reaches a physical-access gate | Typed `ArtifactGenerationMismatch` occurs before a new lease or content access |
| Browser in-memory package content retires while logical history remains | The byte buffer becomes collectible after existing artifact leases drain |

The target Release gates are:

| Gate | Property |
| --- | --- |
| `ArtifactRootCorrespondence_IsExactAndResourceFree` | Correspondence uses owner-issued typed Root facts and strongly retains no physical artifact resource or access capability. |
| `ArtifactRootCorrespondence_StableOnlyAcrossCorrespondingReplacement` | Equal logical request retains correspondence across replacement; changed coordinate, target, runtime, producer, or non-package coordinate does not. |
| `ArtifactRootCorrespondence_ExactRequestMatchPerformsNoPhysicalAccess` | A fully resolved exact request can match correspondence without opening content, constructing a context, or acquiring a lease. |
| `ArtifactRootGenerationReference_ChangesWithPhysicalGeneration` | Content, selection, or binding-context replacement changes the non-reused issuance reference even when correspondence remains equal. |
| `ArtifactRootProjection_RefreshReturnsCurrentPointInTimeStatus` | A retained correspondence refreshes to the exact current `Ready`, `Pending`, or `Failed` projection; retained old projections do not claim live status. |
| `ArtifactRootProjection_NonReadyCarriesNoCurrentReference` | A refreshed `Pending` or `Failed` projection exposes resource-free evidence and no current generation reference. |
| `ArtifactRootGenerationReference_StaleOrForeignCannotEnterAccess` | Owner validation, not equality alone, rejects a stale, foreign, unknown, or non-current reference before physical access or lease issuance. |
| `BrowserArtifactRootProjection_DoesNotRetainRetiredPackageBytes` | Browser package bytes can drain after artifact leases release even while logical consumers retain old projections. |

The initial implementation verifies the Package correspondence arm through:

- `PackageArtifactRootCorrespondence_IsExactAndResourceFree`;
- `PackageArtifactRootCorrespondence_StableOnlyAcrossCorrespondingReplacement`;
- `PackageArtifactRootCorrespondence_ExactRequestMatchPerformsNoPhysicalAccess`;
  and
- `PackageArtifactRootCorrespondence_RuntimeCloseStopsIssuance`.

The generic target gates remain **unverified** until the non-package adapter
exists. Generation-reference, current-status, stale-access, and byte-drain
targets also remain **unverified**. #5727 must issue physical-generation
identity from the ArtifactSetSession-backed realization established by #5607,
publish it through the owner gate, and validate it at physical access; the
older direct-group completion does not define Workspace current composition.

This focused addition does not define logical Workspace membership, Root
occurrence identity or order, Add/Replace/Remove/Clear, dependency-expansion
eligibility, closure evidence, Navigation focus, browser history, packet
schema, source authorization, or a new preparation/adoption transaction.

### Artifact Root preparation and scope publication

Artifact Acquisition owns one focused handoff from provisional physical Root
preparation to current runtime Workspace composition:

```text
ArtifactRootPreparationReceipt
  Workspace               InspectionWorkspaceIdentity
  Preparation             ArtifactRootPreparationIdentity
  CandidateSet            ArtifactRootCandidateSetIdentity
  Deadline
  Cancellation
  State                   Prepared | Publishing | Published | Released

ArtifactRootPublicationPlan
  Workspace               InspectionWorkspaceIdentity
  ExpectedComposition     ArtifactRootCompositionGenerationIdentity
  Deadline
  Cancellation
  DesiredRoots            ordered ArtifactRootPublicationEntry sequence
  Preparations            ordered ArtifactRootPreparationReceipt sequence
  Participant             ArtifactRootScopePublicationParticipant

ArtifactRootPublicationEntry
  = Retain(ArtifactRootCorrespondence,
      ArtifactRootGenerationReference)
  | Adopt(ArtifactRootPreparationIdentity,
      ArtifactRootPreparationEntryIdentity)
```

`ArtifactRootPreparationReceipt` is opaque, process-local, one-shot, and
resource-bearing. It owns one complete prepared Root batch, its exact candidate
correspondence, provisional artifact sessions and contexts, aggregate budget
reservation, cancellation authority, and finite deadline. It is never stored
in logical Workspace history, Navigation, browser history, portable state, or
serialized output.

Preparation is all-or-failure for its requested candidate batch. It returns
either one receipt containing every successfully prepared candidate or one
typed failure after releasing the whole provisional batch. Each prepared entry
has one opaque identity unique within that receipt. Entry identities cannot be
constructed from package coordinates, paths, display text, correspondence, or
row order.

The caller chooses preparation partitioning before invoking Artifact
Acquisition; Artifact Acquisition does not infer whether one candidate is
required or optional from logical policy. Explicit Add or Replace can prepare
one multi-candidate batch. Bounded dependency expansion can prepare each
independently optional candidate as its own batch, retain the successful
receipts, and publish those receipts together while the scope participant
records exact failure evidence for unsuccessful candidates.

The [Static Ecosystem Packs](ecosystem-packs.md) catalog is not a publication
participant and its pack identity does not enter this protocol. A front end may
select a pack's package-set or package-prefix action, but the selected
owner-issued currency passes through its source and scope owners before
Artifact Acquisition sees exact Root candidates. A required curated-set Add
may therefore use one all-or-failure multi-candidate receipt; an explicit Add
of selected prefix-query results does the same. Only Scope-owned optional
dependency expansion chooses independent preparation batches and a successful
subset.

The receipt begins `Prepared`. `ReleaseArtifactRootPreparation` is idempotent:
it changes `Prepared` to `Released`, releases every provisional resource and
budget reservation, and returns `NoEffect` for an already released receipt.
Once publication changes it to `Publishing`, the publication operation owns
the only authority to publish or release the batch. A concurrent explicit
release returns typed `PreparationPublishing` without draining staging.
`Published` is terminal and cannot be released through preparation authority.
A foreign-Workspace, unknown, or forged receipt is rejected without affecting
another receipt.

The owner observes the finite deadline and releases an abandoned `Prepared`
receipt even when its caller drops the value or never submits publication.
Disposal or explicit release may settle earlier. After either `Published` or
`Released`, retaining the terminal receipt strongly owns no provisional or
current physical resource; successful publication transfers resource lifetime
to the current runtime composition.

`ArtifactRootPublicationPlan` is a complete desired physical Root set, not a
logical membership policy. `Retain` names one exact current correspondence and
generation reference. `Adopt` names one entry by its receipt's preparation
identity plus its receipt-local entry identity. Every listed receipt must be
distinct; every entry from every listed receipt must appear exactly once;
every desired correspondence must be unique; and no entry may be both retained
and adopted. Current Roots omitted from the complete desired set retire if
publication commits. An empty desired set supports Clear with an empty
preparation sequence. A plan always carries its own finite deadline and
cancellation authority. Every listed receipt's Workspace, deadline, and
cancellation authority must match the plan. An empty preparation sequence
cannot accompany an `Adopt` entry.

`ArtifactRootCompositionGenerationIdentity` is an opaque, process-local,
non-reused identity for one current physical Root composition epoch. It changes
on every change to current physical Root admission, including owner-internal
retirement and replacement settlement, and on a logical publication that
retains an equal physical set. Every such transition observes the runtime
composition gate. Equality proves that neither a physical-composition change
nor another scope-requested publication has intervened; it is not a scope
revision, query lease, or access grant.

Artifact Acquisition may reserve a fresh identity for one privately staged
candidate composition before commit. Equality with that value proves only the
same reservation, not currentness. Only successful publication makes it the
current identity returned by
`GetCurrentArtifactRootCompositionGeneration`; refusal discards it without
reuse.

Artifact Acquisition creates an initial composition identity when the runtime
Workspace opens, including for an empty physical composition.
`GetCurrentArtifactRootCompositionGeneration(Workspace)` observes the runtime
composition gate and returns that current resource-free identity or a typed
absent, closing, or closed Workspace result. It opens no artifact, source,
session, binding context, or access authority. A refused caller reads this
operation again before constructing a replacement plan; it never synthesizes
or advances the identity. Successful publication also returns the freshly
assigned identity with the exact published Root projections.

The `ArtifactRootScopePublicationParticipant` is a sealed host-neutral contract
implemented only by Workspace Scope and Expansion. It is not a plugin or a
general transaction participant. It carries the exact Workspace, expected
opaque Scope-owned publication-base value, operation and candidate identities,
and a complete resource-free candidate publication. The publication base must
be a fresh, process-lifetime non-reused issuance for every successful Scope
current-pointer swap, including membership, policy, closure-only, and
physical-refresh publication. Artifact Acquisition treats those values as
opaque and cannot inspect membership, order, expansion policy, closure,
operation results, or Navigation intent.

The participant is process-local and single-use. A plan rejected before
`PrepareCommit` leaves the participant available. Invoking `PrepareCommit`
consumes it: a refusal or discarded token is terminal, and an invoked token is
terminally committed. Reusing the same participant returns a typed
`ParticipantAlreadyConsumed` result. A separately constructed equivalent
participant still carries the same expected Scope publication base and is
refused after the first publication replaces that base.

The participant exposes two owner-defined steps:

1. `PrepareCommit(current composition, candidate composition identity,
   projected desired Roots)` is side-effect-free. It revalidates the scope
   candidate under the runtime composition gate and returns either a typed
   refusal or one private, single-use, no-fail commit token. The candidate
   identity is owner-issued and unpublished but is the exact identity that will
   become current if the token commits, so the participant can preconstruct its
   complete logical snapshot.
2. The commit token performs only the scope owner's preconstructed current-state
   pointer swap and returns its already constructed operation result. It does
   not acquire, allocate, call a source, wait, yield, invoke user code, render,
   or perform another validation.

The runtime Workspace composition gate is one asynchronous exclusion boundary
shared by Root publication, scope current-state publication, and new artifact
query entry. Owner-internal current Root retirement and replacement publication
also observe this gate and advance the physical-composition identity. Waiting
for the gate does not block a thread and is compatible with single-threaded
Browser/Wasm. The final commit region is synchronous and non-yielding.

`PublishArtifactRootComposition` applies this order:

1. Before consuming any listed receipt, validate the operation shape,
   Workspace identity, finite plan deadline, cancellation authority, entry
   uniqueness, receipt uniqueness, and receipt/plan correspondence. A plan with
   no preparations but an `Adopt`, or a plan that does not use every entry from
   every listed receipt exactly once, is malformed. Rejection leaves every
   matching `Prepared` receipt and the unused participant under caller
   ownership.
2. Enter the exact runtime Workspace composition gate. Revalidate that the
   plan still applies in this order: listed receipt states in plan order, the
   open Workspace, cancellation and deadline, expected composition generation,
   every retained generation reference, then admission budgets for the complete
   desired set. Receipt-state precedence reports
   `PreparationAlreadyPublished`, `PreparationReleased`, or
   `PreparationPublishing` for the first non-`Prepared` receipt. Any refusal in
   this step changes every still-`Prepared` listed receipt to `Released`, drains
   those complete provisional batches, and leaves the unused participant
   unconsumed.
3. Change every listed receipt to `Publishing`, privately stage the complete
   new physical composition, and construct unpublished candidate
   `ArtifactRootScopeProjection` values for the desired Roots. Reserve one fresh
   unpublished candidate `ArtifactRootCompositionGenerationIdentity` for that
   exact staged composition. A plan with no preparations stages only retained
   or empty composition. Nothing is query-admissible, current, returned, or
   retainable yet.
4. Ask the participant to prepare its commit from the exact current composition
   together with the reserved candidate composition identity and ordered
   projected Roots. A stale scope candidate, supersession, consumed participant,
   participant refusal, cancellation, or deadline expiry releases all staging,
   permanently discards the candidate identity, changes every listed receipt
   to `Released`, and preserves both current states.
5. Recheck cancellation, deadline, retained generation currentness, and
   composition identity. Then invoke the participant's no-fail commit token,
   swap the staged physical composition into current query admission, publish
   the exact reserved composition identity, make the candidate projections
   valid for that new current composition, and change every listed receipt to
   `Published` in one non-yielding critical region.
6. Exit the gate with both current pointers changed or neither changed. Return
   the participant's complete scope-operation result and exact published Root
   projections together with the fresh composition identity.

Every scope snapshot read and new artifact query entry observes the runtime
composition gate. The order of the two internal pointer assignments is
therefore unobservable: an observer obtains either the complete old logical and
physical composition or the complete new pair. No query can enter a staged or
retired Root. Work that entered an old generation before publication keeps its
ordinary lease and drains under the existing generation-access contract.

A product-level participant refusal occurs before the final commit token
exists. Once issued, the token's pointer swap is no-fail by the participant
contract. A reserved candidate composition identity that does not commit never
becomes current and is never reused; retaining or comparing it grants no
authority. Process termination and runtime-corruption recovery are outside
this transaction; the design does not add a broad exception-catching or
durable journaling protocol.

Cancellation or deadline expiry before the final recheck releases every listed
preparation. After the non-yielding commit starts, publication wins and returns
`Published`; cancellation cannot turn a committed composition into a
cancelled result. A second publish attempt with any previously listed receipt
returns a typed `PreparationAlreadyPublished` or `PreparationReleased` outcome
and releases every other still-`Prepared` listed receipt. A preparation-free
retry with the same participant returns
`ParticipantAlreadyConsumed`; an equivalent new participant is refused by its
stale Scope publication base. None can repeat scope publication.

The named consumer is Workspace Scope and Expansion in #5701. Add, Replace,
Remove, Clear, expansion-policy edits, and dependency expansion remain that
owner's semantics. This owner sees only the complete desired physical set and
the sealed participant. Browser/Wasm and CLI use the same host-neutral
composition through the scope owner; neither host receives the receipt or
commit token.

The required pathological cases are:

| Case | Required result |
| --- | --- |
| Clear supersedes a slow prepared Add before publication | Participant refusal releases the complete prepared batch; Clear remains current |
| Artifact budget or expected composition changes before publication | Typed refusal releases a present prepared batch; no logical or physical current state changes |
| Replace retains one Root, adopts one Root, and omits one Root | One gate exit exposes the complete new logical scope and matching physical set; the omitted Root rejects new query entry |
| Expansion prepares three optional candidates and one fails | The two successful independent receipts publish together while the scope participant records exact failure evidence for the third |
| Removed Root has an admitted query lease | No new query enters after publication; the existing lease drains normally |
| Participant refuses after physical staging | Staging releases before gate exit; both old current states remain observable |
| Any receipt is submitted twice after publication | Typed `PreparationAlreadyPublished`; every other still-Prepared listed receipt releases and no second adoption or scope publication occurs |
| Delayed receipt-free retry after several later Scope publications | Every intervening pointer swap issued a distinct non-reused Scope base; the old participant remains stale and cannot become current again through ABA |
| Clear without preparation receipts | Plan deadline and cancellation govern the operation; the single-use participant and Scope publication base prevent replay |
| Explicit release races a publishing receipt | Typed `PreparationPublishing`; publication alone publishes or releases the staged batch |
| Unrelated Root replacement settles while a plan waits | Physical-composition identity advances; the stale plan releases, the caller reads the new identity, and no replacement is retired or overwritten |
| Receipt deadline expires while waiting for the gate | Receipt releases; no prepared resource remains retained |
| Browser package bytes back an abandoned receipt | Bytes become collectible after release and lower-level preparation leases drain |
| Single-threaded Browser/Wasm waits for another publication | Asynchronous exclusion waits without blocking the host thread; the final commit does not yield |

The target Release gates are:

| Gate | Property |
| --- | --- |
| `ArtifactRootPreparation_IsCompleteOrReleasesAll` | One requested batch returns one complete receipt or typed failure after every provisional resource releases. |
| `ArtifactRootPreparation_BindsExactWorkspaceCandidateAndDeadline` | A receipt cannot cross Workspace, candidate set, preparation occurrence, or finite deadline. |
| `ArtifactRootPreparation_ReleaseIsIdempotentAndTerminal` | Explicit release of Prepared or owner-observed deadline drains the complete prepared batch once; repeated release has no effect, Publishing returns a typed non-release result, and publication cannot follow a completed release. |
| `ArtifactRootPreparation_TerminalReceiptRetainsNoResources` | Retaining a Published or Released receipt prolongs neither provisional nor current physical resources. |
| `ArtifactRootPublication_ValidatesCompleteDesiredSetBeforeConsumption` | Malformed, duplicate, foreign, or mismatched plans are rejected before consuming any matching prepared receipt. |
| `ArtifactRootPublication_StalePhysicalOrLogicalCandidateCannotCommit` | Stale composition, generation, scope base, supersession, budget, cancellation, and deadline checks preserve both current states and release every listed prepared batch once applicability validation starts. |
| `ArtifactRootPublication_CompositionIdentityCoversEveryPhysicalChange` | Owner-internal Root retirement or replacement and scope-requested publication all advance one gate-observed physical-composition identity. |
| `ArtifactRootPublication_CompositionIdentityIsOwnerIssued` | An empty or populated open Workspace exposes its current resource-free composition identity through a gate-observing owner read, and successful publication returns the fresh replacement identity. |
| `ArtifactRootPublication_CandidateIdentityPrecedesParticipantCommit` | Scope receives the exact unpublished candidate composition identity before constructing its no-fail commit token; commit publishes that identity, while refusal discards it permanently. |
| `ArtifactRootPublication_PreparationSetPublishesAtomically` | One plan adopts every entry from one or more independently prepared successful batches, publishes all listed receipts together, or releases every listed prepared batch. |
| `ArtifactRootPublication_ReceiptFreePlanCommitsOrRefusesOnce` | Empty and retain-only plans use plan deadline/cancellation plus a single-use participant and a fresh process-lifetime non-reused Scope base for every logical pointer swap, so they cannot repeat logical publication or become current again through ABA. |
| `ArtifactRootPublication_OldOrNewCompositionIsObserved` | Scope reads and query entries observe either the complete old logical/physical pair or the complete new pair, never a half-state. |
| `ArtifactRootPublication_ParticipantRefusalReleasesStaging` | A typed participant refusal after staging publishes nothing and releases every provisional resource. |
| `ArtifactRootPublication_ReceiptPublishesAtMostOnce` | Each listed receipt has one terminal Published or Released outcome and cannot duplicate adoption or logical publication. |
| `ArtifactRootPublication_RetirementStopsNewEntryAndDrainsLeases` | Roots omitted from a committed desired set reject new query entry while already admitted leases drain. |
| `BrowserArtifactRootPreparation_ReleaseDoesNotRetainPackageBytes` | Abandoned Browser preparation bytes become collectible after receipt release and lower-level lease drainage. |
| `BrowserArtifactRootPublication_GateDoesNotBlockOrYieldDuringCommit` | Single-threaded Browser/Wasm waits asynchronously and the final old-to-new commit region performs no yield or blocking wait. |

Every target is **unverified** until its named Release gate exists. Before
implementation, a focused model under
`docs/design/models/artifact-root-publication/` must check receipt states,
plan/receipt authority association, validation and cancellation precedence,
participant refusal, old-or-new visibility, and eventual settlement under a
finite deadline. Retirement query-entry rejection and old-generation lease
drainage remain owned by the existing generation-access contract and the
`ArtifactRootPublication_RetirementStopsNewEntryAndDrainsLeases` implementation
gate; they do not enter this focused publication model. #5701's scope-revision
model should instantiate this owner-issued publication transition rather than
copying it.

This focused addition does not define source resolution, logical Root
membership or order, expansion policy, closure, Navigation focus, browser
effects, portable schema, arbitrary transaction participants, durable recovery,
or a second query-access protocol.

### Runtime Workspace identity

`InspectionWorkspace` owns one opaque `InspectionWorkspaceIdentity` for its
exact runtime instance. The identity is stable for that instance and differs
from every replacement or independently opened Workspace, even when both were
activated from equal portable
`WorkspaceContextAddress` values. Definition IDs, context names, URLs, cache
keys, and display text do not participate in runtime identity.

While its state is `Open`, the Workspace supplies live operation authority to
the [Workspace Scope and Expansion](workspace-scope-and-expansion.md) owner.
That owner may issue Workspace-bound occurrence identities only while the
authority remains valid. Synchronous `Dispose()` and asynchronous
`CloseAsync()` stop new scope-operation authority in the same critical section
that changes the runtime state to `Closing`. Existing identities remain
comparable after close, but neither identity nor equality authorizes later
scope operations, package-content access, or query entry.

The runtime identity currency is:

| Property | Contract |
| --- | --- |
| Authority | Issued once by the exact `InspectionWorkspace` instance |
| Scope | One runtime Workspace occurrence |
| Lifetime | Equality remains meaningful after close; operations still require a live owner |
| Portability | Process-local and never serialized |
| Erasure | Carries no definition, context, inventory, membership, or presentation facts |
| Rebinding | No value can reconstruct or rebind it in another Workspace |
| Correspondence | Reference equality proves the same runtime Workspace |

The current `PackageRootOccurrenceBinding`,
`NonPackageRootOccurrenceIdentity`, and
`InspectionWorkspacePackageOccurrenceView` implementation is the first
package-only substrate for #5656. Architectural ownership of occurrence
issuance, order, retained membership, activation-bearing operation results,
and their future replacement moves to Workspace Scope and Expansion.
`PackageRootBinding` remains acquisition-owned and authoritative for package
coordinate, content generation, selection, and exact physical correspondence.
The replacement scope contract does not retain that resource-bearing value in
a logical occurrence. It instead consumes the `ArtifactRootCorrespondence`
and point-in-time `ArtifactRootScopeProjection` defined above; neither retains
package content, contexts, sessions, leases, or access authority. The scope
owner separately composes its typed resource-free Root descriptor from the
exact coordinate-owner facts returned by the source composition.

The runtime-identity and close gates remain
`WorkspaceIdentity_IsStableAndExactPerInstance`,
`SynchronousClose_StopsOccurrenceIssuanceButKeepsIdentity`, and
`AsynchronousClose_StopsOccurrenceIssuanceImmediately`. Existing
`PackageOccurrence_*` gates and the order, empty-view, repeated-binding, exact
activation, foreign-view rejection, and closed-Workspace rejection
`PackageOccurrenceView_*` gates are implementation evidence consumed by the
new owner; this document no longer defines their logical membership semantics.

`InspectionWorkspace.CreatePackageOccurrenceView` composes an immutable
ordered view from acquisition-issued package Root bindings. Input order is the
view order; equal or repeated bindings remain distinct occurrences. An empty
input produces a typed empty view. Each descriptor exposes package, version,
and selected framework presentation facts from its exact Root binding and
carries an opaque action issued for that exact view. Activating the action
returns the exact `PackageRootOccurrenceBinding` only while the Workspace is
open and the action belongs to that view. A foreign-view action returns
`ViewMismatch`; an action whose Workspace has closed returns
`WorkspaceClosed`.

The action and both runtime identities remain process-local. Browser/Wasm
lowers an action to an opaque random transport token and resolves that token
back to the product action before selecting or projecting a package. The CLI
lowers the same ordered descriptors through Markout. Neither host derives
activation identity from package text, version, framework, row position, or a
cache key.

This shipped action is a transitional adapter until #5584 replaces it with
Navigation-owned actions. Its ordered package view is no longer canonical
Workspace membership, but its existing activation and host-lowering behavior
remains owned here during that transition. It does not project non-package Root
occurrences. The first CLI acquisition adapter requires a package with at
least one selected managed assembly; root-only, analyzer-only, and tools-only
package acquisition is not yet a supported CLI input.

### Workspace composition and query execution

The Workspace runtime owns one or more artifact set sessions and one or more
assembly context groups. Its
[logical scope owner](workspace-scope-and-expansion.md) decides which exact
Root composition a candidate revision requests. When an authorized query plan
first demands a context, the
artifact owner issues its admission lease; the context loader constructs and
seals a session from all required acquisitions for that context, then creates
its group. Loading a definition alone performs none of that work. Retained hosts
may prepare additional contexts for one candidate scope revision. Logical
publication, Root order, and Add/Replace/Remove/Clear policy remain with
Workspace Scope and Expansion. Groups compose projected assembly participants
under one binding policy and may span artifact sources within their session.

The execution path is:

```text
workspace
  -> plan demands an unrealized context
  -> artifact owner authorizes admission for that plan
  -> context loader seals artifact session and creates group
  -> execute typed query
  -> artifact owner authorizes this query plan and retained catalog generation
  -> owner issues query access lease
  -> select context group and participant
  -> query opens or borrows AssemblyInspectionSession
  -> inspection producers compute evidence
  -> query returns typed result and failure
```

The query owns session use. A host or presentation layer cannot open raw
readers and invoke producers around the query registry.

For an accepted analysis plan,
[analysis universe realization](analysis-universe-realization.md) owns the
operation-scoped binding from the plan's exact finite universe description to
its authenticated Workspace offer and the capability-owner-issued access
required by the plan. Workspace retains ownership of admission, groups, query
authorization, and close behavior; the analysis consumer receives no mutable
Workspace or group enumeration surface.

Operations that do not inspect assemblies remain narrower. Package metadata,
feed discovery, archive listing, and artifact inventory queries do not create
fake assembly participants merely to enter the workspace path.

## Project and dependency boundaries

Project references must enforce optionality. Runtime registration alone is
insufficient because an unused package implementation would still burden a
local-only application.

The target project graph has these roles:

```text
artifact contracts
  ^                 ^                    ^
  |                 |                    |
storage impls   source adapters   workspace composition
                    |
       +------------+-------------+
       |            |             |
     local       package          CI
                    |
              package domain

artifact contracts --> Metadata --> core assembly Queries
artifact contracts --> workspace composition

full host --> core Queries + selected optional adapters/companions
local host --> core Queries + local adapter
```

`DotnetInspector.Artifacts` owns the source-neutral contract floor,
`DotnetInspector.Artifacts.Workspaces` owns artifact-session composition, and
`DotnetInspector.Artifacts.Local` owns explicit local-file acquisition. The
remaining adapter and companion project names are deferred, but the split must
produce these compile-time properties:

1. artifact contracts reference no storage implementation, package domain, or
   assembly inspection project;
2. Metadata references no package domain, NuGet library, source adapter, or
   storage implementation;
3. core assembly workspace/query projects reference no package implementation;
4. package-specific queries live in an optional companion rather than forcing
   the package domain into core assembly queries;
5. package-aware composition references both sides through an adapter; neither
   Packages nor Metadata references the other;
6. package graph correspondence is validated and projected by the optional
   package companion rather than core assembly Queries;
7. platform correspondence is minted by the platform adapter and projected
   without a package or Metadata provenance dependency;
8. platform graph projection references neither package/NuGet implementations
   nor the package companion; an installed-platform adapter has the same
   closure, while an optional NuGet-backed remote-platform implementation
   reuses package acquisition without exposing it through the realization;
9. neutral symbol/PDB storage and source-access contracts do not reference
   package source policy;
10. hosts choose adapters through project references and capabilities.

## Current mismatches

Several current types are migration inputs, not target precedent:

- `AssemblySetRequest` carries packages, projects, platform inputs, directories,
  NuGet source options, package selection mode, and temporary-directory policy.
  `AssemblySetResolver` directly calls `PackageExtractor`.
- `AssemblySet` owns temporary package extraction directories.
- `DotnetInspector.Services` references `DotnetInspector.Packages` and
  `NuGet.Versioning`, so its full closure is not suitable as an assembly-only
  service layer.
- `DotnetInspector.Queries` references `DotnetInspector.Packages` directly.
- `WorkspaceContextLoader` realizes package and platform coordinates inside the
  core query project.
- No current realizer owns the explicit local/designated/installed-platform
  request above. `ArtifactWorkspaceRole` exposes `CallerDesignated` but not
  `PlatformAuthorized`; no package-free installed-platform adapter contributes
  a validated #5139 closure to an `ArtifactSetSession`.
- No current workspace-issued preparation binds one completed composed policy
  to the exact context generation, ordered participants, roles, delegate map,
  and captured non-reusable composite-policy token. Group construction and
  current publication therefore have no implementation correspondence for
  policy adoption, observed-drift retirement, or ordered generation
  replacement.
- No current Root-level preparation receipt or shared runtime composition gate
  joins a prevalidated logical scope publication with complete physical Root
  adoption, retirement, and query visibility.
- Current artifact-backed Metadata projection requires a query-authorized
  `ArtifactContentReference` from an already published session and returns a
  descriptor with public path/opener compatibility surfaces. #5143 owns the
  missing admission-scoped, opener-free projection used by this context.
- `AssemblyContextSourceQueryContext` exposes package-owned `IPdbStore`,
  `IPackageSourceAuthorization`, and `NuGetSourceOptions` even for
  assembly-authored-source queries.
- `InspectionGraphPackageBoundary` validates package and platform
  correspondence by pattern matching Metadata-owned provenance and parsing
  package versions inside core Queries.
- `AssemblyResolutionProvenance` is defined by Metadata but enumerates package,
  project, platform, local, embedded, and caller-designated concepts. The
  `DesignatedAsset` arm also combines acquisition provenance with a trust-policy
  role.
- `MetadataContext.Open(string)`, `MetadataSource.OpenCore(string, ...)`, and
  `MetadataSource.OpenFromPrefetchedImage` treat a raw path, or a path paired
  with caller-supplied bytes, as caller designation and grant core-library
  trust without consulting an admission role. That is current compatibility
  behavior, not the target meaning of a lease-scoped retained-snapshot path.
- `workspace-definitions.md` currently maps member kinds directly onto that
  closed Metadata provenance hierarchy.
- `type-forwarding-resolution.md` currently calls that hierarchy authoritative
  and gates the parameterless opener shape.
- `IPackageContent` provides path-optional package entry access, but also
  exposes `RootPath`, `NupkgPath`, and unguarded archive/entry openers for
  compatibility with current desktop consumers. It is a package-specific
  migration input, not the generic guarded artifact contract.

These types need not move in one change. The design requires each migration
slice to reduce the forbidden dependency closure rather than add another
source-specific case to the assembly layer.

## Migration

The migration is intentionally incremental:

1. **Land the design and closure gates.** Record the target forbidden
   dependencies and add a package-free closure canary before moving behavior.
2. **Extract source-neutral artifact contracts.** Introduce artifact identity,
   guarded content access, provenance marker, acquisition registration and
   outcome, admission/query authorization, quiescent lifetime, and lease
   contracts in a package- and Metadata-free project. Implemented by
   `DotnetInspector.Artifacts`; no existing acquisition path consumes the new
   contracts yet.
3. **Prove local acquisition.** Explicit local files now enter
   `DotnetInspector.Artifacts.Local`, freeze before registration, publish through
   `ArtifactSetSession`, and feed the package-free Metadata fixture through a
   current query lease. Explicit caller designation is assigned by workspace
   admission as a role rather than local provenance. Metadata trust does not yet
   consume that role, and bounded directory acquisition remains outstanding.
4. **Extract neutral symbol capabilities.** Move PDB content storage and
   source-access authorization below core assembly Queries; keep NuGet symbol
   source policy in an optional companion.
5. **Separate workspace realization.** Move package/platform realization out of
   core assembly Queries into optional adapters or companion projects. The
   asynchronous workspace now owns exact sealed artifact sessions through their
   dependent-group release receipts; package/platform realization migration and
   multi-session host adoption remain outstanding.
6. **Adapt package acquisition.** Reuse current package stores, source policy,
   package admission, and TFM selection behind a package artifact adapter.
7. **Move package correspondence.** Have the package adapter mint typed
   realization proofs and move package graph construction out of core assembly
   Queries while preserving the full host's graph wire contract.
8. **Move platform correspondence.** Have the platform adapter mint typed
   realization proofs and remove platform provenance/version parsing from core
   assembly Queries without pulling the package companion into platform
   projection. Keep installed-platform acquisition package-free; place any
   NuGet-backed remote-platform implementation with the optional package
   acquisition side.
9. **Retire package-aware assembly sets.** Replace package cases in
   `AssemblySetRequest` with host composition of artifact acquisitions and
   workspace groups.
10. **Migrate API source selection.** Select package assets in the package
   adapter, then pass neutral assembly artifacts to the existing assembly/query
   path.
11. **Add other adapters independently.** Project, platform, embedded, and CI
   adapters land only with their own typed coordinates, capabilities, limits,
   and provenance gates.

Each slice must preserve current visible diagnostics and selection semantics
unless its owning design names an intentional change. Shared local-path
admission deliberately changes an explicit-file coordinate that resolves to a
directory or another non-regular entry from
`Failed`/`local.file.read-failed` to
`Rejected`/`local.file.unsupported-entry`; its missing, size-limit, and genuine
path-specific metadata, open, or read failures retain their current
projections. `local.file.classification-unsupported` identifies an unexpected
platform-classifier deployment failure rather than disguising it as a read
failure. The migration does not justify a success-shaped fallback or an
unbounded eager materialization.

## Required gates

The target is complete only when tests equivalent to these exist:

- every gate under
  [Explicit local/designated/platform assembly context](#required-implementation-gates)
- `ArtifactContractsClosure_ExcludesMetadataPackagesAndStorageImplementations`
- `ArtifactWorkspaceClosure_ExcludesMetadataPackagesAndStorageImplementations`
- `LocalArtifactAdapterClosure_ExcludesMetadataPackagesAndStorageImplementations`
- `PackagesClosure_ExcludesMetadata`
- `LocalOnlyHostClosure_ExcludesPackageFeedCacheAndArchiveImplementations`
- `MetadataClosure_ExcludesPackageAndStorageImplementations`
- `CoreAssemblyQueries_ExcludePackageImplementations`
- `CoreAssemblySourceQueries_ExcludePackageSymbolCapabilities`
- `PlatformProjectionClosure_ExcludesPackageNuGetAndPackageCompanion`
- `InstalledPlatformAdapterClosure_ExcludesPackageAndNuGetImplementations`
- `ArtifactSetSession_ComposesArtifactsFromMultipleSources`
- `ArtifactSetSession_SealedGenerationCannotMutate`
- `ArtifactIdentity_IsScopedToOwningGeneration`
- `WorkspaceAdmissionBudget_RejectsAggregateMultiSourcePlanBeforeAdapterCall`
- `WorkspaceAdmissionBudget_CountsConcurrentAndRetainedGenerations`
- `ArtifactSetSession_SealingRequiresMaterializedBoundedContent`
- `ArtifactAdmission_OverrunOrIdentityMismatchRejectsPublication`
- `ArtifactAdmission_PublicationIncludesEveryRequiredParticipant`
- `ArtifactAdmission_IsSingleFlightAcrossConcurrentContextDemands`
- `ArtifactAdmission_CancellationDrainRejectsJoinAndLatePublication`
- `BrowserArtifactAdmission_IsSingleFlightAcrossAwaitedReentrancy`
- `ArtifactOpen_AfterPublicationPerformsNoAcquisitionOrExpansion`
- `WorkspaceAdmissionBudget_ReleasesOnlyAfterCleanupOrSessionQuiescence`
- `DesignatedArtifactTrust_RequiresAuthorizedAdmissionRole`
- `PlatformArtifactTrust_RequiresAuthorizedAdmissionRole`
- `LeaseScopedPath_IsNotADesignationGrant`
- `ArtifactSetSession_DisposesEveryContributingLease`
- `ArtifactSetSession_DisposalReleasesOwnerHeldState`
- `ArtifactSetSession_ConcurrentTerminationWaitsForCleanup`
- `ArtifactSetSession_ConcurrentAbortAndDisposalShareCleanup`
- `ArtifactSetSession_DisposalDuringAcquisitionDisposesLateLease`
- `ArtifactSetSession_SealRejectsAcquisitionInProgress`
- `ArtifactSetSession_DisposalDuringSealCannotPublish`
- `ArtifactAccess_OpenRegistrationIsAtomicWithGenerationEnd`
- `ArtifactAccess_RetainedOpenerIsCancelledAfterGateAdmission`
- `ArtifactAccess_AuthorizationReplacementIsAtomicWithOpenRegistration`
- `ArtifactAccess_LeaseDisposalIsAtomicWithOpenRegistration`
- `ArtifactAccess_ReturnedStreamKeepsGenerationAliveUntilDisposed`
- `ArtifactAccess_RetainedOpenerCancellationEndsAtCallbackReturn`
- `ArtifactAccess_StreamDisposalFailureStillReportsQuiescence`
- `ArtifactAccess_MaterializationReadPreservesCallerCancellation`
- `ArtifactSetSession_ReleasesLeasesOnlyAfterOpenArtifactStreamsQuiesce`
- `WorkspaceClose_ReleasesArtifactSessionAfterExactDependentGroupQuiesces`
- `RegisterArtifactSession_RejectsForeignOrIncompleteGroupSet`
- `RegisterArtifactSession_RejectsLaterCoordinatedGroup`
- `WorkspaceClose_ReportsArtifactSessionCleanupFailure`
- `WorkspaceClose_ReleasesArtifactSessionWhenCoordinatedCloseFaults`
- `WorkspaceClose_WaitsForPhysicalReleaseWhenCoordinatedCloseFaultsEarly`
- `WorkspaceClose_WaitsForCoordinatedOwnerAfterReleaseRequestThrows`
- `ArtifactSetSession_DisposalCancelsInFlightMaterialization`
- `ArtifactSetSession_CancellationCallbackFailureDoesNotSkipLeaseCleanup`
- `ArtifactSetSession_PreservesPrimaryFailureWhenCleanupFails`
- `SupplementalAcquisition_RequiredCheckpointPreservesSealOutcome`
- `SupplementalAcquisition_SealUsesCheckpointedSnapshots`
- `SupplementalAcquisition_EmptyBatchPublishesNoArtifactsAndOwnsItsLease`
- `SupplementalAcquisition_ReservesBeforeAdapterAndCannotOverrunAtSeal`
- `SupplementalAcquisition_PreservesAdapterOutcomeKindAndDiagnostic`
- `SupplementalAcquisition_NonEmptyBatchPreservesScopeAndRoleChecks`
- `SupplementalAcquisition_IdentityAndMaterializationAreAtomic`
- `SupplementalAcquisition_RejectedAcquiredBatchCleansLeaseWithoutMaskingFailure`
- `SupplementalAcquisition_ConcurrentTerminationDisposesLateOutcomeAndReservation`
- `SupplementalAcquisition_LateDiagnosticRemainsVisibleOnTermination`
- `SupplementalAcquisition_CancellationRemainsCancellation`
- `WorkspaceDisposal_CancelsAdmissionAndDisposesLateOutcome`
- `BrowserWorkspace_DisposalDuringAwaitedAdmissionCannotPublish`
- `ArtifactAdmission_ProjectsAssembliesThroughAuthorizedLease`
- `AdmissionLease_CannotOpenContentAfterGroupPublication`
- `ArtifactAccess_RejectsChangedOrRevokedQueryAuthorization`
- `ArtifactCatalog_RejectsRevokedPolicyBeforeParticipantSelection`
- `ArtifactCatalog_NarrowedPolicyCannotReusePriorGeneration`
- `DefinitionLoadAndScenarioResolution_PerformNoAcquisition`
- `ArtifactDescriptor_ExposesNoUnguardedContentRoute`
- `ArtifactOpen_RejectsContentSubstitutionAfterAdmission`
- `ArtifactContentReference_BindsIdentityRegistrationRoleAndContent`
- `LocalArtifactSnapshot_MutationCannotChangeInspectionBytes`
- `LocalPathAdmission_ExpectedKindsAndLinksAreShared`
- `LocalPathAdmission_StableNonRegularEntriesRejectBeforeOpen`
- `LocalPathAdmission_ConsumerReceivesTheVerifiedOpenGeneration`
- `LocalPathAdmission_OutcomesAndCancellationRemainDistinct`
- `LocalPathAdmission_PlatformClassifiersRemainPortable`
- `LocalDirectoryAcquisition_BoundedDeterministicSelection`
- `LocalDirectoryAcquisition_EmptyOrFailedBatchPublishesNothing`
- `LocalDirectoryAcquisition_ProvenanceSnapshotAndCancellationArePreserved`
- `ArtifactAcquisition_CancellationRemainsCancellation`
- `RequiredMember_EmptyOrNonProjectableAcquisitionFailsContext`
- `RequiredAcquisitionFailure_DoesNotShortenWorkspaceContext`
- `AssemblyContextGroup_CanBindParticipantsFromDifferentArtifactSources`
- `RetainedWorkspace_CanAddASecondSealedContextGeneration`
- `PackageAdapter_ProjectsSelectedEntriesWithoutLeakingPackageTypes`
- `PackageWithoutCompileAssets_RetainsRootWithoutAssemblyRoles`
- `ExplicitEmptyCompileGroup_RetainsRootWithoutAssemblyRoles`
- `NoMatchingFramework_RetainsRequestedRootWithoutAssemblyRoles`
- `InvalidImplementationLayout_RetainsFailedRootWithoutAssemblyRoles`
- `MixedPackages_CreateRolesOnlyForSelectedCompileAssets`
- `PackageRootIdentity_DistinguishesRequestedFrameworksByReference`
- `PackageWorkspaceIntegrationsQuery_RejectsRootOnlyRealization`
- `PackageWorkspaceIntegrationsQuery_PreservesExactRootIdentity`
- `PackageCoordinate_RejectsDifferentContentWithSameIdentity`
- `PackageScope_DoesNotCollapseDifferentContentAtSameCoordinate`
- `PackageScope_ValidatesEveryCoordinateAgainstCacheProvenance`
- `PackageScope_RequestedFrameworkCannotForgeCompositeRegistryKey`
- `MixedPackageScope_RealizesOnlySelectedCoordinates`
- `PackageFrameworkUnavailability_DoesNotEmitArtifactFramework`
- `PackageDependencies_BlankDeclaredFrameworkDoesNotAbortProjection`
- `QueryPackage_ToolsPointerRetainsRootAndManifestDependencies`
- `QueryPackage_ExplicitEmptyCompileGroupRetainsTypedAbsence`
- `QueryPackage_NoMatchingFrameworkRetainsRequestedRoot`
- `PackageGraphProjection_UsesAdapterOwnedCorrespondence`
- `PlatformGraphProjection_UsesAdapterOwnedCorrespondence`
- `RemotePlatformPack_UsesPackageMappingVersionAndProducerAuthorization`
- `RemotePlatformPack_RejectsUnauthorizedOrNarrowedProducerCache`
- `LocalOnlyWorkspace_ExecutesAssemblyQueryWithoutPackageCapabilities`
- `CiArtifactScenario_PreservesProviderRunCommitAndDigestProvenance`
- `CrossProviderCiArtifacts_CompareAcrossSealedAuthorizedContexts`
- `BrowserWorkspace_ComposesSequentiallyWithoutFilesystemOrThreads`

The first ten are structural edge/closure gates derived from the actual project
graph, not a hand-maintained allow list. The remainder are behavior and lifetime
gates. The local-only query gate covers metadata and authored-source query
families so a metadata-only success cannot hide package-owned source
capabilities. `LeaseScopedPath_IsNotADesignationGrant` derives the set of
unconditional path and prefetched-image grants from the reader-construction
site inventory and asserts coverage equality, so adding or reshaping an entry
point cannot escape the migration. The browser gate runs the same composition
sequentially without threads, blocking waits, or a filesystem.

`ArtifactContractsClosure_ExcludesMetadataPackagesAndStorageImplementations`,
`ArtifactWorkspaceClosure_ExcludesMetadataPackagesAndStorageImplementations`,
`LocalArtifactAdapterClosure_ExcludesMetadataPackagesAndStorageImplementations`,
and
`LocalOnlyHostClosure_ExcludesPackageFeedCacheAndArchiveImplementations` are
enforced from the Release project and resolved-assets graphs by
`LayeringTests`. They witness the required package-free local-only variant; they
do not claim that every configuration-specific full-host graph is package-free.
The remaining gates are migration targets and remain unverified.
`ArtifactContractTests` enforce generation-scoped identity,
closed acquisition outcome arms, catalog descriptors without an unguarded
content route, admission expiry, atomic query-authorization replacement,
revocation of new opens without invalidating an already-issued stream, and
one retained snapshot for every minted registration.

`ArtifactSetSessionTests` enforce multi-source contribution, sealed-generation
immutability, bounded owner-private materialization, read-only retained streams,
visible required-acquisition and cleanup failures, acquisition-lease disposal,
owner-held state release, late-outcome lease disposal, seal exclusion during
acquisition and disposal, shared termination completion, query revocation,
non-masking disposal, role assignment separate from provenance, and
owner-bound content references that cannot mix descriptor, registration, role,
or bytes across artifacts or generations.
`LocalArtifactSourceTests` enforce pre-registration local snapshots, typed
path-admission outcomes, expected kinds, link handling, pre-open rejection of
stable non-regular entries, once-opened generation identity, mutation and
deletion resistance, bounded deterministic top-level directory selection,
atomic empty and failed directory batches, directory provenance, immutable
directory snapshots, and cancellation remaining cancellation. The executable
NativeAOT and Browser/Wasm probes enforce the normalized `Stat`/`FStat` imports
and the platform-specific missing, not-directory, and link-loop outcome
mappings. Deep Inspect's Windows `platform-test` execution of
`LocalPathAdmission_WindowsExtendedRelativeLinkTargetIsNormalized`,
`LocalPathAdmission_WindowsAbsoluteExtendedLinkTargetRetainsSyntaxPolicy`, and
`LocalPathAdmission_WindowsAncestorLinkLoopIsRejected` enforces
extended-coordinate admission through a parent-relative symbolic-link target,
absolute-target syntax preservation, and rejected ancestor link cycles.
The three named `LocalDirectoryAcquisition_*` gates enforce bounded
deterministic top-level selection, source-neutral exclusions, atomic empty and
failure outcomes, directory provenance, immutable batch snapshots, and
cancellation preservation. Shared local-path admission remains with the
[local adapter](#shared-local-path-admission) rather than these directory
gates.
The eleven named `SupplementalAcquisition_*` gates enforce the one-way required
checkpoint, reuse of checkpointed snapshots, finite pre-adapter capacity,
empty-batch lease ownership, exact visible failure, atomic scoped nonempty
admission, validation-failure cleanup, termination cleanup, late-diagnostic
projection, and cancellation preservation.
The eight named `ArtifactAccess_*` gates and three
`ArtifactSetSession_*` content-quiescence gates enforce gate-atomic open and
lease-disposal admission, owner interruption of stalled opening and
materialization, returned-stream validity through generation end, deferred
acquisition-lease release, and quiescence reporting even when stream cleanup
fails.
`LocalOnlyHost_InspectsCallerSuppliedLocalAssembly`
deletes its temporary source after publication, then passes an
`ArtifactContentReference`'s guarded published snapshot opener to Metadata, so
a source-path fallback cannot satisfy the gate.

`PackageAssemblyContextRealizationTests` enforce package Root retention,
producer/cache provenance, and assembly-group creation only for selected
compile assets. `BrowserEngineBoundaryTests` enforce the tools-v2 pointer and
explicit-empty-group cases, including typed compile-library absence, package
documents, manifest dependencies, and no fabricated default assembly.

Owner-mediated on-demand content digests are covered by the
[named digest gates](#on-demand-retained-content-digests).

Workspace-wide admission budgets, single-flight/reentrancy,
assembly-group reporting into session quiescence, and Metadata consumption of
workspace roles remain unverified.

## Non-goals

- Defining a universal package session.
- Treating every archive as a package.
- Making storage infer semantic identity from filenames or paths.
- Replacing assembly context groups with artifact sets; artifact lifetime and
  assembly binding remain separate axes.
- Requiring every workspace artifact to be an assembly.
- Defining logical Root membership, selective dependency expansion, scope
  revisions, or scope-operation results.
- Scraping arbitrary deployed Wasm applications for runtime assemblies. A
  cooperating application may supply an explicit manifest or adapter, but
  framework-version-specific boot-resource discovery is not a general source
  contract.
- Implementing Azure DevOps or GitHub Actions acquisition in the first slice.
- Changing user-visible CLI commands in the design PR.
