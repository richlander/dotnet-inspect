# Untrusted data threat model

`dotnet-inspect` reads artifacts that may be malformed or intentionally hostile.
Inspection must not grant those artifacts authority to execute code, choose
network destinations, escape storage boundaries, or turn malformed input into
unbounded work.

This document records the trust boundaries and security rules for product code.
It is a living model: new acquisition paths, parsers, caches, or output features
must update the relevant boundary and verification obligations.

## Scope and priority

State intent; do not make promises. These libraries may eventually ship, and
the patterns in a tool like `mdi` may be reimplemented elsewhere, so the model
should be legible enough to copy — but "we thought about this" is not a
guarantee, and this document does not offer one.

**In scope:** harm to the machine or the user's tooling caused by untrusted
input arriving over the internet — a package from a feed, a PDB from a symbol
server, source fetched from SourceLink. Using a NuGet package is a trust
decision the user makes; that this tool is a no-commitment offer, easy to point
at anything, raises rather than lowers the bar for what it does with what it
finds.

The intended consumer raises it further. This tool is built to be handed to
**autonomous agents**, so its output is frequently acted on without a human
reading it. Two things follow. A rendering hazard is not bounded by whether
someone is watching a terminal, and output that misstates identity is not
caught by a reader who would have noticed. Trust in the *input* is also
misplaced in a specific way worth naming: a caller who pre-vetted their
dependencies concludes that reading them is safe, but a vetted package can be
hijacked after the fact, and the names entering a build are not spelled
uniformly across projects, transitive edges, and floating versions.

**Out of scope:** deliberately opening artifacts you already know are hostile,
and running the tool elevated. Both are the caller's decision, and neither is a
boundary this tool can defend.

**Target: two to three nines, not five.** That is a real ceiling, and it is
what makes the ordering below meaningful rather than aspirational.

Work is ranked in this order, and the order is not negotiable when they compete
for attention:

1. **Reliability and security on correct, well-formed binaries.** This is first
   because it is the normal case, and because rigor on correct inputs is what
   makes reasoning about malformed ones possible at all. A tool that is wrong
   about ordinary assemblies has no standing to claim anything about hostile
   ones.
2. **Security on malformed binaries.** Everything downstream of a parser that
   accepts what it should have rejected.
3. **Reliability against environmental patterns that require an attacker to
   already have access to the machine.** Not zero — but at most two nines, and
   never ahead of the first two.

A crash on malformed input is a **reliability** defect, not a security one. It
is still worth fixing, and not only for tidiness: a crash means the code is one
step away from the same input producing an effect that does *not* announce
itself.

Local machine weirdness is tier 3. A symbolic link someone placed in a package
cache is a user doing user things, not an elevation of privilege. It would be a
security issue if a *package* could create it during extraction — and that
would be a defect in NuGet's restore, not in this tool.

## Strategy: reject, do not sanitize

When untrusted input violates a contract, the response is a typed rejection.
Sanitizing — accepting the artifact and repairing the offending value — is
rejected as a strategy for two reasons:

- **It is hard to get right, and being wrong is silent.** A sanitizer has to be
  correct about the full set of dangerous forms. A rejecter only has to be
  correct that *this* form is not allowed.
- **Where there is one mouse, there are many.** A field that contains a
  terminal escape sequence is evidence about the artifact, not about the field.
  Repairing it and continuing gives a malformed or hostile package a second
  chance to be interesting somewhere the check does not reach.

The consequence is a stack that must be **resilient to errors**, not resilient
to handling bad input. Those are different engineering problems and only one of
them is defensible: error paths are few, shared, and testable, while
bad-input-tolerance is diffuse and every new consumer re-litigates it.

### Prefer an allow list wherever the grammar is known

A rejecter still has to decide what to reject, and there are two ways to write
that down. A **deny list** enumerates the bad forms; an **allow list**
enumerates the permitted ones and refuses everything else. Where a field's
grammar is externally defined — a package id, a version — use that grammar as
the allow list rather than inventing a narrower substitute.

The difference is not stylistic. A deny list is only ever as current as the
last hazard someone thought of. An allow list can still contain Unicode:
NuGet's package-ID grammar uses Unicode word characters, and narrowing it to
ASCII rejects legitimate packages. Cyrillic `а` (`U+0430`) and Latin `a`
(`U+0061`) are the same glyph but both valid word characters, so homoglyph
typosquatting is an identity-confusion signal rather than malformed syntax.
Package and assembly signals report that separate concern below.

An allow list is also cheap to audit — one defined grammar checked against one
field. It is the same reject-over-sanitize rule applied one step earlier: at
the point the value is admitted rather than the point it is printed.

Free-form fields cannot be treated this way. Assembly-derived type and member
names are legitimately non-ASCII, and prose is legitimately international, so
those fall back to visual encoding. See
[metadata-table-projection.md](metadata-table-projection.md#constrain-the-grammar-first-encode-only-what-cannot-be)
for the sink classes and the encoding rules that follow from them.

Push the decision **down**. The best shape is a type whose construction *is*
the check, so choosing the type grants the capability and auditing is a search
rather than an argument. A rule enforced by *calling a function* is a rule a
new path can forget, and `string` is the type of both a checked and an
unchecked value.

`HardenedJson` is the repository's closest existing move in this direction, and
it is worth being precise about how far it actually goes: it is a `static
class` whose `Parse` returns an ordinary `JsonDocument`, so it is a single
named entry point that centralizes the policy — not a type whose construction
enforces it. Choosing it grants the capability; nothing stops a new call site
from reaching for `JsonDocument.Parse` instead, and some already do (see open
work). A centralized entry point is a real improvement over per-call-site
options and is cheap to audit by grep, but it is the weaker of the two shapes,
and new hardening should prefer the stronger one where the value crosses a
layer boundary.

The stronger shape now exists. `InertText.InertString` (#3636) is a type whose
construction *is* the encoding, so treated text has a different type from
untreated text and survives composition — a site that merely passes a value
along cannot drop the property, and forgetting becomes a compile error rather
than a missing line in a hand-maintained list. It is the primitive this section
argues for; prefer it over a new `HardenedJson`-shaped static entry point when
the thing being contained is text bound for a sink.

One thing this rule does **not** forbid is escaping and encoding. Escaping a
value on the way into a sink — JSON string escapes, `vis(3)`-style visual
encoding of control characters — is a property of the *encoding*, applied
uniformly to all text, lossless and invertible. Sanitization is different: it
inspects a value, judges it dangerous, and alters or drops part of it so the
rest can proceed.

Both have to know which characters the sink interprets; a terminal has no
formal grammar, so encoding for one is not the free lunch that escaping for
JSON is. The distinction is what happens when you are **wrong** about that set:

- Under-encode, and the fix is to widen the set in one place. Nothing was
  lost, the decoder still recovers the original, and every call site inherits
  the correction at once.
- Under-sanitize, and the data is already gone, the judgment is spread across
  every call site that made it, and there is no decoder to appeal to.

Uniformity is the other half. An encoder does not decide *whether* a given
value is hostile, so it cannot be wrong about a value — only about the sink.
That is a much smaller thing to be right about, and it is written down.

### URL path-component redaction

`InertText.UrlRedaction` owns both complete-URL diagnostic redaction and the
narrower redaction of an already-parsed URL path component. The two inputs have
different trust boundaries:

- `ForDiagnostics` accepts URL-like text, classifies its locator and authority
  shape, and fails closed when safe components cannot be located.
- `ForPathComponent` accepts only a path already separated from scheme,
  authority, query, and fragment. It does not parse or classify that value as a
  locator. It applies the same owner-issued `auth` credential-slot rule and
  returns an `InertString`, preserving every other path distinction and
  encoding non-graphic scalars.

Consumers may retain or frame the path-only result without copying the
credential-slot rule or depending on complete-URL parser branch order. They
must not pass an unseparated URL or reconstruct removed components from the
safe result. Producer identity, endpoint validation, cache authority, and
presentation policy remain outside this owner.

`UrlRedaction.PathComponentContractVersion` is the InertText-owned semantic
compatibility discriminator for the encoded text returned by
`ForPathComponent`. Its current value is `1`. Increment it before merging any
change for which an admitted path can produce different `ToString()` output,
including changes to the credential-slot grammar, `RedactedMarker`,
`TextPolicy.Field`, or visual spelling. It is independent of assembly and
package versions. Changes to `InertString` metadata or APIs that leave the
encoded text unchanged do not increment it.

This contract is gated by
`ForPathComponent_ContractVersionPinsCurrentOutput`,
`ForPathComponent_PreservesNonCredentialPathText`,
`ForPathComponent_RedactsCredentialSlots`, and
`ForPathComponent_EncodesNonGraphicScalars` in the Release
`InertText.Tests` suite. The authority-shaped and credential-bearing cases
make the path-only wiring non-vacuous.

### Failure messages carry no artifact data

A rejection message names the **user-supplied** input — the path, coordinate,
or package the caller asked for — plus the rule that fired and the location
within the artifact. It must not quote the offending value.

This is not in tension with keeping failures attributable: attribution is
satisfied by naming what the user wrote and where the problem is. The rejected
value is by construction the most hostile string encountered, and echoing it
into an exception message or onto `stderr` re-opens on the error path the exact
channel the check just closed.

See [metadata-table-projection.md](metadata-table-projection.md#safety) for
this model worked through on one surface, including how a hostile image stays
inspectable without handing over its bytes.

## Security objectives

The product must:

1. Inspect assemblies without loading or executing them.
2. Keep artifact-derived paths inside an explicit caller- or product-owned root.
3. Treat artifact-derived URLs as untrusted network destinations.
4. Bound CPU, memory, network, archive, and recursion work where hostile input
   can amplify it.
5. Keep malformed-input failures visible rather than returning plausible,
   success-shaped output.
6. Treat rendered artifact text as data, not terminal commands, markup
   authority, or agent instructions.

The product path remains SRM-only, NativeAOT-friendly, Roslyn-free, and free of
inspected-assembly loading. Those architectural constraints are security
boundaries as well as deployment choices.

## Trust boundaries

| Boundary | Untrusted input | Trusted side | Primary risks |
| --- | --- | --- | --- |
| Assembly inspection | PE headers, metadata tables, signatures, IL, resources | Metadata, Instructions, Analysis, Decompiler | Parser crashes, recursion or allocation denial of service, path derivation, misleading identities |
| Package acquisition | `.nupkg` / `.snupkg`, nuspec and package file names, feed responses | Package extraction and cache roots | Archive traversal, disk exhaustion, cache poisoning, dependency confusion |
| Symbols and source | Portable/embedded PDBs, SourceLink maps, document names, checksums, source URLs and content | PDB sessions, source cache, source rendering | SSRF, local-file access, cache escape, oversized downloads, spoofed provenance |
| Restored project inputs | `project.assets.json`, `.deps.json`, runtime/tool settings, paths within those files | Project and dependency resolvers | Path confusion, unintended file reads, excessive graph expansion |
| Filesystem output | Resource names, generated file names, user-selected output paths | Explicit output or cache directory | Arbitrary write, overwrite, symlink/reparse escape, partial output |
| Presentation | Names, documentation, source, paths, diagnostics, package metadata | Terminal, Markdown/JSON consumers, agents | Terminal control injection, broken structured output, prompt-like content treated as authority |

User-supplied local paths are trusted as *locations the user chose*, but the
contents found there are not trusted. Product cache directories and
process-created temporary directories are trusted roots; names appended beneath
them are not trusted unless derived from a cryptographic key or validated
component.

**Code running in this process is not the boundary these controls defend.**
The untrusted input in every row above is *data* — an artifact, a feed
response, a file. It is not a caller. So a control that a `BindingFlags.NonPublic`
call can undo is not thereby broken, because a party who can execute arbitrary
code in this process does not need to smuggle text through a type to reach a
sink; it can write to the sink. The claim is that narrow, and deliberately so:
not that reflection is harmless in general, but that it is not the entry point
any control here stands in front of. This is also not a self-serving line — no
.NET type meets the other standard. Measured, the same technique that rewrites
a private backing field on `SourceLinkOrigin` rewrites one on `System.Uri` —

```text
Uri backing field: _string
Uri.OriginalString => https://example.com/<LRI>hostile
```

— making `OriginalString` return a live `U+2066` from a `Uri` constructed over
inert text. `Uri` is the type the SourceLink origin readers rely on for
canonicalization, so a rule that treats reflection as in scope would condemn the
control and its substrate together and leave nothing constructible in its place.

What *is* in scope is the ordinary language surface: a public constructor, a
`with` expression, a settable property. Those are reachable by a future
contributor writing normal code, which is how an invariant actually decays, and
`SourceLinkProvenanceTests.ASourceLinkOrigin_CannotBeConstructedOrRewrittenOutsideItsOwnAssembly`
is the gate for them. It deliberately does not claim more.

**Nor is a local actor on the user's own machine.** Do not model our own code,
another contributor or agent, or a user who can act on the machine as a hostile
actor that product code must contain. A party that can edit the codebase, run
code in the process, create local symlinks, or place credentials in the
repository can already bypass product invariants and has more direct targets.
Treat those scenarios as code review, testing, or repository-hygiene concerns,
not as reasons to add product hardening. Before accepting a security concern,
identify how an actor *outside* the user's machine can affect the user through
data the tool reads — that is the boundary table above. A locally supplied
assembly does not independently establish an attacker boundary; it may receive
the same containment as an internet-origin assembly when both use a shared
path, but that benefit is incidental and does not justify extra complexity.
Robustness against accidental internal mistakes still matters, and is achieved
with simple, auditable code, structured types instead of strings, narrow APIs,
compiler-enforced invariants, and focused tests — not by pretending trusted
code is an attacker.

## Existing controls

### Assemblies are parsed, never loaded

Assembly, metadata, and method-body paths use
`System.Reflection.PortableExecutable` and `System.Reflection.Metadata`. Product
inspection must not introduce `Assembly.Load`, `AssemblyLoadContext`, reflection
or `MetadataLoadContext` over inspected binaries, module initializers, or
dependency resolution that executes target code.

`AssemblyLoadingPolicyTests` is the gate for this absence claim.
`IsInspectionProductProject` marks the shipped tool and Inspect Web project
closures, `Directory.Build.targets` supplies
`eng/BannedSymbols.InspectionProduct.txt` to
`Microsoft.CodeAnalysis.BannedApiAnalyzers`, and the compile-negative canary
proves the analyzer is live.

Reader-backed values remain inside their owning session. Values that cross a
session boundary are copied or reduced to immutable tokens and shapes. This
prevents use-after-dispose and avoids lending privileged readers to higher
layers.

Assembly-reference names remain metadata identity rather than filesystem path
components. Reference-tree traversal resolves `AssemblyReferenceIdentity`
through the shared resolver's enumerated candidate catalog. Its tree-specific
policy preserves sibling-first, version-tolerant selection relative to each
resolved parent before falling back to installed platform assets; supplied
culture and public-key-token constraints still bind, while omitted constraints
remain wildcards because typed identity travels through the inspection model
rather than being reconstructed from display text. It excludes the inspecting
process's own trusted platform assembly closure. Platform lookup matches
requested names against enumerated file names. An unreadable name-matching
candidate blocks fallback to lower-priority candidate tiers, while other
case-distinct candidates in the same tier remain eligible. A readable
same-name sibling that does not satisfy identity likewise owns the local tier
and blocks installed-platform fallback. The decompiler's
default sibling-only resolver follows the same boundary: it resolves the owner
path before enumerating neighboring assemblies, then selects by metadata
identity rather than deriving a path from the requested name. The
`AssemblyReferenceTreeResolutionTests.TraversingAssemblyRefName_IsIdentityAndCannotEscapeTheAssemblyDirectory`
and the sibling/platform/culture/failure-state tests in that class,
`AssemblyDependencyResolverTests.Select_UnreadableSiblingDoesNotFallThroughToTpa`,
`AssemblyDependencyResolverTests.AssemblyDependencyResolver_PreservesOwnerIssuedNameDisposition`,
`AssemblyDependencyResolverTests.Select_CaseDistinctSameTierCandidateIsMatchedAfterUnavailableCandidate`,
`AssemblyReferenceTreeResolutionTests.MismatchingPlatformNamedSibling_ShadowsInstalledPlatformFallback`,
`AssemblyReferenceResolverTests.SiblingResolver_BareOwnerPathUsesCurrentDirectory`,
`AssemblyReferenceResolverTests.SiblingResolver_AssemblyReferenceNameCannotEscapeDirectory`,
and
`PlatformResolverTests.ResolveAssembly_AssemblyNameCannotEscapeReferencePack`
gate these seams.

### Core-library identity is granted by acquisition, not by self-declaration

An assembly that decodes as `TypeRef.CoreLibrary` is privileged: the decompiler
treats its types as the canonical platform types, so a
`System.Collections.IEnumerable` bearing that identity compares equal to the
real one and authorizes raising decisions such as
`SupportsCollectionInitializer`.

That identity must never be derived from what an assembly says about itself.
The platform public keys are published data and nothing in this product
verifies a strong-name signature. Nor could it: shipped platform assemblies are
**public-signed**, so the `AssemblyDef` advertises `StrongNameSigned` while the
signature slot is zero-filled and there is nothing to verify. Any file can
therefore name itself `System.Runtime`, copy the ECMA public key blob in
verbatim, and satisfy any check made purely on self-declared name and key.

The concern this guards is **unintentional type confusion**, not an attacker. A
directory of loose binaries is rarely a coherent closure. A stale copy left over
from an older build, a reference-only assembly with no bodies, or a core library
from a different runtime version confuses types exactly as effectively as a
planted one, and arrives with no malice at all. Cryptography would not help
here even if it were available: a genuine, Microsoft-signed .NET 6
`System.Runtime.dll` sitting beside a .NET 10 library is authentic *and* wrong.
The question is not "is this file real?" but "may this file speak for the core
library of the assembly under inspection?" — and only acquisition can answer it.

`CoreLibraryIdentityTrust` owns the current rule. Trust follows
**acquisition**, and which acquisition applies follows how the caller named the
file. Current raw-path entry points treat the path as an explicit designation
because the caller chose that exact file. A `ResolvedAssemblyReference` was
reached by discovery, so it is trusted only when its
`AssemblyResolutionProvenance` is a `PlatformAsset` or a `DesignatedAsset`.
`TypeRefDecoder.CanonicalSelf` consults the registry before honouring a platform
key.

Entitlement has exactly **one door**. `MayMint` is the rule and
`GrantIfEntitled` is the only way to reach the grant, because
`GrantCoreLibraryIdentity` is `private`. That privacy is the fix, not a
convention: through round 8 the grant was `internal` and three of the five grant
sites called it directly, two of them building `Local` provenance — which
`MayMint` denies — and granting anyway. Each of those sites opens a file the
caller named, so the behaviour was right; it was just right by *bypass*, and
every gate on `MayMint` therefore proved nothing about them. Four consecutive
rounds found the escape one frame further out because it was never a missing
gate, it was a second door. Reintroducing a direct grant **from outside the
type** is now CS0122, a compile error rather than a test that can rot; the
in-type case is beyond privacy's reach and is held instead by
`TrustTypeMembers_AreClassified` — which forbids nested types precisely because
a nested helper reaches the table without naming it — and by
`TrustTableAccess_IsConfinedToItsPinnedMembers`, both described below.

The registry is an **allow list**, and the polarity is load-bearing. A deny
list has to enumerate every site that turns bytes into a reader, so a site
nobody remembered fails open and silently restores the vulnerability. That is
not hypothetical: the first version of this fix classified only in
`MetadataContext.Open(ResolvedAssemblyReference)` and missed
`MetadataSource.OpenCore`, which creates readers directly and is the path
`MemberBodyProducer` takes for a type defined in a sibling assembly. Failing
closed bounds the obligation to the few sites that deliberately *grant* trust:
a new open path that forgets to classify loses core-library identity, which is
visible and safe, rather than gaining it, which is neither.

Acquisition separates the two workflows that share a shape. A developer
inspecting a dotnet/runtime build layout has a real core library beside the
assembly under inspection; a package or upload may have an arbitrary
`System.Runtime.dll` beside its own library. **No metadata distinguishes
them** — only how the file was acquired does.

The rule is deliberately strict: **`PlatformAsset` means the file came from a
coherent closure** — a dotnet hive, a runtime pack, or a reference pack — and
nothing else earns it. Loose binaries remain fully inspectable; they are simply
never promoted to platform. `CorpusAssembly` is the one adjacent case, and it is
not an exception to the principle: a corpus is enumerated explicitly by the
caller, which is designation rather than discovery, so it satisfies platform
*scope* on the strength of `DesignatedAsset`.

There is deliberately **no host opt-in** to relax this. An opt-in would be a
blanket switch over provenance, and the provenances it would enable —
`PackageAsset`, `EmbeddedAsset`, discovered siblings — are precisely the ones
whose closure cannot be established. Better loose-layout support is a scenario
to design later, and it needs a coherence test, not a policy flag. What such a
scenario has to establish — overlay composition, coherence, and precedence
between two entitled candidates — is specified in
[platform composition and overlays](platform-composition-and-overlays.md).

Today the only product caller that designates is corpus enumeration
(`CorpusAssemblyPaths`). No command turns a user-named directory into a
designation, so a discovered sibling core library is denied identity however
the user reached it.

That denial does not block build-layout inspection, because a sibling is not
how a build layout supplies a core library in the first place. A core-library
reference carries a platform public-key token, so it is asserted at
`AssemblyResolutionScope.Platform`, and platform scope admits only
trusted-platform, shared-framework, and corpus candidates — siblings are
filtered out before trust is ever consulted. The layout's own core library is
therefore never the candidate for a platform-token reference. When the user
names that core library directly it is opened rather than resolved, and the
deny list is scoped to resolution, so it keeps its identity. Both halves of
the developer workflow work without designating the directory.

Because trust is read off provenance, provenance must not overstate acquisition
either. `PlatformAsset` is load-bearing beyond trust: it drives the
user-visible `ResolvedFrom` value, symbol-server PDB acquisition, and
inspection-graph boundary classification. The intrinsic core-library binding —
which returns the designated target when that target is itself the core
library — therefore reports `DesignatedAsset`, not `PlatformAsset`: the caller
named the file, but a loose file is not a hive. It keeps core-library identity
through designation, while `ResolvedFrom` stops claiming a platform origin it
cannot support. Local PDB probing is unaffected, since only symbol-*server*
acquisition is gated on platform status.

**The raw-path shortcut is a known live gap, not merely a shortcut the target
improves on.** `MetadataSource.Open(path)` and
`MetadataSource.OpenFromPrefetchedImage(path, image)` infer designation from the
presence of a path. Package extraction produces a path on disk that is
indistinguishable from a file the user named, so a package carrying a forged
`System.Runtime.dll` reaches these entry points and mints core-library identity.
Platform-in-package is consequently rejected in policy but **not** in mechanism.
This is pre-existing — both sites granted unconditionally before the rule was
funnelled — and it is tracked as **#4606**, whose fix is to require callers to
supply the acquisition they actually obtained the bytes under. Until then, treat
this section's rule as describing the decision, not the whole carrier.

That describes the current carrier. The target
[artifact acquisition design](artifact-acquisition-and-workspaces.md)
preserves the same allow-list decision but moves caller designation and
platform trust onto authorized workspace admission-role evidence. Source
adapters retain acquisition provenance separately, and Metadata no longer owns
the trust arm. The decompiler still receives an explicit owner-issued trust
grant; no path, assembly name, public-key blob, or source-provenance display
field can reconstruct it. In particular, a lease-scoped path to a retained
snapshot is only a content-access form. The target retires the current
raw-path-implies-designation shortcut; opening that path cannot grant
core-library trust without a separate authorized admission role. The same rule
applies when caller-supplied bytes are paired with a path, as in the current
`MetadataSource.OpenFromPrefetchedImage` compatibility entry point.
`LeaseScopedPath_IsNotADesignationGrant` derives every unconditional path and
prefetched-image grant from the `ReaderConstructionSiteTests` inventory and
asserts coverage equality, rather than relying on a hand-maintained method
list.

In the target architecture the platform adapter mints only validated platform
realization and correspondence evidence, and workspace admission grants the
corresponding platform-trust role under explicit host policy. An
adapter-provided provenance record, platform-shaped coordinate, assembly name,
or public-key blob cannot grant that role by itself;
`PlatformArtifactTrust_RequiresAuthorizedAdmissionRole` gates that boundary.
That is the same decision this section already makes, expressed against
workspace admission rather than against provenance.

`PlantedCoreLibraryIdentityTests.PlantedPlatformKey_DoesNotMintCoreLibraryIdentity`
gates the boundary with a real planted assembly carrying the verbatim ECMA
key;
`PlantedCoreLibraryIdentityTests.PlantedSibling_OpenedThroughMetadataSource_LosesCoreLibraryIdentity`
gates the reader-creation path that bypasses `MetadataContext`, and fails if
the registry ever returns to deny-list polarity;
`PlantedCoreLibraryIdentityTests.DesignatedTarget_KeepsCoreLibraryIdentity`
and `PlantedCoreLibraryIdentityTests.RawPathOpen_KeepsCoreLibraryIdentity`
gate the current scope, so failing closed does not cost ordinary use. During
the artifact migration, the latter changes from preserving blanket raw-path
trust to proving `LeaseScopedPath_IsNotADesignationGrant`;
`PlantedCoreLibraryIdentityTests.DesignatedCorpusAssembly_SatisfiesPlatformScope`
gates the resolver half, since a core-library `TypeRef` forces
`AssemblyResolutionScope.Platform` and a designated corpus assembly must be
able to satisfy it;
`PlantedCoreLibraryIdentityTests.DesignatedAcquisition_KeepsCoreLibraryIdentity`
gates the build-layout and corpus workflow;
`PlantedCoreLibraryIdentityTests.DiscoveredSibling_IsDenied` gates the denial
of a resolved `LocalAsset`, injecting that provenance directly rather than
exercising the resolver's classification of a discovered sibling, which is
ungated; and
`PlantedCoreLibraryIdentityTests.PackagesAndUploads_AreDenied` gates the
package and embedded provenances, so no future opt-in can reach them by
accident.

The gate that has been hardest to get right is the one asserting that *no*
reader-creation site was overlooked, because the obvious formulation — reflect
over the factory methods — enumerates signatures, and a signature has
unboundedly many cosmetic dimensions. Three consecutive review rounds on the
fix escaped it along a different one each time: the method name, then the
declared return type, then visibility and `Task` wrapping (issue #4464).
`ReaderConstructionSiteTests.TrustRelevantSites_MatchThePin` replaces that with
an observation those escapes cannot reach. It reads the compiled IL of
`ILInspector.Decompiler` and pins every method that obtains a `MetadataReader`
or calls a grant on `CoreLibraryIdentityTrust`. Trust attaches to a reader
instance, and this assembly creates one only by calling `GetMetadataReader` or
by constructing a reader directly, so a site is visible whatever it is called,
however it is declared, and whether or not its result is wrapped. Both
directions fail:
an unpinned site is an unreviewed way to obtain a reader, and a pinned site
that stops obtaining or granting is a stale entry. Listing is not approval —
most pinned sites deliberately do *not* classify, and the table records which
half of the design each one is on.
`ReaderConstructionSiteTests.Scanner_ObservesBothAcquisitionAndGrantSites` is
its non-vacuity check, since a scan compared against a table would pass just as
happily if the scan silently observed nothing, and
`ReaderConstructionSiteTests.SiteKeys_AreUniquePerMethod` keeps each site key an
identity rather than a label, so an added overload or a lowered local function
cannot inherit an existing entry's approval.

Grants are recognised by the primitive, not by the call surface, and that is
what makes the gate converge. Core-library identity *is* membership in the
`s_trusted` table, so `ReaderConstructionSiteTests.TrustTableAccess_IsConfinedToItsPinnedMembers`
pins every method in the assembly whose IL reaches that field — whatever it is
called, whatever type declares it, and whether or not it is reachable through
the trust type's own surface. Loading the field counts as reach because mutating
the table requires getting hold of it first; storing it is initialization, and
is allowed only in the static constructor that creates it. A call into
`CoreLibraryIdentityTrust` is still reported as a grant unless its full
signature is allow-listed, and
`ReaderConstructionSiteTests.TrustTypeMembers_AreClassified` requires the type
to account for every member it declares and to declare no nested types.

The structure was arrived at by being escaped. Rounds 3 and 4 of PR #4469 broke
a scan keyed on calls into the trust type four times: a member named `Classify`
that forwarded to the grant; a nested `Helper` reaching the table directly, so
its call sites never named the trust type at all; a static constructor that
granted from a staged reader; and a `MayMint(MetadataReader)` overload that
inherited the exemption belonging to the unrelated `MayMint`. Every one was a
fresh cosmetic dimension of the call surface, which is precisely the endless
series issue #4464 exists to stop. The field is not such a dimension — a grant
written as ordinary code has to name the table to mutate it — so within direct
IL access the pin is complete by construction rather than by enumerating the
ways a grant might be spelled.

That completeness is bounded in two ways worth stating rather than assuming.
Reflection over the field emits no `ldsfld`, so a reflective mutation reaches
the table without naming it in IL. And the scan watches the grant, not the
*consumer*: making `MayMintCoreLibraryIdentity` return `true` unconditionally,
having `TypeRefDecoder.CanonicalSelf` mint without consulting trust, or adding a
second trust store all confer identity while adding no referent to `s_trusted`,
and all leave the IL gate green. Neither bound is unguarded.
`PlantedCoreLibraryIdentityTests` owns them, and round 5 of PR #4469 confirmed
by tampering that each of those cases fails three of its tests —
`PlantedPlatformKey`, `DiscoveredSibling`, and `PlantedSibling`. The division of
labour is the same one stated above: this gate asks where readers come from and
what reaches the trust table, and that suite asks whether the identity is
deserved.

The pin is deliberately bounded: it answers where readers come from and which of
them are classified, not whether each grant is deserved. A method that passed a
discovered path into the raw-path designation overload would launder discovery
into designation while neither obtaining a reader nor granting identity itself,
so it would not appear in the pin. That property belongs to provenance, and
`PlantedCoreLibraryIdentityTests` gates it.

The scan also sees creation rather than receipt, so a method handed a reader
through a delegate, an interface, or a reflective invoke is invisible to it.
That is sound for two reasons. A reader created outside the assembly was never
classified, and unclassified means no core-library identity, so laundering one
inward loses the privilege rather than gaining it. And the grant is a direct
call into `CoreLibraryIdentityTrust` at five sites, every one of which the scan
reports, so a reflectively-obtained reader cannot be granted identity
invisibly. Reflection costs the completeness of the acquisition inventory, not
the trust boundary.

### Restored manifest paths remain within their owning roots

Paths read from `.deps.json` and `project.assets.json` are relative artifact
identity, not authority to choose a filesystem location. `StorePath` resolves
each path one root at a time and rejects rooted values, traversal segments,
volume-qualified segments, empty segments, and separator ambiguity before the
filesystem is consulted. A `.deps.json` `localPath` remains under the target
assembly directory. Package paths remain under the global packages root, and
asset paths remain under their owning package directory rather than merely
somewhere else in the package cache.

Project-assets rejection diagnostics name the manifest field and containment
rule without echoing the rejected value. The
`AssemblyDependencyResolverTests.ResolveAll_DepsJsonLocalPathCannotEscapeTargetDirectory`,
`AssemblyDependencyResolverTests.ResolveAll_DepsJsonPackagePathCannotEscapeGlobalPackagesRoot`,
`ProjectAssetsParserTests.Parse_LibraryPathCannotEscapeGlobalPackagesRoot`,
`ProjectAssetsParserTests.Parse_AssetPathCannotEscapeOwningPackageDirectory`,
`ProjectAssetsParserTests.ParsePackageReferences_LibraryPathCannotEscapeGlobalPackagesRoot`,
`ProjectAssetsParserTests.ParsePackageFileEntries_FilePathCannotEscapeOwningPackageDirectory`,
and `StorePathTests` gates enforce this boundary.

### Package archives use traversal-aware extraction

NuGet package extraction uses `ZipFile.ExtractToDirectory`, which rejects
archive entries that escape the destination directory. Extraction occurs under
process-created temporary directories before the validated content is committed
into product caches (`FileSystemPackageStore.CommitAsync`).

Symbol-package (`.snupkg`) PDB acquisition does not extract the archive to disk.
`SnupkgPdbReader` opens the archive in memory, matches candidate entries by file
name only (never by attacker-controlled directory paths), validates each
candidate's PDB header and complete Portable PDB content id (GUID plus stamp),
and returns the matching bytes. Those
bytes are then persisted through `IPdbStore`; the filesystem implementation
(`FileSystemPdbStore`) maps only store-composed, per-segment-validated keys onto
disk, so no archive-entry name is ever used as an output path. It publishes
through a unique sibling staging file and atomically replaces the final entry,
so a concurrent reader never accepts a partially written PDB. Symbol-server
keys include the canonical provider host and complete Portable PDB content
identity, preserving both provider and payload identity on cache reuse. A
pathless host reads the same validated store entry through
`AcquiredPortablePdb`; its explicit-capability service overload requires both
the store and an already authorized package-source policy, while the legacy
desktop overload remains path-bound instead of permitting a pathless caller to
discover ambient NuGet configuration. Store failures while opening or reading
that acquired entry propagate rather than becoming ordinary symbol absence.
These properties are gated by
`PdbIdentityTests.LoadPdbFromStream_RejectsMatchingGuidWithDifferentStamp`,
`PdbIdentityTests.PortablePdbIdentity_WindowsCodeViewCannotAuthorizePortablePdb`,
`SymbolPackageDownloaderTests.AcquirePdbAsync_MsdlCachePreservesProvider`,
`SymbolPackageDownloaderTests.AcquiredPortablePdb_DifferentStampsRemainRepeatable`,
`SymbolPackageDownloaderTests.AcquirePdbAsync_ExplicitStore_DoesNotUseAmbientCaches`,
`PdbAcquisitionServiceTests.DescriptorAcquisition_RequiresExplicitHostCapabilities`,
`PdbAcquisitionServiceTests.PathlessParticipant_DesktopOverloadDoesNotAcquire`,
`PdbAcquisitionServiceTests.PathlessParticipant_StoreReadFailureIsVisible`,
and
`PdbStoreTests.FileSystemPdbStore_FailedReplacementPreservesPublishedContent`.

Package identifiers and versions used as cache path components pass
`NuGetCache.ValidatePathComponent`, which rejects empty or whitespace values,
traversal (`..`), separators, volume qualifiers (`:`), null characters, and
otherwise rooted values before any cache path is built. Store keys (PDB cache
keys and package entry paths) resolve through the shared `StorePath.ResolveUnderRoot` guard: it splits
on `/`, rejects any segment that is empty, `.`, `..`, separator-bearing,
volume-qualified (`:`), null-character-bearing, or otherwise rooted, then
verifies the composed absolute path stays under the store root with a final
`Path.GetFullPath` containment check. This closes the Windows volume-reset
vector where `Path.Combine(root, "C:..", ...)` would discard the root, while
still permitting the interior dots of a real PDB or assembly file name. A PDB
file name recovered from untrusted PE debug metadata that is not a usable single
segment yields a graceful "no symbols" miss rather than an output path. General
cache entries use SHA-256-derived keys through `CoreCache`.

The Browser-Wasm package path is filesystem-free but uses the shared
`PackageCoordinateResolver`, `PackagePayloadAcquisition`,
`PackageArchiveValidator`, and `PackageResourceUrl` owners. Host policy supplies
the narrower bounds: the shared 16 MB text-response cap for a version listing,
128 MB for a downloaded nupkg, 512 MB for aggregate declared archive expansion,
64 MB for one expanded assembly entry, and 16 MB for one expanded Markdown or
XML entry. It also rejects more than 4,096 archive entries. The shared archive
validator selects the same highest-offset end record as `ZipArchive` and scans
its central directory without allocating entry objects before `ZipArchive` can
materialize them, then applies its path, directory, compression, CRC, and
observed-expansion checks.
`InMemoryPackageContent` rejects an entry whose declared expanded length exceeds
the caller's limit before allocating that length, then verifies the observed
expansion against the declaration. `InMemoryPackageContentTests` gates the
pre-expansion rejection and bounded declared/unknown-length stream reads.
The host's 128 MB package-cache budget is aggregate: an open inspection scope's
nupkg remains represented in the same LRU, and evicting that package disposes
every retaining scope before removing the cache entry. Scope reuse therefore
cannot keep an evicted archive alive outside the advertised budget.
Package downloads must declare their content length. The Browser implements
`IPackagePayloadTransferPolicy`, which reserves that length and evicts unleased
entries after shared transport receives the headers but before it reads the
body. The reservation becomes a cache entry only after shared archive validation,
store commit, and re-admission complete.
Composite workspace construction temporarily leases each resolved coordinate,
so a later acquisition cannot evict an earlier pending coordinate. In-flight
reservations and retained cache entries share the same 12-package/128 MB limit.
Before assembly identity decoding, each workspace role also rejects more than
256 selected assemblies or a declared expanded total above that role's 32/64 MB
retained-image budget.
Browser API-surface projection additionally spends one shared
32,000,000-character retained-text budget across its selected assemblies. The
extractor charges every string-bearing model field as it retains each member,
type, inspection failure, type forwarder, and late canonical identity; repeated
references are charged per field. It observes text incrementally while decoding
type nodes, parameters, attributes, generic constraints, and interfaces. A
separate extraction-wide work ledger charges encoded names, signature nodes,
and custom-attribute blobs before materializing their expanded forms. It grants
one fixed decode floor, then only bounded credit for retained model text;
accepted or rejected type candidates cannot rearm the floor. The exact
retained-character total therefore remains unchanged while one wide signature,
deeply nested generic head or argument, nested or high-rank array, default value,
hidden signature, or attribute cannot allocate its complete amplified model
before the check. Composite type nodes carry non-materializing rendered-length
estimates, and bounded decoding charges each constructed node's complete
estimated output so repeated wrapping cannot reallocate an uncharged subtree.
Structured nested type names are read once and enforce their cumulative limit
before the remaining chain is materialized. Legacy `FormatChain`, `ReadChain`,
and leaf-append readers preflight UTF-8 storage against three times that
4,096-character budget (the UTF-8 worst case), then recheck decoded length,
and report `NameBudget` rather than malformed or success-shaped output.
Display string APIs (`GetFullName`, `GetTypeNameFrom*`) keep only the encoded
cap so a 5,030-character classifier fixture can still be spelled;
`Resolve*`/`Read` remain on the 4,096-character policy.
`AppendLeaf` continues the declaring walk's live encoded and character
ledgers rather than reseeding them from rendered text. Shared #Strings entries, many individually
small segments, projected WinRT virtual strings, TypeSpec `NameBudget` kind
preservation, and the display/structured split are gated by
`SharedOversizeHeapString_IsRejectedBeforeAggregateMaterialization`,
`ManySmallSegments_AreRejectedOnAggregateEncodedLength`,
`LeafAppendOverBudget_IsRejectedBeforeLeafMaterialization`,
`StructuredRead_ReportsNameBudgetNotMalformed`,
`ProjectedVirtualStringLength_IsRecheckedAfterBlobReader`,
`TypeSpecNameBudget_IsPreservedAsTypedEvidence`,
`TypeSpecNameBudget_SurvivesLaterMalformedArgument`,
`AppendLeaf_PreflightsActualUtf8OfMaterializedDeclaringName`,
`DisplayNameApis_AdmitCharacterOverBudgetNamesUnderTheEncodedCap`,
`NestedDisplayNameApis_AdmitCharacterOverBudgetNamesUnderTheEncodedCap`,
`TypeSpecDisplayNameApis_AdmitCharacterOverBudgetNamesUnderTheEncodedCap`,
`EmptyLeadingNameSegment_DoesNotCollideWithTopLevelName`,
`EmptyNamespaceExactCharacterBudget_AgreesWithCreate`,
`EmptyNamespaceNestedResolveFullName_AgreesWithCreate`, and
`NilNameAfterEncodedCap_IsRejectedOnDisplayPath`. Tuple-name, nullability, and dynamic
transform arrays charge their encoded blob before allocating arrays, and one
type generic context is reused across all of that type's members.
Visibility probes use bounded blob readers rather than copying skipped
attribute values. Declared custom-attribute SZArray and named-argument counts
are checked against remaining value-blob bytes before SRM allocates builders
from those counts, and each declared slot is charged as decode work so a
hostile four-byte count cannot become a gigabyte-scale argument array or a
swallowed OOM. The same walk covers each named argument's
`FieldOrPropType`, name, and value — a named SZArray count or a nested
named array type is not left for `DecodeValue` to allocate or recurse
on. Boxed and nested SZArray encodings are walked on a heap work-stack
and depth-bounded before decode, so a chain of tags cannot overflow the
native stack even if the policy cap moves. Signature-type skips reuse
one work-stack per decode (`Clear` at entry) so a wide `int[][]` cannot
allocate a stack per inner array. That reuse is structural; the path
gate is `CustomAttributeValueGuardTests.NestedEmptySzArray_IsSafe`. The
small-stack gate is the 128 KiB
`CustomAttributeValueGuardTests.BoxedNestingAtLimit_OnSmallNativeStack_IsSafe`. Enum-typed
fixed and named arguments use one shared underlying-width oracle, so a
TypeRef that resolves to a local non-`int32` enum, a TypeDef whose
full name collides with an earlier row, an over-deep
`value__` field signature, or a non-fixed-width `value__` primitive
such as `string`, cannot desynchronize later count reads.
A serialized enum name that is not a TypeDef in the current image
stays `int32` unless a caller-supplied resolver found the defining
image; local TypeDefs still win over that resolver. Guard and SRM
invoke that resolver with the same normalized name (assembly suffix
stripped), so a resolver keyed on the simple name cannot skip four
bytes in the guard and eight in SRM. Absent a defining image, decode
returns null rather than emitting values from a four-byte skip of an
eight-byte argument. A defining image does not make a later hostile
count legal.
`CLASS`/`VALUETYPE` `System.Type` uses the same rendered-name oracle as
SRM (`type == "System.Type"`), so a TypeRef whose namespace is empty
and whose name is `System.Type`, or a nested `System`+`Type` TypeRef,
consumes a SerString rather than four enum bytes.
A truncated value walk returns `Truncated` and stops remaining work,
so leftover named-count bytes after a short SZArray cannot be charged
as 65,535 named arguments. A boxed SZArray of `ENUM` consumes the
enum-name SerString before the `Int32` count, matching SRM's
`DecodeNamedArgumentType(isElementType: true)`, so an empty name
cannot hide a gigabyte-scale builder behind a 9-byte blob and a
legal boxed enum array is not dropped.
Declared SZArray leftovers stop once the value blob is exhausted, so a
jagged constructor signature cannot re-walk the element type once per
unreadable slot.
Serialized enum names are normalized the same way SRM's provider sees
them (assembly suffix stripped, nested `+` matched to the metadata
index). `CLASS`/`VALUETYPE` constructor parameters special-case only
`System.Type`; other tokens use the enum-width oracle so a
`class System.String` argument cannot shift later counts. Generic
attribute constructors whose parameter is a `VAR` resolve that
argument through the owning TypeSpec. Earlier generic arguments
that cannot be skipped, including `FNPTR` and `PTR`+`FNPTR`, fail
closed instead of leaving the substituted value unconsumed. Earlier
`CLASS`/`VALUETYPE` arguments are skipped the same way SRM
`CustomAttributeDecoder.SkipType` does — including treating a
TypeDefOrRef coded index as another type code — so a TypeDef row 4
or TypeRef row 4 cannot hide a later SZArray count. A
substituted type is not itself re-substituted, so a self-referential
`GENERICINST` `!0` cannot recurse the guard. A budget observer failure
raised while the guard consults the enum index unwraps to the same
typed truncation `DecodeValue` already propagated. Every bounded member-name decode and every namespace/name
segment used to resolve an attribute type is also charged before SRM
materializes it, including names inspected only to skip an accessor,
compiler-generated field, or hidden member. Property-accessor nullable-context
probes use the same observer as method attributes, so a TypeSpec constructor
parent on a getter is charged before its rank string is rendered. Property
`ref` return spelling charges every SequenceNumber-0 Param row the same way
the method path does, so extra return-parameter rows cannot multiply an
uncharged TypeSpec decode. This includes every enclosing
TypeDef or TypeRef segment reached from a signature, skipped enum names scanned
while formatting defaults, forwarded type and target-assembly names, and
strong-name blobs read while proving a finalizer slot reaches the core library.
TypeSpec-owned generic attribute constructors use the same guarded signature
decoder and preserve bounded/unbounded parity. Enum-valued arguments build one
charged type-name index per extraction instead of rescanning every type
for every argument or attribute. That includes parameter attributes and the
DecimalConstant/DateTimeConstant default-materialization path, which reuses the
same extraction-wide observer even though those attributes are not re-emitted
in the surface. Both a successful index and its rejected
outcome are cached, so malformed metadata is reported once rather than silently
reallocating the index for each attribute. The same cache is used on the
unbounded path, which is the public `AssemblyReader.ExtractApiSurface` route. Enum-default classification also
charges base-type names before resolving them. Decode failures may skip malformed
attributes, but a budget-observer failure must escape the decoder and produce
typed truncation. The Browser separately applies the same bound while deriving
type/member transport records, including canonical signatures, documentation
IDs, and graph selectors that repeat declaring-type identity. It preflights
each source model against the budget remaining after committed and pending
participants before creating those derived strings, and collision-qualified
IDs are created and charged before their participant commits. An assembly that
would exceed either remaining retained-text budget is abandoned whole, and the
Browser reports truncation rather than presenting a shortened surface as
complete.
`BrowserEngineBoundaryTests.WorkspaceOwnership_AccountsArchivesAndCarriesSelectedFailures`
gates aggregate ownership and eviction; its oversized-role case gates
pre-decoding rejection.
`ApiSurfaceExtractorBoundsTests.RetainedTextBudget_IsExact`,
`RepeatedLongMemberName_StopsBeforeLargeAllocationAmplification`,
`RepeatedLongSkippedAccessorName_StopsBeforeLargeAllocationAmplification`,
`RepeatedLongSkippedFieldName_StopsBeforeLargeAllocationAmplification`,
`RepeatedLongVisibilityAttributeTypeName_StopsBeforeLargeAllocationAmplification`,
`OneWideSignature_StopsBeforeLargeAllocationAmplification`,
`OneInterfaceHeavyType_StopsBeforeLargeAllocationAmplification`,
`OneWideFieldSignature_StopsBeforeLargeAllocationAmplification`,
`OneWideTypeSpec_StopsBeforeLargeAllocationAmplification`,
`OneLargeCustomAttribute_StopsBeforeLargeAllocationAmplification`,
`OneHugeCustomAttributeArrayCount_StopsBeforeLargeAllocationAmplification`,
`RepeatedNamedArgumentCount_StopsBeforeLargeAllocationAmplification`,
`PropertyAccessorNullableContextTypeSpec_StopsBeforeLargeAllocationAmplification`,
`PropertyRefReturnDuplicateSeq0Attributes_StopsBeforeLargeAllocationAmplification`,
`LegalNamedAttribute_HasBoundedUnboundedParity`,
`DeepBoxedCustomAttribute_StopsBeforeStackOverflow`,
`OneHugeNamedArgumentArrayCount_StopsBeforeLargeAllocationAmplification`,
`DeepNamedNestedArrayCustomAttribute_StopsBeforeStackOverflow`,
`TypeRefEnumWidthDesync_StopsBeforeLargeAllocationAmplification`,
`OverDeepEnumFieldModifiers_StopsBeforeLargeAllocationAmplification`,
`CustomAttributeValueGuardTests.HugeNamedArgumentArrayCount_IsUnsafe`,
`CustomAttributeValueGuardTests.BoxedNestingAtLimit_OnSmallNativeStack_IsSafe`,
`CustomAttributeValueGuardTests.NestedEmptySzArray_IsSafe`,
`CustomAttributeValueGuardTests.WideInt32Array_IsSafe`,
`CustomAttributeValueGuardTests.NamedArrayNestingJustOverLimit_IsUnsafe`,
`CustomAttributeValueGuardTests.TypeRefEnumMatchingLocalInt64_SeesFollowingArrayCount`,
`CustomAttributeValueGuardTests.DuplicateTypeDefEnumName_SeesFollowingArrayCount`,
`CustomAttributeValueGuardTests.ExhaustedJaggedSzArray_IsSafe`,
`CustomAttributeValueGuardTests.OverDeepEnumFieldModifiers_UseInt32WidthAndSeeFollowingArrayCount`,
`CustomAttributeValueGuardTests.AssemblyQualifiedNamedEnum_SeesFollowingArrayCount`,
`CustomAttributeValueGuardTests.CrossAssemblyInt64NamedEnum_WithoutDefiningImage_DoesNotDecode`,
`CustomAttributeValueGuardTests.CrossAssemblyInt64NamedEnum_WithDefiningImage_Decodes`,
`CustomAttributeValueGuardTests.CrossAssemblyInt64NamedEnum_WithDefiningImage_StillRefusesHostileCount`,
`TypeResolutionEnumWidthTests.PlannedQualifiedName_DecodesInt64FromRetainedDefiningImage`,
`TypeResolutionEnumWidthTests.UnplannedRequest_StaysInt32`,
`TypeResolutionEnumWidthTests.MissingDefiningImage_StaysInt32`,
`TypeResolutionEnumWidthTests.FacadeForwarder_DecodesInt64`,
`TypeResolutionEnumWidthTests.HostileLeftoverCount_IsUnsafe`,
`CustomAttributeValueGuardTests.CrossAssemblyInt64NamedEnum_ExactSimpleNameResolver_Decodes`,
`CustomAttributeValueGuardTests.CrossAssemblyInt64NamedEnum_ExactSimpleNameResolver_SeesOverlappingHostileCount`,
`CustomAttributeValueGuardTests.LocalInt64EnumFixedArgument_IgnoresConflictingExternalResolver`,
`CustomAttributeValueGuardTests.DirectGuard_LocalInt64NamedEnum_IgnoresInt32Resolver_SeesOverlappingHostileCount`,
`CustomAttributeValueGuardTests.DirectGuard_NormalizesNonFixedWidthResolver_SeesHostileCount`,
`CustomAttributeValueGuardTests.DirectGuard_MalformedTypeDefIndex_DoesNotBypassHostileCount`,
`CustomAttributeValueGuardTests.ClassSystemStringFixedArgument_SeesFollowingArrayCount`,
`CustomAttributeValueGuardTests.DottedSystemTypeTypeRef_SeesFollowingArrayCount`,
`CustomAttributeValueGuardTests.NestedSystemTypeTypeRef_SeesFollowingArrayCount`,
`CustomAttributeValueGuardTests.LegalSystemTypeArgument_IsSafe`,
`CustomAttributeValueGuardTests.StringTypedEnumValue_SeesFollowingArrayCount`,
`CustomAttributeValueGuardTests.TruncatedInt32ArrayThenHugeNamedCount_IsSafe`,
`CustomAttributeValueGuardTests.LegalBoxedEnumArray_IsSafe`,
`CustomAttributeValueGuardTests.LegalBoxedInt32Array_IsSafe`,
`CustomAttributeValueGuardTests.BoxedEnumArrayEmptyName_SeesFollowingArrayCount`,
`CustomAttributeValueGuardTests.NamedBoxedEnumArrayEmptyName_SeesFollowingArrayCount`,
`CustomAttributeValueGuardTests.GenericAttributeTypeParameterInt32_IsSafe`,
`CustomAttributeValueGuardTests.FnPtrEarlierGenericArgumentThenArray_SeesFollowingArrayCount`,
`CustomAttributeValueGuardTests.PtrFnPtrEarlierGenericArgumentThenArray_SeesFollowingArrayCount`,
`CustomAttributeValueGuardTests.ClassTypeDefRow4EarlierArgument_SeesFollowingArrayCount`,
`CustomAttributeValueGuardTests.ValueTypeTypeRefRow4EarlierArgument_SeesFollowingArrayCount`,
`CustomAttributeValueGuardTests.SelfReferentialGenericVar_IsUnsafe`,
`ClassTypeDefRow4EarlierArgument_StopsBeforeLargeAllocationAmplification`,
`ValueTypeTypeRefRow4EarlierArgument_StopsBeforeLargeAllocationAmplification`,
`CustomAttributeValueGuardTests.ObserverFailureDuringNamedEnumLookup_EscapesTryDecode`,
`FnPtrEarlierGenericArgumentThenArray_StopsBeforeLargeAllocationAmplification`,
`PtrFnPtrEarlierGenericArgumentThenArray_StopsBeforeLargeAllocationAmplification`,
`SelfReferentialGenericVar_StopsBeforeStackOverflow`,
`AssemblyQualifiedNamedEnum_StopsBeforeLargeAllocationAmplification`,
`ClassSystemStringFixedArgument_StopsBeforeLargeAllocationAmplification`,
`DottedSystemTypeTypeRef_StopsBeforeLargeAllocationAmplification`,
`StringTypedEnumValue_StopsBeforeLargeAllocationAmplification`,
`BoxedEnumArrayEmptyName_StopsBeforeLargeAllocationAmplification`,
`LegalNestedLongEnumNamedArgument_HasBoundedUnboundedParity`,
`LegalGenericCtorAttribute_HasBoundedUnboundedParity`,
`RepeatedEnumAttributeLookups_DoNotAllocateQuadratically`,
`SeparateEnumAttributes_ReuseTheChargedTypeNameIndex`,
`FailedEnumAttributeIndexBuild_IsCachedAndVisible`,
`FailedEnumAttributeIndexBuild_IsCachedOnTheUnboundedPath`,
`ParameterEnumAttributes_ReuseTheChargedTypeNameIndex`,
`ParameterEnumAttributes_ReuseTheChargedTypeNameIndexOnTheUnboundedPath`,
`DecimalConstantParameterAttributes_ReuseTheChargedTypeNameIndex`,
`DecimalConstantParameterAttributes_ReuseTheChargedTypeNameIndexOnTheUnboundedPath`,
`GenericAttributeTypeSpec_StopsBeforeLargeAllocationAmplification`,
`OneDeeplyNestedTypeSpec_StopsBeforeLargeAllocationAmplification`,
`OneArgumentNestedTypeSpec_StopsBeforeLargeAllocationAmplification`,
`OneNestedArrayType_StopsBeforeLargeAllocationAmplification`,
`EnclosingTypeNameChain_StopsBeforeLargeAllocationAmplification`,
`EnclosingTypeReferenceChain_StopsBeforeLargeAllocationAmplification`,
`RejectedTypes_SpendDecodeWorkAcrossTheExtraction`,
`LargeTransformArray_StopsBeforeLargeAllocationAmplification`,
`RepeatedMethodGenericContext_ReusesTypeParameterNames`,
`OneHugeArrayRank_StopsBeforeLargeAllocationAmplification`,
`HiddenAutoPropertySignature_StopsBeforeLargeAllocationAmplification`,
`HugeParameterDefault_StopsBeforeLargeAllocationAmplification`,
`EnumDefaultScan_ChargesSkippedEnclosingTypeNames`,
`EnumDefaultScan_ChargesRejectedBaseTypeNames`,
`EnumDefaultScan_ChargesTypeSpecArrayRank`,
`AttributeTypeSpec_ChargesArrayRankBeforeRendering`,
`LocalExtensionAttachment_DoesNotAllocateQuadratically`,
`FinalizerScan_ChargesCoreLibraryPublicKeyBeforeCopying`,
`PropertyAccessorReturnAttribute_StopsBeforeLargeAllocationAmplification`,
`EventAccessorReturnAttribute_StopsBeforeLargeAllocationAmplification`,
`LargeVisibilityAttribute_StopsBeforeDecodingItsMessage`,
`RepeatedHiddenAttributeProbe_DoesNotCopyTheValueBlob`,
`ExhaustedForwarderBudgetStopsBeforeDecodingItsName`,
`ForwarderTargetAssemblyIsChargedBeforeDecoding`,
`ExtensionReceiverIdentityContributesItsOwnRetainedText`,
`GenericAttributeConstructorHasBoundedUnboundedParity`,
`AssemblyContextApiSurfaceQueryTests.ExecuteBounded_SpendsRetainedTextAcrossParticipants`,
and
`BrowserEngineBoundaryTests.ApiSurfaceProjection_IsBoundedAndReportsTruncation`
gate exact extraction, metadata allocation-amplification shapes,
shared-budget spending, and host reporting. The original allocation gate uses a
146 KB synthetic PE containing 10,000 methods with one repeated 4,000-character
name: unbounded extraction allocated approximately 335 MB on the measuring
host, while the bounded path allocated approximately 22 MB and returned typed
truncation. The wide-signature and interface-heavy gates concentrate repeated
4,000-character names inside one member and one type respectively and require
each bounded path to allocate less than 64 MB. The skipped-accessor canary uses
a 92 KB image whose 10,000 repeated 4,000-character names allocated
approximately 200 MB while escaping both retained text and member counts; the
bounded path now returns typed truncation after approximately 4 MB.
`BrowserEngineBoundaryTests.SurfaceProjection_LongDeclaringTypeStopsIncrementally`
gates the derived-identity transport budget.
`SurfaceProjection_OneHugeTypeStopsBeforeDerivedIdentities` and
`SurfaceProjection_OneHugeMemberStopsBeforeDerivedIdentities` gate
pre-materialization rejection for one amplified transport record.
`SurfaceProjection_PreflightUsesTheRemainingSharedBudget` gates preflight after
earlier participants have committed most of that shared budget.
`QueryPackage_FirstTransportTruncationReturnsTypedNotice` gates typed
zero-participant truncation, and
`SurfaceProjection_QualifiedCollisionIdIsAccountedBeforeCommit` gates final-ID
accounting before participant commit. Finally,
`ApiSurfacePolicy_AcceptsCoreLibraryAtEveryBrowserScope` pins the 32-million
policy against CoreLib at both Browser extraction scopes.
`PackageArchiveEntryFlood_IsRejectedBeforeArchiveEnumeration` gates the
host-specific central-directory entry limit.
`PackagePayloadAcquisitionTests.TransferPolicy_ReservesBeforeBodyReadAndCompletesAfterCommit`,
`TransferPolicy_RejectedPayloadDisposesWithoutCompleting`, and
`TransferPolicy_CanRequireContentLengthBeforeBodyRead` gate the capacity seam.

Those controls are specific to the Browser-Wasm acquisition host. Archive
containment in the broader product does not itself bound expanded bytes, entry
count, or disk consumption. Symbol acquisition now accepts optional
host-supplied limits for response bytes, expanded PDB bytes, archive entry
count, aggregate candidate-PDB expansion, and in-memory retention; the Browser
supplies those limits on every symbol-server and symbol-package path. Bounded
symbol-package inspection rejects ZIP64 sentinels in the end-of-central-directory
record before archive enumeration and observes
cancellation while expanding candidates. Browser source operations are
exclusive and superseding, and each operation leases its workspace and package
archives until its bounded PDB and source stores are released. This prevents
concurrent stale requests or workspace eviction from multiplying the
request-local budgets; `SourceOperations_AreExclusiveAndSuperseding` and
`ActiveScopeLease_PreventsWorkspaceAndPackageEviction` gate that host contract.
A canceled caller also stops waiting on shared package acquisition immediately,
releasing the exclusive source-operation gate while the shared download remains
inside the aggregate package-cache reservation for other consumers;
`CancelledWait_ReleasesSharedPackageAcquisition` gates that ownership split.
Callers that do not supply limits retain the shared 500 MB transport ceiling
as the per-PDB expansion ceiling;
`ExtractPortablePdb_WithoutHostLimitsRejectsDeclaredExpansionAboveTransportCeiling`
gates that a hostile ZIP declaration cannot bypass it. That end-of-central-directory
sentinel check does not cover a per-entry ZIP64 extra field, which is where
`ZipArchiveEntry.Length` actually comes from, so every declared PDB length is
also rejected when it is negative. A negative length clears every ceiling
comparison and then narrows, unchecked, to a large positive allocation — .NET 10,
which official builds target, surfaces such a length, while .NET 11 rejects the
archive earlier. `SnupkgPdbReaderTests.ValidateDeclaredPdbLength_RejectsNegativeDeclaredLength`
gates the lower bound on every runtime and
`ExtractPortablePdb_RejectsNegativeZip64DeclaredLength` is the end-to-end canary.
Product-wide default
aggregate expansion, entry-count, and retention budgets remain an open
requirement below.

RID companion verification has a narrower aggregate compressed-input bound:
one operation reads at most 500 MB across all local sibling archives, in
addition to the per-archive limit. Exhaustion leaves unexamined existing
candidates indeterminate rather than reporting authoritative absence, while
missing paths need no byte reservation. Reservation uses the length of the
opened handle and the bounded reader consumes that same handle, preventing a
path replacement from acquiring an uncharged allowance. The two cases of
`RidPackageVerifierTests.VerifyAsync_LocalArchiveReadBudgetIsShared` gate
exhaustion and positive evidence within the budget;
`ProbeLocalPackageArchiveAsync_MissingThenCreatedArchiveConsumesBudgetWhenOpened`
gates reservation ownership.

### Untrusted JSON rejects duplicate properties

JSON does not define how duplicate object keys resolve, so two readers of one payload can
disagree. `DotnetInspector.Core.HardenedJson` and SourceLinkFetch's map parser reject duplicate
properties, while `ILInspector.SourceLink.SourceLinkJsonContext` applies the same rule to its
persistent type-index cache. Such payloads fail visibly instead of binding one of
several possible readings.

This is generic hardening, not a fix for a known divergence. The SourceLink
provenance divergence it does **not** address is closed separately, by the
control below.

Feed responses, package contents, `project.assets.json`, `.deps.json`, and product cache entries
are *intended* to parse through the same guard, and most do. Callers that already treated malformed
JSON as "no data" now treat duplicate-bearing JSON the same way; that is fail-closed, but it does
not by itself convert those callers to explicit failure reporting, which remains open work below.

The coverage is not yet complete, and the gaps are on the feed path specifically:
`PackageExtractor` uses `HardenedJson.Parse` at four call sites but plain
`System.Text.Json.JsonDocument.Parse` at two more when reading registration pages, and
`NuGetFetch.NuGetApi` deserializes the service index, version index, and search responses through a
source-generated context that does not reject duplicates. `runfaster` also still parses its trace
inputs directly. Nothing gates the invariant, which is why the gaps persisted; see open work below.

### NuGet metadata response bodies are bounded

NuGetFetch reads service indexes, version indexes, and search responses headers-first, rejects an
advertised `Content-Length` above the configured ceiling, and counts the bytes actually consumed
when the length is absent or false. The default ceiling is 16 MiB. Every HTTP request, including
response-body consumption, has a 30-second default request deadline that a shorter configured
`HttpClient.Timeout` tightens. A logical operation spanning service discovery, version or search
metadata, pagination, and package-stream consumption has a separate 120-second default ceiling.
Direct `NuGetApi` stream readers use the request deadline as their body-parse timeout, and callers
may configure a stricter metadata-body timeout. Header-first NuGet and package-layer requests also
require Browser/Wasm streaming-response mode so the browser transport cannot buffer an unbounded
body before the counting or deadline streams see it.
`NuGetSearchSourcesTests.GetSearchQueryServiceAsync_ServiceIndexRequiresBrowserStreamingResponse`
gates the mandatory search-discovery path.

Oversize, request-timeout, operation-timeout, and optional body-timeout failures have dedicated
exception types. They are not represented as `JsonException`, `HttpRequestException`, a null
document, or an empty result, so existing malformed-JSON handling and multi-source fallback cannot
turn a resource-limit failure into success-shaped output. Direct `NuGetApi` stream consumers pass
through the same bounded reader. Package payload streams (`.nupkg` and `.snupkg`) are deliberately
excluded from the metadata byte ceiling; their larger download policy belongs to the acquisition
layer, while request and operation deadlines still cover their streamed consumption.

This is gated by
`NuGetMetadataLimitTests.Search_AdvertisedOversizeRejectsBeforeReadingTheBody`,
`NuGetMetadataLimitTests.Search_UnderreportedLengthCannotBypassTheActualByteLimit`,
`NuGetMetadataLimitTests.NuGetGets_RequestBrowserStreaming`,
`NuGetMetadataLimitTests.StalledBodyUsesTheBodyPhaseTimeout`,
`NuGetMetadataLimitTests.ShorterHttpClientTimeoutBoundsTheWholeRequest`,
`NuGetMetadataLimitTests.DirectNuGetApiReadersUseTheDefaultLimit`, and
`NuGetMetadataLimitTests.PackagePayloadIsNotSubjectToTheMetadataLimit`, plus
`NuGetDeadlineTests.RequestDeadline_BoundsARequestBeforeHeaders`,
`NuGetDeadlineTests.RequestDeadline_BoundsPackageStreamConsumption`,
`NuGetDeadlineTests.OperationCeiling_SpansServiceDiscoveryAndVersionLookup`,
`NuGetDeadlineTests.OperationCeiling_IncludesPackageStreamConsumption`, and
`NuGetDeadlineTests.CallerCancellation_IsNotReportedAsADeadline`, plus
`HttpRetryHelperTests.HeaderFirstBodyRead_RequiresBrowserStreamingResponse`,
`HttpRetryHelperTests.StringBodyRead_RequiresBrowserStreamingResponse`,
`HttpRetryHelperTests.StreamedResponse_RequiresBrowserStreamingResponse`, and
`HttpRetryHelperTests.RangeResponse_RequiresBrowserStreamingResponse`.

### Malformed NuGet metadata fails visibly

The `NuGetApi` readers propagate service-index, version-index, and search documents with invalid
JSON or missing required data as `JsonException` rather than representing them as an absent
resource, empty version list, or empty search. A top-level JSON `null` remains an explicit null
document. The multi-source client isolates `JsonException` to the source that supplied the
malformed document and continues to later sources, while metadata response limits and body
timeouts remain fatal.

This is gated by `NuGetApiTests.GetServiceIndexAsync_MalformedJson_Throws`,
`NuGetApiTests.GetServiceIndexAsync_InvalidRequiredData_Throws`,
`NuGetApiTests.GetVersionIndexAsync_MalformedJson_Throws`,
`NuGetApiTests.GetVersionIndexAsync_InvalidRequiredData_Throws`,
`NuGetApiTests.GetSearchResponseAsync_MalformedJson_Throws`,
`NuGetApiTests.GetSearchResponseAsync_InvalidRequiredData_Throws`,
`NuGetApiTests.MetadataReaders_TopLevelNull_RemainsNull`, and
`NuGetClientTests.LatestVersion_MalformedSourceContinuesToHealthySource`.

### SourceLink provenance is read off the URL source is fetched from

SourceLink map presence is not reported as successful usability. The
SourceLink-aware audit retains whole-map parse failures and individually
rejected keys, reports unusable and partially usable states in `Signals`, and
exposes details through `SourceLink: Diagnostics`. Authored document keys also
participate in `Non-normalized Paths`; a normalized CodeView path cannot hide a
non-normalized SourceLink key. This fail-visible boundary is gated by
`CommandExecutionTests.Library_MalformedSourceLink_ReportsMapAndPathDiagnostics`,
`OutputFormatterTests.SingleAudit_Signals_UnusableSourceLink_ReportsTheParseError`,
and
`CommandExecutionTests.SourceLinkAudit_NormalizedFixtureStaysClean`.

Reported provenance must describe the origin that source content is actually
fetched from, for every document the assembly resolves. When that cannot be
established for all of them, report no repository.

`SourceLinkFetch.SourceLinkProvenance` is the single owner of this rule. It
resolves every document the assembly declares through
`SourceLinkFetch.SourceLinkResolver` — the single owner of the mapping rule —
and reads the origin off each **final resolved URL, after wildcard substitution,
percent-encoding, and `System.Uri` canonicalization**. Never off the mapping
text, and never off the mapping prefix alone. Agreement is required on the whole
`(host, organization, repository, revision)` tuple, because
`raw.githubusercontent.com` serves any revision reachable in a repository,
including the head of an unmerged pull request.

Every way a weaker formulation has been found to fail, all reproduced. They are
a regression floor, not a specification of what to block: each was found only by
attacking a previous formulation, so passing them is not evidence that the
invariant holds. The list deliberately carries no count — nothing enforced the
one that was here, and it had gone stale by two.

- Agreement on `owner/repo` ignores the revision, so two entries on one
  repository at different commits "agree" while serving different code.
- `System.Uri` applies RFC 3986 dot-segment removal, so a mapping value
  containing `../` is fetched from the traversed-to path while a regex over the
  raw string reports the literal one.
- Even a clean mapping is not enough. The wildcard suffix comes from the PDB
  document path, which is equally attacker-controlled, so a benign
  `.../dotnet/runtime/<commit>/*` resolves
  `/_/../../../attacker/evil/main/Program.cs` into `attacker/evil`.
- `System.Uri` preserves percent-encoded separators verbatim: `..%2f` and
  `..%5c` survive canonicalization, so a "canonicalize, then prefix-check" step
  passes while a server that percent-decodes before resolving dot segments still
  traverses out. Encoded separators are rejected rather than assumed resolved.
  Encoded dots are different: `System.Uri` decodes them, removes encoded parent
  segments from the path before the origin is read, and sends that canonical
  path. Provenance therefore follows the final origin exactly as it does for
  literal `..`; an encoded dot in a file name is not treated as traversal.
  Measured against `dotnet/runtime` commit `9904b934...`, GitHub serves
  `README%2Emd` as the same 4800 bytes and SHA-256 as `README.md`. Gated by
  `SourceLinkProvenanceTests.AnEncodedDotOutsideAParentSegment_RemainsAttributable`,
  `...AnEncodedParentSegment_ReportsWhereContentIsReallyServedFrom`, and
  `...AnEncodedDotInAnAzureContentPath_RemainsAttributable`.
- `https://raw.githubusercontent.com@evil.example/...` parses with host
  `evil.example` and user info `raw.githubusercontent.com`. The host allow list
  rejects it, since `Uri` takes the authority after the last `@`; user info is
  additionally rejected on its own account, because a credential presented to an
  allowed host makes the response depend on the identity presented rather than
  on the public path the URL names.
- `raw.githubusercontent.com` serves branch names, and a branch may contain `/`,
  so `.../owner/repo/feature/auth/File.cs` reads equally well as revision
  `feature` with path `auth/File.cs` or as revision `feature/auth` with path
  `File.cs`. Nothing in the URL says which. Taking the third path segment made
  `feature/auth` and `feature/login` report one revision and one cache identity.
  The revision must therefore be a full commit hash, which cannot contain `/`,
  or the URL is not attributable.
- Whether a host matches query parameter names case-insensitively is not stated
  by the URL, so `?VERSION=evil&version=legit` has two readings. A case-sensitive
  match reports `legit` while a case-insensitive host may serve `evil`. A
  parameter differing from the expected spelling only by case is not
  attributable.
- Azure's Items API accepts the revision as the flat `version` parameter and as
  `versionDescriptor.version`, and the **descriptor takes precedence**. Reading
  only `version` reported the losing selector, so a URL carrying both named one
  revision while fetching the other. Confirmed against the live API. Both
  spellings are read; disagreeing selectors are not attributable.
- The cache identity must be an unambiguous serialization of the origin tuple.
  Azure DevOps repository names and Git ref names may both contain `/` and `@`
  (`git check-ref-format` accepts `branch@tip`), so a delimiter-joined key let
  repository `repo@branch` at revision `tip` and repository `repo` at revision
  `branch@tip` collide. The identity is length-prefixed. This key selects a
  persistent source index, so a collision serves one repository's source for
  another's assembly.
- A query parameter repeated with *equal* values still has two readings, and the
  host takes neither: measured against the live API,
  `?version=aaaa&version=aaaa` returns 400 "Ambiguous values for version". An
  earlier note here reasoned from `HttpUtility.ParseQueryString`, which joins
  repeats with a comma, and concluded Azure would select the ref `aaaa,aaaa`.
  That is a client decoder's behaviour, not the host's; the refusal was right
  and the stated mechanism was wrong. A repeat is refused however its values
  compare.
- The repeat rule stopped at the revision selectors, and the **content**
  selectors are where it mattered. Azure serves the *first* occurrence of
  `path`, so `path=/fixed.cs&path=/*` substitutes every document into an
  occurrence the host ignores: each document produces a distinct URL — enough
  for the resolver's two-probe check, which sees only text — while every one of
  them fetches `fixed.cs`. Measured: `path=/README.md&path=/nope.txt` returns
  README, and `path=/.gitignore&path=/README.md` returns 404 for the *first*
  path. Names are compared case-insensitively because the host binds them that
  way, also measured. No parameter may be given twice.
- The segments before `_apis` are the host's route, and joining however many
  there were reported an organization that was assembled rather than read. A
  project-less `dev.azure.com/{org}/_apis/...` was attributed to `{org}` at a
  commit, and `dev.azure.com/a/b/c/_apis/...` to the organization `a/b/c` with
  the repository page `https://dev.azure.com/a/b/c/_git/{repo}`, which is not a
  page. Measured, the route is keyed on exactly organization and project: the
  two-segment shape returns 200, while project-less, wrong-project and
  wrong-organization shapes each redirect to a sign-in page on another host and
  an extra segment returns 404. The count is now fixed per host — two on
  `dev.azure.com`, one on `*.visualstudio.com`, where the account is the host
  label — which is exactly what `AzureDevOpsUrlParser` builds. `DefaultCollection`
  is dropped rather than made part of the identity, because the host serves
  byte-identical content with and without it.
- A wildcard confined to the **query** changes the request text without changing
  what the host serves, on a host that ignores the query. `{"*":
  ".../{sha}/fixed.cs?ignored=*"}` gives every document its own URL — so the
  two-probe check, which compares request text, is satisfied — while every one of
  them fetches `fixed.cs`, and the reported origin is genuinely where `fixed.cs`
  is served from, so the agreement check is satisfied too. One file is then shown
  as the source of every document under a clean attribution. Measured against
  `raw.githubusercontent.com`: no query, `?ignored=A.cs`, `?ignored=B.cs` and
  `?path=/other.cs` all return the same 33400 bytes with the same SHA-256. This
  cannot be refused by the host-agnostic matcher, because the identical shape is
  the *generated* Azure Repos form where `path=` does select the file. It is
  refused by the host grammar instead: `raw.githubusercontent.com` URLs may not
  carry a query, which loses nothing, since that generator builds its URL by pure
  path concatenation and never appends one.
- A substitution can land in a component the host does not select on, which
  varies the request text while leaving the served file fixed. The Azure
  spelling is
  `{"*": ".../items?api-version=*&versionType=commit&version={sha}&path=/README.md"}`:
  every document gets its own URL, so the two-probe check passes; `path=` never
  moves, so every one fetches `README.md`; and the origin reported is genuinely
  where `README.md` is served from, so agreement passes too. Measured against
  `dev.azure.com/dnceng-public/public`, repository `dotnet-public-wiki`:
  `api-version` of `1.0`, `7.1`, `1.0-preview` and `5.0` all return the same
  content, SHA-256 `0129277c5fd5e35a…`. The allow list said each parameter was
  *understood*, never that each one *selects*. Provenance now requires the
  substituted text to land in the content-selecting component — the path for
  `raw.githubusercontent.com`, the `path` or `scopePath` value for Azure DevOps
  — which also refuses a substitution in the route, in the repository segment,
  and in `version`. One reviewer cleared `api-version` on the grounds that Azure
  answers a file-like value with 400; another defeated that by naming the PDB's
  documents `1.0` and `7.1`, which the threat model treats as attacker-chosen.
  `scopePath` is on the accept side because it was measured to select, not
  because the allow list already named it: `scopePath=/README.md` returns the
  same 985 bytes and SHA-256 as `path=/README.md`, while `scopePath=/` returns a
  different 425-byte response. Gated by
  `SourceLinkProvenanceTests.ASubstitutionThatSelectsNoContent_IsNotAttributable`.
- A query has one delimiter, not an arbitrary run of them. Trimming every
  leading `?` from
  `items??versionType=commit&version={sha}&path=/*` made this reader see
  `versionType=commit`; Azure sees the first parameter as `?versionType`,
  ignores it, and applies the default branch interpretation to `version`.
  A 40-hex branch is valid, so the URL could serve a branch while provenance
  and the cache identity named the same text as a commit. The reader now removes
  exactly one delimiter and refuses the unknown `?versionType` name for
  attribution. Resolution remains available because `path` still selects the
  requested document. Gated by
  `SourceLinkProvenanceTests.AnExtraQueryDelimiter_DoesNotTurnABranchIntoAnAttributedCommit`;
  the non-refusal boundary is pinned by the corresponding row in
  `SourceLinkMapConformanceTests.OnlyAnEntryThatCannotSelectContent_IsRefusedResolution`.
- The two content selectors are each allow-listed, and their *combination* was
  never considered. `path` names an item and `scopePath` a collection, and the
  host refuses to be asked for both rather than preferring one: measured,
  `scopePath=/&path=/*` and `path=/*&scopePath=/` both return 400, `Cannot
  specify an item "path" as well as "scopePath"`. The pair passes every other
  rule — both names are known, neither is repeated, and each carries its own
  wildcard, so the two-probe check sees two distinct request texts — while
  nothing states which selector governs. This is the repeated-parameter rule
  applied to two spellings of one role: were the host to start preferring one,
  every document would resolve through the selector that does *not* carry the
  wildcard and they would all fetch the same content while attributing cleanly.
  An allow list states that each entry is understood, not that any two of them
  compose. The rule is **ambiguity, not fetchability** — `api-version` is
  allow-listed and unvalidated, and `api-version=bogus` returns 400, which is
  fine: a request that fails serves no content, so nothing is misattributed and
  the failure stays visible.
- Reading the route positionally is not enough on `dev.azure.com`, where a
  leading `e` is the enterprise discovery prefix rather than an account.
  `/e/{org}/_apis/git/repositories/{repo}/items` satisfies the segment count
  exactly and reports the organization `e`. Measured: it returns 404 where the
  same request without the prefix returns 200, so the shape serves nothing for
  the reported origin to describe. `AzureDevOpsUrlParser` refuses it for the
  same reason, so no generated shape is lost by refusing it here.
- A literal `+` in a value decodes to a space under a form decoder and to a plus
  under a percent decoder, so `version=a%2Bb&versionDescriptor.version=a+b`
  presents two agreeing selectors to one reader and two disagreeing ones to
  another. The descriptor wins at the host, so we reported `a+b` while Azure
  served `a b`. A literal `+` is refused; `%2B` is unambiguous and stays accepted.
- Azure reads `version` against `versionType`, which defaults to `branch`, so a
  branch and a tag of one name are two different contents behind one spelling
  and one cache identity. Measured against a live repository: `main` as a branch
  returned 200 and as a tag 404. Only `versionType=commit` with a commit hash is
  attributable, which is exactly what `Microsoft.SourceLink.AzureRepos.Git` and
  `Microsoft.SourceLink.AzureDevOpsServer.Git` generate.
- `versionOptions=previousChange` and `firstParent` serve a different commit's
  content under an unchanged `version`, so the reported revision would not be the
  one fetched. Both are refused.
- The Azure path was matched at `/_apis/git/repositories/{repo}` without
  requiring the `items` endpoint, so endpoints that ignore `version` entirely
  were attributed to an attacker-chosen revision. The repository-metadata
  endpoint returned byte-identical content for every revision supplied. The path
  must now end at `items`.
- Query parameters are allow-listed rather than deny-listed. Azure's Items API
  takes several parameters that change which content is returned, and it grows
  while this reader does not, so an unrecognized name may select content the
  reported origin does not describe.
- Absence and emptiness are different readings. A parameter present with an
  empty value (`versionDescriptor.version=`) or present with no `=` at all was
  treated as absent, so the flat `version` was read as unopposed and its value
  reported — while a host that treats the descriptor as present-and-empty
  selects the default ref instead. Only a genuinely absent parameter counts as
  absent; a present one with nothing to say is refused on its own account.
- Whether a hex string is an object name is a property of the host's object
  format, not of the string. Accepting the 64-character SHA-256 length let
  `raw.githubusercontent.com/owner/repo/<64 hex>/*` report a commit, but GitHub
  stores SHA-1 repositories only and Git will create a branch of that name
  (`git branch` accepts one), so the value could only be a moving ref whose head
  moves under a fixed reported revision and a fixed cache identity. Both hosts
  this reader knows are SHA-1-only; the SHA-256 length needs the same evidence
  as a new host before it is admitted.
- An origin is `(scheme, host, port)`, but the reader identifies a host by name.
  `https://raw.githubusercontent.com:444/owner/repo/<sha>/*` was attributed to
  GitHub and given the same persistent cache identity as port 443, so a
  different service on that machine served content under GitHub's name and into
  GitHub's index. A port other than the scheme's default is refused; an explicit
  `:443` is the same origin and stays accepted. Neither generator emits a port
  for these hosts, so nothing generated is refused.
- The reported origin is itself artifact text, and one component of it is not
  escaped by the parser. `Uri.AbsolutePath` neutralizes a hostile path segment
  by leaving its percent-escape escaped, but `Uri.Host` does not: a raw `U+2066`
  in `a<U+2066>ccount.visualstudio.com` survives into `Uri.Host`, passes the
  `.visualstudio.com` suffix rule, and reached the rendered `RepositoryUrl` as a
  live bidi control — a Trojan Source code point aimed at the reader's terminal
  rather than at the fetch. `TryCheckOriginTextIsInert` now refuses any origin
  component carrying `Cc`, `Cf`, `Cs`, `Zl` or `Zp`. It runs from
  `TryEmitOrigin`, the single point at which an origin becomes visible to a
  caller, so the rule is a property of the value rather than of one code path:
  the first fix placed it in `Determine`, and the re-review found that
  `BrowseUrl` — a rendered product path, reached from
  `SourceLinkResolver.ConvertToGitHubBrowseUrl` and emitted as
  `GitHubBrowseUrl` — reads an origin without going through `Determine`. Gated
  by `SourceLinkProvenanceTests.ALiveFormatCharacterInAHostLabel_IsNotAttributable`
  and `…NoOriginIsEverProducedCarryingAScalarThatCanActOnASink`. The second
  asserts at the construction seam rather than over rendered text on purpose:
  `BrowseUrl`'s own output is inert for an unrelated reason — every hostile
  scalar in a path is percent-escaped by `Uri.AbsolutePath`, and its host must
  equal `raw.githubusercontent.com` exactly — so a test over what it prints
  would pass whether or not the check exists.

Two consequences are deliberate scope, not gaps, and are gated as decisions so
that changing them is visible:

- The host allow list is the set of hosts whose URL grammar this reader knows,
  not a trust boundary. SourceLink's generators also emit `*.vsts.me` and Azure
  DevOps Server URLs on arbitrary hosts and ports; both report no repository.
  Admitting such a host needs its own evidence — who operates the domain, where
  the virtual directory ends, and which port it answers on, none of which the
  URL states.
  Gated by
  `SourceLinkProvenanceTests.AHostWhoseUrlGrammarIsNotKnown_ReportsNoRepositoryRatherThanAGuess`.
- The encoded-separator refusal applies inside Azure's repository segment too.
  Two reviewers read this as over-refusal of a "repository folder", but Azure
  DevOps has no repository folders and forbids `/` in a repository name, so no
  such repository exists. The generator does pass the sequence through when the
  git remote contains it, so the map shape is real even though the repository it
  names cannot be. Accepting it would also undercut the rule that the path must
  end at `items`: that rule is decided by splitting the path, and `%2F` survives
  canonicalization, so our split and the server's need not agree on where the
  repository segment ends. Gated by
  `SourceLinkProvenanceTests.AnEncodedSeparatorInTheAzureRepositorySegment_IsNotAttributable`.

Attribution is decided from the URL's text, offline; the fetch that follows is
a separate step. That fetch must compare where it *landed* with what was
attributed.
`CreateUntrustedFetchClient` follows redirects (five hops, SSRF-guarded per hop)
and an HTTP client otherwise accepts any 2xx, so a syntactically valid but
nonexistent, private, or
unauthenticated Azure route redirects to a sign-in page on another host and
answers 203:

```text
final=https://spsprodeus27.vssps.visualstudio.com/_signin?realm=dev.azure.com&...
code=203
type=text/html; charset=utf-8
```

`SourceLinkProvenance.ValidateFetchOrigin` now compares the complete attributed
origin tuple from the requested URL with the tuple read from
`HttpResponseMessage.RequestMessage.RequestUri`. A final URL with no
attributable origin, or one naming another repository or revision, is rejected
before its body is read or cached. Browser/Wasm's HTTP transport does not expose
the final URL after an automatic redirect, so attributed SourceLink fetches fail
closed there; unattributed URLs remain fetchable because no repository is
reported for them, and checksum verification remains their
content-authenticity boundary.

Header-first source fetches keep the untrusted-fetch timeout active through the
body read, retry transient mid-body failures, and count decoded bytes against
the download limit even when `Content-Length` is absent or describes compressed
content. Source bodies are capped at 16 MB each, including decoded bytes.
Bounded fetches require Browser/Wasm's streaming-response mode so the browser
transport cannot buffer an unbounded body before that loop; a browser without
streaming support fails before body acquisition.

Source fetch progress and bounded-retry failure diagnostics are content-free:
they report the operation, status, and safe counts, not artifact-derived URLs,
paths, credentials, fragments, or transport exception text. This is gated by
`CommandExecutionTests.SourceEnrichment_VerboseProgressDoesNotDiscloseArtifactUrlOrPath`
and
`HttpRetryHelperTests.HeaderFirstBodyRead_FailureLogsCarryNoUrlOrExceptionText`.

Every product consumer that renders or derives output from fetched source now
uses `PdbSourceAcquisition.FetchVerifiedSourceTextAsync`. PDB Source,
printed Source Files and Source Locations, IL-offset source lines, and
documentation/sample enrichment all require the portable-PDB checksum before
using network content. `SourceAvailabilityService` and
`SourceIntegrityService` apply the same final-origin check before recording
reachability or reading bytes. The source-byte, availability, and integrity
cache categories were versioned when this rule landed, so entries created
without final-origin evidence cannot satisfy the new path.

Checksum evidence follows the portable-PDB document row rather than a display
or canonical path. Direct member, type, and IL-offset projections join on row
identity and verify the PDB document path; path-only heuristic projections
attach a checksum only when that path names one document row. This is gated by
`PdbSourceAcquisitionTests.SelectMappedDocument_UsesDocumentRowWhenPathsAreDuplicated`,
`...SelectMappedDocument_RejectsAMismatchedRowPathPair`, and
`MetadataSourceFindingsTests.DocumentChecksumIndexes_PreserveRowsAndRejectAmbiguousPathFallback`.

The fetch-origin grammar is gated by
`SourceLinkProvenanceTests.FetchOrigin_AttributedResponseMustPreserveTheCompleteOrigin`,
`...FetchOrigin_AzureSignInRedirectIsNotTheAttributedRepository`, and
`...FetchOrigin_UnknownSourceLinkHostCarriesNoOriginClaim`. The Services gate
exercises the response boundary, pre-fix cache invalidation, and the
availability/integrity projections in
`PdbSourceAcquisitionTests.FetchSourceBytes_RejectsRedirectOutsideAttributedOrigin`,
`...FetchSourceBytes_IgnoresPreOriginValidationCache`,
`HttpRetryHelperTests.HeaderFirstBodyRead_TimesOutAndRetriesAStalledBody`,
`...HeaderFirstBodyRead_CapsAChunkedBodyByDecodedBytes`,
`...HeaderFirstBodyRead_RetriesAMidBodyIoFailure`,
`...HeaderFirstBodyRead_RequiresBrowserStreamingResponse`,
`SourceLinkQueryServiceTests.Availability_DoesNotCountCrossOriginRedirectAsReachable`,
`...BrowserTransport_FailsClosedOnlyForAttributedSourceUrls`,
and
`...Integrity_DoesNotAcceptMatchingBytesFromCrossOriginRedirect`.

Gates. `SourceLinkProvenanceTests` covers all twenty-one as named tests, plus the
cache-identity distinction between forks and the requirement that every
unestablished result carry a reason. Where a refusal has more than one possible
cause, the test asserts the *reason* and not merely that the URL was refused: an
empty selector, for instance, is refused by every downstream rule as well, so a
test asserting only "not established" would pass with the rule it names deleted.
`SourceLinkProvenance.BrowseUrl` makes the same claim as the origin reader, in
the form a user is most likely to click, so it is held to the same rule and
gated by
`SourceLinkProvenanceTests.ABrowseLink_IsOnlyOfferedForAnAttributableGitHubOrigin`.
`SourceLinkProvenanceTests.OnlyTheProvenanceOwner_AndTwoNonAttributingReaders_NameTheGitHubRawHost`
and `SourceLinkMapConformanceTests.OnlyTheSourceLinkOwner_ReadsTheDocumentsMap`
pin the reader sets by set equality, so a second implementation of either rule
fails rather than quietly diverging.

### Artifact-derived source URLs use an SSRF-hardened client

SourceLink and other artifact-derived fetches must use
`HttpClientFactory.SharedUntrustedFetch`. It allows only HTTP(S), resolves every
connection including redirects, and rejects loopback, link-local, private,
CGNAT, multicast, unspecified, and reserved destinations. Callers must not
replace it with the general shared client.

Browser-Wasm cannot perform the DNS-level checks that
`SharedUntrustedFetch` performs. Its source host instead supplies an
`ISourceFetchPolicy` that authorizes a narrow set of HTTPS source hosts before
dispatch, omits credentials, and configures Fetch to reject redirects. A
destination outside that set is a PDB-source acquisition limitation and may
fall back to decompilation; it is never probed. The shared `SourceFetcher`
applies that host policy before its memory or content-store caches and before
creating the request. `PdbSourceAcquisitionTests.FetchSourceBytes_PolicyRejectsDestinationBeforeDispatch`
and
`BrowserEngineBoundaryTests.SourceFetchPolicy_OmitsCredentialsAndRefusesRedirects`
gate those rules.

Checksums from portable PDB documents authenticate source content when the
workflow claims PDB-source integrity. A reachable URL without a matching
checksum is not equivalent to verified source.

### Feed-discovered package resources use a guarded destination policy

An explicitly configured package-source host and port may resolve to private
addresses; that is the network location the user selected. A service-index
response does not inherit authority over the rest of the private network.
Desktop package-source transports therefore resolve and connect through the
same shared policy as untrusted source fetches. Every feed-advertised
cross-origin resource and redirect hop must resolve entirely to public
addresses, closing both direct private-target selection and DNS rebinding.
Bracketed and unbracketed IPv6 host spellings are canonicalized before the
configured-origin exception is applied.

Browser-Wasm cannot perform that connection-time DNS check. Its v3 client
therefore accepts only same-origin feed resources and sets Fetch
`redirect: error`; the built-in Gallery remains a separate fixed-host
transport. `PackageSourceClientTests.DefaultV3TransportBlocksPrivateCrossOriginSearchEndpoint`
and
`PackageSourceClientTests.DefaultV3TransportBlocksPrivateCrossOriginVersionAndPackageResources`
gate the desktop source-client wiring for search, version, and package
resources,
`HttpClientFactoryTests.PackageSourceClient_AllowsConfiguredPrivateOriginButBlocksPrivateRedirect`
gates redirect-hop enforcement,
`PackageSourceClientTests.DefaultV3TransportAllowsConfiguredPrivateIpv6Source`
and
`HttpClientFactoryTests.PackageSourceClient_AllowsConfiguredPrivateIpv6Origin`
gate the shared IPv6 origin normalization, and
`PackageSourceClientTests.BrowserV3ResourcesRequireSameOrigin` plus
`PackageSourceClientTests.BrowserNuGetRequestsOmitAmbientCredentials` gate the
Browser boundary.

### PDB-source lexing is complexity-bounded

The source byte limit is not by itself a memory bound. A punctuation-dense file
can produce nearly one retained lexical token per byte, and each token costs
more memory than its source spelling. A newline-dense file can likewise
materialize one retained line entry per byte before tokenization begins.
`CSharpLexer` therefore stops emission at 500,000 tokens, and
`DeclarationIndex` refuses more than 500,000 physical lines before splitting
the source. CR, LF, CRLF, NEL, line separator, and paragraph separator each
follow the same physical-line accounting.
`DeclarationIndex` carries the declaration's starting column so
`BodySlicer` consumes that bounded token stream once rather than tokenizing the
same untrusted file again.

Conditional branch projection remains within those bounds. Metadata partitions
visible sequence-point start lines by PDB document and sorts and deduplicates
each set. `BodySlicer` accepts only a positive, ordered PDB range within the
verified source and positive, strictly increasing point lines within that
range's physical file, uses binary range queries rather than a group-by-point
cross product, and refuses PDB correlation when a recognized `#line` directive
can remap the coordinates. CSharpText applies only caller-selected branch
objects produced by the same index; it blanks unselected half-open ranges with
one difference array and rebuilds over the line-preserving projection. Before
slicing the checksum-verified PDB-mapped text, the slicer refuses a selected
group that crosses exactly one boundary of the projected declaration. A group
wholly inside the declaration is removed from a second, boundary-only
projection; CSharpText must still vouch for the same declaration and slice
boundaries. Otherwise projected-away braces, terminators, or declarations
could expose unmatched directives or an unrelated dead-branch member. These
boundaries are gated by
`DeclarationIndexTests.ConditionalProjection_RejectsABranchFromAnotherIndex`,
`DeclarationIndexTests.ConditionalProjection_ManySelectionsAllocateLinearly`,
`ExtractMethodBodyTests.InvalidSequencePointCoordinates_FailVisibly`,
`ExtractMethodBodyTests.InvalidSequencePointRange_FailsVisibly`,
`ExtractMethodBodyTests.UnbalancedConditionalGroupInsideProjectedDeclaration_DoesNotLeakADeadSibling`,
`ExtractMethodBodyTests.TerminatorConditionalGroupInsideProjectedDeclaration_DoesNotLeakDeadSiblings`,
`ExtractMethodBodyTests.LineDirective_RefusesPhysicalLineCorrelationWhenPointEvidenceIsProvided`,
and
`AuthoredSourceValidityTests.RealPortablePdb_RefusesAConditionalGroupThatMakesTheOriginalSliceUnsafe`.
The binary-search complexity itself is unverified by a dedicated performance
gate.

Uncertain transparent scopes record row ranges during the lexical pass and
apply all overlapping ranges in one linear finalization pass. They do not
rescan and rewrite the declaration suffix once per scope.
`DeclarationIndexTests.ManyUnclosedExtensionScopes_ApplyTrustInOneFinalPass`
compares elapsed time with an equivalent row baseline and applies an allocation
budget to an accepted input near the token limit.
File-scoped namespace ends likewise reuse one suffix summary rather than
rescanning every later row;
`DeclarationIndexTests.ManyFileScopedNamespaces_ReuseOneSuffixSummary` gates
that work and allocation bound. Conditional initializer tails inspect each
pending token once. Conditional terminators revoke each direct sibling at most
once and memoize each completed outward ancestor walk across the scan; a later
child is still visited before its parent's completed walk stops the traversal.
Those bounds are gated respectively by
`DeclarationIndexTests.ConditionalInitializerTail_ExaminesEachPendingTokenOnce`,
`DeclarationIndexTests.ConditionalSiblingFanOut_RefusesEachSiblingOnce`, and
`DeclarationIndexTests.ConditionalNamespaceChainAndRepeatedTerminators_TraverseEachOutwardEdgeOnce`.
Carried interpolation state maintains the number of
line-bound frames as frames change rather than walking every frame after every
physical line;
`ScanTokenTests.DeepMultilineInterpolation_DoesNotMultiplyFrameDepthByPhysicalLines`
gates that depth-by-lines bound.

Limit exhaustion is a visible extraction failure, not an absent declaration.
`ScanTokenTests.TokenLimit_StopsTokenDenseInputDuringEmission` gates the
token emission boundary, while
`DeclarationIndexTests.LineLimit_StopsLineDenseInputBeforeSplitting` gates the
pre-allocation line boundary, and
`PdbSourceAcquisitionTests.FromContent_TokenDenseSourceProducesVisibleFailedEvidence`
gates the Findings-facing result, while
`CommandExecutionTests.PdbSource_TokenDenseInputCarriesAVisibleFailureState`
gates the member-command result.
`DeclarationIndexTests.TheBodySlicerCannotAccessLexerInternals` gates the
one-pass ownership boundary.

## Resource extraction contract

Manifest resource names are attacker-controlled metadata. They are not safe
paths merely because a compiler normally emits dotted logical names.

Resource extraction therefore follows this contract:

- Preserve nested resource paths only after validating every component.
- Treat both `/` and `\` as separators on every platform.
- Reject rooted and drive-qualified names, empty components, `.` and `..`,
  control characters, a fixed portable set of invalid filename characters,
  alternate data stream syntax, trailing dot/space aliases, and Windows device
  names.
- Normalize and case-fold destination identities so extraction has one
  deterministic collision policy across operating systems and filesystems.
- Preflight all resource data ranges and destination paths before creating the
  output directory or writing any resource.
- Reject malformed resource payloads, duplicate destinations, and
  file/directory prefix conflicts.
- Reject existing destination files; extraction never overwrites.
- Reject existing symbolic-link or reparse-point components beneath the output
  root.
- Open destination files with create-new semantics so a concurrent file
  creation cannot become an overwrite.
- Surface the failure to the caller. Do not silently skip an unsafe name.

The caller-selected output directory is the trust anchor. Portable .NET APIs do
not currently provide an atomic cross-platform "open beneath this directory,
never follow links" primitive. Reparse checks and create-new writes narrow the
race window, but a local adversary able to mutate the chosen directory
concurrently remains a residual risk. Security-sensitive automation should use
a fresh directory with permissions restricted to the invoking user.

That residual risk is **tier 3**: it requires an attacker who already has write
access to a directory the user chose. It is worth the checks already listed
here, and it is not worth trading against correctness on ordinary inputs.

## Required rules for new code

These are requirements for new or changed paths and audit targets for existing
code. They do not claim that every legacy scanner already distinguishes
malformed input from an ordinary empty or zero-valued result.

### Derived paths

Validate before side effects. Prefer rejection over sanitization: sanitization
can create collisions and hides the artifact's original identity.

For each artifact-derived path:

1. Define the trusted root.
2. Parse the untrusted value into components.
3. Reject rooted, traversing, empty, control, device, and platform-alias forms.
4. Resolve the full destination and prove it remains beneath the root.
5. Preflight collisions across the full operation.
6. Refuse unintended overwrite.
7. Keep failures visible, and attributable to the input the **user** supplied.
   Do not quote the rejected artifact value.

Do not use `Path.Combine(root, untrustedValue)` as a containment check.

### Parsing and resource consumption

Use existing guarded signature, metadata, IL, and recursion limits. A new
decoder must identify:

- maximum input size;
- maximum nesting or graph depth;
- maximum produced rows or objects;
- cancellation and timeout behavior;
- malformed-input failure shape.

Do not catch `Exception` and return an ordinary empty result for malformed
input. Empty means the producer completed and found no evidence; failure is a
different state.

Metadata relationship and name traversal follows the
[bounded metadata traversal](bounded-metadata-traversal.md) contract. Cycles,
depth, count expansion, and projected text are separate budget dimensions;
exceeding any one produces a visible rejection rather than a partial identity.

### Network and caches

Network capability policy is enforced in the shared HTTP handler after the
attempt is recorded for diagnostics and before it reaches the transport.
Traffic families that require explicit authorization, currently vulnerability
data, must run inside their matching `NetworkTelemetry.Allow` scope. Offline
mode remains the broader prohibition over every traffic family.

Network access derived from inspected content must be explicit in the command
surface, use the untrusted-fetch client, have a timeout, and retain provenance.
Cache paths must be hashed or use validated single components. Downloads should
land in temporary files and become visible atomically after validation.

A cache entry created before a content-validation gate existed is not evidence
that the gate passed. Persistent cache cutovers follow the
[`CoreCache` contract](../inspection-space.md#corecache): either revalidate on
every hit or select a successor contract version before lookup, and pair the
newly rejected case with a still-valid recomputation case. Dynamic network,
capability, and liveness policy is always rechecked and cannot be replaced by a
version bump. The cache key, validation, and derived result must also consume
the owner-retained immutable snapshot for every contributing artifact; equal
pre/post hashes around work over a reopened mutable path do not exclude a
W-to-S-to-W substitution. `MDP017` gates that ABA case for both assembly and
PDB inputs to the library effective catalog. At that cutover, bounded
assembly-format admission also precedes every SourceLink/PDB probe and catalog
lookup; only a supported assembly may reach the separately bounded
identity-validated portable-PDB reader. The successor key includes complete
typed local-symbol evidence rather than the predecessor's Boolean-only
SourceLink token, and typed root-route evidence for route-dependent catalog
semantics. That evidence is frozen before lookup and shared by all cold
producers and publication; post-production evidence cannot re-key an existing
result. An observed evidence-generation change declines publication and belongs
to a later recomputation. Bare effective discovery reserves at most 64 MiB of
portable-PDB content across adjacent, cached, acquired, or decompressed embedded
providers before copying, hashing, or reader construction. An over-limit PDB
fails visibly as `PortablePdbRetentionLimitExceeded`; it is not ignored as
absent or retried through another provider.

### Presentation

Artifact text can contain Markdown delimiters, newlines, terminal control
characters, URLs, or prompt-like instructions. Renderers must preserve output
structure and must not interpret inspected text as authority.

> **Status.** Both axes are built in `mdi`, which is the reference consumer:
> the default refuses, `--show-untrusted-text` opts out of the trust axis, and
> `--dangerously-print-raw` additionally opts out of the rendering axis. The
> rendering axis rests on `InertText.InertString` (#3636), extended to Unicode
> general categories in #3628.
>
> Two things remain unbuilt. **Survey mode** is not implemented: refusal stops
> at the first violation rather than reporting every one, though
> `InertString.IsPermitted` already returns a `ScalarViolation` shaped for it.
> And **`dotnet-inspect` has neither flag**; the library default is to encode
> and continue, so its behavior is the middle row of both tables. That default
> is deliberate — containment is a safety property the library owes every
> caller, while refusing is a policy only a caller can choose — but it means the
> trust axis currently exists only where a command line can express it.

The package inspection path now has the enabling boundary and bounded audit
detail, but not the refusal policy:
`PackageInspectionText` carries every package-model text field to Markdown,
direct JSON, and focused package table/JSONL metadata as `InertString`;
content-output rows do the same for their package, version, and path framing.
`InspectionResultView.RequiredContainment` reports the inspection model's
aggregate before a sink unwraps it, and package `Signals` reports whether that
aggregate is empty plus its `TextConcern` category kinds. The explicit
`Audit: Artifact Text` section lists package-model field locations and concern
kinds, but never the field values. `Audit: Findings` explicitly scans
text-bearing files and SourceLink mappings, reporting bounded, visually encoded
evidence plus NuGet restore-source semantics. It also reports every literal
`../` in a decoded SourceLink document key or URL as a review-oriented parent
path finding. This does not classify the mapping as malicious; the existing
provenance boundary above remains responsible for canonicalizing resolved URLs
before attribution. Candidate paths, text files, SourceLink carriers, aggregate embedded-PDB
inflation, PE debug-directory entries, CodeView record bytes, SourceLink map
bytes, and mapping inventory are bounded. The PDB owner rejects oversized
debug directories and CodeView records before SRM materializes authored paths,
reads the same embedded-PDB file pointer as the framework decoder, and reserves
both per-file and shared expansion budgets before decompression; the named
gates are
`PdbContextDescriptorTests.DebugDirectoryAndCodeViewLimits_PrecedePathMaterialization`,
`PdbContextDescriptorTests.EmbeddedPdbAndSourceLinkLimits_PrecedePayloadMaterialization`,
`PdbContextDescriptorTests.EmbeddedPdbLimit_ReadsTheFilePointerUsedByTheDecoder`,
`PdbContextDescriptorTests.EmbeddedPdbLimit_AppliesDataPointerRelativeToPeImageStart`,
`PdbContextDescriptorTests.EmbeddedPdbExpansionBudget_IsSharedAcrossOpens`,
`PdbContextDescriptorTests.MalformedEmbeddedPdb_ConsumesExpansionBudgetBeforeDecode`,
`PackageContentAuditTests.CandidatePathLimit_BoundsRepeatedInputBeforeMaterialization`,
`PackageContentAuditTests.TextFileLimit_BoundsZeroByteReads`,
`PackageContentAuditTests.SourceLinkCarrierLimit_BoundsZeroByteWork`,
`PackageContentAuditTests.OversizedCodeViewRecord_MarksAuditPartialBeforeDecode`,
and
`SourceLinkMapConformanceTests.MappingLimit_StopsBeforeRetainingAnOverBudgetInventory`.
Neither audit section is the scalar-by-scalar refusal
survey mode described below, and neither changes acceptance policy. Document payloads are
encoded on stdout; exact bytes require `--out` with a single-file selection.
`PackageSignals_ReportsEveryArtifactTextConcernKindWithoutContent`
and `Package_MultiplePackages_SignalsIncludePackageFileConcerns` gate the
summary across single-package and survey modes;
`PackageArtifactTextAudit_ListsLocationsAndKindsInMarkdownAndJsonl` gates the
model-detail boundary; `PackageContentAuditTests` and
`PackageAudit_RendersContentAndSourceLinkFindings` gate the file and
PDB scan and its detail shape. This is intentionally not a global CLI signal.
Other commands and projections still have their own presentation models, so
adopting the flags at the root today would claim coverage they do not have.

Identifier confusion is a separate semantic risk from rendering. Package IDs
and assembly names containing non-ASCII characters remain safe to carry as
graphic text, so `TextConcern` correctly stays empty. Package Signals and
library Signals over the selected assembly plus direct references nevertheless
report those identifiers for review, and a bounded exact homoglyph fold
confirms Greek/Cyrillic lookalikes in the ecosystem prefixes `System`,
`Microsoft`, and `Azure`. The explicit library
`Audit: Identifier Confusion` section additionally resolves the transitive
reference closure; the unbounded traversal requires that explicit gesture. The
section exposes model locations, classifications, matched prefixes, similarity,
and code points without echoing identifier content.
`IdentifierConfusionDetectorTests` gates the detector boundary, including
monotone classification when several confirmed homoglyphs compose one reserved
prefix,
`DescribeCharacters_DeduplicatesRepeatedHomoglyphCodePoints` gates stable
code-point rendering when one substitution occurs more than once,
`LibraryIdentifierConfusionAudit_CollectsDirectAndTransitiveReferenceNames`
gates the direct library producer demand,
`PackageAllLibrariesIdentifierConfusionAudit_CollectsTransitiveReferences`
gates survey-mode demand,
`LibraryIdentifierConfusionAudit_FullEffectiveDiscoveryIncludesTransitiveOnlyConcern`
gates full-effective discovery,
`LibrarySignals_FullEffectiveDiscoveryPropagatesReferenceFailure`
gates nonzero failure propagation through Signals effective discovery,
`LibraryIdentifierConfusionAudit_DoesNotRepeatDirectReferenceFromClosure`
gates direct/closure identity deduplication,
`LibraryAudit_PreservesCaseDistinctResolvedNames` gates case-distinct
direct/closure suppression, while
`LibraryIdentifierConfusionAudit_PreservesCaseDistinctUnresolvedReferences`
gates preservation of those spellings through traversal,
`LibraryIdentifierConfusionAudit_DeduplicatesDiamondClosure` gates one
projection row per resolved identity when several reference paths converge,
`AssemblyReferenceTreeResolutionTests.DistinctSameNameReferences_DoNotSuppressResolvableIdentity`
gates distinct typed AssemblyRefs that share a simple name,
`LibraryIdentifierConfusionAudit_FailsWhenResolvedReferenceCannotBeRead`
gates visible traversal failure for absolute and bare relative library paths,
including preservation of the other selected `@Audit` sections,
`LibraryPackageIdentifierConfusionAudit_FailsWithoutPartialDocument` gates the
same content-free hard failure for an exact package-backed library selection,
`PackageAllLibrariesIdentifierConfusionAudit_PreservesHealthyResultsOnTraversalFailure`
gates clean diagnostics, healthy partial results, and nonzero completion for
survey-mode traversal failure,
`LibraryCommand_TfmAll_PreservesHealthyIdentifierAuditResults` gates the same
per-source outcome contract across target frameworks, and
`LibraryIdentifierConfusionAudit_FailsWhenDirectReferencesCannotBeDecoded`
plus
`PackageAllLibrariesIdentifierConfusionAudit_FailsWhenDirectReferencesCannotBeDecoded`
gate visible root AssemblyRef decode failure without a success-shaped clean
result. Traversal diagnostics retain caller-known command context, while survey
warnings identify the package-relative library and a bounded failure category;
neither repeats the AssemblyRef value, product-owned extraction path, or inner
exception message.
`LibraryReferenceTree_ReadFailureDiagnosticIsContentFree` gates that same
diagnostic contract on the public reference-tree projection.
`PackagePipeline_IdentifierConfusionAudit_DemandsRegistrationMetadata` plus
`InspectAsync_IdentifierAuditMetadataIncludesAlternatePackageId` gate the
alternate-package metadata demand, producer result, and moderated network
cost. `InspectAsync_IdentifierAuditMetadataFailureRemainsVisible` gates an
`Unavailable` result when registry acquisition cannot establish
alternate-package metadata; a failed acquisition is not interpreted as an
absent alternate ID.
`FetchAllMetadataAsync_FlatContainerOnlyCompletesOptionalMetadata` and
`PackageCommand_FlatContainerOnlyPreservesLocalIdentifierDetection` gate the
complement: absence of optional deprecation resources in a valid service index
is not an acquisition failure.
`FetchAllMetadataAsync_SearchDeprecationMustMatchRequestedVersion` gates
version-specific authority for search deprecation metadata;
`FetchAllMetadataAsync_DoesNotCacheMismatchedSearchVersion` gates retry after
that mismatch, while
`FetchAllMetadataAsync_CachesMatchingSearchVersionWithoutDeprecation` and
`FetchAllMetadataAsync_CachesCatalogAuthorityDespiteSearchVersionMismatch`
gate authoritative absence and catalog precedence;
`FetchAllMetadataAsync_DoesNotCacheMismatchedInlineCatalogIdentity` and
`FetchAllMetadataAsync_DoesNotCacheMismatchedFetchedCatalogIdentity` gate the
same identity and retry contract for both catalog forms; and
`FetchAllMetadataAsync_IgnoresMalformedCatalogReference` gates retry after a
malformed catalog reference.
`PackageCommand_IdentifierMetadataFailureIsNonzero` gates nonzero completion
and content-free diagnostics for that failure.
`MultiPackageCount_CountsSelectedAuditRows` gates scalar counts against the
selected audit rows rather than unrelated package-info fields;
`MultiPackageCount_PreservesSelectedSectionMap` plus
`MultiPackageCount_PreservesFixedOverviewMap` gate multi-section count maps;
`Package_MultiplePackages_FixedOverviewCountPopulatesSections` gates the
command path that supplies those fixed-overview sections;
`LibraryPackageSignals_FullEffectiveDiscoveryWarnsOnce` gates one diagnostic
per package effective-discovery failure;
`LibraryCommand_SelectedReferences_TreeDedupUsesShallowestPath` gates
minimum-depth canonicalization under a bounded reference traversal; and
`PackageIdentifierConfusionAudit_ListsClassificationWithoutIdentifierContent`
gates content-free Markdown and structured output.

Presentation is **two orthogonal decisions**, and collapsing them into one flag
is a design error.

**Trust** — what happens when a concerning pattern is found:

| Flag | Behavior |
| --- | --- |
| *(default)* | abort at the first one |
| survey mode | keep going; report location and pattern kind, never content, bounded by the traversal budget |
| `--show-untrusted-text` | keep going and render the values anyway |

The trust skip is **not** `dangerously`-named, which is a correction to an
earlier draft of this section rather than a departure from it. The argument
below is that the skip is defensible precisely because visual encoding still
applies underneath it — it means "do not refuse," not "it is fine to put my
terminal at risk." A name that called it dangerous would contradict that, and
would spend the word on the safe path, leaving nothing louder for the flag that
genuinely hands over live control characters.

**Rendering** — how artifact text is spelled once something is printed:

| Flag | Behavior |
| --- | --- |
| *(default)* | visually encoded into an inert form |
| `--dangerously-print-raw` | no visual encoding; the output format's own structural escaping still applies |

That last clause is load-bearing and measured: the format keeps itself well
formed regardless of the flag. JSONL escapes scalars below U+0020 per RFC 8259,
which is not containment — those escapes decode back to the original scalar —
and TSV replaces the line and paragraph separators, which it cannot carry in a
record. Markdown carries everything. So the raw mode promises that `mdi` adds
no encoding of its own, not that every scalar reaches the stream.

**Raw output is produced by the decoder, at the sink.** Since #3687 the
projection cannot hold untreated text at all: its text-bearing fields are
`InertString`, and no conversion admits a `string` into one. That closes off the
obvious implementation — keeping a second, raw copy of every value beside the
contained one — and forces the honest one, which is to run the encoding
backwards at the moment of printing. This is the `vis`/`unvis` pairing named
below rather than a workaround for it: the encoding is lossless and invertible
precisely so that a decoder can exist, and having the decoder is what makes raw
output a *rendering* choice instead of a property of the model. A literal
backslash is rewritten when it could introduce an encoded spelling; lone and
unrelated backslashes remain literal because the decoder can recognize that
they introduce nothing. Refusal is unaffected and still happens upstream,
against the raw text, because the question it asks — does this artifact carry
something concerning — is about the artifact rather than about the spelling.

Placing the decode after the character budget also buys a property the earlier
implementation lacked: **both modes cut the same value at the same point.**
Bounding raw text separately, in its own units, made the rendering axis quietly
a *content* axis too, so that asking for a different spelling changed how much
of the value you saw — a subtler form of exactly the collapse this section
warns about. `MdiUntrustedTextModeTests.RawAndEncodedRenderingsShowTheSamePrefix`
gates it by re-encoding the raw cell and comparing it to the encoded one,
running the encoder forward so the assertion is not a restatement of the
decode. On an artifact carrying nothing that needs containment the three
tiers are byte-identical; measured over a 2,031,325-byte assembly, all three
agree exactly.

The axes are independent, and that is the design. Visual encoding is the
default on **every** artifact-text path, including underneath the trust-axis
skip — which is precisely what makes that skip defensible: it means "do not
refuse," not "it is fine to put my terminal at risk." Reaching a live control
character therefore requires opting out of both axes, two separately named
mistakes. `mdi` enforces exactly that: `--dangerously-print-raw` on its own is
rejected rather than silently ignored, because refusal comes first and the flag
would otherwise change nothing while appearing to.

Rendering is **visual encoding, not neutralization**: control characters are
re-spelled into an inert, lossless, invertible form rather than removed or
replaced. The vocabulary is borrowed rather than coined — see below — and the
three properties together are what let the encoding be the default: it costs
the reader nothing, so there is no case for making it opt-in, and a default
cannot be forgotten by a new path. Nothing passes a flag to make a JSON
serializer escape `\u001B`.

This is established practice for tools that read hostile bytes:

- **BSD `vis(3)`** is where the vocabulary and the contract come from. It
  visually encodes arbitrary input into graphic characters only, and pairs the
  encoder with a decoder (`unvis`) so the transform is unique and invertible.
  Encode-without-a-decoder is not this pattern.
- **Caret notation** — `^[` for `ESC`, `^?` for `DEL` — is the standard
  spelling for C0 and `DEL`, used by `cat -v`, `less`, and `stty`, and dating
  to the PDP-6 up-arrow that the 1967 ASCII revision replaced with `^`. Specify
  it; do not invent a spelling.
- **`grep`** refuses binary content by default and prints `Binary file X
  matches` — location, never content — with `-a`/`--text` as the named opt-in,
  for exactly this threat.
- **`less`** renders control characters in caret notation by default and
  reserves raw output for `-r`.
- **`rustc`** made bidirectional control characters a deny-by-default hard
  error after Trojan Source (CVE-2021-42574) rather than stripping them. Its
  denied set is nine code points — the embeddings and overrides
  `U+202A`–`U+202E` and the isolates `U+2066`–`U+2069`. Unicode's
  `Bidi_Control` property adds the three marks `U+200E`, `U+200F`, and `U+061C`
  for twelve; do not attribute those three to `rustc`. Every one of the twelve
  is `Cf`, and none is anywhere near C1, so a rule written as "control
  characters" excludes all of them — which is why the encoded set is defined by
  Unicode property rather than by a hand-written list.
- **`binutils`** is the cautionary case rather than a model: its parsers are
  continuously fuzzed, and fuzzing has repeatedly found parser defects that
  received CVEs.

Do not copy `less`'s one mistake: its protection is conditional on stdout being
a TTY, and it degrades to `cat` when output is redirected — `-r` makes no
difference there, because nothing is being encoded in the first place. A pipe
is precisely where an agent or a log is.

JSON serializers provide structural escaping. Markdown, table, plain-text, and
stderr paths need equivalent discipline, and stderr especially — see
[failure messages carry no artifact data](#failure-messages-carry-no-artifact-data).

[metadata-table-projection.md](metadata-table-projection.md#safety) works this
through on one surface and specifies a concrete encoding.

One artifact-derived string on the SourceLink path is rendered today: the
reported `RepositoryUrl`, which `AssemblyInspector` writes to
`audit.RepositoryUrl`. It is built from segments of a URL that came out of a
downloaded package's PDB, so a hostile map can aim `ESC`, `CR`/`LF`, or a bidi
override at it.

The **path** components are inert incidentally. `Uri.AbsolutePath` leaves a
percent-escape escaped, so `%1b` stays the three characters `%`, `1`, `b`, and a
raw `U+202E` comes back as `%E2%80%AE`; measured,
`https://github.com/ow%1bner/repo` and `https://github.com/ow%E2%80%AEner/repo`
are what get reported.

The **host** is not, and assuming otherwise was a real bypass (round 17,
twenty-first entry). `Uri.Host` does not escape the way `Uri.AbsolutePath` does:
a raw `U+2066` in an `account.visualstudio.com` label survives into `Uri.Host`
unchanged, passes the `.visualstudio.com` suffix rule, and reached the reported
URL as a live bidi control — one of the code points `rustc` made a hard error
after Trojan Source. The first gate written for this missed it because every row
it had was a *percent-encoded* escape, and those really are neutralized by the
path reader; nobody had written down the raw form.

So the rule is no longer "the readers happen to escape". `TryCheckOriginTextIsInert`
refuses an origin any of whose components carries a scalar in `Cc`, `Cf`, `Cs`,
`Zl` or `Zp` — by category, not by a list, because `Cf` is what a list misses.
It runs from `TryEmitOrigin`, the one place an origin becomes visible to a
caller, and not from `Determine`: `Determine` is where the round-17 fix put it,
and the re-review pointed out that `BrowseUrl` reads an origin without going
through `Determine` and is rendered as `GitHubBrowseUrl`. A rule enforced by one
consumer is a rule the next consumer does not inherit — the same shape of defect
as the one it was written to close. Refusal rather than encoding
follows the strategy above: no legitimate repository needs a bidi control in its
name. The rejection names the component and the code point and never the value,
so the diagnostic channel does not carry the hazard it is reporting.

Refusal by category was checked against legitimate input rather than assumed
safe: repository names in Japanese, Chinese, Korean, Cyrillic, Greek, Arabic,
Hebrew, Devanagari, Thai and Vietnamese, plus an emoji, a combining sequence and
its precomposed form, all still attribute.

The gates: `ALiveFormatCharacterInAHostLabel_IsNotAttributable` pins the refusal,
`NoOriginIsEverProducedCarryingAScalarThatCanActOnASink` pins the invariant at the
construction seam so it covers `BrowseUrl` and the cache identity as well as the
reported URL, `AnEstablishedRepositoryUrl_CarriesNoScalarThatCanActOnASink` pins
that anything still reported is inert, and
`TheHostileOriginRows_MostlyEstablish_SoTheScalarGateIsNotVacuous` pins that its
rows still establish, because a gate whose every row is refused asserts nothing.
Disabling the check fails nine of them.

`SourceLinkProvenanceResult.Reason` is the *latent* half of the same exposure.
Its messages quote artifact text throughout — the query, the path, the host, a
revision, a rejected map key — and today no caller renders it: current callers read
`Origin?.RepositoryUrl` and drop the reason. The library map-diagnostics path
does not render that composite provenance reason: it projects map errors and
rejected keys separately, and its view records apply visual containment. Any
future surface for the provenance reason must do the same rather than merely
printing the string.

A test framework is a sink too. xUnit builds its row labels from the theory
arguments, so the runner prints a raw `U+202E` from a hostile fixture to the
same terminal — assertion *messages* under our control name the code point
instead (`U+202E (Format) at 2`), and fixtures should assume the label is not
under our control.

## Verification obligations

Security-sensitive parsers and writers require close negative fixtures, not
only ordinary compiler output.

| Surface | Required evidence |
| --- | --- |
| Resource extraction | Traversal and rooted names rejected before writes; valid nested and empty resources retained; malformed ranges rejected; separator/case aliases collide; existing file preserved; device/control names rejected |
| Archive extraction | Zip-slip fixture; Browser-Wasm declared/observed expanded-size rejection; bounded symbol-response, central-directory entry-count, expanded-PDB, and retained-store rejection; product-wide default expanded-size and entry-count policy tests once those budgets exist |
| Metadata and signatures | Malformed table/blob fixtures, depth/size limits, no process crash |
| SourceLink | Private/loopback targets rejected per hop; attributed redirects must preserve the complete repository/revision origin; rendered network source requires the portable-PDB checksum; pre-origin-validation caches are ignored; allowed public targets and checksum paths retained; a duplicate `documents` key fails the parse rather than binding one of its values; the mapping rule is pinned against the specification's worked example, and the set of product files reading the map is pinned by set equality |
| Untrusted JSON | Duplicate properties rejected at top level, nested, and from UTF-8 bytes; case-distinct and sibling-repeated names still parse |
| Cache paths | Traversal/separator components rejected; content-addressed keys deterministic |
| Structured output | Untrusted non-graphic scalars cannot escape the selected format. `MdiContainmentTests` splices a payload reaching past any single predicate's notion of "control" (a live `ESC [ 3 1 m` sequence, `BEL`, `DEL`, a C1 control, the bidi override `U+202E`, the line separator `U+2028`, the zero-width space `U+200B`, and the supplementary tag character `U+E0074`) into both a real `#Strings` entry and the metadata version stamp, then renders that assembly in every format through the three views that carry artifact text — table, heap, and overview — asserting no raw non-graphic scalar survives and every contained form is present. The `--references` view carries no artifact text, so it is asserted only against raw scalars, as a regression net. Mutation-checked by restoring the pre-#3628 range predicate (dies naming `U+202E`) and by a category-correct but `char`-based predicate (dies naming `U+E0074`). Until #3628 this row named a payload that was `Cc` only, so a bidi override would not have been noticed; the payload and the assertion helper had both been scoped to the projector's own predicate, which is why the gate stayed green while `U+202E` reached the terminal. Both now classify by Unicode general category over scalars. Two limits remain: the assertion deliberately permits raw `CR`/`LF`/`TAB`, and format *delimiters* are not covered by this gate at all |

## Open work

1. Extend duplicate-property rejection to the readers that still bypass
   `HardenedJson`: the two `JsonDocument.Parse` call sites in
   `PackageExtractor` registration-page reading, `NuGetFetch.NuGetApi`'s
   source-generated feed contexts, and `runfaster` trace parsing. Add a gate
   asserting no product JSON entry point parses outside the guard, so the set
   cannot silently regrow.
2. Define product-wide package, symbol, source-download, and
   decompressed-archive byte and entry-count budgets. The Browser-Wasm package
   host now has byte and entry-count limits, but that host-specific policy does not settle the
   extraction, symbol, or entry-count contracts for other consumers.
3. Audit every product write against the derived-path rules, including symbol
   server cache path construction.
4. Continue auditing Markdown, plain-text, and stderr rendering for terminal
   control characters and structure injection. Nuspec descriptions now cross
   both object models as `InertString` and render as Markdown quotations;
   `NuspecHardeningTests.HostileDescription_RemainsQuotedInMarkdownAndContainedInJson`
   gates the Markdown and JSON sinks. Other prose-bearing paths remain in scope.
5. Implement the [bounded metadata traversal](bounded-metadata-traversal.md)
   migration and expand malformed PE/PDB product-entry-point coverage around
   graph depth, row count, and allocation limits.
6. Migrate legacy metadata scanners that collapse malformed reads into empty or
   zero-valued results onto explicit failure-bearing outcomes.
7. Revisit filesystem containment if .NET exposes a portable atomic
   no-follow/open-beneath primitive. **Tier 3.**
8. Complete the trust-axis adoption beyond `mdi`. The metadata projection now
   carries `InertString`, applies the Unicode general-category rule, and exposes
   refusal, contained rendering, and a separately named raw mode. What remains
   is survey mode and command-line policy for `dotnet-inspect`, whose library
   paths currently contain and continue. Keep identity allow-list rejection
   separate: package IDs and versions are not repaired into acceptable
   identities.
9. Audit failure messages for artifact data. `NuGetCache.ValidatePathComponent`
   throws `Invalid {name}: '{value}'`, echoing the value it just rejected.
   Printability here is a function of **provenance, not content**: the same
   helper receives user-typed coordinates and artifact-derived ones, so it
   cannot be decided by inspecting the value. Three graph-resolved paths reach
   it, all verified:

   - `ProjectCommand` → `ProjectAssetsParser` package references →
     `PackageExtractor.ExtractPackageAsync`.
   - `NuspecParser` → `PackageDependency.Id`/`.Version` →
     `DependencyResolutionService.ResolveDependencyTreeAsync` →
     `PackageExtractor.TryGetNuspecXmlAsync` → `NuGetCache.TryGetCachedPackage`.
   - A package-authored `DotnetToolSettings.xml` `Id` becoming the current
     package source, then reaching acquisition and cache validation.

   The second path leaks twice and reaches a package the user never named.
   `DependencyResolutionService` logs `dep.Id`/`dep.Version` before any
   validation, then catches `Exception` and logs `ex.Message`, re-emitting the
   rejected value; that same handler returns an empty result, which is the
   success-shaped failure this document forbids elsewhere.
   `ValidatePathComponent` does not reject control characters other than
   `NUL`, so an `ESC` passes it outright. The nuspec projection boundary now
   rejects malformed XML, unsupported structure, identity mismatch, dependency
   contract violations, and query-owned resource limits with typed,
   content-free reasons, and carries descriptions as `InertString`.
   `PackageManifestFactsQueryTests.FailureMessage_IsStableForEveryReason`,
   `FailureMessage_IsSafeForUnknownFutureReason`, and the hostile-input
   execution tests gate that diagnostic contract. Package-coordinate validation
   and the two graph-resolution leaks remain the next application of the
   hardened-entrypoint pattern.
10. Establish fuzzing over the PE, metadata, PDB, nuspec, and archive entry
    points. The domain-matched precedent is `binutils`, whose parsers are
    continuously fuzzed and have repeatedly yielded CVEs that way. Most of
    those are memory-safety defects that C# denies us, so the realistic harm
    set here is smaller and enumerable — hang or unbounded allocation,
    plausible-but-wrong output, and output-channel injection — but nothing
    currently searches for any of the three. This is the one open item that
    pays into tiers 1 and 2 at the same time.
