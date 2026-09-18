# Relationship section naming

## Status

This document is the normative owner for naming user-visible relationship
sections by semantic result shape. It establishes the naming contract before
the Dependency, Library, Graph, Subject Relations, View Facet, CLI, and Browser
owners adopt it through focused follow-on work.

Current product spellings remain current behavior until their owning component
adopts this contract. Examples marked **target** describe that settled
direction; they do not claim that the spelling is already executable.

## Normative owner and claim

Relationship Section Naming owns one claim:

> After a relationship operation and semantic result shape are known, its
> section name identifies whether the result is direct evidence, a rooted
> hierarchy, identity-preserving topology, or an optional analysis without
> depending on the command entrance or renderer.

This owner defines the grammar, conventional exceptions, and compatibility
invariants for those names. It does not own relationship evidence, subject
resolution, traversal, graph identity, defaults, section selection, rendering,
or host interaction.

The grammar applies equally to a top-level operation and a curated subject
section. An equivalent Graph result is `Call Graph` whether it was requested
through `graph` or through a Member section. An equivalent Depends result is
`Dependency Hierarchy` whether its roots came from `depends` or from a Package
section.

## Why result shape belongs in the name

The same relationship domain can answer materially different questions:

- **Direct evidence:** which dependencies did this package declare?
- **Rooted explanation:** through which parent chain does this root reach a
  dependency?
- **Topology:** which unique subjects and typed edges form the selected
  dependency network?
- **Analysis:** where are calls concentrated strongly enough to form a useful
  direct-use cluster?

Calling all four `Dependencies`, or changing their meaning with `--tree`, hides
the semantic distinction in a renderer switch. Calling a rooted result
`Dependency Graph` because its internal carrier has nodes and edges hides its
root-relative path contract. The user-visible name follows the result, not the
implementation type.

## Canonical grammar

Choose the name only after classifying the semantic result. Use the same
canonical name in CLI discovery, Browser labels, documentation, and
human-facing shared or replayed presentation. Portable payloads continue to
bind stable facet identities.

| Result shape | Naming form | Examples |
| --- | --- | --- |
| Set-valued direct evidence | Concise plural relationship noun | `Dependencies`, `References`, `Calls`, `Callers`, `Interfaces`, `Implementers`, `Extensions`, `Integrations` |
| Structurally scalar direct evidence | Conventional singular relationship noun | `Baseclass` |
| Rooted, occurrence-addressed result | Singular relationship domain + `Hierarchy` | `Dependency Hierarchy`, `Reference Hierarchy` |
| Identity-preserving typed topology | Singular relationship or purpose + `Graph` | `Dependency Graph`, `Reference Graph`, `Call Graph`, `Dispatch Graph`, `Integration Graph` |
| Optional derived analysis | Descriptive analysis noun phrase | `Direct Use Clusters` |

The relationship noun encodes direction when direction changes the answer:
`Calls` and `Callers` are distinct direct results. A broader typed one-hop
inventory may retain the conventional `Relations` name, but `Relations` is not
a substitute for a more precise authored result.

Names remain concise within a subject command. Do not repeat coordinates
already established by the route with names such as `Package Dependencies` or
`Member Call Graph` unless two sections in the same authored catalog would
otherwise be ambiguous.

### Direct evidence

A direct section reports owner-issued facts adjacent to the selected subject.
It does not acquire transitive participants merely because the host selects a
tree, Mermaid, table, JSON, or Count projection.

Set-valued direct sections use a plural noun. This resolves the proposed
singular `Integration` spelling in favor of the shipped plural `Integrations`
when the result is a set of direct Integration associations.

A singular noun is permitted only when the relation is structurally scalar for
the selected subject or when a strongly established domain term is itself
singular. Do not use singular wording merely because row selection returns one
item.

### Rooted hierarchies

A `Hierarchy` makes rooted occurrence identity its primary result currency.
Each owner-issued occurrence identity preserves its root, parent occurrence or
root position, relationship, and target associations. A shared target reached
through two parents therefore has two hierarchy occurrences even when both
refer to one canonical subject identity.

Canonical nodes and edges may back or enrich a hierarchy, but they do not
collapse its occurrence identity. A result that exposes only the union of
canonical nodes and typed edges, with roots serving as seeds or annotations,
is a Graph rather than a hierarchy. Dependency-specific resolution, pruning,
failures, and partial completion remain owned by Depends or the corresponding
relationship owner.

`Hierarchy` does not assert that the underlying domain is mathematically a
tree. The semantic obligation is rooted explanatory occurrence and ancestry.
A cycle boundary, repeated shared target, or truncated branch remains part of
that hierarchy contract.

### Identity-preserving graphs

A `Graph` makes canonical subject and typed-edge identities its primary result
currency. Convergence and cycles remain explicit; repeated paths do not create
a new subject or edge identity. Roots and paths may be seeds, queries, or
annotations, but root-relative path multiplicity does not define a second node
or edge result identity. Graph modes may add peer seeds, induced sets, mixed
relationship families, clusters, or other Graph-owned characteristics without
changing the base naming rule.

An internal graph-shaped carrier does not make a result a `Graph`. A rooted
Depends hierarchy may use stable node identifiers and edges while retaining
root occurrences, ancestry, and root-relative depth. Conversely, a Graph
rendered as an indented tree remains a Graph.

### Conventional `Type Hierarchy`

`Type Hierarchy` is the one named conventional exception to the suffix rule.
It denotes Graph-owned identity-preserving inheritance and implementation
topology, not a Depends-owned rooted hierarchy. The established .NET phrase is
more useful than mechanically renaming the concept `Type Graph`.

Future exceptions require an explicit amendment to this document. A host or
adopter cannot infer another exception from local precedent.

### Optional analyses

An analysis computed over direct evidence, a hierarchy, or a graph uses a name
that describes the analysis, such as `Direct Use Clusters`. It does not borrow
the direct, `Hierarchy`, or `Graph` name merely because it consumes that
result.

Selecting both a base result and an analysis returns two named semantic
sections. The analysis may be opt-in and expensive while the underlying direct
evidence remains cheap.

## Projections do not rename results

Tree, Mermaid, Markdown, plaintext, table, TSV, JSON, JSONL, and Count are
projections. A projection may repeat, elide, group, or summarize occurrences
only within the result's projection contract; it does not authorize a
different semantic query.

In particular:

- `--tree` does not turn `References` into `Reference Hierarchy`;
- a Mermaid rendering of `Dependency Hierarchy` does not become
  `Dependency Graph`;
- a table of Graph edges remains `Call Graph`;
- Count reports the selected result unit rather than replacing its section
  identity; and
- a Browser tree widget does not make every displayed relation a hierarchy.

When a requested projection cannot faithfully represent the selected result,
the host reports that incompatibility or uses another supported projection. It
must not silently execute a richer or different relationship operation.

## Categories are not result shapes

Categories such as `@Dependencies`, `@Calls`, `@Relations`, and
`@Integrations` are authored discovery and grouping doors. A category may
contain direct, hierarchy, graph, and analysis sections when that combination
is useful for the command.

Category membership does not rename a section, define its operation, or imply
that every member has the same result shape. Selecting a category executes the
effective member sections under the ordinary Section Model.

## Stable identities and structured schemas

Human-facing section names, portable View Facet IDs, and structured schema
members are separate identities:

- a section name communicates semantic result shape to a person;
- a View Facet ID is an issued portable capability identity; and
- a schema member is a versioned machine contract.

Each issued machine identity maps to one exact semantic result shape and
canonical display name, but several subject-scoped identities may share that
name when their semantics align. A display-name correction does not by itself
authorize changing a facet ID or schema member.

An issued identity cannot be repurposed for another result shape. For example,
the current `library.references` facet identifies direct references; it cannot
later mean `Reference Hierarchy`. A hierarchy facet requires a distinct issued
identity. The same rule applies to direct `package.dependencies` and to any
structured result whose current field represents another semantic shape.

Compatibility aliases may lower an obsolete spelling to the same canonical
semantic section for a bounded migration. They must not:

- map one relationship domain to another;
- turn direct evidence into a hierarchy or graph;
- change subject grain, direction, scope, or cost; or
- remain an undocumented alternate canonical name.

The current global `Dependencies` to `References` alias is compatibility debt
because the terms identify different relationship domains. Its owning CLI
adoption must classify and retire or narrow it under
[CLI change classification](cli-change-classification.md); this document does
not set the compatibility period.

## Current-state classification

This inventory records the migration surface without adopting the contract in
each component.

| Current or proposed surface | Semantic shape | Classification |
| --- | --- | --- |
| Package `Dependencies` | Direct declared dependency evidence | Conforming direct name |
| Package `Dependencies --tree` | Resolved transitive rooted dependency result | **Target:** separate `Dependency Hierarchy`; projection must stop changing the result |
| Depends `Dependency Graph` | Mixed carrier with roots and root-relative behavior over canonical nodes and a union of logical edges | **Target:** #7648 adopts an occurrence-addressed `Dependency Hierarchy`; the current mixed result is not renamed in place |
| Library `References` without `--tree` | Direct assembly-reference evidence | Conforming direct name |
| Library `References --tree` | Resolved transitive rooted reference result | **Target:** separate `Reference Hierarchy`; projection must stop changing the result |
| `Calls` and `Callers` | Direct call evidence by direction | Conforming direct names |
| `Call Graph` | Identity-preserving typed call topology | Conforming Graph name |
| Shipped `Integrations` | Set-valued direct Integration evidence | Conforming direct name |
| Proposed `Integration` direct section | Set-valued direct Integration evidence | **Target:** `Integrations` |
| `Integration Graph` | Identity-preserving Integration topology | Conforming Graph name |
| `Direct Use Clusters` | Optional call-concentration analysis | Conforming analysis name |
| `Type Hierarchy` | Identity-preserving type topology | Reserved conventional exception |
| Legacy `Dependencies` to `References` alias | Cross-domain compatibility routing | Incompatible debt requiring focused CLI disposition |

Internal CLR type names such as `DependencyGraphDocument` are outside this
inventory. They may remain when they do not leak a false user-facing contract.

## Migration surface inventory

Each focused adoption audits the surfaces by semantic identity rather than
performing a blind text replacement:

| Surface | Current evidence | Adoption obligation |
| --- | --- | --- |
| Canonical section registration | Depends registers `Dependency Graph`; Package and Library register `Dependencies` and `References` | Register the new canonical section in the owning catalog and preserve direct sections as direct |
| Request planning and acquisition | Package `Dependencies --tree` and Library `References --tree` currently authorize transitive work | Bind `Dependency Hierarchy` or `Reference Hierarchy` before producer planning; make `--tree` projection-only |
| Help, discovery, and completion | Host output is derived from or supplemented around current section catalogs | Advertise the canonical name once, classify obsolete spellings, and avoid presenting an alias as a second supported result |
| Categories | `@Dependencies`, `@Calls`, `@Relations`, and `@Integrations` group current sections | Keep category identity separate; deliberately place each new hierarchy or graph section |
| Compatibility aliases | `SelectResolver.LegacySectionAliases` globally maps `Dependencies` to `References` when no exact section wins | Remove or narrow the cross-domain alias under CLI change classification |
| Portable View Facets | `package.dependencies`, `library.references`, `library.integrations`, and `member.call-graph` are issued identities | Preserve their current purposes; issue a distinct identity for each new hierarchy or graph result |
| Structured output | Depends currently exposes a `DependencyGraph` schema member and graph-named nested types for the mixed result | Decide whether the hierarchy result requires a new or versioned schema in the Dependency adoption; do not infer a field rename from the display name |
| Share and replay | Portable state binds facet identity and query intent rather than display text | Preserve old packet meaning and map new result shapes through new or explicitly versioned identities |
| README, focused docs, and shipped skills | Current guidance contains existing `Dependency Graph`, `Dependencies --tree`, and `References --tree` spellings | Update examples in the adoption that makes the replacement executable |
| Browser labels and gestures | Browser currently exposes direct `Integrations` and `Call Graph`; hierarchy facets are not issued | Reuse conforming names and add hierarchy labels only when the shared host-neutral result is available |
| Tests and snapshots | Existing gates assert current selectors, help, projections, schemas, and facet titles | Replace or add assertions in the owning adoption and retain compatibility cases only for the approved migration |

Generated code and checked-in Browser facades follow their owning generation
workflow. They are not edited by a naming-only adoption unless the semantic
contract that generates them changes.

## Owner map

| Concern | Owner | Boundary |
| --- | --- | --- |
| Relationship result-shape names | This document | Defines the grammar, exception, and compatibility invariants |
| General section identity, categories, and execution planning | [Section Model](section-model.md) and [Section Pipeline](section-pipeline.md) | Apply authored names without deriving behavior from spelling |
| Top-level command versus subject-section placement | [Operation Commands and Subject Sections](operation-command-and-subject-section-composition.md) | Reuses the same canonical section name across equivalent entrances |
| Direct relationship evidence | Each relationship producer and [Subject Relations](subject-relations-workflows.md) | Own fact meaning, population, direction, evidence, coverage, and failures |
| Rooted dependency results | [Dependency Inspection](dependency-inspection-command.md) | Own roots, resolution, ancestry, pruning, failures, and partial completion |
| Identity-preserving topology | [Inspection Graph](inspection-graph-document.md) and [Graph Modes](inspection-graph-modes.md) | Own node identity, typed edges, lenses, modes, traversal, and characteristics |
| Default-section inference | [#7625](https://github.com/richlander/dotnet-inspect/issues/7625) | Selects an authored result only when context is unambiguous |
| Portable facet identities | [View Facet Registry](view-facet-registry.md) | Issues stable IDs and records their exact purpose |
| Compatibility migration | [CLI Change Classification](cli-change-classification.md) | Owns warnings, removal sequencing, and obsolete-input behavior |
| Rendering and interaction | Markout and focused CLI or Browser owners | Project the selected semantic result without changing it |

This document transfers no evidence, query, result, or host ownership.

## Demonstration

The following target spellings show three distinct answers over one real
package coordinate:

```console
# Direct declarations: a set of package dependency facts.
dotnet-inspect package Aspire.Hosting.Redis@13.5.3 \
  --tfm net8.0 -S Dependencies

# Root-relative explanation: why the package reaches each dependency.
dotnet-inspect depends \
  --package Aspire.Hosting.Redis@13.5.3 --tfm net8.0 \
  -S "Dependency Hierarchy" --tree

# Identity-preserving topology: unique package nodes and dependency edges.
dotnet-inspect graph \
  --package Aspire.Hosting.Redis@13.5.3 --tfm net8.0 \
  -S "Dependency Graph" --format mermaid
```

The second and third commands may consume the same owner-issued dependency
evidence. Their names remain different because one preserves root-relative
occurrences and the other preserves graph identity. Rendering both with lines
and connectors would not make their result contracts equivalent.

The same distinction applies to a Library:

```console
# Direct metadata evidence.
dotnet-inspect library System.Text.Json -S References

# Target rooted transitive explanation.
dotnet-inspect library System.Text.Json -S "Reference Hierarchy" --tree
```

## Pathological cases

Conforming owner adoptions must preserve these distinctions:

1. **Shared dependency:** two parents reach one package. A hierarchy issues two
   root/parent-addressed occurrences that may refer to one canonical package;
   a graph retains one package node with two edges.
2. **Cycle:** a hierarchy reports a cycle boundary in its rooted path context;
   a graph preserves the cycle as topology.
3. **Renderer coincidence:** identical-looking indented output from a hierarchy
   and a graph does not merge their semantic names.
4. **Direct empty result:** zero `References` rows remains a successful direct
   evidence answer; it does not imply that traversal was attempted.
5. **Projection request:** `Dependencies --tree` and `References --tree`
   cannot trigger transitive acquisition after their owners adopt this
   contract.
6. **Analysis selection:** `Direct Use Clusters` remains separately selectable
   and opt-in even when its source call evidence is also selected.
7. **Conventional exception:** `Type Hierarchy` retains Graph identity and
   cycle/convergence behavior despite the `Hierarchy` suffix.
8. **Alias collision:** an obsolete alias cannot bind to a canonical section
   with another relationship domain or result shape.
9. **Stable facet:** a display-name migration cannot repurpose
   `library.references`, `package.dependencies`, or another issued facet.
10. **Host parity:** CLI and Browser gestures that select the same facet expose
    the same semantic name and `InspectionEnvelope<TContent>` result even when
    their widgets differ.

## Analogous implementations

Analogous tools provide evidence, not authority:

- The [.NET package listing
  command](https://learn.microsoft.com/dotnet/core/tools/dotnet-package-list)
  adds transitive rows with `--include-transitive` while retaining list
  semantics. This supports distinguishing a relationship inventory from richer
  topology rather than assuming that additional depth creates a graph.
- [`cargo
  tree`](https://doc.rust-lang.org/cargo/commands/cargo-tree.html) describes a
  tree visualization of a dependency graph. It de-duplicates repeated packages
  by default, can repeat them with `--no-dedupe`, can invert direction, and
  bounds display depth. Those choices demonstrate how a tree projection may
  change occurrence presentation. This contract instead keeps the semantic
  result name independent from those renderer choices.

dotnet-inspect deliberately distinguishes rooted explanatory hierarchy from
identity-preserving topology even when another tool uses `tree` and `graph`
interchangeably in user-facing prose.

## Production adoption

Adoption is staged by architectural owner:

1. **Naming owner:** this document establishes the grammar and transfers
   relationship-specific naming from the general Section Model.
2. **Dependency owner:** [#7648](https://github.com/richlander/dotnet-inspect/issues/7648)
   adopts an occurrence-addressed `Dependency Hierarchy`, updates discovery
   and compatibility behavior, and decides any public schema migration. It
   does not treat the current mixed canonical-node carrier as a display-only
   rename.
3. **Package owner:** [#7649](https://github.com/richlander/dotnet-inspect/issues/7649)
   keeps `Dependencies` direct and binds a distinct
   `Dependency Hierarchy` subject section to the shared Depends operation.
4. **Library reference owner:** [#7647](https://github.com/richlander/dotnet-inspect/issues/7647)
   separates direct `References` from `Reference Hierarchy`; `--tree` becomes a
   projection of the latter rather than an acquisition switch for the former.
5. **Graph owner:** #7624 applies `Dependency Graph`, `Reference Graph`,
   `Call Graph`, `Dispatch Graph`, `Integration Graph`, and the conventional
   `Type Hierarchy` to authored Graph presets.
6. **Default owner:** #7625 may infer only one of these authored results and
   preserves explicit override before producer planning.
7. **Subject Relations owner:** [#6761](https://github.com/richlander/dotnet-inspect/issues/6761)
   adopts plural `Integrations` for set-valued direct evidence and preserves
   `Integration Graph` for topology.
8. **Facet and host owners:** issue new facet identities for new semantic
   shapes, map structured schemas deliberately, and expose the same names and
   envelopes through CLI and Browser/Wasm.

Each adoption changes only its owning component. The naming document does not
authorize one cross-owner implementation sweep.

## Evidence and gates

This specification is documentation-only. `markdownlint` and `git diff
--check` gate its Markdown and patch integrity.

Runtime conformance remains **unverified** until focused adoptions add Release
gates covering:

- exact section registration, discovery, help, and completion names;
- selection, category expansion, aliases, and obsolete-input behavior;
- direct versus hierarchy versus graph acquisition and result identity;
- root/parent-addressed hierarchy occurrences versus canonical Graph node and
  edge identities;
- tree, Mermaid, table, structured, and Count projection invariance;
- stable View Facet IDs and any versioned schema mappings;
- the shared-dependency, cycle, empty-result, and incompatible-projection
  pathological cases; and
- equivalent CLI and Browser/Wasm consumption of the same host-neutral
  envelope.

No TLA+ model is required. The contract has no concurrent or stateful protocol;
its correctness is expressed by authored identity mappings and observable
selection/projection gates.

## Non-goals

This document does not:

- rename current runtime sections;
- define relationship evidence, Graph traversal, or Depends resolution;
- choose contextual defaults;
- define a new renderer or structured graph schema;
- rename internal implementation types;
- preserve every historical alias;
- turn human-facing section names into portable facet IDs; or
- retire relation-specific commands before replacement parity.
