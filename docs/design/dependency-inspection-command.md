# Dependency inspection command

This document owns the target CLI dependency operation tracked by
[#5993](https://github.com/richlander/dotnet-inspect/issues/5993).

**Status:** adopted implementation contract with a superseded placement
proposal. `depends` implements the type-relationship and explicit-root
dependency contracts through #5994. The separate `dependency-evidence` command
and positional type-to-library fallback were retired in #5995.
[Operation Commands and Subject Sections](operation-command-and-subject-section-composition.md)
now retains `depends` as a top-level operation and uses subject sections for
curated subject-first dependency views. It supersedes every later proposal in
this document to move the operation to `type graph` or `graph dependencies`
and retire `depends`; those passages remain historical input for focused
Dependency adoption rather than target grammar.
The host-neutral dependency settlement operation and ordinary CLI adoption are
implemented under
[#7117](https://github.com/richlander/dotnet-inspect/issues/7117). Debug CLI
sidecar delivery is implemented under
[#7293](https://github.com/richlander/dotnet-inspect/issues/7293);
Browser/Wasm adoption remains proposed.

## Owner and claim

The Dependency command owner defines one CLI operation, exposed through
top-level `depends` and future curated subject sections:

> Admit explicitly named dependency subjects and assets, project their
> owner-issued relationships and evidence, apply one traversal and disclosure
> model, and close each entry point into its declared document and
> rendering contract.

Traversal and evidence are not separate operations. Traversal selects which
reachable relationships participate; sections and verbosity select how much
evidence about those relationships and their roots is disclosed.

This owner defines:

- command grammar and mode selection;
- explicit root and source-scope gestures;
- root-set lifetime and partial-failure behavior;
- traversal direction and depth;
- the current Dependency documents and CLI section composition;
- subject-section bindings that preserve the same Dependency operation and
  result contract;
- row selection, count, output-format eligibility, diagnostics, and exit
  status; and
- migration from the former two-command split to the current `depends`
  surface and any focused subject-section adoption.

It consumes owner-issued facts and does not redefine their construction:

- [Package Dependency Evidence](package-dependency-evidence.md) owns normalized
  package, nuspec, and restored-project declarations, resolution evidence,
  identity, completion, failures, and `InertString` containment.
- [Restored Project Dependency Facts](restored-project-dependency-facts.md)
  owns exact `project.assets.json` target selection, package nodes, and graph
  edges.
- [Restored Project Dependency Traversal](restored-project-dependency-traversal.md),
  tracked by [#5998](https://github.com/richlander/dotnet-inspect/issues/5998),
  owns root-relative restored-project traversal: typed project-reference and
  package relationships, minimum distance from the restored root, depth
  boundaries, scoped failures, completion, and topology identity.
- Metadata and Services own type hierarchy, assembly-reference, and package
  resolution facts.
- [Package Source Model](package-source-model.md) owns source authorization,
  authority identity, transport, and source-result association.
- [Package Dependency Candidate Resolution](package-dependency-candidate-resolution.md),
  tracked by [#5765](https://github.com/richlander/dotnet-inspect/issues/5765),
  owns
  source-authorized package version-range resolution and exact acquisition
  candidates.
- [Package House](package-house.md) owns the candidate-bound pruning
  applicability, policy result, receipt, and distinction between package
  retention and platform delegation.
- [Package Dependency Traversal](package-dependency-traversal.md) owns
  package-manifest graph identity, direct source boundaries, root-relative
  reachability, failures, and completion while preserving owner-issued
  candidate and source evidence.
- [Search Scope Resolution](search-scope-resolution.md) owns default source
  activation, explicit-source suppression, and source composition in type
  relationship mode.
- Markout owns graph and tabular lowering after the command supplies typed
  nodes, edges, evidence rows, and presentation context.
- [Progressive Disclosure](progressive-disclosure.md) and
  [Output Shapes](output-shapes.md) own section selection, verbosity,
  projection, row shaping, and output-format rules.

The command may adapt those facts into one presentation-neutral dependency
document. It must not parse artifact text, reconstruct owner identity from
labels, or create a second dependency-normalization model.

## User purpose

The two former commands divided one user question along an implementation
boundary:

- `depends` follows reachable relationships but drops most declaration,
  constraint, provenance, completion, and failure evidence; and
- `dependency-evidence` retained that evidence but did not expand package
  manifests into a transitive traversal.

That division creates both overlap and underlap. A user may need to know that a
package is reachable, why it is reachable, which constraint introduced it,
which version was restored, whether the graph is complete, and which source or
artifact established the answer. Those are different projections over one
dependency operation, not reasons to choose two top-level commands.

The target experience lets the user vary two independent axes:

1. **Traversal:** how far dependency relationships are followed.
2. **Disclosure:** which graph, declaration, resolution, provenance,
   completion, and failure sections are rendered.

Neither axis changes the admitted subject or operation arity, so the
[Command Transition Model](command-transition-model.md) keeps them within one
Dependency operation. Top-level `depends` admits explicit subjects and
heterogeneous asset-root sets. Subject commands may expose curated Dependency
sections using their already resolved subject. Neither entrance splits
traversal from evidence.

The historical `dependency-evidence` design used heterogeneous root cardinality
to justify a separate command. This target supersedes that conclusion. Root-set
cardinality is source context inside the asset dependency operation: one or
several roots still produce the same dependency document, per-root completion,
graph edges, and evidence row families.

Type relationship mode and asset dependency mode use different admission plans
and target documents. They remain under one Dependency semantic owner because
both project owner-issued dependency relationships and evidence through the
same traversal/disclosure axes, not because they share a target envelope.
Graph may separately consume those owner-issued relationships for
identity-preserving topology without replacing either Dependency result.

## Consumer, tracker, and delivery

The current production consumer is the top-level `depends` CLI command. Target
production consumers also include Package, Library, Type, and Member sections
that bind curated dependency requests to already resolved subjects.

This document remains the sole owner of each Dependency request, producer
result, evidence, failure, and migration contract. Subject sections supply
local subject admission and authored presets without changing that ownership.
The Graph owner may project the same owner-issued evidence into a separate
Graph result while retaining Dependency identity, resolution, and failure
semantics.

The shared evidence substrate is implemented by
[#5533](https://github.com/richlander/dotnet-inspect/issues/5533), and
[#5532](https://github.com/richlander/dotnet-inspect/issues/5532) remains the
end-to-end dependency-evidence tracker. Browser/Wasm adoption remains owned by
[#5535](https://github.com/richlander/dotnet-inspect/issues/5535); this
CLI-focused design neither changes nor blocks that host.

The dependency service adopter in #7117 is shared host-neutral work despite
this command's CLI presentation ownership. `DependencyInspectionOperation`
now settles the selected-plan `DependencyInspectionContent` and ordinary
`InspectionEnvelope<DependencyInspectionContent>`, and its enriched entry
point composes the existing Package Dependency Evidence outcome without moving
its normalization contract.
The CLI supplies acquired owner-issued results to that operation and retains
only acquisition, graph-row shaping, labels, section composition, and
rendering. Debug sidecar publication is the third slice of the
diagnostic-sidecar composition tracked by
[#7293](https://github.com/richlander/dotnet-inspect/issues/7293), consuming
the generic attachment contract and CLI sidecar transport without redefining
either owner.
[Network request evidence](https://github.com/richlander/dotnet-inspect/issues/7287)
and the
[CLI network-diagnostic migration](https://github.com/richlander/dotnet-inspect/issues/7289)
remain separately owned; this design does not define Networking capture or
CLI-wide diagnostic retirement.

The original seven-step consolidation delivery is complete:

1. Lock this command contract in #5993.
2. Supply the shared declaration-to-exact-candidate handoff under
   [#5765](package-dependency-candidate-resolution.md).
3. Define typed package dependency traversal under
   [#5996](https://github.com/richlander/dotnet-inspect/issues/5996).
4. Define typed restored-project root and project-reference traversal under
   [#5998](https://github.com/richlander/dotnet-inspect/issues/5998), specified
   by [Restored Project Dependency Traversal](restored-project-dependency-traversal.md).
5. Replace the lossy tree-owned `depends` model with one typed Markout graph
   and edge-row projection under
   [#3320](https://github.com/richlander/dotnet-inspect/issues/3320).
6. Adopt asset-driven project, assets, nuspec, and normalized package evidence
   plus depth-controlled traversal under
   [#5994](https://github.com/richlander/dotnet-inspect/issues/5994).
7. Remove `dependency-evidence`, reserve its token against implicit routing,
   and update current README, help, product skills, demos, and machine
   contracts under
   [#5995](https://github.com/richlander/dotnet-inspect/issues/5995).

This replaced the former two-command architecture. The old command and its
command-specific projection path are removed.

The exact-candidate adapter in #5765 and typed package traversal owner in #5996
are shared host-neutral prerequisites. They are separated because
source-authorized version selection and exact acquisition correspondence belong
to the candidate adapter, while graph traversal, root-relative reachability,
and traversal completion belong to the traversal query. This command consumes
both results without redefining either contract. Its first production consumer
is CLI `depends`; #5532 also sequences later Browser/Wasm reuse so neither
capability becomes CLI-only substrate.

Type-hierarchy and library-reference graph correction remains the existing
focused work in #3320. This design specifies the command-visible graph
contract, but it does not redefine how Metadata or Services produce those
relationships. #3320 must consume complete owner-issued relationships rather
than reconstructing missing edges from the current lossy trees.

## Conventional baseline and deliberate divergence

Two .NET SDK commands establish useful comparison points:

- [`dotnet nuget why`](https://learn.microsoft.com/dotnet/core/tools/dotnet-nuget-why)
  accepts a project, solution, or `project.assets.json` and shows the paths by
  which one named package is reachable.
- [`dotnet package list --include-transitive`](https://learn.microsoft.com/dotnet/core/tools/dotnet-package-list)
  separates top-level declarations from a flat resolved transitive inventory
  and preserves requested versus resolved versions.

Those commands confirm three useful conventions: restored assets are a
first-class graph input, direct and transitive roles are distinct, and
requested constraints are not interchangeable with resolved versions.

`dotnet-inspect` deliberately diverges in scope:

- it accepts package manifests, local archives, nuspecs, libraries, types, and
  restored project assets through one dependency operation;
- it can return multiple explicit roots and retain a failed root beside valid
  siblings;
- it exposes traversal and evidence as independently selectable projections;
  and
- it never initiates MSBuild evaluation, restore, or build when a project
  locator lacks existing assets.

The broader asset set is justified by the product's inspection purpose. The
typed result and explicit completion states are required so that combining
those assets does not turn unavailable evidence into a false complete
hierarchy.

An explicit remote package root remains the authorization for package
acquisition, including its current complete traversal when no depth is
supplied. Section selection may demand traversal, but it never grants network
authority; the explicit root gesture does. Selecting evidence without the graph
does not acquire transitive package manifests.

The executed request plan is nevertheless section-dependent, as progressive
disclosure requires. Selecting `Dependency Hierarchy` requests traversal;
selecting only evidence sections does not. Completion, failures, and exit
status describe the producers that the selected plan actually ran. A phase
that was not requested is `NotRequested`, not complete, failed, or silently
omitted.

## Command modes

`depends` has two modes selected by whether the positional type subject is
present.

The target grammar places those modes according to their admitted subject:

```console
dotnet-inspect type graph <type> \
  [--package <package>]... \
  [--library <library>]... \
  [--project <project>]... \
  [--platform [<platform-library>]] \
  [--platform-library <library>]... \
  [--extensions] \
  [--aspnetcore] \
  [--tfm <target-framework>] \
  [--depth <positive-integer>]

dotnet-inspect graph dependencies \
  [--package <package-target>]... \
  [--nuspec <path>]... \
  [--library <library>]... \
  [--project <path>]... \
  [--package-prefix <prefix>] \
  [--tfm <target-framework>] \
  [--depth <positive-integer>]
```

License inventory is also an explicit projection:

```console
dotnet-inspect depends --project ./App.csproj -S Licenses
dotnet-inspect depends --nuspec ./Package.nuspec \
  -S "Licenses,Failures"
```

The dependency owners supply the exact package-coordinate set. Restored
projects contribute every distinct package node in the selected
`project.assets.json` graph. Package and nuspec roots contribute every distinct
resolved dependency node within the requested traversal boundary; explicit
roots themselves are omitted. The license inventory then acquires only each
coordinate's nuspec and never downloads or reads package payload content.

Each row answers `Package`, `Version`, and `License`. SPDX-expression
declarations answer with their expression. A declared `OSMFEULA.*` basename
answers `OSMF`; other file and URL declarations answer `unknown`, absent
declarations answer `none`, and failed manifest acquisition answers
`unavailable`. Typed Content keeps declaration kind/value or failure reason
beside that answer as evidence. Partial coordinate discovery or manifest
acquisition makes license completion partial, produces a nonzero exit status,
and prevents an exact `--count`.

Exact relationship-selection spelling inside `type graph` remains owned by its
focused adoption. The bare Dependency-backed Type Graph route must preserve
the existing base-type and interface topology, bounded search-scope meaning,
traversal, evidence, failures, and output-format eligibility before the
positional `depends` mode can retire. Its section names, discovery schema, row
fields, JSON shape, and envelope content intentionally transition to the
Inspection Graph contract. Package, library, project, platform,
platform-library, Extensions-package, and ASP.NET Core-package gestures remain
search scope there; target framework refines the selected or implicit sources.
None becomes a graph root.

### Type relationship mode

```console
dotnet-inspect depends <type> \
  [--package <package>]... \
  [--library <library>]... \
  [--project <project>]... \
  [--platform [<platform-library>]] \
  [--platform-library <library>]... \
  [--extensions] \
  [--aspnetcore] \
  [--tfm <target-framework>] \
  [--depth <positive-integer>]
```

The positional type is the focus. Package, library, project, platform,
platform-library, Extensions-package, and ASP.NET Core-package options identify
the bounded search scope in which that type and its base-type or interface
relationships are resolved. `--tfm` refines those sources and does not select
or suppress them. They are not graph roots.

This preserves the existing source-context distinction:

```console
dotnet-inspect depends System.Int128 --platform
```

means "follow the dependency relationships of this type in this scope," not
"treat the platform as an asset root."

The current type-miss fallback from `depends X` to library inspection is
removed. A positional subject means a type. Library dependency inspection uses
the explicit `--library` root, eliminating a data-dependent mode transition.

### Asset dependency mode

```console
dotnet-inspect depends \
  [--package <package-target>]... \
  [--nuspec <path>]... \
  [--library <library>]... \
  [--project <path>]... \
  [--package-prefix <prefix>] \
  [--tfm <target-framework>] \
  [--depth <positive-integer>]
```

With no positional type, every named source is an explicit dependency root.
Package, nuspec, library, and project roots may repeat and may be combined in
one request. Their command-line order is retained as root occurrence order.

At least one root gesture is required. Blank or malformed values are failed
root occurrences; they never bind the current directory, become floating
package coordinates, or suppress valid siblings.

`--package-prefix` remains a bounded root-set producer and is mutually
exclusive with explicit roots. Its candidate, match, failure, and truncation
accounting describes one independently completed root-set operation; mixing
unrelated roots into that accounting would make completion ambiguous.

The positional shorthand is not expanded to arbitrary paths in this design.
`depends X` means type relationship mode. Explicit root options avoid ambiguity
between a type, package ID, library name, project path, and nuspec path.

### Mode and option compatibility

The parser rejects gestures that have no meaning in the selected mode rather
than silently ignoring them:

| Gesture | Type relationship mode | Asset dependency mode |
| --- | --- | --- |
| `--package`, `--library`, `--project` | Search scope | Explicit roots |
| `--platform`, `--platform-library`, `--extensions`, `--aspnetcore` | Search scope | Rejected |
| `--nuspec`, `--package-prefix`, `--max-packages`, `--preview` | Rejected | Root or root-set policy |
| `--tfm` | Source refinement; does not suppress the implicit Platform default | Root-owner selection |
| `--depth` | Hierarchy traversal bound | Dependency traversal bound |
| NuGet source options | Accepted when package scope consumes them | Accepted when the selected plan performs remote package acquisition |
| `-D`, `-S`, verbosity, rows, count, and output formats | Dependency document projection | Dependency document projection |

`--depth` is valid only when `Dependency Hierarchy` is selected directly or
through the active verbosity preset. A depth supplied to an evidence-only
request fails as an unused operation gesture.

One positional type gesture produces exactly one root attempt. The type
resolver must return one owner-issued selected type identity or a typed
not-found, ambiguous, or rejected outcome before traversal begins. Candidate
assemblies supplied by scope options are search participants, not roots.
`--depth` counts from that one selected type node.

### Query operation profiles

Dependency registers one executable Query Operation with two profiles:

- **Type relationships** binds the selected-Type subject, `Dependency Graph`
  result, and existing Source, Target, Kind, and Traversal row vocabulary.
  `--where`, `--order-by`, `--top`, Head, Tail, Window, and Depth lower to one
  portable intent and resolve to the existing `TypeDependencySectionPlan`.
  Traversal remains a sequence order and cannot rank Top.
- **Rooted hierarchy** binds either the explicit asset-root set or an already
  resolved Package subject to `Dependency Hierarchy`. Both routes inherit
  Depth and Head, Tail, and Window from the same profile rather than copying a
  command-local inventory.

Depth is an upstream traversal-work dimension. Row stages run after traversal
and do not imply that the graph was exhausted at the selected row boundary.
An unsupported term, order, ranking, stage, or depth fails before source
acquisition. Query registration does not change root admission, traversal,
evidence, completion, hierarchy occurrence identity, failures, section
selection, or rendering.

## Asset admission and expansion authority

Each root gesture authorizes only the acquisition implied by that root and its
ordinary source policy. Traversal depth can narrow that work; it cannot invent
authority that the root did not grant.

| Root | Admitted evidence | Expansion authority |
| --- | --- | --- |
| Remote package ID or coordinate | Exact package identity and manifest dependency groups | Authorized package sources may resolve reachable package manifests up to the requested depth. |
| Local `.nupkg` | Validated archive identity and root manifest | Ordinary package-source policy may resolve reachable package dependencies up to the requested depth. |
| Direct `.nuspec` | Self-attested manifest identity and dependency groups | The file gesture alone authorizes only that nuspec, so ordinary dependency edges terminate as unresolved boundaries. Explicit `Licenses` additionally authorizes dependency-range resolution and exact manifest acquisition through the configured package sources. |
| `.csproj` or project directory | Existing located `project.assets.json`, with locator provenance | The selected restored graph is traversed without package or project acquisition. Explicit `Licenses` may acquire only the exact manifests named by that graph. |
| Direct `project.assets.json` | Exact restored-project facts with direct-assets provenance | The selected restored graph is traversed without package or project acquisition. Explicit `Licenses` may acquire only the exact manifests named by that graph. |
| Library | Owner-issued assembly identity and direct references | Existing library-reference resolution may follow resolvable references up to the requested depth. |
| Package prefix | Bounded package-profile roots and their manifest declarations | Profile acquisition authorizes the bounded manifests, not recursive expansion from every match. |
| Type | Owner-issued type and hierarchy relationships in the selected scope | Traversal stays within the admitted search scope. |

A `.csproj` and a direct assets path that select identical bytes produce the
same restored dependency facts and graph. They retain different locator
provenance. A `.csproj` is not parsed as the dependency graph and does not
authorize project evaluation; it is a locator for already-restored assets.

Supplying package-source options when no root can consume them fails rather
than silently accepting an inert gesture. Direct nuspec and restored-project requests consume source options only when
`Licenses` explicitly authorizes manifest acquisition. Library-only, type-only,
and package-prefix requests do not acquire package manifests merely because a
source option is present.

A local `.nupkg` can consume source options only when `Dependency Hierarchy` is
selected and traversal may expand beyond its direct declarations. An
evidence-only local-package request has no remote package operation, so source
options are rejected as unused.

Package-prefix discovery uses its existing credential-free NuGet Gallery
authority and rejects general package-source overrides. Its retained source
field identifies that producer; it does not imply support for configured feed
search.

## Dependency direction and root identity

Every graph edge points from a subject to something it depends on:

- selected type to base type or implemented interface;
- library to referenced library;
- package to declared or resolved package dependency; and
- restored project to direct package dependency, then package to package for
  restored transitive edges.

Every admitted explicit root is a graph node, including a root with no outgoing
edges. A formatter must never replace root identity with a heading comment that
drops its outgoing edges.

A root attempt that fails before semantic identity is established remains in
the diagnostic root ledger and the public `Failures` section but is not
invented as a semantic graph node. A root admitted before a later projection
or traversal failure retains its graph node and the associated failure.

Root identity is owner-issued:

- package and restored-project roots retain their query identities;
- configured package-source association remains source evidence, not root
  identity;
- library and type roots retain their Metadata or resolution identities; and
- document occurrence numbers are presentation addresses, never substitutes
  for semantic identity.

Two root gestures that resolve to the same semantic subject remain two root
occurrences with shared dependency identity. Their occurrence order and
provenance remain visible without duplicating the semantic node.

The producer-issued dependency topology uses two identity layers:

- semantic node identity is the owner-issued type, library, package,
  restored-project, or dependency identity; and
- document-local node and edge IDs address one immutable command result.

An edge retains its owner-issued relationship identity when one exists, such
as a restored-project edge or package declaration. Otherwise its document
identity combines relationship kind, semantic endpoints, and the
owner-provided relationship occurrence. Document IDs never become semantic
identity outside the result that owns them.

## Traversal contract

`--depth <N>` accepts a positive integer. Depth counts directed dependency
edges from each explicit root:

- `--depth 1` includes only direct dependency edges;
- `--depth 2` includes direct edges and one additional dependency level; and
- omitting `--depth` requests complete traversal within the root's authorized
  and available expansion boundary.

Depth is operation intent, not a rendering filter. Producers that would
otherwise acquire another package manifest or resolve another library must
receive the remaining bound before doing that work. A later row or line limit
does not authorize early termination of graph acquisition because it cannot
predict which logical edges survive graph ordering.

The selected-Type `type graph` adapter maps an explicit positive `--depth N` to
`InspectionGraphNeighborhoodDepth.Finite(N)` and omission to
`InspectionGraphNeighborhoodDepth.Complete`. Complete depth reaches semantic
closure only within the finite admitted search population and every
relationship-owner node, edge, acquisition, and work limit. A hit limit returns
typed incomplete content; it does not substitute an arbitrary depth or claim
closure. This preserves the current omitted-depth Dependency meaning without
introducing unbounded whole-program traversal.

For fixed graph evidence such as `project.assets.json`, omitted depth means the
complete selected restored graph already present in the asset. It never means
opening every resolved package. Explicit `Licenses` may independently acquire
the exact manifests named by that already-materialized graph. In that case
depth bounds graph admission rather than license-manifest acquisition. For
direct nuspec and package-prefix roots, the ordinary graph ends at their
declared dependency edges because those gestures do not authorize recursive
acquisition; the explicit `Licenses` exception applies only to direct nuspec
roots.

The current restored-project facts are not by themselves sufficient for that
root-relative traversal: they retain restored package edges but may omit the
incoming explicit-project and project-reference relationships that connect an
ordinary graph such as `App -> ProjectB -> PackageC`. The focused
restored-project traversal owner tracked by
[#5998](https://github.com/richlander/dotnet-inspect/issues/5998) must issue
those typed relationships and completion before #5994 can implement this
contract. The CLI does not reconstruct them from raw assets or display text.

A node at the requested depth may still be rendered as an endpoint. Its
outgoing edges are not acquired or admitted. The graph carries a typed depth
limit so a leaf caused by the bound cannot be confused with a subject proven
to have no dependencies.

Cycles and revisits are graph facts, not reasons to delete nodes or edges.
Traversal terminates by the reusable producer's owner-issued expansion
identity while retaining every distinct logical edge encountered within the
bound. Package traversal defines that expansion identity separately from its
exact-coordinate document node identity.

For several roots, traversal retains a finite root-occurrence reachability
relation:

- for each root occurrence and semantic node, the minimum discovered distance;
  and
- for each root occurrence and graph edge, whether that edge is admitted from
  that root within the requested depth.

Reusable producers also intersect admission with each root's expansion
authority. Sharing a semantic node or source projection never lets a
source-bounded root inherit recursive edges authorized by another root.

Cycles terminate because an owner-issued expansion identity is propagated for
one root occurrence only when traversal discovers a shorter distance. Package
traversal uses source-relative manifest projection identity while retaining
exact package coordinate as document node identity. The document graph is the
union of the per-root admitted edges. Tree and depth-boundary lowering consume
the per-root relation, so an edge admitted through a short path from root B is
not incorrectly rendered below root A when it lies beyond A's depth.

A semantic node that is also an explicit root remains one canonical dependency
node with a separate root occurrence. The hierarchy projects that occurrence
as its own top-level root even when the same node also appears below another
root.

## Dependency Hierarchy occurrence model

`Dependency Hierarchy` is the Depends-owned rooted explanatory result. Its
primary currency is one dependency relationship occurrence addressed within
one explicit root occurrence. Canonical dependency nodes and relationships
remain backing evidence; they do not collapse ancestry or root-relative path
identity.

The immutable hierarchy contains:

- one root occurrence for every admitted explicit root, including roots with no
  outgoing relationships;
- one non-root occurrence for every emitted parent-to-target relationship;
- a document-local occurrence ID;
- the owning root occurrence;
- the parent occurrence ID, or a root position for a root occurrence;
- the canonical target node and incoming relationship association;
- root-relative depth; and
- one of `Expanded`, `Revisit`, or `Cycle` for a non-root occurrence.

Projection is deterministic in explicit-root order, root-relative breadth, and
producer-issued relationship order. Expansion belongs to a root-relative
expansion context: the canonical node plus its source-relative package
projection when package evidence supplies one, or the canonical node alone
otherwise. The minimum-depth occurrence of each expansion context is expanded;
producer relationship order breaks equal-depth ties. Another occurrence of
the same context under that root is retained as a `Revisit` boundary and is not
expanded again. An occurrence whose target context is already in its ancestor
chain is retained as a `Cycle` boundary and is not expanded.

The same canonical target reached from two parents therefore produces two
hierarchy occurrences even though the backing topology contains one canonical
node. Distinct source-relative package projections of that canonical node are
distinct expansion contexts: each may expand once, and each consumes only the
outgoing relationships issued for that projection. This prevents a
longer-path occurrence or one package authority's projection from owning
another occurrence's descendants.

This bounded expansion emits each root-admitted backing relationship once per
root. It cannot recurse indefinitely, and it preserves the source relationship
that explains every occurrence. A node admitted at the traversal depth remains
an occurrence endpoint; its typed depth boundary explains why it has no
expanded children. Depth-boundary association uses the same expansion context,
including source-relative package projection identity, so one projection's
boundary never annotates another projection of the same canonical node.
Hierarchy construction rejects a depth boundary unless its node exists, its
optional package projection belongs to that node, and it names a non-empty,
duplicate-free set of known root occurrences. Malformed boundary evidence
therefore fails visibly instead of becoming an unexplained leaf.

Rows, row windows, Count, Tree, Mermaid, tables, JSONL, and structured JSON all
consume the same ordered non-root occurrence sequence. Roots are required
context rather than counted relationship rows. A window retains explicit roots
and the selected occurrences' endpoint context, but it never invents an
unselected parent-to-child relationship.

## One dependency document

The command constructs one immutable dependency document for the selected
request plan:

```text
ordered explicit root occurrences
  + typed semantic nodes
  + typed directed dependency edges
  + rooted dependency hierarchy occurrences
  + owner-issued declaration and resolution evidence
  + nuspec-derived package-license answers and declaration evidence
  + candidate-bound package-pruning applicability and policy evidence
  + root, acquisition, projection, and traversal failures
  + root-set, traversal, license, and pruning completion
        |
        v
section selection and row shaping
        |
        v
Markdown / tree / Mermaid / table / TSV / JSONL / JSON / count
```

The host-neutral `DependencyInspectionOperation` now settles the semantic
selected-plan value as owner-issued `DependencyInspectionContent` and returns
it in `InspectionEnvelope<DependencyInspectionContent>`. Its request contains
the already-acquired Package Dependency Evidence outcome, explicit root inputs,
traversal topology, package-license inventory, pruning, and typed host-adapted
failures. The operation owns root and hierarchy occurrence association, phase
projection, declaration-to-restored-edge joins, plan-relative traversal,
license and pruning projection, failure inclusion, and aggregate completion.
Producer values for an unselected traversal, license, or pruning phase do not
enter Content even if a host adapter supplies them. The CLI projection consumes
that envelope and continues to own section membership, row windows,
presentation ordering, and rendering. The ordered hierarchy occurrence
sequence itself is host-neutral Content so CLI and Browser/Wasm consumers
cannot disagree about root, parent, revisit, cycle, or row identity. This
extraction is not a new dependency-semantics model. The Content value carries
references to or copies of owner-issued identities and evidence plus
dependency-inspection occurrence identities, canonical endpoint indices, and
stable semantic ordering.

Package candidate, source failure, authority failure, manifest failure,
restored-traversal failure, and pruning-result Content use closed portable
projections. A package source projection retains credential-free producer
identity and transport kind but never the configured source, credential,
runtime source association, or acquisition correspondence. A candidate retains
its coordinate, kind, and discovery contract but no configured authorities or
acquisition capability. The CLI presentation aggregate may retain those live
runtime values only for immediate compatibility rendering of the existing
ordinary output. They are not part of the issued
`DependencyInspectionContent`: Content construction copies only the portable
values, so both in-memory host delivery and generated serialization are
authority-free and remain usable after the operation ends.

This selected-plan document is baseline Content for envelope adoption.
Root-set and requested-phase completion, hierarchy meaning, normalized
dependencies, package licenses, pruning results, and typed failures remain here
whether or not service evidence is requested. The
[Debug enrichment](#debug-service-evidence-enrichment) composes supplemental
owner-issued facts beside this document; it does not replace or weaken the
baseline.

The CLI owns traversal gestures and presentation composition, not reusable
dependency algorithms. Type, library, package, and restored-project owners
continue to produce their typed relationships and identities. If an
implementation slice needs a new reusable traversal or normalization
algorithm, that work belongs in a focused owner below the CLI rather than in a
second host-local implementation. Browser/Wasm consumes the extracted
`DependencyInspectionContent` and host-neutral evidence query; it never
consumes the CLI projection or presentation model.

Normalized evidence currencies have one stable universe: explicit admitted
roots only. Selecting `Dependency Hierarchy` may acquire or admit transitive
subjects, but it does not add those subjects' manifests to `Dependencies` or
`Pruning`, or to the diagnostic group, restored-package, and restored-edge
projections. Adding or removing the graph section therefore never changes
those already-selected evidence row sets. `Pruning` evaluates only normalized
direct declarations from explicit roots; it neither admits transitive roots nor
deletes graph edges. `Failures` is plan-relative: selecting traversal or
license or pruning work can add typed failures that a declaration-only plan
never produced. `Licenses` intentionally uses a different currency: distinct
exact dependent package coordinates supplied by package traversal or the
selected restored-project graph. It acquires only those coordinates' nuspec
manifests and never adds the resulting manifests as dependency roots or
declaration rows. For restored-project roots, the explicit root's owner-issued
evidence already contains the selected restored package nodes and edges.

The same semantic node may be reached from several parents. It appears once in
the canonical node set and once under each explaining parent in the hierarchy.
Every directed relationship remains backing evidence; each root-relative
parent occurrence remains a separately addressed hierarchy occurrence.

Section resolution happens before producer execution. The acquisition plan is
the union of producers required by the selected sections and the traversal
bound. An evidence-only request does not run transitive traversal, while a
hierarchy request acquires only the facts needed for its requested depth. Once
that plan completes, one immutable document supplies every selected view
without rerunning a producer.

The hierarchy is usable when some evidence sections are unavailable, and
evidence is usable when traversal stops at an unresolved boundary. Neither
projection turns the other's incompleteness into success-shaped absence.

## Sections and disclosure

The asset dependency route uses ordinary verbosity and section selection
instead of adding `--evidence` or `--details`. At the target placement,
`graph dependencies` retains this sectioned Dependency document. `type graph`
instead composes the selected-Type producer result into the Inspection Graph
document. The proposed flags would duplicate an axis already owned by
progressive disclosure and would not say which evidence is wanted.

The retail base section ladder is:

| Section | Declared row | Default visibility |
| --- | --- | --- |
| `Dependency Hierarchy` | One rooted dependency relationship occurrence. | Minimal |
| `Dependencies` | One normalized direct declaration. | Normal when applicable |
| `Licenses` | One distinct dependency package coordinate and its nuspec-derived semantic license answer. | Explicit only |
| `Pruning` | One direct declaration's pruning applicability or candidate-bound policy result. | Explicit only |
| `Failures` | One typed root, acquisition, projection, or traversal failure occurrence. | Normal when present |

`Dependency Hierarchy` is the asset route's single high-value minimal section. It
preserves the current reason to invoke the Dependency operation: seeing what
depends on what.

`-v:n` adds normalized direct dependency evidence and any failures needed to
interpret the result. `Licenses` is unbounded because it may resolve dependency
ranges and acquire every exact dependency manifest. `Pruning` is unbounded
because it may read an installed platform inventory and resolve exact package
candidates. Neither enters a verbosity level, including `-v:d`.

The `@Dependencies` category contains `Dependency Hierarchy`, `Dependencies`,
and `Failures`. It deliberately excludes `Licenses` and `Pruning`, so selecting
the category preserves its established cost and acquisition contract. A caller
that wants declaration evidence without traversal selects the public evidence
and failure sections it needs:

```console
dotnet-inspect graph dependencies --project ./App.csproj \
  -S Dependencies -S Failures
```

Pruning policy evidence is a separate explicit projection:

```console
dotnet-inspect graph dependencies --package Some.Package@1.2.3 \
  --tfm net11.0 -S Pruning
```

The section requires one base .NET `--tfm`. `--platform-family runtime` is the
default; `--platform-family aspnetcore` selects the ASP.NET Core comparison
inventory. The family is a disclosed policy comparison target, not a claim
that an application activates that shared framework.

Root-set completion and the state of every requested phase are mandatory typed
Content and remain present in unprojected JSON when the selected hierarchy or
evidence rows are empty or partial. Ordinary Markdown renders the selected H2
sections directly rather than projecting completion as a root document or
table. Diagnostics, exit status, and exact-count eligibility still consume
the typed completion state. A traversal phase omitted by section planning is
`NotRequested`; the pruning summary has the same state when `Pruning` is not
selected.

The implementation also retains four **diagnostic sections** for developing
and diagnosing the command:

| Diagnostic section | Declared row |
| --- | --- |
| `Roots` | One explicit root occurrence with identity, provenance, state, and completion. |
| `Restored Edges` | One owner-issued restored-project graph edge. |
| `Dependency Groups` | One normalized framework-scoped declaration group. |
| `Restored Packages` | One owner-issued restored package node with role and coordinate. |

Diagnostic is their purpose; `DEBUG` is their registration mechanism. A
`[Conditional("DEBUG")]` registration helper adds them as explicit-only
sections in Debug builds. In Release builds they are absent from the compiled
catalog, category membership, verbosity, exact and wildcard selection,
structural and effective discovery, schemas, count ordering, rendering, and
typed JSON section output. Their descriptor, view, and owner-issued evidence
types may remain compiled where the retail graph and dependency projections
reuse them; those types do not make a section public.

For the asset dependency route, `@Dependencies` is the base category. The same
category name may have different authored membership in another command;
package inspection continues to use its own direct dependency-section
membership. Automatic verbosity selects only the asset route's base category.

`Dependency Hierarchy` declares conditional acquisition cost. It is network-free
for restored assets and already-admitted local facts, and package-acquiring
when an expandable package root requires another manifest. A plain `-D`
remains structural and network-free. Bare `-S` includes the graph only when
the effective root plan can produce it without network acquisition; the
explicit remote `--package` gesture and ordinary default `-v:m` continue to
authorize the package traversal they request.

Type and library roots may not have package-declaration sections. Release
static discovery lists the four retail sections. Bare effective discovery
excludes `Pruning` because it is explicit-only and unbounded; exact or wildcard
selection may request it and therefore requires `--tfm`. Effective discovery
otherwise reports which sections are applicable to the admitted root kinds
and available evidence. In a Debug build, bare `-D` also lists the four
registered diagnostic sections, and effective discovery reports the applicable
diagnostic sections. This visible Release/Debug difference is the direct
demonstration that diagnostic registration disappears from retail compilation.

## Debug service-evidence enrichment

**Status:** partially implemented under
[#7117](https://github.com/richlander/dotnet-inspect/issues/7117). The
host-neutral ordinary and enriched settlement entry points and ordinary CLI
cutover are implemented. Debug CLI sidecar delivery is implemented under
[#7293](https://github.com/richlander/dotnet-inspect/issues/7293);
Browser/Wasm adoption remains proposed.
This section owns the dependency inspection service's concrete `TEvidence`,
capture request, and association with baseline Content. The generic
[service-evidence enrichment](inspection-envelope.md#service-evidence-enrichment)
owns the envelope, capture lifetime, transport-neutral attachment permission,
and Debug-only availability policy. [Output Shapes](output-shapes.md#envelope-transport)
owns CLI sidecar admission, publication, diagnostics, and failure behavior.
Package Dependency Evidence continues to own normalized package facts.

The first adopter is asset-mode dependency inspection. Positional type
relationship mode has no corresponding diagnostic sections and does not admit
`--evidence-envelope` under this adoption. Supporting it later requires its own
owner-issued evidence value rather than an empty package result.

### Required and supplemental roles

The current projection mixes facts with different obligations. Adoption uses
these roles:

| Current data | Role after adoption |
| --- | --- |
| Root-set completion, requested and admitted counts, per-root admission, traversal and pruning completion, graph counts, and selected-plan phase completion | Required baseline Content. |
| `Dependency Hierarchy`, `Dependencies`, and `Pruning` rows | Baseline Content selected by the command contract. |
| Root, acquisition, declaration, relationship, traversal, and pruning failures | Typed baseline Content; required operational context may also remain in ordinary Diagnostics. |
| Package root identity, provenance, declaration groups, group selection, restored package nodes, restored edges, processing observations, and their owner-issued phase states | Supplemental Package Dependency Evidence retained in `TEvidence`. |
| CLI root labels, section membership, row windows, display ordering, and Markout or JSON lowering | Host presentation, not service evidence. |
| Network policy rejection or offline failure | Ordinary operation failure, not evidence-only data. |

The same owner-issued fact may support a baseline row and remain in Evidence.
That is deliberate: optional capture cannot make Content incomplete or force a
baseline consumer to understand `TEvidence`.

### Concrete evidence value

The typed Content, evidence Document, root-occurrence currency,
same-execution association, selected-plan settlement operation, and ordinary
CLI consumption, closed generated serialization, and Debug CLI sidecar
delivery are implemented. Browser/Wasm adoption remains proposed.

The dependency service issues one named settled Document:

```text
DependencyInspectionEvidenceDocument
  PackageInputs: PackageDependencyEvidenceOutcome
  AdmittedRootOccurrences: DependencyRootOccurrenceIdentity[]
  FailedRootOccurrences: DependencyRootOccurrenceIdentity?[]
```

`PackageInputs` is the existing complete host-neutral outcome, not a copied
root, declaration, package, edge, processing, failure, or completion model.
The two occurrence sequences are association currency:

- `AdmittedRootOccurrences` is parallel to `PackageInputs.Roots`;
- `FailedRootOccurrences` is parallel to `PackageInputs.FailedRoots`; and
- a missing failed-root occurrence is admitted only for package-prefix
  producer failures that do not correspond to one explicit root occurrence.

`DependencyRootOccurrenceIdentity` is the dependency inspection request's
typed document-local root occurrence in admitted request order. Baseline
Content retains the same identity. The service validates both sequence lengths
and never joins by a root label, path, package display name, or array search.
Library-only roots have no Package Dependency Evidence association; their
baseline root and graph facts do not become a synthetic package input. A
zero-root `PackageInputs` outcome is therefore a complete empty package
evidence value only when baseline Content establishes that no admitted root was
applicable.

Package-source provenance uses
`PackageDependencyEvidenceSourceIdentity`, not the runtime
`PackageSourceResultIdentity`. It retains credential-free producer identity,
inert display, transport kind, and a one-based document-local association.
The association correlates prefix completion, admitted roots, and failures
without serializing the opaque caller association or granting acquisition
authority.

The wrapper is a Document rather than another Outcome. Package-root rejection,
phase unavailability, incomplete evidence, and typed producer failure already
have closed states in `PackageDependencyEvidenceOutcome`. Cancellation and an
unexpected operation failure still prevent envelope delivery instead of
manufacturing an evidence failure or returning a bare baseline.

The first schema contains only `PackageInputs` and its root associations.
[#7287](https://github.com/richlander/dotnet-inspect/issues/7287) separately
owns a bounded operation-scoped `NetworkRequestEvidenceDocument`. After that
owner exists,
[#7289](https://github.com/richlander/dotnet-inspect/issues/7289) may evolve
the registered dependency evidence schema to compose its value under the
envelope wire-version rules. This design does not reserve an untyped slot,
subscriber, or logging field for it.

### Capture request and baseline preservation

`--evidence-envelope` and selection of any retained diagnostic section are
Debug-only evidence gestures. They select the evidence-enabled service entry
point before execution. One capture requests normalized declaration and
produced-relationship evidence for every applicable explicit root. It retains
processing observations already supplied by those owners. This single full
package-input capture deliberately avoids four section-shaped evidence
contracts.

Capture is bounded by the explicit root set and existing Package Dependency
Evidence producer bounds. Package-prefix candidate, match, failure, and
truncation accounting remains in its owner-issued completion. Capture does not
request transitive traversal or pruning, and section or envelope selection
does not grant package-source, filesystem, network, restore, or build
authority. An explicit package root keeps its existing acquisition authority;
serialization has none.

The implementation may execute the union of baseline and evidence producer
work once, but it preserves the baseline request plan as a separate typed
input to Content construction. A declaration or relationship failure affects
baseline completion, Diagnostics, and exit status only when that phase belonged
to the ordinary selected plan. A failure encountered solely for evidence
capture remains visible in `PackageInputs` and does not reinterpret an
otherwise equivalent baseline. Producers shared by both plans retain their
ordinary failure meaning.

For one settled operation request and baseline plan, ordinary and enriched
execution yield equal ContentKind, Content, PortableProjection, and
Diagnostics. This is not a
cross-owner structural-equality contract for independently reconstructed
Package Dependency Evidence or dependency graphs. Rendering or serialization
consumes the settled values and never reopens an archive, assets file, package
source, or traversal.

When an explicit package Share request settles an exact package coordinate and
source authorization, evidence capture consumes that same settlement rather
than resolving the original selector again. Evidence-only acquisition may add
typed Evidence, but its verbose and network-traffic logs do not enter the
ordinary Share stderr stream. A nonprojectable Share remains governed by the
ordinary sidecar ordering and refusal contract.

### Thin Debug views and Browser adoption

The four diagnostic sections remain useful Debug views but no longer own
duplicated semantic production:

- `Dependency Groups`, `Restored Packages`, and `Restored Edges` project
  `PackageInputs` through the retained owner-issued identities;
- `Roots` joins baseline root occurrences to the parallel evidence
  associations and uses baseline facts for non-package roots; and
- all four may add host presentation context, but no view creates a second
  evidence snapshot or enters the evidence wire contract.

The CLI's complete machine transport is
`EvidenceInspectionEnvelope<DependencyInspectionContent,
DependencyInspectionEvidenceDocument>` in the file named by
`--evidence-envelope <path>`. The option preserves the ordinary primary
presentation on stdout or its distinct `--out` destination. Section selection
and row shaping continue to affect that ordinary presentation without shaping
Content or Evidence inside the attachment. When paired with `--envelope`, the
baseline envelope on stdout equals the attachment's `Inspection` value and
both derive from the same evidence-enabled execution, as specified by
[Output Shapes](output-shapes.md#envelope-transport).

Asset-mode dependency inspection registers `result_kind`
`asset-dependencies` at `schema_version` `1` for the Debug evidence capability.
The baseline form binds `content` to the source-generated
`DependencyInspectionJsonContext` metadata for
`DependencyInspectionContent`; the enriched form retains that exact binding
and additionally binds `evidence` to the same context's
`DependencyInspectionEvidenceDocument` metadata. Both forms use the same
framing pair because Evidence presence is an admitted form of one registered
contract, not another result kind. A different Content or incompatible
Content/Evidence schema requires the Output Shapes version transition rather
than reuse of `asset-dependencies` version `1`.

This adoption admits the baseline form only when paired with
`--evidence-envelope`; standalone asset-mode `--envelope` remains unadopted.
Ordinary asset-mode `--json`, with or without the sidecar, retains the existing
`DependsAssetDocument` serializer, including selected-section presence and row
windows. It is a named host presentation, not attachment Content. The sidecar
and paired baseline instead serialize `DependencyInspectionContent` without
post-service shaping. A later public standalone-envelope adoption must resolve
the differing ordinary JSON schema under Output Shapes rather than silently
reuse this Debug-only compatibility exception.

The Debug Browser/Wasm consumer receives the same closed evidence type and
the same `DependencyInspectionContent`, then renders an owner-selected
inspection-evidence view. It does not consume `DependsAssetProjection`, CLI
section rows, or Markout display models. Host-specific views may differ; the
service-issued values and wire associations do not.

### Gates and production adoption

Release correctness gates use the restored
`src/DotnetInspect.Cli/DotnetInspect.Cli.csproj` scenario and focused
pathological fixtures. They cover:

- semantic equality between the extracted `DependencyInspectionContent` and
  the existing asset-mode command projection before host rendering;
- exclusion of configured source locations, credentials, runtime source
  associations, acquisition correspondence, and live authority objects from
  both in-memory Content and its generated wire form while ordinary CLI
  rendering remains unchanged;
- round-trip of every reachable closed candidate, manifest,
  restored-traversal, and pruning outcome, including default immutable
  collections, plus rejection of noncanonical framework and portable producer
  identities;
- exact admitted and failed occurrence association, including mixed root kinds
  and a package-prefix failure without an explicit occurrence, while rejecting
  an unassociated failure for a non-prefix request;
- complete empty, partial, unavailable, failed, and bounded/truncated Package
  Dependency Evidence outcomes;
- equal extracted baselines for ordinary and enriched execution, including an
  evidence-only producer failure and complete envelope equality for one
  settled request;
- exact and `@latest` package Share requests reuse the settled coordinate and
  authorization for evidence acquisition, with evidence-only verbose and
  network-traffic logging excluded from ordinary Share stderr;
- selected-plan exclusion of graph, traversal, pruning, and their failures
  when a host adapter supplies values for an unselected phase;
- one execution, detached lifetime, and serialization without acquisition or
  recapture; and
- exact `asset-dependencies` version `1` framing for baseline and enriched
  forms, rejection of a missing or mismatched registration, and round-trip of
  both concrete source-generated serializers;
- parsed-wire equality for existing `DependsAssetDocument` JSON with and
  without the sidecar, including selected-section presence and row windows;
  rejection of standalone asset-mode `--envelope`; and
- matching runtime JSON and generated Browser/Wasm types for the complete
  closed enrichment.

The Debug CLI public-entry gates required by Output Shapes additionally cover
ordinary tree, JSON, and selected diagnostic-section output while the sidecar
receives the complete closed enrichment; paired `--envelope` baseline
equality including identical framing and Content serialization; and ordinary
output plus nonzero status when sidecar publication fails. Demonstrations use
the restored CLI project and each thin diagnostic view. A Debug Browser/Wasm
demonstration consumes the same evidence value. These gates do not verify the
already documented Release host-surface absence, which remains **unverified**
under the generic envelope policy.

## Hierarchy rendering and row currency

The hierarchy's declared row currency is one rooted dependency relationship
occurrence. The same selected non-root occurrence sequence supplies:

- the default Markdown dependency tree;
- standalone `--tree`;
- standalone or embedded Mermaid;
- the `Dependency Hierarchy` occurrence table;
- `--table`, `--tsv`, and `--jsonl`;
- typed and lowered JSON hierarchy occurrences; and
- `--count`.

Root headings and endpoint nodes are presentation context. Every revisit,
cycle, and ordinary dependency branch is an occurrence row because each
preserves a distinct parent-to-target explanation. Depth-boundary annotations
and window-fragment markers are context rather than additional rows.

Multi-root output is a hierarchy forest with several explicit roots, not a
synthetic semantic super-root. The host-neutral hierarchy has already bounded
expansion and classified each non-root occurrence before a renderer runs.
Renderers do not deduplicate relationships, re-run traversal, or decide which
occurrence is a revisit.

A semantic node that is also a later explicit root remains visible as a
top-level root occurrence and has its own per-root expansion. Reaching that
same node below an earlier root does not spend, merge, or suppress the later
root's occurrences.

`--rows` windows the ordered non-root occurrence sequence before every
hierarchy renderer. Required endpoint nodes and explicit root context remain
present so a selected occurrence is interpretable. When the selected
occurrence's parent row is absent, the parent endpoint is marked as a windowed
fragment. The renderer never draws an unselected parent relationship. Isolated
explicit roots remain hierarchy context but do not become invented
relationship rows.

Hierarchy ordering is deterministic and independent of rendered labels. It
uses root occurrence order, producer-issued relationship order, and
document-local occurrence identity. Source-authored text never participates in
deduplication, cycle detection, or row identity.

The heterogeneous occurrence table has one common schema:

- occurrence ID;
- root occurrence;
- parent occurrence ID;
- root-relative depth;
- source kind and typed source identity;
- relationship kind and backing relationship identity;
- target kind and typed target identity;
- occurrence disposition (`Expanded`, `Revisit`, or `Cycle`);
- resolution state; and
- evidence identity when an owner issued one.

The document-local backing relationship identity is exposed as `Edge ID` in
tables and projected views and as `edge_id` in JSONL and structured JSON.
Human columns render safe labels beside those typed fields. Typed JSON uses
discriminated endpoint identities rather than forcing type, library, project,
and package identities into one string grammar. `-D "Dependency Hierarchy"`
exposes that common schema without acquiring roots.

[#3320](https://github.com/richlander/dotnet-inspect/issues/3320) supplies the
pathological acceptance case: a shared package dependency reached through
several parents must retain every occurrence; tree output must distinguish a
revisit from a true leaf; and Mermaid must contain the explicit root and its
outgoing occurrences.

## Evidence rows and graph association

Evidence sections preserve the row currencies already owned by Package
Dependency Evidence:

- one root occurrence;
- one normalized dependency declaration;
- one declaration group;
- one restored package node;
- one owner-issued restored-project graph edge; and
- one typed failure occurrence.

`Pruning` composes one additional CLI row over those owner-issued currencies:
one normalized direct declaration plus its PackageHouse pruning applicability
or candidate-bound policy result. When a root has a selected declaration
group, only that group's declarations participate. A post-processing root such
as a restored project may report its earlier application-authored or already-
processed outcome without manufacturing a selected package-manifest group.

Candidate-free applicability runs before inventory or package-source work. It
preserves application-authored exemption, unattributed authorship, unavailable
or incomplete processing, runtime projection, and prior pruning evaluation as
distinct outcomes. Only a package-manifest declaration that reaches
`CandidateRequired` and whose root authorizes package-source candidate
resolution proceeds to inventory and candidate acquisition. Direct nuspec and
package-prefix roots remain source-bounded.

The comparison uses one exact target-bound installed inventory per request.
Candidate resolution remains declaration- and source-authorized. A policy
result of `Subsumed` projects `PlatformDelegation`; every other completed
policy result projects `PackageRetained`. `PlatformDelegation` means the policy
receipt authorizes delegation; it does not claim that PackageHouse execution,
PlatformHouse execution, payload acquisition, or dependency-edge pruning ran.

The candidate version and the platform-provided version are separate fields.
For example, candidate `System.Runtime` version `4.3.2` compared with platform
version `4.3.1` is `PackageRetained`. The platform value is never displayed as
the selected package version, and the command never downgrades the candidate.

The command does not infer a declaration from a graph edge. A restored edge may
exist without a root-authored direct declaration, and a declared constraint
may exist when no child could be resolved.

Associations use owner-issued identities:

- declarations retain their root and group identity;
- restored edges retain their selected restored-project identity;
- traversed package nodes retain the identity and source evidence issued by the
  typed package traversal owner;
- graph nodes and edges retain references to the evidence identities from
  which they were projected; and
- `InertString` remains intact until a serializer or renderer consumes it.

A combined declaration/resolution row is permitted only when the owner-issued
explicit-root evidence already associates the declaration with that resolved
package edge.
The join uses the retained root, group, dependency, selection, and package
identities, never package display text. A declared-but-unresolved dependency
renders an empty resolved value; a restored edge with no declaration remains
in `Restored Edges` and is not invented as a declaration row.

Consequently, the `Resolved` value is ordinarily populated for a restored
project root. Package and direct-nuspec declaration rows leave it empty;
transitive package traversal does not retroactively change those evidence rows.

Human output may use document-stable occurrence numbers where an opaque typed
identity would not be useful. Machine output retains the typed identity and
association fields.

## Partial results, completion, and exit status

Every explicit root gesture completes independently. A malformed, unavailable,
mapping-denied, unsupported, or failed root contributes a typed failed root and
does not suppress valid siblings.

The document reports at least, for phases selected into the request plan:

- root-set completion;
- per-root admission and evidence completion;
- graph availability;
- traversal completion;
- the requested depth, when bounded; and
- whether a producer, authorization, resolution, or depth boundary prevented
  further traversal.

When requested, pruning completion distinguishes `Complete`,
`SourceBounded`, `Partial`, and `Failed`; when omitted it is `NotRequested`.
Candidate-free abstention and explicit source boundaries are successful,
explained outcomes. Inventory failure, incomplete or failed processing, and
candidate failure or incompleteness remain typed failures and return nonzero.
An inventory producer failure is represented once in `Failures` with affected
root and declaration counts while every affected pruning row retains its
unavailable disposition.

Traversal completion distinguishes `Complete`, `DepthBounded`,
`SourceBounded`, `Partial`, `Failed`, and `NotRequested`. Explicit depth and a
root kind that deliberately exposes only owned direct evidence are successful
bounded outcomes, not failures. `Partial` and `Failed` mean that evidence
required inside the authorized boundary was lost or rejected.

`Failed` is a document-level state: traversal was requested but no applicable
root was admitted or an operation-level producer failure prevented a traversal
outcome. When at least one root has usable graph evidence, failed sibling root
attempts make the document `Partial` instead.

An empty edge set is a complete empty graph only when every applicable producer
established that the admitted roots have no dependency relationships within
the requested scope. Missing assets, rejected metadata, unavailable package
sources, unresolved declarations, and exhausted producer budgets are not empty
graphs.

Usable partial output is written to stdout. Exit status derives from the
complete retained outcome of the selected request plan: any `Partial` or
`Failed` requested phase or retained failure returns nonzero. `DepthBounded`,
`SourceBounded`, and `NotRequested` are successful states. Selecting a graph
may therefore expose traversal failure and return nonzero where a
declaration-only request succeeds; that is an observable consequence of
requesting additional work, not a presentation-dependent reinterpretation of
one completed result. Diagnostics on stderr add safe command context; they do
not replace typed failures in machine-readable output.

Standalone tree, Mermaid, table, TSV, and JSONL shapes cannot add a second
completion schema. They preserve the selected rows on stdout, emit a bounded
partial-result diagnostic on stderr, and return the same nonzero status.
Typed and lowered document JSON retain structured completion and failure
fields.

Cancellation propagates and is not converted into a failed root.

## Target framework and restored target selection

`--tfm` remains one exact selection gesture whose meaning is delegated to the
root owner:

- package and nuspec roots select one dependency group while retaining every
  normalized group in evidence;
- restored-project roots select one assets target and scope restored nodes and
  edges to it;
- library and type roots use their existing TFM-aware source resolution; and
- package-prefix roots preserve the profile producer's admitted manifest
  selection.

For `Pruning`, the same spelling must also parse as a base .NET platform target
such as `net11.0`. The command derives an exact release-band inventory request
for the selected platform family and verifies that the returned inventory
describes the requested target before resolving candidates. A mismatched or
family-incomplete inventory is a visible typed inventory failure.

No matching package group, unavailable restored target selection, and an empty
selected group remain distinct states.

When `--tfm` is omitted, a package root may retain the dependency-group owner's
package-local no-request selection, while recursive package-manifest traversal
uses `TraversalTargetFrameworkPolicy.ProductDefault(net12.0)`. Supplying
`--tfm` configures the traversal policy as well as the command's existing root
selection gesture. Candidate-acquired manifests use compatible selection
against that one traversal target; a selected lower framework never replaces
the target on a later edge.

This design does not add `--rid`. A restored target selected by existing
owner policy retains and discloses its RID. A future explicit RID gesture would
be a focused extension of this command owner.

## Output shapes and count

Markdown and typed JSON may carry the complete multi-section document.
Lowered JSON carries the same selected Markout sections. Table, TSV, and JSONL
require exactly one selected table-shaped section.

Standalone tree and Mermaid require exactly one selected hierarchy section.
The default Markdown document may combine the dependency hierarchy with evidence
tables.

Outside discovery, `--tree` selects the standalone tree rendering of
`Dependency Hierarchy`. With `-D/--discover`, the existing discovery contract
retains ownership: `--tree` renders the schema tree and does not request
dependency traversal.

Count follows the selected section's declared row currency:

- `Dependency Hierarchy` counts selected non-root relationship occurrences;
- `Dependencies` counts normalized direct declarations;
- `Pruning` counts projected direct-declaration policy rows;
- `Failures` counts failure occurrences.

In a Debug build, explicitly selected diagnostic sections retain their natural
counts: root occurrences, normalized groups, restored package nodes, or
owner-issued restored graph edges. Those count cases are unreachable through
the Release catalog.

Several selected row sets produce the existing ordered section/count table;
they do not collapse into one request-wide scalar.

Traversal depth never filters `Dependencies` or the diagnostic group,
`Pruning`, restored-package, and restored-edge projections. Those projections
describe direct owner-issued evidence for the explicit roots. Depth applies
only to `Dependency Hierarchy`. A row window may reduce rendered pruning rows, but
it does not change acquisition, the pruning summary, failure retention, or exit
status.

Count is exact only when the selected row set's completion supports an exact
answer. A depth-bounded graph can be counted exactly within that explicit
bound. A partial producer result or truncated package-prefix population cannot
be described as an exhaustive unbounded total.

`--columns` and `--fields` project selected sections after typed association is
formed. Projection never removes mandatory completion fields from
document-shaped output.

## Package-prefix boundary

Package-prefix discovery remains a bounded evidence operation. It produces
many manifest roots but does not authorize recursive expansion from every
match.

Its dependency graph therefore contains each admitted package root and its
direct declared dependency edges. Those child endpoints are unresolved
package identities unless the profile producer already supplied stronger
owner-issued evidence.

Omitted depth, `--depth 1`, and a larger maximum all produce the complete graph
available from the authorized manifest set: root-to-declaration edges ending
at unresolved package identities. Traversal completion states that expansion
ended at the source boundary. A larger depth is an upper bound, not a promise
that every root kind can supply that many levels. Direct nuspec roots use the
same rule.

The prefix summary remains visible in document output, including source,
candidate count, match count, failure count, and truncation reason. Requested
bounds are successful bounded evidence; producer failure or pagination
truncation is partial and returns nonzero.

## Migration and retirement

The completed consolidation into `depends` was intentionally breaking:

- `depends` gains asset-root, depth, section, discovery, table, and normalized
  evidence behavior;
- its graph output moves from hand-built lossy trees to one typed graph
  projection;
- positional type-to-library fallback is removed in favor of explicit
  `--library`;
- `Dependencies` moves from the separate command's single minimal section to a
  normal evidence section behind the minimal `Dependency Hierarchy`;
- `dependency-evidence` is removed as a supported command; and
- current README examples, help, demos, and product skills use `depends`.

No compatibility alias or hidden forwarding command remains for
`dependency-evidence`. Keeping both former names would preserve the conceptual
split this design removed and would leave two published entry points for one
operation.

The removed `dependency-evidence` token remains reserved because releasing it
would send a bare invocation through implicit package-target routing. The
focused invalid-input guard fails nonzero and points to `depends`; it does not
execute the replacement command or reinterpret old arguments.

That completed change was **intentionally breaking** under
[CLI Change Classification](cli-change-classification.md). Its implementation
requires a Breaking release-note entry, replacement examples, routing tests,
and machine-contract tests for the new `depends` document.

### Historical Graph placement proposal

This section and the remaining target grammar, demonstrations, and gates that
refer to `type graph`, `graph dependencies`, or retiring `depends` record the
superseded #7308 proposal. They are non-normative input to the focused
Dependency, Graph, section-naming, and default-selection follow-ons under
issues #7623, #7624, #7625, and #7628.

The next placement is also intentionally breaking:

```text
depends <type> <search scope and traversal>
  -> type graph <type> <same search scope and traversal>

depends <asset roots and traversal>
  -> graph dependencies <same roots and traversal>
```

`type graph` adopts the complete selected-Type relationship mode. The Type is
its already selected local subject, and every current package, library,
project, platform, platform-library, Extensions-package, and ASP.NET
Core-package option remains bounded search scope. `--tfm` refines the explicit
or implicit source population without suppressing the default Platform
frameworks. Its focused Graph adoption composes owner-issued Dependency
relationships into `TypeDependencyGraphContent`; that adaptation does not
transfer relationship, scope, evidence, or failure ownership from this
document.

This selected-Type cutover intentionally changes the presentation and machine
contract. Current `depends <type> --json` and `--envelope` expose the versioned
`type-dependencies` result whose Content is `TypeDependencySectionResult`.
That Content carries the complete `AssemblyContextTypeDependencyResult`—the
matched dependency relationships plus ordered completed/rejected participant
outcomes and their provenance—and the separate
`TypeDependencyRowSelectionResult`. `DependencyGraphDocument` is only the
later human-rendering projection and is not the current machine content.
Target `type graph --json` and `--envelope` expose the same
`TypeDependencyGraphContent`; envelope mode wraps it in
`InspectionEnvelope<TypeDependencyGraphContent>`. The Dependency owner defines
this wrapper:

```text
TypeDependencyGraphContent
  Outcome: Available | NotFound | Ambiguous | Unavailable
  Graph: InspectionGraphDocument? // Available only
  Participants: ordered TypeDependencyGraphParticipantOutcome[]
  RowSelection: NotApplicable | Selected | Failed
```

`Available` requires one resolved owner-issued Type seed and a non-null complete
unwindowed `Graph`. `NotFound`, `Ambiguous`, and `Unavailable` require
`Graph = null`; they retain their typed outcome instead of fabricating a seed
from user text. `Unavailable` includes the all-participants-rejected case.

Each participant outcome preserves the owner-issued subject identity and
provenance plus completed or rejected status and any typed failure.
`RowSelection.Selected` preserves the requested selection order as stable Graph
edge identities; `Failed` preserves the typed semantic-selection failure; and
`NotApplicable` is used when no available Graph exists. Selection never
replaces or truncates `Graph`.

Human graph, table, and row projections consume `RowSelection` against
`Graph`. Unprojected JSON and envelope Content serialize the complete wrapper,
so both expose identical topology, participant, provenance, and selection
semantics. The wrapper is Dependency-owned; this migration does not add
participant or selection fields to shared `InspectionGraphDocument`.

The replacement envelope registers:

```text
result_kind: "type-graph"
schema_version: 1
content: TypeDependencyGraphContent
```

It does not reuse `type-dependencies` version 1. Plain `--json` serializes the
same `TypeDependencyGraphContent` without envelope framing.

Structural discovery uses the Graph section and field schemas rather than the
Dependency section catalog.
Effective discovery is a new target-only Type Graph capability because current
selected-Type `depends` rejects `-D --effective`; it has no old-command schema
baseline. Obsolete selected-Type section or field names fail with `type graph`
discovery guidance; they do not forward or silently select a similarly named
Graph projection.

The migration gate compares the selected subject, base-type and interface
relationships, search-scope behavior, traversal depth, participant completion
and rejection outcomes, provenance, row-selection success or typed failure,
exit status, and supported output-format classes. The adapter maps the complete
relationship set into `Graph`, ordered participant outcomes into
`Participants`, and selected relationship ordinals into stable Graph edge
identities in `RowSelection`. A selection failure produces no selected edge
identities and retains its typed stage, required position, available count, and
row-set identity. Missing, ambiguous, and unavailable requests retain their
typed `Outcome`, participant evidence, nonzero exit status, and null Graph.

Separate before/after machine-contract fixtures prove that the old
`TypeDependencySectionResult` schema and structural discovery remain stable
until retirement and that the replacement emits the versioned
`TypeDependencyGraphContent` schema with equivalent complete topology,
participant/provenance, and row-selection outcomes. A fixture with two
relationships and `--rows 2..2` proves that `Graph` retains both edges while
`RowSelection` names exactly the second. Missing-Type, ambiguous-Type, and
all-participants-rejected fixtures prove null Graph, typed Outcome, preserved
participants, and nonzero status without constructing a seed. Envelope
fixtures assert `result_kind: "type-graph"` and `schema_version: 1`. A separate
target-only gate proves bounded effective discovery. Byte-for-byte rendering,
old section names, old row fields, and the old JSON or envelope content type
are intentionally not parity requirements.

`graph dependencies` adopts the complete asset dependency mode. It does not
construct a Workspace, consume a Workspace packet, or convert the Dependency
document into `InspectionGraphDocument`. All existing explicit roots,
traversal, sections, row semantics, partial failures, output formats, and
source policies remain owned here.

The cutover:

1. adds the Dependency-backed `type graph` relationship family with complete
   selected-Type semantic, scope, traversal, failure, exit-status, and
   output-format coverage plus the explicit Inspection Graph schema transition;
2. adds `graph dependencies` with full asset dependency mode parity;
3. updates help, discovery, completion, README examples, demos, replay or
   sharing surfaces, and product skills;
4. removes `depends` only after both replacement routes are complete, without
   a forwarding alias or hidden fallback; and
5. reserves the removed `depends` token so obsolete input fails nonzero and
   points to `type graph` or `graph dependencies` according to whether a
   positional Type was supplied, without executing either replacement or
   reinterpreting its arguments.

Selected-Type semantic and capability coverage plus the machine-schema
transition are Release gates for step 5; asset-root parity is a Release gate
for step 8 of
[#7308](https://github.com/richlander/dotnet-inspect/issues/7308).
Obsolete-token routing is part of step 9. Missing support for any currently
admitted selected-Type scope, relationship/evidence fact, traversal, typed
failure, exit status, or output-format class blocks retirement. Its old section
names, fields, and machine schema instead follow the explicit rejection and
before/after transition above. The asset route still requires full root,
section, row, failure, and output parity.

The [Dependency Evidence CLI](dependency-evidence-cli.md) document is
historical. This document is the sole command owner.

## Demonstration

The target asset-root experience combines traversal and evidence under
`graph dependencies`:

```console
$ dotnet-inspect graph dependencies --project ./src/App/App.csproj \
    --depth 2 -S "Dependency Hierarchy" -S Dependencies

# App dependencies

**Target:** net10.0
**Roots:** 1 complete
**Traversal:** complete through depth 2

## Dependency Hierarchy

App
└─ Microsoft.Extensions.Hosting 10.0.0
   ├─ Microsoft.Extensions.Configuration 10.0.0
   └─ Microsoft.Extensions.Logging 10.0.0

## Dependencies

| Root | Framework | Package | Constraint | Resolved |
| --- | --- | --- | --- | --- |
| App | net10.0 | Microsoft.Extensions.Hosting | 10.* | 10.0.0 |
```

The neighboring direct-only package case uses the same route and hierarchy
occurrence currency:

```console
dotnet-inspect graph dependencies \
  --package Microsoft.Extensions.Hosting@10.0.0 \
  --depth 1 --table
```

The DAG case must not erase a repeated dependency:

```text
App
├─ Package.A
│  └─ Shared
└─ Package.B
   └─ Shared ↩
```

Both `Package.A -> Shared` and `Package.B -> Shared` remain distinct
root-relative occurrences and appear in the occurrence table, Mermaid, JSON,
row-window, and count output.

The explicit pruning projection shows direct-declaration policy evidence
without changing that graph:

```console
$ dotnet-inspect graph dependencies --package Some.Package@1.2.3 \
    --tfm net11.0 -S Pruning

## Pruning

| Package | Constraint | Candidate | Platform Provides | Disposition |
| --- | --- | --- | --- | --- |
| System.Text.Json | [9.0.0] | 9.0.0 | 11.0.0 | PlatformDelegation |
| System.Runtime | [4.3.2] | 4.3.2 | 4.3.1 | PackageRetained |
```

The second row is not a downgrade: `4.3.2` remains the package candidate, and
the older platform-supplied version explains why the package path is retained.

The selected-Type workflow instead begins with its local subject:

```console
dotnet-inspect type graph System.Int128 --platform --tfm net10.0 \
  --depth 2 --table
```

Package, library, project, platform, platform-library, Extensions-package, and
ASP.NET Core-package options remain search scope for this route. `--tfm`
refines explicit sources or the implicit Platform default; it does not become a
source selector or asset root. The base-type/interface relationships compose
into the Inspection Graph document rather than the asset Dependency document
above.

## Evidence and gates

The implementation slices must provide focused gates. Product correctness
runs in Release; the compile-time diagnostic registration contract also gets a
targeted Debug-build probe.

| Claim | Gate |
| --- | --- |
| `type graph` source options remain search scopes; bare `--platform` selects all Platform frameworks; valued `--platform <library>` selects one Platform library; TFM-only input refines the implicit Platform default without suppressing it; `graph dependencies` options become asset roots; route-invalid options fail. | Product-entry parser and execution matrix covering both meanings of `--package`, `--library`, and `--project`; bare `--platform`; valued `--platform System.Private.CoreLib`; repeatable `--platform-library`; `--extensions`; `--aspnetcore`; `--tfm net10.0` with no explicit source; explicit source plus `--tfm`; and rejected cross-route gestures. |
| Selected-Type migration preserves subject, relationship/evidence facts, scope, traversal, participant completion/rejection and provenance, row-selection outcomes, typed failures, exit status, and output-format classes while intentionally replacing the `TypeDependencySectionResult` JSON/envelope with `TypeDependencyGraphContent` and Dependency structural discovery with Graph discovery. | Fixed-fixture before/after Release contracts for text, Markdown, table, JSON, envelope, and structural discovery; machine fixtures assert complete `Graph`, ordered `Participants`, and `RowSelection`, including two relationships with only the second selected and typed strict-window failure; missing, ambiguous, and unavailable cases assert typed Outcome with null Graph and preserved participants; envelope fixtures assert result kind `type-graph` schema version 1; obsolete selected-Type section and field names reject with discovery guidance. |
| Type Graph effective discovery is a new bounded capability rather than a claimed old/new migration surface. | Target-only Release gate for effective Graph discovery plus a current-command guard proving selected-Type `depends -D --effective` remains rejected until retirement. |
| Omitted selected-Type depth retains complete traversal semantics while explicit positive depth remains finite. | Release fixtures compare depthless `depends` and `type graph` over a finite cyclic hierarchy, assert identical closure and completion, then inject an owner work limit and assert partial topology plus typed incompleteness without `queries.neighborhood-complete`; explicit depths 1 and 2 retain current boundaries. |
| One `type graph` subject resolves to one owner-issued seed or a typed ambiguity/failure. | Multi-source type fixture with equal display names and distinct typed identities. |
| `.csproj` and direct assets with identical bytes produce equivalent graph and evidence identities except locator provenance. | CLI tests over the same checked-in restored assets fixture through both locators. |
| Restored-project depth is measured from the explicit project through project-reference and package edges. | #5998 fixture containing `App -> ProjectB -> PackageC`, asserted at depths 1, 2, and unbounded without opening package manifests. |
| Missing restored assets fail visibly without changing valid sibling results. | Multi-root CLI test with one unrestored project and one valid root. |
| `--depth 1` performs no deeper package-manifest acquisition. | Instrumented package-source test that fails if a child manifest is requested. |
| Evidence-only selection performs no transitive acquisition. | Instrumented package-source test selecting `Dependencies` without `Dependency Hierarchy`. |
| License completion includes root-set admission and treats an explicit depth boundary as a successful bounded inventory. | Release CLI tests combining one valid and one missing nuspec root, plus a depth-one direct-nuspec graph with a known deeper frontier. |
| License manifest acquisition failures retain unavailable answer rows and enter `Failures` with the exact package coordinate and typed reason. | Release restored-project test with one available and one missing exact manifest, asserted across JSON, nonzero status, and exact-count rejection. |
| Pruning is explicit-only and does not enter `@Dependencies`, verbosity, or bare effective discovery. | Release catalog, category, structural/effective discovery, and no-inventory tests. |
| Candidate-free pruning outcomes perform no inventory or candidate work. | Restored-project application-authorship and direct-nuspec source-boundary tests with throwing producers. |
| `Subsumed` delegates, while an older platform-supplied version retains the newer package candidate. | CLI projection test for `System.Text.Json@9.0.0` against platform `11.0.0` and `System.Runtime@4.3.2` against platform `4.3.1`. |
| Inventory failure and target mismatch remain typed failures and prevent candidate work. | Instrumented inventory tests asserting nonzero status, affected declarations, and `Failures` rows. |
| Row windows and rendering formats do not reinterpret pruning policy. | Markdown, table, typed JSON, and one-row window tests over the same two policy outcomes. |
| Multi-root depth is preserved per root occurrence rather than by one global distance. | Cyclic DAG fixture in which one shared node is reached at different depths from two roots. |
| A semantic node that is both a transitive child and a later explicit root does not duplicate or suppress either root's occurrence rows. | Depth-asymmetric two-root fixture run in both root orders, asserting independent per-root expansion and equal tree/table/JSON/count cardinality. |
| Shared DAG targets retain every parent occurrence and roots survive Mermaid lowering. | #3320 fixture across Markdown tree, Mermaid, occurrence table, JSON, count, and row selection. |
| Declaration constraints remain when child resolution is unavailable. | Package or nuspec test with a valid declaration and unavailable child expansion. |
| Restored-edge identity survives independently of command graph-edge projection. | Direct-assets owner-level identity assertions and command graph projection assertions over the same owner-issued edge. |
| Retail builds expose only sections with documented consumer scenarios. | Release catalog, category, exact and wildcard selection, discovery/schema, verbosity, count-order, and typed JSON absence tests; Debug exact-selection tests for the diagnostic farm team. |
| Partial or truncated evidence never renders or counts as complete. | Multi-root and package-prefix completion tests across Markdown and typed JSON. |
| Source-authored labels remain inert and never supply graph identity. | Existing hostile-text fixtures extended through graph, evidence, and JSON sinks. |
| Removed commands cannot enter implicit package routing or forward obsolete input. | Product-entry reservation tests for `dependency-evidence` and `depends`, including selected-Type and asset-root guidance. |

Documentation-only #5993 is gated by Markdown lint. These named product gates
become obligations of the implementation slices; they do not claim current
behavior before those slices land.

## Non-claims

This design does not:

- redefine package-manifest, restored-project, Metadata, source-policy, or
  Markout semantics;
- make one dependency model span type hierarchy, assembly references, and
  package declarations below the CLI composition boundary;
- add automatic restore, build, project evaluation, or package mutation;
- apply pruning to dependency graph edges or claim that PackageHouse or
  PlatformHouse execution occurred;
- add a Browser/Wasm command or presentation contract;
- make package-prefix discovery an exhaustive package universe;
- guarantee that every root kind can expand beyond the evidence it owns; or
- retain obsolete command syntax solely for compatibility.
