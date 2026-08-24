# Platform composition and overlays

How a workspace decides which assemblies constitute *the platform*, what may sit
on top of one, and what happens when the two disagree.

This document owns three questions that are easy to conflate and must not be:

| Question | Asked of | Answered when | Owner |
| --- | --- | --- | --- |
| **Entitlement** — may this acquisition speak for the core library? | one acquisition | at open | [untrusted-data-threat-model.md](untrusted-data-threat-model.md#core-library-identity-is-granted-by-acquisition-not-by-self-declaration) |
| **Precedence** — two entitled candidates define the same type; which wins? | a pair | at resolution | this document |
| **Coherence** — do these two actually fit together? | a pair | at traversal | this document |

The entitlement **rule** and its current product carrier are settled and
enforced ([#4606](#current-carrier-a-path-is-not-an-acquisition)). Precedence
and coherence are specified here but **not yet implemented**; each names its
tracking issue. Where this document describes intent rather than current
behaviour, it says so at that point.

## What a platform is

A platform is a **coherent closure** — a set of assemblies that were built to go
together and can therefore be trusted to agree about types. In practice that is
a dotnet hive, a runtime pack, or a reference pack.

Coherence is the property that matters, and it is a property of the *set*, not
of any file in it. This is why no amount of inspecting an individual assembly
can establish it. A genuine, Microsoft-signed .NET 6 `System.Runtime.dll` is
authentic and, sitting beside a .NET 10 library, wrong. Authenticity is not
coherence.

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

### Current carrier: a path is not an acquisition

The rule above is implemented and carried through product inspection paths.
API-source resolution creates one `ResolvedAssemblyReference` with the actual
`PackageAsset`, `ProjectAsset`, `PlatformAsset`, or `DesignatedAsset`
acquisition. Type-forwarder resolution retains the descriptor of the assembly
that supplied each type. Member code, Body Shapes, Match implementation diffs,
whole-type Decompiled Source, and prefetched scanner images consume those
descriptors rather than reopening their retained paths as designations.

`MetadataSource.Open(path)` and `MetadataSource.OpenFromPrefetchedImage(path,
image)` remain compatibility entry points whose contract is explicit caller
designation. A retained package-extraction path must not reach them:
`LayeringTests.Cli_MetadataSourceFactories_RetainAcquisitionDescriptors`
derives every CLI call to those factories from compiled IL and requires the
descriptor overload, while
`PlantedCoreLibraryIdentityTests.PackagePrefetchedImage_DoesNotMintCoreLibraryIdentity`
drives package provenance through the snapshot reader. Snapshotting preserves
the original acquisition registration and rejects bytes with a different
assembly identity. Descriptor-backed metadata consumers perform the same
identity check before deriving facts or granting core-library trust, and
whole-type composition resolves metadata facts and bodies from one validated
reader. Inspection-root netmodules use the same carrier without
becoming assembly-binding candidates: their module name is diagnostic and their
MVID binds every descriptor-backed open, snapshot, and direct whole-type
definition address to the selected content.
`InspectionAcquisitionPlanTests.WithContentSnapshot_*` and
`InspectionAcquisitionPlanTests.ModuleContentSnapshot_*`,
`DescriptorContentIdentityTests`, and
`ReaderConstructionSiteTests.DescriptorOpenReferences_MatchTheReviewedInventory`
gate those properties.

This closes #4606's platform-in-package carrier bug without pretending that a
path proves acquisition. The future workspace-admission design goes further:
it retires blanket raw-path designation and requires an owner-authorized
admission role even for direct callers.

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
composition this way reduces the incoherence surface when acquisition systems
combine; it does not prove that the replacement still *fits*. That is the
separate coherence question below.

## Coherence is a property of the pair

An overlay built against a newer framework than the base can reference platform
members the base does not contain. Both sides are individually legitimate — the
platform is a genuine coherent closure, and the overlay is a genuine file the
user named. The **combination** is not.

This is why coherence cannot be folded into entitlement. Entitlement is computed
per acquisition, at open, and both sides pass. Only the pair fails, and the pair
only exists once traversal crosses from one into the other.

**Detect at load, attribute at traverse** (**#4592**):

- **At load**, compute the skew and surface it as a warning. Do not block. An
  assembly built for a newer framework still renders its own surface correctly,
  which is most of what the user opened it for; refusing at open would reject a
  session that mostly works.
- **At traversal into a platform assembly**, report it with attribution — *"this
  assembly targets net15.0; the loaded platform is net10.0, so this member may
  not resolve."*

The failure mode this replaces is the one `AGENTS.md` forbids under *keep
failure visible*: today an incoherent pair surfaces as a missing type, which is
indistinguishable from the type not existing. A success-shaped empty result is
the worst available answer.

Expect a degree of incoherence to remain even when everything is reported.
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
That gives every acquisition system the same well-defined graph to compose
with; it does not require specifying the current resolver's case-by-case
accidents. Any other tie between entitled candidates needs its own stated rule
or a diagnostic rather than a silent pick. The current resolver does not yet
enforce this contract; tracked as **#4593**.

## Related

- [untrusted-data-threat-model.md](untrusted-data-threat-model.md#core-library-identity-is-granted-by-acquisition-not-by-self-declaration)
  — the entitlement rule, its allow-list polarity, and the gates that hold it.
- [artifact-acquisition-and-workspaces.md](artifact-acquisition-and-workspaces.md)
  — the target architecture, in which designation and platform trust become
  authorized workspace admission roles rather than provenance arms.
- [platform-assemblies.md](platform-assemblies.md) — how a platform *layout* is
  located and how ref and runtime assemblies divide the work.
