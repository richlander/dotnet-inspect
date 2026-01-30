# Backlog

Ideas for improving dotnet-inspect for LLM-driven C# development.

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
