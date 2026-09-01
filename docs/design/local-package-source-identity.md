# Local package source identity

This document is the normative owner for canonical local package-source
identity. It defines how a path or `file://` declaration becomes one absolute
host-local identity before package-source mapping, source-client construction,
cache authorization, or filesystem access.

This is the first focused slice of
[#3759](https://github.com/richlander/dotnet-inspect/issues/3759). Folder-feed
layout recognition, candidate enumeration, manifest and payload acquisition,
search capability, and local-before-HTTP composition remain separate
successors [#5399](https://github.com/richlander/dotnet-inspect/issues/5399)
and [#5400](https://github.com/richlander/dotnet-inspect/issues/5400).

## Boundary

The owner consumes:

- untreated source text;
- the absolute directory against which a relative declaration is resolved;
  and
- the current host's path semantics.

It returns either:

- an absolute lexical path and owner-defined equality;
- a determination that the input is an absolute non-file URI and therefore
  belongs to another source owner; or
- a visible pre-client rejection for unusable local syntax.

The package-configuration adapter supplies the base directory. A source read
from a NuGet configuration file uses that declaring file's directory, including
when several configuration files are merged. A command-line source uses the
command working directory. The local identity owner does not discover either
base from ambient state.

The owner rejects a relative resolution base. Legacy direct `PackageSource`
construction has no base parameter, so that compatibility adapter explicitly
supplies the process working directory; source-resolution paths canonicalize
relative declarations before construction.

The configuration adapter expands NuGet's `%NAME%` environment-variable syntax
before calling this owner. Command-line sources are not given a second
environment expansion after shell processing.

`SourceResolverTests.ResolveSources_ConfigRelativePathsUseEachDeclaringDirectory`,
`SourceResolverTests.ResolveSources_ConfigExpandsPercentEnvironmentVariables`,
and
`SourceResolverTests.ResolveSources_CommandRelativePathUsesWorkingDirectory`
are the Release gates for this handoff.

## Canonical identity

Canonicalization is lexical:

1. trim surrounding source-value whitespace;
2. convert an explicit `file://` URI to its host path;
3. resolve a relative path from the supplied base directory;
4. call the host path normalizer to remove `.` and `..` segments and use host
   directory separators;
5. remove a trailing directory separator unless the result is a root; and
6. retain that absolute path as the identity value.

Path and `file://` spellings of one directory therefore produce one identity.
Roots remain roots. Canonicalization does not require the directory to exist
and does not resolve symbolic links, junctions, hard links, mount aliases, or
filesystem-specific short names. Two lexical paths to the same object remain
two authorities unless the host path rules themselves equate them.

Windows identity uses ordinal case-insensitive path equality. Other hosts use
ordinal path equality. This refuses to fold case-distinct Unix paths. It
deliberately does not probe the current volume for a more permissive comparison:
identity must be deterministic before filesystem access, and a probe on one
volume cannot authorize a path on another mounted volume.

An explicit file URI must not carry user information, a query, or a fragment.
A UNC file URI is admitted only on Windows, where the host path implementation
can preserve UNC root semantics. Windows drive and UNC paths otherwise follow
Windows path normalization; Unix paths follow Unix normalization. A test run
on one host is evidence only for that host's path semantics.

`LocalPackageSourceIdentityTests.PathAndFileUriSpellingsShareIdentity`,
`DotSegmentsAndTrailingSeparatorsShareIdentity`,
`RootRetainsItsDirectorySeparator`,
`CaseComparisonUsesHostPathSemantics`, and
`SymbolicLinkDoesNotCollapseToItsTarget` are the cross-platform Release gates.
`ResolutionBaseMustBeAbsolute` gates explicit base ownership.
`WindowsDrivePathAndFileUriShareIdentity` and
`WindowsUncPathAndFileUriShareIdentity` are the Windows-host gates.

## Consumer handoff

`NuGetFetch.SourceResolver` canonicalizes local declarations while their
resolution base is still known. The resulting `PackageSource.Url` carries the
canonical path for legacy consumers. Package-source mapping continues to
select configured alias names first; only selected aliases may collapse by
local identity.

Persistent consumers derive their opaque key from the same owner-issued local
identity. They do not independently call `Path.GetFullPath`, parse a file URI,
fold path case, or trim separators. In particular:

- active and package-mapped sources carry the canonical path;
- source-scoped candidate and payload cache keys hash the canonical identity;
  and
- `.nupkg.metadata.source` is canonicalized by this owner before it is compared
  with an authorized source key.

When a provenance consumer no longer has a declaring base, it accepts only an
absolute local path or absolute `file://` URI. It rejects a relative spelling
rather than deriving authority from the process working directory.

The metadata sidecar remains a payload-cache provenance claim, not a version
candidate and not authority by itself. The current operation must still
authorize the matching local identity.

`NuGetSearchSourcesTests.ResolveSourcesForPackage_MappingCollapsesEquivalentLocalAliases`,
`PackageAcquisitionConcurrencyTests.GetSourceKey_PathAndFileUriShareLocalFolderIdentity`,
`GetSourceKey_RejectsRelativeLocalFolderWithoutBase`,
`GlobalPackageContent_FileUriProvenanceMatchesPathAuthorization`, and
`GlobalPackageContent_RejectsRelativeLocalProvenanceWithoutBase` are the Release
gates for these consumers. The owner-level
`LocalPackageSourceIdentityTests.AbsoluteIdentityRejectsRelativePathWithoutBase`
gate enforces the same no-base boundary.

## Failure and safety

Classification happens before HTTP client or authentication-context
construction. Plain paths and file URIs never become HTTP requests. Absolute
schemes other than HTTP, HTTPS, and file are rejected rather than treated as
paths or handed to a client.

Identity construction performs no directory enumeration and no content read.
Existence, readability, reparse-point policy, layout capability, archive
admission, and operation deadlines belong to later owners. An unusable local
value is a source failure, not package absence.

`SourceResolverTests.ResolveSources_UnsupportedSchemeFailsBeforeClientCreation`
gates the scheme boundary. The existing
`PackageSourceClientTests.LegacyLocalSourceRemainsAnExplicitUnsupportedKind`
gate remains evidence that the not-yet-implemented local client cannot fall
through to HTTP.

## Convention and deliberate differences

The contract adopts observable NuGet conventions: each configuration item
retains its declaring-file base, `%NAME%` variables expand before path
resolution, command-line overrides use the startup directory, native host path
syntax applies, and symlinks are not resolved for identity.

The analogous NuGet.Client paths are
[`AddItem.GetValueAsPath`](https://github.com/NuGet/NuGet.Client/blob/a173713d680ed2f40600034599e2c41108ca0c59/src/NuGet.Core/NuGet.Configuration/Settings/Items/AddItem.cs#L69-L77),
[`Settings.ResolvePathFromOrigin`](https://github.com/NuGet/NuGet.Client/blob/a173713d680ed2f40600034599e2c41108ca0c59/src/NuGet.Core/NuGet.Configuration/Settings/Settings.cs#L742-L806),
and
[`BuildTasksUtility.GetSources`](https://github.com/NuGet/NuGet.Client/blob/a173713d680ed2f40600034599e2c41108ca0c59/src/NuGet.Core/NuGet.Build.Tasks/BuildTasksUtility.cs#L638-L654).

NuGet.Client does not expose one canonical source identity across configuration,
protocol resources, caches, and `.nupkg.metadata`; those layers use different
representations and comparers. This owner deliberately supplies one identity
so those consumers cannot disagree. NuGet filesystem helpers may probe one
runtime volume for case behavior on non-Windows hosts; this owner instead keeps
non-Windows paths ordinal because source directories may reside on another
volume with stricter semantics. The differing NuGet behavior is visible in
[`PackageSource.Equals`](https://github.com/NuGet/NuGet.Client/blob/a173713d680ed2f40600034599e2c41108ca0c59/src/NuGet.Core/NuGet.Configuration/PackageSource/PackageSource.cs#L198-L220)
and
[`PathUtility.GetStringComparerBasedOnOS`](https://github.com/NuGet/NuGet.Client/blob/a173713d680ed2f40600034599e2c41108ca0c59/src/NuGet.Core/NuGet.Common/PathUtil/PathUtility.cs#L440-L505).

## Non-claims and successors

This owner does not define:

- which NuGet folder-feed layouts are supported;
- package search, version enumeration, manifest, symbol, or payload
  capabilities;
- directory enumeration bounds or mutation handling;
- local package archive validation or stream lifetime;
- local-before-HTTP acquisition tiers or cross-authority aggregation;
- package authority, `PackageSourceAssociation`, or source-result adoption;
- package-source mapping policy;
- cache publication and concurrency; or
- browser filesystem availability or user-interface registration.

A [local folder client successor](https://github.com/richlander/dotnet-inspect/issues/5399)
will consume this identity and define layout-specific capabilities and bounded
filesystem operations. A
[package composition successor](https://github.com/richlander/dotnet-inspect/issues/5400)
will adopt that client under the
[package source model](package-source-model.md), including local-before-HTTP
acquisition and authority-bearing candidate results. Those successors must not
redefine path identity.
