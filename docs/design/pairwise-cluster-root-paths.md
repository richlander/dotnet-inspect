# Pairwise cluster public-root paths

This document owns the focused composition that connects one exact pairwise
direct-use cluster to the public MethodDefs that reach its consumer use sites.
Implementation is tracked by
[#7390](https://github.com/richlander/dotnet-inspect/issues/7390); the broader
dependency-capability experience remains
[#6313](https://github.com/richlander/dotnet-inspect/issues/6313).

## Claim and owner

`DotnetInspector.Queries` owns one bounded composition: given one
binding-consistent assembly group, one scoped direct-use cluster projection
from that group, and explicit Metadata and Analysis limits, return:

- the exact selected cluster and its pair evidence;
- the consumer participant's Metadata-issued public MethodDef inventory;
- deterministic shortest local paths from retained public roots to the
  cluster's direct-use MethodDefs;
- physical call receipts for every retained path step; and
- the pair, Metadata, and Analysis completion evidence without collapsing one
  owner's boundary into another's.

`DotnetInspector.Sections` exposes the completed operation as
`InspectionEnvelope<TContent>`. Hosts consume the same composition result; they
do not reconstruct public roots, destinations, paths, or completion from
rendered text.

## Supporting owners

[Pairwise Library Direct-Use Clustering](pairwise-library-direct-use-clusters.md)
owns cluster membership, exact occurrence receipts, identity, and ordinal
assignment.
[Public MethodDef root inventory](public-method-root-inventory.md) owns exact
public-root membership and Metadata limits.
[Library-body root paths](library-body-root-paths.md) owns local graph
traversal, shortest-witness selection, physical step receipts, and Analysis
limits.

This composition consumes those owner-issued values. It does not redefine
their internal membership, ordering, or boundary semantics.

## Exact input and selection

The selected value is an
`AssemblyPairDirectUseClusterProjection` scoped to exactly one observed
cluster. The projection retains the exact pair occurrences and one
`AssemblyPairDirectUseCluster`, including:

- source and target acquisition registrations;
- source and target module version IDs;
- source and target anchor MethodDef tokens; and
- exact consumer MethodDefs.

The composition does not accept a cluster ordinal as identity. An ordinal is a
document-local selection convenience used by hosts to obtain the scoped
projection. The composition validates that both selected pair registrations
are participants in the supplied group and that the live immutable source
snapshot has the selected source MVID. A stale or foreign selection is
rejected before Metadata or Analysis results are produced.

The caller supplies:

- positive TypeDef, MethodDef, and retained-root limits for the Metadata
  inventory; and
- non-negative depth plus positive node, edge, and retained-path limits for
  Analysis.

## Join currency

Acquisition registration identifies the exact live participant. Module MVID
and MethodDef token identify declarations and path endpoints within that
participant.

The selected cluster's source MethodDefs become Analysis destinations only
after:

1. the source registration selects one participant in the group;
2. the participant snapshot MVID equals the cluster source MVID; and
3. Metadata and Analysis independently report that same MVID.

Names, formatted signatures, paths, cluster ordinals, and anchor labels never
substitute for that join.

## Execution and retained evidence

The composition borrows the consumer participant's immutable snapshot for one
operation. Metadata reads the exhaustive public-root inventory from that
snapshot. Analysis builds one full method-evidence body index from the same
snapshot and searches only within that consumer module.

When Metadata retains at least one public root, Analysis searches from those
roots to the selected cluster's distinct source MethodDefs. A complete empty
root inventory is a complete empty public-root-path result and does not require
an Analysis search. An incomplete empty root inventory remains incomplete; it
does not manufacture an Analysis absence result.

The result retains the owner-issued Metadata inventory and, when a search ran,
the owner-issued Analysis result. Positive roots and witnesses remain visible
when either operation is bounded. The composition adds no second path or
receipt representation.

## Completion and failure

An available result is complete exactly when:

- the selected pair result is complete;
- the public-root inventory is complete; and
- either the inventory contains no roots or the Analysis path result is
  complete.

Pair incompleteness does not invalidate observed cluster membership or
positive paths, but it prevents a complete cluster-level absence claim.
Metadata incompleteness prevents a complete public-root population claim.
Analysis incompleteness prevents a complete reachability claim. Each boundary
remains typed by its owner.

Snapshot rejection and malformed consumer metadata are typed unavailable
outcomes associated with the selected source participant. Invalid or stale
selection is a request rejection because no supported execution can join it to
the supplied group. No failure becomes empty success.

## CLI adoption

`graph cluster` exposes one explicit-only section:

```console
dotnet-inspect graph cluster 3 \
  --library ./Consumer.dll \
  --library ./Provider.dll \
  -S "Public Root Paths"
```

The route requires one positive pair-local cluster ordinal. Selection is
validated before library acquisition so an omitted cluster cannot start an
all-cluster path search. Pair-wide `graph libraries -S "Public Root Paths"`
fails with guidance to use the focused route.

Omitting the section preserves the focused route's exact call-site default.
Bare `-S` remains the existing two summary sections. The new section does not
enter any automatic verbosity preset, and wildcard section selection does not
opt into it; callers name `Public Root Paths` explicitly.

Each retained witness lowers to one row containing:

- consumer library and cluster ordinal;
- public root member and MethodDef token;
- direct-use destination member and MethodDef token;
- shortest depth;
- the ordered method path; and
- physical step receipts with evidence MethodDef token, IL offset, and operand
  token.

The typed query result remains the information model. The CLI uses Markout to
lower it to Markdown, plain text, table, TSV, JSONL, and projected JSON.
Unprojected service transport is not added by this slice.

If any pair, Metadata, or Analysis boundary is present, positive rows are still
rendered, diagnostics identify the incomplete owner, and the command returns a
nonzero result. A complete empty result states that no selected public root has
a local static path to the cluster's consumer use sites.

## Pathological evidence

Release gates cover:

1. two public roots reaching one private direct-use MethodDef at equal depth;
2. a public direct-use MethodDef producing a zero-depth witness;
3. a selected cluster with no public-root path producing complete empty
   evidence;
4. public property access admitting only its public accessor;
5. Metadata limits retaining positive roots while making absence incomplete;
6. Analysis depth, work, path, and generated-body boundaries retaining
   positive witnesses;
7. stale registration, a shared-source selection with a foreign target, and
   mismatched MVID selections failing before execution; and
8. the CLI requiring `graph cluster N`, preserving focused exact-call
   drill-down, and rendering exact path and receipt coordinates.

The motivating real package remains `Serilog.Sinks.Console` 6.0.0 against
`Serilog` 4.0.0. Its selected cluster reaches private
`ThemedValueFormatter..ctor` from public `Console` configuration entrypoints.
A neighboring selected cluster with no local static public path is the
complete negative case.

## Production path

The production path under #6313 is:

1. direct-use clusters identify exact connected consumer/provider use;
2. Metadata supplies exact public roots;
3. Analysis supplies bounded local root paths;
4. this composition and CLI section expose the joined evidence; and
5. a future Browser/Wasm two-library surface calls the same Sections operation
   and lowers the same typed content without a TypeScript-side join.

No existing architecture is replaced, so there is no retirement plan.

## Non-claims

This composition does not:

- recommend package removal or source inlining;
- assign semantic feature or capability names;
- traverse provider implementations or another participant;
- infer reflection, delegates, dynamic dispatch, or runtime virtual targets;
- include protected members as public roots;
- enumerate every equal shortest path; or
- make an absence claim when any contributing owner is incomplete.
