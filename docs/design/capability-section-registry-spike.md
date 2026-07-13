# Capability-driven section registry spike

Status: **evaluation spike; staged-migration recommendation**.

This is the runnable evaluation for
[issue #2605](https://github.com/richlander/dotnet-inspect/issues/2605).
It does not migrate a production command. It compares the current planning
shape with a typed registry, measures planner overhead and allocations, and
records the code-quality tradeoff.

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
   `GetAuthorizedSections` about PDB, HEAD-audit, and source-body permissions,
   then orders those calls with Boolean branches.
4. Other work, such as source collection and API projections, still uses
   direct section-name branches.

The current behavior is efficient: the body index is already built once.
The problem is not duplicate execution. It is that selection metadata,
dependencies, authorization, and execution live in separate places and must
remain synchronized by convention.

## Design

The revised spike uses static capability executors compiled into immutable
runtime plans:

```text
requested sections
    -> typed RequiredCapabilities
    -> dependency closure and topological order
    -> authorization preflight
    -> static executors over caller-owned context
    -> existing SectionPipeline render filtering
    -> existing Markout rendering
```

### Capability declaration

```csharp
public interface ICapability<TContext>
{
    static abstract string Name { get; }
    static abstract CapabilityExecutionModes AllowedModes { get; }
    static abstract CapabilityKey[] DependsOn { get; }
    static abstract ValueTask ExecuteAsync(TContext context);
}
```

`Register<TCapability>()` captures `TCapability.ExecuteAsync` in a runtime
entry. There is no capability object, factory, `Activator`, reflection scan, or
dynamic code. Per-run values remain on the caller-owned context, matching
today's `ScannerContext` model.

`CapabilityExecutionModes` distinguishes:

- `Probe`: cheap work safe during effective discovery;
- `Detailed`: work authorized by detailed verbosity, such as PDB acquisition;
- `Explicit`: work requiring direct section selection, such as source-body
  fetch.

Probe safety and network authorization are therefore properties of the full
compiled plan, not separate section flags.

### Section declaration

```csharp
public interface ICapabilitySectionDescriptor<TModel, TContext>
{
    static abstract string Name { get; }
    static abstract bool IsExpensive { get; }
    static virtual bool ExplicitOnly => false;
    static virtual bool Info => false;
    static abstract bool CanRender(TModel model);
    static abstract CapabilityKey[] RequiredCapabilities { get; }
}
```

The typed descriptor deliberately has no `ScannerKey`,
`SectionCapabilities`, or `ProbeEffectiveness`. Its capability requirements are
the single execution declaration.

`CapabilitySectionRegistry` resolves the plan during registration, derives
probe safety from `plan.CanExecute(Probe)`, and materializes a normal
`SectionEntry<TModel>`. `SectionPipeline` gained an overload for that runtime
entry; its existing generic `Add<TDescriptor>()` delegates to the same path.
Selection behavior remains in the real pipeline rather than a spike copy.

### Plan behavior

`CapabilityRegistry.ResolvePlan` performs a deterministic post-order traversal:

- dependencies precede dependents;
- shared prerequisites are deduplicated;
- missing registrations fail;
- dependency cycles fail with the cycle path.

`CapabilityPlan.ExecuteAsync` preflights every entry's execution mode before
running any work. A source-body plan rejected at detailed verbosity therefore
does not acquire a PDB first and fail later.

Plans contain static delegates and no mutable execution state. A failed run
cannot leave a partially initialized capability object cached for a later run.

## Representative set

| Section | Direct capability | Compiled plan | Policy |
| --- | --- | --- | --- |
| Metadata | `Metadata` | `Metadata` | Probe, detailed, explicit |
| Decompiled Source | `Decompile` | `Decompile` | Explicit |
| Original Source | `FetchSource` | `AcquirePdb`, `FetchSource` | Explicit |
| Calls | `Calls` | `BodyIndex`, `Calls` | Explicit |
| Facts | `Facts` | `BodyIndex`, `Facts` | Explicit |

`Calls + Facts` compiles to `BodyIndex, Calls, Facts`; the shared index executes
once. `Original Source` models ordered asynchronous work:
`FetchSource` depends on `AcquirePdb`.

The current baseline uses the real `SectionPipeline`, a string-keyed scanner
registry, context-owned body-index memoization, and a separate network branch.
It does not fabricate object creation events. Both paths count actual executed
work and compare representative output.

## Strategy and correctness evidence

Run:

```bash
dotnet run --project tools/SectionRegistrySpike -c Release -- --verify
```

The verifier covers all four issue strategies:

| Strategy | Result |
| --- | --- |
| Describe/schema | Same names, order, categories, cost annotations, and category resolution; no work |
| Structural discovery | Same discoverable sections on an unexecuted model; no work |
| Effective discovery | Only `Metadata` executes; decompiler, network, and body-index plans remain deferred |
| Render | Same trace, work count, render-filter sections, and output for every representative selection |

Measured work:

| Selection | Current | Typed |
| --- | ---: | ---: |
| Empty | 0 | 0 |
| Metadata | 1 | 1 |
| Decompiled Source | 1 | 1 |
| Original Source | 2 | 2 |
| Calls | 2 | 2 |
| Calls + Facts | 3 | 3 |

Negative checks cover missing dependencies, cycles, derived probe safety,
operation-specific authorization, preflight-before-mutation, and retry after a
failed static executor.

## Performance evaluation

Run:

```bash
dotnet run --project tools/SectionRegistrySpike -c Release -- --benchmark
```

Representative result on .NET 11 preview 5, macOS arm64:

| Scenario | Current ns/op | Typed ns/op | Time delta | Current B/op | Typed B/op | Allocation delta |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Registry construction | 844.7 | 5031.8 | +495.7% | 1416 | 6784 | +379.1% |
| Plan one section | 130.7 | 6.9 | -94.7% | 176 | 0 | -100.0% |
| Plan shared sections | 38.6 | 302.0 | +683.1% | 176 | 1040 | +490.9% |
| Execute one section | 69.7 | 55.6 | -20.2% | 96 | 80 | -16.7% |
| Execute shared sections | 109.5 | 24.4 | -77.7% | 96 | 80 | -16.7% |

These are microbenchmarks of registry mechanics, not product throughput. They
use nine post-warmup samples and report the median. Execution rows use
precompiled plans and include each design's model/context allocation.

The same executable published with NativeAOT preserved the direction:

| Scenario | Current ns/op | Typed ns/op | Time delta | Allocation delta |
| --- | ---: | ---: | ---: | ---: |
| Registry construction | 326.2 | 1566.5 | +380.3% | +390.6% |
| Plan one section | 45.5 | 15.0 | -67.0% | -100.0% |
| Plan shared sections | 51.8 | 385.7 | +645.2% | +513.6% |
| Execute one section | 36.5 | 22.2 | -39.3% | -16.7% |
| Execute shared sections | 42.8 | 31.1 | -27.3% | -16.7% |

The result is mixed and useful:

- A precompiled plan dispatches direct static delegates instead of scanning a
  string registry and probing a `HashSet`; representative execution is faster
  and allocates 16 bytes less per run.
- A single section returns its precompiled plan with no allocation.
- Combining multiple arbitrary sections still pays a cold graph-merge cost:
  about 0.28 microseconds and 1 KB in this five-section spike.
- Registry construction is about 4.7 microseconds and 6.8 KB because it
  validates and compiles every section closure. This is setup cost, but it
  argues for one generated/reused registry rather than rebuilding it per
  inspected assembly.

The registry should not migrate unless production integration preserves the
execution gain and removes repeated construction. A source-generated singleton
or command-level cached registry is the expected production shape.

## Code-quality evaluation

The representative current baseline and typed core were counted with blank and
comment-only lines excluded:

| Measure | Current shape | Typed shape | Delta |
| --- | ---: | ---: | ---: |
| Core non-comment lines | 148 | 234 | +86 |
| Per-section execution declarations across five descriptors | 10 | 5 | -50% |
| Execution dispatch paths | Scanner registry + network branch | One compiled plan | Consolidated |
| Probe decision source | Section Boolean | Full capability closure | Derived |
| Network authorization | Capability flags + caller branches | Plan entry modes + one preflight | Centralized |
| Per-capability runtime objects | Not applicable | 0 | No new objects |
| Reusable failed execution state | Not applicable | 0 | No session cache |

The typed core is 86 lines larger in the small example. The design is not a
line-count win by itself. Its code-quality improvement is narrower and
structural:

- half as many per-section execution declarations;
- dependencies, policy, and execution are registered together;
- invalid graphs and unauthorized plans fail before domain mutation;
- one execution path replaces scanner and network dispatch paths;
- static executors eliminate the original spike's factory/object/session
  lifecycle and its failed-instance ambiguity.

This is enough to justify a contained production pilot, not a broad rewrite.

## NativeAOT and source generation

The design uses concrete generic registrations, `typeof(TCapability)` identity,
static delegates, arrays, and dictionaries. It does not use reflection
enumeration, `Activator`, expression compilation, or runtime code generation.
The spike publishes and runs successfully as an `osx-arm64` NativeAOT binary.

A source generator can emit:

- capability runtime entries;
- section runtime entries;
- precompiled single-section plans;
- the ordered registry singleton.

That generated shape would remove repeated construction and retain the measured
zero-allocation single-section planning path. Source generation is not required
to prove the runtime design and should follow a successful production pilot.

## Boundaries

The spike does not:

- replace Markout or move domain values into descriptors;
- load inspected assemblies;
- add Roslyn or reflection-based plugins;
- force cheap sections through executable capabilities;
- migrate production command behavior.

The product path remains SRM-only, NativeAOT-friendly, and free of inspected
assembly loading.

## Conclusion

Proceed with a staged pilot, with stricter gates than the original spike:

1. Keep `SectionPipeline` as the selection/schema/render-filter authority and
   Markout as the renderer.
2. Pilot a reusable typed registry only for source/PDB work and body-index-backed
   library sections.
3. Replace legacy `ScannerKey`, `SectionCapabilities`, and
   `ProbeEffectiveness` metadata for each migrated section; do not keep two
   authorities.
4. A/B product output and actual work counts for every migrated section.
5. Re-run the benchmark on the production registry. Reject the migration if
   repeated construction remains on the per-assembly path or execution loses
   the measured advantage.
6. Do not migrate cheap, dependency-free sections merely for uniformity.

The design now demonstrates both a runtime improvement in the hot execution
path and concrete code-quality invariants, while exposing rather than hiding
its setup and cold multi-plan costs.
