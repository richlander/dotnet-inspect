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

For bare `-S` discovery (list effective sections):

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

The `ClassifiedMethods` scanner runs once and populates both lists. `GetRequiredScanners` deduplicates keys, so requesting both sections does not scan twice.

Sections with a `null` scanner key have their data collected unconditionally as part of core metadata loading.

## Library Sections

The library command currently has 14 registered sections:

| Section | MinVerbosity | Scanner Key |
| ------- | ------------ | ----------- |
| Library Info | Minimal | — |
| References | Minimal | — |
| Package Dependencies | Normal | — |
| Symbols | Detailed | — |
| Assembly Attributes | Normal | — |
| Target Framework | Normal | — |
| Extension Methods | Detailed | `ExtensionMethods` |
| Unsafe Methods | Detailed | `ClassifiedMethods` |
| P/Invoke Methods | Detailed | `ClassifiedMethods` |
| Resources | Detailed | `Resources` |
| Custom Attributes | Detailed | `CustomAttributes` |
| Type Forwarders | Detailed | `TypeForwarders` |
| Build Audit | Detailed | `Audit` |
| Transitive References | Detailed | `TransitiveRefs` |

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
- **Is filterable** — `-S Summary` includes it; `-S Package` omits it
- **Emits no heading** — `WriteSectionStart(headless: true)` calls `UpdateSectionState` for filtering but skips the `##` render
- **Uses inline rendering** — headless `FieldCollection` sections use `WriteFieldsInline` rather than `WriteFieldsTable`

The pipeline always uses `IncludeSections`:

```csharp
// BuildWriterOptions — clean, additive model
var includeSections = pipeline.ComputeIncludeSections(
    result, options.Verbosity, options.IncludeSections);

return new MarkoutWriterOptions { IncludeSections = includeSections };
```

When the user runs `-S Package`, the pipeline returns `{"Package"}` — no Summary, so preamble is hidden. In the default view, the pipeline returns `{"Summary", "Package", ...}` — Summary is included, preamble renders.

## Package Sections

The package command has 8 registered sections:

| Section | MinVerbosity | Scanner Key | Notes |
| ------- | ------------ | ----------- | ----- |
| Summary | Quiet | — | Headless; compact inline fields |
| Package | Minimal | — | Full metadata field table |
| RID Packages | Minimal | — | Only when RID-specific packages present |
| Runtime Dependencies | Minimal | — | Only when runtime deps present |
| Package Dependencies | Normal | — | Only when dependency groups present |
| Statistics | Detailed | — | Published date, download counts |
| Vulnerabilities | Detailed | — | Only when vulnerabilities present |
| Files | Detailed | — | Package file listing |

## Format Auto-Promotion

When the pipeline computes multiple sections and the output format is the default (oneline), the command auto-promotes to markdown. If the user explicitly requested `--oneline`, a diagnostic error is returned instead.

This is tracked via `OneLineExplicitlySet` on options objects, which distinguishes the default format from an explicit `--oneline` flag.

## Design Decisions

**Static abstract interfaces over instances.** Descriptors are never instantiated. The pipeline extracts static members into delegate-based `SectionEntry<T>` records at registration time. This avoids allocations and keeps metadata in a format that NativeAOT can optimize.

**Manual registration over reflection.** Each command has a `CreatePipeline()` factory that calls `Add<TDescriptor>()` for each section. This is explicit, ordered, and has zero startup cost from assembly scanning.

**`CanRender` is static.** It takes the model as a parameter rather than being an instance method. This means the pipeline can answer "does this section have data?" without constructing a renderer or section object.

**Pipeline computes, serializer renders.** The pipeline never touches Markout directly. It produces a set of section names. The Markout serializer's existing `IncludeSections` mechanism handles the rest. This keeps the boundary clean: pipeline = decision logic, Markout = rendering.

## Future Work

- **Comma-separated multi-value `-S`.** Support `dotnet-inspect ... -S "Stats*,files,Foo"` as a single argument with comma-delimited section names. Wrong names should not block valid ones.
- **Discovery as renderable data.** `-S --markdown` and `-S --json` should render discovery output through the same Markout pipeline as actual data.
- **Lightweight pre-scanners for `CanRender`.** Currently, `-S` discovery runs full scanners to determine which sections have data. A future optimization would add cheap pre-scan checks (e.g., "does the assembly have any resources?" via PE header flags) that can answer `CanRender` without full data collection.
- **Scanner dependency graph.** Some scanners might share intermediate results. A dependency graph between scanners could enable further deduplication.
