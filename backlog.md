# Backlog

Ideas for improving dotnet-inspect for LLM-driven C# development.

## Source URLs Per-Member

Consider adding source URLs for individual members in the API member table. Currently only type-level source URLs are shown. Per-member URLs would enable:

- Direct navigation to specific method/property definitions
- More precise context for LLMs when reasoning about specific members
- Better integration with code review workflows

Challenges:
- Would significantly increase output size
- Requires resolving line numbers for each member from PDB
- May need to be opt-in (`--member-source-urls` or similar)

## Samples Command

Consider extracting samples functionality into a dedicated `samples` verb alongside `api`. This mirrors how `api` evolved from a flag on `assembly` into its own top-level command.

Current state:
- `--docs` fetches type/member documentation AND sample references
- Samples are rendered inline in the output

Proposed:
- `api` command shows basic API info (optionally with `--docs` for summaries)
- `samples` command fetches and displays sample content for a type
- Possibly add `--has-samples` flag to `api` to check if samples exist without fetching

This would enable a workflow:
1. `api JsonSerializer --package Newtonsoft.Json` → basic API info
2. `api JsonSerializer --package Newtonsoft.Json --docs` → with summaries
3. `samples JsonSerializer --package Newtonsoft.Json` → fetch sample content

## Docs Availability Check

Add a lightweight `--check-for-docs` (or `--has-docs`) flag to check if documentation/samples exist without fetching them. This enables a two-phase workflow:

```bash
# Phase 1: Get basic API info and check if docs are worth fetching
dotnet-inspect api JsonSerializer --package Newtonsoft.Json --check-for-docs
# Output includes: has_docs: true, has_samples: true

# Phase 2: Only fetch docs if they exist
dotnet-inspect api JsonSerializer --package Newtonsoft.Json --docs
```

This avoids the latency of fetching source when docs don't exist, and lets users/LLMs make informed decisions about whether `--docs` or a future `samples` command is worth calling.

Alternative: Always include `has_docs`/`has_samples` fields in default output (requires checking PDB for SourceLink presence).

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
