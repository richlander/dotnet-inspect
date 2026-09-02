# Search scope resolution

This document owns the CLI-scoped normalization that decides when the search
default applies and expands named search-scope groups into ordered platform
framework and package sets. `find`, `implements`, `extensions`, and type-mode
`depends` consume the result.

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

With no explicit source indicator, the resolved scope is exactly the platform
frameworks in this order:

1. `runtime`
2. `aspnetcore`
3. `netstandard`

No package catalog is part of the implicit default.

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
| `--package-prefix` | up to 100 matching package coordinates; its presence suppresses the default even when expansion is empty |
| valued `--platform`, `--library`, `--project`, or `--bin` | no framework or package contribution; their presence suppresses the default |

Valued `--platform` is parser output for a platform-library source, not the
bare platform-group selection. This design consumes that distinction and does
not define its token grammar.

Package-prefix contribution is bounded to the first 100 coordinates returned
by prefix search. Reaching that bound produces a visible warning because
additional matches may exist. Prefix query, provider ordering, and paging
remain acquisition concerns.

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

`find`, `implements`, and `extensions` always use this normalization for their
search operation. `depends` uses it only for type-hierarchy mode; its
package-dependency and library-reference modes are unary source operations and
do not acquire a search default.

Commands may support different direct-source options. Every supported direct
source must nevertheless contribute to the same explicitness decision. Adding
a source option without wiring its presence into default suppression is a
contract violation: an unchanged explicit request would silently acquire
additional platform sources.

## Obsolete hidden input

The historical `--curated` input duplicated bare `--platform` after the default
package catalog became empty. It was hidden from help, README, and product
skills and had no independent current-interface utility; explicit
`--platform` already composes with packages and other source selectors.

The input is removed. Because an option-shaped token is rejected by the
ordinary parser rather than rebound or routed to another operation, no focused
invalid-input guard or reservation is required.

## Implementation and gates

`ScopeResolver` implements default activation, explicit group expansion,
package ordering, and deduplication. Command parsers supply the direct-source
presence that suppresses the default; they retain the direct values in their
own option records.

The Release `dotnet-inspect.Tests` suite enforces the contract:

- `SearchScopeResolutionTests.NoExplicitSource_UsesOnlyPlatformFrameworks`
  gates the exact default and framework order;
- `SearchScopeResolutionTests.EachExplicitSourceKind_SuppressesTheDefault`
  gates package, library, package-prefix, and the generic additional-source
  signal consumed by the normalizer;
- `SearchScopeResolutionTests.EachCommandDirectSource_DoesNotFallBackToPlatform`
  gates library, platform-library, and project wiring on every participating
  command, plus binary-directory wiring on `find`;
- `SearchScopeResolutionTests.ExplicitGroups_ComposeInOrderWithoutDuplicatePackages`
  gates additive composition, order, and case-insensitive set semantics;
- `SearchScopeResolutionTests.ExplicitMissingDirectory_DoesNotFallBackToPlatform`
  and
  `SearchScopeResolutionTests.DependsExplicitSourceMiss_DoesNotFallBackToLibraryMode`
  are the outcome-level pathological gates;
- `SearchScopeResolutionTests.DependsImplicitScope_RetainsBareLibraryFallback`
  gates the command's source-free type-or-library convenience; and
- `SearchScopeResolutionTests.PackagePrefixGuidance_DisclosesExpansionLimit`
  and `SearchScopeResolutionTests.PackagePrefixLimitReached_IsVisible` gate the
  prefix bound's user-facing help and warning; and
- `SearchScopeResolutionTests.CuratedCompatibilityInput_IsNotRegistered`
  gates removal of the redundant hidden input from every participating
  command.

## Non-claims

This design does not:

- define command token grammar or optional-value disambiguation;
- define workspace identity, partitioning, or lifetime;
- define package, platform, project, local-library, or prefix acquisition;
- choose candidate or result order after source resolution;
- promise that catalog membership is stable or backward compatible;
- turn a source miss or failure into fallback; or
- define output shape, verbosity, or diagnostics.
