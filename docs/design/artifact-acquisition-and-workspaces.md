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
quiescence. The target configurations pass safety and
liveness; three committed current-mechanics configurations produce
counterexamples showing an open can complete after `EndGeneration`, a
disposal racing `SealAsync` disposes acquisition leases under an active
materialization read, and leases can be released while a published
generation's query stream is open. Its `README.md` records the checked
bounds, results, assumptions, and the three termination-policy obligations it
exposes: a registered opener must be owner-interruptible (today
`OpenReadable` invokes a synchronous `Func<Stream>` with no cancellation or
bounded-completion contract), an in-flight materialization read must likewise
be owner-interruptible (today it awaits `Stream.ReadAsync` with only the caller
token), and abandoned returned query streams need a stated policy (bound the
wait, or invalidate visibly). These results establish evidence about the
model, not the implementation.

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
it is rejected as invalid. Any other non-empty path that cannot be normalized
is also rejected rather than escaping as a platform exception. Invalid-path
results carry the requested path and no canonical path.

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
above. Ordinary filesystem paths are inspected through managed attributes. For
a final reparse point, a metadata handle opened without following the point
queries its tag. Classification is tag-semantic rather than based only on the
name-surrogate bit:

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
Browser/Wasm as well as NativeAOT. If that gate fails on a supported target, the
platform design reopens; returning classification-unsupported is an operational
failure mode, not approval to ship an unsupported-platform degradation.

The implementation is complete when focused gates prove:

- canonicalization, including extended-path segment rejection, expected-kind
  mismatches, requested-path retention when canonicalization fails,
  final-target link following, dangling links, name-surrogate handling, special
  and unknown reparse rejection, audited data-bearing reparse treatment, and
  hard-link non-deduplication have the same semantics for every consuming local
  coordinate;
- stable FIFOs, sockets, devices, and their link aliases are rejected before a
  blocking content open, while an empty regular file remains admissible;
- a regular-file consumer receives the once-opened, post-classified handle and
  cannot reopen the coordinate;
- unavailable, rejected, failed, and cancellation results remain distinct; and
- the normalized `Stat` and `FStat` classifier compiles and runs under both
  NativeAOT and Browser/Wasm, while Windows gates cover disk files,
  directories, traversable links, reserved device names, named-pipe
  coordinates, allowed data-bearing reparse tags, every supported special-tag
  family, and an unknown tag.

These properties are represented by
`LocalPathAdmission_ExpectedKindsAndLinksAreShared`,
`LocalPathAdmission_StableNonRegularEntriesRejectBeforeOpen`,
`LocalPathAdmission_ConsumerReceivesTheVerifiedOpenGeneration`,
`LocalPathAdmission_OutcomesAndCancellationRemainDistinct`, and
`LocalPathAdmission_PlatformClassifiersRemainPortable`. They are unverified
until those gates exist. The bounded-directory implementation must exercise
the same contract through its public root and selected-entry outcomes rather
than adding another classifier.

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

An exact acquired package is a `PackageRootRealization` before compile-asset
selection succeeds. That host-neutral product result retains:

- exact package id and version;
- the requested target framework and the selector's selected framework, when
  either exists;
- the package content producer key and cache origin;
- the complete typed `PackageCompileAssetSelection`, including
  `NoCompileAssets`, `NoMatchingTargetFramework`, `EmptyCompileGroup`, and
  `InvalidImplementationAssets`.

Compile-library availability is a capability of that Root, not a precondition
for the Root to exist. The host workspace retains every requested Root.
`PackageAssemblyContextRealization` separately creates surface or
implementation assembly-context groups only for Roots whose selection status is
`Selected` and whose selected asset set is non-empty. It does not become a
package-root container. A workspace containing only Root-capable coordinates
has no assembly groups. A mixed workspace retains all Roots at the host
boundary while creating groups for selected coordinates only.

A host may project Root-owned facts such as exact identity, package documents,
or manifest dependencies from a Root-only coordinate. Assembly-backed
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
missing/limit diagnostics, mutation and deletion resistance, and cancellation
remaining cancellation. The three named `LocalDirectoryAcquisition_*` gates
remain unverified. Together they require bounded deterministic top-level
selection, source-neutral exclusions, atomic empty and failure outcomes,
directory provenance, immutable batch snapshots, and cancellation
preservation. Shared local-path admission remains with the
[local adapter](#shared-local-path-admission) rather than these directory
gates.
`LocalOnlyHost_InspectsCallerSuppliedLocalAssembly`
deletes its temporary source after publication, then passes an
`ArtifactContentReference`'s guarded published snapshot opener to Metadata, so
a source-path fallback cannot satisfy the gate.

`PackageAssemblyContextRealizationTests` enforce package Root retention,
producer/cache provenance, and assembly-group creation only for selected
compile assets. `BrowserEngineBoundaryTests` enforce the tools-v2 pointer and
explicit-empty-group cases, including typed compile-library absence, package
documents, manifest dependencies, and no fabricated default assembly.

Workspace-wide admission budgets, single-flight/reentrancy, directory
acquisition, content digests, dependent-group quiescence, and Metadata
consumption of workspace roles remain unverified.

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
