# Backlog

Ideas for improving dotnet-inspect for LLM-driven C# development.

> **Note:** Completed features are removed from this backlog. See git history for implemented items.

## SourceLink Command

A top-level `sourcelink` command for inspecting SourceLink metadata across packages and assemblies:

```bash
dotnet-inspect sourcelink --package System.Text.Json           # table of all types with source URLs
dotnet-inspect sourcelink --package System.Text.Json JsonSerializer  # source URL for a specific type
dotnet-inspect sourcelink --assembly ./bin/MyLib.dll           # local assembly
```

Default output: a table with Type, Source URL, and Resolution (SourceLink/Inferred/XmlDoc) columns for all public types. When a type name is given, output the full source URL for that type (useful for fetching source).

This replaces the current behaviour where Source, Source Resolution, and Partial Type fields appear in the default `api` view. Those fields now only appear with `--docs`. A dedicated command surfaces this information more intentionally and avoids the network cost of SourceLink resolution in the default API view.

Could also integrate with the audit command to show SourceLink coverage as part of provenance checks.

## Audit Command

A dedicated `audit` subcommand for streamlined package provenance checks:

```bash
dotnet-inspect audit Markout@0.1.4                      # NuGet package
dotnet-inspect audit System.Text.Json                   # platform assembly
dotnet-inspect audit ./bin/MyLib.dll                    # local file
dotnet-inspect audit Markout@0.1.3 Markout@0.1.4        # batch/compare
```

Currently requires `assembly --package X --audit` which isn't intuitive to discover. A top-level `audit` command would:

- Provide a clear entry point for security/provenance workflows
- Support batch mode for auditing multiple versions at once
- Surface SourceLink, determinism, and PDB status in one view

Feedback: "Took a couple attempts to find the right command - I first tried `package X --assembly`"

## Migration Hints

When diffing versions, suggest code transformations:

```bash
dotnet-inspect diff Command --package System.CommandLine@2.0.0-beta4.22272.1..2.0.2 --migrate
```

Would add a section:

```text
## Migration Notes

- `AddOption(opt)` → `Add(opt)` or collection initializer `Options = { opt }`
- `AddArgument(arg)` → `Add(arg)`
- `Handler.SetHandler(...)` → `SetAction(Action<ParseResult>)`
```

Could be powered by:

- Heuristics (method renamed, similar signature)
- Curated migration data for popular packages
- XML doc deprecation messages

## Derived Types Table

Add a "Derived Types" section to `api` output that displays:

- Interfaces implemented by the type (already available via `--interfaces`, but could be a table)
- Base class hierarchy (immediate base, optionally full chain)
- Known derived types within the same assembly

This would help LLMs understand type relationships and inheritance patterns at a glance.

## Source URLs Per-Member

Consider adding source URLs for individual members in the API member table. Currently only type-level source URLs are shown. Per-member URLs would enable:

- Direct navigation to specific method/property definitions
- More precise context for LLMs when reasoning about specific members
- Better integration with code review workflows

Challenges:

- Would significantly increase output size
- Requires resolving line numbers for each member from PDB
- May need to be opt-in (`--member-source-urls` or similar)

## Inherited Members Option

A `--inherited` flag to show members from base classes inline:

```bash
dotnet-inspect api Command --package System.CommandLine --inherited
```

Currently you have to separately query base classes (`Symbol`) to find inherited members like `Description` and `Name`. Flattening the inheritance chain would give a complete picture in one query.

## Example Snippets

A `--examples` flag could show basic usage patterns for types and methods:

```bash
dotnet-inspect api JsonSerializer --package System.Text.Json --examples
```

This could pull from XML documentation `<example>` tags if present, or generate minimal usage patterns from constructor/method signatures. Would significantly accelerate LLM code generation by providing working starting points.

## Constructor Parameter Context

When viewing a class, it would help to see what the constructor parameters represent—especially for dependency injection scenarios. Understanding "what do I need to construct this?" is a common LLM task.

Could show required vs optional parameters, and flag parameters that are typically injected vs provided directly.

## Related Types Discovery

When inspecting a type like `JsonSerializer`, suggest related types that are commonly used together:

- `JsonSerializerOptions`
- `JsonTypeInfo<T>`
- `JsonSerializerContext`

This would help LLMs understand the ecosystem around a type without multiple exploratory queries.

## Style Guide Review

Audit all command outputs to ensure they follow the style guide in `docs/style-guide.md`:

- Top-level metadata uses `**Field:** value` format
- Tables appear in named H2 sections for collections
- Consistent field ordering across commands
- Boolean values rendered appropriately (✓/✗ in tables, yes/no in fields)

Also evaluate the "verbosity spread" across `-v q`, `-v m`, `-v n`, `-v d`:

- Is the default (`-v n`) the right balance for most use cases?
- Is there meaningful differentiation between levels?
- Are the right sections at the right verbosity levels?
- Consider if some fields should only appear at higher verbosity

## Dependency Graph Command

A `list` or `graph` command that provides flat or tree-like views of package dependencies:

```bash
dotnet-inspect deps --package Newtonsoft.Json
dotnet-inspect deps --project ./src/MyApp.csproj --tree
```

Could expose audit information for each dependency:

- Unsafe code usage (similar to cargo-geiger)
- Package age / last updated date
- SourceLink availability
- License information
- Known vulnerabilities (optional, requires external data)
- Signature verification status

Tree view would show the full transitive dependency graph. Flat view would deduplicate and summarize. Could support filtering to find "why is this package included" scenarios.

Inspiration: [cargo-geiger](https://github.com/rust-secure-code/cargo-geiger) for Rust.

## Speculative Output for LLMs

Experiment with writing low-verbosity output to stdout and high-verbosity output to a secondary channel (FD3 or temp file) simultaneously. This would enable LLMs to:

1. Read the concise stdout to determine if more detail is needed
2. Access the pre-computed detailed output without a second query

Similar to CPU speculative execution—we compute the detailed output in parallel, betting that it might be needed. The LLM avoids a round-trip if the speculation was correct.

Needs investigation:

- Identify high-value scenarios where this pattern makes sense
- Measure the cost of always computing detailed output
- Determine the right mechanism (FD3, temp file, named pipe)
- Consider how LLM tooling would consume the secondary channel

## On-Demand Ref Pack Downloads

Download reference packs from NuGet on-demand, similar to how regular packages are handled.

Benefits:

- Inspect framework versions not installed locally
- Consistent behavior between `--package` and `--platform`
- Access to older framework versions without SDK installation

Implementation:

- Ref packs are published to nuget.org (e.g., `Microsoft.NETCore.App.Ref`)
- Download and cache in `~/.local/share/dotnet-inspect/packs/`
- Fall back to local SDK packs when available

```bash
# Inspect .NET 8 BCL without having .NET 8 SDK installed
dotnet-inspect api JsonSerializer --platform System.Text.Json --framework runtime@8.0
```

## Performance Optimization Review

Audit text and binary processing code for optimization opportunities:

Areas to review:

- Use of `SearchValues<T>` for repeated character/string searches
- SIMD-accelerated operations via `Vector<T>` or `Vector128/256`
- Span-based parsing to reduce allocations
- String interning for repeated type/namespace names
- Lazy initialization where appropriate

Important considerations:

- JIT cost vs. runtime benefit—optimizations must be used dozens of times, not 3
- Measure before and after with realistic workloads
- Some optimizations only pay off for large assemblies (100+ types)
- Consider tiered JIT implications

Create benchmarks for key operations before optimizing.

## Namespace Listing

Add namespace discovery to the `api` command:

```bash
dotnet-inspect api --package System.Text.Json --namespaces
```

Output:

```text
# System.Text.Json Namespaces

| Namespace | Types |
|-----------|-------|
| System.Text.Json | 15 |
| System.Text.Json.Nodes | 8 |
| System.Text.Json.Serialization | 22 |
| System.Text.Json.Serialization.Metadata | 12 |
```

Could also support filtering the main `api` output by namespace:

```bash
dotnet-inspect api --package System.Text.Json --namespace "*.Serialization"
```

## Extension Methods Discovery

Find extension methods that apply to a given type:

```bash
dotnet-inspect extensions IEnumerable<T> --package System.Linq
```

Would search for static methods where the first parameter is the target type (with `this` modifier). Challenging because extension methods can target interfaces and base classes.

## Attribute Inspection

Expose custom attributes on types and members:

```bash
dotnet-inspect api JsonSerializer --package System.Text.Json --attributes
```

Would show attributes like `[Obsolete]`, `[RequiresUnreferencedCode]`, `[JsonConverter]`, etc. Particularly useful for understanding trimming/AOT compatibility and serialization behavior.

## Interface Implementation Finder

Given an interface, find all types that implement it:

```bash
dotnet-inspect implements IDisposable --package System.Text.Json
dotnet-inspect implements IJsonTypeInfoResolver --package System.Text.Json
```

Useful for:

- Discovering available implementations of a strategy/plugin interface
- Understanding the breadth of a package's type hierarchy
- Finding concrete types when you only know the interface from docs

## Nullability Annotations

Expose nullable reference type annotations in API output:

```bash
dotnet-inspect api --package System.Text.Json JsonSerializer --nullability
```

Would show:

- Which parameters accept null (`string?` vs `string`)
- Which return types can be null
- Nullability attributes like `[NotNull]`, `[MaybeNull]`, `[NotNullWhen]`

Critical for LLMs generating null-safe code without unnecessary null checks.

## Async Method Analysis

Identify async patterns and cancellation support:

```bash
dotnet-inspect api HttpClient --package System.Net.Http --async
```

Would highlight:

- Methods returning `Task`, `Task<T>`, `ValueTask`, `ValueTask<T>`
- Whether CancellationToken parameter is available
- Sync-over-async pairs (e.g., `Read` vs `ReadAsync`)
- `IAsyncEnumerable<T>` support

Helps LLMs choose async overloads and properly propagate cancellation.

## Factory Method Discovery

Find static factory methods and builders that create instances of a type:

```bash
dotnet-inspect factory JsonSerializerOptions --package System.Text.Json
```

Would search for:

- Static `Create*` methods returning the type
- Builder pattern classes
- `Parse`, `From*`, `Of` methods
- DI registration extension methods

Useful when constructors are non-public or when factory methods are the preferred instantiation path.

## Platform Compatibility

Surface `[SupportedOSPlatform]` and `[UnsupportedOSPlatform]` attributes:

```bash
dotnet-inspect api --package System.Drawing.Common --platforms
```

Output would flag members restricted to Windows, Linux, macOS, Browser, etc. Essential for cross-platform LLM code generation.

## Package Build Integration

Inspect what packages contribute to the build:

```bash
dotnet-inspect package Microsoft.SourceLink.GitHub --build-assets
```

Would show:

- MSBuild props/targets files included
- Roslyn analyzers bundled
- Build-time code generators
- Native runtime dependencies (`runtimes/*/native/*.dll`)

Helps understand packages that do more than provide APIs.

## Call Graph / Cross-References

For a method or type, show what it calls or what calls it:

```bash
dotnet-inspect xref JsonSerializer.Serialize --package System.Text.Json
```

Would require IL analysis. Could be scoped to within-assembly references. Useful for understanding code flow and impact analysis.

## Record Type Support

Enhanced display for record types showing:

- Primary constructor parameters
- Init-only properties
- Positional deconstruction
- With-expression cloning

Records are increasingly common and have different instantiation patterns than regular classes.

## Consider writing and integrating with a separate tool that is daemon-like (like the build server)

Some of the commands we are considering (like find) night be expensive. We could potentially only support those with a persistent process to make them cheaper to execute and for caching.

## Member Index Shorthand (`Deserialize:6`)

Support `Name:N` syntax for selecting a specific overload by position, e.g.:

```bash
dotnet-inspect api System.Text.Json JsonSerializer Deserialize    # see all overloads
dotnet-inspect api System.Text.Json JsonSerializer Deserialize:6  # member page for the 6th one
dotnet-inspect api --package Microsoft.Extensions.Options OptionsFactory .ctor:2  # 2nd constructor
```

This replaces the current two-step `--select` → copy-paste `--params` workflow for getting to member pages. The member table already implicitly numbers overloads by row order — this just makes that addressable. Much more ergonomic for humans, especially for constructors where the `--params` values are enormous. The `--select` mode can remain for programmatic/LLM use where full `--params` addressing is more reliable.
