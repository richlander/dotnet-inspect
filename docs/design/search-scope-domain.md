# Typed source intent and search normalization

## Status and authority

Focused component under
[#6031](https://github.com/richlander/dotnet-inspect/issues/6031), the first
declaration slice of
[#5602](https://github.com/richlander/dotnet-inspect/issues/5602).
`DotnetInspector.SourceSelection` owns two independently usable capabilities:
construction and inspection of immutable source intent, and its pure reference
normalization under search policy. Construction does not apply search policy.

This slice does not adopt the component in a production command or browser
adapter. [Search scope resolution](search-scope-resolution.md) remains the
current CLI behavior owner and compatibility oracle until its focused adoption.
[Package Set Registry](package-set-registry.md) retains application identity,
lookup, and membership. [Package source model](package-source-model.md) and
the source-production work in
[#5954](https://github.com/richlander/dotnet-inspect/pull/5954) retain
authorization, provider selection, discovery order, paging, completion, and
realization. This owner issues intent, not evidence of acquisition.

The focused prerequisite
[#6075](https://github.com/richlander/dotnet-inspect/issues/6075) extends the
declaration to preserve the package-reference and local-archive forms already
accepted by the four CLI searches. Its claim is faithful source intent, not a
new version-resolution policy or CLI adoption.

## Basis and consumers

The design follows immutable request values and separate interpretation, as
demonstrated by the repository's `RowSelectionPlan` and `RowSelectionExecutor`.
`NuGetGalleryDiscoveryRequest` is supporting evidence for bounded inert source
intent; its Gallery-specific provider, order, and response-capacity semantics
do not transfer here. The current `ScopeResolver` supplies the search default,
direct-package precedence, and first-occurrence oracle. Its loose flags and
presence parameters are deliberately not the new declaration.

`PackageExtractor` and its existing `AssemblySetResolverTests` supply the
package-reference and explicit-archive compatibility oracle. The package
owner's existing reference parsing and version-acceptance predicate are exposed
through the pure `PackageReferenceParser`; extraction still delegates to those
same operations. [Version resolution](version-resolution.md) retains version
selection, cache bypass, matching, and candidate acquisition semantics.

The end-to-end tracker is #5602. Six adoption stages are:

1. This component and its ordinary public-consumer contract suite.
2. Catalog prefix handoff under #5728, owned by Static Ecosystem Packs.
3. The four CLI multi-source search adapters, retaining `AssemblySetRequest`.
4. Browser package/platform adoption through existing asynchronous acquisition
   and exact workspace generations.
5. Inventory and focused adoption of other CLI source-policy families.
6. Retirement of obsolete adapter shapes when their migrations are complete.

The catalog consumes `PackagePrefixRequest`; CLI and Browser can independently
consume `SourceIntent` and `SearchSourceNormalizer`. Neither host must apply
search defaulting merely to construct or inspect a declaration. The source
production changes in #5954 and the catalog hints/priorities in #6028 remain
separate. This slice introduces no universal realization protocol.

## Declaration contract

A declaration is an immutable ordered snapshot of a closed set of selectors:

| Selector | Retained intent |
| --- | --- |
| Platform group | The search platform group, not a realized framework |
| Package | One Packages-owned `PackageCoordinate` |
| Package reference | A package ID and optional package-owner-accepted version expression |
| Package archive | An uninterpreted explicit local package-archive path |
| Package group | An ordered snapshot of package coordinates already selected by the caller |
| Package prefix | One validated `PackagePrefixRequest` |
| Library | An uninterpreted local library path |
| Platform library | An uninterpreted platform-library name |
| Project | An uninterpreted project path |
| Binary directory | An uninterpreted directory path |

`SourceIntent.Empty` is inspectably empty. `Create` retains every selector and
its order, including repetitions and empty package groups; `Append` produces
a new snapshot without changing either input. Null selectors are invalid.
Runtime inspection uses public typed variants, without friend access.

Package selectors and every package-group member pass the package owner's
`PackageCoordinateResolver.Validate`. Version, framework, and runtime
identifier remain that owner's fields, not newly parsed token syntax.
Package-archive, library, platform-library, project, and directory text must be nonblank and
contain no NUL. Original spelling is retained; existence, absolute-path
resolution, platform-specific path rules, and physical identity belong to
realization. Construction does not reject source combinations, apply
command cardinality, infer positional meaning, or authorize access.

### Package forms

A package reference retains a canonical package ID and the exact optional
version expression accepted by the package owner's
`PackageReferenceParser.IsValidVersion`. Unlike a validated coordinate, it can
retain `latest`, wildcard expressions, and other reference-version spelling
accepted by that owner. Null and empty expressions remain distinguishable;
construction does not resolve or rewrite either. NUL is rejected.

An archive retains its explicit path, not a package identity guessed from its
filename. Two paths with the same filename are still distinct requests.
Existence and archive admission remain realization work. An archive is not a
library and is not interchangeable with a coordinate or remote reference.

`PackageSource` is the closed typed family of coordinates, references, and
archives. Search normalization returns this family rather than a
coordinate-only projection that could silently omit a supported request.
Unresolved reference expressions do not become exact coordinate pins.

### Package-set handoff

The landed Package Set Registry contract supersedes #5602's initial
`PackageSet(PackageSetId)` hypothesis: package-set identity stays in the
front-end application catalog. A front end resolves its typed identity through
that registry and constructs a package group from the descriptor's membership.
The declaration retains the coordinate snapshot, not another package-set ID,
lookup callback, or inventory. A host that needs to remember the user's named
selection retains its catalog identity separately.

A package group may be empty and remains explicit. It is not a prefix result
or a claim that any source was queried. The normalizer distinguishes groups
from direct package requests so explicit packages retain precedence over
selected groups. Adapters own group ordering; the existing CLI mapping must
continue to put Extensions before ASP.NET Core regardless of token order.

## Package-prefix request

`PackagePrefixRequest` is an immutable request with original prefix spelling,
a positive `MaxPackages` integer, and `IncludePrerelease`. All three are
inspectable independently of a declaration or search normalization.

The prefix is a nonempty literal beginning of a package ID accepted by
`PackageCoordinateResolver.IsCanonicalPackageId`. It may be a complete valid
ID or end at a single `.` or `-` separator when appending a word character can
still produce a valid ID within the package owner's maximum ID length. There
is no trimming, wildcard, query syntax, or case folding at construction.
Matching semantics, source ordering, and the meaning of returned versions
remain source-owned.

The bound is required and must be positive; the request does not invent a
universal provider maximum. Command policy chooses bounds such as the existing
500-package type-search limit. A realization adapter must preserve the requested
bound or visibly reject unsupported intent, rather than silently clamp it.
Source-work/page limits, deadlines, source configuration, authentication, and
completion are not request fields. Prerelease eligibility is explicit, with
`false` as the construction default.

An accepted request proves intrinsic well-formedness only. Catalog discovery
may retain and return it; it does not prove that the prefix exists, that a
provider supports it, that a query is complete, or that traversal is permitted.

## Search interpretation

Normalization retains the original declaration alongside immutable output:
ordered framework selections, ordered package sources, and ordered
other selectors. Other selectors are prefixes, libraries, platform libraries,
projects, and directories; their exact values and relative order pass through.

- Only a declaration with zero selectors activates the implicit platform group.
- An explicit platform-group selector contributes the same frameworks but
  remains distinguishable from implicit activation.
- The group contributes `Runtime`, `AspNetCore`, `NetStandard`, in that order,
  once even if the selector repeats.
- Every explicit selector suppresses the implicit group. Empty package groups
  and unrealized prefixes do not disappear before this decision.
- Direct package sources contribute first in declaration order, then package-group
  members in group and member order.
- Coordinate requests use ordered-set semantics: compare all four
  coordinate fields with ordinal case-insensitive equality and retain the
  first request's spelling and position. Absent version and present version
  remain distinct, as do different framework/runtime selections.
- References use the same lexical ID/version identity, with absent framework
  and runtime fields. A reference and coordinate with equal fields coalesce,
  retaining the first source's typed form and spelling. `latest`, wildcards,
  empty expressions, and different exact-version spelling remain distinct.
- Archives use a separate lexical path identity, also ordinal case-insensitive
  to preserve the current search package-list policy. No filename inference,
  path normalization, or cross-kind equality participates.

This is declaration-level equality, not package version resolution or physical
artifact identity. For example, `1.0` and `1.0.0` remain different requests even
if acquisition later resolves them to one version. No selector is inferred from
display text. Prefixes are not replaced by hypothetical coordinates. Normalizing
the same declaration again gives the same semantic result; normalization does
not accept an acquisition result as a replacement declaration.

## Demo

An ordinary consumer can retain a catalog-ready request before running search:

```csharp
var request = new PackagePrefixRequest("Aspire.", maxPackages: 500);
var intent = SourceIntent.Empty.Append(new SourceSelector.PackagePrefix(request));
var normalized = SearchSourceNormalizer.Normalize(intent);

// intent.Selectors.Count == 1
// normalized.UsesImplicitPlatform == false
// normalized.Frameworks.Count == 0
// normalized.Packages.Count == 0
// normalized.OtherSources[0] retains the exact request
```

The neighboring empty declaration is still empty after inspection.
Normalizing it selects the three platform frameworks. Appending an empty
package group instead selects none: zero concrete packages are not zero intent.

Package-reference and archive intent can likewise be inspected without
acquisition:

```csharp
var intent = SourceIntent.Create(
[
    new SourceSelector.PackageGroup([new("Contoso.Core")]),
    new SourceSelector.PackageReference("Contoso.Core", "latest"),
    new SourceSelector.PackageArchive("./local/Contoso.Core.1.0.0.nupkg"),
    new SourceSelector.PackageReference("Contoso.Core", "2.*"),
]);
var selection = SearchSourceNormalizer.Normalize(intent);

// PackageReference: Contoso.Core, latest
// PackageArchive: ./local/Contoso.Core.1.0.0.nupkg
// PackageReference: Contoso.Core, 2.*
// Package: Contoso.Core, no version
// selection.UsesImplicitPlatform == false
```

The first declaration slice could not represent the middle three requests.
Its neighboring exact coordinate and package-group requests remain supported;
the normalizer now preserves all package forms in one ordered output.

## Contract evidence

`tests/DotnetInspector.SourceSelection.Tests` is a non-friend xUnit/MTP
executable, run in Release in normal CI. Its public-consumer gates cover:

| Gate | Claim |
| --- | --- |
| `SourceIntentTests` | Empty inspection, each typed variant, intrinsic rejection, independent snapshot/append, immutable collections, and package-owner validation |
| `PackagePrefixRequestTests` | Retained bounded request, malformed prefix/bound rejection, separator and maximum-length boundaries |
| `SearchSourceNormalizerTests` | Complete finite platform/group truth table, every direct source, stable package precedence/deduplication, retained prefix, and empty-group non-fallback |
| `PackageSourceIntentTests` | Reference/archive inspection, owner-issued parsing and version acceptance, original spelling, mixed-source ordering and equality, and explicit-source non-fallback |

The tests use product construction and normalization, not replacement
algorithms or manufactured acquisition evidence. Browser execution, CLI
adapter completeness, source realization, and end-to-end catalog prefix
selection remain unverified until their respective adoption slices.
