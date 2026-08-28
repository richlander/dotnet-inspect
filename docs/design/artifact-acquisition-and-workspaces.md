# Artifact acquisition and workspace composition

How storage, packages, artifacts, assemblies, workspaces, sessions, and
inspection producers remain separate concepts while still composing into one
inspection experience.

This is a design proposal with an incremental implementation. Target boundaries
remain **unverified** until their named implementation gates exist. The
source-neutral contract floor, artifact-session publication, explicit local-file
snapshot adapter, and package-free local host now have the gates named under
[Required gates](#required-gates). Current types and remaining target behavior
are identified explicitly under [Current mismatches](#current-mismatches).

See [inspection-space.md](../inspection-space.md) for workspace and query
planning, [inspection-layers.md](inspection-layers.md) for consumer layers, and
[assembly-inspection-query.md](assembly-inspection-query.md) for the
`ResolvedAssemblyReference` and `AssemblyInspectionSession` seam, and
[assembly-image-lifetime.md](assembly-image-lifetime.md) for the focused
single-image and MVID correctness contract.
[workspace-definitions.md](workspace-definitions.md) owns static context
coordinates, while
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
| Workspace | Logical inspection composition | artifact sessions, contexts, roles, query plans, aggregate admission budgets | feed or archive mechanics |
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

Caller cancellation detaches that wait. When no authorized waiter remains, the
owner requests cancellation and enters a draining state. A new demand does not
join a draining operation; after cleanup it may start a fresh admission if the
workspace remains open. An incompatible policy generation likewise cannot join
or start duplicate work for the same context while the prior admission is
active; it waits for the terminal transition and replans.

Workspace disposal first closes admission, rejects new demands, requests
cancellation of in-flight operations, and prevents a late result from
publishing a session or group. A late acquisition outcome transfers directly
to cleanup: every returned lease is disposed even when the adapter did not
observe cancellation. These transitions also handle single-threaded
Browser/Wasm awaited reentrancy; they do not depend on a parallel thread
reaching a lock.

Disposal then disposes published groups. A group may already have an active
callback that has not performed its first lazy content open. Artifact leases
therefore outlive `Dispose()` and are released only after every dependent group
reports quiescence. Synchronous disposal may initiate this deferred release; it
must not invalidate content under an active callback. Cleanup failures compose
with, and never replace, the active operation failure.

### Interaction model

[`docs/models/artifact-session-admission/ArtifactSessionAdmission.tla`](../models/artifact-session-admission/ArtifactSessionAdmission.tla)
model-checks the admission lifecycle described above: single-flight admission
across concurrent demands, an incompatible-generation demand's inability to
join or start duplicate work while a prior admission is active, voluntary
cancellation draining, disposal-forced draining, the rule that a late adapter
result must never publish a session or group, and that a published group's
artifact leases release only as part of the disposal cleanup path once the
group is quiescent. It abstracts away budget arithmetic, adapter identity,
content digests, and query-lease authorization, and it bounds the state space
to one outstanding published group's lease lifecycle at a time (a fresh
admission cannot publish while the previous group awaits lease release); this
is a scope-bounding simplification of the model, not a claim about real
concurrent groups. A demand's requested generation is also fixed once it
arrives; the model does not represent a caller re-deriving a different
generation when it replans after an incompatible admission terminates.

The model checks the design intent stated in the prose above, not the current
`ArtifactSetSession` implementation. `ArtifactSetSession`'s own doc comment
states it "does not yet implement workspace-wide reservation, single-flight
admission, or dependent-group quiescence": today it serves one caller per
generation with no multi-demand join or incompatible-generation wait, and
`Dispose()` releases every acquisition lease immediately rather than
deferring release until dependent groups quiesce. Closing that gap is
tracked as future implementation work, not a defect this model found.

TLC 2026.08.21.155922 (rev `9787e65`, from the pinned `tla2tools.jar` v1.8.0 —
see [`docs/runbooks/tla-plus-setup.md`](../runbooks/tla-plus-setup.md))
checked the model with 3 demands and 2 admission generations: 16,790 states
generated, 8,292 distinct states, no invariant violations, and no
counterexamples for the checked liveness properties. The invariants include
the headline `DisposalPreventsPublication` (`disposed => admission #
"InFlight"`, since only `"InFlight"` can transition to a published outcome)
and independent guard-witness invariants that re-derive, at the point of
action, the exact condition each of `DisposalPreventsPublication`, the
lease-release ordering, and outcome authorization (only a demand attached to
the admission immediately beforehand may receive its outcome) depends on.

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

`ArtifactContentReference` is the query-time input to a downstream content
consumer. The artifact owner issues it for one identity in a sealed generation
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

It does not:

- resolve package versions;
- parse project assets;
- choose a target framework;
- inspect PE metadata;
- construct assembly binding groups;
- render diagnostics.

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

The ordinary retained snapshot does not compute a digest eagerly. The target
owner-mediated on-demand digest remains unverified.

#### Bounded directory coordinate

A directory coordinate contributes opaque artifacts from one explicit local
root. It does not decide which artifact is an inspection target, decode managed
metadata, resolve dependencies, assign workspace roles, or establish binding
precedence. A directory is an artifact source, not an assembly context.

The first implementation slice is deliberately top-level and source-neutral.
It applies a finite host-supplied extension allow list and exact relative-name
exclusions, but does not infer semantic kind from either. The default allow
list is `.dll`, preserving the existing loose-directory input shape while
making no claim that matching bytes are a managed assembly. A host inspecting
one exact assembly can exclude that file from the directory batch because it
was already acquired through the explicit-file coordinate. Recursion and
richer entry selection require a later contract rather than becoming hidden
defaults.

The target peer API is
`LocalArtifactSource.AcquireDirectoryAsync(scope, path, options,
cancellationToken)`. `LocalDirectoryArtifactAcquisitionOptions` contains:

- `ExcludedFileNames`, an `IReadOnlyCollection<string>` copied before the first
  await;
- `IncludedFileExtensions`, an `IReadOnlyCollection<string>` copied before the
  first await and defaulting to `.dll`;
- `MaxSelectionInputs`, defaulting to 1,024 values in each selection
  collection;
- `MaxObservedEntries`, defaulting to 1,024 observed top-level entries;
- `MaxSelectedFiles`, defaulting to 1,024 selected files;
- `MaxFileBytes`, defaulting to 512 MiB; and
- `MaxTotalBytes`, defaulting to 512 MiB across selected files.

The limits are local-adapter defaults rather than references to
`ArtifactSetSessionLimits`; the package-free local project must not depend on
the workspace implementation. Each limit must be positive, each byte limit must
fit the current array-backed snapshot implementation, and each selection
collection count must not exceed `MaxSelectionInputs`. Validation and the
defensive selection copies complete before platform, path, or filesystem work.
`LocalDirectoryAcquisition_InvalidOptionsThrowBeforePlatformPathOrFileSystemAccess`
gates that argument-error boundary.

Those defaults are standalone adapter ceilings, not safe compositional
allowances. A host adding a directory batch to a workspace must map the
supplemental capacity reserved by
[#5010](https://github.com/richlander/dotnet-inspect/issues/5010) into options:
`MaxSelectedFiles` cannot exceed remaining artifact-count capacity,
`MaxFileBytes` cannot exceed remaining per-artifact capacity, and
`MaxTotalBytes` cannot exceed remaining retained-byte capacity after required
artifacts and earlier supplemental batches. `MaxObservedEntries` remains an
enumeration-work bound independent of publishable capacity, and
`MaxSelectionInputs` independently bounds defensive option copying. The host
constructs and validates these options before enrolling its supplemental
acquisition callback. Without a reservation, that callback is not enrolled:
reservation denial remains a typed workspace result rather than an invalid
adapter option or a whole-generation overrun discovered only at `SealAsync`.

Exclusions are source-selection inputs, not artifact identities. Each must be
one non-rooted file name with no directory separator or parent traversal. The
adapter matches exclusions with `StringComparer.OrdinalIgnoreCase` on every
platform so a caller's lexical casing cannot reacquire the same explicit file
from a case-insensitive volume. It records the filesystem spelling it actually
observed. On a case-sensitive volume, this deliberately excludes every
case-only spelling of that candidate name. A host that requires case-distinct
inputs acquires each exact file explicitly rather than relying on directory
candidates. Hard links and other physical aliases are not durable artifact
identity and are not collapsed by this adapter. Excluded entries still count
toward the observed-entry limit because discovering them consumes enumeration
work, but they consume neither selected-file, file-copy, nor aggregate-byte
budget.
`LocalDirectoryAcquisition_ExcludesExplicitNamesCaseInsensitivelyWithoutGrantingDesignation`
gates both the casing rule and the absence of a designation grant.

Each included extension is a non-empty extension such as `.dll`, not a glob or
path. Extension matching is ordinal and case-insensitive on every platform so
the selection does not change when the same artifact set moves between Windows
and Unix. An empty allow list is invalid rather than an implicit request for all
files. Each selection collection count is bounded by `MaxSelectionInputs`;
duplicate extensions and exclusions collapse under their respective comparers.

Acquisition follows this order:

1. Validate the options and make defensive selection copies.
2. On Browser, Wasm, or another unsupported host, return
   `local.directory.platform-unsupported` before path, filesystem, or native
   access.
3. Canonicalize the requested root, then repeatedly apply
   `Path.TrimEndingDirectorySeparator` until no non-root trailing separator
   remains.
4. Verify that the normalized root is an existing directory not observed as a
   link or reparse point.
5. Enumerate top-level entries incrementally. Count every observed entry before
   classifying it and stop with a typed rejection as soon as
   `MaxObservedEntries` is exceeded.
6. For an enumeration within the limit, derive one direct-child relative name
   per entry and sort by that observed name with `StringComparer.Ordinal`.
   Underlying filesystem enumeration order is never publication order.
7. Ignore every entry observed as an ordinary directory before filename
   selection because recursion is not enabled. Apply extension selection and
   exclusions to the remaining names. Reject the batch before file-kind probes
   or copies as soon as `MaxSelectedFiles` is exceeded.
8. Before opening a selected path, require a platform file-kind probe to
   establish that the observed entry is a regular file rather than a symbolic
   link, reparse point, device, socket, or pipe. Reject the selected entry if
   its regular-file kind cannot be established.
9. Open and copy each selected file in sorted order. Check its initial length
   against the per-file limit first and then the remaining aggregate budget
   before allocating its snapshot. Enforce both remaining limits in that order
   in the read loop even when the initial length was within them, then subtract
   the exact copied length with checked arithmetic.
10. Register no contribution until every selected file has an adapter-private
    immutable snapshot. Then register the complete sorted batch in one
    contribution scope. Registration or outcome construction failure aborts
    the scope; no contribution from that failed batch may publish.

An existing directory with no selected files returns `Acquired` with an empty
artifact list and `ArtifactAcquisitionLeases.None`. That is a successful answer
to an optional candidate-source coordinate, not a shortened batch: there was no
selected entry to omit and no contribution was registered. It must not be
passed to `ArtifactSetSession.AddRequiredAcquisitionAsync`, which deliberately
turns an empty acquired batch into a required-member failure. The workspace
adoption tracked by
[#5010](https://github.com/richlander/dotnet-inspect/issues/5010) must provide
a supplemental acquisition path before it can compose these candidate batches;
this local-owner design does not define that adjacent workspace API.
`LocalDirectoryAcquisition_NoMatchesReturnsEmptyBatchWithoutRegistration`
gates the empty adapter outcome and proves that it mints no contribution.

`LocalDirectoryArtifactDiagnostic` carries the requested path, nullable
canonical root, and nullable observed relative name separately from its stable
code and non-artifact summary. The unsupported-platform outcome has no
canonical root because it precedes path access; every outcome after platform
admission records the normalized canonical root.

| Condition | Outcome | Diagnostic code |
| --- | --- | --- |
| Root does not exist | `Unavailable` | `local.directory.missing` |
| Root is not a directory | `Rejected` | `local.directory.invalid-root` |
| Host does not support local-directory acquisition | `Failed` | `local.directory.platform-unsupported` |
| Root or selected entry is a link/reparse point | `Rejected` | `local.directory.link` |
| Selected name is not an ordinary file | `Rejected` | `local.directory.unsupported-entry` |
| Observed entry count exceeds the limit | `Rejected` | `local.directory.entry-limit` |
| Selected file count exceeds the limit | `Rejected` | `local.directory.selected-file-limit` |
| Selected file exceeds its byte limit | `Rejected` | `local.directory.file-size-limit` |
| Selected files exceed the aggregate limit | `Rejected` | `local.directory.total-size-limit` |
| Enumeration or attribute inspection fails | `Failed` | `local.directory.enumeration-failed` |
| A selected file disappears or cannot be copied | `Failed` | `local.directory.entry-read-failed` |

Cancellation remains `OperationCanceledException` and is never translated into
a diagnostic arm.

Each contribution records a `LocalDirectoryArtifactProvenance`:

- the canonical requested root;
- the direct-child relative name as observed;
- the full observed entry path;
- the exact copied length; and
- the last-write observation from the opened file handle.

The artifact kind is `local-directory-entry`; the entry name, matched extension,
and bytes do not establish media or semantic kind. The artifact session still
performs its own second bounded copy and may enforce stricter limits. Adapter
acceptance is therefore not by itself a promise that a containing session will
publish. The supplemental-reservation precondition above prevents this batch
from being the reason a containing session first discovers a count or byte
overrun during seal.

The adapter establishes an immutable batch of the bytes it actually copied,
not a transactional filesystem snapshot. A file created after enumeration is
outside that acquisition. A selected file observed to disappear or become
unreadable before its snapshot completes fails the whole acquisition rather
than shortening it. A selected file observed as a link or non-regular entry is
rejected before ordinary file open. Replacement before open may nevertheless
be the content captured; mutation after the file's copy cannot change the
retained snapshot.

Rejecting links prevents ordinary accidental traversal outside the selected
root when the root and selected entry remain stable through their probes. The
implementation must use NativeAOT-compatible platform file-type evidence rather
than managed `FileAttributes` alone: on Unix a FIFO can report normal
attributes and zero length, then block during open. A stable FIFO or other
non-regular selected entry must therefore be rejected promptly before that
ordinary open. On Unix, an `lstat`-style mode probe can establish regular-file
kind without opening the path; Windows uses its file attributes and reparse
metadata.

The first slice supports Windows, Linux, and macOS local filesystems without a
new package dependency. Unix classification uses a local private
`LibraryImport("libSystem.Native")` binding to the runtime's normalized
`SystemNative_LStat` result, not a raw libc `struct stat` layout. This is the
NativeAOT-compatible runtime-shim pattern used by the runtime itself and by
`PhysicalFileIdentityProvider` for `SystemNative_FStat`; the local adapter owns
its private binding rather than depending on the CLI implementation.

The shim converts each host `struct stat` into a fixed-width `FileStatus`;
`SystemNative_LStat` and the runtime's managed `Interop.Sys.FileStatus` contract
carry file mode independently of host struct layout and process bitness. The
adapter reads only that normalized mode. It therefore does not inherit
`PhysicalFileIdentityProvider`'s conservative 64-bit guard, which protects a
different device/inode identity claim.

Browser and Wasm direct calls, and calls on operating systems other than
Windows, Linux, and macOS, return `local.directory.platform-unsupported` before
path, filesystem, or native access. This visible failure affects only the local
directory API; explicit in-memory and other artifact sources remain available.
`BrowserLocalDirectoryAcquisition_RelativePathReturnsUnsupportedBeforePathFileSystemOrNativeAccess`
gates a relative Browser path, and
`LocalDirectoryPlatformPolicy_UnknownHostReturnsUnsupported` pins the complete
supported-host allow list and its unknown-host result.
`LocalDirectoryAcquisition_TrailingSeparatorRootLinkIsRejected` supplies a
stable Unix directory symlink with a trailing separator and gates normalized
root probing.
`LocalDirectoryAcquisition_StableNonRegularEntryRejectsBeforeOpen` gates this
with a stable Unix FIFO under an outer process deadline and asserts typed
rejection with no registered contribution.
`NativeAotLocalDirectoryProbe_AcquiresRegularFileAndRejectsNonRegularEntry`
publishes and runs a package-free local-adapter fixture for the current
NativeAOT RID under an outer process deadline, with timeout as gate failure;
Unix exercises a regular file and FIFO, while Windows exercises a regular file
and reparse point.

This is not a hostile-local-mutation guarantee. Portable .NET APIs do not
provide an atomic cross-platform "enumerate, classify, and open this child
without following a link" operation. A caller able to replace an entry between
the file-kind probe and open is already inside the local-machine trust boundary
described by the threat model. Such a replacement may be followed or may block
before cancellation is observable; the count and byte limits do not claim a
wall-clock deadline for that concurrent-local-mutation case. The adapter
records and snapshots what its handles observe, fails visible races when the
platform reports them, and makes no stronger containment claim.

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

The implementation successor is one local-adapter PR: add this API, options,
provenance, diagnostics, and its focused tests without changing
`AssemblySetResolver`, CLI defaults, assembly projection, or binding policy.
Adopting the API in those callers is a later owner-crossing migration after
[#5010](https://github.com/richlander/dotnet-inspect/issues/5010) provides
supplemental workspace acquisition and the artifact-to-assembly projection can
consume the batch. That migration may choose which directories a scenario
authorizes; it may not weaken this adapter's bounds or reinterpret directory
provenance as caller designation.

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

The workspace owns one or more artifact set sessions and one or more assembly
context groups. When an authorized query plan first demands a context, the
artifact owner issues its admission lease; the context loader constructs and
seals a session from all required acquisitions for that context, then creates
its group. Loading a definition alone performs none of that work. Retained hosts
may repeat the authorized operation to add contexts. Groups compose projected
assembly participants under one binding policy and may span artifact sources
within their session.

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
   core assembly Queries into optional adapters or companion projects, and make
   retained workspaces own multiple sealed artifact sessions.
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

Each slice must preserve current visible diagnostics and selection semantics.
The migration does not justify a success-shaped fallback or an unbounded eager
materialization.

## Required gates

The target is complete only when tests equivalent to these exist:

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
- `WorkspaceSupplementalReservation_ConstrainsDirectoryOptionsBeforeCallbackEnrollment`
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
- `ArtifactSetSession_ReleasesLeasesOnlyAfterDependentGroupsQuiesce`
- `ArtifactSetSession_PreservesPrimaryFailureWhenCleanupFails`
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
- `LocalDirectoryAcquisition_InvalidOptionsThrowBeforePlatformPathOrFileSystemAccess`
- `LocalDirectoryAcquisition_TopLevelBatchIsDeterministicAndSourceNeutral`
- `LocalDirectoryAcquisition_EntryLimitBoundsEnumerationBeforeClassification`
- `LocalDirectoryAcquisition_ObservedAndSelectedLimitsRemainIndependent`
- `LocalDirectoryAcquisition_SelectionInputLimitIsIndependentOfSelectedCapacity`
- `LocalDirectoryAcquisition_ByteLimitsRejectWithoutPublishingAPartialBatch`
- `LocalDirectoryAcquisition_NoMatchesReturnsEmptyBatchWithoutRegistration`
- `LocalDirectoryAcquisition_TrailingSeparatorRootLinkIsRejected`
- `LocalDirectoryAcquisition_StableNonRegularEntryRejectsBeforeOpen`
- `NativeAotLocalDirectoryProbe_AcquiresRegularFileAndRejectsNonRegularEntry`
- `BrowserLocalDirectoryAcquisition_RelativePathReturnsUnsupportedBeforePathFileSystemOrNativeAccess`
- `LocalDirectoryPlatformPolicy_UnknownHostReturnsUnsupported`
- `LocalDirectoryAcquisition_LinkOrEntryFailureCannotShortenTheBatch`
- `LocalDirectoryAcquisition_ExcludesExplicitNamesCaseInsensitivelyWithoutGrantingDesignation`
- `LocalDirectoryAcquisition_ProvenancePreservesRootNameAndSnapshotObservation`
- `LocalDirectoryAcquisition_CancellationRemainsCancellation`
- `ArtifactAcquisition_CancellationRemainsCancellation`
- `RequiredMember_EmptyOrNonProjectableAcquisitionFailsContext`
- `RequiredAcquisitionFailure_DoesNotShortenWorkspaceContext`
- `AssemblyContextGroup_CanBindParticipantsFromDifferentArtifactSources`
- `RetainedWorkspace_CanAddASecondSealedContextGeneration`
- `PackageAdapter_ProjectsSelectedEntriesWithoutLeakingPackageTypes`
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
missing/limit diagnostics, mutation and deletion resistance, and cancellation
remaining cancellation. The named `LocalDirectoryAcquisition_*` gates remain
unverified. Together they require bounded top-level enumeration, ordinal
publication order, independent observed-entry, selected-file, and selection
input limits, source-neutral files, case-insensitive exclusion without
designation, directory-specific provenance, immutable snapshots, empty success
without registration when no names match, trailing-separator root-link
rejection, prompt rejection of stable non-regular entries in managed and
NativeAOT hosts under process deadlines, Browser non-vacuity, a closed
supported-host set with typed unsupported-platform failure before path,
filesystem, or native access, rejection without partial publication for links
or limits, visible entry failure, and cancellation preservation.
`LocalOnlyHost_InspectsCallerSuppliedLocalAssembly`
deletes its temporary source after publication, then passes an
`ArtifactContentReference`'s guarded published snapshot opener to Metadata, so
a source-path fallback cannot satisfy the gate.

Workspace-wide admission budgets, including the supplemental reservation that
must constrain directory options before adapter invocation,
single-flight/reentrancy, directory acquisition, content digests,
dependent-group quiescence, and Metadata consumption of workspace roles remain
unverified.

## Non-goals

- Defining a universal package session.
- Treating every archive as a package.
- Making storage infer semantic identity from filenames or paths.
- Replacing assembly context groups with artifact sets; artifact lifetime and
  assembly binding remain separate axes.
- Requiring every workspace artifact to be an assembly.
- Scraping arbitrary deployed Wasm applications for runtime assemblies. A
  cooperating application may supply an explicit manifest or adapter, but
  framework-version-specific boot-resource discovery is not a general source
  contract.
- Implementing Azure DevOps or GitHub Actions acquisition in the first slice.
- Changing user-visible CLI commands in the design PR.
