# Platform composition and overlays

How a workspace decides which assemblies constitute *the platform*, what may sit
on top of one, and what happens when the two disagree.

This document owns three questions that are easy to conflate and must not be:

| Question | Asked of | Answered when | Owner |
| --- | --- | --- | --- |
| **Entitlement** — may this acquisition speak for the core library? | one acquisition | at open | [untrusted-data-threat-model.md](untrusted-data-threat-model.md#core-library-identity-is-granted-by-acquisition-not-by-self-declaration) |
| **Precedence** — two entitled candidates define the same type; which wins? | a pair | at resolution | this document |
| **Compatibility** — can this platform base satisfy this overlay request? | overlay, base, request | risk at load; outcome at traversal | this document |

The entitlement **rule** is settled and enforced, though its carrier has a known
gap ([#4606](#known-gap-a-path-is-not-a-designation)). Precedence and compatibility
are specified here but **not yet implemented**; each names its tracking issue.
Where this document describes intent rather than current behaviour, it says so
at that point.

## What a platform is

A platform is a **coherent closure** — a set of assemblies that were built to go
together and can therefore be trusted to agree about types. In practice that is
a dotnet hive, a runtime pack, or a reference pack.

Coherence is the property that matters, and it is a property of the *set*, not
of any file in it. This is why no amount of inspecting an individual assembly
can establish it. A genuine, Microsoft-signed .NET 6 `System.Runtime.dll` is
authentic and, sitting beside a .NET 10 library, wrong. Authenticity is not
coherence.

## Installed implementation-platform realization

Issue [#5139](https://github.com/richlander/dotnet-inspect/issues/5139)
defines the platform-owned contract for realizing one exact installed
implementation-platform closure. It is the package-free installed counterpart
to remotely acquired implementation packs. It does not construct a workspace,
assign a platform role, or grant core-library trust. The implementation does
not yet satisfy this contract.

### Boundary

`InstalledPlatformRealization` consumes one owner-issued
`InstalledDotnetHiveIdentity`, one exact case-sensitive
`InstalledPlatformFamily`, one canonical package-neutral
`InstalledPlatformVersion`, and the `SharedFrameworkImplementation` layout
kind.

`InstalledPlatformFamily` is a portable, non-path-bearing value of at most 236
ASCII bytes. It begins and ends with a letter or decimal digit, otherwise
contains only letters, digits, `.`, `_`, or `-`, and its first dot-delimited
component is not case-insensitively `CON`, `PRN`, `AUX`, `NUL`, `COM1` through
`COM9`, or `LPT1` through `LPT9`. The ceiling keeps
`<family>.runtimeconfig.json` within the portable 255-byte component limit. No
path API constructs or normalizes this value before request validation
succeeds.

The optional desktop adapter mints the hive identity from one host-selected
dotnet root. Discovery and selection precede this contract; realization never
searches another root or substitutes `DOTNET_ROOT`, PATH, the current runtime,
or a priority rule for the owner-issued identity. Browser/Wasm does not
reference the desktop adapter and reports `UnsupportedHost` before path
normalization or filesystem work. This approved desktop-only exception covers
Windows, Linux, and macOS; reusable result and evidence contracts remain
NativeAOT-compatible and never load inspected code.
The adapter capability declares finite maximum member and byte bounds for the
workspace owner's aggregate reservation; a request may narrow but not exceed
those bounds. Adapter-capability and complete request validation, including
family, version, layout, and request limits, complete before adapter invocation,
path normalization, or filesystem work.

The requested root family and full canonical version text match ordinally and
never roll forward. SemVer precedence equality does not collapse versions with
different build metadata. Dependency references may roll forward under the
rules below. Reference packs, runtime packs, NuGet implementation packs, TPA
lists, loose directories, and other hives are different acquisition contracts,
not fallbacks.

### Closure contract

The selected framework's manifests are authoritative:

- `<family>.runtimeconfig.json`, when present, defines direct shared-framework
  dependencies; a valid configuration with no framework references or a
  definitive not-found result establishes a dependency-free leaf;
- `<family>.deps.json` is required and defines the exact managed members under
  the target named by `runtimeTarget.name`: every `runtime` asset plus the
  historical `native` asset whose projected leaf is exactly
  `System.Private.CoreLib.dll`.

Directory contents are not membership. Extra DLLs, native assets, resource
satellites, unrelated runtime targets, servicing or shared stores, application
probing, and hostpolicy arbitration do not enter this closure. The historical
CoreLib declaration is a managed-member candidate, not permission to admit any
other `native` asset. A missing manifest-declared member fails the realization.
This is a manifest-defined installed implementation closure, not launch-time
effective TPA.

Manifest coordinates remain contained beneath the selected framework
directory. Manifest bytes are bounded before parsing; `HardenedJson` owns
malformed-JSON and duplicate-property policy. A runtime-configuration probe,
open, or read failure other than definitive not-found is a typed failure, not
an empty dependency set.

Every framework name declared by a runtime configuration must construct the
same `InstalledPlatformFamily` value before dependency path normalization or
lookup. Invalid declared family text rejects the manifest. A valid root or
dependency family matches one frozen family-directory entry ordinally and
case-sensitively rather than inheriting host filesystem case folding.

Each participating dependency-manifest path is a logical asset coordinate. It
must be relative, use `/` separators, contain no empty, `.` or `..` segment,
and end in one valid file name. The installed member coordinate is that final
segment directly beneath the selected framework/version directory. Thus a
modern `System.Runtime.dll` and a legacy
`runtimes/linux-x64/lib/netcoreapp2.1/System.Runtime.dll` both select the
installed top-level `System.Runtime.dll`. Unsupported logical forms or two
logical assets that project to one installed coordinate reject before member
inspection.

Only reached framework families are inventoried. Each reached family's bounded
candidate inventory is frozen for the attempt; unrelated families are never
enumerated. Framework names compare ordinally and case-sensitively.
`InstalledPlatformVersion` provides SemVer 2 comparison without a package-layer
dependency. Invalid directory names are ignored, but two dependency candidates
with equal winning precedence reject as ambiguous rather than inheriting
enumeration order.

The root selection is exact. Dependency selection and multi-reference
reconciliation match the pinned
[.NET framework-version-resolution
contract](https://github.com/dotnet/runtime/blob/aa036afce592ad80e938a35bd376222fb232cba9/docs/design/features/framework-version-resolution.md),
including `rollForward`, `applyPatches`, release/prerelease preference,
compatibility-range narrowing, and propagation of highest-version behavior.
Configuration defaults affect only references declared by that configuration;
ordinary defaults are not inherited. Ambient command-line and environment
overrides are not inputs, and roll-forward-to-prerelease retains its disabled
host default. References to one family with equal SemVer precedence but
different canonical version text reject as ambiguous before reconciliation.
This deterministic product rule replaces hostfxr's argument-order-dependent
choice for that case.

The final selected graph and failure are independent of dependency, family,
version-directory, library, and participating-asset enumeration order. It contains
only dependencies reachable under the final reconciled references; dependencies
unique to a superseded selection do not survive. Cycles, duplicate
same-configuration references, incompatible requirements, or missing
dependencies fail atomically.

Resolution operates over finite bounded inventories and manifest graphs. It
must produce one fixed result or typed failure within the resolution-work
budget; this contract does not prescribe the fixed-point strategy.

Each selected platform member must be one contained regular file, classify as a
supported ECMA-335 assembly rather than native content, a netmodule, Windows
Metadata, or malformed metadata, and have assembly inspection's canonical
`AssemblyReferenceIdentity`. Distinct coordinates with one canonical assembly
identity reject the whole realization even when their bytes are equal.
Repeated platform member coordinates are likewise incoherent rather than
coalesced by discovery order.

One bounded immutable source snapshot per coordinate is the sole basis for
manifest evidence, member identity, and the admission handoff. Manifest
and member digests are platform-owned source-attestation evidence, not artifact
content identities. Each member retains artifact acquisition's source-specific
content lease over that exact snapshot; the realization exposes no raw path,
stream, opener, or mutable buffer. The platform owner never reopens or rehashes
the mutable source.

Local files may change freely between realizations; no source-stability claim
crosses that boundary. The coherence claim assumes that the selected installed
layout is not concurrently serviced or mutated during one realization. This
contract neither detects nor proves an attempt-wide atomic filesystem view.

### Result contract

A successful realization is immutable, bound/non-portable, and not an
interchange format. Each success mints a fresh opaque
`InstalledPlatformRealizationGenerationIdentity` that binds:

- the exact hive, request, reached-family inventories, and selected framework
  graph;
- each selected framework's manifest identities; and
- each member's supplying framework, manifest coordinate, canonical assembly
  identity, and source-attestation digest.

The generation identity and bound evidence form the proof required by #5139.
The proof and every member lease are issued as one owner-bound aggregate and
cannot be rebound across platform generations. Equal content in a later
realization receives a different generation identity. This discriminator lets
[issue #5115](https://github.com/richlander/dotnet-inspect/issues/5115)
reject stale, foreign, replayed, or mixed evidence under its own admission
contract. The platform generation is distinct from the artifact generation that
the workspace owner may later create and grants no workspace or artifact
authority. Absolute paths, handles, streams, openers, and mutable buffers do not
cross this boundary.

Frameworks are ordered dependency-first with ordinal family tie-breaking.
Members are ordered by canonical assembly identity's normalized
`Name`, numeric `Version`, normalized `Culture`, and normalized
`PublicKeyToken`, in that order, followed by the complete owner-issued
coordinate. Text comparisons are ordinal ignore-case except for the ordinal
coordinate tie-break; absent identity components sort before present ones.
Equivalent identities and repeated coordinates reject before ordering, so this
comparison is total over a successful result. Ordering is descriptive only and
never resolves ambiguity.

The
[explicit assembly-context owner](artifact-acquisition-and-workspaces.md#explicit-localdesignatedplatform-assembly-context)
validates this proof and consumes the source-specific content leases for
workspace admission, platform-role assignment, replay control, and group
publication. It composes, without redefining, artifact acquisition's identity
and lifetime contract,
[admission-scoped assembly projection](assembly-inspection-query.md#admission-scoped-artifact-projection),
and the
[core-library entitlement](untrusted-data-threat-model.md#core-library-identity-is-granted-by-acquisition-not-by-self-declaration)
contract.

### Failure contract

| Classification | Conditions |
| --- | --- |
| `UnsupportedHost` | The desktop adapter is unavailable; no filesystem work occurs |
| `Unavailable` | No `shared/` root; absent root family or exact root version; absent dependency family or compatible version |
| `Rejected(InvalidRequest)` | Invalid family, version, or layout; non-positive limit; request limit exceeding the adapter capability |
| `Rejected(InvalidAdapterCapability)` | Missing, non-positive, or unbounded adapter member or byte maximum |
| `Rejected(InvalidManifest)` | Missing dependency manifest; invalid dependency family; malformed, duplicate-bearing, or unsupported present runtime configuration or dependency manifest |
| `Rejected(InvalidFrameworkGraph)` | Duplicate reference, cycle, incompatible requirements, or equal-precedence candidate/reference ambiguity |
| `Rejected(InvalidMember)` | Unsupported logical asset path; escaping, invalid, repeated, or colliding projected member coordinate; missing asset; unsupported or malformed assembly; duplicate assembly identity |
| `Rejected(BudgetExceeded)` | Reached-family inventory, name, framework, resolution-work, manifest, member, or byte limit exceeded |
| `Failed` | Reached-family enumeration; runtimeconfig probe/open/read other than not-found; required dependency-manifest or member read |
| `Realized` | The complete framework and member closure plus its evidence |

Each arm carries a typed reason identifying the exact family, version,
coordinate, limit, or read stage; the listed conditions are distinct reason
arms rather than collapsible examples. Cancellation remains
`OperationCanceledException`. Every non-success result publishes no partial
realization, proof, or content lease.

### Demo

```text
request
  hive: H1
  family: Microsoft.AspNetCore.App
  version: 10.0.10
  layout: SharedFrameworkImplementation

realization
  frameworks:
    Microsoft.NETCore.App 10.0.12  dependency rolled to latest patch
    Microsoft.AspNetCore.App 10.0.10  exact root
  members:
    union of both exact dependency-manifest runtime sets
  proof:
    bound to H1, both manifests, and every member digest
```

The important difference from scanning only the requested directory is that
the ASP.NET Core result contains its required .NET runtime implementation
closure, while the requested root remains exact even when a dependency rolls
forward. A neighboring request for `Microsoft.NETCore.App` `10.0.10` contains
that exact root framework and its own transitive dependencies.

### Evidence

This contract adds no independently mutable platform-generation, scheduling,
retry, or concurrency protocol. Closure resolution is a terminating pure
function over finite frozen reached-family inventories and immutable snapshots;
source-specific lease and admission lifetime are already covered by the
[artifact-session admission model](../models/artifact-session-admission/README.md).
Later designated/platform arbitration remains covered by the
[platform-overlay model](models/platform-overlay-resolution/README.md). A new
TLA+ state model would duplicate those owners rather than test this local,
deterministic closure function.

The required Release evidence is:

Rejection gates derive their expected reason sets from the declarations so
both missing and stale cases fail.
The runtimeconfig-access gate likewise derives its probe, open, and read stages
from the declared failure set.
The invalid-capability and invalid-request early-rejection gates exercise the
complete realization entry point and independently observe adapter invocation,
path normalization, and filesystem work for every declared reason.
The invalid-dependency-family gate starts from an acquired root manifest and
independently observes dependency path normalization and dependency-family
filesystem work.
`InstalledPlatformRealization_FrameworkResolutionMatchesHostFxrOracle` covers
only the deterministic domain shared with hostfxr; product-defined ambiguity
rules are owned by their rejection gates.

| Claim | Named gate |
| --- | --- |
| Exact root and transitive framework closure | `InstalledPlatformRealization_ExactRootNeverRollsForward`, `InstalledPlatformRealization_AspNetCoreIncludesTransitiveCoreClosure`, `InstalledPlatformRealization_CoreRootUsesOnlyItsTransitiveClosure`, `InstalledPlatformRealization_CoreMembershipMatchesIndependentOracle` |
| Dependency-free leaf compatibility | `InstalledPlatformRealization_PresentRuntimeConfigWithoutFrameworkReferencesIsValidLeaf`, `InstalledPlatformRealization_MissingRuntimeConfigIsValidLeaf`, `InstalledPlatformRealization_RuntimeConfigAccessFailuresDoNotBecomeLeaf` |
| Host-compatible dependency resolution | `InstalledPlatformRealization_FrameworkResolutionMatchesHostFxrOracle`, `InstalledPlatformRealization_ReconcilesConvergingFrameworkReferences`, `InstalledPlatformRealization_RejectsEqualPrecedenceReferenceAmbiguity`, `InstalledPlatformRealization_PropagatesLatestVersionPolicyToDependencies`, `InstalledPlatformRealization_PreservesReleaseAndPrereleaseSelection` |
| Replacement and termination behavior | `InstalledPlatformRealization_LateReferenceReplacesPriorExpansion`, `InstalledPlatformRealization_LaterRestrictionRebuildsWithoutStaleDependency`, `InstalledPlatformRealization_OutcomesBudgetsAndCancellationRemainDistinct` |
| Manifest authority and deterministic membership | `InstalledPlatformRealization_ManifestRuntimeAssetsAreExact`, `InstalledPlatformRealization_LegacyCoreMembershipMatchesIndependentOracle`, `InstalledPlatformRealization_LegacyRuntimeAssetProjectsToInstalledLeaf`, `InstalledPlatformRealization_ProjectedMemberCoordinateCollisionRejectsAtomically`, `InstalledPlatformRealization_ResolutionAndMembersAreOrderIndependent`, `InstalledPlatformRealization_IgnoresUnreferencedFamilies` |
| No ambient or fallback authority | `InstalledPlatformRealization_IgnoresAmbientRollForwardOverrides`, `InstalledPlatformRealization_NeverFallsBackOutsideSelectedHiveOrLayout`, `InstalledPlatformRealization_FrameworkFamilyLookupIsOrdinal` |
| Declared rejection behavior | `InstalledPlatformRealization_InvalidRequestCasesRejectAtomically`, `InstalledPlatformRealization_InvalidAdapterCapabilityCasesRejectAtomically`, `InstalledPlatformRealization_InvalidManifestCasesRejectAtomically`, `InstalledPlatformRealization_InvalidDependencyFamilyRejectsBeforePathOrIo`, `InstalledPlatformRealization_InvalidFrameworkGraphCasesRejectAtomically`, `InstalledPlatformRealization_InvalidMemberCasesRejectAtomically`, `InstalledPlatformRealization_NonSuccessReturnsNoProofOrLiveLease` |
| Atomic identity and frozen-member handoff | `InstalledPlatformRealization_DuplicateAssemblyIdentityRejectsAtomically`, `InstalledPlatformRealization_MissingOrInvalidDependencyNeverShortensClosure`, `InstalledPlatformRealization_ProofBindsHiveGraphManifestsAndMemberContent`, `InstalledPlatformRealization_GenerationIsFreshAndProofLeaseBound`, `InstalledPlatformRealization_MemberLeaseReturnsExactFrozenSnapshot`, `InstalledPlatformRealization_SourceMutationDoesNotChangeRetainedMember`, `InstalledPlatformRealization_ProofExposesNoRawContentRoute` |
| Adapter capability bounds | `InstalledPlatformAdapterCapabilities_DeclareFinitePositiveBounds`, `InstalledPlatformRealization_RequestLimitsOnlyNarrowCapability`, `InstalledPlatformRealization_InvalidAdapterCapabilityCasesRejectBeforeAdapterPathOrIo`, `InstalledPlatformRealization_InvalidRequestCasesRejectBeforeAdapterPathOrIo` |
| Platform and dependency boundaries | `InstalledPlatformComposition_UsesDesktopAdaptersAndRejectsBrowserBeforeIo`, `BrowserPlatformComposition_DoesNotReferenceInstalledDesktopAdapter`, `InstalledPlatformAdapter_NativeAotPublishAndRun`, `InstalledPlatformAdapterClosure_ExcludesPackageAndNuGetImplementations`, `InstalledPlatformAdapterClosure_ExcludesInspectedAssemblyLoading`, `InstalledPlatformAdapter_ExcludesHostFxrInterop` |

### Non-claims

This design does not:

- discover or choose among dotnet hives for a host;
- define reference-pack or remote implementation-pack membership;
- define local artifact admission, workspace budgets, artifact generations,
  participant roles, group construction, or query authorization;
- detect concurrent servicing or prove an attempt-wide filesystem snapshot;
- grant core-library trust or define designated-over-platform precedence;
- select among distinct canonical platform identities that share one binding
  name;
- resolve types, members, or call targets; or
- define CLI coordinates, sections, rendering, or exit status.

## Acquisition kinds

Entitlement follows **how** an assembly was acquired, never anything the
acquisition *contains*. An assembly's simple name and public key are both public
data and trivially forgeable, so neither can be evidence.

| Acquisition | Provenance | Platform status | Why |
| --- | --- | --- | --- |
| **Layout** — dotnet hive, runtime pack, reference pack | `PlatformAsset` | **Platform base.** Establishes the closure everything else is read against. | Built as a unit, so the closure is coherent by construction. |
| **Exact file named by the caller** | `DesignatedAsset` | **Entitled.** May be a core library in its own right, or an overlay over a base. | The caller asserted this file specifically. That assertion is the only thing that distinguishes a build layout from an arbitrary directory. |
| **Package** | `PackageAsset` | **Rejected.** | A package is authored by whoever published it. Admitting one would let its contents define what the platform is — the *platform-in-package* case. |
| **Discovered sibling / loose directory** | `LocalAsset` | **Rejected.** | A directory of binaries is rarely a coherent closure: stale copies, reference-only assemblies, and cross-version core libraries all confuse types, with no malice required. |
| **Project output** | `ProjectAsset` | **Rejected.** | Build output is authored by the project under inspection. |
| **Embedded** | `EmbeddedAsset` | **Rejected.** | Carried inside another artifact, so its closure is whatever that artifact chose to carry. |

`MayMint` entitles the first two arms and denies the rest;
`EveryAcquisitionIsClassified_AndExactlyTwoAreEntitled` enumerates every
acquisition the product can express and requires exactly that split. Note which
way that gate fails safe: a provenance arm added later is **denied by default**
and passes the gate silently. What the gate catches is an arm added to the
*entitled* set without argument, not an arm left unclassified.

**A layout is not the only source of a core library.** Naming a core library
directly — `System.Private.CoreLib.dll` out of a build tree — designates it, and
it keeps core-library identity on that basis;
`PlantedCoreLibraryIdentityTests.RawPathOpen_KeepsCoreLibraryIdentity` gates
exactly that, and it is the dotnet/runtime build-layout workflow. What a layout
uniquely supplies is a *coherent set*, not the core library as such.

The rejections share a reason, and it is worth stating plainly because the
security framing tends to crowd it out: **the dominant risk is unintentional
type confusion, not an attacker.** A stale binary left over from an older build corrupts a session exactly as effectively as a planted one.

Rejection costs nothing in reach. A rejected assembly remains fully
inspectable — it is simply never promoted to *platform*, so it cannot speak for
the core library on behalf of everything else.

### Entitlement has exactly one door

`CoreLibraryIdentityTrust.MayMint` is the rule, and `GrantIfEntitled` is the
only way to reach the grant. `GrantCoreLibraryIdentity` is `private`
specifically so that it cannot become a second source of entitlement.

That privacy is load-bearing history, not tidiness. Through round 8 of the
review that produced this design the grant was `internal`, and three of five
grant sites called it directly — two of them constructing `Local` provenance,
which `MayMint` denies, and granting anyway. The *behaviour* was right at each
of those sites, since each opens a file the caller named. But it was right by
bypass, so every gate on `MayMint` proved nothing about them. Four consecutive
rounds each found the escape one frame further out, because the escape was never
a missing gate; it was a second door.

Reintroducing a direct grant **from outside the type** is now CS0122 — a compile
error rather than a test that can rot. Privacy cannot reach the in-type case: a
method or nested helper inside `CoreLibraryIdentityTrust` could still call the
grant legally, and a nested helper would do so without any call site naming the
trust type. Two IL-scanning gates hold that half, not the compiler —
`ReaderConstructionSiteTests.TrustTypeMembers_AreClassified`, which requires the
type to account for every member it declares and to declare **no nested types**,
and `ReaderConstructionSiteTests.TrustTableAccess_IsConfinedToItsPinnedMembers`,
which pins every method in the assembly whose IL reaches the trust table at all.
See
[`untrusted-data-threat-model.md`](untrusted-data-threat-model.md#core-library-identity-is-granted-by-acquisition-not-by-self-declaration)
for the gate inventory.

### Known gap: a path is not a designation

The rule above is correctly implemented and well gated. The **carrier** is not.

`MetadataSource.Open(path)` and `MetadataSource.OpenFromPrefetchedImage(path,
image)` infer designation from the presence of a path. Package extraction
produces a path on disk that is indistinguishable from a file the user named, so
a package carrying a forged `System.Runtime.dll` reaches these entry points and
mints core-library identity.

**Platform-in-package is therefore rejected in policy but not yet in
mechanism.** This is pre-existing rather than a regression, and it is tracked as
**#4606**; the fix is to require callers to supply the acquisition they actually
obtained the bytes under, so package bytes arrive as `PackageAsset` and are
denied.

Do not read the strict acquisition rule as evidence that this case is already
closed.

## Overlays

An overlay is a single assembly the caller named explicitly, composed over a
platform base. `System.Collections.dll` from a local build, placed over an
installed .NET 10 hive, is the shape.

**Overlay is a mechanism, not a scenario.** The scenarios that motivate it all
cross assembly boundaries: checking whether a modified library still satisfies a
contract expressed by its dependencies, inspecting a single binary pulled from
remote build assets, or asking what a rebuilt assembly integrates with. An
overlay that is only ever read on its own does not need to be an overlay.

Two rules govern composition. They describe the graph the product is to build,
not every outcome the current resolver can produce:

- **A designated artifact is preferred during reference binding.** The
  filename is acquisition evidence, not the binding key; assembly identity
  comes from metadata, and admission creates a participant that immutably binds
  that identity to one artifact and one policy snapshot. When resolution finds
  that a reference can bind to both a designated participant and a
  platform-backed participant, the binding policy selects the designated
  participant. Directly opening the file already reads the designated artifact;
  cross-assembly resolution does not yet enforce the same choice. Today an
  earlier candidate may win, the reference may remain unresolved, or the
  overlay may be selected. Those are implementation accidents, not separate
  cases in the product contract. Enforcing the rule is **#4593**.
- **Designation applies only to that artifact.** It does not become the
  platform, and it does not entitle nearby artifacts — directory membership is
  not designation. This half is real: a sibling reached by
  discovery carries `LocalAsset`, which `MayMint` denies. The denial of a
  resolved `LocalAsset` is gated by
  `PlantedCoreLibraryIdentityTests.PlantedSibling_OpenedThroughMetadataSource_LosesCoreLibraryIdentity`,
  which constructs that provenance directly. The other half of the claim — that
  a discovered sibling is in fact classified `LocalAsset` — rests on the default
  arm of the resolver's provenance mapping (`AssemblyDependencyResolver`) and is
  **not** separately gated.

Together, the rules construct one intentional graph: the base supplies a
closure built as a unit, the designated artifact forms a participant, and
binding policy selects that participant in place of the platform-backed one.
That substitution grants no authority to nearby artifacts. Constraining
composition this way reduces the compatibility-risk surface when acquisition
systems combine; it does not prove that the replacement still *fits*. That is
the separate compatibility question below.

## Overlay compatibility is a property of the pair and request

An overlay built against a newer framework than the base can reference platform
members the base does not contain. Both sides are individually legitimate — the
platform is a genuine coherent closure, and the overlay is a genuine file the
user named. The platform remains internally coherent; the overlay and base have
a version-skew risk. Whether one traversal can succeed also depends on the
member requested.

This is why compatibility cannot be folded into entitlement. Entitlement is
computed per acquisition, at open, and both sides pass. Compatibility concerns
the pair and a concrete traversal request. No hostile actor is required: the
correctness risk is that a legitimate assembly asks a legitimate but older
platform for metadata that the platform does not contain.

**Detect risk at load, attribute failure at traverse** (**#4592**):

- **At load**, compute the skew and surface it as a warning. Do not block. An
  assembly built for a newer framework still renders its own surface correctly,
  which is most of what the user opened it for; refusing at open would reject a
  session that mostly works.
- **At traversal into the platform**, attempt the requested lookup. If the
  loaded platform contains the member, return it; the skew warning remains
  useful context but does not invalidate the result. If the member is
  unavailable and the requesting overlay is known to target a newer platform,
  return an attributed typed compatibility failure naming the request, overlay
  target, and loaded platform. Without known skew, preserve the ordinary
  missing or unresolved result.

The failure mode this replaces is the one `AGENTS.md` forbids under *keep
failure visible*: today an unavailable member under known skew surfaces as an
unattributed missing type or member. A blanket refusal would be wrong in the
other direction because many requests remain satisfiable.

Expect a degree of incompatibility to remain even when everything is reported.
Decompiled output on the far side of a reference into a skewed assembly may be
wrong, and a type whose base declaration is unavailable will render
incompletely. That is **inherent** to overlaying: the missing information does
not exist in the workspace. The requirement is that it be attributed, not that
it be avoided.

## Precedence between entitled candidates

Entitlement admits `{PlatformAsset, DesignatedAsset}`. It settles *whether* a
candidate may be used and says nothing about *which* of two entitled candidates
to prefer. Load a platform and a designated build copy of the same assembly, and
both can satisfy the same reference.

The precedence rule for this case is simple: **when resolving a reference that
can bind to both, the binding policy selects the participant backed by the
designated artifact over the participant backed by the platform artifact**.
For a designated participant in this arbitration, assembly version is
descriptive rather than an eligibility barrier, including when no matching
platform participant is present. Platform participants retain the resolver's
existing version policy, and an enabled installed-platform fallback
participates under that policy. The existing assembly-name, culture, and
public-key-token constraints remain binding, including their existing omitted
value semantics; identity comes from metadata rather than the designated
file's name. This exception is limited to the designated/platform name-owning
domain; it does not weaken identity matching or promote package, project,
sibling, discovered, or other non-designated candidates.

The selection answer retains every other eligible entitled candidate as typed
shadow evidence, so consumers can explain the composition without reconstructing
policy from enumeration order. A shadowed candidate is evidence, not an active
participant.
That gives every acquisition system the same well-defined graph to compose
with; it does not require specifying the current resolver's case-by-case
accidents. Any other tie between entitled candidates needs its own stated rule
or a diagnostic rather than a silent pick. Multiple eligible designated
candidates remain ambiguous rather than being chosen by registration order.
If a caller-designated candidate cannot enter that arbitration because its
metadata format is unsupported or malformed, or its snapshot exceeds the
resource budget, the typed candidate failure remains visible and vetoes
platform fallback. Ordinary unreadable peers do not gain that veto.

The designated-precedence cases in `AssemblyDependencyResolverTests` gate
version and registration-order independence, identity constraints, typed
ambiguity and shadow evidence, same-path provenance, and preservation of
unrelated name-owning tiers.
`Select_UnsupportedDesignatedMetadataCannotFallBackToPlatform`,
`SelectAndResolve_MalformedDesignatedMetadataCannotFallBackToPlatform`,
`Select_SnapshotBudgetCannotFallBackFromDesignatedToPlatform`,
`Select_RenamedSnapshotBudgetCannotFallBackToPlatform`, and
`Select_UnreadablePeerDoesNotVetoDesignatedOverlay` gate the failure boundary.
The
`SharedCatalog_ReusesBindingManifestAndShadowsAcrossGenerations` and
`BindingFailure_PreservesShadowsWithoutOpeningThem` tests gate shadow
propagation without activating the shadow descriptor.

### Executable interaction model

The
[platform overlay resolution model](models/platform-overlay-resolution/README.md)
explores candidate registration order, designated/platform arbitration,
unruled ties, shadow evidence, incidental version equality, and attributed
compatibility failure at traversal. Its assumptions, bounds, checked properties,
and mutation controls are recorded beside the executable specification.

TLC results are evidence about the model, not the implementation. Formal
model-to-implementation correspondence remains unverified.

## Related

- [untrusted-data-threat-model.md](untrusted-data-threat-model.md#core-library-identity-is-granted-by-acquisition-not-by-self-declaration)
  — the entitlement rule, its allow-list polarity, and the gates that hold it.
- [artifact-acquisition-and-workspaces.md](artifact-acquisition-and-workspaces.md)
  — the target architecture, in which designation and platform trust become
  authorized workspace admission roles rather than provenance arms.
- [platform-assemblies.md](platform-assemblies.md) — how a platform *layout* is
  located and how ref and runtime assemblies divide the work.
