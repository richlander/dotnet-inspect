# Package-pair Direct Use Clusters

## Status and authority

Status: proposed for
[#9332](https://github.com/richlander/dotnet-inspect/issues/9332), the first
production slice under
[#9327](https://github.com/richlander/dotnet-inspect/issues/9327).

This document is the single normative owner of this claim:

> Given two distinct exact Package Root bindings admitted to one Workspace,
> issue every observed Direct Use Cluster between their implementation
> Libraries, preserving exact Package and Library identity, physical call
> evidence, and pair completeness in one detached Package-pair document.

`DotnetInspector.Queries` owns this semantic composition over one live
`PackageAssemblyContextProjection`. It consumes existing Package-role
participation, pairwise Library call-use, and Direct Use Cluster contracts
without redefining them. `DotnetInspector.PackageQueries` realizes and closes
the exact Package context around that query. `DotnetInspector.Sections` owns
shared section and row plans over the completed document. CLI and Inspect Web
own authorization, operation lifetime, gestures, and presentation.

## Product question

For two exact Package releases and one selected target framework:

- which implementation Libraries call across the Package boundary;
- in which direction does each relationship run;
- which exact source and target methods form each Direct Use Cluster; and
- what physical call evidence and completion state support the result?

The operation is a Package-pair matrix over exact Library-pair evidence. It is
not a Package dependency graph and does not follow the dependency closure of
either endpoint.

## Motivating asset

The real pair is:

- `Microsoft.Extensions.Http.Polly@11.0.0-rc.1.26425.128`; and
- `Polly.Extensions.Http@3.0.0`;

under `netstandard2.0`.

The existing production `graph calls` scenario proves that
`PollyHttpClientBuilderExtensions.AddTransientHttpErrorPolicy` reaches
`Polly.Extensions.Http.HttpPolicyExtensions.HandleTransientHttpError` and
retains the exact `Polly.Extensions.Http@3.0.0` ownership. That command starts
from one selected Member and traverses a dependency closure. The Package-pair
question instead selects both exact Packages and inventories every direct
cross-Package Library relationship.

An independently compiled fixture supplies two implementation Libraries on
each Package side, calls in both directions, disconnected Library pairs,
shared and disjoint Direct Use Clusters, repeated physical call sites, and one
participant whose body evidence is incomplete.

## Design basis

The conventional baseline is an archive- or module-pair dependency matrix with
drill-down to contributing relationships:

- Java `jdeps` reports archive, package, and class dependencies while
  preserving direction.
- NDepend's Dependency Structure Matrix separates component relationships from
  their contributing members and counts.
- Visual Studio Code Maps group dependencies at a component level and allow
  expansion to participating members.

This owner is stricter about evidence. A Package or Library edge exists only
because an existing exact physical call occurrence names both endpoints.
Cluster membership remains the deterministic connected-component result owned
by
[Pairwise Library Direct-Use Clustering](pairwise-library-direct-use-clusters.md),
not a package-level heuristic or community-detection algorithm.

Supporting contracts have these roles:

| Role | Owner-issued contract |
| --- | --- |
| Exact Package content, selection, and Root association | PackageHouse and Package Root realization |
| Binding-consistent implementation participants and exact Package ownership | Package Assembly Context completion |
| Exact directed calls, correspondence, diagnostics, and pair completion | `AssemblyPairCallUseQuery` |
| Cluster assignment and Library-pair-local cluster identity | `AssemblyPairDirectUseClusterProjection` |
| Public-root paths for one selected cluster | `AssemblyPairClusterRootPathQuery` |
| Completed host boundary | `InspectionEnvelope<TContent>` |
| Shared sections and logical row selection | Section Model and QuerySpace |
| CLI lowering | Markout and the Graph command family |

## Exact request and association

The Queries request carries:

- one live `PackageAssemblyContextProjection`;
- exactly two distinct `PackageRootIdentity` values;
- one finite Library-pair admission limit; and
- an optional focused Package-pair cluster identity for a focused operation.

Both identities must:

- belong to the supplied package-role projection;
- name different canonical Package IDs;
- carry compatible explicit target-framework context; and
- identify exact retained package-role participants.

The surrounding PackageQueries operation carries one exact
`InspectionWorkspace`, one captured Scope containing both exact Package
occurrences, exactly two distinct `PackageRootBinding` values, finite Package
Assembly Context realization limits, and one finite Workspace deadline. It
validates Root and Scope association before preparing the live projection and
invoking the Queries owner.

Argument order has no semantic meaning. The operation establishes canonical
endpoint order from the exact detached Package descriptors. Reversing the two
request arguments therefore cannot change Library-pair identity, cluster
assignment, or result order.

Package ownership comes exclusively from
`PackageAssemblyRoleParticipant.Package` while the package-role projection is
live. Assembly simple names, file names, asset paths, namespaces, rendered
labels, and input order never establish Package membership.

## Admitted implementation population

The operation prepares one Package Assembly Context completion from the two
exact bindings and uses its implementation role. A Package with no admitted
implementation participant produces a typed endpoint-unavailable outcome; it
does not become a complete empty call matrix.

Every implementation participant belongs to exactly one of the two exact
Package Roots. Missing or ambiguous Package association is invalid owner input
and fails visibly before pair execution.

The request's existing Package Assembly Context limits bound participant
count, individual entry size, aggregate retained image bytes, and group
retention. Before call analysis, the operation computes the cross-product of
the two admitted Library populations. If that complete pair population exceeds
the operation's finite Library-pair admission limit, the result is typed
unavailable. The operation does not analyze an arbitrary prefix and present it
as the Package pair.

## Library-pair execution

The operation analyzes each admitted implementation Library once through the
existing Assembly Context call-graph analysis. For each canonical
cross-Package Library pair, it then asks `AssemblyPairCallUseQuery` to issue
the exact pair result from those prepared analyses. That query continues to
evaluate both directions and to own:

- exact source and target participant identity;
- resolved `call`, `callvirt`, and `newobj` occurrences;
- physical call receipts;
- body and correspondence diagnostics; and
- pair completeness.

The operation then invokes
`AssemblyPairDirectUseClusterProjection.Create` over that exact pair result.
It does not copy, modify, or supplement cluster connectivity. Existing
Library-pair-local cluster identity and derivation remain owner-issued.

Prepared analyses and pairs execute in canonical Package and Library identity
order. Analysis is not repeated for every pair in the Package cross-product.
A pair failure does not suppress healthy neighboring pairs. Positive calls and
clusters remain valid when their pair is incomplete; the Package-pair document
retains that qualification.

## Detached document

The completed value is one resource-free
`PackagePairDirectUseClusterDocument`. It contains:

1. both exact Package descriptors and the shared selected framework;
2. one exact implementation-Library descriptor per admitted participant;
3. one result per canonical cross-Package Library pair;
4. every owner-issued Direct Use Cluster and supporting physical call
   occurrence;
5. endpoint, participant, pair, and document completion; and
6. an optional focused cluster result, including existing public-root paths
   when that section was requested.

A detached Library descriptor retains:

- exact owning Package descriptor;
- selected implementation asset;
- assembly reference identity;
- module version ID; and
- resource-free acquisition provenance required to distinguish artifacts.

It retains no Workspace occurrence identity, acquisition registration,
Package Root binding, participant, image, stream, or callback.

Each Package-pair cluster receives a canonical document-local ordinal after
Library-pair ordering. The ordinal is a presentation selector for
`graph cluster N`, not durable identity. The cluster also retains its exact
owner-issued Library-pair identity and local anchor tokens, which remain the
semantic identity within this exact document.

Every boundary call retains enough exact target identity for the later
multi-leg workflow:

- Package descriptor and target framework;
- Library descriptor and MVID;
- exact target MethodDef token and stable Member identity; and
- physical call receipts and completion.

This slice does not consume that continuation.

## Completion and failure

The operation distinguishes:

- **available** — the complete admitted Library-pair population was evaluated,
  with each pair retaining its own completion;
- **endpoint unavailable** — one exact Package cannot supply an admitted
  implementation population;
- **pair population rejected** — the complete cross-product exceeds its
  admission limit;
- **Workspace not committed** — the exact Package roots are not both present
  in one committed Scope; and
- **cleanup failed** — package-role projection or completion release failed.

Available does not mean every pair is complete. Document completion requires:

- both endpoints and every admitted implementation participant;
- every canonical cross-Package Library pair;
- complete pair evidence for every pair; and
- successful detachment and cleanup.

Clusters and physical calls remain positive evidence when document completion
is false. Zero clusters proves no admitted cross-Package direct call only when
document completion is true.

Cancellation and unexpected acquisition, Workspace, analysis, detachment, or
cleanup exceptions produce no completed document. Cleanup follows the existing
Package Assembly Context completion precedence.

## Shared inspection and row plans

`DotnetInspector.Sections` returns:

```text
InspectionEnvelope<PackagePairDirectUseClusterInspectionOutcome>
```

Available Content retains the complete owner-issued document. Share is
non-projectable until Workspace Definitions owns an exact two-Package graph
subject and focused cluster selector.

The first shared section catalog declares:

- `Direct Use Clusters` — the command's single high-value default;
- `Library Pairs` — one row per canonical Library pair;
- `Call Sites` — exact physical cross-Package calls; and
- `Public Root Paths` — remains owned by the existing local-Library focused
  operation in this slice.

The document is the first reference execution: it evaluates the complete
admitted Package pair before QuerySpace selects issued rows because cluster
identity and complete absence are defined over complete Library-pair results.
The section owner retains total and selected row counts. Predicate pushdown to
independent Library pairs is deferred until source delegation can prove that
it preserves canonical ordinals, document completion, and focused-cluster
selection.

## CLI adoption

The first production host is:

```console
dotnet-inspect graph packages \
  --package Microsoft.Extensions.Http.Polly@11.0.0-rc.1.26425.128 \
  --package Polly.Extensions.Http@3.0.0 \
  --tfm netstandard2.0
```

The command requires exactly two `--package` values and one explicit `--tfm`.
It realizes only those Package subjects; it does not traverse their dependency
graphs or apply a supply-chain baseline.

Omitting `-S` selects `Direct Use Clusters`. Discover, Count, semantic row
selection, Markdown, table, TSV, JSONL, and complete JSON consume the same
declared rows or document. Markout lowers Markdown and tabular presentation.

`graph cluster N` accepts either exactly two local `--library` paths or exactly
two Package coordinates plus `--tfm`, never both. The local-Library route is
unchanged. The Package route resolves `N` against the canonical Package-pair
document and defaults to `Call Sites`. Package-backed `Public Root Paths`
requires a later focused-operation extension that keeps the implementation
context live; this slice does not silently substitute detached or
Library-name-based correspondence.

CLI option parsing, package-source authorization, ephemeral Workspace lifetime,
format selection, stderr diagnostics, and exit policy remain host concerns.
The CLI does not enumerate Library pairs or construct clusters.

## Inspect Web adoption

[#9334](https://github.com/richlander/dotnet-inspect/issues/9334) consumes the
same inspection after the CLI slice locks it. Inspect Web supplies Browser
package-source capabilities and a retained Workspace borrow, then projects the
same Package, Library, cluster, call-site, path, and completion identities.
TypeScript owns geometry, interaction, and navigation only.

## Required gates

Release gates cover:

1. two Packages with two implementation Libraries each produce exactly the
   canonical cross-product and no same-Package pair;
2. both call directions remain distinct;
3. reversing request endpoints produces identical detached Content;
4. each cluster is the existing Library-pair projection with unchanged
   membership, anchors, and physical occurrences;
5. repeated call sites affect physical count but not cluster membership;
6. an incomplete pair preserves positive neighboring results and prevents a
   complete document claim;
7. no implementation population and pair-population admission failure remain
   typed non-success;
8. every detached endpoint retains exact Package, asset, assembly, MVID, and
   Member identity without live Workspace or registration state;
9. focused Package cluster selection resolves the same owner-issued cluster
   selected by the pair-wide document;
10. CLI terminals and row selection consume the shared declarations; and
11. the pinned Polly pair demonstrates an exact cross-Package cluster without
    dependency-closure traversal.

NativeAOT before/after evidence covers every supported `graph packages`
terminal. Browser performance evidence belongs to its adoption slice.

## Non-claims

This owner does not:

- traverse sibling Libraries to find Package-wide root paths;
- follow package dependencies or include a third Package;
- define Package dependency strength, removability, or feature identity;
- add a clustering or community-detection algorithm;
- pair two versions of the same Package;
- infer runtime virtual targets, reflection, delegates, or dynamic dispatch;
- define Browser rendering or interaction;
- compose `PackageA -> PackageB -> PackageC`; or
- change existing `graph calls`, `graph libraries`, or local
  `graph cluster N` behavior.
