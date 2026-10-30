# Package icon range inspection

## Status, owner, and claim

This document is the normative owner for reading one package's embedded icon
without acquiring its complete archive. It is tracked by
[#8624](https://github.com/richlander/dotnet-inspect/issues/8624).

Given one exact package coordinate on one already-authorized source that
supports package archive ranges, the query reads the archive directory, the
single root nuspec, and the manifest-declared embedded icon entry, then returns
the existing bounded `PackageIconResult`.

The operation never downloads the complete package as a fallback. A source
that cannot serve the range capability, ignores a range, changes during the
read, or uses an unsupported archive shape returns typed non-success so the
consumer can use its ordinary fallback icon.

## Inputs and result

The input is:

- an exact `PackageSourceCoordinate`;
- one `IPackageArchiveRangeSource` already selected and authorized by its
  caller;
- the package archive and directory bounds supplied by the caller; and
- the caller's cancellation and package-source operation context.

The result is one of:

- `Completed(PackageIconResult)`, preserving `Available`, `Missing`, and the
  existing icon-content refusal reasons;
- `Failed(PackageSourceFailure)`, preserving source transport and response
  failure; or
- `Refused(PackageArchiveReadRefusal)`, preserving range-specific refusal.

Cancellation remains cancellation. A failed or refused read is not reported as
a package whose manifest omits an icon.

## Two-stage read

The archive directory identifies the one root nuspec without reading any entry
body. The query reads that nuspec under the existing manifest byte limit and
uses `PackageManifestFactsQuery` to validate its coordinate and obtain its
embedded `<icon>` path.

A missing declaration completes as `PackageIconResult.Missing`. An invalid
manifest or unsafe icon path completes with the existing typed icon refusal.
Otherwise the query reads only the declared icon entry under
`PackageIconQuery.MaxIconBytes`, then applies the existing image signature and
decoded-dimension checks.

No package asset folder, dependency payload, README, license, or unrelated root
entry is selected. The deprecated nuspec `<iconUrl>` grants no network
authority.

## Browser dependency-list adoption

The explicitly approved first production consumer is Inspect Web's Package
Dependencies list. The Browser resolves each declaration through its existing
dependency-version policy, then requests the icon for that exact version.
Requests are bounded and stale results cannot update a different package,
framework, or dependency group.

Dependency rows retain their existing navigation behavior. Available icons use
the admitted embedded bytes. Missing, failed, or refused icon reads use the
existing NuGet default package icon; image failure also returns to that
fallback. Icon acquisition does not delay enabling a dependency row.

This first adoption is Browser-only because terminal output has no package-icon
presentation. The range query remains host-neutral so another graphical host
can consume it without duplicating archive or image validation.

## Basis and evidence

[Package archive range access](package-archive-range-access.md) owns directory
and exact-entry reads. `PackageIconQuery` owns icon path, format, byte, and
decoded-dimension admission. Inspect Web's retained Workspace delivery provides
the local precedent for keeping full icon payloads out of aggregate responses.

`System.Text.Json@9.0.4` is the motivating real package. Its nupkg is retained
under `fixtures/services/signatures/`; the range-query gate demonstrates that
the directory, nuspec, and icon are sufficient and that fewer bytes are read
than the complete archive.

Focused gates cover:

- an admitted real embedded icon read without a complete response;
- a manifest with no icon;
- a missing or invalid declared icon;
- a source that ignores range without a complete-fetch retry;
- Browser facade projection; and
- dependency-row fallback, successful icon replacement, bounded concurrency,
  and stale-result rejection.

## Non-claims

This owner does not:

- resolve dependency version ranges;
- select or authorize package sources;
- cache package entries or complete archives;
- fetch remote nuspec icon URLs;
- identify which package supplies an assembly;
- define package or ecosystem identity; or
- change package acquisition, dependency navigation, or graph semantics.
