# Operation commands and subject sections

## Status and authority

This document defines the approved command-placement composition tracked by
[#7623](https://github.com/richlander/dotnet-inspect/issues/7623).

**Operation Command and Subject Section Composition** is the single normative
owner established here. Its exact claim is:

> Keep a broad operation as a top-level command when it admits operation-first
> roots, endpoints, seeds, or populations; expose a curated invocation of that
> same operation from an already resolved Package, Library, Type, or Member
> through a section, without adding a subject subcommand or a second semantic
> implementation.

This owner defines placement, equivalence, and retirement sequencing. It does
not define Diff correspondence, Graph topology, Dependency resolution,
subject identity, section selection, default inference, relationship evidence,
query predicates, rendering, or host interaction. Those contracts remain with
their focused owners.

The user explicitly approved this cross-owner composition. Focused adoption
remains staged by owner: this document does not authorize one implementation
change that simultaneously rewrites Diff, Graph, Dependency, sections, and
every relation-specific command.

## Motivation and demo

Users approach the same capability in two ways:

1. **Operation first:** compare two endpoints, inspect a heterogeneous
   dependency root set, or construct a graph over several participants.
2. **Subject first:** inspect one already selected package, type, or member and
   ask for a useful comparison, dependency, or graph view without reconstructing
   its coordinate.

The command surface should preserve both workflows without duplicating the
operation:

```console
# Operation-first dependency question.
dotnet-inspect depends \
  --package Aspire.Hosting.Redis@13.5.3 \
  --tfm net8.0

# Subject-first direct evidence.
dotnet-inspect package Aspire.Hosting.Redis@13.5.3 \
  --tfm net8.0 -S Dependencies
```

The section names below follow
[Relationship Section Naming](relationship-section-naming.md):

```console
# Subject-first rooted dependency view backed by Depends.
dotnet-inspect package Aspire.Hosting.Redis@13.5.3 \
  --tfm net8.0 -S "Dependency Hierarchy"

# Subject-first identity-preserving topology backed by Graph.
dotnet-inspect package Aspire.Hosting.Redis@13.5.3 \
  --tfm net8.0 -S "Dependency Graph"

# Existing subject-first Graph consumer.
dotnet-inspect member \
  Microsoft.Extensions.DependencyInjection.ProviderBuilderServiceCollectionExtensions \
  --package OpenTelemetry@1.18.0 --tfm net10.0 \
  -m AddOpenTelemetrySharedProviderBuilderServices~4d95928639 \
  -S "Call Graph"
```

The package coordinate, target framework, selected library, exact subject,
Workspace population, and source authority come from the subject command.
The section contributes an authored operation preset. Equivalent top-level and
subject-first requests consume the same host-neutral operation and preserve the
same Content, Share outcome, diagnostics, limits, and completeness.

## Retained top-level operations

Retain these top-level commands:

| Operation | Why it remains operation-first |
| --- | --- |
| `diff` | Admits two endpoints, versions, or populations and owns comparison correspondence and change results independently of one already selected subject. |
| `graph` | Admits one or more seeds, peer subjects, induced sets, or Workspace participants and returns identity-preserving typed topology. |
| `depends` | Admits dependency roots and source context, applies dependency-specific resolution and traversal, and returns direct evidence or rooted hierarchy with partial failures. |

These commands may also accept a single subject. Single-subject input does not
make an operation command redundant: the user may still begin with the
operation, choose broader scope, or supply roots that have no honest single
subject command.

Operation modes remain part of their broad top-level command when they change
arity, population, or execution while preserving the same operation identity.
For example, pairwise and temporal Diff are modes of top-level `diff`; a
subject-first History section may bind the temporal mode without introducing
another command. A distinct request or Outcome does not by itself require a
different command token.

Command identity follows the semantic result, not the renderer. A Graph edge
table remains a Graph result. A JSON dependency hierarchy remains a Depends
result. Mermaid availability does not move a Dependency request into Graph.

## Subject sections

Package, Library, Type, and Member commands retain their ordinary coordinate
grammars. An operation-backed section:

- reuses the resolved subject and binding context;
- contributes one authored semantic preset;
- lowers through the same host-neutral operation as the top-level command;
- preserves that operation's result, failures, limits, and completeness; and
- remains discoverable through the ordinary section system.

Do not add these command forms:

```text
package graph
library graph
type graph
member graph

package diff
library diff
type diff
member diff
```

The prohibition is about subject subcommands for the retained broad
operations. It does not prohibit existing subject-owned operations whose
identity is already part of the subject grammar, such as `package query` or
`package activity`.

A section is not merely an alias for argv. Its descriptor binds the operation,
subject role, required context, supported query facets, cost, result unit, and
projection. CLI and Browser/Wasm may expose different gestures while consuming
the same semantic request and `InspectionEnvelope<TContent>`.

## Relationship vocabulary is not command vocabulary

`implements`, extension relationships, calls, Integration associations,
references, inheritance, overrides, and similar relations remain typed,
owner-issued evidence. They do not each require a top-level command.

The preferred entrances are:

- `find` or Package Query when the question selects subjects from a population;
- focused sections such as `Implementers`, `Extensions`, or `Integration`
  when one subject is already selected;
- `Relations` and its typed facets for a broader one-hop inventory; and
- Graph when traversal, connectedness, paths, mixed relationships, or
  multi-grain topology changes the answer.

Standalone `implements`, `extensions`, Integration-specific graph surfaces,
and other relation-specific commands may retire only after their useful
candidate-population rules, evidence, direction, limits, failures, structured
output, discovery, and sharing have replacement parity. The semantic relation
does not retire with its command token.

`ecosystem` remains a vocabulary command. It describes owner-issued ecosystem
identities and configured knowledge that other queries consume; it is not an
artifact relation command.

## Depends and Graph

Depends and Graph intentionally overlap in evidence and traversal while
retaining different result contracts.

### Depends

Depends is root-oriented and dependency-specific. It may return:

- normalized direct dependency evidence; or
- a rooted hierarchy or forest preserving ancestry and root-relative paths.

It owns target-framework selection, declaration versus resolution,
source-authorized candidates, pruning, dependency failures, and partial
completion. A shared dependency may appear beneath several parents when each
path explains why the root reaches it.

### Graph

Graph returns identity-preserving relationship topology. One subject has one
document identity even when several edges reach it. Convergence, cycles, peer
seeds, induced sets, clusters, mixed relationship kinds, and subject-grain
lenses remain explicit.

Graph may consume the same owner-issued dependency declarations and resolution
evidence as Depends. It does not acquire Dependency ownership, reinterpret a
version constraint, or manufacture a resolved edge from incomplete evidence.

The focused
[Graph design](https://github.com/richlander/dotnet-inspect/issues/7624)
owns relationship sets, modes, lenses, traversal, and characteristics.
[Relationship Section Naming](relationship-section-naming.md) owns names that
distinguish direct dependency evidence, rooted hierarchy, and Graph topology.

## Defaults and explicit selection

An operation or subject route may infer a section only when its resolved
request has one unambiguous high-value interpretation. Explicit compatible
section selection replaces the inferred selection before producer planning.

The focused
[default-section design](https://github.com/richlander/dotnet-inspect/issues/7625)
owns the algorithm, discovery, compatibility checks, and cost boundaries.
This composition adds only two constraints:

1. the inferred and explicit forms lower to the same semantic operation; and
2. a default cannot silently change a direct view into Diff, Graph, or Depends.

Adding a new available section does not silently change an established default.
An ambiguous route requires an explicit section.

## Owner map

| Concern | Owner | Composition role |
| --- | --- | --- |
| Operation/section placement | This document | Decides top-level versus subject-section entry and equivalent-operation obligation |
| Host-neutral operation composition | [Inspection Operation Composition](inspection-operation-composition.md) | Sequences resolution, House/Workspace admission, query, section, rows, Share, and host projection |
| Subject identity and coordinates | Each Package, Library, Type, or Member owner | Supplies the already resolved subject and binding context |
| Section identity and planning | [Section Model](section-model.md) and [Section Pipeline](section-pipeline.md) | Supplies authored descriptors, selection, applicability, cost, and execution plans |
| Defaults | [#7625](https://github.com/richlander/dotnet-inspect/issues/7625) | Defines contextual inference and explicit override |
| Relationship section names | [Relationship Section Naming](relationship-section-naming.md) | Distinguishes direct evidence, rooted hierarchy, topology, and characteristics |
| Diff | Existing comparison and Diff owners | Retain correspondence, result, evidence, and failure semantics |
| Graph | [Inspection Graph](inspection-graph-document.md), [modes](inspection-graph-modes.md), and [#7624](https://github.com/richlander/dotnet-inspect/issues/7624) | Retain typed topology, occurrences, modes, relationship sets, lenses, and characteristics |
| Depends | [Dependency Inspection](dependency-inspection-command.md) | Retains dependency admission, resolution, traversal, evidence, failures, and rooted result |
| Subject Relations | [Subject Relations](subject-relations-workflows.md) | Retains locate-once relation meaning, population, direction, evidence, and coverage |
| CLI and Browser hosts | Their focused host owners | Supply gestures, lifetime, interaction, and presentation without duplicating semantic operations |

This map transfers no evidence, query, or result ownership.

## Production adoption

The composition reaches production through focused owner adoptions:

1. **Composition:** this document establishes top-level operations, subject
   sections, equivalence, and retirement sequencing.
2. **Section contract:** [Relationship Section Naming](relationship-section-naming.md)
   defines names and migration; #7625 defines contextual defaults and explicit
   override.
3. **Graph:** #7624 adopts the general Graph command, section presets, and
   current `graph calls`, `graph libraries`, `graph cluster`, and
   `graph integrations` migrations.
4. **Dependency:** the Dependency owner retains `depends`, distinguishes its
   direct and rooted results from Graph, and adopts subject sections without a
   second operation. Package adopted `Dependency Hierarchy` as a subject
   section over the same Depends envelope in #7649.
5. **Diff:** comparison owners retain top-level `diff` and adopt curated
   subject sections without subject Diff subcommands.
6. **Relations:** Subject Relations and each producer provide replacement
   parity before relation-specific command retirement.
7. **Hosts:** CLI help, completion, README, skills, Share, and Browser/Wasm
   gestures consume the settled operation and section contracts.

Each step is independently reviewable. A later owner adoption references this
map and changes only that owner's contract.

## Pathological cases

The focused adoptions must preserve these boundaries:

1. **One subject, several plausible operations.** A package with dependency
   evidence and call evidence does not receive an arbitrary Graph default.
2. **No single subject.** A project, nuspec, and package root set remains a
   valid top-level Depends request rather than being forced through a fake
   Package subject.
3. **Shared dependency.** Depends may preserve two root-relative paths while
   Graph presents one target node with two incoming edges.
4. **Unavailable operation.** A subject section whose operation cannot run
   fails visibly; it does not return an empty direct-evidence section.
5. **Expensive characteristic.** Selecting Graph does not silently enable
   clustering, source, exhaustive analysis, or another capability-gated
   producer.
6. **Retired verb with missing parity.** A relation-specific command remains
   until its population, evidence, failures, and output workflows have a
   demonstrated replacement.

## Non-goals

- One universal command, request, result, or section schema.
- Treating every binary relation as graph-worthy topology.
- Defining the final section names or default-selection algorithm in this
  document.
- Replacing Dependency resolution with Graph rendering.
- Moving Diff correspondence or Graph topology into subject commands.
- Requiring top-level and subject-first gestures to have identical argv.
- Retiring a command before production parity.
- Implementing all owner adoptions in one change.
