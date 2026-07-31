# Section Pipeline

The section pipeline is the runtime implementation of the [rendering model](rendering-model.md). It makes section visibility, data collection, and verbosity mapping declarative and data-driven rather than scattered across command handlers.

## Problem

Before the pipeline, each command managed sections imperatively:

- Verbosity gating was inline (`if (verbosity >= Detailed) { ... }`)
- Section filtering (`-S`) required each command to parse and apply filters
- Data collection always ran at the broadest scope, even when only one section was requested
- Adding a section meant editing multiple code paths

## Architecture

The pipeline has three layers, each with a single responsibility.

### Layer 1: Section Descriptors

`ISectionDescriptor<T>` declares metadata about a section using C# static abstract interface members:

```csharp
public interface ISectionDescriptor<T>
{
    static abstract string Name { get; }
    static abstract Verbosity MinVerbosity { get; }
    static abstract string? ScannerKey { get; }
    static abstract bool CanRender(T model);
}
```

Each section is a small struct implementing this interface. The struct is never instantiated — only its static members are read during registration. This is zero-allocation and NativeAOT-compatible (no reflection).

| Member | Purpose |
| ------ | ------- |
| `Name` | Section key used in `-S` filtering and Markout `IncludeSections` |
| `MinVerbosity` | Lowest verbosity level at which this section appears |
| `ScannerKey` | Scanner that collects this section's data (`null` = always collected) |
| `CanRender` | Whether the section has data worth rendering |

### Layer 2: Section Pipeline

`SectionPipeline<T>` is the decision engine. It stores section entries extracted from descriptors and answers four questions:

| Method | Question |
| ------ | -------- |
| `ComputeIncludeSections(verbosity, userSections)` | Which sections should the Markout serializer render? |
| `GetEffectiveSections(model, verbosity, userSections)` | Which sections actually have data? (for `-S` discovery) |
| `GetRequiredVerbosity(userSections)` | What's the minimum verbosity needed for the requested sections? |
| `GetRequiredScanners(includedSections)` | Which scanner keys are needed? |

The pipeline does not render anything. It computes a `HashSet<string>` of section names that is passed to the Markout serializer as `IncludeSections`. The serializer handles all rendering declaratively. Section visibility is always additive: the pipeline computes which sections to include based on verbosity and explicit `-S` selection. There is no exclude mechanism.

### Layer 3: Scanner Registry

`ScannerRegistry` maps scanner keys to a declared cost and a scan function:

```csharp
registry.Add("ExtensionMethods", SectionCost.NetworkFree, ctx =>
    LibraryMetadataService.ScanExtensionMethods(ctx.AssemblyPath, ctx.Model, ctx.Logger));
```

`RunScanners(requiredKeys, context)` executes only the scanners needed for the current request. When a user runs `dotnet-inspect library Foo.dll -S "Extension Methods"`, only the `ExtensionMethods` scanner runs — not the full set of Detailed-level scans.

## Scanner Cost

Cost is declared by the **scanner**, not by the section, and the pipeline raises each section to the cost of the scanner behind it (`UseScannerCosts`). A descriptor may declare a higher cost than its scanner — a cheap scan feeding an enormous rendering is real — but it can no longer declare a lower one.

This inverts the previous arrangement, in which each section restated the cost of work it did not own. Eleven sections were backed by the four scanners that build the whole-assembly IL body index, and only two of them said so; the other nine declared `NetworkFree` while costing seconds. A section is one of many views onto a scan, so the scan is the only place the cost is known once.

`CostOf(key)` is the maximum over the transitive prerequisite closure, so a cheap scanner that requires an expensive one costs what the run will actually do. `AddBundle` takes no cost for the same reason: a bundle does no work of its own, and letting it declare one would let it under-state what it pulls in.

`CostOf` throws rather than guessing in the two cases where a guess would be silently wrong: an unregistered key (a stale or misspelled `ScannerKey` would otherwise resolve to the cheapest tier and keep an expensive section on the ladder), and a registered non-bundle scanner with no declared cost. The second throw is what makes `SectionPipelineTests.LibraryScannerCosts_AreDeclaredForEveryRegisteredScanner` bite: without it, registering a scanner through a cost-less path leaves that gate green.

### The declaration is enforced where the cost is incurred

The registry cannot see that a scanner touches the body index. `ctx.BodyIndex` is handed to scan methods as a lazily-invoked method group bound to `Func<LibraryBodyIndex>`, which is exactly how those nine sections drifted. A declared-cost enum alone would let the next one drift the same way.

So one declaration does both jobs: **only a scanner registered as `Unbounded` may call `ctx.BodyIndex()` or `ctx.DrillMap()`**. Adding a body-index call to a scanner that still claims to be cheap throws instead of quietly restoring the defect. The check is scoped to scanner execution and cleared when the run ends, because the `Func` can outlive the scanner that supplied it and be invoked while rendering.

### What `Unbounded` means for selection

`Unbounded` sections leave the `-v:n` and `-v:d` render ladders and the `@All` pole entirely — the tier means "never auto-run by any verbosity". They stay reachable by exact `-S` name and through any category door that lists them.

Only the four scanners that build the whole-assembly IL body index are `Unbounded`: `UnsafeMembers`, `TopLeverage`, `OptimizationOpportunities`, and `ResourceTriage`. Every other library scanner is `NetworkFree`, including `Switches`, which is offline and never opens the body index (128.7 ms on `ILInspector.Decompiler.dll`). There is deliberately no tier meaning "locally expensive but offline"; adding one is future work, not a gap this change papers over.

Measured on `library <assembly>`, NativeAOT publish, min of 3 warm runs, wall clock:

| Assembly | `-v:n` before | `-v:n` after | `-v:d` before | `-v:d` after |
| --- | --- | --- | --- | --- |
| `ILInspector.Decompiler.dll` (1.7 MB) | 2,928.3 ms | 355.3 ms | 2,856.2 ms | 368.4 ms |
| `System.Private.CoreLib.dll` (12.4 MB) | 7,146.7 ms | 730.9 ms | 7,374.6 ms | 797.3 ms |

Roughly 300 ms of each figure is the NativeAOT process floor, so the scan work itself falls by more than the wall-clock ratio suggests. No section is *added* at any verbosity by this change.

Note that `@Performance` is **not** a generic door. The tabular and JSONL group paths flatten it into a single kind-labeled table over exactly `PerformanceKinds.Sections`, so adding a differently-shaped section to it stops the group rendering as one table at all.

### Two cost axes, and which one the ladder reads

A section's effective cost has two inputs: the cost of its scanner, and any cost the descriptor declares itself. The raise is one-way, so **the descriptor axis can move a section off the ladder without the scanner axis changing at all**. A gate that reads `registry.CostOf(section.ScannerKey)` therefore checks only half the mechanism.

`SectionPipeline.SectionCosts` exposes the effective per-entry cost — the value `IsCuratedAutoRendered` consults — so `LibrarySections_AboveNetworkFree_AreExactlyTheBodyIndexFamily` can pin the decision input rather than one of its sources. It asserts the effective axis and the scanner axis separately, so a failure names which declaration moved.

The effective axis subsumes the scanner axis because a scanner raise always raises the entry — but that holds only because a scanner key's cost is **immutable once declared**. `SectionPipeline.Add` snapshots the cost when the section is registered, so a registry that allowed re-registration could raise a key's cost after entries were already bound to it, leaving the pipeline auto-rendering at a stale cheap cost while `CostOf` reported the truth. `ScannerRegistry.Add` and `AddBundle` therefore reject a key that is already registered, which is what makes the subsumption unconditional rather than an accident of the order `LibrarySections` happens to build in.

Pinning the effective axis is what makes the full non-cheap set visible: the generated `Metadata: <Table>` sections and the `SourceLink: *` family are `Unbounded` by their own descriptors, independently of any scanner.

### Five routes into the same defect

The defect this mechanism exists to prevent has one shape — *a section declares itself cheap while the work behind it is expensive* — and five different ways in. Adversarial review of #3626 surfaced them one at a time, each only after the previous was closed, so the list is recorded here rather than left to be re-derived. Every row has a gate in `SectionPipelineTests`.

| Route | Closed by |
| --- | --- |
| A scanner acquires the body index without declaring `Unbounded` | `RequireUnboundedDeclaration` at acquisition, not at registration |
| A key is re-registered with a higher cost after sections snapshotted it | `RejectReregistration` in `Add`/`AddBundle` |
| A prerequisite list is mutated through an alias the caller still holds | copy-on-registration into `ImmutableArray<string>` |
| A scanner reaches `LibraryBodyIndex.Open` without going through the gated accessor | a pinned opener set plus a reverse-reachability gate, over the compiled product assemblies |
| A caller outside any scanner run takes the body index unchecked | default-deny in `RequireUnboundedDeclaration` (below) |

That the enumeration reached five is itself the finding. After each fix the class looked closed, and the next route was found by review rather than by the author — so for a defect whose shape is "the declaration and the work can drift apart", an author's own enumeration should not be trusted as complete.

The third is worth stating plainly because the original code looked defensive: `RequirementsOf` returned `IReadOnlyList<string>` over the caller's `params string[]`, and that interface casts straight back to the array. A read-only *interface* over a mutable array is not enforcement; `ImmutableArray<T>` is, because the type system carries it.

The fourth is the only one no seam can intercept. `ctx.AssemblyPath` is a `string`, and a static `LibraryBodyIndex.Open(path)` call bypasses `RequireUnboundedDeclaration` entirely while doing the same seconds of whole-assembly work. Unlike deliberate subversion through `ImmutableCollectionsMarshal`, this route is reachable from ordinary code, so it is a genuine drift path.

`NoSectionReachesTheBodyIndexExceptThroughTheGatedAccessor` closes it by running this repository's own IL analysis over its own compiled assemblies. Getting it right took five attempts, and the four failures are worth recording because all four looked convincing:

1. **Direct callers only.** The first version asserted that no caller of `LibraryBodyIndex.Open` lived in `DotnetInspector.Sections`. Review broke it in one line: scanners already route their work through `LibraryMetadataService`, so a scanner calling a helper *there* that opens an index passed untouched. The gate was checking the one spelling nobody would use.
2. **Reachability only.** The second version walked the call graph backwards from every opener. Review broke that too, by calling a helper on a **constructed generic type** — `Helper<int>.Open()` goes through a `MemberRef` on a `TypeSpec`, and `DirectCalls` records *no edge at all* for it. Not a wrong edge: no edge. No backwards walk can find a caller that the graph does not contain. A static constructor is invisible for a second reason — the CLR runs a type initializer on first use and no IL instruction references it.
3. **One assembly, and keyed by name.** The third version pinned the openers, which fixed the graph problem, but pinned them by `Type::Method` within `dotnet-inspect` alone. Both halves of that were escapable: a second overload of an already-pinned opener collapsed into its sibling, and a helper in `ILInspector.Analysis` was outside the graph entirely.
4. **A hand-listed assembly set.** The fourth version merged in the assembly whose helper had just been used to escape it. That fixed the instance and left the shape: review pointed at a third assembly the next round. The set had to stop being a list and start being derived.

The generalizable move is in the second failure: when a gate keeps being escaped, check whether the evidence it rests on can even *see* the escape. Two rounds went into improving matching inside a graph that structurally lacked the edge. Prefer a claim over a **declared set** to a claim over a **derived relation**, because the set fails closed when the analysis is incomplete.

The fourth adds a second habit: when a fix names the specific thing that was just used against you, ask whether the *category* is enumerable, and derive it if so. `ILInspector.Analysis` was the escape; product assemblies were the category.

So the gate now leads with a claim that does not depend on the call graph being complete: **the set of methods that open a body index is pinned**, and pinned by *signature* rather than by name. Every escape of that kind has to *add* an opener, and an opener is visible however it is reached.

Signatures are load-bearing twice over, which is why the earlier name-keyed version was still broken. Review added a second `MethodBodyInspectionSession.OpenWithFeatures` overload that opened an index: projecting to `Type::Method` and calling `Distinct()` collapsed it into the existing entry, so a new opener changed nothing the gate could see. The same collapse also produces false positives — `ResourceLifecycleAnalysis` has two `InspectAssembly` overloads that sit on *opposite* sides of this gate, one that opens an index itself and one that merely invokes the `Func<LibraryBodyIndex>` it was handed. Keyed by name they are one node, and the sanctioned `ResourceTriage` path would be reported as a violation needing an allow list — and the allow list would be exactly where a real escape would hide.

A reverse-reachability walk then adds the claim the pinned set cannot make: no section reaches one of those openers **except through** `ScannerContext.BodyIndex`/`DrillMap`. Calling a sanctioned opener from a scanner adds no new opener, so only the walk catches it. Neither claim subsumes the other. The walk still over-approximates by adding an edge from every call site to the type initializer of the type it touches, because a spurious edge can only make this gate redder, never blind.

Cutting the walk at the accessor is what keeps it precise. Without the cut it reports seven `Sections` members, and all seven are legitimate: the accessor itself, the four `Unbounded` scanner lambdas that use it, and their enclosing factory. A gate that flagged those would need an allow list, and the allow list would become the hole.

Its boundary used to be the assembly, and that boundary was a live hole rather than a theoretical one. A `NetworkFree` scanner calling `LeakTriageAnalyzer.AnalyzeAssembly(ctx.AssemblyPath)` left both claims green while the real CLI spent 5.1 s building an index — ordinary typed code, not subversion, because `ILInspector.Analysis` opens the index *inside* a helper this assembly's call graph knows nothing about. The gate had named that boundary and called it unverified; naming it was not enough.

Naming a *second* assembly was not enough either, and for a reason worth keeping: it fixed the instance rather than the shape. Review promptly pointed at a third — `ILInspector.Research` opens an index in `AnalysisIndexCache.ForPath` and in `ResearchDiff`. So the gate now lists no assemblies at all. The set is **derived as the product reference closure of the CLI**, walked from `dotnet-inspect` through every referenced `ILInspector.*`/`DotnetInspector.*` assembly, which means a new product assembly enters this gate by being referenced rather than by someone remembering to add it.

Deriving it also settles a question a directory scan would get wrong. `DotnetInspector.Fixtures` sits in the same output directory and matches the same name prefix, so a glob would sweep test-support code into the pinned opener set. The closure excludes it because the CLI does not reference it — a property, not an exception, and one the gate asserts.

What remains outside is reflection and interface dispatch that never resolves to a definition. Those are **unverified, not closed**, and the gate says so where it is written.

### Work that belongs to no scanner cannot be afforded

Cutting the walk at `ScannerContext.BodyIndex`/`DrillMap` is only sound if the accessor really does enforce. It did not, at first.

`RequireUnboundedDeclaration` used to **return** when `Running` was null, reasoning that a caller outside a scanner run has no declaration to check against. That made the absence of a declaration the one way to escape needing one. `RunWithRequirements` restores `Running` in a `finally`, so it is null for the whole render phase — and review reached it from ordinary code: a descriptor's `CanRender` that captured the `ScannerContext` called `BodyIndex()` while rendering and spent seconds on work no section had declared. It also laundered the very violation the cut claims to exclude, since the walk treats a path through the accessor as gated.

Cost is declared per scanner, so work that cannot be attributed to one cannot be afforded by anything: an unscoped caller is now **refused**. Exactly one test depended on the old permission, which is the useful measurement — no product path calls these accessors outside a scanner run. `UnscopedCallers_AreRefusedTheBodyIndex` pins the refusal, and `ScannerDeclaration_DoesNotOutliveTheRun` distinguishes the two refusal messages so that deleting the `finally` still fails.

`RequirementsOf` returns `ImmutableArray<string>`, which `ImmutableCollectionsMarshal.AsArray` can still unwrap. That is a documented property of `ImmutableArray` shared by every one of this repository's public `ImmutableArray`-returning members, not a property of this registry, and the type is unused anywhere in the product. The boundary is named in `RequirementsOf`'s doc comment rather than left as an unqualified claim.

## Data Flow

```text
CLI input
  │
  ▼
Pipeline.GetRequiredVerbosity(userSections)
  │  auto-promotes verbosity when -S targets Detailed-only sections
  ▼
Pipeline.ComputeIncludeSections(effectiveVerbosity, userSections)
  │  returns HashSet<string> of section names
  ▼
Pipeline.GetRequiredScanners(includedSections)
  │  returns unique scanner keys for those sections
  ▼
Registry.RunScanners(requiredKeys, context)
  │  collects data into model — only needed scanners run
  ▼
Markout serializer renders with IncludeSections filter
```

For effective discovery (list effective sections):

```text
Pipeline.GetEffectiveSections(model, verbosity, userSections)
  │  filters by MinVerbosity, then CanRender(model)
  ▼
Print section names that have data
```

## Scanner Key Deduplication

Multiple sections can share a scanner key. For example:

| Section | Scanner Key |
| ------- | ----------- |
| Unsafe Methods | `ClassifiedMethods` |
| P/Invoke Methods | `ClassifiedMethods` |
| Async Methods | `ClassifiedMethods` |

The `ClassifiedMethods` scanner runs once and populates both lists. `GetRequiredScanners` deduplicates keys, so requesting both sections does not scan twice.

Sections with a `null` scanner key have their data collected unconditionally as part of core metadata loading.

## Library Sections

The library command currently has 16 registered sections:

| Section | MinVerbosity | Scanner Key |
| ------- | ------------ | ----------- |
| Library Info | Minimal | — |
| Async Methods | Normal | `ClassifiedMethods` |
| Custom Attributes | Normal | `CustomAttributes` |
| Dependencies | Normal | `TransitiveRefs` |
| Extension Methods | Normal | `ExtensionMethods` |
| Non-normalized Paths | Normal | — |
| P/Invoke Methods | Normal | `ClassifiedMethods` |
| References | Normal | — |
| Resources | Normal | `Resources` |
| Signals | Normal | `AuditSignals` |
| Symbols | Normal | `Symbols` |
| Type Forwarders | Normal | `TypeForwarders` |
| Unsafe Methods | Normal | `ClassifiedMethods` |
| SourceLink: Availability | Explicit | — |
| SourceLink: Integrity | Explicit | — |
| SourceLink: Missing Files | Explicit | — |

## Fallback Path

When `scannerRegistry` is null (tests, non-pipeline callers), `InspectAsync` falls back to the original `if (verbosity == Detailed)` gating. This preserves backward compatibility during incremental adoption.

## Headless Sections

Some content logically belongs to a section (for addressing and filtering) but should not render a `##` heading. The canonical example is the package **Summary** — the compact inline fields (`Version: 2.0.3 | Type: Library | ...`) that appear as preamble in the default view.

Markout's `[MarkoutSection(Headless = true)]` enables this:

```csharp
[MarkoutSection(Name = "Summary", Headless = true)]
public List<MarkoutField> Summary => GetCompactFields();
```

A headless section:
- **Is addressable** — appears in `-S` discovery (`Summary  section`)
- **Is filterable** — `-S Summary` includes it; `-S "Package Info"` omits it
- **Emits no heading** — `WriteSectionStart(headless: true)` calls `UpdateSectionState` for filtering but skips the `##` render
- **Uses inline rendering** — headless `FieldCollection` sections use `WriteFieldsInline` rather than `WriteFieldsTable`

The pipeline always uses `IncludeSections`:

```csharp
// BuildWriterOptions — clean, additive model
var includeSections = pipeline.ComputeIncludeSections(
    result, options.Verbosity, options.IncludeSections);

return new MarkoutWriterOptions { IncludeSections = includeSections };
```

When the user runs `-S "Package Info"`, the pipeline returns `{"Package Info"}` — no Summary, so preamble is hidden. In the default view, the pipeline returns `{"Summary", "Package Info", ...}` — Summary is included, preamble renders.

## Package Sections

The package command has 17 registered sections:

| Section | MinVerbosity | Scanner Key | Notes |
| ------- | ------------ | ----------- | ----- |
| Summary | Quiet | — | Headless; compact inline fields |
| Dependencies | Normal | — | Only when dependency groups present |
| Manifest | Minimal | — | Basic package manifest rows, with extra tool manifest rows when present |
| Package files | Explicit | — | Full-depth package file listing with `Path` and `Size`; `Unbounded` cost, so verbosity never reaches it |
| Package Info | Minimal | — | Full metadata field table |
| Package nuspec file | Minimal | — | The `.nuspec` manifest path with `Path` and `Size`; at most one row. `--print` emits the document |
| Package README file | Minimal | — | Best README candidate with `Path` and `Size`; at most one row |
| Package skill files | Normal | — | `skills/**/SKILL.md` files with `Path` and `Size`; only when the package ships skills |
| Runtime Dependencies | Minimal | — | Only when runtime deps present |
| Signals | Detailed | — | Package metadata/assets, dependency, provenance, and NuGet registry observations; `Moderated` cost |
| Signature | Normal | — | Only when signature information is available |
| Statistics | Detailed | — | Published date, download counts |
| Target Frameworks | Normal | — | Explicit package TFM directories |
| Vulnerabilities | Detailed | — | Only when vulnerabilities present |

## Format Auto-Promotion

When the pipeline computes multiple sections and the output format is the default table format, the command auto-promotes to markdown. If the user explicitly requested `--table` or `--tsv`, a diagnostic error is returned instead.

This is tracked on options objects, which distinguish the default format from an explicit tabular flag.

## Tracing

`--trace` writes a diagnostic report to **stderr** describing the work a run actually did. stdout is
untouched, so a caller parsing the document is unaffected (gated by a byte-identical stdout check).

The report answers "did this run do the correct minimum work?", which nothing else can:

```text
trace: library ILInspector.Decompiler.dll [Minimal]
  sections demanding a scanner
    Library Info -> InfoCounts
  scanners requested   InfoCounts
  added by prerequisite ClassifiedMethods, CustomAttributes, ExtensionMethods, Resources, TypeForwarders
  scanners executed
    ExtensionMethods            9.3 ms
    ClassifiedMethods           48.6 ms
    ...
    InfoCounts                  0.0 ms  (bundle, no work of its own)
  resources acquired
    metadata session            borrowed from the command's open image
  total scanner time           74.4 ms
```

Five things are recorded, each at the only layer that knows it:

| Fact | Recorded by |
| --- | --- |
| Which section demanded which scanner | `SectionPipeline.GetRequiredScanners` |
| Which scanners the command asked for directly | `LibraryCommand` (discovery mode) |
| What prerequisite expansion added | `ScannerRegistry.ExpandRequired`, via `InspectAsync` |
| Which scanners ran, in order, with timings | `ScannerRegistry.RunScanners` |
| Whether a scanner is a bundle (no work of its own) | `ScannerRegistry.RunScanners` |
| Which expensive resources were built | `ScannerContext.Session`/`BodyIndex`/`DrillMap` |

Section-to-scanner attribution exists only on the demand side — downstream the registry sees a set of
keys with no memory of who asked for them — which is why it is captured there rather than
reconstructed later.

Design points worth keeping:

- **The typed record is the contract, not the text.** Tests assert on `InspectionTrace`, so the report
  can be reformatted without rewriting gates.
- **Each of the three ways a scanner can be pulled in has its own bucket.** A section demanded it, the
  command asked for it directly (today only discovery mode's metadata row counts), or a declared
  prerequisite pulled it in. Collapsing the second into the third would render a prerequisite edge
  that does not exist, and send anyone chasing an unexpected scan to the wrong declaration.
  `Trace_ExplainsEveryScannerThatRan` seeds reachability from what the trace *claims* — the recorded
  section and command demands — and walks the registry's declared edges to reach the closure the run
  actually produced. That asymmetry is what makes it a gate: seeding from the requested set instead
  would re-derive `ExpandRequired`'s own input and assert a set is a subset of itself.
- **Resources are recorded at acquisition, not at request.** A resource that never appears was never
  built. That absence is the observable: a regression that makes a metadata-only scan open the
  whole-assembly IL index costs seconds and changes no output, so no other test would notice it.
- **A failed acquisition is recorded too.** Scanners swallow a failed body index and render an empty
  section; without a `FAILED` line, a run that tried and failed would look exactly like a run that
  correctly never needed the index.
- **Untraced runs pay nothing.** Tracing is threaded as a nullable object rather than a flag, so an
  untraced run allocates nothing and takes no branch beyond a null check. A gate holds the shared
  scan count equal with and without tracing, because a diagnostic that perturbs what it measures is
  worse than none.
- **The report is written in a `finally`.** A failed run still reports what it had done before
  failing, which is when the question is most worth asking.

## Design Decisions

**Static abstract interfaces over instances.** Descriptors are never instantiated. The pipeline extracts static members into delegate-based `SectionEntry<T>` records at registration time. This avoids allocations and keeps metadata in a format that NativeAOT can optimize.

**Manual registration over reflection.** Each command has a `CreatePipeline()` factory that calls `Add<TDescriptor>()` for each section. This is explicit, ordered, and has zero startup cost from assembly scanning.

**`CanRender` is static.** It takes the model as a parameter rather than being an instance method. This means the pipeline can answer "does this section have data?" without constructing a renderer or section object.

**Pipeline computes, serializer renders.** The pipeline never touches Markout directly. It produces a set of section names. The Markout serializer's existing `IncludeSections` mechanism handles the rest. This keeps the boundary clean: pipeline = decision logic, Markout = rendering.

## Future Work

- **Comma-separated multi-value `-S`.** Support `dotnet-inspect ... -S "Stats*,files,Foo"` as a single argument with comma-delimited section names. Wrong names should not block valid ones.
- **Discovery as renderable data.** `-S --markdown` and `-S --json` should render discovery output through the same Markout pipeline as actual data.
- **Lightweight pre-scanners for `CanRender`.** Currently, `-S` discovery runs full scanners to determine which sections have data. A future optimization would add cheap pre-scan checks (e.g., "does the assembly have any resources?" via PE header flags) that can answer `CanRender` without full data collection.
- **Static execution table.** Some scanners share intermediate results. The [capability section registry spike](capability-section-registry-spike.md) evaluates a process-wide lambda table with precompiled dependency order and authorization policy while keeping selection and rendering here. Reusable acquisition and common-plan lookup are allocation-free; cold initialization remains explicit, so the conclusion is a generated static-table pilot for expensive, multi-prerequisite sections rather than a full replacement of `ScannerKey`.
