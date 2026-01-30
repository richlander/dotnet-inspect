# Backlog

Ideas for improving dotnet-inspect for LLM-driven C# development.

## Platform Command

Add a `platform` command for discovering and listing platform/framework assemblies without requiring knowledge of installation paths:

```bash
# List all platform frameworks
dotnet-inspect platform --list-frameworks

# Output:
# | Framework | Version | Assemblies |
# |-----------|---------|------------|
# | runtime | 9.0.12 | 142 |
# | aspnetcore | 9.0.0 | 87 |

# List assemblies for a specific framework
dotnet-inspect platform --framework runtime

# Output:
# # runtime (9.0.12)
# 
# | Assembly | Types |
# |----------|-------|
# | System.Text.Json.dll | 77 |
# | System.Net.Http.dll | 45 |
# | ... | ... |

# List all assemblies across all frameworks (default)
dotnet-inspect platform

# Specify a version
dotnet-inspect platform --framework runtime@8.0.23
```

Framework short names:
- `runtime` → Microsoft.NETCore.App.Ref
- `aspnetcore` → Microsoft.AspNetCore.App.Ref
- `netstandard` → NETStandard.Library.Ref

Behavior:
- Auto-detect platform installation paths (macOS, Windows, various Linux distros)
- Default to latest installed version (like TFM selection in packages)
- May need a `--list-versions` flag to show available versions per framework
- Similar to `dotnet --info` but focused on ref assemblies for inspection

## Platform Flag for API Commands

Add `--platform` flag to `api` and `samples` commands as an ergonomic alternative to long `--assembly` paths:

```bash
# Instead of:
dotnet-inspect api JsonSerializer --assembly /usr/local/share/dotnet/packs/Microsoft.NETCore.App.Ref/9.0.12/ref/net9.0/System.Text.Json.dll

# Use:
dotnet-inspect api JsonSerializer --platform System.Text.Json
dotnet-inspect api --platform System.Text.Json  # List all types

# Specify framework if needed (defaults to runtime)
dotnet-inspect api --platform Microsoft.AspNetCore.Mvc --framework aspnetcore

# Specify version
dotnet-inspect api --platform System.Text.Json --framework runtime@8.0.23

# Samples from platform
dotnet-inspect samples --platform System.Text.Json
```

The `.dll` extension should be optional. All the same defaulting applies:
- Default to `runtime` framework
- Default to latest installed version
- Search across frameworks if assembly name is unambiguous

Long `--assembly` paths remain supported but not documented as the primary workflow.

## Derived Types Table

Add a "Derived Types" section to `api` output that displays:

- Interfaces implemented by the type (already available via `--interfaces`, but could be a table)
- Base class hierarchy (immediate base, optionally full chain)
- Known derived types within the same assembly

This would help LLMs understand type relationships and inheritance patterns at a glance.

## Generic Constraints Table

Add a "Generic Constraints" section for generic types and methods:

```
## Type Parameters

| Parameter | Constraints |
|-----------|-------------|
| T | class, IDisposable, new() |
| TKey | notnull |
| TValue | struct |
```

Would expose:
- `where T : class` / `struct` / `notnull` / `unmanaged`
- Interface constraints
- Base class constraints
- `new()` constructor constraint
- Variance (`in`/`out` for interface type parameters)

Currently generic constraints are not visible in the API surface, making it hard for LLMs to understand what types can be used as type arguments.

## Source URLs Per-Member

Consider adding source URLs for individual members in the API member table. Currently only type-level source URLs are shown. Per-member URLs would enable:

- Direct navigation to specific method/property definitions
- More precise context for LLMs when reasoning about specific members
- Better integration with code review workflows

Challenges:
- Would significantly increase output size
- Requires resolving line numbers for each member from PDB
- May need to be opt-in (`--member-source-urls` or similar)

## NuGet.config Support

Add support for reading `NuGet.config` files when resolving packages. Currently package resolution only uses the default NuGet cache and nuget.org. This would enable:

- Using local package sources for development/testing
- Using private feeds (Azure Artifacts, GitHub Packages, etc.)
- Respecting repository-specific package configurations

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

## Output to File

An `--out` flag to write results directly to a file:

```bash
dotnet-inspect api --package System.Text.Json --out api-surface.md
dotnet-inspect package System.Text.Json --json --out package-info.json
```

Useful for:
- Saving API surfaces for later reference or diffing
- Generating documentation artifacts
- Caching expensive queries locally
- Piping to other tools that prefer file input over stdin

## Skill Plugin

Add a skill plugin to this repo. (User-requested feature - needs investigation to understand what this means in context. Possibly related to Copilot Extensions, Semantic Kernel skills, or another plugin system.)

## Package README Command

Add a command or flag to print the README.md content from a package:

```bash
dotnet-inspect package System.Text.Json --readme
# or
dotnet-inspect readme System.Text.Json
```

The `**Readme:** yes` field is already displayed when a package contains a README.md. This would expose the actual content, which could be useful for:

- LLMs understanding package purpose and usage patterns
- Quick reference without leaving the terminal
- Automation workflows that need package documentation

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

## Type Search Command

A `find` verb for searching types across packages and assemblies:

```bash
dotnet-inspect find JsonSerializer
dotnet-inspect find "Json*" --package System.Text.Json
dotnet-inspect find ILogger --namespaces Microsoft.Extensions.Logging,Serilog
```

Scope options:
- Single package (`--package`)
- Application's bin directory (`--bin ./bin/Debug/net8.0`)
- Full package graph of an application (`--project ./MyApp.csproj`)
- Platform/framework reference assemblies (`--framework net8.0`)
- Combination of the above

Use cases:
- Resolving "type not found" errors by searching available assemblies
- Discovering which package provides a type seen in documentation or samples
- Finding types when only partial name is known (glob patterns)
- Providing namespace context from `using` statements to disambiguate

## Framework/Platform Documentation Accuracy

Investigate and document how the framework/platform feature works for version-specific documentation:

Questions to answer:
- Does accurate ref pack information require a matching SDK installed?
- How do we resolve documentation for types that changed between framework versions?
- What happens when inspecting code targeting a framework version not installed locally?
- Should we download ref packs on-demand like we do for NuGet packages?

Document findings in `docs/` and update help text to set correct expectations.

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
```
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

## API Diff Between Versions

Compare API surfaces between two versions of a package:

```bash
dotnet-inspect diff System.Text.Json@7.0.0 System.Text.Json@8.0.0
```

Output would show:
- Added types and members
- Removed types and members (breaking changes)
- Changed signatures
- New/removed interfaces on types

Useful for:
- Understanding what changed in a dependency upgrade
- Detecting breaking changes before upgrading
- Generating changelogs

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
