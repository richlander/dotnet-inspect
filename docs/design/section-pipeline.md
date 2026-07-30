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

`ScannerRegistry` maps scanner keys to scan functions:

```csharp
registry.Add("ExtensionMethods", ctx =>
    LibraryMetadataService.ScanExtensionMethods(ctx.AssemblyPath, ctx.Model, ctx.Logger));
```

`RunScanners(requiredKeys, context)` executes only the scanners needed for the current request. When a user runs `dotnet-inspect library Foo.dll -S "Extension Methods"`, only the `ExtensionMethods` scanner runs — not the full set of Detailed-level scans.

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
  `Trace_AttributesEveryAddedScannerToARealPrerequisiteEdge` walks the registry's declared edges and
  fails if anything shown as expansion is not actually reachable from a requested key.
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
