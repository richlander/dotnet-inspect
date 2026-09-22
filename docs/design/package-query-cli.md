# The package query CLI

How `package query` exposes the CLI half of the grep.app-style wide query over
nuget.org: where the term-matching engine lives, how a per-package fact set
becomes a printable shape, and why the browser experience in
[package-query-experience.md](package-query-experience.md) consumes the same
host-neutral query contract rather than inventing its own.

## Status

Implemented product contract with CLI adoption tracked by
[#6768](https://github.com/richlander/dotnet-inspect/issues/6768).
The earlier patternless `find --package-prefix` corpus route and its
`PackageProfileSections` rendering path landed in #4551 and are historical
inputs to this design; `package query` supersedes that command surface. See
[Sections migration: already landed, ahead of this document's sequencing](#sections-migration-already-landed-ahead-of-this-documents-sequencing)
for the retained implementation evidence.

The current sources implement one host-neutral L1 Package Query vocabulary as
`PackageQuery`: one Query Operation definition and effective route,
product-owned ordered term descriptors, complete Portable Query Intent
planning, ANDed predicate evaluation with vocabulary-owned OR families, an
explicit package-content provider for archive-derived terms, semantic answers
with structured evidence, separate candidate and match bounds, retained
Head/Tail/Window stages, visible failures, and typed completion. The route
registers the Package result grain and complete default Query Profile once.
`PackageQuery.RegisteredTerms` projects its effective terms and operators for
both hosts, including each term's acquisition tier and execution class, while
`PackageQuery.ResolveIntent` resolves every complete intent through the same
profile-scoped route.
The host-neutral `PackageQueryInspection` composition in
`DotnetInspector.Sections` is the sole enumerator of Package Query execution.
It publishes `PackageQueryEvent.Nonterminal` values through an optional
feature-owned sink, retains the terminal `Completed` summary, and materializes
one `InspectionEnvelope<PackageQueryDocument>`. The Document contains ordered
Results, typed Failures, and the terminal Summary; advisory Progress and
operational event interleaving do not become completed semantic content. This
is the Package Query adoption tracked by
[#7081](https://github.com/richlander/dotnet-inspect/issues/7081) under the
[host-observable content-kind contract](host-observable-content-kinds.md).

Package Query does not yet have a canonical Workspace packet projection, so
its current Share outcome is explicitly non-projectable rather than a
host-reconstructed URL. The Browser facade projects the same Document,
including owners, manifest facts, declared dependencies, manifest identity
provenance, and stable manifest-failure reasons, plus its Share outcome and
diagnostics through the Worker boundary. Browser state keeps that projection
even though the current UI does not yet render Share metadata.
Package-content evaluation is product-gated to at most 20 candidates.
`PackageQueryTests` is its Release gate;
`PackageQueryPlanner_IsReachableFromBrowserConsumer` is the Browser consumer
canary.

CLI and Browser now consume the same production vocabulary:
`dependencies=none|cross-prefix`, `dependency-target=all|<tfm>`,
`depends=<package-id>`, `depends-transitive=<package-id>`,
`dependency-depth=2|3|4`, `downloads=10k|100k|1m`,
`license=any|MIT|OSMF`, `readme=true`, `tool=true`, `tool-format=v1|v2`, and
`references=<simple-assembly-name>`, `skill=true`, and
`library-literal=<decoded UTF-16 text>`.
`package=<id>`, `prefix=<literal-prefix>`, and
`prerelease=stable|include` are structural terms authored by the shared input
planner rather than host-visible inspection controls. When `library-literal`
is selected, the same planner authors the contextual
`library-target=<canonical-tfm>` term from required CLI `--tfm`; that context
term is not user-selectable through `--where` or discovery. Semantic
composition is owned by
[Package Query library-literal qualification](package-query-library-literal.md).
Its Results have package grain; decoded literal occurrences remain typed
evidence within each matched package Result. The one-candidate evaluator
remains owned by
[Package Query assembly-pattern evaluation](package-query-assembly-evaluation.md).
`package query` uses [CLI execution bounds](cli-execution-bounds.md):
`--take` bounds candidate work and semantic `-n` selects final package rows.
Without explicit `--take`, a single semantic Head is delegated to the shared
query's optional `matches` bound; direct package rows also use that Head as
their candidate bound. Browser requests author the same Head stage and explicit
match bound through the same planner.

The adoption under
[#6972](https://github.com/richlander/dotnet-inspect/issues/6972) removes the
parallel opaque-facet channel. CLI `--where`, Browser presets, and Browser free
term editors all lower to `(key, operator, value)` triples in one intent.
Portable Query Intent owns serialization and generic resolution; Package Query
owns this vocabulary, binding, compatibility, bounds, acquisition tiers,
execution, evidence, and plan construction.

The Query Operation adoption under
[#7712](https://github.com/richlander/dotnet-inspect/issues/7712) removes the
remaining host-local capability inventories. CLI `-Q` and parsing consume the
effective registered inspection terms; Inspect Web presets and free-input
controls consume the same route projection. Host gestures remain distinct, but
equivalent gestures author the same canonical intent and owner-issued plan.

Related docs:

- [Package Query inspection evidence](package-query-inspection-evidence.md)
  owns typed inspection counts and bounded previews, separate from query-wide
  context. CLI and Browser consume the same semantic answers and structured
  evidence facts.
- [Package Query input selection](package-query-input-selection.md) owns the
  shared choice between exact-ID and explicit terminal-star prefix candidate
  inputs. `package query` consumes that spelling directly.
- [The package query experience](package-query-experience.md) — the browser
  front end this document is the CLI counterpart to. Its own non-goals already
  consume the same product-issued term descriptors while each host chooses how
  to present them.
- [Inspection layers](inspection-layers.md) — owns the L1/L2/L3 split this
  document places the new work into.
- [Row query and ordering design](row-query-order.md) — owns the `--where`
  row-predicate model this document reuses rather than inventing a second
  query language.
- [Output shapes](output-shapes.md) — owns the shape ladder (Document → Table
  → Vector → Scalar) and the "declared row unit" discipline a term-matched
  package row must follow.
- [Package source model](package-source-model.md) and
  [browser package sources](browser-package-sources.md) — own the source
  clients and manifest acquisition `package query` composes.
- [Progressive disclosure](progressive-disclosure.md) — owns capability-gated,
  explicit-cost package enrichment.
- [Package Query library-literal qualification](package-query-library-literal.md)
  — owns ordinary-term prequalification, selected-library evaluation, and the
  unified package-grain Result composition.
- [Inspection graph document](inspection-graph-document.md) — owns the
  relational (`graph integrations`) shape a subset of "wide query" questions
  actually need, instead of this document's flat, per-package row model.

## Package Query term binding

Every Package Query condition is a `QuerySpace.PortableQueryTerm`. The shared
planner authors
one complete `PortableQueryIntent` containing:

- exactly one population term: `package=<id>` or
  `prefix=<literal-prefix>`;
- exactly one version-policy term: `prerelease=stable|include`;
- the required `candidates` bound and optional `matches` bound;
- retained Head, Tail, or Window stages; and
- all user-selected inspection terms.

The production inspection vocabulary is:

| Term | Value | Acquisition | Execution class | Meaning |
| --- | --- | --- | --- | --- |
| `dependencies` | `none` or `cross-prefix` | nuspec | nuspec | No declarations, or at least one declaration outside the package's first dot-delimited ID segment |
| `dependency-target` | `all` or NuGet TFM | nuspec | nuspec | Scope dependency terms to every group or one compatible selected group |
| `depends` | NuGet package ID or literal prefix | nuspec | nuspec | With `eq`, a direct dependency with the exact package ID; with `starts-with`, a direct dependency whose package ID begins with the prefix in the selected dependency scope |
| `depends-transitive` | NuGet package ID | nuspec | nuspec-expensive | Source-authorized declared dependency reached at depth 2 through the selected maximum depth |
| `dependency-depth` | `2`, `3`, or `4` | nuspec | nuspec-expensive | Maximum declaration-edge depth for transitive dependency terms |
| `depends-ecosystem` | canonical ecosystem ID | nuspec | nuspec | Direct dependency belonging to the ecosystem's registered package population |
| `downloads` | `10k`, `100k`, or `1m` | search metadata | search metadata | Lifetime downloads meet the closed threshold |
| `license` | `any`, `MIT`, or `OSMF` | nuspec | nuspec | A license declaration exists, or its nuspec metadata identifies the selected license |
| `readme` | `true` | nuspec | nuspec | The manifest declares an embedded README |
| `tool` | `true` | nuspec | nuspec | The manifest declares the .NET tool package type |
| `tool-format` | `v1` or `v2` | package content | package content | Tool settings use the selected format |
| `references` | Assembly simple name | package content | metadata | At least one admitted managed `ref/` or `lib/` asset declares the requested `AssemblyRef` simple name |
| `skill` | `true` | package content | package content | The archive contains an admitted skill document |
| `library-literal` | Exact decoded UTF-16 text | package content | metadata-expensive | The selected primary implementation library contains the ordinal text in at least one decoded `ldstr` use |

Acquisition tier authorizes evidence access and enforces candidate bounds.
Execution class is the product-owned discovery and UI taxonomy for the work
performed after that evidence is available. They intentionally differ for
`references`: the term downloads a package archive but performs managed
metadata inspection. The complete class vocabulary is `search-metadata`,
`nuspec`, `nuspec-expensive`, `package-content`, `metadata`, and
`metadata-expensive`. Transitive dependency search uses `nuspec-expensive`.
`library-literal` is the first `metadata-expensive` term; call-graph or
decompiler-driven search remains future explicit work rather than behavior
implied by that class.
Execution class is descriptor metadata, not acquisition authority or predicate
meaning. Hosts may use it to lower their default candidate controls, while the
product still validates each term's concrete work bound.

All terms admit equality only except `depends`, which also admits
`starts-with`. Independent terms AND. Repeated
`tool-format` values OR within their combining family; `tool=true` is
incompatible with either specific format. Equivalent normalized bindings
collapse, including case variants of NuGet package IDs. Distinct values for
the exclusive `downloads` and `prerelease` families are incompatible.

The CLI spells inspection terms through the existing predicate grammar:

```console
dotnet-inspect package query 'Microsoft.Extensions.*' \
  --where "depends=Microsoft.Extensions.DependencyInjection" \
  --where "downloads=1m"

dotnet-inspect package query 'Polly.*' \
  --where "depends=System.Threading.Tasks.Extensions" \
  --where "dependency-target=netstandard2.0"

dotnet-inspect package query Microsoft.Extensions.Http \
  --where "depends starts-with Microsoft.Extensions." \
  --where "dependency-target=net10.0"

dotnet-inspect package query Aspire.Hosting.PostgreSQL \
  --where "depends-ecosystem=ecosystem.aspire"

dotnet-inspect package query Microsoft.Extensions.Http \
  --where "depends-transitive=Microsoft.Extensions.Primitives" \
  --where "dependency-target=net10.0" \
  --where "dependency-depth=2" --take 1

dotnet-inspect package query 'dotnet-*' \
  --where "tool-format=v1" \
  --where "tool-format=v2" \
  --take 20 -n 5

dotnet-inspect package query wix \
  --where "license=OSMF" --nuspec-only
dotnet-inspect package query Newtonsoft.Json \
  --where "license=MIT" --nuspec-only

dotnet-inspect package query Newtonsoft.Json \
  --where "library-literal=Unexpected end when reading JSON" \
  --tfm net6.0

dotnet-inspect package query 'Azure.Identity*' \
  --where "downloads=1m" \
  --where "library-literal=DefaultAzureCredential" \
  --tfm net8.0 --take 5
```

The old `facet=<opaque-id>` spelling is rejected; it is not retained as an
alias. `-Q Packages` and `Query: Packages` expose the product term keys,
closed values, value kinds, and examples without acquisition. The CLI does not
define a parallel vocabulary or infer terms from labels or evidence text.

One request ordinarily admits at most 22 authored inspection terms. Package
Query reserves the other two slots in Portable Query's 24-term payload limit
for its required population and prerelease terms. A request containing
`library-literal` admits at most 21 authored inspection terms because the
Product planner adds the required contextual `library-target` term. The count
is charged before duplicate collapse, matching the canonical codec; both CLI
and Browser receive the same typed planning rejection when the complete intent
would exceed the bound.

`library-literal` preserves its exact decoded UTF-16 value, including leading
and trailing whitespace and embedded newlines. It accepts 1 through 1,024
UTF-16 code units and has no regex, glob, or byte-pattern interpretation.
The term requires `--tfm`; CLI validates the exact target and passes it to the
Product planner, which canonicalizes it and authors `library-target`. Users
cannot submit `library-target` through `--where`.

Ordinary terms AND-compose with `library-literal` and prequalify candidates
before the existing selected-primary-implementation-library evaluator runs.
The combined request has package-content acquisition,
`metadata-expensive` execution, and a five-candidate maximum. Only final
semantic matches enter `PackageQueryDocument.Results`; complete occurrences,
selected-library context, exact Root reopening, assessments, failures, and
Summary accounting remain typed content in the same Document.

`depends=<package-id>` uses NuGet package-ID comparison semantics and matches
one exact direct dependency. Without `dependency-target`, or with
`dependency-target=all`, dependency predicates inspect every nuspec group.
`all` is Package Query scope rather than a target-framework identity and is
distinct from a manifest's real `any` group.

`depends starts-with <prefix>` uses the Source Selection owner's literal
`PackagePrefixDeclaration` validation and ordinal case-insensitive matching.
The operand contains no wildcard. A trailing `.` is the recommended spelling
for a dot-delimited package family: `Microsoft.Extensions.` excludes
`Microsoft.ExtensionsX`, while `Microsoft.Extensions` intentionally matches
both. Repeated prefix terms are independent conjunctions, so each prefix must
match at least one direct dependency in the selected scope. Evidence counts
matching declaration occurrences and retains bounded `group: ID range`
previews. The term does not resolve ranges or traverse dependencies.

`depends-ecosystem` accepts one canonical, case-sensitive
`ecosystem.<name>` identity and matches a direct dependency against the
Ecosystems owner's resource-free package-population declaration. Membership is
the union of exact package-set members and registered package-ID prefixes,
using NuGet package-ID comparison semantics; an exact registration takes
evidence precedence when both rules match. Repeated ecosystem terms are
independent conjunctions, so each named ecosystem must match at least one
direct dependency in the selected scope.

The CLI and Browser package-query facades inject the same immutable
`PackageQueryEcosystemMembershipCatalog` projected from the application
ecosystem catalog. A malformed identity, a valid but unknown identity, or a
known ecosystem without exact-package or package-prefix membership is rejected
before package-source work. The shared portable intent stores only the
canonical term value, so Workspace Share packet formats do not change; a
decoded plan must bind the application snapshot before execution.

`dependency-target=<tfm>` canonicalizes the requested NuGet target and uses
the dependency-group owner's compatible selection. The plan and evidence
retain the requested target and selected manifest group separately. A selected
empty group and a manifest with no dependency groups satisfy
`dependencies=none`; no matching target framework does not. The target term
requires at least one `depends`, `depends-ecosystem`, `depends-transitive`, or
`dependencies` term, applies to all such terms in the query, and does not
traverse, resolve version ranges, or select package assets.

`depends-transitive=<package-id>` is distinct from `depends`: it matches only
resolved declaration edges at depth 2 through the explicit
`dependency-depth=2|3|4` boundary, so a direct-only dependency does not match.
The term requires one explicit `dependency-target=<tfm>`; `all` is rejected
because traversal needs one framework-selection policy for every manifest.
The root and every traversed manifest use the dependency-group owner's
compatible selection for that requested target. Candidate resolution uses the
existing source-authorized declared-range query and therefore does not claim
NuGet restore, lock-file, or asset-selection equivalence.

One transitive query admits at most five package candidates. Within each
candidate it admits at most 32 acquired manifest projections and 128
declaration resolutions. Repeated transitive terms AND and share one traversal
of that candidate. A candidate-resolution, manifest-acquisition, projection,
or work-budget failure anywhere inside the requested depth makes that
candidate a visible dependency-traversal failure; partial evidence never
becomes a semantic match or non-match. Reaching the explicit depth boundary is
successful because every edge through that boundary is known without
acquiring endpoint manifests.

Transitive evidence counts matching admitted declaration edges. Each preview
is one deterministic shortest root path constructed from the declared version
ranges and resolved exact package coordinates for that edge. The shared
160-character `InertString` display budget applies after construction, so a
shortened preview may omit or truncate later path text and must not be treated
as a complete path record or package coordinate. This avoids unbounded
enumeration when shared nodes or cycles provide multiple paths.
`Microsoft.Extensions.Http@10.0.0` on `net10.0` is the motivating real package:
it reaches `Microsoft.Extensions.Primitives@10.0.0` at depth 2 through
`Microsoft.Extensions.Configuration.Abstractions@10.0.0`, while that
intermediate direct dependency does not itself satisfy `depends-transitive`.

`dependencies=cross-prefix` derives each package's comparison segment from the
text before its first dot, or from the complete ID when no dot is present. It
matches when the selected dependency scope contains at least one direct
declaration whose segment differs under case-insensitive NuGet package-ID
comparison. Evidence retains the complete matching declaration count and
bounded previews of manifest group, dependency ID, and declared range. The term
does not use the query population prefix, traverse dependencies, resolve
version ranges, inspect owners, or acquire package content. `Azure.Core` is a
motivating real package: its `Azure.*` declarations remain inside the segment
while `Microsoft.*` declarations are cross-prefix.

`references=<simple-assembly-name>` uses ordinal case-insensitive equality over
the `AssemblyRef` simple names declared by every admitted managed `ref/` and
`lib/` assembly in every package target-framework group. It does not resolve
the reference, compare version, culture, or public-key token, traverse a
dependency graph, or inspect method bodies. Product evidence counts every
matching asset/reference occurrence and previews at most three values naming
target framework, archive path, and the observed reference spelling.
Framework-group admission uses NuGet framework-folder identity, including
legacy groups such as `portable-*`, rather than the compile selector's
compatibility policy.
Incomplete metadata in any admitted library makes that candidate a visible
package-content evaluation failure because neither a complete count nor a
truthful negative answer remains available.

The assembly-reference inventory admits at most 256 library assets and 16,384
total `AssemblyRef` rows per package. Each library entry is bounded to 16 MiB,
and total library-image bytes are bounded to 32 MiB per package. Exceeding any
bound is a visible package-content evaluation failure, never a partial match.
`Microsoft.Extensions.Http@10.0.0` is the motivating real package: its
`net10.0` and `net462` library assemblies both reference
`Microsoft.Extensions.DependencyInjection.Abstractions` while exposing
different surrounding reference sets.

`depends starts-with` is an absolute literal-prefix query and remains distinct
from the candidate-relative `dependencies=cross-prefix` classification.

## Adaptive result section

Package Query settles package population before choosing its default rendered
section. Its host-neutral `PackageQueryDocument` always retains Results,
Failures, Summary, and the derived `HasPackages` decision. CLI and Browser
consume that same document; a host must not reconstruct the decision from
display text.

Without `-S`, the CLI selects exactly one section after execution:

- `Packages` when `PackageQueryDocument.HasPackages` is true;
- `Query Summary` otherwise.

The selection happens before format lowering, so Markdown, table, TSV, JSONL,
and projected JSON make the same choice. `Query Summary` is one row with
independent `Candidates`, `Matches`, `Evaluation Failures`, and `Status`
columns. Predicate false is a nonmatch, not an evaluation failure. Thus a
missing exact package reports zero candidates, while an existing package
rejected by a predicate reports one candidate and zero matches.

Explicit selection is non-adaptive. Bare `-S` requests the command's default
`Packages` preset regardless of the settled result. `-S Packages` retains the
package schema even when it has no rows, and `-S "Query Summary"` retains the
settlement row even when packages matched. `@Query` composes both sections.
Markdown and JSON render that composition in alphabetical order, with
`Packages` first; table, TSV, and JSONL keep their existing one-section
requirement.

`--count` remains a semantic reduction over matched package rows rather than a
count of whichever adaptive section rendered. It therefore supports the
`Packages` section only.

Matching dependency evidence identifies each declaration's manifest group,
package ID, and declared range, subject to the bounded evidence preview.
Ecosystem dependency evidence additionally identifies the canonical ecosystem
and whether an exact package or package prefix established membership.

`license=any` matches any recognized nuspec `<license>` declaration or legacy
`<licenseUrl>`. Named values are a closed product vocabulary, not arbitrary
filenames. `MIT` matches the exact SPDX expression `MIT`; `OSMF` matches a
case-insensitive declared file basename of `OSMFEULA.*`. Both decisions use
nuspec metadata only. Package Query never opens or reads the declared file to
identify the license. The raw declaration remains provenance and supplies the
separate package-file inventory; explicit `--print` on that section may read
the selected document in the same way as README and SKILL projections.

Selecting `tool-format`, `references`, or `skill` explicitly authorizes archive
acquisition.
Such a query defaults the candidate budget to 20 and cannot bypass the
20-candidate ceiling. `--nuspec-only` rejects it before acquisition.
`downloads` is evaluated from source search metadata and does not force a
manifest request. Nuspec terms acquire manifests but no package archive;
manifest predicates run before archive acquisition. CLI `-Q` projects the
execution class beside every Package Query facet; it does not infer that class
from acquisition behavior.

`PackageQueryTests` gates vocabulary shape, complete intent retention,
resolution, dependency-target default and canonical binding, compatible group
selection, selected-empty/no-groups/no-match behavior, evidence,
candidate/match completion, assembly-reference matching and malformed-image
failure against the pinned `Microsoft.Extensions.Http@10.0.0` assets, and the
search-metadata/no-manifest boundary. Its license cases gate `any`, exact SPDX
identity, the `OSMFEULA.*` nuspec filename pattern, and zero package-content
acquisition. `PackageQueryCliTests` gates
discovery, term spelling, Head/Count behavior, acquisition authorization, and
output parity.

## CLI term binding

The CLI adapter lowers every selection to the product-owned bounded
`PackageQuery` plan and renders returned evidence rather than recomputing it.
The shared engine owns predicate meaning, compatible alternatives, acquisition
tiers, semantic composition, and completion. Browser is the second consumer of
the same descriptors and planner.

The CLI provider consumes the package owner's configured-authority exact-pin
acquisition and authority-scoped filesystem store. It acquires the selected
archive itself, without tool-wrapper redirection. Its fixed nuget.org source
does not borrow the Browser's legacy identity association. The existing
[HTTP authority storage policy](package-source-model.md#caller-pinned-payload-acquisition)
applies: temporary materialization remains owned through query completion,
then is cleaned up; legacy persistent payload caches are not reused.

`--take` lowers to the query's ordered package-candidate work dimension: one
aggregate prefix stream in the fixed Gallery source's effective order, not a
per-source allowance. Candidate work includes nonmatches and failed candidates
because each admitted candidate may require exact-manifest or package-content
evaluation before its match status is known. The manifest-only default remains
200 candidates and an explicit value may raise it to 1,000. Package-content
queries default to and reject values above 20; a `metadata-expensive`
`library-literal` query instead defaults to and rejects values above five.
Reaching either default or explicit candidate bound remains visible bounded
incompleteness; the integer is not a matched-row count.

`-n` is semantic Head over final matched-package rows. When no explicit
`--take` is present and the row plan is one Head operation, the CLI pushes that
head into execution. A direct metadata row path uses N as both the candidate
and match bound, so `package query 'Foo*' -n 2` has the effective work shape
`--take 2 -n 2`. An inspection-term-filtered path retains its default candidate ceiling
and stops after finding N matches. Failures encountered before the Nth match
remain visible; later candidates are outside the requested Head evaluation.
Reaching this derived match bound is successful completion of the requested
Head and does not produce an incompleteness warning.

Pushdown is an optimization within the query engine's 1,000-item work and
match ceilings, not a restriction on semantic Head. A larger `-n` remains
valid: direct prefix queries infer the maximum 1,000-candidate work bound,
filtered queries retain their normal candidate ceiling, and the final Head is
applied after execution with ordinary incompleteness disclosure.

An explicit `--take` disables this pushdown. `--take 100 -n 2` evaluates the
authorized population of up to 100 candidates, including its failures and
intermediate query work, before L2 selects two rows. Tail, open-ended windows,
aggregation, and global ordering likewise require their bounded input before
selection and cannot infer `--take N` from their final row count.

When `-n` is absent, the semantic row-selection plan is empty and every match
from the authorized candidate population reaches row shaping. The historical
implicit 100-match default retires; it is neither an operational ceiling nor a
user-authored semantic selection. `-n` inherits the row grammar's positive
integer range rather than the old 1,000 match-budget maximum.

`package query` always selects Package Query. Its positional input is either
an exact package ID or one terminal-star package-ID prefix. It rejects API
scopes and source overrides before acquisition. Patternless
`find --package-prefix` and `package search` are removed rather than retained
as aliases; `find PATTERN --package-prefix PREFIX` remains API search.

One semantic result row is one matched package, carrying its exact version,
source, semantic answers, and structured evidence. `-n` and `--rows` select those
rows before projection and Count. `--count` composes with `-n`: finding N
ordered matches can witness exact `Head(N) -> Count` while candidate-bound
incompleteness remains visible. Fewer than N matches at a reached candidate
bound is not exact, and without `-n`, Count requires completion evidence for
the candidate population. Partial failures block successful Count, empty
exhausted success remains zero, and cancellation remains a failed operation
rather than empty success.

The low-compatibility migration removes `--candidates` and `--matches`; they do
not remain aliases or retirement shims. The shared query engine may retain its
host-neutral match-budget capability. The CLI does not expose that budget as
another option; it infers one only from a lone semantic Head when no explicit
`--take` is present.

The owner-issued query request, accepted plan, execution state, and summary
therefore carry the same optional match budget end to end. Planning preserves
presence or absence exactly; it does not replace absence with a default,
candidate count, or sentinel. A present positive budget retains the current
product behavior, including `MatchLimitReached`; existing Browser and other
callers continue to supply their current explicit or default budget. An
explicitly absent budget means that matching rows do not stop candidate
execution. Validation applies the existing match-budget range only when a
budget is present. Completion cannot be `MatchLimitReached` when it is absent,
and the typed summary carries no match-limit denominator. Presentation reports
the observed match count without manufacturing an infinite or candidate-equal
ceiling.

The CLI requests an absent match budget when `-n` is absent or explicit
`--take` fixes the candidate population. A lone semantic Head supplies its N
as the match budget; a direct prefix-metadata path also uses N as its candidate
budget. The semantic row intent still reaches L2 after execution as a
backstop. This optional state is required rather than a sentinel: with
`--take 1000`, all 1,000 candidates may match, so no larger valid integer
exists under the owner's 1,000 match-budget maximum.

The CLI binding is gated by `PackageQueryCliTests`:
`DiscoveryValues_ExposeTheProductTermVocabulary`,
`ProductPlanner_OwnsCompatibilityAndDuplicateCollapse`, and
`InvalidCandidateBudgets_AreRejected` gate binding and admission;
`SemanticHeadRunsAfterAllCandidatesAndKeepsOnePackagePerRow` and
`OutputModes_UseTheSameWindowedMatches` gate semantic row shape;
`ContentProvider_UsesAdmittedArchiveAndDisposesTransport` exercises the
production provider over an admitted archive and a rejected archive;
`ReferencesTerm_ExecutesThroughTheCliContentProvider` gates assembly-reference
matching and evidence through that same CLI provider;
`ContentProvider_RetainsAuthorityStorageThroughUseAndThenCleansIt` gates the
temporary storage lifetime;
`PartialManifestFailure_RetainsMatchesAndNonzeroExit`,
`EmptySuccessAndSearchFailureRemainDistinct`, and
`CancellationDoesNotBecomeAnEmptySuccess` gate failure disclosure.
`QueryDiscoveryTests` and focused command-routing cases gate discovery,
retirement diagnostics, and the neighboring patterned Find contract.

## Thesis

`package query` is the package-row CLI verb. It consumes the host-neutral L1
term engine to ask whether an exact package or packages under a literal prefix
satisfy selected product-owned facts available from source metadata, exact
manifests, an explicitly supplied package archive, or the selected primary
implementation library. `find` remains the type/member/API verb; its package
prefix option only scopes a patterned API search. Decoded string-literal
qualification is the `library-literal` Package Query term; bounded
`AssemblyRef` metadata remains the separate `references` package-content term.
This document defines where those pieces belong across the existing L1/L2/L3
split, rather than treating the CLI project as a place to accumulate new
bespoke logic the way it did before that split existed.

## Is this CLI-side or core?

### Exact and prefix source input

Package Query accepts an exact package ID or one explicit terminal-star
package-ID prefix. Exact input uses authoritative version and listing evidence
without falling back to related search results. Prefix input uses the supported
V3 Search page stream, preserves source order and truncation, and can emit
search metadata without forcing manifest or archive acquisition.

Selecting an inspection term authorizes its acquisition/evaluation tier;
package-content terms retain their explicit provider requirement and
20-candidate ceiling. Match limits, candidate limits, source page limits,
failures, and cancellation retain the visible query-event contract. The
retired Gallery browse/order substrate is not a CLI adoption path.

### Existing layering

Core. Concretely:

- **L1 — `DotnetInspector.Queries`.** The term-matching engine belongs here,
  next to `PackageProfileQuery` and `PackageDependencyGroupsQuery`, which
  #4551 places in this layer rather than in the CLI project. A typed
  query that evaluates nuspec-tier terms over a streamed manifest and
  package-content terms through an explicit host capability returns typed
  results and chooses no renderer — the existing L1 contract.
  The existing assembly-semantic query remains a composed evaluator beneath
  Package Query's `library-literal` term; its separate Find-era public route
  does not extend the CLI surface.
  This is what makes the term engine reachable from a second consumer (the
  browser/Wasm engine) without re-deriving it, the exact failure mode
  [inspection-layers.md](inspection-layers.md) exists to prevent.
- **L2 — `Sections` (currently `src/DotnetInspect.Cli/Sections`).** Row
  declaration, `--where` predicate evaluation, and the shape-ladder
  projection into a Table belong here.
  [inspection-layers.md](inspection-layers.md) already places row predicates
  at L2 ("row query — field predicates within a section... L2."), pointing
  at [row-query-order.md](row-query-order.md) for the model; nothing about
  package rows changes that. This is also where the tier capability gate is
  enforced — see [Tier gating](#tier-gating) below — since
  L2 is where a request is checked against what the selected section actually
  offers before L1 is asked to compute anything.
- **L3 — `dotnet-inspect` (`package query`).** Argument parsing,
  exact/prefix input lowering, `--where`/capability wiring, row selection, and
  output-format selection only. L3 does not compute facts and does not decide
  what a term costs — the same rule that already governs every other command.

  Unprojected `--json` serializes the complete owner-issued
  `PackageQueryDocument`; `--envelope` exposes the same Content inside the
  complete service value with result kind `package-query`. Query-planning
  controls such as `--where`, `--take`, and `--tfm` remain admitted service
  inputs. Row selection, Count, discovery, section selection, projections, and
  competing formats request post-service shaping and are incompatible with
  `--envelope`. Typed failed or incomplete Documents are serialized before the
  command returns nonzero.

## Is there a reason to start by changing `find`'s layering?

Not for the corpus-fetch mechanism — that part is correctly designed and
merged. #4551 puts `PackageProfileQuery` and `PackageDependencyGroupsQuery` in
L1, and makes the row-declaration call correctly: as its README addition
states, "`-t` limits packages rather than flattened dependency rows." That is
the same "declared row unit, not rendered row count" discipline
[output-shapes.md](output-shapes.md) requires of call-graph edges, applied
correctly to package rows a version early.

### Sections migration: already landed, ahead of this document's sequencing

This document originally identified a real, narrower gap and recommended
closing it as a preparatory slice before adding term predicates: routing
`find --package-prefix`'s rendering path through the shared Sections registry
the way `library`, `member`, and `package` already are (see
[section-model.md](section-model.md), "first made coherent for the library
command and then adopted by the package command"), rather than leaving it as
bespoke CLI-side code.

**That migration already happened, inside #4551 itself, rather than as a
follow-up slice.** `find --package-prefix` is built directly on
`PackageProfileSections` and `SectionPipeline<PackageProfileView>` — there was
no intermediate bespoke formatter to migrate away from. Concretely, on `main`
today: `--count`, `--rows`, `-D`/`--discover` (with section cost annotations
and category maps), and the JSON/TSV/JSONL/projected-JSON output formats all
route through the shared pipeline, the same infrastructure `library`/`member`/
`package` use.

**What did not land alongside it before #6489:** the flag-numbering half of this
recommendation. This document's own "one deliberate, called-out behavior
change" for this migration step was retiring `-t`-as-package-limit in favor of
the historical #4677 `-n` proposal — but before #6489,
`find --package-prefix`'s corpus limit remained `-t` (`FindOptions.Limit`,
validated as "`-t` must be between 1 and..."). #6107 added `-S Packages` and
the finite `--where` term binding without replacing that ordinary profile
mode.

Under that legacy spelling, numeric `-t` clamps the package candidates
the source is asked to return and is mutually exclusive with `--count`.
Accepting both would present a count over an intentionally shortened
acquisition as though no package clamp applied. This package-source rule does
not define how `--count` composes with L2 row windows.

The CLI requests 500 package manifests by default and accepts an explicit
`-t` value up to 1,000. The host-neutral query retains its separate 10,000
input-safety ceiling because non-Gallery sources may have different paging
contracts. The CLI maximum is the largest measured request that completes
within the Gallery source's default 120-second operation deadline on both
measured hosts.
`FindCommandIntegrationTests.PackageProfileLimits_UseMeasuredDefaultAndMaximum`
gates the declared values, while the invalid-input tests gate the maximum at
the command boundary.
`SearchScopeResolutionTests.PackageProfileGuidance_DisclosesDefaultAndMaximum`
gates their user-facing disclosure.
`PackageProfileQueryTests.ExecuteAsync_ForwardsSharedOperationContext` gates
that search and every manifest request consume one host-supplied
`NuGetOperationContext`.
`FindCommandTests.PackageProfileCatalog_MaterializesOnceAndForwardsOperationContext`
gates the L2 catalog handoff. The CLI creates that context from the same
`NuGetFetchOptions` used to create the Gallery source, so the configured
operation deadline spans the complete profile under the
[package-source operation-context contract](package-source-model.md#shared-operation-context-and-payload-lifetime).

### Measured package-profile limits

The 500 default and 1,000 maximum are based on a search-only and Nuspec-only
measurement at exact repository head
`dade58411dff4ae4d1746505a5764480f298af86` with .NET SDK
`11.0.100-preview.7.26381.103` on 2026-09-02. The pinned query was the stable
package-ID prefix `Microsoft.`. The search-only pass called
`IPackageSourceClient.SearchByPrefixAsync`; the profile pass called
`PackageProfileQuery.ExecuteAsync`, consuming search metadata and exact
manifests without downloading package archives or opening assemblies.
`tools/PackagePrefixBenchmark.cs` preserves the product-backed probe:

```bash
dotnet run tools/PackagePrefixBenchmark.cs -- \
  search Microsoft. 100,500,1000,5000 3
dotnet run tools/PackagePrefixBenchmark.cs -- \
  profile Microsoft. 100,500,1000,5000 1
```

The local host was an Apple M4 Mac with 10 logical CPUs and 24 GiB of memory.
The second host, `merritt`, was a Ryzen 9 9900X Linux machine with 24 logical
CPUs and 60 GiB of memory.

| Requested packages | Search only, M4 Mac | Search only, Ryzen 9 9900X | Nuspec profile, M4 Mac | Nuspec profile, Ryzen 9 9900X |
| ---: | ---: | ---: | ---: | ---: |
| 100 | 0.18 s | 0.15 s | 4.65 s | 3.18 s |
| 500 | 0.72 s | 0.73 s | 36.89 s | 28.02 s |
| 1,000 | 1.56 s | 1.58 s | 70.51 s | 49.81 s |
| 5,000 requested | 4.23 s | 4.19 s | 284.76 s | 284.23 s |

Search-only values are medians of three warm-process passes. Nuspec-profile
values are one pass because the largest case makes thousands of exact manifest
requests. The two profile passes ran concurrently from separate networks, so
the values characterize observed end-to-end service latency rather than
isolated CPU throughput. The probe used a 30-minute operation ceiling so the
source boundary could be measured; the CLI default is 120 seconds. The 5,000
request did not produce 5,000 candidates. NuGet Gallery's
[Search Query Service](https://learn.microsoft.com/nuget/api/search-query-service-resource)
permits `skip` values only through 3,000 and `take` values only through 1,000.
Its response contains `totalHits` and `data`, not a continuation cursor or
next-page link that can cross that offset boundary.

The current prefix client requests fixed 100-row pages. In this measurement it
therefore examined the ranked search rows at offsets 0 through 3,000, at most
3,100 raw rows, before returning `SourcePageLimit`. The Gallery query is
broader ranked text search; the client then applies exact case-insensitive
prefix matching and package-ID deduplication. Those steps yielded 2,933
accepted `Microsoft.` package IDs. That number is neither the source's maximum
package count nor the maximum legal offset.

A client could use the documented maximum `take` on the final legal offset and
inspect up to the first 4,000 ranked rows, but it still could not request the
next offset. The current fixed-page path leaves that final-page capacity
unused. Exhaustive enumeration beyond the bounded Search window requires a
different source mechanism, such as a maintained view over the append-only
NuGet Catalog; it is not another Search page. The measured 500 default and
1,000 CLI maximum remain below this distinction and continue to be selected
from end-to-end latency and operation-deadline evidence rather than from the
largest theoretically reachable search window.

The profile issued 2,964 HTTP requests, produced 2,931 matches and two visible
manifest failures, and retained `SourcePageLimit` truncation. CPU time remained
below 1.3 seconds in every profile run, so wall time was network-bound rather
than compute-bound.

The measurements make 1,000 a poor implicit default: even the cheapest
end-to-end profile takes 50 to 71 seconds. Five hundred is materially broader
than the historical 100 while remaining below 40 seconds on both measured
hosts. One thousand is the explicit maximum because its 50-to-71-second result
fits the default operation deadline on both hosts. The 2,933-candidate source
boundary took about 284 seconds, so neither that boundary nor the requested
5,000 and host-neutral 10,000 ceilings are behavior-safe CLI limits under the
default timeout policy.

**Resolved interaction for #6489:** the Sections migration and
the `-t` retirement were assumed to be one atomic step; in practice they
decoupled, and the migration landed first. The focused
[CLI row-selection](cli-row-selection.md) and
[execution-bound](cli-execution-bounds.md) owners now require independent
semantic `-n` and candidate-work `--take` intent.
[#6547](https://github.com/richlander/dotnet-inspect/issues/6547) records this
owner's adoption decisions so command-wide implementation does not silently
inherit `-t`, `--candidates`, or `--matches`.

### `-t` is the wrong flag to build on; semantic rows use `-n`

The numeric `-t` on `find --package-prefix` reuses `find`'s own pre-existing
`-t`, whose description #4551 widens from "Limit type count (`-t 5`) or
filter by glob (`-t *Json*`)" to "Limit result count... or filter API types
by glob." That reuse is real and merged, and it is not a precedent this
document should build a new predicate/limit flag on: `-t` already means a
type name or glob filter everywhere else in the CLI (`library`, `type`,
`member`, `package -S "SourceLink: Files"`) — a
different noun than "how many rows" — and `find` only overloads it as
count-or-glob because `find`'s own type search predates a dedicated
row-count flag.

Working through this surfaced a repository-wide flag-numbering problem:
rendered-line `-n`, count-form `--rows`, ranked `--top`, and command-owned
`-t`/`-m`/`--take` counts all answered adjacent "how many" questions.
The focused #4677 designs now require:

- `-n`/bare `-N` is the universal first/last item count;
- `--rows` carries only absolute row ranges;
- explicit `--lines` owns rendered-line limits;
- command-specific result counts and short `-t`/`-m` selectors retire; and
- `--top N --order-by <field>` remains the ranked form.

A corpus-match query that names a ranking field (for example, "top 500 by
download count") uses `--top 500 --order-by "DownloadCount desc"`; a plain
"first 500 that match" uses `-n 500`.

The Sections-registry migration was the intended moment to apply that
grammar, but it landed without that part: `find --package-prefix` rows are now
declared sections, yet the corpus limit is still `-t`. See
[Sections migration: already landed, ahead of this document's sequencing](#sections-migration-already-landed-ahead-of-this-documents-sequencing)
for the resulting follow-up.

## Current Package Query tiers

L1 owns the finite term vocabulary and predicate semantics; front ends submit
product-issued keys and values and do not reconstruct those predicates:

- **`search-metadata` tier.** `downloads` consumes the source's search result
  metadata. A prefix request containing only this tier skips manifest
  acquisition. Exact package selection retains authoritative exact-version
  resolution while avoiding an unnecessary manifest request.
- **`nuspec` tier.** `dependencies`, `depends`, `readme`, and `tool` consume
  exact manifest facts. The broad `tool=true` predicate stops at the declared
  package type; it does not open the archive merely to classify tool settings.
- **`package-content` tier.** `tool-format`, `references`, and `skill` require
  an explicit `IPackageQueryContentProvider` and accept at most 20 candidates.
  `PackageQuery` applies all cheaper predicates first. Tool v1 and v2 are
  combining members, so selecting both returns either recognized settings
  format with evidence identifying the observed version.
- **`metadata-expensive` execution.** `library-literal` acquires package
  content, requires one exact planner-authored `library-target`, and admits at
  most five candidates. Package Query applies ordinary terms first, then
  invokes the existing selected-primary-implementation-library evaluator only
  for prequalified candidates.

CLI and Browser invoke the same L1 definitions. The CLI applies semantic row
selection to
`PackageQueryDocument.Results`, renders `PackageQueryDocument.Failures` as
visible diagnostics, and uses `PackageQueryDocument.Summary` for exact-count
and exit-status decisions. It does not reconstruct the Document from streamed
events.

### Tier gating

The current L1 planner rejects package-content requests above 20 candidates,
and execution rejects a package-content plan unless its host supplies the
explicit content-provider capability. Selecting a package-content term is the
explicit cost gesture in both Browser and CLI. Each host lowers its candidate
bound from 200 to 20 before dispatch; the CLI additionally offers
`--nuspec-only` to reject such a plan before acquisition.

`library-literal` is an ordinary `package query --where` term with
package-content acquisition and `metadata-expensive` execution. It does not
use a `--deepen` flag or a separate command mode. The Product planner requires
CLI `--tfm`, authors contextual `library-target`, enforces the five-candidate
ceiling, and composes the existing occurrence-oriented
[assembly-semantic evaluator query](find-assembly-semantic-query.md) beneath
the unified Package Query operation.

## Retired assembly-semantic command route

This document originally recorded `find --literal` as a promoted Package Query
tier, then temporarily assigned assembly-semantic presentation to Find, and
later exposed `package query --library-literal` with a separate result kind.
Those routes are retired. Find returns Type Results, while Package Query now
binds decoded literal qualification through
`--where "library-literal=..." --tfm TFM`.

The package-grain composition, Count, exact Root reopening, complete occurrence
evidence, and bounded exact-ID or prefix population are specified by
[Package Query library-literal
qualification](package-query-library-literal.md). The reusable
occurrence-oriented evaluator contract remains in
[Find assembly-semantic query](find-assembly-semantic-query.md), and the
one-candidate selected-asset and producer contract remains in
[Package Query assembly-pattern
evaluation](package-query-assembly-evaluation.md).

## Row declaration: keeping package evidence out of the Table

A term-matched package is not naturally one flat row: it may match zero or
more terms, each with its own answer and evidence, and evaluating a capability-bearing
term may add fields a nuspec-only row never had. Before this can be a Table,
something has to decide the row grain — the same "declared row unit"
decision #4551 already makes once for package/dependency pairs. This
document specifies:

- **Default grain: one row per package.** Multiple matched terms produce an
  ordered semantic `Answer` vector and separate structured `Evidence`. The
  human-readable and projected package row contains `Package`, `Version`,
  `Tier`, `Source`, and `Answer`. Evidence is explanatory structured content
  rather than a tabular field, so it remains on each `PackageQueryMatch` in
  unprojected JSON and the `InspectionEnvelope<PackageQueryDocument>` instead
  of being flattened into an `Evidence` column. The query layer retrieves
  values, facts, and counts; hosts render any explanation from those typed
  values without re-deriving semantic identity.
- **Literal strings are an explicit evidence projection.** For a
  `library-literal` query, `-S "Literal Strings"` renders one one-column row for
  each distinct full retained matching literal across the selected package
  Results. Exact duplicate values collapse in first-occurrence order. The
  projection does not extract the operand or URL-shaped substrings and does not
  change package-grain `-n`, `--rows`, or `--count` semantics.
- **Denormalization is a per-term decision, not a generic mechanism.** A
  term whose answer is inherently per-sub-item (for example, "which of this
  package's target frameworks are out of support" when a package targets
  several) may choose to emit one row per package × sub-item, the same
  explicit choice #4551 already makes for package × dependency.
  Markout does not decide this cardinality — the producer does, same as a
  call-graph producer decides edges, not nodes, are the row.
- **Relational questions are out of scope for this row model.** "Which
  integrations does `Microsoft.Extensions.*` expose" is not a flat predicate
  match; it is a relationship between a package (or its types) and the
  capability it exposes. That question already has an owner:
  [inspection-graph-document.md](inspection-graph-document.md)'s node/group/
  logical-edge model, surfaced today by `graph integrations`. Extending that
  command's seed to a package-prefix scope is a separate, smaller piece of
  work than anything in this document, and it should not be reimplemented as
  a flat term-match row.

## Completion and bound honesty parity with the browser

The Package Query event contract reports truncation
("Package discovery reached the requested package limit" /
"Package discovery was truncated by a pagination limit; narrow the prefix.")
and visible per-source failures. That is the same completion vocabulary
[package-query-experience.md](package-query-experience.md) settled on
(bounded / exhausted / failed / cancelled, partial failures rendered
alongside already-streamed rows) after fifteen rounds of adversarial review —
the CLI should not reinvent that wording, and the browser experience should
not need to translate a differently-shaped CLI completion signal.

Package Query composes candidate admission and semantic selection in this
order:

- **Nuspec-tier `--where`** evaluates every candidate admitted by `--take` or
  the default candidate ceiling, then `-n` selects matched-package rows. For
  example, `--take 500 -n 20` means "inspect at most 500 candidates, then keep
  the first 20 matches," not "inspect 20 candidates." If only seven match
  before the candidate bound is reached, the command returns seven and
  preserves bounded incompleteness.
- **`library-literal` with ordinary terms** admits at most five candidates,
  evaluates ordinary terms first, and runs selected-library semantic work only
  for those prequalified matches. Semantic `-n` then selects final package
  Results. A candidate that fails an ordinary term does not become a semantic
  `NoMatch`, and a semantic survivor does not become a published match until
  its selected primary implementation library contains the literal.

The implementation must preserve the orderings: candidate admission precedes
term evaluation, nuspec predicates precede semantic `-n`, the package-content
candidate cap applies before archive evaluation with manifest prefilters
running before acquisition. Help text and rendered completion state must name
the candidate bound, and each asserted ordering must name its enforcing gate.

The current `PackageQuery.ExecuteAsync` product contract supports
separate `MaximumCandidates` and `MaximumMatches` and stops before later
candidate acquisition when the match budget is reached.
`ExecuteAsync_FiltersBeforeMatchLimitAndStopsManifestAcquisition` and
`ExecuteAsync_ExactExhaustionAtMatchLimitIsConservative` gate that shared
behavior. The CLI supplies that budget for a lone semantic Head only when no
explicit `--take` fixes a larger candidate population. A
`MatchLimitReached` completion produced by this lowering is successful
completion of the requested Head and does not warn that the package-ID scope
was not exhausted. Existing Browser callers retain their current match-stop
contract.

The command-wide implementation adds Release gates for direct-row Head
pushdown, filtered match-stop behavior, all matches returned when `-n` is
absent, sparse matches at the candidate bound, independent `--take`/`-n`
variation, Count witness and Count-insufficient cases, mode-neutral
`--take`/`-n`, package-content's 20-candidate boundary, and output-format
parity.
`NoMatchBudget_AllCandidatesMatchingPreservesTerminalCompletion` must exercise
1,000 matching candidates and observe candidate/source completion rather than
`MatchLimitReached`.
`Plan_PreservesAbsentMatchBudget` must prove that request absence reaches the
accepted plan and summary unchanged without defaulting or sentinel conversion.
`SemanticHeadWithoutTake_BoundsDirectRowsAndMatches` must prove the direct
one-row-per-candidate lowering, while
`ExplicitTake_PreventsSemanticHeadPushdown` must preserve the full explicit
candidate population.
`PresentMatchBudget_PreservesExistingStopBehavior` must retain the current
product behavior for Browser and other budgeted callers, including the
numeric summary denominator and completion mapping. Existing Package Query gates continue to own the shared product match-budget
behavior; CLI gates do not redefine it. The library-literal composition owner
adds the final-match-only publication and selected-library evaluation ordering.

## Shared request/outcome shape with the browser

[package-query-experience.md](package-query-experience.md)'s non-goals already
anticipate this: "saving is local-storage-only (browser storage or a CLI
file), and only the `query` record is ever shared." The CLI's future
save/resume mechanism (a `--save-as`/`--resume-from` file, name not fixed
here) should persist the same request/outcome shape the browser's local
storage does, so:

- a CLI-saved query and its results are replayable as browser test fixtures
  and vice versa, without a translation layer;
- "first 1,000 resumes from a saved first 500" (the browser's stated goal for
  saved queries) is one mechanism with two front ends, not two.

This document does not fix that shape's exact fields; it only asserts that
one shape should serve both surfaces, matching the precedent set by treating
the product's named terms as canonical for both hosts.

## Non-goals (v1)

- No new general expression grammar in the L1 contract. The finite
  product-issued term keys and values are canonical; any future CLI grammar
  must lower
  through product-owned bindings rather than defining another predicate set.
- No relational query surface here. Package-to-capability or
  package-to-integration questions route through the existing inspection
  graph, not through a new edge concept invented for this document.
- No unbounded package-content evaluation. Package-content terms retain their
  product-owned 20-candidate maximum, while `metadata-expensive`
  `library-literal` requests have a five-candidate maximum.
- No regex, byte-pattern, arbitrary assembly selector, all-assembly scan, RID,
  traversal, or untyped-detail expansion for `library-literal`.
- No separate assembly-semantic CLI mode, output contract, or result kind.

## Landing sequence

1. **This document** — layering and vocabulary, reviewable independently of
   any implementation.
2. **Sections migration — done, via #4551, but not as its own slice.**
   `find --package-prefix` rendering already routes through the shared
   Sections registry (`PackageProfileSections`,
   `SectionPipeline<PackageProfileView>`), so `--count`/`--rows` work the
   same way they do for `library`/`member`/`package`, without a second
   bespoke implementation. What did not land alongside it: retiring
   `-t`-as-package-limit for the focused #4677 `-n` grammar, and
   `-S`/`--where` were not part of that slice. See
   [Sections migration: already landed, ahead of this document's sequencing](#sections-migration-already-landed-ahead-of-this-documents-sequencing).
3. **Product-owned query contract — implemented in the current sources.**
   `PackageQuery` composes package acquisition, publishes stable ordered term
   descriptors, resolves complete Portable Query Intents, and streams matched
   package rows with semantic answers, structured evidence, and honest
   completion.
   Search-metadata terms skip manifests, nuspec terms need no package payload,
   and package-content terms require an explicit host provider and at most 20
   candidates. `PackageQueryTests` and
   `PackageQueryPlanner_IsReachableFromBrowserConsumer` are the named Release
   gates.
4. **CLI package-query command — implemented by #6768.** `package query`
   consumes exact-ID or terminal-star prefix input, admits the shared term
   vocabulary, treats selecting a content term as acquisition approval, offers
   `--nuspec-only` as the restrictive override, and preserves the
   package-content 20-candidate maximum.
5. **CLI limit reconciliation — implemented by #6768.** `--take` owns
   explicit candidate work and semantic `-n` owns final package rows. Without
   explicit `--take`, a lone Head is delegated through the shared optional
   match budget and, for direct rows, the candidate budget. Browser callers
   retain their numeric match budgets.
6. **Command retirement — implemented by #6768.** `package search` and
   patternless `find --package-prefix` are removed without aliases.
   Patterned `find PATTERN --package-prefix PREFIX` remains API search.
7. **Assembly-semantic evaluator extracted behind its focused owners.**
   [Find assembly-semantic query](find-assembly-semantic-query.md) retains the
   reusable bounded evaluator composition, while Package Query owns its public
   package-row adoption.
8. **Define the shared save/resume file shape**, coordinated with whatever
   the browser experience's local-storage record settles on when it is
   implemented.
9. **Adopt one Package Query vocabulary — implemented by #6972.** Structural,
   bound, selection, and inspection intent travel through one Portable Query
   plan. The opaque facet channel is removed from CLI and Browser requests;
   both hosts project the same descriptors and preserve the same execution,
   evidence, and acquisition rules.
10. **Assembly-reference term — implemented by #7618.** `references` applies
    bounded SRM-only `AssemblyRef` inspection across all admitted managed
    `ref/` and `lib/` framework groups. CLI and Browser project the same free
    term, package-grain match, count-plus-preview evidence, and visible
    package-content failures.
11. **Library-literal term unification — #7993.** Register
    `library-literal` as a user-selectable term, have the Product planner author
    non-user-selectable `library-target` from CLI `--tfm`, prequalify ordinary
    terms before selected-library evaluation, and return one
    `InspectionEnvelope<PackageQueryDocument>`. Retire `--library-literal`,
    `package-assembly-semantic-query`, and their separate CLI presentation
    path. The production demo combines an ordinary term with the literal term
    and reopens the exact returned Root.

Each step should name its own gating tests as it lands, per this project's
"asserted properties name their gate" rule — this document is not itself a
gate for anything.
