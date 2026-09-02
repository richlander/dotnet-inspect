# Package index cache

## Status

Target design for the `PackageIndexCache` slice of
[#3738](https://github.com/richlander/dotnet-inspect/issues/3738).
The current `pkg-index-v16` implementation supplies format fencing, source
producer separation, inert-description persistence, malformed-entry rejection,
and request-time RID reverification. It does not yet carry the package-owned
authority and retained-content identity required by this contract.
[Issue #5484](https://github.com/richlander/dotnet-inspect/issues/5484) tracks
the acquisition-owned durable content prerequisite. The complete contract is
therefore **unverified**.

## Decision

`PackageIndexCache` owns one decision: whether a persistent
filesystem-derived package inspection result may replace cold inspection of one
exact authorized package payload.

A hit is valid only for the same package-owned durable authority, normalized
package coordinate, exact retained package content, and package-inspection
projection contract that produced the entry. A hit supplies stable
content-derived facts only. It carries no claim about current authorization,
availability, network, metadata enrichment, or wrapper routing.

This is stricter than treating package ID and version as immutable identity.
nuget.org prevents replacement of one published coordinate, but custom and
local sources need not make that promise, and equal coordinates from different
authorities may contain different bytes. It is also stricter than
content-addressing alone: equal bytes do not grant a caller authority to use an
entry derived under another configured package authority.

## Owner and boundaries

The owner is `PackageIndexCache`, currently in
`src/dotnet-inspect/Inspectors/PackageIndexCache.cs`.

It consumes:

- a package-owned durable configured-authority cache key, when one exists;
- an exact normalized package ID and version;
- the process-local `PackageContentGenerationIdentity` for the retained content
  that cold inspection consumes;
- an acquisition-owned durable package-content identity covering the retained
  archive and its admitted matching product-owned inspection tree;
- the complete filesystem-derived package projection produced from that
  content; and
- `CoreCache` storage under one versioned package-index contract.

It returns either:

- a complete filesystem-derived package projection bound to the requested
  cache subject; or
- a cache miss.

It does not own:

- configured package authority, source mapping, producer identity, or payload
  authorization;
- package download, extraction, admission, retention, or digest construction;
- `CoreCache` paths, hashing, atomic file replacement, maintenance, or
  telemetry;
- NuGet metadata enrichment, RID companion availability, symbol acquisition,
  tool-wrapper routing, or other request-current observations;
- package inspection algorithms or the meaning of individual
  `InspectionResult` fields; or
- package command selection, disclosure, rendering, or output containment.

[Package source model](package-source-model.md) owns configured authority and
whether it has a credential-safe durable key.
[Cache concurrency and publication](cache-concurrency.md) owns admitted package
payload publication.
[Artifact acquisition and workspaces](artifact-acquisition-and-workspaces.md)
owns `PackageContentGenerationIdentity`, `PackageRootBinding`, and the
owner-mediated digest pattern. The durable package-content identity needed by
this CLI path is the focused acquisition prerequisite tracked by
[#5484](https://github.com/richlander/dotnet-inspect/issues/5484).
[InertText](inert-text.md) owns persisted treated-text semantics.
[Inspection space](../inspection-space.md#corecache) owns the repository-wide
derived-cache rule, and `CoreCache` supplies only the storage mechanism.

## Cache subject

The logical subject is:

```text
PackageIndexCacheSubject
  durable configured-authority cache key
  normalized package ID
  normalized package version
  retained archive/tree content digest
  package-index projection contract
```

Every dimension is load-bearing:

- **Configured authority** preserves current package authorization semantics.
  NuGetFetch producer identity remains provenance and cannot substitute for
  package-owned authority. If the authority has no credential-safe durable key,
  this owner does not persist or reuse an entry for it.
- **Coordinate** preserves the package identity the result describes. Package
  ID comparison is case-insensitive and version spelling uses the package
  owner's normalized version; this owner does not define another normalization.
- **Content digest** identifies one retained package-content generation: the
  retained archive plus the product-owned inspection tree that admission proved
  matches that archive. The acquisition owner binds it to the same
  `PackageContentGenerationIdentity` that cold inspection consumes. This cache
  does not reopen a path, hash an extracted tree after inspection, or infer
  content identity from a producer key, timestamp, package manifest, or
  completion marker. The process-local generation token proves same-snapshot
  use while the digest supplies the durable key; neither substitutes for the
  other.
- **Projection contract** identifies the closed set and semantics of persistent
  fields. It is represented by the versioned cache category and an explicit
  completion record in the payload, not by accepting whatever fields an entry
  happens to contain.

The subject is frozen before lookup or cold production. Cold inspection,
serialization, and publication consume that same subject and process-local
content generation. If acquisition cannot retain that generation through cold
inspection and publication, the operation cannot publish.

## Persistent-cache eligibility

Persistent reuse is available only when the package-content owner issues both:

- a durable configured-authority key; and
- durable content identity for the exact tree cold inspection reads.

A product-owned package-content slot can qualify because its retained archive
and extracted tree are admitted as one matching content generation. The
acquisition owner establishes that relationship; this cache does not infer it
from `RequiresArchiveTreeMatch` or a completion marker.

NuGet's global-packages folder is a foreign tree rather than a product-owned
one-to-one extraction. It can be archive-less, its retained archive does not
necessarily identify the tree that inspection reads, and other tools may
change it between operations. Such a tree remains inspectable, but it is
ineligible for this persistent derived cache unless a future acquisition owner
retains and identifies the exact inspected snapshot.

A directly named local `.nupkg` likewise remains outside persistent reuse. Its
current inspection path is explicitly local rather than a configured package
authority. A future owner may admit it only by supplying both required
identities; the shared `explicit-local-input` producer spelling is not enough.

If either identity is unavailable, the package operation uses the ordinary
cold inspection path. Disabling this optimization is the conservative adoption
state, not a package failure.

## Persistent projection

The persistent projection contains only facts that are deterministic functions
of the admitted package payload under one host filesystem's semantics and the
projection contract. It is complete: publication cannot encode a
request-selected subset and later return it as the full projection.

The target projection is the following closed inventory:

- package and manifest facts: `PackageName`, `ManifestVersion`, `Version`,
  `Description`, `Authors`, `License`, `LicenseUrl`, `Repository`,
  `RepositoryType`, and `RepositoryCommit`;
- package-tree facts: `ReadmeFile`, `PackageReadmeFile`, `HasReadme`,
  `HasAgentDocumentation`, `IsToolPackage`, `PackageTypes`,
  `ContentDirectories`, `TargetFrameworks`, `SupportedRids`, `AssemblyCount`,
  `IsFrameworkDependent`, `HasRidSpecificAssets`, `HasNativeDependencies`,
  `ToolFormat`, `IsRidSpecificPointerPackage`, `ToolCommands`,
  `RuntimeTargetRid`, `NativeFiles`, and `LibraryFiles`;
- manifest and deps-file relationships: `DependencyGroups`,
  `RuntimeDependencies`, and each `RuntimeIdentifierPackages` member's
  `RuntimeIdentifier` and `PackageId`, but not its request-current `Exists`;
- `BuiltDate` from the retained archive in the identified archive/tree
  generation; and
- local binary facts: `TotalBinaries`, `EmbeddedPdbs`, `InPackagePdbs`,
  `EmbeddedSourceLinkPdbs`, and `InPackageSourceLinkPdbs`, with
  `SymbolsAvailable` and `SourceLinkAvailable` derived only from those local
  counts.

Every `InspectionResult` field not listed is outside the projection. Adding,
removing, or changing the semantics of an inventory member changes the
projection contract and cache category. A field that depends on another
artifact, authority, capability, clock, or cache either adds that dependency's
owner-issued identity to the subject or remains outside this cache.

The producer supplies one canonical projection. Package-tree paths and
filesystem-derived string lists use normalized relative paths and ordinal
ordering. RID package references order by runtime identifier and package ID.
Dependency groups order by target framework, and their dependencies and
`RuntimeDependencies` order by package ID and version; equal members retain
their multiplicity. Package-authored scalar and list order that comes directly
from the manifest remains payload-defined. More than one distinct deps-file
runtime target makes `RuntimeTargetRid` ambiguous and the result ineligible for
publication rather than selecting whichever file the filesystem enumerated
last.

`PackageIndexCache` validates this canonical shape and declines publication for
a noncanonical or ambiguous value; it does not sort or repair the result after
cold inspection. #3738 owns adoption in `PackageInspector`, including
canonical production before the same result is returned cold or offered for
publication. Its producer-side evidence must seed a multi-RID tool package,
vary deps-file and directory enumeration order, and prove equal canonical
results or visible cache ineligibility.

Binary-signal production for this projection does not acquire or observe
external PDBs. `SnupkgPdbs`, `MsdlPdbs`, `OtherPdbs`, their SourceLink
counterparts, and aggregate availability that includes them remain
request-current. Their absence from the projection is not persistent zero
evidence.

The following facts remain outside the persistent projection:

- package metadata enrichment such as downloads, publication state,
  deprecation, and vulnerabilities;
- whether a RID companion is available under the current source policy;
- whether network or symbol acquisition is currently authorized or succeeds;
- tool-wrapper traversal and the requested package's relationship to the final
  payload; and
- current host capability, cancellation, deadlines, and presentation choices.

The caller may compose those facts after a hit. Their absence from the entry is
not evidence that they are false or unavailable.

## Hit, miss, and failure semantics

Lookup first requires a complete `PackageIndexCacheSubject`. Missing durable
authority or retained-content identity is a miss and does not fall back to a
producer-keyed persistent entry.

An entry is a hit only when:

1. it is read from the exact current contract category;
2. its subject equals the requested subject;
3. its framing and every required value decode successfully;
4. its contained text reconstructs only through the owning text policy; and
5. its explicit completion record proves the complete current projection was
   published.

The completion record represents every inventory member's state, including
null and default states, and closes the framed payload. A decoder rejects a
missing, duplicate, unknown, or unaccounted-for member. It does not infer that
a deleted field had its default value merely because a line-oriented encoding
omits defaults.

Missing, predecessor, truncated, malformed, or semantically incomplete entries
are misses. A miss runs the ordinary authorized cold path. Cache corruption is
not reported as package corruption because the entry is optional derived data,
not the acquired package payload.

An invalid cold result, failed package inspection, or incomplete projection is
not published. A value that this cache cannot represent, including one whose
treated-text provenance cannot be persisted, declines publication rather than
invalidating an otherwise successful package inspection. A storage write
failure likewise leaves the cold result usable and visible. The current
`pkg-index-v16` truncated-description exception is a known mismatch with this
target behavior. Target adoption supersedes that exception's effect on the
package operation: [#3787](https://github.com/richlander/dotnet-inspect/issues/3787)
continues to own whether richer provenance can make a truncated description
persistable, while this owner makes an unpersistable optional value a cache
nonpublication rather than a package failure.

## Freshness and replacement

An entry has no time-based expiry. Its reusable facts are functions of exact
retained content. When acquisition supplies a different durable content
identity, the earlier entry cannot answer even when authority and coordinate
are unchanged.

This owner does not promise that acquisition will detect a source-side
replacement behind an already-committed package-content slot. It describes only
the retained payload acquisition supplied. Source replacement, reacquisition,
and package-content-store freshness remain acquisition-owner decisions.

Current authorization is still checked before cache lookup. Removing a source,
changing package-source mapping, rotating to an authority without the same
owner-issued durable identity, or otherwise revoking eligibility makes the
entry unreachable even when its bytes remain on disk.

A projection-contract change selects a successor category. Older categories
are never reinterpreted under the new contract. `CoreCache` may retire older
numeric categories after the successor registers; that cleanup has no bearing
on whether a current entry is semantically valid.

## Publication and concurrency

`PackageIndexCache` delegates atomic file replacement and best-effort storage to
`CoreCache`. It adds no lock, single-flight registry, or mutable publication
protocol.

Within one host-local `CoreCache` root, concurrent producers may publish the
same subject only when the subject proves that both consumed the same retained
payload and projection contract. They observe the same host filesystem
semantics, and the cache accepts only canonical projections, so their encodings
are semantically equal. An atomic winner or later equal replacement is
sufficient; readers observe an old complete entry, a new complete entry, or a
miss, never a partially written entry. Moving or sharing a cache root across
hosts with different filesystem semantics is unsupported.

No TLA+ model is required for this focused owner. Scheduling and filesystem
publication are delegated to the existing `CoreCache` mechanism, while this
contract's correctness rests on immutable subject equality and complete-value
validation rather than a new stateful interaction. A future mutable
coordination protocol would require its own design and interaction model.

## Pathological case

Two configured authorities intentionally receive one credential-free NuGetFetch
producer identity, and both expose `Example@1.0.0`:

```text
authority A -> payload digest aaa... -> Authors: "A"
authority B -> payload digest bbb... -> Authors: "B"
```

The current key:

```text
producer-key:example@1.0.0
```

allows A's warm result to answer B. A source-policy change can therefore report
facts from bytes the current request did not authorize.

The target subjects differ in both authority and content identity:

```text
authority-A/example/1.0.0/aaa...
authority-B/example/1.0.0/bbb...
```

Neither entry can answer the other request. Replacing B's package while keeping
its coordinate cannot reuse the old entry when acquisition supplies the
replacement as a different retained content identity.

The neighboring positive case is one authority reacquiring the same retained
payload under the same normalized coordinate. Its authority and digest are
equal, so the persistent result remains reusable across processes.

## Precedents and deliberate choices

- NuGet package stores scope payload reuse by an exact coordinate and authorized
  source. This owner consumes that decision rather than reconstructing source
  authorization.
- Git-style content addressing demonstrates that derived data can name immutable
  bytes rather than a mutable location. This owner additionally retains
  authority because digest equality does not grant package access.
- The repository's library effective-catalog cache includes a content hash, and
  `AnalysisIndexCache` records why a path coordinate alone cannot establish
  derived-result identity. Package inspection follows the same principle using
  the acquisition owner's digest instead of reopening a path.

The deliberate divergence from NuGet's ordinary exact-coordinate reuse is the
required content digest. NuGet.org's immutable-coordinate policy is an external
service guarantee, while this cache can consume packages from authorities that
make no such promise and can bypass the complete inspection stage. The digest
keeps that broader reuse honest without turning the cache into an authority.

## Current implementation gap

`pkg-index-v16` keys entries by NuGetFetch producer key, lowercased package ID,
and version. `PackageExtractionResult` does not carry a package-owned durable
authority key, `PackageContentGenerationIdentity`, or durable identity for the
exact inspected tree into `PackageInspector`. It also permits a global-packages
foreign tree to seed or consume the cache, persists `Snupkg`, `Msdl`, and
`Other` PDB observations plus aggregates that can include them, and has no
explicit complete-entry record. The current namespace therefore cannot satisfy
the target subject or projection and must be fenced rather than relabeled when
adoption lands.

Adoption depends on #3738 carrying package authority and provenance through
derived inspection and #5484 issuing exact durable package-content identity.
Until both inputs exist, the correct successor behavior is cold-path-only
rather than fallback to `pkg-index-v16`.

The existing `pkg-index-v16` behavior ships unchanged until that implementation
slice. It remains a legacy producer-scoped namespace, and no existing Release
gate proves configured-authority separation. The package source model's target
`LegacyProducerScopedCache_IsNotReinterpretedAsAuthorityScoped` gate is not
implemented, so interim authority containment is **unverified**. Existing tests
prove only predecessor-category fencing and separation between distinct
producer keys; neither makes producer identity an authority.

Existing tests establish useful partial properties:

- `EqualCoordinatesFromDifferentProducersDoNotShareInspectionResults`;
- `RidAvailability_IsNotPersisted`;
- `Description_RoundTripsAsContainedProse`;
- `Description_MalformedEnvelopeRejectsTheWholeCacheEntry`;
- `Description_NullAndEmptyRemainDistinct`.

They do not prove package authority, retained-content identity, projection
completeness, or current-subject wiring.
`Description_TruncatedValueRequiresHigherProvenancePersistence` instead records
the current exception that target adoption must replace with nonpublication.

## Required gates

The target remains unverified until Release tests establish:

- `PackageIndexCache_DistinctAuthoritiesWithEqualProducerDoNotShare`,
  using two authorities that deliberately share producer identity and publish
  different bytes for one coordinate;
- `PackageIndexCache_DifferentRetainedContentIdentityCannotHit`, presenting two
  acquisition-issued content identities under one authority and coordinate and
  proving the first entry cannot answer the second;
- `PackageIndexCache_AuthorityWithoutDurableKeySkipsPersistentReuse`, proving
  there is no producer-keyed fallback;
- `PackageIndexCache_HitUsesTheExactColdProductionSubject`, proving acquisition,
  inspection, and publication consume one retained content generation and
  durable identity;
- `PackageIndexCache_ForeignTreeCannotSeedPersistentReuse`, covering
  global-packages entries with and without retained archives;
- `PackageIndexCache_RejectsNoncanonicalOrAmbiguousProjection`, covering
  shuffled filesystem-derived lists and multiple distinct deps-file runtime
  targets;
- `PackageIndexCache_CompleteProjectionRoundTripsOrMisses`, covering a complete
  positive entry and missing, predecessor, malformed, truncated,
  missing-completion, deleted-member, duplicate-member, unknown-member, and
  semantically incomplete entries; and
- `PackageIndexCache_ProjectionExcludesRequestCurrentFacts`, proving RID
  availability, metadata enrichment, wrapper routing, current authorization,
  and externally acquired symbol availability cannot enter the persistent
  value.

The existing inert-description and producer-separation tests remain evidence
for their narrower current properties. They do not count as the target gates
under different names. Acquisition's
`PackageRootGenerationIdentity_ReplacementChangesIdentity` remains evidence for
its process-local generation owner; #5484 owns the durable identity and
W-to-S-to-W retained-content evidence. End-to-end `PackageInspector` adoption
and current-fact recomposition remain integration work under #3738 rather than
gates assigned to this cache owner.

## Non-claims

This design does not:

- make a package coordinate immutable;
- make `CoreCache` a semantic cache owner;
- define a universal derived-cache abstraction or migrate another cache;
- authorize sharing merely because two authorities produce equal bytes;
- require persistent caching for an authority without a safe durable identity;
- detect source-side replacement behind an acquisition cache hit;
- persist results derived from a foreign or unretained inspection tree;
- reuse an entry across hosts with different filesystem semantics;
- persist package payloads, source credentials, untreated display text, or
  request-current policy;
- guarantee a cache hit, durable write, cross-process single-flight, or
  power-loss transaction; or
- change current package output or command behavior.
