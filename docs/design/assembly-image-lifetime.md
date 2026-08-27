# Assembly image lifetime and MVID correctness

This document owns the correctness contract for one
`AssemblyInspectionSession`: which assembly bytes its readers inspect, how long
those bytes remain stable, what an MVID proves, and which cache lifetimes may
reuse the resulting metadata.

It is a focused sub-contract of the
[assembly inspection query](assembly-inspection-query.md) design. Artifact
acquisition still owns how content is obtained, storage owns retained bytes,
workspace composition owns admission and binding, and Metadata owns facts
decoded from the retained image. This document owns only the image lifetime
between those boundaries.

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
- session-scoped metadata addresses and caches;
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
global artifact identity. MVID-scoped row addresses remain inside the owning
image generation. Artifact generation or immutable source coordinate supplies
the outer scope.

## Source stability

### nuget.org packages

The primary product scenario is an exact package version from nuget.org.
The design's external operating assumption is that nuget.org package content
is immutable for one package ID and normalized version: the owner may unlist
that version, but it cannot replace its payload. Repository gates do not prove
this service property.

An exact nuget.org package coordinate can therefore support reacquisition or
cache reuse across tool runs. The cache identity must still include the source,
normalized package ID and version, and selected asset path; an assembly MVID is
not the package cache key.

Two different immutable package assets could accidentally carry the same MVID,
but their package and asset scopes remain different, so they do not alias in
this design. The low collision probability matters only to MVID's
defense-in-depth value when detecting an address presented at the wrong
boundary. It is not a reason to add an eager content digest to every
inspection.

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
Run-local caches may key by the owning image generation and may retain row
addresses only while that generation remains alive.

### Other sources

Project outputs, platform installations, CI artifacts, and non-nuget.org
package sources must declare whether their coordinate is immutable. Without
that contract they receive the local rule: one retained image per run and no
cross-run derived-result reuse.

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

### Metadata addresses stay generation-scoped

`MetadataMethodAddress` is MVID plus MethodDef row. A consumer reopening a
reader over the same retained image revalidates both before dereferencing the
row. The address does not outlive the artifact generation, enter a global
cache, or authorize a read from another acquired artifact.

Cross-image API correspondence remains a separate operation. It may produce a
new address in the target image; it does not make the source address portable.

### Cache lifetime follows source stability

Reader-local and session-local caches end with their image generation.

Cross-run cache reuse is allowed only when the cache owner has a source
coordinate whose contract makes the payload immutable, such as exact
`package@version` from nuget.org. The complete immutable coordinate and asset
selection scope the entry. Dynamic authorization and policy are still checked
for the current operation.

Local assembly facts are recomputed in each tool run. A future persistent local
cache would require a separately approved content-identity design; it is not
part of this contract.

### Failures remain visible

An inspection fails rather than:

- reopening a mutable path after its retained image is unavailable;
- falling back from a rejected scoped address to overload ordinal or raw token;
- reusing a local result from another tool run;
- treating an MVID as proof that different acquisition generations contain
  equal bytes.

## Consequences for current work

### PR #4623

Acquisition registration needs to retain one immutable image for the consuming
session. It does not need collision-resistant content identity solely to defend
against deliberately duplicated MVIDs.

For nuget.org, immutable package coordinate plus selected asset path provides
the outer reusable scope. For local content, the retained per-run snapshot
provides the scope and derived results do not survive the run.

This narrows the collision requirement raised in PR #4623: correctness requires
generation-scoped bytes and addresses, not treating arbitrary inspected
metadata as hostile identity material.

### PR #4627

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

## Required gates

Existing gates prove narrower pieces:

- `Session_ParsesTheBytesCopiedBeforeSourceMutation`;
- `RootAndStrictRegistration_ShareOneImmutableImage`;
- `LocalArtifactSnapshot_MutationCannotChangeInspectionBytes`;
- `LocalOnlyHost_InspectsCallerSuppliedLocalAssembly`.

The complete contract remains unverified until equivalent gates exist for:

- `AssemblyInspectionSession_OneImageFeedsEveryProducer`;
- `MemberSourceCorrespondence_UsesTheSessionRuntimeImage`;
- `MetadataMethodAddress_CannotCrossImageGeneration`;
- `LocalAssemblyFacts_DoNotEnterACrossRunCache`;
- `NuGetOrgCacheIdentity_IncludesSourceCoordinateAndAssetPath`;
- `MutablePackageSource_DefaultsToRunLocalImageLifetime`.

The gate names describe required outcomes, not prescribed test classes or an
implementation sequence.

## Non-goals

- Requiring an eager digest for every assembly.
- Treating MVID as artifact identity or security evidence.
- Detecting deliberate MVID collisions.
- Defining package-feed immutability for sources other than nuget.org.
- Adding a persistent cache for local assembly facts.
- Moving acquisition, binding, or producer semantics into
  `AssemblyInspectionSession`.
