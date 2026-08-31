# Package dependency evidence

This document owns the host-neutral L1 contract for asking the same dependency
question of package manifests and restored project graphs. It defines the
normalized evidence, typed result, and equivalence relation. It does not own
how a host locates or acquires either input.

**Status:** target design; unimplemented.

## Owner

**Package Dependency Evidence Query** in `DotnetInspector.Queries` owns:

- normalization of owner-issued declared-dependency observations;
- optional owner observations associated with canonical package identity;
- additive restored-graph edges associated through owner-issued identities;
- stable evidence identity and deterministic result ordering;
- a closed result algebra that preserves owner-issued failures and defines
  normalization failures;
- completion accounting for the admitted root set; and
- the equivalence projection shared by package, nuspec, restored-project, and
  package-prefix adapters.

The query consumes owner-issued typed input. It does not accept a filesystem
path, package source client, cache, CLI option, Markout model, or output
callback.

The query has no external effects. Its work is bounded by the admitted roots,
declarations, and supplied enrichments. Network, filesystem, parsing, and
retry costs are declared by the owners that construct those inputs.

## The question

The query answers:

> For these admitted roots, which package dependencies are declared, under
> which target-framework and version constraints, what additional resolution
> or owner evidence is available, and how complete is that answer?

That question is intentionally neutral. An owner set that does not contain
`Microsoft` is evidence about one owner predicate; it is not an intrinsic
classification of the dependency as "third party." Unknown owner data is not
evidence that an owner is absent.

## Contract shape

```text
typed admitted roots
  + declared dependency observations
  + optional restored-graph edges
  + optional owner observations
  + acquisition completion and failures
        |
        v
Package Dependency Evidence Query
        |
        v
immutable outcome
  - roots
  - declared dependency evidence
  - additive restored-graph evidence
  - owner observations
  - typed failures
  - completion
```

The result is one immutable snapshot. A consumer may project package rows,
dependency rows, unknown-owner rows, failures, or summary data from that same
snapshot. Selecting one projection must not rerun acquisition or change the
meaning of another.

## Immediate typed inputs

An adapter admits a root only after its owning component has established the
input's identity and typed facts.

### Package manifest

The package adapter supplies:

- the validated `PackageSourceCoordinate`;
- a `PackageDependencyGroupsQuery` outcome over validated
  `PackageManifestFacts`;
- source and acquisition provenance when available; and
- the typed completion or failure that governed admission.

Package archive and direct nuspec inputs produce the same dependency evidence
when the archive-extracted and direct nuspec content produce the same
manifest facts, group selection, and selection status. Their acquisition and
identity trust provenance may differ. #5316 extends the manifest-facts owner
with typed self-attested identity for direct nuspec content; the adapter does
not parse nuspec identity independently.

`PackageManifestFactsQuery` remains the owner of bounded XML projection,
manifest identity validation, dependency-contract validation, and
`PackageManifestFailure`. `PackageDependencyGroupsQuery` remains the owner of
declared groups, exact target-framework selection, implicit manifest-group
identity, and the distinction among selected, no dependency groups, and no
matching target framework. The evidence query preserves those states; it does
not turn either absence state into an empty selected group.

### Restored project graph

The restored-project adapter supplies owner-issued typed facts from one
already-acquired `project.assets.json` selection:

- one owner-issued restored-project selection identity;
- the selected target framework and optional runtime identifier;
- admitted root identities;
- project-authored direct dependency observations grouped by their authored
  project target framework;
- optional resolved coordinates and direct/transitive graph roles; and
- typed projection completion or failure.

The adapter does not supply a `.csproj` path to L1. Resolving a `.csproj` to its
existing `project.assets.json`, reading the file, and reporting not-restored or
not-found states remain upstream responsibilities. The query neither evaluates
MSBuild nor initiates restore or build. #5314 owns the claim that a `.csproj`
locator and the exact assets content it selects produce equivalent restored
facts.

The current mutable, path-taking `ProjectAssetsParser` result does not satisfy
this query's input obligation. The construction, validation, identity, and
failure semantics of the replacement input belong to the focused Restored
Project Dependency Facts Query in #5314.

### Package-prefix root set

The package-prefix adapter supplies admitted package roots and the terminal
completion from `PackageProfileQuery`. Search, manifest acquisition, candidate
bounds, source pagination, and producer contract validation remain owned by
the package source and profile query.

A truncated root set is usable bounded evidence, not an exhaustive package
universe. The evidence query preserves that completion and never manufactures
an exact prefix total.

### Owner evidence

Optional owner observations are supplied by their own typed input owner. This
query does not call a metadata source or perform owner lookups. #5315 owns the
separate bounded Package Owner Evidence Query needed to construct those
observations by canonical package identity.

## Common declared evidence

One normalized declared observation states:

- which admitted root made the declaration;
- the canonical dependency package identity;
- available declaration target-framework scope;
- the NuGet version constraint; and
- retained source spellings and duplicate-count provenance.

Canonical package identity, the framework-scope identity defined below, and
NuGet version semantics are used for joins and equivalence. Display spellings
are evidence, not identity.

The query emits at most one successful row per root, framework scope, and
dependency identity. Semantically duplicate declarations with the same
constraint collapse into that row and retain their source occurrence count as
provenance. Repeated declarations with conflicting constraints produce a typed
conflicting-declaration failure; they are not ordered into apparently valid
rows.

Every owner-issued declaration group becomes normalized evidence. A requested
framework selection identifies rows within that complete set; it does not
discard unselected groups. The result preserves selected, no dependency
groups, and no matching target framework as separate selection states.

The common projection contains only facts both package manifests and restored
project graphs can state as declarations. It excludes:

- a resolved dependency version;
- direct versus transitive graph role;
- selected runtime identifier;
- package-cache or source path;
- compile, runtime, resource, analyzer, or build asset selection; and
- transitive closure.

Those facts may be valuable, but they are additive resolution evidence rather
than a reason for equivalent declarations to produce different common rows.

### Framework scope

Declaration scope is one of:

- **Any framework** — a scope with `NuGetFramework.AnyFramework` semantics,
  whether represented by an implicit manifest group or an explicit universal
  group;
- **Exact framework** — a parseable full target framework, including platform
  and platform version when present;
- **Unrecognized framework** — a retained owner-issued token that cannot be
  assigned NuGet framework semantics; or
- **Unavailable** — the input proves the declaration but cannot prove its
  authored framework scope.

This query owns construction of `DependencyFrameworkScopeIdentity` for its
normalized rows. Exact identities use NuGet.Frameworks parsing and canonical
short-folder spelling that retains platform and platform-version identity.
Alternate casing and long/short spellings therefore compare by framework
semantics. Platform-qualified identities remain distinct. Unrecognized tokens
retain opaque identity and inert display evidence but are not
framework-comparable across input forms. Whether a universal group was
implicit or explicit is retained as provenance, not framework identity.

The selected restored target framework is resolution context, not a substitute
for an unavailable authored declaration scope. This query does not infer
framework compatibility or claim that a `netstandard2.0` declaration has
`net8.0` authored scope because it participated in a `net8.0` restore.

The common result therefore supports two explicit projections:

- **Core declaration** — dependency identity and NuGet constraint; and
- **Scoped declaration** — core declaration plus any/exact framework scope.

Scoped comparison is not comparable when either side's authored scope is
unavailable or unrecognized. Unequal and not comparable are distinct outcomes.

## Additive restored-graph evidence

A restored graph may supply a separate immutable edge collection:

- owner-issued stable edge identity;
- parent and dependency identities;
- declared constraint on that graph edge when available;
- exact resolved dependency coordinate;
- selected target framework and runtime identifier; and
- direct or transitive role relative to the restored root.

Absence of an additive fact means unavailable for that input, not false. A
package manifest therefore does not claim that a dependency was unresolved or
non-transitive merely because it cannot provide restored-graph evidence.

Multiple parents may carry different constraints to the same resolved
dependency. Those edges remain distinct and never become conflicting
root-authored declarations. A direct edge may be correlated with a normalized
declaration through typed identities when the restored-facts owner establishes
that correspondence. The query does not infer it from package labels, rendered
version text, row positions, or local paths.

## Owner observations

Owner metadata is an optional enrichment over canonical package identity. The
same Package Owner Evidence Query contract supplies it for every input form.

An owner observation is one of:

- **Known** — the producer authoritatively returned an owner set, including a
  known empty set;
- **Unknown** — the selected producer cannot establish owner metadata for that
  identity; or
- **Failed** — an attempted lookup failed with typed producer and failure
  evidence.

Known, unknown, and failed are distinct. Neither unknown nor failed may be
projected as an empty owner set.

The result retains root-owner and dependency-owner observations independently.
It does not apply an owner predicate or emit `first-party`/`third-party`
labels. A later typed predicate may compare a requested owner identity with
these observations; that later operation must preserve unknown and failed
states.

An owner value contains canonical owner identity separately from its
`InertString` display spelling. Equivalent canonical package identities must
carry the same immutable owner observation within an outcome. Conflicting
supplied observations are a typed input-contract failure, not a reason to pick
one. Resolver batching, caching, source selection, retry, and network policy
remain outside this owner.

## Equivalence

Equivalence is defined over typed projections, not rendered JSON or table text.

### Package and nuspec

Package archive and direct nuspec inputs are dependency-equivalent when the
archive-extracted and direct nuspec bytes produce the same manifest facts and
group-selection outcome. The package path uses an independently expected
coordinate; the #5316 direct-content path uses typed self-attested identity.
Acquisition and identity-trust provenance may differ and is compared
separately.

### Restored input determinism

Identical restored facts and identical supplied owner observations produce the
same evidence outcome regardless of locator provenance. #5314 separately owns
and gates `.csproj` locator versus direct-assets equivalence. If the project
has no existing assets file, its adapter supplies a typed upstream failure
rather than permission to restore or evaluate the project.

### Package manifest and restored graph

Package/nuspec and restored-project inputs are compared only after a caller
pairs one admitted root from each outcome. The query never infers root
correspondence from display labels. Paired roots are equivalent under a common
declared-evidence projection when their owner-issued typed inputs describe the
same logical declarations.

The comparison:

- uses canonical package identity rather than casing or display spelling;
- uses NuGet version-constraint semantics rather than raw range text;
- reports core-declaration equality independently of framework scope;
- reports scoped equality only when both owner-issued scopes are comparable;
- distinguishes unequal from not comparable; and
- compares the canonicalized declaration set after duplicate collapse;
- ignores additive resolution evidence and input provenance.

Input-specific evidence is asserted separately. A restored graph may therefore
be equal under the declared projection while also reporting resolved versions
and transitive relationships unavailable from the nuspec.

## Identity and ordering

Stable evidence identity is constructed from owner-issued root identity,
framework-scope identity, and canonical dependency identity. It is independent
of presentation labels, source order, duplicate occurrence count, and row
position. A conflicting constraint has failure identity rather than successful
row identity.

The outcome retains the admitted root order supplied by the root-set owner and
uses a deterministic order within each root:

1. target-framework identity;
2. dependency package identity;
3. NuGet version-constraint identity.

The equivalence projection compares the normalized sequence after this order.
It does not rely on XML element order, JSON property order, source relevance
order, or serializer behavior.

## Failure and completion

Failure stays visible at the smallest truthful scope:

- a rejected root does not become an empty root;
- a malformed or invalid manifest retains `PackageManifestFailure`;
- an unavailable restored-project selection does not become a project with no
  dependencies;
- an invalid declaration becomes typed declaration failure rather than being
  dropped;
- a supplied failed owner observation remains failed for every row associated
  with that canonical identity; and
- a root-set acquisition failure remains separate from enrichment failure.

The outcome separately reports:

- root-set completion;
- admitted, rejected, and failed root counts;
- declaration projection completion; and
- owner-enrichment completion.

One phase cannot upgrade another phase's completion. In particular, complete
owner enrichment over a truncated package-prefix root set does not make the
prefix exhaustive.

## `InertString` boundary

Canonical typed identities remain suitable for matching, parsing, and control
flow. `InertString` does not replace them.

Every artifact- or source-derived value intended to cross from the query result
to a sink is an `InertString` no later than result construction, under its
declared `TextPolicy`. Already-treated input is retained rather than treated
again. Package and owner display spellings, original target-framework and
version-range spellings, and source labels use `TextPolicy.Field`. Safe
explanatory evidence uses `TextPolicy.Prose`.

The immutable outcome carries those `InertString` values through L2, JSON
projection, Markout, and other sinks without reconstructing them from raw text.
A sink may tighten policy with `EnsurePermitted`; it must not reacquire the raw
artifact string. Containment metadata and model-field location remain
available for audit.

Identity, provenance, and presentation stay separate:

- joins use canonical owner-issued identity;
- provenance states where the observation came from;
- `InertString` carries the display evidence safely; and
- no identity is inferred from the inert spelling.

Package IDs and other identifiers remain eligible for the separate identifier
confusion audit. Visual containment does not assert that two identifiers are
the same or different.

## Demo

The command spellings below are target mockups. This design does not assign
them to L3.

One fixture expresses the same declarations as a package manifest and a
restored project graph:

```console
$ dotnet-inspect <dependency-evidence> --package Contoso.Root@1.0 --json \
    | jq '.dependencies | map({
        framework: .framework.id,
        dependency: .package.id,
        constraint: .declaredConstraint.canonical
      })'
$ dotnet-inspect <dependency-evidence> --nuspec ./Contoso.Root.nuspec --json \
    | jq '.dependencies | map({
        framework: .framework.id,
        dependency: .package.id,
        constraint: .declaredConstraint.canonical
      })'
$ dotnet-inspect <dependency-evidence> --project ./Contoso.Root.csproj --json \
    | jq '.dependencies | map({
        framework: .framework.id,
        dependency: .package.id,
        constraint: .declaredConstraint.canonical
      })'
$ dotnet-inspect <dependency-evidence> \
    --project ./obj/project.assets.json --json \
    | jq '.dependencies | map({
        framework: .framework.id,
        dependency: .package.id,
        constraint: .declaredConstraint.canonical
      })'
```

```json
[
  {
    "framework": "net8.0",
    "dependency": "contoso.logging",
    "constraint": "[2.0.0, 3.0.0)"
  },
  {
    "framework": "net8.0",
    "dependency": "contoso.options",
    "constraint": "[2.1.0, )"
  }
]
```

The four canonical scoped-declaration projections are equal in the composed
target. Original range spellings remain separate `InertString` evidence and
need not be textually equal. #5314 gates that the project locator and direct
assets path supply the same restored facts. Their outcomes also carry the same
restored-graph evidence in the same snapshot:

```json
{
  "dependencies": [
    {
      "framework": {
        "kind": "exact",
        "id": "net8.0",
        "display": "net8.0"
      },
      "package": {
        "id": "contoso.logging",
        "display": "Contoso.Logging"
      },
      "declaredConstraint": {
        "canonical": "[2.0.0, 3.0.0)",
        "display": "[2.0,3.0)"
      }
    }
  ],
  "restoredEdges": [
    {
      "parent": "contoso.root@1.0.0",
      "dependency": "contoso.logging@2.4.1",
      "constraint": "[2.0.0, 3.0.0)",
      "role": "direct",
      "selectedTargetFramework": "net8.0"
    }
  ]
}
```

A broad prefix returns neutral evidence suitable for downstream predicates:

```console
$ dotnet-inspect <dependency-evidence> \
    --package-prefix Microsoft --json > evidence.json

$ jq '
    .dependencies[]
    | select(.rootOwners.state == "known")
    | select(any(.rootOwners.values[]; .id == "microsoft"))
    | select(.dependencyOwners.state == "known")
    | select(any(.dependencyOwners.values[]; .id == "microsoft") | not)
  ' evidence.json
```

The query does not call those rows third party. Unknown and failed owner
lookups remain separately selectable:

```json
{
  "package": {
    "id": "example.unknown",
    "display": "Example.Unknown"
  },
  "dependencyOwners": {
    "state": "failed",
    "producer": "nuget.org",
    "failure": "MetadataAcquisition"
  }
}
```

**What to notice:** all input forms feed one declared-evidence shape; restored
inputs add resolution facts without changing that shape; owner predicates are
explicit; and a failed lookup is not laundered into an empty owner set.

## Evidence and gates

Implementation must establish:

- one normal solution-graph fixture, registered through `FixtureCatalog`,
  whose built package manifest and restored graph express the same declaration
  seed without checking in environment-bound assets;
- deterministic normalization of identical restored facts and enrichments,
  while #5314 gates `.csproj` locator and direct-assets equivalence;
- dependency equivalence between package-extracted and #5316 direct nuspec
  facts with distinct identity-trust provenance;
- core and scoped common-projection equivalence between package/nuspec and
  restored inputs;
- equivalent alternate TFM spellings, distinct platform-qualified TFMs, and
  explicit implicit-versus-explicit any-framework, unavailable, unrecognized,
  unequal, and not comparable cases;
- separate assertions for provenance, resolution, capability, and completion;
- non-vacuity by mutating one declared package identity, framework identity,
  version constraint, or conflicting duplicate and observing inequality or
  typed failure;
- a diamond restored graph retaining distinct parent edges and constraints;
- conflicting supplied owner observations producing typed input-contract
  failure;
- hostile text containment at query-result construction, with sink retention
  gated by each later JSON or Markout adopter; and
- visible root, declaration, and enrichment failures.

Until those Release gates exist, the implementation properties are
`unverified`.

## Adoption sequence

1. Lock this result and equivalence contract.
2. Land typed self-attested direct nuspec identity in #5316.
3. Land the focused Restored Project Dependency Facts Query tracked by #5314.
4. Implement the package/nuspec adapter over `PackageManifestFacts` and
   `PackageDependencyGroupsQuery`.
5. Implement the restored-project adapter and the cross-input equivalence
   fixture.
6. Land the focused Package Owner Evidence Query tracked by #5315, then admit
   its owner observations as optional input.
7. Add L2 section and JSON projections over the immutable outcome.
8. Bind CLI input spellings and, later, product-owned predicates.

Each adoption is independently reviewable. Later syntax must not move owner
filtering ahead of required evidence acquisition unless source delegation
proves exact equivalence and honest completion.

The existing `depends`/`DependencyGraphService` path remains authoritative
until a separate parity-gated adoption replaces its dependency projection.
The #5314 implementation should replace or become the shared basis for its
existing assets projection after parity rather than leave two independent
parsers.

## Non-claims

This owner does not:

- locate files, read paths, evaluate MSBuild, restore, or build;
- parse CLI options or choose command names and aliases;
- select package sources, authenticate, cache, retry, or define network policy;
- redefine nuspec parsing, package search, project-assets parsing, target
  framework compatibility, or NuGet version semantics;
- define L2 row queries, Count, item windows, field projection, or ordering
  syntax;
- define Markout, JSON, JSONL, TSV, or plaintext rendering;
- classify a package as first party or third party;
- replace the current `depends` command or `DependencyGraphService` without a
  focused parity adoption;
- promise an exact total for bounded package-prefix discovery; or
- acquire package archives, assemblies, metadata, PDBs, source, or IL.
