# Section pipeline

The section pipeline is the runtime implementation of the
[section model](section-model.md). It separates user intent from producer
execution:

```text
user gesture
  -> candidate sections
  -> direct producer demand
  -> scanner and query prerequisite closure
  -> typed query execution and projection
  -> scanner execution
  -> effectiveness
  -> rendering
```

The command owns the gesture and budget. `SectionPipeline<TModel>` owns section
and category planning. `InspectionQueryRegistry` owns typed query cost,
prerequisites, and execution. `ScannerRegistry` owns the residual mutable
producer cost, prerequisites, execution, and shared resource declarations.

## Section descriptors

`ISectionDescriptor<TModel>` declares typed section metadata. Descriptors are
read through static members and are never instantiated, preserving
NativeAOT-friendly product behavior.

| Concept | Purpose |
| --- | --- |
| `Name` | Stable selector and rendered heading |
| `SizeClass` | Output cardinality: fixed, terse, informative, or verbose |
| `Cost` | Section-specific lower bound on production cost |
| `ScannerKey` | Producer demand key; `null` means core inspection |
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
to authored members before scanner demand is computed. Bare `-S` uses the
fixed, network-free subset of the base union.

The library catalog calls `WithoutComputedPoles`; it does not expose computed
`@All` or `@Hidden` selectors.

## Producer registries

`ScannerRegistry` maps each scanner key to a scan function, declared
`SectionCost`, and immutable prerequisite list:

```csharp
registry.Add(
    ScannerResourceTriage,
    SectionCost.Unbounded,
    ctx => ctx.Model.Apply(
        LibraryMetadataService.ScanResourceTriage(
            ctx.BodyIndex,
            ctx.DrillMap,
            ctx.AssemblyPath,
            ctx.Logger)));
```

`AddBundle` registers prerequisite closure without adding work or declaring a
synthetic cost. A bundle costs the maximum of the scanners it requires.

Content-shaped producers instead register an `InspectionQuery<T>` definition.
Sections bind to that definition by object identity, and the host projects its
typed result into the compatibility model before residual scanners run.
`ClassifiedMethodsQuery` is shared by `Library Info`, P/Invoke Methods, Async
Methods, and Signals; one demand set executes it once against the command-owned
`AssemblyInspectionSession`. `Signals` also binds `AuditMetadataQuery` and
`AssemblyReferencesQuery`; the host applies all three typed results before
CLI-owned signal composition, then recomposes only model-derived rows after
later source evidence lands. `Unsafe Members` binds the unbounded
`UnsafeEvidenceQuery`, which consumes the command's shared Analysis body index
and retains raw unsafe evidence through the Finding and presentation boundary.
`Top Leverage` binds `TopLeverageQuery`, which retains ranked
`MethodLeverage`, generated-framework evidence, and Analysis diagnostics until
the presentation boundary. The CLI joins its API-surface drill map for legacy
JSON and row selectors; the query does not own visibility or selector policy.
The eight Performance sections share `OptimizationOpportunitiesQuery`, which
retains raw `OptimizationOpportunity`, generated-framework `TypeRef` identity,
and Analysis diagnostics. The CLI owns generated-code suppression, row
filtering, ranking, kind bucketing, MVID-preserving compatibility JSON, and
presentation containment. Body Shapes may demand the same typed result to
compose method candidates without selecting a Performance section. In that
case the filtered typed opportunities remain available to the scanner, but the
compatibility Performance JSON projection is not materialized;
`ComposedBodyShapesJson_OmitsUnselectedPerformanceProjection` gates that
section-isolation contract. Full effective discovery reports a failed typed
producer as a section failure and exits unsuccessfully rather than describing
its sections as empty; `LibraryCommand_EffectivePerformanceDiscoveryNamesOptimizationFailure`
and `LibraryCommand_EffectiveComposedBodyShapesDiscoveryNamesOptimizationFailure`
gate the direct and composed projections.

The residual `ScannerRegistry` now contains only Resource Triage and Body
Shapes.

The registry rejects:

- duplicate keys;
- unregistered requested keys or prerequisites;
- dependency cycles;
- missing cost declarations;
- mutation of prerequisite state after registration.

`ExpandRequired` computes transitive prerequisite closure.
`RunScanners` executes prerequisites first and each scanner once.

## Scanner-owned cost

Production cost belongs to the scanner because multiple sections can be views
over the same work. `UseScannerCosts(registry.CostOf)` binds a pipeline to the
registry before any section is added.

A section's effective cost is:

```text
max(descriptor cost, scanner prerequisite-closure cost)
```

A descriptor may raise cost for section-specific work or output, but it cannot
lower scanner-owned cost. `CostOf` uses the maximum cost over the full
prerequisite closure, so a nominally cheap scanner that requires an unbounded
scanner is itself unbounded.

The three production tiers are:

| Cost | Automatic behavior |
| --- | --- |
| `NetworkFree` | Eligible for ordinary automatic views |
| `Moderated` | Eligible only for detailed automatic output |
| `Unbounded` | Never enters an automatic verbosity preset |

An unbounded section remains reachable through exact selection, explicit
category selection, or effective category discovery.

`LibrarySectionCatalog` constructs one scanner registry, one typed-query
registry, and one cost-bound pipeline. Commands use that catalog for planning
and execution so the pipeline cannot snapshot costs from one registry while
another registry performs the work.

## Resource declarations

Whole-assembly body analysis is acquired through `ScannerContext.BodyIndex()`.
Member drill data is acquired through `ScannerContext.DrillMap()`.

Only a scanner or query declared `Unbounded` may acquire either resource. A
cheaper producer that calls one throws at the acquisition boundary. Production
catch boundaries do not convert that declaration violation into a
success-shaped result.

Typed queries use the same host-side resource guard. The query registry enters
an execution scope with each query's maximum transitive `InspectionCost`; the
CLI adapter maps that cost to `SectionCost`. Query planning, contract, and
executor failures remain fail-visible, while cancellation and cost-declaration
failures retain their specific exception types.

Declarations are scoped to one registry run and cannot leak into later work.
This is a correctness mechanism for well-behaved product-owned scanner and query
wiring, not an in-process security boundary.

Metadata scanners share the command's open inspection session. They do not
reopen the target independently, and they continue to observe the image the
command opened even if the path is retargeted during the run.

## Gesture planning

The command translates a user gesture into candidate scope and command-level
demand before the registry runs.

For library discovery:

| Gesture | Candidate scope | Scanner behavior |
| --- | --- | --- |
| `-D` | Base sections and category doors | Metadata presence only |
| `-D --effective` | Base-category union | Full base scanner closure |
| `-D @Category` | Authored category members | Structural; no member scanners |
| `-D @Category --effective` | Authored category members | Full category scanner closure |
| `-D --schema` | Complete graph | No target scanners |

Plain discovery therefore stays within the local-target latency budget.
Explicit effective discovery may request unbounded work. In particular,
`-D @Performance` does not build the body index, while
`-D @Performance --effective` does.

Some command facts are not expressed by a section producer. The command passes
those scanners or typed queries as attributed command demand so the same closure
and trace machinery still owns execution.

`References` is core metadata rather than scanner work:

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

- `LibraryScannerRegistry_RegistrationMatchesDeclaration`
- `LibraryScannerCosts_AreDeclaredForEveryRegisteredScanner`
- `LibraryScannerPrerequisites_AreAllRegisteredAndAcyclic`
- `CostOf_IsTheMaximumOverTheTransitivePrerequisiteClosure`
- `Scanner_CannotTakeTheBodyIndexWithoutDeclaringItsCost`
- `Scanner_CannotTakeTheDrillMapWithoutDeclaringItsCost`
- `TypedQuery_CannotTakeTheBodyIndexWithoutDeclaringItsTransitiveCost`
- `TypedQuery_CannotTakeTheDrillMapWithoutDeclaringItsCost`
- `ProductionQueryCatchBoundary_DoesNotSwallowExecutorFailure`
- `PrerequisiteCost_CannotShiftAfterSectionsSnapshotIt`
- `SectionsBackedByUnboundedScanners_LeaveTheDetailedLadderButKeepTheirDoor`
- `SharedSessionScanners_ObserveTheImageTheCommandAlreadyOpened`

Category ownership and output-shape gates live with the section model. Tests
derive sets from declarations where possible so both stale and missing entries
fail.

## Tracing

Library `--trace` records:

- section-to-scanner demand;
- section-to-query demand;
- command-level demand;
- prerequisite expansion;
- scanner and query execution time and failure;
- body-index and drill-map acquisition;
- shared metadata-session use.

Trace output is diagnostic stderr and never changes document stdout.
