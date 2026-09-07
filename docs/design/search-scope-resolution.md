# Search scope resolution

This document owns CLI search-source binding: declaring source intent from
command syntax, resolving named groups through the application catalog, and
lowering shared normalization into the existing acquisition request.
Type/member-search `find`, `implements`, `extensions`, and type-mode `depends`
consume the result.

This is the current behavior contract and reference oracle for the ground-up
typed search-scope domain tracked by
[#5602](https://github.com/richlander/dotnet-inspect/issues/5602).
[Typed source intent](search-scope-domain.md) owns the independent declaration
and reference normalizer. The focused CLI adoption is
[#6118](https://github.com/richlander/dotnet-inspect/issues/6118), following the
package-form prerequisite #6075. This document owns the command behavior, not
the shared declaration's construction or normalization contract.

[CLI host architecture](../cli-architecture.md) owns parsing, valued
`--platform` disambiguation, source authorization, operation lifetime,
diagnostics, and rendering. [Find type-search service](find-search-service.md)
owns candidate collection and classification after it receives an authorized
scope. Package, platform, project, and local-library owners retain acquisition,
identity, failure, and caching. [CLI change classification and obsolete
inputs](cli-change-classification.md) governs changes to published scope
syntax, defaults, and named package sets.
[Package Set Registry](package-set-registry.md) owns the identity,
discoverability, and ordered membership of named package sets. This owner
consumes those sets for search composition; it does not own their inventories.

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

The CLI declares each supplied package, archive, library, platform library,
project, directory, prefix, and named group as typed source intent before
prefix discovery. Package-reference splitting uses the Packages-owned parser;
named package-set lookup stays in the application catalog.

The shared normalizer interprets that declaration. The CLI retains its result
and lowers all supported sources into `AssemblySetRequest` fields, passing
through existing command option models and `ToAssemblySetRequest`. This slice
does not replace acquisition, workspace loading, or other source-policy families.
Those four option models retain the shared selection, so downstream legacy
default guards cannot reinterpret a normalized empty contribution as absent
intent. Programmatically constructed options without a selection keep their
existing default behavior.

An original prefix declaration remains authoritative when discovery returns
zero or more package IDs. Discovered IDs augment, rather than replace, that
declaration for acquisition ordering. Their normalized package contribution
does not overwrite the retained user selection or its defaulting decision.

Malformed declaration input produces a clean CLI validation error rather than
an acquisition attempt or implicit fallback. The error identifies the public
option and its value without forwarding framework argument-exception text.
Type-search prefixes consume
the source-intent owner's literal-prefix grammar; patternless profiles retain
their separate grammar. Per-package framework/runtime qualifiers cannot be
represented by this CLI acquisition path and are visibly rejected, not
discarded; command-wide `--tfm` remains unchanged.

The shared normalizer stays pure. Prefix discovery remains explicit CLI source
work with the existing source configuration and timeout policy.

## Default activation

With an empty source declaration, the normalizer returns no package sources
and exactly these platform frameworks in order:

1. `runtime`
2. `aspnetcore`
3. `netstandard`

Any explicit source selector suppresses that default. An explicit source that
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
| `--extensions` | the current Microsoft.Extensions package set |
| `--aspnetcore` | the current ASP.NET Core package set |
| explicit `--package` values | package references or explicit local archives, preserving their spelling |
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

1. explicit package requests and archives in caller order;
2. discovered prefix package IDs in source-provided order;
3. the Microsoft.Extensions package set, when selected; and
4. the ASP.NET Core package set, when selected.

Package coordinates use case-insensitive set semantics while preserving the
first spelling and position. Repeating a coordinate explicitly or through a
package set does not authorize duplicate acquisition. A versioned coordinate
and an unversioned coordinate remain distinct.

Named package-set membership is owned by the
[Package Set Registry](package-set-registry.md). The CLI resolves its two
well-known typed identities through the front-end-only application registry
and declares each ordered member snapshot as a typed package group.
Adding, removing, or reordering members is observable
because it can change network work, source order, results, and failures; such
changes require the package-set owner's contract evidence and the
classification defined by the CLI change-classification design. Neither design
duplicates the inventory in prose.

## Command participation

Type-search `find`, `implements`, and `extensions` use this normalization to
select acquisition scope. Patternless `find --package-prefix` instead runs the
Nuspec-only profile owned by
[the package query CLI](package-query-cli.md). Its parser retains direct scope
options and whether a search group was supplied so the command can reject
incompatible API-search scope before network access. It does not apply type
search normalization or expand the prefix. `depends` uses
normalization only for type-hierarchy mode; its
package-dependency and library-reference modes are unary source operations and
do not acquire a search default.

The target
[Dependency Inspection Command](dependency-inspection-command.md) preserves
this ownership for type relationship mode while replacing the current unary
asset modes with an explicit root set. In that target, a positional type keeps
`--package`, `--library`, and `--project` as search scope; without a positional
type, those same options are dependency roots and never request a search
default.

Commands may support different direct-source options. Every supported direct
source must nevertheless contribute to the same explicitness decision. Adding
a source option without wiring its presence into default suppression is a
contract violation: an unchanged explicit request would silently acquire
additional platform sources.

That command-adapter obligation is distinct from the pure normalizer contract.
The normalizer receives a complete typed declaration; it does not prove that
every command declared all its syntax correctly. The source-free `depends`
type-to-library convenience uses `UsesImplicitPlatform`, not a second
flags-and-presence calculation.

## Implementation and gates

`SearchSourceAdapter` declares CLI intent, consumes `SearchSourceNormalizer`,
and lowers the result. `ScopeResolver` and its flags/presence input are retired
after migration of all four callers. The shared pure contract is gated by its
[public-consumer suite](search-scope-domain.md#contract-evidence).
The CLI-specific Release gates include:

- `SearchScopeResolutionTests.NoExplicitSource_UsesOnlyPlatformFrameworks`
  gates the exact empty-input result and framework order;
- `SearchScopeResolutionTests.EachGroupCombination_ResolvesExactly` gates the
  complete finite group-flag truth table;
- `SearchScopeResolutionTests.EachDirectSourceSignal_SuppressesTheDefault`
  gates meaningful package, library, platform-library, project, directory, and
  prefix declarations rather than a generic presence signal;
- `SearchScopeResolutionTests.ExplicitGroups_ComposeInOrderWithoutDuplicatePackages`
  gates additive composition, order, first-occurrence preservation, and
  case-insensitive set semantics; and
- `SearchScopeResolutionTests.VersionedAndUnversionedPackageCoordinates_RemainDistinct`
  gates coordinate identity across version presence.

Command parsers supply the source intent consumed by the shared
normalizer and lower direct values into their own option records. Their
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

`SearchSourceAdapterTests` additionally gates retained package-reference and
archive spelling, direct-source lowering, source-option identity, concrete
prefix-bound wiring and warning, direct/prefix/group precedence, retained
empty-prefix intent, visible prefix failure, clean declaration diagnostics,
and profile group rejection before type-search prefix validation.
`InvalidSourceTextUsesTheCleanCliErrorBoundary` and
`InvalidDirectSourceIdentifiesThePublicOption` gate the exact option-specific
diagnostics without framework parameter text.
`EachCommandInspectsExplicitLocalPackage` is the positive local-archive witness
through all four real command actions, using independently compiled fixtures
or an existing platform library.

The search-only measurements in
[the package query CLI](package-query-cli.md#measured-package-profile-limits)
characterize prefix expansion through 500 coordinates. They do not measure the
downstream package-archive acquisition that a type search may perform; that
cost remains with the acquisition owners and is not evidence for this bound.

The residual command-adapter matrix is explicitly unverified: the suite does
not provide one outcome-level non-vacuity case for every explicit package,
package-prefix, `--extensions`, and `--aspnetcore` path on every participating
command. The bounded-prefix adapter gate does not generalize into that full
command-by-source outcome matrix.

## Non-claims

This design does not:

- define command token grammar or optional-value disambiguation;
- define workspace identity, partitioning, or lifetime;
- define package, platform, project, local-library, or prefix acquisition;
- choose candidate or result order after source resolution;
- promise that package-set membership is stable or backward compatible;
- turn a source miss or failure into fallback; or
- define output shape, verbosity, or diagnostics.
