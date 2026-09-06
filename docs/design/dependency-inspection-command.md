# Dependency inspection command

This document owns the target CLI dependency operation tracked by
[#5993](https://github.com/richlander/dotnet-inspect/issues/5993).

**Status:** design target. The current `depends` and `dependency-evidence`
commands remain implemented separately until the migration slices named here
land.

## Owner and claim

The `depends` command owns one CLI operation:

> Admit explicitly named dependency subjects and assets, project their
> owner-issued relationships and evidence, and apply one traversal, section,
> row, and rendering contract.

Traversal and evidence are not separate operations. Traversal selects which
reachable relationships participate; sections and verbosity select how much
evidence about those relationships and their roots is disclosed.

This owner defines:

- command grammar and mode selection;
- explicit root and source-scope gestures;
- root-set lifetime and partial-failure behavior;
- traversal direction and depth;
- the dependency graph document and its CLI section composition;
- row selection, count, output-format eligibility, diagnostics, and exit
  status; and
- migration from the two current commands to one supported `depends` surface.

It consumes owner-issued facts and does not redefine their construction:

- [Package Dependency Evidence](package-dependency-evidence.md) owns normalized
  package, nuspec, and restored-project declarations, resolution evidence,
  identity, completion, failures, and `InertString` containment.
- [Restored Project Dependency Facts](restored-project-dependency-facts.md)
  owns exact `project.assets.json` target selection, package nodes, and graph
  edges.
- Metadata and Services own type hierarchy, assembly-reference, and package
  resolution facts.
- [Package Source Model](package-source-model.md) owns source authorization,
  authority identity, transport, and source-result association.
- The exact dependency-candidate adapter tracked by
  [#5765](https://github.com/richlander/dotnet-inspect/issues/5765) owns
  source-authorized package version-range resolution and exact acquisition
  candidates.
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

The two current commands divide one user question along an implementation
boundary:

- `depends` follows reachable relationships but drops most declaration,
  constraint, provenance, completion, and failure evidence; and
- `dependency-evidence` retains that evidence but does not expand package
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
[Command Transition Model](command-transition-model.md) keeps them within
`depends`.

The current `dependency-evidence` design used heterogeneous root cardinality to
justify a separate command. This target supersedes that conclusion. Root-set
cardinality is source context inside the dependency operation: one or several
roots still produce the same dependency document, per-root completion, graph
edges, and evidence row families. Type relationship mode and asset dependency
mode use different admission plans, but they now close into that same
top-level outcome envelope and the same traversal/disclosure axes. The
unification is justified by removing the incompatible result and presentation
shapes that previously made those plans appear to be separate operations.

## Consumer, tracker, and delivery

The production consumer is the `depends` CLI command.

The shared evidence substrate is implemented by
[#5533](https://github.com/richlander/dotnet-inspect/issues/5533), and
[#5532](https://github.com/richlander/dotnet-inspect/issues/5532) remains the
end-to-end dependency-evidence tracker. Browser/Wasm adoption remains owned by
[#5535](https://github.com/richlander/dotnet-inspect/issues/5535); this
CLI-focused design neither changes nor blocks that host.

The delivery plan has seven steps:

1. Lock this command contract in #5993.
2. Supply the shared declaration-to-exact-candidate handoff under
   [#5765](https://github.com/richlander/dotnet-inspect/issues/5765).
3. Define typed package dependency traversal under
   [#5996](https://github.com/richlander/dotnet-inspect/issues/5996).
4. Define typed restored-project root and project-reference traversal under
   [#5998](https://github.com/richlander/dotnet-inspect/issues/5998).
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

This is an alternative to the current two-command architecture. Adoption is
not complete until the old command and its command-specific projection path are
removed.

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
those assets does not turn unavailable evidence into a false complete graph.

An explicit remote package root remains the authorization for package
acquisition, including its current complete traversal when no depth is
supplied. Section selection may demand traversal, but it never grants network
authority; the explicit root gesture does. Selecting evidence without the graph
does not acquire transitive package manifests.

The executed request plan is nevertheless section-dependent, as progressive
disclosure requires. Selecting `Dependency Graph` requests traversal;
selecting only evidence sections does not. Completion, failures, and exit
status describe the producers that the selected plan actually ran. A phase
that was not requested is `NotRequested`, not complete, failed, or silently
omitted.

## Command modes

`depends` has two modes selected by whether the positional type subject is
present.

### Type relationship mode

```console
dotnet-inspect depends <type> \
  [--package <package>]... \
  [--library <library>]... \
  [--project <project>]... \
  [--platform [<framework>...]]
```

The positional type is the focus. Package, library, project, and platform
options identify the bounded search scope in which that type and its base-type
or interface relationships are resolved. They are not graph roots.

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
| `--tfm` | Source selection | Root-owner selection |
| `--depth` | Hierarchy traversal bound | Dependency traversal bound |
| NuGet source options | Accepted when package scope consumes them | Accepted when the selected plan performs remote package acquisition |
| `-D`, `-S`, verbosity, rows, count, and output formats | Dependency document projection | Dependency document projection |

`--depth` is valid only when `Dependency Graph` is selected directly or through
the active verbosity preset. A depth supplied to an evidence-only request
fails as an unused operation gesture.

One positional type gesture produces exactly one root attempt. The type
resolver must return one owner-issued selected type identity or a typed
not-found, ambiguous, or rejected outcome before traversal begins. Candidate
assemblies supplied by scope options are search participants, not roots.
`--depth` counts from that one selected type node.

## Asset admission and expansion authority

Each root gesture authorizes only the acquisition implied by that root and its
ordinary source policy. Traversal depth can narrow that work; it cannot invent
authority that the root did not grant.

| Root | Admitted evidence | Expansion authority |
| --- | --- | --- |
| Remote package ID or coordinate | Exact package identity and manifest dependency groups | Authorized package sources may resolve reachable package manifests up to the requested depth. |
| Local `.nupkg` | Validated archive identity and root manifest | Ordinary package-source policy may resolve reachable package dependencies up to the requested depth. |
| Direct `.nuspec` | Self-attested manifest identity and dependency groups | The file gesture authorizes only that nuspec. Its declared dependency edges terminate as unresolved boundaries. |
| `.csproj` or project directory | Existing located `project.assets.json`, with locator provenance | The selected restored graph is traversed without package or project acquisition. |
| Direct `project.assets.json` | Exact restored-project facts with direct-assets provenance | The selected restored graph is traversed without package or project acquisition. |
| Library | Owner-issued assembly identity and direct references | Existing library-reference resolution may follow resolvable references up to the requested depth. |
| Package prefix | Bounded package-profile roots and their manifest declarations | Profile acquisition authorizes the bounded manifests, not recursive expansion from every match. |
| Type | Owner-issued type and hierarchy relationships in the selected scope | Traversal stays within the admitted search scope. |

A `.csproj` and a direct assets path that select identical bytes produce the
same restored dependency facts and graph. They retain different locator
provenance. A `.csproj` is not parsed as the dependency graph and does not
authorize project evaluation; it is a locator for already-restored assets.

Supplying package-source options when no root can consume them fails rather
than silently accepting an inert gesture. Direct nuspec, restored-project,
library-only, type-only, and package-prefix requests do not acquire package
manifests merely because a source option is present.

A local `.nupkg` can consume source options only when `Dependency Graph` is
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
the `Roots` and `Failures` sections but is not invented as a semantic graph
node. A root admitted before a later projection or traversal failure retains
its graph node and the associated failure.

Root identity is owner-issued:

- package and restored-project roots retain their query identities;
- configured package-source association remains source evidence, not root
  identity;
- library and type roots retain their Metadata or resolution identities; and
- document occurrence numbers are presentation addresses, never substitutes
  for semantic identity.

Two root gestures that resolve to the same semantic subject remain two root
occurrences with shared graph identity. Their occurrence order and provenance
remain visible without duplicating the semantic node.

The command graph uses two identity layers:

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

For fixed graph evidence such as `project.assets.json`, omitted depth means the
complete selected restored graph already present in the asset. It never means
opening every resolved package. In that case depth bounds graph admission over
already-materialized evidence rather than upstream acquisition. For direct
nuspec and package-prefix roots, the available graph ends at their declared
dependency edges because those gestures do not authorize recursive
acquisition.

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

A semantic node that is also an explicit root remains one graph node with a
separate root occurrence. Tree lowering renders that occurrence as its own
top-level tree even when the same node also appears below another root.

## One dependency document

The command constructs one immutable dependency document for the selected
request plan:

```text
ordered explicit root occurrences
  + typed semantic nodes
  + typed directed dependency edges
  + owner-issued declaration and resolution evidence
  + root, acquisition, projection, and traversal failures
  + root-set and traversal completion
        |
        v
section selection and row shaping
        |
        v
Markdown / tree / Mermaid / table / TSV / JSONL / JSON / count
```

The document is a CLI composition result, not a new host-neutral dependency
semantics model. It carries references to or copies of owner-issued identities
and evidence. It may add command-owned occurrence indices, graph endpoint
indices, section membership, and presentation ordering.

The CLI owns traversal gestures and presentation composition, not reusable
dependency algorithms. Type, library, package, and restored-project owners
continue to produce their typed relationships and identities. If an
implementation slice needs a new reusable traversal or normalization
algorithm, that work belongs in a focused owner below the CLI rather than in a
second host-local implementation. Browser/Wasm continues to consume the
host-neutral evidence query and does not consume this CLI document.

Normalized evidence sections have one stable universe: explicit admitted roots
only. Selecting `Dependency Graph` may acquire or admit transitive graph
subjects, but it does not add those subjects' manifests to `Dependencies`,
`Dependency Groups`, `Restored Packages`, or `Restored Edges`. Adding or
removing the graph section therefore never changes those already-selected
evidence row sets. `Failures` is plan-relative: selecting traversal can add
typed traversal failures that an evidence-only plan never produced. For
restored-project roots, the explicit root's owner-issued evidence already
contains the selected restored package nodes and edges.

The same semantic node may be reached from several parents. It appears once in
the node set, and every directed relationship appears as its own edge. A tree
projection may repeat a node label with a revisit marker for readability, but
the underlying graph and edge rows remain lossless.

Section resolution happens before producer execution. The acquisition plan is
the union of producers required by the selected sections and the traversal
bound. An evidence-only request does not run transitive traversal, while a
graph request acquires only the facts needed for its requested depth. Once that
plan completes, one immutable document supplies every selected view without
rerunning a producer.

The graph is usable when some evidence sections are unavailable, and evidence
is usable when traversal stops at an unresolved boundary. Neither projection
turns the other's incompleteness into success-shaped absence.

## Sections and disclosure

The command uses ordinary verbosity and section selection instead of adding
`--evidence` or `--details`. Those proposed flags would duplicate an axis
already owned by progressive disclosure and would not say which evidence is
wanted.

The base section ladder is:

| Section | Declared row | Default visibility |
| --- | --- | --- |
| `Dependency Graph` | One directed logical dependency edge. | Minimal |
| `Roots` | One explicit root occurrence with identity, provenance, state, and completion. | Normal |
| `Dependencies` | One normalized direct declaration. | Normal when applicable |
| `Restored Edges` | One owner-issued restored-project graph edge. | Normal when applicable |
| `Failures` | One typed root, acquisition, projection, or traversal failure occurrence. | Normal when present |
| `Dependency Groups` | One normalized framework-scoped declaration group. | Detailed when applicable |
| `Restored Packages` | One owner-issued restored package node with role and coordinate. | Detailed when applicable |

`Dependency Graph` is the command's single high-value minimal section. It
preserves the current reason to invoke `depends`: seeing what depends on what.

`-v:n` adds the evidence needed to interpret that graph without making every
group and package node part of the default view. `-v:d` adds the complete
applicable base evidence.

The existing `@Dependencies` category contains `Dependency Graph`, `Roots`,
`Dependencies`, `Restored Edges`, `Failures`, `Dependency Groups`, and
`Restored Packages`. A caller that wants evidence without traversal selects
the evidence sections it needs:

```console
dotnet-inspect depends --project ./App.csproj \
  -S Roots -S Dependencies -S "Restored Edges"
```

Root-set completion and the state of every requested phase are mandatory
document fields at every verbosity. They remain visible when the selected
graph or evidence rows are empty or partial. A traversal phase omitted by
section planning renders as `NotRequested`.

For `depends`, `@Dependencies` is the base category. The same category name may
have different authored membership in another command; package inspection
continues to use its own dependency-section membership. Automatic verbosity
selects only the `depends` base category.

`Dependency Graph` declares conditional acquisition cost. It is network-free
for restored assets and already-admitted local facts, and package-acquiring
when an expandable package root requires another manifest. A plain `-D`
remains structural and network-free. Bare `-S` includes the graph only when
the effective root plan can produce it without network acquisition; the
explicit remote `--package` gesture and ordinary default `-v:m` continue to
authorize the package traversal they request.

Type and library roots may not have package-declaration sections. Static
discovery lists the structural command catalog; effective discovery reports
which sections are applicable to the admitted root kinds and available
evidence.

## Graph rendering and row currency

The graph's declared row currency is one directed logical dependency edge.
The same selected edge sequence supplies:

- the default Markdown dependency tree;
- standalone `--tree`;
- standalone or embedded Mermaid;
- the `Dependency Graph` edge table;
- `--table`, `--tsv`, and `--jsonl`;
- typed and lowered JSON graph edges; and
- `--count`.

Tree nodes, root headings, revisit markers, cycle markers, depth boundaries,
and disconnected-component headings are presentation context. They are not
additional rows.

Multi-root output is a graph with several explicit roots, not a synthetic
semantic super-root. Tree lowering renders a spanning forest in root occurrence
order and emits every selected logical edge exactly once. The renderer tracks
emitted edges, not globally expanded nodes. When it reaches an already-emitted
edge whose target still leads, for the current root occurrence, to an
unrendered selected edge, it inserts a non-row revisit connector and continues
until that edge can be rendered. A revisit connector must lead to at least one
unrendered selected edge, so connector traversal is finite. When no such edge
remains, the revisit marker terminates that branch.

A later explicit root whose selected outgoing edges were all emitted below an
earlier root remains visible as a top-level root/revisit marker without
duplicating those edge rows. Root order may choose the branch under which an
edge is first rendered, but it does not change edge cardinality or identity.
Mermaid and edge-table lowering retain all disconnected components.

`--rows` windows the ordered edge sequence before every graph renderer.
Required endpoint nodes and explicit root context remain present so a selected
edge is interpretable. Tree lowering renders each connected component induced
by the selected edges and marks it as a windowed fragment when its original
root path is absent. It never draws an unselected connecting edge. Isolated
explicit roots remain graph context but do not become invented edge rows.

Graph ordering is deterministic and independent of rendered labels. It uses
root occurrence order, owner-issued semantic node identity, relationship or
evidence identity when available, and a document-local edge ordinal.
Source-authored text never participates in deduplication, cycle detection, or
row identity.

The heterogeneous edge table has one common schema:

- root occurrence set;
- source kind and typed source identity;
- relationship kind;
- target kind and typed target identity;
- minimum depth;
- resolution state; and
- evidence identity when an owner issued one.

Human columns render safe labels beside those typed fields. Typed JSON uses
discriminated endpoint identities rather than forcing type, library, project,
and package identities into one string grammar. `-D "Dependency Graph"`
exposes that common schema without acquiring roots.

[#3320](https://github.com/richlander/dotnet-inspect/issues/3320) supplies the
pathological acceptance case: a shared package dependency reached through
several parents must retain every edge; tree output must distinguish a revisit
from a true leaf; and Mermaid must contain the explicit root and its outgoing
edges.

## Evidence rows and graph association

Evidence sections preserve the row currencies already owned by Package
Dependency Evidence:

- one root occurrence;
- one normalized dependency declaration;
- one declaration group;
- one restored package node;
- one owner-issued restored-project graph edge; and
- one typed failure occurrence.

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

No matching package group, unavailable restored target selection, and an empty
selected group remain distinct states.

When `--tfm` is omitted, package-manifest traversal uses the package
dependency-group owner's per-manifest default selection. Each package retains
its selected framework, and the resulting graph does not claim one shared
target framework.

This design does not add `--rid`. A restored target selected by existing
owner policy retains and discloses its RID. A future explicit RID gesture would
be a focused extension of this command owner.

## Output shapes and count

Markdown and typed JSON may carry the complete multi-section document.
Lowered JSON carries the same selected Markout sections. Table, TSV, and JSONL
require exactly one selected table-shaped section.

Standalone tree and Mermaid require exactly one selected graph section. The
default Markdown document may combine the dependency graph with evidence
tables.

Outside discovery, `--tree` selects the standalone tree rendering of
`Dependency Graph`. With `-D/--discover`, the existing discovery contract
retains ownership: `--tree` renders the schema tree and does not request
dependency traversal.

Count follows the selected section's declared row currency:

- `Dependency Graph` counts selected logical edges;
- `Roots` counts explicit root occurrences;
- `Dependencies` counts normalized direct declarations;
- `Failures` counts failure occurrences;
- `Dependency Groups` counts normalized groups;
- `Restored Packages` counts restored package nodes; and
- `Restored Edges` counts owner-issued restored graph edges.

Several selected row sets produce the existing ordered section/count table;
they do not collapse into one request-wide scalar.

Traversal depth never filters `Dependencies`, `Dependency Groups`,
`Restored Packages`, or `Restored Edges`. Those sections describe the complete
owner-issued evidence for the explicit roots. Depth applies only to
`Dependency Graph`.

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

The final command surface is intentionally breaking:

- `depends` gains asset-root, depth, section, discovery, table, and normalized
  evidence behavior;
- its graph output moves from hand-built lossy trees to one typed graph
  projection;
- positional type-to-library fallback is removed in favor of explicit
  `--library`;
- `Dependencies` moves from the separate command's single minimal section to a
  normal evidence section behind the minimal `Dependency Graph`;
- `dependency-evidence` is removed as a supported command; and
- current README examples, help, demos, and product skills use `depends`.

No compatibility alias or hidden forwarding command remains. Keeping both
names would preserve the conceptual split this design removes and would leave
two published entry points for one operation.

The removed `dependency-evidence` token remains reserved because releasing it
would send a bare invocation through implicit package-target routing. The
focused invalid-input guard fails nonzero and points to `depends`; it does not
execute the replacement command or reinterpret old arguments.

The change is **intentionally breaking** under
[CLI Change Classification](cli-change-classification.md). Its implementation
requires a Breaking release-note entry, replacement examples, routing tests,
and machine-contract tests for the new `depends` document.

The current
[Dependency Evidence CLI](dependency-evidence-cli.md) document remains the
implementation contract until the retirement slice lands. At that point it
becomes historical and this document is the sole command owner.

## Demonstration

The target project experience combines traversal and evidence without changing
commands:

```console
$ dotnet-inspect depends --project ./src/App/App.csproj \
    --depth 2 -S "Dependency Graph" -S Dependencies

# App dependencies

**Target:** net10.0
**Roots:** 1 complete
**Traversal:** complete through depth 2

## Dependency Graph

App
└─ Microsoft.Extensions.Hosting 10.0.0
   ├─ Microsoft.Extensions.Configuration 10.0.0
   └─ Microsoft.Extensions.Logging 10.0.0

## Dependencies

| Root | Framework | Package | Constraint | Resolved |
| --- | --- | --- | --- | --- |
| App | net10.0 | Microsoft.Extensions.Hosting | 10.* | 10.0.0 |
```

The neighboring direct-only package case uses the same command and graph row
currency:

```console
dotnet-inspect depends --package Microsoft.Extensions.Hosting@10.0.0 \
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

Both `Package.A -> Shared` and `Package.B -> Shared` remain graph edges and
appear in edge-table, Mermaid, JSON, row-window, and count output.

## Evidence and gates

The implementation slices must provide focused Release gates for:

| Claim | Gate |
| --- | --- |
| Type-present options remain source scopes; type-absent options become roots; mode-invalid options fail. | Product-entry parser and execution matrix covering both meanings of `--package`, `--library`, and `--project`, plus rejected cross-mode gestures. |
| One positional type gesture resolves to one owner-issued root or a typed ambiguity/failure. | Multi-source type fixture with equal display names and distinct typed identities. |
| `.csproj` and direct assets with identical bytes produce equivalent graph and evidence identities except locator provenance. | CLI tests over the same checked-in restored assets fixture through both locators. |
| Restored-project depth is measured from the explicit project through project-reference and package edges. | #5998 fixture containing `App -> ProjectB -> PackageC`, asserted at depths 1, 2, and unbounded without opening package manifests. |
| Missing restored assets fail visibly without changing valid sibling results. | Multi-root CLI test with one unrestored project and one valid root. |
| `--depth 1` performs no deeper package-manifest acquisition. | Instrumented package-source test that fails if a child manifest is requested. |
| Evidence-only selection performs no transitive acquisition. | Instrumented package-source test selecting direct evidence sections without `Dependency Graph`. |
| Multi-root depth is preserved per root occurrence rather than by one global distance. | Cyclic DAG fixture in which one shared node is reached at different depths from two roots. |
| A semantic node that is both a transitive child and a later explicit root does not duplicate or suppress edge rows in tree output. | Depth-asymmetric two-root graph fixture run in both root orders, asserting one rendering per selected logical edge and equal tree/table/JSON/count cardinality. |
| Shared DAG nodes retain every edge and roots survive Mermaid lowering. | #3320 graph fixture across Markdown tree, Mermaid, edge table, JSON, count, and row selection. |
| Declaration constraints remain when child resolution is unavailable. | Package or nuspec test with a valid declaration and unavailable child expansion. |
| Restored-edge identity survives independently of command graph-edge projection. | Direct-assets typed JSON and `Restored Edges` section assertions over the same owner-issued edge. |
| Partial or truncated evidence never renders or counts as complete. | Multi-root and package-prefix completion tests across Markdown and typed JSON. |
| Source-authored labels remain inert and never supply graph identity. | Existing hostile-text fixtures extended through graph, evidence, and JSON sinks. |
| The removed command cannot enter implicit package routing. | Product-entry reservation test for `dependency-evidence`. |

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
- add a Browser/Wasm command or presentation contract;
- make package-prefix discovery an exhaustive package universe;
- guarantee that every root kind can expand beyond the evidence it owns; or
- retain obsolete command syntax solely for compatibility.
