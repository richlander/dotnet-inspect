# Platform composition and overlays

How a workspace decides which assemblies constitute *the platform*, what may sit
on top of one, and what happens when the two disagree.

This document owns three questions that are easy to conflate and must not be:

| Question | Asked of | Answered when | Owner |
| --- | --- | --- | --- |
| **Entitlement** — may this acquisition speak for the core library? | one acquisition | at open | [untrusted-data-threat-model.md](untrusted-data-threat-model.md#core-library-identity-is-granted-by-acquisition-not-by-self-declaration) |
| **Precedence** — two entitled candidates define the same type; which wins? | a pair | at resolution | this document |
| **Coherence** — do these two actually fit together? | a pair | at traversal | this document |

The entitlement **rule** is settled and enforced, though its carrier has a known
gap ([#4606](#known-gap-a-path-is-not-a-designation)). Precedence and coherence
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

Two rules govern composition. The **first is intended behaviour that the current
implementation does not deliver**; the second holds today.

- **The overlay should win for its own filename — but does not.** The caller
  named that exact file, so honouring it is the entire point. Today it holds
  only for the assembly the caller *opens*, which is read from the named path
  directly. It does **not** hold for a reference *resolved* to that same
  filename from another assembly: platform candidates are registered before
  corpus candidates and resolution walks them in registration order, so the
  platform copy is offered first. (`CandidateTier` does not rank anything; it
  is used only for tier-boundary handling.) Whether the overlay is reached at
  all then depends on configuration rather than on the caller's intent.

  If no earlier candidate matches that *filename*, the overlay is simply used:
  it is the first candidate seen, no tier boundary is crossed, and nothing
  below applies. Candidates are also filtered by scope before any of this —
  platform scope considers only trusted-platform, shared-framework, and corpus
  provenances, so a sibling or package candidate for that filename imposes no
  condition there at all. What follows concerns the candidates that survive
  both filters.

  Candidates in the overlay's **own** tier are the easy case: a mismatch or an
  unreadable file simply moves to the next one, and a later same-tier candidate
  can still be chosen
  (`AssemblyDependencyResolverTests.Select_CaseDistinctSameTierCandidateIsMatchedAfterUnavailableCandidate`).
  The conditions below govern **earlier tiers**, because the boundary logic
  runs only when the tier actually changes:

  1. **Every** earlier candidate fails the *effective identity policy* —
     `MatchesCandidate` weighs version, culture, and public key token, and its
     version test is relaxed by `IgnoreAssemblyVersion` and, in platform scope,
     by `AllowPlatformAssemblyVersionRollForward`. One earlier match ends the
     search there and that candidate wins.
  2. None of them failed to **open**. An unreadable candidate records a
     failure, and a recorded failure turns the *next tier crossing* into an
     abandonment — in platform scope too, not only outside it.
  3. The scope permits crossing a tier boundary at all. Non-platform scope
     never does, and this is broader than platform-versus-corpus: resolution is
     effectively confined to the **first tier that had a filename match**. A
     sibling or package candidate that matches the filename and fails the
     identity policy is enough to abandon the whole resolution before either
     the platform copy or the overlay is considered.

  The overlay must then open and match on its own account. A version mismatch
  is the most likely way to satisfy the first condition, not the whole of it.
  Composition is therefore **emergent**, falling out of options, registration
  order, and whether unrelated candidates happened to be readable. This is the
  same defect as
  [precedence](#precedence-between-entitled-candidates) below, and it is
  **#4593**.
- **The overlay does not extend its authority beyond that filename.** It does
  not become the platform, and it does not entitle its siblings — directory
  membership is not designation. This half is real: a sibling reached by
  discovery carries `LocalAsset`, which `MayMint` denies. The denial of a
  resolved `LocalAsset` is gated by
  `PlantedCoreLibraryIdentityTests.PlantedSibling_OpenedThroughMetadataSource_LosesCoreLibraryIdentity`,
  which constructs that provenance directly. The other half of the claim — that
  a discovered sibling is in fact classified `LocalAsset` — rests on the default
  arm of the resolver's provenance mapping (`AssemblyDependencyResolver`) and is
  **not** separately gated.

Stated as intent, the pair keeps the graph mostly coherent: the base supplies a
closure that was built as a unit, and the overlay replaces one member of it.
Until #4593 lands, the replacement is reliably visible only to the caller who
named it; whether any other assembly sees it is an accident of configuration
and version matching. What that cannot guarantee, even once the rule holds, is
that the replacement still *fits*.

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

Today the winner falls out of the order in which candidates are registered:
platform is registered before corpus, so platform is offered first. Worse, the
outcome **turns on configuration** — the designated copy wins only when every
earlier candidate fails the effective identity policy (version, culture, or
public key token, as relaxed by `IgnoreAssemblyVersion` or platform-scope
roll-forward), none of them failed to open, and the scope allows crossing a
tier boundary at all. Non-platform scope does not, which confines resolution to
the first tier that matched the filename. Otherwise an earlier candidate wins —
which may be a sibling or a package, not only the platform copy — or the
reference does not resolve at all. So the selected assembly changes with option
settings, version equality, and even whether an unrelated candidate happened to
be readable, with no signal in any direction.

The rule is that **precedence must be stated rather than emergent**, and that an
unstated tie is a diagnostic rather than a silent pick. Tracked as **#4593**.

## Related

- [untrusted-data-threat-model.md](untrusted-data-threat-model.md#core-library-identity-is-granted-by-acquisition-not-by-self-declaration)
  — the entitlement rule, its allow-list polarity, and the gates that hold it.
- [artifact-acquisition-and-workspaces.md](artifact-acquisition-and-workspaces.md)
  — the target architecture, in which designation and platform trust become
  authorized workspace admission roles rather than provenance arms.
- [platform-assemblies.md](platform-assemblies.md) — how a platform *layout* is
  located and how ref and runtime assemblies divide the work.
