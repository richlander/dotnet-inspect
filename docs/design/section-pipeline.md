# Section pipeline

The section pipeline is the runtime implementation of the
[section model](section-model.md). It separates user intent from producer
execution:

```text
user gesture
  -> candidate sections
  -> direct producer demand
  -> query prerequisite closure
  -> typed query execution and projection
  -> effectiveness
  -> rendering
```

The command owns the gesture and budget. `SectionPipeline<TModel>` owns section
and category planning. `InspectionQueryRegistry` authors typed query
declarations; its immutable `InspectionQueryCatalog` owns query cost,
prerequisites, optional composition, planning, and execution.

## Section descriptors

`ISectionDescriptor<TModel>` declares typed section metadata. Descriptors are
read through static members and are never instantiated, preserving
NativeAOT-friendly product behavior.

| Concept | Purpose |
| --- | --- |
| `Name` | Stable selector and rendered heading |
| `SizeClass` | Output cardinality: fixed, terse, informative, or verbose |
| `Cost` | Section-specific lower bound on production cost |
| `Queries` | Typed producers bound by definition identity |
| `ExplicitOnly` | Excludes the section from automatic verbosity |
| `CanRender` | Post-production effectiveness predicate |
| Applicability predicate | Cheap structural gate supplied at registration |

`SizeClass`, `Cost`, and `ExplicitOnly` are independent. A fixed-size section
can require expensive work, and a cheap producer can feed verbose output.
`ExplicitOnly` is execution policy, not user-facing section identity; discovery
does not label sections as "opt-in".

## Categories and candidates

Categories are authored through `AddBaseCategory` and `AddCategory`.

- Base categories define automatic verbosity, bare-`-S`, and flat discovery
  scope.
- Domain categories are explicit doors.
- A section can belong to more than one category.
- Membership is never inferred from a display-name prefix.

Automatic candidate selection intersects:

- the base-category union;
- the verbosity preset;
- size and effective cost policy;
- explicit-only policy.

Exact section selection overrides automatic scope. Category selection expands
to authored members before query demand is computed. Bare `-S` uses the
fixed, network-free subset of the base union.

The library catalog calls `WithoutComputedPoles`; it does not expose computed
`@All` or `@Hidden` selectors.

## Producer registry

Content-shaped producers register an `InspectionQuery<T>` definition.
Sections bind to that definition by object identity, and the host projects its
typed result into the compatibility model.
`ClassifiedMethodsQuery` is shared by `Library Info`, P/Invoke Methods, Async
Methods, and Signals; one demand set executes it once against the command-owned
`AssemblyInspectionSession`. `Signals` also binds `AuditMetadataQuery` and
`AssemblyReferencesQuery`; the host applies all three typed results before
CLI-owned signal composition, then recomposes only model-derived rows after
later source evidence lands. `Unsafe Members` binds the unbounded
`UnsafeEvidenceQuery`, which consumes the command's shared Analysis body index
and retains raw unsafe evidence through the Finding and presentation boundary.
Bare library discovery instead uses the network-free
`UnsafeEvidencePresenceQuery`, which reuses the same Analysis safety producer
but stops at the first finding and does not materialize the body index or
decoded instruction arrays. It uses a synchronous capability callback over
the command-owned non-prefetched reader and visits methods sequentially in
metadata order. Signature-marker prescans
are no-copy, cached by blob, and charged to a 4 MiB assembly-wide budget;
streaming instruction visits have a separate 4 MiB aggregate budget. A
marker-bearing declaration, local, member-reference, method-definition, or
method-specification signature that the structural guard rejects makes the
presence result explicitly incomplete instead of becoming successful absence
or affirmative evidence, even when a later method contains conclusive evidence.
Decoded custom modifiers retain their unmodified type, so a wrapped pointer
still counts as unsafe evidence. Name-only `Unsafe` candidates require trusted
framework identity resolution before becoming evidence. The discovery gate
also retains renderable metadata signature-decode diagnostics so a negative
bounded probe cannot hide a known-incomplete scan.
`UnsafeEvidencePresenceTests` gates these properties, including lookalike
identity, signature-guard, aggregate-budget, and large-suffix cases.
`Top Leverage` binds `TopLeverageQuery`, which retains ranked
`MethodLeverage`, generated-framework evidence, and Analysis diagnostics until
the presentation boundary. The CLI joins its API-surface drill map for legacy
JSON and row selectors; the query does not own visibility or selector policy.
The eight Performance sections share `OptimizationOpportunitiesQuery`, which
retains raw `OptimizationOpportunity`, generated-framework `TypeRef` identity,
and Analysis diagnostics. The CLI owns generated-code suppression, row
filtering, ranking, kind bucketing, MVID-preserving compatibility JSON, and
presentation containment. `BodyShapesQuery` retains the complete typed
`BodyShapeSearchResult` through structured and Markdown sinks. It declares
`OptimizationOpportunitiesQuery` as an optional dependency: when Performance
predicates independently demand Optimization Opportunities, the registry runs
that query first and exposes its typed result to Body Shapes; an unfiltered
Body Shapes search neither closes over nor pays for it. In the composed case
the filtered typed opportunities remain available to the query adapter, but the
compatibility Performance JSON projection is not materialized;
`ComposedBodyShapesJson_OmitsUnselectedPerformanceProjection` gates that
section-isolation contract. Full effective discovery reports a failed typed
producer as a section failure and exits unsuccessfully rather than describing
its sections as empty; `LibraryCommand_EffectivePerformanceDiscoveryNamesOptimizationFailure`
and `LibraryCommand_EffectiveComposedBodyShapesDiscoveryNamesOptimizationFailure`
gate the direct and composed projections.
`Array Pool Escapes` binds `ResourceTriageQuery`, which retains the complete
resource-lifecycle Finding inspection and every typed triage assessment. The
CLI owns actionable filtering, ordering, member drill coordinates,
compatibility JSON, prose, and final presentation containment.

The former string-keyed `ScannerRegistry` axis has been retired. Library
sections now bind typed queries or consume baseline command facts.

## Query-owned cost

Production cost belongs to the query because multiple sections can be views
over the same work. `UseQueryCosts(catalog.CostOf)` binds a pipeline to the catalog before any
section is added.

A section's effective cost is:

```text
max(descriptor cost, query prerequisite-closure cost)
```

A descriptor may raise cost for section-specific work or output, but it cannot
lower query-owned cost. `CostOf` uses the maximum cost over the required
prerequisite closure. Optional dependencies do not raise the consumer's cost;
when independently demanded they execute under their own declaration.

The three production tiers are:

| Cost | Automatic behavior |
| --- | --- |
| `NetworkFree` | Eligible for ordinary automatic views |
| `Moderated` | Eligible only for detailed automatic output |
| `Unbounded` | Never enters an automatic verbosity preset |

An unbounded section remains reachable through exact selection, explicit
category selection, or effective category discovery.

`LibrarySections` retains one process-wide immutable per-assembly query catalog
and one assembly-group catalog. Query catalog construction validates the
complete required graph and precomputes each query's closure, transitive cost,
and single-query execution plan. Repeated catalog acquisition and single-query
planning allocate no memory after static initialization;
`LibraryQueryCatalog_RepeatedAcquisitionAndPlanningAllocateNothing` gates that
property.

`SectionPipeline<TModel>` remains the mutable section-authoring API.
`Compile()` freezes it and returns an immutable `SectionCatalog<TModel>` that
snapshots stable section and category enumeration. Compilation also preserves
the frozen pipeline for candidate, effectiveness, and rendering APIs rather
than introducing a parallel selection implementation. A compiled builder
rejects later section, category, cost, or pole changes;
`CompiledSectionCatalog_FreezesBuilderAndSnapshotsEnumeration` gates that
boundary.

The library retains one process-wide compiled section catalog. It precomputes
query-demand plans for every automatic verbosity/fixed-overview combination,
exact single-section selection, category selection, and the base-category
union, with bounded and unbounded variants. An uncommon arbitrary section set
is compiled once for that request. Each immutable plan retains both unique
queries in section-registration order and section-to-query demand pairs for
`InspectionTrace`; activation creates the request-owned mutable demand set and
adds attributed command demand. `LibrarySectionCatalog_QueryPlansMatchMutablePipeline`
and `CompiledSectionQueryPlan_PreservesTraceAttributionAndCommandDemand` gate
equivalence. Repeated library catalog acquisition and common plan lookup
allocate no memory after static initialization;
`LibrarySectionCatalog_RepeatedAcquisitionAndCommonPlanningAllocateNothing`
gates that narrower claim. `LibrarySections.CreatePipeline()` remains a fresh
mutable compatibility builder for focused tests and extensions.

`PackageSectionDescriptors` applies the same fixed-domain model to package
inspection: one process-wide SourceLink query catalog and one compiled section
catalog replace per-command registry and pipeline construction.
`PackageSectionCatalog_QueryPlansMatchMutablePipeline` gates section-demand
equivalence, and
`PackageCatalog_RepeatedAcquisitionAndCommonPlanningAllocateNothing` gates
allocation-free repeated catalog acquisition and common plan lookup. Package
execution composes the selected section plan with the immutable query catalog
once per request and reuses that execution plan across every selected package
and every library inspected inside it. `CreatePipeline()` and
`CreateQueryRegistry()` remain fresh mutable compatibility builders.

`DiffSections` likewise retains one process-wide query catalog and compiled
section catalog for its fixed comparison domain. Diff command execution
composes one immutable query plan from the selected section plan and executes
that plan against request-owned comparison inputs.
`DiffSectionCatalog_QueryPlansMatchMutablePipeline` gates section-demand
equivalence, and
`DiffCatalog_RepeatedAcquisitionAndCommonPlanningAllocateNothing` gates
allocation-free repeated catalog acquisition and common plan lookup.
`CreatePipeline()` and `CreateQueryRegistry()` remain fresh mutable
compatibility builders.

`PackageProfileSections` completes the same migration for the fixed
package-prefix profile domain. Discovery, query execution, and rendering share
one process-wide query catalog and compiled section catalog; execution selects
the section plan once and runs its precomputed single-query plan against the
request-owned package source.
`PackageProfileSectionCatalog_QueryPlansMatchMutablePipeline` gates
section-demand equivalence, and
`PackageProfileCatalog_RepeatedAcquisitionAndCommonPlanningAllocateNothing`
gates allocation-free repeated catalog acquisition and common plan lookup.
`CreatePipeline()` and `CreateQueryRegistry()` remain fresh mutable
compatibility builders.

Commands compile arbitrary multi-query demand once and reuse the resulting
immutable query plan for every assembly in that request, so package inspection
does not rebuild dependency state per assembly. The mutable
`InspectionQueryRegistry` remains the query-authoring surface for dynamic hosts
and focused extensions; its `Compile` method snapshots it into a catalog
without allowing later registrations to mutate that snapshot.

## Compiled lenses and query domains

`InspectionLensCatalog<TContext, TModel>` is the reusable L2 composition
between one immutable section lens and one immutable L1 query domain. It does
not create another producer registry: `InspectionQueryCatalog<TContext>`
remains the owner of producer identity, dependencies, cost, planning, and
execution, while `SectionCatalog<TModel>` remains the owner of section
selection and direct producer demand.

A complete lens binding validates at construction that every query declared by
its sections belongs to the bound query catalog. Multiple section lenses may
share the same query catalog, which is the intended shape for commands with
alternate views over one producer domain. Package Profile is the first adopter:
its command now asks the generic lens catalog to lower the selected `Packages`
section directly to the reusable query plan.
`InspectionLensCatalog_RejectsAnUnregisteredSectionQuery` and
`PackageProfileCatalog_RepeatedAcquisitionAndCommonPlanningAllocateNothing`
gate registration completeness and the allocation-free common planning path.

A section lens may span producers that require different context types.
`CreatePartition` binds one such query domain and plans only the selected
queries registered there. The composing host must call `ValidatePartitions`
over all bindings; it rejects both unclaimed and multiply claimed
section-declared queries. A query and its complete required dependency closure
must remain in one partition. Partial-partition filtering is request-time
composition and may allocate; commands compile it once per request rather than
rebuilding it for every inspected item.
`InspectionLensCatalog_PartitionsOneLensAcrossQueryDomains` and
`InspectionLensCatalog_RejectsMissingOrOverlappingPartitions` gate that
boundary.

The lens catalog does not own request context construction, workspace
admission, acquisition lifetime, analysis-request semantics, semantic row
selection, sink projection, or rendering. Those owners provide the context or
consume the typed query results. In particular, row selection remains a
post-production operation, and display-bound artifact text remains
`InertString` when projected to sink models. Synchronous and asynchronous
execution, cancellation, failure visibility, and execution ordering remain
properties of the bound query plan.

## Resource declarations

Whole-assembly body analysis is acquired through
`InspectionQueryContext.BodyIndex()`. Member drill data is acquired through
`InspectionQueryContext.DrillMap()`.

Only a query declared `Unbounded` may acquire either resource. A cheaper query
that calls one throws at the acquisition boundary. Production
catch boundaries do not convert that declaration violation into a
success-shaped result.

Typed queries use the same host-side resource guard. The query catalog enters
an execution scope with each query's maximum transitive `InspectionCost`; the
CLI adapter maps that cost to `SectionCost`. Query planning, contract, and
executor failures remain fail-visible, while cancellation and cost-declaration
failures retain their specific exception types.

Declarations are scoped to one catalog execution and cannot leak into later
work. This is a correctness mechanism for well-behaved product-owned query
wiring, not an in-process security boundary.

Metadata query adapters share the command's open inspection session. They do not
reopen the target independently, and they continue to observe the image the
command opened even if the path is retargeted during the run.

## Gesture planning

The command translates a user gesture into candidate scope and command-level
demand before the registry runs.

For library discovery:

| Gesture | Candidate scope | Producer behavior |
| --- | --- | --- |
| `-D` | Base sections, category doors, effective standalone sections | Metadata and bounded presence queries |
| `-D --effective` | Base-category union | Full base query closure |
| `-D @Category` | Authored category members | Structural; no member queries |
| `-D @Category --effective` | Authored category members | Full category query closure |
| `-D --schema` | Complete graph | No target queries |

Plain discovery therefore stays within the local-target latency budget.
Explicit effective discovery may request unbounded work. In particular,
`-D @Performance` does not build the body index, while
`-D @Performance --effective` does.

Some command facts are not expressed by a section producer. The command passes
those typed queries as attributed command demand so the same closure and trace
machinery still owns execution.

`References` binds typed direct-reference inspection:

- `-S References` collects direct assembly references and renders a flat table.
- `-S References --tree` additionally resolves the transitive graph.
- `--depth N` limits traversal; depth 1 contains direct references.

The planner enables direct or tree collection from the candidate set instead
of creating synonymous sections.

Extension-method, custom-attribute, manifest-resource, and type-forwarder
inspection are also typed query work. `Library Info` binds all four query
definitions, while `Extension Methods`, `Custom Attributes`, `Resources`, and
`Type Forwarders` each bind the definition for their detailed rows. One
immutable result per facet therefore supplies the summary count and detailed
rows without string scanner keys or duplicate metadata passes. `Union Types`
binds `UnionTypesQuery` for its detailed rows; the deeply immutable result
preserves metadata order and exact identity until the row boundary. `Switches`
binds the Research-backed `SwitchesQuery`, which composes declared metadata
with AppContext IL evidence into one immutable ordered result. A section may
bind multiple typed queries; its effective cost is the maximum over every
query's prerequisite closure.

## Effectiveness

The pipeline exposes separate queries for:

- structural applicability;
- candidate selection;
- post-production renderability.

Structural applicability answers whether a section can apply without running
its producer. Post-production effectiveness answers whether the producer
actually found renderable evidence.

For example, method bodies make performance analysis applicable but do not
prove that any performance finding exists. Structural performance discovery
uses the former; `--effective` uses the latter.

Full bare discovery remains scoped to base categories. Domain applicability
can preserve a category door without placing its members in the flat base
catalog.

## Rendering

`ComputeIncludeSections` produces the section-name set passed to Markout.
Markout owns serialization and section filtering.

The curated verbosity contract is:

| Verbosity | Automatic candidates |
| --- | --- |
| Quiet | Headless compact summary only |
| Minimal | High-value info section, excluding unbounded work |
| Normal | Terse and informative, network-free base sections |
| Detailed | All bounded base sections |

Compact identity fields are reserved for quiet verbosity. Minimal does not
inherit the headless quiet summary.

Row-oriented formats require one concrete schema or a homogeneous family.
Heterogeneous categories are rejected before producers run.

## Registration and behavior gates

The test suite names the properties that enforce this architecture:

- `LibraryQueryRegistry_RegistrationMatchesDeclaration`
- `TypedQueryRegistry_OptionalDependencyRunsOnlyWhenIndependentlyRequested`
- `TypedQuery_CannotTakeTheBodyIndexWithoutDeclaringItsTransitiveCost`
- `TypedQuery_CannotTakeTheDrillMapWithoutDeclaringItsCost`
- `ProductionQueryCatchBoundary_DoesNotSwallowExecutorFailure`
- `LibraryPipeline_ConsultsQueryCosts`
- `LibrarySections_AboveNetworkFree_AreExplicitlyPinned`
- `ClassifiedAndAuditQueries_ObserveOneSession`

Category ownership and output-shape gates live with the section model. Tests
derive sets from declarations where possible so both stale and missing entries
fail.

## Tracing

Library `--trace` records:

- section-to-query demand;
- command-level demand;
- prerequisite expansion;
- query execution time and failure;
- body-index and drill-map acquisition;
- shared metadata-session use.

Trace output is diagnostic stderr and never changes document stdout.
