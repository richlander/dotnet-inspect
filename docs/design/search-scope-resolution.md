# Search scope resolution

This document owns the CLI-scoped normalization that decides when the search
default applies and expands named search-scope groups into ordered platform
framework and package sets. Type-search `find`, `implements`, `extensions`,
and type-mode `depends` consume the result.

This is the current behavior contract and reference oracle for the ground-up
typed search-scope domain tracked by
[#5602](https://github.com/richlander/dotnet-inspect/issues/5602). It does not
define that future declaration component or its adoption plan.

[CLI host architecture](../cli-architecture.md) owns parsing, valued
`--platform` disambiguation, source authorization, operation lifetime,
diagnostics, and rendering. [Find type-search service](find-search-service.md)
owns candidate collection and classification after it receives an authorized
scope. Package, platform, project, and local-library owners retain acquisition,
identity, failure, and caching. [CLI change classification and obsolete
inputs](cli-change-classification.md) governs changes to published scope
syntax, defaults, and catalogs.

## Purpose and baseline

Search commands need a useful no-option starting point without silently
widening an explicitly bounded request. The conventional source-selection
shape is:

- implicit sources apply when the caller names none;
- naming any source suppresses the implicit set; and
- multiple explicit selectors form one union.

This matches the .NET/NuGet convention that configured sources are implicit
until an explicit source option overrides them. dotnet-inspect adds one
deliberate distinction: its explicit scope selectors are independently useful
and therefore compose rather than replace one another.

## Boundary

The normalizer consumes:

- the bare platform-group selection;
- the Microsoft.Extensions and ASP.NET Core package-group selections;
- explicit package and library presence;
- explicit platform-library presence;
- explicit project and binary-directory presence; and
- package-prefix presence, including a successful empty expansion.

It returns only:

- an ordered platform-framework set; and
- an ordered package set containing explicit packages and selected catalogs.

Direct libraries, platform libraries, projects, and binary directories pass
through their command-owned option models unchanged. Their presence is still
an input because it suppresses the implicit default.

The normalizer is pure. It performs no parsing, package-prefix expansion,
network authorization, acquisition, matching, result ordering, or rendering.

## Default activation

With no explicit source indicator, the normalizer returns an empty package set
and exactly these platform frameworks in order:

1. `runtime`
2. `aspnetcore`
3. `netstandard`

Any explicit source indicator suppresses that default. An explicit source that
is empty, unavailable, or produces no matches does not fall back to platform
frameworks. Failure and empty-result behavior remain with the source and
operation owners.

Options that refine an already selected operation are not source indicators.
For example, `--tfm`, visibility, relationship traversal, row selection, and
output format do not suppress the default.

## Explicit composition

Explicit selectors compose additively:

| Normalized selection | Contribution |
| --- | --- |
| bare `--platform` | all three platform frameworks in default order |
| `--extensions` | the current Microsoft.Extensions package catalog |
| `--aspnetcore` | the current ASP.NET Core package catalog |
| explicit `--package` values | those package coordinates |
| `--package-prefix` with a type pattern | up to 500 matching package coordinates; its presence suppresses the default even when expansion is empty |
| valued `--platform`, `--library`, `--project`, or `--bin` | no framework or package contribution; their presence suppresses the default |

Valued `--platform` is parser output for a platform-library source, not the
bare platform-group selection. This design consumes that distinction and does
not define its token grammar.

Type-search package-prefix contribution is bounded to the first 500
coordinates returned by prefix search. Reaching that bound produces a visible
warning because additional matches may exist. Prefix query, provider ordering,
and paging remain acquisition concerns.

Package order is:

1. explicit package coordinates in caller order;
2. the Microsoft.Extensions catalog, when selected; and
3. the ASP.NET Core catalog, when selected.

Package coordinates use case-insensitive set semantics while preserving the
first spelling and position. Repeating a coordinate explicitly or through a
catalog does not authorize duplicate acquisition. A versioned coordinate and
an unversioned coordinate remain distinct.

Catalog membership is maintained by this owner in `ScopeConstants`. Adding,
removing, or reordering members is observable because it can change network
work, source order, results, and failures; such changes require the
classification and evidence defined by the CLI change-classification design.
The design does not duplicate the current catalog inventory in prose.

## Command participation

Type-search `find`, `implements`, and `extensions` use this normalization to
select acquisition scope. Patternless `find --package-prefix` instead runs the
Nuspec-only profile owned by
[the package query CLI](package-query-cli.md). Its parser still normalizes raw
search selectors so the command can reject incompatible API-search scope
before network access, but the profile prefix is not expanded and the
normalized result does not select profile acquisition. `depends` uses
normalization only for type-hierarchy mode; its
package-dependency and library-reference modes are unary source operations and
do not acquire a search default.

Commands may support different direct-source options. Every supported direct
source must nevertheless contribute to the same explicitness decision. Adding
a source option without wiring its presence into default suppression is a
contract violation: an unchanged explicit request would silently acquire
additional platform sources.

That command-adapter obligation is distinct from the pure normalizer contract.
The normalizer receives already-lowered flags, coordinates, and presence
signals; it does not prove that every command supplied them correctly.

## Implementation and gates

`ScopeResolver` implements default activation, explicit group expansion,
package ordering, and deduplication. Its pure normalization contract has full
Release gate coverage:

- `SearchScopeResolutionTests.NoExplicitSource_UsesOnlyPlatformFrameworks`
  gates the exact empty-input result and framework order;
- `SearchScopeResolutionTests.EachGroupCombination_ResolvesExactly` gates the
  complete finite group-flag truth table;
- `SearchScopeResolutionTests.EachDirectSourceSignal_SuppressesTheDefault`
  gates package, library, package-prefix, and the generic additional-source
  signal consumed by the normalizer;
- `SearchScopeResolutionTests.ExplicitGroups_ComposeInOrderWithoutDuplicatePackages`
  gates additive composition, order, first-occurrence preservation, and
  case-insensitive set semantics; and
- `SearchScopeResolutionTests.VersionedAndUnversionedPackageCoordinates_RemainDistinct`
  gates coordinate identity across version presence.

Command parsers supply the direct-source presence consumed by that fully gated
normalizer and retain direct values in their own option records. Their
end-to-end wiring has partial Release gate coverage:

- `SearchScopeResolutionTests.EachCommandDirectSource_DoesNotFallBackToPlatform`
  gates library, platform-library, and project wiring on every participating
  command, plus binary-directory wiring on `find`;
- `SearchScopeResolutionTests.ExplicitMissingDirectory_DoesNotFallBackToPlatform`
  and
  `SearchScopeResolutionTests.DependsExplicitSourceMiss_DoesNotFallBackToLibraryMode`
  are the outcome-level pathological gates;
- `SearchScopeResolutionTests.DependsImplicitScope_RetainsBareLibraryFallback`
  gates the command's source-free type-or-library convenience; and
- `SearchScopeResolutionTests.PackagePrefixGuidance_DisclosesExpansionLimit`
  and
  `SearchScopeResolutionTests.PackagePrefixCurrentGuidance_DisclosesExpansionLimit`
  gate the prefix bound's user-facing help, workflow, and skill;
- `SearchScopeResolutionTests.PackagePrefixLimitReached_IsVisible` gates its
  warning; and
- `SearchScopeResolutionTests.PackagePrefixExpansionLimit_UsesSelectedBound`
  gates the declared 500-package type-search expansion bound.

The search-only measurements in
[the package query CLI](package-query-cli.md#measured-package-profile-limits)
characterize prefix expansion through 500 coordinates. They do not measure the
downstream package-archive acquisition that a type search may perform; that
cost remains with the acquisition owners and is not evidence for this bound.

The residual command-adapter matrix is explicitly unverified: the suite does
not provide one outcome-level non-vacuity case for every explicit package,
package-prefix, `--extensions`, and `--aspnetcore` path on every participating
command. The exact `take:` wiring from the declaration into prefix acquisition
is also unverified. This design does not generalize the representative wiring
gates into those stronger claims.

## Non-claims

This design does not:

- define command token grammar or optional-value disambiguation;
- define workspace identity, partitioning, or lifetime;
- define package, platform, project, local-library, or prefix acquisition;
- choose candidate or result order after source resolution;
- promise that catalog membership is stable or backward compatible;
- turn a source miss or failure into fallback; or
- define output shape, verbosity, or diagnostics.
