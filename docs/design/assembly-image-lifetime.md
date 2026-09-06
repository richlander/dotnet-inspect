# Assembly image lifetime and MVID correctness

This document owns the correctness contract for one
`AssemblyInspectionSession`: which assembly bytes its readers inspect, how long
those bytes remain stable, what an MVID proves, and which outer lifetime scopes
cache owners may use.

It is a focused sub-contract of the
[assembly inspection query](assembly-inspection-query.md) design. Artifact
acquisition still owns how content is obtained, storage owns retained bytes,
workspace composition owns admission and binding, and Metadata owns facts
decoded from the retained image. This document owns only the image lifetime
between those boundaries.

Persistent derived-result ownership remains split. Each result's cache owner
defines its complete semantic key and reuse policy;
[artifact acquisition](artifact-acquisition-and-workspaces.md#artifactsetsession)
supplies any content digest from retained bytes; and
[CoreCache](../inspection-space.md#corecache) supplies infrastructure plus the
repository-wide cutover constraints. This document neither replaces nor
weakens those contracts.

The target contract is **unverified** until the gates under
[Required gates](#required-gates) exist. Existing snapshot gates establish
parts of it; they do not establish the complete session contract.

## Decision

One assembly inspection session uses one immutable assembly image for its
entire lifetime.

For a command-line invocation, that session lifetime is contained by one tool
run. A retained host may keep a session through one sealed workspace or
artifact generation, but admitting a replacement local build creates a new
generation and new sessions.

Every metadata reader, method-body reader, PDB/SourceLink correlation, and
decompiler input participating in one inspection must consume that same image.
A filesystem path may describe where acquisition found the image; it is not
permission to reopen a potentially different file during the session.

If an owner cannot provide repeatable access to the same bytes, it must retain
one bounded snapshot before inspection begins or fail visibly. Comparing a
file before and after separate opens is not equivalent: a moving path can
change from A to B and back to A between observations.

## Owner and boundaries

`AssemblyInspectionSession` owns:

- the retained assembly image used by all inspection producers in the session;
- the lifetime of readers over that image;
- bound metadata addresses and caches during the session;
- visible failure when the retained image cannot remain available.

It consumes:

- a `ResolvedAssemblyReference` and owner-authorized content access;
- an artifact or workspace generation that keeps that access alive.

It does not own:

- package coordinates, feeds, extraction, or package immutability policy;
- local path enumeration or filesystem trust policy;
- assembly binding or workspace participant selection;
- cryptographic artifact identity;
- hostile-input authentication based on an MVID;
- producer-specific metadata, IL, PDB, SourceLink, or decompiler semantics.

## Identity vocabulary

These values answer different questions:

| Value | Answers | Does not answer |
| --- | --- | --- |
| Source coordinate | What acquisition request selected the artifact | Which bytes a live reader owns |
| Artifact generation identity | Which admitted content item and lifetime own access | Managed assembly identity |
| Assembly identity | What the assembly metadata declares | Whether two byte sequences are equal |
| MVID | Which ordinary module generation produced a metadata address | Cryptographic content identity or hostile authenticity |
| MVID plus metadata token | Where to re-locate one row in the same module generation | Cross-module correspondence |
| Content digest | Whether two complete byte sequences hash equally under the selected algorithm | Workspace admission, binding, or module-row meaning |

An MVID is useful because ordinary compilers generate a new value for a new
module build. It makes an accidental stale row address overwhelmingly likely
to fail when compared with another ordinary build. It is not a security
boundary, and this design does not require it to resist a producer that
deliberately emits the same MVID for different bytes.

No cache or correspondence operation may therefore treat an MVID alone as a
global artifact identity. Bound handles and dereference authority remain inside
the owning image generation. A durable MVID-scoped address value may cross that
lifetime, but it becomes useful again only when its owner binds it to an
authorized artifact generation and revalidates the MVID, table, and row.
Artifact identity or immutable source coordinate supplies the outer scope.

## Source stability

### nuget.org packages

The primary product scenario is an exact package version from nuget.org.
The design's external operating assumption is that nuget.org package content
is immutable for one package ID and normalized version: the owner may unlist
that version, but it cannot replace its payload. Repository gates do not prove
this service property.

An exact nuget.org package coordinate can therefore support payload
reacquisition or retained-package reuse across tool runs. That outer scope
includes the source, normalized package ID and version, and selected asset
path; an assembly MVID is not the package cache key. A persistent derived-result
cache additionally follows its owner's digest and semantic-key contract.

Two different immutable package assets could accidentally carry the same MVID,
but their package and asset scopes remain different, so they do not alias in
this design. The low collision probability matters only to MVID's
defense-in-depth value when detecting an address presented at the wrong
boundary. It is not a reason to add an eager content digest to every
assembly session merely to authenticate the MVID. A persistent derived-result
cache may independently require a digest.

This immutability assumption is specific to nuget.org. Another package source
may opt into the same behavior only through an explicit immutable-coordinate
contract. Otherwise its assembly payload is run-local, like mutable local
content.

### Local binaries

A local binary is expected to move: rebuilds, atomic replacement, cleanup, and
symlink retargeting are ordinary development behavior.

The local adapter or assembly session therefore captures one bounded image and
uses it throughout the tool run. Later reads do not reopen the original path.
If another operation wants the rebuilt binary, it starts a new image
generation.

Derived metadata for a local binary is not cached across tool runs. A path,
timestamp, length, assembly identity, or MVID does not authorize such reuse.
Run-local caches may key by the owning image generation.

A durable address supplied to a later run is input, not a cached result. The
owner captures a fresh local image and revalidates the address against it before
dereference.

### Other sources

Project outputs, platform installations, CI artifacts, and non-nuget.org
package sources must declare whether their coordinate is immutable. Without
that contract they cannot reuse payload merely by coordinate. A persistent
derived-result cache remains subject to its owner-computed content digest and
source-specific policy.

This document does not define those source-specific coordinates. It defines
the conservative assembly-session behavior when their owners provide no
stronger stability guarantee.

## Correctness rules

### One image feeds every producer

Opening metadata, then reopening the same path for method bodies or PDB source,
creates two image generations even when both opens usually return the same
bytes. The second producer must instead borrow the session image or obtain a
reader from an owner that guarantees the same retained bytes.

The rule applies to PDB correspondence as well as IL:

1. resolve the selected API member against the retained runtime image;
2. keep the resulting module-scoped address inside that image generation;
3. use the same retained runtime image for body state and PDB lookup.

Rechecking only the MVID after reopening a moving path is a diagnostic
improvement, not the lifetime solution. A single retained image removes the
time-of-check/time-of-use gap and also handles a deliberately repeated MVID
without needing to classify it as hostile.

### Address values are portable; dereference authority is not

`MetadataMethodAddress` is MVID plus MethodDef row. The value may be rendered or
persisted. It does not carry artifact identity or permission to open content.

Inside a live session, a consumer binds the address to a reader over the same
retained image and revalidates both fields before dereferencing the row. In a
later context, an acquisition or workspace owner first authorizes and retains
the candidate artifact; Metadata then validates MVID, table, and row against
that reader. A mutable local path is captured afresh rather than reopened as a
continuation of the prior session.

Cross-image API correspondence remains a separate operation. It may produce a
new address in the target image; address equality alone does not establish
correspondence between artifacts.

### Cache lifetime follows source stability

Reader-local and session-local caches end with their image generation.

Persistent derived-result caches follow the contract in
[inspection-space.md](../inspection-space.md#corecache): the result owner
defines the semantic key, acquisition computes any content digest over retained
immutable bytes, and the cold gate, producer, and publication use that same
snapshot. The immutable nuget.org coordinate scopes reacquisition and
provenance; it does not replace the digest in the current derived-cache
contract. Dynamic authorization and policy are still checked for the current
operation.

This design adds a stricter source-lifetime decision for local assemblies:
their derived facts are recomputed in each tool run even when a digest could
make a persistent entry content-correct.

### Failures remain visible

An inspection fails rather than:

- reopening a mutable path after its retained image is unavailable;
- falling back from a rejected scoped address to a raw token,
  name/signature/selector lookup, or overload ordinal;
- reusing a local result from another tool run;
- treating an MVID as proof that different acquisition generations contain
  equal bytes.

## Consequences for successor work

### Acquisition registration successors

PR [#4623](https://github.com/richlander/dotnet-inspect/pull/4623)
historically surfaced the acquisition and collision question, then closed
without merge in favor of the focused sequence tracked by
[#4867](https://github.com/richlander/dotnet-inspect/issues/4867).
[#4606](https://github.com/richlander/dotnet-inspect/issues/4606) owns the
first acquisition defect in that sequence.

Acquisition registration needs to retain one immutable image for the consuming
session. It does not need collision-resistant content identity solely to defend
against deliberately duplicated MVIDs.

For nuget.org, immutable package coordinate plus selected asset path provides
the outer reacquisition and provenance scope. For local content, the retained
per-run snapshot provides the scope and derived results do not survive the run.
If the nuget.org path publishes a persistent derived result, the existing cache
owner still requires a digest computed over the retained snapshot. The local
path does not perform persistent lookup or publication.

This narrows the collision requirement inherited from the superseded broad PR:
correctness requires generation-scoped bytes and addresses, not treating
arbitrary inspected metadata as hostile identity material.

### Cross-image PDB composition successor

PR [#4627](https://github.com/richlander/dotnet-inspect/pull/4627)
historically surfaced the raw-token reopen defect, then closed without merge in
favor of focused prerequisites. Issue
[#4603](https://github.com/richlander/dotnet-inspect/issues/4603) owns the thin
CLI/PDB composition outcome after those prerequisites.

Reference-to-runtime method correspondence must not reduce its target to a raw
MethodDef token and then reopen the runtime path for source lookup. The runtime
image used for correspondence, body-state inspection, and PDB correlation is
one session image.

Retaining and revalidating `MetadataMethodAddress` remains useful at reader
boundaries inside that session. It is not a substitute for retaining the image.

## Threat model

In scope:

- an ordinary local rebuild or atomic replacement during a command;
- stale row addresses crossing reader boundaries;
- accidental MVID mismatch between ordinary builds;
- cache reuse outside the source coordinate or image generation that
  authorized it.

Out of scope:

- a producer deliberately creating two byte-distinct modules with the same
  MVID;
- using MVID as cryptographic proof or a package authenticity mechanism;
- defending in-process reflection or memory corruption;
- making every source globally content-addressed.

The out-of-scope collision does not permit a moving-path shortcut. The session
still retains one image, so every producer in that session sees the same bytes
even when those bytes carry an intentionally reused MVID.

This non-goal is narrow. Existing limits and visible failures for malformed or
adversarial metadata remain required. Only deliberate same-MVID,
different-content construction is excluded from the identity guarantee.

## Current mismatch

The default `effective-v28` library catalog currently persists facts for direct
local-file inspection across tool runs. Its key includes the resolved path and
a SHA-256 digest computed during a separate source open. That rejects ordinary
stable replacement builds, but it does not prove that the later cold producer
read the hashed bytes; the source can change between opens. The
retained-snapshot cutover for that pre-existing cache correctness gap is
unverified and tracked by [#3478](https://github.com/richlander/dotnet-inspect/issues/3478).

The implementation must disable or remove that cross-run cache route for local
subjects rather than carry its separate-process direct-file requirements into
the successor cache. Package and platform routes retain that digest and snapshot
contract. This PR records the mismatch; it does not change shipping
cache behavior.

## Required gates

Existing gates prove narrower pieces:

- `Session_ParsesTheBytesCopiedBeforeSourceMutation`;
- `RootAndStrictRegistration_ShareOneImmutableImage`;
- `LocalArtifactSnapshot_MutationCannotChangeInspectionBytes`;
- `ArtifactDescriptor_PreservesRegistrationAndBindsNonEmptyMvid`;
- `ArtifactDescriptor_RejectsSameIdentityFromDifferentModuleGeneration`;
- `LocalOnlyHost_PreservesArtifactRegistrationThroughAssemblyInspection`.

The complete contract remains unverified until equivalent gates exist for:

- `AssemblyInspectionSession_OneImageFeedsEveryProducer`;
- `MemberSourceCorrespondence_UsesTheSessionRuntimeImage`;
- `MetadataAddress_RebindingRequiresOwnerAndMvidValidation`;
- `ScopedAddressRejection_CannotInvokeAlternateLocator`;
- `LocalAssemblyFacts_DoNotEnterACrossRunCache`;
- `NuGetOrgReacquisition_PreservesCoordinateAndAssetPath`;
- `MutablePackageSource_DefaultsToRunLocalImageLifetime`.

The gate names describe required outcomes, not prescribed test classes or an
implementation sequence.

## Non-goals

- Requiring an eager digest merely to retain one assembly session or validate
  an MVID-scoped address.
- Replacing the persistent derived-result cache contract.
- Treating MVID as artifact identity or security evidence.
- Detecting deliberate MVID collisions.
- Defining package-feed immutability for sources other than nuget.org.
- Adding a persistent cache for local assembly facts.
- Moving acquisition, binding, or producer semantics into
  `AssemblyInspectionSession`.
