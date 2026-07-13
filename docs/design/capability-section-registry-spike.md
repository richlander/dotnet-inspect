# Capability-driven section registry spike (issue #2605)

Status: **evaluation spike, staged-migration conclusion**. This document, plus the
runnable code under `tools/SectionRegistrySpike`, is the deliverable for
[issue #2605](https://github.com/richlander/dotnet-inspect/issues/2605). It is not
a production migration and does not change product behavior — see
[Boundaries](#boundaries-restated).

## Problem: the current selection-versus-execution split

`SectionPipeline<T>` ([`section-pipeline.md`](section-pipeline.md)) owns section
*selection*: registration order, verbosity, `-D`/`-S`, categories, cost
annotations, and `ProbeEffectiveness`. It does not own *execution* — the work
that actually populates a section's data. Execution is planned through several
separate mechanisms that all have to agree with each other and with the
descriptor metadata by hand:

1. **`ScannerKey` + `ScannerRegistry`.** Each descriptor declares a
   `ScannerKey` string (`src/dotnet-inspect/Sections/ISectionDescriptor.cs`).
   `ScannerRegistry.RunScanners` (`src/dotnet-inspect/Sections/ScannerRegistry.cs`)
   runs the scanner whose key matches, but scanner-to-scanner prerequisites are
   not modeled — they are implicit inside `ScannerContext`. For example,
   `ScannerContext.BodyIndex()` lazily builds a shared
   `Analysis.LibraryBodyIndex` the first time any of the unsafe-members,
   top-leverage, or optimization-opportunities scanners calls it
   (`src/dotnet-inspect/Sections/ScannerRegistry.cs:20-30`). That memoization
   works, but nothing in `ISectionDescriptor` says "this scanner needs
   `BodyIndex` first" — the dependency lives in scanner-delegate code, not in
   registered metadata.
2. **`SectionCapabilities` + `GetAuthorizedSections`.** Network work (PDB
   download, SourceLink HEAD audit, source-body fetch) is declared as a
   `[Flags] SectionCapabilities` enum
   (`src/dotnet-inspect/Sections/SectionCapabilities.cs`) on a descriptor, but
   the actual network calls are manual `bool` branches in
   `LibraryMetadataService.InspectAsync`
   (`src/dotnet-inspect/Inspectors/LibraryMetadataService.cs:192-235`):
   `pdbSections`/`runHeadAudit`/`runIntegrity` are computed from
   `pipeline.GetAuthorizedSections(...)`, then `AuditAsync`,
   `SourceAuditService.PopulateAsync`, and `SourceIntegrityService.PopulateAsync`
   are called in sequence with hand-written ordering and a hand-written
   `service.HasSourceLink && pdbContext.HasPdb` guard repeated at each call
   site. `FetchSource` depending on `AcquirePdb` is real, but it is expressed
   as "call these methods in this order," not as a registered dependency.
3. **`ProbeEffectiveness`.** A hand-set per-descriptor boolean
   (`static virtual bool ProbeEffectiveness => true`) tells effective discovery
   whether it is safe to render-probe a section during `-D`. It is a
   *section-level* judgment call, made by whoever wrote the descriptor. If a
   section's underlying scanner later starts doing something unsafe (e.g. a
   scanner that used to be pure IL metadata now opens a whole-assembly body
   index), nothing forces `ProbeEffectiveness` to be revisited — the flag and
   the actual work can drift apart silently.
4. **Direct section-name branches.** Some work remains selected outside both
   mechanisms. `LibraryMetadataService` directly checks
   `include?.Contains("Source Files")` before collecting source files
   (`src/dotnet-inspect/Inspectors/LibraryMetadataService.cs:244-251`), while
   `ApiOutputFormatter` separately converts requested section names into
   decompiler projections and whole-assembly-body requirements
   (`src/dotnet-inspect/Output/ApiOutputFormatter.cs:1171-1220`). These checks
   are valid today, but add more execution-planning sites that can drift from
   descriptor metadata.

None of this is a bug: it is deliberate incremental architecture (see
`section-pipeline.md`'s "Design Decisions" — manual registration, static
abstract interfaces, no reflection). But it does mean **metadata and execution
are two parallel, hand-synchronized systems**, and the drift risk grows with
every new capability-bearing section.

### Concrete drift points

| Mechanism | Where selection metadata lives | Where execution actually happens | Drift risk |
| --- | --- | --- | --- |
| Shared body index | *(nowhere — not modeled)* | `ScannerContext.BodyIndex()` lazy field | A new body-index scanner can forget to reuse it, or an existing one can stop needing it and nobody notices the cost is still paid |
| PDB/source network work | `SectionCapabilities` flags on descriptor | Manual `bool` branches + ordered calls in `LibraryMetadataService.InspectAsync` | Adding a new network-capable section means editing both the descriptor **and** the manual branch, with no compiler check that they agree |
| Effective-discovery safety | `ProbeEffectiveness` flag (author-set) | Whatever the scanner behind `ScannerKey` actually does | The flag can go stale relative to the scanner's real cost/safety with no structural check |
| Direct projection/source work | Section name and descriptor metadata | `IncludeSections.Contains(...)` branches in services/formatters | Renames or new prerequisites require coordinated edits across otherwise separate code paths |

The spike directly targets the first three drift points: it makes the body-index
prerequisite a registered dependency, makes network work a registered
capability with an explicit dependency edge (`FetchSource` depends on
`AcquirePdb`), and derives probe-safety from the full capability closure
instead of a single hand-set flag. The staged plan starts with those
`LibrarySections` paths; it does not claim to solve every command/formatter
branch in this contained evaluation. It also does not model the distinct
`MayAuditSources`/HEAD-audit path; that path needs its own typed capability and
A/B before migration.

## Prior art

Two in-house designs were combined for this spike, matching the same pattern
already validated for the Research/Finding registry
([#2585 registry spike](https://github.com/richlander/dotnet-inspect/issues/2585#issuecomment-4928996081)):

- **smooth-markdown-table `prototype-registry.cs` V3.** Generic
  `StreamingDescriptor<T>`/`BufferedDescriptor<T>` types implement a
  non-generic runtime `IRendererDescriptor`, retain static `T.Name`/`T.Description`,
  and store explicit factories so work is created only when a consumer-selected
  strategy needs it. The spike's capability registry follows the same shape —
  explicit `Func<ICapabilityWork<TContext>>` factories, no
  `typeof(T).IsAssignableTo` capability probing, no reflection scanning.
- **Markout `MarkoutTypeInfo<T>`/`IMarkoutTypeInfo`.** A non-generic runtime
  interface bridges to a generic, source-generator-friendly base type; the
  generated `MarkoutSerializerContext` explicitly enumerates every
  `IMarkoutTypeInfo`. The spike's `ICapabilityWork<TContext>` /
  `ICapability<TContext>` split is the same bridge, for a mechanical reason
  particular to C# 11+: **an interface with `static abstract` members cannot be
  used as a type argument** (`Dictionary<CapabilityKey, ICapability<TContext>>`
  fails to compile with `CS8920`, "does not have a most specific
  implementation"). The spike therefore keeps the static declarative metadata
  (`Name`, `SafeToProbe`, `DependsOn`) on `ICapability<TContext>`, used only as
  a generic constraint at `Register<TCapability>()`, and puts the single
  instance method (`ExecuteAsync`) on a separate non-generic-shaped
  `ICapabilityWork<TContext>`, which is what gets stored in the registry's
  dictionaries and returned by `CapabilitySession<TContext>`. A future
  source-generated registry would emit explicit `Register<TCapability>()` and
  `Add<TDescriptor>()` calls (or the runtime entries they produce) the same way
  Markout's generator emits `MarkoutSerializerContext` entries — enumeration
  stays explicit, never reflection-based.

The CLI section registry and the Research/Finding registry do not share an
implementation (per the issue), but they now share the same proven
construction pattern: static generic metadata, a non-generic runtime bridge,
explicit capability opt-in, and lazy consumer-selected execution.

## Prototype shape

```text
requested sections
    -> ICapabilitySectionDescriptor<TModel>.RequiredCapabilities (direct, per section)
    -> CapabilityRegistry<TContext>.ResolvePlan(...)   (transitive closure, deduped, topological order)
    -> CapabilitySession<TContext>.ExecutePlanAsync    (memoized create+execute, ordered trace)
    -> populated model
    -> existing SectionPipeline<TModel> render-filter + Markout rendering (unchanged)
```

Key types, all under `tools/SectionRegistrySpike` (namespaces
`SectionRegistrySpike.Capabilities` / `SectionRegistrySpike.Sections`):

- **`CapabilityKey`** — `readonly record struct CapabilityKey(Type Type)`,
  created via `CapabilityKey.Of<TCapability>()` (`typeof(TCapability)`). No
  reflection scanning: the type token for any capability that is actually
  registered is already retained by the compiler for that generic
  instantiation.
- **`ICapabilityWork<TContext>`** — non-generic-shaped runtime interface,
  `ValueTask ExecuteAsync(TContext, CapabilitySession<TContext>)`. Stored in
  the registry and session dictionaries.
- **`ICapability<TContext> : ICapabilityWork<TContext>`** — adds
  `static abstract string Name`, `static abstract bool SafeToProbe`,
  `static abstract CapabilityKey[] DependsOn`. Used only as the generic
  constraint on `Register<TCapability>() where TCapability : ICapability<TContext>, new()`.
- **`CapabilityRegistry<TContext>`** — manual, ordered `Register<TCapability>()`
  calls; stores a `Func<ICapabilityWork<TContext>>` factory per capability.
  `ResolvePlan(requested)` does a deterministic post-order DFS: dependencies
  before dependents, deduplicated, with `CapabilityNotRegisteredException` for
  an unregistered dependency and `CapabilityCycleException` (with the detected
  cycle path) for a cycle. `IsClosureSafeToProbe(plan)` checks every node in a
  resolved plan, not just the requested capability's own flag.
- **`CapabilitySession<TContext>`** — one per run. `ExecutePlanAsync` creates
  and executes each not-yet-seen capability in plan order, recording
  `CreatedCount`, `ExecutedCount`, and an ordered `"create X"`/`"execute X"`
  trace. `GetExecuted<TCapability>()` lets a dependent read a dependency's
  already-executed instance (e.g. `Calls`/`Facts` reading `BodyIndex`'s
  computed value).
- **`ICapabilitySectionDescriptor<TModel> : ISectionDescriptor<TModel>`** —
  adds `static abstract CapabilityKey[] RequiredCapabilities`. Still never
  instantiated, same as the base interface.
- **`CapabilitySectionRegistry<TModel, TContext>`** — bridges the two:
  `Add<TDescriptor>(isApplicable)` calls the **real**
  `SectionPipeline<TModel>.Add<TDescriptor>()` (so registration order,
  categories, verbosity, `-D`/`-S`, cost annotations, and
  `ProbeEffectiveness` are exactly today's pipeline behavior) and records
  `TDescriptor.RequiredCapabilities` for planning. `AddCategory` delegates
  straight through. `PlanFor(sectionNames)` resolves the combined,
  deduplicated, ordered capability plan for a set of selected sections.
  Production `-S` values and categories are still canonicalized first by the
  existing `SelectResolver`; the planner consumes those concrete names and
  reorders them by pipeline registration order rather than trusting
  `HashSet` enumeration order.

## Representative descriptors and capabilities

Five representative sections/capabilities cover the pressure points named in
the issue — explicitly **illustrative stand-ins, not production section
descriptors**:

| Section | Capability (direct) | Transitive plan | Represents |
| --- | --- | --- | --- |
| Metadata | `Metadata` (SafeToProbe) | `[Metadata]` | Cheap, safe-to-probe metadata section |
| Decompiled Source | `Decompile` | `[Decompile]` | Local heavy work (expensive, not network) |
| Original Source | `FetchSource` (depends on `AcquirePdb`) | `[AcquirePdb, FetchSource]` | Network PDB acquisition + source fetch |
| Calls | `Calls` (depends on `BodyIndex`) | `[BodyIndex, Calls]` | Projection over a shared prerequisite |
| Facts | `Facts` (depends on `BodyIndex`) | `[BodyIndex, Facts]` | Second projection over the *same* shared prerequisite |

`FetchSourceCapability.ExecuteAsync` uses `await Task.Yield()` before setting
its result, proving the plan executes real asynchronous work, not just
synchronous stand-ins dressed up as `ValueTask`.

## Honest A/B: no over-collection claim

**The spike does not claim `LibrarySections` over-collects `Calls`/`Facts`
today.** `ScannerContext.BodyIndex()` already memoizes one shared index across
separate scanner keys (`src/dotnet-inspect/Sections/ScannerRegistry.cs:20-30`).
The spike's "current" baseline models that accurately:

- `CurrentBaseline.CurrentScannerContext.BodyIndex()` is a lazy, memoized
  method — a direct analog of `ScannerContext.BodyIndex()` — built at most once
  per run regardless of how many registered scanners call it.
- `CurrentBaseline.CurrentScannerRegistry` is a direct analog of
  `ScannerRegistry`: a `Dictionary<string, Action<Context>>`, run only for
  required keys.
- The `Calls`-only and `Calls`+`Facts` selections below show **equal** work
  counts for current and typed: one `BodyIndex` build either way, plus one
  scan per requested projection. The spike does not manufacture a duplicate-
  build scenario to inflate the case for migrating.

The real, demonstrated drift is **execution intent**, not correctness:
scanner dependencies live implicitly inside `ScannerContext`/scanner delegate
closures, and network dependencies live in manual `GetAuthorizedSections` +
`bool`-branch code in `LibraryMetadataService`, separate from descriptor
metadata. The typed plan performs the *same* work and produces the *same*
output, but derives its dependency order from registered capability metadata
instead of hand-coordinated context/branch code.

The current-baseline pipeline in the spike is the **real**
`SectionPipeline<SpikeModel>` (`DotnetInspector.Sections.SectionPipeline<T>`,
unmodified), used with the real `GetRequiredScanners` and
`GetAuthorizedSections(SectionCapabilities, Verbosity, HashSet<string>?)` for
selection and network authorization, mirroring
`LibraryMetadataService.InspectAsync`'s `pdbSections`/`runHeadAudit`/`runIntegrity`
pattern. Only the *scanner registry* and *network-work method* are
representative small analogs (`CurrentBaseline.CurrentScannerRegistry`,
`CurrentBaseline.RunNetworkWorkAsync`), because the real `ScannerRegistry`,
`ScannerContext`, and `LibraryMetadataService` are hard-wired to
`LibraryInspection`, not a synthetic spike model.

## Representative CLI A/B (from `dotnet run --project tools/SectionRegistrySpike -c Release -- --verify`)

No dotnet-inspect CLI invocation was run for this evidence — the issue asks
for a representative A/B, and the following is generated entirely from the
representative model above. Product output and behavior are unchanged.

```text
### Metadata
Current trace: create Metadata, execute Metadata
Typed trace:   create Metadata, execute Metadata
Current/typed work: 1 created, 1 executed
Current/typed output: Metadata: loaded

### Decompiled Source
Current trace: create Decompile, execute Decompile
Typed trace:   create Decompile, execute Decompile
Current/typed work: 1 created, 1 executed
Current/typed output: Decompiled Source: // decompiled source (representative)

### Original Source
Current trace: create AcquirePdb, execute AcquirePdb, create FetchSource, execute FetchSource
Typed trace:   create AcquirePdb, execute AcquirePdb, create FetchSource, execute FetchSource
Current/typed work: 2 created, 2 executed
Current/typed output: Original Source: // original source text (representative)

### Calls only
Current trace: create BodyIndex, execute BodyIndex, create Calls, execute Calls
Typed trace:   create BodyIndex, execute BodyIndex, create Calls, execute Calls
Current/typed work: 2 created, 2 executed
Current/typed output: Calls: 42

### Calls + Facts
Current trace: create BodyIndex, execute BodyIndex, create Calls, execute Calls, create Facts, execute Facts
Typed trace:   create BodyIndex, execute BodyIndex, create Calls, execute Calls, create Facts, execute Facts
Current/typed work: 3 created, 3 executed
Current/typed output:
  Calls: 42
  Facts: 42

### Empty selection
Current trace: (none)
Typed trace:   (none)
Current/typed work: 0 created, 0 executed
Current/typed output: (none)
```

For every row, the real pipeline's post-execution render-filter section list
and the representative rendered text are equal, and current and typed traces
are identical character-for-character. Unknown section names are a separate
negative case: typed planning rejects one with a clear diagnostic instead of
silently treating it as an empty selection.

### Strategy 1–3 evidence (zero-work invariants)

```text
Strategy 1 — Describe/schema: 5 sections registered, in the same order and with the
             same cost annotations/categories/required verbosity on both pipelines.
             Existing SelectResolver expands @Source to its two concrete members;
             typed planning preserves pipeline registration order.
             No CapabilitySession is constructed — zero work by construction.

Strategy 2 — Discover: all 5 sections structurally discoverable on an unexecuted
             model (applicability predicates read structural SpikeModel flags, not
             capability-populated fields) — zero work.

Strategy 3 — Effective discovery: only "Metadata" probed (created: 1, executed: 1).
             "Decompiled Source", "Original Source", "Calls", "Facts" remain
             structurally discoverable but unprobed — heavy/decompiler/network/body
             work stayed at zero.
```

### Negative self-verification

```text
Missing dependency  -> CapabilityNotRegisteredException:
  "Capability 'NotRegisteredCapability' is not registered. Register it before it
   can appear as a dependency or plan target."

Dependency cycle    -> CapabilityCycleException:
  "Capability dependency cycle detected: CycleACapability -> CycleBCapability ->
   CycleACapability."

Unknown section     -> KeyNotFoundException:
  "Section 'No Such Section' is not registered."

Probe-safety (separate registry, not part of the 5-section set above):
  "Misleading Probe" declares ProbeEffectiveness=true (the section-level flag
  alone would allow probing), but its capability closure contains DeepScan
  (SafeToProbe=false). The closure-derived check defers it: created=0, DeepScan
  never ran — a case the hand-set boolean alone could have missed.

Unauthorized network -> InvalidOperationException:
  "AcquirePdb requires network authorization; the section was not explicitly
   selected."
  The failed capability is not recorded as executed.
```

Run the spike yourself for the full, current output:

```bash
dotnet run --project tools/SectionRegistrySpike -c Release -- --verify
```

The tool exits non-zero if any invariant above fails.

## Complexity and tradeoffs (stated honestly)

**Cost of the typed layer:**

- Two more interfaces to learn (`ICapability<TContext>`/`ICapabilityWork<TContext>`)
  beyond `ISectionDescriptor<TModel>`, plus the `CS8920` split, which is a
  genuinely awkward C# corner (static-abstract interfaces cannot be stored as
  values) that every future capability author needs to understand.
- A capability registry and session per model/context pair
  (`CapabilityRegistry<TContext>`, `CapabilitySession<TContext>`) — more moving
  parts than a single `Dictionary<string, Action<ScannerContext>>`.
- Explicit `DependsOn` arrays must be kept correct by hand, same as
  `ScannerKey` today — the win is that a **missing or cyclic** dependency now
  fails loudly (`CapabilityNotRegisteredException`/`CapabilityCycleException`)
  instead of silently producing wrong or duplicated work.

**No claimed runtime win.** Every A/B pair above shows *equal* current vs.
typed work — same trace, same counts, same output. The spike deliberately does
not manufacture a scenario where the current system does more work than the
typed one, because `ScannerContext.BodyIndex()` already prevents that for the
one case (shared body index) where it could happen today.

**Potential migration benefit:** reduced metadata/execution *drift*, not
reduced execution *cost*. In the representative model:

- A capability's dependency is a compile-time-checked, plan-resolved fact
  (`DependsOn`), not an implicit call inside a context helper or a
  hand-ordered sequence of method calls.
- Effective-discovery safety is *derived* from the full capability closure
  (`IsClosureSafeToProbe`), catching cases where a section-level
  `ProbeEffectiveness`/cost flag has drifted from what the section's
  capability actually does (the "Misleading Probe" negative check).
- Network authorization is still owned by `SectionPipeline.GetAuthorizedSections`
  (unchanged), but the capabilities that *use* that authorization now reject
  execution explicitly (`AcquirePdbCapability`/`FetchSourceCapability` throw
  when `NetworkAuthorized` is false) instead of relying on the caller to gate
  correctly at every call site.

## NativeAOT / source-generation story

- No reflection scanning, `Activator`, dynamic code, or expression
  compilation anywhere in the spike. `CapabilityKey` is built from
  `typeof(TCapability)` at a call site where `TCapability` is a concrete,
  statically known type — the same generic-instantiation pattern
  `SectionPipeline<TModel>.Add<TDescriptor>()` already uses today.
- `Register<TCapability>()`/`Add<TDescriptor>()` are the manual, ordered
  registration calls — no assembly scanning, exactly like
  `SectionPipeline<T>.Add<TDescriptor>()` today.
- A later source generator could emit these `Register`/`Add` calls (or the
  non-generic runtime entries — `Registration`/`SectionEntry` — they produce)
  once the shape stabilizes, the same relationship Markout's generated
  `MarkoutSerializerContext` has to hand-written `MarkoutTypeInfo<T>`
  subclasses. This spike does not build that generator; it only confirms the
  manual shape is generator-friendly (no reflection to replace, no dynamic
  registration order).

## Boundaries restated

Per the issue, this spike explicitly does **not**:

- Replace Markout rendering or move domain computation into renderers —
  `SectionPipeline<TModel>` is reused verbatim for selection/schema/render-filter,
  and nothing here touches Markout.
- Make section descriptors own domain values — `SpikeModel` still owns data,
  the same way `LibraryInspection` does today.
- Add a reflection/plugin system.
- Force every section through one uniform executable interface — capabilities
  remain opt-in per descriptor via `RequiredCapabilities`; a section with no
  capabilities is simply never given any.
- Migrate the full CLI. Only the spike's five representative sections exist;
  no production descriptor was changed.

## Conclusion: staged migration

This spike concludes with a **staged migration plan**, not a rejection. The
capability-driven pattern demonstrates a path to address the three
execution-planning drift points exercised by the representative model without
changing behavior or adding runtime cost, and it reuses `SectionPipeline<T>`
rather than replacing it.

1. **`SectionPipeline<T>` keeps owning selection/schema/render-filtering, and
   Markout keeps owning rendering.** Neither changes. The capability layer is
   purely an execution-planning addition behind the pipeline, mirroring how
   this spike's `CapabilitySectionRegistry` wraps rather than replaces
   `SectionPipeline<T>`.
2. **Stage 1 — introduce typed execution planning only behind expensive,
   multi-prerequisite `LibrarySections`.** Start with source/PDB work
   whose execution matches the exercised `AcquirePdb` → `FetchSource` shape
   (`Source Files`/`SourceLink Integrity`) and body-index-backed scanners
   (`Unsafe Members`, `Top Leverage`, `Optimization Opportunities`). These are
   the sections whose execution today spans `ScannerContext.BodyIndex()` and
   the manual `GetAuthorizedSections` network branches exercised by the spike.
   Preserve existing outputs and command behavior throughout; each converted
   section's output must A/B-match its current behavior before it ships, the
   same way this spike's render strategy asserts current-vs-typed equality per
   selection. Add `SourceLink Availability`/`Missing Files` only after a
   separate `AuditSources` capability A/B covers their HEAD-audit path; this
   spike does not provide that evidence.
3. **Replace string `ScannerKey`/`SectionCapabilities` branches incrementally,
   only after an A/B shows equal work traces and output** for the section
   being converted. No section moves to the typed path without that evidence.
4. **Do not migrate cheap sections or all commands merely for uniformity.**
   Sections like `Library Info`, `References`, `Resources`, and most package
   sections have no shared prerequisite or network capability to coordinate —
   the spike demonstrates no meaningful drift reduction from moving them.
5. **Source generation is later.** Once Stage 1's shape has stabilized across
   a handful of real sections, a generator can emit the `Register`/`Add` calls
   (or their runtime entries) the way Markout's generator emits
   `MarkoutSerializerContext` — not before, and not as a prerequisite for
   Stage 1.

The benefit staged migration buys is **reduced metadata/execution drift and
explicit, checked dependencies** for the sections that actually have
non-trivial execution prerequisites — not a performance win, and not a reason
to touch the majority of sections that already work fine with a `ScannerKey`
string and no shared state.
