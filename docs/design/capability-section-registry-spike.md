# Capability-driven section registry spike

Status: **static lambda-table pilot recommendation**.

This is the runnable evaluation for
[issue #2605](https://github.com/richlander/dotnet-inspect/issues/2605).
It does not migrate a production command. It compares the current planning
shape with a reusable registry, measures initialization, lookup, execution, and
allocations, and records the code-quality tradeoff.

## Current split and drift points

`SectionPipeline<TModel>` owns section selection:

- registration order and categories;
- verbosity and explicit-only behavior;
- `-D`/`-S` selection;
- `ProbeEffectiveness`;
- `ScannerKey` and network `SectionCapabilities`.

Execution is coordinated elsewhere:

1. `ScannerRegistry` maps descriptor `ScannerKey` strings to delegates.
2. `ScannerContext.BodyIndex()` hides the shared body-index prerequisite and
   memoizes it outside descriptor metadata.
3. `LibraryMetadataService.InspectAsync` separately asks
   `GetAuthorizedSections` about PDB and source permissions.
4. Other work still uses direct section-name branches.

The current behavior is efficient once running: the body index is already
built once. The problem is that selection metadata, prerequisites,
authorization, and execution live in separate places and remain synchronized
by convention.

## Design progression

The first typed design used `Register<TCapability>()`, a runtime dictionary,
dependency arrays, graph validation, and per-section closure compilation. It
improved execution but made construction materially worse. That design is
retained in the branch history as evidence, not as the recommendation.

The final design follows the
[SmoothMarkdown registry](https://github.com/richlander/smooth-markdown-table/blob/main/src/MarkdownTable.Documents/RendererRegistry.cs)
shape: one static table of noncapturing lambdas, with precompiled plans and
one reusable registry.

```text
static section table
    -> applicability and render lambdas
    -> preordered execution lambdas
    -> mode intersection for authorization
    -> generated-style selection-mask plans
    -> existing SectionPipeline render filtering
    -> existing Markout rendering
```

### Static table

Each executable operation is one data entry:

```csharp
public readonly record struct CapabilityPlanEntry<TContext>(
    int Id,
    string Name,
    CapabilityExecutionModes AllowedModes,
    Func<TContext, ValueTask> Execute);
```

The table defines metadata and behavior with static, noncapturing lambdas:

```csharp
new(
    "Calls",
    IsExpensive: false,
    ExplicitOnly: true,
    Info: false,
    static model => model.HasMethodBodies,
    static model => model.Calls > 0,
    new CapabilityPlan<SpikeContext>(bodyIndex, calls))
```

There is no capability type, `CapabilityKey`, `Register<T>()`, factory,
`Activator`, reflection scan, or runtime dependency graph. Per-run values
remain on caller-owned context.

### Plan behavior

Dependency order is compiled into the static plans:

| Section | Precompiled plan | Policy |
| --- | --- | --- |
| Metadata | `Metadata` | Probe, detailed, explicit |
| Decompiled Source | `Decompile` | Explicit |
| Original Source | `AcquirePdb`, `FetchSource` | Explicit |
| Calls | `BodyIndex`, `Calls` | Explicit |
| Facts | `BodyIndex`, `Facts` | Explicit |

Named categories use a generated-style selection-mask lambda. `Calls + Facts`
returns the static `BodyIndex, Calls, Facts` plan, so the shared prerequisite
executes once without a graph merge. Single sections return their table plan.
An uncommon arbitrary combination takes an explicit cold compile path.

`CapabilityPlan.ExecuteAsync` intersects allowed modes once at plan creation.
The successful path checks that aggregate, then invokes the lambdas directly.
If one lambda becomes asynchronous, execution moves to a slow continuation
only from that entry onward.

Probe safety and network authorization are therefore properties of the whole
plan, not duplicated section flags. Authorization is preflighted before work:
a source plan rejected at detailed verbosity cannot acquire a PDB and then
fail on source fetch.

## Correctness evidence

Run:

```bash
dotnet run --project tools/SectionRegistrySpike -c Release -- --verify
```

The verifier covers the four issue strategies:

| Strategy | Result |
| --- | --- |
| Describe/schema | Same names, order, categories, cost annotations, and category resolution; no work |
| Structural discovery | Same discoverable sections on an unexecuted model; no work |
| Effective discovery | Only `Metadata` executes; expensive plans remain deferred |
| Render | Same trace, work count, selected sections, and output for every representative selection |

Measured work is identical:

| Selection | Current | Static table |
| --- | ---: | ---: |
| Empty | 0 | 0 |
| Metadata | 1 | 1 |
| Decompiled Source | 1 | 1 |
| Original Source | 2 | 2 |
| Calls | 2 | 2 |
| Calls + Facts | 3 | 3 |
| Metadata + Facts (cold arbitrary plan) | 3 | 3 |

Negative checks cover duplicate plan entries and section names, derived probe
safety, operation-specific authorization, preflight-before-mutation, and retry
after a failed lambda. Missing dependencies and cycles no longer have runtime
failure modes because the static plan has no dependency graph; a production
generator should diagnose those while emitting ordered plans.

## Steady-state performance

Run:

```bash
dotnet run --project tools/SectionRegistrySpike -c Release -- --benchmark
```

Representative managed result on .NET 11 preview 5, macOS arm64:

| Scenario | Current ns/op | Static ns/op | Time delta | Current B/op | Static B/op | Allocation delta |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Registry acquisition | 159.1 | 9.6 | -93.9% | 1280 | 0 | -100.0% |
| Plan one section | 99.0 | 8.3 | -91.7% | 176 | 0 | -100.0% |
| Plan shared sections | 39.4 | 23.9 | -39.4% | 176 | 0 | -100.0% |
| Plan arbitrary sections (cold) | 36.6 | 50.3 | +37.4% | 176 | 96 | -45.5% |
| Execute one section | 71.2 | 4.4 | -93.8% | 0 | 0 | 0.0% |
| Execute shared sections | 32.2 | 29.0 | -9.9% | 0 | 0 | 0.0% |

The same executable published with NativeAOT:

| Scenario | Current ns/op | Static ns/op | Time delta | Current B/op | Static B/op | Allocation delta |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Registry acquisition | 225.8 | 2.5 | -98.9% | 1280 | 0 | -100.0% |
| Plan one section | 45.2 | 13.2 | -70.7% | 176 | 0 | -100.0% |
| Plan shared sections | 51.7 | 24.0 | -53.6% | 176 | 0 | -100.0% |
| Plan arbitrary sections (cold) | 52.3 | 70.2 | +34.1% | 176 | 96 | -45.5% |
| Execute one section | 32.3 | 5.3 | -83.5% | 0 | 0 | 0.0% |
| Execute shared sections | 38.4 | 8.3 | -78.5% | 0 | 0 | 0.0% |

These are mechanics microbenchmarks, not product throughput. They use nine
post-warmup samples and report the median. Execution rows reset existing
contexts. Single-section and shared planning query precompiled plans. The
arbitrary-selection row compiles a plan once per call; it retains one exact-size
array but still allocates 45.5% fewer bytes than the current planner. Registry
acquisition compares current per-use construction with reading the initialized
static registry.

## Cold initialization

The static table still has a one-time cost. Measure each design in a separate
fresh process:

```bash
dotnet run --project tools/SectionRegistrySpike -c Release -- \
  --benchmark-init-current
dotnet run --project tools/SectionRegistrySpike -c Release -- \
  --benchmark-init-static
```

Allocated bytes are stable; managed first-use time includes JIT effects and is
not used as evidence:

| Runtime | Current first construction | Static initialization | Later current acquisition | Later static acquisition |
| --- | ---: | ---: | ---: | ---: |
| Managed | 2272 B | 3808 B | 1280 B | 0 B |
| NativeAOT | 2024 B | 3096 B | 1280 B | 0 B |

The static table is not a smaller cold object graph than one current
construction. It fixes construction by changing its lifetime. Cumulative
allocated bytes break even on the third managed acquisition and the second
NativeAOT acquisition; every later acquisition is allocation-free.

That distinction matters for a short-lived CLI. A static registry is justified
only when one process reuses it across commands, assemblies, or repeated
section queries, or when the generated production table is smaller than this
general-purpose spike.

## Allocation-fanout evidence

The opt-in `allocation-fanout` view from
[#2736](https://github.com/richlander/dotnet-inspect/pull/2736) exposes the
structural quantity that occurrence-local diff missed:

| Method/design | Once paths | Lifetime |
| --- | ---: | --- |
| Production `LibrarySections.CreatePipeline` | 53 | Every factory call |
| Dynamic typed `CreateCapabilityRegistry` | 10 | Every construction |
| Static `SpikeSections..cctor` | 34 | Once per process |
| Static registry accessor | 0 | Every acquisition |
| Static `CapabilitySectionRegistry.PlanFor` | 0 | Precompiled selection: 0 B |
| Static `CapabilitySectionRegistry.CompilePlan` | 0 | Cold arbitrary selection: 96 B |
| Static `CapabilityPlan.ExecuteAsync` | 0 | Successful path |

The static initializer also reports 33 direct sites, 10 unknown paths, and 58
opaque paths. Those are not hidden; they are moved to one process-wide event.
The initializer has fewer once paths than the production pipeline factory, and
reuse turns the old repeated construction quantity into zero.

The cold compiler has one direct array site classified as conditional, plus one
unknown path. `PlanFor` exposes that branch plus input-enumeration uncertainty;
its measured precompiled paths allocate nothing.

The analysis remains structural, not a runtime object count. The fresh-process
allocated-byte measurement above supplies the runtime quantity.

## Code-quality evaluation

Blank and comment-only lines are excluded. The baseline count covers
`CurrentBaseline/`; registry counts cover `Capabilities/` and `Sections/`:

| Measure | Current shape | Dynamic typed shape | Static lambda shape |
| --- | ---: | ---: | ---: |
| Core non-comment lines | 155 | 405 | 425 |
| Core source files | 1 | 9 | 5 |
| Per-section execution declarations | 10 | 5 | 5 |
| Runtime dependency graph | None | Dictionary + traversal | None |
| Dispatch paths | Scanner + network branches | One plan | One plan |
| Probe decision | Section Boolean | Plan closure | Plan modes |
| Per-capability objects | Not applicable | 0 | 0 |

The optimized cold compiler makes raw line count slightly higher than the
dynamic design. Code quality improves structurally instead: five source files
replace nine, and four concepts disappear: `CapabilityKey`, capability types,
generic registration, and runtime graph resolution. The remaining extra code
owns authorization, async continuation, selection masks, topological merging,
and allocation-light cold arbitrary-plan composition.

The principal quality gain is one inspectable table:

- one execution lambda per operation;
- one section row for applicability, rendering, and plan selection;
- explicit prerequisite order;
- one authorization and execution path;
- no reusable mutable execution state.

## NativeAOT and generation

The design uses static lambdas, arrays, dictionaries, and concrete generic
types. It does not use reflection enumeration, `Activator`, expression
compilation, runtime code generation, Roslyn, or inspected-assembly loading.
The spike publishes and runs as an `osx-arm64` NativeAOT binary.

A production generator should emit:

- operation entries and noncapturing lambdas;
- ordered single-section plans;
- named category/common-selection plans;
- section rows and the selection-mask switch;
- diagnostics for missing prerequisites, cycles, duplicate IDs, and names.

Generation is valuable here because it removes runtime graph work and makes the
single table the source of truth. The checked-in hand-authored table proves the
runtime shape without making a generator part of this spike.

## Boundaries

The spike does not:

- replace Markout or move domain values into the registry;
- load inspected assemblies;
- add Roslyn or reflection-based plugins;
- force cheap, dependency-free sections through executable plans;
- migrate production command behavior.

The product path remains SRM-only, NativeAOT-friendly, and free of inspected
assembly loading.

## Conclusion

Proceed with a static-table production pilot:

1. Keep `SectionPipeline` as the selection/schema/render-filter authority and
   Markout as the renderer.
2. Generate one process-wide lambda table and precompiled plans for source/PDB
   and body-index-backed library sections.
3. Replace legacy `ScannerKey`, `SectionCapabilities`, and
   `ProbeEffectiveness` metadata for each migrated section.
4. A/B product output and actual work counts for every migrated section.
5. Measure fresh-process initialization and realistic reuse count, not only hot
   lookup.
6. Reject the migration if the CLI initializes the table for a single use and
   cannot recover the cold cost, or if execution loses the measured advantage.

The dynamic registry was the wrong final shape. The static lambda table
preserves its semantic gains, removes repeated construction, improves
steady-state planning and acquisition, and states the remaining cold cost
explicitly.
