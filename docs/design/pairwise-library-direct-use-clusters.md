# Pairwise library direct-use clusters

Pairwise Library Direct-Use Clustering answers one bounded question:

> Which exact cross-library calls are connected through shared consumer or
> provider methods?

Tracking:

- [#7253](https://github.com/richlander/dotnet-inspect/issues/7253) — this
  focused owner and first CLI adopter;
- [#6313](https://github.com/richlander/dotnet-inspect/issues/6313) — broader
  semantic feature relationships and package or ecosystem rollups;
- [#6601](https://github.com/richlander/dotnet-inspect/issues/6601) — public
  consumer roots and bounded paths to direct-use sites.

This document owns deterministic clustering of one already-produced
`AssemblyPairCallUseResult`. Pairwise Library Call-Use continues to own exact
occurrences, participant identity, correspondence, and completion. This owner
does not reopen assemblies, traverse implementation bodies, infer public API
roots, or assign semantic feature names.

## Consumer and production path

The first consumer is the explicit CLI section:

```console
dotnet-inspect graph libraries \
  --library ./Consumer.dll \
  --library ./Provider.dll \
  -S "Direct Use Clusters"
```

The complete production path has four steps:

1. This slice locks the cluster contract, implements the host-neutral
   projection, and adopts it in the CLI through typed Markout views.
2. Browser/Wasm consumes the same projection after its two-library selection
   surface exists; it does not reimplement clustering in TypeScript.
3. Issue #7390 composes Metadata roots and Analysis path evidence from #6601
   above these clusters without changing their identity or assignment.
4. #6313 may add separately proven semantic capability identity and package or
   ecosystem aggregation without relabeling direct-use clusters as features.

The first slice is useful independently: it distinguishes an isolated
one-method dependency footprint from one co-used component and from several
disconnected components. It does not recommend package removal or source
inlining.

## Normative claim

Given one `AssemblyPairCallUseResult`, the projection partitions every exact
occurrence into one deterministic connected component of its directed
source-method to target-method bipartite relation.

`AssemblyPairDirectUseClusterProjection` is separate from the shipped
`AssemblyPairCallUseProjection`; adding clustering does not change that
projection's public constructor or summary contract.

Two occurrences belong to the same direct-use cluster exactly when they:

- have the same directed source and target participants; and
- share a source method or target method, directly or transitively through
  other exact pair occurrences.

Repeated physical call sites do not alter membership. They remain separate
occurrence receipts and contribute to the call-site count.

Every occurrence belongs to exactly one cluster. Empty pair evidence produces
no clusters. Reversing the two request arguments does not change cluster
assignment or ordering because the underlying occurrence order is already
argument-independent.

## Identity and provenance

One cluster identity contains:

- exact source and target `AssemblyContextSubject` values;
- source and target module version IDs;
- the lowest source MethodDef token and lowest target MethodDef token in the
  component.

The two anchor tokens are version-specific component identity, not semantic
names or cross-version correspondence. Distinct components in one directed
pair cannot share a source or target method, so the anchors uniquely identify
the component within that exact pair result.

The derivation is the typed value
`ExactBipartiteConnectedComponent`. A consumer must not reconstruct provenance
from a label, ordinal, count, namespace, or declaring-type spelling.

Cluster ordinals are one-based presentation order across the complete pair
result. They remain deterministic document-local conveniences, not durable
identity; the directed source and target still belong to each cluster row.

## Retained footprint

Each cluster retains:

- distinct source methods in first-occurrence order;
- distinct structured target declaring types in first-occurrence order;
- distinct target methods in first-occurrence order;
- every supporting pair-result occurrence index in ascending order.

The target methods retain `MethodIdentity.IsExtension`. Extension-method count
is therefore a typed fact derived from exact target methods, not name,
namespace, first-parameter shape, or rendered C# syntax.

The directed relationship's **cluster breadth** is its cluster count. The first
within-cluster profile keeps separate counts:

- source methods;
- provider types;
- target methods;
- extension methods;
- physical call sites.

The projection does not collapse these dimensions into shallow, moderate,
deep, leverage, importance, or source-inlining scores. A repeated call site
increases only physical-site count. A second source method or target method
changes the corresponding structural dimension and may connect formerly
separate components.

## Completion

Clusters are positive evidence and remain useful when the pair result is
incomplete. The projection preserves `Pair.IsComplete`; it never turns
observed component count into a complete breadth or absence claim.

When pair evidence is incomplete:

- every exact retained occurrence is still assigned once;
- displayed clusters are explicitly observed direct-use clusters;
- zero clusters does not mean no relationship;
- the CLI retains the pair command's nonzero exit and detailed failure
  diagnostics.

No additional failure state is introduced because clustering is a total,
in-memory partition of already-validated occurrence identities. The projection
expands each distinct source or target method's occurrence adjacency only once,
so traversal work is linear in retained occurrences and method endpoints.
`ProjectionKeepsRepeatedPhysicalSitesLinear` gates the practical repeated-site
boundary with 50,000 exact physical occurrences and a ten-second timeout.

## Pathological evidence

The contract distinguishes these cases:

1. **One isolated method.** One source method calls one target method. The
   result is one cluster with one source method, one target method, and one or
   more physical sites. If the target method is an extension method, extension
   count is one.
2. **One co-used component.** Several source methods share one target method,
   or one source method uses several target methods. Transitive overlap keeps
   the relationship in one cluster while each footprint dimension remains
   separate.
3. **Several disconnected components.** Distinct source methods use distinct
   target methods with no shared endpoint. Declaring-type equality alone does
   not merge them.
4. **Repeated physical use.** Several call sites between the same methods
   remain one structural edge for membership and several occurrence receipts.
5. **Incomplete correspondence.** Exact positive components remain visible,
   but their count is not a complete breadth result.

Repository dogfood uses the exact
`DotnetInspector.Presentation` to `Markout` and
`DotnetInspector.MetadataRendering` to `Markout` relationships at repository
commit `22a076338578dd61fcfe8a122615a920a40e8440`. Markout `0.37.0` is the
current production package reference. Presentation's 38 use sites and 1,258
physical calls form eight direct-use clusters. MetadataRendering's 13 use sites
and 72 physical calls form one connected component because shared writer APIs
link its Markdown, tabular, and reverse-reference paths. That contrast is
intentional evidence of the boundary: connectivity exposes a direct-use
footprint, but does not manufacture semantic feature separation.

## Analogous implementations

- Java 25
  [`jdeps`](https://docs.oracle.com/en/java/javase/25/docs/specs/man/jdeps.html)
  reports archive, package, or class dependencies and can retain direct versus
  recursive scope. It does not group exact used methods into semantic features
  or recommend source inlining.
- The .NET trimmer removes unreachable deployment code and emits warnings when
  reflection or invisible dependencies prevent complete analysis. The
  [Microsoft Learn contract](https://github.com/dotnet/docs/blob/45d22543cf002fb88caf8938c2580740c69d3f25/docs/core/deploying/trimming/trim-self-contained.md)
  supports explicit incomplete-evidence disclosure, but trimming does not
  assign dependency capability identity.
- NDepend's
  [Dependency Structure Matrix](https://www.ndepend.com/docs/dependency-structure-matrix-dsm)
  exposes coupling strength through separate member, method, field, type, or
  namespace counts and uses graph structure to reveal cohesive regions. It
  motivates retaining separate dimensions instead of one opaque score, but
  does not supply the exact occurrence-backed cluster identity required here.

The repository deliberately stops below those tools' architectural or
deployment interpretations. Connected components are used only as an exact,
explainable partition of the observed pair relation. No statistical community
detection, namespace heuristic, or producer-intent inference is introduced.

## CLI projection

`Direct Use Clusters` is an explicit section. It does not enter the default
call-site view or bare `-S`, preserving current output and cost expectations.

Each row exposes the directed pair, cluster ordinal and anchor tokens,
structural footprint counts, and one-based `Call Site Rows` references into the
unchanged exact call-site table. Normal section discovery, row windows, Count,
table, TSV, JSONL, JSON, Markdown, and plain-text lowering apply through the
existing generated Markout context.

`--where "Cluster=N"` is the drill-down gesture for one pair-wide cluster
ordinal. `Cluster` is a typed equality predicate over the logical pair
occurrences, applied before section projection rather than parsed from rendered
rows. It restricts every requested section to that cluster's retained
occurrences; without `-S`, the result is the ordinary exact call-site table for
that cluster. The selected cluster row remaps `Call Site Rows` to its scoped
one-based call table, so the cluster summary and detail document agree. An
unavailable ordinal fails visibly, and an unobserved ordinal in incomplete
evidence is not reported as a proved absence.

The predicate accepts exactly one positive integer ordinal and only the
equality operator. A dedicated `--cluster` flag is intentionally not minted:
the ordinal is existing typed row data, not a new coordinate currency. This
follows [Output shapes](output-shapes.md#coordinate-carriers-sit-before-the-ladder),
which prefers a section, category, or `--where` predicate before adding a
carrier. `graph libraries -Q "Call Sites"` and the other three projection
names expose the same `Cluster` binding without inspecting the library pair.

`AssemblyPairDirectUseClusterProjection.ScopeToObservedCluster` owns the
host-neutral transformation from a complete-pair projection to that
occurrence-scoped pair and remapped cluster receipt. The CLI and future
Browser/Wasm consumers share it rather than reconstructing selection from
rendered row text.

The intended CLI journey is:

```console
dotnet-inspect graph libraries \
  --library ./Consumer.dll \
  --library ./Provider.dll \
  -S "Direct Use Clusters"

dotnet-inspect graph libraries \
  --library ./Consumer.dll \
  --library ./Provider.dll \
  --where "Cluster=3"
```

The second command exposes exact source and target members and tokens, call
kind, evidence method and token, and IL offset. Source and target identities
hand off to ordinary `member` inspection. The evidence token and IL offset hand
off to `library coordinate`, because compiler-generated physical evidence
bodies can differ from attributed source methods. Cluster selection does not
add a parallel source, decompilation, or call-graph host.

## Explicit non-goals

- semantic feature or capability naming;
- public-entrypoint reachability or root-to-use-site paths;
- provider implementation reachability or graph-community inference;
- signature-only, inheritance, field, delegate, reflection, or dynamic use;
- cross-version cluster correspondence;
- package removability, source-inlining safety, license analysis, or generated
  replacement source;
- package, project, network, or Browser UI acquisition and presentation.
